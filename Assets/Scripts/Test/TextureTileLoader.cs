using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[ExecuteAlways]
public class TextureTileLoader : MonoBehaviour
{

    public Texture2D[] textures;

    public bool load = false;

    void LoadTextures()
    {
        const int SIZE = 4;
        textures = new Texture2D[SIZE];

        for (int i = 0; i < SIZE / 2; i++)
        {
            for (int j = 0; j < SIZE / 2; j++)
            {
                textures[i*2+j] = new Texture2D(128, 128);
                TileData tileData = AssetDatabase.LoadAssetAtPath<TileData>(string.Format("Assets/TextureTileCache/0/{0}_{1}.asset", i, j));
                textures[i*2+j].SetPixelData<byte>(tileData.rawData, 0);
                textures[i*2+j].Apply();
            }
        }
    }

    // Update is called once per frame
    void Update()
    {
        if (load)
        {
            load = false;
            LoadTextures(); 
        }
    }
}
