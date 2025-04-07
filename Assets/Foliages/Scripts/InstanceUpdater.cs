using System.Collections.Generic;
using UnityEngine;

public class InstanceUpdater : MonoBehaviour
{
    private static InstanceUpdater instance = null;
    public static InstanceUpdater Instance {
        get {
            if (instance == null)
            {
                GameObject gameObject = new GameObject("Instance Updater", typeof(InstanceUpdater));
                instance = gameObject.GetComponent<InstanceUpdater>();
            }
            return instance;
        }
    }

    private HashSet<InstanceController> renderControllers = new();
    internal void AddRender(InstanceController controller)
    {
        renderControllers.Add(controller);
    }
    internal void RemoveRender(InstanceController controller)
    {
        renderControllers.Remove(controller);
    }
    private void Update()
    {
        foreach (var controller in renderControllers)
        {
            //Debug.Log(controller.transform.position);
            controller.RenderCommand();
        }
    }

    private HashSet<InstanceController> refreshControllers = new();

    internal void QueueRefresh(InstanceController controller)
    {
        refreshControllers.Add(controller);
    }

    private void LateUpdate()
    {
        foreach (var controller in refreshControllers)
            controller.ExecuteRefresh();

        refreshControllers.Clear();
    }
}