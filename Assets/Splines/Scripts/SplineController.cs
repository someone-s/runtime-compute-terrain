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
    public GraphicsBuffer graphicsBuffer { get; private set; }
    
    public void Setup(SplineProfile profileToUse)
    {
        profile = profileToUse;

        MeshFilter filter = GetComponent<MeshFilter>();
        mesh = SplineGenerator.GetMesh(profile);
        mesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.CopyDestination;
        mesh.indexBufferTarget |= GraphicsBuffer.Target.Raw;
        filter.sharedMesh = mesh;
        graphicsBuffer = mesh.GetVertexBuffer(0);

        computeShader = Instantiate(computeShader);

        updateTrackKernel = computeShader.FindKernel("UpdateTrack");

        computeShader.SetInt("_MaxPoint", profile.maxPointCount);
        computeShader.SetInt("_SliceSize", profile.continous.Value.mapping.Length);

        computeShader.SetInt("_Stride", SplineGenerator.GetVertexBufferStride(profile));
        computeShader.SetInt("_PositionOffset", SplineGenerator.GetVertexPositionAttributeOffset(profile));
        computeShader.SetInt("_NormalOffset", SplineGenerator.GetVertexNormalAttributeOffset(profile));
        computeShader.SetInt("_UVOffset", SplineGenerator.GetVertexUVAttributeOffset(profile));

        computeShader.SetBuffer(updateTrackKernel, "_SourceVertices", SplineGenerator.GetReference(profile));
        computeShader.SetBuffer(updateTrackKernel, "_DestVertices", graphicsBuffer);

        computeShader.SetFloat("_UVStretch", profile.continous.Value.stretch);

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
        int segmentCount = Mathf.Min(Mathf.CeilToInt(approxLength / profile.spacing), profile.maxPointCount - 1);
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

        computeShader.SetInt("_Vertical", profile.vertical ? 1 : 0);

        computeShader.Dispatch(updateTrackKernel, 1, 1, 1);

        Bounds bounds = new Bounds(p0, Vector3.zero);
        bounds.Encapsulate(p1);
        bounds.Encapsulate(p2);
        bounds.Encapsulate(p3);
        bounds.Expand(10f);
        mesh.bounds = bounds;

        SubMeshDescriptor descriptor = new SubMeshDescriptor {
            baseVertex = 0,
            firstVertex = 0,
            vertexCount = segmentCount * profile.continous.Value.mapping.Length,
            indexStart = 0,
            indexCount = 6 * segmentCount * (profile.continous.Value.mapping.Length - 1),
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
    #endregion
}