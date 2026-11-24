using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

[assembly: ExportsPlugin(typeof(Sandevistan.Editor.SandevistanBuildPlugin))]
namespace Sandevistan.Editor
{
    public sealed class SandevistanBuildPlugin : Plugin<SandevistanBuildPlugin>
    {
        public override string QualifiedName=>"sandevistan.avatar.visibility";
        public override string DisplayName=>"Sandevistan clothing visibility";
        protected override void Configure()
        {
            // MA has finished merging controllers and costume armatures in Transforming.
            InPhase(BuildPhase.Optimizing).BeforePlugin("nadena.dev.modular-avatar")
                .Run("Bind ghost pieces to costume visibility",ctx=>Apply(ctx.AvatarRootObject));
        }

        public static void Apply(GameObject avatar)
        {
            var metadata=avatar.GetComponentsInChildren<SandevistanVisibility>(true);
            if(metadata.Length==0)return;
            var root=avatar.transform;
            var all=metadata.SelectMany(d=>d.bindings).ToArray();
            if(all.Any(b=>b.source==null||b.proxy==null))throw new InvalidOperationException("Missing source/proxy before ghost grouping.");
            var oldPaths=all.ToDictionary(b=>b,b=>AnimationUtility.CalculateTransformPath(b.proxy.transform,root));
            SandevistanMeshGrouping.Merge(avatar,all);
            var processed=new HashSet<SkinnedMeshRenderer>();
            var pathMap=new Dictionary<string,string>();
            var rendererGates=new Dictionary<string,List<string>>();
            foreach(var data in metadata)
            foreach(var b in data.bindings)
            {
                if(b.source==null || b.proxy==null)
                    throw new InvalidOperationException("A Sandevistan source renderer was removed by another build plugin. Reinstall or exclude that renderer from mesh merging.");
                if(!b.source.transform.IsChildOf(root)||!b.proxy.transform.IsChildOf(root))
                    throw new InvalidOperationException("Sandevistan visibility source must belong to this avatar.");
                SandevistanOcclusionMaterials.SyncRenderer(b.proxy);
                string oldPath=oldPaths[b];
                if(!processed.Add(b.proxy)){pathMap.Add(oldPath,AnimationUtility.CalculateTransformPath(b.proxy.transform,root));continue;}
                string sourcePath=AnimationUtility.CalculateTransformPath(b.source.transform,root);
                string name="SandevistanGhost_"+b.proxy.transform.parent.name+"_"+b.proxy.name;
                if(b.source.transform.Find(name)!=null)throw new InvalidOperationException("Duplicate Sandevistan visibility gate.");
                // Parent hierarchy supplies GameObject visibility, independently of the FX renderer switch.
                b.proxy.transform.SetParent(b.source.transform,true);b.proxy.name=name;
                b.proxy.gameObject.SetActive(b.source.enabled);
                string newPath=AnimationUtility.CalculateTransformPath(b.proxy.transform,root);
                pathMap.Add(oldPath,newPath);
                if(!rendererGates.TryGetValue(sourcePath,out var gates))rendererGates.Add(sourcePath,gates=new List<string>());
                gates.Add(newPath);
            }
            var descriptor=avatar.GetComponent<VRCAvatarDescriptor>();
            var clips=new Dictionary<AnimationClip,AnimationClip>();
            Func<AnimationClip,AnimationClip> rewrite=original=>
            {
                if(clips.TryGetValue(original,out var known))return known;
                var bindings=AnimationUtility.GetCurveBindings(original);
                if(!bindings.Any(b=>pathMap.ContainsKey(b.path)||(typeof(Renderer).IsAssignableFrom(b.type)&&b.propertyName=="m_Enabled"&&rendererGates.ContainsKey(b.path))))
                    return clips[original]=original;
                var copy=UnityEngine.Object.Instantiate(original);copy.name=original.name+" (ghost visibility)";clips.Add(original,copy);
                foreach(var binding in bindings)
                {
                    var curve=AnimationUtility.GetEditorCurve(original,binding);
                    if(pathMap.TryGetValue(binding.path,out var path))
                    {
                        AnimationUtility.SetEditorCurve(copy,binding,null);var dst=binding;dst.path=path;
                        AnimationUtility.SetEditorCurve(copy,dst,curve);
                    }
                    if(typeof(Renderer).IsAssignableFrom(binding.type)&&binding.propertyName=="m_Enabled"&&rendererGates.TryGetValue(binding.path,out var gates))
                        foreach(var gate in gates)AnimationUtility.SetEditorCurve(copy,EditorCurveBinding.FloatCurve(gate,typeof(GameObject),"m_IsActive"),curve);
                }
                return copy;
            };
            // MA cloned the controllers before this pass; clips are copied on write above.
            foreach(var layer in (descriptor.baseAnimationLayers??Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>()).Concat(descriptor.specialAnimationLayers??Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>()))
                if(layer.animatorController is AnimatorController ac)
                    foreach(var al in ac.layers)RewriteStates(al.stateMachine,rewrite);
            foreach(var data in metadata)UnityEngine.Object.DestroyImmediate(data);
        }

        static void RewriteStates(AnimatorStateMachine sm,Func<AnimationClip,AnimationClip> rewrite)
        {
            foreach(var s in sm.states)s.state.motion=RewriteMotion(s.state.motion,rewrite);
            foreach(var child in sm.stateMachines)RewriteStates(child.stateMachine,rewrite);
        }
        static Motion RewriteMotion(Motion motion,Func<AnimationClip,AnimationClip> rewrite)
        {
            if(motion is AnimationClip clip)return rewrite(clip);
            if(motion is BlendTree bt)
            {
                var copy=UnityEngine.Object.Instantiate(bt);var children=copy.children;
                for(int i=0;i<children.Length;i++)children[i].motion=RewriteMotion(children[i].motion,rewrite);
                copy.children=children;return copy;
            }
            return motion;
        }
    }
}
