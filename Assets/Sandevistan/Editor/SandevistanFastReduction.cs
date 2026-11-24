using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Vertex=Sandevistan.Editor.SandevistanProxy.Vertex;

namespace Sandevistan.Editor
{
    // Threshold QEM over real topology edges. No spatial cell clustering and no heap
    // invalidation storm; exact attribute welding restores seams that do not carry a mask.
    internal static class SandevistanFastReduction
    {
        struct Q
        {
            public double xx,xy,xz,xw,yy,yz,yw,zz,zw,ww;
            public int count;
            public void Plane(Vector3 n,double d,double weight=1)
            {xx+=n.x*n.x*weight;xy+=n.x*n.y*weight;xz+=n.x*n.z*weight;xw+=n.x*d*weight;yy+=n.y*n.y*weight;yz+=n.y*n.z*weight;yw+=n.y*d*weight;zz+=n.z*n.z*weight;zw+=n.z*d*weight;ww+=d*d*weight;count++;}
            public static Q operator +(Q a,Q b)=>new Q{xx=a.xx+b.xx,xy=a.xy+b.xy,xz=a.xz+b.xz,xw=a.xw+b.xw,yy=a.yy+b.yy,yz=a.yz+b.yz,yw=a.yw+b.yw,zz=a.zz+b.zz,zw=a.zw+b.zw,ww=a.ww+b.ww,count=a.count+b.count};
            public double Cost(Vector3 p)=>Math.Max(0,xx*p.x*p.x+yy*p.y*p.y+zz*p.z*p.z+ww+2*(xy*p.x*p.y+xz*p.x*p.z+xw*p.x+yz*p.y*p.z+yw*p.y+zw*p.z))/Math.Max(1,count);
            public Vector3 Point(Vector3 a,Vector3 b,out float t)
            {
                var v=b-a;double denominator=xx*v.x*v.x+yy*v.y*v.y+zz*v.z*v.z+2*(xy*v.x*v.y+xz*v.x*v.z+yz*v.y*v.z);
                double numerator=v.x*(xx*a.x+xy*a.y+xz*a.z+xw)+v.y*(xy*a.x+yy*a.y+yz*a.z+yw)+v.z*(xz*a.x+yz*a.y+zz*a.z+zw);
                t=denominator>1e-14?Mathf.Clamp01((float)(-numerator/denominator)):0.5f;return Vector3.Lerp(a,b,t);
            }
        }
        sealed class Node
        {public Vertex v;public Q q;public bool dead,border,locked;public int stamp=-1;public readonly List<int> faces=new List<int>(8);}
        sealed class Face
        {
            public int a,b,c;public bool dead;public Vector3 original;
            public bool Has(int v)=>a==v||b==v||c==v;
        }
        struct Edge {public int count,face;}
        static Vector3 Unit(Vector3 v){float length=v.magnitude;return length>1e-15f?v/length:Vector3.zero;}
        static ulong EdgeKey(int a,int b)=>((ulong)(uint)Math.Min(a,b)<<32)|(uint)Math.Max(a,b);
        static float Weight(BoneWeight w,int bone)=>(w.boneIndex0==bone?w.weight0:0)+(w.boneIndex1==bone?w.weight1:0)+(w.boneIndex2==bone?w.weight2:0)+(w.boneIndex3==bone?w.weight3:0);
        static float WeightDistance(BoneWeight a,BoneWeight b)
        {
            float d=0;
            if(a.weight0>0)d+=Mathf.Abs(a.weight0-Weight(b,a.boneIndex0));if(a.weight1>0)d+=Mathf.Abs(a.weight1-Weight(b,a.boneIndex1));
            if(a.weight2>0)d+=Mathf.Abs(a.weight2-Weight(b,a.boneIndex2));if(a.weight3>0)d+=Mathf.Abs(a.weight3-Weight(b,a.boneIndex3));
            if(b.weight0>0&&Weight(a,b.boneIndex0)==0)d+=b.weight0;if(b.weight1>0&&Weight(a,b.boneIndex1)==0)d+=b.weight1;
            if(b.weight2>0&&Weight(a,b.boneIndex2)==0)d+=b.weight2;if(b.weight3>0&&Weight(a,b.boneIndex3)==0)d+=b.weight3;return d;
        }
        static BoneWeight Blend(BoneWeight a,BoneWeight b,float t)
        {
            var weights=new Dictionary<int,float>(8);
            void Add(int bone,float weight){if(weight<=0)return;weights.TryGetValue(bone,out float old);weights[bone]=old+weight;}
            Add(a.boneIndex0,a.weight0*(1-t));Add(a.boneIndex1,a.weight1*(1-t));Add(a.boneIndex2,a.weight2*(1-t));Add(a.boneIndex3,a.weight3*(1-t));
            Add(b.boneIndex0,b.weight0*t);Add(b.boneIndex1,b.weight1*t);Add(b.boneIndex2,b.weight2*t);Add(b.boneIndex3,b.weight3*t);
            var top=weights.OrderByDescending(p=>p.Value).Take(4).ToArray();float sum=top.Sum(p=>p.Value);var w=new BoneWeight();
            if(top.Length>0){w.boneIndex0=top[0].Key;w.weight0=top[0].Value/sum;}if(top.Length>1){w.boneIndex1=top[1].Key;w.weight1=top[1].Value/sum;}
            if(top.Length>2){w.boneIndex2=top[2].Key;w.weight2=top[2].Value/sum;}if(top.Length>3){w.boneIndex3=top[3].Key;w.weight3=top[3].Value/sum;}return w;
        }
        static string WeldKey(Vertex v)
        {
            // Quantization is attribute identity tolerance (one micrometre), not a voxel size.
            // Distinct nearby sheets do not become edge-collapse neighbors.
            return Mathf.RoundToInt(v.position.x*1e6f)+","+Mathf.RoundToInt(v.position.y*1e6f)+","+Mathf.RoundToInt(v.position.z*1e6f)+"/"+
                Mathf.RoundToInt(v.uv.x*1e6f)+","+Mathf.RoundToInt(v.uv.y*1e6f)+"/"+v.part+"/"+v.region+"/"+
                v.weight.boneIndex0+":"+Mathf.RoundToInt(v.weight.weight0*1e5f)+","+v.weight.boneIndex1+":"+Mathf.RoundToInt(v.weight.weight1*1e5f)+","+
                v.weight.boneIndex2+":"+Mathf.RoundToInt(v.weight.weight2*1e5f)+","+v.weight.boneIndex3+":"+Mathf.RoundToInt(v.weight.weight3*1e5f);
        }
        internal static void Reduce(List<Vertex> input,List<int> indices,int target,out List<Vertex> output,out List<int> triangles)
        {
            target=Math.Max(1,target);var map=new int[input.Count];var keys=new Dictionary<string,int>();var nodes=new List<Node>(input.Count);
            for(int i=0;i<input.Count;i++)
            {
                string key=WeldKey(input[i]);if(!keys.TryGetValue(key,out int id)){id=nodes.Count;keys.Add(key,id);nodes.Add(new Node{v=input[i]});}
                else{var v=nodes[id].v;var combined=v.normal+input[i].normal;if(combined.sqrMagnitude>1e-12f)v.normal=combined.normalized;nodes[id].v=v;}map[i]=id;
            }
            var faces=new List<Face>(indices.Count/3);var unique=new HashSet<(int,int,int)>();var edges=new Dictionary<ulong,Edge>();
            void EdgeCount(int a,int b,int face){ulong key=EdgeKey(a,b);edges.TryGetValue(key,out var value);value.count++;value.face=face;edges[key]=value;}
            for(int i=0;i<indices.Count;i+=3)
            {
                int a=map[indices[i]],b=map[indices[i+1]],c=map[indices[i+2]];if(a==b||b==c||a==c)continue;
                int lo=Math.Min(a,Math.Min(b,c)),hi=Math.Max(a,Math.Max(b,c));if(!unique.Add((lo,a+b+c-lo-hi,hi)))continue;
                Vector3 cross=Vector3.Cross(nodes[b].v.position-nodes[a].v.position,nodes[c].v.position-nodes[a].v.position);
                if(cross.sqrMagnitude<1e-20f)continue;Vector3 normal=Unit(cross);double d=-Vector3.Dot(normal,nodes[a].v.position);
                int f=faces.Count;faces.Add(new Face{a=a,b=b,c=c,original=normal});nodes[a].faces.Add(f);nodes[b].faces.Add(f);nodes[c].faces.Add(f);
                nodes[a].q.Plane(normal,d);nodes[b].q.Plane(normal,d);nodes[c].q.Plane(normal,d);EdgeCount(a,b,f);EdgeCount(b,c,f);EdgeCount(c,a,f);
            }
            foreach(var pair in edges)
            {
                int a=(int)(pair.Key>>32),b=(int)(pair.Key&0xffffffff);
                if(pair.Value.count==1)
                {
                    nodes[a].border=true;nodes[b].border=true;var face=faces[pair.Value.face];
                    Vector3 n=Vector3.Cross((nodes[b].v.position-nodes[a].v.position).normalized,face.original).normalized;
                    double d=-Vector3.Dot(n,nodes[a].v.position);nodes[a].q.Plane(n,d,32);nodes[b].q.Plane(n,d,32);
                }
                else if(pair.Value.count>2){nodes[a].locked=true;nodes[b].locked=true;}
            }
            var bounds=new Bounds(input[0].position,Vector3.zero);foreach(var v in input)bounds.Encapsulate(v.position);
            float dimension=Mathf.Max(bounds.size.x,Mathf.Max(bounds.size.y,bounds.size.z));double scale=Math.Max(1e-8,dimension*dimension);
            Debug.Log("QEM init: "+input.Count+" -> "+nodes.Count+" vertices, "+faces.Count+" faces, "+nodes.Count(n=>n.locked)+" locked, "+nodes.Count(n=>n.border)+" boundary");
            var rejects=new long[8];
            int remaining=faces.Count;var nearA=new HashSet<int>();var nearB=new HashSet<int>();var affected=new HashSet<int>();var shared=new HashSet<int>();
            void Gather(int index,HashSet<int> neighbors)
            {
                neighbors.Clear();foreach(int f in nodes[index].faces){var face=faces[f];if(face.dead||!face.Has(index))continue;if(face.a!=index)neighbors.Add(face.a);if(face.b!=index)neighbors.Add(face.b);if(face.c!=index)neighbors.Add(face.c);}
            }
            bool Collapse(int a,int b,int iteration,double threshold)
            {
                var x=nodes[a];var y=nodes[b];if(x.dead||y.dead||x.stamp==iteration||y.stamp==iteration)return false;
                if(x.locked||y.locked){rejects[0]++;return false;}if(x.border!=y.border){rejects[1]++;return false;}
                if(x.v.part!=y.v.part||x.v.region!=y.v.region||WeightDistance(x.v.weight,y.v.weight)>1.45f){rejects[2]++;return false;}
                var q=x.q+y.q;Vector3 position=q.Point(x.v.position,y.v.position,out float blend);
                double cost=q.Cost(position)+(x.v.position-y.v.position).sqrMagnitude*1e-7;if(cost>threshold)return false;
                affected.Clear();shared.Clear();
                foreach(int f in x.faces)if(!faces[f].dead&&faces[f].Has(a))affected.Add(f);
                foreach(int f in y.faces)if(!faces[f].dead&&faces[f].Has(b))affected.Add(f);
                int edgeFaces=0;
                foreach(int f in affected){var face=faces[f];if(face.Has(a)&&face.Has(b)){edgeFaces++;if(face.a!=a&&face.a!=b)shared.Add(face.a);if(face.b!=a&&face.b!=b)shared.Add(face.b);if(face.c!=a&&face.c!=b)shared.Add(face.c);}}
                if(edgeFaces!=(x.border?1:2)){rejects[3]++;return false;}
                Gather(a,nearA);Gather(b,nearB);int common=0;foreach(int v in nearA)if(nearB.Contains(v)){if(!shared.Contains(v)){rejects[4]++;return false;}common++;}
                if(common!=shared.Count){rejects[4]++;return false;}
                Vector2 uv=Vector2.Lerp(x.v.uv,y.v.uv,blend);
                foreach(int f in affected)
                {
                    var face=faces[f];if(face.Has(a)&&face.Has(b))continue;
                    Vector3 p=face.a==a||face.a==b?position:nodes[face.a].v.position;
                    Vector3 r=face.b==a||face.b==b?position:nodes[face.b].v.position;
                    Vector3 s=face.c==a||face.c==b?position:nodes[face.c].v.position;
                    Vector3 normal=Vector3.Cross(r-p,s-p);if(normal.sqrMagnitude<1e-19f||Vector3.Dot(Unit(normal),Unit(Vector3.Cross(nodes[face.b].v.position-nodes[face.a].v.position,nodes[face.c].v.position-nodes[face.a].v.position)))<0.15f){rejects[5]++;return false;}
                    if(x.v.cutout)
                    {
                        Vector2 oldA=nodes[face.a].v.uv,oldB=nodes[face.b].v.uv,oldC=nodes[face.c].v.uv;
                        Vector2 newA=face.a==a||face.a==b?uv:oldA,newB=face.b==a||face.b==b?uv:oldB,newC=face.c==a||face.c==b?uv:oldC;
                        float oldArea=(oldB.x-oldA.x)*(oldC.y-oldA.y)-(oldB.y-oldA.y)*(oldC.x-oldA.x);
                        float newArea=(newB.x-newA.x)*(newC.y-newA.y)-(newB.y-newA.y)*(newC.x-newA.x);
                        if(Mathf.Abs(oldArea)>1e-9f&&newArea*oldArea<=0)return false;
                    }
                }
                var vertex=x.v;vertex.position=position;var blendedNormal=Vector3.Lerp(x.v.normal,y.v.normal,blend);vertex.normal=blendedNormal.sqrMagnitude>1e-12f?blendedNormal.normalized:x.v.normal;vertex.uv=uv;vertex.weight=Blend(x.v.weight,y.v.weight,blend);
                x.v=vertex;x.q=q;x.stamp=iteration;y.dead=true;
                foreach(int f in affected)
                {
                    var face=faces[f];if(face.Has(a)&&face.Has(b)){face.dead=true;remaining--;continue;}
                    bool fromB=face.Has(b);if(face.a==b)face.a=a;if(face.b==b)face.b=a;if(face.c==b)face.c=a;if(fromB)x.faces.Add(f);
                }
                if(x.faces.Count>64)x.faces.RemoveAll(f=>faces[f].dead||!faces[f].Has(a));return true;
            }
            try
            {
                for(int iteration=0;iteration<32&&remaining>target;iteration++)
                {
                    if(!Application.isBatchMode&&EditorUtility.DisplayCancelableProgressBar("生成低模残影",remaining+" / "+target+" 三角面",Mathf.InverseLerp(faces.Count,target,remaining)))throw new OperationCanceledException("Proxy generation cancelled.");
                    double threshold=scale*1e-11*Math.Pow(3,iteration);
                    for(int f=0;f<faces.Count&&remaining>target;f++)
                    {
                        var face=faces[f];if(face.dead)continue;
                        if(Collapse(face.a,face.b,iteration,threshold))continue;
                        if(Collapse(face.b,face.c,iteration,threshold))continue;
                        Collapse(face.c,face.a,iteration,threshold);
                    }
                }
            }
            finally{if(!Application.isBatchMode)EditorUtility.ClearProgressBar();}
            var result=new List<Vertex>();var resultTriangles=new List<int>();var remap=new Dictionary<int,int>();
            void Emit(int index){if(!remap.TryGetValue(index,out int id)){id=result.Count;remap.Add(index,id);var v=nodes[index].v;if(v.normal.sqrMagnitude<0.99f)v.normal=Vector3.up;result.Add(v);}resultTriangles.Add(id);}
            foreach(var face in faces)if(!face.dead){Emit(face.a);Emit(face.b);Emit(face.c);}
            Debug.Log("QEM rejects locked/border/weights/edge/link/flip: "+string.Join(",",rejects));
            output=result;triangles=resultTriangles;
        }
    }
}
