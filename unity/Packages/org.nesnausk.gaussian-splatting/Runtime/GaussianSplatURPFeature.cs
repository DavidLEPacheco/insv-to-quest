// SPDX-License-Identifier: MIT
#if GS_ENABLE_URP

#if !UNITY_6000_0_OR_NEWER
#error Unity Gaussian Splatting URP support only works in Unity 6 or later
#endif

using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.XR;

namespace GaussianSplatting.Runtime
{
    // Note: I have no idea what is the purpose of ScriptableRendererFeature vs ScriptableRenderPass, which one of those
    // is supposed to do resource management vs logic, etc. etc. Code below "seems to work" but I'm just fumbling along,
    // without understanding any of it.
    //
    // ReSharper disable once InconsistentNaming
    public class GaussianSplatURPFeature : ScriptableRendererFeature
    {
        class GSRenderPass : ScriptableRenderPass
        {
            const string GaussianSplatRTName = "_GaussianSplatRT";

            const string ProfilerTag = "GaussianSplatRenderGraph";
            static readonly ProfilingSampler s_profilingSampler = new(ProfilerTag);
            static readonly int s_gaussianSplatRT = Shader.PropertyToID(GaussianSplatRTName);
            static readonly MaterialPropertyBlock s_composite = new MaterialPropertyBlock();
            const int MonoCompositePass=1, PcArrayCompositePass=2;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            static readonly bool s_capturePc=System.Array.IndexOf(System.Environment.GetCommandLineArgs(),"-pcCapture")>=0;
            static readonly bool s_noPcDepth=System.Array.IndexOf(System.Environment.GetCommandLineArgs(),"-pcNoDepth")>=0;
            static bool s_capturedPc;
            static bool s_recordedPc;
            static int s_pcStereoFrames;

            static void CapturePc(CommandBuffer cmd,TextureHandle texture,string label)
            {
                RTHandle handle=texture;
                if(handle?.rt==null){Debug.Log("[PC Splat Render] No readback texture for "+label);return;}
                int width=handle.rt.width,height=handle.rt.height;
                cmd.RequestAsyncReadback(handle.rt,0,TextureFormat.RGBA32,request=>{
                    if(request.hasError){Debug.LogError("[PC Splat Render] Readback failed: "+label);return;}
                    string dir=System.IO.Path.Combine(Application.persistentDataPath,"pc-render-check");
                    System.IO.Directory.CreateDirectory(dir);
                    for(int layer=0;layer<System.Math.Min(2,request.layerCount);layer++)
                    {
                        var bytes=request.GetData<byte>(layer).ToArray();
                        long rgb=0,alpha=0;
                        for(int i=0;i<bytes.Length;i+=4){rgb+=bytes[i]+bytes[i+1]+bytes[i+2];alpha+=bytes[i+3];}
                        var png=ImageConversion.EncodeArrayToPNG(bytes,GraphicsFormat.R8G8B8A8_UNorm,(uint)width,(uint)height);
                        string path=System.IO.Path.Combine(dir,$"{label}-{layer}.png");
                        System.IO.File.WriteAllBytes(path,png);
                        Debug.Log($"[PC Splat Render] {label} eye={layer} size={width}x{height} rgbMean={rgb/(bytes.Length*.75):F2} alphaMean={alpha/(bytes.Length*.25):F2} path={path}");
                    }
                });
            }
#endif

            class PassData
            {
                internal UniversalCameraData CameraData;
                internal TextureHandle SourceTexture;
                internal TextureHandle SourceDepth;
                internal TextureHandle OpaqueDepth;
                internal Vector4 SplatTargetSize;
                internal Vector4 CameraTargetSize;
                internal TextureHandle GaussianSplatRT;
                internal bool IsStereo;
                internal bool IsScaled;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                using var builder = renderGraph.AddUnsafePass(ProfilerTag, out PassData passData);

                var cameraData = frameData.Get<UniversalCameraData>();
                var resourceData = frameData.Get<UniversalResourceData>();
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
                if(!s_recordedPc){s_recordedPc=true;Debug.Log($"[PC Splat Render] Record camera={cameraData.camera.name} stereo={cameraData.camera.stereoEnabled} dimension={cameraData.cameraTargetDescriptor.dimension} mode={XRSettings.stereoRenderingMode}");}
#endif

                // isStereo requires the actual render target to be a Tex2DArray (main XR swapchain).
                // OVROverlayCanvas and other stereo-enabled-but-2D cameras must take the non-stereo path.
                bool isStereo = XRSettings.enabled && cameraData.camera.stereoEnabled &&
                                (XRSettings.stereoRenderingMode == XRSettings.StereoRenderingMode.SinglePassInstanced ||
                                 XRSettings.stereoRenderingMode == XRSettings.StereoRenderingMode.SinglePassMultiview) &&
                                !Application.isEditor &&
                                cameraData.cameraTargetDescriptor.dimension == TextureDimension.Tex2DArray;
                // Always use cameraTargetDescriptor — it matches the actual depth buffer size (including render scale).
                // XRSettings.eyeTextureDesc returns the unscaled XR eye texture and causes dimension mismatches.
                RenderTextureDescriptor rtDesc = cameraData.cameraTargetDescriptor;
                rtDesc.depthBufferBits = 0;
                rtDesc.msaaSamples = 1;
                var system = GaussianSplatRenderSystem.instance;
                var rtFormat = system.GetPreferredRTFormat();
                float renderScale = system.GetPreferredRenderScale();
                bool isScaled = renderScale < 1.0f;
                rtDesc.graphicsFormat = rtFormat;
                if (isScaled)
                {
                    rtDesc.width = Mathf.Max(1, (int)(rtDesc.width * renderScale));
                    rtDesc.height = Mathf.Max(1, (int)(rtDesc.height * renderScale));
                }

                // Create render texture (use bilinear for upscale when scaled)
                var gaussianSplatRT = UniversalRenderer.CreateRenderGraphTexture(renderGraph, rtDesc, GaussianSplatRTName, true, FilterMode.Bilinear);

                passData.CameraData = cameraData;
                passData.SourceTexture = resourceData.activeColorTexture;
                passData.SourceDepth = resourceData.activeDepthTexture;
                passData.GaussianSplatRT = gaussianSplatRT;
                passData.IsStereo = isStereo;
                passData.IsScaled = isScaled;
                passData.SplatTargetSize = new Vector4(rtDesc.width, rtDesc.height, 0, 0);
                passData.CameraTargetSize = new Vector4(cameraData.cameraTargetDescriptor.width,cameraData.cameraTargetDescriptor.height,0,0);
                if (isStereo && resourceData.cameraDepthTexture.IsValid())
                {
                    passData.OpaqueDepth = resourceData.cameraDepthTexture;
                    builder.UseTexture(passData.OpaqueDepth);
                }

                builder.UseTexture(resourceData.activeColorTexture, AccessFlags.ReadWrite);
                builder.UseTexture(resourceData.activeDepthTexture);
                builder.UseTexture(gaussianSplatRT, AccessFlags.ReadWrite);
                if (system.ShareNearDetailColour())
                    builder.SetGlobalTextureAfterPass(gaussianSplatRT, Shader.PropertyToID("_SceneNearSplatColour"));
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (PassData data, UnsafeGraphContext context) =>
                {
                    var commandBuffer = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    using var _ = new ProfilingScope(commandBuffer, s_profilingSampler);

                    if (data.IsStereo)
                    {
                        bool manualPc=Application.platform==RuntimePlatform.WindowsPlayer && !SystemInfo.supportsMultiview;
                        // Windows SPI doubles procedural instances. These draws already select
                        // one array slice and one eye explicitly, so suspend SPI around them.
                        if(manualPc)data.CameraData.xr.StopSinglePass(commandBuffer);
                        // The fork draws each eye into a colour-only slice. Compare against
                        // URP's resolved opaque depth so avatars and museum props occlude it.
                        commandBuffer.SetGlobalInt("_ViewerDepthAvailable", data.OpaqueDepth.IsValid() ? 1 : 0);
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
                        if(s_noPcDepth)commandBuffer.SetGlobalInt("_ViewerDepthAvailable",0);
#endif
                        if (data.OpaqueDepth.IsValid())
                        {
                            commandBuffer.SetGlobalTexture("_ViewerOpaqueDepth", data.OpaqueDepth);
                            commandBuffer.SetGlobalVector("_ViewerSplatTargetSize", data.SplatTargetSize);
                        }
                        // Sort once, render twice — sorting uses center eye matrix so per-eye sort
                        // produces identical results at 2x the GPU cost.
                        CoreUtils.SetRenderTarget(commandBuffer, data.GaussianSplatRT, ClearFlag.Color, Color.clear);

                        var renderData = GaussianSplatRenderSystem.instance.PrepareSplats(data.CameraData.camera,commandBuffer,true,data.SplatTargetSize);

                        // [Quest3] Workaround: Unity doesn't correctly set unity_stereoEyeIndex when drawing to
                        // a render texture array, so we draw each eye manually.

                        CoreUtils.SetRenderTarget(commandBuffer, data.GaussianSplatRT, ClearFlag.None, Color.clear, 0, CubemapFace.Unknown, 0);
                        GaussianSplatRenderSystem.instance.RenderPreparedSplats(commandBuffer, 0);

                        CoreUtils.SetRenderTarget(commandBuffer, data.GaussianSplatRT, ClearFlag.None, Color.clear, 0, CubemapFace.Unknown, 1);
                        GaussianSplatRenderSystem.instance.RenderPreparedSplats(commandBuffer, 1);
                        Material matComposite = renderData.matComposite;
                        if(manualPc)
                        {
                            var desc=data.CameraData.cameraTargetDescriptor;
                            commandBuffer.SetViewport(new Rect(0,0,desc.width,desc.height));
                        }
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
                        bool capture=s_capturePc && !s_capturedPc && ++s_pcStereoFrames>90;
                        if(capture)
                        {
                            s_capturedPc=true;
                            Debug.Log($"[PC Splat Render] Capturing camera={data.CameraData.camera.name} stereo={data.IsStereo} depth={data.OpaqueDepth.IsValid()} manualPc={manualPc}");
                            CapturePc(commandBuffer,data.GaussianSplatRT,"splats");
                        }
#endif

                        // Composite to the final target
                        commandBuffer.BeginSample(GaussianSplatRenderSystem.s_ProfCompose);
                        s_composite.Clear();
                        s_composite.SetTexture(s_gaussianSplatRT,data.GaussianSplatRT);
                        s_composite.SetVector("_GaussianCompositeTargetSize",data.CameraTargetSize);

                        // [Quest3] Workaround for stereo rendering. Unity is not able to correctly set unity_stereoEyeIndex when drawing to
                        // a render texture array, so we need to do it manually. Also, we need to draw the same material twice,
                        // once for each eye. TODO: Revisit this when Unity fixes the issue.
                        commandBuffer.SetRenderTarget(data.SourceTexture, 0, CubemapFace.Unknown, 0);
                        s_composite.SetInt("_CustomStereoEyeIndex",0);
                        commandBuffer.DrawProcedural(Matrix4x4.identity,matComposite,manualPc?PcArrayCompositePass:0,MeshTopology.Triangles,3,1,s_composite);

                        commandBuffer.SetRenderTarget(data.SourceTexture, 0, CubemapFace.Unknown, 1);
                        s_composite.SetInt("_CustomStereoEyeIndex",1);
                        commandBuffer.DrawProcedural(Matrix4x4.identity,matComposite,manualPc?PcArrayCompositePass:0,MeshTopology.Triangles,3,1,s_composite);
                        commandBuffer.EndSample(GaussianSplatRenderSystem.s_ProfCompose);
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
                        if(capture)CapturePc(commandBuffer,data.SourceTexture,"composite");
#endif
                        if(manualPc)data.CameraData.xr.StartSinglePass(commandBuffer);
                    }
                    else
                    {
                        if (data.IsScaled)
                            CoreUtils.SetRenderTarget(commandBuffer, data.GaussianSplatRT, ClearFlag.Color, Color.clear);
                        else
                            CoreUtils.SetRenderTarget(commandBuffer, data.GaussianSplatRT, data.SourceDepth, ClearFlag.Color, Color.clear);
                        var system=GaussianSplatRenderSystem.instance;
                        var prepared=system.PrepareSplats(data.CameraData.camera,commandBuffer,false,data.SplatTargetSize);
                        system.RenderPreparedSplats(commandBuffer,-1);

                        commandBuffer.BeginSample(GaussianSplatRenderSystem.s_ProfCompose);
                        commandBuffer.SetGlobalVector("_GaussianCompositeTargetSize",data.CameraTargetSize);
                        // Blitter binds this camera's 2D splat texture on the draw itself.
                        Blitter.BlitCameraTexture(commandBuffer,data.GaussianSplatRT,data.SourceTexture,prepared.matComposite,MonoCompositePass);
                        commandBuffer.EndSample(GaussianSplatRenderSystem.s_ProfCompose);
                    }
                });
            }
        }

        GSRenderPass m_Pass;
        bool m_HasCamera;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        bool m_LoggedPcCull;
#endif

        public override void Create()
        {
            m_Pass = new GSRenderPass
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingTransparents
            };
            m_Pass.ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        public override void OnCameraPreCull(ScriptableRenderer renderer, in CameraData cameraData)
        {
            m_HasCamera = false;
            var system = GaussianSplatRenderSystem.instance;
            bool gathered=system.GatherSplatsForCamera(cameraData.camera);
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if(!m_LoggedPcCull){m_LoggedPcCull=true;Debug.Log($"[PC Splat Render] PreCull camera={cameraData.camera.name} gathered={gathered}");}
#endif
            if (!gathered)
                return;

            m_HasCamera = true;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!m_HasCamera)
                return;
            renderer.EnqueuePass(m_Pass);
        }

        protected override void Dispose(bool disposing)
        {
            m_Pass = null;
        }
    }
}

#endif // #if GS_ENABLE_URP
