using System;
using UnityEngine;

[CreateAssetMenu(fileName = "Spline Profile", menuName = "Spline/Profile", order = 0)]
[Serializable]
public class SplineProfile : ScriptableObject
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