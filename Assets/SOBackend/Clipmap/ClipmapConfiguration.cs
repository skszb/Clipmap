using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "ClipmapConfiguration", menuName = "ScriptableObjects/Clipmap/ClipmapConfiguration")]
public class ClipmapConfiguration : ScriptableObject
{
    public int WorldScale;
    public int ClipSize;
    public int InvalidBorder;
    public int ClipmapUpdateGridSize;

    public TextureFormat TextureFormat;

    public Texture2D[] BaseTexture;
}
