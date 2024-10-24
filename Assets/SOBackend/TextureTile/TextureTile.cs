using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

[CreateAssetMenu(fileName = "TextureTileData", menuName = "ScriptableObjects/TextureTile/TextureTile")]
class TextureTile : ScriptableObject
{
    public byte[] rawData;
}
