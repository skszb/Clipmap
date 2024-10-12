using System;
using System.Drawing;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.XR;

[Serializable]
public struct ClipmapParam
{
    public int clipSize;
    public int invalidBorder;
    public int worldScale;
    public int clipmapUpdateGridSize;

    public TextureFormat mipTextureFormat;
    public Texture2D[] baseTexture;
}

/*
Terminologies:
    World Space: 
    The coordinate in unity world space, range (-inf, +inf)

    Mip Space: 
    The coordinate in each mip level. The units are the same regardless of mip level, which is 1 texel, but differs in range.
    A clipmap with the mipmap of [ 0: 2048x2048 ] will have the mip space coordinate in range of [ 0: -1024x1023 ]  (floored)
                                 [ 1: 1024x1024 ]                                                [ 1: -512x511   ]
                                 [ 2: 512x512   ]                                                [ 2: -256x255   ]
 */

// [ExecuteInEditMode]
public class Clipmap : MonoBehaviour
{
    public Renderer m_surfaceRenderer;
    private Material m_Material;

    // The length in one dimension of a grid in texture space.
    // Depends on clip center deviation, the clipmap will only update a multiple of MipGridSize pixels in a direction
    private int m_clipmapUpdateGridSize;

    private TextureFormat m_mipTextureFormat;

    // Faked as data in disk, should be changed to streaming address later 
    private Texture2D[] m_baseMipTexture; 

    // The memory chunk that is larger than the actual mip texture, acts as a cache, 
    // Currently load the whole texture to fake as a 2nd-level cache in mem
    private Texture2D[] m_clipmapStackCache;

    // the bounding box of the safe region that the clipmap center can move, so that the clipmap level does not include areas beyond the mip texture
    private AABB2Int[] m_clipmapCenterSafeRegion;

    #region Variables that sync with the surface shader
        // The texture array that contains both the clipmap stack and the clipmap pyramid.
        // The last level is the top of clipmap pyramid, which is a mipmap that covers the whole texture
        // The levels before the last compose the clipmap stack, each clipmap stack level is a proportion of the corrisponding mipmap level
        private Texture2DArray m_clipmapLevel;
        
        // The snapped center of each clipmap level in the texture space
        private Vector2Int[] m_clipmapCenter;
        private Vector4[] m_clipmapCenterFloat; // cached for passing to shader

    /* -------- Only sync when initialize -------- */

        // The length in one dimension of a grid in world space that binds to one texel.
        // Used to convert player coordinate to homogeneous coordinate (mip0 coordinate): homogeneousCoordinate = worldCoordinate / m_worldScale
        private int m_worldScale = 1;

        // The number of texels in one dimension from both ends, used to determine whether to wait for mipTexture update
        private int m_invalidBorder;

        // The number of texels in one dimension in a stack level
        private int m_clipSize;
        private int m_clipHalfSize;

        // The number of levels in the clipmap region
        int m_clipmapLevelCount;
        int m_clipmapStackLevelCount; // The number of levels in the clipmap stack, which is (clipmapSize - 1)

        float[] m_clipScaleToMip;
        float[] m_mipScaleToWorld;  

        // The dimensions of each climap level's base mip texture (in texels)
        private int[] m_mipSize;
        private float[] m_mipSizeFloat; // cached for passing to shader
        private int[] m_mipHalfSize;
        private float[] m_mipHalfSizeFloat; // cached for passing to shader
    #endregion

    public void Intialize(ClipmapParam param)
    {
        m_clipSize = param.clipSize;
        m_clipHalfSize = m_clipSize >> 1;
        m_worldScale = param.worldScale;
        m_clipmapUpdateGridSize = param.clipmapUpdateGridSize;
        m_invalidBorder = param.invalidBorder;
        m_mipTextureFormat = param.mipTextureFormat;

        m_clipmapLevelCount = param.baseTexture.Length;
        m_mipSize = new int[m_clipmapLevelCount];
        m_mipSizeFloat = new float[m_clipmapLevelCount];
        m_mipHalfSize = new int[m_clipmapLevelCount];
        m_mipHalfSizeFloat = new float[m_clipmapLevelCount];
        m_baseMipTexture = new Texture2D[m_clipmapLevelCount];
        m_clipScaleToMip = new float[m_clipmapLevelCount];
        m_mipScaleToWorld = new float[m_clipmapLevelCount];
        for (int i = 0; i < m_clipmapLevelCount; i++)
        {
            m_baseMipTexture[i] = param.baseTexture[i];
            m_mipSize[i] = m_baseMipTexture[i].width;
            m_mipSizeFloat[i] = m_mipSize[i];
            m_mipHalfSize[i] = m_baseMipTexture[i].width >> 1;
            m_mipHalfSizeFloat[i] = m_mipHalfSize[i];
        }

        m_clipmapStackLevelCount = m_clipmapLevelCount - 1;
        m_clipmapStackCache = new Texture2D[m_clipmapStackLevelCount];
        m_clipmapCenter = new Vector2Int[m_clipmapStackLevelCount];
        m_clipmapCenterFloat = new Vector4[m_clipmapStackLevelCount];

        m_clipmapCenterSafeRegion = new AABB2Int[m_clipmapStackLevelCount];

        InitializeMips();

        m_Material = m_surfaceRenderer.material;
        m_Material.SetFloat("_WorldGridSize", m_worldScale);
        m_Material.SetInteger("_InvalidBorder", m_invalidBorder);
        m_Material.SetInteger("_ClipSize", m_clipSize);
        m_Material.SetInteger("_ClipHalfSize", m_clipHalfSize);
        m_Material.SetInteger("_ClipmapStackLevelCount", m_clipmapStackLevelCount);
        m_Material.SetFloatArray("_MipSize", m_mipSizeFloat);
        m_Material.SetFloatArray("_MipHalfSize", m_mipHalfSizeFloat);
        m_Material.SetFloatArray("_ClipScaleToMip", m_clipScaleToMip);
        m_Material.SetFloatArray("_MipScaleToWorld", m_mipScaleToWorld);
        m_Material.SetTexture("_ClipmapLevel", m_clipmapLevel);
    }

    // Create space for each level in the clipmap
    private void InitializeMips()
    {
        m_clipmapLevel = new Texture2DArray(m_clipSize, m_clipSize, m_clipmapLevelCount, m_mipTextureFormat, true, false, true);

        int clipScaleToMip = 1 << m_clipmapStackLevelCount;
        int mipScaleToWorld = 1;

        for (int mipLevelIndex = 0; mipLevelIndex < m_clipmapStackLevelCount; mipLevelIndex++, clipScaleToMip >>= 1, mipScaleToWorld <<= 1) 
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
            m_clipmapCenterFloat[mipLevelIndex].x = m_clipmapCenter[mipLevelIndex].x;
            m_clipmapCenterFloat[mipLevelIndex].y = m_clipmapCenter[mipLevelIndex].y;

            m_clipScaleToMip[mipLevelIndex] = clipScaleToMip;
            m_mipScaleToWorld[mipLevelIndex] = mipScaleToWorld;
        }

        // Use the last level as the clipmap pyramid, it is a fixed texture that covers the whole surface
        int lastLevelIndex = m_clipmapLevelCount - 1;
        Graphics.CopyTexture(m_baseMipTexture[lastLevelIndex], 0, m_clipmapLevel, lastLevelIndex);

        m_clipScaleToMip[lastLevelIndex] = clipScaleToMip;
        m_mipScaleToWorld[lastLevelIndex] = mipScaleToWorld;
    }

    public void UpdateClipmap(Vector3 cameraPositionInWorldSpace)
    {
        Vector2 centerInWorldSpace = new Vector2(cameraPositionInWorldSpace.x, cameraPositionInWorldSpace.z);
        float height = cameraPositionInWorldSpace.y;

        Vector2 centerInHomogeneousSpace = centerInWorldSpace / m_worldScale;
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

            if (diff.sqrMagnitude < 0.1)
            {
                continue;
            }
            Debug.Log("-----------------------------------------");
            for(int i = 0; i < m_clipmapCenterFloat.Length; ++i)
            {
                Debug.Log("Center " + i + " coordinate: "+ m_clipmapCenterFloat[i].ToSafeString());
            }



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
            m_clipmapCenterFloat[clipmapStackLevelIndex].x = updatedClipmapCenter.x;
            m_clipmapCenterFloat[clipmapStackLevelIndex].y = updatedClipmapCenter.y;
        }
        m_Material.SetVectorArray("_ClipmapCenter", m_clipmapCenterFloat);

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
    }
};
