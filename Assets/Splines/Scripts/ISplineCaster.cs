using UnityEngine;

public interface ISplineCaster
{
    public void Setup(int size, out ComputeBuffer requestBuffer, out ComputeBuffer resultBuffer);
    public void Cast(Bounds bounds, int count);
    public void Release();
}