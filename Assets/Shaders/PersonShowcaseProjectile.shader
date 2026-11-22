Shader "EarthOL/ShowcaseProjectile"
{
    Properties
    {
        _MainTex ("Photo", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _EdgeWidth ("Edge shadow width in photo pixels", Range(1,8)) = 3
        _EdgeShade ("Edge shadow strength", Range(0,1)) = .35
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite On
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; };
            struct v2f { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; };
            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _EdgeWidth, _EdgeShade;
            fixed4 _Color;
            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                fixed4 color = tex2D(_MainTex, i.uv) * _Color;
                clip(color.a - .01);
                // Preserve the captured RGB everywhere except a narrow inner contour shadow.
                // Alpha remains unchanged, so the silhouette matches the showcase photo.
                float2 stepUV = _MainTex_TexelSize.xy * _EdgeWidth;
                float nearbyAlpha = tex2D(_MainTex, i.uv + float2(stepUV.x, 0)).a;
                nearbyAlpha = min(nearbyAlpha, tex2D(_MainTex, i.uv - float2(stepUV.x, 0)).a);
                nearbyAlpha = min(nearbyAlpha, tex2D(_MainTex, i.uv + float2(0, stepUV.y)).a);
                nearbyAlpha = min(nearbyAlpha, tex2D(_MainTex, i.uv - float2(0, stepUV.y)).a);
                nearbyAlpha = min(nearbyAlpha, tex2D(_MainTex, i.uv + stepUV * .7071).a);
                nearbyAlpha = min(nearbyAlpha, tex2D(_MainTex, i.uv - stepUV * .7071).a);
                nearbyAlpha = min(nearbyAlpha, tex2D(_MainTex, i.uv + stepUV * float2(.7071,-.7071)).a);
                nearbyAlpha = min(nearbyAlpha, tex2D(_MainTex, i.uv - stepUV * float2(.7071,-.7071)).a);
                color.rgb *= 1.0 - _EdgeShade * (1.0 - nearbyAlpha);
                return color;
            }
            ENDCG
        }
    }
    Fallback Off
}
