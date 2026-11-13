Shader "EasyBuilding/Cell Colors"
{
    Properties { _Color ("Tint", Color) = (1,1,1,1) }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; fixed4 color : COLOR; };
            struct v2f { float4 position : SV_POSITION; fixed4 color : COLOR; };
            fixed4 _Color;
            v2f vert(appdata v)
            {
                v2f o; o.position = UnityObjectToClipPos(v.vertex);
                float shade = 0.65 + 0.35 * saturate(dot(UnityObjectToWorldNormal(v.normal), normalize(float3(0.5,1,0.3))));
                o.color = v.color * _Color; o.color.rgb *= shade; return o;
            }
            fixed4 frag(v2f i) : SV_Target { return i.color; }
            ENDCG
        }
    }
}
