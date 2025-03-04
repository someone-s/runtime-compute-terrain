using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;


[RequireComponent(typeof(TerrainModifier), typeof(TerrainIntersector))]
public class TerrainCoordinator : MonoBehaviour
{
    [SerializeField] private GameObject chunkPrefab;

    private (int x, int z)[] renderedChunks;
    private (int x, int z)[] previousChunks;
    private int renderRange = 3;
    [SerializeField] internal float area = 50f;
    internal static int meshSize = 100;

    internal TerrainModifier modifier;
    internal TerrainIntersector intersector;
    internal Dictionary<(int x, int z), TerrainController> controllers = new();

    private void Start()
    {
        modifier = GetComponent<TerrainModifier>();
        intersector = GetComponent<TerrainIntersector>();
    }

    private TerrainController Generate(int x, int z)
    {
        GameObject chunkInstance = Instantiate(chunkPrefab);

        Transform chunkTransform = chunkInstance.transform;
        chunkTransform.parent = transform;
        chunkTransform.position = new Vector3(x * area, 0f, z * area);
        chunkTransform.localScale = new Vector3(area, 1f, area);

        TerrainController controller = chunkInstance.GetComponent<TerrainController>();
        controllers.Add((x, z), controller);

        return controller;
    }

    public void CastRay(Vector3 position, Vector3 direction, Action<Vector3?> callback)
    {

        Vector3 scaledPosition = position;
        scaledPosition.x /= area;
        scaledPosition.z /= area;

        // Minus 1 for setting center to bottom left of 4 modified terrain pieces
        int centerX = Mathf.RoundToInt(scaledPosition.x) - 1;
        int centerZ = Mathf.RoundToInt(scaledPosition.z) - 1;

        Vector3 scaledDirection = direction;
        scaledDirection.x /= area;
        scaledDirection.z /= area;

        // Ensure all terrain pieces exists
        for (int x = centerX; x <= centerX + 1; x++)
            for (int z = centerZ; z <= centerZ + 1; z++)
                if (!controllers.TryGetValue((x, z), out TerrainController controller))
                    controller = Generate(x, z);


        Vector3 translatedPosition = scaledPosition;
        translatedPosition.x -= centerX;
        translatedPosition.z -= centerZ;
        intersector.QueueIntersect((centerX, centerZ), translatedPosition, Vector3.Normalize(scaledDirection), (Vector3? hitPoint) =>
        {
            if (hitPoint is null)
                callback(null);
            else
            {
                Vector3 scaledPoint = hitPoint.Value;
                scaledPoint.x *= area;
                scaledPoint.z *= area;
                callback(scaledPoint);
            }
        });
    }

    public void UpdateVisual(Vector3 position)
    {
        Vector3 scaledPosition = position;
        scaledPosition.x /= area;
        scaledPosition.z /= area;

        int centerX = Mathf.RoundToInt(scaledPosition.x);
        int centerZ = Mathf.RoundToInt(scaledPosition.z);

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

    public void Save(string saveName)
    {
        // Create directory
        TerrainFiler.CreateSavePath(saveName);

        // Remove files not to be overwritten
        foreach (((int x, int z) grid, string path) entry in TerrainFiler.GetAllTerrain(saveName))
            if (!controllers.ContainsKey(entry.grid))
                File.Delete(entry.path);

        // Write new files
        foreach (KeyValuePair<(int x, int z), TerrainController> entry in controllers)
            entry.Value.Save(TerrainFiler.GetTerrainPath(saveName, entry.Key));

        AsyncGPUReadback.WaitAllRequests();

    }

    public void Load(string saveName)
    {
        // Get all files found
        ((int x, int z) grid, string path)[] entries = TerrainFiler.GetAllTerrain(saveName);

        // Reset all out of bound terrain
        foreach (KeyValuePair<(int x, int z), TerrainController> entry in controllers)
            if (!TerrainFiler.SavePathExist(saveName, entry.Key))
                entry.Value.Reset();

        // Load files
        foreach (((int x, int z) grid, string path) entry in entries)
        {           
            if (!controllers.TryGetValue(entry.grid, out TerrainController controller))
                controller = Generate(entry.grid.x, entry.grid.z);

            controller.Load(entry.path);  
        }
    }

    public void ModifyAdd(Vector3 position, float radius, float magnitude)
    {
        float delta = Time.deltaTime * magnitude;

        Modify(position, radius, TerrainModifier.OperationType.Add, delta);
    }

    public void ModifySubtract(Vector3 position, float radius, float magnitude)
    {
        float delta = Time.deltaTime * magnitude;

        Modify(position, radius, TerrainModifier.OperationType.Subtract, delta);
    }

    public void ModifyLevel(Vector3 position, float radius, float height)
    {
        Modify(position, radius, TerrainModifier.OperationType.Level, height);
    }

    public void ModifySmooth(Vector3 position, float radius)
    {
        Modify(position, radius, TerrainModifier.OperationType.Smooth, 0f);
    }

    private void Modify(Vector3 position, float radius, TerrainModifier.OperationType operation, float parameter)
    {
        Vector3 scaledPoint = position;
        scaledPoint.x /= area;
        scaledPoint.z /= area;

        int centerX = Mathf.RoundToInt(scaledPoint.x) - 1;
        int centerZ = Mathf.RoundToInt(scaledPoint.z) - 1;

        for (int x = centerX; x <= centerX + 1; x++)
            for (int z = centerZ; z <= centerZ + 1; z++)
                if (!controllers.TryGetValue((x, z), out TerrainController controller))
                    controller = Generate(x, z);

        float scaledRadius = radius / area;

        modifier.QueueModify((centerX, centerZ), new Vector2(scaledPoint.x - centerX, scaledPoint.z - centerZ), scaledRadius, parameter, operation);
    }

    public void Project(Vector3 minPosition, Vector3 maxPosition, bool limitToOne = false)
    {
        (int x, int z) minRegion = (Mathf.FloorToInt(minPosition.x / area), Mathf.FloorToInt(minPosition.z / area));
        (int x, int z) maxRegion = (Mathf.CeilToInt(maxPosition.x / area) - 1, Mathf.CeilToInt(maxPosition.z / area) - 1);

        modifier.QueueProject(minRegion);

        if (limitToOne)
            return;
        
        (int x, int z) deltaRegion = (maxRegion.x - minRegion.x, maxRegion.z - minRegion.z);
        deltaRegion = (deltaRegion.x < 1 ? 1 : deltaRegion.x, deltaRegion.z < 1 ? 1 : deltaRegion.z);

        for (int x = 0; x < deltaRegion.x; x++)
            for (int z = 0; z < deltaRegion.z; z++) 
                modifier.QueueProject((minRegion.x + x, minRegion.z + z));
    }
}
