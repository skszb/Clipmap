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

    // The memory chunk that is larger than the actual mip texture, acts as a cache, 
    // Currently load the whole texture to fake as a 2nd-level cache in mem
    private Texture2D[] m_clipmapStackCache;
    
    // the bounding box of the safe region that the clipmap center can move, so that the clipmap level does not include areas beyond the mip texture
    private AABB2Int[] m_clipmapCenterSafeRegion;

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
    private Vector2Int[] m_clipCenters;
    private Vector4[] m_clipCentersFloat; // cached for passing to shader
    private Vector2Int[] m_latestValidClipCenters;

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


    public void UpdateClipmap(Vector3 cameraPositionInWorldSpace)
    {
        Vector2 centerInWorldSpace = new Vector2(cameraPositionInWorldSpace.x, cameraPositionInWorldSpace.z);
        float height = cameraPositionInWorldSpace.y;

        UpdateCurrentCenters(centerInWorldSpace);

        m_maxTextureLOD = 0;
        
        for (int depth = 0; depth < m_clipmapStackLevelCount;  depth++)
        {
            Vector2Int updatedClipCenter = m_clipCenters[depth];
            
            // We are updating from the level of highest precision, so we can safely skip the rest if current one doesn't need update
            List<AABB2Int> regionsToUpdate = GetUpdateRegions(m_latestValidClipCenters[depth], m_clipCenters[depth], m_clipSize);
            if (!regionsToUpdate.Any()) break;
            
            // 1. get cached texture tiles where the need-to-update regions lie within 
            var regionPairsToUpdate = new  List<(Texture2D textureTile, AABB2Int tileBound, AABB2Int croppedUpdateRegion)>();
            foreach (AABB2Int region in regionsToUpdate)
            {
                regionPairsToUpdate.AddRange(m_tileCacheManager.GetTiles(region, depth));
            }
            
            // 2. further divide into target regions
            Vector2Int clipmapBottomLeftCorner = m_clipCenters[depth] - new Vector2Int(m_clipHalfSize, m_clipHalfSize);
            clipmapBottomLeftCorner = ClipmapUtil.SnapToGrid(clipmapBottomLeftCorner, m_clipSize);

            AABB2Int bottomLeftTile = new AABB2Int(clipmapBottomLeftCorner, clipmapBottomLeftCorner + new Vector2Int(m_clipSize, m_clipSize));
            List<AABB2Int> tilesToUpdate = new List<AABB2Int>
            {
                bottomLeftTile,
                bottomLeftTile + new Vector2Int(m_clipSize, 0),
                bottomLeftTile + new Vector2Int(0, m_clipSize),
                bottomLeftTile + m_clipSize
            };

            int copyIncomplete = 0;
            int mipHalfSize = m_mipHalfSize[depth];
            foreach (AABB2Int tile in tilesToUpdate)
            {
                foreach (var regionPair in regionPairsToUpdate)
                {
                    if (regionPair.textureTile is null)
                    {
                        copyIncomplete = 1;
                        m_tileCacheManager.LoadTiles(regionPair.tileBound, depth); // TODO: replace this with look ahead cache
                        continue;
                    }
                    
                    AABB2Int regionInBothTiles = regionPair.croppedUpdateRegion.ClampBy(tile);
                    if (regionInBothTiles.IsValid())
                    {
                        int srcX = regionInBothTiles.min.x - regionPair.tileBound.min.x;
                        int srcY = regionInBothTiles.min.y - regionPair.tileBound.min.y;
                        int dstX = regionInBothTiles.min.x - tile.min.x;
                        int dstY = regionInBothTiles.min.y - tile.min.y;
                        Graphics.CopyTexture(regionPair.textureTile, 0, 0, srcX, srcY,
                            regionInBothTiles.Width(), regionInBothTiles.Height(),
                            ClipmapStack, depth, 0, dstX, dstY);
                    }
                }
            }
            m_maxTextureLOD += copyIncomplete;
            
            if (copyIncomplete == 0)
            {
                m_latestValidClipCenters[depth] = updatedClipCenter;
                m_clipCentersFloat[depth].x = updatedClipCenter.x;
                m_clipCentersFloat[depth].y = updatedClipCenter.y;
            }
            
        }

        PassDynamicUniforms();
    }

    
    private void UpdateCurrentCenters(Vector2 playerPosition)
    {
        
        for (int depth = 0; depth < m_clipmapStackLevelCount; depth++, playerPosition /= 2)
        {
            // The coordinate of snapped center is floored, so we added a positive bias of half grid size to the player's position 
            // this ensures that the boundary that triggers clipmap update is [-0.5, 0.5) around the center instead of [0, 1);
            Vector2 biasedPosition = playerPosition + m_updateGridSize * new Vector2(0.5f, 0.5f);

            AABB2Int clipmapCenterSafeRegion = m_clipmapCenterSafeRegion[depth];
            Vector2Int updatedClipCenter = ClipmapUtil.SnapToGrid(biasedPosition, m_updateGridSize);
            updatedClipCenter = clipmapCenterSafeRegion.ClampVec2Int(updatedClipCenter);
            m_clipCenters[depth] = updatedClipCenter;
        }
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
        m_clipCenters = new Vector2Int[m_clipmapStackLevelCount];
        m_clipCentersFloat = new Vector4[m_clipmapStackLevelCount];
        m_latestValidClipCenters = new Vector2Int[m_clipmapStackLevelCount];

        m_clipmapCenterSafeRegion = new AABB2Int[m_clipmapStackLevelCount];

        m_tileCacheManager = new TileCacheManager();
        m_tileCacheManager.Initialize(this, m_clipmapConfiguration.baseTextureSize, 
            m_clipmapConfiguration.tileSize, m_clipmapConfiguration.folderName);
        
        ClipmapStack = new Texture2DArray(m_clipSize, m_clipSize, m_clipmapStackLevelCount, m_mipTextureFormat, false,
            false, true);
        ClipmapPyramid = new Texture2D(m_clipSize, m_clipSize, m_mipTextureFormat, true, false, true);
    }
    
    
    private void InitializeMips()
    {
        int clipScaleToMip = 1 << m_clipmapStackLevelCount;
        int mipScaleToWorld = 1;
        for (int depth = 0; depth < m_clipmapStackLevelCount; 
             depth++, clipScaleToMip >>= 1, mipScaleToWorld <<= 1)
        {
            int mipSize = m_mipSize[depth];

            // Initialize cache from disk, currently load the whole mip texture, should change to data streaming later
            m_clipmapStackCache[depth] = new Texture2D(mipSize, mipSize, m_mipTextureFormat, false, false);
            Texture2D mipmapLevelDiskData = m_baseMipTexture[depth];
            Texture2D clipmapLevelCache = m_clipmapStackCache[depth];
            Graphics.CopyTexture(mipmapLevelDiskData, 0, 0, 0, 0, clipmapLevelCache.width, clipmapLevelCache.height,
                clipmapLevelCache, 0, 0, 0, 0);
            // Revised Version TODO: replace code above
            // m_tileCacheManager.LoadTiles(new AABB2Int(0, 0, mipSize,  mipSize) - mipSize / 2, depth);

            // Initialize clipmap stack levels
            // Set clipmap centers outside the mip area so that their textures will be automatically loaded in the first update
            int safeRegionHalfSize = m_mipHalfSize[depth] - m_clipHalfSize;
            m_clipmapCenterSafeRegion[depth] = new AABB2Int(-safeRegionHalfSize, -safeRegionHalfSize,
                safeRegionHalfSize, safeRegionHalfSize);
            m_clipCenters[depth] = m_clipmapCenterSafeRegion[depth].min -
                                                       new Vector2Int(m_clipSize, m_clipSize);
            m_latestValidClipCenters[depth] = m_clipCenters[depth];
            m_clipCentersFloat[depth].x = m_clipCenters[depth].x;
            m_clipCentersFloat[depth].y = m_clipCenters[depth].y;

            m_clipScaleToMip[depth] = clipScaleToMip;
            m_mipScaleToWorld[depth] = mipScaleToWorld;
        }

        // Use the last level as the clipmap pyramid, it is a fixed texture that covers the whole surface
        int lastLevelIndex = m_clipmapLevelCount - 1;

        Graphics.CopyTexture(m_baseMipTexture[lastLevelIndex], ClipmapPyramid);

        m_clipScaleToMip[lastLevelIndex] = clipScaleToMip;
        m_mipScaleToWorld[lastLevelIndex] = mipScaleToWorld;
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
        m_Material.SetVectorArray("_ClipCenter", m_clipCentersFloat);
        m_Material.SetInteger("_MaxTextureLOD", m_maxTextureLOD);
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
}