using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sandevistan.Editor
{
    // Separate from the reduced proxy path: retain every selected triangle and original UV.
    public static class SandevistanOriginalMesh
    {
        public sealed class Output
        {
            public Transform[] bones;
            public SandevistanRig.Part[] parts;
            public int triangles, vertices, materials;
            public double seconds;
        }

        public static Transform[] GetBones(Transform root, Renderer[] renderers)
        {
            var bones = new List<Transform>();
            foreach (var renderer in renderers)
            {
                var mesh = SandevistanProxy.GetMesh(renderer);
                var skin = renderer as SkinnedMeshRenderer;
                var sources = skin != null && mesh.bindposes.Length > 0 ? skin.bones : new[] {renderer.transform};
                if (skin != null && mesh.bindposes.Length > 0 && sources.Length != mesh.bindposes.Length)
                    throw new InvalidOperationException(renderer.name + ": bone count does not match bind poses.");
                foreach (var bone in sources)
                {
                    if (bone == null || (bone != root && !bone.IsChildOf(root)))
                        throw new InvalidOperationException(renderer.name + ": original mesh mode requires valid bones inside this avatar.");
                    if (!bones.Contains(bone)) bones.Add(bone);
                }
            }
            return bones.ToArray();
        }

        public static Output Build(Transform root, Renderer[] renderers, string folder)
        {
            if (renderers.Length == 0) throw new InvalidOperationException("No meshes selected.");
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var output = new Output {bones = GetBones(root, renderers)};
            var parts = new List<SandevistanRig.Part>();
            var materials = new Dictionary<Material, Material>();
            Directory.CreateDirectory(folder);
            foreach (var renderer in renderers)
            {
                var source = SandevistanProxy.GetMesh(renderer);
                if (!source.isReadable) throw new InvalidOperationException(renderer.name + ": enable mesh Read/Write before generating.");
                var slots = renderer.sharedMaterials;
                if (slots.Length != source.subMeshCount || slots.Any(m => m == null))
                    throw new InvalidOperationException(renderer.name + ": original mesh mode requires one non-null material per source submesh.");
                var skin = renderer as SkinnedMeshRenderer;
                bool weighted = skin != null && source.bindposes.Length > 0;
                var sourceBones = weighted ? skin.bones : new[] {renderer.transform};
                var boneIndices = sourceBones.Select(b => Array.IndexOf(output.bones, b)).ToArray();
                var matrix = root.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                if (Mathf.Abs(matrix.determinant) < 1e-8f)
                    throw new InvalidOperationException(renderer.name + ": source transform has zero scale.");
                // Repeated slots using the same material can share one part without changing visibility.
                foreach (var group in Enumerable.Range(0, source.subMeshCount).GroupBy(i => slots[i]))
                {
                    var indices = new List<int>();
                    foreach (int sub in group)
                    {
                        if (source.GetTopology(sub) != MeshTopology.Triangles)
                            throw new InvalidOperationException(renderer.name + ": only triangle submeshes are supported.");
                        indices.AddRange(source.GetTriangles(sub));
                    }
                    if (indices.Count == 0) continue;
                    if (!materials.TryGetValue(group.Key, out var material))
                    {
                        material = SourceMaterial(group.Key);
                        AssetDatabase.CreateAsset(material, folder + "/Ghost_Source_" + materials.Count.ToString("D2") + ".mat");
                        materials.Add(group.Key, material);
                    }
                    var mesh = Extract(source, indices, matrix, weighted);
                    mesh.name = "Original_" + renderer.name + "_" + parts.Count.ToString("D2");
                    AssetDatabase.CreateAsset(mesh, folder + "/OriginalPart_" + parts.Count.ToString("D2") + ".asset");
                    parts.Add(new SandevistanRig.Part {mesh = mesh, material = material, source = renderer, boneIndices = boneIndices, preserveMesh = true, sourceMaterial = group.Key});
                    output.triangles += indices.Count / 3;
                    output.vertices += mesh.vertexCount;
                }
            }
            if (parts.Count == 0) throw new InvalidOperationException("Selected original meshes contain no triangles.");
            output.parts = parts.ToArray(); output.materials = materials.Count; output.seconds = timer.Elapsed.TotalSeconds;
            return output;
        }

        static Mesh Extract(Mesh source, List<int> indices, Matrix4x4 matrix, bool weighted)
        {
            var used = indices.Distinct().OrderBy(i => i).ToArray();
            var remap = used.Select((old, index) => new {old, index}).ToDictionary(p => p.old, p => p.index);
            var normalMatrix = matrix.inverse.transpose;
            var mesh = new Mesh {indexFormat = used.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16};
            var positions = source.vertices;
            mesh.vertices = used.Select(i => matrix.MultiplyPoint3x4(positions[i])).ToArray();
            if (source.HasVertexAttribute(VertexAttribute.Normal))
            {
                var normals = source.normals;
                mesh.normals = used.Select(i => normalMatrix.MultiplyVector(normals[i]).normalized).ToArray();
            }
            if (source.HasVertexAttribute(VertexAttribute.Tangent))
            {
                var tangents = source.tangents;
                mesh.tangents = used.Select(i => {var t = tangents[i]; var v = matrix.MultiplyVector(new Vector3(t.x,t.y,t.z)).normalized; return new Vector4(v.x,v.y,v.z,t.w * Mathf.Sign(matrix.determinant));}).ToArray();
            }
            if (source.HasVertexAttribute(VertexAttribute.Color)) {var colors = source.colors; mesh.colors = used.Select(i => colors[i]).ToArray();}
            for (int channel = 0; channel < 8; channel++)
            {
                if (!source.HasVertexAttribute((VertexAttribute)((int)VertexAttribute.TexCoord0 + channel))) continue;
                var uv = new List<Vector4>(); source.GetUVs(channel, uv);
                int dimensions = source.GetVertexAttributeDimension((VertexAttribute)((int)VertexAttribute.TexCoord0 + channel));
                if (dimensions == 2) mesh.SetUVs(channel, used.Select(i => new Vector2(uv[i].x,uv[i].y)).ToList());
                else if (dimensions == 3) mesh.SetUVs(channel, used.Select(i => new Vector3(uv[i].x,uv[i].y,uv[i].z)).ToList());
                else mesh.SetUVs(channel, used.Select(i => uv[i]).ToList());
            }
            if (weighted)
            {
                // Keep variable bone influences; the legacy BoneWeight API silently truncates to four.
                using (var counts = source.GetBonesPerVertex())
                using (var weights = source.GetAllBoneWeights())
                {
                    if (counts.Length != source.vertexCount) throw new InvalidOperationException(source.name + ": missing skin weights.");
                    var offsets = new int[counts.Length];
                    for (int i = 1; i < offsets.Length; i++) offsets[i] = offsets[i-1] + counts[i-1];
                    var selectedCounts = used.Select(i => counts[i]).ToArray();
                    var selectedWeights = new List<BoneWeight1>();
                    foreach (int i in used) for (int w = 0; w < counts[i]; w++) selectedWeights.Add(weights[offsets[i] + w]);
                    using (var nc = new NativeArray<byte>(selectedCounts, Allocator.Temp))
                    using (var nw = new NativeArray<BoneWeight1>(selectedWeights.ToArray(), Allocator.Temp)) mesh.SetBoneWeights(nc, nw);
                }
                mesh.bindposes = source.bindposes.Select(b => b * matrix.inverse).ToArray();
            }
            else
            {
                mesh.boneWeights = used.Select(i => new BoneWeight {boneIndex0 = 0, weight0 = 1}).ToArray();
                mesh.bindposes = new[] {matrix.inverse};
            }
            for (int shape = 0; shape < source.blendShapeCount; shape++)
            for (int frame = 0; frame < source.GetBlendShapeFrameCount(shape); frame++)
            {
                var dp = new Vector3[source.vertexCount]; var dn = new Vector3[source.vertexCount]; var dt = new Vector3[source.vertexCount];
                source.GetBlendShapeFrameVertices(shape, frame, dp, dn, dt);
                mesh.AddBlendShapeFrame(source.GetBlendShapeName(shape), source.GetBlendShapeFrameWeight(shape, frame),
                    used.Select(i => matrix.MultiplyVector(dp[i])).ToArray(),
                    used.Select(i => normalMatrix.MultiplyVector(dn[i])).ToArray(),
                    used.Select(i => matrix.MultiplyVector(dt[i])).ToArray());
            }
            mesh.SetTriangles(indices.Select(i => remap[i]).ToArray(), 0); mesh.RecalculateBounds();
            return mesh;
        }

        internal static Material SourceMaterial(Material source)
        {
            var shader = Shader.Find(SandevistanMaterialPreviewWindow.ShaderName);
            if (shader == null) throw new InvalidOperationException("Historical Ghost shader is missing.");
            var result = new Material(shader) {name = "Ghost · " + source.name};
            SandevistanOcclusionMaterials.ApplyStyle(result, false);
            SandevistanOcclusionMaterials.ApplySourceStyle(result);
            // Serialized fallback handles optimized/locked shaders that removed constant properties.
            var serialized = new SerializedObject(source);
            var textures = serialized.FindProperty("m_SavedProperties.m_TexEnvs");
            string property = source.HasProperty("_BaseMap") && !source.HasProperty("_MainTex") ? "_BaseMap" : "_MainTex";
            var savedTexture = FindSaved(textures, property);
            var texture = source.HasProperty(property) ? source.GetTexture(property) : null;
            if (texture == null && savedTexture != null) texture = savedTexture.FindPropertyRelative("m_Texture").objectReferenceValue as Texture;
            result.SetTexture("_MainTex", texture != null ? texture : Texture2D.whiteTexture);
            result.SetTextureScale("_MainTex", source.HasProperty(property) ? source.GetTextureScale(property) : savedTexture?.FindPropertyRelative("m_Scale").vector2Value ?? Vector2.one);
            result.SetTextureOffset("_MainTex", source.HasProperty(property) ? source.GetTextureOffset(property) : savedTexture?.FindPropertyRelative("m_Offset").vector2Value ?? Vector2.zero);
            string colorProperty = property == "_BaseMap" ? "_BaseColor" : "_Color";
            var savedColor = FindSaved(serialized.FindProperty("m_SavedProperties.m_Colors"), colorProperty);
            result.SetColor("_Color", source.HasProperty(colorProperty) ? source.GetColor(colorProperty) : savedColor?.colorValue ?? Color.white);
            string renderType = source.GetTag("RenderType", false, "");
            bool cutout = renderType == "TransparentCutout" || source.IsKeywordEnabled("_ALPHATEST_ON") || (source.renderQueue >= 2450 && source.renderQueue < 3000);
            bool alpha = cutout || renderType == "Transparent" || source.renderQueue >= 3000;
            result.SetFloat("_SourceAlpha", alpha ? 1 : 0);
            var savedCutoff = FindSaved(serialized.FindProperty("m_SavedProperties.m_Floats"), "_Cutoff");
            float cutoff = source.HasProperty("_Cutoff") ? source.GetFloat("_Cutoff") : savedCutoff?.floatValue ?? 0.3f;
            result.SetFloat("_SourceCutoff", cutout ? Mathf.Clamp(cutoff,0.001f,1) : 0.001f);
            return result;
        }

        static SerializedProperty FindSaved(SerializedProperty array, string name)
        {
            if (array == null) return null;
            for (int i = 0; i < array.arraySize; i++)
            {
                var entry = array.GetArrayElementAtIndex(i);
                if (entry.FindPropertyRelative("first").stringValue == name) return entry.FindPropertyRelative("second");
            }
            return null;
        }
    }
}
