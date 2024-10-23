using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.Profiling.Memory.Experimental;
using UnityEngine;

[CreateAssetMenu(fileName = "TextureTileSlicer", menuName = "ScriptableObjects/TextureTile/TextureTileSlicer")]
public class TextureTileSlicer : ScriptableObject
{
    public int tileSize = 64;
    public Texture2D[] textures;

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

    unsafe void GenerateTileData()
    {
       
        for (int textureID = 0; textureID < textures.Length; ++textureID)
        {
            Texture2D currentTexture = textures[textureID];
            if (currentTexture == null)
            {
                continue;
            }

            int vertexSize = GetVertexSizeInBytes(currentTexture);
            if (vertexSize == -1)
            {
                Debug.Log(string.Format("Failed to slice texture{0}.", textureID));
                continue;
            }
            int tileBufferSize = tileSize * tileSize * vertexSize;

            // Acquire base texture data for processing
            NativeArray<byte> texData = currentTexture.GetPixelData<byte>(0);

            // Create a folder for caching the current texture tiles
            AssetDatabase.CreateFolder(folderPath, textureID.ToString());
            string currentFolderPath = Path.Combine(folderPath, textureID.ToString());

            // Get tile data and save in the folder
            int textureSize = textures[textureID].width;
            int tileCount = textureSize / tileSize;
            NativeArray<byte> intermediateBuffer = new NativeArray<byte>(tileBufferSize, Allocator.Temp);
            for (int u = 0; u < tileCount; ++u)
            {
                for (int v = 0; v < tileCount; ++v)
                {
                    int sourceOffset = (u * textureSize + v) * tileSize * vertexSize;
                    UnsafeUtility.MemCpyStride(intermediateBuffer.GetUnsafePtr(), 
                                                tileSize * vertexSize, 
                                                (byte*)texData.GetUnsafePtr() + sourceOffset, 
                                                textureSize * vertexSize, 
                                                tileSize * vertexSize, 
                                                tileSize);
                    
                    TextureTile tile = new TextureTile();
                    tile.dimension = new int[2] { tileSize, tileSize };
                    tile.rawData = new byte[tileBufferSize];
                    intermediateBuffer.CopyTo(tile.rawData);

                    string assetPath = Path.Combine(currentFolderPath, String.Format("{0}_{1}.asset", u, v));
                    AssetDatabase.CreateAsset(tile, assetPath);
                }
            }
        }
        Debug.Log("Created Sliced Texture Tiles");
    }

    public void RegenerateTileData()
    {
        ClearTileData();
        GenerateTileData();
    }

    // return the size in bytes of a pixel in the given texture
    int GetVertexSizeInBytes(Texture2D tex)
    {
        int vertexSize = -1;
        switch (tex.format) 
        {
            case TextureFormat.RGBA32:
                vertexSize = 4; 
                break;
            default:
                Debug.Log("Fail to slice texture: format not yet supported.");
                vertexSize = - 1;
                break;
        }
        return vertexSize;
    }
}
