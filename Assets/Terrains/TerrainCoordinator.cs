using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class TerrainCoordinator : MonoBehaviour
{
    public Transform testPlayer;
    public Transform testDisplay;
    public InputAction action;


    [SerializeField] private GameObject chunkPrefab;

    private static int extent = 2;
    private static float area = 50f;

    private Dictionary<(int, int), TerrainController> controllers = new();

    private void Start()
    {
        for (int x = -extent; x < extent; x++)
            for (int y = -extent; y < extent; y++) {

                GameObject chunkInstance = Instantiate(chunkPrefab);

                Transform chunkTransform = chunkInstance.transform;
                chunkTransform.parent = transform;
                chunkTransform.position = new Vector3(x * area, 0f, y * area);
                chunkTransform.localScale = new Vector3(area, 1f, area);

                TerrainController controller = chunkInstance.GetComponent<TerrainController>();
                controllers.Add((x, y), controller);
            }

        action.performed += (_) => Test();
        action.Enable();
    }

    private void LateUpdate() => Cast();

    private void Test()
    {
        controllers[(0, 0)].Modify(new Vector2(0f, 0f), 0.5f, 10f, TerrainController.OperationType.Add);
        controllers[(-1, 0)].Modify(new Vector2(1f, 0f), 0.5f, 10f, TerrainController.OperationType.Add);
        controllers[(-1, -1)].Modify(new Vector2(1f, 1f), 0.5f, 10f, TerrainController.OperationType.Add);
        controllers[(0, -1)].Modify(new Vector2(0f, 1f), 0.5f, 10f, TerrainController.OperationType.Add);

        controllers[(1, 1)].Modify(new Vector2(0f, 0f), 0.5f, 10f, TerrainController.OperationType.Add);
        controllers[(0, 1)].Modify(new Vector2(1f, 0f), 0.5f, 10f, TerrainController.OperationType.Add);
        controllers[(0, 0)].Modify(new Vector2(1f, 1f), 0.5f, 10f, TerrainController.OperationType.Add);
        controllers[(1, 0)].Modify(new Vector2(0f, 1f), 0.5f, 10f, TerrainController.OperationType.Add);
    }

    private void Cast()
    {
        Vector3 translatedPosition = testPlayer.position;
        translatedPosition.x /= area;
        translatedPosition.z /= area;

        Vector3 translatedDirection = testPlayer.rotation * Vector3.forward;
        translatedDirection.x /= area;
        translatedDirection.z /= area;
        
        Vector3? result = controllers[(0, 0)].GetIntersect(translatedPosition, Vector3.Normalize(translatedDirection));

        if (result != null) {
            Debug.Log($"{result.Value}");
            Vector3 intersectPoint = result.Value;
            intersectPoint.x *= area;
            intersectPoint.z *= area;
            testDisplay.position = intersectPoint;
        }
    }
}
