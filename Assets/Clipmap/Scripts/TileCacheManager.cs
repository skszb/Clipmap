#define SAFETY_CHECK

using System.Collections.Generic;
using UnityEngine;

public class TileCacheManager
{
    private enum CacheStatus
    {
        Missing,
        Loading,
        Ready
    }

    private class TileCache
    {
        public Dictionary<Vector2Int, (CacheStatus status, int location)> cacheLookupTable;
        public Vector2Int Center;
        public int TextureSize;
        public List<Texture2D> TextureTiles;

        public int TileSize;

        public Texture2D LoadTile(Vector2Int key)
        {
            if (!cacheLookupTable.ContainsKey(key))
            {
                cacheLookupTable.Add(key, (CacheStatus.Missing, 0));
                return null;
            }
            
            switch (cacheLookupTable[key].status)
            {
                case CacheStatus.Ready:
                    return TextureTiles[cacheLookupTable[key].location];
                case CacheStatus.Loading:
                    // TODO: Check read status
                case CacheStatus.Missing:
                    // TODO: read from disk
                default:
                    return null;
            }
        }
    }

    private List<TileCache> m_tileCache;

    private string m_path = "Assets/Resources/Cache/TextureTileCache";


    List<(Texture2D tile, AABB2Int tileBound, AABB2Int updateRegion)> GetTiles(AABB2Int updateRegion, int depth)
    {
        var ret = new List<(Texture2D tile, AABB2Int tileBound, AABB2Int updateRegion)>();

        TileCache tileCache = m_tileCache[depth];

        // Get the keys to the tiles that contains the given area
        var keys = new List<Vector2Int>();
        int tileSize = tileCache.TileSize;

        var bottomLeft = tileSize * ClipmapUtil.FloorDivision(updateRegion.min, tileSize);
        for (var key = bottomLeft; key.x < updateRegion.max.x; key.x += tileSize)
        for (key.y = 0; key.y < updateRegion.max.y; key.y += tileSize)
            keys.Add(key);

        var bounds = new List<AABB2Int>();
        foreach (var key in keys)
        {
            Texture2D textureTile = tileCache.LoadTile(key);
            if (textureTile is null)
            {
                continue;
            }

            Texture2D tile = tileCache.TextureTiles[tileCache.cacheLookupTable[key].location];
            AABB2Int tileBound = new AABB2Int(key.x, key.y, key.x + tileSize, key.y + tileSize);
            ret.Add((tile, tileBound, updateRegion.Clamp(tileBound)));
        }

        return ret;
    }
}