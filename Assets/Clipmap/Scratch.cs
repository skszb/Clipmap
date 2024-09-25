using System.IO;
using Unity.Collections;
using Unity.IO.LowLevel.Unsafe;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

class AsyncReadSample : MonoBehaviour
{
    private ReadHandle readHandle;
    NativeArray<ReadCommand> cmds;
    // VVVVVVVVVVVVVVV Optional, for profiling VVVVVVVVVVVVVVV
    string assetName = "myfile";
    ulong typeID = 114; // from ClassIDReference
    AssetLoadingSubsystem subsystem = AssetLoadingSubsystem.Scripts;
    // ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
    public unsafe void Start()
    {
        string filePath = Path.Combine(Application.streamingAssetsPath, "myfile.bin");
        cmds = new NativeArray<ReadCommand>(1, Allocator.Persistent);
        ReadCommand cmd;
        cmd.Offset = 0;
        cmd.Size = 1024;
        cmd.Buffer = (byte*)UnsafeUtility.Malloc(cmd.Size, 16, Allocator.Persistent);
        cmds[0] = cmd;
        readHandle = AsyncReadManager.Read(filePath, (ReadCommand*)cmds.GetUnsafePtr(), 1, assetName, typeID, subsystem);
    }

    public unsafe void Update()
    {
        if (readHandle.IsValid() && readHandle.Status != ReadStatus.InProgress)
        {
            Debug.LogFormat("Read {0}", readHandle.Status == ReadStatus.Complete ? "Successful" : "Failed");
            readHandle.Dispose();
            UnsafeUtility.Free(cmds[0].Buffer, Allocator.Persistent);
            cmds.Dispose();
        }
    }
}