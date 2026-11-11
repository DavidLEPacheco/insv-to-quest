using System;
using System.IO;
using System.Linq;
using GaussianSplatting.Editor;
using GaussianSplatting.Runtime;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

namespace ScanViewer.Editor
{
    public static class BuildViewer
    {
        [Serializable] public class Request { public string ply,placement,output,target; public float renderScale=1; }
        [Serializable] public class Placement
        {
            public float[] position,rotation,scale,startPosition,startForward,walkBounds;
            public int splatCount;
        }
        const string Settings="Assets/Generated";

        static T Save<T>(T asset,string name) where T:UnityEngine.Object
        {
            string path=$"{Settings}/{name}.asset";
            var existing=AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing) {EditorUtility.CopySerialized(asset,existing);UnityEngine.Object.DestroyImmediate(asset);return existing;}
            AssetDatabase.CreateAsset(asset,path);return asset;
        }

        static void ConfigureXR(BuildTargetGroup group)
        {
            var per=AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>($"{Settings}/XR.asset");
            if (!per) per=Save(ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>(),"XR");
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey,per,true);
            if (!per.HasSettingsForBuildTarget(group)) per.CreateDefaultSettingsForBuildTarget(group);
            if (!per.HasManagerSettingsForBuildTarget(group)) per.CreateDefaultManagerSettingsForBuildTarget(group);
            var general=per.SettingsForBuildTarget(group);general.InitManagerOnStart=true;
            if (!XRPackageMetadataStore.AssignLoader(general.Manager,"UnityEngine.XR.OpenXR.OpenXRLoader",group))
                throw new BuildFailedException("Could not configure OpenXR loader");
            UnityEditor.XR.OpenXR.Features.FeatureHelpers.RefreshFeatures(group);
            var xr=OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            xr.renderMode=OpenXRSettings.RenderMode.SinglePassInstanced;
            foreach (var feature in xr.GetFeatures<OpenXRFeature>())
            {
                string type=feature.GetType().Name;
                feature.enabled=type=="OculusTouchControllerProfile" || type=="MetaQuestTouchPlusControllerProfile" || (group==BuildTargetGroup.Android && type=="MetaQuestFeature");
                EditorUtility.SetDirty(feature);
            }
            EditorUtility.SetDirty(xr);EditorUtility.SetDirty(general);EditorUtility.SetDirty(general.Manager);EditorUtility.SetDirty(per);
        }

        static Vector3 V(float[] a) => a?.Length==3 ? new Vector3(a[0],a[1],a[2]) : throw new BuildFailedException("Expected a 3-vector in placement JSON");

        public static void Configure()
        {
            var settings=new SerializedObject(Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings"));
            var input=settings.FindProperty("activeInputHandler");
            if(input==null)throw new BuildFailedException("Input System setting unavailable in this editor version");
            input.intValue=1;settings.ApplyModifiedPropertiesWithoutUndo();AssetDatabase.SaveAssets();
            Debug.Log("[ScanViewer] Input backend configured. Restart before building to refresh editor compilation defines.");
        }

        public static void Review()
        {
            var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"-viewerRequest");
            var request=JsonUtility.FromJson<Request>(File.ReadAllText(args[index+1]));
            EditorSceneManager.OpenScene("Assets/Viewer.unity");
            var camera=Camera.main;camera.stereoTargetEye=StereoTargetEyeMask.None;
            foreach(var splat in UnityEngine.Object.FindObjectsByType<GaussianSplatRenderer>(FindObjectsSortMode.None)) splat.Update();
            var target=new RenderTexture(640,480,24,RenderTextureFormat.ARGB32);target.Create();
            RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
            var previous=RenderTexture.active;RenderTexture.active=target;
            var image=new Texture2D(640,480,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,640,480),0,0);image.Apply();
            RenderTexture.active=previous;
            int lit=image.GetPixels32().Count(c=>c.r>8 || c.g>8 || c.b>8);
            File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(request.output),"preview.png"),image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);target.Release();UnityEngine.Object.DestroyImmediate(target);
            if(lit<1000)throw new BuildFailedException("Preview is almost empty; verify placement and rendering.");
            Debug.Log($"[ScanViewer] Preview verified: {lit} visible pixels. This is a mono editor check, not a headset test.");
        }

        public static void Build()
        {
            var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"-viewerRequest");
            if (index<0 || index+1>=args.Length) throw new BuildFailedException("Missing -viewerRequest JSON path");
            var request=JsonUtility.FromJson<Request>(File.ReadAllText(args[index+1]));
            var placement=JsonUtility.FromJson<Placement>(File.ReadAllText(request.placement));
            bool mobile=request.target=="quest";
            var target=mobile?BuildTarget.Android:BuildTarget.StandaloneWindows64;
            var group=mobile?BuildTargetGroup.Android:BuildTargetGroup.Standalone;
            if (EditorUserBuildSettings.activeBuildTarget!=target) throw new BuildFailedException("Start Unity with the requested build target");
            Directory.CreateDirectory(Settings);AssetDatabase.Refresh();
            PlayerSettings.companyName="Scan Viewer";PlayerSettings.productName="Scan Viewer";
            PlayerSettings.colorSpace=ColorSpace.Linear;PlayerSettings.runInBackground=true;
            PlayerSettings.fullScreenMode=FullScreenMode.Windowed;PlayerSettings.defaultScreenWidth=640;PlayerSettings.defaultScreenHeight=480;
            PlayerSettings.SetUseDefaultGraphicsAPIs(target,false);
            PlayerSettings.SetGraphicsAPIs(target,new[]{mobile?GraphicsDeviceType.Vulkan:GraphicsDeviceType.Direct3D12});
            PlayerSettings.SetScriptingBackend(mobile?NamedBuildTarget.Android:NamedBuildTarget.Standalone,mobile?ScriptingImplementation.IL2CPP:ScriptingImplementation.Mono2x);
            // OpenXR requires the Input System backend. The rig reads XR device features.
            var serialized=new SerializedObject(Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings"));
            var input=serialized.FindProperty("activeInputHandler");if(input!=null){input.intValue=1;serialized.ApplyModifiedPropertiesWithoutUndo();}
            if (mobile)
            {
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android,"org.scanviewer.local");
                PlayerSettings.Android.targetArchitectures=AndroidArchitecture.ARM64;
                PlayerSettings.Android.minSdkVersion=AndroidSdkVersions.AndroidApiLevel32;
                PlayerSettings.Android.targetSdkVersion=AndroidSdkVersions.AndroidApiLevelAuto;
                PlayerSettings.Android.useAPKExpansionFiles=false;
                EditorUserBuildSettings.buildAppBundle=false;
            }
            ConfigureXR(group);
            var renderer=Save(ScriptableObject.CreateInstance<UniversalRendererData>(),"Renderer");
            var feature=ScriptableObject.CreateInstance<GaussianSplatURPFeature>();feature.name="Gaussian Splats";
            AssetDatabase.AddObjectToAsset(feature,renderer);renderer.rendererFeatures.Clear();renderer.rendererFeatures.Add(feature);renderer.SetDirty();
            var pipeline=Save(UniversalRenderPipelineAsset.Create(renderer),"Pipeline");
            pipeline.msaaSampleCount=1;pipeline.renderScale=1;pipeline.supportsHDR=true;pipeline.supportsCameraDepthTexture=true;
            GraphicsSettings.defaultRenderPipeline=pipeline;
            for(int i=0;i<QualitySettings.names.Length;i++){QualitySettings.SetQualityLevel(i);QualitySettings.renderPipeline=pipeline;}
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var asset=GaussianSplatAssetCreator.ImportPly(request.ply,"Assets/Imported",true,!mobile);
            if (placement.splatCount>0 && asset.splatCount!=placement.splatCount) throw new BuildFailedException("Placement belongs to another model");
            var scan=new GameObject("Scan");var splat=scan.AddComponent<GaussianSplatRenderer>();splat.m_Asset=asset;
            scan.transform.position=V(placement.position);scan.transform.localScale=V(placement.scale);
            var q=placement.rotation;if(q?.Length!=4)throw new BuildFailedException("Expected rotation quaternion");
            scan.transform.rotation=new Quaternion(q[0],q[1],q[2],q[3]);
            const string shaders="Packages/org.nesnausk.gaussian-splatting/Shaders/";
            splat.m_ShaderSplats=AssetDatabase.LoadAssetAtPath<Shader>(shaders+"RenderGaussianSplats.shader");
            splat.m_ShaderComposite=AssetDatabase.LoadAssetAtPath<Shader>(shaders+"GaussianComposite.shader");
            splat.m_ShaderDebugPoints=AssetDatabase.LoadAssetAtPath<Shader>(shaders+"GaussianDebugRenderPoints.shader");
            splat.m_ShaderDebugBoxes=AssetDatabase.LoadAssetAtPath<Shader>(shaders+"GaussianDebugRenderBoxes.shader");
            splat.m_CSSplatUtilities=AssetDatabase.LoadAssetAtPath<ComputeShader>(shaders+"SplatUtilities.compute");
            // Reuse the established mobile profile; PC keeps its full-quality settings.
            splat.m_RenderScale=Mathf.Clamp(request.renderScale, mobile ? .25f : .5f, 1);splat.m_SHOrder=mobile?2:3;splat.m_SHLodEnabled=false;
            splat.m_SortNthFrame=1;splat.m_SortPasses=mobile?2:4;splat.m_AdaptiveSort=false;splat.m_CullChunks=true;
            splat.m_HighPrecisionRT=!mobile;splat.m_QuadExtent=mobile?2.5f:3;splat.m_ContributionCullThreshold=mobile ? .1f : 0;
            Debug.Log($"[ScanViewer] Render profile: mobile={mobile}, scale={splat.m_RenderScale}, fp16={splat.m_HighPrecisionRT}, sort={splat.m_SortPasses}, sh={splat.m_SHOrder}");
            var rig=new GameObject("Viewer rig");var controller=rig.AddComponent<ViewerRig>();
            controller.startPosition=V(placement.startPosition);controller.startForward=V(placement.startForward);
            var b=placement.walkBounds;if(b?.Length!=4 || b[0]>=b[2] || b[1]>=b[3])throw new BuildFailedException("Invalid walk bounds");
            controller.walkBounds=new Vector4(b[0],b[1],b[2],b[3]);
            var cameraObject=new GameObject("Eye");cameraObject.tag="MainCamera";cameraObject.transform.SetParent(rig.transform,false);
            var camera=cameraObject.AddComponent<Camera>();controller.eye=camera;
            camera.nearClipPlane=.05f;camera.farClipPlane=200;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
            cameraObject.transform.position=controller.startPosition+Vector3.up*1.6f;cameraObject.transform.rotation=Quaternion.LookRotation(controller.startForward,Vector3.up);
            camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;
            controller.leftHand=Hand(rig.transform,"Left hand",new Color(.2f,.5f,1));
            controller.rightHand=Hand(rig.transform,"Right hand",new Color(.2f,.5f,1));
            RenderSettings.skybox=null;AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene,"Assets/Viewer.unity");
            Directory.CreateDirectory(Path.GetDirectoryName(request.output));
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{"Assets/Viewer.unity"},target=target,locationPathName=request.output,options=BuildOptions.StrictMode|BuildOptions.CompressWithLz4});
            if(report.summary.result!=BuildResult.Succeeded)throw new BuildFailedException("Build failed: "+report.summary.result);
            Debug.Log($"[ScanViewer] Build verified; splats={asset.splatCount}; target={target}; OpenXR stereo configured. Headset performance requires a separate wear test.");
        }

        static Transform Hand(Transform parent,string name,Color color)
        {
            var hand=GameObject.CreatePrimitive(PrimitiveType.Cube);hand.name=name;hand.transform.SetParent(parent,false);hand.transform.localScale=Vector3.one*.07f;
            UnityEngine.Object.DestroyImmediate(hand.GetComponent<Collider>());
            var material=Save(new Material(Shader.Find("Universal Render Pipeline/Unlit")){color=color},name.Replace(" ",""));
            hand.GetComponent<Renderer>().sharedMaterial=material;return hand.transform;
        }
    }
}
