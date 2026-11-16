// KK worm swarm dust (WormDustFx.cs). Particles lit by the sun, nothing else:
// - KK_SIXWAY: puffs. Texture A rgb = how the puff is lit from +x, +y, +z (toward the viewer), its alpha = coverage;
//   texture B rgb = lit from -x, -y, -z. The sun's direction in the billboard's own axes picks the blend, so a flat puff
//   lights like a 3D dust ball from any side (bright sun side, dark core, thin edges glowing against the light).
// - else: grit specks, lit like tiny spheres (the share of their lit half the viewer sees).
// - KK_SOFT: fades where the dust meets the ground or a craft (needs the camera's depth texture).
// - KK_HOT: the kill blast's glow (WormBlastFx.cs): unlit, emissive, the particle colour times the puff's coverage; the
//   material sets additive blending (_SrcBlend One, _DstBlend One).
// Every variant fades out close to the camera (_NearFade: start, length in m), so a puff never slides into the lens with a
// hard edge cut by the near plane.
// No air on Moho: the colours are hard sun plus a very low ambient; there is no sky light or haze.
Shader "KK/WormDust"
{
	Properties
	{
		_MainTex ("Six-way A (+x +y +z, coverage)", 2D) = "white" {}
		_SixB ("Six-way B (-x -y -z)", 2D) = "white" {}
		_Soft ("Soft edge range (m)", Float) = 1.5
		_NearFade ("Near fade start, length (m)", Vector) = (1, 4, 0, 0)
		[HideInInspector] _SrcBlend ("Src blend", Float) = 5
		[HideInInspector] _DstBlend ("Dst blend", Float) = 10
	}
	SubShader
	{
		Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
		Blend [_SrcBlend] [_DstBlend]
		ZWrite Off
		Cull Off
		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile __ KK_SIXWAY
			#pragma multi_compile __ KK_SOFT
			#pragma multi_compile __ KK_HOT
			#pragma target 3.0
			#include "UnityCG.cginc"

			sampler2D _MainTex, _SixB;
			float4 _MainTex_ST;
			float4 _KKSunDir;      // world, toward the sun
			float4 _KKSunColor;    // colour times intensity
			float4 _KKAmbient;
			float _Soft;
			float4 _NearFade;
			UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

			struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
			struct v2f
			{
				float4 pos : SV_POSITION;
				float2 uv : TEXCOORD0;
				float4 col : COLOR;
				float3 lv : TEXCOORD1;    // sun direction in the billboard's axes: x right, y up, z toward the viewer
				float4 proj : TEXCOORD2;
			};

			v2f vert(appdata v)
			{
				v2f o;
				o.pos = UnityObjectToClipPos(v.vertex);
				o.uv = TRANSFORM_TEX(v.uv, _MainTex);
				o.col = v.color;
				float3 L = normalize(_KKSunDir.xyz);
				// rows of the view matrix are the camera's right, up and back (toward the viewer) axes in world space
				o.lv = float3(dot(L, UNITY_MATRIX_V[0].xyz), dot(L, UNITY_MATRIX_V[1].xyz), dot(L, UNITY_MATRIX_V[2].xyz));
				o.proj = ComputeScreenPos(o.pos);
				COMPUTE_EYEDEPTH(o.proj.z);
				return o;
			}

			float4 frag(v2f i) : SV_Target
			{
				float light, alpha;
			#ifdef KK_HOT
				float cov = tex2D(_MainTex, i.uv).a * i.col.a;
				cov *= saturate((i.proj.z - _NearFade.x) / max(_NearFade.y, 0.01));
			#ifdef KK_SOFT
				float sceneH = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.proj)));
				cov *= saturate((sceneH - i.proj.z) / max(_Soft, 0.01));
			#endif
				return float4(i.col.rgb * cov, cov);
			#endif
			#ifdef KK_SIXWAY
				float4 a = tex2D(_MainTex, i.uv);
				float3 b = tex2D(_SixB, i.uv).rgb;
				// squared components of the unit sun vector sum to 1, so a puff lit from a diagonal is not up to 1.7x too bright
				float3 lp = saturate(i.lv), ln = saturate(-i.lv);
				lp *= lp; ln *= ln;
				light = dot(a.rgb, lp) + dot(b, ln);
				alpha = a.a;
			#else
				alpha = tex2D(_MainTex, i.uv).a;
				light = 0.5 + 0.5 * i.lv.z;      // a sphere: the lit share of the half the viewer sees
			#endif
				float3 rgb = i.col.rgb * (_KKSunColor.rgb * light + _KKAmbient.rgb);
				alpha *= i.col.a;
				alpha *= saturate((i.proj.z - _NearFade.x) / max(_NearFade.y, 0.01));
			#ifdef KK_SOFT
				float scene = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.proj)));
				alpha *= saturate((scene - i.proj.z) / max(_Soft, 0.01));
			#endif
				return float4(rgb, alpha);
			}
			ENDCG
		}
	}
}
