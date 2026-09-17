using System.Collections.Generic;
using PaperTrails.Core;
using UnityEngine;

namespace PaperTrails
{
    public sealed class GameView : MonoBehaviour
    {
        public static readonly Color Red=new Color(.94f,.19f,.29f),Blue=new Color(.12f,.55f,.96f);
        public static Color TeamColor(Team t)=>t==Team.Red?Red:Blue;
        GameSimulation game;
        GameObject root;
        readonly MeshFilter[] chunks=new MeshFilter[25];
        readonly GameObject[] bodies=new GameObject[10];
        readonly int[] skins=new int[10];
        readonly LineRenderer[] trails=new LineRenderer[10];
        readonly List<Vector3> ribbon=new List<Vector3>(512);
        readonly List<Vector3> trailPoints=new List<Vector3>(512);
        readonly List<GameObject> coins=new List<GameObject>();
        Material floorMaterial,coinMaterial;
        public RenderTexture Map {get;private set;}
        Camera cam,mapCamera;
        float nextMap;
        int local;
        GridPoint[] surface;
        public void Bind(GameSimulation simulation,int localId)
        {
            if(root)Destroy(root);if(Map)Destroy(Map);
            game=simulation;local=localId;root=new GameObject("Match visuals");coins.Clear();
            floorMaterial=new Material(Shader.Find("PaperTrails/Turf"));coinMaterial=SkinFactory.Material(new Color(1,.8f,.16f),.65f);
            for(int i=0;i<25;i++)
            {var o=new GameObject("Turf chunk "+i);o.layer=9;o.transform.SetParent(root.transform);chunks[i]=o.AddComponent<MeshFilter>();o.AddComponent<MeshRenderer>().sharedMaterial=floorMaterial;}
            for(int i=0;i<10;i++)
            {
                skins[i]=-1;
                var o=new GameObject("Trail "+i);o.transform.SetParent(root.transform);var line=o.AddComponent<LineRenderer>();
                Color trailColor=TeamColor(game.Players[i].Team);trailColor.a=.40f;
                line.sharedMaterial=new Material(Shader.Find("Sprites/Default"));line.startColor=line.endColor=trailColor;line.widthMultiplier=.48f;line.numCornerVertices=6;line.numCapVertices=6;trails[i]=line;
                bodies[i]=null;
            }
            for(int t=1;t<=2;t++)
            {
                int hub=game.Arena.Hubs[t-1];var mat=SkinFactory.Material(TeamColor((Team)t));
                var o=SkinFactory.Part(root.transform,PrimitiveType.Cylinder,Position(hub)+Vector3.up*.12f,new Vector3(5.4f,.13f,5.4f),mat);
                SkinFactory.Part(o.transform,PrimitiveType.Cylinder,new Vector3(0,1.2f,0),new Vector3(.7f,.3f,.7f),SkinFactory.Material(Color.white));
            }
            cam=Camera.main;
            if(!cam){var c=new GameObject("Camera");c.tag="MainCamera";cam=c.AddComponent<Camera>();c.AddComponent<AudioListener>();}
            cam.orthographic=true;cam.orthographicSize=7.5f;cam.nearClipPlane=.1f;cam.farClipPlane=250;
            cam.backgroundColor=new Color(.17f,.59f,.7f);cam.clearFlags=CameraClearFlags.SolidColor;
            cam.transform.rotation=Quaternion.Euler(58,0,0);cam.transform.position=new Vector3(game.Players[local].X,0,game.Players[local].Z)+new Vector3(0,32,-20);
            Map=new RenderTexture(320,320,16,RenderTextureFormat.ARGB32){antiAliasing=4};Map.Create();
            mapCamera=new GameObject("Minimap camera").AddComponent<Camera>();mapCamera.transform.SetParent(root.transform);mapCamera.enabled=false;mapCamera.orthographic=true;mapCamera.orthographicSize=40;mapCamera.transform.position=new Vector3(40,100,40);mapCamera.transform.rotation=Quaternion.Euler(90,0,0);mapCamera.clearFlags=CameraClearFlags.SolidColor;mapCamera.backgroundColor=Color.clear;mapCamera.cullingMask=1<<9;mapCamera.targetTexture=Map;
            surface=SmoothGrid.Build(game.Owners);foreach(int c in game.DirtyChunks)Rebuild(c);game.DirtyChunks.Clear();UpdateMap();
        }
        public static Vector3 Position(int cell)=>new Vector3(cell%Arena.Size+.5f,.15f,cell/Arena.Size+.5f);
        void LateUpdate()
        {
            if(game==null)return;
            if(game.DirtyChunks.Count>0)
            {
                surface=SmoothGrid.Build(game.Owners);
                var affected=new HashSet<int>();foreach(int c in game.DirtyChunks)for(int dz=-1;dz<=1;dz++)for(int dx=-1;dx<=1;dx++){int x=c%5+dx,z=c/5+dz;if(x>=0&&x<5&&z>=0&&z<5)affected.Add(z*5+x);}
                foreach(int c in affected)Rebuild(c);game.DirtyChunks.Clear();
            }
            for(int i=0;i<10;i++)
            {
                Player p=game.Players[i];
                if(skins[i]!=p.Skin)
                {if(bodies[i])Destroy(bodies[i]);bodies[i]=SkinFactory.Create(p.Skin,p.Human,TeamColor(p.Team));bodies[i].transform.SetParent(root.transform);bodies[i].transform.position=new Vector3(p.X,0,p.Z);skins[i]=p.Skin;}
                bodies[i].SetActive(p.Alive);
                var target=new Vector3(p.X,0,p.Z);
                bodies[i].transform.position=Vector3.Distance(bodies[i].transform.position,target)>8?target:Vector3.Lerp(bodies[i].transform.position,target,1-Mathf.Exp(-18*Time.deltaTime));
                if(p.DX*p.DX+p.DZ*p.DZ>.01f)bodies[i].transform.rotation=Quaternion.Slerp(bodies[i].transform.rotation,Quaternion.LookRotation(new Vector3(p.DX,0,p.DZ)),Time.deltaTime*15);
                DrawTrail(p,trails[i],bodies[i].transform.position);
            }
            var focus=bodies[local].transform.position;
            cam.orthographicSize=Mathf.Lerp(cam.orthographicSize,cam.aspect<1?8.4f:7.5f,1-Mathf.Exp(-8*Time.deltaTime));
            cam.transform.position=Vector3.Lerp(cam.transform.position,focus+new Vector3(0,32,-20),1-Mathf.Exp(-6*Time.deltaTime));
            var list=game.Coins[local];
            while(coins.Count<list.Count)coins.Add(SkinFactory.Part(root.transform,PrimitiveType.Sphere,Vector3.zero,new Vector3(.5f,.65f,.18f),coinMaterial));
            for(int i=0;i<coins.Count;i++)
            {coins[i].SetActive(i<list.Count);if(i<list.Count){coins[i].transform.position=Position(list[i])+Vector3.up*(.6f+Mathf.Sin(Time.time*3+i)*.13f);coins[i].transform.rotation=Quaternion.Euler(0,Time.time*100,0);}}
            if(Time.unscaledTime>=nextMap){nextMap=Time.unscaledTime+.1f;UpdateMap();}
        }
        void UpdateMap()
        {
            mapCamera.Render();
        }
        void DrawTrail(Player p,LineRenderer line,Vector3 head)
        {
            ribbon.Clear();trailPoints.Clear();head.y=.19f;
            if(p.TrailPath.Count==0){line.positionCount=0;return;}
            // Trim the network/interpolation lead so the ribbon never doubles back past its character.
            int end=p.TrailPath.Count-1;
            float best=float.MaxValue;
            for(int i=p.TrailPath.Count-1;i>=Mathf.Max(0,p.TrailPath.Count-12);i--)
            {
                GridPoint point=p.TrailPath[i];float d=(head-new Vector3(point.X,.19f,point.Z)).sqrMagnitude;
                if(d<best){best=d;end=i;}
            }
            for(int i=0;i<=end;i++)trailPoints.Add(new Vector3(p.TrailPath[i].X,.19f,p.TrailPath[i].Z));
            trailPoints.Add(head);
            if(trailPoints.Count==2){ribbon.Add(trailPoints[0]);ribbon.Add(trailPoints[1]);}
            else
            {
                ribbon.Add(trailPoints[0]);
                for(int i=0;i<trailPoints.Count-1;i++)
                {
                    Vector3 p0=i>0?trailPoints[i-1]:trailPoints[i],p1=trailPoints[i],p2=trailPoints[i+1],p3=i+2<trailPoints.Count?trailPoints[i+2]:p2;
                    for(int sample=1;sample<=3;sample++)ribbon.Add(SmoothPoint(p0,p1,p2,p3,sample/3f));
                }
            }
            line.positionCount=ribbon.Count;
            for(int i=0;i<ribbon.Count;i++)line.SetPosition(i,ribbon[i]);
        }
        static Vector3 SmoothPoint(Vector3 p0,Vector3 p1,Vector3 p2,Vector3 p3,float t)
        {
            const float tension=.32f;float t2=t*t,t3=t2*t;
            Vector3 m1=(p2-p0)*tension,m2=(p3-p1)*tension;
            return (2*t3-3*t2+1)*p1+(t3-2*t2+t)*m1+(-2*t3+3*t2)*p2+(t3-t2)*m2;
        }
        void Rebuild(int chunk)
        {
            var v=new List<Vector3>();var colors=new List<Color>();var triangles=new List<int>();
            for(int z=chunk/5*16;z<chunk/5*16+16;z++)
            for(int x=chunk%5*16;x<chunk%5*16+16;x++)
            {
                int i=z*Arena.Size+x;if(!game.Arena.Mask[i])continue;
                Color c=game.Owners[i]==Team.Neutral?new Color(.91f,.99f,.96f):TeamColor(game.Owners[i]);
                Vector3 a=Vertex(x,z),b=Vertex(x,z+1),cc=Vertex(x+1,z+1),d=Vertex(x+1,z);
                Quad(a,b,cc,d,c,v,colors,triangles);
                if(!game.Arena.Playable(x-1,z))Quad(a-Vector3.up,b-Vector3.up,b,a,c*.6f,v,colors,triangles);
                if(!game.Arena.Playable(x+1,z))Quad(cc-Vector3.up,d-Vector3.up,d,cc,c*.6f,v,colors,triangles);
                if(!game.Arena.Playable(x,z-1))Quad(d-Vector3.up,a-Vector3.up,a,d,c*.6f,v,colors,triangles);
                if(!game.Arena.Playable(x,z+1))Quad(b-Vector3.up,cc-Vector3.up,cc,b,c*.6f,v,colors,triangles);
            }
            if(chunks[chunk].sharedMesh)Destroy(chunks[chunk].sharedMesh);
            var mesh=new Mesh();mesh.SetVertices(v);mesh.SetColors(colors);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();chunks[chunk].sharedMesh=mesh;
        }
        Vector3 Vertex(int x,int z){GridPoint p=surface[z*SmoothGrid.Width+x];return new Vector3(p.X,0,p.Z);}
        static void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Color color,List<Vector3> v,List<Color> colors,List<int> t)
        {int n=v.Count;v.Add(a);v.Add(b);v.Add(c);v.Add(d);for(int i=0;i<4;i++)colors.Add(color);t.Add(n);t.Add(n+1);t.Add(n+2);t.Add(n);t.Add(n+2);t.Add(n+3);}
    }
}
