using UnityEngine;

public class ClipmapDemo : MonoBehaviour
{
    // The pawn indicating the player position
    public Transform PlayerPawn;

    // The pawn indicating the world's origin
    public Transform WorldOrigin;

    [SerializeField] private Clipmap m_clipMap;


    public Renderer[] ClipmapLevelDisplay;

    // Start is called before the first frame update
    private void Start()
    {
        var clipmapStack = m_clipMap.ClipmapStack;
        for (var i = 0; i < ClipmapLevelDisplay.Length; i++)
        {
            ClipmapLevelDisplay[i].material.SetTexture("_ClipmapStack", clipmapStack);
            ClipmapLevelDisplay[i].material.SetFloat("_ClipmapStackLevelIndex", i);
            ClipmapLevelDisplay[i].material.SetFloat("_ClipmapStackLevelCount", ClipmapLevelDisplay.Length);
        }
    }

    // Update is called once per frame
    private void Update()
    {
        var worldCoordinate = PlayerPawn.position - WorldOrigin.position;
        // m_clipMap.UpdateClipmap(worldCoordinate);
        m_clipMap.UpdateCamera(worldCoordinate);   
    }
}