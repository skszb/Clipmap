using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.PlayerLoop;


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
    public bool DRAW_DEBUG_INFO;
    
    [SerializeField] private ClipmapConfiguration m_clipmapConfiguration;

    // Faked as data in disk, should be changed to streaming address later 
    private Texture2D[] m_baseMipTextures;
    
    private AABB2Int[] m_baseMipTextureBounds;

    // the bounding box of the safe region that the clipmap center can move, so that the clipmap level does not include areas beyond the mip texture
    private AABB2Int[] m_clipCenterSafeRegions;

    // The length in one dimension of a grid in mip space.
    // Depends on clip center deviation, the clipmap will only update a multiple of MipGridSize pixels in a direction
    private int m_updateGridSize;

    private Material m_Material;

    private TextureFormat m_mipTextureFormat;
    
    private TileCacheManager m_tileCacheManager;

    private int m_cacheTileSize;
    
    // The number of texels in one dimension of look-ahead-cache calculation
    private int m_cacheSize;
    private int m_cacheHalfSize;
    
    
    public Texture2DArray ClipmapStack { get; private set; }

    public Texture2D ClipmapPyramid { get; private set; }

    // The snapped center of each clipmap level in the mip space
    public Vector2Int[] m_clipCenters;
    public Vector2Int[] m_latestValidClipCenters;
    private Vector2Int[] m_clipCentersLastFrame;

    public Vector4[] m_clipCentersFloat; // cached for passing to shader

    public int m_maxTextureLOD = 0;
    
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

    
    private void Awake()
    {
        m_Material = GetComponent<Renderer>().material;
        Initialize();
    }


    public void Initialize()
    {
        LoadConfiguration();
        InitializeCache();
        InitializeClipmap();
        PassStaticUniforms();
    }
    
    
    private void LoadConfiguration()
    {
        // Clipmap
        m_clipSize = m_clipmapConfiguration.ClipSize;
        m_updateGridSize = m_clipmapConfiguration.ClipmapUpdateGridSize;
        m_invalidBorder = m_clipmapConfiguration.InvalidBorder;
        m_mipTextureFormat = m_clipmapConfiguration.TextureFormat;
        m_clipmapLevelCount = m_clipmapConfiguration.BaseTexture.Length;
        m_clipmapStackLevelCount = m_clipmapLevelCount - 1;
        m_clipHalfSize = m_clipSize >> 1;
        m_mipSize = new int[m_clipmapLevelCount];
        m_mipSizeFloat = new float[m_clipmapLevelCount];
        m_mipHalfSize = new int[m_clipmapLevelCount];
        m_mipHalfSizeFloat = new float[m_clipmapLevelCount];
        m_baseMipTextures = m_clipmapConfiguration.BaseTexture;
        m_clipScaleToMip = new float[m_clipmapLevelCount];
        m_mipScaleToWorld = new float[m_clipmapLevelCount];
        m_baseMipTextureBounds = new AABB2Int[m_clipmapLevelCount];
        for (int i = 0; i < m_clipmapLevelCount; i++)
        {
            m_mipSize[i] = m_baseMipTextures[i].width;
            m_mipSizeFloat[i] = m_mipSize[i];
            m_mipHalfSize[i] = m_baseMipTextures[i].width >> 1;
            m_mipHalfSizeFloat[i] = m_mipHalfSize[i];
            m_baseMipTextureBounds[i] = new AABB2Int(0,0,m_mipSize[i], m_mipSize[i]) - m_mipHalfSize[i];
        }
        m_clipCenters = new Vector2Int[m_clipmapStackLevelCount];
        m_clipCentersLastFrame = new Vector2Int[m_clipmapStackLevelCount];
        m_clipCentersFloat = new Vector4[m_clipmapStackLevelCount];
        m_latestValidClipCenters = new Vector2Int[m_clipmapStackLevelCount];
        m_clipCenterSafeRegions = new AABB2Int[m_clipmapStackLevelCount];
        ClipmapStack = new Texture2DArray(m_clipSize, m_clipSize, m_clipmapStackLevelCount, m_mipTextureFormat, false,
            false, true);
        ClipmapPyramid = new Texture2D(m_clipSize, m_clipSize, m_mipTextureFormat, true, false, true);
        
        // Cache
        m_cacheTileSize = m_clipmapConfiguration.tileSize;
        m_tileCacheManager = new TileCacheManager();
    }
    
    
    private void InitializeClipmap()
    {
        int clipScaleToMip = 1 << m_clipmapStackLevelCount;
        int mipScaleToWorld = 1;
        for (int depth = 0; depth < m_clipmapStackLevelCount; 
             depth++, clipScaleToMip >>= 1, mipScaleToWorld <<= 1)
        {
            int safeRegionHalfSize = m_mipHalfSize[depth] - m_clipHalfSize;
            m_clipCenterSafeRegions[depth] = new AABB2Int(-safeRegionHalfSize, -safeRegionHalfSize,
                safeRegionHalfSize, safeRegionHalfSize);
            
            // Set clipmap centers outside the mip area so that their textures will be automatically loaded in the first update
            m_clipCenters[depth] = m_baseMipTextureBounds[depth].min - new Vector2Int(m_mipSize[depth], m_mipSize[depth]);
            m_latestValidClipCenters[depth] = m_clipCenters[depth];
            m_clipCentersLastFrame[depth] = m_clipCenters[depth];
            m_clipCentersFloat[depth].x = m_clipCenters[depth].x;
            m_clipCentersFloat[depth].y = m_clipCenters[depth].y;

            m_clipScaleToMip[depth] = clipScaleToMip;
            m_mipScaleToWorld[depth] = mipScaleToWorld;
        }

        // Use the last level as the clipmap pyramid, it is a fixed texture that covers the whole surface
        int lastLevelIndex = m_clipmapLevelCount - 1;

        Graphics.CopyTexture(m_baseMipTextures[lastLevelIndex], ClipmapPyramid);

        m_clipScaleToMip[lastLevelIndex] = clipScaleToMip;
        m_mipScaleToWorld[lastLevelIndex] = mipScaleToWorld;
        
        m_maxTextureLOD = m_clipmapStackLevelCount; // set to clipmap pyramid at start
    }

    
    private void InitializeCache()
    {
        m_tileCacheManager.Initialize(this, m_clipmapStackLevelCount);
        
        /*
            Cache should cover at least the texture areas of the next possible move
            
                |__|_|
             |^^|^^|^^|
             
            Theoretical minimum: 
            int cacheCapacity1Dim = 1 + Mathf.CeilToInt((m_clipSize + m_updateGridSize) / (float)m_cacheTileSize);
         */
        int cacheCapacity1Dim = 1 + Mathf.CeilToInt((m_clipSize + m_updateGridSize) / (float)m_cacheTileSize);
        int cacheCapacity = cacheCapacity1Dim * cacheCapacity1Dim;
        Debug.Log($"Cache Capacity: {cacheCapacity}.");

        // detection area is the clipSize plus one updateGridSize one each side
        m_cacheSize = m_clipSize + 2 * m_updateGridSize;
        m_cacheHalfSize = m_clipSize >> 1;
        
        for (int depth = 0; depth < m_clipmapStackLevelCount; depth++)
        {
            int baseTextureSize = m_clipmapConfiguration.baseTextureSize[depth];
            m_tileCacheManager.SetCacheAtDepth(depth, baseTextureSize, m_cacheTileSize, 
                m_clipmapConfiguration.folderName[depth], cacheCapacity);
        }
    }
    
    
    public void UpdateCache(Vector2 camCoord2D, float height)
    {
        // TODO: add height logic
        for (int depth = m_clipmapStackLevelCount - 1; depth >= 0; --depth)
        {
            Vector2Int currentClipCenter = m_clipCenters[depth];
            var updateRegions = GetUpdateRegions(m_clipCentersLastFrame[depth], currentClipCenter, m_cacheSize);
            foreach (var region in updateRegions)
            {
                var validRegion = region.Clamp(m_baseMipTextureBounds[depth]);
                if (validRegion.IsValid())
                {
                    m_tileCacheManager.LoadRequiredTiles(validRegion, depth);
                }
            }
        }
    }

    
    public void UpdateClipmap(Vector2 camCoord2D, float height)
    {
        // TODO: add height logic
        
        for (int depth = m_clipmapStackLevelCount - 1; depth >= 0; --depth)
        {
            Vector2Int currentClipCenter = m_clipCenters[depth];
            
            List<AABB2Int> regionsToUpdate = GetUpdateRegions(m_latestValidClipCenters[depth], currentClipCenter, m_clipSize);
            if (!regionsToUpdate.Any())
            {
                m_maxTextureLOD = depth;
                continue;
            }

            // 1. get cached texture tiles where the need-to-update regions lie within 
            var updateRegionInfo = new  List<(TileCacheManager.TextureTileInfo sourceTextureTileInfo, AABB2Int updateRegionBound)>();
            bool allCached = true;
            foreach (AABB2Int region in regionsToUpdate)
            {
                TileCacheManager.TextureLoadResult loadResult = new TileCacheManager.TextureLoadResult();
                allCached = allCached && m_tileCacheManager.GetTiles(region, depth, out loadResult);

                if (loadResult.Successful == null)
                {
                    Debug.Log("null1");
                }
                foreach (TileCacheManager.TextureTileInfo tileInfo in loadResult.Successful)
                {
                    updateRegionInfo.Add((tileInfo, region.Clamp(tileInfo.Bound)));
                }
                foreach (TileCacheManager.TextureTileInfo tileInfo in loadResult.Failed)
                {
                    m_tileCacheManager.LoadRequiredTiles(tileInfo.Bound, depth);
                }
            }
            if (!allCached)
            {
                m_maxTextureLOD = depth + 1;
                break;
            }
            
            // 2. further divide into target regions
            Vector2Int clipmapBottomLeftCorner = currentClipCenter - new Vector2Int(m_clipHalfSize, m_clipHalfSize);
            clipmapBottomLeftCorner = ClipmapUtility.SnapToGrid(clipmapBottomLeftCorner, m_clipSize);

            AABB2Int bottomLeftTile = new AABB2Int(clipmapBottomLeftCorner, clipmapBottomLeftCorner + new Vector2Int(m_clipSize, m_clipSize));
            List<AABB2Int> tilesToUpdate = new List<AABB2Int>
            {
                bottomLeftTile,
                bottomLeftTile + new Vector2Int(m_clipSize, 0),
                bottomLeftTile + new Vector2Int(0, m_clipSize),
                bottomLeftTile + m_clipSize
            };

            int copyIncomplete = 0;
            foreach (AABB2Int tile in tilesToUpdate)
            {
                foreach (var regionPair in updateRegionInfo)
                {
                    AABB2Int regionInBothTiles = regionPair.updateRegionBound.Clamp(tile);
                    if (regionInBothTiles.IsValid())
                    {
                        int srcX = regionInBothTiles.min.x - regionPair.sourceTextureTileInfo.Bound.min.x;
                        int srcY = regionInBothTiles.min.y - regionPair.sourceTextureTileInfo.Bound.min.y;
                        int dstX = regionInBothTiles.min.x - tile.min.x;
                        int dstY = regionInBothTiles.min.y - tile.min.y;
                        Graphics.CopyTexture(regionPair.sourceTextureTileInfo.Texture, 0, 0, srcX, srcY,
                            regionInBothTiles.Width(), regionInBothTiles.Height(),
                            ClipmapStack, depth, 0, dstX, dstY);
                    }
                }
            }

            m_latestValidClipCenters[depth] = currentClipCenter;
            m_clipCentersFloat[depth].x = currentClipCenter.x;
            m_clipCentersFloat[depth].y = currentClipCenter.y;
            
            m_maxTextureLOD = depth;
        }
        PassDynamicUniforms();
    }

    
    public void UpdateCamera(Vector3 camPosition)
    {
        Vector2 camCoord2D = new Vector2(camPosition.x, camPosition.z);
        float camHeight = camPosition.y;
        
        // Update clipmap centers
        for (int depth = 0; depth < m_clipmapStackLevelCount; depth++, camCoord2D /= 2)
        {
            // The coordinate of snapped center is floored, so we added a positive bias of half grid size to the player's position
            // this ensures that the boundary that triggers clipmap update is [-0.5, 0.5) around the center instead of [0, 1);
            Vector2 biasedPosition = camCoord2D + m_updateGridSize * new Vector2(0.5f, 0.5f);

            AABB2Int clipCenterSafeRegion = m_clipCenterSafeRegions[depth];
            Vector2Int updatedClipCenter = ClipmapUtility.SnapToGrid(biasedPosition, m_updateGridSize);
            updatedClipCenter = clipCenterSafeRegion.ClampVec2Int(updatedClipCenter);
            m_clipCenters[depth] = updatedClipCenter;
        }
        
        UpdateCache(camCoord2D, camHeight);
        UpdateClipmap(camCoord2D, camHeight);
        
        Array.Copy(m_clipCenters, m_clipCentersLastFrame, m_clipmapStackLevelCount);
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
    
    
    // get the newly covered region after the clip-region moves, in the form of a list of AABB2Ints
    private static List<AABB2Int> GetUpdateRegions(in Vector2Int oldCenter, in Vector2Int newCenter, int regionSize)
    {
        Vector2Int diff = newCenter - oldCenter;
        int updateWidth = Math.Min(regionSize, Math.Abs(diff.x));
        int regionHalfSize = regionSize / 2;
        
        List<AABB2Int> updateRegions = new List<AABB2Int>();
        // Find the updated regions in current space
        // We separate the update regions into at most 2 parts:
        // (1) the rectangular update zone that is of the size (x,tileSize)
        // (2) the rest of the update zone
        if (updateWidth > 0)
        {
            AABB2Int xUpdateZone = new AABB2Int();
            if (diff.x < 0)
            {
                xUpdateZone.min.x = newCenter.x - regionHalfSize;
                xUpdateZone.max.x = xUpdateZone.min.x + updateWidth;
            }
            else
            {
                xUpdateZone.max.x = newCenter.x + regionHalfSize;
                xUpdateZone.min.x = xUpdateZone.max.x - updateWidth;
            }

            xUpdateZone.min.y = newCenter.y - regionHalfSize;
            xUpdateZone.max.y = xUpdateZone.min.y + regionSize;
            updateRegions.Add(xUpdateZone);
        }

        // We will skip vertical update if there is no displacement along the y-axis or
        // if the x-axis displacement is too large that already covers the entire tile
        int updateHeight = Math.Min(regionSize, Math.Abs(diff.y));
        if (updateHeight > 0 && updateWidth < regionSize)
        {
            AABB2Int yUpdateZone = new AABB2Int();
            if (diff.y < 0)
            {
                yUpdateZone.min.y = newCenter.y - regionHalfSize;
                yUpdateZone.max.y = yUpdateZone.min.y +updateHeight;
            }
            else
            {
                yUpdateZone.max.y = newCenter.y + regionHalfSize;
                yUpdateZone.min.y = yUpdateZone.max.y - updateHeight;
            }

            if (diff.x < 0)
            {
                yUpdateZone.max.x = newCenter.x + regionHalfSize;
                yUpdateZone.min.x = yUpdateZone.max.x - regionSize + updateWidth;
            }
            else
            {
                yUpdateZone.min.x = newCenter.x - regionHalfSize;
                yUpdateZone.max.x = yUpdateZone.min.x + regionSize - updateWidth;
            }
            updateRegions.Add(yUpdateZone);
        }
        return updateRegions;
    }

    private void OnDrawGizmos()
    {
        if (!DRAW_DEBUG_INFO)
        {
            return;
        }
        //
        Color[] cols = new Color[3];
        cols[0] = new Color(1, 0, 0, 0.5f);
        cols[1] = new Color(0, 1, 0, 0.5f);
        cols[2] = new Color(0, 0, 1, 0.5f);

        for (int d = m_clipmapStackLevelCount-1; d >= 0 ; --d)
        {

            Vector3 center = new Vector3(m_clipCenters[d].x, 0, m_clipCenters[d].y) * m_mipScaleToWorld[d];
            Gizmos.color= cols[d];
            Gizmos.DrawWireCube(center, new Vector3(m_clipSize, 1, m_clipSize) * m_mipScaleToWorld[d]);
        }
        
        int depth = 0;
        int cacheTileHalfSize = m_cacheTileSize / 2;
        float mipScaleToWorld = m_mipScaleToWorld[depth];
        foreach (var key in m_tileCacheManager.GetCachedTileCoords(depth))
        {
            Vector3 center = new Vector3(key.x + cacheTileHalfSize, 0, key.y + cacheTileHalfSize) - new Vector3(m_mipHalfSize[depth], 0, m_mipHalfSize[depth]);
            center *= mipScaleToWorld;
            Gizmos.color = new Color(1,0, 1, 0.8f);
            Gizmos.DrawCube(center, new Vector3(m_cacheTileSize * mipScaleToWorld, 5, m_cacheTileSize * mipScaleToWorld));
        }
    }
    
}

