Shader "EarthOL/ShowcasePhoto"
{
    Properties
    {
        _MainTex ("Photo", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite On Cull Back
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex:POSITION; float3 normal:NORMAL; float2 uv:TEXCOORD0; };
            struct v2f { float4 vertex:SV_POSITION; float3 viewNormal:TEXCOORD0; float2 uv:TEXCOORD1; };
            sampler2D _MainTex;
            fixed4 _Color;
            v2f vert(appdata v)
            {
                v2f o; o.vertex=UnityObjectToClipPos(v.vertex);o.uv=v.uv;
                o.viewNormal=mul((float3x3)UNITY_MATRIX_IT_MV,v.normal);
                return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                fixed4 photo=tex2D(_MainTex,i.uv)*_Color;
                clip(photo.a-.01);
                float3 n=normalize(i.viewNormal);
                // Fixed in preview-camera space, independent of the AR pose and all scene lights.
                float3 light=normalize(float3(-.85,.25,.45));
                float diffuse=saturate(dot(n,light));
                float highlight=pow(saturate(dot(n,normalize(light+float3(0,0,1)))),28);
                photo.rgb=photo.rgb*(.64+.48*diffuse)+highlight*.08;
                return photo;
            }
            ENDCG
        }
    }
    Fallback Off
}
