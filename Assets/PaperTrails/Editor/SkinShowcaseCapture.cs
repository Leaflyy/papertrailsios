using System.IO;
using UnityEditor;
using UnityEngine;

namespace PaperTrails.Editor
{
    public static class SkinShowcaseCapture
    {
        [MenuItem("PaperTrails/Capture normalized skin showcase")]
        public static void Capture()
        {
            string output=Path.Combine(Directory.GetParent(Application.dataPath).FullName,"Builds","Screenshots","SkinShowcase");
            Directory.CreateDirectory(output);
            RenderSettings.ambientLight=new Color(.48f,.52f,.58f);

            var cameraObject=new GameObject("Skin showcase camera");var camera=cameraObject.AddComponent<Camera>();
            camera.orthographic=true;camera.orthographicSize=1.7f;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.035f,.075f,.105f);
            camera.transform.position=new Vector3(2.35f,2.75f,-3.8f);camera.transform.LookAt(new Vector3(0,.48f,0));
            var lightObject=new GameObject("Skin showcase light");var light=lightObject.AddComponent<Light>();
            light.type=LightType.Directional;light.intensity=1.35f;light.color=new Color(1,.92f,.82f);lightObject.transform.rotation=Quaternion.Euler(48,-32,18);

            const int size=384;
            for(int skin=0;skin<SkinFactory.Names.Length;skin++)
            {
                Color team=skin%2==0?GameView.Red:GameView.Blue;
                var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.name="Turf";floor.transform.position=new Vector3(0,-.09f,0);floor.transform.localScale=new Vector3(3.3f,.12f,3.3f);floor.GetComponent<Renderer>().sharedMaterial=SkinFactory.Material(new Color(.74f,.91f,.86f));UnityEngine.Object.DestroyImmediate(floor.GetComponent<Collider>());
                var trailObject=new GameObject("Trail");var trail=trailObject.AddComponent<LineRenderer>();trail.sharedMaterial=new Material(Shader.Find("Sprites/Default"));Color tc=team;tc.a=.58f;trail.startColor=trail.endColor=tc;trail.widthMultiplier=.34f;trail.numCornerVertices=6;trail.numCapVertices=6;trail.positionCount=5;
                trail.SetPositions(new[]{new Vector3(-.72f,.01f,-1.55f),new Vector3(-.58f,.03f,-1.12f),new Vector3(-.28f,.06f,-.72f),new Vector3(-.12f,.1f,-.35f),new Vector3(0,.16f,0)});
                var avatar=SkinFactory.Create(skin,true,team);avatar.transform.position=Vector3.zero;avatar.transform.rotation=Quaternion.LookRotation(new Vector3(.32f,0,.95f));

                var target=new RenderTexture(size,size,24,RenderTextureFormat.ARGB32){antiAliasing=4};target.Create();camera.targetTexture=target;camera.Render();RenderTexture.active=target;
                var image=new Texture2D(size,size,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,size,size),0,0);image.Apply();
                File.WriteAllBytes(Path.Combine(output,skin.ToString("00")+".png"),image.EncodeToPNG());
                RenderTexture.active=null;camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(image);target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(avatar);UnityEngine.Object.DestroyImmediate(trailObject);UnityEngine.Object.DestroyImmediate(floor);
            }
            UnityEngine.Object.DestroyImmediate(cameraObject);UnityEngine.Object.DestroyImmediate(lightObject);
            Debug.Log("PAPERTRAILS_SKIN_SHOWCASE_OK count="+SkinFactory.Names.Length+" path="+output);
        }
    }
}
