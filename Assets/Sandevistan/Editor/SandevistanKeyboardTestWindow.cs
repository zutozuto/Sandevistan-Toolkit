using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Sandevistan.Editor
{
    // Lives entirely in Editor: no avatar component, build hook, or runtime dependency is added.
    public sealed class SandevistanKeyboardTestWindow : ScriptableObject
    {
        [SerializeField] GameObject target;
        [SerializeField] float walkSpeed=3,runSpeed=6,jumpHeight=1;
        [SerializeField] bool cameraRelative=true,sceneKeys=true,autoEffect=true;
        readonly HashSet<KeyCode> keys=new HashSet<KeyCode>();
        SandevistanGestureBridge bridge;
        SandevistanTestMotor motor;
        bool running,jumpRequested,ownsEffect,sceneMouseLook;
        Vector3 startPosition,velocity;
        Quaternion startRotation;
        int lastFrame;
        string status="先进入 Play 模式，并让 Gesture Manager 开始模拟你的人物。";

        internal EditorWindow host;
        public static void Open()=>SandevistanWindow.OpenTab(1);
        void Repaint(){if(host!=null)host.Repaint();}
        void Focus(){if(host!=null)host.Focus();}
        internal void LoseFocus(){ClearInput();ZeroHorizontal();}
        void OnEnable()
        {
            EditorApplication.update+=Tick;EditorApplication.playModeStateChanged+=PlayModeChanged;
            SceneView.duringSceneGui+=SceneInput;AssemblyReloadEvents.beforeAssemblyReload+=Stop;
        }
        void OnDisable()
        {
            Stop();EditorApplication.update-=Tick;EditorApplication.playModeStateChanged-=PlayModeChanged;
            SceneView.duringSceneGui-=SceneInput;AssemblyReloadEvents.beforeAssemblyReload-=Stop;
        }
        void PlayModeChanged(PlayModeStateChange state)
        {if(state!=PlayModeStateChange.EnteredPlayMode)Stop();}
        internal void DrawGUI()
        {
            if(running)HandleKeys(Event.current);
            EditorGUILayout.LabelField("WASD 移动 · 空格跳跃 · Shift 加速",EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("仅在 Play 模式下控制 Gesture Manager 正在模拟的人物。点击本窗口或 Scene 视图后使用键盘；点击菜单或其他窗口会停走。按 Esc 结束。"+(SupportsGameKeys?" 也支持 Game 视图。":""),MessageType.Info);
            using(new EditorGUI.DisabledScope(running))
            {
                target=(GameObject)EditorGUILayout.ObjectField("人物根节点",target,typeof(GameObject),true);
                if(GUILayout.Button("读取 Gesture Manager 当前人物"))SelectControlledAvatar();
            }
            walkSpeed=EditorGUILayout.Slider("移动速度（米/秒）",walkSpeed,0.1f,15);
            runSpeed=EditorGUILayout.Slider("Shift 速度（米/秒）",runSpeed,walkSpeed,30);
            EditorGUILayout.LabelField("测试“仅跑步”时，走路速度需低于安装门槛，Shift 速度需高于门槛（默认 3.5 m/s）。菜单模式由你选择，测试工具只自动开启总开关。",EditorStyles.wordWrappedMiniLabel);
            jumpHeight=EditorGUILayout.Slider("跳跃高度（米）",jumpHeight,0.1f,3);
            cameraRelative=EditorGUILayout.Toggle("方向相对 Scene 视角",cameraRelative);
            sceneKeys=EditorGUILayout.Toggle("允许在 Scene 视图按键",sceneKeys);
            using(new EditorGUI.DisabledScope(running))autoEffect=EditorGUILayout.Toggle("测试时自动开启残影",autoEffect);
            using(new EditorGUI.DisabledScope(!EditorApplication.isPlaying||EditorApplication.isPaused))
            {
                if(GUILayout.Button(running?"停止控制":"开始键盘控制",GUILayout.Height(32)))
                {
                    GUIUtility.keyboardControl=0;if(running)Stop();else StartControl();
                }
            }
            using(new EditorGUI.DisabledScope(!running))if(GUILayout.Button("回到起点"))ResetPosition();
            EditorGUILayout.HelpBox(status,MessageType.None);
            if(running)
            {
                EditorGUILayout.LabelField("当前速度",velocity.magnitude.ToString("0.00")+" m/s");
                EditorGUILayout.LabelField("状态",motor.grounded?"已落地":"跳跃 / 下落");
                EditorGUILayout.LabelField("输入",HasInputFocus()?"键盘已就绪":"点击工具 / Scene 视图继续");
            }
            EditorGUILayout.LabelField("按启动位置的高度模拟平地，不进行场景碰撞检测。",EditorStyles.wordWrappedMiniLabel);
        }
        void SelectControlledAvatar()
        {
            var avatars=SandevistanGestureBridge.ControlledAvatars();
            var selected=Selection.activeGameObject;
            if(selected!=null)
                for(var t=selected.transform;t!=null;t=t.parent)
                    if(avatars.Contains(t.gameObject)){target=t.gameObject;return;}
            if(avatars.Length==1){target=avatars[0];status="已选择 "+target.name;}
            else if(avatars.Length==0)status="还没有已连接的人物：进入 Play 模式，在 Gesture Manager 中开始模拟。";
            else status="有多个模拟人物，请将目标人物根节点拖入上方。";
        }
        void StartControl()
        {
            if(!EditorApplication.isPlaying||EditorApplication.isPaused)return;
            try
            {
                if(target==null)SelectControlledAvatar();
                bridge=SandevistanGestureBridge.Connect(target);
                startPosition=target.transform.position;startRotation=target.transform.rotation;
                motor=new SandevistanTestMotor(startPosition);lastFrame=Time.frameCount;ClearInput();sceneMouseLook=false;
                ownsEffect=autoEffect&&bridge.Has("Sandevistan/Enabled")&&bridge.Read("Sandevistan/Enabled")<0.5f;
                running=true;if(ownsEffect)bridge.Set("Sandevistan/Enabled",1);
                bridge.Push(Vector3.zero,true);Focus();
                status="已连接 "+target.name+"。WASD 移动，空格跳跃，Shift 加速，Esc 停止。";
            }
            catch(Exception ex){status=Message(ex);Stop();}
        }
        static bool SupportsGameKeys
        {
            get
            {
#if ENABLE_LEGACY_INPUT_MANAGER
                return true;
#else
                return false;
#endif
            }
        }
        static bool IsGameView=>SupportsGameKeys&&EditorWindow.focusedWindow!=null&&EditorWindow.focusedWindow.GetType().Name=="GameView";
        bool HasInputFocus()=>UnityEditorInternal.InternalEditorUtility.isApplicationActive&&(EditorWindow.focusedWindow==host||(sceneKeys&&EditorWindow.focusedWindow is SceneView)||IsGameView);
        void ReadGameKeys()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if(Input.GetKey(KeyCode.LeftAlt)||Input.GetKey(KeyCode.RightAlt)||Input.GetKey(KeyCode.LeftControl)||Input.GetKey(KeyCode.RightControl)){ClearInput();return;}
            if(Input.GetKeyDown(KeyCode.Escape)){Stop();return;}
            keys.Clear();
            foreach(var key in new[]{KeyCode.W,KeyCode.A,KeyCode.S,KeyCode.D,KeyCode.LeftShift,KeyCode.RightShift})
                if(Input.GetKey(key))keys.Add(key);
            jumpRequested=Input.GetKeyDown(KeyCode.Space);
#endif
        }
        void SceneInput(SceneView view)
        {
            if(running&&sceneKeys&&EditorWindow.focusedWindow==view)
            {
                HandleKeys(Event.current);
            }
        }
        static bool IsMoveKey(KeyCode key)=>key==KeyCode.W||key==KeyCode.A||key==KeyCode.S||key==KeyCode.D||key==KeyCode.Space||key==KeyCode.LeftShift||key==KeyCode.RightShift;
        void HandleKeys(Event e)
        {
            if(e==null)return;
            if(e.type==EventType.MouseDown&&e.button==1){ClearInput();sceneMouseLook=true;return;}
            if(e.type==EventType.MouseUp&&e.button==1){sceneMouseLook=false;return;}
            if(e.type==EventType.MouseDown&&e.button==0)sceneMouseLook=false;
            if(sceneMouseLook)return;
            if(e.alt||e.control||e.command||EditorGUIUtility.editingTextField){ClearInput();return;}
            if(e.type==EventType.KeyDown&&e.keyCode==KeyCode.Escape){e.Use();Stop();return;}
            if(!IsMoveKey(e.keyCode))return;
            if(e.type==EventType.KeyDown)
            {
                bool first=keys.Add(e.keyCode);if(e.keyCode==KeyCode.Space&&first)jumpRequested=true;e.Use();
            }
            else if(e.type==EventType.KeyUp){keys.Remove(e.keyCode);e.Use();}
        }
        void ClearInput(){keys.Clear();jumpRequested=false;}
        void ZeroHorizontal()
        {
            if(!running||bridge==null||!bridge.Connected)return;
            try{velocity=new Vector3(0,velocity.y,0);bridge.Push(velocity,motor.grounded);}
            catch(Exception){/* The simulator may already be disconnecting during a reload. */}
        }
        void Tick()
        {
            if(!running)return;
            if(!EditorApplication.isPlaying||target==null||bridge==null||!bridge.Connected){Stop();return;}
            if(EditorApplication.isPaused||Time.timeScale<=0){ClearInput();velocity=Vector3.zero;bridge.Push(velocity,motor.grounded);Repaint();return;}
            if(lastFrame==Time.frameCount)return;lastFrame=Time.frameCount;
            try
            {
                bool focused=HasInputFocus()&&!EditorGUIUtility.editingTextField;
                if(!focused){ClearInput();sceneMouseLook=false;}
                else if(IsGameView){ReadGameKeys();if(!running)return;}
                var input=new Vector2((keys.Contains(KeyCode.D)?1:0)-(keys.Contains(KeyCode.A)?1:0),(keys.Contains(KeyCode.W)?1:0)-(keys.Contains(KeyCode.S)?1:0));
                var forward=Vector3.forward;
                if(cameraRelative&&SceneView.lastActiveSceneView!=null&&SceneView.lastActiveSceneView.camera!=null)
                    forward=SceneView.lastActiveSceneView.camera.transform.forward;
                forward=Vector3.ProjectOnPlane(forward,Vector3.up);
                if(forward.sqrMagnitude<0.0001f)forward=Vector3.forward;forward.Normalize();
                var direction=Vector3.Cross(Vector3.up,forward)*input.x+forward*input.y;
                float speed=keys.Contains(KeyCode.LeftShift)||keys.Contains(KeyCode.RightShift)?runSpeed:walkSpeed;
                float frameDt=Time.deltaTime;if(frameDt<=0)return;
                Vector3 before=target.transform.position;
                // A limited step avoids a large teleport on a stalled editor frame; report actual displacement.
                motor.position=before;motor.Step(direction,speed,jumpRequested,jumpHeight,Mathf.Min(frameDt,0.1f));jumpRequested=false;
                target.transform.position=motor.position;velocity=(motor.position-before)/frameDt;
                if(motor.grounded)velocity.y=0;
                bridge.Push(velocity,motor.grounded);SceneView.RepaintAll();Repaint();
            }
            catch(Exception ex){status=Message(ex);Stop();}
        }
        void ResetPosition()
        {
            ClearInput();if(target==null||motor==null)return;
            target.transform.SetPositionAndRotation(startPosition,startRotation);motor=new SandevistanTestMotor(startPosition);velocity=Vector3.zero;
            if(bridge!=null&&bridge.Connected)bridge.Push(Vector3.zero,true);
        }
        internal void Stop()
        {
            ClearInput();
            try
            {
                if(running&&bridge!=null&&bridge.Connected)
                {
                    if(target!=null&&motor!=null){var p=target.transform.position;p.y=motor.floor;target.transform.position=p;}
                    bridge.Push(Vector3.zero,true);if(ownsEffect)bridge.Set("Sandevistan/Enabled",0);
                }
            }
            catch(Exception){/* Play-mode teardown can invalidate the manager's graph first. */}
            running=false;ownsEffect=false;bridge=null;motor=null;velocity=Vector3.zero;Repaint();
        }
        static string Message(Exception ex)=>"未能控制人物："+(ex is TargetInvocationException t&&t.InnerException!=null?t.InnerException.Message:ex.Message);
    }

    public sealed class SandevistanTestMotor
    {
        public Vector3 position;
        public readonly float floor;
        public float verticalSpeed;
        public bool grounded=true;
        public SandevistanTestMotor(Vector3 start){position=start;floor=start.y;}
        public void Step(Vector3 direction,float speed,bool jump,float height,float dt)
        {
            if(dt<=0)return;
            direction.y=0;direction=Vector3.ClampMagnitude(direction,1);position+=direction*Mathf.Max(0,speed)*dt;
            if(jump&&grounded){verticalSpeed=Mathf.Sqrt(2*9.81f*Mathf.Max(0,height));grounded=false;}
            if(!grounded)
            {
                position.y+=verticalSpeed*dt-0.5f*9.81f*dt*dt;verticalSpeed-=9.81f*dt;
                if(position.y<=floor&&verticalSpeed<=0){position.y=floor;verticalSpeed=0;grounded=true;}
            }
        }
    }

    // Public Gesture Manager APIs are reflected so this editor tool also imports when GM is absent.
    public sealed class SandevistanGestureBridge
    {
        const string ManagerName="BlackStartX.GestureManager.GestureManager";
        readonly object module;
        readonly GameObject avatar;
        readonly MethodInfo setMethod,readMethod;
        readonly Dictionary<string,object> parameters=new Dictionary<string,object>();
        static Type ManagerType=>AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType(ManagerName)).FirstOrDefault(t=>t!=null);
        static IDictionary Registry=>ManagerType?.GetField("ControlledAvatars",BindingFlags.Public|BindingFlags.Static)?.GetValue(null) as IDictionary;
        public static GameObject[] ControlledAvatars()
        {
            var registry=Registry;if(registry==null)return Array.Empty<GameObject>();
            return registry.Keys.Cast<object>().OfType<GameObject>().Where(g=>g!=null&&g.activeInHierarchy).ToArray();
        }
        public static SandevistanGestureBridge Connect(GameObject avatar)
        {
            if(avatar==null)throw new InvalidOperationException("请先读取或指定 Gesture Manager 正在模拟的人物根节点。");
            var registry=Registry;
            if(registry==null)throw new InvalidOperationException("找不到 Gesture Manager。请安装并启动模拟（已验证 3.9.8）。");
            if(!registry.Contains(avatar))throw new InvalidOperationException("这个人物尚未连接 Gesture Manager，或选中了人物的子物体。");
            return new SandevistanGestureBridge(avatar,registry[avatar]);
        }
        SandevistanGestureBridge(GameObject avatar,object module)
        {
            this.avatar=avatar;this.module=module;
            var getter=module.GetType().GetMethod("GetParam",new[]{typeof(string)});
            if(getter==null)throw new InvalidOperationException("需要 Gesture Manager 的 Avatars 3.0 模拟器。");
            foreach(var name in new[]{"VelocityX","VelocityY","VelocityZ","VelocityMagnitude","Grounded","Sandevistan/Enabled"})
            {
                var parameter=getter.Invoke(module,new object[]{name});
                if(parameter==null&&name!="Sandevistan/Enabled")throw new InvalidOperationException("模拟器缺少参数 "+name+"；请重新开始模拟。");
                if(parameter!=null)parameters.Add(name,parameter);
            }
            var type=getter.ReturnType;
            setMethod=type.GetMethods().FirstOrDefault(m=>m.Name=="Set"&&m.GetParameters().Length==3&&m.GetParameters()[1].ParameterType==typeof(float));
            readMethod=type.GetMethod("FloatValue",Type.EmptyTypes);
            if(setMethod==null||readMethod==null)throw new InvalidOperationException("当前 Gesture Manager 参数 API 不受支持。");
        }
        public bool Connected=>avatar!=null&&avatar.activeInHierarchy&&Registry!=null&&Registry.Contains(avatar)&&ReferenceEquals(Registry[avatar],module);
        public bool Has(string name)=>parameters.ContainsKey(name);
        public float Read(string name)=>(float)readMethod.Invoke(parameters[name],null);
        public void Set(string name,float value)=>setMethod.Invoke(parameters[name],new object[]{module,value,this});
        public void Push(Vector3 worldVelocity,bool grounded)
        {
            var local=avatar.transform.InverseTransformDirection(worldVelocity);
            Set("Grounded",grounded?1:0);Set("VelocityX",local.x);Set("VelocityY",local.y);Set("VelocityZ",local.z);
            Set("VelocityMagnitude",worldVelocity.magnitude);
        }
    }
}
