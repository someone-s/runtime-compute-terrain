using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

public class TerrainCoordinator : MonoBehaviour
{
    public Transform testPlayer;
    public Transform testDisplay;

    [SerializeField] private GameObject chunkPrefab;

    private (int x, int z)[] renderedChunks;
    private (int x, int z)[] previousChunks;
    private int renderRange = 3;
    private int liveRange = 1;
    private float area = 50f;

    private Dictionary<(int x, int z), TerrainController> controllers = new();

    private TerrainController Generate(int x, int z)
    {
        GameObject chunkInstance = Instantiate(chunkPrefab);

        Transform chunkTransform = chunkInstance.transform;
        chunkTransform.parent = transform;
        chunkTransform.position = new Vector3(x * area, 0f, z * area);
        chunkTransform.localScale = new Vector3(area, 1f, area);

        TerrainController controller = chunkInstance.GetComponent<TerrainController>();
        controller.SetCallbackIndex(x, z, OnIntersectResult);
        controllers.Add((x, z), controller);

        return controller;
    }

    private void LateUpdate()
    {

        Vector3 scaledPosition = testPlayer.position;
        scaledPosition.x /= area;
        scaledPosition.z /= area;

        int centerX = Mathf.FloorToInt(scaledPosition.x);
        int centerZ = Mathf.FloorToInt(scaledPosition.z);

        UpdateCast(scaledPosition, centerX, centerZ);

        UpdateVisual(centerX, centerZ);
    }

    private void UpdateCast(Vector3 scaledPosition, int centerX, int centerZ)
    {

        Vector3 scaledDirection = testPlayer.rotation * Vector3.forward;
        scaledDirection.x /= area;
        scaledDirection.z /= area;

        for (int x = centerX - liveRange; x <= centerX + liveRange; x++)
            for (int z = centerZ - liveRange; z <= centerZ + liveRange; z++)
            {
                Vector3 translatedPosition = scaledPosition;
                translatedPosition.x -= x;
                translatedPosition.z -= z;
                if (!controllers.TryGetValue((x, z), out TerrainController controller))
                    controller = Generate(x, z);
                controller.QueueIntersect(translatedPosition, Vector3.Normalize(scaledDirection));
            }
    }

    private void UpdateVisual(int centerX, int centerZ)
    {

        bool setup = previousChunks == null;

        if (setup)
        {
            previousChunks = new (int x, int z)[(2 * renderRange + 1) * (2 * renderRange + 1)];
            renderedChunks = new (int x, int z)[(2 * renderRange + 1) * (2 * renderRange + 1)];
        }

        for (int x = 0; x < 2 * renderRange + 1; x++)
            for (int z = 0; z < 2 * renderRange + 1; z++)
                renderedChunks[z * (2 * renderRange + 1) + x] = (x + centerX - renderRange, z + centerZ - renderRange);

        if (setup)
        {
            foreach ((int x, int z) chunk in renderedChunks)
            {

                if (!controllers.TryGetValue(chunk, out TerrainController controller))
                    controller = Generate(chunk.x, chunk.z);

                controller.SetVisible(true);
            }
        }
        else
        {
            foreach ((int x, int z) chunk in previousChunks.Except(renderedChunks))
                controllers[chunk].SetVisible(false);

            foreach ((int x, int z) chunk in renderedChunks.Except(previousChunks))
            {
                if (!controllers.TryGetValue(chunk, out TerrainController controller))
                    controller = Generate(chunk.x, chunk.z);

                controller.SetVisible(true);
            }
        }

        // Swap without reallocate
        (int x, int y)[] temp = previousChunks;
        previousChunks = renderedChunks;
        renderedChunks = temp;
    }

    private void Test()
    {
        controllers[(0, 0)].QueueModify(new Vector2(0f, 0f), 0.5f, 10f, TerrainController.OperationType.Add);
        controllers[(-1, 0)].QueueModify(new Vector2(1f, 0f), 0.5f, 10f, TerrainController.OperationType.Add);
        controllers[(-1, -1)].QueueModify(new Vector2(1f, 1f), 0.5f, 10f, TerrainController.OperationType.Add);
        controllers[(0, -1)].QueueModify(new Vector2(0f, 1f), 0.5f, 10f, TerrainController.OperationType.Add);

        controllers[(1, 1)].QueueModify(new Vector2(0f, 0f), 0.5f, 10f, TerrainController.OperationType.Add);
        controllers[(0, 1)].QueueModify(new Vector2(1f, 0f), 0.5f, 10f, TerrainController.OperationType.Add);
        controllers[(0, 0)].QueueModify(new Vector2(1f, 1f), 0.5f, 10f, TerrainController.OperationType.Add);
        controllers[(1, 0)].QueueModify(new Vector2(0f, 1f), 0.5f, 10f, TerrainController.OperationType.Add);

    }

    private void OnIntersectResult(Vector3 intersectPoint)
    {
        intersectPoint.x *= area;
        intersectPoint.z *= area;
        testDisplay.position = intersectPoint;
    }
}
