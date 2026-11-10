Shader "ShizukuCity/Sprite Alpha Outline"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _OutlineColor ("Outline Color", Color) = (1, 1, 1, 1)
        _OutlineWidth ("Outline Width (Pixels)", Float) = 1
        [HideInInspector] _OutlineGeometryScale ("Geometry Scale", Vector) = (1, 1, 0, 0)
        [HideInInspector] _SpriteUvRect ("Sprite UV Rect", Vector) = (0, 0, 1, 1)
        [HideInInspector] _SpritePivot ("Sprite Pivot", Vector) = (0.5, 0.5, 0, 0)
        [MaterialToggle] PixelSnap ("Pixel Snap", Float) = 0
        [HideInInspector] _RendererColor ("Renderer Color", Color) = (1, 1, 1, 1)
        [HideInInspector] _Flip ("Flip", Vector) = (1, 1, 1, 1)
        [PerRendererData] _AlphaTex ("External Alpha", 2D) = "white" {}
        [PerRendererData] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex SpriteVert
            #pragma fragment Frag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ PIXELSNAP_ON
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA

            #include "UnitySprites.cginc"

            fixed4 _OutlineColor;
            float _OutlineWidth;
            float4 _MainTex_TexelSize;
            float2 _OutlineGeometryScale;
            float4 _SpriteUvRect;
            float2 _SpritePivot;

            fixed SampleAlpha(float2 uv)
            {
                float2 uvSize = max(_SpriteUvRect.zw - _SpriteUvRect.xy, _MainTex_TexelSize.xy);
                float2 localUv = (uv - _SpriteUvRect.xy) / uvSize;
                fixed inside = step(0.0, localUv.x)
                    * step(localUv.x, 1.0)
                    * step(0.0, localUv.y)
                    * step(localUv.y, 1.0);
                float2 halfTexel = _MainTex_TexelSize.xy * 0.5;
                float2 clampedUv = clamp(
                    uv,
                    _SpriteUvRect.xy + halfTexel,
                    _SpriteUvRect.zw - halfTexel
                );
                return SampleSpriteTexture(clampedUv).a * inside;
            }

            fixed4 Frag(v2f IN) : SV_Target
            {
                const int MaximumKernelRadius = 12;
                float outlineWidth = clamp(_OutlineWidth, 0.0, MaximumKernelRadius);
                float2 uvSize = max(_SpriteUvRect.zw - _SpriteUvRect.xy, _MainTex_TexelSize.xy);
                float2 expandedLocalUv = (IN.texcoord - _SpriteUvRect.xy) / uvSize;
                float2 sourceLocalUv = _SpritePivot
                    + (expandedLocalUv - _SpritePivot) * _OutlineGeometryScale;
                float2 sourceUv = _SpriteUvRect.xy + sourceLocalUv * uvSize;

                fixed sourceAlpha = SampleAlpha(sourceUv);
                fixed surroundingAlpha = 0;

                [loop]
                for (int y = -MaximumKernelRadius; y <= MaximumKernelRadius; y++)
                {
                    [loop]
                    for (int x = -MaximumKernelRadius; x <= MaximumKernelRadius; x++)
                    {
                        float distanceFromCenter = sqrt((float)(x * x + y * y));
                        float kernelCoverage = saturate(outlineWidth + 1.0 - distanceFromCenter);
                        if (kernelCoverage <= 0.0)
                        {
                            continue;
                        }

                        float2 sampleOffset = float2(x, y) * _MainTex_TexelSize.xy;
                        fixed sampledAlpha = SampleAlpha(sourceUv + sampleOffset) * kernelCoverage;
                        surroundingAlpha = max(surroundingAlpha, sampledAlpha);
                    }
                }

                fixed ringAlpha = surroundingAlpha * (1.0 - sourceAlpha) * _OutlineColor.a;
                return fixed4(_OutlineColor.rgb, ringAlpha);
            }
            ENDCG
        }
    }
}
