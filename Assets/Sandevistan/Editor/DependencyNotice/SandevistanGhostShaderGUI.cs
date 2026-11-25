using UnityEditor;
using UnityEngine;

namespace Sandevistan.Editor
{
    // The shader looks up this type by name. Show the dependency notice in the Inspector too.
    public sealed class SandevistanGhostShaderGUI : ShaderGUI
    {
        public override void OnGUI(MaterialEditor editor, MaterialProperty[] properties)
        {
            EditorGUILayout.HelpBox(SandevistanMissingDependencies.Message, MessageType.Info);
            if (GUILayout.Button("材质预览"))
                SandevistanMissingDependencies.ShowDialog();
        }
    }
}
