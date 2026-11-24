#ifndef SANDEVISTAN_GHOST_SURFACE
#define SANDEVISTAN_GHOST_SURFACE
sampler2D _MaskTex;
sampler2D _SurfaceTex;
sampler2D _MainTex;
float4 _MainTex_ST, _Color;
float _UseSourceTexture, _SourceTextureStrength, _SourceColorRetention, _SourceAlpha, _SourceCutoff;
float _GradientStrength;
float _WeaveStrength, _WeaveDensity, _WeaveWidth;
float4 _SurfaceTex_ST, _SurfaceScroll;
float _SurfaceStrength, _SurfaceColor, _SurfaceAlpha, _SurfaceLumaMask;
float _DetailStrength, _Brightness, _RimPower;
float4 _RimColor;
float _MasterOpacity, _PreviewTime;
float _Fade, _Age, _Opacity, _RimStrength, _Palette;
float _PatternMode, _PatternBrightness, _PatternDensity, _PatternWidth;
float4 _PatternColor;
float4 _FreshColor, _MiddleColor, _OldColor;
float _LocalViewProtection, _LocalHideDistance, _LocalFadeDistance, _IsLocalAvatar;
// Globals set by VRChat per camera; ordinary Unity cameras leave these at zero.
float _VRChatMirrorMode, _VRChatCameraMode;
struct appdata
{
    float4 vertex : POSITION;
    float3 normal : NORMAL;
    float2 uv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};
struct v2f
{
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
    float3 worldPosition : TEXCOORD1;
    float3 worldNormal : TEXCOORD2;
    UNITY_VERTEX_OUTPUT_STEREO
};
v2f vert(appdata v)
{
    v2f o;
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_INITIALIZE_OUTPUT(v2f, o);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    o.position = UnityObjectToClipPos(v.vertex);
    o.uv = v.uv;
    o.worldPosition = mul(unity_ObjectToWorld, v.vertex).xyz;
    o.worldNormal = UnityObjectToWorldNormal(v.normal);
    return o;
}
float4 surfaceTexture(float3 position, float3 normal, float clock)
{
    // Existing proxies use UV0 for the alpha atlas. World-space triplanar mapping
    // keeps a separate, uniform pattern across all clothes without rebuilding meshes.
    float3 weights = normal * normal;
    weights *= weights;
    weights /= max(dot(weights, float3(1,1,1)), 0.0001);
    float2 offset = _SurfaceTex_ST.zw + _SurfaceScroll.xy * clock;
    float4 x = tex2D(_SurfaceTex, position.zy * _SurfaceTex_ST.xy + offset);
    float4 y = tex2D(_SurfaceTex, position.xz * _SurfaceTex_ST.xy + offset);
    float4 z = tex2D(_SurfaceTex, position.xy * _SurfaceTex_ST.xy + offset);
    return x * weights.x + y * weights.y + z * weights.z;
}
float linePattern(float3 position, float3 normal)
{
    // One dominant plane keeps the lines crisp and avoids blended double patterns.
    float3 n = abs(normal);
    float2 uv = n.x > n.y && n.x > n.z ? position.zy : (n.y > n.z ? position.xz : position.xy);
    uv *= max(0.001,_PatternDensity);
    float distance;
    UNITY_BRANCH if (_PatternMode < 1.5)
    {
        // Nearest centre on a triangular lattice; the Voronoi borders are hexagons.
        const float2 repeat = float2(1,1.7320508);
        float2 a = frac(uv/repeat)*repeat-repeat*0.5;
        float2 b = frac((uv-repeat*0.5)/repeat)*repeat-repeat*0.5;
        float2 cell = abs(dot(a,a) < dot(b,b) ? a : b);
        distance = abs(0.5-max(cell.x,dot(cell,float2(0.5,0.8660254))));
    }
    else
    {
        float2 cell = abs(frac(uv+0.5)-0.5);
        UNITY_BRANCH if (_PatternMode < 2.5)
            distance = min(max(cell.x,cell.y-0.24),max(cell.y,cell.x-0.24));
        else
            distance = min(cell.x,cell.y);
    }
    float halfWidth = max(0.001,_PatternWidth)*0.5;
    float aa = max(fwidth(distance)*0.5,0.0001);
    return 1-smoothstep(halfWidth-aa,halfWidth+aa,distance);
}
float fineWeave(float3 position, float3 normal)
{
    float3 n = abs(normal);
    float2 uv = n.x > n.y && n.x > n.z ? position.zy : (n.y > n.z ? position.xz : position.xy);
    uv *= max(1,_WeaveDensity);
    float2 cell = abs(frac(uv+0.5)-0.5);
    float2 footprint = max(fwidth(uv),0.0001);
    float halfWidth = saturate(_WeaveWidth)*0.5;
    float2 lines = 1-smoothstep(halfWidth-footprint*0.5,halfWidth+footprint*0.5,cell);
    // Fade unresolved cells to their average area to avoid distant moire and shimmer.
    lines = lerp(lines,saturate(_WeaveWidth).xx,saturate(footprint-0.5));
    return 1-(1-lines.x)*(1-lines.y);
}
float ownerViewCoverage(float3 worldPosition)
{
    UNITY_BRANCH if (_IsLocalAvatar < 0.5 || _LocalViewProtection < 0.5 || _VRChatMirrorMode > 0.5 || _VRChatCameraMode > 0.5)
        return 1;
    // Use one centre for both eyes so the fade sphere does not shift between eyes.
    #ifdef USING_STEREO_MATRICES
        float3 cameraPosition = (unity_StereoWorldSpaceCameraPos[0] + unity_StereoWorldSpaceCameraPos[1]) * 0.5;
    #else
        float3 cameraPosition = _WorldSpaceCameraPos;
    #endif
    float nearDistance = max(0,_LocalHideDistance);
    float farDistance = max(nearDistance + 0.01,_LocalFadeDistance);
    return smoothstep(nearDistance,farDistance,distance(worldPosition,cameraPosition));
}
float clipGhost(v2f i, out float4 source)
{
    clip(_Fade - 0.001);
    float viewCoverage = ownerViewCoverage(i.worldPosition);
    clip(viewCoverage - 0.001);
    source = float4(1,1,1,1);
    UNITY_BRANCH if (_UseSourceTexture > 0.5)
    {
        #ifndef SANDEVISTAN_COLOR_PASS
        UNITY_BRANCH if (_SourceAlpha > 0.5)
        #endif
            source = tex2D(_MainTex,i.uv * _MainTex_ST.xy + _MainTex_ST.zw) * _Color;
        UNITY_BRANCH if (_SourceAlpha > 0.5) clip(source.a - max(0.001,_SourceCutoff));
        else source.a = 1;
    }
    else
    {
        float mask = tex2D(_MaskTex, i.uv).a;
        clip(mask - 0.3);
    }
    return viewCoverage;
}
float ghostCoverage(float4 pattern, float viewCoverage)
{
    // The hidden legacy multiplier preserves previously saved transparency.
    float coverage = saturate(_Opacity * _Fade) * _MasterOpacity * viewCoverage;
    coverage *= lerp(1, pattern.a, _SurfaceAlpha * _SurfaceStrength);
    float luminance = dot(pattern.rgb, float3(0.299,0.587,0.114));
    return coverage * lerp(1, luminance, _SurfaceLumaMask * _SurfaceStrength);
}
float4 ghostSurface(v2f i)
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
    float4 source;
    float viewCoverage = clipGhost(i,source);
    float age = saturate(_Age);
    float3 fresh = _FreshColor.rgb, middle = _MiddleColor.rgb, old = _OldColor.rgb;
    if (_Palette > 0.5 && _Palette < 1.5) {
        fresh=float3(0.18,1,0.67); middle=float3(0.08,0.42,1); old=float3(0.55,0.08,1);
    } else if (_Palette > 1.5 && _Palette < 2.5) {
        fresh=float3(1,0.82,0.12); middle=float3(1,0.25,0.30); old=float3(0.68,0.05,0.75);
    } else if (_Palette > 2.5 && _Palette < 3.5) {
        fresh=float3(0.90,1,1); middle=float3(0.08,0.72,1); old=float3(0.10,0.18,0.92);
    } else if (_Palette > 3.5 && _Palette < 4.5) {
        fresh=float3(1,0.90,0.12); middle=float3(1,0.32,0.02); old=float3(0.85,0.02,0.06);
    } else if (_Palette > 4.5 && _Palette < 5.5) {
        fresh=float3(0.02,0.92,1); middle=float3(0.62,0.08,1); old=float3(1,0.08,0.55);
    } else if (_Palette > 5.5) {
        fresh=float3(0.90,1,0.04); middle=float3(0.15,1,0.12); old=float3(0.02,0.52,0.55);
    }
    float3 color = age < 0.5 ? lerp(fresh,middle,age*2) : lerp(middle,old,(age-0.5)*2);
    float3 gradient = color;
    UNITY_BRANCH if (_UseSourceTexture > 0.5)
    {
        float luminance = dot(source.rgb,float3(0.299,0.587,0.114));
        float textureStrength = saturate(_SourceTextureStrength);
        float3 original = lerp(float3(1,1,1),source.rgb,textureStrength);
        float3 tinted = color * lerp(float3(1,1,1),lerp(luminance.xxx,source.rgb,saturate(_SourceColorRetention)),textureStrength);
        float gradientStrength = saturate(_GradientStrength);
        color = lerp(original,tinted,gradientStrength);
        gradient = lerp(float3(1,1,1),gradient,gradientStrength);
    }
    float3 viewDirection = normalize(UnityWorldSpaceViewDir(i.worldPosition));
    float3 normal = normalize(i.worldNormal);
    float rim = pow(1-saturate(abs(dot(normal,viewDirection))),max(0.01,_RimPower));
    float illumination = 1;
#if defined(_GHOST_DETAIL)
    // View-relative illumination keeps folds and facial features readable in unlit worlds.
    // Alpha is independent of this shading, so detail does not bring back layer stacking.
    float3 facing = normal * (dot(normal,viewDirection) < 0 ? -1 : 1);
    float3 lightDirection = normalize(viewDirection + float3(-0.45,0.7,0.15));
    float diffuse = saturate(dot(facing,lightDirection));
    illumination = lerp(1,0.28 + 0.72*diffuse,_DetailStrength);
#endif
    float clock = _PreviewTime >= 0 ? _PreviewTime : _Time.y;
    float4 pattern = float4(1,1,1,1);
    UNITY_BRANCH if (_SurfaceStrength > 0.001)
    {
        pattern = surfaceTexture(i.worldPosition, normal, clock);
        float luminance = dot(pattern.rgb, float3(0.299,0.587,0.114));
        color *= lerp(float3(1,1,1), lerp(luminance.xxx, pattern.rgb, _SurfaceColor), _SurfaceStrength);
    }
    // Keep HDR radiance above 1; emission shares the same coverage and depth mask.
    float3 emission = lerp(color,gradient,step(0.5,_UseSourceTexture)) * _RimColor.rgb * rim * max(0,_RimStrength);
    UNITY_BRANCH if (_WeaveStrength > 0.001)
        color *= 1-saturate(_WeaveStrength)*fineWeave(i.worldPosition,normal);
    UNITY_BRANCH if (_PatternMode > 0.5 && _PatternBrightness > 0.001)
        emission += _PatternColor.rgb * max(0,_PatternBrightness) * linePattern(i.worldPosition,normal);
    return float4((color * illumination + emission) * max(0,_Brightness), ghostCoverage(pattern,viewCoverage) * source.a);
}

float4 frag(v2f i) : SV_Target { float4 color=ghostSurface(i); clip(color.a-0.001); return color; }
float4 depthFrag(v2f i) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
    float4 source;
    float viewCoverage = clipGhost(i,source);
    float4 pattern = float4(1,1,1,1);
    UNITY_BRANCH if (_SurfaceStrength > 0.001 && (_SurfaceAlpha > 0 || _SurfaceLumaMask > 0))
    {
        float clock = _PreviewTime >= 0 ? _PreviewTime : _Time.y;
        pattern = surfaceTexture(i.worldPosition, normalize(i.worldNormal), clock);
    }
    clip(ghostCoverage(pattern,viewCoverage) * source.a - 0.001);
    return 0;
}
#endif
