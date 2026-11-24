using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VRC.SDK3.Avatars.Components;

namespace Sandevistan.Editor
{
    internal static class SandevistanMeshGrouping
    {
        internal static void Merge(GameObject avatar,SandevistanVisibility.Binding[] bindings)
        {
            var root=avatar.transform;
            var descriptor=avatar.GetComponent<VRCAvatarDescriptor>();
            var clips=(descriptor.baseAnimationLayers??Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>()).Concat(descriptor.specialAnimationLayers??Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>())
                .Where(l=>l.animatorController!=null).SelectMany(l=>l.animatorController.animationClips).Distinct().ToArray();
            var activePaths=new HashSet<string>();var enabledPaths=new HashSet<string>();
            var signatures=new Dictionary<string,StringBuilder>();
            for(int ci=0;ci<clips.Length;ci++)foreach(var curve in AnimationUtility.GetCurveBindings(clips[ci]))
            {
                if(curve.type==typeof(GameObject)&&curve.propertyName=="m_IsActive")activePaths.Add(curve.path);
                if(typeof(Renderer).IsAssignableFrom(curve.type)&&curve.propertyName=="m_Enabled")enabledPaths.Add(curve.path);
                if(!signatures.TryGetValue(curve.path,out var signature))signatures.Add(curve.path,signature=new StringBuilder());
                signature.Append(ci).Append('/').Append(curve.type.FullName).Append('/').Append(curve.propertyName);
                foreach(var k in AnimationUtility.GetEditorCurve(clips[ci],curve).keys)signature.Append('/').Append(k.time.ToString("R")).Append(':').Append(k.value.ToString("R")).Append(':').Append(k.inTangent.ToString("R")).Append(':').Append(k.outTangent.ToString("R"));
            }
            string Key(SandevistanVisibility.Binding b)
            {
                var s=new StringBuilder().Append(b.proxy.transform.parent.GetInstanceID()).Append('/').Append(b.source.enabled);
                for(var t=b.source.transform;t!=null;t=t.parent)
                {
                    string path=AnimationUtility.CalculateTransformPath(t,root);
                    if(activePaths.Contains(path)||!t.gameObject.activeSelf)s.Append('/').Append(path).Append(':').Append(t.gameObject.activeSelf);
                    if(t==root)break;
                }
                string sourcePath=AnimationUtility.CalculateTransformPath(b.source.transform,root);
                if(enabledPaths.Contains(sourcePath))s.Append("/renderer:").Append(sourcePath);
                foreach(var mat in b.proxy.sharedMaterials)s.Append('/').Append(mat!=null?mat.GetInstanceID():0);
                foreach(var bone in b.proxy.bones)s.Append('/').Append(bone!=null?bone.GetInstanceID():0);
                var matrix=b.proxy.transform.localToWorldMatrix;for(int i=0;i<16;i++)s.Append('/').Append(matrix[i].ToString("R"));
                foreach(var bp in b.proxy.sharedMesh.bindposes)for(int i=0;i<16;i++)s.Append('/').Append(bp[i].ToString("R"));
                string proxyPath=AnimationUtility.CalculateTransformPath(b.proxy.transform,root);
                if(signatures.TryGetValue(proxyPath,out var signature))s.Append('/').Append(signature);
                return s.ToString();
            }
            int before=bindings.Select(b=>b.proxy).Distinct().Count();
            var meshCache=new Dictionary<string,Mesh>();
            // Original parts retain variable skin weights, all UV channels and BlendShape frames.
            // The reduced-proxy combiner intentionally handles only its smaller vertex format.
            foreach(var group in bindings.Where(b=>!b.preserveMesh).GroupBy(Key))
            {
                var entries=group.ToArray();if(entries.Length<2)continue;
                var lead=entries[0].proxy;
                string meshKey=string.Join("/",entries.Select(b=>b.proxy.sharedMesh.GetInstanceID()));
                if(!meshCache.TryGetValue(meshKey,out var merged))
                {
                var vertices=new List<Vector3>();var normals=new List<Vector3>();var uv=new List<Vector2>();var weights=new List<BoneWeight>();var triangles=new List<int>();
                foreach(var b in entries)
                {
                    var mesh=b.proxy.sharedMesh;int offset=vertices.Count;
                    vertices.AddRange(mesh.vertices);normals.AddRange(mesh.normals);uv.AddRange(mesh.uv);weights.AddRange(mesh.boneWeights);
                    triangles.AddRange(mesh.triangles.Select(i=>i+offset));
                }
                merged=new Mesh{name="Sandevistan visibility group",indexFormat=IndexFormat.UInt32};
                merged.SetVertices(vertices);merged.SetNormals(normals);merged.SetUVs(0,uv);merged.boneWeights=weights.ToArray();merged.bindposes=lead.sharedMesh.bindposes;merged.SetTriangles(triangles,0);merged.RecalculateBounds();meshCache.Add(meshKey,merged);
                }
                lead.sharedMesh=merged;var bounds=merged.bounds;bounds.Expand(0.5f);lead.localBounds=bounds;
                foreach(var b in entries.Skip(1)){UnityEngine.Object.DestroyImmediate(b.proxy.gameObject);b.proxy=lead;}
            }
            int after=bindings.Select(b=>b.proxy).Distinct().Count();
            Debug.Log("Sandevistan build: "+before+" -> "+after+" skinned renderers / "+after*3+" material slots; merged only identical visibility and animation groups.");
        }
    }
}
