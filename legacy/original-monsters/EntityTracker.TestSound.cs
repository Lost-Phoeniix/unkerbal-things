using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using KrakensKreations.Dread;
using UnityEngine;

// In-game scenes of the recorded soundscape (KK_TEST_ONLY=sound_set). sound_moho: craft on Moho, a real monster walks in, a landing and a crunch are forced.
// sound_water: the craft is reported 80 m under water at Kerbin with forced menace, so bed and whales run. Output: sound-<scene>.txt (measured results, cue list), sound-<scene>.wav (what the mixer put out).
public partial class EntityTracker
{
	private static class SoundTest
	{
		static readonly CultureInfo C = CultureInfo.InvariantCulture;

		internal static IEnumerator Run(string id, EntityTracker t, Vessel v, string outDir)
		{
			StringBuilder log = new StringBuilder();
			Action<string> say = s => { log.AppendLine(s); Debug.Log("[KK Sound test] " + s); };
			DreadAudioHost host = t.dreadAudioHost;
			DreadSoundMixer mixer = host.Mixer;
			int rate = AudioSettings.outputSampleRate;
			float secs = id == "sound_moho" ? 170f : 150f;
			float[] record = new float[(int)(rate * 2 * (secs + 5f))];
			mixer.RecordPosition = 0;
			mixer.Record = record;
			Dictionary<DreadSoundKind, int> count = new Dictionary<DreadSoundKind, int>();
			float start = Time.realtimeSinceStartup;
			t.DreadSoundCueObserver = (k, g) =>
			{
				int n;
				count.TryGetValue(k, out n);
				count[k] = n + 1;
				say((Time.realtimeSinceStartup - start).ToString("F1", C) + " cue " + k + " " + (20f * Mathf.Log10(g)).ToString("F0", C) + " dB");
			};
			if (id == "sound_moho")
			{
				t.SpawnBulkEntity();
				yield return null;
				say("monster spawned: " + t.bulkEntityActive + " " + t.bulkEntityStatus);
				t.PlaceTestWalker(v, 2200.0);
			}
			// sound_water: first 60 s only the depth (150 m, nothing about), then forced menace
			float noiseAt = 1.5f, traceAt = 0f;
			bool wasListening = false;
			bool owned = false, forced = false, switchChecked = false;
			float maxSynthDrone = 0f, maxSynthCreature = 0f, peak = 0f;
			float minBed = float.MaxValue;
			int ownFrames = 0, frames = 0;
			string env = "";
			while (Time.realtimeSinceStartup - start < secs)
			{
				float e = Time.realtimeSinceStartup - start;
				DreadSoundLogic l = t.DreadSoundLogicForTest;
				frames++;
				if (id == "sound_water" && e > 60f && t.dreadDebugMenace < 0f)
				{
					t.dreadDebugMenace = 0.8f;
					say("60 s: menace forced to 0.8");
				}
				if (id == "sound_moho")
				{
					// noises 2 km to the side make it search there; at 90 s the craft itself gives itself away
					if (e > noiseAt && e < 14f)
					{
						noiseAt += 1f;
						Vector3d upv = (v.GetWorldPos3D() - v.mainBody.position).normalized;
						Vector3d side = Vector3d.Exclude(upv, v.mainBody.transform.up).normalized;
						t.HuntInjectNoise(HuntNoise.Throttle, 0.9f, ProjectToGround(v.GetWorldPos3D() + side * 2200.0, v.mainBody));
					}
					if (e > 90f && e < 94f && e > noiseAt)
					{
						noiseAt = e + 1f;
						t.HuntInjectNoise(HuntNoise.Throttle, 0.9f, v.GetWorldPos3D());
					}
					if (t.huntBulk.hunt != null && t.huntBulk.hunt.Listening != wasListening)
					{
						wasListening = t.huntBulk.hunt.Listening;
						say(e.ToString("F1", C) + " listening " + wasListening + ", bed A gain " + l.VacBedA.ToString("F3", C));
					}
				}
				if (e > traceAt && l != null)
				{
					traceAt = e + 5f;
					say(e.ToString("F0", C) + " trace distance " + t.dreadLastDistance.ToString("F0", C) + ", state " + (t.huntBulk.hunt != null ? t.huntBulk.info.State.ToString() : "-") + ", menace " + t.dreadLast.Menace.ToString("F2", C) + ", tension " + l.Tension.ToString("F2", C) + ", beds " + l.VacBedA.ToString("F3", C) + " " + l.WaterBed.ToString("F3", C) + ", step gain " + (host.Steps != null ? host.Steps.Gain.ToString("F3", C) : "-") + ", music gain " + l.MusicGain.ToString("F2", C) + ", game music volume " + MusicVolume().ToString("F3", C) + " of " + GameSettings.MUSIC_VOLUME.ToString("F2", C));
				}
				if (t.DreadSoundOwns)
				{
					owned = true;
					ownFrames++;
					maxSynthDrone = Mathf.Max(maxSynthDrone, host.Synth.DroneLevel);
					maxSynthCreature = Mathf.Max(maxSynthCreature, host.Synth.CreatureLevel);
					float bed = id == "sound_moho" ? l.VacBedA : l.WaterBed;
					if (e > 20f)
					{
						minBed = Mathf.Min(minBed, bed);
					}
				}
				if (e > 2f && e < 3f && env == "")
				{
					env = DreadSoundEnvOf(v, false).ToString();
					say("environment " + env + ", altitude " + v.altitude.ToString("F1", C) + ", set wanted " + (l != null ? l.WantedSet : -1));
				}
				peak = Mathf.Max(peak, mixer.TakePeak());
				if (!forced && e > 100f)
				{
					forced = true;
					if (id == "sound_moho")
					{
						t.DreadSoundOnWarning(3, 1f);
						t.DreadSoundOnLanding(0.9f);
						t.DreadSoundCue(DreadSoundKind.Crunch, 1f);
					}
					else
					{
						t.DreadSoundCue(DreadSoundKind.Whale, 1f);
						t.DreadSoundCue(DreadSoundKind.Boom, 1f);
					}
				}
				if (!switchChecked && e > 130f)
				{
					switchChecked = true;
					dreadSoundscape = false;
					yield return new WaitForSecondsRealtime(1f);
					say("master switch off: owns " + t.DreadSoundOwns + ", mixer level " + mixer.MasterLevel.ToString("F2", C));
					dreadSoundscape = true;
				}
				yield return null;
			}
			t.DreadSoundCueObserver = null;
			t.dreadDebugMenace = -1f;
			mixer.Record = null;
			int n2 = mixer.RecordPosition;
			double sum = 0.0, lowest = 0.0;
			int w = rate / 10 * 2;
			int from = rate * 2 * 25;
			for (int a = from; a + w <= n2; a += w)
			{
				double acc = 0.0;
				for (int i = a; i < a + w; i++)
				{
					acc += record[i] * record[i];
				}
				double db = 10.0 * Math.Log10(acc / w + 1e-14);
				sum += acc;
				lowest = Math.Min(lowest, db);
			}
			say("frames " + frames + ", frames owned " + ownFrames + ", owned " + owned);
			say("synth while owned: drone max " + maxSynthDrone.ToString("F3", C) + ", creature max " + maxSynthCreature.ToString("F3", C));
			say("recorded " + (n2 / 2f / rate).ToString("F1", C) + " s, peak " + (20f * Mathf.Log10(Mathf.Max(peak, 1e-6f))).ToString("F1", C) + " dBFS, RMS after 25 s " + (10.0 * Math.Log10(sum / Math.Max(1, n2 - from) + 1e-14)).ToString("F1", C) + " dBFS, quietest 100 ms " + lowest.ToString("F1", C) + " dB, lowest bed gain " + minBed.ToString("F4", C));
			StringBuilder cs = new StringBuilder("cues:");
			foreach (KeyValuePair<DreadSoundKind, int> kv in count)
			{
				cs.Append(" ").Append(kv.Key).Append("=").Append(kv.Value);
			}
			say(cs.ToString());
			say("played " + mixer.Played + ", dropped " + mixer.Dropped + ", faulted " + host.Faulted + ", dread failed " + t.dreadFailed);
			File.WriteAllText(Path.Combine(outDir, "sound-" + id + ".txt"), log.ToString());
			WriteWav(Path.Combine(outDir, "sound-" + id + ".wav"), record, n2, rate);
		}

		static float MusicVolume()
		{
			System.Reflection.FieldInfo f = typeof(MusicLogic).GetField("setVolume1", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
			return f != null ? (float)f.GetValue(null) : -1f;
		}

		static void WriteWav(string path, float[] x, int n, int rate)
		{
			using (BinaryWriter w = new BinaryWriter(File.Create(path)))
			{
				w.Write(Encoding.ASCII.GetBytes("RIFF"));
				w.Write(36 + n * 2);
				w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
				w.Write(16);
				w.Write((short)1);
				w.Write((short)2);
				w.Write(rate);
				w.Write(rate * 4);
				w.Write((short)4);
				w.Write((short)16);
				w.Write(Encoding.ASCII.GetBytes("data"));
				w.Write(n * 2);
				for (int i = 0; i < n; i++)
				{
					w.Write((short)Mathf.Clamp(x[i] * 32767f, -32768f, 32767f));
				}
			}
		}
	}
}
