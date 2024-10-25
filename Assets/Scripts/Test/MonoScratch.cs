using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.IO.LowLevel.Unsafe;
using UnityEditor;
using UnityEngine;

public class MonoScratch : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        string texPath = "Assets/Cache/TestTex.asset";
        // Texture2D tex = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        // AssetDatabase.CreateAsset(tex, );
        
        var rr = Resources.LoadAsync<Texture2D>(texPath);
        if (rr.isDone)
        {
            var tex = rr.asset;
        }
        StartCoroutine(LoadTexture(texPath));
    }

    
    IEnumerator LoadTexture(string path)
    {
        ResourceRequest request = Resources.LoadAsync<TextureTile>("Assets/Resources/Textures/Mips/BMP/mip0.bmp");
        yield return request;

        while (true)
        {
            if (request.isDone)
            {
                Debug.Log("complete");
                break;
            }
            yield return new WaitForSeconds(2);
        }
        Texture2D texture = request.asset as Texture2D;
        if (texture != null)
        {
            // Use your loaded texture here
        }
    }
    
    // Update is called once per frame
    void Update()
    {
        
    }
}

