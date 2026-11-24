using System;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Sandevistan.Editor
{
    // Preview rendering uses temporary copies; the inspector edits the selected material assets.
    public sealed class SandevistanMaterialPreviewWindow : ScriptableObject
    {
        internal const string ShaderName = "Sandevistan/Historical Ghost";
        static readonly string[] Palettes = {"经典绿青紫", "极光绿蓝紫", "落日金红紫", "冰川白青蓝", "焰火金橙红", "霓虹青紫粉", "酸性黄绿青"};
        [SerializeField] Material source;
        [SerializeField] Renderer sample;
        [SerializeField] float age = 0.25f;
        [SerializeField] int palette;
        [SerializeField] float exposure;
        [SerializeField] bool animate = true;
        [SerializeField] bool previewOwnerView;
        [SerializeField] bool wholeSnapshot=true;
        [SerializeField] bool editAllMaterials=true;
        [SerializeField] Color background = new Color(0.055f, 0.065f, 0.085f, 1);
        Vector2 scroll, orbit = new Vector2(200, -10);
        float zoom = 1;
        PreviewRenderUtility preview;
        Material copy;
        Material previewCleanup;
        Material previewDepth;
        MaterialEditor materialEditor;
        readonly SandevistanGhostShaderGUI inspector = new SandevistanGhostShaderGUI();
        Mesh baked;
        string status;
        double lastRepaint;

        internal EditorWindow host;
        internal bool visible;
        public static void Open()=>SandevistanWindow.OpenTab(2);
        public static void Open(Material material)=>SandevistanWindow.Preview(material);
        void Repaint(){if(host!=null)host.Repaint();}
        internal void SetSample(Renderer renderer){sample=renderer;}

        void OnEnable() { EditorApplication.update += Tick; Undo.undoRedoPerformed += OnUndoRedo; }
        void OnDisable()
        {
            EditorApplication.update -= Tick;
            Undo.undoRedoPerformed -= OnUndoRedo;
            ReleasePreview();
            if (materialEditor != null) DestroyImmediate(materialEditor);
            materialEditor = null;
        }
        void OnUndoRedo()
        {
            if(materialEditor!=null)materialEditor.PropertiesChanged();
            Repaint();
        }
        void Tick()
        {
            if (!visible || !animate || EditorApplication.timeSinceStartup - lastRepaint < 1.0 / 30) return;
            lastRepaint = EditorApplication.timeSinceStartup;
            Repaint();
        }

        internal static bool IsGhost(Material material)
        {
            return material != null && material.shader != null && material.shader.name == ShaderName;
        }

        internal void SetMaterial(Material material)
        {
            source = material;
            status = null;
            if (materialEditor != null) DestroyImmediate(materialEditor);
            materialEditor = null;
            if (copy != null) DestroyImmediate(copy);
            copy = null;
        }

        internal Material[] InstallationMaterials()
        {
            var single=IsGhost(source)&&AssetDatabase.Contains(source)?new[]{source}:Array.Empty<Material>();
            if(single.Length==0||sample==null)return single;
            for(var root=sample.transform;root!=null;root=root.parent)
            {
                if(root.name!="Sandevistan_A"&&root.GetComponent<SandevistanVisibility>()==null)continue;
                // Include every pool and hidden outfit. Preview visibility must not limit editing.
                var materials=root.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials)
                    .Where(m=>IsGhost(m)&&AssetDatabase.Contains(m)).Distinct().ToArray();
                return materials.Contains(source)?new[]{source}.Concat(materials.Where(m=>m!=source)).ToArray():single;
            }
            return single;
        }

        internal Material[] EditingMaterials()=>editAllMaterials?InstallationMaterials():
            IsGhost(source)&&AssetDatabase.Contains(source)?new[]{source}:Array.Empty<Material>();

        internal MaterialEditor PrepareMaterialEditor(Material[] targets)
        {
            if(materialEditor!=null&&!materialEditor.targets.Cast<Material>().SequenceEqual(targets))
            {DestroyImmediate(materialEditor);materialEditor=null;}
            if(materialEditor==null&&targets.Length>0)
                materialEditor=(MaterialEditor)UnityEditor.Editor.CreateEditor(targets,typeof(MaterialEditor));
            return materialEditor;
        }

        internal void SynchronizeEdits(Material[] targets)
        {
            SandevistanOcclusionMaterials.SyncEdits(targets);
            if(materialEditor!=null)materialEditor.PropertiesChanged();
        }

        internal int ApplyToAll()
        {
            int count=SandevistanOcclusionMaterials.ApplyAppearance(source,InstallationMaterials());
            if(count>1)editAllMaterials=true;
            status="已将当前材质效果应用到 "+count+" 个残影材质；各部件原主贴图、UV、原主色与裁切设置已保留。可 Ctrl+Z 撤销。";
            if(materialEditor!=null)materialEditor.PropertiesChanged();
            Repaint();return count;
        }

        internal void SaveEdits()
        {
            var targets=EditingMaterials();SandevistanOcclusionMaterials.Save(targets);
            status="已保存 "+targets.Length+" 个残影材质及其 Depth / Cleanup 遮挡材质。";Repaint();
        }

        internal void ReadSelection()
        {
            if (Selection.activeObject is Material material && IsGhost(material))
            {
                SetMaterial(material);
                sample = null;
                return;
            }
            var selected = Selection.activeGameObject;
            if (selected != null)
            {
                var selectedRenderer = selected.GetComponent<Renderer>();
                if (selectedRenderer != null && selectedRenderer.sharedMaterials.Any(IsGhost))
                {
                    sample = selectedRenderer;
                    SetMaterial(selectedRenderer.sharedMaterials.First(IsGhost));
                    return;
                }
                Transform search = selected.transform;
                // Preview one selected pool part regardless of the installed capacity.
                for (var t = selected.transform; t != null; t = t.parent)
                {
                    var effect = t.Find("Sandevistan_A");
                    if (effect == null) continue;
                    search = effect.Find("Snapshot_0") ?? effect;
                    break;
                }
                var renderer = search.GetComponentsInChildren<Renderer>(true)
                    .FirstOrDefault(r => r.sharedMaterials.Any(IsGhost));
                if (renderer != null)
                {
                    sample = renderer;
                    SetMaterial(renderer.sharedMaterials.First(IsGhost));
                    return;
                }
            }
            status = "请选择人物根节点、某个残影片段或 Ghost.mat，再点“读取选中对象”。";
        }

        internal void DrawGUI()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                var next = (Material)EditorGUILayout.ObjectField("实际残影材质", source, typeof(Material), false);
                if (EditorGUI.EndChangeCheck()) SetMaterial(next);
                if (GUILayout.Button("读取选中对象", GUILayout.Width(110))) ReadSelection();
            }
            if (!string.IsNullOrEmpty(status)) EditorGUILayout.HelpBox(status, MessageType.Info);
            if (!IsGhost(source))
            {
                EditorGUILayout.HelpBox("拖入使用 Sandevistan/Historical Ghost Shader 的 .mat 材质。也可先选中已安装的人物，再读取选中对象。", MessageType.Info);
                return;
            }
            if (!AssetDatabase.Contains(source))
            {
                EditorGUILayout.HelpBox("当前是临时材质。请拖入 Project 中原始 Ghost.mat，确保修改保留到退出 Play 后。", MessageType.Warning);
                return;
            }
            EditorGUILayout.LabelField(AssetDatabase.GetAssetPath(source), EditorStyles.wordWrappedMiniLabel);
            var allMaterials=InstallationMaterials();
            if(allMaterials.Length>1)
                editAllMaterials=EditorGUILayout.Popup("编辑范围",editAllMaterials?0:1,new[]{"全部残影材质 · "+allMaterials.Length+" 个","仅当前材质"})==0;
            else EditorGUILayout.LabelField("编辑范围","当前材质；读取已安装人物可批量编辑全部残影材质。",EditorStyles.wordWrappedMiniLabel);
            var targets=EditingMaterials();
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(330)))
                {
                    EditorGUILayout.HelpBox(targets.Length>1?"左侧同时编辑全部 "+targets.Length+" 个残影材质，包括隐藏穿搭；不同值显示为混合状态。各部件保留原主贴图、UV 和裁切。支持 Ctrl+Z。":"左侧直接修改当前 .mat，支持 Ctrl+Z。右侧独立预览，不改人物与 FX 动画。", MessageType.None);
                    if(allMaterials.Length>1&&GUILayout.Button("将当前材质效果应用到全部（保留原贴图）"))ApplyToAll();
                    scroll = EditorGUILayout.BeginScrollView(scroll);
                    PrepareMaterialEditor(targets);
                    EditorGUI.BeginChangeCheck();
                    inspector.Draw(materialEditor, MaterialEditor.GetMaterialProperties(targets), false);
                    if (EditorGUI.EndChangeCheck()) SynchronizeEdits(targets);
                    EditorGUILayout.EndScrollView();
                }
                using (new EditorGUILayout.VerticalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    sample = (Renderer)EditorGUILayout.ObjectField("预览部件（可选）", sample, typeof(Renderer), true);
                    if (EditorGUI.EndChangeCheck()) { orbit = new Vector2(200, -10); zoom = 1; }
                    var siblings=SnapshotParts();
                    if(siblings.Length>1)
                    {
                        wholeSnapshot=EditorGUILayout.Toggle("预览整道残影 · 当前穿搭",wholeSnapshot);
                        var materials=siblings.Select(r=>r.sharedMaterials.First(IsGhost)).Distinct().ToArray();
                        int current=Array.IndexOf(materials,source);
                        int nextMaterial=EditorGUILayout.Popup(targets.Length>1?"当前参考材质":"编辑部件材质",Mathf.Max(0,current),materials.Select(m=>m.name).ToArray());
                        if(nextMaterial!=current&&nextMaterial>=0){var material=materials[nextMaterial];sample=siblings.First(r=>r.sharedMaterials.Contains(material));SetMaterial(material);}
                    }
                    EditorGUILayout.LabelField(sample == null ? "使用 1 米直径球体；轮廓图集在球体预览中暂时忽略。" : "显示所选部件的当前姿态；可拖入其他 Part_XX。", EditorStyles.wordWrappedMiniLabel);
                    age = EditorGUILayout.Slider("预览残影年龄", age, 0, 1);
                    palette = EditorGUILayout.Popup("预览渐变", palette, Palettes);
                    animate = EditorGUILayout.Toggle("播放纹理滚动", animate);
                    previewOwnerView=EditorGUILayout.Toggle("预览本人近处保护",previewOwnerView);
                    background = EditorGUILayout.ColorField("背景颜色", background);
                    exposure = EditorGUILayout.Slider("预览曝光（EV）",exposure,-6,2);
                    Rect rect = GUILayoutUtility.GetRect(160, 160, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                    HandlePreviewInput(rect);
                    if (Event.current.type == EventType.Repaint) DrawPreview(rect);
                    EditorGUILayout.LabelField("拖动旋转 · 滚轮缩放。曝光只影响预览，可调低检查 HDR 亮部；此预览不附带 Bloom。年龄、渐变仅供预览。", EditorStyles.wordWrappedMiniLabel);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("定位原始材质")) EditorGUIUtility.PingObject(source);
                        if (GUILayout.Button(targets.Length>1?"保存全部材质修改":"保存材质修改")) SaveEdits();
                    }
                }
            }
        }

        void HandlePreviewInput(Rect rect)
        {
            var e = Event.current;
            int id = GUIUtility.GetControlID(FocusType.Passive, rect);
            if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            { GUIUtility.hotControl = id; e.Use(); }
            else if (e.type == EventType.MouseDrag && GUIUtility.hotControl == id)
            { orbit += e.delta * 0.5f; orbit.y = Mathf.Clamp(orbit.y, -85, 85); e.Use(); Repaint(); }
            else if (e.type == EventType.MouseUp && GUIUtility.hotControl == id)
            { GUIUtility.hotControl = 0; e.Use(); }
            else if (e.type == EventType.ScrollWheel && rect.Contains(e.mousePosition))
            { zoom = Mathf.Clamp(zoom * Mathf.Exp(e.delta.y * 0.06f), 0.1f, 4); e.Use(); Repaint(); }
        }

        internal static Material MakePreviewMaterial(Material material, float age, int palette, float time)
        {
            var result = new Material(material) {hideFlags = HideFlags.HideAndDontSave};
            ConfigurePreviewMaterial(result, age, palette, time);
            return result;
        }
        static void ConfigurePreviewMaterial(Material material, float age, int palette, float time)
        {
            material.SetFloat("_Fade", 1);
            material.SetFloat("_Age", age);
            material.SetFloat("_Palette", palette);
            material.SetFloat("_PreviewTime", time);
            material.SetFloat("_IsLocalAvatar",0);
        }

        Mesh SampleMesh()
        {
            if (sample is SkinnedMeshRenderer skinned && skinned.sharedMesh != null)
            {
                if (baked == null) baked = new Mesh {name = "Ghost material preview", hideFlags = HideFlags.HideAndDontSave};
                skinned.BakeMesh(baked, false);
                baked.RecalculateBounds();
                return baked;
            }
            if (sample != null)
            {
                var filter = sample.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null) return filter.sharedMesh;
            }
            return Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
        }

        Renderer[] SnapshotParts()
        {
            if(sample==null||sample.transform.parent==null||!sample.transform.parent.name.StartsWith("Snapshot_"))return sample==null?Array.Empty<Renderer>():new[]{sample};
            var metadata=sample.GetComponentInParent<SandevistanVisibility>();
            return sample.transform.parent.GetComponentsInChildren<Renderer>(true).Where(r=>r.sharedMaterials.Any(IsGhost)&&
                (r==sample||metadata==null||metadata.bindings.Any(b=>b.proxy==r&&b.source!=null&&b.source.enabled&&b.source.gameObject.activeInHierarchy))).ToArray();
        }

        void DrawPreview(Rect rect)
        {
            if (rect.width < 2 || rect.height < 2) return;
            var texture = RenderPreview(rect, false);
            if (texture != null) GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, false);
        }

        internal Texture2D Capture(Material material, Renderer renderer, int width, int height)
        {
            SetMaterial(material);
            sample = renderer;
            animate = false;
            return (Texture2D)RenderPreview(new Rect(0, 0, width, height), true);
        }

        Texture RenderPreview(Rect rect, bool capture)
        {
            if (preview == null)
            {
                preview = new PreviewRenderUtility();
                preview.camera.fieldOfView = 32;
                preview.camera.clearFlags = CameraClearFlags.SolidColor;
            }
            if (copy == null) copy = new Material(source) {hideFlags = HideFlags.HideAndDontSave};
            else copy.CopyPropertiesFromMaterial(source);
            ConfigurePreviewMaterial(copy, age, palette, animate ? (float)(EditorApplication.timeSinceStartup % 2048) : 0);
            copy.SetFloat("_IsLocalAvatar",previewOwnerView?1:0);
            copy.SetFloat("_Brightness",source.GetFloat("_Brightness")*Mathf.Pow(2,exposure));
            Mesh mesh = SampleMesh();
            if (mesh == null) return null;
            bool sphere = sample == null;
            if (sphere) copy.SetTexture("_MaskTex", Texture2D.whiteTexture);
            if(previewCleanup==null)previewCleanup=SandevistanOcclusionMaterials.CreateCleanup(copy);
            SandevistanOcclusionMaterials.Copy(copy,previewCleanup);
            if(previewDepth==null)previewDepth=SandevistanOcclusionMaterials.CreateTemporary(copy);
            else SandevistanOcclusionMaterials.Copy(copy,previewDepth);
            copy.renderQueue=SandevistanOcclusionMaterials.ColorQueue;
            var bounds = mesh.bounds;
            // Keep world-space pattern size correct when the avatar uses a scaled root.
            Vector3 scale = sphere ? Vector3.one : sample.transform.lossyScale;
            Matrix4x4 model = Matrix4x4.Scale(scale) * Matrix4x4.Translate(-bounds.center);
            float radius = Vector3.Scale(bounds.extents, scale).magnitude;
            var siblings=wholeSnapshot?SnapshotParts():Array.Empty<Renderer>();
            var temporary=new List<Object>();
            var draws=new List<Tuple<Mesh,Matrix4x4,Material,Material,Material>>();
            if(siblings.Length>1)
            {
                var reference=sample.transform.parent;
                var basis=Matrix4x4.Scale(reference.lossyScale)*reference.worldToLocalMatrix;
                bool first=true;
                foreach(var renderer in siblings)
                {
                    Mesh part;
                    if(renderer==sample)part=mesh;
                    else if(renderer is SkinnedMeshRenderer skin){part=new Mesh{hideFlags=HideFlags.HideAndDontSave};skin.BakeMesh(part,false);part.RecalculateBounds();temporary.Add(part);}
                    else part=renderer.GetComponent<MeshFilter>()?.sharedMesh;
                    if(part==null)continue;
                    var transform=basis*renderer.transform.localToWorldMatrix;
                    var partBounds=TransformedBounds(part.bounds,transform);
                    if(first){bounds=partBounds;first=false;}else bounds.Encapsulate(partBounds);
                    var original=renderer.sharedMaterials.First(IsGhost);
                    Material color=copy,depth=previewDepth,cleanup=previewCleanup;
                    if(original!=source)
                    {
                        color=MakePreviewMaterial(original,age,palette,animate?(float)(EditorApplication.timeSinceStartup%2048):0);
                        color.SetFloat("_IsLocalAvatar",previewOwnerView?1:0);
                        color.SetFloat("_Brightness",original.GetFloat("_Brightness")*Mathf.Pow(2,exposure));
                        depth=SandevistanOcclusionMaterials.CreateTemporary(color);cleanup=SandevistanOcclusionMaterials.CreateCleanup(color);
                        temporary.Add(color);temporary.Add(depth);temporary.Add(cleanup);
                    }
                    draws.Add(Tuple.Create(part,transform,color,depth,cleanup));
                }
                radius=bounds.extents.magnitude;
            }
            radius = Mathf.Max(0.1f, radius);
            float aspect = rect.width / rect.height;
            float halfFov = 16 * Mathf.Deg2Rad;
            float distance = radius / Mathf.Sin(Mathf.Atan(Mathf.Tan(halfFov) * Mathf.Min(1, aspect))) * 1.1f * zoom;
            Quaternion rotation = Quaternion.Euler(-orbit.y, -orbit.x, 0);
            preview.camera.transform.SetPositionAndRotation(rotation * new Vector3(0, 0, -distance), rotation);
            preview.camera.nearClipPlane = Mathf.Max(0.001f, distance - radius * 1.2f);
            preview.camera.farClipPlane = distance + radius * 4;
            preview.camera.backgroundColor = background;
            if (capture) preview.BeginStaticPreview(rect);
            else preview.BeginPreview(rect, GUIStyle.none);
            try
            {
                if(draws.Count>0)
                    foreach(var draw in draws)for(int sub=0;sub<draw.Item1.subMeshCount;sub++)
                    {
                        var transform=Matrix4x4.Translate(-bounds.center)*draw.Item2;
                        preview.DrawMesh(draw.Item1,transform,draw.Item4,sub);preview.DrawMesh(draw.Item1,transform,draw.Item3,sub);preview.DrawMesh(draw.Item1,transform,draw.Item5,sub);
                    }
                else for (int sub = 0; sub < mesh.subMeshCount; sub++) {preview.DrawMesh(mesh, model, previewDepth, sub);preview.DrawMesh(mesh, model, copy, sub);preview.DrawMesh(mesh, model, previewCleanup, sub);}
                preview.Render();
                return capture ? preview.EndStaticPreview() : preview.EndPreview();
            }
            finally{foreach(var item in temporary)DestroyImmediate(item);}
        }

        static Bounds TransformedBounds(Bounds bounds,Matrix4x4 matrix)
        {
            var result=new Bounds(matrix.MultiplyPoint3x4(bounds.center),Vector3.zero);
            for(int i=0;i<8;i++)result.Encapsulate(matrix.MultiplyPoint3x4(bounds.center+Vector3.Scale(bounds.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1))));
            return result;
        }

        void ReleasePreview()
        {
            if (preview != null) preview.Cleanup();
            preview = null;
            if (copy != null) DestroyImmediate(copy);
            if (previewDepth != null) DestroyImmediate(previewDepth);
            if (previewCleanup != null) DestroyImmediate(previewCleanup);
            if (baked != null) DestroyImmediate(baked);
            copy = null;
            previewDepth = null;previewCleanup=null;
            baked = null;
        }
    }
}
