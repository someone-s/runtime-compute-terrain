using UnityEngine;

[RequireComponent(typeof(MeshFilter))]
public class TerrainProjector : MonoBehaviour
{
    [SerializeField] private TerrainCoordinator coordinator;

    public TerrainMask mode = TerrainMask.Unset;

    private Bounds? previousWorldBounds = null;
    internal MeshFilter filter;
    internal Matrix4x4 currentLocalToWorld;

    private void Start()
    {
        filter = GetComponent<MeshFilter>();
        if (coordinator == null)
            coordinator = TerrainCoordinator.Instance;
    }

    public void QueueRefreshWorldSpace() => QueueRefresh(true);
    public void QueueRefresh(bool meshInWorldSpace = false)
    {
        Bounds currentWorldBounds = filter.sharedMesh.GetSubMesh(0).bounds;

        if (!meshInWorldSpace)
        {
            currentLocalToWorld = transform.localToWorldMatrix;

            Vector3 min = currentWorldBounds.min;
            Vector3 max = currentWorldBounds.max;
            BoundsUtility.TransformMinMax(ref min, ref max, currentLocalToWorld);
            currentWorldBounds.SetMinMax(min, max);
        }
        else
            currentLocalToWorld = Matrix4x4.identity;


        if (previousWorldBounds == null)
            coordinator.AddProjector(currentWorldBounds, this);
        else
            coordinator.UpdateProjector(previousWorldBounds.Value, currentWorldBounds, this);

        previousWorldBounds = currentWorldBounds;
    }

    private void OnDestroy()
    {
        if (previousWorldBounds != null)
            coordinator?.RemoveProjector(previousWorldBounds.Value, this);
    }
}