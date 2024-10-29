using System.Collections.Generic;
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

    public static Vector2Int FloorDivision(Vector2 vec, int num)
    {
        return new Vector2Int((int)Mathf.Floor(vec.x / num), (int)Mathf.Floor(vec.y / num));
    }
    
    // Snap the coordinate to the bottom left of the grid
    public static Vector2Int SnapToGrid(Vector2 coord, int gridSize)
    {
        return FloorDivision(coord, gridSize) * gridSize;
    }
}