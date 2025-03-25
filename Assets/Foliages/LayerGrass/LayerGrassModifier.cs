using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LayerGrassModifier : MonoBehaviour
{
    private static LayerGrassModifier instance = null;
    public static LayerGrassModifier Instance
    {
        get
        {
            if (instance == null)
                instance = FindFirstObjectByType<LayerGrassModifier>();
            return instance;
        }
    }

    [SerializeField] private ComputeShader layerShader;

    [Header("Grass Setting")]
    [SerializeField] private float grassHeight = 1f;

    private static int numLayers = 12;
    private static int meshSize => TerrainCoordinator.meshSize;
    private (int x, int z) threadGroups;
    private int layerKernel;

    private void Start() => Setup();
    private void Setup()
    {
        layerShader = Instantiate(layerShader);

        layerKernel = layerShader.FindKernel("Layer");

        //layerShader.SetBuffer(layerKernel, Shader.PropertyToID("_TerrainVertices"), controller.vertexBuffer);
        layerShader.SetInt(Shader.PropertyToID("_TerrainStride"), TerrainController.VertexBufferStride);
        layerShader.SetInt(Shader.PropertyToID("_TerrainPositionOffset"), TerrainController.VertexPositionAttributeOffset);
        layerShader.SetInt(Shader.PropertyToID("_TerrainNormalOffset"), TerrainController.VertexNormalAttributeOffset);

        //layerShader.SetBuffer(layerKernel, Shader.PropertyToID("_GeneratedVertices"), vertexBuffer);
        layerShader.SetInt(Shader.PropertyToID("_GeneratedStride"), LayerGrassController.VertexBufferStride);
        layerShader.SetInt(Shader.PropertyToID("_GeneratedPositionOffset"), LayerGrassController.VertexPositionAttributeOffset);
        layerShader.SetInt(Shader.PropertyToID("_GeneratedNormalOffset"), LayerGrassController.VertexNormalAttributeOffset);
        layerShader.SetInt(Shader.PropertyToID("_GeneratedUVAndHeightOffset"), LayerGrassController.VertexUVAndHeightAttributeOffset);

        layerShader.SetFloat("_GrassHeight", grassHeight);
        layerShader.SetInt("_NumGrassLayers", numLayers);
        layerShader.SetInt("_MeshSize", meshSize);
        layerShader.SetInt(Shader.PropertyToID("_VerticesPerAxis"), meshSize + 1);

        layerShader.GetKernelThreadGroupSizes(layerKernel, out uint threadGroupSizeX, out _, out uint threadGroupSizeZ);
        threadGroups = (
            Mathf.CeilToInt((float)(meshSize + 1) / threadGroupSizeX),
            Mathf.CeilToInt((float)(meshSize + 1) / threadGroupSizeZ)
        );
    }

    private Queue<(TerrainController controller, GraphicsBuffer vertexBuffer)> queue = new();

    public void QueueRefresh(TerrainController controller, GraphicsBuffer vertexBuffer)
    {
        queue.Enqueue((controller, vertexBuffer));

        enabled = true;
    }

    private void ExecuteRefresh()
    {
        for (int i = 0; i < 4; i++)
        {
            if (!queue.TryDequeue(out (TerrainController controller, GraphicsBuffer vertexBuffer)entry))
                break;
            if (entry.controller == null && entry.vertexBuffer == null)
                continue;

            layerShader.SetBuffer(layerKernel, Shader.PropertyToID("_TerrainVertices"), entry.controller.vertexBuffer);
            layerShader.SetBuffer(layerKernel, Shader.PropertyToID("_GeneratedVertices"), entry.vertexBuffer);
            layerShader.Dispatch(layerKernel, threadGroups.x, 1, threadGroups.z);
        }
    }

    private void LateUpdate()
    {
        ExecuteRefresh();
        if (queue.Count == 0)
            enabled = false;
    }
}