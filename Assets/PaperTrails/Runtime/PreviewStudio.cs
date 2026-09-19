using System.Collections.Generic;
using UnityEngine;

namespace PaperTrails
{
    public sealed class PreviewStudio : MonoBehaviour
    {
        readonly Dictionary<int,RenderTexture> icons=new Dictionary<int,RenderTexture>();
        readonly Dictionary<int,RenderTexture> largeIcons=new Dictionary<int,RenderTexture>();
        readonly Queue<(int skin,int size)> pending=new Queue<(int skin,int size)>();
        readonly HashSet<string> queued=new HashSet<string>();
        Texture2D placeholder;
        Camera cameraRig;
        Transform stage,machine,handle,ball;
        RenderTexture machineTexture;
        void Awake()
        {
            stage=new GameObject("Cosmetic studio").transform;stage.position=new Vector3(1000,0,1000);
            cameraRig=new GameObject("Preview camera").AddComponent<Camera>();cameraRig.enabled=false;cameraRig.orthographic=true;cameraRig.orthographicSize=1.1f;cameraRig.clearFlags=CameraClearFlags.SolidColor;cameraRig.backgroundColor=Color.clear;cameraRig.cullingMask=1<<8;
            placeholder=new Texture2D(4,4,TextureFormat.RGBA32,false);
            var fill=new Color32(26,37,48,255);
            for(int y=0;y<4;y++)for(int x=0;x<4;x++)placeholder.SetPixel(x,y,fill);
            placeholder.Apply();
        }
        static void Layer(Transform root){root.gameObject.layer=8;foreach(Transform c in root)Layer(c);}
        public Texture Icon(int skin)=>GetOrQueue(skin,96,icons);
        public Texture BigIcon(int skin)=>GetOrQueue(skin,256,largeIcons);
        Texture GetOrQueue(int skin,int size,Dictionary<int,RenderTexture> cache)
        {
            if(cache.TryGetValue(skin,out var texture))return texture;
            string key=skin+":"+size;
            if(queued.Add(key))pending.Enqueue((skin,size));
            return placeholder;
        }
        void Update()
        {
            // Rendering all 35 thumbnails at once (some are million-vertex
            // meshes) froze the Collection screen for seconds. Spread the work
            // so no single frame renders more than a few icons.
            int perFrame=Application.isMobilePlatform?1:3;
            for(int n=0;n<perFrame&&pending.Count>0;n++)
            {
                var job=pending.Dequeue();queued.Remove(job.skin+":"+job.size);
                if(job.size==96){if(!icons.ContainsKey(job.skin))icons[job.skin]=RenderIcon(job.skin,96);}
                else if(!largeIcons.ContainsKey(job.skin))largeIcons[job.skin]=RenderIcon(job.skin,256);
            }
        }
        RenderTexture RenderIcon(int skin,int size)
        {
            if(machine)machine.gameObject.SetActive(false);
            var model=SkinFactory.Create(skin,true,Color.white);model.transform.SetParent(stage,false);Layer(model.transform);
            var texture=new RenderTexture(size,size,16,RenderTextureFormat.ARGB32);texture.Create();
            cameraRig.orthographicSize=1.1f;cameraRig.transform.position=stage.position+new Vector3(1.6f,2.4f,2.6f);cameraRig.transform.LookAt(stage.position+Vector3.up*.55f);cameraRig.targetTexture=texture;cameraRig.Render();model.SetActive(false);Destroy(model);return texture;
        }
        public Texture Machine(float elapsed)
        {
            if(!machine)BuildMachine();machine.gameObject.SetActive(true);
            handle.localRotation=Quaternion.Euler(elapsed<2?elapsed*360:0,0,0);
            ball.localPosition=new Vector3(0,elapsed<2?Mathf.Lerp(.7f,.2f,Mathf.Clamp01(elapsed-1)): .2f,.68f);
            cameraRig.orthographicSize=2.1f;cameraRig.transform.position=stage.position+new Vector3(3,3.5f,6);cameraRig.transform.LookAt(stage.position+Vector3.up*1.5f);cameraRig.targetTexture=machineTexture;cameraRig.Render();return machineTexture;
        }
        void BuildMachine()
        {
            machine=new GameObject("Capsule machine").transform;machine.SetParent(stage,false);
            var red=SkinFactory.Material(GameView.Red);var silver=SkinFactory.Material(new Color(.7f,.8f,.85f),.7f);var glass=SkinFactory.Material(new Color(.5f,.8f,.95f,.18f));
            glass.SetFloat("_Mode",2);glass.SetOverrideTag("RenderType","Transparent");glass.SetInt("_SrcBlend",(int)UnityEngine.Rendering.BlendMode.SrcAlpha);glass.SetInt("_DstBlend",(int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);glass.SetInt("_ZWrite",0);glass.DisableKeyword("_ALPHAPREMULTIPLY_ON");glass.EnableKeyword("_ALPHABLEND_ON");glass.renderQueue=3000;
            SkinFactory.Part(machine,PrimitiveType.Cylinder,new Vector3(0,.15f,0),new Vector3(1.9f,.15f,1.9f),silver);
            SkinFactory.Part(machine,PrimitiveType.Cube,new Vector3(0,.75f,0),new Vector3(1.35f,1.15f,1.15f),red);
            SkinFactory.Part(machine,PrimitiveType.Sphere,new Vector3(0,2.1f,0),new Vector3(2.2f,2.2f,2.2f),glass);
            SkinFactory.Part(machine,PrimitiveType.Cylinder,new Vector3(0,3.18f,0),new Vector3(.9f,.12f,.9f),red);
            handle=SkinFactory.Part(machine,PrimitiveType.Cube,new Vector3(0,.95f,.65f),new Vector3(.7f,.13f,.15f),silver).transform;
            SkinFactory.Part(machine,PrimitiveType.Cube,new Vector3(0,.4f,.59f),new Vector3(.7f,.36f,.1f),SkinFactory.Material(new Color(.07f,.1f,.12f)));
            for(int i=0;i<16;i++){float angle=i*2.4f;SkinFactory.Part(machine,PrimitiveType.Sphere,new Vector3(Mathf.Cos(angle)*.65f,1.55f+i/6*.36f,Mathf.Sin(angle)*.65f),Vector3.one*.4f,SkinFactory.Material(Color.HSVToRGB(i/16f,.65f,1)));}
            ball=SkinFactory.Part(machine,PrimitiveType.Sphere,new Vector3(0,.2f,.68f),Vector3.one*.4f,SkinFactory.Material(new Color(1,.8f,.1f))).transform;
            Layer(machine);machineTexture=new RenderTexture(384,384,16);machineTexture.Create();
        }
        void OnDestroy(){foreach(var t in icons.Values)t.Release();foreach(var t in largeIcons.Values)t.Release();if(placeholder)Destroy(placeholder);if(machineTexture)machineTexture.Release();if(stage)Destroy(stage.gameObject);if(cameraRig)Destroy(cameraRig.gameObject);}
    }
}
