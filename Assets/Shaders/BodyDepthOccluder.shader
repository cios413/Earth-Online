Shader "EarthOL/BodyDepthOccluder"
{
    SubShader
    {
        // AR camera background is already drawn. Populate depth before opaque
        // projectiles, without adding any color, tint, blur or shadow.
        Tags { "Queue"="Geometry-10" "RenderType"="Opaque" "IgnoreProjector"="True" "ForceNoShadowCasting"="True" }
        Pass
        {
            ColorMask 0
            ZWrite On
            ZTest LEqual
            Cull Back

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #include "UnityCG.cginc"

            float4 vert(float4 vertex : POSITION) : SV_POSITION
            {
                return UnityObjectToClipPos(vertex);
            }

            fixed4 frag() : SV_Target
            {
                return 0;
            }
            ENDCG
        }
    }
    FallBack Off
}
