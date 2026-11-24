Shader "EarthOL/RetroArrow"
{
    Properties
    {
        _MainTex ("Vintage red and teal print", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "Queue"="Geometry+10" "RenderType"="Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_ST, _Color;
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; float3 normal : TEXCOORD1; };
            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv,_MainTex);
                o.normal = mul((float3x3)UNITY_MATRIX_V,UnityObjectToWorldNormal(v.normal));
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                float4 ink = tex2D(_MainTex,i.uv)*_Color;
                // Broad matte fill keeps the fine red shaft readable while retaining cylindrical shading.
                float light = .78+.22*saturate(dot(normalize(i.normal),normalize(float3(-.35,.65,-1))));
                return float4(ink.rgb*light,1);
            }
            ENDCG
        }
    }
}
