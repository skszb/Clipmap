using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.PlayerLoop;
using UnityEngine.Serialization;

public class MonoScratch : MonoBehaviour
{
    
    private void Start()
    {
        tileCacheManager = new TileCacheManager();
        tileCacheManager.Initialize(this, 128, 2);
    }

    public TileCacheManager tileCacheManager;

    public bool load = false;

    public int depth;
    public Vector2Int bottomLeft;
    public Vector2Int topRight;

    public List<Texture2D> LoadedTextures;

    void Update()
    {
        if (!load) { return;}

        load = false;
        var result = tileCacheManager.GetTiles(new AABB2Int(bottomLeft, topRight), depth);
        LoadedTextures = new List<Texture2D>(result.Count);
        foreach (var tile in result)
        {
            LoadedTextures.Add(tile.tile);
        }
    }
}