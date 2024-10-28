using UnityEngine;

[CreateAssetMenu(fileName = "TextureTileData", menuName = "ScriptableObjects/TextureTile/TextureTile")]
internal class TextureTile : ScriptableObject
{
    public byte[] rawData;
}