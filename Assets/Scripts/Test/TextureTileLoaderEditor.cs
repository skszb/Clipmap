using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(TextureTileLoader))]
public class TextureTileLoaderEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        var script = (TextureTileLoader)target;

        if (GUILayout.Button("Load Tile Data", GUILayout.Height(40))) script.LoadTextures();
    }
}