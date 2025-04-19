using System;
using UnityEngine;

[Serializable]
public class SplineProfile
{
    public bool vertical;
    public int count;
    public float extent;
    public TrackPoint[] points;
}

[Serializable]
public struct TrackPoint
{
    public Vector3 position;
    public Vector3 normal;
    public Vector2 uv;
}