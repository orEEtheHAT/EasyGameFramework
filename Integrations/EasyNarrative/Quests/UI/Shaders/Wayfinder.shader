Shader "EasyGameFramework/UI/Wayfinder"
{
    Properties
    {
        [PerRendererData] _MainTex ("UI texture", 2D) = "white" {}
        _Halo ("Halo opacity", Range(0,1)) = .32
        _PulseRate ("Pulse cycles per second", Range(0,2)) = .42
        _StencilComp ("Stencil comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil operation", Float) = 0
        _StencilWriteMask ("Stencil write mask", Float) = 255
        _StencilReadMask ("Stencil read mask", Float) = 255
        _ColorMask ("Color mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Alpha clipping", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        ZWrite Off Cull Off ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #pragma editor_sync_compilation
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            struct Input { float4 vertex:POSITION; half4 color:COLOR; float4 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Pixel { float4 vertex:SV_POSITION; half4 color:COLOR; float4 uv:TEXCOORD0; float2 local:TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            float4 _ClipRect;
            float _UIMaskSoftnessX, _UIMaskSoftnessY;
            half _Halo, _PulseRate;
            int _UIVertexColorAlwaysGammaSpace;
            Pixel Vert(Input v)
            {
                Pixel o;
                UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex=UnityObjectToClipPos(v.vertex);o.local=v.vertex.xy;o.color=v.color;o.uv=v.uv;
                #ifndef UNITY_COLORSPACE_GAMMA
                if(_UIVertexColorAlwaysGammaSpace)o.color.rgb=UIGammaToLinear(o.color.rgb);
                #endif
                return o;
            }
            float Segment(float2 p,float2 a,float2 b)
            {
                float2 ab=b-a;
                return length(p-a-ab*saturate(dot(p-a,ab)/dot(ab,ab)));
            }
            half4 Frag(Pixel i):SV_Target
            {
                // UV.z selects the silhouette. UV.w selects moving, static, or high contrast.
                float motion=i.uv.w>.5 ? 1 : 0;
                float contrast=i.uv.w<-.5 ? 1 : 0;
                float phase=frac(_Time.y*_PulseRate)*motion;
                float d,accent;
                if(i.uv.z>1.5)
                {
                    // A narrow annulus mesh supplies radial coverage and a continuous turn coordinate.
                    d=abs(i.uv.x-.5)-.115;
                    float arc=abs(frac(i.uv.y-phase+.5)-.5);
                    accent=saturate(1-arc*8)*motion;
                }
                else
                {
                    float2 p=i.uv.xy;
                    if(i.uv.z>.5)
                    {
                        d=min(Segment(p,float2(-.48,-.14),float2(0,.45)),Segment(p,float2(0,.45),float2(.48,-.14)))-.095;
                        d=min(d,Segment(p,float2(-.26,-.48),float2(0,-.17))-.055);
                        d=min(d,Segment(p,float2(0,-.17),float2(.26,-.48))-.055);
                    }
                    else
                    {
                        float diamond=abs(p.x)+abs(p.y);
                        d=min(abs(diamond-.61)*.7071-.064,(diamond-.205)*.7071);
                    }
                    accent=motion*(.5-.5*cos(phase*6.283185));
                }
                float aa=max(fwidth(d)*.75,.002);
                float face=1-smoothstep(-aa,aa,d);
                float rim=1-smoothstep(.075-aa,.075+aa,d);
                float halo=(1-smoothstep(0,.28,max(d,0)))*_Halo*(1-contrast);
                half3 pigment=lerp(i.color.rgb,half3(1,1,1),contrast);
                float4 result=float4(pigment*halo,halo);
                float outline=rim*.96;
                result=float4(float3(.012,.025,.032)*outline,outline)+result*(1-outline);
                half3 fill=lerp(pigment,half3(1,1,1),accent*.55);
                result=float4(fill*face,face)+result*(1-face);
                result*=i.color.a;
                #ifdef UNITY_UI_CLIP_RECT
                float2 px=max(fwidth(i.local),.0001);
                float2 mask=saturate(min(i.local-_ClipRect.xy,_ClipRect.zw-i.local)/(px*(1+float2(_UIMaskSoftnessX,_UIMaskSoftnessY))));
                result*=mask.x*mask.y;
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(result.a-.001);
                #endif
                return result;
            }
            ENDCG
        }
    }
}
