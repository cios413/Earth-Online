Shader "EarthOL/AlienJelly"
{
    Properties
    {
        _Color ("Jelly body", Color) = (.53,1,.025,.90)
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
            struct appdata { float4 vertex:POSITION; float3 normal:NORMAL; };
            struct v2f { float4 vertex:SV_POSITION; float3 normal:TEXCOORD0; float3 view:TEXCOORD1; float3 local:TEXCOORD2; };
            fixed4 _Color;
            v2f vert(appdata v)
            {
                v2f o; o.vertex=UnityObjectToClipPos(v.vertex);
                o.normal=UnityObjectToWorldNormal(v.normal);
                o.view=UnityWorldSpaceViewDir(mul(unity_ObjectToWorld,v.vertex).xyz);
                o.local=v.vertex.xyz; return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                float3 n=normalize(i.normal),v=normalize(i.view);
                float rim=pow(1-saturate(dot(n,v)),3);
                float3 light=normalize(float3(-.45,.75,-.65));
                float glow=saturate(dot(n,light))*.30+.70;
                float shine=pow(saturate(dot(reflect(-light,n),v)),36);
                float yellow=.3+.22*sin(i.local.y*48+i.local.x*32);
                float3 color=lerp(_Color.rgb,float3(.91,1,.055),yellow)*glow;
                color=lerp(color,float3(.76,1,.38),rim*.65);
                color=lerp(color,1,shine*.92);
                return fixed4(color,saturate(_Color.a+rim*.08));
            }
            ENDCG
        }
    }
    Fallback "Standard"
}
