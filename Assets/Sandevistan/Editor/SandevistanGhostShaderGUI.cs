using UnityEditor;
using UnityEngine;

namespace Sandevistan.Editor
{
    public sealed class SandevistanGhostShaderGUI : ShaderGUI
    {
        bool textureOptions, gradientOptions, stencilOptions;

        public override void OnGUI(MaterialEditor editor, MaterialProperty[] properties)
        {
            EditorGUI.BeginChangeCheck();
            Draw(editor, properties, true);
            if(EditorGUI.EndChangeCheck())SandevistanOcclusionMaterials.SyncEdits(System.Array.ConvertAll(editor.targets,t=>(Material)t));
        }

        internal void Draw(MaterialEditor editor, MaterialProperty[] properties, bool openButton)
        {
            Section("材质方案");
            var sourceTexture=FindProperty("_UseSourceTexture",properties);
            if(sourceTexture.floatValue>0.5f||sourceTexture.hasMixedValue)
            {
                EditorGUILayout.LabelField("网格路径","原网格＋原主色贴图");
                var main=FindProperty("_MainTex",properties);
                using(new EditorGUI.DisabledScope(editor.targets.Length>1))
                {
                    editor.TexturePropertySingleLine(new GUIContent("原主色贴图 · 使用原 UV"),main,FindProperty("_Color",properties));
                    editor.TextureScaleOffsetProperty(main);
                }
                if(editor.targets.Length>1)EditorGUILayout.LabelField("批量编辑保留各部件的原主贴图、UV 与原主色；切到仅当前材质可单独调整。",EditorStyles.wordWrappedMiniLabel);
                editor.ShaderProperty(FindProperty("_GradientStrength",properties),"渐变染色强度 · 0 关闭 / 1 完整");
                editor.ShaderProperty(FindProperty("_SourceTextureStrength",properties),"主贴图细节强度");
                editor.ShaderProperty(FindProperty("_SourceColorRetention",properties),"原色保留 · 0 明暗染色 / 1 乘原色");
                EditorGUILayout.LabelField("渐变染色强度 0 保留原主贴图颜色，1 使用完整渐变；觉得染色过深可先试 0.3–0.6。各部件材质可分别选择编辑。",EditorStyles.wordWrappedMiniLabel);
            }
            var detail=FindProperty("_DetailMode",properties);
            EditorGUI.showMixedValue=detail.hasMixedValue;EditorGUI.BeginChangeCheck();
            int style=EditorGUILayout.Popup("显示方式",detail.floatValue>0.5f?1:0,sourceTexture.floatValue>0.5f?new[]{"原贴图 · 均匀染色","原贴图 · 叠加表面明暗"}:new[]{"纯色轮廓","细节明暗"});
            if(EditorGUI.EndChangeCheck()){editor.RegisterPropertyChangeUndo("Ghost material style");detail.floatValue=style;foreach(Material target in editor.targets){target.SetFloat("_DetailMode",style);SandevistanOcclusionMaterials.SetDetailKeyword(target);}}
            EditorGUI.showMixedValue=false;
            if(detail.floatValue>0.5f||detail.hasMixedValue)editor.ShaderProperty(FindProperty("_DetailStrength",properties),"表面明暗强度");

            Section("菲涅尔发光");
            editor.ShaderProperty(FindProperty("_Brightness",properties),"整体亮度");
            editor.ShaderProperty(FindProperty("_RimColor",properties),sourceTexture.floatValue>0.5f?"发光颜色 · HDR（跟随渐变染色强度）":"发光颜色 · HDR（乘上当前渐变）");
            editor.ShaderProperty(FindProperty("_RimStrength",properties),"菲涅尔发光强度 · 0 关闭");
            editor.ShaderProperty(FindProperty("_RimPower",properties),"菲涅尔收紧 · 越大边缘越窄");
            EditorGUILayout.LabelField("轮廓外的光晕需要世界启用 Bloom。",EditorStyles.wordWrappedMiniLabel);
            Section("半透明");
            DrawOpacity(editor);

            Section("细密网纹");
            var weave=FindProperty("_WeaveStrength",properties);
            editor.ShaderProperty(weave,"网纹强度 · 0 关闭");
            if(weave.floatValue>0.001f||weave.hasMixedValue)
            {
                editor.ShaderProperty(FindProperty("_WeaveDensity",properties),"网纹密度 · 每米单元数");
                editor.ShaderProperty(FindProperty("_WeaveWidth",properties),"网纹线宽 · 占单元宽度");
                EditorGUILayout.LabelField("网纹改变表面明暗，不额外发光；远处自动抗锯齿，减少密集图案闪烁。",EditorStyles.wordWrappedMiniLabel);
            }

            Section("本人视野保护");
            var protection=FindProperty("_LocalViewProtection",properties);
            editor.ShaderProperty(protection,"淡出本人相机附近的残影");
            if(protection.floatValue>0.5f||protection.hasMixedValue)
            {
                var near=FindProperty("_LocalHideDistance",properties);var far=FindProperty("_LocalFadeDistance",properties);
                EditorGUI.BeginChangeCheck();
                editor.ShaderProperty(near,"完全隐藏距离 · 米");
                editor.ShaderProperty(far,"恢复显示距离 · 米");
                if(EditorGUI.EndChangeCheck())far.floatValue=Mathf.Max(near.floatValue+0.01f,far.floatValue);
                EditorGUILayout.LabelField("仅影响本人普通视角，镜子和拍照相机保留完整效果，旁观者不受影响。需新版 FX 安装；距离按世界米数计算。",EditorStyles.wordWrappedMiniLabel);
            }

            Section("发光线纹");
            var patternMode=FindProperty("_PatternMode",properties);
            EditorGUI.showMixedValue=patternMode.hasMixedValue;EditorGUI.BeginChangeCheck();
            int pattern=EditorGUILayout.Popup("图案",Mathf.Clamp(Mathf.RoundToInt(patternMode.floatValue),0,3),new[]{"关闭","蜂巢","十字网格","方格"});
            if(EditorGUI.EndChangeCheck()){editor.RegisterPropertyChangeUndo("Ghost line pattern");patternMode.floatValue=pattern;}EditorGUI.showMixedValue=false;
            if(patternMode.floatValue>0.5f||patternMode.hasMixedValue)
            {
                editor.ShaderProperty(FindProperty("_PatternColor",properties),"线条颜色 · HDR");
                editor.ShaderProperty(FindProperty("_PatternBrightness",properties),"线条亮度");
                editor.ShaderProperty(FindProperty("_PatternDensity",properties),"图案密度 · 每米单元数");
                editor.ShaderProperty(FindProperty("_PatternWidth",properties),"线条粗细 · 占单元宽度");
                EditorGUILayout.LabelField("无需贴图，可与自己的表面纹理叠加。",EditorStyles.wordWrappedMiniLabel);
            }

            Section("表面纹理");
            var texture = FindProperty("_SurfaceTex", properties);
            var strength = FindProperty("_SurfaceStrength", properties);
            EditorGUI.BeginChangeCheck();
            editor.TexturePropertySingleLine(new GUIContent("表面纹理 · 拖入自己的图片"), texture);
            if (EditorGUI.EndChangeCheck())
            {
                editor.RegisterPropertyChangeUndo("Surface texture strength");
                if(texture.textureValue==null)strength.floatValue=0;
                else if(strength.floatValue<0.001f)strength.floatValue=1;
            }
            if(texture.textureValue!=null)
            {
                editor.ShaderProperty(strength, "纹理强度 · 0 关闭");
                editor.TextureScaleOffsetProperty(texture);
                EditorGUILayout.LabelField("Tiling 越大图案越小；重复图片建议设置为 Repeat。",EditorStyles.wordWrappedMiniLabel);
                textureOptions=EditorGUILayout.Foldout(textureOptions,"纹理选项",true);
                if(textureOptions)
                {
                    var scroll=FindProperty("_SurfaceScroll",properties);
                    EditorGUI.showMixedValue=scroll.hasMixedValue;EditorGUI.BeginChangeCheck();
                    var next=EditorGUILayout.Vector2Field("滚动速度 XY",new Vector2(scroll.vectorValue.x,scroll.vectorValue.y));
                    if(EditorGUI.EndChangeCheck()){editor.RegisterPropertyChangeUndo("Texture scroll");scroll.vectorValue=new Vector4(next.x,next.y,0,0);}EditorGUI.showMixedValue=false;
                    editor.ShaderProperty(FindProperty("_SurfaceColor", properties), "图片原色 · 0 保留渐变 / 1 乘上原色");
                    editor.ShaderProperty(FindProperty("_SurfaceAlpha", properties), "图片 Alpha 影响");
                    editor.ShaderProperty(FindProperty("_SurfaceLumaMask", properties), "黑色透明 · 亮度遮罩");
                }
            }

            EditorGUILayout.Space(8);
            gradientOptions=EditorGUILayout.Foldout(gradientOptions,"自定义经典渐变 · HDR",true);
            if(gradientOptions)
            {
                editor.ShaderProperty(FindProperty("_FreshColor", properties), "新残影");
                editor.ShaderProperty(FindProperty("_MiddleColor", properties), "中段残影");
                editor.ShaderProperty(FindProperty("_OldColor", properties), "旧残影");
                EditorGUILayout.LabelField("只影响经典配色；其余六套配色由 Shader 内置。", EditorStyles.wordWrappedMiniLabel);
            }

            stencilOptions=EditorGUILayout.Foldout(stencilOptions,"高级 · 与其他特效的遮挡冲突",true);
            if(stencilOptions)
            {
                var bit=FindProperty("_StencilBit",properties);var values=new[]{0,1,2,4,8,16,32,64,128};
                EditorGUI.showMixedValue=bit.hasMixedValue;EditorGUI.BeginChangeCheck();
                int selected=EditorGUILayout.Popup("防重复叠色 · Stencil 位",System.Array.IndexOf(values,Mathf.RoundToInt(bit.floatValue)),new[]{"Off 关闭","1","2","4","8","16（默认）","32","64","128"});
                if(EditorGUI.EndChangeCheck()){editor.RegisterPropertyChangeUndo("Stencil anti stacking bit");bit.floatValue=values[Mathf.Clamp(selected,0,8)];}EditorGUI.showMixedValue=false;
                EditorGUILayout.LabelField("仅在其他特效占用同一 Stencil 位时更改；Off 会关闭防重复叠色。",EditorStyles.wordWrappedMiniLabel);
            }
            EditorGUILayout.Space(8);
            if(GUILayout.Button("应用半透与发光默认值（保留纹理）"))
            {
                editor.RegisterPropertyChangeUndo("半透与发光默认材质");
                foreach(Material target in editor.targets){SandevistanOcclusionMaterials.ApplyStyle(target,target.GetFloat("_DetailMode")>0.5f);if(target.GetFloat("_UseSourceTexture")>0.5f)SandevistanOcclusionMaterials.ApplySourceStyle(target);EditorUtility.SetDirty(target);}
                GUI.changed=true;
            }
            if (openButton && editor.target is Material material && GUILayout.Button("打开静态材质预览…", GUILayout.Height(28)))
                SandevistanMaterialPreviewWindow.Open(material);
        }

        internal static float EffectiveOpacity(Material material)=>material.GetFloat("_Opacity")*material.GetFloat("_MasterOpacity");
        internal static void SetOpacity(Material material,float value)
        {
            material.SetFloat("_Opacity",Mathf.Clamp01(value));material.SetFloat("_MasterOpacity",1);
        }
        static void DrawOpacity(MaterialEditor editor)
        {
            float value=EffectiveOpacity((Material)editor.target);bool mixed=false;
            foreach(Material material in editor.targets)if(!Mathf.Approximately(EffectiveOpacity(material),value)){mixed=true;break;}
            EditorGUI.showMixedValue=mixed;EditorGUI.BeginChangeCheck();
            float next=EditorGUILayout.Slider("不透明度 · 0 透明 / 1 不透明",value,0,1);
            if(EditorGUI.EndChangeCheck())
            {
                editor.RegisterPropertyChangeUndo("Ghost opacity");
                foreach(Material material in editor.targets){SetOpacity(material,next);EditorUtility.SetDirty(material);}
            }
            EditorGUI.showMixedValue=false;
        }

        static void Section(string title)
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        }
    }
}
