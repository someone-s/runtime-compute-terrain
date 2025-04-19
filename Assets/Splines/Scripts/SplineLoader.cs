using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using UnityEngine;
using GltfImport = GLTFast.Newtonsoft.GltfImport;

public static class SplineLoader
{
    private static Dictionary<string, SplineProfile> loadedObjects = new();
    private static Dictionary<string, Task<SplineProfile?>> loadingObjects = new();

    private static async Task<SplineProfile?> Load(string filePath)
    {
        var gltfDataAsByteArray = await File.ReadAllBytesAsync(filePath);
        var gltf = new GltfImport();
        var success = await gltf.Load(
            gltfDataAsByteArray,
            new Uri(filePath)  // The URI of the original data is important for resolving relative URIs within the glTF
            );

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

            loadedObject = new SplineProfile(
                mesh:          mesh,
                materials:     materialList,
                spacing:       metaData.TryGetValue("spacing",          out float spacing)       ? spacing       : 1f,
                vertical:      metaData.TryGetValue("vertical",         out bool vertical)       ? vertical      : false,
                maxPointCount: metaData.TryGetValue("maxPointCount",    out int maxPointCount)   ? maxPointCount : 64,
               continous:      continousSettings
            );
        }
        else
            loadedObject = new SplineProfile(mesh, materialList);

        loadedObjects.Add(filePath, loadedObject);
        return loadedObject;
    }

    public static async Task<SplineProfile?> Get(string filePath, bool reload = false, Action<SplineProfile?> callback = null)
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
}