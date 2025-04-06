using Unity.IntegerTime;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(TerrainCoordinator))]
public class TerrainTester : MonoBehaviour {

    public Transform testDisplay;
    public InputAction actionA;
    public InputAction actionB;
    public InputAction actionC;
    public InputAction actionD;
    public InputAction actionE;
    public InputAction actionF;
    public string saveName = "save 2";
    public float degree = 90f;

    private TerrainCoordinator coordinator;
    private Camera mainCamera;

    private void Start()
    {
        mainCamera = Camera.main;
        coordinator = GetComponent<TerrainCoordinator>();

        actionA.Enable();
        actionB.Enable();
        actionC.Enable();
        actionD.Enable();
        actionE.Enable();
        actionE.performed += (_) => {
            coordinator.Save(saveName);
        };
        actionF.Enable();
        actionF.performed += (_) => {
            coordinator.Load(saveName);
        };
    }

    private Vector3 target = Vector3.zero;

    private void Update()
    {
        Ray ray = mainCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
        coordinator.CastRay(mainCamera.transform.position, ray.direction, (Vector3? result) => {
            if (result is null)
                return;
            target = result.Value;
        });

        coordinator.UpdateVisual(
            mainCamera.transform.position, 
            GeometryUtility.CalculateFrustumPlanes(mainCamera));

        if (actionA.IsPressed())
            coordinator.ModifyAdd(testDisplay.position, 10f, 5f);
        if (actionB.IsPressed())
            coordinator.ModifySubtract(testDisplay.position, 10f, 5f);
        if (actionC.IsPressed())
            coordinator.ModifyLevel(testDisplay.position, 10f, 5f);
        if (actionD.IsPressed())
            coordinator.ModifySmooth(testDisplay.position, 10f);
        
    }

    private void LateUpdate()
    {
        float distance = Vector3.Distance(testDisplay.position, target);
        testDisplay.position = Vector3.MoveTowards(testDisplay.position, target, distance * distance / 4f);
    }
}