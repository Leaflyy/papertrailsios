using System;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace PaperTrails.Editor
{
    public static class ProjectBuilder
    {
        [MenuItem("PaperTrails/Create startup scene")]
        public static void Setup()
        {
            System.IO.Directory.CreateDirectory("Assets/PaperTrails/Scenes");
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene,"Assets/PaperTrails/Scenes/PaperTrails.unity");
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene("Assets/PaperTrails/Scenes/PaperTrails.unity",true)};
            PlayerSettings.companyName="PaperTrails";PlayerSettings.productName="PaperTrails";PlayerSettings.bundleVersion="0.1.0";
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android,"com.papertrails.game");
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.iOS,"com.papertrails.game");
            PlayerSettings.defaultInterfaceOrientation=UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait=true;PlayerSettings.allowedAutorotateToPortraitUpsideDown=true;PlayerSettings.allowedAutorotateToLandscapeLeft=true;PlayerSettings.allowedAutorotateToLandscapeRight=true;
            PlayerSettings.defaultScreenWidth=1280;PlayerSettings.defaultScreenHeight=800;PlayerSettings.fullScreenMode=FullScreenMode.Windowed;
            PlayerSettings.runInBackground=true;
            var settings=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
            settings.FindProperty("cloudProjectId").stringValue="ee6f361f-8676-4b2e-a668-87487892d59c";
            settings.FindProperty("projectName").stringValue="PaperTrails";
            settings.FindProperty("organizationId").stringValue="2475153869932";
            settings.ApplyModifiedPropertiesWithoutUndo();
            var input=settings.FindProperty("activeInputHandler");if(input!=null){input.intValue=0;settings.ApplyModifiedPropertiesWithoutUndo();}
            var graphics=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
            var list=graphics.FindProperty("m_AlwaysIncludedShaders");
            foreach(string name in new[]{"PaperTrails/Turf","Standard","Sprites/Default"})
            {
                var shader=Shader.Find(name);if(!shader)throw new Exception("Missing shader: "+name);bool found=false;
                for(int i=0;i<list.arraySize;i++)if(list.GetArrayElementAtIndex(i).objectReferenceValue==shader)found=true;
                if(!found){list.InsertArrayElementAtIndex(list.arraySize);list.GetArrayElementAtIndex(list.arraySize-1).objectReferenceValue=shader;}
            }
            graphics.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
        }
        [MenuItem("PaperTrails/Build Windows")]
        public static void Windows(){Setup();Build("Builds/Windows/PaperTrails.exe",BuildTarget.StandaloneWindows64);}
        [MenuItem("PaperTrails/Build Android APK")]
        public static void Android(){Setup();PlayerSettings.Android.applicationEntry=AndroidApplicationEntry.Activity;PlayerSettings.Android.minSdkVersion=AndroidSdkVersions.AndroidApiLevel26;PlayerSettings.Android.targetArchitectures=AndroidArchitecture.ARM64;PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android,ScriptingImplementation.IL2CPP);PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android,false);PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,new[]{GraphicsDeviceType.OpenGLES3});Build("Builds/Android/PaperTrails.apk",BuildTarget.Android);}
        [MenuItem("PaperTrails/Build iOS Xcode")]
        public static void iOS(){Setup();PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.iOS,ScriptingImplementation.IL2CPP);PlayerSettings.iOS.sdkVersion=iOSSdkVersion.DeviceSDK;Build("Builds/iOS",BuildTarget.iOS);}
        [MenuItem("PaperTrails/Validate avatar sizes")]
        public static void ValidateAvatarSizes()
        {
            for(int skin=0;skin<SkinFactory.Names.Length;skin++)
            {
                var avatar=SkinFactory.Create(skin,true,Color.red);var renderers=avatar.GetComponentsInChildren<Renderer>();bool found=false;Bounds bounds=new Bounds();
                foreach(var renderer in renderers)if(renderer.gameObject.name!="TeamRing"){if(!found){bounds=renderer.bounds;found=true;}else bounds.Encapsulate(renderer.bounds);}
                float maximum=Mathf.Max(bounds.size.x,Mathf.Max(bounds.size.y,bounds.size.z));UnityEngine.Object.DestroyImmediate(avatar);
                float expected=SkinFactory.TargetVisualSize(skin);
                if(!found||Mathf.Abs(maximum-expected)>.02f)throw new Exception("Avatar size normalization failed for "+SkinFactory.Names[skin]+": "+maximum+" (expected "+expected+")");
            }
            var cpu=SkinFactory.Create(0,false,Color.red);var cpuRenderers=cpu.GetComponentsInChildren<Renderer>();Bounds cpuBounds=new Bounds();bool cpuFound=false;
            foreach(var renderer in cpuRenderers)if(!cpuFound){cpuBounds=renderer.bounds;cpuFound=true;}else cpuBounds.Encapsulate(renderer.bounds);
            float cpuMaximum=Mathf.Max(cpuBounds.size.x,Mathf.Max(cpuBounds.size.y,cpuBounds.size.z));UnityEngine.Object.DestroyImmediate(cpu);
            if(!cpuFound||Mathf.Abs(cpuMaximum-SkinFactory.TargetVisualSize(0))>.02f)throw new Exception("CPU cube size normalization failed: "+cpuMaximum);
            Debug.Log("PAPERTRAILS_AVATAR_SIZES_OK count="+SkinFactory.Names.Length+" cpu="+cpuMaximum);
        }
        static void Build(string path,BuildTarget target)
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            BuildReport report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{"Assets/PaperTrails/Scenes/PaperTrails.unity"},locationPathName=path,target=target,options=BuildOptions.None});
            if(report.summary.result!=BuildResult.Succeeded)throw new Exception("Build failed: "+report.summary.result);
            Debug.Log("PAPERTRAILS_BUILD_OK "+path);
        }
    }
}
