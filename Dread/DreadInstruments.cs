using System;
using System.Collections.Generic;
using KSP.UI.Screens;
using KSP.UI.Screens.Flight;
using UnityEngine;
using UnityEngine.UI;

// Dread instruments: portrait static, navball and altimeter jitter (display only, never real data), IVA light dips.
// Runs in LateUpdate after the game's own scripts. Everything it changes is remembered and given back exactly.
[DefaultExecutionOrder(5000)]
public sealed class DreadInstrumentsHost : MonoBehaviour
{
    // Written by EntityTracker once per frame (game thread, same thread as LateUpdate).
    public bool EnablePortraits, EnableJitter, EnableIvaLights, InIva;
    public bool EnableNavballJitter = true;   // false: the navball always shows true attitude, clean (it is flown by)
    public bool PortraitStatic;
    public float JitterAmount, IvaDip;

    public bool Faulted;

    private readonly DreadJitter jitter = new DreadJitter();

    private sealed class PortraitSave
    {
        public Texture texture;
        public Rect uv;
        public Texture overlay;
    }

    private readonly Dictionary<RawImage, PortraitSave> portraits = new Dictionary<RawImage, PortraitSave>();
    private float portraitTimer;
    private NavBall navBall;

    private readonly DreadScaleBook sunBook = new DreadScaleBook();
    private readonly Dictionary<Light, DreadScaleBook> lightBooks = new Dictionary<Light, DreadScaleBook>();
    private readonly List<Light> lightScratch = new List<Light>();

    private void LateUpdate()
    {
        if (Faulted) return;
        if (Time.timeScale <= 0f) return;   // paused: navball, portraits and cabin lights hold still
        try
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            DoPortraits(dt);
            DoJitter(dt);
            DoIvaLights(dt);
        }
        catch (Exception ex)
        {
            Faulted = true;
            Debug.Log("[UT Dread] instruments disabled: " + ex.GetType().Name + ": " + ex.Message);
            try { RestoreAll(); } catch (Exception) { }
        }
    }

    // ---- portraits ----

    private void DoPortraits(float dt)
    {
        if (!(EnablePortraits && PortraitStatic))
        {
            if (portraits.Count > 0) RestorePortraits();
            return;
        }
        KerbalPortraitGallery gallery = KerbalPortraitGallery.Instance;
        if (gallery == null) { RestorePortraits(); return; }
        portraitTimer -= dt;
        bool tick = portraitTimer <= 0f;
        if (tick) portraitTimer = 0.07f;
        List<KerbalPortrait> list = gallery.Portraits;
        if (list == null) return;
        for (int i = 0; i < list.Count; i++)
        {
            KerbalPortrait kp = list[i];
            if (kp == null || kp.portrait == null || kp.overlayImg == null) continue;
            RawImage img = kp.portrait;
            PortraitSave save;
            if (!portraits.TryGetValue(img, out save))
            {
                if (img.texture == kp.overlayImg) continue;   // the game is already showing its own static
                save = new PortraitSave { texture = img.texture, uv = img.uvRect, overlay = kp.overlayImg };
                portraits[img] = save;
                tick = true;
            }
            if (tick || img.texture != save.overlay)
            {
                img.texture = save.overlay;
                int col = UnityEngine.Random.Range(0, 3);
                img.uvRect = new Rect(col / 3f, 2f / 3f, 0.3334f, 0.3334f);
            }
        }
    }

    private void RestorePortraits()
    {
        foreach (KeyValuePair<RawImage, PortraitSave> kv in portraits)
        {
            RawImage img = kv.Key;
            if (img == null) continue;
            if (img.texture == kv.Value.overlay)
            {
                img.texture = kv.Value.texture;
                img.uvRect = kv.Value.uv;
            }
        }
        portraits.Clear();
    }

    // ---- navball / altimeter ----

    private void DoJitter(float dt)
    {
        float amount = EnableJitter ? JitterAmount : 0f;
        jitter.Update(dt, amount);
        if (!(amount > 0f)) return;
        if (navBall == null) navBall = UnityEngine.Object.FindObjectOfType<NavBall>();
        if (EnableNavballJitter && navBall != null && navBall.navBall != null)
        {
            navBall.navBall.rotation = navBall.navBall.rotation * Quaternion.Euler(jitter.NavX, jitter.NavY, jitter.NavZ);
        }
        Vessel v = FlightGlobals.ActiveVessel;
        AltitudeTumbler at = AltitudeTumbler.Instance;
        if (v != null && at != null && at.tumbler != null && FlightGlobals.ready)
        {
            double value = (at.CurrentMode == AltimeterDisplayState.AGL) ? v.radarAltitude : v.altitude * AltitudeTumbler.altitudeMultiplier;
            at.tumbler.SetValue(value * (1.0 + jitter.AltRel) + jitter.AltAbs);
        }
    }

    // ---- IVA lights ----

    private void DoIvaLights(float dt)
    {
        float dip = (EnableIvaLights && InIva) ? IvaDip : 0f;
        jitter.UpdateFlicker(dt, dip);
        float factor = DreadMapping.IvaLightFactor(dip, jitter.FlickerState);
        IVASun sun = null;
        if (InternalSpace.Instance != null)
        {
            Transform t = InternalSpace.Instance.transform.Find("IVASun");
            if (t != null) sun = t.GetComponent<IVASun>();
        }
        if (sun != null)
        {
            float v;
            if (sunBook.Apply(sun.intensityScalar, factor, out v)) sun.intensityScalar = v;
        }
        else
        {
            sunBook.Active = false;
        }
        Vessel vessel = FlightGlobals.ActiveVessel;
        if (dip > 0f && vessel != null && vessel.parts != null)
        {
            for (int i = 0; i < vessel.parts.Count; i++)
            {
                Part part = vessel.parts[i];
                if (part == null || part.internalModel == null) continue;
                lightScratch.Clear();
                part.internalModel.GetComponentsInChildren<Light>(false, lightScratch);
                for (int j = 0; j < lightScratch.Count; j++)
                {
                    Light l = lightScratch[j];
                    if (l == null) continue;
                    DreadScaleBook book;
                    if (!lightBooks.TryGetValue(l, out book)) { book = new DreadScaleBook(); lightBooks[l] = book; }
                    float nv;
                    if (book.Apply(l.intensity, factor, out nv)) l.intensity = nv;
                }
            }
        }
        else if (lightBooks.Count > 0)
        {
            ReleaseLights();
        }
    }

    private void ReleaseLights()
    {
        foreach (KeyValuePair<Light, DreadScaleBook> kv in lightBooks)
        {
            if (kv.Key == null) continue;
            float v;
            if (kv.Value.Release(kv.Key.intensity, out v)) kv.Key.intensity = v;
        }
        lightBooks.Clear();
    }

    /// <summary>Gives every changed value back. Safe to call repeatedly.</summary>
    public void RestoreAll()
    {
        RestorePortraits();
        ReleaseLights();
        InternalSpace space = InternalSpace.Instance;
        if (sunBook.Active && space != null)
        {
            Transform t = space.transform.Find("IVASun");
            IVASun sun = t != null ? t.GetComponent<IVASun>() : null;
            float v;
            if (sun != null && sunBook.Release(sun.intensityScalar, out v)) sun.intensityScalar = v;
        }
        sunBook.Active = false;
        jitter.Update(0f, 0f);
        jitter.UpdateFlicker(0f, 0f);
    }

    private void OnDestroy()
    {
        try { RestoreAll(); } catch (Exception) { }
    }
}
