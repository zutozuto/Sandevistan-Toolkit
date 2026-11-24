Shader "Sandevistan/Historical Ghost"
{
    Properties
    {
        [Toggle(_GHOST_DETAIL)] _DetailMode ("Detail shading", Float) = 0
        _DetailStrength ("Detail shading strength", Range(0,1)) = 0.65
        _Brightness ("Overall brightness", Range(0,8)) = 1
        [HideInInspector] _UseSourceTexture ("Original mesh main texture", Float) = 0
        _MainTex ("Original main texture (UV0)", 2D) = "white" {}
        _Color ("Original main tint", Color) = (1,1,1,1)
        _GradientStrength ("Gradient tint strength", Range(0,1)) = 1
        _SourceTextureStrength ("Original texture detail strength", Range(0,1)) = 1
        _SourceColorRetention ("Original color retention", Range(0,1)) = 0.1
        [HideInInspector] _SourceAlpha ("Use original texture alpha", Float) = 0
        [HideInInspector] _SourceCutoff ("Original alpha cutoff", Range(0,1)) = 0.001
        _WeaveStrength ("Fine mesh pattern strength", Range(0,1)) = 0
        _WeaveDensity ("Fine mesh cells per metre", Range(8,160)) = 90
        _WeaveWidth ("Fine mesh line fraction", Range(0.03,0.5)) = 0.14
        [HideInInspector] _MaskTex ("Silhouette alpha mask", 2D) = "white" {}
        _SurfaceTex ("Surface texture (your image)", 2D) = "white" {}
        _SurfaceStrength ("Surface texture strength", Range(0,1)) = 0
        _SurfaceColor ("Use image colors", Range(0,1)) = 0
        _SurfaceAlpha ("Use image alpha", Range(0,1)) = 1
        _SurfaceLumaMask ("Use brightness as opacity", Range(0,1)) = 0
        _SurfaceScroll ("Texture scroll (tiles per second XY)", Vector) = (0,0,0,0)
        [HideInInspector] _MasterOpacity ("Legacy opacity multiplier", Range(0,1)) = 1
        [HideInInspector] _Fade ("Visibility", Range(0,1)) = 0
        [HideInInspector] _Age ("Age", Range(0,1)) = 0
        [HideInInspector] _Palette ("Gradient index", Range(0,6)) = 0
        [HDR] _FreshColor ("Fresh", Color) = (0.08,1,0.48,1)
        [HDR] _MiddleColor ("Middle", Color) = (0.05,0.7,1,1)
        [HDR] _OldColor ("Old", Color) = (0.58,0.12,1,1)
        [HDR] _RimColor ("Fresnel emission tint", Color) = (1,1,1,1)
        _RimStrength ("Fresnel emission strength", Range(0,10)) = 2
        _RimPower ("Fresnel power (higher is narrower)", Range(0.5,8)) = 3
        _Opacity ("Opacity", Range(0,1)) = 0.35
        [Toggle] _LocalViewProtection ("Protect owner's near view", Float) = 1
        _LocalHideDistance ("Owner view fully hidden within (metres)", Range(0,2)) = 0.65
        _LocalFadeDistance ("Owner view fully visible beyond (metres)", Range(0.01,3)) = 1
        [HideInInspector] _IsLocalAvatar ("Owner flag managed by FX", Float) = 0
        [Enum(Off,0,Honeycomb,1,CrossGrid,2,SquareGrid,3)] _PatternMode ("Emissive line pattern", Float) = 0
        [HDR] _PatternColor ("Line color", Color) = (0.15,1,1,1)
        _PatternBrightness ("Line brightness", Range(0,12)) = 3
        _PatternDensity ("Pattern cells per metre", Range(2,40)) = 12
        _PatternWidth ("Line width as cell fraction", Range(0.01,0.15)) = 0.05
        _StencilBit ("Equal-depth anti stacking bit", Float) = 16
        [HideInInspector] _StencilComp ("Stencil comparison", Float) = 6
        [HideInInspector] _PreviewTime ("Editor preview time", Float) = -1
    }
    SubShader
    {
        Tags { "Queue"="Transparent+100" "RenderType"="Transparent" "VRCFallback"="Hidden" }
        Cull Off
        ZTest LEqual
        ZWrite Off
        Stencil { Ref [_StencilBit] ReadMask [_StencilBit] WriteMask [_StencilBit] Comp [_StencilComp] Pass Replace }
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma shader_feature_local _ _GHOST_DETAIL
            #include "UnityCG.cginc"
            #define SANDEVISTAN_COLOR_PASS 1
            #include "GhostSurface.cginc"
            ENDCG
        }
    }
    CustomEditor "Sandevistan.Editor.SandevistanGhostShaderGUI"
    Fallback Off
}
