// 识别图卡片形变 + 摄像头实时画面投影
//
// 关键点：抓屏（GrabPass）里已经有 Vuforia 画上去的摄像头画面，
// 用「变形前的屏幕坐标」去采样它 —— 于是卡片被拉弯时，表面显示的仍是
// 物理卡片原本所在位置的画面，看起来就是物理卡片本身在形变。
//
// 因为要 GrabPass，不能用 Surface Shader，这里写成自定义 pass（不参与光照）。
Shader "EarthOL/PokeWarp"
{
    Properties
    {
        _Color ("纯色模式颜色（关掉摄像头投影时用）", Color) = (0.86, 0.84, 0.80, 1)
        [Toggle] _UseCamera ("投影摄像头画面", Float) = 1
        _MainTex ("（保留占位）", 2D) = "white" {}

        [HideInInspector] _PokePos0 ("Poke 0", Vector) = (0, 0, 0, 0)
        [HideInInspector] _PokeAmp0 ("Stretch 0", Float) = 0
        [HideInInspector] _PokeBulge0 ("Bulge 0", Float) = 0
        [HideInInspector] _PokePos1 ("Poke 1", Vector) = (0, 0, 0, 0)
        [HideInInspector] _PokeAmp1 ("Stretch 1", Float) = 0
        [HideInInspector] _PokeBulge1 ("Bulge 1", Float) = 0
        [HideInInspector] _PokePos2 ("Poke 2", Vector) = (0, 0, 0, 0)
        [HideInInspector] _PokeAmp2 ("Stretch 2", Float) = 0
        [HideInInspector] _PokeBulge2 ("Bulge 2", Float) = 0
        [HideInInspector] _PokePos3 ("Poke 3", Vector) = (0, 0, 0, 0)
        [HideInInspector] _PokeAmp3 ("Stretch 3", Float) = 0
        [HideInInspector] _PokeBulge3 ("Bulge 3", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }

        // 抓一帧当前画面（含 Vuforia 的摄像头背景）。命名后整帧只抓一次，多张卡片共用。
        GrabPass { "_PokeWarpGrab" }

        Pass
        {
            Cull Off
            ZWrite On

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _UseCamera;

            float4 _PokePos0; float _PokeAmp0; float _PokeBulge0;
            float4 _PokePos1; float _PokeAmp1; float _PokeBulge1;
            float4 _PokePos2; float _PokeAmp2; float _PokeBulge2;
            float4 _PokePos3; float _PokeAmp3; float _PokeBulge3;

            sampler2D _PokeWarpGrab;
            float4 _PokeWarpGrab_TexelSize;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 restScreen : TEXCOORD0;
                float2 uv : TEXCOORD1;
            };

            void Warp(float3 p, float3 n, float4 poke, float amp, float bulge, inout float3 disp)
            {
                float radius = poke.w;
                if (radius <= 1e-6) return;

                float3 delta = p - poke.xyz;
                float dist = length(delta);
                if (dist >= radius) return;

                float x = 1.0 - dist / radius;
                float f = x * x * (3.0 - 2.0 * x);

                // 面内：把顶点沿切向推开/收拢 —— 画面被局部拉伸
                disp += (delta - n * dot(delta, n)) * (amp * f);

                // 出平面：鼓起
                disp += n * (bulge * f);
            }

            v2f vert(appdata v)
            {
                v2f o;

                float3 n = normalize(v.normal);
                float3 p0 = v.vertex.xyz;
                float3 disp = float3(0, 0, 0);

                Warp(p0, n, _PokePos0, _PokeAmp0, _PokeBulge0, disp);
                Warp(p0, n, _PokePos1, _PokeAmp1, _PokeBulge1, disp);
                Warp(p0, n, _PokePos2, _PokeAmp2, _PokeBulge2, disp);
                Warp(p0, n, _PokePos3, _PokeAmp3, _PokeBulge3, disp);

                // 变形前的位置 -> 屏幕坐标：用它去"取回"物理卡片原来那块的摄像头画面
                o.restScreen = ComputeScreenPos(UnityObjectToClipPos(float4(p0, 1.0)));

                // 实际渲染位置 = 变形后
                o.pos = UnityObjectToClipPos(float4(p0 + disp, 1.0));
                o.uv = v.uv;

                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 screenUv = i.restScreen.xy / max(i.restScreen.w, 1e-5);

                // D3D 系列抓屏纹理是上下翻转的
                #if UNITY_UV_STARTS_AT_TOP
                if (_PokeWarpGrab_TexelSize.y < 0)
                    screenUv.y = 1.0 - screenUv.y;
                #endif

                float3 cameraColor = tex2D(_PokeWarpGrab, screenUv).rgb;
                float3 color = lerp(_Color.rgb, cameraColor, saturate(_UseCamera));

                return fixed4(color, 1.0);
            }
            ENDCG
        }
    }

    FallBack Off
}
