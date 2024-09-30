using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;


public class ClipmapDemo : MonoBehaviour
{

    // The pawn indicating the player position
    public Transform PlayerPawn;

    // The pawn indicating the world's origin
    public Transform WorldOrigin;

    [SerializeField]
    private Clipmap m_clipMap;

    public ClipmapParam Param;

    public Renderer[] ClipmapLevelDisplay;

    public Renderer ClipmapDisplay;


    // Start is called before the first frame update
    void Start()
    {
        Vector3 worldCoordinate = PlayerPawn.position - WorldOrigin.position;
        m_clipMap.Intialize(Param);
        m_clipMap.UpdateClipmap(PlayerPawn.position - WorldOrigin.position);

        Texture2DArray clipmapStackTextureArray = m_clipMap.GetClipmapStackTexture();
        for (int i = 0; i < ClipmapLevelDisplay.Length; i++)
        {
            ClipmapLevelDisplay[i].material.SetTexture("_ClipmapStack", clipmapStackTextureArray);
            ClipmapLevelDisplay[i].material.SetFloat("_ClipmapStackLevelIndex", i);
        }

        ClipmapDisplay?.material.SetTexture("_ClipmapStack", clipmapStackTextureArray);
        ClipmapDisplay?.material.SetInteger("_BaseMapSize", Param.baseTexture[0].width);

    }

    // Update is called once per frame
    void Update()
    {
        Vector3 worldCoordinate = PlayerPawn.position - WorldOrigin.position;
        m_clipMap.UpdateClipmap(worldCoordinate);
    }
}
