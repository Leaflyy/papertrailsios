using System.IO;
using UnityEditor;
using UnityEngine;

namespace PaperTrails.Editor
{
    public static class StarRenderCapture
    {
        public static void Capture()
        {
            var root=SkinFactory.Create(4,true,new Color(.93f,.18f,.58f));
            root.transform.position=Vector3.zero;

            var floor=GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.transform.position=new Vector3(0,-.04f,0);
            floor.transform.localScale=new Vector3(2.4f,1,2.4f);
            floor.GetComponent<Renderer>().sharedMaterial=SkinFactory.Material(new Color(.82f,.94f,.9f));
            Object.DestroyImmediate(floor.GetComponent<Collider>());

            var lightObject=new GameObject("Key Light");
            var key=lightObject.AddComponent<Light>();
            key.type=LightType.Directional;key.intensity=1.35f;key.color=new Color(1,.9f,.82f);
            lightObject.transform.rotation=Quaternion.Euler(45,-35,25);
            RenderSettings.ambientLight=new Color(.34f,.4f,.42f);

            var cameraObject=new GameObject("Render Camera");
            var camera=cameraObject.AddComponent<Camera>();
            camera.orthographic=true;camera.orthographicSize=1.65f;
            camera.backgroundColor=new Color(.07f,.12f,.14f);camera.clearFlags=CameraClearFlags.SolidColor;
            camera.transform.position=new Vector3(2.35f,2.65f,-3.7f);
            camera.transform.LookAt(new Vector3(0,.42f,0));

            const int width=900,height=900;
            var texture=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32);
            camera.targetTexture=texture;camera.Render();RenderTexture.active=texture;
            var image=new Texture2D(width,height,TextureFormat.RGBA32,false);
            image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();
            var output=Path.Combine(Directory.GetParent(Application.dataPath).FullName,"Builds","Screenshots","cat-render-v3.png");
            Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllBytes(output,image.EncodeToPNG());
            Debug.Log("PAPERTRAILS_STAR_RENDER_OK "+output);
            RenderTexture.active=null;camera.targetTexture=null;
            Object.DestroyImmediate(image);Object.DestroyImmediate(texture);Object.DestroyImmediate(cameraObject);Object.DestroyImmediate(lightObject);Object.DestroyImmediate(floor);Object.DestroyImmediate(root);
        }
    }
}
