using UnityEngine;

public class TrackTester : MonoBehaviour {

    private static TrackProfile[] profiles = new TrackProfile[] {
        new () {
            extent = 20f,
            vertical = true,
            count = 2,
            points = new Vector3[] 
            { 
                new Vector3(-0.5f,  0f,   0f),
                new Vector3( 0.5f,  0f,   0f),
            },
            normals = new Vector3[] 
            {
                new Vector3( 0f,     1f,     0f),
                new Vector3( 0f,     1f,     0f),
            }
        },

        new () {
            extent = 20f,
            vertical = true,
            count = 4,
            points = new Vector3[] 
            { 
                new Vector3(-10f, -5f, 0f),
                new Vector3(-0.5f, 0f,  0f),
                new Vector3( 0.5f, 0f,  0f),
                new Vector3( 10f, -5f, 0f)
            },
            normals = new Vector3[] 
            {
                new Vector3(-0.707f, 0.707f, 0f),
                new Vector3( 0f,     1f,     0f),
                new Vector3( 0f,     1f,     0f),
                new Vector3( 0.707f, 0.707f, 0f)
            }
        },

        new () {
            extent = 20f,
            vertical = true,
            count = 4,
            points = new Vector3[] 
            { 
                new Vector3(-10f,  5f, 0f),
                new Vector3(-0.5f, 0f, 0f),
                new Vector3( 0.5f, 0f, 0f),
                new Vector3( 10f,  5f, 0f)
            },
            normals = new Vector3[] 
            {
                new Vector3(0.707f, -0.707f, 0f),
                new Vector3( 0f,     1f,     0f),
                new Vector3( 0f,     1f,     0f),
                new Vector3(-0.707f, 0.707f, 0f)
            }
        },
    };
    public int profileIndex;

    TrackController controller;

    private void Start()
    {
        controller = GetComponent<TrackController>();
        controller.Setup(profiles[profileIndex]);
    }


    private Vector3 aLastPos, bLastPos;
    private Quaternion aLastRot, bLastRot;
    public Transform a, b;
    private void Update()
    {
        a.GetPositionAndRotation(out Vector3 aPos, out Quaternion aRot);
        b.GetPositionAndRotation(out Vector3 bPos, out Quaternion bRot);
        //if (aPos != aLastPos || bPos != bLastPos || aRot != aLastRot || bRot != bLastRot) {
            aLastPos = aPos;
            bLastPos = bPos;
            aLastRot = aRot;
            bLastRot = bRot;
            controller.SetPoints(a.position, a.rotation, b.position, b.rotation);
            controller.QueueRefresh();
        //}

        
    }
}