using System.Collections;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;

public class SplineTester : MonoBehaviour {

    [SerializeField] private string profileName;
    private SplineController controller;
    private bool canUpdate = false;

    private string ProfilePath => Path.Join(FileCoordinator.Instance.ResourceRoot, "Splines", $"{profileName}.glb");

    private IEnumerator Start()
    {
        controller = GetComponent<SplineController>();
        Task<SplineProfile?> loadTask = SplineLoader.Get(ProfilePath);
        yield return new WaitUntil(() => loadTask.IsCompleted || loadTask.IsCompleted);
        
        if (!loadTask.IsCompletedSuccessfully) yield break;

        SplineProfile? profile = loadTask.Result;
        Debug.Log(profile == null);
        if (profile != null) {
            controller.Setup(profile.Value);
            canUpdate = true;
        }
    }
    


    private Vector3 aLastPos, bLastPos;
    private Quaternion aLastRot, bLastRot;
    public Transform a, b;
    private void Update()
    {
        if (!canUpdate) return;

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