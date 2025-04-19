using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

public class SaveCoordinator : MonoBehaviour
{
    public List<UnityEvent<string>> OnSaveOrdered;
    public List<UnityEvent<string>> OnLoadOrdered;
    
    public string folderName = "Testing";
    public string saveName = "temp";
    private string SavePath => Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), folderName, saveName);

    public void Save(string name)
    {
        saveName = name;
        Save();
    }
    public void Save(InputAction.CallbackContext context)
    {
        if (!context.performed) return;
        Save();
    }
    public void Save()
    {
        foreach (var e in OnSaveOrdered)
            e.Invoke(SavePath);
    }

    public void Load(string name)
    {
        saveName = name;
        Load();
    }
    public void Load(InputAction.CallbackContext context)
    {
        if (!context.performed) return;
        Load();
    }

    public void Load()
    {
        foreach (var e in OnLoadOrdered)
            e.Invoke(SavePath);
    }
}
