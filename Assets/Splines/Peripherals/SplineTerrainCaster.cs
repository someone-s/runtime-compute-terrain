using UnityEngine;

public class SplineTerrainCaster : MonoBehaviour, ISplineCaster
{
    private ComputeBuffer requestBuffer;
    private ComputeBuffer minTBuffer;
    private ComputeBuffer resultBuffer;

    public void Setup(int size, out ComputeBuffer requestBuffer, out ComputeBuffer resultBuffer)
    {
        TerrainCoordinator.Instance.GetBatchBuffer(size, out this.requestBuffer, out this.minTBuffer, out this.resultBuffer);

        requestBuffer = this.requestBuffer;
        resultBuffer = this.resultBuffer;
    }

    public void Cast(Bounds bounds, int count) =>
        TerrainCoordinator.Instance.CastBatchRay(bounds, count, requestBuffer, minTBuffer, resultBuffer);

    public void Release() =>
        TerrainCoordinator.Instance.ReturnBatchBuffer(requestBuffer, minTBuffer, resultBuffer);
}