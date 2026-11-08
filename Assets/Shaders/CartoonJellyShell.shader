Shader "EarthOL/UI/ConnectedJellySkeleton"
{
    Properties
    {
        [PerRendererData] _MainTex ("UI texture", 2D) = "white" {}
        _BodyColor ("Head and torso", Color) = (1,.24,.51,1)
        _LimbColor ("All limbs", Color) = (.20,.85,.76,1)
        _Opacity ("Translucent candy", Range(0,1)) = .76
        _TrackingAlpha ("Tracking fade", Range(0,1)) = 1
        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "CanUseSpriteAtlas"="True" }
        Stencil
        {
            Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp]
            ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask]
        }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; };
            struct v2f { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; };
            float4 _JellySegments[12]; // Canvas-space endpoints inside this person's bounded UI quad.
            float4 _JellyRadii[12];    // Start radius, end radius, color group (0 body / 1 limbs), enabled.
            float4 _JellySize;
            float4 _JellyHead;         // Head center/radius and presence for an interior shine.
            float _JellyBlend, _Opacity, _TrackingAlpha;
            fixed4 _BodyColor, _LimbColor;

            v2f vert(appdata v)
            {
                v2f o; o.vertex = UnityObjectToClipPos(v.vertex); o.uv = v.uv; o.color = v.color; return o;
            }

            void Union(inout float distance, inout float2 normal, float d, float2 n, float smoothness)
            {
                float h = saturate(.5 + .5 * (d-distance) / smoothness);
                distance = lerp(d, distance, h) - smoothness * h * (1-h);
                normal = lerp(n, normal, h);
            }

            fixed4 frag(v2f i):SV_Target
            {
                float2 p = i.uv * _JellySize.xy;
                float bodyD = 100000, limbD = 100000;
                float2 bodyN = 0, limbN = 0;
                [unroll] for (int j=0; j<12; j++)
                {
                    if (_JellyRadii[j].w < .5) continue;
                    float2 a = _JellySegments[j].xy, b = _JellySegments[j].zw;
                    float2 axis = b-a;
                    float t = saturate(dot(p-a, axis) / max(dot(axis,axis), .001));
                    float2 delta = p-a-axis*t;
                    float length = max(.001, sqrt(dot(delta,delta)));
                    float d = length - lerp(_JellyRadii[j].x, _JellyRadii[j].y, t);
                    float2 n = delta / length;
                    if (_JellyRadii[j].z < .5) Union(bodyD, bodyN, d, n, _JellyBlend);
                    else Union(limbD, limbN, d, n, _JellyBlend);
                }
                float bodyWeight = saturate(.5 + .5*(limbD-bodyD) / _JellyBlend);
                float d = lerp(limbD, bodyD, bodyWeight) - _JellyBlend * bodyWeight * (1-bodyWeight);
                float aa = max(fwidth(d), .6);
                float coverage = 1-smoothstep(-aa, aa, d);
                clip(coverage-.002);
                float2 normal = normalize(lerp(limbN, bodyN, bodyWeight) + float2(.0001,.0001));
                float edgeDistance = max(0,-d);
                float rimWidth = max(1.4, _JellyBlend*.38);
                float rim = exp(-edgeDistance / rimWidth);
                float lighting = saturate(dot(normal, normalize(float2(-.55,.83))));
                float specular = pow(lighting, 9) * exp(-abs(edgeDistance-rimWidth*1.6) / (rimWidth*1.1));
                float coolEdge = pow(saturate(dot(normal,normalize(float2(.65,-.76)))),3)*rim;
                fixed3 candy = lerp(_LimbColor.rgb,_BodyColor.rgb,bodyWeight);
                candy = lerp(candy*.68, candy, saturate(edgeDistance / (rimWidth*2)));
                candy = lerp(candy, fixed3(1,1,1), saturate(rim*.32+specular*.86));
                candy += coolEdge * fixed3(.08,.10,.15);
                // A soft rectangular glint inside the skull's jelly hood.
                float2 headDelta=(p-_JellyHead.xy)/max(1,_JellyHead.z);
                float glint=exp(-dot((headDelta-float2(-.37,.47))*float2(5,10),
                                    (headDelta-float2(-.37,.47))*float2(5,10)))*_JellyHead.w;
                candy=lerp(candy,fixed3(1,1,1),glint*.58);
                float alpha=saturate(_Opacity + rim*.14 + specular*.10);
                return fixed4(candy,coverage*alpha*_TrackingAlpha*i.color.a);
            }
            ENDCG
        }
    }
    FallBack Off
}
