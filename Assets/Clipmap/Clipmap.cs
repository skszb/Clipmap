using System;
using System.Drawing;
using Unity.VisualScripting;
using UnityEngine;

[Serializable]
public struct ClipmapParam
{
    public int clipmapLevelCount;
    public int clipSize;
    public int invalidBorder;
    public int clipmapUpdateGridSize;
    public TextureFormat mipTextureFormat;

    public int worldGridSize;

    public Texture2D[] baseTexture;
}

public class Clipmap : MonoBehaviour
{
    // The length in one dimension of a grid in world space that binds to one texel.
    // Used to convert player coordinate to homogeneous coordinate: homogeneousCoordinate = worldCoordinate / worldGridSize
    private int m_worldGridSize;

    // The length in one dimension of a grid in texture space.
    // Depends on clip center deviation, the clipmap will only update a multiple of MipGridSize pixels in a direction
    private int m_clipmapUpdateGridSize;

    // The number of texels in one dimension in a stack level
    private int m_clipSize;
    private int m_clipHalfSize;

    // The number of texels in one dimension from both ends, used to determine whether to wait for mipTexture update
    private int m_invalidBorder;

    private int m_clipmapStackSize;

    private int m_clipmapLevelCount;
    private TextureFormat m_mipTextureFormat;

    // Faked as data in disk, should be changed later
    private Texture2D[] m_baseMipTexture; 

    // The memory chunk that is larger than the actual mip texture, acts as a cache, 
    // Currently load the whole texture to fake as a 2nd-level cache in mem
    private Texture2D[] m_clipmapStackCache;

    private Texture2D[] m_clipmapPyramidTexture;
    private Texture2DArray m_clipmapStackTextureArray;

    private int[] m_mipSize;
    private int[] m_mipHalfSize;
    // the bounding box of the safe region that the clipmap center can move, so that the clipmap level does not include areas beyond the mip texture
    private AABB2Int[] m_clipmapCenterSafeRegion;
    private Vector2Int[] m_clipmapCenter;


    #region Variables that are passed to the surface shader
    #endregion

    public void Intialize(ClipmapParam param)
    {
        m_clipSize = param.clipSize;
        m_clipHalfSize = m_clipSize / 2;

        m_worldGridSize = param.worldGridSize;
        m_clipmapUpdateGridSize = param.clipmapUpdateGridSize;

        m_invalidBorder = param.invalidBorder;
        m_mipTextureFormat = param.mipTextureFormat;
        m_clipmapLevelCount = param.clipmapLevelCount;

        m_clipmapStackSize = 0;
        m_clipmapStackCache = new Texture2D[param.clipmapLevelCount];
        m_clipmapCenter = new Vector2Int[param.clipmapLevelCount];
        m_clipmapCenterSafeRegion = new AABB2Int[param.clipmapLevelCount];
        m_clipmapPyramidTexture = new Texture2D[param.clipmapLevelCount];
        m_mipSize = new int[param.clipmapLevelCount];
        m_mipHalfSize = new int[param.clipmapLevelCount];
        m_baseMipTexture = new Texture2D[param.clipmapLevelCount];

        for (int i = 0; i < param.clipmapLevelCount; i++)
        {
            m_baseMipTexture[i] = param.baseTexture[i];
            m_mipSize[i] = m_baseMipTexture[i].width;
            m_mipHalfSize[i] = m_baseMipTexture[i].width / 2;
        }
        InitializeMips();
    }

    // Create space for each level in the clipmap by resolutions
    private void InitializeMips()
    {
        for (int mipLevelIndex = 0; mipLevelIndex < m_clipmapLevelCount; mipLevelIndex++) 
        {
            int mipSize = m_mipSize[mipLevelIndex];
            bool inClipStack = mipSize > m_clipSize;
            if (inClipStack)
            {
                m_clipmapStackSize++;
                m_clipmapStackCache[mipLevelIndex] = new Texture2D(mipSize, mipSize, m_mipTextureFormat, false, false);

                // Initialize cache, currently load all
                Texture2D mipTexture = m_baseMipTexture[mipLevelIndex];
                Texture2D clipmapCache = m_clipmapStackCache[mipLevelIndex];
                Graphics.CopyTexture(mipTexture, 0, 0, 0, 0, clipmapCache.width, clipmapCache.height, clipmapCache, 0, 0, 0, 0);

                // Initialize clip stack levels
                // Initialize all levels outside the mip area so that their textures will be automatically loaded in the first update
                int mipHalfSize = m_mipHalfSize[mipLevelIndex];
                m_clipmapCenterSafeRegion[mipLevelIndex] = new AABB2Int(-mipHalfSize + m_clipHalfSize, -mipHalfSize + m_clipHalfSize, mipHalfSize - m_clipHalfSize, mipHalfSize - m_clipHalfSize);
                m_clipmapCenter[mipLevelIndex] = m_clipmapCenterSafeRegion[mipLevelIndex].min - new Vector2Int(m_clipSize, m_clipSize);
            }
            else
            {
                // load entire mip level from disk
                Texture2D mipTexture = m_baseMipTexture[mipLevelIndex];
                m_clipmapPyramidTexture[mipLevelIndex] = new Texture2D(mipSize, mipSize, m_mipTextureFormat, false, false);
                Graphics.CopyTexture(mipTexture, 0, 0, 0, 0, mipTexture.width, mipTexture.height, m_clipmapPyramidTexture[mipLevelIndex], 0, 0, 0, 0);
            }
        }
        Array.Resize(ref m_clipmapStackCache, m_clipmapStackSize);
        Array.Resize(ref m_clipmapCenter, m_clipmapStackSize);
        Array.Resize(ref m_clipmapCenterSafeRegion, m_clipmapStackSize);
        Array.Resize(ref m_clipmapPyramidTexture, m_clipmapLevelCount - m_clipmapStackSize);
        m_clipmapStackTextureArray = new Texture2DArray(m_clipSize, m_clipSize, m_clipmapStackSize, m_mipTextureFormat, false, false, true);
        m_clipmapStackTextureArray.Apply(false, true);  // set Texture.isReadable to false
    }

    public void UpdateClipmap(Vector3 cameraPositionInWorldSpace)
    {
        Vector2 centerInWorldSpace = new Vector2(cameraPositionInWorldSpace.x, cameraPositionInWorldSpace.z);
        float height = cameraPositionInWorldSpace.y;

        Vector2 centerInHomogeneousSpace = centerInWorldSpace / m_worldGridSize;
        Vector2 centerInClipmapSpace = centerInHomogeneousSpace;
        for (int clipmapStackLevelIndex = 0; clipmapStackLevelIndex < m_clipmapStackSize; clipmapStackLevelIndex++, centerInClipmapSpace /= 2)
        {
            // confining the clipmap level within its corresponding mipmap level
            // then calculate the region that needs to be updated
            AABB2Int clipmapCenterSafeRegion = m_clipmapCenterSafeRegion[clipmapStackLevelIndex];
            Vector2Int updatedClipmapCenter = GetSnappedCenter(centerInClipmapSpace);
            updatedClipmapCenter.x = Math.Clamp(updatedClipmapCenter.x, clipmapCenterSafeRegion.min.x, clipmapCenterSafeRegion.max.x);
            updatedClipmapCenter.y = Math.Clamp(updatedClipmapCenter.y, clipmapCenterSafeRegion.min.y, clipmapCenterSafeRegion.max.y);

            Vector2Int diff = updatedClipmapCenter - m_clipmapCenter[clipmapStackLevelIndex];
            Vector2Int absDiff = new Vector2Int(Math.Abs(diff.x), Math.Abs(diff.y));

            // find the updated regions in mipmap space
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
                                         m_clipmapStackTextureArray, clipmapStackLevelIndex, 0, dstX, dstY);
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
                                         m_clipmapStackTextureArray, clipmapStackLevelIndex, 0, dstX, dstY);
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
        return m_clipmapStackTextureArray;
    }
};
