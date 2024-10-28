using System.Collections;
using UnityEngine;

public class MonoScratch : MonoBehaviour
{
    // Start is called before the first frame update
    private void Start()
    {
        var texPath = "Assets/Cache/TestTex.asset";
        // Texture2D tex = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        // AssetDatabase.CreateAsset(tex, );

        var rr = Resources.LoadAsync<Texture2D>(texPath);
        if (rr.isDone)
        {
            var tex = rr.asset;
        }

        StartCoroutine(LoadTexture(texPath));
    }

    // Update is called once per frame
    private void Update()
    {
    }


    private IEnumerator LoadTexture(string path)
    {
        var request = Resources.LoadAsync<TextureTile>("Assets/Resources/Textures/Mips/BMP/mip0.bmp");
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

        var texture = request.asset as Texture2D;
        if (texture != null)
        {
            // Use your loaded texture here
        }
    }
}