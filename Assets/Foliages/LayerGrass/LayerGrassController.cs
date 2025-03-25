using System.Collections;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(MeshFilter))]
public class LayerGrassController : MonoBehaviour
{
    private static int numLayers = 12;
    private static float area => TerrainCoordinator.area;
    private static int meshSize => TerrainCoordinator.meshSize;


    [SerializeField] private TerrainController controller;
    private GraphicsBuffer vertexBuffer;
    

    private void Start() 
    {
        if (controller.terrainReady)
            QueueSetup();
        controller.OnTerrainReady.AddListener(QueueSetup);

        controller.OnTerrainChange.AddListener(QueueRefresh);

        controller.OnTerrainVisible.AddListener(Enable);
        controller.OnTerrainHidden.AddListener(Disable);
    }

    private void Enable()
    {
        gameObject.SetActive(true);
    }

    private void Disable()
    {
        gameObject.SetActive(false);
    }

    private bool setupComplete = false;
    private void QueueSetup() => StartCoroutine(WaitSetup());
    private IEnumerator WaitSetup()
    {
        for (int i = 0; i < UnityEngine.Random.Range(0, 30); i++)
            yield return null;
        yield return new WaitForEndOfFrame();
        Setup();
    }
    private void Setup()
    {
        MeshFilter filter = GetComponent<MeshFilter>();

        Mesh mesh = MeshGenerator.GetMesh();
        mesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.CopyDestination;
        filter.sharedMesh = mesh;

        vertexBuffer = mesh.GetVertexBuffer(0);

        setupComplete = true;

        QueueRefresh();
    }

    private void QueueRefresh()
    {
        if (setupComplete)
            LayerGrassModifier.Instance.QueueRefresh(controller, vertexBuffer);
    }


    public static int VertexBufferStride => MeshGenerator.GetVertexBufferStride();
    public static int VertexNormalAttributeOffset => MeshGenerator.GetVertexNormalAttributeOffset();
    public static int VertexPositionAttributeOffset => MeshGenerator.GetVertexPositionAttributeOffset();
    public static int VertexUVAndHeightAttributeOffset => MeshGenerator.GetVertexUVAndHeightAttributeOffset();

    private static class MeshGenerator
    {
        private static Mesh prototype = null;
        private static GraphicsBuffer vertexReferenceBuffer = null;
        private static int vertexBufferStride;
        private static int vertexPositionAttributeOffset;
        private static int vertexNormalAttributeOffset;
        private static int vertexUVAndHeightAttributeOffset;

        public static int GetVertexBufferStride()
        {
            if (prototype == null)
                CreateMesh();

            return vertexBufferStride;
        }

        public static int GetVertexPositionAttributeOffset()
        {
            if (prototype == null)
                CreateMesh();

            return vertexPositionAttributeOffset;
        }

        public static int GetVertexNormalAttributeOffset()
        {
            if (prototype == null)
                CreateMesh();

            return vertexNormalAttributeOffset;
        }

        public static int GetVertexUVAndHeightAttributeOffset()
        {
            if (prototype == null)
                CreateMesh();

            return vertexUVAndHeightAttributeOffset;
        }

        public static Mesh GetMesh()
        {
            if (prototype == null)
                CreateMesh();

            return Instantiate(prototype);
        }

        public static GraphicsBuffer GetVertexReference()
        {
            if (prototype == null)
                CreateMesh();

            return vertexReferenceBuffer;
        }

        private static void CreateMesh()
        {
            int verticesPerRow = meshSize + 1;
            int verticesPerLayer = verticesPerRow * verticesPerRow;

            NativeArray<float3> vertices = new NativeArray<float3>(verticesPerLayer * numLayers, Allocator.Temp);
            NativeArray<float3> normals = new NativeArray<float3>(verticesPerLayer * numLayers, Allocator.Temp);
            NativeArray<float4> uvs = new NativeArray<float4>(verticesPerLayer * numLayers, Allocator.Temp);
            for (int z = 0; z < verticesPerRow; z++)
                for (int x = 0; x < verticesPerRow; x++)
                    for (int l = 0; l < numLayers; l++)
                    {
                        vertices[z * verticesPerRow * numLayers + x * numLayers + l] = new float3(x / (float)meshSize, 0, z / (float)meshSize);
                        normals [z * verticesPerRow * numLayers + x * numLayers + l] = new float3(0f, 1f, 0f);
                        uvs     [z * verticesPerRow * numLayers + x * numLayers + l] = float4.zero;
                    }

            int indicesPerLayer = 6 * meshSize * meshSize;
            NativeArray<int> indices = new NativeArray<int>(6 * meshSize * meshSize * numLayers, Allocator.Temp);
            for (int z = 0; z < meshSize; z++)
                for (int x = 0; x < meshSize; x++)
                    for (int l = 0; l < numLayers; l++)
                    {
                        indices[indicesPerLayer * l + (z * meshSize + x) * 6 + 0] = (z + 0) * verticesPerRow * numLayers + (x + 0) * numLayers + l;
                        indices[indicesPerLayer * l + (z * meshSize + x) * 6 + 1] = (z + 1) * verticesPerRow * numLayers + (x + 1) * numLayers + l;
                        indices[indicesPerLayer * l + (z * meshSize + x) * 6 + 2] = (z + 0) * verticesPerRow * numLayers + (x + 1) * numLayers + l;
                        indices[indicesPerLayer * l + (z * meshSize + x) * 6 + 3] = (z + 1) * verticesPerRow * numLayers + (x + 1) * numLayers + l;
                        indices[indicesPerLayer * l + (z * meshSize + x) * 6 + 4] = (z + 0) * verticesPerRow * numLayers + (x + 0) * numLayers + l;
                        indices[indicesPerLayer * l + (z * meshSize + x) * 6 + 5] = (z + 1) * verticesPerRow * numLayers + (x + 0) * numLayers + l;
                    }


            Mesh mesh = new Mesh();
            mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.CopySource;
            mesh.indexBufferTarget |= GraphicsBuffer.Target.Raw;
            mesh.SetVertexBufferParams(vertices.Length, new VertexAttributeDescriptor[] {
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0),
                new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, 0),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 4, 0)
            });
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetIndices(indices, MeshTopology.Triangles, 0);
            mesh.bounds = new Bounds(new Vector3(0.5f * area, 0f, 0.5f * area), new Vector3((1f + 1f / meshSize) * area, 200f, (1f + 1f / meshSize) * area));

            vertices.Dispose();
            normals.Dispose();
            indices.Dispose();

            prototype = mesh;
            vertexReferenceBuffer = mesh.GetVertexBuffer(0);
            vertexBufferStride = mesh.GetVertexBufferStride(0);
            vertexPositionAttributeOffset = mesh.GetVertexAttributeOffset(VertexAttribute.Position);
            vertexNormalAttributeOffset = mesh.GetVertexAttributeOffset(VertexAttribute.Normal);
            vertexUVAndHeightAttributeOffset = mesh.GetVertexAttributeOffset(VertexAttribute.TexCoord0);
        }

    }
}
