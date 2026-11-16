using System;
using System.IO;
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
            else if (!File.Exists(bundlePath))
            {
                failure = "bundle not found: " + bundlePath;
            }
            else
            {
                AssetBundle bundle = AssetBundle.LoadFromFile(bundlePath);
                if (bundle == null)
                {
                    failure = "bundle failed to load";
                }
                else
                {
                    Shader[] all = bundle.LoadAllAssets<Shader>();
                    for (int i = 0; i < all.Length; i++)
                    {
                        if (all[i] != null && all[i].name == "KK/Dread") { shader = all[i]; break; }
                    }
                    bundle.Unload(false);
                    if (shader == null) failure = "bundle has no KK/Dread shader";
                    else if (!shader.isSupported) { failure = "KK/Dread is not supported on this GPU"; shader = null; }
                }
            }
        }
        catch (Exception ex)
        {
            failure = ex.GetType().Name + ": " + ex.Message;
            shader = null;
        }
        if (shader == null) Debug.Log("[KK Dread] screen effect off: " + failure);
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
            Debug.Log("[KK Dread] screen pass attached to '" + cam.name + "' via " + (useCommandBuffer ? "commandBuffer (does not show in KSP 1.12)" : "onRenderImage")
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
        Graphics.Blit(source, destination, material);
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
