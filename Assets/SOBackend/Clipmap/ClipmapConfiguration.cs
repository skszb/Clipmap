using System;
using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "ClipmapConfiguration", menuName = "ScriptableObjects/Clipmap/ClipmapConfiguration")]
public class ClipmapConfiguration : ScriptableObject
{
    [Header("Clipmap Descriptor")] [Space(5)]
    public int ClipSize;
    public int InvalidBorder;
    public int ClipmapUpdateGridSize;

    public TextureFormat TextureFormat;

    public Texture2D ClipmapPyramidTexture;

     [Space(10)] [Header("Tile Cache Descriptor")] [Space(5)]
    
    public string[] TextureTilePath;
    public int[] baseTextureSize;
    public int tileSize;
    
}