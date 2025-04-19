using System;
using System.IO;
using System.Linq;
using UnityEngine;

public static class TerrainFiler
{
    private const string subFolder = "Terrain";
    private static string ToFileName(this (int x, int z) grid) => $"{grid.x}_{grid.z}";
    private static string ToDirectory(this string saveRoot) => Path.Combine(saveRoot, subFolder);

    public static ((int x, int z) grid, string path)[] GetAllTerrain(string saveRoot)
    {
        string directory = saveRoot.ToDirectory();

        if (!Directory.Exists(directory))
            return null;

        string[] paths = Directory.GetFiles(directory);

        return paths
            .Select((path) =>
            {
                string[] segment = Path.GetFileNameWithoutExtension(path).Split('_');

                (bool valid, int x, int z, string path) result;

                if (segment.Length != 2)
                    result = (false, 0, 0, "");
                else if (!int.TryParse(segment[0], out int x))
                    result = (false, 0, 0, "");
                else if (!int.TryParse(segment[1], out int z))
                    result = (false, 0, 0, "");
                else
                    result = (true, x, z, path);

                return result;
            })
            .Where((entry) => entry.valid)
            .Select((entry) => ((entry.x, entry.z), entry.path))
            .ToArray();
    }

    public static void CreateSavePath(string saveRoot) 
    {
        Directory.CreateDirectory(saveRoot.ToDirectory());
    }

    public static bool SavePathExist(string saveRoot)
    {
        return Directory.Exists(saveRoot.ToDirectory());
    }

    public static bool SavePathExist(string saveRoot, (int x, int z) grid)
    {
        if (!SavePathExist(saveRoot))
            return false;

        string fileName = Path.Combine(saveRoot.ToDirectory(), grid.ToFileName());

        return File.Exists(fileName);
    }

    public static string GetTerrainPath(string saveRoot, (int x, int z) grid)
    {
       return Path.Combine(saveRoot.ToDirectory(), grid.ToFileName());
    }
}