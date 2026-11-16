// KK/WormFlesh: wet, glossy worm flesh for the Moho worm swarm (MohoWormHeapSkin).
// Vertex colour: r = ambient occlusion (1 open, 0 buried deep in the pile), g = per-worm tint shift.
// Two specular lobes (a tight wet glint and a broad sheen), wrapped diffuse with red light bleed on the shadow side, a rim.
Shader "KK/WormFlesh"
{
    Properties
    {
        _Color ("Flesh tint", Color) = (0.70,0.12,0.12,1)
        _SpecColor ("Wet highlight", Color) = (1,0.86,0.84,1)
        _Shininess ("Gloss", Range(0.05,1)) = 0.82
        _Sheen ("Broad sheen", Range(0,1)) = 0.4
        _Rim ("Rim", Range(0,1)) = 0.3
        _Ambient ("Self light floor", Range(0,0.3)) = 0.03
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200
        CGPROGRAM
        #pragma surface surf WormFlesh fullforwardshadows addshadow
        #pragma target 3.0
        fixed4 _Color;
        half _Shininess, _Sheen, _Rim, _Ambient;
        struct Input { fixed4 color : COLOR; };

        void surf(Input IN, inout SurfaceOutput o)
        {
            fixed ao = IN.color.r;
            fixed3 tint = _Color.rgb * lerp(fixed3(0.85, 0.8, 0.85), fixed3(1.15, 1.1, 1.0), IN.color.g);
            o.Albedo = tint * ao * (0.3 + 0.7 * ao);
            o.Specular = _Shininess;
            o.Gloss = ao * ao;
            o.Emission = tint * _Ambient * ao;
            o.Alpha = 1;
        }

        inline half4 LightingWormFlesh(SurfaceOutput s, half3 lightDir, half3 viewDir, half atten)
        {
            half ndl = dot(s.Normal, lightDir);
            half wrap = saturate((ndl + 0.35) / 1.35);
            half3 bleed = half3(1.0, 0.3, 0.25) * saturate(0.25 - ndl * 0.5) * 0.3;
            half3 h = normalize(lightDir + viewDir);
            half nh = saturate(dot(s.Normal, h));
            half spec = pow(nh, exp2(2 + s.Specular * 9)) * 1.8 + pow(nh, 10) * _Sheen * 0.3;
            half rim = pow(1 - saturate(dot(s.Normal, viewDir)), 3) * _Rim;
            half3 light = _LightColor0.rgb * atten;
            half3 c = s.Albedo * (wrap + bleed + rim * 0.5) * light + _SpecColor.rgb * spec * s.Gloss * light;
            return half4(c, 1);
        }
        ENDCG
    }
    FallBack "Specular"
}
