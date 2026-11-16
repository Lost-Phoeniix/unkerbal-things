using System;
using UnityEngine;
using UnityEngine.Rendering;

// Dread: the KK/Dread screen pass on one camera. D3D11 only. Any problem means no effect and one log line, never an error per frame.
// Default attach method: OnRenderImage on the flight camera ("Camera 00"), measured in KSP 1.12 D3D11 (full-menace screen change 0.12 mean).
// Alternative method (setting dreadScreenMethod = commandBuffer): a CommandBuffer at CameraEvent.AfterImageEffects. It attaches and its uniforms are
// set, but in-game it changed the picture by noise level only (0.0005 at full menace), so it is kept only for experiments.

public sealed class DreadCamera
{
    private static Shader shader;
    private static bool loadTried;
    private static string failure;

    private Material material;
    private Camera camera;
    private CommandBuffer buffer;
    private DreadImageEffect imageEffect;
    private RenderTexture freezeTexture;
    private bool bufferHasCapture;
    private bool useCommandBuffer = true;
    private int tempId;

    public bool ShaderReady { get { return shader != null; } }
    public bool Attached { get { return camera != null && (buffer != null || imageEffect != null); } }
    public Camera Target { get { return camera; } }
    public static string Failure { get { return failure; } }

    /// <summary>Loads the bundle once per session. Returns false (and logs one line) when the effect cannot run.</summary>
    public static bool EnsureShader(string bundlePath)
    {
        if (shader != null) return true;
        if (loadTried) return false;
        loadTried = true;
        try
        {
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D11)
            {
                failure = "graphics API is " + SystemInfo.graphicsDeviceType + ", the Dread shader bundle is D3D11 only";
            }
            else
            {
                shader = KKShaderBundle.Find("KK/Dread", bundlePath);
                if (shader == null) failure = KKShaderBundle.Failure ?? "bundle has no KK/Dread shader";
                else if (!shader.isSupported) { failure = "KK/Dread is not supported on this GPU"; shader = null; }
            }
        }
        catch (Exception ex)
        {
            failure = ex.GetType().Name + ": " + ex.Message;
            shader = null;
        }
        if (shader == null) Debug.Log("[UT Dread] screen effect off: " + failure);
        return shader != null;
    }

    public void SetMethod(bool commandBuffer)
    {
        if (commandBuffer != useCommandBuffer) { Detach(); useCommandBuffer = commandBuffer; }
    }

    /// <summary>Attaches to the given camera (null detaches). Cheap when the camera is unchanged.</summary>
    public void SetCamera(Camera cam)
    {
        if (cam == camera && Attached) return;
        Detach();
        if (cam == null || shader == null) return;
        if (material == null)
        {
            material = new Material(shader);
            material.hideFlags = HideFlags.HideAndDontSave;
        }
        camera = cam;
        if (useCommandBuffer)
        {
            tempId = Shader.PropertyToID("_KKDreadTemp");
            buffer = new CommandBuffer();
            buffer.name = "KK Dread";
            bufferHasCapture = false;
            BuildBuffer(false);
            camera.AddCommandBuffer(CameraEvent.AfterImageEffects, buffer);
        }
        else
        {
            imageEffect = cam.gameObject.AddComponent<DreadImageEffect>();
            imageEffect.Owner = this;
        }
        if (attachLogs < 4)
        {
            attachLogs++;
            Debug.Log("[UT Dread] screen pass attached to '" + cam.name + "' via " + (useCommandBuffer ? "commandBuffer (does not show in KSP 1.12)" : "onRenderImage")
                + ", target " + (cam.targetTexture != null ? cam.targetTexture.name : "screen") + ", hdr " + cam.allowHDR + ", msaa " + cam.allowMSAA + ", " + Screen.width + "x" + Screen.height
                + ", camera components: " + CameraComponents(cam));
        }
    }

    private static int attachLogs;

    private static string CameraComponents(Camera cam)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        foreach (Component k in cam.GetComponents<Component>())
        {
            if (k != null && !(k is Transform) && !(k is Camera)) sb.Append(k.GetType().Name).Append(' ');
        }
        return sb.ToString().Trim();
    }

    private void BuildBuffer(bool capture)
    {
        buffer.Clear();
        buffer.GetTemporaryRT(tempId, -1, -1, 0, FilterMode.Bilinear, RenderTextureFormat.DefaultHDR);
        buffer.Blit(BuiltinRenderTextureType.CurrentActive, tempId);
        if (capture && freezeTexture != null)
        {
            buffer.Blit(tempId, new RenderTargetIdentifier(freezeTexture));
        }
        buffer.Blit(tempId, BuiltinRenderTextureType.CurrentActive, material);
        buffer.ReleaseTemporaryRT(tempId);
        bufferHasCapture = capture && freezeTexture != null;
    }

    private void EnsureFreezeTexture()
    {
        if (camera == null) return;
        int w = Mathf.Max(16, camera.pixelWidth), h = Mathf.Max(16, camera.pixelHeight);
        if (freezeTexture != null && (freezeTexture.width != w || freezeTexture.height != h))
        {
            ReleaseFreezeTexture();
        }
        if (freezeTexture == null)
        {
            freezeTexture = new RenderTexture(w, h, 0, RenderTextureFormat.DefaultHDR);
            freezeTexture.name = "KK Dread freeze";
            freezeTexture.filterMode = FilterMode.Bilinear;
            freezeTexture.Create();
        }
    }

    private void ReleaseFreezeTexture()
    {
        if (freezeTexture != null)
        {
            if (RenderTexture.active == freezeTexture) RenderTexture.active = null;
            freezeTexture.Release();
            UnityEngine.Object.Destroy(freezeTexture);
            freezeTexture = null;
        }
    }

    /// <summary>Sets the uniforms for the next frame. captureFrame stores that frame as the frozen picture.</summary>
    public void Apply(in DreadScreenParams p, bool captureFrame)
    {
        if (!Attached || material == null) return;
        if (camera == null) { Detach(); return; }
        if (captureFrame) EnsureFreezeTexture();
        if (buffer != null)
        {
            bool wantCapture = captureFrame && freezeTexture != null;
            if (wantCapture != bufferHasCapture) BuildBuffer(wantCapture);
        }
        pendingCapture = captureFrame;
        Material m = material;
        m.SetFloat("_Strength", p.Strength);
        m.SetFloat("_Grade", p.Grade);
        m.SetFloat("_Vignette", p.Vignette);
        m.SetFloat("_Grain", p.Grain);
        m.SetFloat("_Chroma", p.Chroma);
        m.SetFloat("_SignalNoise", p.SignalNoise);
        m.SetFloat("_Tear", p.Tear);
        m.SetFloat("_TearY", p.TearY);
        m.SetFloat("_TearH", p.TearH);
        m.SetFloat("_Block", p.Block);
        m.SetFloat("_Freeze", p.Freeze);
        m.SetFloat("_Sync", p.Sync);
        m.SetFloat("_TrackY", p.TrackY);
        m.SetFloat("_Loss", p.Loss);
        m.SetFloat("_Dark", p.Dark);
        m.SetFloat("_Seed", p.Seed);
        m.SetFloat("_Tube", p.Tube);
        m.SetFloat("_Blow", p.Blow);
        m.SetFloat("_Snow", p.Snow);
        m.SetFloat("_Wear", p.Wear);
        m.SetFloat("_Seq", p.Seq);
        seqWanted = p.Seq > 0f;
        m.SetTexture("_FreezeTex", freezeTexture != null ? (Texture)freezeTexture : (Texture)Texture2D.blackTexture);
    }

    private bool pendingCapture;

    /// <summary>The uniforms as the material holds them (diagnostics for the test scene and the log).</summary>
    public string DescribeUniforms()
    {
        if (material == null) return "no material";
        Material m = material;
        return "S " + m.GetFloat("_Strength").ToString("F2") + " grade " + m.GetFloat("_Grade").ToString("F3") + " vig " + m.GetFloat("_Vignette").ToString("F3")
            + " grain " + m.GetFloat("_Grain").ToString("F3") + " signal " + m.GetFloat("_SignalNoise").ToString("F3") + " sync " + m.GetFloat("_Sync").ToString("F3") + " loss " + m.GetFloat("_Loss").ToString("F3") + " dark " + m.GetFloat("_Dark").ToString("F3") + " tear " + m.GetFloat("_Tear").ToString("F3") + " block " + m.GetFloat("_Block").ToString("F3") + " freeze " + m.GetFloat("_Freeze").ToString("F3") + " seed " + m.GetFloat("_Seed").ToString("F0");
    }

    // Fallback path, called by DreadImageEffect.
    internal void RenderImage(RenderTexture source, RenderTexture destination)
    {
        if (material == null) { Graphics.Blit(source, destination); return; }
        if (pendingCapture && freezeTexture != null) Graphics.Blit(source, freezeTexture);
        if (seqWanted) BindHistory(source);
        Graphics.Blit(source, destination, material);

    }

    // ---- field-sequential colour camera (the imager feed, in the style of the Apollo television pictures).
    // Apollo 10-17 colour TV (Westinghouse, then RCA): one monochrome tube behind a colour wheel with six filter segments turning at
    // 9.99 rev/s, so 59.94 fields per second, each field one colour (sources: Wikipedia "Apollo TV camera"; NASA TN D-7476, Coan 1973).
    // In Houston each red, blue and green field was stored on a magnetic disk recorder used as a frame buffer and combined in real
    // time into NTSC colour (Wikipedia; Lebar 1997), so every output picture holds the newest field plus the last field of each other
    // colour: the three colours are taken 1/60 s apart, and anything that moves (or a panning camera) splits into colour fringes.
    // The field order of the Apollo wheel is not documented; the CBS system it was adapted from ran red, blue, green (Wetmore 1969),
    // which is used here. The field clock runs on real time, independent of the game's frame rate: at each new field the scene as
    // rendered now is stored as that field's colour plane (at low frame rates several fields fall on one frame and share it).
    private bool seqWanted;
    private RenderTexture planeR, planeG, planeB, agcTex;
    private long lastField = long.MinValue;
    private int planeFrame = -10;
    public const double FieldsPerSecond = 59.94;
    private static readonly int[] FieldOrder = { 0, 2, 1 };   // R, B, G (0 = R, 1 = G, 2 = B)
    private static readonly System.Diagnostics.Stopwatch FieldClock = System.Diagnostics.Stopwatch.StartNew();   // real time, double precision

    private void BindHistory(RenderTexture source)
    {
        int w = source.width, h = source.height;
        if (planeR == null || planeR.width != w || planeR.height != h) { ReleaseHistory(); planeR = NewHist(w, h, "R"); planeG = NewHist(w, h, "G"); planeB = NewHist(w, h, "B"); }
        long field = (long)Math.Floor(FieldClock.Elapsed.TotalSeconds * FieldsPerSecond);
        bool fresh = Time.frameCount - planeFrame > 2 || lastField == long.MinValue || field < lastField;
        if (fresh)
        {
            // first frame or a gap (the feed was off): every plane starts from the current picture, no stale ghost
            Graphics.Blit(source, planeR); Graphics.Blit(source, planeG); Graphics.Blit(source, planeB);
        }
        else
        {
            // each field that began since the last frame captures the current picture into its colour's plane (at most three
            // matter: older ones would be overwritten by newer fields of the same colour)
            long from = Math.Max(lastField + 1, field - 2);
            for (long f = from; f <= field; f++)
            {
                int c = FieldOrder[(int)(((f % 3) + 3) % 3)];
                Graphics.Blit(source, c == 0 ? planeR : c == 1 ? planeG : planeB);
            }
        }
        lastField = field;
        planeFrame = Time.frameCount;
        // camera gain: the frame's mean brightness from the 1x1 mip of a small copy (the shader reads mip 9 of 256x144)
        if (agcTex == null)
        {
            agcTex = new RenderTexture(256, 144, 0, RenderTextureFormat.ARGB32);
            agcTex.name = "KK Dread AGC";
            agcTex.useMipMap = true;
            agcTex.autoGenerateMips = true;
            agcTex.filterMode = FilterMode.Trilinear;
            agcTex.Create();
        }
        Graphics.Blit(source, agcTex);
        // the gain follows that mean slowly, as a real AGC loop does (it changed too quickly when it reacted
        // in the same frame). The 1x1 mip is read back without stalling; the shader gets the smoothed mean as _AgcAvg.
        if (!agcPending)
        {
            agcPending = true;
            AsyncGPUReadback.Request(agcTex, agcTex.mipmapCount - 1, TextureFormat.RGBA32, OnAgc);
        }
        float dt = Mathf.Clamp(Time.unscaledDeltaTime, 0f, 0.25f);
        if (agcAvg < 0f) agcAvg = agcTarget >= 0f ? agcTarget : 0.16f;
        else if (agcTarget >= 0f) agcAvg += (agcTarget - agcAvg) * (1f - Mathf.Exp(-dt / AgcSeconds));
        material.SetFloat("_AgcAvg", agcAvg);
        material.SetTexture("_FieldR", planeR);
        material.SetTexture("_FieldG", planeG);
        material.SetTexture("_FieldB", planeB);
    }

    private bool agcPending;
    private float agcTarget = -1f, agcAvg = -1f;
    /// <summary>Time constant of the camera gain, seconds (a choice: the Apollo AGC's loop time is not documented).</summary>
    public const float AgcSeconds = 1.2f;

    private void OnAgc(AsyncGPUReadbackRequest r)
    {
        agcPending = false;
        if (r.hasError || r.done == false) return;
        try
        {
            var px = r.GetData<Color32>();
            if (px.Length < 1) return;
            Color32 c = px[0];
            float l = (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) / 255f;
            agcTarget = Mathf.Pow(Mathf.Max(l, 1e-3f), 2.2f);
        }
        catch (Exception) { }
    }

    private static RenderTexture NewHist(int w, int h, string n)
    {
        RenderTexture r = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32);
        r.name = "KK Dread field " + n;
        r.filterMode = FilterMode.Bilinear;
        r.Create();
        return r;
    }

    private void ReleaseHistory()
    {
        foreach (RenderTexture r in new[] { planeR, planeG, planeB, agcTex })
        {
            if (r == null) continue;
            if (RenderTexture.active == r) RenderTexture.active = null;
            r.Release();
            UnityEngine.Object.Destroy(r);
        }
        planeR = planeG = planeB = agcTex = null;
        agcTarget = agcAvg = -1f;
        lastField = long.MinValue;
        if (material != null) { material.SetFloat("_Seq", 0f); }
    }

    public void Detach()
    {
        if (camera != null && buffer != null)
        {
            try { camera.RemoveCommandBuffer(CameraEvent.AfterImageEffects, buffer); } catch (Exception) { }
        }
        if (buffer != null) { buffer.Release(); buffer = null; }
        if (imageEffect != null) { imageEffect.Owner = null; UnityEngine.Object.Destroy(imageEffect); imageEffect = null; }
        camera = null;
        bufferHasCapture = false;
        pendingCapture = false;
        ReleaseFreezeTexture();
        ReleaseHistory();
        seqWanted = false;
    }

    public void Dispose()
    {
        Detach();
        if (material != null) { UnityEngine.Object.Destroy(material); material = null; }
    }
}

public sealed class DreadImageEffect : MonoBehaviour
{
    internal DreadCamera Owner;

    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        DreadCamera owner = Owner;
        if (owner == null) { Graphics.Blit(source, destination); return; }
        owner.RenderImage(source, destination);
    }
}
