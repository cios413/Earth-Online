Shader "EarthOL/ShowcasePhoto"
{
    Properties
    {
        _MainTex ("Photo", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [HideInInspector] _PokePos0 ("Poke 0", Vector) = (0,0,0,0)
        [HideInInspector] _PokePos1 ("Poke 1", Vector) = (0,0,0,0)
        [HideInInspector] _PokePos2 ("Poke 2", Vector) = (0,0,0,0)
        [HideInInspector] _PokePos3 ("Poke 3", Vector) = (0,0,0,0)
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
            float4 _PokePos0, _PokePos1, _PokePos2, _PokePos3;
            float _PokeAmp0, _PokeAmp1, _PokeAmp2, _PokeAmp3;
            float _PokeBulge0, _PokeBulge1, _PokeBulge2, _PokeBulge3;
            void WarpModel(float3 p,float4 poke,float stretch,float bulge,inout float3 displacement)
            {
                if(poke.w<=.000001)return;
                float3 delta=p-poke.xyz;
                float distance=length(delta);
                if(distance>=poke.w)return;
                float x=1-distance/poke.w;
                float influence=x*x*(3-2*x);
                // Same plane-warp formula; a common local plane normal keeps both photo surfaces joined.
                displacement+=float3(delta.xy,0)*(stretch*influence);
                displacement.z+=bulge*influence;
            }
            v2f vert(appdata v)
            {
                float3 displacement=0;
                WarpModel(v.vertex.xyz,_PokePos0,_PokeAmp0,_PokeBulge0,displacement);
                WarpModel(v.vertex.xyz,_PokePos1,_PokeAmp1,_PokeBulge1,displacement);
                WarpModel(v.vertex.xyz,_PokePos2,_PokeAmp2,_PokeBulge2,displacement);
                WarpModel(v.vertex.xyz,_PokePos3,_PokeAmp3,_PokeBulge3,displacement);
                v2f o; o.vertex=UnityObjectToClipPos(float4(v.vertex.xyz+displacement,1));o.uv=v.uv;
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
