using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Sandevistan.Editor
{
    public static class SandevistanOcclusionMaterials
    {
        public const string DepthShader = "Sandevistan/Ghost Depth";
        public const string CleanupShader="Sandevistan/Ghost Stencil Cleanup";
        public const int ColorQueue = 3100;
        public const int DepthQueue = 3090;
        const string Folder = "Assets/Sandevistan_Depth";
        static readonly string[] CoverageFloats={"_SurfaceStrength","_SurfaceAlpha","_SurfaceLumaMask","_MasterOpacity","_Fade","_Opacity","_StencilBit","_PreviewTime","_LocalViewProtection","_LocalHideDistance","_LocalFadeDistance","_IsLocalAvatar","_UseSourceTexture","_SourceAlpha","_SourceCutoff"};
        static readonly string[] AppearanceFloats={"_DetailMode","_DetailStrength","_Brightness","_GradientStrength","_SourceTextureStrength","_SourceColorRetention","_WeaveStrength","_WeaveDensity","_WeaveWidth","_SurfaceStrength","_SurfaceColor","_SurfaceAlpha","_SurfaceLumaMask","_MasterOpacity","_Opacity","_RimStrength","_RimPower","_LocalViewProtection","_LocalHideDistance","_LocalFadeDistance","_PatternMode","_PatternBrightness","_PatternDensity","_PatternWidth","_StencilBit"};
        static readonly string[] AppearanceColors={"_FreshColor","_MiddleColor","_OldColor","_RimColor","_PatternColor"};

        static string DepthPath(Material color)
        {
            string guid=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(color));
            return string.IsNullOrEmpty(guid)?null:Folder+"/Depth-"+guid+".mat";
        }
        public static Material Ensure(Material color)
        {
            if(!SandevistanMaterialPreviewWindow.IsGhost(color))throw new ArgumentException("Expected a Historical Ghost material.");
            string path=DepthPath(color);
            if(path==null)throw new InvalidOperationException("Save the color material as a .mat asset before creating its depth partner.");
            Directory.CreateDirectory(Folder);AssetDatabase.Refresh();
            var depth=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(depth==null)
            {
                depth=CreateTemporary(color);depth.hideFlags=HideFlags.None;depth.name=color.name+" (depth only)";
                AssetDatabase.CreateAsset(depth,path);
            }
            Copy(color,depth);return depth;
        }
        public static Material EnsureCleanup(Material color)
        {
            string path=DepthPath(color)?.Replace("Depth-","Cleanup-");if(path==null)throw new InvalidOperationException("Save the ghost material first.");
            Directory.CreateDirectory(Folder);
            var result=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(result==null){result=CreateCleanup(color);result.hideFlags=HideFlags.None;AssetDatabase.CreateAsset(result,path);}
            Copy(color,result);return result;
        }
        public static Material CreateCleanup(Material color)
        {
            var result=new Material(Shader.Find(CleanupShader)){hideFlags=HideFlags.HideAndDontSave};Copy(color,result);return result;
        }
        public static Material[] Slots(Material color)=>new[]{color,Ensure(color),EnsureCleanup(color)};
        public static Material CreateTemporary(Material color)
        {
            var shader=Shader.Find(DepthShader);if(shader==null)throw new InvalidOperationException("Ghost Depth shader is missing.");
            var result=new Material(shader){hideFlags=HideFlags.HideAndDontSave};Copy(color,result);return result;
        }
        public static void Copy(Material color,Material depth)
        {
            SetDetailKeyword(color);
            if(color.HasProperty("_StencilComp"))color.SetFloat("_StencilComp",color.GetFloat("_StencilBit")<0.5f?8:6);
            foreach(string property in CoverageFloats)depth.SetFloat(property,color.GetFloat(property));
            depth.SetTexture("_MaskTex",color.GetTexture("_MaskTex"));
            depth.SetTexture("_MainTex",color.GetTexture("_MainTex"));
            depth.SetTextureScale("_MainTex",color.GetTextureScale("_MainTex"));
            depth.SetTextureOffset("_MainTex",color.GetTextureOffset("_MainTex"));
            depth.SetColor("_Color",color.GetColor("_Color"));
            depth.SetTexture("_SurfaceTex",color.GetTexture("_SurfaceTex"));
            depth.SetTextureScale("_SurfaceTex",color.GetTextureScale("_SurfaceTex"));
            depth.SetTextureOffset("_SurfaceTex",color.GetTextureOffset("_SurfaceTex"));
            depth.SetVector("_SurfaceScroll",color.GetVector("_SurfaceScroll"));
            // Coverage passes have no detail/color variants.
            depth.shaderKeywords=Array.Empty<string>();
            // Queues are global: all clothes/props write depth before any ghost writes color.
            depth.renderQueue=depth.shader.name==CleanupShader?3101:DepthQueue;
        }
        internal static Material[] ExistingCompanions(Material color)
        {
            string path=DepthPath(color);if(path==null)return Array.Empty<Material>();
            return new[]{AssetDatabase.LoadAssetAtPath<Material>(path),AssetDatabase.LoadAssetAtPath<Material>(path.Replace("Depth-","Cleanup-"))}
                .Where(m=>m!=null).ToArray();
        }
        public static void Sync(Material color)
        {
            SetDetailKeyword(color);
            if(color.HasProperty("_StencilComp"))color.SetFloat("_StencilComp",color.GetFloat("_StencilBit")<0.5f?8:6);
            foreach(var companion in ExistingCompanions(color)){Copy(color,companion);EditorUtility.SetDirty(companion);}
        }
        internal static void SyncEdits(Material[] targets)
        {
            var companions=targets.SelectMany(ExistingCompanions).Distinct().ToArray();
            if(companions.Length>0)Undo.RecordObjects(companions,"修改残影遮挡材质");
            foreach(var color in targets){Sync(color);EditorUtility.SetDirty(color);}
        }
        internal static int ApplyAppearance(Material source,Material[] targets)
        {
            if(!SandevistanMaterialPreviewWindow.IsGhost(source))return 0;
            var colors=targets.Where(m=>SandevistanMaterialPreviewWindow.IsGhost(m)&&AssetDatabase.Contains(m)).Distinct().ToArray();
            if(colors.Length==0)return 0;
            Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("应用全部残影材质效果");
            Undo.RecordObjects(colors.SelectMany(m=>new[]{m}.Concat(ExistingCompanions(m))).Distinct().ToArray(),"应用全部残影材质效果");
            foreach(var target in colors)
            {
                if(target!=source)CopyAppearance(source,target);
                Sync(target);EditorUtility.SetDirty(target);
            }
            Undo.CollapseUndoOperations(group);return colors.Length;
        }
        internal static void CopyAppearance(Material source,Material target)
        {
            // Mesh-dependent coverage, source textures and FX state belong to the new mesh.
            foreach(string property in AppearanceFloats)target.SetFloat(property,source.GetFloat(property));
            foreach(string property in AppearanceColors)target.SetColor(property,source.GetColor(property));
            target.SetVector("_SurfaceScroll",source.GetVector("_SurfaceScroll"));
            target.SetTexture("_SurfaceTex",source.GetTexture("_SurfaceTex"));
            target.SetTextureScale("_SurfaceTex",source.GetTextureScale("_SurfaceTex"));
            target.SetTextureOffset("_SurfaceTex",source.GetTextureOffset("_SurfaceTex"));
        }
        internal static void Save(Material[] targets)
        {
            foreach(var color in targets.Where(m=>SandevistanMaterialPreviewWindow.IsGhost(m)&&AssetDatabase.Contains(m)).Distinct())
            {
                Sync(color);EditorUtility.SetDirty(color);AssetDatabase.SaveAssetIfDirty(color);
                foreach(var companion in ExistingCompanions(color))AssetDatabase.SaveAssetIfDirty(companion);
            }
        }
        public static void SyncRenderer(Renderer renderer)
        {
            var materials=renderer.sharedMaterials;
            if(materials.Length==0||!SandevistanMaterialPreviewWindow.IsGhost(materials[0]))return;
            if(materials[0].renderQueue!=ColorQueue)throw new InvalidOperationException("Ghost color queue must remain 3100.");
            if(materials.Length!=3)renderer.sharedMaterials=Slots(materials[0]);
            else {Copy(materials[0],materials[1]);Copy(materials[0],materials[2]);}
        }
        public static void ApplyStyle(Material material,bool detailed)
        {
            ApplyFlatPreset(material);
            material.SetFloat("_DetailMode",detailed?1:0);
            material.SetFloat("_DetailStrength",0.65f);
            material.SetFloat("_RimStrength",2);
            if(detailed)material.EnableKeyword("_GHOST_DETAIL");else material.DisableKeyword("_GHOST_DETAIL");
        }
        public static void SetDetailKeyword(Material material)
        {
            if(material.GetFloat("_DetailMode")>0.5f)material.EnableKeyword("_GHOST_DETAIL");else material.DisableKeyword("_GHOST_DETAIL");
        }
        public static void ApplySourceStyle(Material material)
        {
            material.SetFloat("_UseSourceTexture",1);
            material.SetFloat("_GradientStrength",1);
            material.SetFloat("_SourceTextureStrength",1);material.SetFloat("_SourceColorRetention",0.1f);
            material.SetFloat("_Opacity",0.4f);material.SetFloat("_RimStrength",1.5f);
            material.SetFloat("_WeaveStrength",0.4f);material.SetFloat("_WeaveDensity",90);material.SetFloat("_WeaveWidth",0.14f);
        }
        public static void ApplyFlatPreset(Material material)
        {
            material.SetFloat("_DetailMode",0);material.DisableKeyword("_GHOST_DETAIL");
            material.SetFloat("_MasterOpacity",1);material.SetFloat("_Opacity",0.35f);
            material.SetFloat("_RimStrength",0);
            material.SetFloat("_Brightness",1);material.SetFloat("_RimPower",3);material.SetColor("_RimColor",Color.white);
            material.SetFloat("_PatternMode",0);material.SetFloat("_PatternBrightness",3);
            material.SetFloat("_WeaveStrength",0);
            material.SetFloat("_PatternDensity",12);material.SetFloat("_PatternWidth",0.05f);
            material.SetColor("_PatternColor",new Color(0.15f,1,1,1));
            material.SetFloat("_StencilBit",16);material.SetFloat("_StencilComp",6);material.renderQueue=ColorQueue;
        }
    }
}
