using System.Collections;
using System.Collections.Generic;

using UnityEngine;

public class TileCacheManager
{
    private enum CacheStatus
    {
        Missing,
        Loading,
        Ready,
        Failed,
    }

    private List<TileCache> m_tileCache;

    private string m_path = "Cache/TextureTileCache";
    
    public void Initialize(MonoBehaviour owner, int[] baseTextureSize, int[] tileSize, string[] folderName)
    {
        int depth = folderName.Length;
        m_tileCache = new List<TileCache>(depth);
        for (var i = 0; i < depth; ++i)
        {
            m_tileCache.Add(new TileCache(owner, baseTextureSize[i], tileSize[i], m_path + "/" + folderName[i]));
        }
    }
    
    // Return the texture tiles that the given region lies within. If a tile isn't cached yet, it will be null
    public List<(Texture2D textureTile, AABB2Int tileBound, AABB2Int croppedUpdateRegion)> GetTiles(AABB2Int updateRegion, int depth)
    {
        var ret = new List<(Texture2D textureTile, AABB2Int tileBound, AABB2Int croppedUpdateRegion)>();
        TileCache tileCache = m_tileCache[depth];
        int halfTextureSize = tileCache.BaseTextureSize / 2;
        // convert to texel space
        updateRegion += halfTextureSize;
        
        
        // Get the coordinates to the tiles that contains the given area
        int tileSize = tileCache.TileSize;
        var bottomLeft = ClipmapUtil.SnapToGrid(updateRegion.min, tileSize);

        for (var coord = bottomLeft; coord.x < updateRegion.max.x; coord.x += tileSize)
        {
            for (coord.y = 0; coord.y < updateRegion.max.y; coord.y += tileSize)
            {
                Texture2D tile = tileCache.TryAcquireTile(coord);
                AABB2Int tileBound = new AABB2Int(coord.x, coord.y, coord.x + tileSize, coord.y + tileSize);
                AABB2Int croppedUpdateRegion = updateRegion.ClampBy(tileBound);
                if (!croppedUpdateRegion.IsValid()) { continue; }
                
                // convert back to mip space of [-halfTextureSize, +halfTextureSize) for easier addressing in clipmap update
                ret.Add((tile, tileBound - halfTextureSize, croppedUpdateRegion - halfTextureSize));
            }
        }
        return ret;
    }

    public void LoadTiles(AABB2Int updateRegion, int depth)
    {
        TileCache tileCache = m_tileCache[depth];
        int tileSize = tileCache.TileSize;
        int textureSize = tileCache.BaseTextureSize;

        // convert to texel space
        updateRegion += textureSize / 2;
        updateRegion.ClampBy(new AABB2Int(0, 0, textureSize, textureSize));
        
        var bottomLeft = ClipmapUtil.SnapToGrid(updateRegion.min, tileSize);

        for (var coord = bottomLeft; coord.x < updateRegion.max.x; coord.x += tileSize)
        {
            for (coord.y = 0; coord.y < updateRegion.max.y; coord.y += tileSize)
            {
                tileCache.LoadTile(coord, -depth);
            }
        }
    }
    
    
    private class TileCache
    {
        public Dictionary<Vector2Int, int> cacheLookupTable;
        public List<CacheStatus> LoadingStatus;
        public List<Texture2D> TextureTiles;

        public int BaseTextureSize;
        public int TileSize;
        public Vector2Int Center;

        private int m_availableIndex = 0;
        private MonoBehaviour m_owner;
        private string m_folderPath;


        public TileCache(MonoBehaviour owner, int textureSize, int tileSize, string folderPath)
        {
            this.m_owner = owner;
            this.BaseTextureSize = textureSize;
            this.TileSize = tileSize;
            this.m_folderPath = folderPath;

            cacheLookupTable = new Dictionary<Vector2Int, int>();
            LoadingStatus = new List<CacheStatus>();
            TextureTiles = new List<Texture2D>();
        }


        private int GetAvailableSlot()
        {
            LoadingStatus.Add(CacheStatus.Missing);
            TextureTiles.Add(null);
            return m_availableIndex++;
        }


        public void Update(Vector2Int position)
        {
            // TODO: update with player
        }


        public Texture2D TryAcquireTile(Vector2Int tileCoordinates)
        {
            if (cacheLookupTable.TryGetValue(tileCoordinates, out int index))
            {
                if (LoadingStatus[index] == CacheStatus.Ready)
                {
                    return TextureTiles[index];
                }
            }
            return null;
        }


        public void LoadTile(Vector2Int tileCoordinates, int priority=0)
        {
            if (!cacheLookupTable.ContainsKey(tileCoordinates))
            {
                int slot = GetAvailableSlot();
                if (slot == -1)
                {
                    return;
                }

                cacheLookupTable.Add(tileCoordinates, slot);
                LoadingStatus[slot] = CacheStatus.Missing;
            }

            int index = cacheLookupTable[tileCoordinates];
            CacheStatus status = LoadingStatus[index];
            switch (status)
            {
                case CacheStatus.Ready:
                case CacheStatus.Loading:
                    return;
                case CacheStatus.Missing:
                {
                    LoadingStatus[index] = CacheStatus.Loading;
                    string tilePath = $"{m_folderPath}/{tileCoordinates.x}_{tileCoordinates.y}";
                    m_owner.StartCoroutine(LoadTileAsync(index, tilePath, priority));
                    Debug.Log($"Loading tile {tileCoordinates}, priority: {priority}");
                    break;
                }
                case CacheStatus.Failed:
                    Debug.LogWarningFormat("Tile cache {0} loading failed", tileCoordinates.ToString());
                    break;
                default:
                    break;
            }
        }
        
        private IEnumerator LoadTileAsync(int index, string path, int priority = 0)
        {
            LoadingStatus[index] = CacheStatus.Loading;
            ResourceRequest request = Resources.LoadAsync<Texture2D>(path);
            request.priority = priority;
            yield return request;
            
            var tex = request.asset as Texture2D;
            if (tex is not null)
            {
                TextureTiles[index] = tex;
                LoadingStatus[index] = CacheStatus.Ready;
            }
            else
            {
                LoadingStatus[index] = CacheStatus.Failed;
            }
            
            // yield break to stop
        }
    }
}