#define SAFETY_CHECK

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TileCacheManager
{
    enum CacheStatus
    {
        Missing,
        Loading,
        Ready,
    }
    
    class TileCache
    {
        public Dictionary<Vector2Int, (CacheStatus status, int location)> cacheLookupTable;
        public List<Texture2D> TextureTiles;

        public int TileSize;
        public int TextureSize;
        public Vector2Int Center;

        void LoadTile(Vector2Int key)
        {
            
        }
        
        
    }
    private List<TileCache> m_tileCache;

    private string m_path = "Assets/Resources/Cache/TextureTileCache";
    
    
    List<(Texture2D tile, AABB2Int tileBound)> GetTiles(AABB2Int area, int depth)
    {
        var ret = new List<(Texture2D tile, AABB2Int tileBound)>();
        
        TileCache tileCache = m_tileCache[depth];
        
        // Get the keys to the tiles that contains the given area
        List<Vector2Int> keys = new List<Vector2Int>();
        int tileSize = tileCache.TileSize;
        
        Vector2Int bottomLeft = tileSize * ClipmapUtil.FloorDivision(area.min, tileSize);
        for (Vector2Int key = bottomLeft; key.x < area.max.x; key.x += tileSize)
        {
            for (key.y = 0; key.y < area.max.y; key.y += tileSize)
            {
                keys.Add(key);
            }
        }
        
        List<AABB2Int> bounds = new List<AABB2Int>();
        foreach (var key in keys)
        {
            if (!tileCache.cacheLookupTable.ContainsKey(key))
            {
                tileCache.cacheLookupTable.Add(key, (CacheStatus.Missing, 0));
                
            }
            if (tileCache.cacheLookupTable[key].status == CacheStatus.Missing)
            {
                // TODO: read from disk
            }
            else if (tileCache.cacheLookupTable[key].status == CacheStatus.Ready)
            {
                Texture2D tile = tileCache.TextureTiles[tileCache.cacheLookupTable[key].location];
                AABB2Int tileBound = new AABB2Int(key.x, key.y, key.x + tileSize, key.y + tileSize);
                ret.Add((tile, tileBound));
            }
        }
        
        return ret;
    }

    
}