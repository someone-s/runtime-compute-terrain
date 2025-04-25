using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

public class FileCoordinator : MonoBehaviour
{
    private static FileCoordinator instance;
    public static FileCoordinator Instance
    {
        get {
            if (instance == null)
                instance = FindFirstObjectByType<FileCoordinator>();
            return instance;
        }
    }

    public string gameFolderName = "Testing";

    #region Save Section
    [Header("Save Section")]
    public List<UnityEvent<string>> OnSaveOrdered;
    public List<UnityEvent<string>> OnLoadOrdered;
    public string SaveRoot => Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), gameFolderName, "Saves");
    public string currentSaveName = "temp";

    public void Save(InputAction.CallbackContext context)
    {
        if (!context.performed) return;
        Save(currentSaveName);
    }
    public void Save(string name)
    {
        currentSaveName = name;
        foreach (var e in OnSaveOrdered)
            e.Invoke(Path.Join(SaveRoot, currentSaveName));
    }
    public void Load(InputAction.CallbackContext context)
    {
        if (!context.performed) return;
        Load(currentSaveName);
    }
    public void Load(string name)
    {
        currentSaveName = name;
        foreach (var e in OnLoadOrdered)
            e.Invoke(Path.Join(SaveRoot, currentSaveName));
    }
    #endregion


    #region Resource Section
    [Header("Resource Section")]
    public string ResourceRoot => Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), gameFolderName, "Resources");
    #endregion
}
