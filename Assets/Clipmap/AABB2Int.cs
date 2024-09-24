using System;
using System.Numerics;
using UnityEngine;

public class AABB2Int
{
    Vector2Int min;
    Vector2Int max;

    public AABB2Int()
    {
    }

    public AABB2Int(Vector2Int min, Vector2Int max)
    {
        this.min = min;
        this.max = max;
    }

    public AABB2Int(int minX, int minY, int maxX, int maxY)
    {
        this.min.x = minX;
        this.min.y = minY;
        this.max.x = maxX;
        this.max.y = maxY;
    }

    public bool isValid()
    {
        return min.x < max.x && min.y < max.y;
    }

    public AABB2Int clamp(AABB2Int box)
    {
        return new AABB2Int(Math.Max(box.min.x, this.min.x), Math.Max(box.min.y, this.min.y),
                            Math.Min(box.max.x, this.max.x), Math.Min(box.max.y, this.max.y));
    }
}
