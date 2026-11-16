// KK/Dread: one-pass screen shader for the Kraken's Kreations "feed under threat" look.
// Used as a full-screen blit after image effects (no depth, no extra textures except the optional frozen frame).
// Every effect is driven by a uniform; ALL ZERO (or _Strength 0) returns the source texel unchanged.
// Colour space: gamma (KSP).
// One failing analog feed, not a stack of filters: everything is built from whole scan lines (sync, tear, loss, streaks) and from taking light and colour away.
Shader "KK/Dread"
{
	Properties
	{
		_MainTex ("Source", 2D) = "white" {}
		_FreezeTex ("Frozen frame", 2D) = "black" {}
		_Strength ("Master strength", Range(0, 1)) = 1
		_Grade ("Grade (colour loss, cool, darker, harder)", Range(0, 1)) = 0
		_Vignette ("Vignette", Range(0, 1)) = 0
		_Grain ("Grain", Range(0, 1)) = 0
		_Chroma ("Edge chroma split (transient)", Range(0, 1)) = 0
		_SignalNoise ("Signal noise floor", Range(0, 1)) = 0
		_Tear ("Line tear amount", Range(0, 1)) = 0
		_TearY ("Tear band centre (0..1, bottom=0)", Range(0, 1)) = 0.5
		_TearH ("Tear band height", Range(0, 0.3)) = 0.06
		_Block ("Dropout low-res blocks", Range(0, 1)) = 0
		_Freeze ("Frozen-frame blend", Range(0, 1)) = 0
		_Sync ("Sync instability (line wander, tracking band, smear)", Range(0, 1)) = 0
		_TrackY ("Tracking band position (0..1)", Range(0, 1)) = 0.3
		_Loss ("Signal loss (bands of picture go, then all of it)", Range(0, 1)) = 0
		_Dark ("Gain loss (whole picture)", Range(0, 1)) = 0
		_Tube ("Colour TV camera look (imager feed)", Range(0, 1)) = 0
		_Wear ("Feed degradation: colour drains and drifts, bleeds, softens", Range(0, 1)) = 0
		_Seq ("Field-sequential colour (channels from the last R, G, B fields)", Range(0, 1)) = 0
		_FieldR ("Last red field", 2D) = "black" {}
		_FieldG ("Last green field", 2D) = "black" {}
		_FieldB ("Last blue field", 2D) = "black" {}
		_AgcAvg ("Camera gain: smoothed mean brightness of the frame (linear)", Float) = 0.16
		_Blow ("Overexposure: highlights bloom and roll off to white", Range(0, 1)) = 0
		_Snow ("Static snow (whole picture)", Range(0, 1)) = 0
		_Seed ("Frame seed (integer, driver changes it)", Float) = 0
	}
	SubShader
	{
		Cull Off ZWrite Off ZTest Always Blend Off
		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 4.0
			#include "UnityCG.cginc"

			sampler2D _MainTex;
			float4 _MainTex_TexelSize; // 1/w, 1/h, w, h
			sampler2D _FreezeTex;
			float _Strength, _Grade, _Vignette, _Grain, _Chroma, _SignalNoise;
			float _Tear, _TearY, _TearH, _Block, _Freeze, _Seed;
			float _Sync, _TrackY, _Loss, _Dark;
			float _Tube, _Blow, _Snow, _Wear, _Seq;
			sampler2D _FieldR, _FieldG, _FieldB;
			float _AgcAvg;

			struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

			v2f vert (appdata_img v)
			{
				v2f o;
				o.pos = UnityObjectToClipPos(v.vertex);
				o.uv = v.texcoord.xy;
				return o;
			}

			uint hashU (uint x)
			{
				x ^= x >> 16; x *= 0x7feb352dU; x ^= x >> 15; x *= 0x846ca68bU; x ^= x >> 16;
				return x;
			}
			float rnd (uint2 p, uint s)
			{
				uint h = hashU(p.x + hashU(p.y + hashU(s) * 0x9E3779B9U));
				return (h >> 8) * (1.0 / 16777216.0);
			}
			// triangular distribution in [-1, 1], zero mean
			float tri (uint2 p, uint s) { return rnd(p, s) + rnd(p, s + 7919U) - 1.0; }

			static float2 gCell = 0; // dropout cell size in uv, and whether this pixel is in a dropped tile
			static float gAct = 0;

			float3 raw (float2 u, float f)
			{
				float3 c = tex2Dlod(_MainTex, float4(u, 0, 0)).rgb;
				if (f > 0) c = lerp(c, tex2Dlod(_FreezeTex, float4(u, 0, 0)).rgb, f);
				// field-sequential colour: each channel from the last field of its colour (taken 1/60 s apart)
				if (_Seq > 0) c = lerp(c, float3(tex2Dlod(_FieldR, float4(u, 0, 0)).r, tex2Dlod(_FieldG, float4(u, 0, 0)).g, tex2Dlod(_FieldB, float4(u, 0, 0)).b), saturate(_Seq) * saturate(_Strength));
				return c;
			}
			float3 samp (float2 u, float f)
			{
				float3 c = raw(u, f);
				if (gAct > 0)
				{
					// signal loss: low-res cell, isolated bright pixels are dropped (min of centre and cell average), darker
					float2 o = gCell * 0.25;
					float3 a = (raw(u + float2(o.x, o.y), f) + raw(u + float2(-o.x, o.y), f) + raw(u + float2(o.x, -o.y), f) + raw(u + float2(-o.x, -o.y), f)) * 0.25;
					c = min(c, a) * 0.99;
				}
				return c;
			}
			float4 frag (v2f i) : SV_Target
			{
				float s = saturate(_Strength);
				float grade = saturate(_Grade) * s, vig = saturate(_Vignette) * s, grain = saturate(_Grain) * s;
				float chroma = saturate(_Chroma) * s, sn = saturate(_SignalNoise) * s, tear = saturate(_Tear) * s;
				float blk = saturate(_Block) * s, frz = saturate(_Freeze) * s;
				float sync = saturate(_Sync) * s, loss = saturate(_Loss) * s, dark = saturate(_Dark) * s;
				float tube = saturate(_Tube) * s, blow = saturate(_Blow) * s, snow = saturate(_Snow) * s;

				float4 src = tex2Dlod(_MainTex, float4(i.uv, 0, 0));
				if (grade + vig + grain + chroma + sn + tear + blk + frz + sync + loss + dark + tube + blow + snow + saturate(_Seq) * s <= 0) return src; // exact pass-through

				float2 res = _MainTex_TexelSize.zw;
				float aspect = res.x / res.y;
				float pxScale = res.y / 1080.0;                 // keeps effects the same size relative to the frame
				float cell = max(1.0, floor(pxScale + 0.5));    // integer noise-cell size in pixels
				uint seed = (uint)max(_Seed, 0.0);
				float2 uv0 = i.uv;
				float2 uv = uv0;
				float splitX = 0;
				float rowDark = 1.0;
				// picture lines, 2 px high at 1080p: thicker than a pixel so that lines and grain never weave into a cloth pattern
				uint line2 = (uint)floor(uv0.y * res.y / (2.0 * cell));

				// sync instability: the lines lose their horizontal lock, gently everywhere and hard inside one slow tracking band
				float track = 0;
				if (sync > 0)
				{
					float td = abs(frac(uv0.y - _TrackY + 0.5) - 0.5);
					track = sync * smoothstep(0.075, 0.0, td);
					float wander = 0.004 * sync * sin(uv0.y * 7.0 + _TrackY * 6.2832);
					float jit = 0.0011 * sync * tri(uint2(line2, 17U), seed / 2U);
					float hard = track * track * (0.03 + 0.035 * tri(uint2(line2, 19U), seed / 2U));
					uv.x = frac(uv.x + wander + jit + hard);
					rowDark *= 1.0 - 0.45 * track;
				}

				// line tear: whole bands of lines slip sideways, skewed towards their lower edge; one main band and three more that change together
				if (tear > 0)
				{
					uint hold = seed / 3U;
					float dx = 0, inBand = 0;
					for (uint k = 0U; k < 4U; k++)
					{
						float yc = k == 0U ? _TearY : frac(_TearY + 0.17 + 0.23 * k + 0.16 * rnd(uint2(k, 31U), hold));
						float hh = (k == 0U ? 1.5 : 0.35 + 0.9 * rnd(uint2(k, 37U), hold)) * _TearH * 0.5;
						float e = (uv.y - yc) / max(hh, 1e-4);
						if (abs(e) < 1.0)
						{
							float side = rnd(uint2(k, 41U), hold) < 0.5 ? -1.0 : 1.0;
							dx = side * (0.035 + 0.11 * rnd(uint2(k, 43U), hold)) + 0.03 * side * (1.0 - e) * (1.0 - e) * 0.5;
							inBand = 1.0;
						}
					}
					dx = inBand * tear * (dx + 0.006 * tri(uint2(line2, 3U), seed));
					uv.x = frac(uv.x + dx);
					splitX = inBand * tear * 0.004;
					rowDark *= 1.0 - 0.12 * inBand * tear;   // only a little: the bands end at once, and light must not come back fast
				}

				// dropout: tiles of the picture drop to low resolution (down to 320x180 at full amount)
				if (blk > 0)
				{
					uint2 tile = (uint2)floor(uv0 * float2(16, 9));
					float act = step(rnd(tile, seed / 6U + 11U), saturate(blk * 1.15));
					float rx = lerp(960.0, 320.0, saturate(blk));
					float2 g = float2(rx, rx / aspect);
					uv = lerp(uv, (floor(uv * g) + 0.5) / g, act);
					gCell = 1.0 / g; gAct = act;
				}

				// edge-only chroma split, centre (radius < 0.45) is exactly clean
				float3 rgb;
				float2 d = (uv0 - 0.5);
				float rr = length(d * float2(aspect, 1.0) * 2.0);
				float cw = smoothstep(0.45, 1.2, rr) * chroma;
				if (cw > 0 || splitX > 0)
				{
					float2 dp = d * res;
					float2 dpn = dp / max(length(dp), 1e-4);
					float2 off = dpn / res * (cw * pxScale * 4.0) + float2(splitX, 0);
					rgb = float3(samp(uv + off, frz).r, samp(uv, frz).g, samp(uv - off, frz).b);
				}
				else rgb = samp(uv, frz);

				// bandwidth loss: horizontal smear and one faint echo, the fine detail goes first
				if (sync > 0)
				{
					float px = (1.0 + 2.5 * sync) * pxScale / res.x;
					float3 sm = (raw(frac(uv + float2(px, 0)), frz) + raw(frac(uv - float2(px, 0)), frz)) * 0.5;
					rgb = lerp(rgb, sm, 0.7 * sync);
					float3 echo = raw(frac(uv - float2(14.0 * pxScale / res.x, 0)), frz);
					rgb = lerp(rgb, min(rgb, echo), 0.35 * sync);
				}

				const float3 LW = float3(0.299, 0.587, 0.114);

				// field-sequential colour TV (the imager's feed, in the style of the Apollo television pictures). Modelled on the documented
				// Apollo 10-17 colour camera (Wikipedia "Apollo TV camera"; NASA TN D-7476):
				// - colour: one tube behind a colour wheel, 59.94 fields/s, one colour per field; the C# side keeps the last field of each
				//   colour (_FieldR/G/B, see raw()), so moving things split into colour fringes.
				// - resolution: about 350 TV lines vertically; 2-3 MHz video bandwidth gives about 200 TV lines across a picture height.
				//   NTSC carried colour at lower bandwidth than brightness, so colour bleeds wider than detail.
				// - tone: "The camera did not have a gamma correction circuit" (mid-tones lose detail) and its automatic gain made bright
				//   surfaces "bloom": a linear signal (picture^2.2) with gain, clipped to white, with a halo around what clips.
				// _Wear (0..1) is the swarm's interference on top: resolution and colour drop further, the colour phase drifts.
				if (tube + blow > 0)
				{
					float wear = saturate(_Wear) * s;
					// resolution limit in pixels of this screen: one TV line = picture height / lines
					float hx = (res.y / 200.0) * 0.5 * (1.0 + 1.5 * wear) / res.x;
					float vy = (res.y / 350.0) * 0.5 * (1.0 + 1.0 * wear) / res.y;
					float3 lum = rgb * 0.36
						+ (raw(frac(uv + float2(hx, 0)), frz) + raw(frac(uv - float2(hx, 0)), frz)) * 0.20
						+ (raw(frac(uv + float2(2.0 * hx, 0)), frz) + raw(frac(uv - float2(2.0 * hx, 0)), frz)) * 0.06
						+ (raw(frac(uv + float2(0, vy)), frz) + raw(frac(uv - float2(0, vy)), frz)) * 0.06;
					rgb = lerp(rgb, lum, tube);
					// colour at NTSC chroma bandwidth (about a third of the brightness bandwidth)
					float cwx = hx * 3.0;
					float3 cs = (rgb + raw(frac(uv + float2(cwx, 0)), frz) + raw(frac(uv - float2(cwx, 0)), frz)
						+ 0.5 * raw(frac(uv + float2(2.0 * cwx, 0)), frz) + 0.5 * raw(frac(uv - float2(2.0 * cwx, 0)), frz)) / 4.0;
					float Y = dot(rgb, LW);
					float cy = dot(cs, LW);
					float2 C = float2(cs.b - cy, cs.r - cy);
					// interference: the colour phase wanders in slow waves down the picture and the colour drains
					float ph = wear * (0.35 * sin(uv0.y * 3.1 + seed * 0.013) + 0.12 * sin(uv0.y * 17.0 - seed * 0.031));
					float cph = cos(ph), sph = sin(ph);
					C = float2(C.x * cph - C.y * sph, C.x * sph + C.y * cph) * lerp(1.0, 1.0 - 0.85 * wear, tube);
					float3 col = max(float3(Y + C.y, (Y - 0.299 * (Y + C.y) - 0.114 * (Y + C.x)) / 0.587, Y + C.x), 0);
					// no gamma correction + automatic gain: a linear signal (picture^2.2) whose gain follows the scene's AVERAGE brightness
					// (an average-reading AGC with a slow loop: _AgcAvg), clipped at white. A dark scene with a bright subject
					// therefore blows the subject out, as the Westinghouse camera did with white suits; _Blow raises the exposure target.
					float avg = _AgcAvg;   // the frame's mean (linear), smoothed over about a second on the C# side (DreadCamera)
					float gain = clamp((0.16 * (1.0 + 0.8 * blow)) / max(avg, 1e-3), 1.0, 10.0);
					float3 lin = pow(col, 2.2) * gain;
					float halo = 0;
					for (uint k = 0U; k < 8U; k++)
					{
						float ang = k * 0.785398 + 0.39;
						float2 o = float2(cos(ang), sin(ang)) * float2(hx * res.x, vy * res.y) * 3.0 / res;
						halo += saturate(pow(dot(raw(frac(uv + o), frz), LW), 2.2) * gain - 0.9);
					}
					lin += halo * 0.125 * 0.6 * blow;
					float3 tv = saturate(lin);
					rgb = lerp(rgb, tv, max(tube, blow));
				}

				// grade: the picture gets darker and harder, colour drains out, warm light goes last. Black stays black.
				if (grade > 0)
				{
					rgb *= 1.0 - 0.30 * grade;
					rgb *= 1.0 - 0.3 * grade * (1.0 - smoothstep(0.0, 0.35, dot(rgb, LW)));   // toe 0.3: night at full menace stays playable
					float l0 = saturate(dot(rgb, LW));
					rgb *= lerp(l0, l0 * l0 * (3.0 - 2.0 * l0), 0.55 * grade * smoothstep(0.08, 0.4, l0)) / max(l0, 1e-4); // S curve on luminance, not in the shadows: night and deep water stay readable
					float l = dot(rgb, LW);
					float warm = saturate((rgb.r - rgb.b) * 3.0 / max(l, 0.08));               // the one accent: fire and warning lamps hold their colour longer
					rgb = lerp(float3(l, l, l), rgb, 1.0 - 0.80 * grade * (1.0 - 0.45 * warm));
					rgb *= lerp(float3(1, 1, 1), float3(0.94, 0.99, 1.06), grade);
				}
				// vignette: up to 70% at the corners, smooth, no ring: the view closes in. The centre stays untouched.
				if (vig > 0)
				{
					// frame-relative (elliptical) radius: 0 at centre, 1 at the edge midpoints, 1.414 at the corners
					float ve = length(d * 2.0);
					rgb *= 1.0 - 0.70 * vig * smoothstep(0.30, 1.42, ve);
				}
				rgb *= rowDark;
				if (dark > 0) rgb *= 1.0 - 0.85 * dark;

				float gone = 0;
				// signal loss: bands of lines fall away, irregular in height, held for about a third of a second; at 1 only a ghost of the picture is left
				if (loss > 0)
				{
					// each band changes at its own moment: changing them together would make the mean brightness jump
					uint bandId = (uint)floor(uv0.y * 49.0);
					uint groupId = bandId / 4U;
					float a = rnd(uint2(bandId, 47U), (seed + bandId * 7U) / 20U), b = rnd(uint2(groupId, 53U), (seed + groupId * 13U) / 28U);
					gone = step(0.45 * a + 0.55 * b, loss * 1.02);
					rgb *= lerp(1.0 - 0.45 * loss, 0.17, gone);
				}

				float lum = dot(rgb, LW);
				float edge0 = smoothstep(0.0, 0.1, lum); // no grain on true black: blacks are not lifted

				// signal noise: slow bars of gain, never line-by-line (that wove a cloth pattern into the sky before), and rare streaks of static along a line
				float nz = max(max(sn, frz * 0.12), loss * 0.8);
				if (nz > 0)
				{
					// the bars drift, they are not re-rolled: re-rolling flickered the whole picture at 7 Hz
					float gy = uv0.y * 17.0 + (seed % 100000U) * 0.012;
					float gf = frac(gy); gf = gf * gf * (3.0 - 2.0 * gf);
					uint gi = (uint)floor(gy);
					float bar = lerp(rnd(uint2(gi, 59U), 5U), rnd(uint2(gi + 1U, 59U), 5U), gf) * 2.0 - 1.0;
					float band = 0.5 + 0.5 * sin(uv0.y * 6.0 + 0.04 * seed);
					rgb *= saturate(1.0 + 0.16 * nz * bar - 0.30 * nz * (band - 0.5));
					uint srow = (uint)floor(i.pos.y / (2.0 * cell));
					// every line has its own streak length and offset, so that streaks can never line up into columns
					float slen = cell * (70.0 + 260.0 * rnd(uint2(srow, 83U), seed / 4U));
					float sx = i.pos.x / slen + rnd(uint2(srow, 61U), seed) * 13.0;
					uint si = (uint)floor(sx);
					float density = 0.006 * nz + 0.12 * track + 0.10 * gone * loss;
					if (rnd(uint2(si, srow), seed + 67U) < density)
					{
						float sf = frac(sx);
						float sw = smoothstep(0.0, 0.12, sf) * (1.0 - smoothstep(0.55, 1.0, sf)) * (0.35 + 0.65 * rnd(uint2((uint)(i.pos.x / cell), srow), seed + 71U));
						rgb += (0.08 + 0.22 * rnd(uint2(si, srow), seed + 73U)) * sw;
					}
				}
				// grain: luminance only, up to 5% amplitude, stronger in shadows
				if (grain > 0)
				{
					uint2 gp = (uint2)floor(i.pos.xy / cell);
					float w = (1.0 - 0.7 * smoothstep(0.1, 0.85, lum)) * edge0;
					rgb += tri(gp, seed + 101U) * 0.05 * grain * w;
				}

				// static snow: per-pixel noise that replaces the picture (the outside view fading out near the swarm). Changes every frame.
				if (snow > 0)
				{
					uint2 sp = (uint2)floor(i.pos.xy / max(1.0, cell * 1.5));
					float sv = rnd(sp, seed * 3U + 307U);
					sv = sv * sv * 0.85 + 0.05;
					float lineGain = 0.75 + 0.25 * rnd(uint2(sp.y, 311U), seed);
					rgb = lerp(rgb, float3(sv, sv, sv) * lineGain * (1.0 - 0.85 * dark), snow);
				}

				return float4(saturate(rgb), src.a);
			}
			ENDCG
		}
	}
	Fallback Off
}
