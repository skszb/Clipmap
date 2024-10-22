using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.VisualScripting;
using UnityEditor;
using UnityEditor.Profiling.Memory.Experimental;
using UnityEngine;

[ExecuteAlways]
public class TextureTileSlicer : MonoBehaviour
{
    public int tileSize;
    
    public Texture2D[] textures;

    public bool regenerateTiles = false;

    private const string folderName = "TextureTileCache";
    private const string folderPath =  "Assets/" + folderName;

    void ClearTileData()
    {
        
        if (Directory.Exists(folderPath))
        {
            AssetDatabase.DeleteAsset(folderPath);
        }
        AssetDatabase.CreateFolder("Assets", folderName);
    }

    void GenerateTileData()
    {
        for (int index = 0; index < textures.Length; ++index)
        {
            AssetDatabase.CreateFolder(folderPath, index.ToString());
            string currentFolderPath = Path.Combine(folderPath, index.ToString());

            NativeArray<byte> textureData = textures[index].GetPixelData<byte>(0);

            TextureTileData textureTileData = ScriptableObject.CreateInstance<TextureTileData>();
            textureTileData.data = textureData;

            string assetPath = Path.Combine(currentFolderPath, "_{0}_{1}.asset");
            AssetDatabase.CreateAsset(textureTileData, assetPath);



            //int textureSize = (int)textures[index].texelSize.x;
            //for (int i = 0; i < textureSize; i += tileSize)
            //{
            //    TextureTileData tile = new TextureTileData();
            //}

            //string currentFolderPath = Path.Combine(folderPath, index.ToString());
            //string assetPath = Path.Combine(currentFolderPath, "_{0}_{1}.asset");
            //TextureTileData textureTileData = AssetDatabase.LoadAssetAtPath<TextureTileData>(assetPath);
        }
        Debug.Log("Created Sliced Texture Tiles");
    }

    // Update is called once per frame
    void Update()
    {
        if (regenerateTiles)
        {
            regenerateTiles = false;
            ClearTileData();
            GenerateTileData();

        }
    }
}
