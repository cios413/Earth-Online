Shader "EarthOL/PersonWarp"
{
    Properties
    {
        [HideInInspector] _PokePos0 ("Poke 0", Vector) = (0,0,0,0)
        [HideInInspector] _PokeAmp0 ("Stretch 0", Float) = 0
        [HideInInspector] _PokeBulge0 ("Bulge 0", Float) = 0
        [HideInInspector] _PokePos1 ("Poke 1", Vector) = (0,0,0,0)
        [HideInInspector] _PokeAmp1 ("Stretch 1", Float) = 0
        [HideInInspector] _PokeBulge1 ("Bulge 1", Float) = 0
        [HideInInspector] _PokePos2 ("Poke 2", Vector) = (0,0,0,0)
        [HideInInspector] _PokeAmp2 ("Stretch 2", Float) = 0
        [HideInInspector] _PokeBulge2 ("Bulge 2", Float) = 0
        [HideInInspector] _PokePos3 ("Poke 3", Vector) = (0,0,0,0)
        [HideInInspector] _PokeAmp3 ("Stretch 3", Float) = 0
        [HideInInspector] _PokeBulge3 ("Bulge 3", Float) = 0
        [HideInInspector] _SwipeDir ("Swipe Dir", Vector) = (1, 0, 0, 0)
        [HideInInspector] _SwipeAmp ("Swipe Amp", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Overlay-100" "RenderType"="Transparent" "IgnoreProjector"="True" }
        GrabPass { "_PersonWarpGrab" }
        Pass
        {
            Cull Off ZWrite Off ZTest Always
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            sampler2D _PersonWarpGrab;
            float4 _PokePos0, _PokePos1, _PokePos2, _PokePos3;
            float _PokeAmp0, _PokeAmp1, _PokeAmp2, _PokeAmp3;
            float _PokeBulge0, _PokeBulge1, _PokeBulge2, _PokeBulge3;
            float4 _SwipeDir; float _SwipeAmp;
            float4 _BodyParts[10];
            float _BodyRadius[10];
            float _BodyPartCount;
            struct appdata { float4 vertex:POSITION; float3 normal:NORMAL; float2 uv:TEXCOORD0; };
            struct v2f { float4 position:SV_POSITION; float4 originalScreen:TEXCOORD0; };
            void Warp(float3 p, float3 n, float4 poke, float stretch, float bulge, inout float3 displacement)
            {
                if (poke.w <= 0.000001) return;
                float3 delta = p - poke.xyz;
                float distance = length(delta);
                if (distance >= poke.w) return;
                float x = 1.0 - distance / poke.w;
                float influence = x * x * (3.0 - 2.0 * x);
                displacement += (delta - n * dot(delta, n)) * (stretch * influence);
                displacement += n * (bulge * influence);
            }
            float BodyMask(float2 p)
            {
                float mask = 0.0;
                for (int i = 0; i < (int)_BodyPartCount; i++)
                {
                    float2 a = _BodyParts[i].xy;
                    float2 b = _BodyParts[i].zw;
                    float r = _BodyRadius[i];
                    if (r <= 0.0001) continue;
                    float2 ab = b - a;
                    float len2 = dot(ab, ab);
                    float t = len2 < 1e-6 ? 0.0 : saturate(dot(p - a, ab) / len2);
                    float2 closest = a + ab * t;
                    float d = length(p - closest);
                    float w = 1.0 - smoothstep(r, r + 0.05, d);
                    mask = max(mask, w);
                }
                return mask;
            }
            v2f vert(appdata v)
            {
                v2f o;
                float3 displacement = 0;
                float3 normal = normalize(v.normal);
                Warp(v.vertex.xyz, normal, _PokePos0, _PokeAmp0, _PokeBulge0, displacement);
                Warp(v.vertex.xyz, normal, _PokePos1, _PokeAmp1, _PokeBulge1, displacement);
                Warp(v.vertex.xyz, normal, _PokePos2, _PokeAmp2, _PokeBulge2, displacement);
                Warp(v.vertex.xyz, normal, _PokePos3, _PokeAmp3, _PokeBulge3, displacement);
                // 挥扫：整体沿面内方向横向剪切（随边缘 feather 平滑衰减）
                displacement += float3(_SwipeDir.x, _SwipeDir.y, 0.0) * _SwipeAmp;
                // 只在识别出的人形轮廓内形变
                displacement *= BodyMask(v.vertex.xy);
                float edge = min(min(v.uv.x, 1.0-v.uv.x), min(v.uv.y, 1.0-v.uv.y));
                float feather = saturate(edge * 12.0);
                displacement *= feather * feather * (3.0 - 2.0 * feather);
                o.originalScreen = ComputeGrabScreenPos(UnityObjectToClipPos(v.vertex));
                o.position = UnityObjectToClipPos(float4(v.vertex.xyz + displacement, 1));
                return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                return tex2Dproj(_PersonWarpGrab, UNITY_PROJ_COORD(i.originalScreen));
            }
            ENDCG
        }
    }
    FallBack Off
}
