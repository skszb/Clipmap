using System;
using UnityEngine;

public struct AABB2Int
{
    public Vector2Int min;
    public Vector2Int max;


    public AABB2Int(Vector2Int min, Vector2Int max)
    {
        this.min = min;
        this.max = max;
    }


    public AABB2Int(int minX, int minY, int maxX, int maxY)
    {
        min = new Vector2Int(minX, minY);
        max = new Vector2Int(maxX, maxY);
    }


    public static AABB2Int operator +(AABB2Int boxA, int num)
    {
        return new AABB2Int(boxA.min.x + num,
            boxA.min.y + num,
            boxA.max.x + num,
            boxA.max.y + num);
    }


    public static AABB2Int operator -(AABB2Int boxA, int num)
    {
        return new AABB2Int(boxA.min.x - num,
            boxA.min.y - num,
            boxA.max.x - num,
            boxA.max.y - num);
    }


    public static AABB2Int operator +(AABB2Int boxA, Vector2Int vec)
    {
        return new AABB2Int(boxA.min.x + vec.x,
            boxA.min.y + vec.y,
            boxA.max.x + vec.x,
            boxA.max.y + vec.y);
    }


    public static AABB2Int operator -(AABB2Int boxA, Vector2Int vec)
    {
        return new AABB2Int(boxA.min.x - vec.x,
            boxA.min.y - vec.y,
            boxA.max.x - vec.x,
            boxA.max.y - vec.y);
    }


    public bool IsValid()
    {
        return min.x < max.x && min.y < max.y;
    }


    public int Width()
    {
        return max.x - min.x;
    }


    public int Height()
    {
        return max.y - min.y;
    }


    public int Area()
    {
        return Width() * Height();
    }

    public bool Contains(Vector2Int point)
    {
        var result = min.x <= point.x && point.x <= max.x &&
                     min.y <= point.y && point.y <= max.y;
        return result;
    }
    
    // Clamp this AABB to within the given AABB box
    public AABB2Int ClampBy(AABB2Int box)
    {
        return new AABB2Int(Math.Max(box.min.x, min.x), Math.Max(box.min.y, min.y),
            Math.Min(box.max.x, max.x), Math.Min(box.max.y, max.y));
    }

    // Clamp the coordinate of given vector to within this AABB
    public Vector2Int ClampVec2Int(Vector2Int vector)
    {
        return new Vector2Int(Math.Clamp(vector.x, min.x, max.x), Math.Clamp(vector.y, min.y, max.y));
    }
}