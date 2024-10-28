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
    private int m_clipmapUpdateGridSize;

    private Material m_Material;

    private TextureFormat m_mipTextureFormat;


    private void Awake()
    {
        m_Material = GetComponent<Renderer>().material;
        var ty = SystemInfo.copyTextureSupport;
        Initialize();
    }


    public void Initialize()
    {
        m_clipSize = m_clipmapConfiguration.ClipSize;
        m_worldScale = m_clipmapConfiguration.WorldScale;
        m_clipmapUpdateGridSize = m_clipmapConfiguration.ClipmapUpdateGridSize;
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
        for (var i = 0; i < m_clipmapLevelCount; i++)
        {
            m_mipSize[i] = m_baseMipTexture[i].width;
            m_mipSizeFloat[i] = m_mipSize[i];
            m_mipHalfSize[i] = m_baseMipTexture[i].width >> 1;
            m_mipHalfSizeFloat[i] = m_mipHalfSize[i];
        }

        m_clipmapStackLevelCount = m_clipmapLevelCount - 1;
        m_clipmapStackCache = new Texture2D[m_clipmapStackLevelCount];
        m_clipmapCenterInMipSpace = new Vector2Int[m_clipmapStackLevelCount];
        m_clipmapCenterInMipSpaceFloat = new Vector4[m_clipmapStackLevelCount];

        m_clipmapCenterSafeRegion = new AABB2Int[m_clipmapStackLevelCount];

        InitializeMips();

        m_Material.SetFloat("_WorldGridSize", m_worldScale);
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


    // Create space for each level in the clipmap
    private void InitializeMips()
    {
        ClipmapStack = new Texture2DArray(m_clipSize, m_clipSize, m_clipmapStackLevelCount, m_mipTextureFormat, false,
            false, true);
        ClipmapPyramid = new Texture2D(m_clipSize, m_clipSize, m_mipTextureFormat, true, false, true);


        var clipScaleToMip = 1 << m_clipmapStackLevelCount;
        var mipScaleToWorld = 1;
        for (var mipLevelIndex = 0;
             mipLevelIndex < m_clipmapStackLevelCount;
             mipLevelIndex++, clipScaleToMip >>= 1, mipScaleToWorld <<= 1)
        {
            var mipSize = m_mipSize[mipLevelIndex];

            // Initialize cache from disk, currently load the whole mip texture, should change to data streaming later
            m_clipmapStackCache[mipLevelIndex] = new Texture2D(mipSize, mipSize, m_mipTextureFormat, false, false);
            var mipmapLevelDiskData = m_baseMipTexture[mipLevelIndex];
            var clipmapLevelCache = m_clipmapStackCache[mipLevelIndex];
            Graphics.CopyTexture(mipmapLevelDiskData, 0, 0, 0, 0, clipmapLevelCache.width, clipmapLevelCache.height,
                clipmapLevelCache, 0, 0, 0, 0);

            // Initialize clipmap stack levels
            // Set clipmap centers outside the mip area so that their textures will be automatically loaded in the first update
            var safeRegionHalfSize = m_mipHalfSize[mipLevelIndex] - m_clipHalfSize;
            m_clipmapCenterSafeRegion[mipLevelIndex] = new AABB2Int(-safeRegionHalfSize, -safeRegionHalfSize,
                safeRegionHalfSize, safeRegionHalfSize);
            m_clipmapCenterInMipSpace[mipLevelIndex] = m_clipmapCenterSafeRegion[mipLevelIndex].min -
                                                       new Vector2Int(m_clipSize, m_clipSize);
            m_clipmapCenterInMipSpaceFloat[mipLevelIndex].x = m_clipmapCenterInMipSpace[mipLevelIndex].x;
            m_clipmapCenterInMipSpaceFloat[mipLevelIndex].y = m_clipmapCenterInMipSpace[mipLevelIndex].y;

            m_clipScaleToMip[mipLevelIndex] = clipScaleToMip;
            m_mipScaleToWorld[mipLevelIndex] = mipScaleToWorld;
        }

        // Use the last level as the clipmap pyramid, it is a fixed texture that covers the whole surface
        var lastLevelIndex = m_clipmapLevelCount - 1;

        Graphics.CopyTexture(m_baseMipTexture[lastLevelIndex], ClipmapPyramid);

        m_clipScaleToMip[lastLevelIndex] = clipScaleToMip;
        m_mipScaleToWorld[lastLevelIndex] = mipScaleToWorld;
    }


    public void UpdateClipmap(Vector3 cameraPositionInWorldSpace)
    {
        var centerInWorldSpace = new Vector2(cameraPositionInWorldSpace.x, cameraPositionInWorldSpace.z);
        var height = cameraPositionInWorldSpace.y;

        var centerInHomogeneousSpace = centerInWorldSpace / m_worldScale;
        var centerInMipSpace = centerInHomogeneousSpace;
        for (var clipmapStackLevelIndex = 0;
             clipmapStackLevelIndex < m_clipmapStackLevelCount;
             clipmapStackLevelIndex++, centerInMipSpace /= 2)
        {
            // The coordinate of snapped center is floored, so we added a positive bias of half grid size to the player's position 
            // this ensures that the boundary that triggers clipmap update is [-0.5, 0.5) around the center instead of [0, 1);
            var updatedClipmapCenter =
                GetSnappedCenter(centerInMipSpace + m_clipmapUpdateGridSize * new Vector2(0.5f, 0.5f),
                    m_clipmapUpdateGridSize);

            var clipmapCenterSafeRegion = m_clipmapCenterSafeRegion[clipmapStackLevelIndex];
            updatedClipmapCenter.x = Math.Clamp(updatedClipmapCenter.x, clipmapCenterSafeRegion.min.x,
                clipmapCenterSafeRegion.max.x);
            updatedClipmapCenter.y = Math.Clamp(updatedClipmapCenter.y, clipmapCenterSafeRegion.min.y,
                clipmapCenterSafeRegion.max.y);

            var regionsToUpdate = GetUpdateRegions(m_clipmapCenterInMipSpace[clipmapStackLevelIndex],
                updatedClipmapCenter, m_clipSize);

            // We are updating from the level of highest precision, so we can safely skip the rest if current one doesn't need update
            if (!regionsToUpdate.Any()) break;

            var clipmapBottomLeftCorner = updatedClipmapCenter - new Vector2Int(m_clipHalfSize, m_clipHalfSize);
            // clipmapBottomLeftCorner.x = m_clipSize * ClipmapUtil.FloorDivision(clipmapBottomLeftCorner.x, m_clipSize);
            // clipmapBottomLeftCorner.y = m_clipSize * ClipmapUtil.FloorDivision(clipmapBottomLeftCorner.y, m_clipSize);
            clipmapBottomLeftCorner = m_clipSize * ClipmapUtil.FloorDivision(clipmapBottomLeftCorner, m_clipSize);

            var bottomLeftTile = new AABB2Int(clipmapBottomLeftCorner,
                clipmapBottomLeftCorner + new Vector2Int(m_clipSize, m_clipSize));

            var tilesToUpdate = new List<AABB2Int>
            {
                bottomLeftTile,
                bottomLeftTile + new Vector2Int(m_clipSize, 0),
                bottomLeftTile + new Vector2Int(0, m_clipSize),
                bottomLeftTile + m_clipSize
            };

            var mipHalfSize = m_mipHalfSize[clipmapStackLevelIndex];
            foreach (var tile in tilesToUpdate)
            foreach (var updateRegion in regionsToUpdate)
            {
                var updateRegionInTile = updateRegion.Clamp(tile);
                if (updateRegionInTile.IsValid())
                {
                    var srcX = updateRegionInTile.min.x + mipHalfSize;
                    var srcY = updateRegionInTile.min.y + mipHalfSize;
                    var dstX = updateRegionInTile.min.x - tile.min.x;
                    var dstY = updateRegionInTile.min.y - tile.min.y;
                    Graphics.CopyTexture(m_clipmapStackCache[clipmapStackLevelIndex], 0, 0, srcX, srcY,
                        updateRegionInTile.Width(), updateRegionInTile.Height(),
                        ClipmapStack, clipmapStackLevelIndex, 0, dstX, dstY);
                }
            }

            m_clipmapCenterInMipSpace[clipmapStackLevelIndex] = updatedClipmapCenter;
            m_clipmapCenterInMipSpaceFloat[clipmapStackLevelIndex].x = updatedClipmapCenter.x;
            m_clipmapCenterInMipSpaceFloat[clipmapStackLevelIndex].y = updatedClipmapCenter.y;
        }

        m_Material.SetVectorArray("_ClipmapCenter", m_clipmapCenterInMipSpaceFloat);
    }


    // A method for getting the new covered regions due to square tile movement, in the form of list of AABB2Ints
    private static List<AABB2Int> GetUpdateRegions(in Vector2Int oldCenter, in Vector2Int newCenter, int tileSize)
    {
        var diff = newCenter - oldCenter;
        var absDiff = new Vector2Int(Math.Abs(diff.x), Math.Abs(diff.y));
        var tileHalfSize = tileSize / 2;

        var updateRegions = new List<AABB2Int>();
        // Find the updated regions in current space
        // We separate the update regions into at most 2 parts:
        // (1) the rectangular update zone that is of the size (x,tileSize)
        // (2) the rest of the update zone
        if (absDiff.x > 0)
        {
            var xUpdateZone = new AABB2Int();
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
            var yUpdateZone = new AABB2Int();
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

    #region Variables that sync with the surface shader

    public Texture2DArray ClipmapStack { get; private set; }

    public Texture2D ClipmapPyramid { get; private set; }

    // The snapped center of each clipmap level in the mip space
    private Vector2Int[] m_clipmapCenterInMipSpace;
    private Vector4[] m_clipmapCenterInMipSpaceFloat; // cached for passing to shader

    /* -------- Only sync when initialize -------- */

    // The length in one dimension of a grid in world space that binds to one texel.
    // Used to convert player coordinate to homogeneous coordinate (mip0 coordinate): homogeneousCoordinate = worldCoordinate / m_worldScale
    private int m_worldScale = 1;

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
}