using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;


[CustomEditor(typeof(TextureTileLoader))]
public class TextureTileLoaderEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        TextureTileLoader script = (TextureTileLoader)target;

        if (GUILayout.Button("Load Tile Data", GUILayout.Height(40)))
        {
            script.LoadTextures();
        }
    }
}