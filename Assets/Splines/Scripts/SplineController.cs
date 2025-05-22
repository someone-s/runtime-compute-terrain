using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;

[RequireComponent(typeof(MeshFilter))]
public class SplineController : MonoBehaviour
{
    
    private Vector3 p0, p1, p2, p3;

    public UnityEvent OnTrackChange;

    [SerializeField] private ComputeShader computeShader;
    private int calculatePointKernel;
    private int updateTrackKernel;
    private uint threadCount;
    private ComputeBuffer transformBuffer;

    public ISplineCaster splineCaster;
    private int calculateRayKernel;
    private int offsetByRayKernel;
    
    private SplineProfile profile;
    private Mesh mesh;
    private SplineGenerator.Entry entry;
    public GraphicsBuffer graphicsBuffer { get; private set; }

    private bool setupComplete = false;
    private bool renderQueued = false;
    
    
    public void QueueSetup(MonoBehaviour runner, SplineDescription description)
    {
        IEnumerator loadingRoutine = SplineLoader.GetCoroutine(
            description,
            (profileToUse) => {
                if (profileToUse != null) 
                    Setup(profileToUse.Value);
            });

        runner.StartCoroutine(loadingRoutine);
    }
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

        // Common Parameters
        transformBuffer = new ComputeBuffer(profile.maxPointCount, sizeof(float) * 4 * 4, ComputeBufferType.Structured, ComputeBufferMode.SubUpdates);

        // CalculatePoint Parameters
        calculatePointKernel = computeShader.FindKernel("CalculatePoint");
        computeShader.SetBuffer(calculatePointKernel, "_Transforms", transformBuffer);
        computeShader.SetInt("_Vertical", profile.vertical ? 1 : 0);

        if (profile.cast.HasValue)
        {
            SplineProfile.CastSettings castSettings = profile.cast.Value;

            splineCaster = GetComponent<ISplineCaster>();
            splineCaster.Setup(profile.maxPointCount, out ComputeBuffer castRequestBuffer, out ComputeBuffer castResultBuffer);

            // CalculateRay Parameters
            calculateRayKernel = computeShader.FindKernel("CalculateRay");
            computeShader.SetBuffer(calculateRayKernel, "_Transforms", transformBuffer);
            computeShader.SetBuffer(calculateRayKernel, "_Request",    castRequestBuffer);
            computeShader.SetVector("_CastOrigin",    castSettings.origin);
            computeShader.SetVector("_CastDirection", castSettings.direction);
            computeShader.SetFloat("_CastMaxOffset",  castSettings.maxOffset);
            
            // OffsetByRay Parameters
            offsetByRayKernel = computeShader.FindKernel("OffsetByRay");
            computeShader.SetBuffer(offsetByRayKernel, "_Transforms", transformBuffer);
            computeShader.SetBuffer(offsetByRayKernel, "_Result",     castResultBuffer);
        }

        // UpdateTrack Parameters
            updateTrackKernel = computeShader.FindKernel("UpdateTrack");
        computeShader.SetBuffer(updateTrackKernel, "_Transforms",     transformBuffer);
        computeShader.SetBuffer(updateTrackKernel, "_SourceVertices", entry.reference);
        computeShader.SetBuffer(updateTrackKernel, "_DestVertices",   graphicsBuffer);
        computeShader.SetInt("_Stride",         entry.vertexBufferStride);
        computeShader.SetInt("_PositionOffset", entry.vertexPositionAttributeOffset);
        computeShader.SetInt("_NormalOffset",   entry.vertexNormalAttributeOffset);
        computeShader.SetInt("_UVOffset",       entry.vertexUVAttributeOffset);

        computeShader.GetKernelThreadGroupSizes(updateTrackKernel, out threadCount, out _, out _);
        
        setupComplete = true;
        if (renderQueued)
            enabled = true;
    }

    private void OnDestroy()
    {
        transformBuffer?.Dispose();

        splineCaster?.Release();
    }

    public void QueueRefresh(Vector3 aPos, Quaternion aRot, Vector3 bPos, Quaternion bRot)
    {
        p0 = aPos - transform.position;
        p1 = p0 + aRot * Vector3.forward * Vector3.Distance(aPos, bPos) * 0.5f;
        p3 = bPos - transform.position;
        p2 = p3 + bRot * Vector3.forward * Vector3.Distance(aPos, bPos) * 0.5f;

        if (setupComplete)
            enabled = true;
        else
            renderQueued = true;
    }
    private void ExecuteRefresh()
    {
        float approxLength = (Vector3.Distance(p0, p1) + Vector3.Distance(p1, p2) + Vector3.Distance(p2, p3) + Vector3.Distance(p3, p0)) * 0.5f;
        int segmentCount = Mathf.Clamp(Mathf.FloorToInt(approxLength / profile.spacing), 1, profile.maxPointCount - 1);
        float actualSpacing = approxLength / segmentCount;
        float startOffset = 0f; //entry.GetStartOffset(actualSpacing);
        int pointCount = entry.GetActualPointCount(segmentCount);

        // Common Parameters
        computeShader.SetInt("_PointCount", pointCount);
        int groupCount = Mathf.CeilToInt((float)pointCount / threadCount);

        // CalculatePoint Parameters
        computeShader.SetFloat("_ApproxLength",  approxLength);
        computeShader.SetFloat("_ActualSpacing", actualSpacing);
        computeShader.SetVector("_P0", p0);
        computeShader.SetVector("_P1", p1);
        computeShader.SetVector("_P2", p2);
        computeShader.SetVector("_P3", p3);
        computeShader.SetVector("_US", Vector3.up);
        computeShader.SetVector("_UE", Vector3.up);
        computeShader.SetFloat("_StartOffset", startOffset);
        
        // CalculatePoint Dispatch
        computeShader.Dispatch(calculatePointKernel, groupCount, 1, 1);

        Bounds bounds = new(p0, Vector3.zero);
        bounds.Encapsulate(p1);
        bounds.Encapsulate(p2);
        bounds.Encapsulate(p3);
        bounds.Expand(profile.extends);
        mesh.bounds = bounds;

        if (profile.cast.HasValue)
        {
            computeShader.Dispatch(calculateRayKernel, groupCount, 1, 1);

            splineCaster.Cast(bounds, pointCount);

            computeShader.Dispatch(offsetByRayKernel, groupCount, 1, 1);
        }
        
        // UpdateTrack Parameters
        computeShader.SetFloat("_UVStretch", entry.uvStretch);

        for (int s = 0; s < entry.subMeshRanges.Length; s++)
            {
                SplineGenerator.SubMeshRange subMeshRange = entry.subMeshRanges[s];
                computeShader.SetInt("_BaseVertex", subMeshRange.vertexStart);
                computeShader.SetInt("_SliceSize",  subMeshRange.vertexCount);

                computeShader.Dispatch(updateTrackKernel, groupCount, 1, 1);

                SubMeshDescriptor descriptor = new SubMeshDescriptor
                {
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
        if (setupComplete)
        {
            ExecuteRefresh();
            OnTrackChange.Invoke();
            enabled = false;
        }

    }
}