using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using Settings=Sandevistan.SandevistanVisibility.InstallationSettings;

namespace Sandevistan.Editor
{
    public sealed class SandevistanInstaller : ScriptableObject
    {
        [SerializeField] VRCAvatarDescriptor avatar;
        [SerializeField] Renderer[] meshes = Array.Empty<Renderer>();
        [SerializeField] bool[] selected = Array.Empty<bool>();
        internal const int DefaultTriangleTarget = 8000;
        [SerializeField] int triangleTarget = DefaultTriangleTarget;
        [SerializeField] string settingsStatus;
        [SerializeField] int loadedInstallationId;
        Vector2 scroll;

        [SerializeField] int capacity=3;
        [SerializeField] float maxSpacing=0.5f;
        [SerializeField] float runThreshold=SandevistanRig.DefaultRunThreshold;
        [SerializeField] bool detailed;
        [SerializeField] bool originalMesh;
        public static void Open()=>SandevistanWindow.OpenTab(0);
        internal void ReadSelection()
        {
            if(Selection.activeGameObject!=null)SetAvatar(Selection.activeGameObject.GetComponentInParent<VRCAvatarDescriptor>());
        }

        internal void FollowSelection()
        {
            var next=Selection.activeGameObject!=null?Selection.activeGameObject.GetComponentInParent<VRCAvatarDescriptor>():null;
            if(next!=null&&next!=avatar)SetAvatar(next);
        }

        internal void RefreshInstallation()
        {
            if(avatar==null)return;
            var effect=avatar.transform.Find("Sandevistan_A");
            if((effect!=null?effect.GetInstanceID():0)!=loadedInstallationId)SetAvatar(avatar);
        }

        internal void SetAvatar(VRCAvatarDescriptor value)
        {
            avatar=value;
            meshes=avatar!=null?SandevistanProxy.Candidates(avatar.transform):Array.Empty<Renderer>();
            selected=meshes.Select(r=>r is SkinnedMeshRenderer).ToArray();
            capacity=3;maxSpacing=0.5f;runThreshold=SandevistanRig.DefaultRunThreshold;
            triangleTarget=DefaultTriangleTarget;detailed=false;originalMesh=false;settingsStatus=null;
            var existing=avatar!=null?avatar.transform.Find("Sandevistan_A"):null;
            loadedInstallationId=existing!=null?existing.GetInstanceID():0;
            if(existing==null)return;
            var settings=SandevistanInstallation.Read(existing,out bool inferred);
            capacity=settings.capacity;maxSpacing=settings.maxSpacing;runThreshold=settings.runThreshold;
            triangleTarget=settings.targetTriangles;detailed=settings.detailed;originalMesh=settings.originalMesh;
            if(!inferred||settings.sources.Length>0)selected=meshes.Select(r=>settings.sources.Contains(r)).ToArray();
            settingsStatus=inferred?"已从旧安装恢复可读取的设置；旧版未保存目标面数，减面模式按当前实际面数估算，请核对。更新后会完整记录配置。":"已读取此人物的安装配置，修改后可直接更新并替换。";
        }

        internal void RefreshMeshes()
        {
            var previous=meshes;var choices=selected;
            meshes=avatar!=null?SandevistanProxy.Candidates(avatar.transform):Array.Empty<Renderer>();
            selected=meshes.Select(r=>{int index=Array.IndexOf(previous,r);return index>=0&&index<choices.Length&&choices[index];}).ToArray();
        }

        internal Settings CurrentSettings()=>new Settings{capacity=capacity,maxSpacing=maxSpacing,runThreshold=runThreshold,
            targetTriangles=triangleTarget,detailed=detailed,originalMesh=originalMesh,
            sources=meshes.Where((r,i)=>r!=null&&i<selected.Length&&selected[i]).ToArray()};

        internal void DrawGUI()
        {
            RefreshInstallation();
            var next=(VRCAvatarDescriptor)EditorGUILayout.ObjectField("目标人物",avatar,typeof(VRCAvatarDescriptor),true);
            if(next!=avatar)SetAvatar(next);
            using(new EditorGUILayout.HorizontalScope())
            {
                if(GUILayout.Button("读取选中人物"))ReadSelection();
                if(GUILayout.Button("刷新网格"))RefreshMeshes();
            }
            if(!string.IsNullOrEmpty(settingsStatus))EditorGUILayout.HelpBox(settingsStatus,MessageType.None);
            capacity=EditorGUILayout.IntSlider("最大残影数（实际生成）",capacity,SandevistanRig.MinCount,SandevistanRig.MaxCapacity);
            originalMesh=EditorGUILayout.Popup("网格与贴图",originalMesh?1:0,new[]{"减面代理（默认）","原网格＋原主色贴图 · 不减面"})==1;
            if(originalMesh)
            {
                EditorGUILayout.LabelField("材质方案","原贴图 · 渐变半透与细密网纹");
                EditorGUILayout.LabelField("保留选中部件的全部三角面、原 UV 与骨骼权重，直接引用各材质主贴图。默认用渐变染色，保留贴图明暗细节；发光线纹仍关闭。",EditorStyles.wordWrappedMiniLabel);
            }
            else
            {
            EditorGUI.BeginChangeCheck();
            int mode=EditorGUILayout.Popup("材质方案",detailed?1:0,new[]{"纯色轮廓（默认）","细节明暗 · 保留表面起伏"});
            if(EditorGUI.EndChangeCheck())detailed=mode==1;
            EditorGUILayout.LabelField(detailed?"填充保留五官和衣褶明暗，叠加菲涅尔发光。":"填充颜色均匀，叠加菲涅尔发光。",EditorStyles.wordWrappedMiniLabel);
            triangleTarget=EditorGUILayout.IntSlider("每道目标三角面（合计）",triangleTarget,1000,50000);
            EditorGUILayout.HelpBox("两种模式默认均为每道 8,000 面。面数越高，每道的几何和蒙皮负担越大；最大残影数越多，生成的网格、骨骼与约束越多。额外预建面数约为每道面数 × 最大残影数；菜单调低显示数量不会删除已生成的池。",MessageType.None);
            }
            maxSpacing=EditorGUILayout.FloatField("间距轮盘上限（米）",maxSpacing);
            if(float.IsNaN(maxSpacing)||float.IsInfinity(maxSpacing))maxSpacing=0.5f;
            maxSpacing=Mathf.Max(SandevistanRig.MinSpacing,maxSpacing);
            EditorGUILayout.LabelField("间距轮盘：0.12–"+maxSpacing.ToString("0.###")+" 米；存留：0.2–2 秒。",EditorStyles.wordWrappedMiniLabel);
            runThreshold=EditorGUILayout.FloatField("跑步触发速度（米/秒）",runThreshold);
            if(float.IsNaN(runThreshold)||float.IsInfinity(runThreshold))runThreshold=SandevistanRig.DefaultRunThreshold;
            runThreshold=Mathf.Max(0.2f,runThreshold);
            EditorGUILayout.LabelField("菜单可切换一直触发 / 仅跑步。一直触发时站立仍停发；仅跑步在地面达到速度门槛后触发，跑跳时延续，落地减速后停发。原地或走路起跳不会在空中启动。不能直接读取 Shift；门槛应高于走路、低于跑步速度，可按世界调整。",EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(5);EditorGUILayout.LabelField("选择所有可能穿戴的部件",EditorStyles.boldLabel);
            scroll=EditorGUILayout.BeginScrollView(scroll,GUILayout.MinHeight(140));
            for(int i=0;i<meshes.Length;i++)
            {
                if(meshes[i]==null)continue;
                selected[i]=EditorGUILayout.ToggleLeft(meshes[i].name+(!meshes[i].enabled||!meshes[i].gameObject.activeInHierarchy?" [当前隐藏]":"")+" · "+(SandevistanProxy.GetMesh(meshes[i]).triangles.Length/3).ToString("N0")+" 面",selected[i]);
            }
            EditorGUILayout.EndScrollView();
            var chosen=meshes.Where((r,i)=>r!=null&&selected[i]).ToArray();
            int sourceTriangles=chosen.Sum(TriangleCount);
            if(originalMesh&&chosen.Length>0)
            {
                int sourceParts=chosen.Sum(r=>r.sharedMaterials.Distinct().Count());
                int boneCount=chosen.SelectMany(r=>r is SkinnedMeshRenderer skin&&SandevistanProxy.GetMesh(r).bindposes.Length>0?skin.bones:new[]{r.transform}).Where(b=>b!=null).Distinct().Count();
                EditorGUILayout.HelpBox("原网格预估：每道 "+sourceTriangles.ToString("N0")+" 三角面；容量 "+capacity+" 共增加约 "+((long)sourceTriangles*capacity).ToString("N0")+" 面、"+sourceParts*capacity+" 个 Renderer / "+sourceParts*capacity*3+" 个材质槽、"+(2*boneCount+1)*capacity+" 个官方约束（位置旋转＋缩放）。原贴图直接复用，不压成小图集。面数、材质分组和骨骼越多，开销越高；请以构建后的 SDK 评级为准。",MessageType.Warning);
            }
            bool installed=avatar!=null&&avatar.transform.Find("Sandevistan_A")!=null;
            EditorGUILayout.HelpBox(installed?"此人物已有残影。更新会先生成新安装，成功后替换原安装，并保留透明度、渐变、发光等材质效果。支持 Ctrl+Z；失败时保留原效果。":
                "实际只生成 "+capacity+" 个池，"+(originalMesh?"原始几何合计约 "+((long)sourceTriangles*capacity).ToString("N0"):"目标合计约 "+(triangleTarget*capacity).ToString("N0"))+" 面；隐藏池也计入 SDK 评级。需要 PC SDK、Modular Avatar 和 NDMF。",MessageType.None);
            using(new EditorGUI.DisabledScope(avatar==null||EditorApplication.isPlaying||!selected.Any(x=>x)))
            if(GUILayout.Button(installed?"更新并替换安装":"安装到人物",GUILayout.Height(32)))
            {
                try
                {
                    var result=Install(avatar,chosen,triangleTarget,capacity,maxSpacing,detailed,runThreshold,originalMesh);
                    SetAvatar(avatar);Selection.activeGameObject=result.root;SandevistanWindow.Installed(result);
                    EditorUtility.DisplayDialog("安装完成","已生成 "+result.capacity+" 个残影池。用移动测试页与 Gesture Manager 测试，再用 SDK Build & Test 检查最终评级。","确定");
                }
                catch(Exception ex){Debug.LogException(ex);EditorUtility.DisplayDialog("安装未完成",ex.Message,"确定");}
            }
        }

        static int TriangleCount(Renderer renderer)
        {
            var mesh=SandevistanProxy.GetMesh(renderer);
            return Enumerable.Range(0,mesh.subMeshCount).Where(s=>mesh.GetTopology(s)==UnityEngine.MeshTopology.Triangles).Sum(s=>(int)mesh.GetIndexCount(s)/3);
        }

        public static SandevistanRig.Result Install(VRCAvatarDescriptor descriptor,Renderer[] meshes,int target,int capacity=3,float maxSpacing=0.5f,bool detailed=false,float runThreshold=SandevistanRig.DefaultRunThreshold,bool originalMesh=false)
        {
            if(descriptor==null)throw new ArgumentNullException(nameof(descriptor));
            if(Application.isPlaying)throw new InvalidOperationException("Exit Play mode before installing.");
            SandevistanRig.ValidateSettings(capacity,maxSpacing,runThreshold);
            var root=descriptor.transform;
            if(EditorUtility.IsPersistent(root)||!root.gameObject.scene.IsValid())throw new InvalidOperationException("Install on an avatar in a scene or open Prefab Mode.");
            if(meshes==null||meshes.Length==0)throw new InvalidOperationException("Select at least one source renderer.");
            var candidates=SandevistanProxy.Candidates(root);
            if(meshes.Any(r=>r==null||!candidates.Contains(r)))throw new InvalidOperationException("Selected source renderers must belong to this avatar and be outside the installed effect.");
            meshes=meshes.Distinct().ToArray();
            if(target<=0)throw new ArgumentOutOfRangeException(nameof(target));
            var scale=root.lossyScale;
            if(Mathf.Min(scale.x,Mathf.Min(scale.y,scale.z))<=0||Mathf.Abs(scale.x-scale.y)>0.001f||Mathf.Abs(scale.x-scale.z)>0.001f)
                throw new InvalidOperationException("Avatar root must have a positive uniform scale.");
            foreach(string name in new[]{"ModularAvatarMergeAnimator","ModularAvatarParameters","ModularAvatarMenuInstaller"})
                if(FindMA(name)==null) throw new InvalidOperationException("Install Modular Avatar before using this installer.");
            if(root.Cast<Transform>().Count(t=>t.name=="Sandevistan_A")>1)throw new InvalidOperationException("This avatar has multiple Sandevistan_A roots. Keep a single installation before updating.");
            var previous=root.Find("Sandevistan_A");
            var bones=originalMesh?SandevistanOriginalMesh.GetBones(root,meshes):SandevistanProxy.GetMajorBones(root.GetComponent<Animator>());
            string folder="Assets/Sandevistan_Installed/"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6);
            SandevistanRig.Result rig=null;
            try
            {
                Directory.CreateDirectory(folder);AssetDatabase.Refresh();
                SandevistanRig.Part[] parts;int sourceTriangles,ghostTriangles;double seconds;bool cacheHit=false;
                if(originalMesh)
                {
                    var original=SandevistanOriginalMesh.Build(root,meshes,folder);
                    bones=original.bones;parts=original.parts;sourceTriangles=ghostTriangles=original.triangles;seconds=original.seconds;
                }
                else
                {
                    var proxy=SandevistanProxy.Build(root,bones,meshes,target,folder);
                    var shader=Shader.Find("Sandevistan/Historical Ghost");
                    if(shader==null) throw new InvalidOperationException("Historical Ghost shader is missing.");
                    var material=new Material(shader){name="Sandevistan Ghost"};
                    material.SetTexture("_MaskTex",proxy.mask);
                    SandevistanOcclusionMaterials.ApplyStyle(material,detailed);
                    AssetDatabase.CreateAsset(material,folder+"/Ghost.mat");
                    bones=proxy.bones;
                    parts=proxy.parts.Select((m,i)=>new SandevistanRig.Part{mesh=m,material=material,source=proxy.sources[i]}).ToArray();
                    sourceTriangles=proxy.originalTriangles;ghostTriangles=proxy.mesh.triangles.Length/3;seconds=proxy.seconds;cacheHit=proxy.cacheHit;
                }
                SandevistanInstallation.PreserveAppearance(previous,parts,detailed,originalMesh);
                rig=SandevistanRig.CreateParts(root,bones,parts,folder,capacity,maxSpacing,runThreshold,"Sandevistan_Pending_"+Guid.NewGuid().ToString("N"));
                var metadata=rig.root.GetComponent<SandevistanVisibility>();
                metadata.installation=new Settings{capacity=capacity,maxSpacing=maxSpacing,runThreshold=runThreshold,
                    targetTriangles=target,detailed=detailed,originalMesh=originalMesh,sources=meshes.ToArray()};
                EditorUtility.SetDirty(metadata);
                AttachModularAvatar(rig,folder);
                File.WriteAllText(folder+"/Installation-report.txt",
                    "Sandevistan Toolkit v5.7 / PC Avatar\nDefault: OFF / trigger: all movement / material appearance retained when replacing\nInstallation settings: saved in build-stripped metadata\nSynced parameters: 3 Bool + Int + 3 Float = 35 bits\n"+
                    "Owner view protection: enabled by material default; hide within 0.65m, fade to full at 1m; mirrors/photo cameras bypass; observers unaffected\nOwner flag: built-in IsLocal animated to all three material slots; no new synced parameters\n"+
                    "Trigger menu: all movement (stationary suppressed) / run only\nRun speed threshold: "+rig.runThreshold+" m/s while grounded; release below "+rig.runThreshold*0.9f+" m/s while grounded\nRun jumps retain eligibility until landing; airborne speed cannot arm running; master OFF resets immediately\nRun eligibility: owner-only detection, Boolean synchronized to observers; no direct Shift-key input\n"+
                    "Style: "+(originalMesh?"Original mesh/main texture, gradient tint, translucent fine weave":detailed?"Detail":"Solid silhouette")+"\nCapacity: "+rig.capacity+"\nSource meshes: "+string.Join(", ",meshes.Select(r=>r.name))+"\n"+
                    "Requested target triangles: "+target+(originalMesh?" (unused in original mode)":"")+"\nSource triangles: "+sourceTriangles+"\nTriangles per ghost: "+ghostTriangles+"\n"+
                    "Mesh generation: "+seconds.ToString("0.000")+" seconds / cache hit: "+cacheHit+"\nAdditional triangles (all prebuilt slots): "+(long)ghostTriangles*rig.capacity+"\nAdditional skinned renderers: "+rig.renderers.Count+"\nAdditional material slots: "+rig.renderers.Count*3+"\nPrebuild counts. Each group draws depth + color + cleanup; queues 3090 / 3100 / 3101.\n"+
                    (originalMesh?"No reduction or transparent-triangle deletion. Original UV channels, variable bone weights, bind poses and BlendShape frames preserved. Original material parts bypass proxy merging.\n":"Fast topology edge collapse; actual count may exceed target at topology limits. Build merges identical visibility/animation groups.\n")+
                    "Additional constraints: "+(rig.constraints.Count+rig.scaleConstraints.Count)+" (parent: "+rig.constraints.Count+", scale: "+rig.scaleConstraints.Count+")\nAdditional sampled bones: "+bones.Length*rig.capacity+"\n"+
                    "Spacing dial: "+SandevistanRig.MinSpacing+" to "+rig.maxSpacing+" metres (editable installation limit)\nCount dial: 3 to "+rig.capacity+" whole ghosts\nLifetime dial: 0.2 to 2.0 seconds\n"+
                    "Runtime custom scripts: none\n"+(originalMesh?"Main textures: original asset references, original import resolution and UV0 tiling/offset/tint; no color atlas or texture copies\n":"Texture atlas: 1024 max\n")+
                    "This report is the effect's incremental budget. Final avatar rank, parameter budget and upload size require SDK validation after MA build.\n"+
                    "Current costume GameObject / Renderer visibility is inherited at build; it also changes existing ghosts.\n"+
                    "Runtime material changes, dissolve, BlendShape changes and runtime mesh replacement are NOT automatically mirrored. BlendShape weights use installation values. Cloth deformation and arbitrary shader vertex displacement are not captured.\n"+
                    "Spacing uses VelocityMagnitude integration, so frame rate and remote velocity estimation affect accuracy. Physical roomscale motion is not guaranteed to update that built-in parameter.\n"+
                    "Lifetime is a maximum; a full pool reuses the oldest slot early.\n");
                AssetDatabase.SaveAssets();
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
                SandevistanInstallation.Commit(rig,previous);
                return rig;
            }
            catch
            {
                if(rig?.root!=null)UnityEngine.Object.DestroyImmediate(rig.root);
                SandevistanInstallation.DiscardGeneratedAssets(folder);throw;
            }
        }

        static Type FindMA(string name)
        {
            return AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("nadena.dev.modular_avatar.core."+name)).FirstOrDefault(t=>t!=null);
        }

        static void AddOptions(VRCExpressionsMenu parent,string folder,string label,string parameter,string[] choices,int offset)
        {
            var menu=ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            for(int i=0;i<choices.Length;i++)menu.controls.Add(new VRCExpressionsMenu.Control{
                name=choices[i],type=VRCExpressionsMenu.Control.ControlType.Toggle,value=i+offset,
                parameter=new VRCExpressionsMenu.Control.Parameter{name=parameter}
            });
            AssetDatabase.CreateAsset(menu,folder+"/Menu_"+parameter.Substring(parameter.IndexOf('/')+1)+".asset");
            parent.controls.Add(new VRCExpressionsMenu.Control{name=label,type=VRCExpressionsMenu.Control.ControlType.SubMenu,subMenu=menu});
        }

        static void AddDial(VRCExpressionsMenu menu,string label,string parameter)
        {
            menu.controls.Add(new VRCExpressionsMenu.Control{
                name=label,type=VRCExpressionsMenu.Control.ControlType.RadialPuppet,
                subParameters=new[]{new VRCExpressionsMenu.Control.Parameter{name=parameter}}
            });
        }

        public static void AttachModularAvatar(SandevistanRig.Result rig,string folder)
        {
            var merge=rig.root.AddComponent(FindMA("ModularAvatarMergeAnimator"));
            var so=new SerializedObject(merge);
            so.FindProperty("animator").objectReferenceValue=rig.controller;
            so.FindProperty("layerType").intValue=(int)VRCAvatarDescriptor.AnimLayerType.FX;
            so.FindProperty("pathMode").enumValueIndex=0;
            so.FindProperty("deleteAttachedAnimator").boolValue=true;
            so.FindProperty("matchAvatarWriteDefaults").boolValue=true;
            so.ApplyModifiedPropertiesWithoutUndo();

            var parameters=rig.root.AddComponent(FindMA("ModularAvatarParameters"));
            so=new SerializedObject(parameters);
            var names=new[]{SandevistanRig.Parameter,SandevistanRig.ColorParameter,SandevistanRig.CountParameter,SandevistanRig.SpacingParameter,SandevistanRig.LifetimeParameter,SandevistanRig.RunOnlyParameter,SandevistanRig.RunActiveParameter};
            var defaults=new[]{0f,0f,0f,Mathf.InverseLerp(SandevistanRig.MinSpacing,rig.maxSpacing,0.2f),SandevistanRig.LifetimeControl(SandevistanRig.DefaultLifetime),0f,0f};
            var list=so.FindProperty("parameters");list.arraySize=names.Length;
            for(int i=0;i<names.Length;i++)
            {
                var parameter=list.GetArrayElementAtIndex(i);
                parameter.FindPropertyRelative("nameOrPrefix").stringValue=names[i];
                parameter.FindPropertyRelative("syncType").enumValueIndex=i==0||i>=5?3:i==1?1:2;
                parameter.FindPropertyRelative("localOnly").boolValue=false;
                parameter.FindPropertyRelative("saved").boolValue=false;
                parameter.FindPropertyRelative("defaultValue").floatValue=defaults[i];
                parameter.FindPropertyRelative("hasExplicitDefaultValue").boolValue=true;
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            var submenu=ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            submenu.controls.Add(new VRCExpressionsMenu.Control {
                name="启用残影",type=VRCExpressionsMenu.Control.ControlType.Toggle,value=1,
                parameter=new VRCExpressionsMenu.Control.Parameter{name=SandevistanRig.Parameter}
            });
            AddOptions(submenu,folder,"触发方式",SandevistanRig.RunOnlyParameter,new[]{"一直触发 · 移动时","仅跑步时触发"},0);
            AddOptions(submenu,folder,"渐变配色",SandevistanRig.ColorParameter,new[]{"经典 · 绿青紫","极光 · 绿蓝紫","落日 · 金红紫","冰川 · 白青蓝","焰火 · 金橙红","霓虹 · 青紫粉","酸性 · 黄绿青"},0);
            AddDial(submenu,"数量 · "+(rig.capacity==3?"固定3":"3–"+rig.capacity),SandevistanRig.CountParameter);
            AddDial(submenu,"间距 · 0.12–"+rig.maxSpacing.ToString("0.###")+"m",SandevistanRig.SpacingParameter);
            AddDial(submenu,"存留 · 0.2–2秒",SandevistanRig.LifetimeParameter);
            AssetDatabase.CreateAsset(submenu,folder+"/SandevistanMenu.asset");
            var entry=ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            entry.controls.Add(new VRCExpressionsMenu.Control {name="斯安威斯坦",type=VRCExpressionsMenu.Control.ControlType.SubMenu,subMenu=submenu});
            AssetDatabase.CreateAsset(entry,folder+"/MenuEntry.asset");
            var installer=rig.root.AddComponent(FindMA("ModularAvatarMenuInstaller"));
            so=new SerializedObject(installer);
            so.FindProperty("menuToAppend").objectReferenceValue=entry;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
