using System;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(TerrainCoordinator))]
public class TerrainCaster : MonoBehaviour {

    public Transform cursorObject;

    private TerrainCoordinator coordinator;
    private Camera mainCamera;

    public enum Mode
    {
        Add,
        Subtract,
        Level,
        Smooth,
        Length
    }
    [SerializeField] private Mode currentMode = Mode.Add;
    private Vector2 mousePosition = Vector2.zero;
    private bool mousePressed = false;

    public void OnCycleMode(InputAction.CallbackContext context)
    {
        if (!context.performed) return;

        int newValue = ((int)currentMode) + 1;
        if (newValue >= (int)Mode.Length)
            newValue = 0;
        currentMode = (Mode)newValue;
        Debug.Log(currentMode);
    }

    public void OnPointerPosition(InputAction.CallbackContext context)
    {
        mousePosition = context.ReadValue<Vector2>();
    }

    public void OnPointerPressed(InputAction.CallbackContext context)
    {
        mousePressed = context.ReadValueAsButton();
    }

    private void Start()
    {
        mainCamera = Camera.main;
        coordinator = GetComponent<TerrainCoordinator>();
    }

    private Vector3 target = Vector3.zero;

    private void Update()
    {
        Ray ray = mainCamera.ScreenPointToRay(mousePosition);
        coordinator.CastRay(mainCamera.transform.position, ray.direction, false, (Vector3? result) => {
            if (result is null)
                return;
                
            target = result.Value;
        });

        coordinator.UpdateVisual(
            mainCamera.transform.position, 
            GeometryUtility.CalculateFrustumPlanes(mainCamera));

        if (mousePressed)
        {
            switch (currentMode)
            {
                case Mode.Add:
                    coordinator.ModifyAdd(cursorObject.position, 10f, 5f);
                    break;
                case Mode.Subtract:
                    coordinator.ModifySubtract(cursorObject.position, 10f, 5f);
                    break;
                case Mode.Level:
                    coordinator.ModifyLevel(cursorObject.position, 10f, cursorObject.position.y);
                    break;
                case Mode.Smooth:
                    coordinator.ModifySmooth(cursorObject.position, 10f);
                    break;
            }
        }

        
    }

    private void LateUpdate()
    {
        float distance = Vector3.Distance(cursorObject.position, target);
        cursorObject.position = Vector3.MoveTowards(cursorObject.position, target, distance * distance / 4f);
    }
}