using UnityEngine;

namespace PaperTrails
{
    public static class SkinFactory
    {
        public static readonly string[] Names={"Cube","Sphere","Star","Heart","Cat","Dog","Husky","Duck","Shark","Penguin","Frog","Dinosaur","Bee","Sports Car","Tank","UFO","Rocket","Robot","Astronaut","Dragon","Ghost","Slime","Burger","Pizza","Donut","Banana","Traffic Cone","Rubber Duck","Eyeball","Knife","Bronze Block","Silver Block","Gold Block","Galaxy Cube"};
        static readonly System.Collections.Generic.Dictionary<int,string> PremiumModels=new System.Collections.Generic.Dictionary<int,string>{{4,"Skins/Cat/Cat"},{5,"Skins/Dog/Dog"},{6,"Skins/Husky/Husky"},{7,"Skins/Duck/Duck"},{8,"Skins/Shark/Shark"},{9,"Skins/Penguin/Penguin"},{10,"Skins/Frog/Frog"},{11,"Skins/Dinosaur/Dinosaur"},{12,"Skins/Bee/Bee"},{13,"Skins/SportsCar/SportsCar"},{14,"Skins/Tank/Tank"},{15,"Skins/UFO/UFO"},{17,"Skins/Robot/Robot"}};
        static readonly System.Collections.Generic.Dictionary<int,float> PremiumYaw=new System.Collections.Generic.Dictionary<int,float>{{4,0f},{6,0f},{13,-90f},{14,-90f},{17,0f}};
        static readonly System.Collections.Generic.Dictionary<int,float> PremiumSize=new System.Collections.Generic.Dictionary<int,float>{{4,1.5f},{5,1.5f},{6,1.5f},{7,1.5f},{8,1.5f},{9,1.5f},{10,1.5f},{11,1.5f},{12,1.5f},{13,1.3f},{14,1.3f},{15,1.3f},{17,1.6f}};
        static void WirePremiumTextures(GameObject model,int skin)
        {
            // The FBX material import loses its texture links when the model file
            // is renamed, so wire the PBR set explicitly. Paths match
            // Resources/Skins/<Skin>/<Skin>_{Albedo,Normal,Metallic}.png.
            // Mutating sharedMaterial is idempotent across respawns, no leaks.
            string folder;
            if(!PremiumModels.TryGetValue(skin,out folder))return;
            var albedo=Resources.Load<Texture2D>(folder+"_Albedo");
            var normal=Resources.Load<Texture2D>(folder+"_Normal");
            var metallic=Resources.Load<Texture2D>(folder+"_Metallic");
            bool metal=skin==13||skin==14||skin==15||skin==17;
            foreach(var r in model.GetComponentsInChildren<Renderer>())
            {
                var m=r.sharedMaterial;
                if(m==null)continue;
                if(albedo!=null)m.mainTexture=albedo;
                if(normal!=null){m.SetTexture("_BumpMap",normal);m.EnableKeyword("_NORMALMAP");}
                if(metal)
                {
                    if(metallic!=null)m.SetTexture("_MetallicGlossMap",metallic);
                    m.SetFloat("_Metallic",1f);m.SetFloat("_Glossiness",.6f);
                }
                else m.SetFloat("_Metallic",0f);
            }
        }
        static GameObject TryLoadPremium(int skin,Color team)
        {
            if(!PremiumModels.TryGetValue(skin,out var path))return null;
            var prefab=Resources.Load<GameObject>(path);
            if(prefab==null)return null;
            // Fresh identity root: the Meshy FBX root carries a x100 cm-conversion
            // scale plus a -90-degree Z-up relic tilt. Parenting meter-authored
            // parts (team ring) under that TRS blows up bounds and shrinks the
            // model to ~1cm, so the model keeps its TRS on a child instead.
            var root=new GameObject(Names[skin]);
            var model=Object.Instantiate(prefab);
            model.name="Model";
            model.transform.SetParent(root.transform,false);
            model.transform.localPosition=Vector3.zero;
            float yaw=PremiumYaw.TryGetValue(skin,out var y)?y:0f;
            model.transform.localRotation=Quaternion.Euler(0,yaw,0)*model.transform.localRotation;
            foreach(var c in model.GetComponentsInChildren<Collider>())Object.Destroy(c);
            foreach(var r in model.GetComponentsInChildren<Renderer>())r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.On;
            WirePremiumTextures(model,skin);
            NormalizeSize(model);
            if(PremiumSize.TryGetValue(skin,out var ps))model.transform.localScale*=ps;
            var renderers=model.GetComponentsInChildren<Renderer>();
            if(renderers.Length>0)
            {
                Bounds bounds=renderers[0].bounds;
                for(int i=1;i<renderers.Length;i++)bounds.Encapsulate(renderers[i].bounds);
                model.transform.position+=new Vector3(0,.09f-bounds.min.y,0);
            }
            Part(root.transform,PrimitiveType.Cylinder,new Vector3(0,.08f,0),new Vector3(1.25f,.035f,1.25f),Material(team));
            return root;
        }
        public static Material Material(Color c,float metallic=0)
        {var m=new Material(Shader.Find("Standard"));m.color=c;m.SetFloat("_Metallic",metallic);m.SetFloat("_Glossiness",.35f+metallic*.4f);return m;}
        public static GameObject Part(Transform parent,PrimitiveType shape,Vector3 pos,Vector3 scale,Material material)
        {
            var o=GameObject.CreatePrimitive(shape);o.transform.SetParent(parent,false);o.transform.localPosition=pos;o.transform.localScale=scale;o.GetComponent<Renderer>().sharedMaterial=material;
            Object.Destroy(o.GetComponent<Collider>());return o;
        }
        public static GameObject Create(int skin,bool human,Color team)
        {
            if(human&&PremiumModels.ContainsKey(skin))
            {
                var premium=TryLoadPremium(skin,team);
                if(premium!=null)return premium;
            }
            var root=new GameObject(human?Names[skin]:"CPU");
            var white=Material(new Color(.93f,.95f,.97f));var black=Material(new Color(.08f,.10f,.13f));
            Color[] palette={new Color(.95f,.73f,.16f),new Color(.28f,.85f,.65f),new Color(.96f,.42f,.67f),new Color(.75f,.8f,.87f)};
            var body=Material(human?palette[skin%4]:team);var dark=Material(new Color(.26f,.31f,.36f));
            if(skin==3)body.color=new Color(.92f,.08f,.12f);
            if(skin==7||skin==12||skin==25||skin==27)body.color=new Color(1,.8f,.15f);
            if(skin==10||skin==11||skin==19||skin==21)body.color=new Color(.23f,.8f,.42f);
            void Add(PrimitiveType shape,float x,float y,float z,float sx,float sy,float sz,Material m)=>Part(root.transform,shape,new Vector3(x,y,z),new Vector3(sx,sy,sz),m);
            if(!human){Add(PrimitiveType.Cube,0,.43f,0,.8f,.72f,.8f,body);NormalizeSize(root);return root;}
            Add(PrimitiveType.Cylinder,0,.08f,0,1.25f,.035f,1.25f,Material(team));
            if(skin==15){Add(PrimitiveType.Sphere,0,.4f,0,1.45f,.28f,1.45f,dark);Add(PrimitiveType.Sphere,0,.63f,0,.7f,.6f,.7f,body);}
            else if(skin==20||skin==21)
            {Add(PrimitiveType.Sphere,0,.52f,0,1.1f,.8f,1.05f,skin==20?white:body);for(int i=-1;i<=1;i+=2)Add(PrimitiveType.Sphere,i*.22f,.7f,.43f,.15f,.2f,.1f,black);if(skin==20)for(int i=-1;i<=1;i++)Add(PrimitiveType.Sphere,i*.33f,.19f,0,.4f,.4f,.65f,white);}
            else if(skin==23)
            {Wedge(root.transform,new[]{new Vector2(-.6f,-.5f),new Vector2(.6f,-.5f),new Vector2(0,.75f)},.2f,Material(new Color(1,.77f,.25f)));Add(PrimitiveType.Sphere,-.22f,.26f,-.12f,.23f,.07f,.23f,Material(new Color(.85f,.15f,.14f)));Add(PrimitiveType.Sphere,.2f,.26f,-.24f,.23f,.07f,.23f,Material(new Color(.85f,.15f,.14f)));}
            else if(skin==25)
            {for(int i=0;i<7;i++){float a=-1+i/6f*2;Add(PrimitiveType.Sphere,Mathf.Cos(a)*.7f-.4f,.48f,Mathf.Sin(a)*.7f,.35f,.35f,.35f,body);}Add(PrimitiveType.Cube,0,.48f,.67f,.18f,.18f,.18f,dark);}
            else if(skin==1){Add(PrimitiveType.Sphere,0,.62f,0,1,1,1,body);}
            else if(skin==13||skin==14)
            {Add(PrimitiveType.Cube,0,.38f,0,.9f,.35f,1.5f,body);Add(PrimitiveType.Cube,0,.65f,0,.65f,.3f,.65f,dark);for(int i=-1;i<=1;i+=2)for(int j=-1;j<=1;j+=2)Add(PrimitiveType.Sphere,i*.46f,.28f,j*.47f,.32f,.36f,.36f,black);if(skin==14)Add(PrimitiveType.Cube,0,.7f,.6f,.17f,.17f,.85f,dark);}
            else if(skin==29){Add(PrimitiveType.Cube,0,.35f,-.35f,.3f,.3f,.55f,black);Add(PrimitiveType.Cube,0,.45f,.25f,.16f,.5f,.8f,white);}
            else if(skin>=30)
            {
                Color c=skin==30?new Color(.45f,.27f,.13f):skin==31?new Color(.65f,.75f,.8f):skin==32?new Color(1,.67f,.09f):new Color(.17f,.12f,.34f);
                Add(PrimitiveType.Cube,0,.6f,0,.95f,.95f,.95f,Material(c,.8f));
                for(int i=0;i<6;i++)Add(PrimitiveType.Cube,(i%3-1)*.28f,1.09f,(i/3-.5f)*.46f,.15f,.04f,.15f,skin==33?white:Material(c*.75f,.6f));
            }
            else if(skin==4)
            {
                var gray=Material(new Color(.48f,.5f,.55f));var light=Material(new Color(.9f,.9f,.92f));var pink=Material(new Color(1,.42f,.46f));
                Add(PrimitiveType.Cube,0,.58f,0,1.12f,.9f,.92f,gray);Add(PrimitiveType.Cube,0,1.1f,-.18f,1.02f,.82f,.72f,gray);
                Add(PrimitiveType.Cube,0,1.02f,-.57f,.62f,.5f,.18f,light);Add(PrimitiveType.Cube,0,1.08f,-.68f,.16f,.16f,.08f,pink);
                for(int side=-1;side<=1;side+=2){Add(PrimitiveType.Cube,side*.28f,1.22f,-.56f,.14f,.16f,.08f,black);Add(PrimitiveType.Cube,side*.39f,1.62f,-.18f,.28f,.7f,.28f,gray);Add(PrimitiveType.Cube,side*.39f,1.5f,-.3f,.18f,.18f,.08f,pink);Add(PrimitiveType.Cube,side*.38f,.05f,.28f,.34f,.3f,.45f,light);}
                for(int side=-1;side<=1;side+=2){for(int stripe=0;stripe<2;stripe++)Add(PrimitiveType.Cube,side*.53f,.9f,-.58f,.08f,.08f,.2f,dark);}
                for(int segment=0;segment<3;segment++){var tail=Part(root.transform,PrimitiveType.Cube,new Vector3(.62f,.72f,-.48f-segment*.28f),new Vector3(.32f,.32f,.7f),gray);tail.transform.localRotation=Quaternion.Euler(-18+segment*18,0,segment*18);}
            }
            else if(skin==5||skin==6||skin==9||skin==10||skin==11||skin==19)
            {
                bool husky=skin==6;var coat=husky?dark:skin==9?black:body;
                Add(PrimitiveType.Sphere,0,.55f,0,.8f,.8f,1.0f,coat);Add(PrimitiveType.Sphere,0,.88f,.4f,.74f,.7f,.65f,coat);
                Add(PrimitiveType.Sphere,0,.71f,.68f,.56f,.4f,.28f,white);
                for(int i=-1;i<=1;i+=2){Add(PrimitiveType.Cube,i*.25f,1.2f,.37f,.18f,.38f,.23f,coat);Add(PrimitiveType.Sphere,i*.18f,.98f,.68f,.1f,.1f,.08f,black);}
                var tail=Part(root.transform,PrimitiveType.Capsule,new Vector3(0,.8f,-.5f),new Vector3(.24f,.4f,.24f),husky?white:coat);tail.transform.localRotation=Quaternion.Euler(55,0,0);
                if(husky){Add(PrimitiveType.Sphere,0,.4f,.28f,.7f,.64f,.5f,white);Add(PrimitiveType.Sphere,0,1.08f,-.57f,.42f,.35f,.4f,white);for(int i=-1;i<=1;i+=2)Add(PrimitiveType.Sphere,i*.3f,.2f,.3f,.27f,.25f,.35f,white);}
                if(skin==9){Add(PrimitiveType.Sphere,0,.54f,.41f,.6f,.65f,.23f,white);Add(PrimitiveType.Cube,0,.82f,.79f,.2f,.18f,.22f,Material(new Color(1,.7f,.1f)));}
                if(skin==10){for(int i=-1;i<=1;i+=2){Add(PrimitiveType.Sphere,i*.3f,1.16f,.5f,.35f,.35f,.35f,white);Add(PrimitiveType.Sphere,i*.3f,1.17f,.66f,.17f,.17f,.1f,black);}}
                if(skin==19){for(int side=-1;side<=1;side+=2){var wing=Part(root.transform,PrimitiveType.Cube,new Vector3(side*.65f,.85f,-.1f),new Vector3(.9f,.1f,.7f),body);wing.transform.localRotation=Quaternion.Euler(0,side*25,side*25);}}
            }
            else if(skin==7||skin==27||skin==12||skin==8)
            {Add(PrimitiveType.Sphere,0,.5f,0,1,.7f,1.15f,body);Add(PrimitiveType.Sphere,0,.84f,.35f,.6f,.6f,.6f,body);Add(PrimitiveType.Cube,0,.78f,.7f,.4f,.14f,.3f,Material(new Color(1,.4f,.08f)));if(skin==12){Add(PrimitiveType.Sphere,-.55f,.8f,0,.5f,.13f,.6f,white);Add(PrimitiveType.Sphere,.55f,.8f,0,.5f,.13f,.6f,white);Add(PrimitiveType.Cube,0,.5f,-.15f,1.02f,.6f,.16f,black);}if(skin==8){var fin=Part(root.transform,PrimitiveType.Cube,new Vector3(0,1.06f,-.1f),new Vector3(.12f,.55f,.48f),dark);fin.transform.localRotation=Quaternion.Euler(-25,0,0);}}
            else if(skin==17||skin==18||skin==16)
            {Add(PrimitiveType.Capsule,0,.6f,0,.75f,.5f,.75f,white);Add(PrimitiveType.Sphere,0,1.05f,0,.75f,.7f,.75f,dark);Add(PrimitiveType.Cube,0,1.05f,.34f,.48f,.28f,.12f,body);}
            else if(skin==22){Add(PrimitiveType.Cylinder,0,.4f,0,1.1f,.2f,1.1f,Material(new Color(.92f,.55f,.17f)));Add(PrimitiveType.Cylinder,0,.45f,0,1.16f,.04f,1.16f,Material(new Color(.24f,.65f,.24f)));Add(PrimitiveType.Cylinder,0,.57f,0,1.08f,.05f,1.08f,dark);}
            else if(skin==24){for(int i=0;i<12;i++){float a=i*Mathf.PI/6;Add(PrimitiveType.Sphere,Mathf.Cos(a)*.43f,.45f,Mathf.Sin(a)*.43f,.4f,.4f,.4f,body);}}
            else if(skin==2){var points=new Vector2[10];for(int i=0;i<10;i++){float a=Mathf.PI/2+i*Mathf.PI/5;points[i]=new Vector2(Mathf.Cos(a),Mathf.Sin(a))*(i%2==0?.8f:.36f);}Wedge(root.transform,points,.4f,body);}
            else if(skin==3){Heart(root.transform,body);}
            else if(skin==26){for(int i=0;i<5;i++)Add(PrimitiveType.Cylinder,0,.2f+i*.17f,0,.8f-i*.14f,.09f,.8f-i*.14f,i%2==0?body:white);}
            else {Add(skin==0?PrimitiveType.Cube:PrimitiveType.Sphere,0,.6f,0,.95f,.95f,.95f,body);if(skin==28){Add(PrimitiveType.Sphere,0,.65f,.43f,.5f,.5f,.2f,white);Add(PrimitiveType.Sphere,0,.65f,.55f,.23f,.23f,.12f,black);}}
            NormalizeSize(root);
            return root;
        }
        static void NormalizeSize(GameObject root)
        {
            var renderers=root.GetComponentsInChildren<Renderer>();
            if(renderers.Length==0)return;
            Bounds bounds=renderers[0].bounds;
            for(int i=1;i<renderers.Length;i++)bounds.Encapsulate(renderers[i].bounds);
            float maximum=Mathf.Max(bounds.size.x,Mathf.Max(bounds.size.y,bounds.size.z));
            if(maximum>.01f)root.transform.localScale*=.8f/maximum;
        }
        static GameObject Heart(Transform root,Material material)
        {
            var p=new[]{new Vector2(-.62f,.18f),new Vector2(-.64f,.34f),new Vector2(-.63f,.48f),new Vector2(-.56f,.59f),new Vector2(-.45f,.68f),new Vector2(-.31f,.74f),new Vector2(-.18f,.73f),new Vector2(-.07f,.67f),new Vector2(0,.55f),new Vector2(.07f,.67f),new Vector2(.18f,.73f),new Vector2(.31f,.74f),new Vector2(.45f,.68f),new Vector2(.56f,.59f),new Vector2(.63f,.48f),new Vector2(.64f,.34f),new Vector2(.62f,.18f),new Vector2(.5f,-.02f),new Vector2(.38f,-.2f),new Vector2(.25f,-.39f),new Vector2(.13f,-.56f),new Vector2(0,-.7f),new Vector2(-.13f,-.56f),new Vector2(-.25f,-.39f),new Vector2(-.38f,-.2f),new Vector2(-.5f,-.02f)};
            float d=.22f;var v=new System.Collections.Generic.List<Vector3>();var t=new System.Collections.Generic.List<int>();
            v.Add(new Vector3(0,0,-d));for(int i=0;i<p.Length;i++)v.Add(new Vector3(p[i].x,p[i].y,-d));
            v.Add(new Vector3(0,0,d));for(int i=0;i<p.Length;i++)v.Add(new Vector3(p[i].x,p[i].y,d));
            for(int i=0;i<p.Length;i++){int n=(i+1)%p.Length;t.Add(0);t.Add(1+i);t.Add(1+n);t.Add(1+p.Length);t.Add(2+p.Length+n);t.Add(2+p.Length+i);int a=1+i,b=1+n,c=2+p.Length+i,e=2+p.Length+n;t.Add(a);t.Add(c);t.Add(b);t.Add(b);t.Add(c);t.Add(e);}
            var mesh=new Mesh();mesh.SetVertices(v);mesh.SetTriangles(t,0);mesh.RecalculateNormals();var o=new GameObject("HeartSilhouette");o.transform.SetParent(root,false);o.transform.localPosition=new Vector3(0,.72f,0);o.AddComponent<MeshFilter>().sharedMesh=mesh;material.SetInt("_Cull",0);o.AddComponent<MeshRenderer>().sharedMaterial=material;return o;
        }
        static GameObject Wedge(Transform root,Vector2[] outline,float height,Material material)
        {
            var v=new System.Collections.Generic.List<Vector3>();var t=new System.Collections.Generic.List<int>();
            for(int i=0;i<outline.Length;i++)
            {
                var a=outline[i];var b=outline[(i+1)%outline.Length];int n=v.Count;
                v.Add(new Vector3(0,height,0));v.Add(new Vector3(b.x,height,b.y));v.Add(new Vector3(a.x,height,a.y));
                v.Add(new Vector3(a.x,0,a.y));v.Add(new Vector3(a.x,height,a.y));v.Add(new Vector3(b.x,height,b.y));
                v.Add(new Vector3(a.x,0,a.y));v.Add(new Vector3(b.x,height,b.y));v.Add(new Vector3(b.x,0,b.y));
                for(int j=0;j<9;j++)t.Add(n+j);
            }
            var mesh=new Mesh();mesh.SetVertices(v);mesh.SetTriangles(t,0);mesh.RecalculateNormals();var o=new GameObject("Silhouette");o.transform.SetParent(root,false);o.AddComponent<MeshFilter>().sharedMesh=mesh;material.SetInt("_Cull",0);o.AddComponent<MeshRenderer>().sharedMaterial=material;return o;
        }
    }
}
