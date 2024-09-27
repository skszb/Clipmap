using System.IO;
using Unity.Collections;
using Unity.IO.LowLevel.Unsafe;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using System;

class AsyncReadSample : MonoBehaviour
{
    private ReadHandle readHandle;
    NativeArray<ReadCommand> cmds;
    // VVVVVVVVVVVVVVV Optional, for profiling VVVVVVVVVVVVVVV
    string assetName = "myfile";
    ulong typeID = 114; // from ClassIDReference
    AssetLoadingSubsystem subsystem = AssetLoadingSubsystem.Scripts;
    // ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^

    Texture2D texture;
    public Renderer rd;

    public unsafe void Start()
    {
        string filePath = Path.Combine(Application.streamingAssetsPath, "StreamingMips/mip8.bmp");
        cmds = new NativeArray<ReadCommand>(1, Allocator.TempJob);
        ReadCommand cmd;
        cmd.Offset = 0;
        cmd.Size = 1024;
        cmd.Buffer = (byte*)UnsafeUtility.Malloc(cmd.Size, 16, Allocator.Persistent);
        cmds[0] = cmd;
        readHandle = AsyncReadManager.Read(filePath, (ReadCommand*)cmds.GetUnsafePtr(), 1, assetName, typeID, subsystem);

        texture = new Texture2D(1, 1);
        int mainTexId = Shader.PropertyToID("_MipTexture");
        rd.material.SetTexture(mainTexId, texture);
    }

    public unsafe void Update()
    {
        // copy to texture
        if (readHandle.IsValid() && readHandle.Status != ReadStatus.InProgress)
        {
            Debug.LogFormat("Read {0}", readHandle.Status == ReadStatus.Complete ? "Successful" : "Failed");
            readHandle.Dispose();

            NativeArray<byte> data = new NativeArray<byte>(16 * 16, Allocator.Persistent);
            int size = data.Length;
            UnsafeUtility.MemCpy(data.GetUnsafePtr(), cmds[0].Buffer, size);
            texture.LoadRawTextureData(data);
            texture.Apply();
            // UnsafeUtility.MemCpy(cmds[0].Buffer, data, cmds[0].Size)
            UnsafeUtility.Free(cmds[0].Buffer, Allocator.Persistent);
            data.Dispose();
            cmds.Dispose();
        }
    }
}