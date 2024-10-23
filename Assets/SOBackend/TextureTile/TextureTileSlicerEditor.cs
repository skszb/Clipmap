using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;


[CustomEditor(typeof(TextureTileSlicer))]
public class TextureTileSlicerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        TextureTileSlicer script = (TextureTileSlicer)target;

        if (GUILayout.Button("Regenerate Tile Data", GUILayout.Height(40)))
        {
            script.RegenerateTileData();
        }
    }
}
