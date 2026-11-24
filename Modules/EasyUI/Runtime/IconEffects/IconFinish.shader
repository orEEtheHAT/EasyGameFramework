Shader "EasyGameFramework/UI/Icon Finish"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Pulse ("Pulse depth", Range(0,1)) = 0.12
        _PulseRate ("Pulse cycles per second", Range(0,4)) = 0.7
        _Shine ("Shimmer strength", Range(0,1)) = 0.35
        _ShineColor ("Shimmer tint", Color) = (1,0.93,0.72,1)
        _ShineRate ("Shimmer cycles per second", Range(0,3)) = 0.45
        _Spacing ("Shimmer spacing (texture pixels)", Range(32,512)) = 160
        [HideInInspector] _RendererColor ("Renderer tint", Color) = (1,1,1,1)
        [HideInInspector] _Flip ("Flip", Vector) = (1,1,1,1)
        [PerRendererData] _AlphaTex ("External alpha", 2D) = "white" {}
        [PerRendererData] _EnableExternalAlpha ("External alpha enabled", Float) = 0
        [HideInInspector] _StencilComp ("Stencil comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil write mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil read mask", Float) = 255
        [HideInInspector] _ColorMask ("Color mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Alpha clipping", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "CanUseSpriteAtlas"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        ZWrite Off Cull Off ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex IconVert
            #pragma fragment IconFrag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnitySprites.cginc"
            #include "UnityUI.cginc"
            struct Pixel { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; float2 local:TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            float4 _ClipRect, _MainTex_TexelSize;
            float _UIMaskSoftnessX, _UIMaskSoftnessY;
            half _Pulse, _PulseRate, _Shine, _ShineRate, _Spacing;
            half4 _ShineColor, _TextureSampleAdd;
            Pixel IconVert(appdata_t v)
            {
                Pixel o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float4 pos = UnityFlipSprite(v.vertex, _Flip);
                o.vertex = UnityObjectToClipPos(pos);
                o.local = pos.xy;
                o.uv = v.texcoord;
                o.color = v.color * _Color * _RendererColor;
                return o;
            }
            half4 IconFrag(Pixel i):SV_Target
            {
                half4 pixel = (SampleSpriteTexture(i.uv) + _TextureSampleAdd) * i.color;
                float wave = 0.5 - 0.5 * cos(_Time.y * _PulseRate * 6.2831853);
                pixel.a *= 1.0 - _Pulse * wave;
                float2 texels = i.uv * _MainTex_TexelSize.zw;
                float sweep = abs(frac((texels.x + 0.6 * texels.y) / _Spacing - _Time.y * _ShineRate) - 0.5);
                pixel.rgb = lerp(pixel.rgb, _ShineColor.rgb, (1.0 - smoothstep(0.0, 0.13, sweep)) * _Shine * _ShineColor.a);
                #ifdef UNITY_UI_CLIP_RECT
                float2 softness = max(fwidth(i.local),0.0001) * (1 + float2(_UIMaskSoftnessX,_UIMaskSoftnessY));
                float2 mask = saturate(min(i.local-_ClipRect.xy,_ClipRect.zw-i.local)/softness);
                pixel.a *= mask.x * mask.y;
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(pixel.a-.001);
                #endif
                pixel.rgb *= pixel.a;
                return pixel;
            }
            ENDCG
        }
    }
}
