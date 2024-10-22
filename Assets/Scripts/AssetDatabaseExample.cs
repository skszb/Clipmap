// This script file has two CS classes.  The first is a simple Unity ScriptableObject script.
// The class it defines is used by the Example class below.
// (This is a single Unity script file. You could split this file into a ScriptObj.cs and an
// Example.cs file which is more structured.)

using UnityEngine;
using UnityEditor;
using System.IO;
using Unity.Collections;
using System;

[Serializable]
public class ScriptObj : ScriptableObject
{
    public NativeArray<byte> data;
    public void Awake()
    {
        Debug.Log("ScriptObj created");
    }
}

// Use ScriptObj to show how AssetDabase.FindAssets can be used

public class Example
{
    static ScriptObj testI;
    static ScriptObj testJ;
    static ScriptObj testK;

    [MenuItem("Examples/FindAssets Example two")]
    static void ExampleScript()
    {
        CreateAssets();
        NamesExample();
        LabelsExample();
        TypesExample();
    }

    static void CreateAssets()
    {
        if (!Directory.Exists("Assets/AssetFolder"))
        {
            AssetDatabase.CreateFolder("Assets", "AssetFolder");
        }

        if (!Directory.Exists("Assets/AssetFolder/SpecialFolder"))
        {
            AssetDatabase.CreateFolder("Assets/AssetFolder", "SpecialFolder");
        }

        testI = (ScriptObj)ScriptableObject.CreateInstance(typeof(ScriptObj));
        AssetDatabase.CreateAsset(testI, "Assets/AssetFolder/testI.asset");

        testJ = (ScriptObj)ScriptableObject.CreateInstance(typeof(ScriptObj));
        AssetDatabase.CreateAsset(testJ, "Assets/AssetFolder/testJ.asset");

        // create an asset in a sub-folder and with a name which contains a space
        testK = (ScriptObj)ScriptableObject.CreateInstance(typeof(ScriptObj));
        AssetDatabase.CreateAsset(testK, "Assets/AssetFolder/SpecialFolder/testK example.asset");

        // an asset with a material will be used later
        Material material = new Material(Shader.Find("Standard"));
        AssetDatabase.CreateAsset(material, "Assets/AssetFolder/SpecialFolder/MyMaterial.mat");
    }

    static void NamesExample()
    {
        Debug.Log("*** FINDING ASSETS BY NAME ***");

        string[] results;

        results = AssetDatabase.FindAssets("testI");
        foreach (string guid in results)
        {
            Debug.Log("testI: " + AssetDatabase.GUIDToAssetPath(guid));
        }

        results = AssetDatabase.FindAssets("testJ");
        foreach (string guid in results)
        {
            Debug.Log("testJ: " + AssetDatabase.GUIDToAssetPath(guid));
        }

        results = AssetDatabase.FindAssets("testK example");
        foreach (string guid in results)
        {
            Debug.Log("testK example: " + AssetDatabase.GUIDToAssetPath(guid));
        }

        Debug.Log("*** More complex asset search ***");

        // find all assets that contain test (which is all assets)
        results = AssetDatabase.FindAssets("test");
        foreach (string guid in results)
        {
            Debug.Log("name:test - " + AssetDatabase.GUIDToAssetPath(guid));
        }
    }

    static void LabelsExample()
    {
        Debug.Log("*** FINDING ASSETS BY LABELS ***");

        string[] setLabels;

        setLabels = new string[] { "wrapper" };
        AssetDatabase.SetLabels(testI, setLabels);

        setLabels = new string[] { "bottle", "banana", "carrot" };
        AssetDatabase.SetLabels(testJ, setLabels);

        setLabels = new string[] { "swappable", "helmet" };
        AssetDatabase.SetLabels(testK, setLabels);

        // label searching:
        //   testI has wrapper, testK has swappable, so both have 'app'
        //   testJ has bottle, so have a label searched as 'bot'
        string[] getGuids = AssetDatabase.FindAssets("l:app l:bot");
        foreach (string guid in getGuids)
        {
            Debug.Log("label lookup: " + AssetDatabase.GUIDToAssetPath(guid));
        }
    }

    static void TypesExample()
    {
        Debug.Log("*** FINDING ASSETS BY TYPE ***");

        string[] guids;

        guids = AssetDatabase.FindAssets("t:material");
        foreach (string guid in guids)
        {
            Debug.Log("Material: " + AssetDatabase.GUIDToAssetPath(guid));
        }

        guids = AssetDatabase.FindAssets("t:Object l:helmet");
        foreach (string guid in guids)
        {
            Debug.Log("ScriptObj+helmet: " + AssetDatabase.GUIDToAssetPath(guid));
        }
    }
}