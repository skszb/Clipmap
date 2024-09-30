using System.IO;
using Unity.Collections;
using Unity.IO.LowLevel.Unsafe;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using System;
using System.Drawing;

class AsyncReadSample : MonoBehaviour
{
    private ReadHandle readHandle;
    ReadCommand cmd;
    // VVVVVVVVVVVVVVV Optional, for profiling VVVVVVVVVVVVVVV
    string assetName = "myfile";
    ulong typeID = 114; // from ClassIDReference
    AssetLoadingSubsystem subsystem = AssetLoadingSubsystem.Scripts;
    // ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^

    Texture2D texture;
    public Renderer rd;

    public unsafe void Start()
    {
        texture = new Texture2D(16, 16);

        cmd = new ReadCommand();

        string filePath = Path.Combine(Application.streamingAssetsPath, "StreamingMips/mip8.bmp");
        cmd.Offset = 0;
        cmd.Size = 1024;
        cmd.Buffer = (byte*)UnsafeUtility.Malloc(cmd.Size, 16, Allocator.Persistent);
        fixed (ReadCommand* cmdAddr = &cmd) 
        {
            readHandle = AsyncReadManager.Read(filePath, cmdAddr, 1, assetName, typeID, subsystem);
        }
        int mainTexId = Shader.PropertyToID("_MipTexture");
        rd.material.SetTexture(mainTexId, texture);
    }

    int startAddr = 0;
    public unsafe void Update()
    {
        // copy to texture
        if (readHandle.IsValid() && readHandle.Status != ReadStatus.InProgress)
        {
            Debug.LogFormat("Read {0}", readHandle.Status == ReadStatus.Failed ? "Failed" : "Success");
            var dataSize = readHandle.GetBytesRead();
            readHandle.Dispose();

            var textureData = texture.GetPixelData<byte>(0);
            UnsafeUtility.MemCpy(textureData.GetUnsafePtr(), cmd.Buffer, dataSize);
            // texture.LoadRawTextureData(textureData);
            // texture.Apply();
            UnsafeUtility.Free(cmd.Buffer, Allocator.Persistent);
        }
    }
}

// UnsafeUtility.MemCpyStride
// https://docs.unity3d.com/ScriptReference/Unity.Collections.LowLevel.Unsafe.UnsafeUtility.MemCpyStride.html