using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;


[RequireComponent(typeof(TerrainModifier), typeof(TerrainIntersector))]
public class TerrainCoordinator : MonoBehaviour
{
    private static TerrainCoordinator instance = null;
    public static TerrainCoordinator Instance
    {
        get {
            if (instance == null)
                instance = FindFirstObjectByType<TerrainCoordinator>();

            return instance;
        }
    }

    public const float area = 50f;
    public const int meshSize = 126;

    internal TerrainModifier modifier;
    internal TerrainIntersector intersector;
    internal Dictionary<(int x, int z), TerrainController> controllers = new();

    private void Start()
    {
        modifier = GetComponent<TerrainModifier>();
        intersector = GetComponent<TerrainIntersector>();
    }

    #region Creation Section
    private TerrainController Generate(int x, int z)
    {
        GameObject chunkInstance = Instantiate(chunkPrefab);

        Transform chunkTransform = chunkInstance.transform;
        chunkTransform.parent = transform;
        chunkTransform.position = new Vector3(x * area, 0f, z * area);

        TerrainController controller = chunkInstance.GetComponent<TerrainController>();
        controller.Setup();
        controllers.Add((x, z), controller);

        return controller;
    }
    #endregion

    #region Intersect Section
    public void CastRay(Vector3 position, Vector3 direction, float range, bool useBase, Action<Vector3?> callback)
    {
        intersector.QueueIntersect(position, direction, range, useBase, (Vector3? hitPoint) =>
        {
            if (hitPoint is null)
                callback(null);
            else
                callback(hitPoint.Value);
        });
    }
    #endregion

    #region Culling Section
    [SerializeField] private GameObject chunkPrefab;
    [SerializeField] private float lod1Distance = 135f;
    [SerializeField] private float lod2Distance = 225f;

    private List<(int x, int z)> renderedChunks = new();
    private List<(int x, int z)> previousChunks = new();
    [SerializeField] private int renderRange = 3;

    public void UpdateVisual(Vector3 position, Plane[] frsutumPlanes)
    {
        int centerX = Mathf.RoundToInt(position.x / area);
        int centerZ = Mathf.RoundToInt(position.z / area);

        Bounds localBounds = TerrainController.LocalBounds;

        renderedChunks.Clear();

        for (int x = 0; x < 2 * renderRange + 1; x++)
            for (int z = 0; z < 2 * renderRange + 1; z++)
            {
                int xRegion = x + centerX - renderRange;
                int zRegion = z + centerZ - renderRange;

                if (GeometryUtility.TestPlanesAABB(frsutumPlanes, 
                new Bounds(localBounds.center + new Vector3(xRegion * area, 0f, zRegion * area), localBounds.size)))
                    renderedChunks.Add((xRegion, zRegion));
            }

        foreach ((int x, int z) chunk in previousChunks.Except(renderedChunks))
            controllers[chunk].SetHidden();

        foreach ((int x, int z) chunk in renderedChunks)
        {
            if (!controllers.TryGetValue(chunk, out TerrainController controller))
                controller = Generate(chunk.x, chunk.z);

            int lodLevel = controller.lodLevel;    
            float distance = Vector3.Distance(controller.worldBound.center, position);
            bool lodChanged;

            if (distance < lod1Distance)
            {
                lodChanged = lodLevel != 0;
                lodLevel = 0;
            }
            else if (distance < lod2Distance)
            {
                lodChanged = lodLevel != 1;
                lodLevel = 1;
            }
            else
            {
                lodChanged = lodLevel != 2;
                lodLevel = 2;
            }

            if (previousChunks.Contains(chunk))
            {
                if (lodChanged)
                    controller.SetLod(lodLevel);
            }
            else
            {
                controller.SetVisible(lodLevel);
            }
        }

        // Swap without reallocate
        List<(int x, int y)> temp = previousChunks;
        previousChunks = renderedChunks;
        renderedChunks = temp;
    }
    #endregion

    #region IO Section
    public void Save(string saveRoot)
    {
        // Create directory
        TerrainFiler.CreateSavePath(saveRoot);

        // Remove files not to be overwritten
        foreach (((int x, int z) grid, string path) entry in TerrainFiler.GetAllTerrain(saveRoot))
            if (!controllers.ContainsKey(entry.grid))
                File.Delete(entry.path);

        // Write new files
        foreach (KeyValuePair<(int x, int z), TerrainController> entry in controllers)
            entry.Value.Save(TerrainFiler.GetTerrainPath(saveRoot, entry.Key));

        AsyncGPUReadback.WaitAllRequests();

    }

    public void Load(string saveRoot)
    {
        // Get all files found
        ((int x, int z) grid, string path)[] entries = TerrainFiler.GetAllTerrain(saveRoot);

        // Reset all out of bound terrain
        foreach (KeyValuePair<(int x, int z), TerrainController> entry in controllers)
            if (!TerrainFiler.SavePathExist(saveRoot, entry.Key))
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
    #endregion

    #region Modify Region
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
        int centerX = Mathf.RoundToInt(position.x / area) - 1;
        int centerZ = Mathf.RoundToInt(position.z / area) - 1;

        for (int x = centerX; x <= centerX + 1; x++)
            for (int z = centerZ; z <= centerZ + 1; z++)
                if (!controllers.TryGetValue((x, z), out TerrainController controller))
                    controller = Generate(x, z);

        modifier.QueueModify((centerX, centerZ), new Vector2(position.x - centerX * area, position.z - centerZ * area), radius, parameter, operation);
    }
    #endregion

    #region Project Region
    internal void AddProjector(Bounds worldBounds, TerrainProjector projector)
    {
        (int x, int z) minRegion = (Mathf.FloorToInt(worldBounds.min.x / area), Mathf.FloorToInt(worldBounds.min.z / area));
        (int x, int z) maxRegion = (Mathf.CeilToInt(worldBounds.max.x / area), Mathf.CeilToInt(worldBounds.max.z / area));

        for (int x = minRegion.x; x <= Mathf.Max(minRegion.x, maxRegion.x); x++)
            for (int z = minRegion.z; z <= Mathf.Max(minRegion.z, maxRegion.z); z++) 
            {
                if (!controllers.TryGetValue((x, z), out TerrainController controller))
                    controller = Generate(x, z);

                controller.projectors.Add(projector);
            }

        Project(minRegion, maxRegion);
    }

    internal void UpdateProjector(Bounds oldWorldBounds, Bounds newWorldBounds, TerrainProjector projector)
    {
        (int x, int z) oldMinRegion = (Mathf.FloorToInt(oldWorldBounds.min.x / area), Mathf.FloorToInt(oldWorldBounds.min.z / area));
        (int x, int z) oldMaxRegion = (Mathf.CeilToInt(oldWorldBounds.max.x / area), Mathf.CeilToInt(oldWorldBounds.max.z / area));

        (int x, int z) newMinRegion = (Mathf.FloorToInt(newWorldBounds.min.x / area), Mathf.FloorToInt(newWorldBounds.min.z / area));
        (int x, int z) newMaxRegion = (Mathf.CeilToInt(newWorldBounds.max.x / area), Mathf.CeilToInt(newWorldBounds.max.z / area));

        (int x, int z) combinedMinRegion = (Mathf.Min(oldMinRegion.x, newMinRegion.x), Mathf.Min(oldMinRegion.z, newMinRegion.z));
        (int x, int z) combinedMaxRegion = (Mathf.Max(oldMaxRegion.x, newMaxRegion.x), Mathf.Max(oldMaxRegion.z, newMaxRegion.z));

        for (int x = combinedMinRegion.x; x <= combinedMaxRegion.x; x++)
            for (int z = combinedMinRegion.z; z <= combinedMaxRegion.z; z++) 
            {
                bool inOldGrid = x >= oldMinRegion.x && x <= oldMaxRegion.x &&
                                 z >= oldMinRegion.z && z <= oldMaxRegion.z;

                bool inNewGrid = x >= newMinRegion.x && x <= newMaxRegion.x &&
                                 z >= newMinRegion.z && z <= newMaxRegion.z;

                bool removeCase = inOldGrid && !inNewGrid;
                bool addCase = !inOldGrid && inNewGrid;

                if (removeCase || addCase)
                {
                    if (!controllers.TryGetValue((x, z), out TerrainController controller))
                        controller = Generate(x, z);

                    if (removeCase)
                        controller.projectors.Remove(projector);
                    else if (addCase)
                        controller.projectors.Add(projector);
                }
            }

        Project(combinedMinRegion, combinedMaxRegion);
    }

    internal void RemoveProjector(Bounds worldBounds, TerrainProjector projector)
    {
        (int x, int z) minRegion = (Mathf.FloorToInt(worldBounds.min.x / area), Mathf.FloorToInt(worldBounds.min.z / area));
        (int x, int z) maxRegion = (Mathf.CeilToInt(worldBounds.max.x / area), Mathf.CeilToInt(worldBounds.max.z / area));

        for (int x = minRegion.x; x <= Mathf.Max(minRegion.x, maxRegion.x); x++)
            for (int z = minRegion.z; z <= Mathf.Max(minRegion.z, maxRegion.z); z++) 
            {
                if (!controllers.TryGetValue((x, z), out TerrainController controller))
                    controller = Generate(x, z);

                controller.projectors.Remove(projector);
            }

        Project(minRegion, maxRegion);
    }

    private void Project((int x, int z) minRegion, (int x, int z) maxRegion)
    {   
        for (int x = minRegion.x; x <= Mathf.Max(minRegion.x, maxRegion.x - 1); x++)
            for (int z = minRegion.z; z <= Mathf.Max(minRegion.z, maxRegion.z - 1); z++) 
            {
                if (!controllers.ContainsKey((x, z)))
                    Generate(x, z);
                if (!controllers.ContainsKey((x+1, z)))
                    Generate(x+1, z);
                if (!controllers.ContainsKey((x+1, z+1)))
                    Generate(x+1, z+1);
                if (!controllers.ContainsKey((x, z+1)))
                    Generate(x, z+1);
                modifier?.QueueProject((x, z));
            }
    }
    #endregion
}
