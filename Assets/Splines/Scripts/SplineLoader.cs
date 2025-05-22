using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using UnityEngine;
using GltfImport = GLTFast.Newtonsoft.GltfImport;
using System.Collections;

public static class SplineLoader
{
    private static Dictionary<string, SplineProfile> loadedObjects = new();
    private static Dictionary<string, Task<SplineProfile?>> loadingObjects = new();

    public static IEnumerator GetCoroutine(SplineDescription description, Action<SplineProfile?> callback, bool reload = false)
    {
        Task<SplineProfile?> loadTask = Get(description, reload);
        yield return new WaitUntil(() => loadTask.IsCompleted || loadTask.IsCompleted);
        
        if (!loadTask.IsCompletedSuccessfully) yield break;
        
        callback?.Invoke(loadTask.Result);
    }
    public static async Task<SplineProfile?> Get(SplineDescription description, bool reload = false)
    {
        string filePath = Path.Join(
            description.isInternal ? 
                Application.streamingAssetsPath : 
                FileCoordinator.Instance.ResourceRoot, 
            "Splines", 
            $"{description.name}.glb");
        
        return await Get(filePath, reload);
    }
    public static async Task<SplineProfile?> Get(string filePath, bool reload = false)
    {
        // Check loading first in case reloading
        if (loadingObjects.TryGetValue(filePath, out Task<SplineProfile?> loadingObject))
            return await loadingObject;

        // No reload, item exist
        if (!reload && loadedObjects.TryGetValue(filePath, out SplineProfile loadedObject))
            return loadedObject;

        // reload or item not exist
        Task<SplineProfile?> loadTask = Load(filePath);
        loadingObjects.Add(filePath, loadTask);

        return await loadTask;
    }

    private static async Task<SplineProfile?> Load(string filePath)
    {
        var gltfDataAsByteArray = await File.ReadAllBytesAsync(filePath);
        var gltf = new GltfImport();
        var success = await gltf.Load(gltfDataAsByteArray);

        if (!success) return null;

        var gltfMeshIndex = gltf.GetSourceNode().mesh;
        var gltfMesh = gltf.GetSourceMesh(gltfMeshIndex);

        var gltfPrimitives = gltfMesh.Primitives;
        var materialList = new List<Material>(gltfPrimitives.Count);
        foreach (var gltfPrimitive in gltfPrimitives)
            materialList.Add(gltf.GetMaterial(gltfPrimitive.material));

        var meshSplitCount = gltf.GetMeshCount(gltfMeshIndex);
        if (meshSplitCount > 1)
            Debug.Log($"Loader: Only first submesh will be used ({filePath})");
        else if (meshSplitCount <= 0)
        {
            Debug.Log($"Loader: No mesh loaded ({filePath})");
            return null;
        }
        var mesh = gltf.GetMesh(gltfMeshIndex, 0);

        var metaData = (gltf.GetSourceNode() as GLTFast.Newtonsoft.Schema.Node)?.extras;

        SplineProfile loadedObject;
        if (metaData != null)
        {
            SplineProfile.ContinousSettings? continousSettings = null;
            if (metaData.TryGetValue("continous", out bool continous) && continous)
            {
                continousSettings = new SplineProfile.ContinousSettings(
                    mapping: metaData.TryGetValue("continousMapping", out int[] customMapping) ? customMapping : null,
                    stretch: metaData.TryGetValue("continousStretch", out float uvStretch)     ? uvStretch     : 1f
                );
            }

            SplineProfile.CastSettings? castSettings = null;
            if (metaData.TryGetValue("cast", out bool cast) && cast)
            {
                castSettings = new SplineProfile.CastSettings(
                    origin:    metaData.TryGetValue("castOrigin",    out float[] origin)    ? new(origin[0],    origin[1],    origin[2])    : Vector3.zero,
                    direction: metaData.TryGetValue("castDirection", out float[] direction) ? new(direction[0], direction[1], direction[2]) : Vector3.up,
                    maxOffset: metaData.TryGetValue("castMaxOffset", out float maxOffset)   ? maxOffset : 1f
                );
            }

            loadedObject = new SplineProfile(
                mesh:          mesh,
                materials:     materialList,
                spacing:       metaData.TryGetValue("spacing",          out float spacing)       ? spacing       : 1f,
                vertical:      metaData.TryGetValue("vertical",         out bool vertical)       ? vertical      : false,
                maxPointCount: metaData.TryGetValue("maxPointCount",    out int maxPointCount)   ? maxPointCount : 64,
                extends:       metaData.TryGetValue("extends",          out float extends)       ? extends       : 0f,
                continous:     continousSettings,
                cast:          castSettings
            );
        }
        else
            loadedObject = new SplineProfile(mesh, materialList);

        loadedObjects.Add(filePath, loadedObject);
        return loadedObject;
    }
}