using UnityEngine;
using UnityEngine.InputSystem;

public class SplineTester : MonoBehaviour {

    private static SplineProfile[] profiles = new SplineProfile[] {
        new () {
            extent = 20f,
            vertical = true,
            count = 2,
            points = new TrackPoint[] 
            { 
                new TrackPoint() {
                    position = new Vector3(-0.75f, 0f, 0f),
                    normal =   new Vector3( 0f,    1f, 0f),
                    uv =       new Vector2( 0f,    0f),
                },
                new TrackPoint() {
                    position = new Vector3( 0.75f, 0f, 0f),
                    normal =   new Vector3( 0f,    1f, 0f),
                    uv =       new Vector2( 0f,    0f),
                }
            },
        },

        new () {
            extent = 20f,
            vertical = true,
            count = 4,
            points = new TrackPoint[] 
            {
                new TrackPoint() {
                    position = new Vector3(-10f,   -5f,     0f),
                    normal =   new Vector3(-0.707f, 0.707f, 0f),
                    uv =       new Vector2( 0f,     0f),
                },
                new TrackPoint() {
                    position = new Vector3(-0.75f, 0f, 0f),
                    normal =   new Vector3( 0f,    1f, 0f),
                    uv =       new Vector2( 0f,    0f),
                },
                new TrackPoint() {
                    position = new Vector3( 0.75f, 0f, 0f),
                    normal =   new Vector3( 0f,    1f, 0f),
                    uv =       new Vector2( 0f,    0f),
                },
                new TrackPoint() {
                    position = new Vector3( 10f,   -5f,     0f),
                    normal =   new Vector3( 0.707f, 0.707f, 0f),
                    uv =       new Vector2( 0f,     0f),
                }
            }
        },

        new () {
            extent = 20f,
            vertical = true,
            count = 4,
            points = new TrackPoint[] 
            { 
                new TrackPoint() {
                    position = new Vector3(-10f,     5f,     0f),
                    normal =   new Vector3( 0.707f, -0.707f, 0f),
                    uv =       new Vector2( 0f,      0f),
                },
                new TrackPoint() {
                    position = new Vector3(-0.75f, 0f, 0f),
                    normal =   new Vector3( 0f,    1f, 0f),
                    uv =       new Vector2( 0f,    0f),
                },
                new TrackPoint() {
                    position = new Vector3( 0.75f, 0f, 0f),
                    normal =   new Vector3( 0f,    1f, 0f),
                    uv =       new Vector2( 0f,    0f),
                },
                new TrackPoint() {
                    position = new Vector3( 10f,    5f,     0f),
                    normal =   new Vector3(-0.707f, 0.707f, 0f),
                    uv =       new Vector2( 0f,     0f),
                }
            },
        },
    };
    public int profileIndex;

    SplineController controller;

    private void Start()
    {
        controller = GetComponent<SplineController>();
        controller.Setup(profiles[profileIndex]);
        Debug.Log(JsonUtility.ToJson(profiles[profileIndex], prettyPrint:true));
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