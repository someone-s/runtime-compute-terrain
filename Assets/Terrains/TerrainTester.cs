using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(TerrainCoordinator))]
public class TerrainTester : MonoBehaviour {

    public Transform testPlayer;
    public Transform testDisplay;
    public InputAction actionA;
    public InputAction actionB;
    public InputAction actionC;
    public InputAction actionD;
    public InputAction actionE;
    public InputAction actionF;
    public string saveName = "save 2";

    private TerrainCoordinator coordinator;

    private void Start()
    {
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

    private Vector3 trail = Vector3.zero;

    private void Update()
    {
        Vector3 direction = testDisplay.position - trail;

        coordinator.CastRay(testPlayer.position, testPlayer.rotation, (Vector3? result) => {
            if (result is null)
                return;
            trail = testDisplay.position;
            testDisplay.position = result.Value;
        });

        coordinator.UpdateVisual(testPlayer.position);

        if (actionA.IsPressed())
            coordinator.ModifyAdd(testDisplay.position, 10f, 5f);
        if (actionB.IsPressed())
            coordinator.ModifySubtract(testDisplay.position, 10f, 5f);
        if (actionC.IsPressed())
            coordinator.ModifyLevel(testDisplay.position, 10f, 5f);
        if (actionD.IsPressed())
            coordinator.ModifySmooth(testDisplay.position, 10f);
        
    }
}