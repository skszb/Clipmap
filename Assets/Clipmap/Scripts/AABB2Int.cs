using System;
using System.Numerics;
using UnityEngine;

public class AABB2Int
{
    public Vector2Int min;
    public Vector2Int max;

    public AABB2Int()
    {
        min = new Vector2Int(0, 0);
        max = new Vector2Int(0, 0);
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

    public static AABB2Int operator+(AABB2Int boxA, int num)
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

    public bool isValid()
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

    public AABB2Int Clamp(AABB2Int box)
    {
        return new AABB2Int(Math.Max(box.min.x, this.min.x), Math.Max(box.min.y, this.min.y),
                            Math.Min(box.max.x, this.max.x), Math.Min(box.max.y, this.max.y));
    }
}
