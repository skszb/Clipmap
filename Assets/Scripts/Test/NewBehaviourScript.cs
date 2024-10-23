using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class NewBehaviourScript : MonoBehaviour
{
    public Texture2D texture;
    public TextureTileLoader textureTileLoader;
    // Start is called before the first frame update
    void Start()
    {
        textureTileLoader.LoadTextures();
        texture = textureTileLoader.textures[0];
    }

    // Update is called once per frame
    void Update()
    {
    }
}
