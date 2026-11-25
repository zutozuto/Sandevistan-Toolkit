using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Sandevistan.Editor
{
    public sealed class SandevistanDependencyWindow : EditorWindow
    {
        static readonly string[] Actions = { "安装", "移动测试", "材质预览" };

        [MenuItem("Tools/Sandevistan...")]
        public static void Open()
        {
            var window = GetWindow<SandevistanDependencyWindow>("斯安威斯坦");
            window.minSize = new Vector2(320, 145);
        }

        void OnGUI()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("by：ずっと子都", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(SandevistanMissingDependencies.Message, MessageType.Info);
            EditorGUILayout.Space(6);

            using (new EditorGUILayout.HorizontalScope())
            {
                foreach (var action in Actions)
                {
                    if (GUILayout.Button(action, GUILayout.Height(26)))
                        SandevistanMissingDependencies.ShowDialog();
                }
            }
        }
    }

    internal static class SandevistanMissingDependencies
    {
        internal static readonly string Message = GetMessage();

        internal static void ShowDialog()
        {
            EditorUtility.DisplayDialog("斯安威斯坦 · 缺少依赖", Message, "确定");
        }

        static string GetMessage()
        {
            var missing = new List<string>();
#if !SANDEVISTAN_VRC_BASE || !SANDEVISTAN_VRC_AVATARS
            missing.Add("VRChat Avatars SDK");
#endif
#if !SANDEVISTAN_MODULAR_AVATAR
            missing.Add("Modular Avatar");
#endif
#if !SANDEVISTAN_NDMF
            missing.Add("NDMF");
#endif
            return "当前工程缺少 " + string.Join("、", missing)
                + "。请在 VRChat Creator Companion 管理的 Avatars 工程中安装所需依赖后使用。";
        }
    }
}
