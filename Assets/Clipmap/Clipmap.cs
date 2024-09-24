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

        InitializeMips(worldPosition);
    }

    // Create space for each level in the clipmap by resolutions
    private void InitializeMips(Vector3 worldPosition)
    {
        Vector2 centerInWorldSpace = new Vector2(worldPosition.x, worldPosition.z);
        float height = worldPosition.y;
        Vector2 centerInHomogeneousSpace = centerInWorldSpace / m_worldGridSize;

        int mipSize = m_baseTextureResolution.x;
        Vector2 centerInClipmapSpace = centerInHomogeneousSpace;
        for (int m = 0; m < m_clipmapLevelCount; m++, mipSize /= 2, centerInClipmapSpace /= 2) 
        {
            bool inClipStack = mipSize > m_clipSize;
            if (inClipStack)
            {
                m_clipStackSize++;

                Vector2Int snappedCenterInClipmapSpace = GetSnappedCenter(centerInClipmapSpace);

                m_clipmapCache[m] = new Texture2D(m_baseTexture.width, m_baseTexture.height, m_mipTextureFormat, false, false);
                m_clipmapLevel[m] = new Texture2D(m_clipSize, m_clipSize, m_mipTextureFormat, false, false);

                // load cache from disk
                Graphics.CopyTexture(m_baseTexture, 0, 0, 0, 0, m_baseTexture.width, m_baseTexture.height, m_clipmapCache[0], 0, 0, 0, 0);

                // load mip from cache

                m_clipmapCenter[m] = snappedCenterInClipmapSpace;
            }
            else
            {
                // load entire mip level from disk
                m_clipmapLevel[m] = new Texture2D(mipSize, mipSize, m_mipTextureFormat, false, false);
            }
        }
        Array.Resize(ref m_clipmapCache, m_clipStackSize);
    }

    public void UpdateClipmap(Vector3 worldPosition)
    {
        Vector2 centerInWorldSpace = new Vector2(worldPosition.x, worldPosition.z);
        float height = worldPosition.y;
        int mipSize = m_baseTextureResolution.x;
        Vector2 centerInHomogeneousSpace = centerInWorldSpace / m_worldGridSize;
        Vector2 centerInClipmapSpace = centerInHomogeneousSpace;
        for (int clipmapLevel = 0; clipmapLevel < m_clipStackSize; clipmapLevel++, mipSize /=2, centerInClipmapSpace /= 2)
        {
            Vector2Int snappedCenterInClipmapSpace = GetSnappedCenter(centerInClipmapSpace);
            Vector2Int diff = snappedCenterInClipmapSpace - m_clipmapCenter[clipmapLevel];

            AABB2Int mipBound = new AABB2Int(-mipSize, -mipSize, mipSize, mipSize);
            // load mip from cache
            // TEMP
            //Vector2Int coord = new Vector2Int();
            //coord.x = (snappedCenterInClipmapSpace.x - m_halfSize + (int)m_clipmapCache[m].width / 2);
            //coord.y = (snappedCenterInClipmapSpace.y - m_halfSize + (int)m_clipmapCache[m].width / 2);
            //Graphics.CopyTexture(m_clipmapCache[m], 0, 0, coord.x, coord.y, m_clipSize, m_clipSize, m_clipmapLevel[m], 0, 0, 0, 0);
            //Debug.Log(coord);
            //m_clipmapCenter[m] = snappedCenterInClipmapSpace;

            // load mip from cache

            /* 
            find four quadrants and copy to mip level 
            _____________________________
            |      |      |      |      |  
            |      |      |      |      |  
            |______|______|______|______|  
            |      |     _|___   |      |  
            |      |    | |   |  |      |  
            |______|____|_|___|__|______|  
            |      |    |_|___|  |      |
            |      |      |      |      |
            |______|______|______|______|
            |      |      |      |      |
            |      |      |      |      |
            |______|______|______|______|

            */
            

            Vector2Int bottomLeftTile;

            m_clipmapCenter[clipmapLevel] = snappedCenterInClipmapSpace;
        }
    }

    // get the player's position in homogeneous coordinate
    private Vector2 GetCenterInHomogenousSpace(Vector2 position)
    {
        Vector2 hCoord = position / m_worldGridSize;
        return hCoord;
    }

    
    /*
    _____________________________
    |      |      |      |      |       When the clipcenter changes and makes clip offset moves from O  to N, there are 5 zones need to be updated to ensure correct toroidal addressing
    |      |  5   |      |      |     
    |______|______|______|______|     
    |      |      |      |      |     
    |      |  5   |      |      |       Can be spearate into two two steps and simplify: move up and right
    |______|______N______|______|     
    |      |      |      |      |
    |  2   |  1   |  4   |  4   |
    |______O______|______|______|
    |      |      |      |      |
    |      |  3   |      |      |
    |______|______|______|______|

    */
    private void copyZones()
    {
        // zone 1
        // zone 2
        // zone 3
        // zone 4
        // zone 5
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
