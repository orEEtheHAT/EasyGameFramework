Shader "EasyBuilding/Sample Surface"
{
    Properties { _Color ("Color", Color) = (0.8, 0.6, 0.3, 1) }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f { float4 vertex : SV_POSITION; float3 normal : TEXCOORD0; };
            fixed4 _Color;
            v2f vert(appdata v) { v2f o; o.vertex = UnityObjectToClipPos(v.vertex); o.normal = UnityObjectToWorldNormal(v.normal); return o; }
            fixed4 frag(v2f i) : SV_Target { float shade = 0.55 + 0.45 * saturate(dot(normalize(i.normal), normalize(float3(-0.4, 0.8, -0.5)))); return fixed4(_Color.rgb * shade, 1); }
            ENDCG
        }
    }
}
