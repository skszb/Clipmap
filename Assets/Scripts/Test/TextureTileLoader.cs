using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CreateAssetMenu(fileName = "TextureTileLoader", menuName = "ScriptableObjects/TextureTile/TextureTileLoader")]
public class TextureTileLoader : ScriptableObject
{

    public Texture2D[] textures;

    public bool load = false;

    public void LoadTextures()
    {
        
        const int SIZE = 4;
        textures = new Texture2D[SIZE];

        for (int i = 0; i < SIZE / 2; i++)
        {
            for (int j = 0; j < SIZE / 2; j++)
            {
                textures[i*2+j] = new Texture2D(128, 128);
                //AssetDatabase.CreateAsset(textures[i * 2 + j], string.Format("Assets/TextureTileCache/Tmp{0}_{1}.asset", i, j));
                TextureTile tileData = AssetDatabase.LoadAssetAtPath<TextureTile>(string.Format("Assets/Resources/Cache/TextureTileCache/0/{0}_{1}.asset", i, j));
                textures[i*2+j].SetPixelData<byte>(tileData.rawData, 0);
                textures[i*2+j].Apply();
            }
        }
    }
}
