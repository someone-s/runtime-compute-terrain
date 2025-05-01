using System.Collections.Generic;
using UnityEngine;

public class TestCaster : MonoBehaviour
{
    public List<Transform> transforms;

    void Update()
    {
        foreach (var t in transforms)
        {
            Transform t2 = t;
            TerrainCoordinator.Instance.CastRay(t.position + Vector3.up * 1000f, Vector3.down, 1000f, false, (Vector3? res) => {
                if (res == null) return;
                t2.position = res.Value;
            });
        }
    }
}
