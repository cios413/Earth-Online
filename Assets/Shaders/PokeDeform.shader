// 戳形变 Shader：根据最多 4 个"戳点"在物体表面压出平滑凹陷，同时修正法线。
// 由 PokeDeformer.cs 通过 MaterialPropertyBlock 逐帧写入 _PokePos / _PokeAmp。
// 注意：凹陷是按顶点计算的，所以被戳的网格需要有足够多的顶点（见 SoftMesh.cs）。
Shader "EarthOL/PokeDeform"
{
    Properties
    {
        _Color ("颜色", Color) = (0.98, 0.58, 0.22, 1)
        _Glossiness ("光滑度", Range(0, 1)) = 0.45
        _Metallic ("金属感", Range(0, 1)) = 0.0
        _DentNormalStrength ("凹陷法线强度", Range(0, 3)) = 1.0

        // ---- 下面这些由脚本逐帧驱动，不在面板上显示 ----
        [HideInInspector] _PokePos0 ("Poke Pos 0", Vector) = (0, 0, 0, 0)
        [HideInInspector] _PokeAmp0 ("Poke Amp 0", Float) = 0
        [HideInInspector] _PokePos1 ("Poke Pos 1", Vector) = (0, 0, 0, 0)
        [HideInInspector] _PokeAmp1 ("Poke Amp 1", Float) = 0
        [HideInInspector] _PokePos2 ("Poke Pos 2", Vector) = (0, 0, 0, 0)
        [HideInInspector] _PokeAmp2 ("Poke Amp 2", Float) = 0
        [HideInInspector] _PokePos3 ("Poke Pos 3", Vector) = (0, 0, 0, 0)
        [HideInInspector] _PokeAmp3 ("Poke Amp 3", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 200

        CGPROGRAM
        // vertex:vert 做顶点位移，addshadow 让投影也跟着形变
        #pragma surface surf Standard fullforwardshadows vertex:vert addshadow
        #pragma target 3.0

        fixed4 _Color;
        half _Glossiness;
        half _Metallic;
        float _DentNormalStrength;

        float4 _PokePos0; float _PokeAmp0;
        float4 _PokePos1; float _PokeAmp1;
        float4 _PokePos2; float _PokeAmp2;
        float4 _PokePos3; float _PokeAmp3;

        struct Input
        {
            float3 worldPos;
        };

        // 单个戳点：poke.xyz = 物体空间戳点，poke.w = 物体空间影响半径，amp = 物体空间压入量
        // disp / tilt 分别是位移累加和法线倾斜累加
        void ApplyPoke(float3 p, float3 n, float4 poke, float amp, inout float3 disp, inout float3 tilt)
        {
            float radius = poke.w;
            if (abs(amp) < 1e-6 || radius <= 1e-6) return;

            float3 delta = p - poke.xyz;
            float dist = length(delta);
            if (dist >= radius) return;

            // 平滑衰减（smoothstep），边缘一阶导为 0，不会出现折痕
            float x = 1.0 - dist / radius;
            float f = x * x * (3.0 - 2.0 * x);
            float slope = -6.0 * x * (1.0 - x) / radius;

            // 沿顶点法线反方向压入
            disp -= n * (amp * f);

            // 用衰减场的切向梯度把法线"掰"向凹坑内侧，否则凹陷看起来是平的
            float3 radial = delta / max(dist, 1e-6);
            float3 tangential = radial - n * dot(radial, n);
            tilt += tangential * (amp * slope) * _DentNormalStrength;
        }

        void vert(inout appdata_full v)
        {
            float3 n0 = normalize(v.normal);
            float3 p0 = v.vertex.xyz;
            float3 disp = float3(0, 0, 0);
            float3 tilt = float3(0, 0, 0);

            ApplyPoke(p0, n0, _PokePos0, _PokeAmp0, disp, tilt);
            ApplyPoke(p0, n0, _PokePos1, _PokeAmp1, disp, tilt);
            ApplyPoke(p0, n0, _PokePos2, _PokeAmp2, disp, tilt);
            ApplyPoke(p0, n0, _PokePos3, _PokeAmp3, disp, tilt);

            v.vertex.xyz = p0 + disp;
            v.normal = normalize(n0 + tilt);
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            o.Albedo = _Color.rgb;
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            o.Alpha = _Color.a;
        }
        ENDCG
    }

    FallBack "Diffuse"
}
