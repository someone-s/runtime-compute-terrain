using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Collections;
using System.Runtime.InteropServices;
using UnityEngine.Events;
using UnityEngine.Rendering;

[RequireComponent(typeof(TerrainCoordinator))]
public class TerrainIntersector : MonoBehaviour {


    // General state
    private static int meshSize = 250;
    [SerializeField] private ComputeShader computeShader;
    private TerrainCoordinator coordinator;

    private (int x, int y)? currentRegion = null;

    private void Start()
    {
        coordinator = GetComponent<TerrainCoordinator>();

        computeShader = Instantiate(computeShader);

        int findIntersectKernelIndex = computeShader.FindKernel("FindIntersect");

        computeShader.SetInt(Shader.PropertyToID("size"), meshSize);

        computeShader.SetInt(Shader.PropertyToID("intersectSection"), Mathf.CeilToInt(((float)meshSize * 2) / 32f));
        intersectBuffer = new ComputeBuffer(1, sizeof(float) * 3 + sizeof(uint), ComputeBufferType.Structured, ComputeBufferMode.SubUpdates);
        computeShader.SetBuffer(findIntersectKernelIndex, Shader.PropertyToID("intersectResult"), intersectBuffer);
    }

    private void OnDestroy()
    {
        intersectBuffer.Dispose();
    }

    private void SetIntersectArea() {
        int findIntersectKernelIndex = computeShader.FindKernel("FindIntersect");

        computeShader.SetInt(Shader.PropertyToID("stride"), TerrainController.VertexBufferStride);
        computeShader.SetInt(Shader.PropertyToID("positionOffset"), TerrainController.VertexPositionAttributeOffset);
        computeShader.SetInt(Shader.PropertyToID("normalOffset"), TerrainController.VertexNormalAttributeOffset);

        GraphicsBuffer meshBL = coordinator.controllers[currentRegion.Value].graphicsBuffer;
        computeShader.SetBuffer(findIntersectKernelIndex, Shader.PropertyToID("verticesBL"), meshBL);

        GraphicsBuffer meshBR = coordinator.controllers[(currentRegion.Value.x + 1, currentRegion.Value.y)].graphicsBuffer;
        computeShader.SetBuffer(findIntersectKernelIndex, Shader.PropertyToID("verticesBR"), meshBR);

        GraphicsBuffer meshTL = coordinator.controllers[(currentRegion.Value.x    , currentRegion.Value.y + 1)].graphicsBuffer;
        computeShader.SetBuffer(findIntersectKernelIndex, Shader.PropertyToID("verticesTL"), meshTL);

        GraphicsBuffer meshTR = coordinator.controllers[(currentRegion.Value.x + 1, currentRegion.Value.y + 1)].graphicsBuffer;
        computeShader.SetBuffer(findIntersectKernelIndex, Shader.PropertyToID("verticesTR"), meshTR);

    }


    private Vector3 rayOrigin, rayDirection;
    private (int x, int y) targetRegion;
    public UnityAction<Vector3> intersectCallback;
    private ComputeBuffer intersectBuffer;
    

    [StructLayout(LayoutKind.Sequential)]
    private struct IntersectResult
    {
        public Vector3 position { get; set; }
        public uint hit { get; set; }
    }

    public void QueueIntersect((int x, int y) region, Vector3 origin, Vector3 direction)
    {

        rayOrigin = origin;
        rayDirection = direction;

        targetRegion = region;
        enabled = true;
    }

    private void ExecuteIntersect()
    {
        //if (targetRegion != currentRegion) {
            currentRegion = targetRegion;
            SetIntersectArea();
        //}

        computeShader.SetVector(Shader.PropertyToID("origin"), rayOrigin);
        computeShader.SetVector(Shader.PropertyToID("direction"), rayDirection);
        computeShader.Dispatch(computeShader.FindKernel("FindIntersect"), 32, 32, 1);
        AsyncGPUReadback.Request(intersectBuffer, (request) =>
        {
            NativeArray<IntersectResult> result = request.GetData<IntersectResult>();
            if (result[0].hit == 1)
            {
                Vector3 globalPosition = result[0].position;
                globalPosition.x += currentRegion.Value.x;
                globalPosition.z += currentRegion.Value.y;
                intersectCallback.Invoke(globalPosition);
            }
            result.Dispose();
        });
    }


    private void LateUpdate()
    {
        ExecuteIntersect();

        enabled = false;
    }
}