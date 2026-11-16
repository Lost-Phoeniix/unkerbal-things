// KK worm swarm, P1: draws every worm as one smooth tube straight from the simulation buffers.
// The mesh only carries (along, around) in uv and the worm index in uv2; the vertex function builds the tube on a
// Catmull-Rom spline through the worm's points, interpolated between the last two simulation steps. Capsules are only the
// collision shape: what you see is one continuous, tapered, segmented skin (the fix for the "pill body" look).
// Look is provisional (P4 does the real look): wet dark flesh, annuli grooves, a paler saddle band.
Shader "KK/WormTube"
{
	Properties
	{
		_ColorA ("Flesh A", Color) = (0.46, 0.11, 0.11, 1)
		_ColorB ("Flesh B", Color) = (0.30, 0.07, 0.13, 1)
		_Band ("Saddle band", Color) = (0.55, 0.25, 0.24, 1)
		_SpecTint ("Wet specular", Color) = (0.07, 0.065, 0.065, 1)
		_Smooth ("Wetness", Range(0, 1)) = 0.86
		_Annuli ("Annuli per worm", Float) = 70
	}
	SubShader
	{
		Tags { "RenderType" = "Opaque" }
		Cull Back
		CGPROGRAM
		#pragma surface surf StandardSpecular vertex:vert addshadow fullforwardshadows
		#pragma target 4.5
		#pragma only_renderers d3d11

		float _Alpha;
		fixed4 _ColorA, _ColorB, _Band, _SpecTint;
		half _Smooth, _Annuli;

		#define PPW 16

		// the surface-shader analysis pass compiles without SM5 buffers, so they are guarded
		#ifdef SHADER_API_D3D11
		StructuredBuffer<float4> _Pos;
		StructuredBuffer<float4> _RenderPrev;
		StructuredBuffer<float4> _Frame;
		StructuredBuffer<float4> _WormInfo;
		StructuredBuffer<uint> _State;
		float3 PointAt(uint base, int s) { s = clamp(s, 0, PPW - 1); return lerp(_RenderPrev[base + s].xyz, _Pos[base + s].xyz, _Alpha); }
		float3 FrameAt(uint i) { return _Frame[i].xyz; }
		float4 InfoAt(uint w) { return _WormInfo[w]; }
		bool HiddenAt(uint w) { return _State[w] == 2u; }
		#else
		float3 PointAt(uint base, int s) { return float3(0, 0, s * 0.1); }
		float3 FrameAt(uint i) { return float3(0, 1, 0); }
		float4 InfoAt(uint w) { return float4(0.04, 0.06, 1, 0.5); }
		bool HiddenAt(uint w) { return false; }
		#endif

		struct Input { float2 tubeCoord; float tint; float shade; };

		void vert(inout appdata_full v, out Input o)
		{
			UNITY_INITIALIZE_OUTPUT(Input, o);
			uint worm = (uint)(v.texcoord1.x + 0.5);
			float u = v.texcoord.x, side = v.texcoord.y;
			uint base = worm * PPW;
			float fs = u * (PPW - 1);
			int s1 = min((int)floor(fs), PPW - 2);
			float t = fs - s1;
			float3 p0 = PointAt(base, s1 - 1), p1 = PointAt(base, s1), p2 = PointAt(base, s1 + 1), p3 = PointAt(base, s1 + 2);
			float t2 = t * t, t3 = t2 * t;
			float3 pos = 0.5 * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3);
			float3 tg = 0.5 * ((-p0 + p2) + 2 * (2 * p0 - 5 * p1 + 4 * p2 - p3) * t + 3 * (-p0 + 3 * p1 - 3 * p2 + p3) * t2);
			tg = normalize(tg + float3(1e-6, 0, 0));
			float3 n = lerp(FrameAt(base + s1), FrameAt(base + s1 + 1), t);
			n = normalize(n - dot(n, tg) * tg + float3(0, 1e-5, 0));
			float3 b = cross(tg, n);

			float4 info = InfoAt(worm);
			// a squeezed link bulges, a stretched one thins (volume roughly kept), so the peristaltic wave is visible
			float len = length(p2 - p1);
			float bulge = clamp(sqrt(info.y / max(len, 1e-4)), 0.75, 1.3);
			float headCap = sqrt(saturate(u / 0.045));
			float tailCap = sqrt(saturate((1 - u) / 0.16));
			float saddle = exp(-pow((u - 0.27) / 0.045, 2));
			float r = info.x * bulge * headCap * lerp(0.45, 1.0, tailCap) * tailCap * (1 + 0.12 * saddle);
			if (HiddenAt(worm)) r = 0;   // underground: collapsed to nothing

			float ang = side * 6.2831853;
			float3 nrm = cos(ang) * n + sin(ang) * b;
			v.vertex = float4(pos + nrm * r, 1);
			v.normal = nrm;
			o.tubeCoord = float2(u, side);
			o.tint = info.w;
			// every worm a little lighter or darker (+-18 %), so single bodies read in the heap instead of one dark mass
			o.shade = 0.82 + 0.36 * frac(sin(worm * 12.9898 + 78.233) * 43758.5453);
		}

		void surf(Input IN, inout SurfaceOutputStandardSpecular o)
		{
			float u = IN.tubeCoord.x;
			float groove = 0.5 + 0.5 * cos(u * _Annuli * 6.2831853);
			float saddle = smoothstep(0.035, 0.0, abs(u - 0.27) - 0.035);
			fixed3 c = lerp(_ColorA.rgb, _ColorB.rgb, IN.tint);
			c *= lerp(0.72, 1.0, groove);
			c = lerp(c, _Band.rgb, saddle * 0.75);
			c *= IN.shade;
			o.Albedo = c;
			o.Specular = _SpecTint.rgb;
			o.Smoothness = _Smooth * lerp(0.8, 1.0, groove);
			o.Occlusion = lerp(0.85, 1.0, groove);
			o.Alpha = 1;
		}
		ENDCG
	}
	FallBack Off
}
