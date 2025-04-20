using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;

[RequireComponent(typeof(MeshFilter))]
public class SplineController : MonoBehaviour
{
    
    private Vector3 p0, p1, p2, p3;

    #region Event Section
    public UnityEvent OnTrackChange;
    #endregion

    
    #region Modify Section
    [SerializeField] private ComputeShader computeShader;
    private int updateTrackKernel;
    private uint threadCount;

    private SplineProfile profile;
    private Mesh mesh;
    private SplineGenerator.Entry entry;
    public GraphicsBuffer graphicsBuffer { get; private set; }
    
    public void Setup(SplineProfile profileToUse)
    {
        profile = profileToUse;

        MeshFilter filter = GetComponent<MeshFilter>();
        (Mesh instancedMesh, SplineGenerator.Entry sourceEntry) = SplineGenerator.GetMesh(profile);
        mesh = instancedMesh;
        entry = sourceEntry;
        MeshRenderer renderer = GetComponent<MeshRenderer>();
        if (renderer != null)
            renderer.SetMaterials(profile.materials);

        mesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.CopyDestination;
        mesh.indexBufferTarget |= GraphicsBuffer.Target.Raw;
        filter.sharedMesh = mesh;
        graphicsBuffer = mesh.GetVertexBuffer(0);

        computeShader = Instantiate(computeShader);

        updateTrackKernel = computeShader.FindKernel("UpdateTrack");

        computeShader.SetInt("_MaxPoint", profile.maxPointCount);

        computeShader.SetInt("_Stride", entry.vertexBufferStride);
        computeShader.SetInt("_PositionOffset", entry.vertexPositionAttributeOffset);
        computeShader.SetInt("_NormalOffset", entry.vertexNormalAttributeOffset);
        computeShader.SetInt("_UVOffset", entry.vertexUVAttributeOffset);

        computeShader.SetBuffer(updateTrackKernel, "_SourceVertices", entry.reference);
        computeShader.SetBuffer(updateTrackKernel, "_DestVertices", graphicsBuffer);

        computeShader.SetInt("_Vertical", profile.vertical ? 1 : 0);

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
        int segmentCount = Mathf.Clamp(Mathf.FloorToInt(approxLength / profile.spacing), 1, profile.maxPointCount - 1);
        float actualSpacing = approxLength / segmentCount;
        float startOffset = entry.GetStartOffset(actualSpacing);
        int pointCount = entry.GetActualPointCount(segmentCount);

        computeShader.SetFloat("_ApproxLength", approxLength);
        computeShader.SetFloat("_ActualSpacing", actualSpacing);

        computeShader.SetVector("_P0", p0);
        computeShader.SetVector("_P1", p1);
        computeShader.SetVector("_P2", p2);
        computeShader.SetVector("_P3", p3);

        computeShader.SetVector("_US", Vector3.up);
        computeShader.SetVector("_UE", Vector3.up);

        computeShader.SetFloat("_UVStretch", entry.uvStretch);

        computeShader.SetFloat("_StartOffset", startOffset);
        computeShader.SetInt("_PointCount", pointCount);
        computeShader.SetInt("_PointPerThread", Mathf.CeilToInt((float)pointCount / threadCount));

        Bounds bounds = new Bounds(p0, Vector3.zero);
        bounds.Encapsulate(p1);
        bounds.Encapsulate(p2);
        bounds.Encapsulate(p3);
        bounds.Expand(profile.extends);
        mesh.bounds = bounds;

        for (int s = 0; s < entry.subMeshRanges.Length; s++)
        {
            SplineGenerator.SubMeshRange subMeshRange = entry.subMeshRanges[s];
            computeShader.SetInt("_BaseVertex", subMeshRange.vertexStart);
            computeShader.SetInt("_SliceSize", subMeshRange.vertexCount);

            computeShader.Dispatch(updateTrackKernel, 1, 1, 1);

            SubMeshDescriptor descriptor = new SubMeshDescriptor {
                baseVertex = 0,
                firstVertex = subMeshRange.vertexStart,
                vertexCount = pointCount * subMeshRange.vertexCount,
                indexStart = subMeshRange.indexStart,
                indexCount = segmentCount * subMeshRange.indexCount,
                topology = MeshTopology.Triangles,
                bounds = bounds
            };

            MeshUpdateFlags updateFlags = 
                MeshUpdateFlags.DontValidateIndices |    // dont check against CPU index buffer
                MeshUpdateFlags.DontResetBoneBounds |    // dont reset skinned mesh bones bound
                // MeshUpdateFlags.DontNotifyMeshUsers | // do notify on possible mesh bound change 
                MeshUpdateFlags.DontRecalculateBounds;   // dont recalculate bounds

            mesh.SetSubMesh(s, descriptor, updateFlags);
        }
    }

    private void LateUpdate()
    {
        ExecuteRefresh();
        OnTrackChange.Invoke();
        enabled = false;
    }
    #endregion

    #region Generator Section
    #endregion
}