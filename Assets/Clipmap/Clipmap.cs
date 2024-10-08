using System;
using System.Drawing;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UIElements;

[Serializable]
public struct ClipmapParam
{
    public int clipSize;
    public int invalidBorder;
    public int worldGridSize;
    public int clipmapUpdateGridSize;

    public TextureFormat mipTextureFormat;
    public Texture2D[] baseTexture;
}

[ExecuteInEditMode]
public class Clipmap : MonoBehaviour
{
    public Material m_Material;

    // The length in one dimension of a grid in texture space.
    // Depends on clip center deviation, the clipmap will only update a multiple of MipGridSize pixels in a direction
    private int m_clipmapUpdateGridSize;

    private TextureFormat m_mipTextureFormat;

    // Faked as data in disk, should be changed to streaming address later 
    private Texture2D[] m_baseMipTexture; 

    // The memory chunk that is larger than the actual mip texture, acts as a cache, 
    // Currently load the whole texture to fake as a 2nd-level cache in mem
    private Texture2D[] m_clipmapStackCache;

    // The dimensions of each climap level's base mip texture (in texels)
    private int[] m_mipSize;
    private int[] m_mipHalfSize;

    // the bounding box of the safe region that the clipmap center can move, so that the clipmap level does not include areas beyond the mip texture
    private AABB2Int[] m_clipmapCenterSafeRegion;

    #region Variables that sync with the surface shader
        // The texture array that contains both the clipmap stack and the clipmap pyramid.
        // The last level is the top of clipmap pyramid, which is a mipmap that covers the whole texture
        // The levels before the last compose the clipmap stack, each clipmap stack level is a proportion of the corrisponding mipmap level
        private Texture2DArray m_clipmapLevel;
        
        // The snapped center of each clipmap level in the texture space
        private Vector2Int[] m_clipmapCenter;

        // The length in one dimension of a grid in world space that binds to one texel.
        // Used to convert player coordinate to homogeneous coordinate: homogeneousCoordinate = worldCoordinate / worldGridSize
        private int m_worldGridSize;
        
        // The number of texels in one dimension in a stack level
        private int m_clipSize;
        private int m_clipHalfSize;

        // The number of texels in one dimension from both ends, used to determine whether to wait for mipTexture update
        private int m_invalidBorder;

        // The number of levels in the clipmap region
        int m_clipmapLevelCount;
        int m_clipmapStackLevelCount; // The number of levels in the clipmap stack, which is (clipmapSize - 1)
    #endregion

    public void Intialize(ClipmapParam param)
    {
        m_clipSize = param.clipSize;
        m_clipHalfSize = m_clipSize / 2;
        m_worldGridSize = param.worldGridSize;
        m_clipmapUpdateGridSize = param.clipmapUpdateGridSize;
        m_invalidBorder = param.invalidBorder;
        m_mipTextureFormat = param.mipTextureFormat;

        m_clipmapLevelCount = param.baseTexture.Length;
        m_mipSize = new int[m_clipmapLevelCount];
        m_mipHalfSize = new int[m_clipmapLevelCount];
        m_baseMipTexture = new Texture2D[m_clipmapLevelCount];
        for (int i = 0; i < m_clipmapLevelCount; i++)
        {
            m_baseMipTexture[i] = param.baseTexture[i];
            m_mipSize[i] = m_baseMipTexture[i].width;
            m_mipHalfSize[i] = m_baseMipTexture[i].width / 2;
        }

        m_clipmapStackLevelCount = m_clipmapLevelCount - 1;
        m_clipmapStackCache = new Texture2D[m_clipmapStackLevelCount];
        m_clipmapCenter = new Vector2Int[m_clipmapStackLevelCount];
        m_clipmapCenterSafeRegion = new AABB2Int[m_clipmapStackLevelCount];

        InitializeMips();
    }

    // Create space for each level in the clipmap by resolutions
    private void InitializeMips()
    {
        m_clipmapLevel = new Texture2DArray(m_clipSize, m_clipSize, m_clipmapLevelCount, m_mipTextureFormat, true, false, true);

        for (int mipLevelIndex = 0; mipLevelIndex < m_clipmapStackLevelCount; mipLevelIndex++) 
        {
            int mipSize = m_mipSize[mipLevelIndex];

            // Initialize cache from disk, currently load the whole mip texture, should change to data streaming later
            m_clipmapStackCache[mipLevelIndex] = new Texture2D(mipSize, mipSize, m_mipTextureFormat, false, false);
            Texture2D mipmapLevelDiskData = m_baseMipTexture[mipLevelIndex];
            Texture2D clipmapLevelCache = m_clipmapStackCache[mipLevelIndex];
            Graphics.CopyTexture(mipmapLevelDiskData, 0, 0, 0, 0, clipmapLevelCache.width, clipmapLevelCache.height, clipmapLevelCache, 0, 0, 0, 0);

            // Initialize clipmap stack levels
            // Set clipmap centers outside the mip area so that their textures will be automatically loaded in the first update
            int safeRegionHalfSize = m_mipHalfSize[mipLevelIndex] - m_clipHalfSize;
            m_clipmapCenterSafeRegion[mipLevelIndex] = new AABB2Int(-safeRegionHalfSize, -safeRegionHalfSize, safeRegionHalfSize, safeRegionHalfSize);
            m_clipmapCenter[mipLevelIndex] = m_clipmapCenterSafeRegion[mipLevelIndex].min - new Vector2Int(m_clipSize, m_clipSize);
        }

        // Use the last level as the clipmap pyramid, it is a fixed texture that covers the whole surface
        int lastLevelIndex = m_clipmapLevelCount - 1;
        Graphics.CopyTexture(m_baseMipTexture[lastLevelIndex], 0, m_clipmapLevel, lastLevelIndex);
    }

    public void UpdateClipmap(Vector3 cameraPositionInWorldSpace)
    {
        Vector2 centerInWorldSpace = new Vector2(cameraPositionInWorldSpace.x, cameraPositionInWorldSpace.z);
        float height = cameraPositionInWorldSpace.y;

        Vector2 centerInHomogeneousSpace = centerInWorldSpace / m_worldGridSize;
        Vector2 centerInMipSpace = centerInHomogeneousSpace;
        for (int clipmapStackLevelIndex = 0; clipmapStackLevelIndex < m_clipmapStackLevelCount; clipmapStackLevelIndex++, centerInMipSpace /= 2)
        {
            // confining the clipmap level within its corresponding mipmap level
            // then calculate the region that needs to be updated
            AABB2Int clipmapCenterSafeRegion = m_clipmapCenterSafeRegion[clipmapStackLevelIndex];
            Vector2Int updatedClipmapCenter = GetSnappedCenter(centerInMipSpace);
            updatedClipmapCenter.x = Math.Clamp(updatedClipmapCenter.x, clipmapCenterSafeRegion.min.x, clipmapCenterSafeRegion.max.x);
            updatedClipmapCenter.y = Math.Clamp(updatedClipmapCenter.y, clipmapCenterSafeRegion.min.y, clipmapCenterSafeRegion.max.y);

            Vector2Int diff = updatedClipmapCenter - m_clipmapCenter[clipmapStackLevelIndex];
            Vector2Int absDiff = new Vector2Int(Math.Abs(diff.x), Math.Abs(diff.y));

            // find the updated regions in the mip space
            AABB2Int verticalUpdateZone = new AABB2Int();
            if (absDiff.x > 0)
            {
                if (diff.x < 0)
                {
                    verticalUpdateZone.min.x = updatedClipmapCenter.x - m_clipHalfSize;
                    verticalUpdateZone.max.x = verticalUpdateZone.min.x + absDiff.x;
                }
                else
                {
                    verticalUpdateZone.max.x = updatedClipmapCenter.x + m_clipHalfSize;
                    verticalUpdateZone.min.x = verticalUpdateZone.max.x - absDiff.x;
                }
                verticalUpdateZone.min.y = updatedClipmapCenter.y - m_clipHalfSize;
                verticalUpdateZone.max.y = verticalUpdateZone.min.y + m_clipSize;
            }

            AABB2Int horizontalUpdateZone = new AABB2Int();
            if (absDiff.y > 0 && absDiff.x < m_clipSize)
            {
                if (diff.y < 0)
                {
                    horizontalUpdateZone.min.y = updatedClipmapCenter.y - m_clipHalfSize;
                    horizontalUpdateZone.max.y = horizontalUpdateZone.min.y + absDiff.y;
                }
                else
                {
                    horizontalUpdateZone.max.y = updatedClipmapCenter.y + m_clipHalfSize;
                    horizontalUpdateZone.min.y = horizontalUpdateZone.max.y - absDiff.y;
                }

                if (diff.x < 0)
                {
                    horizontalUpdateZone.max.x = updatedClipmapCenter.x + m_clipHalfSize;
                    horizontalUpdateZone.min.x = horizontalUpdateZone.max.x - m_clipSize + absDiff.x;
                }
                else
                {
                    horizontalUpdateZone.min.x = updatedClipmapCenter.x - m_clipHalfSize;
                    horizontalUpdateZone.max.x = horizontalUpdateZone.min.x + m_clipSize - absDiff.x;
                }
            }

            Vector2Int clipmapBottomLeftCorner = updatedClipmapCenter - new Vector2Int(m_clipHalfSize, m_clipHalfSize);
            clipmapBottomLeftCorner.x = (int)Mathf.Floor((float)clipmapBottomLeftCorner.x / (float)m_clipSize) * m_clipSize;
            clipmapBottomLeftCorner.y = (int)Mathf.Floor((float)clipmapBottomLeftCorner.y / (float)m_clipSize) * m_clipSize;

            AABB2Int bottomLeftTile = new AABB2Int(clipmapBottomLeftCorner, 
                                                   clipmapBottomLeftCorner + new Vector2Int(m_clipSize, m_clipSize));

            AABB2Int[] tilesToUpdate = {bottomLeftTile,
                                        bottomLeftTile + new Vector2Int(m_clipSize, 0),
                                        bottomLeftTile + new Vector2Int(0, m_clipSize),
                                        bottomLeftTile + m_clipSize};

            int mipHalfSize = m_mipHalfSize[clipmapStackLevelIndex];
            foreach(AABB2Int tile in tilesToUpdate)
            {
                AABB2Int verticalPart = verticalUpdateZone.Clamp(tile);
                if (verticalPart.isValid())
                {
                    int srcX = verticalPart.min.x + mipHalfSize;
                    int srcY = verticalPart.min.y + mipHalfSize;
                    int dstX = verticalPart.min.x - tile.min.x;
                    int dstY = verticalPart.min.y - tile.min.y;
                    Graphics.CopyTexture(m_clipmapStackCache[clipmapStackLevelIndex], 0, 0, srcX, srcY,
                                         verticalPart.Width(), verticalPart.Height(),
                                         m_clipmapLevel, clipmapStackLevelIndex, 0, dstX, dstY);
                }
                AABB2Int horizontalPart = horizontalUpdateZone.Clamp(tile);
                if (horizontalPart.isValid())
                {
                    int srcX = horizontalPart.min.x + mipHalfSize;
                    int srcY = horizontalPart.min.y + mipHalfSize;
                    int dstX = horizontalPart.min.x - tile.min.x;
                    int dstY = horizontalPart.min.y - tile.min.y;
                    Graphics.CopyTexture(m_clipmapStackCache[clipmapStackLevelIndex], 0, 0, srcX, srcY,
                                         horizontalPart.Width(), horizontalPart.Height(),
                                         m_clipmapLevel, clipmapStackLevelIndex, 0, dstX, dstY);
                }
            }
            m_clipmapCenter[clipmapStackLevelIndex] = updatedClipmapCenter;
        }
    }
    // Snap the center to multiples of m_mipUpdateGridSize
    private Vector2Int GetSnappedCenter(Vector2 worldCenter)
    {
        return Vector2Int.FloorToInt(worldCenter / m_clipmapUpdateGridSize) * m_clipmapUpdateGridSize;
    }

    public Texture2DArray GetClipmapStackTexture()
    {
        return m_clipmapLevel;
    }

    private void Start()
    {
        m_Material = GetComponent<Renderer>().material;
        float[] a = new float[2];
        a[0] = 0.5f;
        a[1] = 0.9f;
    }
};
