using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class SplineCoordinator : MonoBehaviour
{
    [SerializeField] private SplineDescription[] descriptions;
    [SerializeField] private SplineTypeTable typesTable;

    private List<SplineController> controllers;

    private void Start() => Setup();
    
    public void Setup(SplineDescription[] splineEntriesToUse)
    {
        descriptions = splineEntriesToUse;
        Setup();
    }
    private void Setup()
    {
        controllers = new(descriptions.Length);

        for (int i = 0; i < descriptions.Length; i++)
        {
            SplineDescription description = descriptions[i];

            GameObject instance = Instantiate(typesTable.GetSplineType(description.type), transform);
            if (instance == null) continue;

            SplineController controller = instance.GetComponent<SplineController>();
            controllers.Add(controller);

            controller.QueueSetup(this, descriptions[i]);
        }
    }


    private Vector3 aLastPos, bLastPos;
    private Quaternion aLastRot, bLastRot;
    public Transform a, b;
    private void Update()
    {
        a.GetPositionAndRotation(out Vector3 aPos, out Quaternion aRot);
        b.GetPositionAndRotation(out Vector3 bPos, out Quaternion bRot);
        if (aPos != aLastPos || bPos != bLastPos || aRot != aLastRot || bRot != bLastRot) {
            aLastPos = aPos;
            bLastPos = bPos;
            aLastRot = aRot;
            bLastRot = bRot;
            if (controllers != null)
                foreach (var controller in controllers)
                    controller.QueueRefresh(a.position, a.rotation, b.position, b.rotation);
        }

        
    }
}