using Unity.VisualScripting;
using UnityEngine;

public struct ClipmapLevel
{
    public int baseResolution;
    public int mipLevel;
    public Texture2D texture;
    public Vector2Int offset;
    public Vector2Int center;
}

public class Clipmap : MonoBehaviour
{
    // The pawn indicating the player position
    [SerializeField]
    private Transform playerPawn;

    // The pawn indicating the world's origin
    [SerializeField]
    private Transform worldOrigin;

    // The length in one dimension of a grid in world space that binds to one texel.
    // Used to convert player coordinate to homogeneous coordinate: hCoord = wCoord / worldGridSize
    public readonly int WorldGridSize = 10;

    // The length in one dimension of a grid in texture space.
    // Depends on clip center deviation, the clipmap will only update a multiple of MipGridSize pixels in a direction
    public readonly int MipGridSize = 4;

    // The number of texels in one dimension in a stack level
    public int ClipSize;

    // The number of texels in one dimension from both ends, this will be used to determine whether to wait for mipTexture update
    public int InvalidBorder;

    // The Number of miplevels that will be supported, will be used to calculate grid size
    public int MipLevelCount;

    // Faked Disk data, will be replaced with data streaming
    public Texture2D BaseTexture;

    // the internal texture format used for each mip level when streaming data from disk
    public TextureFormat MipTextureFormat;

    // The debugging renderers to show each mip level
    [SerializeField]
    private Renderer[] mipDisplay;


    #region Internal Variables
    // The memory chunk that is larger than the actual mip texture, acts as a cache, 
    // Currently load the whole texture to fake as a 2nd-level cache in mem
    // TODO: Adjust to TextureArray with up to 3 mip levels
    private Texture2D[] m_clipmapCache;

    // The actual texture that will be sync with GPU as the mipmap
    // Updates every frame?
    // TODO: Adjust to TextureArray with up to 3 mip levels
    private Texture2D[] m_clipmapTexture;

    private Vector2Int[] m_clipmapCenter;
    private Vector2Int[] m_clipmapOffset;

    private int m_halfSize;


    #endregion

    private void Start()
    {
        //************** DEBUG Param ***************************************************
        MipLevelCount = 1;
        ClipSize = 512;
        MipTextureFormat = TextureFormat.RGBA32;
        //******************************************************************************
        Intialize();

    }

    private void Update()
    {
        UpdateClipmap();
    }

    private void Intialize()
    {
        m_clipmapTexture = new Texture2D[MipLevelCount];
        m_clipmapCache = new Texture2D[MipLevelCount];
        m_clipmapCenter = new Vector2Int[MipLevelCount];
        m_clipmapOffset = new Vector2Int[MipLevelCount];
        m_halfSize = ClipSize / 2;

        InitMips();

        // bind debug textures
        mipDisplay[0].material.SetTexture("_Mip", m_clipmapTexture[0]);
    }

    // Create space for each level in the clipmap by resolutions
    private void InitMips()
    {
        int clipmapLevelSize = BaseTexture.width;
        Vector2 center = GetCenterInHomogenousSpace();

        for (int m = 0; m < 1; m++, clipmapLevelSize /= 2, center /= 2) 
        {
            bool inClipStack = clipmapLevelSize > ClipSize;
            if (inClipStack)
            {
                // load cache from disk
                m_clipmapCache[0] = new Texture2D(BaseTexture.width, BaseTexture.height, MipTextureFormat, false, false);
                Graphics.CopyTexture(BaseTexture, 0, 0, 0, 0, clipmapLevelSize, clipmapLevelSize, m_clipmapCache[0], 0, 0, 0, 0);
      
                // load mip from cache
                m_clipmapTexture[m] = new Texture2D(ClipSize, ClipSize, MipTextureFormat, false, false);

                m_clipmapOffset[m] = new Vector2Int();
                Vector2Int centerInTexturespace = Vector2Int.FloorToInt(center) + Vector2Int.FloorToInt(BaseTexture.Size())/ 2;
                m_clipmapCenter[m] = centerInTexturespace;
                Graphics.CopyTexture(m_clipmapCache[m], 0, 0, centerInTexturespace.x - m_halfSize, centerInTexturespace.y - m_halfSize, ClipSize, ClipSize, m_clipmapTexture[m], 0, 0, 0, 0);
            }
            else
            {
                // load entire mip from disk
                m_clipmapTexture[m] = new Texture2D(clipmapLevelSize, clipmapLevelSize, MipTextureFormat, false, false);
            }
        }
    }

    private void UpdateClipmap()
    {
        // load cached area and load mip from cached area
        Vector2 center = GetCenterInHomogenousSpace();
        for (int m = 0; m < 1; m++, center /= 2)
        {
            if (m_clipmapTexture[m].width < ClipSize) { break; }

            Vector2Int centerInTexturespace = Vector2Int.FloorToInt(center) + Vector2Int.FloorToInt(BaseTexture.Size()) / 2;
            Vector2Int diff = centerInTexturespace - m_clipmapCenter[m];
            Vector2Int newOffset = m_clipmapOffset[m] + diff;

            int updateWidth = Mathf.Abs(diff.x);
            int updateHeight = Mathf.Abs(diff.y);
            // update the whole texture when displacement is too large
            if (updateWidth > ClipSize || updateHeight > ClipSize)
            {
                Graphics.CopyTexture(m_clipmapCache[m], 0, 0, centerInTexturespace.x - m_halfSize, centerInTexturespace.y - m_halfSize, ClipSize, ClipSize, m_clipmapTexture[m], 0, 0, 0, 0);
                m_clipmapOffset[m].Set(0, 0);
                m_clipmapCenter[m] = centerInTexturespace;
            }

            if (Mathf.Abs(diff.x) > MipGridSize || Mathf.Abs(diff.y) > MipGridSize)
            {
                // Graphics.CopyTexture(m_clipmapCache[m], 0, 0, centerInTexturespace.x - m_halfSize, centerInTexturespace.y - m_halfSize, ClipSize, ClipSize, m_clipmapTexture[m], 0, 0, 0, 0);
                
            }

        }

        // actual center = virtual center + offset
    }

    // get the player's position in homogeneous coordinate
    private Vector2 GetCenterInHomogenousSpace()
    {
        Vector2 wCoord = new Vector2(playerPawn.position.x, playerPawn.position.z) - 
                            new Vector2(worldOrigin.position.x, worldOrigin.position.z);

        Vector2 hCoord = wCoord / WorldGridSize;
        return hCoord;
    }

    void recalculateClipCenters()
    {
        // TODO: FILL - force updates the clip centers of each mip level based on the clip center in mip0
        // should update from mip level 0 to higher levels
        // should takes account of invalid borders

    }

    private void OnDrawGizmos()
    {
        // Gizmos.DrawWireCube(new Vector3(0, 1, 0), new Vector3(4, 1, 4));
    }
};
