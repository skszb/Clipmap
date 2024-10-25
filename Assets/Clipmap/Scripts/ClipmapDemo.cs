using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public class ClipmapDemo : MonoBehaviour
{

    // The pawn indicating the player position
    public Transform PlayerPawn;

    // The pawn indicating the world's origin
    public Transform WorldOrigin;

    [SerializeField]
    private Clipmap m_clipMap;


    public Renderer[] ClipmapLevelDisplay;

    // Start is called before the first frame update
    void Start()
    {
        Texture2DArray clipmapStack = m_clipMap.ClipmapStack;
        for (int i = 0; i < ClipmapLevelDisplay.Length; i++)
        {
            ClipmapLevelDisplay[i].material.SetTexture("_ClipmapStack", clipmapStack);
            ClipmapLevelDisplay[i].material.SetFloat("_ClipmapStackLevelIndex", i);
            ClipmapLevelDisplay[i].material.SetFloat("_ClipmapStackLevelCount", ClipmapLevelDisplay.Length);
        }
    }

    // Update is called once per frame
    void Update()
    {
        Vector3 worldCoordinate = PlayerPawn.position - WorldOrigin.position;
        m_clipMap.UpdateClipmap(worldCoordinate);
    }
}
