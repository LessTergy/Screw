using System.Threading;
using Cysharp.Threading.Tasks;
using Screw.Levels;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Screw.Bootstrap
{
    internal static class GameplayShaderWarmup
    {
        public static async UniTask RunAsync(Camera camera, Transform modelRoot, CancellationToken cancellationToken)
        {
            // Let Gameplay initialize the camera and HUD before rendering the warmup passes.
            if (await UniTask.Yield(PlayerLoopTiming.LastPreLateUpdate, cancellationToken).SuppressCancellationThrow()
                || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null
                || !(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset pipeline))
            {
                return;
            }

            var descriptor = new RenderTextureDescriptor(
                Mathf.Max(1, camera.pixelWidth / 2), Mathf.Max(1, camera.pixelHeight / 2),
                camera.allowHDR && pipeline.supportsHDR ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default, 24)
            {
                msaaSamples = camera.allowMSAA ? pipeline.msaaSampleCount : 1
            };
            descriptor.msaaSamples = SystemInfo.GetRenderTextureSupportedMSAASampleCount(descriptor);
            var target = RenderTexture.GetTemporary(descriptor);
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var request = new RenderPipeline.StandardRequest { destination = target };
            var preview = new PartPreview();
#if UNITY_EDITOR
            var previousAsyncCompilation = UnityEditor.ShaderUtil.allowAsyncCompilation;
#endif
            try
            {
                target.name = "Gameplay Shader Warmup";
#if UNITY_EDITOR
                UnityEditor.ShaderUtil.allowAsyncCompilation = false;
#endif
                RenderPipeline.SubmitRenderRequest(camera, request);
                preview.Show(modelRoot);
                RenderPipeline.SubmitRenderRequest(camera, request);
            }
            finally
            {
                preview.Clear();
#if UNITY_EDITOR
                UnityEditor.ShaderUtil.allowAsyncCompilation = previousAsyncCompilation;
#endif
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(target);
            }
        }
    }
}
