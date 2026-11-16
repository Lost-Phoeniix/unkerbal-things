using System;
using System.Reflection;
using UnkerbalThings.Dread;
using UnityEngine;

// Dread audio: one OnAudioFilterRead host on a persistent object, the music duck and the listener low-pass.
// The audio thread touches only the synth (pure C#) and two volatile fields; no Unity API is called from it.
public sealed class DreadAudioHost : MonoBehaviour
{
    private volatile DreadSynth synth;
    private volatile DreadStepVoice steps;
    private volatile DreadSoundMixer mixer;
    private float[][] stepClips;
    private int stepClipRate = 48000;
    private volatile bool running;
    private volatile bool faulted;
    private AudioSource source;
    private AudioClip clip;
    private int rate;
    private int seed = 1;
    private bool configHooked;

    public bool Faulted { get { return faulted; } }
    public DreadSynth Synth { get { return synth; } }
    public DreadStepVoice Steps { get { return steps; } }
    public DreadSoundMixer Mixer { get { return mixer; } }
    public bool SourcePlaying { get { return source != null && source.isPlaying; } }

    /// <summary>Creates the source and the synth. Sample rate comes from the audio system.</summary>
    public void Init(int synthSeed, string stepClipPath)
    {
        seed = synthSeed;
        stepClips = LoadStepClips(stepClipPath, out stepClipRate);
        if (stepClips == null) Debug.Log("[UT Dread] step clip not found, using the built-in one: " + stepClipPath);
        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = true;
        source.spatialBlend = 0f;
        source.priority = 0;
        source.volume = 1f;
        BuildSynth();
        if (!configHooked)
        {
            configHooked = true;
            AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
        }
    }

    // step-01.wav, step-02.wav ... next to step.wav; step.wav alone if they are not there
    private static float[][] LoadStepClips(string stepClipPath, out int clipRate)
    {
        System.Collections.Generic.List<float[]> list = new System.Collections.Generic.List<float[]>();
        clipRate = 48000;
        for (int i = 1; i <= 16; i++)
        {
            int r;
            float[] c = DreadStepVoice.LoadWav(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(stepClipPath), "step-" + i.ToString("00") + ".wav"), out r);
            if (c == null) break;
            clipRate = r;
            list.Add(c);
        }
        if (list.Count == 0)
        {
            float[] one = DreadStepVoice.LoadWav(stepClipPath, out clipRate);
            if (one != null) list.Add(one);
        }
        return list.Count > 0 ? list.ToArray() : null;
    }

    private void BuildSynth()
    {
        rate = AudioSettings.outputSampleRate;
        if (rate < 8000) rate = 48000;
        DreadSynth s = new DreadSynth(rate, seed);
        s.AutoEvents = false;          // the director decides every event
        s.ThrustCorrelation = false;
        s.MasterLevel = 0f;
        s.DroneLevel = 0f;
        synth = s;
        steps = new DreadStepVoice(rate, stepClips, stepClipRate);
        mixer = new DreadSoundMixer(rate);
        if (clip != null) { source.Stop(); UnityEngine.Object.Destroy(clip); clip = null; }
        // A looping mono clip of ones: only there so Unity runs the filter; Render overwrites every sample.
        clip = AudioClip.Create("KK Dread host", Mathf.Max(rate / 4, 1024), 1, rate, false);
        float[] ones = new float[Mathf.Max(rate / 4, 1024)];
        for (int i = 0; i < ones.Length; i++) ones[i] = 1f;
        clip.SetData(ones, 0);
        source.clip = clip;
        source.loop = true;
        running = true;
        source.Play();
    }

    private void OnAudioConfigurationChanged(bool deviceWasChanged)
    {
        try
        {
            if (this != null && source != null && AudioSettings.outputSampleRate != rate) BuildSynth();
            else if (this != null && source != null && !source.isPlaying) source.Play();
        }
        catch (Exception ex)
        {
            Debug.Log("[UT Dread] audio rebuild failed: " + ex.Message);
            faulted = true;
        }
    }

    /// <summary>Game-thread volume from the game's own sliders (ambience, via the source).</summary>
    public void SetSourceVolume(float v)
    {
        if (source != null) source.volume = Mathf.Clamp01(v);
    }

    public void SetRunning(bool on) { running = on && !faulted; }

    // Audio thread. No Unity API here.
    private void OnAudioFilterRead(float[] data, int channels)
    {
        DreadSynth s = synth;
        if (s == null || !running || faulted)
        {
            if (data != null) Array.Clear(data, 0, data.Length);
            return;
        }
        try
        {
            s.Render(data, channels);
            DreadStepVoice v = steps;
            if (v != null) v.MixInto(data, channels);
            DreadSoundMixer m = mixer;
            if (m != null) m.MixInto(data, channels);
        }
        catch (Exception)
        {
            faulted = true;
            Array.Clear(data, 0, data.Length);
        }
    }

    private void OnDestroy()
    {
        running = false;
        if (configHooked) { AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged; configHooked = false; }
        if (clip != null) { UnityEngine.Object.Destroy(clip); clip = null; }
    }
}

/// <summary>Ducks the game's music through MusicLogic.SetVolume and restores the exact values. If the game changes the music
/// volume while ducked (settings dialog), the new value becomes the one to restore.</summary>
public sealed class DreadMusicDuck
{
    private static FieldInfo field1, field2;
    private static bool reflected;
    private readonly DreadScaleBook book1 = new DreadScaleBook();
    private readonly DreadScaleBook book2 = new DreadScaleBook();

    public bool Active { get { return book1.Active || book2.Active; } }

    private static void Reflect()
    {
        if (reflected) return;
        reflected = true;
        try
        {
            field1 = typeof(MusicLogic).GetField("setVolume1", BindingFlags.Static | BindingFlags.NonPublic);
            field2 = typeof(MusicLogic).GetField("setVolume2", BindingFlags.Static | BindingFlags.NonPublic);
        }
        catch (Exception) { field1 = null; field2 = null; }
    }

    private static void Read(out float v1, out float v2)
    {
        Reflect();
        v1 = v2 = GameSettings.MUSIC_VOLUME;
        if (field1 != null && field2 != null)
        {
            try { v1 = (float)field1.GetValue(null); v2 = (float)field2.GetValue(null); }
            catch (Exception) { v1 = v2 = GameSettings.MUSIC_VOLUME; }
        }
    }

    public void Update(float gain)
    {
        if (MusicLogic.fetch == null) return;
        float c1, c2;
        Read(out c1, out c2);
        float n1, n2;
        bool w1 = book1.Apply(c1, gain, out n1);
        bool w2 = book2.Apply(c2, gain, out n2);
        if (w1 || w2) MusicLogic.SetVolume(w1 ? n1 : c1, w2 ? n2 : c2);
    }

    public void Release()
    {
        if (!Active) return;
        if (MusicLogic.fetch != null)
        {
            float c1, c2, n1, n2;
            Read(out c1, out c2);
            bool w1 = book1.Release(c1, out n1);
            bool w2 = book2.Release(c2, out n2);
            if (w1 || w2) MusicLogic.SetVolume(w1 ? n1 : c1, w2 ? n2 : c2);
        }
        book1.Active = false;
        book2.Active = false;
    }
}

/// <summary>AudioLowPassFilter on the listener's object, cutoff lerped, removed again at zero menace.</summary>
public sealed class DreadListenerLowpass
{
    private AudioLowPassFilter filter;
    private GameObject owner;
    private float cutoff = DreadMapping.ListenerClosedHz;

    public bool Active { get { return filter != null; } }

    public void Update(float dt, float target)
    {
        if (target >= DreadMapping.ListenerClosedHz - 1f && cutoff >= DreadMapping.ListenerClosedHz - 5f)
        {
            Release();
            return;
        }
        GameObject go = FindListenerObject();
        if (go == null) { Release(); return; }
        if (filter == null || owner != go)
        {
            Release();
            owner = go;
            filter = go.GetComponent<AudioLowPassFilter>();
            if (filter != null) { filter = null; owner = null; return; }   // somebody else owns one there: leave it alone
            filter = go.AddComponent<AudioLowPassFilter>();
            filter.lowpassResonanceQ = 1f;
            filter.cutoffFrequency = cutoff;
        }
        // lerp in the log domain so the sweep sounds even
        float k = 1f - Mathf.Exp(-Mathf.Max(dt, 0f) / 1.5f);
        float lc = Mathf.Log(cutoff), lt = Mathf.Log(Mathf.Max(target, 200f));
        cutoff = Mathf.Exp(lc + (lt - lc) * k);
        if (cutoff > DreadMapping.ListenerClosedHz) cutoff = DreadMapping.ListenerClosedHz;
        filter.cutoffFrequency = cutoff;
    }

    private static GameObject FindListenerObject()
    {
        if (FlightCamera.fetch != null && FlightCamera.fetch.AudioListenerGameObject != null) return FlightCamera.fetch.AudioListenerGameObject;
        AudioListener l = UnityEngine.Object.FindObjectOfType<AudioListener>();
        return l != null ? l.gameObject : null;
    }

    public void Release()
    {
        if (filter != null) UnityEngine.Object.Destroy(filter);
        filter = null;
        owner = null;
        cutoff = DreadMapping.ListenerClosedHz;
    }
}
