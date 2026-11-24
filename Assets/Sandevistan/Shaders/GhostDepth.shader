Shader "Sandevistan/Ghost Depth"
{
    Properties
    {
        [HideInInspector] _MaskTex ("Silhouette alpha mask", 2D) = "white" {}
        [HideInInspector] _UseSourceTexture ("Original main texture enabled", Float) = 0
        [HideInInspector] _MainTex ("Original main texture", 2D) = "white" {}
        [HideInInspector] _Color ("Original main tint", Color) = (1,1,1,1)
        [HideInInspector] _SourceAlpha ("Use original texture alpha", Float) = 0
        [HideInInspector] _SourceCutoff ("Original alpha cutoff", Float) = 0.001
        [HideInInspector] _SurfaceTex ("Surface texture", 2D) = "white" {}
        [HideInInspector] _SurfaceStrength ("Surface texture strength", Range(0,1)) = 0
        [HideInInspector] _SurfaceAlpha ("Use image alpha", Range(0,1)) = 1
        [HideInInspector] _SurfaceLumaMask ("Use brightness as opacity", Range(0,1)) = 0
        [HideInInspector] _SurfaceScroll ("Texture scroll XY", Vector) = (0,0,0,0)
        [HideInInspector] _MasterOpacity ("Legacy opacity multiplier", Range(0,1)) = 1
        [HideInInspector] _Fade ("Visibility", Range(0,1)) = 0
        [HideInInspector] _Opacity ("Opacity", Range(0,1)) = 0.35
        [HideInInspector] _StencilBit ("Equal-depth anti stacking bit", Float) = 16
        [HideInInspector] _PreviewTime ("Editor preview time", Float) = -1
        [HideInInspector] _LocalViewProtection ("Protect owner's near view", Float) = 1
        [HideInInspector] _LocalHideDistance ("Owner hidden distance", Float) = 0.65
        [HideInInspector] _LocalFadeDistance ("Owner visible distance", Float) = 1
        [HideInInspector] _IsLocalAvatar ("Owner flag managed by FX", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent+90" "RenderType"="Transparent" "VRCFallback"="Hidden" }
        Cull Off
        ZTest LEqual
        ZWrite On
        Stencil { Ref 0 ReadMask [_StencilBit] WriteMask [_StencilBit] Comp Always Pass Zero }
        ColorMask 0
        Blend Off
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment depthFrag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "GhostSurface.cginc"
            ENDCG
        }
    }
    Fallback Off
}
