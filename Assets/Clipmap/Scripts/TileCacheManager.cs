using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.WSA;

public class TileCacheManager : MonoBehaviour
{
    private Dictionary<Vector2Int, int> m_lookupTable;
    private List<Texture2D> m_textureTiles;

    private int m_tileSize;
    private int m_textureSize;
    private Vector2Int m_center;

    private string path = "Assets/Cache/TextureTileCache";
}
