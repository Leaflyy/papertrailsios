using System;

namespace PaperTrails.Core
{
    [Serializable] public struct GridPoint
    {
        public float X,Z;
        public GridPoint(float x,float z){X=x;Z=z;}
    }
    public static class SmoothGrid
    {
        public const int Width=Arena.Size+1;
        public static GridPoint[] Build(Team[] owners)
        {
            var points=new GridPoint[Width*Width];var next=new GridPoint[points.Length];
            var links=new int[points.Length*2];var movable=new bool[points.Length];
            Team At(int x,int z)=>x<0||z<0||x>=Arena.Size||z>=Arena.Size?Team.Blocked:owners[z*Arena.Size+x];
            bool Different(Team a,Team b,bool outer)=>outer?(a==Team.Blocked)!=(b==Team.Blocked):a!=b;
            for(int z=0;z<Width;z++)for(int x=0;x<Width;x++)
            {
                int i=z*Width+x;points[i]=new GridPoint(x,z);
                Team sw=At(x-1,z-1),se=At(x,z-1),nw=At(x-1,z),ne=At(x,z);
                bool outer=sw==Team.Blocked||se==Team.Blocked||nw==Team.Blocked||ne==Team.Blocked;
                int count=0;
                void Link(bool connects,int n){if(!connects)return;if(count<2)links[i*2+count]=n;count++;}
                Link(x>0&&Different(sw,nw,outer),i-1);Link(x<Width-1&&Different(se,ne,outer),i+1);
                Link(z>0&&Different(sw,se,outer),i-Width);Link(z<Width-1&&Different(nw,ne,outer),i+Width);
                movable[i]=count==2;
            }
            // Smooth connected contour vertices, not cell ownership. Shared vertices keep turf watertight.
            for(int pass=0;pass<6;pass++)
            {
                for(int i=0;i<points.Length;i++)
                {
                    next[i]=points[i];if(!movable[i])continue;
                    GridPoint a=points[links[i*2]],b=points[links[i*2+1]];
                    next[i]=new GridPoint(points[i].X*.5f+(a.X+b.X)*.25f,points[i].Z*.5f+(a.Z+b.Z)*.25f);
                }
                var swap=points;points=next;next=swap;
            }
            return points;
        }
        public static bool InQuad(GridPoint[] points,int cell,float x,float z)
        {
            int v=cell/Arena.Size*Width+cell%Arena.Size;
            GridPoint a=points[v],b=points[v+1],c=points[v+Width+1],d=points[v+Width];
            return Cross(a,b,x,z)>=-.0001f&&Cross(b,c,x,z)>=-.0001f&&Cross(c,d,x,z)>=-.0001f&&Cross(d,a,x,z)>=-.0001f;
        }
        static float Cross(GridPoint a,GridPoint b,float x,float z)=>(b.X-a.X)*(z-a.Z)-(b.Z-a.Z)*(x-a.X);
    }
}
