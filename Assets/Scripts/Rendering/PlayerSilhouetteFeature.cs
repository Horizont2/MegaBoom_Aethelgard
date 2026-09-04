using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

// Draws the player's silhouette through whatever is in front of them.
//
// The first attempt did this with a plain material in the transparent queue and
// ZTest Greater. That is not reliable: the queue only says WHEN a material is
// drawn, not what depth buffer it is tested against, and in this project it drew
// over the whole character everywhere instead of only where occluded. A render
// pass is the right level: it runs at a point we choose, against the camera's
// real depth attachment, so the depth test means what it says.
//
// The pass draws only shader passes tagged LightMode = "PlayerSilhouette", which
// no built-in URP pass knows. So the silhouette meshes are invisible to every
// other pass, and if this feature is ever removed from the renderer they simply
// stop rendering rather than reappearing as solid figures.
//
// Add it to the renderer in use (Assets/Settings/PC_Renderer.asset) via
// Add Renderer Feature.
public class PlayerSilhouetteFeature : ScriptableRendererFeature
{
    [Tooltip("After opaques: foliage and terrain have written depth by then, which is exactly what should occlude the player, and the silhouette still sits under transparent VFX.")]
    public RenderPassEvent injectionPoint = RenderPassEvent.AfterRenderingOpaques;

    [Tooltip("Layers to consider. Everything is fine — only meshes carrying the silhouette shader can be drawn by this pass anyway.")]
    public LayerMask layerMask = ~0;

    private SilhouettePass _pass;

    public override void Create()
    {
        _pass = new SilhouettePass(layerMask) { renderPassEvent = injectionPoint };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        // Game and Scene views only — no reflection probes or preview cameras.
        var type = renderingData.cameraData.cameraType;
        if (type != CameraType.Game && type != CameraType.SceneView) return;
        renderer.EnqueuePass(_pass);
    }

    private class SilhouettePass : ScriptableRenderPass
    {
        private static readonly ShaderTagId s_tag = new ShaderTagId("PlayerSilhouette");
        private readonly LayerMask _layerMask;

        public SilhouettePass(LayerMask layerMask) { _layerMask = layerMask; }

        private class PassData
        {
            public RendererListHandle list;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var renderingData = frameData.Get<UniversalRenderingData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            var lightData = frameData.Get<UniversalLightData>();
            var resourceData = frameData.Get<UniversalResourceData>();

            if (resourceData.activeColorTexture.IsValid() == false) return;

            var drawSettings = RenderingUtils.CreateDrawingSettings(
                s_tag, renderingData, cameraData, lightData, SortingCriteria.CommonTransparent);
            var filterSettings = new FilteringSettings(RenderQueueRange.all, _layerMask);
            var listParams = new RendererListParams(renderingData.cullResults, drawSettings, filterSettings);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Player Silhouette", out var passData))
            {
                passData.list = renderGraph.CreateRendererList(listParams);
                builder.UseRendererList(passData.list);

                builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
                // Depth is READ, never written: the shader's ZTest Greater needs
                // the scene's depth to test against, and the silhouette must not
                // occlude anything itself.
                builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.Read);

                builder.SetRenderFunc((PassData data, RasterGraphContext ctx) =>
                    ctx.cmd.DrawRendererList(data.list));
            }
        }
    }
}
