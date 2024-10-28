using UnityEngine;

internal class ClipmapUtil
{
    public static int FloorDivision(int num1, int num2)
    {
        return (int)Mathf.Floor((float)num1 / num2);
    }

    public static Vector2Int FloorDivision(Vector2Int vec, int num)
    {
        return new Vector2Int(FloorDivision(vec.x, num), FloorDivision(vec.y, num));
    }

    public class UpdateRegion
    {
        public AABB2Int ClipmapBound;
        public AABB2Int SrcBound;

        public Texture2D SrcTexture;
        public AABB2Int UpdateRegionBound;
    }
}