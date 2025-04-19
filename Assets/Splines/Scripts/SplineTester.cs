using UnityEngine;
using UnityEngine.InputSystem;

public class SplineTester : MonoBehaviour {

    [SerializeField] private SplineProfile profile;
    private SplineController controller;

    private void Start()
    {
        controller = GetComponent<SplineController>();
        controller.Setup(profile);
    }


    private Vector3 aLastPos, bLastPos;
    private Quaternion aLastRot, bLastRot;
    public Transform a, b;
    private void Update()
    {
        if (Keyboard.current.rKey.isPressed)
            a.position += Vector3.left * Time.deltaTime;
        if (Keyboard.current.qKey.isPressed)
            a.position += Vector3.right * Time.deltaTime;

        a.GetPositionAndRotation(out Vector3 aPos, out Quaternion aRot);
        b.GetPositionAndRotation(out Vector3 bPos, out Quaternion bRot);
        if (aPos != aLastPos || bPos != bLastPos || aRot != aLastRot || bRot != bLastRot) {
            aLastPos = aPos;
            bLastPos = bPos;
            aLastRot = aRot;
            bLastRot = bRot;
            controller.QueueRefresh(a.position, a.rotation, b.position, b.rotation);
        }

        
    }
}