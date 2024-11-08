using System.IO;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEditor;
using UnityEngine;

[CreateAssetMenu(fileName = "TextureTileSlicer", menuName = "ScriptableObjects/TextureTile/TextureTileSlicer")]
public class TextureTileSlicer : ScriptableObject
{
    private const string folderName = "TextureTileCache";
    private const string folderFullPath = "Assets/Resources/Cache/" + folderName;
    public int tileSize = 64;   // TODO: to variable tile size for each depth
    public Texture2D[] textures;

    private void ClearTileData()
    {
        if (Directory.Exists(folderFullPath)) AssetDatabase.DeleteAsset(folderFullPath);
        AssetDatabase.CreateFolder("Assets/Resources/Cache", folderName);
    }

    private unsafe void GenerateTileData()
    {
        for (var textureID = 0; textureID < textures.Length; ++textureID)
        {
            var currentTexture = textures[textureID];
            if (currentTexture is null) continue;

            var vertexSize = GetVertexSizeInBytes(currentTexture);
            if (vertexSize == -1)
            {
                Debug.LogWarning(string.Format("Failed to slice texture{0}.", textureID));
                continue;
            }

            var tileBufferSize = tileSize * tileSize * vertexSize;

            // Acquire base texture data for processing
            var texData = currentTexture.GetPixelData<byte>(0);

            // Create a folder for caching the current texture tiles
            AssetDatabase.CreateFolder(folderFullPath, textureID.ToString());
            var currentFolderPath = Path.Combine(folderFullPath, textureID.ToString());

            // Get tile data and save in the folder
            int textureSize = textures[textureID].width;
            int tileCount = textureSize / tileSize;
            var intermediateBuffer = new NativeArray<byte>(tileBufferSize, Allocator.Temp);
            for (int u = 0; u < textureSize; u += tileSize)
            {
                for (int v = 0; v < textureSize; v += tileSize)
                {
                    var sourceOffset = (v * textureSize + u) * vertexSize;
                    UnsafeUtility.MemCpyStride(intermediateBuffer.GetUnsafePtr(),
                        tileSize * vertexSize,
                        (byte*)texData.GetUnsafePtr() + sourceOffset,
                        textureSize * vertexSize,
                        tileSize * vertexSize,
                        tileSize);

                    var tex = new Texture2D(tileSize, tileSize, TextureFormat.RGBA32, false);
                    tex.SetPixelData(intermediateBuffer, 0);
                    var assetPath = Path.Combine(currentFolderPath, $"{u}_{v}.asset");
                    AssetDatabase.CreateAsset(tex, assetPath);
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
    private int GetVertexSizeInBytes(Texture2D tex)
    {
        var vertexSize = -1;
        switch (tex.format)
        {
            case TextureFormat.RGBA32:
                vertexSize = 4;
                break;
            default:
                Debug.LogWarning("Fail to slice texture: format not yet supported.");
                vertexSize = -1;
                break;
        }

        return vertexSize;
    }
}