using Unity.VisualScripting;
using UnityEngine;

public struct ClipmapParam
{
    public int clipmapLevels;
    public int clipSize;
    public int worldGridSize;
    public int mipUpdateGridSize;
    public int invalidBorder;
    public TextureFormat mipTextureFormat;

    // 
    public Vector2Int baseResolution;
    public Texture2D baseTexture;

}

public struct ClipmapLevel
{
    public Vector2Int center;
}

public class Clipmap : MonoBehaviour
{
    // the internal texture format used for each mip level when streaming data from disk
    public TextureFormat MipTextureFormat;

    // The debugging renderers to show each mip level
    [SerializeField]
    private Renderer[] mipDisplay;

    [SerializeField]
    private int m_mipLevelCount;

    #region Internal Variables
    // The resolution of the mip0 texture
    private Vector2Int m_baseTextureResolution;

    // The length in one dimension of a grid in world space that binds to one texel.
    // Used to convert player coordinate to homogeneous coordinate: hCoord = wCoord / worldGridSize
    private int m_worldGridSize;

    // The length in one dimension of a grid in texture space.
    // Depends on clip center deviation, the clipmap will only update a multiple of MipGridSize pixels in a direction
    private int m_mipUpdateGridSize;

    // The number of texels in one dimension in a stack level
    private int m_clipSize;
    private int m_halfSize;

    // The number of texels in one dimension from both ends, this will be used to determine whether to wait for mipTexture update
    private int m_invalidBorder;

    // Faked Disk data, will be replaced with data streaming
    private Texture2D m_baseTexture;

    // The memory chunk that is larger than the actual mip texture, acts as a cache, 
    // Currently load the whole texture to fake as a 2nd-level cache in mem
    private Texture2D[] m_clipmapCache;

    // The actual texture that will be sync with GPU as the mipmap
    private Texture2D[] m_clipmapLevel;

    private Vector2Int[] m_clipmapCenter;   // range - +
    #endregion

    private void Start()
    {
        //************** DEBUG Param ***************************************************
        m_baseTextureResolution = new Vector2Int(2048, 2048);
        m_worldGridSize = 10;
        m_mipUpdateGridSize = 4;
        m_clipSize = 512;
        MipTextureFormat = TextureFormat.RGBA32;
        //******************************************************************************

        Intialize();

    }

    private void Intialize()
    {
        m_clipmapLevel = new Texture2D[m_mipLevelCount];
        m_clipmapCache = new Texture2D[m_mipLevelCount];
        m_clipmapCenter = new Vector2Int[m_mipLevelCount];
        m_halfSize = m_clipSize / 2;
        InitMips();

        // bind debug textures
        mipDisplay[0].material.SetTexture("_Mip", m_clipmapLevel[0]);
    }

    // Create space for each level in the clipmap by resolutions
    private void InitMips()
    {
        int clipmapLevelSize = m_baseTexture.width;
        Vector2 center = GetCenterInHomogenousSpace();

        for (int m = 0; m < 1; m++, clipmapLevelSize /= 2, center /= 2) 
        {
            bool inClipStack = clipmapLevelSize > m_clipSize;
            if (inClipStack)
            {
                // load cache from disk
                m_clipmapCache[0] = new Texture2D(m_baseTexture.width, m_baseTexture.height, MipTextureFormat, false, false);
                Graphics.CopyTexture(m_baseTexture, 0, 0, 0, 0, clipmapLevelSize, clipmapLevelSize, m_clipmapCache[0], 0, 0, 0, 0);
      
                // load mip from cache
                m_clipmapLevel[m] = new Texture2D(m_clipSize, m_clipSize, MipTextureFormat, false, false);

                Vector2Int centerInTexturespace = Vector2Int.FloorToInt(center) + Vector2Int.FloorToInt(m_baseTexture.Size())/ 2;
                m_clipmapCenter[m] = centerInTexturespace;
                Graphics.CopyTexture(m_clipmapCache[m], 0, 0, centerInTexturespace.x - m_halfSize, centerInTexturespace.y - m_halfSize, m_clipSize, m_clipSize, m_clipmapLevel[m], 0, 0, 0, 0);
            }
            else
            {
                // load entire mip from disk
                m_clipmapLevel[m] = new Texture2D(clipmapLevelSize, clipmapLevelSize, MipTextureFormat, false, false);
            }
        }
    }

    private void UpdateClipmap(Vector2 c)
    {
        // load cached area and load mip from cached area
        Vector2 center = GetCenterInHomogenousSpace();
        for (int m = 0; m < 1; m++, center /= 2)
        {
            if (m_clipmapLevel[m].width < m_clipSize) { break; }

            Vector2Int centerInTextureSpace = Vector2Int.FloorToInt(center) + Vector2Int.FloorToInt(m_baseTexture.Size()) / 2;
            Vector2Int diff = centerInTextureSpace - m_clipmapCenter[m];

            int updateWidth = Mathf.Abs(diff.x);
            int updateHeight = Mathf.Abs(diff.y);
            // update the whole texture when displacement is too large
            if (updateWidth > m_clipSize || updateHeight > m_clipSize)
            {
                Graphics.CopyTexture(m_clipmapCache[m], 0, 0, centerInTextureSpace.x - m_halfSize, centerInTextureSpace.y - m_halfSize, m_clipSize, m_clipSize, m_clipmapLevel[m], 0, 0, 0, 0);
                m_clipmapCenter[m] = centerInTextureSpace;
                continue;
            }

            // update by parts


        }

        // actual center = virtual center + offset
    }

    // get the player's position in homogeneous coordinate
    private Vector2 GetCenterInHomogenousSpace(Vector2 position)
    {
        Vector2 wCoord = new Vector2(playerPawn.position.x, playerPawn.position.z) - 
                            new Vector2(worldOrigin.position.x, worldOrigin.position.z);

        Vector2 hCoord = wCoord / m_worldGridSize;
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

    private void clipToTexture() {}
    private void textureToClip() {}

    private void OnDrawGizmos()
    {
        // Gizmos.DrawWireCube(new Vector3(0, 1, 0), new Vector3(4, 1, 4));
    }
};
