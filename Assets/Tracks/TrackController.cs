using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Splines;

[RequireComponent(typeof(MeshFilter))]
public class TrackController : MonoBehaviour
{
    [SerializeField] private ComputeShader computeShader;
    private Spline spline;

    private void Start()
    {
        SplineContainer container = GetComponent<SplineContainer>();
        spline = new Spline(2);

        Setup();
    }

    #region Test Section
    public Transform a, b;
    public float x, y;
    private void Update()
    {
        SetPoints(a.position, a.rotation, x, b.position, b.rotation, y);
        QueueRefresh();
        
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

    private void SetPoints(Vector3 aPos, Quaternion aRot, float aExt, Vector3 bPos, Quaternion bRot, float bExt)
    {
        BezierKnot[] knots = new BezierKnot[2];
        knots[0].Position = aPos - transform.position;
        knots[0].TangentOut = aRot * Vector3.forward * aExt;
        knots[1].Position = bPos - transform.position;
        knots[1].TangentIn = bRot * Vector3.forward * bExt;
        spline.Knots = knots;
    }
    #endregion
    
    private static TrackProfile[] profiles = new TrackProfile[] {
        new () {
            count = 2,
            points = new Vector3[] 
            { 
                new Vector3(-0.5f,  0f,   0f),
                new Vector3( 0.5f,  0f,   0f),
            },
            normals = new Vector3[] 
            {
                new Vector3( 0f,     1f,     0f),
                new Vector3( 0f,     1f,     0f),
            }
        },

        new () {
            count = 2,
            points = new Vector3[] 
            { 
                new Vector3(-10f,   -5f, 0f),
                new Vector3(-0.5f,  0f,   0f),
            },
            normals = new Vector3[] 
            {
                new Vector3(-0.707f, 0.707f, 0f),
                new Vector3( 0f,     1f,     0f),
            }
        },

        new () {
            count = 2,
            points = new Vector3[] 
            { 
                new Vector3( 0.5f,  0f,   0f),
                new Vector3( 10f,   -5f, 0f)
            },
            normals = new Vector3[] 
            {
                new Vector3( 0f,     1f,     0f),
                new Vector3( 0.707f, 0.707f, 0f)
            }
        },

        new () {
            count = 2,
            points = new Vector3[] 
            { 
                new Vector3(-0.5f,  0f,   0f),
                new Vector3(-10f,   5f, 0f)
            },
            normals = new Vector3[] 
            {
                new Vector3( 0f,     1f,     0f),
                new Vector3(0.707f, -0.707f, 0f)
            }
        },

        new () {
            count = 2,
            points = new Vector3[] 
            { 
                new Vector3( 10f,   5f, 0f),
                new Vector3( 0.5f,  0f,   0f)
            },
            normals = new Vector3[] 
            {
                new Vector3(-0.707f, 0.707f, 0f),
                new Vector3( 0f,     1f,     0f)
            }
        }
    };
    private static int maxSliceCount = 1024;

    private Mesh mesh;
    public GraphicsBuffer graphicsBuffer { get; private set; }
    private ComputeBuffer pointsBuffer;
    public int profileIndex;
    private void Setup()
    {
        TrackProfile profile = profiles[profileIndex];

        MeshFilter filter = GetComponent<MeshFilter>();
        mesh = MeshGenerator.GetMesh(profile);
        mesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.CopyDestination;
        filter.sharedMesh = mesh;
        graphicsBuffer = mesh.GetVertexBuffer(0);

        computeShader = Instantiate(computeShader);

        int updateTrackKernelIndex = computeShader.FindKernel("UpdateTrack");

        computeShader.SetInt(Shader.PropertyToID("sliceSize"), profile.count);
        computeShader.SetInt(Shader.PropertyToID("sliceCount"), maxSliceCount);
        computeShader.SetInt(Shader.PropertyToID("slicePerThread"), Mathf.CeilToInt((float)maxSliceCount / 64f));

        computeShader.SetInt(Shader.PropertyToID("stride"), MeshGenerator.GetVertexBufferStride(profile));
        computeShader.SetInt(Shader.PropertyToID("positionOffset"), MeshGenerator.GetVertexPositionAttributeOffset(profile));
        computeShader.SetInt(Shader.PropertyToID("normalOffset"), MeshGenerator.GetVertexNormalAttributeOffset(profile));

        computeShader.SetBuffer(updateTrackKernelIndex, Shader.PropertyToID("sourceVertices"), MeshGenerator.GetReference(profile));
        computeShader.SetBuffer(updateTrackKernelIndex, Shader.PropertyToID("destVertices"), graphicsBuffer);
        
        pointsBuffer = new ComputeBuffer(maxSliceCount, sizeof(float) * 7, ComputeBufferType.Structured, ComputeBufferMode.SubUpdates);
        computeShader.SetBuffer(updateTrackKernelIndex, Shader.PropertyToID("points"), pointsBuffer);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public Vector3 position;
        public Quaternion rotation;
    }

    private void QueueRefresh() {
        enabled = true;
    }
    private static float targetSegmentLength = 1f;
    private void ExecuteRefresh()
    {
        float totalLength = CurveUtility.ApproximateLength(spline.GetCurve(0));
        int segmentCount = Mathf.Min(Mathf.CeilToInt(totalLength / targetSegmentLength), maxSliceCount - 1);
        float actualSegmentLength = totalLength / segmentCount;
        int pointCount = segmentCount + 1;

        Vector3 meshMin = Vector3.positiveInfinity;
        Vector3 meshMax = Vector3.negativeInfinity;

        NativeArray<Point> pointsDestination = pointsBuffer.BeginWrite<Point>(0, pointCount);
        float t = 0;
        for (int p = 0; p < pointCount; p++)
        {
            spline.Evaluate(t, out float3 position, out float3 tangent, out float3 upVector);
            pointsDestination[p] = new Point
            {
                position = position,
                rotation = quaternion.LookRotation(tangent, upVector)
            };
            if (position.x < meshMin.x)
                meshMin.x = position.x;
            if (position.y < meshMin.y)
                meshMin.y = position.y;
            if (position.z < meshMin.z)
                meshMin.z = position.z;
            if (position.x > meshMax.x)
                meshMax.x = position.x;
            if (position.y > meshMax.y)
                meshMax.y = position.y;
            if (position.z > meshMax.z)
                meshMax.z = position.z;
            spline.GetPointAtLinearDistance(t, actualSegmentLength, out t);
        }
        pointsBuffer.EndWrite<Point>(pointCount);

        computeShader.SetInt(Shader.PropertyToID("pointCount"), pointCount);

        computeShader.Dispatch(computeShader.FindKernel("UpdateTrack"), 64, 1, 1);

        mesh.bounds = new Bounds((meshMax + meshMin) * 0.5f, (meshMax - meshMin) * 1f + Vector3.one);
    }

    public TerrainModifier modifier;

    private void LateUpdate()
    {
        ExecuteRefresh();
        modifier.QueueProject((0, 0));
        modifier.QueueProject((-1, 0));
        modifier.QueueProject((0, -1));
        modifier.QueueProject((-1, -1));
        //enabled = false;
    }

    private static class MeshGenerator
    {
        public struct Entry
        {
            public Mesh prototype;
            public GraphicsBuffer reference;
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

            return entry.reference;
        }

        private static Entry CreateMesh(TrackProfile profile)
        {
            NativeArray<float3> vertices = new NativeArray<float3>(profile.count * maxSliceCount, Allocator.Temp);
            NativeArray<float3> normals = new NativeArray<float3>(profile.count * maxSliceCount, Allocator.Temp);
            for (int s = 0; s < maxSliceCount; s++)
                for (int p = 0; p < profile.count; p++)
                {
                    vertices[s * profile.count + p] = profile.points[p];
                    normals[s * profile.count + p] = profile.normals[p];
                }

            NativeArray<int> indices = new NativeArray<int>(6 * (profile.count - 1) * (maxSliceCount - 1), Allocator.Temp);
            for (int s = 0; s < (maxSliceCount - 1); s++)
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
                reference                        = mesh.GetVertexBuffer(0), 
                vertexBufferStride              = mesh.GetVertexBufferStride(0), 
                vertexPositionAttributeOffset   = mesh.GetVertexAttributeOffset(VertexAttribute.Position), 
                vertexNormalAttributeOffset     = mesh.GetVertexAttributeOffset(VertexAttribute.Normal)
            };

            entries.Add(profile, entry);

            return entry;
        }

    }
}