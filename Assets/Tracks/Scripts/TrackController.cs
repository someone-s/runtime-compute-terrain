using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Splines;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class TrackController : MonoBehaviour
{
    [SerializeField] private ComputeShader computeShader;
    private Spline spline;

    #region Knot Section
    public void SetPoints(Vector3 aPos, Quaternion aRot, Vector3 bPos, Quaternion bRot)
    {
        BezierKnot[] knots = new BezierKnot[2];
        knots[0].Position = aPos - transform.position;
        knots[0].TangentOut = aRot * Vector3.forward * Vector3.Distance(aPos, bPos) * 0.5f;
        knots[1].Position = bPos - transform.position;
        knots[1].TangentIn = bRot * Vector3.forward * Vector3.Distance(aPos, bPos) * 0.5f;
        spline.Knots = knots;
    }
    #endregion
    
    private static int maxSliceCount = 512;

    private TrackProfile profile;
    private Mesh mesh;
    public GraphicsBuffer graphicsBuffer { get; private set; }
    private ComputeBuffer pointsBuffer;
    public void Setup(TrackProfile profileToUse)
    {
        profile = profileToUse;

        spline = new Spline(2);

        MeshFilter filter = GetComponent<MeshFilter>();
        mesh = MeshGenerator.GetMesh(profile);
        mesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.CopyDestination;
        mesh.indexBufferTarget |= GraphicsBuffer.Target.Raw;
        filter.sharedMesh = mesh;
        graphicsBuffer = mesh.GetVertexBuffer(0);

        computeShader = Instantiate(computeShader);

        int updateTrackKernelIndex = computeShader.FindKernel("UpdateTrack");

        computeShader.SetInt(Shader.PropertyToID("_SliceSize"), profile.count);

        computeShader.SetInt(Shader.PropertyToID("_Stride"), MeshGenerator.GetVertexBufferStride(profile));
        computeShader.SetInt(Shader.PropertyToID("_PositionOffset"), MeshGenerator.GetVertexPositionAttributeOffset(profile));
        computeShader.SetInt(Shader.PropertyToID("_NormalOffset"), MeshGenerator.GetVertexNormalAttributeOffset(profile));

        computeShader.SetBuffer(updateTrackKernelIndex, Shader.PropertyToID("_SourceVertices"), MeshGenerator.GetReference(profile));
        computeShader.SetBuffer(updateTrackKernelIndex, Shader.PropertyToID("_DestVertices"), graphicsBuffer);
        
        pointsBuffer = new ComputeBuffer(maxSliceCount, sizeof(float) * 7, ComputeBufferType.Structured, ComputeBufferMode.SubUpdates);
        computeShader.SetBuffer(updateTrackKernelIndex, Shader.PropertyToID("_Points"), pointsBuffer);

    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public Vector3 position;
        public Quaternion rotation;
    }

    public void QueueRefresh() {
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
                rotation = profile.vertical ? quaternion.LookRotation(Vector3.Normalize(new Vector3(tangent.x, 0f, tangent.z)), Vector3.up) : quaternion.LookRotation(tangent, upVector)
            };
            // if (position.x < meshMin.x)
            //     meshMin.x = position.x;
            // if (position.y < meshMin.y)
            //     meshMin.y = position.y;
            // if (position.z < meshMin.z)
            //     meshMin.z = position.z;
            // if (position.x > meshMax.x)
            //     meshMax.x = position.x;
            // if (position.y > meshMax.y)
            //     meshMax.y = position.y;
            // if (position.z > meshMax.z)
            //     meshMax.z = position.z;
            spline.GetPointAtLinearDistance(t, actualSegmentLength, out t);
        }
        pointsBuffer.EndWrite<Point>(pointCount);

        computeShader.SetInt(Shader.PropertyToID("_PointCount"), pointCount);

        computeShader.Dispatch(computeShader.FindKernel("UpdateTrack"), 1, 1, 1);

        Bounds b = spline.GetBounds();
        b.Expand(profile.extent);
        mesh.bounds = b;

        SubMeshDescriptor descriptor = new SubMeshDescriptor {
            baseVertex = 0,
            firstVertex = 0,
            vertexCount = maxSliceCount * profile.count,
            indexStart = 0,
            indexCount = 6 * (maxSliceCount - 1) * (profile.count - 1),
            topology = MeshTopology.Triangles,
            bounds = b
        };

        // int gotPoints = 50; // from compute shader
        // Bounds bound = new Bounds(); // from compute shader
        // int vertexCountPerPoint = 5; // from track profile

        // SubMeshDescriptor descriptor = new SubMeshDescriptor {
        //     baseVertex = 0,
        //     firstVertex = 0,
        //     vertexCount = gotPoints * vertexCountPerPoint,
        //     indexStart = 0,
        //     indexCount = 6 * (gotPoints - 1) * (vertexCountPerPoint - 1),
        //     topology = MeshTopology.Triangles,
        //     bounds = bound
        // };

        MeshUpdateFlags updateFlags = 
            MeshUpdateFlags.DontValidateIndices |    // dont check against CPU index buffer
            MeshUpdateFlags.DontResetBoneBounds |    // dont reset skinned mesh bones bound
            // MeshUpdateFlags.DontNotifyMeshUsers | // do notify on possible mesh bound change 
            MeshUpdateFlags.DontRecalculateBounds;   // dont recalculate bounds

        mesh.SetSubMesh(0, descriptor, updateFlags);

        //GetComponent<MeshRenderer>().subMeshStartIndex =
    }

    public TerrainCoordinator coordinator;

    private void LateUpdate()
    {
        Bounds oldBounds = mesh.bounds;
        ExecuteRefresh();
        Bounds newBounds = mesh.bounds;
        coordinator.Project(Vector3.Min(oldBounds.min, newBounds.min), Vector3.Max(oldBounds.max, newBounds.max));
        enabled = false;
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
            mesh.indexBufferTarget |= GraphicsBuffer.Target.Raw;
            mesh.SetVertices(vertices);
            mesh.SetIndices(indices, MeshTopology.Triangles, 0);
            mesh.SetNormals(normals);

            vertices.Dispose();
            normals.Dispose();
            indices.Dispose();

            Entry entry = new Entry() {
                prototype                       = mesh,
                reference                       = mesh.GetVertexBuffer(0), 
                vertexBufferStride              = mesh.GetVertexBufferStride(0), 
                vertexPositionAttributeOffset   = mesh.GetVertexAttributeOffset(VertexAttribute.Position), 
                vertexNormalAttributeOffset     = mesh.GetVertexAttributeOffset(VertexAttribute.Normal)
            };

            entries.Add(profile, entry);

            return entry;
        }

    }
}