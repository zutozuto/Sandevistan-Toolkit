using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Sandevistan.Editor
{
    internal static class SandevistanProxyCache
    {
        const string Folder="Library/SandevistanProxyCache-v4";
        [Serializable] sealed class Entry {public string mesh,mask;public string[] parts;public int original,removed;}
        internal static string Key(Transform root,Transform[] bones,Renderer[] sources,int target)
        {
            var text=new StringBuilder("fast-qem-v4.5/").Append(target);
            var dependencies=new Dictionary<string,string>();
            void Asset(UnityEngine.Object asset)
            {
                if(asset==null){text.Append("/null");return;}
                string path=AssetDatabase.GetAssetPath(asset);
                if(!string.IsNullOrEmpty(path))
                {
                    if(!dependencies.TryGetValue(path,out string hash)){hash=AssetDatabase.GetAssetDependencyHash(path).ToString();dependencies.Add(path,hash);}
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset,out string guid,out long id);text.Append('/').Append(guid).Append(':').Append(id).Append(':').Append(hash);
                }
                else if(asset is Material material)
                {
                    text.Append('/').Append(material.shader!=null?material.shader.name:"null").Append('/').Append(material.renderQueue);
                    if(material.HasProperty("_MainTex")){Asset(material.GetTexture("_MainTex"));text.Append('/').Append(material.mainTextureScale.ToString("R")).Append('/').Append(material.mainTextureOffset.ToString("R"));}
                }
                else if(asset is Texture texture)text.Append('/').Append(texture.imageContentsHash).Append('/').Append(texture.updateCount).Append('/').Append(texture.width).Append('/').Append(texture.height).Append('/').Append(texture.wrapMode).Append('/').Append(texture.filterMode);
                else text.Append('/').Append(EditorJsonUtility.ToJson(asset));
            }
            void Matrix(Matrix4x4 matrix){for(int i=0;i<16;i++)text.Append('/').Append(matrix[i].ToString("R",System.Globalization.CultureInfo.InvariantCulture));}
            foreach(var bone in bones){text.Append('/').Append(AnimationUtility.CalculateTransformPath(bone,root));Matrix(root.worldToLocalMatrix*bone.localToWorldMatrix);}
            foreach(var source in sources)
            {
                text.Append('/').Append(AnimationUtility.CalculateTransformPath(source.transform,root));Asset(SandevistanProxy.GetMesh(source));Matrix(root.worldToLocalMatrix*source.transform.localToWorldMatrix);
                foreach(var material in source.sharedMaterials)Asset(material);
                if(source is SkinnedMeshRenderer skin)
                {
                    foreach(var bone in skin.bones)if(bone!=null)Matrix(root.worldToLocalMatrix*bone.localToWorldMatrix);
                    for(int i=0;i<skin.sharedMesh.blendShapeCount;i++)text.Append('/').Append(skin.GetBlendShapeWeight(i).ToString("R",System.Globalization.CultureInfo.InvariantCulture));
                }
            }
            return Hash128.Compute(text.ToString()).ToString();
        }
        internal static SandevistanProxy.Output Read(string key,Transform[] bones,Renderer[] sources)
        {
            try
            {
                string path=Folder+"/"+key+".json";if(!File.Exists(path))return null;
                var e=JsonUtility.FromJson<Entry>(File.ReadAllText(path));
                var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(e.mesh);var mask=AssetDatabase.LoadAssetAtPath<Texture2D>(e.mask);
                var parts=e.parts.Select(AssetDatabase.LoadAssetAtPath<Mesh>).ToArray();
                if(mesh==null||mask==null||parts.Length!=sources.Length||parts.Any(p=>p==null))return null;
                return new SandevistanProxy.Output{mesh=mesh,mask=mask,parts=parts,bones=bones,sources=sources,originalTriangles=e.original,removedTransparentTriangles=e.removed};
            }
            catch{return null;}
        }
        internal static void Write(string key,SandevistanProxy.Output result)
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Folder+"/"+key+".json",JsonUtility.ToJson(new Entry{mesh=AssetDatabase.GetAssetPath(result.mesh),mask=AssetDatabase.GetAssetPath(result.mask),parts=result.parts.Select(AssetDatabase.GetAssetPath).ToArray(),original=result.originalTriangles,removed=result.removedTransparentTriangles}));
        }
    }
}
