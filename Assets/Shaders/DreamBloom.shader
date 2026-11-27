Shader "EarthOL/DreamBloom"
{
    Properties
    {
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Destination blend", Float) = 1
        _Intensity ("Glow strength", Float) = 1.3
        _Grain ("Fine film grain", Range(0, 0.15)) = 0.045
        _CenterGap ("Transparent center radius", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent+110" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZWrite Off
        Blend SrcAlpha [_DstBlend]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            float _Intensity, _Grain, _CenterGap;
            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float2 profile : TEXCOORD1;
                float4 color : COLOR;
            };
            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 profile : TEXCOORD1;
                float4 color : COLOR;
                float2 localPosition : TEXCOORD2;
            };
            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float noise(float2 p)
            {
                float2 a = floor(p), f = frac(p); f = f*f*(3-2*f);
                return lerp(lerp(hash(a), hash(a+float2(1,0)), f.x),
                    lerp(hash(a+float2(0,1)), hash(a+1), f.x), f.y);
            }
            v2f vert(appdata v)
            {
                v2f o; o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv; o.profile = v.profile; o.color = v.color;
                o.localPosition = v.vertex.xy; return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                float2 p = i.uv*2-1;
                float r2 = dot(p,p), shape, core;
                float grain = (hash(floor(i.vertex.xy) + floor(_Time.y*12))-.5)*_Grain;
                float gapMask = _CenterGap > 0 ? smoothstep(_CenterGap,
                    _CenterGap+max(.07,_CenterGap*.45),length(i.localPosition)) : 1;
                if (i.profile.x > 3.5 && i.profile.x < 5.5)
                {
                    bool bead = i.profile.x > 4.5;
                    float radius = bead ? r2 : p.y*p.y;
                    float mask = 1-smoothstep(.83,1.0,radius);
                    float roundness = sqrt(saturate(1-radius));
                    float across = bead ? p.x : p.y;
                    float along = bead ? p.y : p.x;
                    float pearl = sin(along*7+across*3+i.profile.y*1.7)*.5+.5;
                    float3 rainbow = lerp(float3(.68,1,.65),float3(.94,1,.65),pearl);
                    float3 base = lerp(i.color.rgb,rainbow,.07+pearl*.12);
                    float shade = .55+.45*roundness-.12*across;
                    float3 liquid = base*shade;
                    float highlight = exp(-pow((across+.38)/.15,2)) *
                        (bead ? exp(-pow((along-.30)/.36,2)) : smoothstep(-.3,.8,along));
                    float smallSpec = exp(-pow((across+.48)/.085,2))*
                        exp(-pow((along-.63)/.16,2));
                    float rim = pow(saturate(abs(across)),7)*.32;
                    liquid = lerp(liquid,float3(.98,1,.82),saturate(highlight*.81+smallSpec*.6));
                    liquid += rainbow*rim;
                    // Pale lime pearl at the source; the vertex palette supplies green and rose tips.
                    if (!bead) liquid = lerp(float3(.94,1,.70),liquid,smoothstep(0,.28,i.uv.x));
                    float flecks = pow(noise(i.uv*float2(55,13)+i.profile.y),16)*.35;
                    return float4(liquid+flecks+grain*.25,mask*i.color.a*.96*gapMask);
                }
                if (i.profile.x > 5.5)
                {
                    float star = pow(abs(p.x),.57)+pow(abs(p.y),.57);
                    float solid = 1-smoothstep(.85,1.06,star);
                    float halo = exp(-r2*4.5)*.22;
                    float white = exp(-r2*26)*.38;
                    return float4(lerp(i.color.rgb,float3(.96,1,.87),white)*_Intensity*.78,
                        saturate((solid+halo)*i.color.a)*gapMask);
                }
                if (i.profile.x < .5) // Translucent, softly lit billows.
                {
                    float n = noise(p*2.5+i.profile.y+_Time.y*.08);
                    n = n*.68 + noise(p*5.5-i.profile.y)*.32;
                    float2 edge = saturate(1-p*p);
                    shape = exp(-r2*2.2) * edge.x*edge.x*edge.y*edge.y * (.65+n*.50);
                    core = exp(-r2*10)*.23;
                }
                else if (i.profile.x < 1.5) // White-hot pinpoints and diffuse four-point stars.
                {
                    float2 a = abs(p);
                    float rays = exp(-min(a.x,a.y)*68) * exp(-max(a.x,a.y)*5.5);
                    shape = exp(-r2*7)*.23 + exp(-r2*95) + rays*.5;
                    shape *= smoothstep(1.0,.45,r2);
                    core = exp(-r2*50)*.8;
                }
                else // Curved ribbons: soft across width and fading at both ends.
                {
                    float edge = sin(saturate(i.uv.x)*UNITY_PI);
                    float thin = i.profile.x < 2.5 ? 1 : 0;
                    shape = (exp(-p.y*p.y*5)*.48 + exp(-p.y*p.y*48)*.60*thin) * edge;
                    shape *= smoothstep(1.0,.65,abs(p.y));
                    core = exp(-p.y*p.y*65)*edge*.30*thin;
                }
                float3 color = lerp(i.color.rgb, float3(.96,1,.88), core)*_Intensity;
                // Dither opacity too, so the broad haze does not form visible gradient bands.
                float alpha = saturate(shape*i.color.a + grain*.35*saturate(shape*12));
                return float4(color+grain, alpha*gapMask);
            }
            ENDCG
        }
    }
}
