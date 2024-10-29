using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;


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
    [SerializeField] private ClipmapConfiguration m_clipmapConfiguration;

    // Faked as data in disk, should be changed to streaming address later 
    private Texture2D[] m_baseMipTexture;

    // the bounding box of the safe region that the clipmap center can move, so that the clipmap level does not include areas beyond the mip texture
    private AABB2Int[] m_clipmapCenterSafeRegion;

    // The memory chunk that is larger than the actual mip texture, acts as a cache, 
    // Currently load the whole texture to fake as a 2nd-level cache in mem
    private Texture2D[] m_clipmapStackCache;

    // The length in one dimension of a grid in mip space.
    // Depends on clip center deviation, the clipmap will only update a multiple of MipGridSize pixels in a direction
    private int m_updateGridSize;

    private Material m_Material;

    private TextureFormat m_mipTextureFormat;
    
    private TileCacheManager m_tileCacheManager;


    #region Variables that sync with the surface shader

    public Texture2DArray ClipmapStack { get; private set; }

    public Texture2D ClipmapPyramid { get; private set; }

    // The snapped center of each clipmap level in the mip space
    private Vector2Int[] m_clipCenter;
    private Vector4[] m_clipCenterFloat; // cached for passing to shader

    public int m_maxTextureLOD = 0;

    /* -------- Only sync when initialize -------- */



    // The number of texels in one dimension from both ends, used to determine whether to wait for mipTexture update
    private int m_invalidBorder; // TODO: Update every frame: https://notkyon.moe/vt/Clipmap.pdf

    // The number of texels in one dimension in a stack level
    private int m_clipSize;
    private int m_clipHalfSize;

    // The number of levels in the clipmap region
    private int m_clipmapLevelCount;
    private int m_clipmapStackLevelCount; // The number of levels in the clipmap stack, which is (clipmapSize - 1)

    private float[] m_clipScaleToMip;
    private float[] m_mipScaleToWorld;

    // The dimensions of each climap level's base mip texture (in texels)
    private int[] m_mipSize;
    private float[] m_mipSizeFloat; // cached for passing to shader
    private int[] m_mipHalfSize;
    private float[] m_mipHalfSizeFloat; // cached for passing to shader

    #endregion
    
    private void Awake()
    {
        m_Material = GetComponent<Renderer>().material;
        Initialize();
    }


    public void Initialize()
    {
        LoadConfiguration();
        InitializeMips();
        PassStaticUniforms();
    }


    // Create space for each level in the clipmap
    private void InitializeMips()
    {
        ClipmapStack = new Texture2DArray(m_clipSize, m_clipSize, m_clipmapStackLevelCount, m_mipTextureFormat, false,
            false, true);
        ClipmapPyramid = new Texture2D(m_clipSize, m_clipSize, m_mipTextureFormat, true, false, true);


        int clipScaleToMip = 1 << m_clipmapStackLevelCount;
        int mipScaleToWorld = 1;
        for (int mipLevelIndex = 0; 
             mipLevelIndex < m_clipmapStackLevelCount;
             mipLevelIndex++, clipScaleToMip >>= 1, mipScaleToWorld <<= 1)
        {
            int mipSize = m_mipSize[mipLevelIndex];

            // Initialize cache from disk, currently load the whole mip texture, should change to data streaming later
            m_clipmapStackCache[mipLevelIndex] = new Texture2D(mipSize, mipSize, m_mipTextureFormat, false, false);
            Texture2D mipmapLevelDiskData = m_baseMipTexture[mipLevelIndex];
            Texture2D clipmapLevelCache = m_clipmapStackCache[mipLevelIndex];
            Graphics.CopyTexture(mipmapLevelDiskData, 0, 0, 0, 0, clipmapLevelCache.width, clipmapLevelCache.height,
                clipmapLevelCache, 0, 0, 0, 0);

            // Initialize clipmap stack levels
            // Set clipmap centers outside the mip area so that their textures will be automatically loaded in the first update
            int safeRegionHalfSize = m_mipHalfSize[mipLevelIndex] - m_clipHalfSize;
            m_clipmapCenterSafeRegion[mipLevelIndex] = new AABB2Int(-safeRegionHalfSize, -safeRegionHalfSize,
                safeRegionHalfSize, safeRegionHalfSize);
            m_clipCenter[mipLevelIndex] = m_clipmapCenterSafeRegion[mipLevelIndex].min -
                                                       new Vector2Int(m_clipSize, m_clipSize);
            m_clipCenterFloat[mipLevelIndex].x = m_clipCenter[mipLevelIndex].x;
            m_clipCenterFloat[mipLevelIndex].y = m_clipCenter[mipLevelIndex].y;

            m_clipScaleToMip[mipLevelIndex] = clipScaleToMip;
            m_mipScaleToWorld[mipLevelIndex] = mipScaleToWorld;
        }

        // Use the last level as the clipmap pyramid, it is a fixed texture that covers the whole surface
        int lastLevelIndex = m_clipmapLevelCount - 1;

        Graphics.CopyTexture(m_baseMipTexture[lastLevelIndex], ClipmapPyramid);

        m_clipScaleToMip[lastLevelIndex] = clipScaleToMip;
        m_mipScaleToWorld[lastLevelIndex] = mipScaleToWorld;
    }


    public void UpdateClipmap(Vector3 cameraPositionInWorldSpace)
    {
        Vector2 centerInWorldSpace = new Vector2(cameraPositionInWorldSpace.x, cameraPositionInWorldSpace.z);
        float height = cameraPositionInWorldSpace.y;

        Vector2 centerInMipSpace = centerInWorldSpace;
        Vector2Int[] updatedClipCenters = new Vector2Int[m_clipmapStackLevelCount];
        var updateRegions = new List<List<(Texture2D tile, AABB2Int tileBound, AABB2Int updateRegion)>>(m_clipmapStackLevelCount);
        for (int depth = 0; depth < m_clipmapStackLevelCount;  depth++, centerInMipSpace /= 2)
        {
            // The coordinate of snapped center is floored, so we added a positive bias of half grid size to the player's position 
            // this ensures that the boundary that triggers clipmap update is [-0.5, 0.5) around the center instead of [0, 1);
            Vector2 biasedPosition = centerInMipSpace + m_updateGridSize * new Vector2(0.5f, 0.5f);
            
            AABB2Int clipmapCenterSafeRegion = m_clipmapCenterSafeRegion[depth];
            Vector2Int updatedClipCenter = GetSnappedCenter(biasedPosition, m_updateGridSize);
            updatedClipCenter = clipmapCenterSafeRegion.ClampVec2Int(updatedClipCenter);
            updatedClipCenters[depth] = updatedClipCenter;
            
            // We are updating from the level of highest precision, so we can safely skip the rest if current one doesn't need update
            List<AABB2Int> regionsToUpdate = GetUpdateRegions(m_clipCenter[depth], updatedClipCenter, m_clipSize);
            if (!regionsToUpdate.Any()) break;
            
            // 1. split regions in mip tile cache
            foreach (AABB2Int region in regionsToUpdate)
            {
                updateRegions[depth] = m_tileCacheManager.GetTiles(region, depth);
            }
        }

        for (int depth = m_clipmapStackLevelCount - 1; depth >= 0; depth--)
        {
            Vector2Int updatedClipCenter = updatedClipCenters[depth];
            // 2. further divide into clip tile
            
            // 3. copytexture
            Vector2Int clipmapBottomLeftCorner = updatedClipCenter - new Vector2Int(m_clipHalfSize, m_clipHalfSize);
            clipmapBottomLeftCorner = m_clipSize * ClipmapUtil.FloorDivision(clipmapBottomLeftCorner, m_clipSize);

            AABB2Int bottomLeftTile = new AABB2Int(clipmapBottomLeftCorner, clipmapBottomLeftCorner + new Vector2Int(m_clipSize, m_clipSize));

            List<AABB2Int> tilesToUpdate = new List<AABB2Int>
            {
                bottomLeftTile,
                bottomLeftTile + new Vector2Int(m_clipSize, 0),
                bottomLeftTile + new Vector2Int(0, m_clipSize),
                bottomLeftTile + m_clipSize
            };

            int mipHalfSize = m_mipHalfSize[depth];
            foreach (AABB2Int tile in tilesToUpdate)
            {
                foreach (AABB2Int updateRegion in regionsToUpdate)
                {
                    AABB2Int updateRegionInTile = updateRegion.ClampBy(tile);
                    if (updateRegionInTile.IsValid())
                    {
                        int srcX = updateRegionInTile.min.x + mipHalfSize;
                        int srcY = updateRegionInTile.min.y + mipHalfSize;
                        int dstX = updateRegionInTile.min.x - tile.min.x;
                        int dstY = updateRegionInTile.min.y - tile.min.y;
                        Graphics.CopyTexture(m_clipmapStackCache[depth], 0, 0, srcX, srcY,
                            updateRegionInTile.Width(), updateRegionInTile.Height(),
                            ClipmapStack, depth, 0, dstX, dstY);
                    }
                }
            }
            m_clipCenter[depth] = updatedClipCenter;
            m_clipCenterFloat[depth].x = updatedClipCenter.x;
            m_clipCenterFloat[depth].y = updatedClipCenter.y;
        }

        PassDynamicUniforms();
    }


    // A method for getting the new covered regions by square tile movement, in the form of list of AABB2Ints
    private static List<AABB2Int> GetUpdateRegions(in Vector2Int oldCenter, in Vector2Int newCenter, int tileSize)
    {
        Vector2Int diff = newCenter - oldCenter;
        Vector2Int absDiff = new Vector2Int(Math.Abs(diff.x), Math.Abs(diff.y));
        int tileHalfSize = tileSize / 2;

        List<AABB2Int> updateRegions = new List<AABB2Int>();
        // Find the updated regions in current space
        // We separate the update regions into at most 2 parts:
        // (1) the rectangular update zone that is of the size (x,tileSize)
        // (2) the rest of the update zone
        if (absDiff.x > 0)
        {
            AABB2Int xUpdateZone = new AABB2Int();
            if (diff.x < 0)
            {
                xUpdateZone.min.x = newCenter.x - tileHalfSize;
                xUpdateZone.max.x = xUpdateZone.min.x + absDiff.x;
            }
            else
            {
                xUpdateZone.max.x = newCenter.x + tileHalfSize;
                xUpdateZone.min.x = xUpdateZone.max.x - absDiff.x;
            }

            xUpdateZone.min.y = newCenter.y - tileHalfSize;
            xUpdateZone.max.y = xUpdateZone.min.y + tileSize;
            updateRegions.Add(xUpdateZone);
        }

        // We will skip vertical update if there is no displacement along the y-axis or
        // if the x-axis displacement is too large that already covers the entire tile
        if (absDiff.y > 0 && absDiff.x < tileSize)
        {
            AABB2Int yUpdateZone = new AABB2Int();
            if (diff.y < 0)
            {
                yUpdateZone.min.y = newCenter.y - tileHalfSize;
                yUpdateZone.max.y = yUpdateZone.min.y + absDiff.y;
            }
            else
            {
                yUpdateZone.max.y = newCenter.y + tileHalfSize;
                yUpdateZone.min.y = yUpdateZone.max.y - absDiff.y;
            }

            if (diff.x < 0)
            {
                yUpdateZone.max.x = newCenter.x + tileHalfSize;
                yUpdateZone.min.x = yUpdateZone.max.x - tileSize + absDiff.x;
            }
            else
            {
                yUpdateZone.min.x = newCenter.x - tileHalfSize;
                yUpdateZone.max.x = yUpdateZone.min.x + tileSize - absDiff.x;
            }

            updateRegions.Add(yUpdateZone);
        }

        return updateRegions;
    }


    // Snap the center to multiples of the space unit
    private Vector2Int GetSnappedCenter(Vector2 coord, int spaceUnit)
    {
        return Vector2Int.FloorToInt(coord / spaceUnit) * spaceUnit;
    }

    
    private void LoadConfiguration()
    {
        m_clipSize = m_clipmapConfiguration.ClipSize;
        m_updateGridSize = m_clipmapConfiguration.ClipmapUpdateGridSize;
        m_invalidBorder = m_clipmapConfiguration.InvalidBorder;
        m_mipTextureFormat = m_clipmapConfiguration.TextureFormat;
        m_clipmapLevelCount = m_clipmapConfiguration.BaseTexture.Length;

        m_clipHalfSize = m_clipSize >> 1;
        m_mipSize = new int[m_clipmapLevelCount];
        m_mipSizeFloat = new float[m_clipmapLevelCount];
        m_mipHalfSize = new int[m_clipmapLevelCount];
        m_mipHalfSizeFloat = new float[m_clipmapLevelCount];
        m_baseMipTexture = m_clipmapConfiguration.BaseTexture;
        m_clipScaleToMip = new float[m_clipmapLevelCount];
        m_mipScaleToWorld = new float[m_clipmapLevelCount];
        for (int i = 0; i < m_clipmapLevelCount; i++)
        {
            m_mipSize[i] = m_baseMipTexture[i].width;
            m_mipSizeFloat[i] = m_mipSize[i];
            m_mipHalfSize[i] = m_baseMipTexture[i].width >> 1;
            m_mipHalfSizeFloat[i] = m_mipHalfSize[i];
        }

        m_clipmapStackLevelCount = m_clipmapLevelCount - 1;
        m_clipmapStackCache = new Texture2D[m_clipmapStackLevelCount];
        m_clipCenter = new Vector2Int[m_clipmapStackLevelCount];
        m_clipCenterFloat = new Vector4[m_clipmapStackLevelCount];

        m_clipmapCenterSafeRegion = new AABB2Int[m_clipmapStackLevelCount];

        m_tileCacheManager = new TileCacheManager();
        m_tileCacheManager.Initialize(this, m_clipmapConfiguration.baseTextureSize, 
            m_clipmapConfiguration.tileSize, m_clipmapConfiguration.folderName);
    }
    
    
    private void PassStaticUniforms()
    {
        m_Material.SetInteger("_InvalidBorder", m_invalidBorder);
        m_Material.SetInteger("_ClipSize", m_clipSize);
        m_Material.SetInteger("_ClipHalfSize", m_clipHalfSize);
        m_Material.SetInteger("_ClipmapStackLevelCount", m_clipmapStackLevelCount);
        m_Material.SetFloatArray("_MipSize", m_mipSizeFloat);
        m_Material.SetFloatArray("_MipHalfSize", m_mipHalfSizeFloat);
        m_Material.SetFloatArray("_ClipScaleToMip", m_clipScaleToMip);
        m_Material.SetFloatArray("_MipScaleToWorld", m_mipScaleToWorld);
        m_Material.SetTexture("_ClipmapStack", ClipmapStack);
        m_Material.SetTexture("_ClipmapPyramid", ClipmapPyramid);
    }


    private void PassDynamicUniforms()
    {
        m_Material.SetVectorArray("_ClipCenter", m_clipCenterFloat);
        m_Material.SetInteger("_MaxTextureLOD", m_maxTextureLOD);
    }
    
}