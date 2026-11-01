Shader "Hidden/EarthOL/BodyGlassBlur"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _BlurRadius ("Blur spread", Float) = 1.3
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex;
        float4 _MainTex_TexelSize;
        float _BlurRadius;

        fixed4 Blur(float2 uv, float2 stepSize)
        {
            // Five bilinear taps approximate a nine-tap separable Gaussian.
            fixed4 color = tex2D(_MainTex, uv) * 0.2270270270;
            color += tex2D(_MainTex, uv + stepSize * 1.3846153846) * 0.3162162162;
            color += tex2D(_MainTex, uv - stepSize * 1.3846153846) * 0.3162162162;
            color += tex2D(_MainTex, uv + stepSize * 3.2307692308) * 0.0702702703;
            color += tex2D(_MainTex, uv - stepSize * 3.2307692308) * 0.0702702703;
            return color;
        }

        fixed4 Horizontal(v2f_img i) : SV_Target
        {
            return Blur(i.uv, float2(_MainTex_TexelSize.x * _BlurRadius, 0));
        }

        fixed4 Vertical(v2f_img i) : SV_Target
        {
            return Blur(i.uv, float2(0, abs(_MainTex_TexelSize.y) * _BlurRadius));
        }
        ENDCG

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment Horizontal
            #pragma target 3.0
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment Vertical
            #pragma target 3.0
            ENDCG
        }
    }
    FallBack Off
}
