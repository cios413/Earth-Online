Shader "EarthOL/BodyFrostedGlass"
{
    Properties
    {
        _Tint ("Glass tint (alpha is tint strength)", Color) = (0.88, 0.96, 1.0, 0.08)
        _FrostOpacity ("Frost coverage", Range(0, 1)) = 0.82
        _RimStrength ("Soft edge highlight", Range(0, 0.4)) = 0.10
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _BodyGlassScreen;
            float _BodyGlassReady;
            fixed4 _Tint;
            half _FrostOpacity;
            half _RimStrength;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float4 screen : TEXCOORD0;
                float3 normal : TEXCOORD1;
                float3 view : TEXCOORD2;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.position = UnityObjectToClipPos(v.vertex);
                o.screen = ComputeScreenPos(o.position);
                float3 world = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.view = _WorldSpaceCameraPos.xyz - world;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = saturate(i.screen.xy / max(i.screen.w, 0.00001));
                half rim = pow(1.0 - saturate(dot(normalize(i.normal), normalize(i.view))), 3.0);
                fixed3 background = tex2D(_BodyGlassScreen, uv).rgb;
                fixed3 glass = lerp(background, _Tint.rgb, _Tint.a);
                glass += rim * _RimStrength;
                // Before a camera frame is available, use a faint translucent tint.
                half ready = saturate(_BodyGlassReady);
                fixed3 color = lerp(_Tint.rgb, glass, ready);
                half opacity = lerp(_Tint.a, _FrostOpacity, ready);
                return fixed4(color, saturate(opacity + rim * _RimStrength));
            }
            ENDCG
        }
    }
    FallBack Off
}
