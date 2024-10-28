using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(TextureTileSlicer))]
public class TextureTileSlicerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        var script = (TextureTileSlicer)target;

        if (GUILayout.Button("Regenerate Tile Data", GUILayout.Height(40)))
        {
            script.RegenerateTileData();
        }
    }
}