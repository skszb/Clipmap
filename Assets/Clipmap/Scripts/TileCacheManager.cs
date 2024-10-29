#define SAFETY_CHECK

using System;
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

    private class TileCache
    {
        public Dictionary<Vector2Int, int> cacheLookupTable ;
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
        
        
        public Texture2D GetTexture(Vector2Int tileCoordinates)
        {
            
            if (!cacheLookupTable.ContainsKey(tileCoordinates))
            {
                int slot = GetAvailableSlot();
                if (slot == -1)
                {
                    Debug.LogWarning("not enough space for tile cache");
                    return null;
                }
                cacheLookupTable.Add(tileCoordinates, slot);
                LoadingStatus[slot] = CacheStatus.Missing;
            }

            int index = cacheLookupTable[tileCoordinates];
            CacheStatus status = LoadingStatus[index];
            
            if (status == CacheStatus.Missing)
            {
                LoadingStatus[index] = CacheStatus.Loading;
                string tilePath =  String.Format("{0}/{1}_{2}", m_folderPath, tileCoordinates.x, tileCoordinates.y);
                m_owner.StartCoroutine(LoadTile(index, tilePath));
            }

            if (status == CacheStatus.Ready)
            {
                return TextureTiles[index];
            }

            return null;
        }
        
        
        IEnumerator LoadTile(int index, string path)
        {
            LoadingStatus[index] = CacheStatus.Loading;
            ResourceRequest request = Resources.LoadAsync<Texture2D>(path);
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
        }
    }
    private List<TileCache> m_tileCache;

    private string m_path = "Cache/TextureTileCache";
    


    public void Initialize(MonoBehaviour owner, int[] baseTextureSize, int[] tileSize, string[] folderName)
    {
        int depth = folderName.Length;
        m_tileCache = new List<TileCache>(depth);
        for (var i = 0; i < depth; ++i)
        {
            m_tileCache.Add(new TileCache(owner, tileSize[i], tileSize[i], m_path + "/" + folderName[i]));
        }
    }
    
    // Return the texture tiles that the given region lies within. If a tile isn't cached yet, it will be null
    public List<(Texture2D tile, AABB2Int tileBound, AABB2Int updateRegion)> GetTiles(AABB2Int updateRegion, int depth)
    {
        var ret = new List<(Texture2D tile, AABB2Int tileBound, AABB2Int updateRegion)>();

        TileCache tileCache = m_tileCache[depth];

        // Get the keys to the tiles that contains the given area
        var keys = new List<Vector2Int>();
        int tileSize = tileCache.TileSize;

        var bottomLeft = tileSize * ClipmapUtil.FloorDivision(updateRegion.min, tileSize);
        for (var key = bottomLeft; key.x < updateRegion.max.x; key.x += tileSize)
        {
            for (key.y = 0; key.y < updateRegion.max.y; key.y += tileSize)
            {
                keys.Add(key);
            }
        }

        var bounds = new List<AABB2Int>();
        foreach (var key in keys)
        {
            Texture2D tile = tileCache.GetTexture(key);
            
            AABB2Int tileBound = new AABB2Int(key.x, key.y, key.x + tileSize, key.y + tileSize);
            ret.Add((tile, tileBound, updateRegion.Clamp(tileBound)));
        }

        return ret;
    }
    
}