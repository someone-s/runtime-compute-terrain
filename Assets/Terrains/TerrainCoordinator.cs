using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;


[RequireComponent(typeof(TerrainModifier), typeof(TerrainIntersector))]
public class TerrainCoordinator : MonoBehaviour
{
    public Transform testPlayer;
    public Transform testDisplay;
    public InputAction actionA;
    public InputAction actionB;
    public InputAction actionC;
    public InputAction actionD;

    [SerializeField] private GameObject chunkPrefab;

    private (int x, int z)[] renderedChunks;
    private (int x, int z)[] previousChunks;
    private int renderRange = 3;
    private float area = 50f;

    private TerrainModifier modifier;
    private TerrainIntersector intersector;
    public Dictionary<(int x, int z), TerrainController> controllers = new();

    private void Start()
    {
        modifier = GetComponent<TerrainModifier>();
        intersector = GetComponent<TerrainIntersector>();
        intersector.intersectCallback = OnIntersectResult;

        actionA.Enable();
        actionB.Enable();
        actionC.Enable();
        actionD.Enable();
    }
    private TerrainController Generate(int x, int z)
    {
        GameObject chunkInstance = Instantiate(chunkPrefab);

        Transform chunkTransform = chunkInstance.transform;
        chunkTransform.parent = transform;
        chunkTransform.position = new Vector3(x * area, 0f, z * area);
        chunkTransform.localScale = new Vector3(area, 1f, area);

        TerrainController controller = chunkInstance.GetComponent<TerrainController>();
        controllers.Add((x, z), controller);

        return controller;
    }

    private void Update()
    {

        Vector3 scaledPosition = testPlayer.position;
        scaledPosition.x /= area;
        scaledPosition.z /= area;

        int centerX = Mathf.RoundToInt(scaledPosition.x);
        int centerZ = Mathf.RoundToInt(scaledPosition.z);

        UpdateCast(scaledPosition, centerX - 1, centerZ - 1);

        UpdateVisual(centerX, centerZ);

        if (actionA.IsPressed() || actionB.IsPressed() || actionC.IsPressed() || actionD.IsPressed())
            Test();
    }

    private void UpdateCast(Vector3 scaledPosition, int centerX, int centerZ)
    {

        Vector3 scaledDirection = testPlayer.rotation * Vector3.forward;
        scaledDirection.x /= area;
        scaledDirection.z /= area;

        for (int x = centerX; x <= centerX + 1; x++)
            for (int z = centerZ; z <= centerZ + 1; z++)
                if (!controllers.TryGetValue((x, z), out TerrainController controller))
                    controller = Generate(x, z);


        Vector3 translatedPosition = scaledPosition;
        translatedPosition.x -= centerX;
        translatedPosition.z -= centerZ;
        intersector.QueueIntersect((centerX, centerZ), translatedPosition, Vector3.Normalize(scaledDirection));
    }

    private void UpdateVisual(int centerX, int centerZ)
    {

        bool setup = previousChunks == null;

        if (setup)
        {
            previousChunks = new (int x, int z)[(2 * renderRange + 1) * (2 * renderRange + 1)];
            renderedChunks = new (int x, int z)[(2 * renderRange + 1) * (2 * renderRange + 1)];
        }

        for (int x = 0; x < 2 * renderRange + 1; x++)
            for (int z = 0; z < 2 * renderRange + 1; z++)
                renderedChunks[z * (2 * renderRange + 1) + x] = (x + centerX - renderRange, z + centerZ - renderRange);

        if (setup)
        {
            foreach ((int x, int z) chunk in renderedChunks)
            {

                if (!controllers.TryGetValue(chunk, out TerrainController controller))
                    controller = Generate(chunk.x, chunk.z);

                controller.SetVisible(true);
            }
        }
        else
        {
            foreach ((int x, int z) chunk in previousChunks.Except(renderedChunks))
                controllers[chunk].SetVisible(false);

            foreach ((int x, int z) chunk in renderedChunks.Except(previousChunks))
            {
                if (!controllers.TryGetValue(chunk, out TerrainController controller))
                    controller = Generate(chunk.x, chunk.z);

                controller.SetVisible(true);
            }
        }

        // Swap without reallocate
        (int x, int y)[] temp = previousChunks;
        previousChunks = renderedChunks;
        renderedChunks = temp;
    }

    private void OnIntersectResult(Vector3 intersectPoint)
    {

        Debug.Log(intersectPoint);
        intersectPoint.x *= area;
        intersectPoint.z *= area;
        testDisplay.position = intersectPoint;
    }

    private void Test()
    {
        Vector3 point = testDisplay.position;
        point.x /= area;
        point.z /= area;

        int centerX = Mathf.RoundToInt(point.x) - 1;
        int centerZ = Mathf.RoundToInt(point.z) - 1;

        float delta = Time.deltaTime * 5;

        for (int x = centerX; x <= centerX + 1; x++)
            for (int z = centerZ; z <= centerZ + 1; z++)
                if (!controllers.TryGetValue((x, z), out TerrainController controller))
                    controller = Generate(x, z);

        if (actionA.IsPressed())
            modifier.QueueModify((centerX, centerZ), new Vector2(point.x - centerX, point.z - centerZ), 0.2f, delta, TerrainModifier.OperationType.Add);
        if (actionB.IsPressed())
            modifier.QueueModify((centerX, centerZ), new Vector2(point.x - centerX, point.z - centerZ), 0.2f, delta, TerrainModifier.OperationType.Subtract);
        if (actionC.IsPressed())
            modifier.QueueModify((centerX, centerZ), new Vector2(point.x - centerX, point.z - centerZ), 0.2f, 5f, TerrainModifier.OperationType.Level);
        if (actionD.IsPressed())
            modifier.QueueModify((centerX, centerZ), new Vector2(point.x - centerX, point.z - centerZ), 0.2f, 0f, TerrainModifier.OperationType.Smooth);
    }
}
