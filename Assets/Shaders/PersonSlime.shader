Shader "EarthOL/PersonSlime"
{
    Properties { _MainTex ("Unused", 2D) = "white" {} }
    SubShader
    {
        Tags { "Queue"="Transparent-20" "RenderType"="Transparent" }
        Cull Off ZWrite Off ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; };
            struct v2f { float4 vertex:SV_POSITION; float2 positionXY:TEXCOORD0; };
            float4 _SlimeBlobs[24]; // center in camera-local XY, radius, tail travel
            float4 _SlimeColors[24];
            int _SlimeCount;
            float _SlimeBlend;
            float4 _SlimeBounds;
            v2f vert(appdata v)
            {
                v2f o; o.vertex=UnityObjectToClipPos(v.vertex);
                o.positionXY=_SlimeBounds.xy+v.uv*_SlimeBounds.zw;
                return o;
            }
            float smin(float a,float b,float k)
            {
                float h=saturate(.5+.5*(b-a)/k);
                return lerp(b,a,h)-k*h*(1-h);
            }
            float blob(float2 p,float4 b)
            {
                float2 d=p-b.xy;
                float head=(length(d/float2(1.12,.78))-b.z)*.78;
                float t=saturate(-d.y/max(b.w,.0001));
                float2 delta=d+float2(0,t*b.w);
                float tail=length(delta)-lerp(b.z*.45,b.z*.27,t);
                return smin(head,tail,max(b.z*.36,.0001));
            }
            fixed4 frag(v2f i):SV_Target
            {
                float distance=1000;
                float3 color=0;
                float2 normal=0;
                [loop] for(int j=0;j<24;j++)
                {
                    if(j>=_SlimeCount)break;
                    float4 b=_SlimeBlobs[j];
                    float d=blob(i.positionXY,b);
                    float eps=max(b.z*.025,.0001);
                    float2 gradient=float2(blob(i.positionXY+float2(eps,0),b)-blob(i.positionXY-float2(eps,0),b),
                        blob(i.positionXY+float2(0,eps),b)-blob(i.positionXY-float2(0,eps),b));
                    float2 n=normalize(gradient+float2(.000001,.000001));
                    float k=max(_SlimeBlend,.0001);
                    float h=saturate(.5+.5*(d-distance)/k);
                    distance=lerp(d,distance,h)-k*h*(1-h);
                    color=lerp(_SlimeColors[j].rgb,color,h);
                    normal=lerp(n,normal,h);
                }
                float aa=max(fwidth(distance),.00015);
                float coverage=1-smoothstep(-aa,aa,distance);
                clip(coverage-.002);
                float rim=exp(-max(0,-distance)/max(_SlimeBlend*.38,.001));
                normal=normalize(normal+float2(.00001,.00001));
                float shine=pow(saturate(dot(normal,normalize(float2(-.55,.83)))),11)*rim;
                float interior=max(0,-distance);
                float bevel=max(_SlimeBlend*2.5,.006);
                float3 domeNormal=normalize(float3(normal*saturate(1-interior/bevel),
                    sqrt(saturate(interior/bevel))));
                float softHighlight=pow(saturate(dot(domeNormal,normalize(float3(-.4,.55,.72)))),18);
                color=lerp(color*.64,color,saturate(-distance/max(_SlimeBlend,.001)));
                color=lerp(color,float3(1,1,.91),saturate(rim*.32+shine*.64+softHighlight*.52));
                return fixed4(color,coverage*(.80+rim*.12));
            }
            ENDCG
        }
    }
    Fallback Off
}
