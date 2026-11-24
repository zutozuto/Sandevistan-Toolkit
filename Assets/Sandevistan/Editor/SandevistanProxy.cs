using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sandevistan.Editor
{
    public static class SandevistanProxy
    {
        public sealed class Output
        {
            public Mesh mesh;
            public Transform[] bones;
            public Texture2D mask;
            public int originalTriangles;
            public Mesh[] parts;
            public Renderer[] sources;
            public bool cacheHit;
            public double seconds;
            public int removedTransparentTriangles;
        }

        static readonly HumanBodyBones[] MajorBones = {
            HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest,
            HumanBodyBones.Neck, HumanBodyBones.Head,
            HumanBodyBones.LeftShoulder, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
            HumanBodyBones.RightShoulder, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes,
            HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, HumanBodyBones.RightToes
        };

        internal struct Vertex
        {
            public Vector3 position, normal;
            public Vector2 uv;
            public BoneWeight weight;
            public int region, part;
            public bool cutout;
        }

        public static Transform[] GetMajorBones(Animator animator)
        {
            if (animator == null || !animator.isHuman)
                throw new InvalidOperationException("Select a humanoid avatar with a valid Animator Avatar.");
            return MajorBones.Select(animator.GetBoneTransform).Where(t => t != null).Distinct().ToArray();
        }

        public static Renderer[] Candidates(Transform root)
        {
            return root.GetComponentsInChildren<Renderer>(true)
                .Where(r => (r is SkinnedMeshRenderer || r is MeshRenderer)
                    && !r.transform.GetComponentsInParent<Transform>(true).Any(t => t.CompareTag("EditorOnly") || t.name == "Sandevistan_A")
                    && GetMesh(r) != null).ToArray();
        }

        public static Mesh GetMesh(Renderer r)
        {
            if (r is SkinnedMeshRenderer skinned) return skinned.sharedMesh;
            var filter = r.GetComponent<MeshFilter>();
            return filter != null ? filter.sharedMesh : null;
        }

        public static Output Build(Transform root, Transform[] bones, Renderer[] renderers, int triangleTarget, string folder)
        {
            if (renderers.Length == 0) throw new InvalidOperationException("No meshes selected.");
            // Rigid props need their own sampled transform: their attachment can change at runtime.
            bones = bones.Concat(renderers.Where(r=>r is MeshRenderer).Select(r=>r.transform)).Distinct().ToArray();
            var timer=System.Diagnostics.Stopwatch.StartNew();
            string cacheKey=SandevistanProxyCache.Key(root,bones,renderers,triangleTarget);
            var cached=SandevistanProxyCache.Read(cacheKey,bones,renderers);
            if(cached!=null){cached.cacheHit=true;cached.seconds=timer.Elapsed.TotalSeconds;return cached;}
            int originalTriangles=0, removedTransparent=0;
            var maskTextures = new List<Texture2D>();
            var cuts=new Dictionary<Material,bool>();
            var materialIndex = new Dictionary<Material, int>();
            var temporaryTextures = new List<Texture2D>();
            var vertices = new List<Vertex>();
            var triangles = new List<int>();
            try
            {
                foreach (var renderer in renderers)
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null || materialIndex.ContainsKey(material)) continue;
                    var texture = AlphaTexture(material);
                    bool cut=texture.GetPixels32().Any(p=>p.a!=255);cuts.Add(material,cut);
                    int white=maskTextures.FindIndex(t=>t.width==2&&t.height==2&&t.GetPixels32().All(p=>p.a==255));
                    if(!cut&&white>=0)materialIndex.Add(material,white);
                    else {materialIndex.Add(material,maskTextures.Count);maskTextures.Add(texture);}
                    temporaryTextures.Add(texture);
                }
                if (maskTextures.Count == 0) throw new InvalidOperationException("Selected meshes have no materials.");
                var atlas = new Texture2D(1024, 1024, TextureFormat.RGBA32, false, true) {name="SandevistanAlphaAtlas"};
                temporaryTextures.Add(atlas);
                var rectangles = atlas.PackTextures(maskTextures.ToArray(), 2, 1024, false);
                Directory.CreateDirectory(folder);
                string atlasPath = folder + "/AlphaMask.png";
                File.WriteAllBytes(atlasPath, atlas.EncodeToPNG());
                AssetDatabase.ImportAsset(atlasPath);
                var importer = (TextureImporter)AssetImporter.GetAtPath(atlasPath);
                importer.sRGBTexture = false;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = false;
                importer.mipmapEnabled = true;
                importer.maxTextureSize = 1024;
                importer.textureCompression = TextureImporterCompression.Compressed;
                importer.isReadable = false;
                importer.SaveAndReimport();

                foreach (var renderer in renderers)
                {
                    int part = Array.IndexOf(renderers, renderer);
                    var source = GetMesh(renderer);
                    if (!source.isReadable) throw new InvalidOperationException(renderer.name + ": enable mesh Read/Write before generating.");
                    var baked = new Mesh();
                    try
                    {
                        var smr = renderer as SkinnedMeshRenderer;
                        if (smr != null) smr.BakeMesh(baked, false);
                        else UnityEngine.Object.DestroyImmediate(baked);
                        var shape = smr != null ? baked : source;
                        var positions = shape.vertices;
                        var normals = shape.normals;
                        var uvs = source.uv;
                        var weights = source.boneWeights;
                        var matrix = root.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                        var normalMatrix = matrix.inverse.transpose;
                        var mapping = smr != null && smr.bones.Length>0 ? smr.bones.Select(b => MapBone(b, bones)).ToArray() : new[]{MapBone(renderer.transform, bones)};
                        var materials = renderer.sharedMaterials;
                        for (int sub=0; sub<source.subMeshCount && sub<materials.Length; sub++)
                        {
                            var mat = materials[sub];
                            if (mat == null) continue;
                            var region = materialIndex[mat];
                            var rect = rectangles[region];
                            bool cut=cuts[mat];
                            var alpha=maskTextures[region];
                            var pixels=cut?alpha.GetPixels32():null;
                            bool EmptyTriangle(int a,int b,int c)
                            {
                                if(!cut||uvs.Length==0)return false;
                                Vector2 A=Vector2.Scale(uvs[a],mat.mainTextureScale)+mat.mainTextureOffset,B=Vector2.Scale(uvs[b],mat.mainTextureScale)+mat.mainTextureOffset,C=Vector2.Scale(uvs[c],mat.mainTextureScale)+mat.mainTextureOffset;
                                float x0=Mathf.Min(A.x,Mathf.Min(B.x,C.x)),x1=Mathf.Max(A.x,Mathf.Max(B.x,C.x)),y0=Mathf.Min(A.y,Mathf.Min(B.y,C.y)),y1=Mathf.Max(A.y,Mathf.Max(B.y,C.y));
                                if(x0<0||y0<0||x1>1||y1>1)return false;
                                for(int y=Mathf.Max(0,Mathf.FloorToInt(y0*alpha.height)-2);y<=Mathf.Min(alpha.height-1,Mathf.CeilToInt(y1*alpha.height)+2);y++)
                                for(int x=Mathf.Max(0,Mathf.FloorToInt(x0*alpha.width)-2);x<=Mathf.Min(alpha.width-1,Mathf.CeilToInt(x1*alpha.width)+2);x++)if(pixels[y*alpha.width+x].a>0)return false;
                                return true;
                            }
                            var remap = new Dictionary<int, int>();
                            var sourceIndices=source.GetTriangles(sub);originalTriangles+=sourceIndices.Length/3;
                            for(int t=0;t<sourceIndices.Length;t+=3)
                            {
                            if(EmptyTriangle(sourceIndices[t],sourceIndices[t+1],sourceIndices[t+2])){removedTransparent++;continue;}
                            for(int j=0;j<3;j++)
                            {
                                int old=sourceIndices[t+j];
                                if (!remap.TryGetValue(old, out int index))
                                {
                                    index = vertices.Count;
                                    remap.Add(old, index);
                                    Vector2 uv = old < uvs.Length ? uvs[old] : Vector2.zero;
                                    if (mat.HasProperty("_MainTex")) uv = Vector2.Scale(uv, mat.mainTextureScale) + mat.mainTextureOffset;
                                    uv = new Vector2(uv.x>=0&&uv.x<=1?uv.x:Mathf.Repeat(uv.x,1),uv.y>=0&&uv.y<=1?uv.y:Mathf.Repeat(uv.y,1));
                                    var weight = smr != null && weights.Length == positions.Length ? RemapWeight(weights[old], mapping)
                                        : new BoneWeight {boneIndex0=mapping[0], weight0=1};
                                    vertices.Add(new Vertex {
                                        position=matrix.MultiplyPoint3x4(positions[old]),
                                        normal=old<normals.Length ? normalMatrix.MultiplyVector(normals[old]).normalized : Vector3.up,
                                        uv=cut?new Vector2(rect.x+uv.x*rect.width, rect.y+uv.y*rect.height):rect.center,
                                        cutout=cut,
                                        weight=weight, region=region, part=part
                                    });
                                }
                                triangles.Add(index);
                            }
                            }
                        }
                    }
                    finally { if (baked != null) UnityEngine.Object.DestroyImmediate(baked); }
                }
                if (triangles.Count == 0) throw new InvalidOperationException("Selected meshes produce no triangles.");
                SandevistanFastReduction.Reduce(vertices, triangles, triangleTarget, out var optimizedVertices, out var optimizedTriangles);
                var mesh = new Mesh {name="SandevistanProxy", indexFormat=IndexFormat.UInt32};
                mesh.SetVertices(optimizedVertices.Select(v=>v.position).ToList());
                mesh.SetNormals(optimizedVertices.Select(v=>v.normal).ToList());
                mesh.SetUVs(0,optimizedVertices.Select(v=>v.uv).ToList());
                mesh.boneWeights = optimizedVertices.Select(v=>v.weight).ToArray();
                mesh.bindposes = bones.Select(b=>b.worldToLocalMatrix*root.localToWorldMatrix).ToArray();
                mesh.SetTriangles(optimizedTriangles,0);
                mesh.RecalculateBounds();
                AssetDatabase.CreateAsset(mesh,folder+"/ProxyMesh.asset");
                var partMeshes = new List<Mesh>();
                var partSources = new List<Renderer>();
                for(int p=0;p<renderers.Length;p++)
                {
                    var indices=new List<int>();var remap=new Dictionary<int,int>();var vv=new List<Vertex>();
                    for(int t=0;t<optimizedTriangles.Count;t+=3)
                    {
                        if(optimizedVertices[optimizedTriangles[t]].part!=p)continue;
                        for(int j=0;j<3;j++)
                        {
                            int old=optimizedTriangles[t+j];
                            if(!remap.TryGetValue(old,out int ni)){ni=vv.Count;remap.Add(old,ni);vv.Add(optimizedVertices[old]);}
                            indices.Add(ni);
                        }
                    }
                    // Keep an empty part for a wholly transparent source so visibility bindings stay valid.
                    var pm=new Mesh{name="ProxyPart_"+p,indexFormat=IndexFormat.UInt32};
                    pm.SetVertices(vv.Select(v=>v.position).ToList());pm.SetNormals(vv.Select(v=>v.normal).ToList());
                    pm.SetUVs(0,vv.Select(v=>v.uv).ToList());pm.boneWeights=vv.Select(v=>v.weight).ToArray();
                    pm.bindposes=mesh.bindposes;pm.SetTriangles(indices,0);pm.RecalculateBounds();
                    AssetDatabase.CreateAsset(pm,folder+"/ProxyPart_"+p+".asset");partMeshes.Add(pm);partSources.Add(renderers[p]);
                }
                var result=new Output {mesh=mesh, bones=bones, mask=AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath), originalTriangles=originalTriangles,parts=partMeshes.ToArray(),sources=partSources.ToArray(),seconds=timer.Elapsed.TotalSeconds,removedTransparentTriangles=removedTransparent};
                SandevistanProxyCache.Write(cacheKey,result);return result;
            }
            finally {foreach (var texture in temporaryTextures) if (texture != null) UnityEngine.Object.DestroyImmediate(texture);}
        }

        static Texture2D AlphaTexture(Material material)
        {
            bool needsAlpha = material.renderQueue >= 2450;
            var source = needsAlpha && material.HasProperty("_MainTex") ? material.GetTexture("_MainTex") : null;
            if (source == null)
            {
                var white = new Texture2D(2,2,TextureFormat.RGBA32,false,true);
                white.SetPixels(Enumerable.Repeat(Color.white,4).ToArray()); white.Apply(); return white;
            }
            int size = Mathf.Clamp(Mathf.Max(source.width,source.height),2,512);
            var rt = RenderTexture.GetTemporary(size,size,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
            var previous = RenderTexture.active;
            try
            {
                Graphics.Blit(source,rt);
                RenderTexture.active=rt;
                var readable = new Texture2D(size,size,TextureFormat.RGBA32,false,true);
                readable.ReadPixels(new Rect(0,0,size,size),0,0);
                var pixels=readable.GetPixels32();
                for(int i=0;i<pixels.Length;i++) pixels[i]=new Color32(255,255,255,pixels[i].a);
                readable.SetPixels32(pixels); readable.Apply(); return readable;
            }
            finally {RenderTexture.active=previous; RenderTexture.ReleaseTemporary(rt);}
        }

        static int MapBone(Transform source, Transform[] bones)
        {
            if (source == null) return 0;
            for(var t=source;t!=null;t=t.parent)
            {
                int match=Array.IndexOf(bones,t);
                if(match>=0) return match;
                match=Array.FindIndex(bones,b=>b.name==t.name);
                if(match>=0) return match;
            }
            int nearest=0; float distance=float.PositiveInfinity;
            for(int i=0;i<bones.Length;i++)
            {float d=(bones[i].position-source.position).sqrMagnitude;if(d<distance){nearest=i;distance=d;}}
            return nearest;
        }

        static BoneWeight RemapWeight(BoneWeight weight,int[] mapping)
        {
            var weights=new Dictionary<int,float>();
            AddWeight(weights,mapping[Mathf.Clamp(weight.boneIndex0,0,mapping.Length-1)],weight.weight0);
            AddWeight(weights,mapping[Mathf.Clamp(weight.boneIndex1,0,mapping.Length-1)],weight.weight1);
            AddWeight(weights,mapping[Mathf.Clamp(weight.boneIndex2,0,mapping.Length-1)],weight.weight2);
            AddWeight(weights,mapping[Mathf.Clamp(weight.boneIndex3,0,mapping.Length-1)],weight.weight3);
            return Weight(weights);
        }

        static void AddWeight(Dictionary<int,float> weights,int bone,float weight)
        {
            if(weight<=0) return;
            weights.TryGetValue(bone,out float previous); weights[bone]=previous+weight;
        }

        static BoneWeight Weight(Dictionary<int,float> weights)
        {
            var top=weights.OrderByDescending(p=>p.Value).Take(4).ToArray();
            if(top.Length==0) return new BoneWeight{boneIndex0=0,weight0=1};
            float total=top.Sum(p=>p.Value);
            var result=new BoneWeight();
            result.boneIndex0=top[0].Key; result.weight0=top[0].Value/total;
            if(top.Length>1){result.boneIndex1=top[1].Key;result.weight1=top[1].Value/total;}
            if(top.Length>2){result.boneIndex2=top[2].Key;result.weight2=top[2].Value/total;}
            if(top.Length>3){result.boneIndex3=top[3].Key;result.weight3=top[3].Value/total;}
            return result;
        }

    }
}
