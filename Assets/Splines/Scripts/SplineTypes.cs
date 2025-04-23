using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "Spline Type Table", menuName = "Spline Custom/Type Table", order = 0)]
public class SplineTypeTable : ScriptableObject
{
    public GameObject[] typePrefabs;
    private Dictionary<string, GameObject> lookupPrefabs;
    private void GenerateLookup()
    {
        lookupPrefabs = new(typePrefabs.Length);
        if (typePrefabs != null)
            foreach (var typePrefab in typePrefabs)
                lookupPrefabs.Add(typePrefab.name, typePrefab);
    }
    private Dictionary<string, GameObject> LookupPrefabs
    {
        get
        {
            if (lookupPrefabs == null)
                GenerateLookup();

            return lookupPrefabs;
        }
    }

    public GameObject GetSplineType(string typeName)
    {
        if (LookupPrefabs.TryGetValue(typeName, out GameObject prefab))
            return prefab;

        // update lookup incase outdated
        // the array is not changed in built
        // so only happends in editor
    #if UNITY_EDITOR
        GenerateLookup();
        if (LookupPrefabs.TryGetValue(typeName, out prefab))
            return prefab;
    #endif

        return null;
    }
}