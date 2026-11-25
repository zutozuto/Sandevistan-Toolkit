using UnityEditor;
using UnityEngine;

namespace Sandevistan.Editor
{
    public sealed class SandevistanWindow : EditorWindow
    {
        [SerializeField] int tab;
        [SerializeField] SandevistanInstaller installer;
        [SerializeField] SandevistanKeyboardTestWindow keyboard;
        [SerializeField] SandevistanMaterialPreviewWindow materials;

        [MenuItem("Tools/Sandevistan...")]
        public static void Open()=>OpenTab(0);
        public static SandevistanWindow OpenTab(int index)
        {
            var window=GetWindow<SandevistanWindow>("斯安威斯坦");
            window.minSize=new Vector2(800,580);window.EnsurePanels();window.SelectTab(index);
            if(index==0)window.installer.ReadSelection();
            if(index==2)window.materials.ReadSelection();
            window.Show();return window;
        }
        internal static void Installed(SandevistanRig.Result rig)
        {
            var window=GetWindow<SandevistanWindow>();window.EnsurePanels();
            window.materials.SetMaterial(rig.renderers[0].sharedMaterial);window.materials.SetSample(rig.renderers[0]);
        }
        internal static void Preview(Material material)
        {
            var window=OpenTab(2);window.materials.SetMaterial(material);
        }
        void OnEnable(){EnsurePanels();Undo.undoRedoPerformed+=OnUndoRedo;}
        void OnUndoRedo(){if(installer!=null)installer.RefreshInstallation();Repaint();}
        void OnSelectionChange()
        {
            EnsurePanels();if(tab==0)installer.FollowSelection();Repaint();
        }
        void EnsurePanels()
        {
            if(installer==null)installer=CreateInstance<SandevistanInstaller>();
            if(keyboard==null)keyboard=CreateInstance<SandevistanKeyboardTestWindow>();
            if(materials==null)materials=CreateInstance<SandevistanMaterialPreviewWindow>();
            foreach(var panel in new ScriptableObject[]{installer,keyboard,materials})panel.hideFlags=HideFlags.HideAndDontSave;
            keyboard.host=this;materials.host=this;materials.visible=tab==2;
        }
        internal void SelectTab(int index)
        {
            if(tab==1&&index!=1)keyboard.Stop();
            tab=index;materials.visible=tab==2;GUIUtility.keyboardControl=0;Repaint();
            if(index==0)installer.FollowSelection();
        }
        void OnLostFocus(){if(keyboard!=null)keyboard.LoseFocus();}
        void OnGUI()
        {
            EnsurePanels();
            EditorGUILayout.LabelField("by：ずっと子都", EditorStyles.boldLabel);
            int next=GUILayout.Toolbar(tab,new[]{"安装","移动测试","材质预览"},GUILayout.Height(28));
            if(next!=tab)SelectTab(next);
            EditorGUILayout.Space(6);
            switch(tab){case 0:installer.DrawGUI();break;case 1:keyboard.DrawGUI();break;case 2:materials.DrawGUI();break;}
        }
        void OnDisable()
        {
            Undo.undoRedoPerformed-=OnUndoRedo;
            if(keyboard!=null)DestroyImmediate(keyboard);
            if(materials!=null)DestroyImmediate(materials);
            if(installer!=null)DestroyImmediate(installer);
            keyboard=null;materials=null;installer=null;
        }
    }
}
