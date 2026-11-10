Shader "ShizukuCity/UI/Glow"
{
    Properties
    {
        [PerRendererData] _MainTex ("UI Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [HDR] _GlowColor ("Glow Color", Color) = (1,0.05,0.4,1)
        _GlowIntensity ("Glow Intensity", Range(0,12)) = 3
        _GlowBrightness ("Glow Brightness (HDR)", Range(0,8)) = 3
        _GlowRadius ("Glow Radius", Range(0,128)) = 12
        _GlowFalloff ("Glow Falloff", Range(0.25,4)) = 1.2
        [Toggle] _GlowHDR ("Allow HDR Glow (off = consistent SDR UI)", Float) = 0
        _SourceBrightness ("Source Brightness", Range(0,4)) = 0
        [HideInInspector] _GlowPolygonCount ("Analytic Polygon Count", Integer) = 0

        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
        [HideInInspector] [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }
        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "UI Glow"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 glow : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f
            {
                float4 vertex : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float2 glow : TEXCOORD1;
                float4 mask : TEXCOORD2;
                float2 localPosition : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _TextureSampleAdd;
            float4 _Color;
            float4 _GlowColor;
            float _GlowIntensity;
            float _GlowBrightness;
            float _GlowRadius;
            float _GlowFalloff;
            float _GlowHDR;
            int _GlowPolygonCount;
            float4 _GlowPolygonPoints[64];
            float _SourceBrightness;
            float4 _ClipRect;
            float _UIMaskSoftnessX;
            float _UIMaskSoftnessY;
            float _StencilOp;
            float _StencilWriteMask;
            int _UIVertexColorAlwaysGammaSpace;

            v2f vert(appdata input)
            {
                v2f output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.glow = input.glow.xy;
                output.localPosition = _GlowPolygonCount >= 3 ? input.glow.xy : input.vertex.xy;
                if (_UIVertexColorAlwaysGammaSpace && !IsGammaSpace())
                    input.color.rgb = UIGammaToLinear(input.color.rgb);
                output.color = input.color * _Color;
                float2 pixelSize = output.vertex.w;
                pixelSize /= max(abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy)), float2(0.0001, 0.0001));
                float4 clipRect = clamp(_ClipRect, -2e10, 2e10);
                output.mask = float4(input.vertex.xy * 2 - clipRect.xy - clipRect.zw,
                    0.25 / (0.25 * float2(_UIMaskSoftnessX, _UIMaskSoftnessY) + abs(pixelSize)));
                return output;
            }

            float PolygonDistance(float2 samplePosition)
            {
                float minimumSquared = 1e20;
                float inside = 0;
                int vertexCount = min(_GlowPolygonCount, 64);
                [loop] for (int i = 0; i < vertexCount; i++)
                {
                    float2 a = _GlowPolygonPoints[i].xy;
                    float2 b = _GlowPolygonPoints[i == 0 ? vertexCount - 1 : i - 1].xy;
                    float2 edge = b - a;
                    float2 offset = samplePosition - a;
                    float2 nearest = offset - edge * saturate(dot(offset, edge) / max(dot(edge, edge), 1e-12));
                    minimumSquared = min(minimumSquared, dot(nearest, nearest));
                    if ((a.y > samplePosition.y) != (b.y > samplePosition.y))
                    {
                        float crossing = a.x + (samplePosition.y - a.y) * (b.x - a.x) / (b.y - a.y);
                        if (samplePosition.x < crossing) inside = 1 - inside;
                    }
                }
                return sqrt(max(0, minimumSquared)) * (inside > 0.5 ? -1 : 1);
            }

            float4 frag(v2f input) : SV_Target
            {
                float4 sampled = tex2D(_MainTex, input.uv) + _TextureSampleAdd;
                float4 color = sampled * input.color;
                if (_GlowPolygonCount >= 3)
                {
                    float distance = PolygonDistance(input.localPosition);
                    float aa = max(fwidth(distance) * 0.5, 0.0001);
                    float coverage = 1 - smoothstep(-aa, aa, distance);
                    float radius = max(_GlowRadius, 0.0001);
                    float normalizedDistance = max(distance, 0) / radius;
                    float tail = 1 - smoothstep(0.6, 1, normalizedDistance);
                    float haze = exp(-5 * max(_GlowFalloff, 0.25) * normalizedDistance * normalizedDistance) * tail;
                    float coreWidth = max(radius * 0.08, aa);
                    float core = exp(-pow(max(distance, 0) / coreWidth, 2));
                    float glowCoverage = 1 - exp(-max(_GlowIntensity, 0) * (0.7 * haze + 1.5 * core));
                    if (_GlowRadius <= 0) glowCoverage = 0;
                    float sourceAlpha = color.a * coverage;
                    float haloAlpha = color.a * _GlowColor.a * glowCoverage;
                    float3 sourceRGB = color.rgb * (1 + _SourceBrightness);
                    float3 glowRGB = _GlowColor.rgb * max(0, _GlowBrightness);
                    if (_GlowHDR < 0.5) { sourceRGB = saturate(sourceRGB); glowRGB = saturate(glowRGB); }
                    float visibleHaloAlpha = haloAlpha * (1 - coverage);
                    color.a = sourceAlpha + visibleHaloAlpha;
                    color.rgb = (sourceRGB * sourceAlpha + glowRGB * visibleHaloAlpha) / max(color.a, 1e-6);
                }
                else if (input.glow.x > 0.5)
                {
                    // A Mask's own stencil silhouette must not grow to include its glow copies.
                    if (_StencilOp > 0.5 && _StencilWriteMask > 0.5) clip(-1);
                    float density = max(0, input.glow.y) * max(0, _GlowIntensity) * 4;
                    if (input.glow.x > 1.5) density *= 1.5;
                    color.rgb = _GlowColor.rgb * max(0, _GlowBrightness);
                    color.a = saturate(color.a * _GlowColor.a * (1 - exp(-density)));
                }
                else color.rgb *= 1 + _SourceBrightness;
                if (_GlowHDR < 0.5) color.rgb = saturate(color.rgb);
                #ifdef UNITY_UI_CLIP_RECT
                    float2 mask = saturate((_ClipRect.zw - _ClipRect.xy - abs(input.mask.xy)) * input.mask.zw);
                    color.a *= mask.x * mask.y;
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                    clip(color.a - 0.001);
                #endif
                // Premultiplied output keeps graphic/CanvasGroup alpha at zero completely invisible.
                color.rgb *= color.a;
                return color;
            }
            ENDCG
        }
    }
    CustomEditor "UIGlowShaderGUI"
    Fallback Off
}
