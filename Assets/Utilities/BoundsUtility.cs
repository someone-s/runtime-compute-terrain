using UnityEngine;
using Unity.Mathematics;

public class BoundsUtility
{
    public static void TransformMinMax(ref Vector3 min, ref Vector3 max, in float4x4 matrix)
    {
        float4 minf = new float4((float3)min, 1f);
        float4 maxf = new float4((float3)max, 1f);
        TransformMinMax(ref minf, ref maxf, matrix);
        min.Set(minf.x, minf.y, minf.z);
        max.Set(maxf.x, maxf.y, maxf.z);
    }
    public static void TransformMinMax(ref float4 min, ref float4 max, in float4x4 matrix)
    {
        float4 xa = matrix.c0 * min.x;
        float4 xb = matrix.c0 * max.x;

        float4 ya = matrix.c1 * min.y;
        float4 yb = matrix.c1 * max.y;

        float4 za = matrix.c2 * min.z;
        float4 zb = matrix.c2 * max.z;

        float4 col4Pos = new float4(matrix.c3.xyz, 0f);
        min = math.min(xa, xb) + math.min(ya, yb) + math.min(za, zb) + col4Pos;
        max = math.max(xa, xb) + math.max(ya, yb) + math.max(za, zb) + col4Pos;
    }
}