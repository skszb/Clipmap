using System;
using System.Drawing;
using Unity.VisualScripting;
using UnityEngine;

[Serializable]
public struct ClipmapParam
{
    public int clipmapLevelCount;
    public int clipSize;
    public int worldGridSize;
    public int clipmapUpdateGridSize;
    public int invalidBorder;
    public TextureFormat mipTextureFormat;

    // 
    public Vector2Int baseTextureResolution;
    public Texture2D baseTexture;

}

public struct ClipmapLevel
{
    public Vector2Int center;
    public float worldToClipmapScale;  
}

public class Clipmap : MonoBehaviour
{
    #region Internal Variables
    // The resolution of the mip0 texture
    private Vector2Int m_baseTextureResolution;

    private TextureFormat m_mipTextureFormat;

    // The length in one dimension of a grid in world space that binds to one texel.
    // Used to convert player coordinate to homogeneous coordinate: homogeneousCoordinate = worldCoordinate / worldGridSize
    private int m_worldGridSize;

    // The length in one dimension of a grid in texture space.
    // Depends on clip center deviation, the clipmap will only update a multiple of MipGridSize pixels in a direction
    private int m_clipmapUpdateGridSize;

    // The number of texels in one dimension in a stack level
    private int m_clipSize;
    private int m_halfSize;

    private int m_clipStackSize;

    private int m_clipmapLevelCount;

    // The number of texels in one dimension from both ends, this will be used to determine whether to wait for mipTexture update
    private int m_invalidBorder;

    // Faked Disk data, will be replaced with data streaming
    private Texture2D m_baseTexture;

    // The memory chunk that is larger than the actual mip texture, acts as a cache, 
    // Currently load the whole texture to fake as a 2nd-level cache in mem
    private Texture2D[] m_clipmapCache;

    // The actual texture that will be sync with GPU as the mipmap
    private Texture2D[] m_clipmapLevel;

    private Vector2Int[] m_clipmapCenter;

    // the bounding box of the safe region that the clipmap center can move, so that the clipmap level does not include areas beyond the mip texture
    private AABB2Int[] m_clipmapCenterSafeRegion;
    #endregion

    public void Intialize(ClipmapParam param, Vector3 worldPosition)
    {
        m_baseTexture = param.baseTexture; // to be changed to data streaming 
        m_baseTextureResolution = param.baseTextureResolution; // to be changed to data streaming 

        m_clipSize = param.clipSize;
        m_halfSize = m_clipSize / 2;
        m_worldGridSize = param.worldGridSize;
        m_clipmapUpdateGridSize = param.clipmapUpdateGridSize;
        m_invalidBorder = param.invalidBorder;
        m_mipTextureFormat = param.mipTextureFormat;
        m_clipmapLevelCount = param.clipmapLevelCount;

        m_clipStackSize = 0;
        m_clipmapLevel = new Texture2D[param.clipmapLevelCount];
        m_clipmapCache = new Texture2D[param.clipmapLevelCount];
        m_clipmapCenter = new Vector2Int[param.clipmapLevelCount];
        m_clipmapCenterSafeRegion = new AABB2Int[param.clipmapLevelCount];

        InitializeMips(worldPosition);
    }

    // Create space for each level in the clipmap by resolutions
    private void InitializeMips(Vector3 worldPosition)
    {
        int mipSize = m_baseTextureResolution.x;
        for (int m = 0; m < m_clipmapLevelCount; m++, mipSize /= 2) 
        {
            bool inClipStack = mipSize > m_clipSize;
            if (inClipStack)
            {
                m_clipStackSize++;

                m_clipmapCache[m] = new Texture2D(m_baseTexture.width, m_baseTexture.height, m_mipTextureFormat, false, false);
                m_clipmapLevel[m] = new Texture2D(m_clipSize, m_clipSize, m_mipTextureFormat, false, false);

                // load cache from disk
                Graphics.CopyTexture(m_baseTexture, 0, 0, 0, 0, m_baseTexture.width, m_baseTexture.height, m_clipmapCache[0], 0, 0, 0, 0);

                // load mip from cache
                // Initialize all clipmap levels at the very bottom left
                int mipHalfSize = mipSize / 2;
                m_clipmapCenterSafeRegion[m] = new AABB2Int(-mipHalfSize + m_halfSize, -mipHalfSize + m_halfSize, mipHalfSize - m_halfSize, mipHalfSize - m_halfSize);
                m_clipmapCenter[m] = m_clipmapCenterSafeRegion[m].min;
            }
            else
            {
                // load entire mip level from disk
                m_clipmapLevel[m] = new Texture2D(mipSize, mipSize, m_mipTextureFormat, false, false);
  
            }
        }
        Array.Resize(ref m_clipmapCache, m_clipStackSize);
        Array.Resize(ref m_clipmapCenterSafeRegion, m_clipStackSize);
    }

    public void UpdateClipmap(Vector3 worldPosition)
    {
        Vector2 centerInWorldSpace = new Vector2(worldPosition.x, worldPosition.z);
        float height = worldPosition.y;
        int mipSize = m_baseTextureResolution.x;
        Vector2 centerInHomogeneousSpace = centerInWorldSpace / m_worldGridSize;
        Vector2 centerInClipmapSpace = centerInHomogeneousSpace;
        for (int levelIndex = 0; levelIndex < m_clipStackSize; levelIndex++, mipSize /=2, centerInClipmapSpace /= 2)
        {
            AABB2Int clipmapCenterSafeRegion = m_clipmapCenterSafeRegion[levelIndex];
            Vector2Int updatedClipmapCenter = GetSnappedCenter(centerInClipmapSpace);
            updatedClipmapCenter.x = Math.Clamp(updatedClipmapCenter.x, clipmapCenterSafeRegion.min.x, clipmapCenterSafeRegion.max.x);
            updatedClipmapCenter.y = Math.Clamp(updatedClipmapCenter.y, clipmapCenterSafeRegion.min.y, clipmapCenterSafeRegion.max.y);

            Vector2Int diff = updatedClipmapCenter - m_clipmapCenter[levelIndex];
            Vector2Int absDiff = new Vector2Int(Math.Abs(diff.x), Math.Abs(diff.y));

            // find the updated regions in mipmap space
            AABB2Int verticalUpdateZone = new AABB2Int();
            if (absDiff.x > 0)
            {
                if (diff.x < 0)
                {
                    verticalUpdateZone.min.x = updatedClipmapCenter.x - m_halfSize;
                    verticalUpdateZone.max.x = verticalUpdateZone.min.x + absDiff.x;
                }
                else
                {
                    verticalUpdateZone.max.x = updatedClipmapCenter.x + m_halfSize;
                    verticalUpdateZone.min.x = verticalUpdateZone.max.x - absDiff.x;
                }
                verticalUpdateZone.min.y = updatedClipmapCenter.y - m_halfSize;
                verticalUpdateZone.max.y = verticalUpdateZone.min.y + m_clipSize;
            }

            AABB2Int horizontalUpdateZone = new AABB2Int();
            if (absDiff.y > 0 && absDiff.x < m_clipSize)
            {
                if (diff.y < 0)
                {
                    horizontalUpdateZone.min.y = updatedClipmapCenter.y - m_halfSize;
                    horizontalUpdateZone.max.y = horizontalUpdateZone.min.y + absDiff.y;
                }
                else
                {
                    horizontalUpdateZone.max.y = updatedClipmapCenter.y + m_halfSize;
                    horizontalUpdateZone.min.y = horizontalUpdateZone.max.y - absDiff.y;
                }

                if (diff.x < 0)
                {
                    horizontalUpdateZone.max.x = updatedClipmapCenter.x + m_halfSize;
                    horizontalUpdateZone.min.x = horizontalUpdateZone.max.x - m_clipSize + absDiff.x;
                }
                else
                {
                    horizontalUpdateZone.min.x = updatedClipmapCenter.x - m_halfSize;
                    horizontalUpdateZone.max.x = horizontalUpdateZone.min.x + m_clipSize - absDiff.x;
                }
            }

            /* 
            find four quadrants and copy to mip level 

            +---------------------------+
            |      |      |      |      |  
            |      |      |      |      |  
            +------+------+------+------+  
            |      |  +---|---+  |      |  
            |      |  |   |   |  |      |  
            +------+--+---+---+--+------+  
            |      |  |   |   |  |      |
            |      |  +---|---+  |      |
            +------+------+------+------+  
            |      |      |      |      |
            |      |      |      |      |
            +------+------+------+------+  

            */
            Vector2Int clipmapBottomLeftCorner = updatedClipmapCenter - new Vector2Int(m_halfSize, m_halfSize);
            clipmapBottomLeftCorner.x = (int)Mathf.Floor((float)clipmapBottomLeftCorner.x / (float)m_clipSize) * m_clipSize;
            clipmapBottomLeftCorner.y = (int)Mathf.Floor((float)clipmapBottomLeftCorner.y / (float)m_clipSize) * m_clipSize;

            AABB2Int bottomLeftTile = new AABB2Int(clipmapBottomLeftCorner, 
                                                   clipmapBottomLeftCorner + new Vector2Int(m_clipSize, m_clipSize));

            AABB2Int[] tilesToUpdate = {bottomLeftTile,
                                        bottomLeftTile + new Vector2Int(0, m_clipSize),
                                        bottomLeftTile + new Vector2Int(m_clipSize, 0),
                                        bottomLeftTile + m_clipSize};

            foreach(AABB2Int tile in tilesToUpdate)
            {
                AABB2Int verticalPart = verticalUpdateZone.Clamp(tile);
                if (verticalPart.isValid())
                {
                    int dstX = verticalPart.min.x - tile.min.x;
                    int dstY = verticalPart.min.y - tile.min.y;
                    Graphics.CopyTexture(m_clipmapCache[levelIndex], 0, 0, verticalPart.min.x, verticalPart.min.y,
                                         verticalPart.max.x - verticalPart.min.x, verticalPart.max.y - verticalPart.min.y,
                                         m_clipmapLevel[levelIndex], 0, 0, dstX, dstY);
                }

            }
            m_clipmapCenter[levelIndex] = updatedClipmapCenter;
        }

    }

    // get the player's position in homogeneous coordinate
    private Vector2 GetCenterInHomogenousSpace(Vector2 position)
    {
        Vector2 hCoord = position / m_worldGridSize;
        return hCoord;
    }

    private void loadFromCacheToLevel(int clipmapLevel, int srcX, int srcY, int width, int height, int clipLevelX, int clipLeveY)
    {
        Texture2D cache = m_clipmapCache[clipmapLevel];
        Texture2D stackLevel = m_clipmapLevel[clipmapLevel];
        Mathf.Sign(-123);
        // convert srcX 

    }

    // Snap the center to multiples of m_mipUpdateGridSize
    private Vector2Int GetSnappedCenter(Vector2 worldCenter)
    {
        return Vector2Int.FloorToInt(worldCenter / m_clipmapUpdateGridSize) * m_clipmapUpdateGridSize;
    }

    public Texture2D GetClipmapLevel(int level)
    {
        if (level >= m_clipStackSize) 
        {
            return null;
        }

        return m_clipmapLevel[level];
    }

    private void OnDrawGizmos()
    {
        // Gizmos.DrawWireCube(new Vector3(0, 1, 0), new Vector3(4, 1, 4));
    }
};
