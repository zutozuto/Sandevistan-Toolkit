using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Constraint.Components;
using Driver = VRC.SDKBase.VRC_AvatarParameterDriver;

namespace Sandevistan.Editor
{
    public static class SandevistanRig
    {
        public const string Parameter = "Sandevistan/Enabled";
        public const string ColorParameter = "Sandevistan/Color";
        public const string CountParameter = "Sandevistan/Count";
        public const string SpacingParameter = "Sandevistan/Spacing";
        public const string LifetimeParameter = "Sandevistan/Lifetime";
        public const string RunOnlyParameter = "Sandevistan/RunOnly";
        public const string RunActiveParameter = "Sandevistan/_RunActive";
        public const string CountValue = "Sandevistan/_CountValue";
        public const string Velocity = "VelocityMagnitude";
        public const float DefaultRunThreshold = 3.5f;
        public const float FollowSeconds = 0.06f;
        public const float HoldSeconds = 0.4f;
        public const int MaxCapacity = 10;
        public const int MinCount = 3;
        public const float MinSpacing = 0.12f;
        public const float MinLifetime = 0.2f;
        public const float MaxLifetime = 2f;
        public const float DefaultLifetime = 0.4f;
        public static float LifetimeControl(float seconds) => Mathf.InverseLerp(MinLifetime,MaxLifetime,seconds);
        public static float CountControl(int count,int capacity=MaxCapacity) => Mathf.InverseLerp(MinCount,capacity,count);
        public static int CountFromControl(float value,int capacity=MaxCapacity) => Mathf.RoundToInt(Mathf.Lerp(MinCount,capacity,Mathf.Clamp01(value)));
        public static string Request(int slot) => "Sandevistan/_Capture" + slot;

        public sealed class Part { public Mesh mesh; public Material material; public Renderer source; public int[] boneIndices; public bool preserveMesh; public Material sourceMaterial; }
        public sealed class Result
        {
            public GameObject root;
            public AnimatorController controller;
            public float maxSpacing;
            public float runThreshold;
            public int capacity;
            public readonly List<VRCParentConstraint> constraints = new List<VRCParentConstraint>();
            public readonly List<VRCScaleConstraint> scaleConstraints = new List<VRCScaleConstraint>();
            public readonly List<SkinnedMeshRenderer> renderers = new List<SkinnedMeshRenderer>();
            public readonly List<Transform[]> bones = new List<Transform[]>();
            public readonly List<Transform> snapshotRoots = new List<Transform>();
            public readonly List<List<SkinnedMeshRenderer>> slotRenderers = new List<List<SkinnedMeshRenderer>>();
        }

        public static Result Create(Transform avatar, Transform[] sources, Mesh mesh, Material material, string folder,int capacity=3,float maxSpacing=0.5f,float runThreshold=DefaultRunThreshold)
            => CreateParts(avatar, sources, new[]{new Part{mesh=mesh,material=material}},folder,capacity,maxSpacing,runThreshold);

        public static Result CreateParts(Transform avatar, Transform[] sources, Part[] parts, string folder,int capacity=3,float maxSpacing=0.5f,float runThreshold=DefaultRunThreshold,string effectName="Sandevistan_A")
        {
            ValidateSettings(capacity,maxSpacing,runThreshold);
            if(sources.Length==0 || parts.Length==0 || parts.Any(p=>p.mesh==null ||
                p.mesh.bindposes.Length!=(p.boneIndices?.Length??sources.Length) ||
                (p.boneIndices!=null&&p.boneIndices.Any(i=>i<0||i>=sources.Length))))
                throw new ArgumentException("Proxy meshes and source bones must match.");
            if(avatar.Find(effectName)!=null) throw new InvalidOperationException("A Sandevistan installation already uses this name.");
            Directory.CreateDirectory(folder); AssetDatabase.Refresh();
            var slotMaterials=parts.Select(p=>p.material).Distinct().ToDictionary(m=>m,SandevistanOcclusionMaterials.Slots);
            var rig=new Result{root=new GameObject(effectName),capacity=capacity,maxSpacing=maxSpacing,runThreshold=runThreshold};
            rig.root.transform.SetParent(avatar,false);
            var visibility=rig.root.AddComponent<SandevistanVisibility>();
            try
            {
                for(int slot=0;slot<rig.capacity;slot++)
                {
                    var snapshot=new GameObject("Snapshot_"+slot).transform;
                    snapshot.SetParent(rig.root.transform,false);
                    rig.snapshotRoots.Add(snapshot);
                    rig.constraints.Add(AddConstraint(snapshot.gameObject,avatar));
                    var bones=new Transform[sources.Length];
                    for(int b=0;b<sources.Length;b++)
                    {
                        bones[b]=new GameObject("Bone_"+b.ToString("D2")+"_"+sources[b].name).transform;
                        bones[b].SetParent(snapshot,false);
                        bones[b].SetPositionAndRotation(sources[b].position,sources[b].rotation);
                        bones[b].localScale=Divide(sources[b].lossyScale,snapshot.lossyScale);
                        rig.constraints.Add(AddConstraint(bones[b].gameObject,sources[b]));
                        if(parts.Any(p=>p.preserveMesh))rig.scaleConstraints.Add(AddScaleConstraint(bones[b].gameObject,sources[b]));
                    }
                    var slotParts=new List<SkinnedMeshRenderer>();
                    for(int p=0;p<parts.Length;p++)
                    {
                        var part=parts[p];
                        var smr=new GameObject("Part_"+p.ToString("D2")).AddComponent<SkinnedMeshRenderer>();
                        smr.transform.SetParent(snapshot,false);
                        smr.sharedMesh=part.mesh; smr.sharedMaterials=slotMaterials[part.material]; smr.bones=part.boneIndices==null?bones:part.boneIndices.Select(i=>bones[i]).ToArray(); smr.rootBone=snapshot;
                        if(part.preserveMesh&&part.source is SkinnedMeshRenderer sourceSkin)
                        {
                            smr.quality=sourceSkin.quality;
                            for(int shape=0;shape<part.mesh.blendShapeCount;shape++)smr.SetBlendShapeWeight(shape,sourceSkin.GetBlendShapeWeight(shape));
                        }
                        var bounds=part.mesh.bounds; bounds.Expand(0.5f); smr.localBounds=bounds;
                        smr.updateWhenOffscreen=false; smr.shadowCastingMode=ShadowCastingMode.Off; smr.receiveShadows=false;
                        smr.lightProbeUsage=LightProbeUsage.Off; smr.reflectionProbeUsage=ReflectionProbeUsage.Off;
                        smr.motionVectorGenerationMode=MotionVectorGenerationMode.ForceNoMotion; smr.enabled=false;
                        rig.renderers.Add(smr); slotParts.Add(smr);
                        if(part.source!=null)visibility.bindings.Add(new SandevistanVisibility.Binding{source=part.source,proxy=smr,preserveMesh=part.preserveMesh,sourceMaterial=part.sourceMaterial});
                    }
                    rig.slotRenderers.Add(slotParts);rig.bones.Add(bones);
                }
                if(visibility.bindings.Count==0)UnityEngine.Object.DestroyImmediate(visibility);
                rig.controller=BuildController(rig,folder);return rig;
            }
            catch{UnityEngine.Object.DestroyImmediate(rig.root);throw;}
        }

        static Vector3 Divide(Vector3 a,Vector3 b)=>new Vector3(a.x/b.x,a.y/b.y,a.z/b.z);
        public static VRCParentConstraint AddConstraint(GameObject target,Transform source)
        {
            var c=target.AddComponent<VRCParentConstraint>();var so=new SerializedObject(c);
            so.FindProperty("Sources.totalLength").intValue=1;
            so.FindProperty("Sources.source0.SourceTransform").objectReferenceValue=source;
            so.FindProperty("Sources.source0.Weight").floatValue=1;
            so.FindProperty("IsActive").boolValue=false;so.FindProperty("GlobalWeight").floatValue=1;
            so.FindProperty("Locked").boolValue=true;so.FindProperty("FreezeToWorld").boolValue=false;
            so.FindProperty("SolveInLocalSpace").boolValue=false;so.FindProperty("RebakeOffsetsWhenUnfrozen").boolValue=false;
            foreach(string axis in new[]{"AffectsPositionX","AffectsPositionY","AffectsPositionZ","AffectsRotationX","AffectsRotationY","AffectsRotationZ"})so.FindProperty(axis).boolValue=true;
            so.ApplyModifiedPropertiesWithoutUndo();return c;
        }

        static VRCScaleConstraint AddScaleConstraint(GameObject target,Transform source)
        {
            var c=target.AddComponent<VRCScaleConstraint>();var so=new SerializedObject(c);
            so.FindProperty("Sources.totalLength").intValue=1;
            so.FindProperty("Sources.source0.SourceTransform").objectReferenceValue=source;
            so.FindProperty("Sources.source0.Weight").floatValue=1;
            so.FindProperty("IsActive").boolValue=false;so.FindProperty("GlobalWeight").floatValue=1;
            so.FindProperty("Locked").boolValue=true;so.FindProperty("FreezeToWorld").boolValue=false;
            so.FindProperty("SolveInLocalSpace").boolValue=false;so.FindProperty("RebakeOffsetsWhenUnfrozen").boolValue=false;
            foreach(string axis in new[]{"AffectsScaleX","AffectsScaleY","AffectsScaleZ"})so.FindProperty(axis).boolValue=true;
            so.ApplyModifiedPropertiesWithoutUndo();return c;
        }

        public static void ValidateSettings(int capacity,float maxSpacing,float runThreshold=DefaultRunThreshold)
        {
            if(capacity<MinCount||capacity>MaxCapacity)throw new ArgumentOutOfRangeException(nameof(capacity),"Maximum ghosts must be 3–10.");
            if(float.IsNaN(maxSpacing)||float.IsInfinity(maxSpacing)||maxSpacing<MinSpacing)throw new ArgumentOutOfRangeException(nameof(maxSpacing),"Maximum spacing must be finite and at least 0.12 m.");
            if(float.IsNaN(runThreshold)||float.IsInfinity(runThreshold)||runThreshold<0.2f)throw new ArgumentOutOfRangeException(nameof(runThreshold),"Run threshold must be finite and at least 0.2 m/s.");
        }

        static AnimatorController BuildController(Result rig,string folder)
        {
            var ac=AnimatorController.CreateAnimatorControllerAtPath(folder+"/SandevistanFX.controller");
            ac.AddParameter(Parameter,AnimatorControllerParameterType.Bool);AddInt(ac,ColorParameter,0);
            AddFloat(ac,CountParameter,0);AddFloat(ac,SpacingParameter,Mathf.InverseLerp(MinSpacing,rig.maxSpacing,0.2f));
            AddFloat(ac,LifetimeParameter,LifetimeControl(DefaultLifetime));AddInt(ac,CountValue,MinCount);
            ac.AddParameter(Velocity,AnimatorControllerParameterType.Float);
            ac.AddParameter("Grounded",AnimatorControllerParameterType.Bool);
            ac.AddParameter("IsLocal",AnimatorControllerParameterType.Bool);
            ac.AddParameter(RunOnlyParameter,AnimatorControllerParameterType.Bool);
            ac.AddParameter(RunActiveParameter,AnimatorControllerParameterType.Bool);
            for(int i=0;i<rig.capacity;i++)ac.AddParameter(Request(i),AnimatorControllerParameterType.Bool);
            var runGate=ac.layers[0].stateMachine;
            var layers=ac.layers;layers[0].name="Sandevistan Local Run Detection";layers[0].defaultWeight=1;ac.layers=layers;
            var idle=State(runGate,"Not Running",Clock(folder,"Run_Idle",1));runGate.defaultState=idle;
            var running=State(runGate,"Running",Clock(folder,"Run_Active",1));
            Drive(idle,new KeyValuePair<string,float>(RunActiveParameter,0)).localOnly=true;
            Drive(running,new KeyValuePair<string,float>(RunActiveParameter,1)).localOnly=true;
            var runStart=Instant(idle,running);
            runStart.AddCondition(AnimatorConditionMode.If,0,"IsLocal");
            runStart.AddCondition(AnimatorConditionMode.If,0,Parameter);
            runStart.AddCondition(AnimatorConditionMode.If,0,"Grounded");
            runStart.AddCondition(AnimatorConditionMode.Greater,rig.runThreshold,Velocity);
            // The owner publishes one Boolean; remote velocity estimates never decide run eligibility.
            // Only grounded speed can arm/release running; a run jump retains the owner's eligibility.
            // Airborne speed alone cannot arm it. The emitter still suppresses zero-speed captures.
            var disabled=Instant(running,idle);
            disabled.AddCondition(AnimatorConditionMode.If,0,"IsLocal");
            disabled.AddCondition(AnimatorConditionMode.IfNot,0,Parameter);
            var slowed=Instant(running,idle);
            slowed.AddCondition(AnimatorConditionMode.If,0,"IsLocal");
            slowed.AddCondition(AnimatorConditionMode.If,0,"Grounded");
            slowed.AddCondition(AnimatorConditionMode.Less,rig.runThreshold*0.9f,Velocity);
            var emitter=Layer(ac,"Sandevistan Distance Emitter");
            var stopped=State(emitter,"Stopped",Clock(folder,"Stopped",1));emitter.defaultState=stopped;
            Drive(stopped,Enumerable.Range(0,rig.capacity).Select(i=>new KeyValuePair<string,float>(Request(i),0)).ToArray());
            var near=Clock(folder,"Distance_Min",MinSpacing);var far=Clock(folder,"Distance_Max",rig.maxSpacing);
            var distance=DurationTree(ac,"Distance Clock",SpacingParameter,near,far);
            var emit=new AnimatorState[rig.capacity];var wait=new AnimatorState[rig.capacity];
            for(int i=0;i<rig.capacity;i++)
            {
                emit[i]=State(emitter,"Emit_"+i,Clock(folder,"Emit_"+i,0.001f));
                Drive(emit[i],new KeyValuePair<string,float>(Request(i),1));StopWhen(emit[i],stopped);
                wait[i]=State(emitter,"Distance_"+i,distance);
                wait[i].speedParameter=Velocity;wait[i].speedParameterActive=true;
                StopWhen(wait[i],stopped);Timed(emit[i],wait[i]);
            }
            foreach(bool runOnly in new[]{false,true})
            {
                var start=Instant(stopped,wait[rig.capacity-1]);RequireTrigger(start,runOnly);
                start.AddCondition(AnimatorConditionMode.Greater,0.08f,Velocity);
            }
            for(int i=0;i<rig.capacity;i++)
            {
                int next=(i+1)%rig.capacity;
                var t=Timed(wait[i],emit[next]);
                if(next>0)t.AddCondition(AnimatorConditionMode.Greater,next,CountValue);
                if(next>0){t=Timed(wait[i],emit[0]);t.AddCondition(AnimatorConditionMode.Less,next+1,CountValue);}
            }
            // Three Float radial controls occupy 0..1. Only count is quantized to whole snapshots.
            // Neighbour transitions retain the previous count at an exact half-way boundary.
            var counts=Layer(ac,"Sandevistan Count Dial");var choices=new AnimatorState[rig.capacity-MinCount+1];
            for(int i=0;i<choices.Length;i++)
            {
                choices[i]=State(counts,"Count_"+(i+MinCount),Clock(folder,"Count_"+(i+MinCount),1));
                Drive(choices[i],new KeyValuePair<string,float>(CountValue,i+MinCount));
            }
            for(int i=0;i<choices.Length;i++)
            {
                if(i<choices.Length-1)Instant(choices[i],choices[i+1]).AddCondition(AnimatorConditionMode.Greater,(i+0.5f)/(rig.capacity-MinCount),CountParameter);
                if(i>0)Instant(choices[i],choices[i-1]).AddCondition(AnimatorConditionMode.Less,(i-0.5f)/(rig.capacity-MinCount),CountParameter);
            }
            counts.defaultState=choices[0];
            for(int slot=0;slot<rig.capacity;slot++)
            {
                var sm=Layer(ac,"Sandevistan Snapshot "+slot);
                var off=State(sm,"Off",Clip(rig,slot,"Off",FollowSeconds,false,false,folder));sm.defaultState=off;
                Drive(off,new KeyValuePair<string,float>(Request(slot),0));
                var follow=State(sm,"Resample",Clip(rig,slot,"Resample",FollowSeconds,true,false,folder));
                var shortLife=Clip(rig,slot,"Frozen_Min",MinLifetime,true,true,folder);
                var longLife=Clip(rig,slot,"Frozen_Max",MaxLifetime,true,true,folder);
                var frozen=State(sm,"Frozen",DurationTree(ac,"Lifetime "+slot,LifetimeParameter,shortLife,longLife));
                Drive(follow,new KeyValuePair<string,float>(Request(slot),0));
                foreach(bool runOnly in new[]{false,true})
                {
                    var capture=sm.AddAnyStateTransition(follow);Setup(capture);capture.canTransitionToSelf=false;
                    RequireTrigger(capture,runOnly);capture.AddCondition(AnimatorConditionMode.If,0,Request(slot));
                    capture.AddCondition(AnimatorConditionMode.Greater,slot,CountValue);capture.AddCondition(AnimatorConditionMode.Greater,0.04f,Velocity);
                }
                var abort=Instant(follow,off);abort.AddCondition(AnimatorConditionMode.Less,0.04f,Velocity);
                StopWhenRunEnds(follow,off);
                Timed(follow,frozen);Timed(frozen,off);
                foreach(var state in new[]{follow,frozen})
                {
                    var disable=Instant(state,off);disable.AddCondition(AnimatorConditionMode.IfNot,0,Parameter);
                    var excess=Instant(state,off);excess.AddCondition(AnimatorConditionMode.Less,slot+1,CountValue);
                }
            }
            var color=Layer(ac,"Sandevistan Gradient");
            for(int p=0;p<7;p++)
            {
                var clip=new AnimationClip{name="Palette_"+p,frameRate=60};
                foreach(var r in rig.renderers)Constant(clip,AnimationUtility.CalculateTransformPath(r.transform,rig.root.transform),typeof(SkinnedMeshRenderer),"material._Palette",p,1);
                AssetDatabase.CreateAsset(clip,folder+"/"+clip.name+".anim");
                var state=State(color,"Palette_"+p,clip);
                var t=color.AddAnyStateTransition(state);Setup(t);t.canTransitionToSelf=false;t.AddCondition(AnimatorConditionMode.Equals,p,ColorParameter);
            }
            var view=Layer(ac,"Sandevistan Owner View");
            var remote=State(view,"Observer",OwnerViewClip(rig,folder,false));view.defaultState=remote;
            var owner=State(view,"Owner",OwnerViewClip(rig,folder,true));
            Instant(remote,owner).AddCondition(AnimatorConditionMode.If,0,"IsLocal");
            Instant(owner,remote).AddCondition(AnimatorConditionMode.IfNot,0,"IsLocal");
            EditorUtility.SetDirty(ac);AssetDatabase.SaveAssets();return ac;
        }

        static AnimationClip OwnerViewClip(Result rig,string folder,bool owner)
        {
            var clip=new AnimationClip{name=owner?"View_Owner":"View_Observer",frameRate=60};
            foreach(var renderer in rig.renderers)
                Constant(clip,AnimationUtility.CalculateTransformPath(renderer.transform,rig.root.transform),typeof(SkinnedMeshRenderer),"material._IsLocalAvatar",owner?1:0,1);
            AssetDatabase.CreateAsset(clip,folder+"/"+clip.name+".anim");return clip;
        }

        static BlendTree DurationTree(AnimatorController ac,string name,string parameter,AnimationClip min,AnimationClip max)
        {
            var tree=new BlendTree{name=name,blendType=BlendTreeType.Simple1D,blendParameter=parameter,useAutomaticThresholds=false};
            AssetDatabase.AddObjectToAsset(tree,ac);tree.AddChild(min,0);tree.AddChild(max,1);return tree;
        }
        static void AddFloat(AnimatorController ac,string name,float value)=>ac.AddParameter(new AnimatorControllerParameter{name=name,type=AnimatorControllerParameterType.Float,defaultFloat=value});
        static void AddInt(AnimatorController ac,string name,int value)=>ac.AddParameter(new AnimatorControllerParameter{name=name,type=AnimatorControllerParameterType.Int,defaultInt=value});
        static AnimatorStateMachine Layer(AnimatorController ac,string name)
        {
            ac.AddLayer(name);var layers=ac.layers;layers[layers.Length-1].defaultWeight=1;ac.layers=layers;return layers[layers.Length-1].stateMachine;
        }
        static AnimationClip Clock(string folder,string name,float length)
        {
            var clip=new AnimationClip{name=name,frameRate=60};
            // A constant curve on an existing object supplies clip duration without moving it.
            Constant(clip,"",typeof(Transform),"m_LocalScale.x",1,length);
            AssetDatabase.CreateAsset(clip,folder+"/"+name+".anim");return clip;
        }
        static AnimatorState State(AnimatorStateMachine sm,string name,Motion clip)
        {var state=sm.AddState(name);state.motion=clip;state.writeDefaultValues=false;return state;}
        static void Setup(AnimatorStateTransition t){t.hasExitTime=false;t.duration=0;t.hasFixedDuration=true;}
        static AnimatorStateTransition Instant(AnimatorState from,AnimatorState to){var t=from.AddTransition(to);Setup(t);return t;}
        static AnimatorStateTransition Timed(AnimatorState from,AnimatorState to)
        {var t=Instant(from,to);t.hasExitTime=true;t.exitTime=1;return t;}
        static void StopWhen(AnimatorState state,AnimatorState stop)
        {
            var a=Instant(state,stop);a.AddCondition(AnimatorConditionMode.IfNot,0,Parameter);
            var b=Instant(state,stop);b.AddCondition(AnimatorConditionMode.Less,0.04f,Velocity);
            StopWhenRunEnds(state,stop);
        }
        static void RequireTrigger(AnimatorStateTransition transition,bool runOnly)
        {
            transition.AddCondition(AnimatorConditionMode.If,0,Parameter);
            transition.AddCondition(runOnly?AnimatorConditionMode.If:AnimatorConditionMode.IfNot,0,RunOnlyParameter);
            if(runOnly)transition.AddCondition(AnimatorConditionMode.If,0,RunActiveParameter);
        }
        static void StopWhenRunEnds(AnimatorState state,AnimatorState stop)
        {
            var t=Instant(state,stop);t.AddCondition(AnimatorConditionMode.If,0,RunOnlyParameter);
            t.AddCondition(AnimatorConditionMode.IfNot,0,RunActiveParameter);
        }
        static VRCAvatarParameterDriver Drive(AnimatorState state,params KeyValuePair<string,float>[] values)
        {
            var driver=state.AddStateMachineBehaviour<VRCAvatarParameterDriver>();driver.localOnly=false;
            driver.parameters=values.Select(v=>new Driver.Parameter{type=Driver.ChangeType.Set,name=v.Key,value=v.Value}).ToList();
            return driver;
        }
        static AnimationClip Clip(Result rig,int slot,string name,float duration,bool active,bool frozen,string folder)
        {
            var clip=new AnimationClip{name="Snapshot"+slot+"_"+name,frameRate=60};
            var snapshot=rig.snapshotRoots[slot];
            foreach(var c in rig.constraints.Where(c=>c.transform==snapshot || c.transform.parent==snapshot))
            {
                var path=AnimationUtility.CalculateTransformPath(c.transform,rig.root.transform);
                Constant(clip,path,typeof(VRCParentConstraint),"IsActive",active?1:0,duration);
                Constant(clip,path,typeof(VRCParentConstraint),"FreezeToWorld",frozen?1:0,duration);
            }
            foreach(var c in rig.scaleConstraints.Where(c=>c.transform.parent==snapshot))
            {
                var path=AnimationUtility.CalculateTransformPath(c.transform,rig.root.transform);
                Constant(clip,path,typeof(VRCScaleConstraint),"IsActive",active?1:0,duration);
                Constant(clip,path,typeof(VRCScaleConstraint),"FreezeToWorld",frozen?1:0,duration);
            }
            foreach(var r in rig.slotRenderers[slot])
            {
                var path=AnimationUtility.CalculateTransformPath(r.transform,rig.root.transform);
                Constant(clip,path,typeof(SkinnedMeshRenderer),"m_Enabled",frozen?1:0,duration);
                clip.SetCurve(path,typeof(SkinnedMeshRenderer),"material._Fade",frozen?AnimationCurve.Linear(0,1,duration,0):AnimationCurve.Constant(0,duration,0));
                clip.SetCurve(path,typeof(SkinnedMeshRenderer),"material._Age",frozen?AnimationCurve.Linear(0,0,duration,1):AnimationCurve.Constant(0,duration,0));
            }
            AssetDatabase.CreateAsset(clip,folder+"/"+clip.name+".anim");return clip;
        }
        static void Constant(AnimationClip clip,string path,Type type,string property,float value,float duration)
            =>clip.SetCurve(path,type,property,AnimationCurve.Constant(0,duration,value));
    }
}
