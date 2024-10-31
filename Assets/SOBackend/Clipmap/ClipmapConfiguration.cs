using System;
using UnityEngine;

[CreateAssetMenu(fileName = "ClipmapConfiguration", menuName = "ScriptableObjects/Clipmap/ClipmapConfiguration")]
public class ClipmapConfiguration : ScriptableObject
{
    [Header("Clipmap Descriptor")] [Space(5)]
    public int ClipSize;
    public int InvalidBorder;
    public int ClipmapUpdateGridSize;

    public TextureFormat TextureFormat;

    public Texture2D[] BaseTexture;

    [Space(10)] [Header("Tile Cache Descriptor")] [Space(5)]

    public string[] folderName;
    public int[] baseTextureSize;
    public int[] tileSize;
    public int[] capacity;
}