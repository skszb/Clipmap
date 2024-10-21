using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEditor.Profiling.Memory.Experimental;
using UnityEngine;

public class TextureTileManager : MonoBehaviour
{
    public int tileSize;
    public int count = 0;
    public string srcPath = "Textures/Mips/BMP";
    public string dstPath = "Textures/Mips/TiledMips";
    public string fileName = "mip";

    private static uint textureID = 0;
    // Start is called before the first frame update
    void Start()
    {
        string texPath = String.Format("Textures/Mips/BMP/{0}{1}", fileName, 5);
        Texture2D tex = (Texture2D)Resources.Load(texPath);
        NativeArray<byte> texData = tex.GetPixelData<byte>(0);

        // Use AssetDatabase! 
        FileStream fWrite = new FileStream(String.Format("{0}/Assets/TileCache{1}_{2}.bytes", Application.persistentDataPath, 0, 0), FileMode.Create);
        // FileStream fWrite = new FileStream(String.Format("{0}/{1}_{2}.bytes", dstPath, 0, 0), FileMode.Create);
        fWrite.Write(texData.AsReadOnlySpan());
        fWrite.Close();

        // File.WriteAllBytes(String.Format("{0}/{1}_{2}.bytes", dstPath, 0, 0), texData.ToArray());
    }

    public void LoadTextureFromImage()
    {

    }

    public void LoadTextureFromBinaryAssets()
    {

    }

    // Update is called once per frame
    void Update()
    {
        
    }


}
