using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

[Serializable]
class TextureTileData : ScriptableObject
{
    public NativeArray<byte> data;

}