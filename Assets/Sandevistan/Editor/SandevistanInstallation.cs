using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Settings=Sandevistan.SandevistanVisibility.InstallationSettings;

namespace Sandevistan.Editor
{
    internal static class SandevistanInstallation
    {
        internal static Settings Read(Transform effect,out bool inferred)
        {
            var data=effect.GetComponent<SandevistanVisibility>();
            inferred=data==null||data.installation==null||data.installation.version<1;
            var saved=inferred?null:data.installation;
            var settings=saved==null?new Settings():new Settings{
                targetTriangles=saved.targetTriangles,capacity=saved.capacity,maxSpacing=saved.maxSpacing,
                runThreshold=saved.runThreshold,detailed=saved.detailed,originalMesh=saved.originalMesh,
                sources=(saved.sources??Array.Empty<Renderer>()).ToArray()};
            var material=effect.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials)
                .FirstOrDefault(SandevistanMaterialPreviewWindow.IsGhost);
            if(inferred)
            {
                settings.sources=data==null?Array.Empty<Renderer>():data.bindings.Select(b=>b.source).Where(r=>r!=null).Distinct().ToArray();
                settings.originalMesh=data!=null&&data.bindings.Any(b=>b.preserveMesh);
                int count=effect.Cast<Transform>().Count(t=>t.name.StartsWith("Snapshot_"));
                settings.capacity=Mathf.Clamp(count,SandevistanRig.MinCount,SandevistanRig.MaxCapacity);
                var controller=Controller(effect);
                if(controller!=null)
                {
                    var clock=controller.animationClips.FirstOrDefault(c=>c.name=="Distance_Max");
                    if(clock!=null)settings.maxSpacing=Mathf.Max(SandevistanRig.MinSpacing,clock.length);
                    var layer=controller.layers.FirstOrDefault(l=>l.name=="Sandevistan Local Run Detection");
                    var start=layer?.stateMachine.states.FirstOrDefault(s=>s.state.name=="Not Running").state;
                    if(start!=null)
                    {
                        var condition=start.transitions.SelectMany(t=>t.conditions).FirstOrDefault(c=>c.parameter==SandevistanRig.Velocity&&c.mode==AnimatorConditionMode.Greater);
                        if(condition.parameter!=null)settings.runThreshold=condition.threshold;
                    }
                }
                var first=effect.Find("Snapshot_0");
                if(!settings.originalMesh&&first!=null)
                {
                    int actual=first.GetComponentsInChildren<Renderer>(true).Where(r=>r.sharedMaterials.Any(SandevistanMaterialPreviewWindow.IsGhost))
                        .Sum(r=>SandevistanProxy.GetMesh(r)?.triangles.Length/3??0);
                    if(actual>0)settings.targetTriangles=Mathf.Clamp(actual,1000,50000);
                }
            }
            if(material!=null)settings.detailed=material.GetFloat("_DetailMode")>0.5f;
            return settings;
        }

        static AnimatorController Controller(Transform effect)
        {
            var merge=effect.GetComponents<Component>().FirstOrDefault(c=>c!=null&&c.GetType().Name=="ModularAvatarMergeAnimator");
            return merge==null?null:new SerializedObject(merge).FindProperty("animator")?.objectReferenceValue as AnimatorController;
        }

        internal static void PreserveAppearance(Transform previous,SandevistanRig.Part[] parts,bool detailed,bool originalMesh)
        {
            if(previous==null)return;
            var oldSettings=Read(previous,out _);
            var data=previous.GetComponent<SandevistanVisibility>();
            var bindings=data?.bindings.Where(b=>b.source!=null&&b.proxy!=null&&SandevistanMaterialPreviewWindow.IsGhost(b.proxy.sharedMaterial)).ToArray();
            var fallback=previous.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).FirstOrDefault(SandevistanMaterialPreviewWindow.IsGhost);
            foreach(var group in parts.GroupBy(p=>p.material))
            {
                var part=group.First();Material source=null;
                if(part.sourceMaterial!=null&&bindings!=null)
                {
                    source=bindings.FirstOrDefault(b=>b.sourceMaterial==part.sourceMaterial)?.proxy.sharedMaterial;
                    if(source==null)
                    {
                        // Older originals have no material key. Their per-renderer groups retain
                        // the first-occurrence order of the source's distinct material slots.
                        var same=bindings.Where(b=>b.source==part.source&&b.preserveMesh).ToArray();
                        var sourceMesh=SandevistanProxy.GetMesh(part.source);
                        var sourceMaterials=Enumerable.Range(0,sourceMesh.subMeshCount).Where(i=>sourceMesh.GetIndexCount(i)>0)
                            .Select(i=>part.source.sharedMaterials[i]).Distinct().ToArray();
                        int index=Array.IndexOf(sourceMaterials,part.sourceMaterial);
                        var ghosts=same.Select(b=>b.proxy.sharedMaterial).Distinct().ToArray();
                        if(index>=0&&index<ghosts.Length)source=ghosts[index];
                    }
                }
                source=source??bindings?.FirstOrDefault(b=>b.source==part.source)?.proxy.sharedMaterial??fallback;
                if(source==null)continue;
                SandevistanOcclusionMaterials.CopyAppearance(source,part.material);
                if(!originalMesh&&(oldSettings.originalMesh||oldSettings.detailed!=detailed))part.material.SetFloat("_DetailMode",detailed?1:0);
                SandevistanOcclusionMaterials.SetDetailKeyword(part.material);EditorUtility.SetDirty(part.material);
            }
        }

        internal static void Commit(SandevistanRig.Result rig,Transform previous)
        {
            Undo.FlushUndoRecordObjects();Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();
            string name=previous==null?"安装斯安威斯坦":"更新斯安威斯坦安装";Undo.SetCurrentGroupName(name);
            try
            {
                Undo.RegisterCreatedObjectUndo(rig.root,name);
                int sibling=previous==null?rig.root.transform.GetSiblingIndex():previous.GetSiblingIndex();
                bool active=previous==null||previous.gameObject.activeSelf;
                if(previous!=null)
                {
                    Undo.DestroyObjectImmediate(previous.gameObject);
                    if(previous!=null)throw new InvalidOperationException("The existing effect cannot be removed from this Prefab instance. Open its source Prefab in Prefab Mode to update it.");
                }
                Undo.RecordObject(rig.root,name);rig.root.name="Sandevistan_A";rig.root.SetActive(active);
                Undo.RecordObject(rig.root.transform,name);rig.root.transform.SetSiblingIndex(sibling);
                Undo.FlushUndoRecordObjects();Undo.CollapseUndoOperations(group);
            }
            catch{Undo.RevertAllDownToGroup(group);throw;}
        }

        internal static void DiscardGeneratedAssets(string folder)
        {
            // Only this attempt's unique directory and its GUID-derived depth partners.
            if(!folder.StartsWith("Assets/Sandevistan_Installed/",StringComparison.Ordinal)||!AssetDatabase.IsValidFolder(folder))return;
            foreach(string guid in AssetDatabase.FindAssets("t:Material",new[]{folder}))
            {
                var material=AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                foreach(var companion in SandevistanOcclusionMaterials.ExistingCompanions(material))AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(companion));
            }
            AssetDatabase.DeleteAsset(folder);
        }
    }
}
