using UnityEngine;
using UnityEngine.Rendering;

public class TerainRenderPipeline : RenderPipeline
{
    protected override void Render(ScriptableRenderContext context, Camera[] cameras)
    {
        if (cameras.Length <= 0)
            return;

        context.SetupCameraProperties(cameras[0]);

        string bufferName = "Render Terrain";

        CommandBuffer buffer = new CommandBuffer {
            name = bufferName
        };

        buffer.BeginSample(bufferName);

        context.ExecuteCommandBuffer(buffer);
        buffer.Clear();


        context.ExecuteCommandBuffer(buffer);
        buffer.Clear();

        buffer.EndSample(bufferName);

        context.Submit();
        
    }
}