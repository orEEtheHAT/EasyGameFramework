Shader "EasyGameFramework/UI/Letterpress SDF"
{
    Properties
    {
        _RevealEdgeColor ("Reveal highlight", Color) = (1,.84,.58,.58)
        _MainTex ("TMP font atlas", 2D) = "white" {}
        _FaceColor ("Face", Color) = (1,1,1,1)
        _FaceDilate ("Face dilation", Range(-1,1)) = 0
        _OutlineColor ("Outline", Color) = (0.035,0.06,0.09,1)
        _OutlineWidth ("Outline width", Range(0,1)) = .16
        _OutlineSoftness ("Outline softness", Range(0,1)) = 0
        _GlowColor ("Halo", Color) = (0.22,0.83,0.91,0)
        _GlowOuter ("Halo radius", Range(0,1)) = 0
        [HideInInspector] _GlowOffset ("Halo offset", Float) = 0
        [HideInInspector] _GlowInner ("Halo inner", Float) = 0
        _SheenColor ("Highlight", Color) = (1,0.96,0.78,1)
        _SheenStrength ("Highlight strength", Range(0,1)) = 0
        _SheenSpeed ("Sweeps per second", Range(0,1)) = .16
        _WeightNormal ("Normal weight", Float) = 0
        _WeightBold ("Bold weight", Float) = .75
        _GradientScale ("Atlas padding scale", Float) = 6
        _TextureWidth ("Atlas width", Float) = 1024
        _TextureHeight ("Atlas height", Float) = 1024
        [HideInInspector] _ScaleRatioA ("SDF ratio A", Float) = 1
        [HideInInspector] _ScaleRatioB ("SDF ratio B", Float) = 1
        [HideInInspector] _ScaleRatioC ("SDF ratio C", Float) = 1
        [HideInInspector] _ClipRect ("Mask rectangle", Vector) = (-32767,-32767,32767,32767)
        [HideInInspector] _MaskSoftnessX ("Mask softness X", Float) = 0
        [HideInInspector] _MaskSoftnessY ("Mask softness Y", Float) = 0
        _StencilComp ("Stencil comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil operation", Float) = 0
        _StencilWriteMask ("Stencil write mask", Float) = 255
        _StencilReadMask ("Stencil read mask", Float) = 255
        _ColorMask ("Color mask", Float) = 15
        [Enum(UnityEngine.Rendering.CullMode)] _CullMode ("Cull mode", Float) = 0
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Alpha clipping", Float) = 0
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Stencil
        {
            Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp]
            ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask]
        }
        Cull [_CullMode]
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma editor_sync_compilation
            #pragma multi_compile_local _ GLOW_ON
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            sampler2D _MainTex;
            float4 _ClipRect;
            half4 _FaceColor, _OutlineColor, _GlowColor, _SheenColor;
            half _OutlineWidth, _OutlineSoftness, _GlowOuter, _FaceDilate, _ScaleRatioA, _ScaleRatioB;
            half _SheenStrength, _SheenSpeed, _WeightNormal, _WeightBold;
            float _UIMaskSoftnessX, _UIMaskSoftnessY, _MaskSoftnessX, _MaskSoftnessY;
            half4 _RevealEdgeColor;
            float _EasyUIRevealClock;
            int _UIVertexColorAlwaysGammaSpace;

            struct Attributes { float4 position : POSITION; fixed4 color : COLOR; float4 uv : TEXCOORD0; float2 revealData : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 position : SV_POSITION;
                half4 color : COLOR;
                float4 atlasAndLocal : TEXCOORD0;
                float4 screen : TEXCOORD1;
                half threshold : TEXCOORD2;
                float reveal : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.position = UnityObjectToClipPos(v.position);
                o.screen = ComputeScreenPos(o.position);
                o.reveal = 1;
                if (v.revealData.x <= -4096 && v.revealData.y > 0)
                {
                    float scheduled = -4096 - v.revealData.x;
                    float elapsed = frac((_EasyUIRevealClock - scheduled + 128) / 256) * 256 - 128;
                    o.reveal = elapsed * v.revealData.y;
                }
                o.atlasAndLocal = float4(v.uv.xy,v.position.xy);
                o.color = v.color;
                #ifndef UNITY_COLORSPACE_GAMMA
                    if (_UIVertexColorAlwaysGammaSpace) o.color.rgb = UIGammaToLinear(o.color.rgb);
                #endif
                half weight = v.uv.w < 0 ? _WeightBold : _WeightNormal;
                o.threshold = .5 - (.25 * weight + _FaceDilate) * _ScaleRatioA * .5;
                return o;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                // One SDF atlas sample. Derivatives keep the edge readable at different Canvas scales.
                float d = tex2D(_MainTex,i.atlasAndLocal.xy).a - i.threshold;
                float aa = max(fwidth(d), .001);
                float stroke = .5 * _OutlineWidth * _ScaleRatioA;
                float face = saturate(.5 + d / aa);
                float border = saturate(.5 + (d + stroke) / (aa + _OutlineSoftness * .25));
                float halo = saturate(1 + (d + stroke) / max(.001,.5 * _GlowOuter * _ScaleRatioB));
                half3 ink = i.color.rgb * _FaceColor.rgb;
                if (_SheenStrength > 0)
                {
                    float2 screen = i.screen.xy / i.screen.w;
                    float sweep = abs(frac(screen.x * .64 + screen.y * .13 - _Time.y * _SheenSpeed) - .5);
                    ink = lerp(ink, _SheenColor.rgb, (1 - smoothstep(.015,.105,sweep)) * _SheenStrength);
                }
                half reveal = saturate(i.reveal);
                half leadingEdge = 4 * reveal * (1 - reveal);
                ink = lerp(ink,_RevealEdgeColor.rgb,leadingEdge * _RevealEdgeColor.a);
                half a = 0;
                #if defined(GLOW_ON)
                    a = _GlowColor.a * halo * halo;
                #endif
                half4 result = half4(_GlowColor.rgb * a, a);
                a = _OutlineColor.a * border;
                result = half4(_OutlineColor.rgb*a,a) + result*(1-a);
                a = _FaceColor.a * face;
                result = half4(ink*a,a) + result*(1-a);
                result *= i.color.a * smoothstep(0,1,reveal);
                #ifdef UNITY_UI_CLIP_RECT
                    float2 pos = i.atlasAndLocal.zw;
                    float2 pixel = max(fwidth(pos),float2(.0001,.0001));
                    float2 soft = max(float2(_UIMaskSoftnessX,_UIMaskSoftnessY),float2(_MaskSoftnessX,_MaskSoftnessY));
                    float2 mask = saturate(min(pos-_ClipRect.xy,_ClipRect.zw-pos) / (pixel*(1+soft)));
                    result *= mask.x*mask.y;
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                    clip(result.a - .001);
                #endif
                return result;
            }
            ENDCG
        }
    }
}
