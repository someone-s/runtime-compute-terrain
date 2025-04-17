using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;

[RequireComponent(typeof(MeshFilter))]
public class TrackController : MonoBehaviour
{
    
    private Vector3 p0, p1, p2, p3;

    #region Event Section
    public UnityEvent OnTrackChange;
    #endregion

    
    #region Modify Section
    [SerializeField] private ComputeShader computeShader;
    private int updateTrackKernel;
    private uint threadCount;

    private static int maxPointCount = 512;
    private static float targetSegmentLength = 1f;

    private TrackProfile profile;
    private Mesh mesh;
    public GraphicsBuffer graphicsBuffer { get; private set; }
    
    public void Setup(TrackProfile profileToUse)
    {
        profile = profileToUse;

        MeshFilter filter = GetComponent<MeshFilter>();
        mesh = MeshGenerator.GetMesh(profile);
        mesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.CopyDestination;
        mesh.indexBufferTarget |= GraphicsBuffer.Target.Raw;
        filter.sharedMesh = mesh;
        graphicsBuffer = mesh.GetVertexBuffer(0);

        computeShader = Instantiate(computeShader);

        updateTrackKernel = computeShader.FindKernel("UpdateTrack");

        computeShader.SetInt("_MaxPoint", maxPointCount);
        computeShader.SetInt("_SliceSize", profile.count);

        computeShader.SetInt("_Stride", MeshGenerator.GetVertexBufferStride(profile));
        computeShader.SetInt("_PositionOffset", MeshGenerator.GetVertexPositionAttributeOffset(profile));
        computeShader.SetInt("_NormalOffset", MeshGenerator.GetVertexNormalAttributeOffset(profile));

        computeShader.SetBuffer(updateTrackKernel, "_SourceVertices", MeshGenerator.GetReference(profile));
        computeShader.SetBuffer(updateTrackKernel, "_DestVertices", graphicsBuffer);

        computeShader.GetKernelThreadGroupSizes(updateTrackKernel, out threadCount, out _, out _);
    }


    public void QueueRefresh(Vector3 aPos, Quaternion aRot, Vector3 bPos, Quaternion bRot) 
    {
        p0 = aPos - transform.position;
        p1 = p0 + aRot * Vector3.forward * Vector3.Distance(aPos, bPos) * 0.5f;
        p3 = bPos - transform.position;
        p2 = p3 + bRot * Vector3.forward * Vector3.Distance(aPos, bPos) * 0.5f;

        enabled = true;
    }
    private void ExecuteRefresh()
    {
        float approxLength = (Vector3.Distance(p0, p1) + Vector3.Distance(p1, p2) + Vector3.Distance(p2, p3) + Vector3.Distance(p3, p0)) * 0.5f;
        int segmentCount = Mathf.Min(Mathf.CeilToInt(approxLength / targetSegmentLength), maxPointCount - 1);
        float actualSpacing = approxLength / segmentCount;
        int pointCount = segmentCount + 1;

        computeShader.SetFloat("_ApproxLength", approxLength);
        computeShader.SetFloat("_ActualSpacing", actualSpacing);
        computeShader.SetInt("_PointCount", pointCount);
        computeShader.SetInt("_PointPerThread", Mathf.CeilToInt((float)pointCount / threadCount));

        computeShader.SetVector("_P0", p0);
        computeShader.SetVector("_P1", p1);
        computeShader.SetVector("_P2", p2);
        computeShader.SetVector("_P3", p3);

        computeShader.SetVector("_US", Vector3.up);
        computeShader.SetVector("_UE", Vector3.up);

        computeShader.Dispatch(updateTrackKernel, 1, 1, 1);

        Bounds bounds = new Bounds(p0, Vector3.zero);
        bounds.Encapsulate(p1);
        bounds.Encapsulate(p2);
        bounds.Encapsulate(p3);
        bounds.Expand(profile.extent);
        mesh.bounds = bounds;

        SubMeshDescriptor descriptor = new SubMeshDescriptor {
            baseVertex = 0,
            firstVertex = 0,
            vertexCount = pointCount * profile.count,
            indexStart = 0,
            indexCount = 6 * segmentCount * (profile.count - 1),
            topology = MeshTopology.Triangles,
            bounds = bounds
        };

        MeshUpdateFlags updateFlags = 
            MeshUpdateFlags.DontValidateIndices |    // dont check against CPU index buffer
            MeshUpdateFlags.DontResetBoneBounds |    // dont reset skinned mesh bones bound
            // MeshUpdateFlags.DontNotifyMeshUsers | // do notify on possible mesh bound change 
            MeshUpdateFlags.DontRecalculateBounds;   // dont recalculate bounds

        mesh.SetSubMesh(0, descriptor, updateFlags);
    }

    private void LateUpdate()
    {
        ExecuteRefresh();
        OnTrackChange.Invoke();
        enabled = false;
    }
    #endregion

    #region Generator Section
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
            NativeArray<float3> vertices = new NativeArray<float3>(profile.count * maxPointCount, Allocator.Temp);
            NativeArray<float3> normals = new NativeArray<float3>(profile.count * maxPointCount, Allocator.Temp);
            for (int s = 0; s < maxPointCount; s++)
                for (int p = 0; p < profile.count; p++)
                {
                    vertices[s * profile.count + p] = profile.points[p];
                    normals[s * profile.count + p] = profile.normals[p];
                }

            NativeArray<int> indices = new NativeArray<int>(6 * (profile.count - 1) * (maxPointCount - 1), Allocator.Temp);
            for (int s = 0; s < (maxPointCount - 1); s++)
                for (int p = 0; p < profile.count - 1; p++)
                {
                    indices[(s * (profile.count - 1) + p) * 6 + 0] = (s + 0) * profile.count + (p + 0);
                    indices[(s * (profile.count - 1) + p) * 6 + 1] = (s + 0) * profile.count + (p + 1);
                    indices[(s * (profile.count - 1) + p) * 6 + 2] = (s + 1) * profile.count + (p + 1);
                    indices[(s * (profile.count - 1) + p) * 6 + 3] = (s + 1) * profile.count + (p + 1);
                    indices[(s * (profile.count - 1) + p) * 6 + 4] = (s + 1) * profile.count + (p + 0);
                    indices[(s * (profile.count - 1) + p) * 6 + 5] = (s + 0) * profile.count + (p + 0);
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
    #endregion
}