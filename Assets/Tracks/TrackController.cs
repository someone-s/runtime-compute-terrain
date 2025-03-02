using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Splines;

[RequireComponent(typeof(SplineContainer))]
public class TrackController : MonoBehaviour
{
    private Spline spline;

    private Vector3 worldPosition;


    private void Start()
    {
        SplineContainer container = GetComponent<SplineContainer>();
        spline = container.AddSpline();

        worldPosition = transform.position;
    }

    #region Test Section
    public Transform a, b;
    private void Update()
    {
        SetPoints(a.position, a.rotation, b.position, b.rotation);
    }
    #endregion

    #region Knot Section
    private static AnimationCurve multiplierCurve = new AnimationCurve(
        new Keyframe(0f, 0.3440594f, 0.000061201645f, 0.000061201645f),
        new Keyframe(45f, 2.758091f, 0.021357296f, 0.021357296f),
        new Keyframe(90f, 2.266216f, -0.018373445f, -0.018373445f),
        new Keyframe(135f, 1.104481f, -0.017840002f, -0.017840002f),
        new Keyframe(180f, 0.6606158f, -0.007067918f, -0.007067918f),
        new Keyframe(225f, 0.4683684f, -0.0032283035f, -0.0032283035f),
        new Keyframe(270f, 0.3700685f, -0.0013002945f, -0.0013002945f),
        new Keyframe(315f, 0.3513419f, -0.0002889898f, -0.0002889898f),
        new Keyframe(360f, 0.3440594f, -0.00016183323f, -0.00016183323f));

    private void SetPoints(Vector3 aPos, Quaternion aRot, Vector3 bPos, Quaternion bRot)
    {
        float degree = 180f - Quaternion.Angle(aRot, bRot);
        if (Vector3.Dot(aRot * Vector3.right, bRot * Vector3.back) < 0f) 
            degree = 360f - degree;
        float distance = Vector3.Distance(aPos, bPos);

        float multiplier = multiplierCurve.Evaluate(degree) * distance;

        BezierKnot[] knots = new BezierKnot[2];
        knots[0].Position = aPos - worldPosition;
        knots[0].TangentOut = aRot * Vector3.forward * multiplier;
        knots[1].Position = bPos - worldPosition;
        knots[1].TangentIn = bRot * Vector3.forward * multiplier;
        spline.Knots = knots;
    }
    #endregion
    
    private static int subDivision = 30;

    private void Generate()
    {
    }

    private static class MeshGenerator
    {
        public struct Entry
        {
            public Mesh prototype;
            public GraphicsBuffer referece;
            public int vertexBufferStride;
            public int vertexPositionAttributeOffset;
            public int vertexNormalAttributeOffset;
        }

        private static Dictionary<TrackProfile, Entry> entries = new();

        public static int GetVertexBufferStride(TrackProfile profile)
        {
            if (!entries.TryGetValue(profile, out var entry))
                entry = CreateMesh(profile);

            return entry.vertexBufferStride;
        }

        public static int GetVertexPositionAttributeOffset(TrackProfile profile)
        {
            if (!entries.TryGetValue(profile, out var entry))
                entry = CreateMesh(profile);

            return entry.vertexPositionAttributeOffset;
        }

        public static int GetVertexNormalAttributeOffset(TrackProfile profile)
        {
            if (!entries.TryGetValue(profile, out var entry))
                entry = CreateMesh(profile);

            return entry.vertexNormalAttributeOffset;
        }

        public static Mesh GetMesh(TrackProfile profile)
        {
            if (!entries.TryGetValue(profile, out var entry))
                entry = CreateMesh(profile);

            return Instantiate(entry.prototype);

        }

        public static GraphicsBuffer GetReference(TrackProfile profile)
        {
            if (!entries.TryGetValue(profile, out var entry))
                entry = CreateMesh(profile);

            return entry.referece;
        }

        private static Entry CreateMesh(TrackProfile profile)
        {
            NativeArray<float3> vertices = new NativeArray<float3>(profile.count * subDivision + 1, Allocator.Temp);
            NativeArray<float3> normals = new NativeArray<float3>(profile.count * subDivision + 1, Allocator.Temp);
            for (int s = 0; s < subDivision; s++)
                for (int p = 0; p < profile.count; p++)
                {
                    vertices[s * profile.count + p] = profile.points[p];
                    normals[s * profile.count + p] = profile.normals[p];
                }

            NativeArray<int> indices = new NativeArray<int>(6 * (profile.count - 1) * subDivision, Allocator.Temp);
            for (int s = 0; s < subDivision; s++)
                for (int p = 0; p < profile.count - 1; p++)
                {
                    indices[(s * (profile.count - 1) + p) * 6 + 0] = (s + 0) * profile.count + (p + 0);
                    indices[(s * (profile.count - 1) + p) * 6 + 1] = (s + 1) * profile.count + (p + 1);
                    indices[(s * (profile.count - 1) + p) * 6 + 2] = (s + 0) * profile.count + (p + 1);
                    indices[(s * (profile.count - 1) + p) * 6 + 3] = (s + 1) * profile.count + (p + 1);
                    indices[(s * (profile.count - 1) + p) * 6 + 4] = (s + 0) * profile.count + (p + 0);
                    indices[(s * (profile.count - 1) + p) * 6 + 5] = (s + 1) * profile.count + (p + 0);
                }


            Mesh mesh = new Mesh();
            mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.CopySource;
            mesh.SetVertices(vertices);
            mesh.SetIndices(indices, MeshTopology.Triangles, 0);
            mesh.SetNormals(normals);

            vertices.Dispose();
            normals.Dispose();
            indices.Dispose();

            Entry entry = new Entry() {
                prototype                       = mesh,
                referece                        = mesh.GetVertexBuffer(0), 
                vertexBufferStride              = mesh.GetVertexBufferStride(0), 
                vertexPositionAttributeOffset   = mesh.GetVertexAttributeOffset(VertexAttribute.Position), 
                vertexNormalAttributeOffset     = mesh.GetVertexAttributeOffset(VertexAttribute.Normal)
            };

            entries.Add(profile, entry);

            return entry;
        }

    }
}