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
    [SerializeField] private int renderRange = 3;
    public static float area { get; private set; } = 50f;
    public static int meshSize { get; private set; } = 126;

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
        //chunkTransform.localScale = new Vector3(, 1f, area);

        TerrainController controller = chunkInstance.GetComponent<TerrainController>();
        controllers.Add((x, z), controller);

        return controller;
    }

    public void CastRay(Vector3 position, Vector3 direction, Action<Vector3?> callback)
    {
        //Vector3 scaledPosition = position;
        //scaledPosition.x /= area;
        //scaledPosition.z /= area;

        // Minus 1 for setting center to bottom left of 4 modified terrain pieces
        int centerX = Mathf.RoundToInt(position.x / area) - 1;
        int centerZ = Mathf.RoundToInt(position.z / area) - 1;

        // Vector3 scaledDirection = direction;
        // scaledDirection.x /= area;
        // scaledDirection.z /= area;

        // Ensure all terrain pieces exists
        for (int x = centerX; x <= centerX + 1; x++)
            for (int z = centerZ; z <= centerZ + 1; z++)
                if (!controllers.TryGetValue((x, z), out TerrainController controller))
                    controller = Generate(x, z);


        Vector3 translatedPosition = position;
        translatedPosition.x -= centerX * area;
        translatedPosition.z -= centerZ * area;
        intersector.QueueIntersect((centerX, centerZ), translatedPosition, direction, (Vector3? hitPoint) =>
        {
            if (hitPoint is null)
                callback(null);
            else
            {
                //Vector3 scaledPoint = hitPoint.Value;
                //scaledPoint.x *= area;
                //scaledPoint.z *= area;
                callback(hitPoint.Value);
            }
        });
    }

    public void UpdateVisual(Vector3 position)
    {
        //Vector3 scaledPosition = position;
        //scaledPosition.x /= area;
        //scaledPosition.z /= area;

        int centerX = Mathf.RoundToInt(position.x / area);
        int centerZ = Mathf.RoundToInt(position.z / area);

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

        if (entries.Length < 1)
            return;

        (int x, int z) minGrid = entries[0].grid;
        (int x, int z) maxGrid = entries[0].grid;
        
        // Load files
        foreach (((int x, int z) grid, string path) entry in entries)
        {           
            if (entry.grid.x < minGrid.x)
                minGrid.x = entry.grid.x;
            if (entry.grid.z < minGrid.z)
                minGrid.z = entry.grid.z;

            if (entry.grid.x > maxGrid.x)
                maxGrid.x = entry.grid.x;
            if (entry.grid.z > maxGrid.z)
                maxGrid.z = entry.grid.z;

            if (!controllers.TryGetValue(entry.grid, out TerrainController controller))
                controller = Generate(entry.grid.x, entry.grid.z);

            controller.Load(entry.path);  
        }

        for (int x = minGrid.x; x < Mathf.Max(1 + minGrid.x, maxGrid.x); x++)
            for (int z = minGrid.z; z < Mathf.Max(1 + minGrid.z, maxGrid.z); z++) 
            {
                if (!controllers.ContainsKey((x, z)))
                    Generate(x, z);
                if (!controllers.ContainsKey((x+1, z)))
                    Generate(x+1, z);
                if (!controllers.ContainsKey((x+1, z+1)))
                    Generate(x+1, z+1);
                if (!controllers.ContainsKey((x, z+1)))
                    Generate(x, z+1);

                modifier.QueueProject((x, z));
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
        //Vector3 scaledPoint = position;
        //scaledPoint.x /= area;
        //scaledPoint.z /= area;

        int centerX = Mathf.RoundToInt(position.x / area) - 1;
        int centerZ = Mathf.RoundToInt(position.z / area) - 1;

        for (int x = centerX; x <= centerX + 1; x++)
            for (int z = centerZ; z <= centerZ + 1; z++)
                if (!controllers.TryGetValue((x, z), out TerrainController controller))
                    controller = Generate(x, z);

        //float scaledRadius = radius / area;

        modifier.QueueModify((centerX, centerZ), new Vector2(position.x - centerX * area, position.z - centerZ * area), radius, parameter, operation);
    }

    public void Project(Vector3 minPosition, Vector3 maxPosition, bool limitToOne = false)
    {
        (int x, int z) minRegion = (Mathf.FloorToInt(minPosition.x / area), Mathf.FloorToInt(minPosition.z / area));
        (int x, int z) maxRegion = (Mathf.CeilToInt(maxPosition.x / area) - 1, Mathf.CeilToInt(maxPosition.z / area) - 1);

        modifier.QueueProject(minRegion);

        if (limitToOne)
            return;
        
        for (int x = minRegion.x; x < Mathf.Max(1 + minRegion.x, maxRegion.x); x++)
            for (int z = minRegion.z; z < Mathf.Max(1 + minRegion.z, maxRegion.z); z++) 
            {
                if (!controllers.ContainsKey((x, z)))
                    Generate(x, z);
                if (!controllers.ContainsKey((x+1, z)))
                    Generate(x+1, z);
                if (!controllers.ContainsKey((x+1, z+1)))
                    Generate(x+1, z+1);
                if (!controllers.ContainsKey((x, z+1)))
                    Generate(x, z+1);
                modifier.QueueProject((x, z));
            }
    }
}
