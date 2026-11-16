using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using KSP.UI.Screens;
using UnityEngine;
using UnityEngine.Rendering;

// The creature tracker: spawns, drives and removes the two creatures, the Moho hunter and the Laythe leviathan, and owns the toolbar window.
// The original mod's other monsters and its approaching "box" signal live in legacy/original-monsters (reference only, not built).
[KSPAddon(KSPAddon.Startup.Flight, false)]
public partial class EntityTracker : MonoBehaviour
{
	private const string ModFolderName = "UnkerbalThings";

	private const float BulkSpawnMinimumMetres = 6000f;

	private const float BulkSpawnMaximumMetres = 18000f;

	private const float BulkActivationMetres = 16000f;

	private const float BulkArrivalDistanceMetres = 8f;

	private const float BulkChaseSpeedMetresPerSecond = 42f;

	private const float BulkVisualScale = 2.2f;

	private const float BulkEscapeDistanceMetres = 10000f;

	private const float BloopSpawnMinimumMetres = 3000f;

	private const float BloopSpawnMaximumMetres = 10000f;

	private const float BloopActivationMetres = 8000f;

	private const float BloopDepthMetres = 70f;

	private static EntityTracker instance;

	private bool randomEncounters = true;

	private bool debugTools = false;

	private bool bdArmoryCompatibility = true;

	private bool bdArmoryGpsTargets = true;

	private bool bdArmoryGroundRadarTargets = true;

	private bool bdArmoryWeaponDamage = true;

	private BDArmoryGpsBridge bdArmoryGpsBridge;

	private BDArmoryGroundRadarBridge bdArmoryGroundRadarBridge;

	private BDArmoryDamageBridge bdArmoryDamageBridge;

	private float randomCheckSeconds = 45f;

	private float randomCooldownSeconds = 1800f;

	private float bulkEncounterChance = 0.2f;

	private float bulkEncounterCooldownSeconds = 600f;

	private float bulkEncounterCooldownTimer;

	private GameObject bulkEntityObject;

	private CelestialBody bulkEntityBody;

	private Vector3d bulkEntityRelativePosition;

	private bool bulkEntityActive;

	private bool bulkEntityChasing;

	private readonly HashSet<Guid> bulkSeenVesselIds = new HashSet<Guid>();

	private bool bulkEntityStandingDown;

	private bool bulkEntityRetreating;

	private float bulkWithdrawalTimer;

	private float bulkChaseAnimationPhase;

	private string bulkEntityStatus = "NONE";

	private float bloopEncounterChance = 0.14f;

	private float bloopEncounterCooldownSeconds = 900f;

	private float bloopEncounterCooldownTimer;

	private GameObject bloopEntityObject;

	private CelestialBody bloopEntityBody;

	private Vector3d bloopEntityRelativePosition;

	private bool bloopEntityActive;

	private bool bloopEntityChasing;

	private string bloopEntityStatus = "NONE";


	private bool groundEntityDebugRadar;

	private bool debugDisableWaterFog;

	private bool debugImmortality;

	private bool fogStateCaptured;

	private bool fogEnabledBeforeDebug;

	private ApplicationLauncherButton toolbarButton;

	private Texture2D toolbarIcon;

	private bool toolbarVisible;

	private Rect toolbarPanel = new Rect(60f, 80f, 460f, 320f);

	private Vector2 toolbarScrollPosition;

	private float encounterCheckTimer;

	private int windowId;

	public void Awake()
	{
		if (instance != null && instance != this)
		{
			UnityEngine.Object.Destroy(base.gameObject);
			return;
		}
		instance = this;
		windowId = Guid.NewGuid().GetHashCode();
		LoadSettings();
		UnityEngine.Object.DontDestroyOnLoad(base.gameObject);
		GameEvents.onGUIApplicationLauncherReady.Add(AddToolbarButton);
		GameEvents.onGUIApplicationLauncherDestroyed.Add(RemoveToolbarButton);
		GameEvents.onGameStateLoad.Add(OnGameStateLoaded);
	}

	public void Start()
	{
		if (ApplicationLauncher.Ready)
		{
			AddToolbarButton();
		}
	}

	// Ctrl+Alt+U opens and closes the testing window in flight, also when debugTools (the toolbar button) is off (debugHotkey).
	private bool debugHotkey = true;
	private bool debugWindowEnvChecked;

	private void UpdateDebugWindowKey()
	{
		if (!HighLogic.LoadedSceneIsFlight) return;
		if (!debugWindowEnvChecked)
		{
			// the unattended test driver cannot press keys: KK_TEST_DEBUGWIN=1 opens the window for its screenshots
			debugWindowEnvChecked = true;
			if (Environment.GetEnvironmentVariable("KK_TEST_DEBUGWIN") == "1") toolbarVisible = true;
		}
		if (debugHotkey && Input.GetKeyDown(KeyCode.U) && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)))
		{
			toolbarVisible = !toolbarVisible;
			if (toolbarButton != null)
			{
				// keep the toolbar button's pressed state in step (without firing its callbacks again)
				if (toolbarVisible) toolbarButton.SetTrue(false); else toolbarButton.SetFalse(false);
			}
		}
	}

	public void Update()
	{
		UpdateDebugWindowKey();
		UpdateDebugFogOverride();
		if (bulkEntityActive && !HighLogic.LoadedSceneIsFlight)
		{
			DespawnBulkEntity();
		}
		if (bloopEntityActive && !HighLogic.LoadedSceneIsFlight)
		{
			DespawnBloopEntity();
		}
		UpdateWormAlwaysSpawn();
		UpdateRandomEncounters();
		UpdateAttackSequences();
		UpdateScares();
		UpdateBulkEntity();
		UpdateBloopEntity();
		UpdateBDArmoryGpsTargets();
		UpdateBDArmoryGroundRadarTargets();
		UpdateBDArmoryWeaponDamage();
		UpdateDread();
	}

	private static bool IsOrbital(Vessel vessel)
	{
		return vessel != null && (vessel.situation == Vessel.Situations.ORBITING || vessel.situation == Vessel.Situations.ESCAPING || vessel.situation == Vessel.Situations.SUB_ORBITAL);
	}

	private static float GetCappedSpawnCheckDelta()
	{
		return Mathf.Min(TimeWarp.deltaTime, Time.unscaledDeltaTime * 10f);
	}

	public void OnGUI()
	{
		if (!HighLogic.LoadedSceneIsFlight)
		{
			return;
		}
		DrawViewLockHud();
		if (toolbarVisible)
		{
			DrawDebugWindow();
		}
		if (groundEntityDebugRadar && bulkEntityActive)
		{
			DrawBulkEntityRadar();
		}
		if (groundEntityDebugRadar && bloopEntityActive)
		{
			DrawBloopEntityRadar();
		}
		if (groundEntityDebugRadar)
		{
			DrawSpawnDebugPanel();
		}
	}

	// ---------------------------------------------------------------- debug window (debugTools)
	// Sized to its content (no scrolling until it is taller than the screen), in sections that fold. Buttons that cannot work
	// right now stay visible but greyed out, with the reason next to them, so nothing has to be searched for.

	private const float DebugWindowWidth = 460f;
	private float debugContentHeight = 300f;
	private bool debugFoldCreatures = true, debugFoldWorm = true, debugFoldScares = false, debugFoldDread = false, debugFoldTools = true;
	private GUIStyle debugHeaderStyle, debugSmallStyle;

	private void DrawDebugWindow()
	{
		// the scroll view is as tall as the content measured in the last repaint, up to the screen height
		float maxScroll = Mathf.Max(200f, Screen.height - 140f);
		toolbarPanel.width = DebugWindowWidth;
		toolbarPanel.height = Mathf.Min(debugContentHeight, maxScroll) + 34f;
		toolbarPanel.x = Mathf.Clamp(toolbarPanel.x, 0f, Mathf.Max(0f, Screen.width - toolbarPanel.width));
		toolbarPanel.y = Mathf.Clamp(toolbarPanel.y, 0f, Mathf.Max(0f, Screen.height - toolbarPanel.height));
		toolbarPanel = GUI.Window(windowId + 1, toolbarPanel, DrawToolbarPanel, "UNKERBAL THINGS: TESTING TOOLS");
	}

	private bool DebugSection(string title, ref bool open)
	{
		if (debugHeaderStyle == null)
		{
			debugHeaderStyle = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold };
			debugSmallStyle = new GUIStyle(GUI.skin.label) { wordWrap = true };
		}
		GUILayout.Space(4f);
		if (GUILayout.Button((open ? "▼  " : "▶  ") + title, debugHeaderStyle)) open = !open;
		return open;
	}

	/// <summary>A button that stays in place: greyed out (with the reason, if one is given) when it cannot work now.</summary>
	private static bool DebugButton(string label, string blockedBy)
	{
		bool was = GUI.enabled;
		GUI.enabled = was && blockedBy == null;
		bool hit = GUILayout.Button(string.IsNullOrEmpty(blockedBy) ? label : label + "  (" + blockedBy + ")");
		GUI.enabled = was;
		return hit;
	}

	private void DrawToolbarPanel(int id)
	{
		bool tall = debugContentHeight > Mathf.Max(200f, Screen.height - 140f);
		toolbarScrollPosition = GUILayout.BeginScrollView(toolbarScrollPosition, false, tall);
		GUILayout.BeginVertical();
		Vessel activeVessel = FlightGlobals.ActiveVessel;
		CelestialBody here = activeVessel != null ? activeVessel.mainBody : null;
		bool onMohoBody = here != null && IsBulkEntityBody(here);
		bool onLaythe = here != null && string.Equals(here.bodyName, "Laythe", StringComparison.OrdinalIgnoreCase);

		if (DebugSection("Creatures", ref debugFoldCreatures))
		{
			string moho = bulkEntityActive ? bulkEntityStatus : (WormHuntConfigured && mohoWormAlwaysSpawn && !string.IsNullOrEmpty(WormSpawnState) ? "not out (" + WormSpawnState + ")" : "not out" + (string.IsNullOrEmpty(bulkEntityStatus) || bulkEntityStatus == "NONE" ? "" : " (" + bulkEntityStatus + ")"));
			GUILayout.Label("Moho creature: " + moho + (bulkEntityActive && activeVessel != null && bulkEntityBody != null ? ", " + FormatDistance((bulkEntityBody.position + bulkEntityRelativePosition - activeVessel.GetWorldPos3D()).magnitude) : ""), debugSmallStyle);
			GUILayout.Label("Laythe leviathan: " + (bloopEntityActive ? bloopEntityStatus : "not out") + (bloopEntityActive && activeVessel != null && bloopEntityBody != null ? ", " + FormatDistance((bloopEntityBody.position + bloopEntityRelativePosition - activeVessel.GetWorldPos3D()).magnitude) : ""), debugSmallStyle);
			string mohoBlock = activeVessel == null ? "no craft" : !onMohoBody ? "Moho or Eve only" : activeVessel.Splashed ? "not in water"
				: (WormHuntConfigured ? !WormHuntEligible(activeVessel) : activeVessel.heightFromTerrain >= 10f) ? "too high above the ground" : null;
			string laytheBlock = activeVessel == null ? "no craft" : !onLaythe ? "Laythe only" : null;
			GUILayout.BeginHorizontal();
			if (DebugButton(bulkEntityActive ? "Respawn Moho creature" : "Spawn Moho creature", mohoBlock)) SpawnBulkEntity();
			if (DebugButton("200 m ahead", mohoBlock == null ? null : "")) SpawnBulkEntityForTesting(activeVessel);
			GUILayout.EndHorizontal();
			GUILayout.BeginHorizontal();
			if (DebugButton(bloopEntityActive ? "Respawn leviathan" : "Spawn leviathan", laytheBlock)) SpawnBloopEntity(debugForce: true);
			if (DebugButton("200 m ahead", laytheBlock == null ? null : "")) SpawnBloopEntityForTesting(activeVessel);
			GUILayout.EndHorizontal();
			GUILayout.BeginHorizontal();
			string noneOut = bulkEntityActive || bloopEntityActive ? null : "none out";
			if (DebugButton("Remove creatures", noneOut)) { DespawnBulkEntity(); DespawnBloopEntity(); }
			if (DebugButton("Teleport craft to creature", noneOut)) TeleportToActiveMonster();
			GUILayout.EndHorizontal();
		}

		if (DebugSection("Moho worm hunt", ref debugFoldWorm))
		{
			if (!WormHuntConfigured) GUILayout.Label("Off (needs mohoWormHunt, mohoWormAmbush and mohoWormBody = gpu). The older hunt runs.", debugSmallStyle);
			else if (!bulkEntityActive) GUILayout.Label("No swarm out.", debugSmallStyle);
			else if (!WormHuntOn) GUILayout.Label("The gpu body is not running (no D3D11 compute or no shader bundle): heap fallback with the older hunt.", debugSmallStyle);
			DrawWormHuntDebug();
			DrawHuntDebugLabels();
		}

		if (DebugSection("Scares", ref debugFoldScares))
		{
			DrawScareDebugButtons();
		}

		if (DebugSection("Dread (screen and sound)", ref debugFoldDread))
		{
			DrawDreadDebugButtons();
		}

		if (DebugSection("Tools", ref debugFoldTools))
		{
			groundEntityDebugRadar = GUILayout.Toggle(groundEntityDebugRadar, "Creature radar and spawn timers (lower corners)");
			debugImmortality = GUILayout.Toggle(debugImmortality, "Immortality (creatures cannot destroy the craft)");
			bool fog = GUILayout.Toggle(debugDisableWaterFog, "No water fog (see the leviathan from afar)");
			if (fog != debugDisableWaterFog)
			{
				SetDebugWaterFogDisabled(fog);
			}
		}
		GUILayout.EndVertical();
		if (Event.current.type == EventType.Repaint)
		{
			debugContentHeight = GUILayoutUtility.GetLastRect().height + 12f;
		}
		GUILayout.EndScrollView();
		GUI.DragWindow();
	}

	private void DrawBulkEntityRadar()
	{
		Vessel activeVessel = FlightGlobals.ActiveVessel;
		if (!(activeVessel == null) && !(bulkEntityBody == null))
		{
			Vector3d vector3d = bulkEntityBody.position + bulkEntityRelativePosition;
			Vector3 vector = activeVessel.transform.InverseTransformDirection(vector3d - activeVessel.GetWorldPos3D());
			double magnitude = (vector3d - activeVessel.GetWorldPos3D()).magnitude;
			Rect position = new Rect((float)Screen.width - 190f - 18f, (float)Screen.height - 190f - 18f, 190f, 190f);
			GUI.Box(position, "MOHO CREATURE RADAR (DEBUG)");
			Vector2 vector2 = new Vector2(position.x + 95f, position.y + 106f);
			GUI.Label(new Rect(vector2.x - 8f, vector2.y - 8f, 16f, 16f), "+");
			float magnitude2 = new Vector2(vector.x, vector.z).magnitude;
			float num = Mathf.Min(72f, magnitude2 * 72f / 18000f);
			Vector2 vector3 = ((magnitude2 > 0.01f) ? new Vector2(vector.x, vector.z).normalized : Vector2.zero);
			Vector2 vector4 = vector2 + new Vector2(vector3.x * num, (0f - vector3.y) * num);
			GUIStyle gUIStyle = new GUIStyle(GUI.skin.label);
			gUIStyle.fontSize = 22;
			gUIStyle.fontStyle = FontStyle.Bold;
			gUIStyle.normal.textColor = Color.red;
			gUIStyle.alignment = TextAnchor.MiddleCenter;
			GUI.Label(new Rect(vector4.x - 12f, vector4.y - 12f, 24f, 24f), "â—\u008f", gUIStyle);
			GUIStyle gUIStyle2 = new GUIStyle(GUI.skin.label);
			gUIStyle2.fontSize = 11;
			gUIStyle2.normal.textColor = Color.white;
			gUIStyle2.alignment = TextAnchor.MiddleCenter;
			GUI.Label(new Rect(position.x + 8f, position.y + 145f, 174f, 18f), "RANGE " + FormatDistance(magnitude), gUIStyle2);
			GUI.Label(new Rect(position.x + 8f, position.y + 163f, 174f, 18f), bulkEntityChasing ? "STATE: CHASING" : "STATE: DORMANT", gUIStyle2);
		}
	}

	private void DrawBloopEntityRadar()
	{
		Vessel activeVessel = FlightGlobals.ActiveVessel;
		if (!(activeVessel == null) && !(bloopEntityBody == null))
		{
			Vector3d vector3d = bloopEntityBody.position + bloopEntityRelativePosition;
			Vector3 vector = activeVessel.transform.InverseTransformDirection(vector3d - activeVessel.GetWorldPos3D());
			double magnitude = (vector3d - activeVessel.GetWorldPos3D()).magnitude;
			Rect position = new Rect((float)Screen.width - 190f - 18f, (float)Screen.height - 190f - 18f, 190f, 190f);
			GUI.Box(position, "LAYTHE LEVIATHAN RADAR (DEBUG)");
			Vector2 vector2 = new Vector2(position.x + 95f, position.y + 106f);
			GUI.Label(new Rect(vector2.x - 8f, vector2.y - 8f, 16f, 16f), "+");
			float magnitude2 = new Vector2(vector.x, vector.z).magnitude;
			float num = Mathf.Min(72f, magnitude2 * 72f / 10000f);
			Vector2 vector3 = ((magnitude2 > 0.01f) ? new Vector2(vector.x, vector.z).normalized : Vector2.zero);
			Vector2 vector4 = vector2 + new Vector2(vector3.x * num, (0f - vector3.y) * num);
			GUIStyle gUIStyle = new GUIStyle(GUI.skin.label);
			gUIStyle.fontSize = 22;
			gUIStyle.fontStyle = FontStyle.Bold;
			gUIStyle.normal.textColor = new Color(0.1f, 0.45f, 1f);
			gUIStyle.alignment = TextAnchor.MiddleCenter;
			GUI.Label(new Rect(vector4.x - 12f, vector4.y - 12f, 24f, 24f), "â—\u008f", gUIStyle);
			GUIStyle gUIStyle2 = new GUIStyle(GUI.skin.label);
			gUIStyle2.fontSize = 11;
			gUIStyle2.normal.textColor = Color.white;
			gUIStyle2.alignment = TextAnchor.MiddleCenter;
			GUI.Label(new Rect(position.x + 8f, position.y + 145f, 174f, 18f), "RANGE " + FormatDistance(magnitude), gUIStyle2);
			GUI.Label(new Rect(position.x + 8f, position.y + 163f, 174f, 18f), bloopEntityChasing ? "STATE: CHASING" : "STATE: DORMANT", gUIStyle2);
		}
	}

	private void DrawSpawnDebugPanel()
	{
		Rect position = new Rect(18f, (float)Screen.height - 120f, 260f, 100f);
		GUI.Box(position, "CREATURE SPAWN DEBUG");
		float f = Mathf.Max(0f, randomCheckSeconds - encounterCheckTimer);
		GUI.Label(new Rect(position.x + 10f, position.y + 25f, 240f, 18f), "NEXT CHECK: " + Mathf.CeilToInt(f) + " s");
		if (WormHuntConfigured && mohoWormAlwaysSpawn)
		{
			GUI.Label(new Rect(position.x + 10f, position.y + 43f, 240f, 18f), "MOHO: ALWAYS (" + (string.IsNullOrEmpty(WormSpawnState) ? "-" : WormSpawnState) + ")");
		}
		else
		{
			GUI.Label(new Rect(position.x + 10f, position.y + 43f, 240f, 18f), "MOHO COOLDOWN: " + Mathf.CeilToInt(bulkEncounterCooldownTimer) + " s");
		}
		GUI.Label(new Rect(position.x + 10f, position.y + 61f, 240f, 18f), "LEVIATHAN COOLDOWN: " + Mathf.CeilToInt(bloopEncounterCooldownTimer) + " s");
	}

	private static string DebugState(bool state)
	{
		return state ? "OK" : "BLOCKED";
	}

	private void AddToolbarButton()
	{
		// The toolbar window only holds testing tools; players without debugTools get no button.
		if (debugTools && !(toolbarButton != null) && !(ApplicationLauncher.Instance == null))
		{
			if (toolbarIcon == null)
			{
				toolbarIcon = MakeToolbarIcon();
			}
			toolbarButton = ApplicationLauncher.Instance.AddModApplication(delegate
			{
				toolbarVisible = true;
			}, delegate
			{
				toolbarVisible = false;
			}, null, null, null, null, ApplicationLauncher.AppScenes.FLIGHT, toolbarIcon);
		}
	}

	private void RemoveToolbarButton()
	{
		if (!(toolbarButton == null))
		{
			ApplicationLauncher.Instance.RemoveModApplication(toolbarButton);
			toolbarButton = null;
		}
		toolbarVisible = false;
	}

	// A dim red ring on a dark ground, so the debug button is not a black square.
	private static Texture2D MakeToolbarIcon()
	{
		Texture2D tex = new Texture2D(38, 38, TextureFormat.RGBA32, mipChain: false);
		Color[] px = new Color[38 * 38];
		for (int y = 0; y < 38; y++)
		{
			for (int x = 0; x < 38; x++)
			{
				float r = Mathf.Sqrt((x - 18.5f) * (x - 18.5f) + (y - 18.5f) * (y - 18.5f));
				float ring = Mathf.Clamp01(1f - Mathf.Abs(r - 12f) / 2.5f);
				float dot = Mathf.Clamp01(1f - (r - 3f) / 1.5f);
				float a = Mathf.Max(ring, dot);
				px[y * 38 + x] = new Color(0.75f, 0.12f, 0.1f, a);
			}
		}
		tex.SetPixels(px);
		tex.Apply();
		return tex;
	}

	private void OnGameStateLoaded(ConfigNode gameState)
	{
		DespawnBulkEntity();
		DespawnBloopEntity();
		if (bdArmoryGpsBridge != null)
		{
			bdArmoryGpsBridge.Clear();
		}
		if (bdArmoryGroundRadarBridge != null)
		{
			bdArmoryGroundRadarBridge.Clear();
		}
		if (bdArmoryDamageBridge != null)
		{
			bdArmoryDamageBridge.Clear();
		}
	}

	private static Vector3d GetTestSpawnAhead(Vessel vessel, CelestialBody body)
	{
		if (vessel == null || body == null)
		{
			return Vector3d.zero;
		}
		Vector3d normalized = (vessel.GetWorldPos3D() - body.position).normalized;
		Vector3d lhs = vessel.transform.forward;
		lhs -= normalized * Vector3d.Dot(lhs, normalized);
		if (lhs.sqrMagnitude < 0.01)
		{
			lhs = vessel.transform.right - normalized * Vector3d.Dot(vessel.transform.right, normalized);
		}
		return vessel.GetWorldPos3D() + lhs.normalized * 200.0;
	}

	private void SpawnBulkEntityForTesting(Vessel vessel)
	{
		SpawnBulkEntity();
		if (bulkEntityActive && !(bulkEntityBody == null))
		{
			Vector3d vector3d = ProjectToGround(GetTestSpawnAhead(vessel, bulkEntityBody), bulkEntityBody);
			bulkEntityRelativePosition = vector3d - bulkEntityBody.position;
			if (bulkEntityObject != null)
			{
				bulkEntityObject.transform.position = vector3d;
			}
		}
	}

	private void SpawnBloopEntityForTesting(Vessel vessel)
	{
		SpawnBloopEntity(debugForce: true);
		if (bloopEntityActive && !(bloopEntityBody == null))
		{
			Vector3d vector3d = ProjectToBloopSea(GetTestSpawnAhead(vessel, bloopEntityBody), bloopEntityBody);
			bloopEntityRelativePosition = vector3d - bloopEntityBody.position;
			if (bloopEntityObject != null)
			{
				bloopEntityObject.transform.position = vector3d;
			}
		}
	}

	private void SpawnBulkEntity()
	{
		// a swarm that has fed stays where it is until the flight scene ends: no new creature replaces it
		if (bulkEntityActive && wormSated)
		{
			bulkEntityStatus = "FEEDING";
			return;
		}
		Vessel activeVessel = FlightGlobals.ActiveVessel;
		// the worm hunt drops the old height rule (a craft coming in to land may see the heap); it only needs to be near the ground
		if (activeVessel == null || activeVessel.mainBody == null || (WormHuntConfigured ? !WormHuntEligible(activeVessel) : activeVessel.heightFromTerrain >= 10f))
		{
			bulkEntityStatus = "REQUIRES GROUND";
			return;
		}
		if (!IsBulkEntityBody(activeVessel.mainBody))
		{
			bulkEntityStatus = "MOHO / EVE ONLY";
			return;
		}
		DespawnBulkEntity();
		bulkEntityBody = activeVessel.mainBody;
		Vector3d groundPosition;
		if (WormHuntConfigured)
		{
			string how;
			if (!WormFindSpawn(activeVessel, bulkEntityBody, out groundPosition, out how))
			{
				bulkEntityBody = null;
				bulkEntityStatus = "NO LAND SPAWN POSITION";
				return;
			}
			bulkSpawnPending = false;
			UnityEngine.Debug.Log("[UT Worm] spawn " + how + ", " + (groundPosition - activeVessel.GetWorldPos3D()).magnitude.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + " m from the craft");
		}
		else if (mohoCreature && mohoSpawnHidden)
		{
			// where nobody can see it appear: no line of sight to the craft or the camera, and outside the camera frustum, tested with its height
			float spawnMin = WormHuntConfigured ? mohoWormSpawnMetres * 0.85f : 6000f, spawnMax = WormHuntConfigured ? mohoWormSpawnMetres * 1.15f : 18000f;
			if (!TryFindHiddenSpawnPosition(activeVessel, bulkEntityBody, spawnMin, spawnMax, out groundPosition))
			{
				bulkEntityBody = null;
				bulkEntityStatus = "WAITING FOR COVER";
				if (!bulkSpawnPending)
				{
					bulkSpawnPendingSince = Time.time;
					MohoSpawnDelayed++;
					MohoNote("spawn delayed: no hidden place");
				}
				bulkSpawnPending = true;
				bulkSpawnRetryAt = Time.time + 2.5f;
				return;
			}
			bulkSpawnPending = false;
		}
		else if (!TryFindLandSpawnPosition(activeVessel, bulkEntityBody, WormHuntConfigured ? mohoWormSpawnMetres * 0.85f : 6000f, WormHuntConfigured ? mohoWormSpawnMetres * 1.15f : 18000f, out groundPosition))
		{
			bulkEntityBody = null;
			bulkEntityStatus = "NO LAND SPAWN POSITION";
			return;
		}
		// always the Moho creature with a worm body (gpu, or the heap fallback): the original's imported model for this slot is gone
		bulkEntityObject = CreateMohoEntityVisual();
		if (bulkEntityObject == null)
		{
			bulkEntityBody = null;
			bulkEntityStatus = "MODEL UNAVAILABLE";
			return;
		}
		bulkEntityRelativePosition = groundPosition - bulkEntityBody.position;
		bulkEntityObject.transform.position = groundPosition;
		if (bulkWormSwarm != null)
		{
			CelestialBody body = bulkEntityBody;
			Func<Vector3, Vector3> sampler = delegate (Vector3 p) { return (Vector3)ProjectToGround((Vector3d)p, body); };
			if (bulkMoho != null) bulkMoho.GroundSampler = sampler;
			bulkWormSwarm.Initialize(body, groundPosition, sampler);
		}
		bulkEntityActive = true;
		MohoSpawnCount++;
		if (mohoCreature && mohoSpawnHidden)
		{
			bool exposedAtSpawn = IsBulkExposedNow();
			if (exposedAtSpawn)
			{
				MohoSpawnExposed++;
			}
			MohoNote("spawned " + (groundPosition - activeVessel.GetWorldPos3D()).magnitude.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + " m from the craft, exposed=" + exposedAtSpawn);
		}
		bulkEntityChasing = false;
		bulkSeenVesselIds.Clear();
		bulkEntityStandingDown = false;
		bulkEntityRetreating = false;
		bulkWithdrawalTimer = 0f;
		bulkChaseAnimationPhase = UnityEngine.Random.Range(0f, (float)Math.PI * 2f);
		bulkEntityStatus = "DORMANT";
	}

	private static bool IsBulkEntityBody(CelestialBody body)
	{
		if (body == null)
		{
			return false;
		}
		return string.Equals(body.bodyName, "Moho", StringComparison.OrdinalIgnoreCase) || string.Equals(body.bodyName, "Eve", StringComparison.OrdinalIgnoreCase);
	}

	private static void ConfigureLitEntityMaterials(GameObject root)
	{
		if (root == null)
		{
			return;
		}
		Shader shader = Shader.Find("KSP/Diffuse");
		if (shader == null)
		{
			shader = Shader.Find("Legacy Shaders/Diffuse");
		}
		if (shader == null)
		{
			UnityEngine.Debug.Log("[EntityTracker] Could not find a lit shader for imported entity materials.");
			return;
		}
		Renderer[] componentsInChildren = root.GetComponentsInChildren<Renderer>(includeInactive: true);
		foreach (Renderer renderer in componentsInChildren)
		{
			if (renderer == null)
			{
				continue;
			}
			Material[] materials = renderer.materials;
			foreach (Material material in materials)
			{
				if (!(material == null))
				{
					Texture mainTexture = material.mainTexture;
					Color color = (material.HasProperty("_Color") ? material.color : Color.white);
					material.shader = shader;
					material.mainTexture = mainTexture;
					if (material.HasProperty("_Color"))
					{
						material.color = color;
					}
					if (material.HasProperty("_EmissionColor"))
					{
						material.SetColor("_EmissionColor", Color.black);
					}
					if (material.HasProperty("_EmissiveColor"))
					{
						material.SetColor("_EmissiveColor", Color.black);
					}
					material.DisableKeyword("_EMISSION");
				}
			}
		}
	}

	private static void DisableImportedRootMotion(GameObject root)
	{
		if (!(root == null))
		{
			Animator[] componentsInChildren = root.GetComponentsInChildren<Animator>(includeInactive: true);
			foreach (Animator animator in componentsInChildren)
			{
				animator.enabled = false;
			}
			Animation[] componentsInChildren2 = root.GetComponentsInChildren<Animation>(includeInactive: true);
			foreach (Animation animation in componentsInChildren2)
			{
				animation.Stop();
				animation.enabled = false;
			}
		}
	}

	private static void StripImportedEntityControllersKeepAnimation(GameObject root)
	{
		if (root == null)
		{
			return;
		}
		Behaviour[] componentsInChildren = root.GetComponentsInChildren<Behaviour>(includeInactive: true);
		foreach (Behaviour behaviour in componentsInChildren)
		{
			if (!(behaviour == null) && !(behaviour is Animation))
			{
				UnityEngine.Object.Destroy(behaviour);
			}
		}
		Joint[] componentsInChildren2 = root.GetComponentsInChildren<Joint>(includeInactive: true);
		foreach (Joint joint in componentsInChildren2)
		{
			if (joint != null)
			{
				UnityEngine.Object.Destroy(joint);
			}
		}
	}

	private static void StripImportedEntityControllers(GameObject root)
	{
		if (root == null)
		{
			return;
		}
		Behaviour[] componentsInChildren = root.GetComponentsInChildren<Behaviour>(includeInactive: true);
		foreach (Behaviour behaviour in componentsInChildren)
		{
			if (behaviour != null)
			{
				UnityEngine.Object.Destroy(behaviour);
			}
		}
		Joint[] componentsInChildren2 = root.GetComponentsInChildren<Joint>(includeInactive: true);
		foreach (Joint joint in componentsInChildren2)
		{
			if (joint != null)
			{
				UnityEngine.Object.Destroy(joint);
			}
		}
	}

	private void UpdateBulkEntity()
	{
		if (bulkSpawnPending && !bulkEntityActive && Time.time >= bulkSpawnRetryAt && HighLogic.LoadedSceneIsFlight)
		{
			if (Time.time - bulkSpawnPendingSince > 180f)
			{
				bulkSpawnPending = false;
			}
			else
			{
				SpawnBulkEntity();
			}
		}
		if (!bulkEntityActive || bulkEntityObject == null || bulkEntityBody == null)
		{
			return;
		}
		if (IsAttackSequenceRunning(AttackKind.Bulk))
		{
			return;
		}
		if (UpdateWormAmbush())
		{
			return;
		}
		// the worm hunt (gpu body, mohoWormHunt): ground-only senses, heap / serpent / ambush (EntityTracker.WormHunt.cs)
		if (UpdateWormHunt())
		{
			return;
		}
		if (UpdateWormSated())
		{
			return;
		}
		Vessel vessel = FlightGlobals.ActiveVessel;
		if (vessel == null)
		{
			return;
		}
		Vector3d vector3d = bulkEntityBody.position + bulkEntityRelativePosition;
		Vector3d vector3d2 = vessel.GetWorldPos3D() - vector3d;
		double magnitude = vector3d2.magnitude;
		bool flag = vessel.heightFromTerrain < 10f;
		bool huntAware = huntAi && HuntSense(huntBulk, bulkEntityObject, vector3d, bulkEntityBody, vessel, HuntBulkCorrection);
		if (!bulkEntityChasing && !bulkEntityStandingDown && !bulkEntityRetreating && (huntAi ? huntAware : flag && magnitude <= 16000.0 && HasGroundLineOfSight(vector3d, vessel.GetWorldPos3D(), bulkEntityBody)))
		{
			bulkEntityChasing = true;
		}
		if (bulkEntityChasing && !bulkEntityStandingDown && !bulkEntityRetreating)
		{
			Vessel vessel2 = huntAi ? (huntAware ? vessel : null) : SelectNearestSeenGroundVessel(vector3d, bulkEntityBody, 16000f, bulkSeenVesselIds, behaviorBulk);
			if (vessel2 == null)
			{
				bulkEntityChasing = false;
				bulkSeenVesselIds.Clear();
				bulkEntityStatus = "NO LOCAL TARGETS";
			}
			else
			{
				vessel = vessel2;
				vector3d2 = vessel.GetWorldPos3D() - vector3d;
				magnitude = vector3d2.magnitude;
				flag = vessel.heightFromTerrain < 10f;
			}
		}
		// the GPU worm swarm catches by ambush (EntityTracker.WormAttack.cs): it burrows about 20 m out and erupts under the craft
		if (bulkEntityChasing && !bulkEntityStandingDown && !bulkEntityRetreating && WormGpuCatches)
		{
			if (TryStartWormAmbush(vessel, vector3d))
			{
				return;
			}
		}
		else
		if (bulkEntityChasing && !bulkEntityStandingDown && !bulkEntityRetreating && (magnitude <= 8.0 || TakeHoverCatch(behaviorBulk)))
		{
			// mohoMaxKillParts 0 (default): it tears apart a craft of any size; the original let crafts over 17 parts go
			if (mohoMaxKillParts > 0f && vessel.parts != null && vessel.parts.Count > mohoMaxKillParts)
			{
				ScreenMessages.PostScreenMessage("The Moho creature strikes a vessel too large to kill and vanishes.", 5f, ScreenMessageStyle.UPPER_CENTER);
				LeaveBulkEntity("struck a vessel too large to kill");
				return;
			}
			else
			{
				if (attackSequences && BeginAttackSequence(AttackKind.Bulk, vessel, bulkEntityObject, bulkEntityBody, 8f, -1))
				{
					return;
				}
				bulkSeenVesselIds.Remove(vessel.id);
				HandleEntityCatch(vessel);
				if (debugImmortality)
				{
					bulkEntityStandingDown = true;
					bulkWithdrawalTimer = 3f;
				}
				bulkEntityStatus = "SEEKING NEXT TARGET";
			}
			return;
		}
		if (bulkEntityStandingDown)
		{
			bulkWithdrawalTimer -= TimeWarp.deltaTime;
			bulkEntityStatus = "STANDING STILL";
			if (bulkWithdrawalTimer <= 0f)
			{
				bulkEntityStandingDown = false;
				bulkEntityRetreating = true;
				bulkWithdrawalTimer = 30f;
			}
		}
		else if (bulkEntityRetreating)
		{
			bulkWithdrawalTimer -= TimeWarp.deltaTime;
			if ((flag || bulkLeaving) && Time.timeScale > 0f)
			{
				Vector3d normalized = (vector3d - bulkEntityBody.position).normalized;
				Vector3d vector3d3 = -vector3d2 - normalized * Vector3d.Dot(-vector3d2, normalized);
				if (bulkMoho != null && huntAi && huntSettings.NaturalTurn && huntBulk.hunt != null && huntBulk.frame != null)
				{
					// motion follows the body: the way away from the craft only steers the heading (EntityTracker.Turn.cs)
					MohoRetreatMove(ref vector3d, vector3d3, vessel);
				}
				else if (vector3d3.sqrMagnitude > 1.0)
				{
					float retreatSpeed = 42f;
					if (bulkLeaving && bulkMoho != null)
					{
						// runs, up to the sprint speed, and does not stop for the terrain
						bulkLeaveSpeed = Mathf.MoveTowards(bulkLeaveSpeed, Mathf.Max(retreatSpeed, MohoSprintSpeedNow), Mathf.Max(mohoSprintAccel, 5f) * TimeWarp.deltaTime);
						retreatSpeed = bulkLeaveSpeed;
					}
					vector3d += vector3d3.normalized * retreatSpeed * TimeWarp.deltaTime;
					vector3d = ProjectToGround(vector3d, bulkEntityBody);
					if (WormMayMove(vector3d)) bulkEntityRelativePosition = vector3d - bulkEntityBody.position;
				}
			}
			if (bulkMoho != null)
			{
				MohoAdvanceLeapFree(Mathf.Clamp(TimeWarp.deltaTime, 0f, 0.1f), vector3d, bulkEntityBody);
			}
			bulkEntityStatus = bulkLeaving ? "RUNNING OFF" : "RETREATING";
			if (bulkLeaving)
			{
				bulkExposeTimer -= TimeWarp.deltaTime;
				if (bulkExposeTimer <= 0f)
				{
					bulkExposeTimer = 0.25f;
					bulkExposeCached = IsBulkExposedNow();
				}
				if (!bulkExposeCached)
				{
					DespawnBulkEntity();
					return;
				}
			}
			if (bulkWithdrawalTimer <= 0f)
			{
				if (mohoCreature && mohoLeaveRunning && bulkMoho != null && IsBulkExposedNow())
				{
					bulkLeaving = true;
					bulkExposeTimer = 0f;
					bulkExposeCached = true;
					bulkWithdrawalTimer = 20f;
				}
				else
				{
					DespawnBulkEntity();
					return;
				}
			}
		}
		else if (bulkEntityChasing && (flag || huntAi) && Time.timeScale > 0f)
		{
			Vector3d normalized = (vector3d - bulkEntityBody.position).normalized;
			string smartStatus = null;
			if (huntAi && HuntGroundApproach(huntBulk, behaviorBulk, bulkEntityObject, ref vector3d, vessel, bulkEntityBody, 42f, out smartStatus))
			{
			}
			else if (magnitude <= 20.0)
			{
				vector3d += vector3d2.normalized * 42f * TimeWarp.deltaTime;
			}
			else
			{
				Vector3d vector3d4 = vector3d2 - normalized * Vector3d.Dot(vector3d2, normalized);
				if (vector3d4.sqrMagnitude > 1.0)
				{
					vector3d += vector3d4.normalized * 42f * TimeWarp.deltaTime;
					vector3d = ProjectToGround(vector3d, bulkEntityBody);
				}
			}
			if (WormMayMove(vector3d)) bulkEntityRelativePosition = vector3d - bulkEntityBody.position;
			bulkEntityStatus = smartStatus ?? "CHASING";
		}
		else if (bulkEntityChasing && HoverGroundAttack(behaviorBulk, bulkEntityObject, ref vector3d, vessel, bulkEntityBody, 42f, out var hoverStatus))
		{
			if (WormMayMove(vector3d)) bulkEntityRelativePosition = vector3d - bulkEntityBody.position;
			bulkEntityStatus = hoverStatus;
		}
		else if (bulkEntityChasing)
		{
			bulkEntityStatus = "CANNOT REACH";
		}
		Vector3d normalized2 = (vector3d - bulkEntityBody.position).normalized;
		float num = 0f;
		Quaternion rotation = MotionBaseRotation(motionBulk, bulkEntityObject);
		Vector3d huntDir = Vector3d.zero;
		if (huntAi && (HuntFacing(huntBulk, vector3d, bulkEntityBody, out huntDir) || (bulkTurnHold && bulkEntityRetreating && HuntHeadingDirection(huntBulk, vector3d, bulkEntityBody, huntBulk.hunt.Heading, out huntDir))))
		{
			rotation = Quaternion.LookRotation(huntDir, normalized2) * Quaternion.Euler(0f, 180f, 0f);
		}
		else if (huntAi && !bulkEntityStandingDown && !bulkEntityRetreating)
		{
		}
		else if ((flag || HoverFacing(behaviorBulk)) && vector3d2.sqrMagnitude > 0.01)
		{
			Vector3d lhs = (bulkEntityRetreating ? (-vector3d2) : vector3d2);
			lhs -= normalized2 * Vector3d.Dot(lhs, normalized2);
			if (lhs.sqrMagnitude > 0.01)
			{
				rotation = Quaternion.LookRotation(lhs.normalized, normalized2) * Quaternion.Euler(0f, 180f, 0f);
			}
		}
		bool flag2 = bulkEntityChasing && !bulkEntityStandingDown && !bulkEntityRetreating && HuntIsMoving(huntBulk);
		if (flag2 && bulkMoho == null)
		{
			bulkChaseAnimationPhase += TimeWarp.deltaTime * 12f;
			float num2 = Mathf.Sin(bulkChaseAnimationPhase);
			float num3 = Mathf.Sin(bulkChaseAnimationPhase * 2f);
			num = Mathf.Abs(num2) * 0.38f;
			rotation *= Quaternion.Euler(2.5f * num3, 0f, 4f * num2);
			bulkEntityObject.transform.localScale = Vector3.one * 2.2f * (1f + 0.02f * Mathf.Abs(num2));
		}
		else
		{
			bulkEntityObject.transform.localScale = Vector3.one * ((bulkMoho != null) ? 1f : 2.2f);
		}
		num += GetLeapLift(behaviorBulk) + mohoLeapLift;
		bulkEntityObject.transform.position = vector3d + normalized2 * num;
		bulkEntityObject.transform.rotation = rotation;
		if (bulkMoho != null)
		{
			FeedMoho(vector3d, normalized2, vessel, flag2);
		}
		else
		{
			ApplyEntityMotion(motionBulk, MotionKind.Bulk, bulkEntityObject, bulkEntityBody, vector3d, behaviorBulk, 42f);
			HuntUpdateHead(huntBulk, bulkEntityObject, vector3d, bulkEntityBody, vessel);
			AlignBulkEntityFeetToGround(vector3d + normalized2 * num, normalized2);
		}
	}

	private void AlignBulkEntityFeetToGround(Vector3d groundPosition, Vector3 radialUp)
	{
		if (bulkEntityObject == null)
		{
			return;
		}
		Renderer[] componentsInChildren = bulkEntityObject.GetComponentsInChildren<Renderer>(includeInactive: true);
		if (componentsInChildren.Length == 0)
		{
			return;
		}
		float num = float.MaxValue;
		Renderer[] array = componentsInChildren;
		foreach (Renderer renderer in array)
		{
			if (!(renderer == null) && renderer.enabled)
			{
				Bounds bounds = renderer.bounds;
				float b = Vector3.Dot(bounds.center, radialUp) - Mathf.Abs(radialUp.x) * bounds.extents.x - Mathf.Abs(radialUp.y) * bounds.extents.y - Mathf.Abs(radialUp.z) * bounds.extents.z;
				num = Mathf.Min(num, b);
			}
		}
		if (num != float.MaxValue)
		{
			float num2 = Vector3.Dot(groundPosition, radialUp) + 0.03f;
			bulkEntityObject.transform.position += radialUp * (num2 - num);
		}
	}

	private void DespawnBulkEntity()
	{
		if (bulkEntityObject != null)
		{
			if (mohoCreature && bulkMoho != null)
			{
				bool exposedAtDespawn = IsBulkExposedNow();
				MohoDespawnCount++;
				if (exposedAtDespawn)
				{
					MohoDespawnExposed++;
				}
				MohoNote("despawn, exposed=" + exposedAtDespawn);
			}
			bulkSpawnPending = false;
			UnityEngine.Object.Destroy(bulkEntityObject);
		}
		bulkEntityObject = null;
		ReleaseMoho();
		ResetWormHunt();
		bulkEntityBody = null;
		bulkEntityRelativePosition = Vector3d.zero;
		bulkEntityActive = false;
		bulkEntityChasing = false;
		bulkSeenVesselIds.Clear();
		bulkEntityStandingDown = false;
		bulkEntityRetreating = false;
		bulkWithdrawalTimer = 0f;
		bulkChaseAnimationPhase = 0f;
		bulkEntityStatus = "NONE";
	}

	private static void AlignVisualFeetToGround(GameObject visual, Vector3d groundPosition, Vector3 radialUp, float clearance)
	{
		if (visual == null)
		{
			return;
		}
		float num = float.MaxValue;
		Renderer[] componentsInChildren = visual.GetComponentsInChildren<Renderer>(includeInactive: true);
		foreach (Renderer renderer in componentsInChildren)
		{
			if (!(renderer == null) && renderer.enabled)
			{
				Bounds bounds = renderer.bounds;
				float b = Vector3.Dot(bounds.center, radialUp) - Mathf.Abs(radialUp.x) * bounds.extents.x - Mathf.Abs(radialUp.y) * bounds.extents.y - Mathf.Abs(radialUp.z) * bounds.extents.z;
				num = Mathf.Min(num, b);
			}
		}
		if (num != float.MaxValue)
		{
			visual.transform.position += radialUp * (Vector3.Dot(groundPosition, radialUp) + clearance - num);
		}
	}

	private void SpawnBloopEntity()
	{
		SpawnBloopEntity(debugForce: false);
	}

	private void SpawnBloopEntity(bool debugForce)
	{
		Vessel activeVessel = FlightGlobals.ActiveVessel;
		if (activeVessel == null || activeVessel.mainBody == null || !string.Equals(activeVessel.mainBody.bodyName, "Laythe", StringComparison.OrdinalIgnoreCase))
		{
			bloopEntityStatus = "LAYTHE REQUIRED";
			return;
		}
		if (!debugForce && !CanBloopRemainActive(activeVessel))
		{
			bloopEntityStatus = "DEPTH REQUIRED";
			return;
		}
		DespawnBloopEntity();
		bloopEntityBody = activeVessel.mainBody;
		Vector3 up = ((Vector3)(activeVessel.GetWorldPos3D() - bloopEntityBody.position)).normalized;
		Vector3 ahead = activeVessel.transform.forward - up * Vector3.Dot(activeVessel.transform.forward, up);
		if (ahead.sqrMagnitude < 0.01f)
		{
			ahead = Vector3.Cross(up, Vector3.right);
		}
		ahead.Normalize();
		Vector3d spawn = ProjectToBloopSea(activeVessel.GetWorldPos3D() + (Vector3d)(ahead * UnityEngine.Random.Range(3000f, 10000f)), bloopEntityBody);
		bloopEntityObject = CreateBloopVisual();
		if (bloopEntityObject == null)
		{
			bloopEntityBody = null;
			bloopEntityStatus = "MODEL UNAVAILABLE";
			return;
		}
		bloopEntityObject.SetActive(value: true);
		foreach (Transform t in bloopEntityObject.GetComponentsInChildren<Transform>(includeInactive: true))
		{
			t.gameObject.SetActive(value: true);
			t.gameObject.layer = 0;
		}
		foreach (Collider c in bloopEntityObject.GetComponentsInChildren<Collider>(includeInactive: true))
		{
			UnityEngine.Object.Destroy(c);
		}
		foreach (Rigidbody r in bloopEntityObject.GetComponentsInChildren<Rigidbody>(includeInactive: true))
		{
			UnityEngine.Object.Destroy(r);
		}
		bloopEntityRelativePosition = spawn - bloopEntityBody.position;
		bloopEntityObject.transform.position = spawn;
		bloopEntityActive = true;
		bloopEntityChasing = false;
		bloopEntityStatus = "DORMANT";
		BloopAiStart(activeVessel);
	}

	private void UpdateBloopEntity()
	{
		if (!bloopEntityActive || bloopEntityObject == null || bloopEntityBody == null)
		{
			return;
		}
		Vessel activeVessel = FlightGlobals.ActiveVessel;
		if (activeVessel == null || activeVessel.mainBody != bloopEntityBody || bloopBrain == null)
		{
			DespawnBloopEntity();
			return;
		}
		UpdateBloopAi(activeVessel);
	}

	private void DespawnBloopEntity()
	{
		BloopAiStop();
		if (bloopEntityObject != null)
		{
			UnityEngine.Object.Destroy(bloopEntityObject);
		}
		bloopEntityObject = null;
		bloopEntityBody = null;
		bloopEntityRelativePosition = Vector3d.zero;
		bloopEntityActive = false;
		bloopEntityChasing = false;
		bloopEntityStatus = "NONE";
	}

	private void HandleEntityCatch(Vessel vessel)
	{
		if (!debugImmortality)
		{
			DestroyCaughtVessel(vessel);
		}
	}

	private void HandleEntityCatch(Vessel vessel, int randomPartsToDestroy)
	{
		if (!debugImmortality)
		{
			DestroyRandomCaughtVesselParts(vessel, randomPartsToDestroy);
		}
	}

	private void TeleportToActiveMonster()
	{
		Vessel activeVessel = FlightGlobals.ActiveVessel;
		if (activeVessel == null)
		{
			return;
		}
		Vector3d position;
		if (bloopEntityActive && bloopEntityBody != null)
		{
			position = bloopEntityBody.position + bloopEntityRelativePosition;
		}
		else if (bulkEntityActive && bulkEntityBody != null)
		{
			position = bulkEntityBody.position + bulkEntityRelativePosition;
		}
		else
		{
			return;
		}
		activeVessel.SetPosition(position);
	}

	private static void DestroyCaughtVessel(Vessel vessel)
	{
		if (vessel == null)
		{
			return;
		}
		if (vessel.parts == null || vessel.parts.Count == 0)
		{
			vessel.Die();
			return;
		}
		List<Part> list = new List<Part>(vessel.parts);
		foreach (Part item in list)
		{
			if (item != null)
			{
				item.explode();
			}
		}
	}

	private static void DestroyRandomCaughtVesselParts(Vessel vessel, int partCount)
	{
		if (vessel == null)
		{
			return;
		}
		if (vessel.parts == null || vessel.parts.Count < partCount)
		{
			DestroyCaughtVessel(vessel);
			return;
		}
		List<Part> list = new List<Part>(vessel.parts);
		for (int num = list.Count - 1; num > 0; num--)
		{
			int index = UnityEngine.Random.Range(0, num + 1);
			Part value = list[num];
			list[num] = list[index];
			list[index] = value;
		}
		int num2 = Mathf.Min(partCount, list.Count);
		for (int num = 0; num < num2; num++)
		{
			if (list[num] != null)
			{
				list[num].explode();
			}
		}
	}

	private static Vector3d ProjectToBloopSea(Vector3d worldPosition, CelestialBody body)
	{
		double latitude = body.GetLatitude(worldPosition);
		double longitude = body.GetLongitude(worldPosition);
		return body.GetWorldSurfacePosition(latitude, longitude, -70.0);
	}

	private static bool CanBloopRemainActive(Vessel vessel)
	{
		return vessel != null && vessel.mainBody != null && vessel.altitude <= -(double)bloopMinDepthMetres && string.Equals(vessel.mainBody.bodyName, "Laythe", StringComparison.OrdinalIgnoreCase);
	}

	private static bool CanBloopTarget(Vessel vessel)
	{
		return CanBloopRemainActive(vessel);
	}

	private static Vessel SelectNearestSeenGroundVessel(Vector3d entityPosition, CelestialBody body, float range, HashSet<Guid> seenVesselIds, GroundBehaviorState behavior = null)
	{
		Vessel result = null;
		double num = double.MaxValue;
		foreach (Vessel vessel in FlightGlobals.Vessels)
		{
			if (vessel == null || vessel.mainBody != body || vessel.state == Vessel.State.DEAD || vessel.heightFromTerrain >= GetGroundTargetHeightLimit())
			{
				continue;
			}
			double magnitude = (vessel.GetWorldPos3D() - entityPosition).magnitude;
			if (!(magnitude > (double)range))
			{
				if (HasGroundLineOfSight(entityPosition, vessel.GetWorldPos3D(), body))
				{
					seenVesselIds.Add(vessel.id);
				}
				if (vessel == FlightGlobals.ActiveVessel && DreadDarkLosesTarget(behavior, magnitude))
				{
					seenVesselIds.Remove(vessel.id);
					continue;
				}
				if (seenVesselIds.Contains(vessel.id) && !(magnitude >= num))
				{
					result = vessel;
					num = magnitude;
				}
			}
		}
		return result;
	}

	private static Vector3d ProjectToGround(Vector3d worldPosition, CelestialBody body)
	{
		double latitude = body.GetLatitude(worldPosition);
		double longitude = body.GetLongitude(worldPosition);
		double num = body.TerrainAltitude(latitude, longitude);
		Vector3d worldSurfacePosition = body.GetWorldSurfacePosition(latitude, longitude, num + 0.11999999731779099);
		return KeepOutsideSpaceCenter(worldSurfacePosition, body);
	}

	private static Vector3d KeepOutsideSpaceCenter(Vector3d groundPosition, CelestialBody body)
	{
		SpaceCenter spaceCenter = SpaceCenter.Instance;
		if (spaceCenter == null || body == null || spaceCenter.cb != body)
		{
			return groundPosition;
		}
		double latitude = spaceCenter.Latitude;
		double longitude = spaceCenter.Longitude;
		double alt = body.TerrainAltitude(latitude, longitude);
		Vector3d worldSurfacePosition = body.GetWorldSurfacePosition(latitude, longitude, alt);
		Vector3d vector3d = groundPosition - worldSurfacePosition;
		Vector3d normalized = (worldSurfacePosition - body.position).normalized;
		Vector3d vector3d2 = vector3d - normalized * Vector3d.Dot(vector3d, normalized);
		double magnitude = vector3d2.magnitude;
		if (magnitude >= 750.0)
		{
			return groundPosition;
		}
		if (magnitude < 0.01)
		{
			vector3d2 = Vector3d.Cross(normalized, Vector3d.up);
			if (vector3d2.sqrMagnitude < 0.001)
			{
				vector3d2 = Vector3d.Cross(normalized, Vector3d.right);
			}
		}
		Vector3d pos = worldSurfacePosition + vector3d2.normalized * 750.0;
		double latitude2 = body.GetLatitude(pos);
		double longitude2 = body.GetLongitude(pos);
		double num = body.TerrainAltitude(latitude2, longitude2);
		return body.GetWorldSurfacePosition(latitude2, longitude2, num + 0.11999999731779099);
	}

	private static bool TryFindLandSpawnPosition(Vessel vessel, CelestialBody body, float minimumDistance, float maximumDistance, out Vector3d groundPosition)
	{
		groundPosition = Vector3d.zero;
		if (vessel == null || body == null)
		{
			return false;
		}
		for (int i = 0; i < 32; i++)
		{
			Vector3 normalized = (vessel.transform.forward * UnityEngine.Random.Range(-1f, 1f) + vessel.transform.right * UnityEngine.Random.Range(-1f, 1f)).normalized;
			if (normalized.sqrMagnitude < 0.01f)
			{
				normalized = vessel.transform.forward.normalized;
			}
			Vector3d worldPosition = vessel.GetWorldPos3D() + (Vector3d)(normalized * UnityEngine.Random.Range(minimumDistance, maximumDistance));
			Vector3d vector3d = ProjectToGround(worldPosition, body);
			if (IsLandAt(vector3d, body) && (vector3d - vessel.GetWorldPos3D()).magnitude >= (double)minimumDistance)
			{
				groundPosition = vector3d;
				return true;
			}
		}
		return false;
	}

	private static bool IsLandAt(Vector3d worldPosition, CelestialBody body)
	{
		if (body == null || !body.ocean)
		{
			return true;
		}
		double latitude = body.GetLatitude(worldPosition);
		double longitude = body.GetLongitude(worldPosition);
		return body.TerrainAltitude(latitude, longitude) > 0.01;
	}

	private static bool HasGroundLineOfSight(Vector3d entityPosition, Vector3d vesselPosition, CelestialBody body)
	{
		if (body == null)
		{
			return false;
		}
		Vector3d normalized = (entityPosition - body.position).normalized;
		Vector3d normalized2 = (vesselPosition - body.position).normalized;
		Vector3d vector3d = entityPosition + normalized * 35.0;
		Vector3d vector3d2 = vesselPosition + normalized2 * 6.0;
		double magnitude = (vector3d2 - vector3d).magnitude;
		int num = Mathf.Clamp(Mathf.CeilToInt((float)(magnitude / 350.0)), 12, 72);
		for (int i = 1; i < num; i++)
		{
			double t = (double)i / (double)num;
			Vector3d vector3d3 = Vector3d.Lerp(vector3d, vector3d2, t);
			double latitude = body.GetLatitude(vector3d3);
			double longitude = body.GetLongitude(vector3d3);
			double alt = body.TerrainAltitude(latitude, longitude);
			Vector3d worldSurfacePosition = body.GetWorldSurfacePosition(latitude, longitude, alt);
			if ((vector3d3 - body.position).magnitude <= (worldSurfacePosition - body.position).magnitude + 0.5)
			{
				return false;
			}
		}
		return true;
	}

	private void UpdateBDArmoryGpsTargets()
	{
		if (!bdArmoryCompatibility || !bdArmoryGpsTargets)
		{
			if (bdArmoryGpsBridge != null)
			{
				bdArmoryGpsBridge.Clear();
			}
			return;
		}
		if (bdArmoryGpsBridge == null)
		{
			bdArmoryGpsBridge = new BDArmoryGpsBridge();
		}
		bdArmoryGpsBridge.Sync("Moho Creature", bulkEntityActive && bulkEntityObject != null, (bulkEntityObject != null) ? ((Vector3d)bulkEntityObject.transform.position) : Vector3d.zero);
		bdArmoryGpsBridge.Sync("Laythe Leviathan", bloopEntityActive && bloopEntityObject != null, (bloopEntityObject != null) ? ((Vector3d)bloopEntityObject.transform.position) : Vector3d.zero);
	}

	private void UpdateBDArmoryGroundRadarTargets()
	{
		if (!bdArmoryCompatibility || !bdArmoryGroundRadarTargets)
		{
			if (bdArmoryGroundRadarBridge != null)
			{
				bdArmoryGroundRadarBridge.Clear();
			}
			return;
		}
		if (bdArmoryGroundRadarBridge == null)
		{
			bdArmoryGroundRadarBridge = new BDArmoryGroundRadarBridge();
		}
		bdArmoryGroundRadarBridge.Sync("Moho Creature", bulkEntityActive && bulkEntityObject != null, (bulkEntityObject != null) ? bulkEntityObject.transform.position : Vector3.zero, 30f);
		bdArmoryGroundRadarBridge.Sync("Laythe Leviathan", bloopEntityActive && bloopEntityObject != null, (bloopEntityObject != null) ? bloopEntityObject.transform.position : Vector3.zero, 75f);
	}

	private void UpdateBDArmoryWeaponDamage()
	{
		if (!bdArmoryCompatibility || !bdArmoryWeaponDamage)
		{
			if (bdArmoryDamageBridge != null)
			{
				bdArmoryDamageBridge.Clear();
			}
			return;
		}
		if (bdArmoryDamageBridge == null)
		{
			bdArmoryDamageBridge = new BDArmoryDamageBridge();
		}
		bdArmoryDamageBridge.Sync("Moho Creature", bulkEntityActive, bulkEntityObject, 150f, DestroyBulkEntityFromBda);
		bdArmoryDamageBridge.Sync("Laythe Leviathan", bloopEntityActive, bloopEntityObject, 600f, DestroyBloopEntityFromBda);
		bdArmoryDamageBridge.Update();
	}

	private void DestroyBulkEntityFromBda()
	{
		DespawnBulkEntity();
		bulkEntityStatus = "DESTROYED BY BDA";
	}

	private void DestroyBloopEntityFromBda()
	{
		DespawnBloopEntity();
		bloopEntityStatus = "DESTROYED BY BDA";
	}

	private void LoadSettings()
	{
		string text = Path.Combine(KSPUtil.ApplicationRootPath, "GameData", ModFolderName, "settings.cfg");
		ConfigNode configNode = (File.Exists(text) ? ConfigNode.Load(text) : null);
		if (configNode != null && configNode.HasValue("randomEncounters"))
		{
			bool.TryParse(configNode.GetValue("randomEncounters"), out randomEncounters);
		}
		if (configNode != null && configNode.HasValue("debugTools"))
		{
			bool.TryParse(configNode.GetValue("debugTools"), out debugTools);
		}
		if (configNode != null && configNode.HasValue("debugHotkey"))
		{
			bool.TryParse(configNode.GetValue("debugHotkey"), out debugHotkey);
		}
		if (configNode != null && configNode.HasValue("bdArmoryGpsTargets"))
		{
			bool.TryParse(configNode.GetValue("bdArmoryGpsTargets"), out bdArmoryGpsTargets);
		}
		if (configNode != null && configNode.HasValue("bdArmoryGroundRadarTargets"))
		{
			bool.TryParse(configNode.GetValue("bdArmoryGroundRadarTargets"), out bdArmoryGroundRadarTargets);
		}
		if (configNode != null && configNode.HasValue("bdArmoryCompatibility"))
		{
			bool.TryParse(configNode.GetValue("bdArmoryCompatibility"), out bdArmoryCompatibility);
		}
		if (configNode != null && configNode.HasValue("bdArmoryWeaponDamage"))
		{
			bool.TryParse(configNode.GetValue("bdArmoryWeaponDamage"), out bdArmoryWeaponDamage);
		}
		if (configNode != null && configNode.HasValue("creatureDebugRadar"))
		{
			bool.TryParse(configNode.GetValue("creatureDebugRadar"), out groundEntityDebugRadar);
		}
		ReadFloatSetting(configNode, "randomCheckSeconds", ref randomCheckSeconds);
		ReadFloatSetting(configNode, "randomCooldownSeconds", ref randomCooldownSeconds);
		ReadFloatSetting(configNode, "bulkEncounterChance", ref bulkEncounterChance);
		ReadFloatSetting(configNode, "bulkEncounterCooldownSeconds", ref bulkEncounterCooldownSeconds);
		ReadFloatSetting(configNode, "bloopEncounterChance", ref bloopEncounterChance);
		ReadFloatSetting(configNode, "bloopEncounterCooldownSeconds", ref bloopEncounterCooldownSeconds);
		LoadBehaviorSettings(configNode);
		LoadAttackSettings(configNode);
		LoadMotionSettings(configNode);
		LoadMohoSettings(configNode);
		LoadWormHuntSettings(configNode);
		LoadScareSettings(configNode);
		LoadDreadSettings(configNode);
		LoadViewLockSettings(configNode);
		LoadBloopSettings(configNode);
		LoadHuntSettings(configNode);
		LoadSprintSettings(configNode);
	}

	private static void ReadFloatSetting(ConfigNode settings, string name, ref float value)
	{
		if (settings != null && settings.HasValue(name) && float.TryParse(settings.GetValue(name), out var result))
		{
			value = result;
		}
	}

	// Every randomCheckSeconds one roll per creature that could appear here; one creature at a time.
	private void UpdateRandomEncounters()
	{
		if (!randomEncounters || Time.timeScale <= 0f)
		{
			return;
		}
		Vessel activeVessel = FlightGlobals.ActiveVessel;
		if (activeVessel == null || activeVessel.mainBody == null)
		{
			return;
		}
		float unscaledDeltaTime = Time.unscaledDeltaTime;
		bulkEncounterCooldownTimer = Mathf.Max(0f, bulkEncounterCooldownTimer - unscaledDeltaTime);
		bloopEncounterCooldownTimer = Mathf.Max(0f, bloopEncounterCooldownTimer - unscaledDeltaTime);
		encounterCheckTimer += GetCappedSpawnCheckDelta();
		if (encounterCheckTimer < randomCheckSeconds)
		{
			return;
		}
		encounterCheckTimer = 0f;
		if (!bulkEntityActive && !bloopEntityActive && !(WormHuntConfigured && mohoWormAlwaysSpawn) && bulkEncounterCooldownTimer <= 0f && IsEligibleForBulkEncounter(activeVessel) && UnityEngine.Random.value <= Mathf.Clamp01(bulkEncounterChance))
		{
			SpawnBulkEntity();
			if (bulkEntityActive)
			{
				bulkEncounterCooldownTimer = bulkEncounterCooldownSeconds;
			}
		}
		if (!bulkEntityActive && !bloopEntityActive && bloopEncounterCooldownTimer <= 0f && CanBloopTarget(activeVessel) && UnityEngine.Random.value <= Mathf.Clamp01(bloopEncounterChance))
		{
			SpawnBloopEntity();
			if (bloopEntityActive)
			{
				bloopEncounterCooldownTimer = bloopEncounterCooldownSeconds;
			}
		}
	}

	private static bool IsEligibleForBulkEncounter(Vessel vessel)
	{
		if (WormHuntConfigured) return WormHuntEligible(vessel);
		return vessel != null && vessel.mainBody != null && !vessel.Splashed && vessel.heightFromTerrain < 10f && IsBulkEntityBody(vessel.mainBody);
	}

	private static string GetCurrentBiomeName(Vessel vessel)
	{
		if (vessel == null || vessel.mainBody == null || vessel.mainBody.BiomeMap == null)
		{
			return "UNKNOWN";
		}
		return vessel.mainBody.BiomeMap.GetAtt(vessel.latitude, vessel.longitude).name;
	}

	private static bool IsBlockedGroundBiome(string biome)
	{
		if (string.IsNullOrEmpty(biome))
		{
			return false;
		}
		string text = biome.ToLowerInvariant();
		return text.Contains("ocean") || text.Contains("arctic") || text.Contains("mountain");
	}

	private static bool IsNightAtVessel(Vessel vessel)
	{
		CelestialBody celestialBody = ((Planetarium.fetch != null) ? Planetarium.fetch.Sun : null);
		if (vessel == null || vessel.mainBody == null || celestialBody == null)
		{
			return false;
		}
		Vector3d normalized = (vessel.GetWorldPos3D() - vessel.mainBody.position).normalized;
		Vector3d normalized2 = (celestialBody.position - vessel.GetWorldPos3D()).normalized;
		return Vector3d.Dot(normalized, normalized2) < 0.0;
	}

	private static bool BodyListContains(string list, string bodyName)
	{
		string[] array = list.Split(',');
		for (int i = 0; i < array.Length; i++)
		{
			if (string.Equals(array[i].Trim(), bodyName, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	private static AudioSource ConfigureAudioSource(AudioSource source)
	{
		source.loop = true;
		source.playOnAwake = false;
		source.spatialBlend = 1f;
		source.rolloffMode = AudioRolloffMode.Linear;
		source.minDistance = 5f;
		source.maxDistance = 50000f;
		source.volume = 0f;
		return source;
	}

	private static AudioSource ConfigureGroundAudioSource(AudioSource source, float maximumDistance, float volume)
	{
		source.loop = true;
		source.playOnAwake = false;
		source.spatialBlend = 1f;
		source.rolloffMode = AudioRolloffMode.Linear;
		source.minDistance = 3f;
		source.maxDistance = maximumDistance;
		source.volume = volume;
		return source;
	}

	private static void UpdateGroundAudioSource(AudioSource source, bool loaded, bool shouldPlay)
	{
		if (source == null)
		{
			return;
		}
		if (!shouldPlay)
		{
			if (source.isPlaying)
			{
				source.Pause();
			}
		}
		else if (loaded && !source.isPlaying)
		{
			source.Play();
		}
	}

	private static void PauseAudio(AudioSource source)
	{
		source.volume = 0f;
		if (source.isPlaying)
		{
			source.Pause();
		}
	}

	private static void PlayIfLoaded(AudioSource source, bool loaded)
	{
		if (loaded && !source.isPlaying)
		{
			source.Play();
		}
	}

	private void SetDebugWaterFogDisabled(bool disabled)
	{
		if (disabled && !fogStateCaptured)
		{
			fogEnabledBeforeDebug = RenderSettings.fog;
			fogStateCaptured = true;
		}
		debugDisableWaterFog = disabled;
		if (!disabled && fogStateCaptured)
		{
			RenderSettings.fog = fogEnabledBeforeDebug;
			fogStateCaptured = false;
		}
	}

	private void UpdateDebugFogOverride()
	{
		if (debugDisableWaterFog)
		{
			RenderSettings.fog = false;
		}
	}

	public void OnDestroy()
	{
		if (instance == this)
		{
			instance = null;
		}
		CancelAllAttackSequences();
		ShutdownScares();
		DestroyDread();
		DestroyHunt();
		SetDebugWaterFogDisabled(disabled: false);
		DespawnBulkEntity();
		DespawnBloopEntity();
		if (bdArmoryGpsBridge != null)
		{
			bdArmoryGpsBridge.Clear();
		}
		if (bdArmoryGroundRadarBridge != null)
		{
			bdArmoryGroundRadarBridge.Clear();
		}
		GameEvents.onGUIApplicationLauncherReady.Remove(AddToolbarButton);
		GameEvents.onGUIApplicationLauncherDestroyed.Remove(RemoveToolbarButton);
		GameEvents.onGameStateLoad.Remove(OnGameStateLoaded);
		RemoveToolbarButton();
	}

	private static string FormatDistance(double metres)
	{
		return (metres >= 1000.0) ? ((metres / 1000.0).ToString("F2") + " km") : (metres.ToString("F0") + " m");
	}

	private static bool IsTypingInControl()
	{
		return GUIUtility.keyboardControl != 0;
	}
}
