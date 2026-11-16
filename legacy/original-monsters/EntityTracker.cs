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

[KSPAddon(KSPAddon.Startup.Flight, false)]
public partial class EntityTracker : MonoBehaviour
{
	private sealed class EvaSpeedState
	{
		public float runSpeed;

		public float boundSpeed;
	}

	private enum GroundVariant
	{
		Pilot,
		Scientist,
		Engineer,
		Tourist
	}

	private class UnknownMapContact
	{
		public float phase;

		public double radius;

		public float inclination;

		public float angularRate;

		public float visibleStart;
	}

	private struct WindowRect
	{
		public int Left;

		public int Top;

		public int Right;

		public int Bottom;
	}

	private const string ModFolderName = "Kraken's Kreations";

	private const float SpawnDistanceMetres = 50000f;

	private const float InterplanetarySpawnDistanceMetres = 1000000f;

	private const float ArrivalDistanceMetres = 15f;

	private const float VisualWorldSizeMetres = 6f;

	private const float EyeVisualWorldSizeMetres = 6f;

	private const float GrabberVisualWorldSizeMetres = 12f;

	private const float DresSomethingSpawnMinimumMetres = 3000f;

	private const float DresSomethingSpawnMaximumMetres = 10000f;

	private const float DresSomethingTriggerDistanceMetres = 30f;

	private const float DresSomethingWorldSizeMetres = 10f;

	private const float DresSomethingHoverHeightMetres = 24f;

	private const float DresSomethingFadeSeconds = 7f;

	private const float GrabberSpawnDistanceMetres = 50000f;

	private const float GrabberCatchDistanceMetres = 28f;

	private const float GrabberChaseSpeedMetresPerSecond = 650f;

	private const float GrabberReleaseSpeedLossMetresPerSecond = 300f;

	private const float GrabberFleeSpeedMetresPerSecond = 1250f;

	private const float GrabberFleeDurationSeconds = 7f;

	private const float GrabberBrakingForceKilonewtons = 35f;

	private const float GrabberBreakoutAccelerationMetresPerSecondSquared = 8f;

	private const float GrabberBreakoutSustainSeconds = 0.75f;

	private const float GrabberLowJoolOrbitMaximumAltitude = 1000000f;

	private const float MaximumApproachSpeedMetresPerSecond = 1500f;

	private const float SlowdownDistanceMetres = 10000f;

	private const float OrbitalSpeedMultiplier = 10f;

	private const float InterplanetarySpeedMultiplier = 40f;

	private const float GroundSpawnMinimumMetres = 6000f;

	private const float GroundSpawnMaximumMetres = 18000f;

	private const float GroundActivationMetres = 16000f;

	private const double SpaceCenterSafetyRadiusMetres = 750.0;

	private const float GroundSightHeightMetres = 35f;

	private const float VesselSightHeightMetres = 6f;

	private const float GroundIgnoreAltitudeMetres = 10f;

	private const float GroundArrivalDistanceMetres = 2f;

	private const float GroundChaseSpeedMetresPerSecond = 30f;

	private const float GroundRoamSpeedMetresPerSecond = 2.5f;

	private const float GroundStalkSpeedMetresPerSecond = 6f;

	private const float GroundAggroDistanceMetres = 150f;

	private const float GroundVisualClearanceMetres = 0.12f;

	private const float ScientistFollowSpeedMetresPerSecond = 9f;

	private const float ScientistFleeSpeedMetresPerSecond = 28f;

	private const float ScientistFleeDistanceMetres = 100f;

	private const float ScientistFleeSeconds = 5f;

	private const float BulkSpawnMinimumMetres = 6000f;

	private const float BulkSpawnMaximumMetres = 18000f;

	private const float BulkActivationMetres = 16000f;

	private const float BulkArrivalDistanceMetres = 8f;

	private const float BulkChaseSpeedMetresPerSecond = 42f;

	private const float BulkVisualScale = 2.2f;

	private const float WithdrawalStandSeconds = 3f;

	private const float WithdrawalFleeSeconds = 30f;

	private const float BulkEscapeDistanceMetres = 10000f;

	private const string BulkModelPath = "Kraken's Kreations/Models/nightmare_creature_1";

	private const string MimicModelPath = "Kraken's Kreations/Models/kerbal_mimic";

	private const float LaytheCreatureSpawnMinimumMetres = 6000f;

	private const float LaytheCreatureSpawnMaximumMetres = 18000f;

	private const float LaytheCreatureActivationMetres = 2500f;

	private const float LaytheCreatureArrivalDistanceMetres = 3f;

	private const float LaytheCreatureChaseSpeedMetresPerSecond = 34f;

	private const float LaytheCreatureRoamSpeedMetresPerSecond = 2.4f;

	private const float LaytheCreatureVisualScale = 0.42f;

	private const string LaytheCreatureModelPath = "Kraken's Kreations/Models/the_smile";

	private const float SeaSpawnMinimumMetres = 900f;

	private const float SeaSpawnMaximumMetres = 2200f;

	private const float SeaActivationMetres = 1500f;

	private const float SeaArrivalDistanceMetres = 18f;

	private const float SeaChaseSpeedMetresPerSecond = 52f;

	private const float SeaDepthMetres = 18f;

	private const float SeaVisualElevationMetres = 16f;

	private const float SeaVisualScale = 0.035f;

	private const string SeaModelPath = "Kraken's Kreations/Models/sea_monster_shark_concept";

	private const float BloopSpawnMinimumMetres = 3000f;

	private const float BloopSpawnMaximumMetres = 10000f;

	private const float BloopActivationMetres = 8000f;

	private const float BloopArrivalDistanceMetres = 120f;

	private const float BloopCloseLungeDistanceMetres = 300f;

	private const float BloopChaseSpeedMetresPerSecond = 18f;

	private const float BloopDepthMetres = 70f;

	private const float BloopVisualElevationMetres = 0f;

	private const float BloopVisualScale = 6f;

	private const string BloopModelPath = "Kraken's Kreations/Models/bloop";

	private const float RakeSpawnMinimumMetres = 6000f;

	private const float RakeSpawnMaximumMetres = 18000f;

	private const float RakeActivationMetres = 16000f;

	private const float RakeArrivalDistanceMetres = 8f;

	private const float RakeChaseSpeedMetresPerSecond = 58f;

	private const float RakeObservationEscapeSeconds = 20f;

	private const float RakeModelRootBelowFeetMetres = 6.56f;

	private const string RakeModelPath = "Kraken's Kreations/Models/the_rake";

	private const uint NoSizeNoZOrderNoActivate = 21u;

	private static EntityTracker instance;

	private static readonly BindingFlags ExpressionFieldFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

	private static readonly FieldInfo ExpressionPanicLevelField = typeof(kerbalExpressionSystem).GetField("panicLevel", ExpressionFieldFlags);

	private static readonly FieldInfo ExpressionFearFactorField = typeof(kerbalExpressionSystem).GetField("fearFactor", ExpressionFieldFlags);

	private static readonly FieldInfo ExpressionWheeLevelField = typeof(kerbalExpressionSystem).GetField("wheeLevel", ExpressionFieldFlags);

	private static readonly FieldInfo ExpressionTargetField = typeof(kerbalExpressionSystem).GetField("targetExpression", ExpressionFieldFlags);

	private static readonly FieldInfo ExpressionCurrentField = typeof(kerbalExpressionSystem).GetField("expression", ExpressionFieldFlags);

	private static readonly FieldInfo EvaRunSpeedField = typeof(KerbalEVA).GetField("runSpeed", ExpressionFieldFlags);

	private static readonly FieldInfo EvaBoundSpeedField = typeof(KerbalEVA).GetField("boundSpeed", ExpressionFieldFlags);

	private bool tracking;

	private float currentSpawnDistanceMetres = 50000f;

	private bool arrived;

	private bool arrivalHandled;

	private bool quitOnArrival;

	private bool alwaysVisible = true;

	private bool tufxGlitchOnArrival = true;

	private bool randomEncounters = true;

	private bool debugTools = true;

	private bool bdArmoryCompatibility = true;

	private bool bdArmoryGpsTargets = true;

	private bool bdArmoryGroundRadarTargets = true;

	private bool bdArmoryWeaponDamage = true;

	private BDArmoryGpsBridge bdArmoryGpsBridge;

	private BDArmoryGroundRadarBridge bdArmoryGroundRadarBridge;

	private BDArmoryDamageBridge bdArmoryDamageBridge;

	private float approachSpeedMetresPerSecond = 120f;

	private float blackboxSpeedMultiplier = 1f;

	private float groundEntitySpeedMultiplier = 1f;

	private bool classicMimicBehavior;

	private float seaEntitySpeedMultiplier = 1f;

	private float eyeSpeedMultiplier = 1f;

	private float grabberSpeedMultiplier = 1f;

	private float randomCheckSeconds = 45f;

	private float baseEncounterChance = 3f / 160f;

	private float grabberEncounterChance = 0.025f;

	private float dresSomethingEncounterChance = 0.12f;

	private float dresSomethingEncounterCooldownSeconds = 900f;

	private float dresSomethingEncounterCooldownTimer;

	private float groundEncounterChance = 0.35f;

	private float highEncounterMultiplier = 8f;

	private float rareEncounterMultiplier = 0.2f;

	private float randomCooldownSeconds = 1800f;

	private float groundEncounterCooldownSeconds = 600f;

	private string immuneBodies = "Laythe,Vall,Tylo,Bop";

	private string highEncounterBodies = "Eeloo";

	private string rareEncounterBodies = "Pol,Kerbin";

	private float encounterCheckTimer;

	private float encounterCooldownTimer;

	private float groundEncounterCooldownTimer;

	private float escapeDistanceGainMetres = 3000f;

	private float survivalEscapeSeconds = 180f;

	private bool survivalJolt = true;

	private double closestEncounterDistance;

	private bool survivalTimerStarted;

	private float survivalTimer;

	private int lastCountdownSecond = -1;

	private float countdownTextShakeTimer;

	private float safeMessageTimer;

	private int joltFramesRemaining;

	private float proximityJoltTimer;

	private WindowRect originalWindowRect;

	private bool hasOriginalWindowRect;

	private float bulkEncounterChance = 0.2f;

	private float bulkEncounterCooldownSeconds = 600f;

	private float bulkEncounterCooldownTimer;

	private GameObject bulkEntityObject;

	private BulkEntityBoneAnimation bulkEntityBoneAnimation;

	private AudioSource bulkFootstepsAudio;

	private bool bulkFootstepsLoaded;

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

	private static readonly Vector3 LaytheCreatureVisualScaleVector = new Vector3(0.42f, 0.21f, 0.42f);

	private static readonly Quaternion LaytheCreatureModelAxisCorrection = Quaternion.identity;

	private float laytheCreatureEncounterChance = 0.18f;

	private float laytheCreatureEncounterCooldownSeconds = 600f;

	private float laytheCreatureEncounterCooldownTimer;

	private GameObject laytheCreatureObject;

	private LaytheCreatureBoneAnimation laytheCreatureBoneAnimation;

	private Animation laytheCreatureAnimation;

	private AudioSource laytheCreatureFootstepsAudio;

	private AudioSource laytheCreatureScreamAudio;

	private bool laytheCreatureFootstepsLoaded;

	private bool laytheCreatureScreamLoaded;

	private CelestialBody laytheCreatureBody;

	private Vector3d laytheCreatureRelativePosition;

	private bool laytheCreatureActive;

	private bool laytheCreatureChasing;

	private readonly HashSet<Guid> laytheSeenVesselIds = new HashSet<Guid>();

	private float laytheCreatureAnimationPhase;

	private string laytheCreatureStatus = "NONE";

	private static readonly Quaternion SeaModelAxisCorrection = Quaternion.Euler(90f, 0f, 0f);

	private float seaEncounterChance = 0.18f;

	private float seaEncounterCooldownSeconds = 600f;

	private float seaEncounterCooldownTimer;

	private GameObject seaEntityObject;

	private AudioSource seaGrowlAudio;

	private bool seaGrowlLoaded;

	private CelestialBody seaEntityBody;

	private Vector3d seaEntityRelativePosition;

	private bool seaEntityActive;

	private bool seaEntityChasing;

	private float seaAnimationPhase;

	private string seaEntityStatus = "NONE";

	private static readonly Quaternion BloopModelAxisCorrection = Quaternion.Euler(90f, 0f, 0f);

	private float bloopEncounterChance = 0.14f;

	private float bloopEncounterCooldownSeconds = 900f;

	private float bloopEncounterCooldownTimer;

	private GameObject bloopEntityObject;

	private AudioSource bloopBurrowAudio;

	private bool bloopBurrowLoaded;

	private float bloopBurrowTimer;

	private bool bloopCloseCuePlayed;

	private CelestialBody bloopEntityBody;

	private Vector3d bloopEntityRelativePosition;

	private bool bloopEntityActive;

	private bool bloopEntityChasing;

	private float bloopAnimationPhase;

	private string bloopEntityStatus = "NONE";

	private static readonly Quaternion RakeModelAxisCorrection = Quaternion.Euler(-90f, 0f, 0f);

	private float rakeEncounterChance = 0.06f;

	private float rakeEncounterCooldownSeconds = 1200f;

	private float rakeEncounterCooldownTimer;

	private GameObject rakeEntityObject;

	private RakeEntityAnimation rakeEntityAnimation;

	private RakeBoneAnimation rakeBoneAnimation;

	private AudioSource rakeChaseAudio;

	private bool rakeChaseSoundLoaded;

	private bool rakeChaseSoundRequested;

	private CelestialBody rakeEntityBody;

	private Vector3d rakeEntityRelativePosition;

	private bool rakeEntityActive;

	private bool rakeEntityChasing;

	private bool rakeEntityStandingDown;

	private bool rakeEntityRetreating;

	private float rakeWithdrawalTimer;

	private float rakeChaseSurvivalTimer;

	private bool rakeEntityVisible;

	private float rakeObservationTimer;

	private float rakeAnimationPhase;

	private string rakeEntityStatus = "NONE";

	private GameObject groundEntityObject;

	private GroundEntityGlitchAnimation groundEntityAnimation;

	private MimicImportedAnimation groundEntityMimicAnimation;

	private bool groundEntityAggroPending;

	private AudioSource groundFootstepsAudio;

	private AudioSource groundScreamAudio;

	private bool groundFootstepsLoaded;

	private bool groundScreamLoaded;

	private CelestialBody groundEntityBody;

	private Vector3d groundEntityRelativePosition;

	private bool groundEntityActive;

	private bool groundEntityChasing;

	private float groundEntitySpottingTimer;

	private readonly HashSet<Guid> groundSeenVesselIds = new HashSet<Guid>();

	private bool groundEntityFleeing;

	private float groundEntityFleeTimer;

	private bool groundEntityStandingDown;

	private bool groundEntityRetreating;

	private float groundEntityWithdrawalTimer;

	private float groundEntityChaseSurvivalTimer;

	private GroundVariant groundVariant;

	private Vector3d groundRoamDirection;

	private float groundRoamDirectionTimer;

	private bool groundEntityDebugRadar = true;

	private bool debugGroundEntityBeacons;

	private GameObject mimicDebugBeacon;

	private GameObject bulkDebugBeacon;

	private GameObject laytheDebugBeacon;

	private GameObject dresSomethingDebugBeacon;

	private bool debugDisableWaterFog;

	private bool debugImmortality;

	private bool fogStateCaptured;

	private bool fogEnabledBeforeDebug;

	private string groundEntityStatus = "NONE";

	private bool unknownMapContacts = true;

	private readonly List<UnknownMapContact> mapContacts = new List<UnknownMapContact>();

	private ApplicationLauncherButton toolbarButton;

	private Texture2D toolbarIcon;

	private bool toolbarVisible;

	private bool trackerVisible = true;

	private Rect toolbarPanel = new Rect(18f, 285f, 250f, 320f);

	private Vector2 toolbarScrollPosition;

	private bool tufxGlitchStarted;

	private int glitchFramesRemaining;

	private Vector3d entityPosition;

	private CelestialBody entityReferenceBody;

	private Vector3d entityBodyRelativePosition;

	private Vector3d lastTrackingVesselPosition;

	private bool hasLastTrackingVesselPosition;

	private Vector3d orbitalTetherOffset;

	private bool orbitalTetherActive;

	private GameObject entitySoundObject;

	private AudioSource distantAudio;

	private AudioSource nearbyAudio;

	private AudioClip normalDistantClip;

	private AudioClip interplanetaryDistantClip;

	private AudioClip normalNearbyClip;

	private AudioClip interplanetaryNearbyClip;

	private Texture2D visualTexture;

	private Texture2D interplanetaryVisualTexture;

	private Texture2D eyeTexture;

	private Texture2D eyeBlinkTexture;

	private Texture2D eyeWarningTexture;

	private readonly Texture2D[] dresSomethingFrames = new Texture2D[3];

	private readonly Texture2D[] dresSomethingOpenFrames = new Texture2D[3];

	private readonly Texture2D[] dresSomethingHoldFrames = new Texture2D[2];

	private CelestialBody dresSomethingBody;

	private Vector3d dresSomethingRelativePosition;

	private bool dresSomethingActive;

	private bool dresSomethingDropping;

	private bool dresSomethingOpening;

	private float dresSomethingOpeningTimer;

	private float dresSomethingFadeTimer;

	private float dresSomethingFrameTimer;

	private GameObject dresDroppedPartObject;

	private Vector3d dresDroppedPartPosition;

	private bool dresDroppedPartLanded;

	private string dresSomethingStatus = "NONE";

	private GameObject dresSomethingBillboardObject;

	private Material dresSomethingBillboardMaterial;

	private GameObject dresSomethingSoundObject;

	private AudioSource dresSomethingOpenAudio;

	private AudioSource dresSomethingAmbientAudio;

	private bool dresSomethingOpenSoundLoaded;

	private bool dresSomethingAmbientSoundLoaded;

	private bool dresSomethingAmbientPausedByGame;

	private GameObject eyeBillboardObject;

	private Material eyeBillboardMaterial;

	private CelestialBody eyeLastObservedBody;

	private Vector3d eyeEntityPosition;

	private Vector3d eyeWatchDirection;

	private CelestialBody eyeChaseReferenceBody;

	private Vector3d eyeChaseRelativePosition;

	private Vector3d eyeChaseBaseVelocity;

	private bool eyeEntityActive;

	private bool eyeEntityChasing;

	private float eyeChaseTimer;

	private float eyeWarningTextTimer;

	private AudioSource eyeSpawnAudio;

	private AudioSource eyeWarningAudio;

	private AudioSource eyeChaseAudio;

	private AudioSource randomHorrorAudio;

	private bool randomHorrorLoaded;

	private float randomHorrorCheckTimer;

	private GameObject ambientSoundObject;

	private AudioSource kerbinAmbientAudio;

	private bool kerbinAmbientLoaded;

	private float kerbinAmbientCheckTimer;

	private readonly List<AudioClip> kerbinAmbientClips = new List<AudioClip>();

	private AudioSource laytheAmbientAudio;

	private bool laytheAmbientLoaded;

	private float laytheAmbientCheckTimer;

	private AudioSource eveAmbientAudio;

	private bool eveAmbientLoaded;

	private float eveAmbientCheckTimer;

	private bool kerbinAmbientPausedByGame;

	private bool laytheAmbientPausedByGame;

	private bool eveAmbientPausedByGame;

	private readonly Dictionary<AudioSource, float> mutedMusicSources = new Dictionary<AudioSource, float>();

	private bool eyeWarningSoundLoaded;

	private bool eyeSpawnSoundLoaded;

	private bool eyeChaseSoundLoaded;

	private Texture2D grabberIdleTexture;

	private Texture2D grabberGrabTexture;

	private GameObject grabberBillboardObject;

	private Material grabberBillboardMaterial;

	private CelestialBody grabberEntityBody;

	private Vector3d grabberEntityPosition;

	private Vector3d grabberVisualPosition;

	private Vector3d grabberRelativePosition;

	private Vector3d grabberBaseVelocity;

	private bool grabberEntityActive;

	private bool grabberEntityGrabbing;

	private bool grabberEntityFleeing;

	private bool grabberHasVisualPosition;

	private double grabberGrabStartOrbitalSpeed;

	private Vector3d grabberFleeDirection;

	private float grabberFleeTimer;

	private float grabberBreakoutTimer;

	private float grabberEncounterCooldownTimer;

	private string grabberEntityStatus = "NONE";

	private bool distantSoundLoaded;

	private bool nearbySoundLoaded;

	private bool blackboxScienceAwarded;

	private bool groundScienceAwarded;

	private bool touristScienceAwarded;

	private readonly Dictionary<ProtoCrewMember, float> courageBeforeTerror = new Dictionary<ProtoCrewMember, float>();

	private readonly Dictionary<KerbalEVA, EvaSpeedState> evaSpeedBeforePanic = new Dictionary<KerbalEVA, EvaSpeedState>();

	private float evaPanicSpeedCheckTimer;

	private bool crewTerrorActive;

	private bool bulkEvaScienceAwarded;

	private bool bulkSeismometerScienceAwarded;

	private bool seaScienceAwarded;

	private Rect panel = new Rect(18f, 120f, 275f, 145f);

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
		GameEvents.OnScienceRecieved.Add(OnScienceReceived);
		GameEvents.OnExperimentDeployed.Add(OnExperimentDeployed);
	}

	public void Start()
	{
		CreateWorldSoundSource();
		CreateUnknownMapContacts();
		if (ApplicationLauncher.Ready)
		{
			AddToolbarButton();
		}
		StartCoroutine(LoadEntitySound("DistantA120.mp3.mpeg", isDistantTrack: true, interplanetaryTrack: false));
		StartCoroutine(LoadEntitySound("orin-ayo-made-with-Voicemod.mp3", isDistantTrack: true, interplanetaryTrack: true));
		StartCoroutine(LoadEntitySound("NearbyA120.mp3.mpeg", isDistantTrack: false, interplanetaryTrack: false));
		StartCoroutine(LoadEntitySound("radio-glitch-sound-(loud)-made-with-Voicemod.mp3", isDistantTrack: false, interplanetaryTrack: true));
		StartCoroutine(LoadVisual());
		StartCoroutine(LoadInterplanetaryVisual());
		StartCoroutine(LoadEyeVisual("eye.png", blink: false));
		StartCoroutine(LoadEyeVisual("eye blink.png", blink: true));
		StartCoroutine(LoadEyeWarningVisual());
		StartCoroutine(LoadGrabberVisual("grabber-idle.png", grabbing: false));
		StartCoroutine(LoadGrabberVisual("grabber-grab.png", grabbing: true));
		StartCoroutine(LoadDresSomethingVisual("dres-something-1.png", 0));
		StartCoroutine(LoadDresSomethingVisual("dres-something-2.png", 1));
		StartCoroutine(LoadDresSomethingVisual("dres-something-3.png", 2));
		StartCoroutine(LoadDresSomethingVisual("dres-something-open-1.png", 0, opening: true));
		StartCoroutine(LoadDresSomethingVisual("dres-something-open-2.png", 1, opening: true));
		StartCoroutine(LoadDresSomethingVisual("dres-something-open-3.png", 2, opening: true));
		StartCoroutine(LoadDresSomethingVisual("dres-something-hold-1.png", 0, opening: false, holding: true));
		StartCoroutine(LoadDresSomethingVisual("dres-something-hold-2.png", 1, opening: false, holding: true));
		StartCoroutine(LoadDresSomethingSound("dres-something-open.mp3", dresSomethingOpenAudio, ambient: false));
		StartCoroutine(LoadDresSomethingSound("dres-something-ambient.wav", dresSomethingAmbientAudio, ambient: true));
		StartCoroutine(LoadEyeSpawnSound());
		StartCoroutine(LoadEyeSound("alex_jauk-horrifying-entity-scream-216936.mp3", isChaseSound: false));
		StartCoroutine(LoadEyeSound("eye-chase.mp3", isChaseSound: true));
		StartCoroutine(LoadRandomHorrorSound());
		StartCoroutine(LoadKerbinAmbientSound("dragon-studio-creepy-distant-footsteps-482883.mp3"));
		StartCoroutine(LoadKerbinAmbientSound("alex_jauk-distant-train-190312.mp3"));
		StartCoroutine(LoadKerbinAmbientSound("freesound_community-201110-distant-sirens-urban-night-quiet-roof-11pm-49901.mp3"));
		StartCoroutine(LoadLaytheAmbientSound());
		StartCoroutine(LoadEveAmbientSound());
	}

	public void Update()
	{
		UpdateWindowJolt();
		UpdateDebugFogOverride();
		countdownTextShakeTimer = Mathf.Max(0f, countdownTextShakeTimer - Time.unscaledDeltaTime);
		safeMessageTimer = Mathf.Max(0f, safeMessageTimer - Time.unscaledDeltaTime);
		eyeWarningTextTimer = Mathf.Max(0f, eyeWarningTextTimer - Time.unscaledDeltaTime);
		if (tracking && !HighLogic.LoadedSceneIsFlight)
		{
			EvadeEncounter();
		}
		if (groundEntityActive && !HighLogic.LoadedSceneIsFlight)
		{
			DespawnGroundEntity();
		}
		if (bulkEntityActive && !HighLogic.LoadedSceneIsFlight)
		{
			DespawnBulkEntity();
		}
		if (laytheCreatureActive && !HighLogic.LoadedSceneIsFlight)
		{
			DespawnLaytheCreature();
		}
		if (seaEntityActive && !HighLogic.LoadedSceneIsFlight)
		{
			DespawnSeaEntity();
		}
		if (bloopEntityActive && !HighLogic.LoadedSceneIsFlight)
		{
			DespawnBloopEntity();
		}
		if (rakeEntityActive && !HighLogic.LoadedSceneIsFlight)
		{
			DespawnRakeEntity();
		}
		if (eyeEntityActive && !HighLogic.LoadedSceneIsFlight)
		{
			DespawnEyeEntity();
		}
		if (grabberEntityActive && !HighLogic.LoadedSceneIsFlight)
		{
			DespawnGrabberEntity();
		}
		if (dresSomethingActive && !HighLogic.LoadedSceneIsFlight)
		{
			DespawnDresSomething();
		}
		UpdateRandomEncounters();
		UpdateAttackSequences();
		UpdateScares();
		UpdateGroundEntity();
		UpdateBulkEntity();
		UpdateLaytheCreature();
		UpdateGroundEntityDebugBeacons();
		UpdateSeaEntity();
		UpdateBloopEntity();
		UpdateRakeEntity();
		UpdateRakeReturnWait();
		UpdateEyeEntity();
		UpdateGrabberEntity();
		UpdateDresSomething();
		UpdateBDArmoryGpsTargets();
		UpdateBDArmoryGroundRadarTargets();
		UpdateBDArmoryWeaponDamage();
		UpdateRandomHorrorMusicEvent();
		UpdateAmbientPauseState();
		UpdateKerbinNightAmbience();
		UpdateLaytheNightAmbience();
		UpdateEveNightAmbience();
		UpdateProximityScreenShake();
		UpdateCrewTerror();
		UpdateEvaPanicSpeed();
		UpdateDread();
		if (!tracking || arrived || FlightGlobals.ActiveVessel == null)
		{
			UpdateSound();
			return;
		}
		if (Time.timeScale <= 0f)
		{
			UpdateSound();
			return;
		}
		Vessel activeVessel = FlightGlobals.ActiveVessel;
		RestoreEntityWorldPosition();
		Vector3d worldPos3D = activeVessel.GetWorldPos3D();
		if (IsOrbital(activeVessel))
		{
			if (!orbitalTetherActive)
			{
				orbitalTetherOffset = entityPosition - worldPos3D;
				orbitalTetherActive = true;
			}
			entityPosition = worldPos3D + orbitalTetherOffset;
		}
		else
		{
			orbitalTetherActive = false;
		}
		lastTrackingVesselPosition = worldPos3D;
		hasLastTrackingVesselPosition = true;
		Vector3d vector3d = worldPos3D - entityPosition;
		double magnitude = vector3d.magnitude;
		if (magnitude < closestEncounterDistance)
		{
			closestEncounterDistance = magnitude;
		}
		else if (magnitude >= closestEncounterDistance + (double)escapeDistanceGainMetres)
		{
			EndEncounter();
			return;
		}
		if (!survivalTimerStarted && magnitude <= 10000.0)
		{
			survivalTimerStarted = true;
		}
		if (survivalTimerStarted)
		{
			survivalTimer += TimeWarp.deltaTime;
			int num = Mathf.CeilToInt(survivalEscapeSeconds - survivalTimer);
			if (num > 0 && num <= 10 && num != lastCountdownSecond)
			{
				lastCountdownSecond = num;
				countdownTextShakeTimer = 0.22f;
				TriggerWindowJolt();
			}
			if (survivalTimer >= survivalEscapeSeconds)
			{
				safeMessageTimer = 10f;
				EndEncounter();
				return;
			}
		}
		if (!tufxGlitchStarted && magnitude <= 15.0 + (double)(approachSpeedMetresPerSecond * TimeWarp.deltaTime) * 3.0)
		{
			tufxGlitchStarted = true;
			glitchFramesRemaining = 3;
			if (tufxGlitchOnArrival)
			{
				TriggerArrivalGlitch();
			}
		}
		if (glitchFramesRemaining > 0)
		{
			glitchFramesRemaining--;
			if (glitchFramesRemaining <= 0)
			{
				entityPosition = worldPos3D;
				arrived = true;
				UpdateSound();
				HandleArrival();
			}
		}
		else if (magnitude <= 15.0)
		{
			entityPosition = worldPos3D;
			arrived = true;
			UpdateSound();
			HandleArrival();
		}
		else
		{
			double val = GetApproachSpeed(magnitude) * TimeWarp.deltaTime;
			entityPosition += vector3d.normalized * Math.Min(val, magnitude - 15.0);
			if (orbitalTetherActive)
			{
				orbitalTetherOffset = entityPosition - worldPos3D;
			}
			SaveEntityWorldPosition();
			UpdateSound();
		}
	}

	private float GetApproachSpeed(double distance)
	{
		float t = Mathf.InverseLerp(10000f, currentSpawnDistanceMetres, (float)distance);
		float num = Mathf.Lerp(approachSpeedMetresPerSecond, 1500f, t);
		num *= Mathf.Clamp(blackboxSpeedMultiplier, 0.05f, 20f);
		return IsInterplanetaryEncounter() ? (num * 40f) : num;
	}

	private static float GetPursuitSpeedMultiplier(Vessel vessel)
	{
		if (vessel == null)
		{
			return 1f;
		}
		if (IsInterplanetaryOrbit(vessel))
		{
			return 40f;
		}
		return IsOrbital(vessel) ? 10f : 1f;
	}

	private static bool IsOrbital(Vessel vessel)
	{
		return vessel != null && (vessel.situation == Vessel.Situations.ORBITING || vessel.situation == Vessel.Situations.ESCAPING || vessel.situation == Vessel.Situations.SUB_ORBITAL);
	}

	private static bool IsInterplanetaryOrbit(Vessel vessel)
	{
		return IsOrbital(vessel) && vessel.mainBody != null && Planetarium.fetch != null && vessel.mainBody == Planetarium.fetch.Sun;
	}

	private bool IsInterplanetaryEncounter()
	{
		return tracking && currentSpawnDistanceMetres >= 1000000f;
	}

	private static float GetCappedSpawnCheckDelta()
	{
		return Mathf.Min(TimeWarp.deltaTime, Time.unscaledDeltaTime * 10f);
	}

	private void UpdateProximityScreenShake()
	{
		if (Time.timeScale <= 0f || !HighLogic.LoadedSceneIsFlight)
		{
			return;
		}
		float num = 0f;
		Vessel activeVessel = FlightGlobals.ActiveVessel;
		if (activeVessel != null && groundEntityActive && groundEntityChasing && !groundEntityStandingDown && !groundEntityRetreating && groundEntityBody == activeVessel.mainBody)
		{
			Vector3d vector3d = groundEntityBody.position + groundEntityRelativePosition;
			num = Mathf.InverseLerp(350f, 2f, (float)(vector3d - activeVessel.GetWorldPos3D()).magnitude);
		}
		if (tracking && IsInterplanetaryEncounter() && activeVessel != null)
		{
			double magnitude = (entityPosition - activeVessel.GetWorldPos3D()).magnitude;
			num = Mathf.Max(num, Mathf.InverseLerp(50000f, 15f, (float)magnitude));
		}
		if (num <= 0f)
		{
			proximityJoltTimer = 0f;
			return;
		}
		proximityJoltTimer -= Time.unscaledDeltaTime;
		if (!(proximityJoltTimer > 0f) && joltFramesRemaining <= 0)
		{
			proximityJoltTimer = Mathf.Lerp(0.7f, 0.2f, num);
			TriggerWindowJolt();
		}
	}

	private void RestoreEntityWorldPosition()
	{
		if (entityReferenceBody != null)
		{
			entityPosition = entityReferenceBody.position + entityBodyRelativePosition;
		}
	}

	private void SaveEntityWorldPosition()
	{
		if (entityReferenceBody != null)
		{
			entityBodyRelativePosition = entityPosition - entityReferenceBody.position;
		}
	}

	public void OnGUI()
	{
		if (!HighLogic.LoadedSceneIsFlight)
		{
			return;
		}
		if (unknownMapContacts && MapView.MapIsEnabled)
		{
			DrawUnknownMapContacts();
		}
		if (toolbarVisible)
		{
			toolbarPanel = GUI.Window(windowId + 1, toolbarPanel, DrawToolbarPanel, "ENTITY TRACKER");
		}
		if (groundEntityDebugRadar && groundEntityActive)
		{
			DrawGroundEntityRadar();
		}
		if (groundEntityDebugRadar && bulkEntityActive)
		{
			DrawBulkEntityRadar();
		}
		if (groundEntityDebugRadar && laytheCreatureActive)
		{
			DrawLaytheCreatureRadar();
		}
		if (groundEntityDebugRadar && seaEntityActive)
		{
			DrawSeaEntityRadar();
		}
		if (groundEntityDebugRadar && bloopEntityActive)
		{
			DrawBloopEntityRadar();
		}
		if (groundEntityDebugRadar && dresSomethingActive)
		{
			DrawDresSomethingRadar();
		}
		if (groundEntityDebugRadar)
		{
			DrawSpawnDebugPanel();
		}
		DrawSurvivalCountdown();
		if (eyeEntityActive && !MapView.MapIsEnabled)
		{
			DrawEyeWarning();
			DrawEyeOverlay();
		}
		if (grabberEntityActive && !MapView.MapIsEnabled)
		{
			DrawGrabberOverlay();
		}
		if (dresSomethingActive && !MapView.MapIsEnabled)
		{
			DrawDresSomethingOverlay();
		}
		if (tracking || dresSomethingActive)
		{
			if (tracking)
			{
				DrawVisualOverlay();
			}
			if (debugTools && trackerVisible)
			{
				panel = GUI.Window(windowId, panel, DrawTracker, "ENTITY TRACKER");
			}
		}
	}

	private void DrawSurvivalCountdown()
	{
		GUIStyle gUIStyle = new GUIStyle(GUI.skin.label);
		gUIStyle.fontSize = Mathf.RoundToInt((float)Mathf.Min(Screen.width, Screen.height) * 0.35f);
		gUIStyle.fontStyle = FontStyle.Bold;
		gUIStyle.alignment = TextAnchor.MiddleCenter;
		if (survivalTimerStarted)
		{
			int num = Mathf.CeilToInt(survivalEscapeSeconds - survivalTimer);
			if (num > 0 && num <= 10)
			{
				Vector2 vector = ((countdownTextShakeTimer > 0f) ? (UnityEngine.Random.insideUnitCircle * 28f) : Vector2.zero);
				gUIStyle.normal.textColor = new Color(1f, 0f, 0f, 0.55f);
				GUI.Label(new Rect(vector.x, vector.y, Screen.width, Screen.height), num.ToString(), gUIStyle);
			}
		}
		if (safeMessageTimer > 0f)
		{
			float a = Mathf.Clamp01(safeMessageTimer / 10f) * 0.8f;
			gUIStyle.fontSize = Mathf.RoundToInt((float)Mathf.Min(Screen.width, Screen.height) * 0.24f);
			gUIStyle.normal.textColor = new Color(0.25f, 1f, 0.35f, a);
			GUI.Label(new Rect(0f, 0f, Screen.width, Screen.height), "SAFE", gUIStyle);
		}
	}

	private void DrawToolbarPanel(int id)
	{
		toolbarScrollPosition = GUILayout.BeginScrollView(toolbarScrollPosition, false, true);
		GUILayout.Label(tracking ? "SIGNAL ACTIVE" : "NO ACTIVE SIGNAL");
		if (debugTools)
		{
			Vessel activeVessel = FlightGlobals.ActiveVessel;
			if (GUILayout.Button((!tracking) ? "Force encounter" : (trackerVisible ? "Hide tracker" : "Show tracker")))
			{
				ToggleTracking();
			}
			bool flag = activeVessel != null && activeVessel.mainBody != null && string.Equals(activeVessel.mainBody.bodyName, "Kerbin", StringComparison.OrdinalIgnoreCase) && !activeVessel.Splashed && activeVessel.heightFromTerrain < 10f;
			bool flag2 = activeVessel != null && activeVessel.mainBody != null && IsBulkEntityBody(activeVessel.mainBody) && !activeVessel.Splashed && activeVessel.heightFromTerrain < 10f;
			bool flag3 = CanSeaEntityTarget(activeVessel);
			bool flag4 = activeVessel != null && activeVessel.mainBody != null && string.Equals(activeVessel.mainBody.bodyName, "Laythe", StringComparison.OrdinalIgnoreCase);
			bool flag5 = flag4 && activeVessel != null && !activeVessel.Splashed && activeVessel.heightFromTerrain < 10f;
			bool flag6 = activeVessel != null && activeVessel.mainBody != null && (string.Equals(activeVessel.mainBody.bodyName, "Jool", StringComparison.OrdinalIgnoreCase) || string.Equals(activeVessel.mainBody.bodyName, "Bop", StringComparison.OrdinalIgnoreCase));
			bool flag7 = IsEligibleForGrabberEncounter(activeVessel);
			bool flag8 = activeVessel != null && activeVessel.mainBody != null && string.Equals(activeVessel.mainBody.bodyName, "Dres", StringComparison.OrdinalIgnoreCase);
			if (flag && GUILayout.Button("Spawn ground entity"))
			{
				SpawnGroundEntity();
			}
			if (flag && GUILayout.Button("Spawn ground entity 200 m ahead"))
			{
				SpawnGroundEntityForTesting(activeVessel);
			}
			if (flag2 && GUILayout.Button("Spawn Moho/Eve entity"))
			{
				SpawnBulkEntity();
			}
			if (flag2 && GUILayout.Button("Spawn Moho/Eve entity 200 m ahead"))
			{
				SpawnBulkEntityForTesting(activeVessel);
			}
			if (flag3 && GUILayout.Button("Spawn Eve sea entity"))
			{
				SpawnSeaEntity();
			}
			if (flag3 && GUILayout.Button("Spawn Eve sea entity 200 m ahead"))
			{
				SpawnSeaEntityForTesting(activeVessel);
			}
			if (flag4 && GUILayout.Button("Spawn Laythe Bloop"))
			{
				SpawnBloopEntity(debugForce: true);
			}
			if (flag4 && GUILayout.Button("Spawn Laythe Bloop 200 m ahead"))
			{
				SpawnBloopEntityForTesting(activeVessel);
			}
			if (flag5 && GUILayout.Button("Spawn Laythe land creature"))
			{
				SpawnLaytheCreature();
			}
			if (flag5 && GUILayout.Button("Spawn Laythe land creature 200 m ahead"))
			{
				SpawnLaytheCreatureForTesting(activeVessel);
			}
			if (flag6 && GUILayout.Button("Spawn Jool Eye"))
			{
				ForceSpawnEyeEntity();
			}
			if (flag6 && GUILayout.Button("Spawn Jool Eye 200 m ahead"))
			{
				SpawnEyeEntityForTesting(activeVessel);
			}
			if (flag7 && GUILayout.Button("Spawn Grabber"))
			{
				SpawnGrabberEntity(activeVessel);
			}
			if (flag8 && GUILayout.Button("Spawn Dres anomaly"))
			{
				SpawnDresSomething(activeVessel);
			}
			DrawScareDebugButtons();
			DrawDreadDebugButtons();
			if (GUILayout.Button("Debug: force local ambient sound"))
			{
				ForceLocalAmbientSound();
			}
			groundEntityDebugRadar = GUILayout.Toggle(groundEntityDebugRadar, "Debug: entity radar / locator");
			debugGroundEntityBeacons = GUILayout.Toggle(debugGroundEntityBeacons, "Debug: ground-entity sky beacons");
			bool flag9 = GUILayout.Toggle(debugDisableWaterFog, "Debug: disable water fog");
			if (flag9 != debugDisableWaterFog)
			{
				SetDebugWaterFogDisabled(flag9);
			}
			debugImmortality = GUILayout.Toggle(debugImmortality, "Debug: immortality");
			if (GUILayout.Button("Debug: teleport to active monster"))
			{
				TeleportToActiveMonster();
			}
		}
		GUILayout.Label("GROUND: " + groundEntityStatus);
		GUILayout.Label("MOHO/EVE: " + bulkEntityStatus);
		GUILayout.Label("EVE OCEAN: " + seaEntityStatus);
		GUILayout.Label("LAYTHE BLOOP: " + bloopEntityStatus);
		GUILayout.Label("LAYTHE LAND: " + laytheCreatureStatus);
		DrawHuntDebugLabels();
		GUILayout.Label("JOOL EYE: " + ((!eyeEntityActive) ? "NONE" : (eyeEntityChasing ? "CHASING" : "WATCHING")));
		GUILayout.Label("GRABBER: " + grabberEntityStatus);
		GUILayout.Label("DRES ANOMALY: " + dresSomethingStatus);
		if (GUILayout.Button("Sea monster model credit: requital_0111"))
		{
			Application.OpenURL("https://sketchfab.com/requital_0111");
		}
		if (GUILayout.Button("Bloop model credit: titaniumammas69"))
		{
			Application.OpenURL("https://sketchfab.com/titaniumammas69");
		}
		if (GUILayout.Button("Leviathan model credit: \"Alien Fish (animated)\" by Jungle Jim, CC BY 4.0"))
		{
			Application.OpenURL("https://skfb.ly/pDVUJ");
		}
		if (GUILayout.Button("Laythe land creature model credit: Wonderful Optics Workshop"))
		{
			Application.OpenURL("https://skfb.ly/pHPoZ");
		}
		GUILayout.Space(4f);
		GUILayout.Label("Return to Space Center to evade.");
		GUILayout.EndScrollView();
		GUI.DragWindow();
	}

	private void UpdateGroundEntityDebugBeacons()
	{
		if (!debugTools || !debugGroundEntityBeacons)
		{
			DestroyGroundEntityDebugBeacons();
			return;
		}
		UpdateGroundEntityDebugBeacon(ref mimicDebugBeacon, groundEntityActive && groundEntityObject != null, (groundEntityObject != null) ? groundEntityObject.transform.position : Vector3.zero, groundEntityBody, new Color(1f, 0.05f, 0.75f, 0.9f));
		UpdateGroundEntityDebugBeacon(ref bulkDebugBeacon, bulkEntityActive && bulkEntityObject != null, (bulkEntityObject != null) ? bulkEntityObject.transform.position : Vector3.zero, bulkEntityBody, new Color(1f, 0.2f, 0.05f, 0.9f));
		UpdateGroundEntityDebugBeacon(ref laytheDebugBeacon, laytheCreatureActive && laytheCreatureObject != null, (laytheCreatureObject != null) ? laytheCreatureObject.transform.position : Vector3.zero, laytheCreatureBody, new Color(0.05f, 0.9f, 1f, 0.9f));
		UpdateGroundEntityDebugBeacon(ref dresSomethingDebugBeacon, dresSomethingActive && dresSomethingBody != null, (dresSomethingBody != null) ? ((Vector3)(dresSomethingBody.position + dresSomethingRelativePosition)) : Vector3.zero, dresSomethingBody, new Color(1f, 0.08f, 0.08f, 0.9f));
	}

	private static void UpdateGroundEntityDebugBeacon(ref GameObject beacon, bool active, Vector3 entityPosition, CelestialBody body, Color color)
	{
		if (!active || body == null)
		{
			if (beacon != null)
			{
				UnityEngine.Object.Destroy(beacon);
			}
			beacon = null;
			return;
		}
		if (beacon == null)
		{
			beacon = new GameObject("KrakenKreations_DebugGroundBeacon");
			LineRenderer lineRenderer = beacon.AddComponent<LineRenderer>();
			lineRenderer.useWorldSpace = true;
			lineRenderer.positionCount = 2;
			lineRenderer.startWidth = 2.5f;
			lineRenderer.endWidth = 16f;
			lineRenderer.shadowCastingMode = ShadowCastingMode.Off;
			lineRenderer.receiveShadows = false;
			Shader shader = Shader.Find("Legacy Shaders/Particles/Additive") ?? Shader.Find("Sprites/Default");
			lineRenderer.material = new Material(shader);
		}
		LineRenderer component = beacon.GetComponent<LineRenderer>();
		if (!(component == null))
		{
			Vector3 normalized = (entityPosition - (Vector3)body.position).normalized;
			component.startColor = color;
			component.endColor = new Color(color.r, color.g, color.b, 0.05f);
			component.SetPosition(0, entityPosition + normalized * 2f);
			component.SetPosition(1, entityPosition + normalized * 18000f);
		}
	}

	private void DestroyGroundEntityDebugBeacons()
	{
		if (mimicDebugBeacon != null)
		{
			UnityEngine.Object.Destroy(mimicDebugBeacon);
		}
		if (bulkDebugBeacon != null)
		{
			UnityEngine.Object.Destroy(bulkDebugBeacon);
		}
		if (laytheDebugBeacon != null)
		{
			UnityEngine.Object.Destroy(laytheDebugBeacon);
		}
		if (dresSomethingDebugBeacon != null)
		{
			UnityEngine.Object.Destroy(dresSomethingDebugBeacon);
		}
		mimicDebugBeacon = null;
		bulkDebugBeacon = null;
		laytheDebugBeacon = null;
		dresSomethingDebugBeacon = null;
	}

	private void DrawGroundEntityRadar()
	{
		Vessel activeVessel = FlightGlobals.ActiveVessel;
		if (!(activeVessel == null) && !(groundEntityBody == null))
		{
			Vector3d vector3d = groundEntityBody.position + groundEntityRelativePosition;
			Vector3 vector = activeVessel.transform.InverseTransformDirection(vector3d - activeVessel.GetWorldPos3D());
			double magnitude = (vector3d - activeVessel.GetWorldPos3D()).magnitude;
			Rect position = new Rect((float)Screen.width - 190f - 18f, (float)Screen.height - 190f - 18f, 190f, 190f);
			GUI.Box(position, "GROUND ENTITY DEBUG RADAR");
			Vector2 vector2 = new Vector2(position.x + 95f, position.y + 106f);
			GUI.Label(new Rect(vector2.x - 8f, vector2.y - 8f, 16f, 16f), "+");
			float magnitude2 = new Vector2(vector.x, vector.z).magnitude;
			float num = Mathf.Min(72f, magnitude2 * 72f / 18000f);
			Vector2 vector3 = ((magnitude2 > 0.01f) ? new Vector2(vector.x, vector.z).normalized : Vector2.zero);
			Vector2 vector4 = vector2 + new Vector2(vector3.x * num, (0f - vector3.y) * num);
			GUIStyle gUIStyle = new GUIStyle(GUI.skin.label);
			gUIStyle.fontSize = 22;
			gUIStyle.fontStyle = FontStyle.Bold;
			gUIStyle.normal.textColor = Color.magenta;
			gUIStyle.alignment = TextAnchor.MiddleCenter;
			GUI.Label(new Rect(vector4.x - 12f, vector4.y - 12f, 24f, 24f), "●", gUIStyle);
			GUIStyle gUIStyle2 = new GUIStyle(GUI.skin.label);
			gUIStyle2.fontSize = 11;
			gUIStyle2.normal.textColor = Color.white;
			gUIStyle2.alignment = TextAnchor.MiddleCenter;
			GUI.Label(new Rect(position.x + 8f, position.y + 145f, 174f, 18f), "RANGE " + FormatDistance(magnitude), gUIStyle2);
			GUI.Label(new Rect(position.x + 8f, position.y + 163f, 174f, 18f), groundEntityChasing ? "STATE: CHASING" : "STATE: DORMANT", gUIStyle2);
		}
	}

	private void DrawDresSomethingRadar()
	{
		Vessel activeVessel = FlightGlobals.ActiveVessel;
		if (!(activeVessel == null) && !(dresSomethingBody == null))
		{
			Vector3d vector3d = dresSomethingBody.position + dresSomethingRelativePosition;
			Vector3 vector = activeVessel.transform.InverseTransformDirection(vector3d - activeVessel.GetWorldPos3D());
			double magnitude = (vector3d - activeVessel.GetWorldPos3D()).magnitude;
			Rect position = new Rect((float)Screen.width - 190f - 18f, (float)Screen.height - 190f - 18f, 190f, 190f);
			GUI.Box(position, "DRES ANOMALY DEBUG RADAR");
			Vector2 vector2 = new Vector2(position.x + 95f, position.y + 106f);
			GUI.Label(new Rect(vector2.x - 8f, vector2.y - 8f, 16f, 16f), "+");
			float magnitude2 = new Vector2(vector.x, vector.z).magnitude;
			float num = Mathf.Min(72f, magnitude2 * 72f / 10000f);
			Vector2 vector3 = ((magnitude2 > 0.01f) ? new Vector2(vector.x, vector.z).normalized : Vector2.zero);
			Vector2 vector4 = vector2 + new Vector2(vector3.x * num, (0f - vector3.y) * num);
			GUIStyle gUIStyle = new GUIStyle(GUI.skin.label);
			gUIStyle.fontSize = 22;
			gUIStyle.fontStyle = FontStyle.Bold;
			gUIStyle.normal.textColor = Color.red;
			gUIStyle.alignment = TextAnchor.MiddleCenter;
			GUI.Label(new Rect(vector4.x - 12f, vector4.y - 12f, 24f, 24f), "●", gUIStyle);
			string text = (dresSomethingOpening ? "STATE: OPENING" : (dresSomethingDropping ? "STATE: FADING" : "STATE: WATCHING"));
			GUIStyle gUIStyle2 = new GUIStyle(GUI.skin.label);
			gUIStyle2.fontSize = 11;
			gUIStyle2.normal.textColor = Color.white;
			gUIStyle2.alignment = TextAnchor.MiddleCenter;
			GUI.Label(new Rect(position.x + 8f, position.y + 145f, 174f, 18f), "RANGE " + FormatDistance(magnitude), gUIStyle2);
			GUI.Label(new Rect(position.x + 8f, position.y + 163f, 174f, 18f), text, gUIStyle2);
		}
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
			GUI.Box(position, "MOHO/EVE ENTITY DEBUG RADAR");
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

	private void DrawLaytheCreatureRadar()
	{
		Vessel activeVessel = FlightGlobals.ActiveVessel;
		if (!(activeVessel == null) && !(laytheCreatureBody == null))
		{
			Vector3d vector3d = laytheCreatureBody.position + laytheCreatureRelativePosition;
			Vector3 vector = activeVessel.transform.InverseTransformDirection(vector3d - activeVessel.GetWorldPos3D());
			double magnitude = (vector3d - activeVessel.GetWorldPos3D()).magnitude;
			Rect position = new Rect((float)Screen.width - 190f - 18f, (float)Screen.height - 190f - 18f, 190f, 190f);
			GUI.Box(position, "LAYTHE LAND CREATURE RADAR");
			Vector2 vector2 = new Vector2(position.x + 95f, position.y + 106f);
			GUI.Label(new Rect(vector2.x - 8f, vector2.y - 8f, 16f, 16f), "+");
			float magnitude2 = new Vector2(vector.x, vector.z).magnitude;
			float num = Mathf.Min(72f, magnitude2 * 72f / 18000f);
			Vector2 vector3 = ((magnitude2 > 0.01f) ? new Vector2(vector.x, vector.z).normalized : Vector2.zero);
			Vector2 vector4 = vector2 + new Vector2(vector3.x * num, (0f - vector3.y) * num);
			GUIStyle gUIStyle = new GUIStyle(GUI.skin.label);
			gUIStyle.fontSize = 22;
			gUIStyle.fontStyle = FontStyle.Bold;
			gUIStyle.normal.textColor = Color.yellow;
			gUIStyle.alignment = TextAnchor.MiddleCenter;
			GUI.Label(new Rect(vector4.x - 12f, vector4.y - 12f, 24f, 24f), "*", gUIStyle);
			GUIStyle gUIStyle2 = new GUIStyle(GUI.skin.label);
			gUIStyle2.fontSize = 11;
			gUIStyle2.normal.textColor = Color.white;
			gUIStyle2.alignment = TextAnchor.MiddleCenter;
			GUI.Label(new Rect(position.x + 8f, position.y + 145f, 174f, 18f), "RANGE " + FormatDistance(magnitude), gUIStyle2);
			GUI.Label(new Rect(position.x + 8f, position.y + 163f, 174f, 18f), laytheCreatureChasing ? "STATE: CHASING" : "STATE: DORMANT", gUIStyle2);
		}
	}

	private void DrawSeaEntityRadar()
	{
		Vessel activeVessel = FlightGlobals.ActiveVessel;
		if (!(activeVessel == null) && !(seaEntityBody == null))
		{
			Vector3d vector3d = seaEntityBody.position + seaEntityRelativePosition;
			Vector3 vector = activeVessel.transform.InverseTransformDirection(vector3d - activeVessel.GetWorldPos3D());
			double magnitude = (vector3d - activeVessel.GetWorldPos3D()).magnitude;
			Rect position = new Rect((float)Screen.width - 190f - 18f, (float)Screen.height - 190f - 18f, 190f, 190f);
			GUI.Box(position, "EVE OCEAN ENTITY RADAR");
			Vector2 vector2 = new Vector2(position.x + 95f, position.y + 106f);
			GUI.Label(new Rect(vector2.x - 8f, vector2.y - 8f, 16f, 16f), "+");
			float magnitude2 = new Vector2(vector.x, vector.z).magnitude;
			float num = Mathf.Min(72f, magnitude2 * 72f / 2200f);
			Vector2 vector3 = ((magnitude2 > 0.01f) ? new Vector2(vector.x, vector.z).normalized : Vector2.zero);
			Vector2 vector4 = vector2 + new Vector2(vector3.x * num, (0f - vector3.y) * num);
			GUIStyle gUIStyle = new GUIStyle(GUI.skin.label);
			gUIStyle.fontSize = 22;
			gUIStyle.fontStyle = FontStyle.Bold;
			gUIStyle.normal.textColor = new Color(0.15f, 0.8f, 0.9f);
			gUIStyle.alignment = TextAnchor.MiddleCenter;
			GUI.Label(new Rect(vector4.x - 12f, vector4.y - 12f, 24f, 24f), "â—\u008f", gUIStyle);
			GUIStyle gUIStyle2 = new GUIStyle(GUI.skin.label);
			gUIStyle2.fontSize = 11;
			gUIStyle2.normal.textColor = Color.white;
			gUIStyle2.alignment = TextAnchor.MiddleCenter;
			GUI.Label(new Rect(position.x + 8f, position.y + 145f, 174f, 18f), "RANGE " + FormatDistance(magnitude), gUIStyle2);
			GUI.Label(new Rect(position.x + 8f, position.y + 163f, 174f, 18f), seaEntityChasing ? "STATE: CHASING" : "STATE: DORMANT", gUIStyle2);
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
			GUI.Box(position, "LAYTHE BLOOP RADAR");
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

	private void DrawRakeEntityRadar()
	{
		Vessel activeVessel = FlightGlobals.ActiveVessel;
		if (!(activeVessel == null) && !(rakeEntityBody == null))
		{
			Vector3d vector3d = rakeEntityBody.position + rakeEntityRelativePosition;
			Vector3 vector = activeVessel.transform.InverseTransformDirection(vector3d - activeVessel.GetWorldPos3D());
			double magnitude = (vector3d - activeVessel.GetWorldPos3D()).magnitude;
			Rect position = new Rect((float)Screen.width - 190f - 18f, (float)Screen.height - 190f - 18f, 190f, 190f);
			GUI.Box(position, "RAKE DEBUG RADAR");
			Vector2 vector2 = new Vector2(position.x + 95f, position.y + 106f);
			GUI.Label(new Rect(vector2.x - 8f, vector2.y - 8f, 16f, 16f), "+");
			float magnitude2 = new Vector2(vector.x, vector.z).magnitude;
			float num = Mathf.Min(72f, magnitude2 * 72f / 18000f);
			Vector2 vector3 = ((magnitude2 > 0.01f) ? new Vector2(vector.x, vector.z).normalized : Vector2.zero);
			Vector2 vector4 = vector2 + new Vector2(vector3.x * num, (0f - vector3.y) * num);
			GUIStyle gUIStyle = new GUIStyle(GUI.skin.label);
			gUIStyle.fontSize = 22;
			gUIStyle.fontStyle = FontStyle.Bold;
			gUIStyle.normal.textColor = new Color(0.8f, 0.8f, 0.8f);
			gUIStyle.alignment = TextAnchor.MiddleCenter;
			GUI.Label(new Rect(vector4.x - 12f, vector4.y - 12f, 24f, 24f), "â—\u008f", gUIStyle);
			GUIStyle gUIStyle2 = new GUIStyle(GUI.skin.label);
			gUIStyle2.fontSize = 11;
			gUIStyle2.normal.textColor = Color.white;
			gUIStyle2.alignment = TextAnchor.MiddleCenter;
			GUI.Label(new Rect(position.x + 8f, position.y + 145f, 174f, 18f), "RANGE " + FormatDistance(magnitude), gUIStyle2);
			GUI.Label(new Rect(position.x + 8f, position.y + 163f, 174f, 18f), rakeEntityChasing ? "STATE: CHASING" : "STATE: WATCHING", gUIStyle2);
		}
	}

	private void DrawSpawnDebugPanel()
	{
		Rect position = new Rect(18f, (float)Screen.height - 198f, 260f, 180f);
		GUI.Box(position, "ENTITY SPAWN DEBUG");
		Vessel activeVessel = FlightGlobals.ActiveVessel;
		float f = Mathf.Max(0f, randomCheckSeconds - encounterCheckTimer);
		GUI.Label(new Rect(position.x + 10f, position.y + 25f, 240f, 18f), "NEXT SHARED CHECK: " + Mathf.CeilToInt(f) + " s");
		GUI.Label(new Rect(position.x + 10f, position.y + 43f, 240f, 18f), "BLACK BOX COOLDOWN: " + Mathf.CeilToInt(encounterCooldownTimer) + " s");
		GUI.Label(new Rect(position.x + 10f, position.y + 61f, 240f, 18f), "GROUND COOLDOWN: " + Mathf.CeilToInt(groundEncounterCooldownTimer) + " s");
		if (activeVessel == null || activeVessel.mainBody == null)
		{
			GUI.Label(new Rect(position.x + 10f, position.y + 87f, 240f, 18f), "NO ACTIVE VESSEL");
			return;
		}
		bool flag = string.Equals(activeVessel.mainBody.bodyName, "Kerbin", StringComparison.OrdinalIgnoreCase);
		bool flag2 = !activeVessel.Splashed && activeVessel.heightFromTerrain < 10f;
		string currentBiomeName = GetCurrentBiomeName(activeVessel);
		bool flag3 = !IsBlockedGroundBiome(currentBiomeName);
		bool flag4 = IsNightAtVessel(activeVessel);
		GUI.Label(new Rect(position.x + 10f, position.y + 87f, 240f, 18f), "KERBIN: " + DebugState(flag) + "   GROUND: " + DebugState(flag2));
		GUI.Label(new Rect(position.x + 10f, position.y + 105f, 240f, 18f), "BIOME (" + currentBiomeName + "): " + DebugState(flag3));
		GUI.Label(new Rect(position.x + 10f, position.y + 123f, 240f, 18f), "NIGHT: " + DebugState(flag4));
		bool flag5 = flag && flag2 && flag3 && flag4 && !tracking && !groundEntityActive && groundEncounterCooldownTimer <= 0f;
		GUI.Label(new Rect(position.x + 10f, position.y + 149f, 240f, 18f), "GROUND SPAWN: " + (flag5 ? "ELIGIBLE" : "BLOCKED"));
	}

	private static string DebugState(bool state)
	{
		return state ? "OK" : "BLOCKED";
	}

	private void AddToolbarButton()
	{
		if (!(toolbarButton != null) && !(ApplicationLauncher.Instance == null))
		{
			toolbarIcon = new Texture2D(38, 38, TextureFormat.RGBA32, mipChain: false);
			Color[] array = new Color[1444];
			for (int i = 0; i < array.Length; i++)
			{
				ref Color reference = ref array[i];
				reference = Color.black;
			}
			toolbarIcon.SetPixels(array);
			toolbarIcon.Apply();
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
	}

	private void EvadeEncounter()
	{
		EndEncounter();
	}

	private void EndEncounter()
	{
		tracking = false;
		arrived = false;
		arrivalHandled = false;
		tufxGlitchStarted = false;
		glitchFramesRemaining = 0;
		entityPosition = Vector3d.zero;
		entityReferenceBody = null;
		entityBodyRelativePosition = Vector3d.zero;
		lastTrackingVesselPosition = Vector3d.zero;
		hasLastTrackingVesselPosition = false;
		orbitalTetherOffset = Vector3d.zero;
		orbitalTetherActive = false;
		currentSpawnDistanceMetres = 50000f;
		closestEncounterDistance = 0.0;
		survivalTimerStarted = false;
		survivalTimer = 0f;
		lastCountdownSecond = -1;
		blackboxScienceAwarded = false;
		RestoreWindowPosition();
		encounterCooldownTimer = Mathf.Max(encounterCooldownTimer, randomCooldownSeconds);
		UpdateSound();
	}

	private void OnGameStateLoaded(ConfigNode gameState)
	{
		EndEncounter();
		DespawnGroundEntity();
		DespawnBulkEntity();
		DespawnLaytheCreature();
		DespawnSeaEntity();
		DespawnBloopEntity();
		DespawnRakeEntity();
		DespawnEyeEntity();
		DespawnGrabberEntity();
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
		eyeLastObservedBody = null;
		safeMessageTimer = 0f;
		countdownTextShakeTimer = 0f;
		trackerVisible = false;
	}

	private void OnScienceReceived(float science, ScienceSubject subject, ProtoVessel vessel, bool transmitted)
	{
		if (subject == null || string.IsNullOrEmpty(subject.id))
		{
			return;
		}
		bool flag = subject.id.StartsWith("evaReport@", StringComparison.OrdinalIgnoreCase);
		bool flag2 = subject.id.StartsWith("seismicScan@", StringComparison.OrdinalIgnoreCase);
		if (flag)
		{
			if (groundEntityActive && groundEntityChasing)
			{
				AwardEncounterScience(ref groundScienceAwarded, "EVA REPORT\nGET OUT OF THERE NOW!", 8f);
			}
			else if (IsTouristMimicNearby())
			{
				AwardEncounterScience(ref touristScienceAwarded, "EVA REPORT\nThe Tourist waves at you. Its visor is completely empty.", 4f);
			}
			else if (bulkEntityActive)
			{
				AwardEncounterScience(ref bulkEvaScienceAwarded, bulkEntityChasing ? "EVA REPORT\nRUN!" : "EVA REPORT\nThere's something nearby.", 8f);
			}
			else if (seaEntityActive)
			{
				AwardEncounterScience(ref seaScienceAwarded, "EVA REPORT\nNothing seems out of the ordinary...", 6f);
			}
			else if (tracking)
			{
				AwardEncounterScience(ref blackboxScienceAwarded, "EVA REPORT\nTo mission control, I see something. It's like a... black box?\n\nMission Control: I'm looking at your suit's camera. I don't see anything. You might just be going crazy.", 8f);
			}
		}
		else if (flag2 && bulkEntityActive)
		{
			AwardEncounterScience(ref bulkSeismometerScienceAwarded, bulkEntityChasing ? "SEISMOMETER REPORT\nYou should really be running instead of doing science scans." : "SEISMOMETER REPORT\nThe seismometer detects rhythmic pulses of seismic activity, definitely not natural.", 10f);
		}
	}

	private void OnExperimentDeployed(ScienceData data)
	{
		if (data == null || string.IsNullOrEmpty(data.subjectID))
		{
			return;
		}
		bool flag = data.subjectID.StartsWith("evaReport@", StringComparison.OrdinalIgnoreCase);
		bool flag2 = data.subjectID.StartsWith("seismicScan@", StringComparison.OrdinalIgnoreCase);
		if (flag)
		{
			if (groundEntityActive && groundEntityChasing)
			{
				data.title += " (While getting chased by mimic)";
				ApplyEncounterExperimentResult(data, ref groundScienceAwarded, "GET OUT OF THERE NOW!", 8f);
			}
			else if (IsTouristMimicNearby())
			{
				data.title += " (Near Tourist mimic)";
				ApplyEncounterExperimentResult(data, ref touristScienceAwarded, "The Tourist waves at you. Its visor is completely empty.", 4f);
			}
			else if (bulkEntityActive)
			{
				ApplyEncounterExperimentResult(data, ref bulkEvaScienceAwarded, bulkEntityChasing ? "RUN!" : "There's something nearby.", 8f);
			}
			else if (seaEntityActive)
			{
				ApplyEncounterExperimentResult(data, ref seaScienceAwarded, "Nothing seems out of the ordinary...", 6f);
			}
			else if (tracking)
			{
				ApplyEncounterExperimentResult(data, ref blackboxScienceAwarded, "To mission control, I see something. It's like a... black box?\n\nI'm looking at your suit's camera. I don't see anything. You might just be going crazy.", 8f);
			}
		}
		else if (flag2 && bulkEntityActive)
		{
			ApplyEncounterExperimentResult(data, ref bulkSeismometerScienceAwarded, bulkEntityChasing ? "You should really be running instead of doing science scans." : "The seismometer detects rhythmic pulses of seismic activity, definitely not natural.", 10f);
		}
	}

	private static void ApplyEncounterExperimentResult(ScienceData data, ref bool awarded, string report, float bonusScience)
	{
		data.extraResultString = report;
		AwardEncounterScience(ref awarded, report, bonusScience);
	}

	private static void AwardEncounterScience(ref bool awarded, string report, float bonusScience)
	{
		if (!awarded)
		{
			awarded = true;
			if (ResearchAndDevelopment.Instance != null)
			{
				ResearchAndDevelopment.Instance.AddScience(bonusScience, TransactionReasons.ScienceTransmission);
			}
			ScreenMessages.PostScreenMessage(report + "\n+" + bonusScience.ToString("F0") + " science", 8f, ScreenMessageStyle.UPPER_CENTER);
		}
	}

	private bool IsTouristMimicNearby()
	{
		if (!groundEntityActive || groundVariant != GroundVariant.Tourist || groundEntityBody == null)
		{
			return false;
		}
		Vessel activeVessel = FlightGlobals.ActiveVessel;
		if (activeVessel == null || activeVessel.mainBody != groundEntityBody)
		{
			return false;
		}
		Vector3d vector3d = groundEntityBody.position + groundEntityRelativePosition;
		return (activeVessel.GetWorldPos3D() - vector3d).magnitude <= 100.0;
	}

	private void UpdateCrewTerror()
	{
		if ((!tracking || arrived) && (!groundEntityChasing || groundEntityStandingDown || groundEntityRetreating) && (!bulkEntityChasing || bulkEntityStandingDown || bulkEntityRetreating) && !seaEntityChasing && (!rakeEntityChasing || rakeEntityStandingDown || rakeEntityRetreating))
		{
			crewTerrorActive = false;
			RestoreCrewCourage();
			return;
		}
		Vessel activeVessel = FlightGlobals.ActiveVessel;
		if (activeVessel == null)
		{
			return;
		}
		foreach (ProtoCrewMember item in activeVessel.GetVesselCrew())
		{
			if (item != null)
			{
				if (!courageBeforeTerror.ContainsKey(item))
				{
					courageBeforeTerror.Add(item, item.courage);
				}
				item.courage = 0f;
			}
		}
		crewTerrorActive = true;
		ForceTerrifiedExpressions(activeVessel);
	}

	public void LateUpdate()
	{
		if (crewTerrorActive && FlightGlobals.ActiveVessel != null)
		{
			ForceTerrifiedExpressions(FlightGlobals.ActiveVessel);
		}
	}

	private static void ForceTerrifiedExpressions(Vessel vessel)
	{
		if (vessel == null || vessel.parts == null)
		{
			return;
		}
		foreach (Part part in vessel.parts)
		{
			if (!(part == null))
			{
				ForceTerrifiedExpressions(part.gameObject.GetComponentsInChildren<kerbalExpressionSystem>(includeInactive: true));
				if (part.internalModel != null)
				{
					ForceTerrifiedExpressions(part.internalModel.gameObject.GetComponentsInChildren<kerbalExpressionSystem>(includeInactive: true));
				}
			}
		}
	}

	private static void ForceTerrifiedExpressions(kerbalExpressionSystem[] systems)
	{
		foreach (kerbalExpressionSystem kerbalExpressionSystem2 in systems)
		{
			if (!(kerbalExpressionSystem2 == null))
			{
				if (ExpressionPanicLevelField != null)
				{
					ExpressionPanicLevelField.SetValue(kerbalExpressionSystem2, 1f);
				}
				if (ExpressionFearFactorField != null)
				{
					ExpressionFearFactorField.SetValue(kerbalExpressionSystem2, 1f);
				}
				if (ExpressionWheeLevelField != null)
				{
					ExpressionWheeLevelField.SetValue(kerbalExpressionSystem2, 0f);
				}
				if (ExpressionTargetField != null)
				{
					ExpressionTargetField.SetValue(kerbalExpressionSystem2, -1f);
				}
				if (ExpressionCurrentField != null)
				{
					ExpressionCurrentField.SetValue(kerbalExpressionSystem2, -1f);
				}
			}
		}
	}

	private void RestoreCrewCourage()
	{
		foreach (KeyValuePair<ProtoCrewMember, float> item in courageBeforeTerror)
		{
			if (item.Key != null)
			{
				item.Key.courage = item.Value;
			}
		}
		courageBeforeTerror.Clear();
	}

	private void UpdateEvaPanicSpeed()
	{
		evaPanicSpeedCheckTimer -= Time.unscaledDeltaTime;
		if (evaPanicSpeedCheckTimer > 0f)
		{
			return;
		}
		evaPanicSpeedCheckTimer = 2f;
		if (!tracking && !groundEntityActive && !bulkEntityActive && !seaEntityActive && !rakeEntityActive)
		{
			RestoreEvaPanicSpeed();
			return;
		}
		Vessel activeVessel = FlightGlobals.ActiveVessel;
		if (activeVessel == null || activeVessel.parts == null)
		{
			return;
		}
		foreach (Part part in activeVessel.parts)
		{
			if (part == null)
			{
				continue;
			}
			KerbalEVA kerbalEVA = part.FindModuleImplementing<KerbalEVA>();
			if (!(kerbalEVA == null) && !(EvaRunSpeedField == null) && !(EvaBoundSpeedField == null))
			{
				if (!evaSpeedBeforePanic.TryGetValue(kerbalEVA, out var value))
				{
					EvaSpeedState evaSpeedState = new EvaSpeedState();
					evaSpeedState.runSpeed = (float)EvaRunSpeedField.GetValue(kerbalEVA);
					evaSpeedState.boundSpeed = (float)EvaBoundSpeedField.GetValue(kerbalEVA);
					value = evaSpeedState;
					evaSpeedBeforePanic.Add(kerbalEVA, value);
				}
				EvaRunSpeedField.SetValue(kerbalEVA, value.runSpeed * 1.5f);
				EvaBoundSpeedField.SetValue(kerbalEVA, value.boundSpeed * 1.5f);
			}
		}
	}

	private void RestoreEvaPanicSpeed()
	{
		foreach (KeyValuePair<KerbalEVA, EvaSpeedState> item in evaSpeedBeforePanic)
		{
			if (!(item.Key == null))
			{
				if (EvaRunSpeedField != null)
				{
					EvaRunSpeedField.SetValue(item.Key, item.Value.runSpeed);
				}
				if (EvaBoundSpeedField != null)
				{
					EvaBoundSpeedField.SetValue(item.Key, item.Value.boundSpeed);
				}
			}
		}
		evaSpeedBeforePanic.Clear();
	}

	private void SpawnGroundEntity()
	{
		SpawnGroundEntity(PickGroundVariant(), naturalSpawn: false);
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

	private void SpawnGroundEntityForTesting(Vessel vessel)
	{
		SpawnGroundEntity();
		if (groundEntityActive && !(groundEntityBody == null))
		{
			Vector3d vector3d = ProjectToGround(GetTestSpawnAhead(vessel, groundEntityBody), groundEntityBody);
			groundEntityRelativePosition = vector3d - groundEntityBody.position;
			if (groundEntityObject != null)
			{
				groundEntityObject.transform.position = vector3d;
			}
		}
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

	private void SpawnLaytheCreatureForTesting(Vessel vessel)
	{
		SpawnLaytheCreature();
		if (laytheCreatureActive && !(laytheCreatureBody == null))
		{
			Vector3d vector3d = ProjectToGround(GetTestSpawnAhead(vessel, laytheCreatureBody), laytheCreatureBody);
			laytheCreatureRelativePosition = vector3d - laytheCreatureBody.position;
			if (laytheCreatureObject != null)
			{
				laytheCreatureObject.transform.position = vector3d;
			}
		}
	}

	private void SpawnSeaEntityForTesting(Vessel vessel)
	{
		SpawnSeaEntity();
		if (seaEntityActive && !(seaEntityBody == null))
		{
			Vector3d vector3d = ProjectToEveSea(GetTestSpawnAhead(vessel, seaEntityBody), seaEntityBody);
			seaEntityRelativePosition = vector3d - seaEntityBody.position;
			if (seaEntityObject != null)
			{
				seaEntityObject.transform.position = vector3d;
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

	private void SpawnEyeEntityForTesting(Vessel vessel)
	{
		ForceSpawnEyeEntity();
		if (eyeEntityActive && vessel != null && vessel.mainBody != null)
		{
			eyeEntityPosition = GetTestSpawnAhead(vessel, vessel.mainBody);
		}
	}

	private static GroundVariant PickGroundVariant()
	{
		float value = UnityEngine.Random.value;
		if (value < 0.45f)
		{
			return GroundVariant.Pilot;
		}
		if (value < 0.9f)
		{
			return GroundVariant.Engineer;
		}
		if (value < 0.95f)
		{
			return GroundVariant.Scientist;
		}
		return GroundVariant.Tourist;
	}

	private void SpawnGroundEntity(GroundVariant variant, bool naturalSpawn)
	{
		Vessel activeVessel = FlightGlobals.ActiveVessel;
		if (activeVessel == null || activeVessel.mainBody == null || activeVessel.heightFromTerrain >= 10f)
		{
			groundEntityStatus = "REQUIRES GROUND";
			return;
		}
		if (!string.Equals(activeVessel.mainBody.bodyName, "Kerbin", StringComparison.OrdinalIgnoreCase))
		{
			groundEntityStatus = "KERBIN ONLY";
			return;
		}
		if (naturalSpawn && !IsNightAtVessel(activeVessel))
		{
			groundEntityStatus = "NIGHT REQUIRED";
			return;
		}
		if (naturalSpawn && variant != GroundVariant.Engineer && !IsEligibleForGroundEncounter(activeVessel))
		{
			groundEntityStatus = "CONDITIONS BLOCKED";
			return;
		}
		DespawnGroundEntity();
		groundVariant = variant;
		groundEntityBody = activeVessel.mainBody;
		Vector3 normalized = (activeVessel.transform.forward * UnityEngine.Random.Range(-1f, 1f) + activeVessel.transform.right * UnityEngine.Random.Range(-1f, 1f)).normalized;
		if (normalized.sqrMagnitude < 0.01f)
		{
			normalized = activeVessel.transform.forward.normalized;
		}
		Vector3d vector3d = Vector3d.zero;
		bool flag = false;
		for (int i = 0; i < 24; i++)
		{
			Vector3 vector = ((i == 0) ? normalized : (activeVessel.transform.forward * UnityEngine.Random.Range(-1f, 1f) + activeVessel.transform.right * UnityEngine.Random.Range(-1f, 1f)).normalized);
			if (vector.sqrMagnitude < 0.01f)
			{
				vector = activeVessel.transform.forward.normalized;
			}
			Vector3d worldPosition = activeVessel.GetWorldPos3D() + (Vector3d)(vector * UnityEngine.Random.Range(6000f, 18000f));
			vector3d = ProjectToGround(worldPosition, groundEntityBody);
			if (IsLandAt(vector3d, groundEntityBody) && (vector3d - activeVessel.GetWorldPos3D()).magnitude >= 6000.0)
			{
				flag = true;
				break;
			}
		}
		if (!flag)
		{
			groundEntityBody = null;
			groundEntityStatus = "NO VALID SPAWN POSITION";
			return;
		}
		groundEntityRelativePosition = vector3d - groundEntityBody.position;
		groundEntityObject = CreateGroundEntityVisual(groundVariant);
		if (groundEntityObject == null)
		{
			groundEntityBody = null;
			groundEntityRelativePosition = Vector3d.zero;
			groundEntityStatus = "KERBAL MODEL UNAVAILABLE";
			return;
		}
		groundEntityObject.transform.position = vector3d;
		groundEntityAnimation = groundEntityObject.GetComponent<GroundEntityGlitchAnimation>();
		groundEntityMimicAnimation = groundEntityObject.GetComponent<MimicImportedAnimation>();
		if (groundVariant == GroundVariant.Pilot || groundVariant == GroundVariant.Engineer)
		{
			groundFootstepsAudio = ConfigureGroundAudioSource(groundEntityObject.AddComponent<AudioSource>(), 150f, 0.85f);
			groundScreamAudio = ConfigureGroundAudioSource(groundEntityObject.AddComponent<AudioSource>(), 2200f, 1f);
			AudioSource audioSource = ConfigureGroundAudioSource(groundEntityObject.AddComponent<AudioSource>(), 5000f, 1f);
			audioSource.loop = false;
			StartCoroutine(LoadGroundSound("seek-footsteps-made-with-Voicemod.mp3", groundFootstepsAudio, isFootsteps: true));
			StartCoroutine(LoadGroundSound("mimic-chase.mp3", groundScreamAudio, isFootsteps: false));
			StartCoroutine(LoadGroundSpawnSound("horror-thump-made-with-Voicemod.mp3", audioSource));
		}
		groundEntityActive = true;
		groundScienceAwarded = false;
		touristScienceAwarded = false;
		groundEntityChasing = false;
		groundEntitySpottingTimer = 0f;
		groundSeenVesselIds.Clear();
		groundEntityFleeing = false;
		groundEntityFleeTimer = 0f;
		groundEntityStandingDown = false;
		groundEntityRetreating = false;
		groundEntityWithdrawalTimer = 0f;
		groundEntityChaseSurvivalTimer = 0f;
		ChooseGroundRoamDirection(vector3d);
		groundEntityStatus = ((groundVariant == GroundVariant.Scientist) ? "SCIENTIST: WATCHING" : ((groundVariant == GroundVariant.Tourist) ? "TOURIST: WANDERING" : string.Concat(groundVariant, ": ROAMING")));
	}

	private void BeginGroundEntityAggro()
	{
		groundEntitySpottingTimer = 0f;
		if (groundEntityMimicAnimation == null || !groundEntityMimicAnimation.HasAggroClip)
		{
			groundEntityChasing = true;
			return;
		}
		groundEntityAggroPending = true;
		groundEntityMimicAnimation.BeginAggro();
	}

	private void UpdateGroundEntity()
	{
		if (!groundEntityActive || groundEntityObject == null || groundEntityBody == null)
		{
			return;
		}
		if (IsAttackSequenceRunning(AttackKind.Mimic))
		{
			return;
		}
		Vessel vessel = FlightGlobals.ActiveVessel;
		if (vessel == null)
		{
			return;
		}
		if (vessel.mainBody != groundEntityBody)
		{
			DespawnGroundEntity();
			return;
		}
		Vector3d vector3d = groundEntityBody.position + groundEntityRelativePosition;
		double magnitude = (vessel.GetWorldPos3D() - vector3d).magnitude;
		bool flag = vessel.heightFromTerrain < 10f;
		if (groundVariant == GroundVariant.Scientist)
		{
			UpdateScientistEntity(vessel, vector3d, magnitude, flag);
			return;
		}
		if (groundVariant == GroundVariant.Tourist)
		{
			UpdateTouristEntity(vessel, vector3d, flag);
			return;
		}
		bool huntAware = huntAi && HuntSense(huntMimic, groundEntityObject, vector3d, groundEntityBody, vessel, Quaternion.identity);
		if (!groundEntityChasing && !groundEntityAggroPending && !groundEntityStandingDown && !groundEntityRetreating)
		{
			if (huntAi)
			{
				groundEntitySpottingTimer = 0f;
				if (huntAware)
				{
					BeginGroundEntityAggro();
				}
			}
			else if (classicMimicBehavior)
			{
				if (!flag || !(magnitude <= 16000.0) || !HasGroundLineOfSight(vector3d, vessel.GetWorldPos3D(), groundEntityBody))
				{
					groundEntitySpottingTimer = 0f;
				}
				else
				{
					float groundSpottingTime = GetGroundSpottingTime(magnitude);
					if (groundSpottingTime <= 0f)
					{
						BeginGroundEntityAggro();
					}
					else
					{
						if (Time.timeScale > 0f)
						{
							groundEntitySpottingTimer += TimeWarp.deltaTime;
						}
						if (groundEntitySpottingTimer >= groundSpottingTime)
						{
							BeginGroundEntityAggro();
							groundEntitySpottingTimer = 0f;
						}
						else
						{
							groundEntityStatus = "SPOTTING " + Mathf.CeilToInt(groundSpottingTime - groundEntitySpottingTimer) + " s";
						}
					}
				}
			}
			else
			{
				groundEntitySpottingTimer = 0f;
				if (flag && magnitude <= 150.0)
				{
					BeginGroundEntityAggro();
				}
			}
		}
		if (groundEntityAggroPending && !groundEntityStandingDown && !groundEntityRetreating)
		{
			if (groundEntityMimicAnimation == null || groundEntityMimicAnimation.IsAggroComplete)
			{
				groundEntityAggroPending = false;
				groundEntityChasing = true;
			}
			else
			{
				groundEntityStatus = "AGGRO";
			}
		}
		if (groundEntityChasing && !groundEntityStandingDown && !groundEntityRetreating)
		{
			Vessel vessel2 = huntAi ? (huntAware ? vessel : null) : SelectNearestSeenGroundVessel(vector3d, groundEntityBody, 16000f, groundSeenVesselIds, behaviorMimic);
			if (vessel2 == null)
			{
				groundEntityChasing = false;
				groundEntitySpottingTimer = 0f;
				groundSeenVesselIds.Clear();
				groundEntityStatus = "NO LOCAL TARGETS";
			}
			else
			{
				vessel = vessel2;
				magnitude = (vessel.GetWorldPos3D() - vector3d).magnitude;
				flag = vessel.heightFromTerrain < 10f;
			}
		}
		if (groundEntityChasing && !groundEntityStandingDown && !groundEntityRetreating && (flag && magnitude <= 2.0 + GetVerticalCatchAllowance(vessel) || TakeHoverCatch(behaviorMimic)))
		{
			if (vessel.parts != null && vessel.parts.Count > 10)
			{
				if (mimicKillsLargeVessels)
				{
					if (!attackSequences || !BeginMimicMaimSequence(vessel))
					{
						MimicMaimLargeVessel(vessel);
					}
				}
				else
				{
					ScreenMessages.PostScreenMessage("The Mimic strikes a vessel too large to kill and vanishes.", 5f, ScreenMessageStyle.UPPER_CENTER);
					DespawnGroundEntity();
				}
			}
			else
			{
				if (attackSequences && BeginAttackSequence(AttackKind.Mimic, vessel, groundEntityObject, groundEntityBody, 2f, -1))
				{
					return;
				}
				groundSeenVesselIds.Remove(vessel.id);
				HandleEntityCatch(vessel);
				if (debugImmortality)
				{
					groundEntityStandingDown = true;
					groundEntityWithdrawalTimer = 3f;
				}
				groundEntityStatus = "SEEKING NEXT TARGET";
			}
			return;
		}
		if (groundEntityStandingDown)
		{
			groundEntityWithdrawalTimer -= TimeWarp.deltaTime;
			groundEntityStatus = string.Concat(groundVariant, ": STANDING STILL");
			if (groundEntityWithdrawalTimer <= 0f)
			{
				groundEntityStandingDown = false;
				groundEntityRetreating = true;
				groundEntityWithdrawalTimer = 30f;
			}
		}
		else if (groundEntityRetreating)
		{
			groundEntityWithdrawalTimer -= TimeWarp.deltaTime;
			Vector3d normalized = (vector3d - groundEntityBody.position).normalized;
			Vector3d vector3d2 = vessel.GetWorldPos3D() - vector3d;
			if (flag && Time.timeScale > 0f)
			{
				Vector3d vector3d3 = -vector3d2 - normalized * Vector3d.Dot(-vector3d2, normalized);
				if (vector3d3.sqrMagnitude > 1.0)
				{
					vector3d += vector3d3.normalized * GetGroundEntitySpeed(30f) * TimeWarp.deltaTime;
					vector3d = ProjectToGround(vector3d, groundEntityBody);
					groundEntityRelativePosition = vector3d - groundEntityBody.position;
				}
			}
			groundEntityStatus = string.Concat(groundVariant, ": RETREATING");
			if (groundEntityWithdrawalTimer <= 0f)
			{
				DespawnGroundEntity();
				return;
			}
		}
		else if (groundEntityChasing && (flag || huntAi) && Time.timeScale > 0f)
		{
			Vector3d normalized = (vector3d - groundEntityBody.position).normalized;
			Vector3d vector3d2 = vessel.GetWorldPos3D() - vector3d;
			Vector3d vector3d4 = vector3d2 - normalized * Vector3d.Dot(vector3d2, normalized);
			string smartStatus = null;
			if ((huntAi ? HuntGroundApproach(huntMimic, behaviorMimic, groundEntityObject, ref vector3d, vessel, groundEntityBody, GetGroundEntitySpeed(30f), out smartStatus) : smartEntityAi && SmartGroundApproach(behaviorMimic, groundEntityObject, ref vector3d, vector3d2, groundEntityBody, GetGroundEntitySpeed(30f), out smartStatus)))
			{
			}
			else if (vector3d4.sqrMagnitude > 1.0)
			{
				vector3d += vector3d4.normalized * GetGroundEntitySpeed(30f) * TimeWarp.deltaTime;
				vector3d = ProjectToGround(vector3d, groundEntityBody);
			}
			if (vector3d2.sqrMagnitude > 0.01)
			{
				groundEntityRelativePosition = vector3d - groundEntityBody.position;
			}
			groundEntityStatus = smartStatus ?? "CHASING";
		}
		else if (groundEntityChasing && HoverGroundAttack(behaviorMimic, groundEntityObject, ref vector3d, vessel, groundEntityBody, GetGroundEntitySpeed(30f), out var hoverStatus))
		{
			groundEntityRelativePosition = vector3d - groundEntityBody.position;
			groundEntityStatus = hoverStatus;
		}
		else if (!huntAi && !groundEntityChasing && !groundEntityAggroPending && flag && Time.timeScale > 0f)
		{
			if (classicMimicBehavior)
			{
				groundRoamDirectionTimer -= TimeWarp.deltaTime;
				if (groundRoamDirectionTimer <= 0f)
				{
					ChooseGroundRoamDirection(vector3d);
				}
				vector3d += groundRoamDirection * 2.5 * TimeWarp.deltaTime;
				groundEntityStatus = "CLASSIC: ROAMING";
			}
			else
			{
				Vector3d normalized = (vector3d - groundEntityBody.position).normalized;
				Vector3d vector3d2 = vessel.GetWorldPos3D() - vector3d;
				Vector3d vector3d4 = vector3d2 - normalized * Vector3d.Dot(vector3d2, normalized);
				if (vector3d4.sqrMagnitude > 1.0)
				{
					vector3d += vector3d4.normalized * 6.0 * TimeWarp.deltaTime;
				}
				groundEntityStatus = "STALKING (WALK)";
			}
			vector3d = ProjectToGround(vector3d, groundEntityBody);
			groundEntityRelativePosition = vector3d - groundEntityBody.position;
		}
		else if (!groundEntityChasing && !groundEntityAggroPending)
		{
			groundEntityStatus = (classicMimicBehavior ? "CLASSIC: WATCHING" : "WATCHING");
		}
		else if (groundEntityChasing)
		{
			groundEntityStatus = "CANNOT REACH";
		}
		groundEntityObject.transform.position = vector3d + (vector3d - groundEntityBody.position).normalized * (double)GetLeapLift(behaviorMimic);
		Vector3d huntDir = Vector3d.zero;
		if (huntAi && HuntFacing(huntMimic, vector3d, groundEntityBody, out huntDir))
		{
			groundEntityObject.transform.LookAt(vector3d + huntDir, (vector3d - groundEntityBody.position).normalized);
		}
		else if (huntAi && !groundEntityStandingDown && !groundEntityRetreating)
		{
		}
		else if (flag || HoverFacing(behaviorMimic))
		{
			Vector3d vector3d5 = (groundEntityRetreating ? (vector3d - vessel.GetWorldPos3D()) : (vessel.GetWorldPos3D() - vector3d));
			Vector3d normalized2 = (vector3d - groundEntityBody.position).normalized;
			vector3d5 -= normalized2 * Vector3d.Dot(vector3d5, normalized2);
			if (vector3d5.sqrMagnitude > 0.01)
			{
				groundEntityObject.transform.LookAt(vector3d + vector3d5, normalized2);
			}
		}
		ApplyEntityMotion(motionMimic, MotionKind.Mimic, groundEntityObject, groundEntityBody, vector3d, behaviorMimic, GetGroundEntitySpeed(30f));
		HuntUpdateHead(huntMimic, groundEntityObject, vector3d, groundEntityBody, vessel);
		bool flag2 = groundEntityChasing && !groundEntityStandingDown && !groundEntityRetreating && HuntIsMoving(huntMimic);
		if (groundEntityMimicAnimation != null)
		{
			groundEntityMimicAnimation.SetChasing(flag2);
		}
		else if (groundEntityAnimation != null)
		{
			groundEntityAnimation.SetChasing(flag2);
			groundEntityAnimation.SetRoaming(!flag2 && !groundEntityStandingDown);
		}
		UpdateGroundEntitySound();
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

	private static float GetGroundSpottingTime(double distance)
	{
		if (distance <= 100.0)
		{
			return 0f;
		}
		if (distance <= 300.0)
		{
			return 2f;
		}
		if (distance <= 500.0)
		{
			return 3f;
		}
		if (distance <= 750.0)
		{
			return 5f;
		}
		if (distance <= 1200.0)
		{
			return 7f;
		}
		if (distance <= 2000.0)
		{
			return 10f;
		}
		return 15f;
	}

	private void UpdateScientistEntity(Vessel vessel, Vector3d entityPosition, double distance, bool vesselOnGround)
	{
		Vector3d vector3d = vessel.GetWorldPos3D() - entityPosition;
		Vector3d normalized = (entityPosition - groundEntityBody.position).normalized;
		if (!groundEntityFleeing && distance <= 100.0)
		{
			groundEntityFleeing = true;
			groundEntityFleeTimer = 5f;
		}
		if (Time.timeScale > 0f && vesselOnGround)
		{
			Vector3d vector3d2 = vector3d - normalized * Vector3d.Dot(vector3d, normalized);
			if (vector3d2.sqrMagnitude > 1.0)
			{
				float num = (groundEntityFleeing ? 28f : 9f);
				if (groundEntityFleeing)
				{
					vector3d2 = -vector3d2;
				}
				entityPosition += vector3d2.normalized * num * TimeWarp.deltaTime;
				entityPosition = ProjectToGround(entityPosition, groundEntityBody);
				groundEntityRelativePosition = entityPosition - groundEntityBody.position;
			}
		}
		if (groundEntityFleeing)
		{
			groundEntityFleeTimer -= TimeWarp.deltaTime;
			groundEntityStatus = "SCIENTIST: FLEEING";
			if (groundEntityFleeTimer <= 0f)
			{
				DespawnGroundEntity();
				return;
			}
		}
		else
		{
			groundEntityStatus = (vesselOnGround ? "SCIENTIST: FOLLOWING" : "SCIENTIST: WATCHING");
		}
		groundEntityObject.transform.position = entityPosition;
		if (vector3d.sqrMagnitude > 0.01)
		{
			Vector3d vector3d3 = (groundEntityFleeing ? (-vector3d) : vector3d);
			vector3d3 -= normalized * Vector3d.Dot(vector3d3, normalized);
			if (vector3d3.sqrMagnitude > 0.01)
			{
				groundEntityObject.transform.LookAt(groundEntityObject.transform.position + (Vector3)vector3d3, normalized);
			}
		}
		if (groundEntityAnimation != null)
		{
			groundEntityAnimation.SetChasing(groundEntityFleeing);
			groundEntityAnimation.SetRoaming(!groundEntityFleeing);
		}
	}

	private void UpdateTouristEntity(Vessel vessel, Vector3d entityPosition, bool vesselOnGround)
	{
		if (Time.timeScale > 0f)
		{
			groundRoamDirectionTimer -= TimeWarp.deltaTime;
			if (groundRoamDirectionTimer <= 0f)
			{
				ChooseGroundRoamDirection(entityPosition);
			}
			entityPosition += groundRoamDirection * 2.5 * TimeWarp.deltaTime;
			entityPosition = ProjectToGround(entityPosition, groundEntityBody);
			groundEntityRelativePosition = entityPosition - groundEntityBody.position;
		}
		groundEntityStatus = "TOURIST: IGNORING YOU";
		groundEntityObject.transform.position = entityPosition;
		Vector3d normalized = (entityPosition - groundEntityBody.position).normalized;
		if (groundRoamDirection.sqrMagnitude > 0.001)
		{
			groundEntityObject.transform.LookAt(entityPosition + groundRoamDirection, normalized);
		}
		if (groundEntityAnimation != null)
		{
			groundEntityAnimation.SetChasing(value: false);
			groundEntityAnimation.SetRoaming(value: true);
		}
	}

	private void SpawnBulkEntity()
	{
		Vessel activeVessel = FlightGlobals.ActiveVessel;
		if (activeVessel == null || activeVessel.mainBody == null || activeVessel.heightFromTerrain >= 10f)
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
		if (mohoCreature && mohoSpawnHidden)
		{
			// where nobody can see it appear: no line of sight to the craft or the camera, and outside the camera frustum, tested with its height
			if (!TryFindHiddenSpawnPosition(activeVessel, bulkEntityBody, 6000f, 18000f, out groundPosition))
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
		else if (!TryFindLandSpawnPosition(activeVessel, bulkEntityBody, 6000f, 18000f, out groundPosition))
		{
			bulkEntityBody = null;
			bulkEntityStatus = "NO LAND SPAWN POSITION";
			return;
		}
		bulkEntityObject = (mohoCreature ? CreateMohoEntityVisual() : CreateBulkEntityVisual());
		if (bulkEntityObject == null)
		{
			bulkEntityBody = null;
			bulkEntityStatus = "MODEL UNAVAILABLE";
			return;
		}
		bulkEntityRelativePosition = groundPosition - bulkEntityBody.position;
		bulkEntityObject.transform.position = groundPosition;
		if (bulkMoho == null)
		{
			bulkEntityBoneAnimation = bulkEntityObject.AddComponent<BulkEntityBoneAnimation>();
			bulkEntityBoneAnimation.Configure(bulkEntityObject);
		}
		bulkFootstepsAudio = ConfigureGroundAudioSource(bulkEntityObject.AddComponent<AudioSource>(), 2200f, 0.85f);
		StartCoroutine(LoadBulkFootsteps());
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
		bulkEvaScienceAwarded = false;
		bulkSeismometerScienceAwarded = false;
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

	private static GameObject CreateBulkEntityVisual()
	{
		GameObject gameObject = ((GameDatabase.Instance != null) ? GameDatabase.Instance.GetModel("Kraken's Kreations/Models/nightmare_creature_1") : null);
		if (gameObject == null)
		{
			UnityEngine.Debug.Log("[EntityTracker] Could not load Kraken's Kreations/Models/nightmare_creature_1.mu");
			return null;
		}
		gameObject.name = "EntityTracker_MohoEveEntity";
		gameObject.SetActive(value: true);
		gameObject.transform.localScale = Vector3.one * 2.2f;
		Transform[] componentsInChildren = gameObject.GetComponentsInChildren<Transform>(includeInactive: true);
		foreach (Transform transform in componentsInChildren)
		{
			transform.gameObject.SetActive(value: true);
			transform.gameObject.layer = 0;
		}
		Renderer[] componentsInChildren2 = gameObject.GetComponentsInChildren<Renderer>(includeInactive: true);
		foreach (Renderer renderer in componentsInChildren2)
		{
			renderer.enabled = true;
		}
		ConfigureLitEntityMaterials(gameObject);
		Collider[] componentsInChildren3 = gameObject.GetComponentsInChildren<Collider>(includeInactive: true);
		foreach (Collider obj in componentsInChildren3)
		{
			UnityEngine.Object.Destroy(obj);
		}
		Rigidbody[] componentsInChildren4 = gameObject.GetComponentsInChildren<Rigidbody>(includeInactive: true);
		foreach (Rigidbody obj2 in componentsInChildren4)
		{
			UnityEngine.Object.Destroy(obj2);
		}
		StripImportedEntityControllers(gameObject);
		DisableImportedRootMotion(gameObject);
		return gameObject;
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
		if (bulkEntityChasing && !bulkEntityStandingDown && !bulkEntityRetreating && (magnitude <= 8.0 || TakeHoverCatch(behaviorBulk)))
		{
			if (vessel.parts != null && vessel.parts.Count > 17)
			{
				ScreenMessages.PostScreenMessage("The Moho/Eve creature strikes a vessel too large to kill and vanishes.", 5f, ScreenMessageStyle.UPPER_CENTER);
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
					float retreatSpeed = GetGroundEntitySpeed(42f);
					if (bulkLeaving && bulkMoho != null)
					{
						// runs, up to the sprint speed, and does not stop for the terrain
						bulkLeaveSpeed = Mathf.MoveTowards(bulkLeaveSpeed, Mathf.Max(retreatSpeed, mohoSprintSpeed), Mathf.Max(mohoSprintAccel, 5f) * TimeWarp.deltaTime);
						retreatSpeed = bulkLeaveSpeed;
					}
					vector3d += vector3d3.normalized * retreatSpeed * TimeWarp.deltaTime;
					vector3d = ProjectToGround(vector3d, bulkEntityBody);
					bulkEntityRelativePosition = vector3d - bulkEntityBody.position;
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
			if ((huntAi ? HuntGroundApproach(huntBulk, behaviorBulk, bulkEntityObject, ref vector3d, vessel, bulkEntityBody, GetGroundEntitySpeed(42f), out smartStatus) : smartEntityAi && SmartGroundApproach(behaviorBulk, bulkEntityObject, ref vector3d, vector3d2, bulkEntityBody, GetGroundEntitySpeed(42f), out smartStatus)))
			{
			}
			else if (magnitude <= 20.0)
			{
				vector3d += vector3d2.normalized * GetGroundEntitySpeed(42f) * TimeWarp.deltaTime;
			}
			else
			{
				Vector3d vector3d4 = vector3d2 - normalized * Vector3d.Dot(vector3d2, normalized);
				if (vector3d4.sqrMagnitude > 1.0)
				{
					vector3d += vector3d4.normalized * GetGroundEntitySpeed(42f) * TimeWarp.deltaTime;
					vector3d = ProjectToGround(vector3d, bulkEntityBody);
				}
			}
			bulkEntityRelativePosition = vector3d - bulkEntityBody.position;
			bulkEntityStatus = smartStatus ?? "CHASING";
		}
		else if (bulkEntityChasing && HoverGroundAttack(behaviorBulk, bulkEntityObject, ref vector3d, vessel, bulkEntityBody, GetGroundEntitySpeed(42f), out var hoverStatus))
		{
			bulkEntityRelativePosition = vector3d - bulkEntityBody.position;
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
			ApplyEntityMotion(motionBulk, MotionKind.Bulk, bulkEntityObject, bulkEntityBody, vector3d, behaviorBulk, GetGroundEntitySpeed(42f));
			HuntUpdateHead(huntBulk, bulkEntityObject, vector3d, bulkEntityBody, vessel);
			AlignBulkEntityFeetToGround(vector3d + normalized2 * num, normalized2);
		}
		if (bulkEntityBoneAnimation != null)
		{
			bulkEntityBoneAnimation.SetChasing(flag2);
		}
		UpdateBulkFootsteps(flag2);
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
		bulkEntityBoneAnimation = null;
		bulkFootstepsAudio = null;
		bulkFootstepsLoaded = false;
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

	private void SpawnLaytheCreature()
	{
		Vessel activeVessel = FlightGlobals.ActiveVessel;
		if (!IsEligibleForLaytheCreature(activeVessel))
		{
			laytheCreatureStatus = "LAYTHE GROUND REQUIRED";
			return;
		}
		DespawnLaytheCreature();
		laytheCreatureBody = activeVessel.mainBody;
		if (!TryFindLandSpawnPosition(activeVessel, laytheCreatureBody, 6000f, 18000f, out var groundPosition))
		{
			laytheCreatureBody = null;
			laytheCreatureStatus = "NO LAND SPAWN POSITION";
			return;
		}
		laytheCreatureObject = ((GameDatabase.Instance != null) ? GameDatabase.Instance.GetModel("Kraken's Kreations/Models/the_smile") : null);
		if (laytheCreatureObject == null)
		{
			laytheCreatureBody = null;
			laytheCreatureStatus = "MODEL UNAVAILABLE";
			UnityEngine.Debug.Log("[EntityTracker] Could not load Kraken's Kreations/Models/the_smile.mu");
			return;
		}
		laytheCreatureObject.name = "EntityTracker_LaytheLandCreature";
		laytheCreatureObject.SetActive(value: true);
		laytheCreatureObject.transform.localScale = LaytheCreatureVisualScaleVector;
		Transform[] componentsInChildren = laytheCreatureObject.GetComponentsInChildren<Transform>(includeInactive: true);
		foreach (Transform transform in componentsInChildren)
		{
			transform.gameObject.SetActive(value: true);
			transform.gameObject.layer = 0;
		}
		Collider[] componentsInChildren2 = laytheCreatureObject.GetComponentsInChildren<Collider>(includeInactive: true);
		foreach (Collider obj in componentsInChildren2)
		{
			UnityEngine.Object.Destroy(obj);
		}
		Rigidbody[] componentsInChildren3 = laytheCreatureObject.GetComponentsInChildren<Rigidbody>(includeInactive: true);
		foreach (Rigidbody obj2 in componentsInChildren3)
		{
			UnityEngine.Object.Destroy(obj2);
		}
		StripImportedEntityControllers(laytheCreatureObject);
		DisableImportedRootMotion(laytheCreatureObject);
		ConfigureLitEntityMaterials(laytheCreatureObject);
		laytheCreatureRelativePosition = groundPosition - laytheCreatureBody.position;
		laytheCreatureObject.transform.position = groundPosition;
		laytheCreatureBoneAnimation = laytheCreatureObject.AddComponent<LaytheCreatureBoneAnimation>();
		laytheCreatureBoneAnimation.Configure(laytheCreatureObject);
		laytheCreatureAnimation = null;
		laytheCreatureFootstepsAudio = ConfigureGroundAudioSource(laytheCreatureObject.AddComponent<AudioSource>(), 180f, 0.8f);
		laytheCreatureScreamAudio = ConfigureGroundAudioSource(laytheCreatureObject.AddComponent<AudioSource>(), 3000f, 0.9f);
		StartCoroutine(LoadLaytheCreatureSound("daviddumaisaudio-robot-heavy-mechanical-footsteps-194039.mp3", laytheCreatureFootstepsAudio, footsteps: true));
		StartCoroutine(LoadLaytheCreatureSound("u_kse9ncnirq-reversed-monster-screams-or-roars-angry-425881.mp3", laytheCreatureScreamAudio, footsteps: false));
		laytheCreatureActive = true;
		laytheCreatureChasing = false;
		laytheSeenVesselIds.Clear();
		laytheCreatureAnimationPhase = UnityEngine.Random.Range(0f, (float)Math.PI * 2f);
		laytheCreatureStatus = "DORMANT";
	}

	private void UpdateLaytheCreature()
	{
		if (!laytheCreatureActive || laytheCreatureObject == null || laytheCreatureBody == null)
		{
			return;
		}
		if (IsAttackSequenceRunning(AttackKind.Laythe))
		{
			return;
		}
		Vessel vessel = FlightGlobals.ActiveVessel;
		if (vessel == null || vessel.mainBody != laytheCreatureBody || vessel.Splashed)
		{
			DespawnLaytheCreature();
			return;
		}
		Vector3d vector3d = laytheCreatureBody.position + laytheCreatureRelativePosition;
		Vector3d vector3d2 = vessel.GetWorldPos3D() - vector3d;
		double magnitude = vector3d2.magnitude;
		bool flag = vessel.heightFromTerrain < 10f;
		bool huntAware = huntAi && HuntSense(huntLaythe, laytheCreatureObject, vector3d, laytheCreatureBody, vessel, LaytheCreatureModelAxisCorrection);
		if (!laytheCreatureChasing && (huntAi ? huntAware : flag && magnitude <= 2500.0 && HasGroundLineOfSight(vector3d, vessel.GetWorldPos3D(), laytheCreatureBody)))
		{
			laytheCreatureChasing = true;
		}
		if (laytheCreatureChasing)
		{
			Vessel vessel2 = huntAi ? (huntAware ? vessel : null) : SelectNearestSeenGroundVessel(vector3d, laytheCreatureBody, 2500f, laytheSeenVesselIds, behaviorLaythe);
			if (vessel2 == null)
			{
				laytheCreatureChasing = false;
				laytheSeenVesselIds.Clear();
				laytheCreatureStatus = "NO LOCAL TARGETS";
			}
			else
			{
				vessel = vessel2;
				vector3d2 = vessel.GetWorldPos3D() - vector3d;
				magnitude = vector3d2.magnitude;
				flag = vessel.heightFromTerrain < 10f;
			}
		}
		if (laytheCreatureChasing && (magnitude <= 3.0 + GetVerticalCatchAllowance(vessel) || TakeHoverCatch(behaviorLaythe)))
		{
			if (vessel.parts != null && vessel.parts.Count > 10)
			{
				ScreenMessages.PostScreenMessage("The Laythe creature strikes a vessel too large to kill and vanishes.", 5f, ScreenMessageStyle.UPPER_CENTER);
				DespawnLaytheCreature();
			}
			else
			{
				if (attackSequences && BeginAttackSequence(AttackKind.Laythe, vessel, laytheCreatureObject, laytheCreatureBody, 3f, -1))
				{
					return;
				}
				laytheSeenVesselIds.Remove(vessel.id);
				HandleEntityCatch(vessel);
				if (debugImmortality)
				{
					DespawnLaytheCreature();
					return;
				}
				laytheCreatureStatus = "SEEKING NEXT TARGET";
			}
			return;
		}
		Vector3d normalized = (vector3d - laytheCreatureBody.position).normalized;
		if (laytheCreatureChasing && (flag || huntAi) && Time.timeScale > 0f)
		{
			Vector3d vector3d3 = vector3d2 - normalized * Vector3d.Dot(vector3d2, normalized);
			string smartStatus = null;
			if ((huntAi ? HuntGroundApproach(huntLaythe, behaviorLaythe, laytheCreatureObject, ref vector3d, vessel, laytheCreatureBody, GetGroundEntitySpeed(34f), out smartStatus) : smartEntityAi && SmartGroundApproach(behaviorLaythe, laytheCreatureObject, ref vector3d, vector3d2, laytheCreatureBody, GetGroundEntitySpeed(34f), out smartStatus)))
			{
				laytheCreatureRelativePosition = vector3d - laytheCreatureBody.position;
				normalized = (vector3d - laytheCreatureBody.position).normalized;
			}
			else if (vector3d3.sqrMagnitude > 1.0)
			{
				vector3d += vector3d3.normalized * GetGroundEntitySpeed(34f) * TimeWarp.deltaTime;
				vector3d = ProjectToGround(vector3d, laytheCreatureBody);
				laytheCreatureRelativePosition = vector3d - laytheCreatureBody.position;
				normalized = (vector3d - laytheCreatureBody.position).normalized;
			}
			laytheCreatureStatus = smartStatus ?? "CHASING";
			laytheCreatureAnimationPhase += TimeWarp.deltaTime * 9f;
		}
		else if (laytheCreatureChasing && HoverGroundAttack(behaviorLaythe, laytheCreatureObject, ref vector3d, vessel, laytheCreatureBody, GetGroundEntitySpeed(34f), out var hoverStatus))
		{
			laytheCreatureRelativePosition = vector3d - laytheCreatureBody.position;
			normalized = (vector3d - laytheCreatureBody.position).normalized;
			laytheCreatureStatus = hoverStatus;
			laytheCreatureAnimationPhase += TimeWarp.deltaTime * 9f;
		}
		else if (laytheCreatureChasing)
		{
			laytheCreatureStatus = "CANNOT REACH";
		}
		else if (!huntAi && flag && Time.timeScale > 0f)
		{
			Vector3d vector3d3 = vector3d2 - normalized * Vector3d.Dot(vector3d2, normalized);
			if (magnitude > 400.0 && vector3d3.sqrMagnitude > 1.0)
			{
				Vector3d normalized2 = vector3d3.normalized;
				Vector3d normalized3 = Vector3d.Cross(normalized, normalized2).normalized;
				float num = Mathf.Sin(laytheCreatureAnimationPhase * 0.55f) * 0.3f;
				vector3d += (normalized2 + normalized3 * num).normalized * 2.4000000953674316 * TimeWarp.deltaTime;
				vector3d = ProjectToGround(vector3d, laytheCreatureBody);
				laytheCreatureRelativePosition = vector3d - laytheCreatureBody.position;
				normalized = (vector3d - laytheCreatureBody.position).normalized;
				vector3d2 = vessel.GetWorldPos3D() - vector3d;
			}
			laytheCreatureAnimationPhase += TimeWarp.deltaTime * 2.2f;
			laytheCreatureStatus = "ROAMING NEARBY";
		}
		Vector3d vector3d4 = vector3d2 - normalized * Vector3d.Dot(vector3d2, normalized);
		Vector3d huntDir = Vector3d.zero;
		Vector3 forward = ((vector3d4.sqrMagnitude > 0.01) ? ((Vector3)vector3d4.normalized) : laytheCreatureObject.transform.forward);
		if (huntAi)
		{
			forward = HuntFacing(huntLaythe, vector3d, laytheCreatureBody, out huntDir) ? (Vector3)huntDir : laytheCreatureObject.transform.forward;
		}
		Quaternion rotation = Quaternion.LookRotation(forward, normalized) * LaytheCreatureModelAxisCorrection;
		laytheCreatureObject.transform.rotation = rotation;
		ApplyEntityMotion(motionLaythe, MotionKind.Laythe, laytheCreatureObject, laytheCreatureBody, vector3d, behaviorLaythe, GetGroundEntitySpeed(34f));
		HuntUpdateHead(huntLaythe, laytheCreatureObject, vector3d, laytheCreatureBody, vessel);
		laytheCreatureObject.transform.localScale = LaytheCreatureVisualScaleVector;
		double laytheLift = GetLeapLift(behaviorLaythe);
		laytheCreatureObject.transform.position = vector3d + normalized * (0.019999999552965164 + laytheLift);
		AlignVisualFeetToGround(laytheCreatureObject, vector3d + normalized * laytheLift, normalized, 0.02f);
		if (laytheCreatureBoneAnimation != null)
		{
			laytheCreatureBoneAnimation.SetState(laytheCreatureChasing && HuntIsMoving(huntLaythe), !laytheCreatureChasing);
		}
		PlayLaytheCreatureAnimation(laytheCreatureChasing ? "Walk_Object_5" : "Idle_Object_5");
		UpdateGroundAudioSource(laytheCreatureFootstepsAudio, laytheCreatureFootstepsLoaded, laytheCreatureChasing && HuntIsMoving(huntLaythe) && Time.timeScale > 0f);
		UpdateGroundAudioSource(laytheCreatureScreamAudio, laytheCreatureScreamLoaded, DreadAllowScream(DreadScreenGateKind.Laythe, laytheCreatureChasing && Time.timeScale > 0f));
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

	private void DespawnLaytheCreature()
	{
		if (laytheCreatureObject != null)
		{
			UnityEngine.Object.Destroy(laytheCreatureObject);
		}
		laytheCreatureObject = null;
		laytheCreatureBoneAnimation = null;
		laytheCreatureAnimation = null;
		laytheCreatureFootstepsAudio = null;
		laytheCreatureScreamAudio = null;
		laytheCreatureFootstepsLoaded = false;
		laytheCreatureScreamLoaded = false;
		laytheCreatureBody = null;
		laytheCreatureRelativePosition = Vector3d.zero;
		laytheCreatureActive = false;
		laytheCreatureChasing = false;
		laytheSeenVesselIds.Clear();
		laytheCreatureAnimationPhase = 0f;
		laytheCreatureStatus = "NONE";
	}

	private void PlayLaytheCreatureAnimation(string clipName)
	{
		if (!(laytheCreatureAnimation == null) && !(laytheCreatureAnimation[clipName] == null) && !laytheCreatureAnimation.IsPlaying(clipName))
		{
			laytheCreatureAnimation.CrossFade(clipName, 0.12f);
		}
	}

	private IEnumerator LoadLaytheCreatureSound(string filename, AudioSource target, bool footsteps)
	{
		string path = Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "Kraken's Kreations", "Sounds", filename);
		if (!File.Exists(path) || target == null)
		{
			yield break;
		}
		WWW request = new WWW("file:///" + path.Replace("\\", "/"));
		yield return request;
		if (string.IsNullOrEmpty(request.error))
		{
			target.clip = request.GetAudioClip(threeD: false, stream: false, AudioType.MPEG);
			if (footsteps)
			{
				laytheCreatureFootstepsLoaded = target.clip != null;
			}
			else
			{
				laytheCreatureScreamLoaded = target.clip != null;
			}
		}
	}

	private void SpawnSeaEntity()
	{
		Vessel activeVessel = FlightGlobals.ActiveVessel;
		if (!CanSeaEntityTarget(activeVessel))
		{
			seaEntityStatus = "EVE OCEAN REQUIRED";
			return;
		}
		DespawnSeaEntity();
		seaEntityBody = activeVessel.mainBody;
		Vector3 normalized = ((Vector3)(activeVessel.GetWorldPos3D() - seaEntityBody.position)).normalized;
		Vector3 vector = activeVessel.transform.forward - normalized * Vector3.Dot(activeVessel.transform.forward, normalized);
		if (vector.sqrMagnitude < 0.01f)
		{
			vector = Vector3.Cross(normalized, Vector3.right);
		}
		vector.Normalize();
		Vector3d worldPosition = activeVessel.GetWorldPos3D() + (Vector3d)(vector * UnityEngine.Random.Range(900f, 2200f));
		Vector3d vector3d = ProjectToEveSea(worldPosition, seaEntityBody);
		seaEntityObject = ((GameDatabase.Instance != null) ? GameDatabase.Instance.GetModel("Kraken's Kreations/Models/sea_monster_shark_concept") : null);
		if (seaEntityObject == null)
		{
			seaEntityBody = null;
			seaEntityStatus = "MODEL UNAVAILABLE";
			return;
		}
		seaEntityObject.name = "EntityTracker_EveSeaMonster";
		seaEntityObject.SetActive(value: true);
		seaEntityObject.transform.localScale = Vector3.one * 0.035f;
		Transform[] componentsInChildren = seaEntityObject.GetComponentsInChildren<Transform>(includeInactive: true);
		foreach (Transform transform in componentsInChildren)
		{
			transform.gameObject.SetActive(value: true);
			transform.gameObject.layer = 0;
		}
		Renderer[] componentsInChildren2 = seaEntityObject.GetComponentsInChildren<Renderer>(includeInactive: true);
		foreach (Renderer renderer in componentsInChildren2)
		{
			renderer.enabled = true;
		}
		ConfigureLitEntityMaterials(seaEntityObject);
		Collider[] componentsInChildren3 = seaEntityObject.GetComponentsInChildren<Collider>(includeInactive: true);
		foreach (Collider obj in componentsInChildren3)
		{
			UnityEngine.Object.Destroy(obj);
		}
		Rigidbody[] componentsInChildren4 = seaEntityObject.GetComponentsInChildren<Rigidbody>(includeInactive: true);
		foreach (Rigidbody obj2 in componentsInChildren4)
		{
			UnityEngine.Object.Destroy(obj2);
		}
		seaEntityRelativePosition = vector3d - seaEntityBody.position;
		seaEntityObject.transform.position = vector3d;
		seaGrowlAudio = ConfigureGroundAudioSource(seaEntityObject.AddComponent<AudioSource>(), 2500f, 0.8f);
		StartCoroutine(LoadSeaGrowl());
		seaEntityActive = true;
		seaScienceAwarded = false;
		seaEntityChasing = false;
		seaAnimationPhase = UnityEngine.Random.Range(0f, (float)Math.PI * 2f);
		seaEntityStatus = "DORMANT";
	}

	private void UpdateSeaEntity()
	{
		if (!seaEntityActive || seaEntityObject == null || seaEntityBody == null)
		{
			return;
		}
		if (IsAttackSequenceRunning(AttackKind.Sea))
		{
			return;
		}
		Vessel activeVessel = FlightGlobals.ActiveVessel;
		if (activeVessel == null)
		{
			return;
		}
		if (!IsSeaEntityActiveRange(activeVessel))
		{
			DespawnSeaEntity();
			return;
		}
		bool flag = CanSeaEntityTarget(activeVessel);
		Vector3d vector3d = seaEntityBody.position + seaEntityRelativePosition;
		Vector3d vector3d2 = activeVessel.GetWorldPos3D() - vector3d;
		double magnitude = vector3d2.magnitude;
		if (!flag)
		{
			seaEntityChasing = false;
			seaEntityStatus = "WAITING BELOW SURFACE";
		}
		else if (!seaEntityChasing && magnitude <= 1500.0)
		{
			seaEntityChasing = true;
		}
		if (flag && seaEntityChasing && magnitude <= 18.0)
		{
			if (attackSequences && BeginAttackSequence(AttackKind.Sea, activeVessel, seaEntityObject, seaEntityBody, 18f, -1))
			{
				return;
			}
			DespawnSeaEntity();
			HandleEntityCatch(activeVessel);
			return;
		}
		Vector3d normalized = (vector3d - seaEntityBody.position).normalized;
		if (flag && seaEntityChasing && Time.timeScale > 0f)
		{
			Vector3d vector3d3 = vector3d2 - normalized * Vector3d.Dot(vector3d2, normalized);
			if (vector3d3.sqrMagnitude > 1.0)
			{
				vector3d += vector3d3.normalized * GetSeaEntitySpeed(52f) * TimeWarp.deltaTime;
				vector3d = ProjectToEveSea(vector3d, seaEntityBody);
				seaEntityRelativePosition = vector3d - seaEntityBody.position;
			}
			seaEntityStatus = "CHASING";
			seaAnimationPhase += TimeWarp.deltaTime * 3f;
		}
		normalized = (vector3d - seaEntityBody.position).normalized;
		if (vector3d2.sqrMagnitude > 0.01)
		{
			Vector3 lhs = vector3d2;
			lhs -= (Vector3)normalized * Vector3.Dot(lhs, normalized);
			if (lhs.sqrMagnitude > 0.01f)
			{
				seaEntityObject.transform.rotation = Quaternion.LookRotation(lhs.normalized, normalized) * Quaternion.Euler(0f, 180f, 0f) * SeaModelAxisCorrection;
			}
		}
		ApplyEntityMotion(motionSea, MotionKind.Sea, seaEntityObject, seaEntityBody, vector3d, null, GetSeaEntitySpeed(52f));
		float num = (seaEntityChasing ? (Mathf.Sin(seaAnimationPhase) * 0.14f) : 0f);
		seaEntityObject.transform.position = vector3d + normalized * (16f + num);
		UpdateSeaGrowl();
	}

	private void DespawnSeaEntity()
	{
		if (seaEntityObject != null)
		{
			UnityEngine.Object.Destroy(seaEntityObject);
		}
		seaEntityObject = null;
		seaGrowlAudio = null;
		seaGrowlLoaded = false;
		seaEntityBody = null;
		seaEntityRelativePosition = Vector3d.zero;
		seaEntityActive = false;
		seaEntityChasing = false;
		seaAnimationPhase = 0f;
		seaEntityStatus = "NONE";
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
			bloopEntityStatus = "100 M DEPTH REQUIRED";
			return;
		}
		DespawnBloopEntity();
		bloopEntityBody = activeVessel.mainBody;
		Vector3 normalized = ((Vector3)(activeVessel.GetWorldPos3D() - bloopEntityBody.position)).normalized;
		Vector3 vector = activeVessel.transform.forward - normalized * Vector3.Dot(activeVessel.transform.forward, normalized);
		if (vector.sqrMagnitude < 0.01f)
		{
			vector = Vector3.Cross(normalized, Vector3.right);
		}
		vector.Normalize();
		Vector3d worldPosition = activeVessel.GetWorldPos3D() + (Vector3d)(vector * UnityEngine.Random.Range(3000f, 10000f));
		Vector3d vector3d = ProjectToBloopSea(worldPosition, bloopEntityBody);
		bloopEntityObject = CreateBloopVisual() ?? ((GameDatabase.Instance != null) ? GameDatabase.Instance.GetModel("Kraken's Kreations/Models/bloop") : null);
		if (bloopEntityObject == null)
		{
			bloopEntityBody = null;
			bloopEntityStatus = "MODEL UNAVAILABLE";
			return;
		}
		bloopEntityObject.name = "EntityTracker_LaytheBloop";
		bloopEntityObject.SetActive(value: true);
		bloopEntityObject.transform.localScale = Vector3.one * 6f;
		Transform[] componentsInChildren = bloopEntityObject.GetComponentsInChildren<Transform>(includeInactive: true);
		foreach (Transform transform in componentsInChildren)
		{
			transform.gameObject.SetActive(value: true);
			transform.gameObject.layer = 0;
		}
		Renderer[] componentsInChildren2 = bloopEntityObject.GetComponentsInChildren<Renderer>(includeInactive: true);
		foreach (Renderer renderer in componentsInChildren2)
		{
			renderer.enabled = true;
		}
		ConfigureLitEntityMaterials(bloopEntityObject);
		Collider[] componentsInChildren3 = bloopEntityObject.GetComponentsInChildren<Collider>(includeInactive: true);
		foreach (Collider obj in componentsInChildren3)
		{
			UnityEngine.Object.Destroy(obj);
		}
		Rigidbody[] componentsInChildren4 = bloopEntityObject.GetComponentsInChildren<Rigidbody>(includeInactive: true);
		foreach (Rigidbody obj2 in componentsInChildren4)
		{
			UnityEngine.Object.Destroy(obj2);
		}
		bloopEntityRelativePosition = vector3d - bloopEntityBody.position;
		bloopEntityObject.transform.position = vector3d;
		bloopBurrowAudio = ConfigureGroundAudioSource(bloopEntityObject.AddComponent<AudioSource>(), 6000f, 0.9f);
		bloopBurrowAudio.loop = false;
		StartCoroutine(LoadBloopBurrowSound());
		bloopEntityActive = true;
		bloopEntityChasing = false;
		bloopAnimationPhase = UnityEngine.Random.Range(0f, (float)Math.PI * 2f);
		bloopBurrowTimer = UnityEngine.Random.Range(10f, 24f);
		bloopCloseCuePlayed = false;
		bloopEntityStatus = "DORMANT";
		BloopAiStart(activeVessel);
	}

	private void UpdateBloopEntity()
	{
		if (!bloopEntityActive || bloopEntityObject == null || bloopEntityBody == null)
		{
			return;
		}
		if (IsAttackSequenceRunning(AttackKind.Bloop))
		{
			return;
		}
		Vessel activeVessel = FlightGlobals.ActiveVessel;
		if (activeVessel == null || activeVessel.mainBody != bloopEntityBody)
		{
			DespawnBloopEntity();
			return;
		}
		if (bloopBrain != null)
		{
			UpdateBloopAi(activeVessel);
			return;
		}
		Vector3d vector3d = bloopEntityBody.position + bloopEntityRelativePosition;
		Vector3d vector3d2 = activeVessel.GetWorldPos3D() - vector3d;
		double magnitude = vector3d2.magnitude;
		bool flag = CanBloopTarget(activeVessel);
		if (!flag)
		{
			bloopEntityChasing = false;
			bloopEntityStatus = "WAITING BELOW -100 M";
		}
		else if (!bloopEntityChasing && magnitude <= 8000.0)
		{
			bloopEntityChasing = true;
		}
		double magnitude2 = (activeVessel.GetWorldPos3D() - GetBloopVisibleCentre()).magnitude;
		if (flag && bloopEntityChasing && magnitude <= 120.0 && magnitude2 <= 120.0)
		{
			if (attackSequences && BeginAttackSequence(AttackKind.Bloop, activeVessel, bloopEntityObject, bloopEntityBody, 120f, -1))
			{
				return;
			}
			DespawnBloopEntity();
			HandleEntityCatch(activeVessel);
			return;
		}
		Vector3d normalized = (vector3d - bloopEntityBody.position).normalized;
		if (flag && bloopEntityChasing && Time.timeScale > 0f)
		{
			if (magnitude <= 300.0 && vector3d2.sqrMagnitude > 1.0)
			{
				vector3d += vector3d2.normalized * GetSeaEntitySpeed(18f) * TimeWarp.deltaTime;
			}
			else
			{
				Vector3d vector3d3 = vector3d2 - normalized * Vector3d.Dot(vector3d2, normalized);
				if (vector3d3.sqrMagnitude > 1.0)
				{
					vector3d += vector3d3.normalized * GetSeaEntitySpeed(18f) * TimeWarp.deltaTime;
					vector3d = ProjectToBloopSea(vector3d, bloopEntityBody);
				}
			}
			bloopEntityRelativePosition = vector3d - bloopEntityBody.position;
			bloopEntityStatus = "CHASING";
			bloopAnimationPhase += TimeWarp.deltaTime * 0.65f;
		}
		normalized = (vector3d - bloopEntityBody.position).normalized;
		if (vector3d2.sqrMagnitude > 0.01)
		{
			Vector3 lhs = vector3d2;
			lhs -= (Vector3)normalized * Vector3.Dot(lhs, normalized);
			if (lhs.sqrMagnitude > 0.01f)
			{
				bloopEntityObject.transform.rotation = Quaternion.LookRotation(lhs.normalized, normalized) * Quaternion.Euler(0f, 180f, 0f) * BloopModelAxisCorrection * Quaternion.Euler(0f, 90f, 0f) * Quaternion.Euler(0f, 0f, -90f);
			}
		}
		ApplyEntityMotion(motionBloop, MotionKind.Bloop, bloopEntityObject, bloopEntityBody, vector3d, null, GetSeaEntitySpeed(18f));
		float swim = (bloopEntityChasing ? (Mathf.Sin(bloopAnimationPhase) * 2f) : 0f);
		PositionBloopVisual(vector3d, normalized, swim);
		UpdateBloopBurrowSound(magnitude);
	}

	private void PositionBloopVisual(Vector3d logicalPosition, Vector3 radialUp, float swim)
	{
		if (bloopEntityObject == null)
		{
			return;
		}
		Vector3 vector = logicalPosition + radialUp * (0f + swim);
		bloopEntityObject.transform.position = vector;
		Renderer[] componentsInChildren = bloopEntityObject.GetComponentsInChildren<Renderer>(includeInactive: true);
		if (componentsInChildren.Length == 0)
		{
			return;
		}
		Bounds bounds = componentsInChildren[0].bounds;
		for (int i = 1; i < componentsInChildren.Length; i++)
		{
			if (componentsInChildren[i] != null)
			{
				bounds.Encapsulate(componentsInChildren[i].bounds);
			}
		}
		bloopEntityObject.transform.position += vector - bounds.center;
	}

	private Vector3d GetBloopVisibleCentre()
	{
		if (bloopEntityObject == null)
		{
			return Vector3d.zero;
		}
		Bounds bounds = new Bounds(bloopEntityObject.transform.position, Vector3.zero);
		bool flag = false;
		Renderer[] componentsInChildren = bloopEntityObject.GetComponentsInChildren<Renderer>(includeInactive: true);
		foreach (Renderer renderer in componentsInChildren)
		{
			if (!(renderer == null) && renderer.enabled)
			{
				if (!flag)
				{
					bounds = renderer.bounds;
					flag = true;
				}
				else
				{
					bounds.Encapsulate(renderer.bounds);
				}
			}
		}
		return flag ? ((Vector3d)bounds.center) : ((Vector3d)bloopEntityObject.transform.position);
	}

	private void DespawnBloopEntity()
	{
		BloopAiStop();
		if (bloopEntityObject != null)
		{
			UnityEngine.Object.Destroy(bloopEntityObject);
		}
		bloopEntityObject = null;
		bloopBurrowAudio = null;
		bloopBurrowLoaded = false;
		bloopBurrowTimer = 0f;
		bloopCloseCuePlayed = false;
		bloopEntityBody = null;
		bloopEntityRelativePosition = Vector3d.zero;
		bloopEntityActive = false;
		bloopEntityChasing = false;
		bloopAnimationPhase = 0f;
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
		if (eyeEntityActive)
		{
			position = eyeEntityPosition;
		}
		else if (bloopEntityActive && bloopEntityBody != null)
		{
			position = bloopEntityBody.position + bloopEntityRelativePosition;
		}
		else if (seaEntityActive && seaEntityBody != null)
		{
			position = seaEntityBody.position + seaEntityRelativePosition;
		}
		else if (bulkEntityActive && bulkEntityBody != null)
		{
			position = bulkEntityBody.position + bulkEntityRelativePosition;
		}
		else if (groundEntityActive && groundEntityBody != null)
		{
			position = groundEntityBody.position + groundEntityRelativePosition;
		}
		else
		{
			if (!rakeEntityActive || !(rakeEntityBody != null))
			{
				return;
			}
			position = rakeEntityBody.position + rakeEntityRelativePosition;
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

	private void UpdateEyeEntity()
	{
		Vessel activeVessel = FlightGlobals.ActiveVessel;
		if (activeVessel == null || activeVessel.mainBody == null)
		{
			return;
		}
		CelestialBody mainBody = activeVessel.mainBody;
		string a = mainBody.bodyName ?? string.Empty;
		bool flag = eyeLastObservedBody != null && eyeLastObservedBody != mainBody;
		if (!eyeEntityActive && flag && string.Equals(a, "Jool", StringComparison.OrdinalIgnoreCase) && UnityEngine.Random.value <= 0.25f)
		{
			SpawnEyeEntity(activeVessel);
		}
		eyeLastObservedBody = mainBody;
		if (!eyeEntityActive)
		{
			return;
		}
		if (!string.Equals(a, "Jool", StringComparison.OrdinalIgnoreCase) && !string.Equals(a, "Bop", StringComparison.OrdinalIgnoreCase))
		{
			DespawnEyeEntity();
			return;
		}
		if (Time.timeScale <= 0f)
		{
			UpdateEyeBillboard();
			return;
		}
		if (string.Equals(a, "Bop", StringComparison.OrdinalIgnoreCase) && !eyeEntityChasing)
		{
			eyeEntityChasing = true;
			eyeChaseReferenceBody = activeVessel.mainBody;
			eyeChaseRelativePosition = eyeEntityPosition - eyeChaseReferenceBody.position;
			eyeChaseBaseVelocity = ((activeVessel.orbit != null) ? activeVessel.orbit.GetVel() : Vector3d.zero);
			if (eyeWarningAudio != null && eyeWarningSoundLoaded)
			{
				eyeWarningAudio.Play();
			}
		}
		if (eyeEntityChasing)
		{
			if (Time.timeScale > 0f)
			{
				eyeChaseTimer += TimeWarp.deltaTime;
			}
			float chaseSpeed = Mathf.Min(1000f, Mathf.Pow(1000f, Mathf.Min(eyeChaseTimer, 10f) / 10f)) * Mathf.Clamp(eyeSpeedMultiplier, 0.05f, 20f);
			UpdateEyeChaseOrbit(activeVessel, chaseSpeed);
			if (eyeChaseAudio != null && eyeChaseSoundLoaded && Time.timeScale > 0f && !eyeChaseAudio.isPlaying)
			{
				eyeChaseAudio.Play();
			}
			if ((eyeEntityPosition - activeVessel.GetWorldPos3D()).magnitude <= 3.0)
			{
				DespawnEyeEntity();
				if (activeVessel.isEVA)
				{
					HighLogic.LoadScene(GameScenes.MAINMENU);
				}
				else
				{
					DestroyCaughtVessel(activeVessel);
				}
			}
			UpdateEyeBillboard();
		}
		else
		{
			float num = 50f + Mathf.Sin(Time.realtimeSinceStartup * 0.45f) * 12f;
			eyeEntityPosition = activeVessel.GetWorldPos3D() + eyeWatchDirection * num;
			UpdateEyeBillboard();
		}
	}

	private static bool IsEligibleForDresSomething(Vessel vessel)
	{
		return vessel != null && vessel.mainBody != null && string.Equals(vessel.mainBody.bodyName, "Dres", StringComparison.OrdinalIgnoreCase);
	}

	private void SpawnDresSomething(Vessel vessel)
	{
		if (!dresSomethingActive && IsEligibleForDresSomething(vessel))
		{
			Vector3 normalized = (vessel.transform.forward * UnityEngine.Random.Range(-1f, 1f) + vessel.transform.right * UnityEngine.Random.Range(-1f, 1f)).normalized;
			if (normalized.sqrMagnitude < 0.01f)
			{
				normalized = vessel.transform.forward.normalized;
			}
			Vector3d worldPosition = vessel.GetWorldPos3D() + (Vector3d)normalized * (double)UnityEngine.Random.Range(3000f, 10000f);
			Vector3d vector3d = ProjectToGround(worldPosition, vessel.mainBody);
			Vector3d normalized2 = (vector3d - vessel.mainBody.position).normalized;
			dresSomethingBody = vessel.mainBody;
			dresSomethingRelativePosition = vector3d + normalized2 * 24.0 - dresSomethingBody.position;
			dresSomethingActive = true;
			dresSomethingDropping = false;
			dresSomethingFadeTimer = 0f;
			dresSomethingFrameTimer = UnityEngine.Random.Range(0f, 0.15f);
			dresSomethingStatus = "WATCHING";
		}
	}

	private void UpdateDresSomething()
	{
		if (!dresSomethingActive)
		{
			return;
		}
		Vessel activeVessel = FlightGlobals.ActiveVessel;
		if (activeVessel == null || activeVessel.mainBody != dresSomethingBody || !IsEligibleForDresSomething(activeVessel))
		{
			DespawnDresSomething();
			return;
		}
		if (dresDroppedPartObject != null && !dresDroppedPartLanded && Time.timeScale > 0f)
		{
			Vector3d normalized = (dresDroppedPartPosition - dresSomethingBody.position).normalized;
			Vector3d vector3d = ProjectToGround(dresDroppedPartPosition, dresSomethingBody);
			dresDroppedPartPosition -= normalized * 5.0 * TimeWarp.deltaTime;
			if ((dresDroppedPartPosition - vector3d).magnitude <= 0.35)
			{
				dresDroppedPartPosition = vector3d;
				dresDroppedPartLanded = true;
			}
			dresDroppedPartObject.transform.position = dresDroppedPartPosition;
			dresDroppedPartObject.transform.Rotate(21f * TimeWarp.deltaTime, 37f * TimeWarp.deltaTime, 13f * TimeWarp.deltaTime, Space.Self);
		}
		Vector3d vector3d2 = dresSomethingBody.position + dresSomethingRelativePosition;
		UpdateDresSomethingAudio(vector3d2);
		if (Time.timeScale <= 0f)
		{
			return;
		}
		dresSomethingFrameTimer += TimeWarp.deltaTime;
		if (dresSomethingOpening)
		{
			dresSomethingOpeningTimer += TimeWarp.deltaTime;
			if (dresSomethingOpeningTimer >= 0.45f)
			{
				dresSomethingOpening = false;
				DropDresSomethingPart(vector3d2);
				dresSomethingDropping = true;
				dresSomethingFadeTimer = 7f;
				dresSomethingStatus = "LEAVING SOMETHING BEHIND";
			}
		}
		else if (!dresSomethingDropping)
		{
			if ((vector3d2 - activeVessel.GetWorldPos3D()).magnitude <= 30.0)
			{
				dresSomethingOpening = true;
				dresSomethingOpeningTimer = 0f;
				dresSomethingFrameTimer = 0f;
				dresSomethingStatus = "OPENING";
				if (dresSomethingOpenSoundLoaded && dresSomethingOpenAudio != null)
				{
					dresSomethingOpenAudio.Stop();
					dresSomethingOpenAudio.Play();
				}
			}
		}
		else
		{
			dresSomethingFadeTimer -= TimeWarp.deltaTime;
			if (dresSomethingFadeTimer <= 0f)
			{
				DespawnDresSomething();
			}
		}
	}

	private static bool IsAllowedDresDropPart(AvailablePart available)
	{
		if (available == null)
		{
			return false;
		}
		string text = (available.title ?? string.Empty).ToLowerInvariant();
		string text2 = (available.name ?? string.Empty).ToLowerInvariant();
		if (text.Contains("mk1 command pod") || text.Contains("fl-t400 fuel tank") || text.Contains("experiment storage unit") || text.Contains("engine nacelle") || text.Contains("modular girder segment xl"))
		{
			goto IL_00bf;
		}
		switch (text2)
		{
		case "mk1pod":
		case "fueltanksmall":
		case "sciencebox":
		case "nacellebody":
			goto IL_00bf;
		}
		int result = ((text2 == "structuralibeam3") ? 1 : 0);
		goto IL_00c0;
		IL_00c0:
		return (byte)result != 0;
		IL_00bf:
		result = 1;
		goto IL_00c0;
	}

	private bool SpawnDresDroppedVessel(AvailablePart chosen, Vector3d position)
	{
		Vessel activeVessel = FlightGlobals.ActiveVessel;
		if (chosen == null || chosen.partPrefab == null || activeVessel == null || dresSomethingBody == null || HighLogic.CurrentGame == null || HighLogic.CurrentGame.flightState == null)
		{
			return false;
		}
		try
		{
			Orbit orbit = new Orbit();
			Vector3d pos = position - dresSomethingBody.position;
			orbit.UpdateFromStateVectors(pos, activeVessel.GetObtVelocity(), dresSomethingBody, Planetarium.GetUniversalTime());
			uint id = HighLogic.CurrentGame.launchID++;
			ConfigNode configNode = ProtoVessel.CreatePartNode(chosen.name, id, (ProtoCrewMember[])null);
			ConfigNode node = ProtoVessel.CreateVesselNode("Unclaimed " + chosen.title, VesselType.DroppedPart, orbit, 0, new ConfigNode[1] { configNode }, (ConfigNode[])null);
			ProtoVessel protoVessel = new ProtoVessel(node, HighLogic.CurrentGame);
			HighLogic.CurrentGame.flightState.protoVessels.Add(protoVessel);
			protoVessel.Load(HighLogic.CurrentGame.flightState);
			return true;
		}
		catch (Exception ex)
		{
			UnityEngine.Debug.Log("[Kraken's Kreations] Failed to spawn Dres dropped vessel: " + ex.Message);
			return false;
		}
	}

	private void DropDresSomethingPart(Vector3d position)
	{
		if (PartLoader.LoadedPartsList == null || dresSomethingBody == null)
		{
			return;
		}
		List<AvailablePart> list = new List<AvailablePart>();
		foreach (AvailablePart loadedParts in PartLoader.LoadedPartsList)
		{
			if (IsAllowedDresDropPart(loadedParts) && loadedParts.partPrefab != null && loadedParts.partPrefab.GetComponentsInChildren<MeshFilter>(includeInactive: true).Length > 0)
			{
				list.Add(loadedParts);
			}
		}
		if (list.Count == 0)
		{
			return;
		}
		AvailablePart availablePart = list[UnityEngine.Random.Range(0, list.Count)];
		if (SpawnDresDroppedVessel(availablePart, position))
		{
			ScreenMessages.PostScreenMessage("Something falls out of it.", 4f, ScreenMessageStyle.UPPER_CENTER);
			return;
		}
		if (dresDroppedPartObject != null)
		{
			UnityEngine.Object.Destroy(dresDroppedPartObject);
		}
		dresDroppedPartObject = new GameObject("Dres Something Drop — " + availablePart.title);
		Transform transform = availablePart.partPrefab.transform;
		MeshFilter[] componentsInChildren = availablePart.partPrefab.GetComponentsInChildren<MeshFilter>(includeInactive: true);
		foreach (MeshFilter meshFilter in componentsInChildren)
		{
			Renderer component = meshFilter.GetComponent<Renderer>();
			if (!(meshFilter.sharedMesh == null) && !(component == null))
			{
				GameObject gameObject = new GameObject("Part mesh");
				gameObject.transform.SetParent(dresDroppedPartObject.transform, worldPositionStays: false);
				gameObject.transform.localPosition = transform.InverseTransformPoint(meshFilter.transform.position);
				gameObject.transform.localRotation = Quaternion.Inverse(transform.rotation) * meshFilter.transform.rotation;
				gameObject.transform.localScale = meshFilter.transform.lossyScale;
				MeshFilter meshFilter2 = gameObject.AddComponent<MeshFilter>();
				meshFilter2.sharedMesh = meshFilter.sharedMesh;
				MeshRenderer meshRenderer = gameObject.AddComponent<MeshRenderer>();
				meshRenderer.sharedMaterials = component.sharedMaterials;
				meshRenderer.enabled = true;
			}
		}
		if (dresDroppedPartObject.GetComponentsInChildren<Renderer>(includeInactive: true).Length == 0)
		{
			UnityEngine.Object.Destroy(dresDroppedPartObject);
			dresDroppedPartObject = null;
			return;
		}
		Vector3d normalized = (position - dresSomethingBody.position).normalized;
		dresDroppedPartPosition = position - normalized * 3.0;
		dresDroppedPartObject.transform.position = dresDroppedPartPosition;
		dresDroppedPartObject.transform.rotation = UnityEngine.Random.rotation;
		dresDroppedPartLanded = false;
		ScreenMessages.PostScreenMessage("Something falls out of it.", 4f, ScreenMessageStyle.UPPER_CENTER);
	}

	private void DespawnDresSomething()
	{
		dresSomethingActive = false;
		dresSomethingDropping = false;
		dresSomethingFadeTimer = 0f;
		dresSomethingFrameTimer = 0f;
		dresSomethingBody = null;
		dresSomethingRelativePosition = Vector3d.zero;
		dresSomethingStatus = "NONE";
		if (dresSomethingOpenAudio != null)
		{
			dresSomethingOpenAudio.Stop();
		}
		if (dresSomethingAmbientAudio != null)
		{
			dresSomethingAmbientAudio.Stop();
		}
		dresSomethingAmbientPausedByGame = false;
		if (dresSomethingBillboardObject != null)
		{
			UnityEngine.Object.Destroy(dresSomethingBillboardObject);
		}
		dresSomethingBillboardObject = null;
		dresSomethingBillboardMaterial = null;
	}

	private static bool IsEligibleForGrabberEncounter(Vessel vessel)
	{
		return vessel != null && vessel.mainBody != null && IsOrbital(vessel) && string.Equals(vessel.mainBody.bodyName, "Jool", StringComparison.OrdinalIgnoreCase) && vessel.altitude > 0.0 && vessel.altitude <= 1000000.0;
	}

	private void SpawnGrabberEntity(Vessel vessel)
	{
		if (!grabberEntityActive && IsEligibleForGrabberEncounter(vessel))
		{
			Vector3 vector = UnityEngine.Random.onUnitSphere;
			if (vector.sqrMagnitude < 0.01f)
			{
				vector = vessel.transform.right;
			}
			vector.Normalize();
			grabberEntityBody = vessel.mainBody;
			grabberEntityPosition = vessel.GetWorldPos3D() + (Vector3d)vector * 50000.0;
			grabberVisualPosition = grabberEntityPosition;
			grabberHasVisualPosition = true;
			grabberRelativePosition = grabberEntityPosition - grabberEntityBody.position;
			grabberBaseVelocity = ((vessel.orbit != null) ? vessel.orbit.GetVel() : Vector3d.zero);
			grabberEntityActive = true;
			grabberEntityGrabbing = false;
			grabberEntityFleeing = false;
			grabberFleeTimer = 0f;
			grabberBreakoutTimer = 0f;
			grabberEntityStatus = "APPROACHING";
			UpdateGrabberBillboard();
			ScreenMessages.PostScreenMessage("Something large is moving through low Jool orbit.", 5f, ScreenMessageStyle.UPPER_CENTER);
		}
	}

	private void UpdateGrabberEntity()
	{
		Vessel activeVessel = FlightGlobals.ActiveVessel;
		if (!grabberEntityActive)
		{
			return;
		}
		if (activeVessel == null || !IsEligibleForGrabberEncounter(activeVessel) || activeVessel.mainBody != grabberEntityBody)
		{
			DespawnGrabberEntity();
			return;
		}
		if (Time.timeScale <= 0f)
		{
			UpdateGrabberBillboard();
			return;
		}
		if (grabberEntityFleeing)
		{
			grabberFleeTimer += TimeWarp.deltaTime;
			grabberEntityPosition += grabberFleeDirection * 1250.0 * TimeWarp.deltaTime;
			grabberRelativePosition = grabberEntityPosition - grabberEntityBody.position;
			SmoothGrabberVisualPosition();
			grabberEntityStatus = "RETREATING";
			if (grabberFleeTimer >= 7f)
			{
				DespawnGrabberEntity();
			}
			else
			{
				UpdateGrabberBillboard();
			}
			return;
		}
		if (grabberEntityGrabbing)
		{
			grabberEntityPosition = activeVessel.GetWorldPos3D();
			grabberVisualPosition = grabberEntityPosition;
			Vector3d vector3d = activeVessel.GetObtVelocity();
			if (vector3d.sqrMagnitude > 0.01)
			{
				if (grabberGrabStartOrbitalSpeed <= 0.0)
				{
					grabberGrabStartOrbitalSpeed = vector3d.magnitude;
				}
				if (CanBreakFreeFromGrabber(activeVessel))
				{
					grabberBreakoutTimer += TimeWarp.deltaTime;
				}
				else
				{
					grabberBreakoutTimer = 0f;
				}
				if (grabberBreakoutTimer >= 0.75f)
				{
					ScreenMessages.PostScreenMessage("The Grabber loses its grip.", 4f, ScreenMessageStyle.UPPER_CENTER);
					ReleaseGrabberEntity(activeVessel, vector3d);
					return;
				}
				if (grabberGrabStartOrbitalSpeed - vector3d.magnitude >= 300.0)
				{
					ScreenMessages.PostScreenMessage("The Grabber lets go.", 4f, ScreenMessageStyle.UPPER_CENTER);
					ReleaseGrabberEntity(activeVessel, vector3d);
					return;
				}
				double magnitude = vector3d.magnitude;
				float num = Mathf.Max(0.1f, activeVessel.GetTotalMass());
				double num2 = 35f / num;
				double num3 = Math.Min(magnitude, num2 * (double)TimeWarp.deltaTime);
				Vector3d vector3d2 = vector3d * ((magnitude - num3) / magnitude);
				activeVessel.ChangeWorldVelocity(vector3d2 - vector3d);
			}
			grabberEntityStatus = "GRABBING — BLEEDING ORBITAL SPEED";
			UpdateGrabberBillboard();
			return;
		}
		grabberEntityPosition = grabberEntityBody.position + grabberRelativePosition;
		Vector3d vector3d3 = activeVessel.GetWorldPos3D() - grabberEntityPosition;
		double magnitude2 = vector3d3.magnitude;
		if (magnitude2 <= 28.0)
		{
			grabberEntityGrabbing = true;
			grabberGrabStartOrbitalSpeed = activeVessel.GetObtVelocity().magnitude;
			grabberBreakoutTimer = 0f;
			grabberVisualPosition = activeVessel.GetWorldPos3D();
			grabberEntityStatus = "GRABBING";
			UpdateGrabberBillboard();
			return;
		}
		double sqrMagnitude = grabberRelativePosition.sqrMagnitude;
		if (sqrMagnitude > 1.0)
		{
			Vector3d vector3d4 = -grabberRelativePosition.normalized * (grabberEntityBody.gravParameter / sqrMagnitude);
			grabberBaseVelocity += vector3d4 * TimeWarp.deltaTime;
		}
		Vector3d vector3d5 = vector3d3.normalized * 650.0 * Mathf.Clamp(grabberSpeedMultiplier, 0.05f, 20f);
		grabberRelativePosition += (grabberBaseVelocity + vector3d5) * TimeWarp.deltaTime;
		grabberEntityPosition = grabberEntityBody.position + grabberRelativePosition;
		SmoothGrabberVisualPosition();
		grabberEntityStatus = "APPROACHING";
		UpdateGrabberBillboard();
	}

	private void ReleaseGrabberEntity(Vessel vessel, Vector3d orbitalVelocity)
	{
		grabberEntityGrabbing = false;
		grabberEntityFleeing = true;
		grabberFleeTimer = 0f;
		grabberBreakoutTimer = 0f;
		Vector3d vector3d = vessel.GetWorldPos3D() - grabberEntityBody.position;
		Vector3d vector3d2 = ((vector3d.sqrMagnitude > 1.0) ? vector3d.normalized : Vector3d.up);
		if (orbitalVelocity.sqrMagnitude > 0.01)
		{
			vector3d2 += -orbitalVelocity.normalized;
		}
		if (vector3d2.sqrMagnitude < 0.01)
		{
			vector3d2 = Vector3d.up;
		}
		grabberFleeDirection = vector3d2.normalized;
		grabberEntityPosition = vessel.GetWorldPos3D();
		grabberVisualPosition = grabberEntityPosition;
		grabberEntityStatus = "RETREATING";
		UpdateGrabberBillboard();
	}

	private static bool CanBreakFreeFromGrabber(Vessel vessel)
	{
		if (vessel == null || vessel.ctrlState.mainThrottle < 0.95f || vessel.parts == null)
		{
			return false;
		}
		float num = 0f;
		foreach (Part part in vessel.parts)
		{
			if (part == null || part.Modules == null)
			{
				continue;
			}
			foreach (PartModule module in part.Modules)
			{
				ModuleEngines moduleEngines = module as ModuleEngines;
				if (moduleEngines != null && moduleEngines.EngineIgnited && moduleEngines.isOperational)
				{
					num += Mathf.Max(0f, moduleEngines.finalThrust);
				}
			}
		}
		float num2 = Mathf.Max(0.1f, vessel.GetTotalMass());
		float num3 = num / num2;
		float num4 = 35f / num2;
		return num3 >= num4 + 8f;
	}

	private void DespawnGrabberEntity()
	{
		grabberEntityActive = false;
		grabberEntityGrabbing = false;
		grabberEntityFleeing = false;
		grabberEntityBody = null;
		grabberEntityPosition = Vector3d.zero;
		grabberVisualPosition = Vector3d.zero;
		grabberRelativePosition = Vector3d.zero;
		grabberBaseVelocity = Vector3d.zero;
		grabberHasVisualPosition = false;
		grabberGrabStartOrbitalSpeed = 0.0;
		grabberFleeDirection = Vector3d.zero;
		grabberFleeTimer = 0f;
		grabberBreakoutTimer = 0f;
		grabberEntityStatus = "NONE";
		if (grabberBillboardObject != null)
		{
			UnityEngine.Object.Destroy(grabberBillboardObject);
		}
		grabberBillboardObject = null;
		grabberBillboardMaterial = null;
	}

	private void SmoothGrabberVisualPosition()
	{
		if (!grabberHasVisualPosition)
		{
			grabberVisualPosition = grabberEntityPosition;
			grabberHasVisualPosition = true;
		}
		else
		{
			float num = 1f - Mathf.Exp(-8f * TimeWarp.deltaTime);
			grabberVisualPosition = Vector3d.Lerp(grabberVisualPosition, grabberEntityPosition, num);
		}
	}

	private void SpawnEyeEntity(Vessel vessel)
	{
		if (!(vessel == null))
		{
			eyeWatchDirection = UnityEngine.Random.onUnitSphere;
			if (eyeWatchDirection.sqrMagnitude < 0.01)
			{
				eyeWatchDirection = vessel.transform.right;
			}
			eyeWatchDirection.Normalize();
			eyeEntityPosition = vessel.GetWorldPos3D() + eyeWatchDirection * 50.0;
			eyeEntityActive = true;
			eyeEntityChasing = false;
			eyeChaseTimer = 0f;
			eyeWarningTextTimer = 10f;
			if (eyeSpawnAudio != null && eyeSpawnSoundLoaded)
			{
				eyeSpawnAudio.Play();
			}
		}
	}

	private void ForceSpawnEyeEntity()
	{
		Vessel activeVessel = FlightGlobals.ActiveVessel;
		if (!(activeVessel == null) && !(activeVessel.mainBody == null))
		{
			string a = activeVessel.mainBody.bodyName ?? string.Empty;
			if (!string.Equals(a, "Jool", StringComparison.OrdinalIgnoreCase) && !string.Equals(a, "Bop", StringComparison.OrdinalIgnoreCase))
			{
				ScreenMessages.PostScreenMessage("The Jool Eye can only be forced in Jool's or Bop's SOI.", 4f, ScreenMessageStyle.UPPER_CENTER);
				return;
			}
			DespawnEyeEntity();
			SpawnEyeEntity(activeVessel);
			eyeLastObservedBody = activeVessel.mainBody;
		}
	}

	private void MoveEyeToward(Vector3d target, float speed)
	{
		if (!(Time.timeScale <= 0f))
		{
			Vector3d vector3d = target - eyeEntityPosition;
			double magnitude = vector3d.magnitude;
			if (!(magnitude <= 0.01))
			{
				eyeEntityPosition += vector3d.normalized * Math.Min(magnitude, speed * TimeWarp.deltaTime);
			}
		}
	}

	private void UpdateEyeChaseOrbit(Vessel vessel, float chaseSpeed)
	{
		if (!(vessel == null) && !(eyeChaseReferenceBody == null) && !(Time.timeScale <= 0f))
		{
			eyeEntityPosition = eyeChaseReferenceBody.position + eyeChaseRelativePosition;
			Vector3d vector3d = vessel.GetWorldPos3D() - eyeEntityPosition;
			Vector3d vector3d2 = ((vector3d.sqrMagnitude > 0.01) ? (vector3d.normalized * chaseSpeed) : Vector3d.zero);
			double sqrMagnitude = eyeChaseRelativePosition.sqrMagnitude;
			if (sqrMagnitude > 1.0)
			{
				Vector3d vector3d3 = -eyeChaseRelativePosition.normalized * (eyeChaseReferenceBody.gravParameter / sqrMagnitude);
				eyeChaseBaseVelocity += vector3d3 * TimeWarp.deltaTime;
			}
			eyeChaseRelativePosition += (eyeChaseBaseVelocity + vector3d2) * TimeWarp.deltaTime;
			eyeEntityPosition = eyeChaseReferenceBody.position + eyeChaseRelativePosition;
		}
	}

	private void DespawnEyeEntity()
	{
		eyeEntityActive = false;
		eyeEntityChasing = false;
		eyeChaseTimer = 0f;
		eyeWarningTextTimer = 0f;
		eyeEntityPosition = Vector3d.zero;
		eyeChaseReferenceBody = null;
		eyeChaseRelativePosition = Vector3d.zero;
		eyeChaseBaseVelocity = Vector3d.zero;
		if (eyeSpawnAudio != null)
		{
			eyeSpawnAudio.Stop();
		}
		if (eyeWarningAudio != null)
		{
			eyeWarningAudio.Stop();
		}
		if (eyeChaseAudio != null)
		{
			eyeChaseAudio.Stop();
		}
		if (eyeBillboardObject != null)
		{
			UnityEngine.Object.Destroy(eyeBillboardObject);
		}
		eyeBillboardObject = null;
		eyeBillboardMaterial = null;
	}

	private static Vector3d ProjectToEveSea(Vector3d worldPosition, CelestialBody body)
	{
		double latitude = body.GetLatitude(worldPosition);
		double longitude = body.GetLongitude(worldPosition);
		return body.GetWorldSurfacePosition(latitude, longitude, -18.0);
	}

	private static bool IsSeaEntityActiveRange(Vessel vessel)
	{
		return vessel != null && vessel.mainBody != null && vessel.altitude <= 30.0 && string.Equals(vessel.mainBody.bodyName, "Eve", StringComparison.OrdinalIgnoreCase);
	}

	private static bool CanSeaEntityTarget(Vessel vessel)
	{
		return IsSeaEntityActiveRange(vessel) && (vessel.Splashed || vessel.altitude < -0.1);
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

	private void SpawnRakeEntity()
	{
		Vessel activeVessel = FlightGlobals.ActiveVessel;
		if (!IsEligibleForRakeEncounter(activeVessel))
		{
			rakeEntityStatus = "NOT ON AN ELIGIBLE BODY";
			return;
		}
		DespawnRakeEntity();
		rakeEntityBody = activeVessel.mainBody;
		if (!TryFindLandSpawnPosition(activeVessel, rakeEntityBody, 6000f, 18000f, out var groundPosition))
		{
			rakeEntityBody = null;
			rakeEntityStatus = "NO LAND SPAWN POSITION";
			return;
		}
		rakeEntityObject = ((GameDatabase.Instance != null) ? GameDatabase.Instance.GetModel("Kraken's Kreations/Models/the_rake") : null);
		if (rakeEntityObject == null)
		{
			rakeEntityBody = null;
			rakeEntityStatus = "MODEL UNAVAILABLE";
			return;
		}
		rakeEntityObject.name = "EntityTracker_Rake";
		rakeEntityObject.SetActive(value: true);
		rakeEntityObject.transform.localScale = Vector3.one * 1.2f;
		Transform[] componentsInChildren = rakeEntityObject.GetComponentsInChildren<Transform>(includeInactive: true);
		foreach (Transform transform in componentsInChildren)
		{
			transform.gameObject.SetActive(value: true);
			transform.gameObject.layer = 0;
		}
		Renderer[] componentsInChildren2 = rakeEntityObject.GetComponentsInChildren<Renderer>(includeInactive: true);
		foreach (Renderer renderer in componentsInChildren2)
		{
			renderer.enabled = true;
		}
		Collider[] componentsInChildren3 = rakeEntityObject.GetComponentsInChildren<Collider>(includeInactive: true);
		foreach (Collider obj in componentsInChildren3)
		{
			UnityEngine.Object.Destroy(obj);
		}
		Rigidbody[] componentsInChildren4 = rakeEntityObject.GetComponentsInChildren<Rigidbody>(includeInactive: true);
		foreach (Rigidbody obj2 in componentsInChildren4)
		{
			UnityEngine.Object.Destroy(obj2);
		}
		DisableImportedRootMotion(rakeEntityObject);
		rakeEntityRelativePosition = groundPosition - rakeEntityBody.position;
		rakeEntityObject.transform.position = groundPosition;
		rakeEntityAnimation = rakeEntityObject.AddComponent<RakeEntityAnimation>();
		rakeEntityAnimation.Configure(rakeEntityObject);
		rakeBoneAnimation = rakeEntityObject.AddComponent<RakeBoneAnimation>();
		rakeBoneAnimation.Configure(rakeEntityObject);
		rakeChaseAudio = ConfigureGroundAudioSource(rakeEntityObject.AddComponent<AudioSource>(), 4000f, 1f);
		rakeChaseAudio.loop = true;
		StartCoroutine(LoadRakeChaseSound());
		rakeEntityActive = true;
		rakeEntityChasing = false;
		rakeEntityStandingDown = false;
		rakeEntityRetreating = false;
		rakeWithdrawalTimer = 0f;
		rakeChaseSurvivalTimer = 0f;
		rakeEntityVisible = false;
		rakeObservationTimer = 0f;
		rakeAnimationPhase = UnityEngine.Random.Range(0f, (float)Math.PI * 2f);
		NoteRakeSpawned();
		rakeEntityStatus = "WATCHING";
	}

	private static bool IsEligibleForRakeEncounter(Vessel vessel)
	{
		if (vessel == null || vessel.mainBody == null || vessel.Splashed || vessel.heightFromTerrain >= 10f)
		{
			return false;
		}
		string bodyName = vessel.mainBody.bodyName;
		if (string.Equals(bodyName, "Kerbin", StringComparison.OrdinalIgnoreCase) || string.Equals(bodyName, "Mun", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		return vessel.mainBody.pqsController != null;
	}

	private bool IsRakeVisibleToPlayer(Vessel vessel)
	{
		return IsVisualVisibleToPlayer(vessel, rakeEntityObject, rakeEntityBody, 7.66f);
	}

	private void UpdateRakeEntity()
	{
		if (!rakeEntityActive || rakeEntityObject == null || rakeEntityBody == null)
		{
			return;
		}
		if (IsAttackSequenceRunning(AttackKind.Rake))
		{
			return;
		}
		Vessel activeVessel = FlightGlobals.ActiveVessel;
		if (activeVessel == null)
		{
			return;
		}
		Vector3d vector3d = rakeEntityBody.position + rakeEntityRelativePosition;
		Vector3d vector3d2 = activeVessel.GetWorldPos3D() - vector3d;
		double magnitude = vector3d2.magnitude;
		bool flag = activeVessel.heightFromTerrain < 10f;
		bool huntAware = huntAi && HuntSense(huntRake, rakeEntityObject, vector3d, rakeEntityBody, activeVessel, Quaternion.Euler(0f, 180f, 0f) * RakeModelAxisCorrection);
		if (huntAi && rakeEntityChasing && !huntAware && !rakeEntityStandingDown && !rakeEntityRetreating)
		{
			rakeEntityChasing = false;
			rakeEntityStatus = "NO LOCAL TARGETS";
		}
		if (!rakeEntityChasing && !rakeEntityStandingDown && !rakeEntityRetreating && (huntAi ? huntAware : flag && magnitude <= 16000.0 && HasGroundLineOfSight(vector3d, activeVessel.GetWorldPos3D(), rakeEntityBody)))
		{
			rakeEntityChasing = true;
			rakeChaseSoundRequested = true;
			PlayRakeChaseSoundIfReady();
		}
		rakeEntityVisible = rakeEntityChasing && !rakeEntityStandingDown && !rakeEntityRetreating && IsRakeVisibleToPlayer(activeVessel);
		if (rakeEntityVisible)
		{
			rakeObservationTimer += TimeWarp.deltaTime;
			if (rakeObservationTimer >= 20f)
			{
				rakeEntityStandingDown = true;
				rakeEntityVisible = false;
				rakeWithdrawalTimer = 3f;
			}
		}
		else if (!rakeEntityRetreating && !rakeEntityStandingDown)
		{
			rakeObservationTimer = 0f;
		}
		if (rakeEntityChasing && !rakeEntityStandingDown && !rakeEntityRetreating && !rakeEntityVisible && (magnitude <= 8.0 || TakeHoverCatch(behaviorRake)))
		{
			if (attackSequences && BeginAttackSequence(AttackKind.Rake, activeVessel, rakeEntityObject, rakeEntityBody, 8f, -1))
			{
				return;
			}
			DespawnRakeEntity();
			HandleEntityCatch(activeVessel);
			return;
		}
		Vector3d normalized = (vector3d - rakeEntityBody.position).normalized;
		bool flag2 = 0 == 0;
		if (rakeEntityStandingDown)
		{
			rakeWithdrawalTimer -= TimeWarp.deltaTime;
			rakeEntityStatus = "STANDING STILL";
			if (rakeWithdrawalTimer <= 0f)
			{
				rakeEntityStandingDown = false;
				rakeEntityRetreating = true;
				rakeWithdrawalTimer = 30f;
			}
		}
		else if (rakeEntityRetreating)
		{
			rakeWithdrawalTimer -= TimeWarp.deltaTime;
			if (flag && Time.timeScale > 0f)
			{
				Vector3d vector3d3 = -vector3d2 - normalized * Vector3d.Dot(-vector3d2, normalized);
				if (vector3d3.sqrMagnitude > 1.0)
				{
					vector3d += vector3d3.normalized * GetGroundEntitySpeed(58f) * TimeWarp.deltaTime;
					vector3d = ProjectToGround(vector3d, rakeEntityBody);
					rakeEntityRelativePosition = vector3d - rakeEntityBody.position;
				}
			}
			rakeEntityStatus = "RETREATING";
			if (rakeWithdrawalTimer <= 0f)
			{
				if (BeginRakeReturnWait())
				{
					return;
				}
				DespawnRakeEntity();
				return;
			}
		}
		else if (rakeEntityVisible)
		{
			rakeEntityStatus = "FROZEN - WATCHING";
		}
		else if (rakeEntityChasing && (flag || huntAi) && Time.timeScale > 0f)
		{
			string smartStatus = null;
			if ((huntAi ? HuntGroundApproach(huntRake, behaviorRake, rakeEntityObject, ref vector3d, activeVessel, rakeEntityBody, GetGroundEntitySpeed(58f), out smartStatus) : smartEntityAi && SmartGroundApproach(behaviorRake, rakeEntityObject, ref vector3d, vector3d2, rakeEntityBody, GetGroundEntitySpeed(58f), out smartStatus)))
			{
			}
			else if (magnitude <= 20.0)
			{
				vector3d += vector3d2.normalized * GetGroundEntitySpeed(58f) * TimeWarp.deltaTime;
			}
			else
			{
				Vector3d vector3d4 = vector3d2 - normalized * Vector3d.Dot(vector3d2, normalized);
				if (vector3d4.sqrMagnitude > 1.0)
				{
					vector3d += vector3d4.normalized * GetGroundEntitySpeed(58f) * TimeWarp.deltaTime;
					vector3d = ProjectToGround(vector3d, rakeEntityBody);
				}
			}
			rakeEntityRelativePosition = vector3d - rakeEntityBody.position;
			rakeEntityStatus = smartStatus ?? "CHASING";
		}
		else if (rakeEntityChasing && HoverGroundAttack(behaviorRake, rakeEntityObject, ref vector3d, activeVessel, rakeEntityBody, GetGroundEntitySpeed(58f), out var hoverStatus))
		{
			rakeEntityRelativePosition = vector3d - rakeEntityBody.position;
			rakeEntityStatus = hoverStatus;
		}
		else if (rakeEntityChasing && hoverAttacks && activeVessel.heightFromTerrain > GetGroundTargetHeightLimit())
		{
			rakeEntityChasing = false;
			rakeEntityStatus = "NO LOCAL TARGETS";
		}
		else if (rakeEntityChasing)
		{
			rakeEntityStatus = "CANNOT REACH";
		}
		bool flag3 = rakeEntityChasing && !rakeEntityStandingDown && !rakeEntityRetreating && !rakeEntityVisible && HuntIsMoving(huntRake);
		rakeAnimationPhase += (flag3 ? (TimeWarp.deltaTime * 11f) : 0f);
		float num = (flag3 ? (Mathf.Abs(Mathf.Sin(rakeAnimationPhase)) * 0.22f) : 0f);
		rakeEntityObject.transform.position = vector3d + normalized * (num - 6.56f + GetLeapLift(behaviorRake));
		Vector3d huntDir = Vector3d.zero;
		if (huntAi && HuntFacing(huntRake, vector3d, rakeEntityBody, out huntDir))
		{
			Quaternion huntRotation = Quaternion.LookRotation(huntDir, normalized) * Quaternion.Euler(0f, 180f, 0f) * RakeModelAxisCorrection;
			if (flag3)
			{
				huntRotation *= Quaternion.Euler(3f * Mathf.Sin(rakeAnimationPhase * 2f), 0f, 5f * Mathf.Sin(rakeAnimationPhase));
			}
			rakeEntityObject.transform.rotation = huntRotation;
		}
		else if (huntAi && !rakeEntityStandingDown && !rakeEntityRetreating)
		{
		}
		else if (vector3d2.sqrMagnitude > 0.01)
		{
			Vector3d lhs = (rakeEntityRetreating ? (-vector3d2) : vector3d2);
			lhs -= normalized * Vector3d.Dot(lhs, normalized);
			if (lhs.sqrMagnitude > 0.01)
			{
				Quaternion rotation = Quaternion.LookRotation(lhs.normalized, normalized) * Quaternion.Euler(0f, 180f, 0f) * RakeModelAxisCorrection;
				if (flag3)
				{
					rotation *= Quaternion.Euler(3f * Mathf.Sin(rakeAnimationPhase * 2f), 0f, 5f * Mathf.Sin(rakeAnimationPhase));
				}
				rakeEntityObject.transform.rotation = rotation;
			}
		}
		ApplyEntityMotion(motionRake, MotionKind.Rake, rakeEntityObject, rakeEntityBody, vector3d, behaviorRake, GetGroundEntitySpeed(58f));
		HuntUpdateHead(huntRake, rakeEntityObject, vector3d, rakeEntityBody, activeVessel);
		if (rakeEntityAnimation != null)
		{
			rakeEntityAnimation.SetChasing(flag3);
		}
		if (rakeBoneAnimation != null)
		{
			rakeBoneAnimation.SetChasing(flag3);
		}
		UpdateRakeChaseSound();
	}

	private void DespawnRakeEntity()
	{
		if (rakeEntityObject != null)
		{
			UnityEngine.Object.Destroy(rakeEntityObject);
		}
		rakeEntityObject = null;
		rakeEntityAnimation = null;
		rakeBoneAnimation = null;
		rakeChaseAudio = null;
		rakeChaseSoundLoaded = false;
		rakeChaseSoundRequested = false;
		rakeEntityBody = null;
		rakeEntityRelativePosition = Vector3d.zero;
		rakeEntityActive = false;
		rakeEntityChasing = false;
		rakeEntityStandingDown = false;
		rakeEntityRetreating = false;
		rakeWithdrawalTimer = 0f;
		rakeChaseSurvivalTimer = 0f;
		rakeEntityVisible = false;
		rakeObservationTimer = 0f;
		rakeAnimationPhase = 0f;
		rakeEntityStatus = "NONE";
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

	private void ChooseGroundRoamDirection(Vector3d entityPosition)
	{
		if (!(groundEntityBody == null))
		{
			Vector3d normalized = (entityPosition - groundEntityBody.position).normalized;
			Vector3 onUnitSphere = UnityEngine.Random.onUnitSphere;
			Vector3d vector3d = onUnitSphere - normalized * Vector3d.Dot(onUnitSphere, normalized);
			groundRoamDirection = ((vector3d.sqrMagnitude > 0.001) ? vector3d.normalized : Vector3d.Cross(normalized, Vector3d.up).normalized);
			groundRoamDirectionTimer = UnityEngine.Random.Range(8f, 22f);
		}
	}

	private static GameObject CreateGroundEntityVisual(GroundVariant variant)
	{
		bool flag = variant == GroundVariant.Pilot || variant == GroundVariant.Engineer;
		bool flag2 = variant == GroundVariant.Tourist;
		Transform[] componentsInChildren;
		Renderer[] componentsInChildren2;
		Collider[] componentsInChildren3;
		Rigidbody[] componentsInChildren4;
		if (flag)
		{
			GameObject gameObject = ((GameDatabase.Instance != null) ? GameDatabase.Instance.GetModel("Kraken's Kreations/Models/kerbal_mimic") : null);
			if (gameObject == null)
			{
				UnityEngine.Debug.Log("[EntityTracker] Could not load Kraken's Kreations/Models/kerbal_mimic.mu");
				return null;
			}
			gameObject.name = "EntityTracker_AnimatedMimic";
			gameObject.SetActive(value: true);
			componentsInChildren = gameObject.GetComponentsInChildren<Transform>(includeInactive: true);
			foreach (Transform transform in componentsInChildren)
			{
				transform.gameObject.SetActive(value: true);
				transform.gameObject.layer = 0;
			}
			componentsInChildren2 = gameObject.GetComponentsInChildren<Renderer>(includeInactive: true);
			foreach (Renderer renderer in componentsInChildren2)
			{
				renderer.enabled = true;
				SkinnedMeshRenderer skinnedMeshRenderer = renderer as SkinnedMeshRenderer;
				if (skinnedMeshRenderer != null)
				{
					skinnedMeshRenderer.updateWhenOffscreen = true;
				}
			}
			componentsInChildren3 = gameObject.GetComponentsInChildren<Collider>(includeInactive: true);
			foreach (Collider obj in componentsInChildren3)
			{
				UnityEngine.Object.Destroy(obj);
			}
			componentsInChildren4 = gameObject.GetComponentsInChildren<Rigidbody>(includeInactive: true);
			foreach (Rigidbody obj2 in componentsInChildren4)
			{
				UnityEngine.Object.Destroy(obj2);
			}
			StripImportedEntityControllersKeepAnimation(gameObject);
			gameObject.transform.localScale = Vector3.one * 1.25f;
			Renderer[] componentsInChildren5 = gameObject.GetComponentsInChildren<Renderer>(includeInactive: true);
			GameObject[] idleFrames = LoadBakedMimicFrames(gameObject, "kerbal_mimic_idle_", 4);
			GameObject[] aggroFrames = LoadBakedMimicFrames(gameObject, "kerbal_mimic_aggro_", 6);
			GameObject[] chaseFrames = LoadBakedMimicFrames(gameObject, "kerbal_mimic_chase_", 4);
			MimicImportedAnimation mimicImportedAnimation = gameObject.AddComponent<MimicImportedAnimation>();
			mimicImportedAnimation.Configure(gameObject.GetComponentInChildren<Animation>(includeInactive: true));
			mimicImportedAnimation.ConfigureBakedAnimations(idleFrames, aggroFrames, chaseFrames, componentsInChildren5);
			return gameObject;
		}
		Component component = FindLoadedKerbalModel();
		if (component == null)
		{
			UnityEngine.Debug.Log("[EntityTracker] No stock Kerbal model is currently loaded.");
			return null;
		}
		GameObject gameObject2 = UnityEngine.Object.Instantiate(component.gameObject);
		gameObject2.name = "EntityTracker_StretchedKerbal";
		gameObject2.SetActive(value: true);
		componentsInChildren = gameObject2.GetComponentsInChildren<Transform>(includeInactive: true);
		foreach (Transform transform in componentsInChildren)
		{
			transform.gameObject.SetActive(value: true);
			transform.gameObject.layer = 0;
		}
		if (gameObject2.GetComponentsInChildren<Renderer>(includeInactive: true).Length == 0)
		{
			UnityEngine.Object.Destroy(gameObject2);
			UnityEngine.Debug.Log("[EntityTracker] The selected EVA prefab contained no visible Kerbal mesh.");
			return null;
		}
		componentsInChildren2 = gameObject2.GetComponentsInChildren<Renderer>(includeInactive: true);
		foreach (Renderer renderer in componentsInChildren2)
		{
			renderer.enabled = true;
			SkinnedMeshRenderer skinnedMeshRenderer = renderer as SkinnedMeshRenderer;
			if (skinnedMeshRenderer != null)
			{
				skinnedMeshRenderer.updateWhenOffscreen = true;
			}
		}
		gameObject2.transform.localScale = Vector3.one * (flag2 ? 0.72f : 1.25f);
		StripImportedEntityControllers(gameObject2);
		ConfigureGlitchRig(gameObject2);
		componentsInChildren3 = gameObject2.GetComponentsInChildren<Collider>(includeInactive: true);
		foreach (Collider obj in componentsInChildren3)
		{
			UnityEngine.Object.Destroy(obj);
		}
		componentsInChildren4 = gameObject2.GetComponentsInChildren<Rigidbody>(includeInactive: true);
		foreach (Rigidbody obj2 in componentsInChildren4)
		{
			UnityEngine.Object.Destroy(obj2);
		}
		Component[] componentsInChildren6 = gameObject2.GetComponentsInChildren<Component>(includeInactive: true);
		foreach (Component component2 in componentsInChildren6)
		{
			string text = component2.GetType().Name;
			switch (text)
			{
			default:
				if (!(text == "PartModule"))
				{
					continue;
				}
				break;
			case "KerbalEVA":
			case "Kerbal3DModel":
			case "Kerbal":
			case "Part":
				break;
			}
			UnityEngine.Object.Destroy(component2);
		}
		Animator[] componentsInChildren7 = gameObject2.GetComponentsInChildren<Animator>(includeInactive: true);
		foreach (Animator animator in componentsInChildren7)
		{
			animator.enabled = false;
		}
		Animation[] componentsInChildren8 = gameObject2.GetComponentsInChildren<Animation>(includeInactive: true);
		foreach (Animation animation in componentsInChildren8)
		{
			animation.Stop();
			animation.enabled = false;
		}
		return gameObject2;
	}

	private static GameObject[] LoadBakedMimicFrames(GameObject parent, string filePrefix, int count)
	{
		List<GameObject> list = new List<GameObject>();
		for (int i = 0; i < count; i++)
		{
			GameObject gameObject = ((GameDatabase.Instance != null) ? GameDatabase.Instance.GetModel("Kraken's Kreations/Models/" + filePrefix + i) : null);
			if (!(gameObject == null))
			{
				gameObject.transform.SetParent(parent.transform, worldPositionStays: false);
				gameObject.transform.localPosition = Vector3.zero;
				gameObject.transform.localRotation = Quaternion.identity;
				gameObject.transform.localScale = Vector3.one;
				gameObject.SetActive(value: false);
				list.Add(gameObject);
			}
		}
		return list.ToArray();
	}

	private static void ConfigureGlitchRig(GameObject root)
	{
		HashSet<Transform> hashSet = new HashSet<Transform>();
		SkinnedMeshRenderer[] componentsInChildren = root.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: true);
		foreach (SkinnedMeshRenderer skinnedMeshRenderer in componentsInChildren)
		{
			Transform[] bones = skinnedMeshRenderer.bones;
			foreach (Transform transform in bones)
			{
				if (transform != null && transform != root.transform)
				{
					hashSet.Add(transform);
				}
			}
		}
		GroundEntityGlitchAnimation groundEntityGlitchAnimation = root.AddComponent<GroundEntityGlitchAnimation>();
		groundEntityGlitchAnimation.Configure(new List<Transform>(hashSet));
	}

	private static Component FindLoadedKerbalModel()
	{
		UnityEngine.Object[] array = Resources.FindObjectsOfTypeAll(typeof(Component));
		UnityEngine.Object[] array2 = array;
		foreach (UnityEngine.Object obj in array2)
		{
			Component component = obj as Component;
			if (component != null && (component.GetType().Name == "Kerbal3DModel" || component.GetType().Name == "Kerbal") && component.GetComponentsInChildren<Renderer>(includeInactive: true).Length > 0)
			{
				return component;
			}
		}
		if (PartLoader.LoadedPartsList != null)
		{
			foreach (AvailablePart loadedParts in PartLoader.LoadedPartsList)
			{
				if (loadedParts != null && loadedParts.name == "kerbalEVA" && loadedParts.partPrefab != null)
				{
					return loadedParts.partPrefab;
				}
			}
		}
		return null;
	}

	private void DespawnGroundEntity()
	{
		if (groundEntityObject != null)
		{
			UnityEngine.Object.Destroy(groundEntityObject);
		}
		groundEntityObject = null;
		groundEntityAnimation = null;
		groundFootstepsAudio = null;
		groundScreamAudio = null;
		groundFootstepsLoaded = false;
		groundScreamLoaded = false;
		groundEntityBody = null;
		groundEntityRelativePosition = Vector3d.zero;
		groundEntityActive = false;
		groundEntityChasing = false;
		groundEntitySpottingTimer = 0f;
		groundSeenVesselIds.Clear();
		groundEntityFleeing = false;
		groundEntityFleeTimer = 0f;
		groundEntityStandingDown = false;
		groundEntityRetreating = false;
		groundEntityWithdrawalTimer = 0f;
		groundEntityChaseSurvivalTimer = 0f;
		groundVariant = GroundVariant.Pilot;
		groundRoamDirection = Vector3d.zero;
		groundRoamDirectionTimer = 0f;
		groundEntityStatus = "NONE";
	}

	private void CreateUnknownMapContacts()
	{
		for (int i = 0; i < 4; i++)
		{
			mapContacts.Add(new UnknownMapContact
			{
				phase = UnityEngine.Random.Range(0f, 360f),
				radius = SelectSolarOrbitRadius() * (double)UnityEngine.Random.Range(0.75f, 1.25f),
				inclination = UnityEngine.Random.Range(-0.12f, 0.12f),
				angularRate = UnityEngine.Random.Range(0.002f, 0.012f),
				visibleStart = UnityEngine.Random.Range(0f, 210f)
			});
		}
	}

	private static double SelectSolarOrbitRadius()
	{
		CelestialBody celestialBody = ((Planetarium.fetch != null) ? Planetarium.fetch.Sun : null);
		if (celestialBody != null)
		{
			List<CelestialBody> list = new List<CelestialBody>();
			foreach (CelestialBody body in FlightGlobals.Bodies)
			{
				if (body != celestialBody && body.orbit != null && body.orbit.referenceBody == celestialBody)
				{
					list.Add(body);
				}
			}
			if (list.Count > 0)
			{
				return list[UnityEngine.Random.Range(0, list.Count)].orbit.semiMajorAxis;
			}
		}
		return 15000000000.0;
	}

	private void DrawUnknownMapContacts()
	{
		if (Planetarium.fetch == null || Planetarium.fetch.Sun == null || PlanetariumCamera.Camera == null)
		{
			return;
		}
		float realtimeSinceStartup = Time.realtimeSinceStartup;
		GUIStyle gUIStyle = new GUIStyle(GUI.skin.label);
		gUIStyle.fontSize = 18;
		gUIStyle.fontStyle = FontStyle.Bold;
		gUIStyle.normal.textColor = new Color(1f, 0.15f, 0.15f, 0.9f);
		gUIStyle.alignment = TextAnchor.MiddleCenter;
		foreach (UnknownMapContact mapContact in mapContacts)
		{
			if (!(Mathf.Repeat(realtimeSinceStartup + mapContact.visibleStart, 210f) > 30f))
			{
				double num = (mapContact.phase + realtimeSinceStartup * mapContact.angularRate) * ((float)Math.PI / 180f);
				double radius = mapContact.radius;
				Vector3d localSpacePoint = Planetarium.fetch.Sun.position + new Vector3d(Math.Cos(num) * radius, Math.Sin(num) * radius * (double)mapContact.inclination, Math.Sin(num) * radius);
				Vector3 position = ScaledSpace.LocalToScaledSpace(localSpacePoint);
				Vector3 vector = PlanetariumCamera.Camera.WorldToScreenPoint(position);
				if (!(vector.z <= 0f) && !(vector.x < 0f) && !(vector.x > (float)Screen.width) && !(vector.y < 0f) && !(vector.y > (float)Screen.height))
				{
					GUI.Label(new Rect(vector.x - 10f, (float)Screen.height - vector.y - 10f, 20f, 20f), "?", gUIStyle);
				}
			}
		}
	}

	private void DrawTracker(int id)
	{
		Vessel activeVessel = FlightGlobals.ActiveVessel;
		if (activeVessel == null)
		{
			GUILayout.Label("NO ACTIVE VESSEL");
			GUI.DragWindow();
			return;
		}
		bool flag = !tracking && dresSomethingActive && dresSomethingBody != null;
		Vector3d vector3d = (flag ? (dresSomethingBody.position + dresSomethingRelativePosition) : entityPosition);
		double magnitude = (vector3d - activeVessel.GetWorldPos3D()).magnitude;
		string text = ((!flag) ? (arrived ? "STATUS: SIGNAL COINCIDENT" : "STATUS: APPROACHING") : (dresSomethingOpening ? "STATUS: OPENING" : (dresSomethingDropping ? "STATUS: FADING" : "STATUS: ANOMALY DETECTED")));
		Vector3 normalized = ((Vector3)(vector3d - activeVessel.GetWorldPos3D())).normalized;
		int num = Mathf.RoundToInt((Vector3.SignedAngle(activeVessel.transform.forward, normalized, activeVessel.transform.up) + 360f) % 360f);
		GUILayout.Label(text);
		GUILayout.Label("DISTANCE: " + FormatDistance(magnitude));
		GUILayout.Label("BEARING: " + num.ToString("000") + "°");
		GUILayout.Space(5f);
		GUILayout.Label("[P]  hide tracker");
		GUI.DragWindow();
	}

	private void ToggleTracking()
	{
		if (tracking)
		{
			trackerVisible = !trackerVisible;
		}
		else
		{
			StartTracking(FlightGlobals.ActiveVessel);
		}
	}

	private void StartTracking(Vessel vessel)
	{
		if (!(vessel == null))
		{
			Vector3 forward = vessel.transform.forward;
			Vector3 right = vessel.transform.right;
			Vector3 normalized = (forward * UnityEngine.Random.Range(-1f, 1f) + right * UnityEngine.Random.Range(-1f, 1f)).normalized;
			if (normalized.sqrMagnitude < 0.01f)
			{
				normalized = forward.normalized;
			}
			currentSpawnDistanceMetres = (IsInterplanetaryOrbit(vessel) ? 1000000f : 50000f);
			entityPosition = vessel.GetWorldPos3D() + (Vector3d)(normalized * currentSpawnDistanceMetres);
			entityReferenceBody = vessel.mainBody;
			SaveEntityWorldPosition();
			lastTrackingVesselPosition = vessel.GetWorldPos3D();
			hasLastTrackingVesselPosition = true;
			orbitalTetherOffset = entityPosition - vessel.GetWorldPos3D();
			orbitalTetherActive = IsOrbital(vessel);
			closestEncounterDistance = currentSpawnDistanceMetres;
			blackboxScienceAwarded = false;
			survivalTimerStarted = false;
			survivalTimer = 0f;
			lastCountdownSecond = -1;
			countdownTextShakeTimer = 0f;
			safeMessageTimer = 0f;
			arrived = false;
			arrivalHandled = false;
			tufxGlitchStarted = false;
			glitchFramesRemaining = 0;
			tracking = true;
			trackerVisible = true;
			ResetBoxEncounterTimeWarp();
			ApplyDistantSoundForEncounter();
			ApplyNearbySoundForEncounter();
			if (distantAudio != null)
			{
				distantAudio.maxDistance = currentSpawnDistanceMetres;
			}
			if (nearbyAudio != null)
			{
				nearbyAudio.maxDistance = currentSpawnDistanceMetres;
			}
			UpdateSound();
		}
	}

	private void ResetBoxEncounterTimeWarp()
	{
		if (tracking && HighLogic.LoadedSceneIsFlight && TimeWarp.CurrentRateIndex != 0)
		{
			TimeWarp.SetRate(0, instant: true);
		}
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
		bdArmoryGpsBridge.Sync("KK Mimic", groundEntityActive && groundEntityObject != null, (groundEntityObject != null) ? ((Vector3d)groundEntityObject.transform.position) : Vector3d.zero);
		bdArmoryGpsBridge.Sync("KK Moho-Eve Creature", bulkEntityActive && bulkEntityObject != null, (bulkEntityObject != null) ? ((Vector3d)bulkEntityObject.transform.position) : Vector3d.zero);
		bdArmoryGpsBridge.Sync("KK Eve Sea Creature", seaEntityActive && seaEntityObject != null, (seaEntityObject != null) ? ((Vector3d)seaEntityObject.transform.position) : Vector3d.zero);
		bdArmoryGpsBridge.Sync("KK Laythe Bloop", bloopEntityActive && bloopEntityObject != null, (bloopEntityObject != null) ? ((Vector3d)bloopEntityObject.transform.position) : Vector3d.zero);
		bdArmoryGpsBridge.Sync("KK Laythe Land Creature", laytheCreatureActive && laytheCreatureObject != null, (laytheCreatureObject != null) ? ((Vector3d)laytheCreatureObject.transform.position) : Vector3d.zero);
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
		bdArmoryGroundRadarBridge.Sync("KK Mimic", groundEntityActive && groundEntityObject != null, (groundEntityObject != null) ? groundEntityObject.transform.position : Vector3.zero, 18f);
		bdArmoryGroundRadarBridge.Sync("KK Moho-Eve Creature", bulkEntityActive && bulkEntityObject != null, (bulkEntityObject != null) ? bulkEntityObject.transform.position : Vector3.zero, 30f);
		bdArmoryGroundRadarBridge.Sync("KK Eve Sea Creature", seaEntityActive && seaEntityObject != null, (seaEntityObject != null) ? seaEntityObject.transform.position : Vector3.zero, 28f);
		bdArmoryGroundRadarBridge.Sync("KK Laythe Bloop", bloopEntityActive && bloopEntityObject != null, (bloopEntityObject != null) ? bloopEntityObject.transform.position : Vector3.zero, 75f);
		bdArmoryGroundRadarBridge.Sync("KK Laythe Land Creature", laytheCreatureActive && laytheCreatureObject != null, (laytheCreatureObject != null) ? laytheCreatureObject.transform.position : Vector3.zero, 24f);
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
		bdArmoryDamageBridge.Sync("Mimic", groundEntityActive, groundEntityObject, 12f, DestroyGroundEntityFromBda);
		bdArmoryDamageBridge.Sync("Moho-Eve Creature", bulkEntityActive, bulkEntityObject, 150f, DestroyBulkEntityFromBda);
		bdArmoryDamageBridge.Sync("Eve Sea Creature", seaEntityActive, seaEntityObject, 140f, DestroySeaEntityFromBda);
		bdArmoryDamageBridge.Sync("Laythe Bloop", bloopEntityActive, bloopEntityObject, 600f, DestroyBloopEntityFromBda);
		bdArmoryDamageBridge.Sync("Laythe Land Creature", laytheCreatureActive, laytheCreatureObject, 100f, DestroyLaytheCreatureFromBda);
		bdArmoryDamageBridge.Update();
	}

	private void DestroyGroundEntityFromBda()
	{
		DespawnGroundEntity();
		groundEntityStatus = "DESTROYED BY BDA";
	}

	private void DestroyBulkEntityFromBda()
	{
		DespawnBulkEntity();
		bulkEntityStatus = "DESTROYED BY BDA";
	}

	private void DestroySeaEntityFromBda()
	{
		DespawnSeaEntity();
		seaEntityStatus = "DESTROYED BY BDA";
	}

	private void DestroyBloopEntityFromBda()
	{
		DespawnBloopEntity();
		bloopEntityStatus = "DESTROYED BY BDA";
	}

	private void DestroyLaytheCreatureFromBda()
	{
		DespawnLaytheCreature();
		laytheCreatureStatus = "DESTROYED BY BDA";
	}

	private void LoadSettings()
	{
		string text = Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "Kraken's Kreations", "settings.cfg");
		ConfigNode configNode = (File.Exists(text) ? ConfigNode.Load(text) : null);
		if (configNode != null && configNode.HasValue("quitOnArrival"))
		{
			bool.TryParse(configNode.GetValue("quitOnArrival"), out quitOnArrival);
		}
		if (configNode != null && configNode.HasValue("alwaysVisible"))
		{
			bool.TryParse(configNode.GetValue("alwaysVisible"), out alwaysVisible);
		}
		if (configNode != null && configNode.HasValue("tufxGlitchOnArrival"))
		{
			bool.TryParse(configNode.GetValue("tufxGlitchOnArrival"), out tufxGlitchOnArrival);
		}
		if (configNode != null && configNode.HasValue("randomEncounters"))
		{
			bool.TryParse(configNode.GetValue("randomEncounters"), out randomEncounters);
		}
		if (configNode != null && configNode.HasValue("debugTools"))
		{
			bool.TryParse(configNode.GetValue("debugTools"), out debugTools);
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
		if (configNode != null && configNode.HasValue("unknownMapContacts"))
		{
			bool.TryParse(configNode.GetValue("unknownMapContacts"), out unknownMapContacts);
		}
		if (configNode != null && configNode.HasValue("classicMimicBehavior"))
		{
			bool.TryParse(configNode.GetValue("classicMimicBehavior"), out classicMimicBehavior);
		}
		if (configNode != null && configNode.HasValue("groundEntityDebugRadar"))
		{
			bool.TryParse(configNode.GetValue("groundEntityDebugRadar"), out groundEntityDebugRadar);
		}
		if (configNode != null && configNode.HasValue("debugGroundEntityBeacons"))
		{
			bool.TryParse(configNode.GetValue("debugGroundEntityBeacons"), out debugGroundEntityBeacons);
		}
		ReadFloatSetting(configNode, "randomCheckSeconds", ref randomCheckSeconds);
		ReadFloatSetting(configNode, "baseEncounterChance", ref baseEncounterChance);
		ReadFloatSetting(configNode, "grabberEncounterChance", ref grabberEncounterChance);
		ReadFloatSetting(configNode, "groundEncounterChance", ref groundEncounterChance);
		ReadFloatSetting(configNode, "groundEncounterCooldownSeconds", ref groundEncounterCooldownSeconds);
		ReadFloatSetting(configNode, "highEncounterMultiplier", ref highEncounterMultiplier);
		ReadFloatSetting(configNode, "rareEncounterMultiplier", ref rareEncounterMultiplier);
		ReadFloatSetting(configNode, "randomCooldownSeconds", ref randomCooldownSeconds);
		ReadFloatSetting(configNode, "approachSpeedMetresPerSecond", ref approachSpeedMetresPerSecond);
		ReadFloatSetting(configNode, "blackboxSpeedMultiplier", ref blackboxSpeedMultiplier);
		ReadFloatSetting(configNode, "groundEntitySpeedMultiplier", ref groundEntitySpeedMultiplier);
		ReadFloatSetting(configNode, "seaEntitySpeedMultiplier", ref seaEntitySpeedMultiplier);
		ReadFloatSetting(configNode, "eyeSpeedMultiplier", ref eyeSpeedMultiplier);
		ReadFloatSetting(configNode, "grabberSpeedMultiplier", ref grabberSpeedMultiplier);
		ReadFloatSetting(configNode, "escapeDistanceGainMetres", ref escapeDistanceGainMetres);
		ReadFloatSetting(configNode, "survivalEscapeSeconds", ref survivalEscapeSeconds);
		ReadFloatSetting(configNode, "bulkEncounterChance", ref bulkEncounterChance);
		ReadFloatSetting(configNode, "bulkEncounterCooldownSeconds", ref bulkEncounterCooldownSeconds);
		ReadFloatSetting(configNode, "bloopEncounterChance", ref bloopEncounterChance);
		ReadFloatSetting(configNode, "bloopEncounterCooldownSeconds", ref bloopEncounterCooldownSeconds);
		ReadFloatSetting(configNode, "rakeEncounterChance", ref rakeEncounterChance);
		ReadFloatSetting(configNode, "rakeEncounterCooldownSeconds", ref rakeEncounterCooldownSeconds);
		if (configNode != null && configNode.HasValue("survivalJolt"))
		{
			bool.TryParse(configNode.GetValue("survivalJolt"), out survivalJolt);
		}
		if (configNode != null && configNode.HasValue("immuneBodies"))
		{
			immuneBodies = configNode.GetValue("immuneBodies");
		}
		if (configNode != null && configNode.HasValue("highEncounterBodies"))
		{
			highEncounterBodies = configNode.GetValue("highEncounterBodies");
		}
		if (configNode != null && configNode.HasValue("rareEncounterBodies"))
		{
			rareEncounterBodies = configNode.GetValue("rareEncounterBodies");
		}
		LoadBehaviorSettings(configNode);
		LoadAttackSettings(configNode);
		LoadMotionSettings(configNode);
		LoadMohoSettings(configNode);
		LoadScareSettings(configNode);
		LoadDreadSettings(configNode);
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

	private float GetGroundEntitySpeed(float baseSpeed)
	{
		return baseSpeed * Mathf.Clamp(groundEntitySpeedMultiplier, 0.05f, 20f);
	}

	private float GetSeaEntitySpeed(float baseSpeed)
	{
		return baseSpeed * Mathf.Clamp(seaEntitySpeedMultiplier, 0.05f, 20f);
	}

	private void UpdateRandomEncounters()
	{
		if (!randomEncounters || tracking || arrived || Time.timeScale <= 0f)
		{
			return;
		}
		Vessel activeVessel = FlightGlobals.ActiveVessel;
		if (activeVessel == null || activeVessel.mainBody == null)
		{
			return;
		}
		float unscaledDeltaTime = Time.unscaledDeltaTime;
		encounterCooldownTimer = Mathf.Max(0f, encounterCooldownTimer - unscaledDeltaTime);
		groundEncounterCooldownTimer = Mathf.Max(0f, groundEncounterCooldownTimer - unscaledDeltaTime);
		bulkEncounterCooldownTimer = Mathf.Max(0f, bulkEncounterCooldownTimer - unscaledDeltaTime);
		laytheCreatureEncounterCooldownTimer = Mathf.Max(0f, laytheCreatureEncounterCooldownTimer - unscaledDeltaTime);
		seaEncounterCooldownTimer = Mathf.Max(0f, seaEncounterCooldownTimer - unscaledDeltaTime);
		bloopEncounterCooldownTimer = Mathf.Max(0f, bloopEncounterCooldownTimer - unscaledDeltaTime);
		rakeEncounterCooldownTimer = Mathf.Max(0f, rakeEncounterCooldownTimer - unscaledDeltaTime);
		grabberEncounterCooldownTimer = Mathf.Max(0f, grabberEncounterCooldownTimer - unscaledDeltaTime);
		dresSomethingEncounterCooldownTimer = Mathf.Max(0f, dresSomethingEncounterCooldownTimer - unscaledDeltaTime);
		encounterCheckTimer += GetCappedSpawnCheckDelta();
		if (encounterCheckTimer < randomCheckSeconds)
		{
			return;
		}
		encounterCheckTimer = 0f;
		string bodyName = activeVessel.mainBody.bodyName;
		float num = baseEncounterChance;
		if (BodyListContains(highEncounterBodies, bodyName))
		{
			num *= highEncounterMultiplier;
		}
		if (BodyListContains(rareEncounterBodies, bodyName))
		{
			num *= rareEncounterMultiplier;
		}
		if (!BodyListContains(immuneBodies, bodyName) && encounterCooldownTimer <= 0f && UnityEngine.Random.value <= Mathf.Clamp01(num))
		{
			encounterCooldownTimer = randomCooldownSeconds;
			StartTracking(activeVessel);
		}
		if (!tracking && !grabberEntityActive && grabberEncounterCooldownTimer <= 0f && IsEligibleForGrabberEncounter(activeVessel) && UnityEngine.Random.value <= Mathf.Clamp01(grabberEncounterChance))
		{
			SpawnGrabberEntity(activeVessel);
			if (grabberEntityActive)
			{
				grabberEncounterCooldownTimer = randomCooldownSeconds;
			}
		}
		if (!tracking && !groundEntityActive && groundEncounterCooldownTimer <= 0f && IsEligibleForAnyGroundVariant(activeVessel) && UnityEngine.Random.value <= Mathf.Clamp01(groundEncounterChance))
		{
			SpawnGroundEntity(PickGroundVariant(), naturalSpawn: true);
			if (groundEntityActive)
			{
				groundEncounterCooldownTimer = groundEncounterCooldownSeconds;
			}
		}
		if (!tracking && !groundEntityActive && !bulkEntityActive && bulkEncounterCooldownTimer <= 0f && IsEligibleForBulkEncounter(activeVessel) && UnityEngine.Random.value <= Mathf.Clamp01(bulkEncounterChance))
		{
			SpawnBulkEntity();
			if (bulkEntityActive)
			{
				bulkEncounterCooldownTimer = bulkEncounterCooldownSeconds;
			}
		}
		if (!tracking && !groundEntityActive && !bulkEntityActive && !laytheCreatureActive && laytheCreatureEncounterCooldownTimer <= 0f && IsEligibleForLaytheCreature(activeVessel) && UnityEngine.Random.value <= Mathf.Clamp01(laytheCreatureEncounterChance))
		{
			SpawnLaytheCreature();
			if (laytheCreatureActive)
			{
				laytheCreatureEncounterCooldownTimer = laytheCreatureEncounterCooldownSeconds;
			}
		}
		if (!tracking && !groundEntityActive && !bulkEntityActive && !seaEntityActive && seaEncounterCooldownTimer <= 0f && CanSeaEntityTarget(activeVessel) && UnityEngine.Random.value <= Mathf.Clamp01(seaEncounterChance))
		{
			SpawnSeaEntity();
			if (seaEntityActive)
			{
				seaEncounterCooldownTimer = seaEncounterCooldownSeconds;
			}
		}
		if (!tracking && !groundEntityActive && !bulkEntityActive && !seaEntityActive && !bloopEntityActive && bloopEncounterCooldownTimer <= 0f && CanBloopTarget(activeVessel) && UnityEngine.Random.value <= Mathf.Clamp01(bloopEncounterChance))
		{
			SpawnBloopEntity();
			if (bloopEntityActive)
			{
				bloopEncounterCooldownTimer = bloopEncounterCooldownSeconds;
			}
		}
		if (!tracking && !groundEntityActive && !bulkEntityActive && !seaEntityActive && !bloopEntityActive && !laytheCreatureActive && !dresSomethingActive && dresSomethingEncounterCooldownTimer <= 0f && IsEligibleForDresSomething(activeVessel) && UnityEngine.Random.value <= Mathf.Clamp01(dresSomethingEncounterChance))
		{
			SpawnDresSomething(activeVessel);
			if (dresSomethingActive)
			{
				dresSomethingEncounterCooldownTimer = dresSomethingEncounterCooldownSeconds;
			}
		}
	}

	private static bool IsEligibleForGroundEncounter(Vessel vessel)
	{
		if (vessel == null || vessel.mainBody == null || vessel.Splashed || vessel.heightFromTerrain >= 10f || !string.Equals(vessel.mainBody.bodyName, "Kerbin", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		if (IsBlockedGroundBiome(GetCurrentBiomeName(vessel)))
		{
			return false;
		}
		return IsNightAtVessel(vessel);
	}

	private static bool IsEligibleForAnyGroundVariant(Vessel vessel)
	{
		return vessel != null && vessel.mainBody != null && !vessel.Splashed && vessel.heightFromTerrain < 10f && string.Equals(vessel.mainBody.bodyName, "Kerbin", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsEligibleForBulkEncounter(Vessel vessel)
	{
		return vessel != null && vessel.mainBody != null && !vessel.Splashed && vessel.heightFromTerrain < 10f && IsBulkEntityBody(vessel.mainBody);
	}

	private static bool IsEligibleForLaytheCreature(Vessel vessel)
	{
		return vessel != null && vessel.mainBody != null && !vessel.Splashed && vessel.heightFromTerrain < 10f && string.Equals(vessel.mainBody.bodyName, "Laythe", StringComparison.OrdinalIgnoreCase);
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

	private void HandleArrival()
	{
		if (arrivalHandled)
		{
			return;
		}
		if (debugImmortality)
		{
			EndEncounter();
			return;
		}
		arrivalHandled = true;
		tracking = false;
		UpdateSound();
		if (quitOnArrival)
		{
			Application.Quit();
		}
		else
		{
			HighLogic.LoadScene(GameScenes.MAINMENU);
		}
	}

	private static void TryActivateTufxGlitch()
	{
		Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
		foreach (Assembly assembly in assemblies)
		{
			Type type = assembly.GetType("TUFX.TexturesUnlimitedFXLoader", throwOnError: false);
			if (!(type == null))
			{
				FieldInfo field = type.GetField("INSTANCE", BindingFlags.Static | BindingFlags.NonPublic);
				MethodInfo method = type.GetMethod("SetCurrentProfile", BindingFlags.Instance | BindingFlags.NonPublic);
				object obj = ((field != null) ? field.GetValue(null) : null);
				if (obj != null && method != null)
				{
					method.Invoke(obj, new object[1] { "EntityTracker-Glitch" });
					break;
				}
			}
		}
	}

	private void CreateWorldSoundSource()
	{
		entitySoundObject = new GameObject("EntityTracker_SoundSource");
		UnityEngine.Object.DontDestroyOnLoad(entitySoundObject);
		distantAudio = ConfigureAudioSource(entitySoundObject.AddComponent<AudioSource>());
		nearbyAudio = ConfigureAudioSource(entitySoundObject.AddComponent<AudioSource>());
		eyeWarningAudio = ConfigureAudioSource(entitySoundObject.AddComponent<AudioSource>());
		eyeWarningAudio.spatialBlend = 0f;
		eyeWarningAudio.loop = false;
		eyeWarningAudio.volume = 0.95f;
		eyeSpawnAudio = ConfigureAudioSource(entitySoundObject.AddComponent<AudioSource>());
		eyeSpawnAudio.spatialBlend = 0f;
		eyeSpawnAudio.loop = false;
		eyeSpawnAudio.volume = 0.9f;
		eyeChaseAudio = ConfigureAudioSource(entitySoundObject.AddComponent<AudioSource>());
		eyeChaseAudio.spatialBlend = 0f;
		eyeChaseAudio.loop = true;
		eyeChaseAudio.volume = 0.8f;
		randomHorrorAudio = ConfigureAudioSource(entitySoundObject.AddComponent<AudioSource>());
		randomHorrorAudio.spatialBlend = 0f;
		randomHorrorAudio.loop = false;
		randomHorrorAudio.volume = 0.95f;
		dresSomethingSoundObject = new GameObject("EntityTracker_DresSomethingAudio");
		UnityEngine.Object.DontDestroyOnLoad(dresSomethingSoundObject);
		dresSomethingOpenAudio = dresSomethingSoundObject.AddComponent<AudioSource>();
		dresSomethingOpenAudio.playOnAwake = false;
		dresSomethingOpenAudio.loop = false;
		dresSomethingOpenAudio.spatialBlend = 0f;
		dresSomethingOpenAudio.volume = 0.9f;
		dresSomethingAmbientAudio = dresSomethingSoundObject.AddComponent<AudioSource>();
		dresSomethingAmbientAudio.playOnAwake = false;
		dresSomethingAmbientAudio.loop = true;
		dresSomethingAmbientAudio.spatialBlend = 1f;
		dresSomethingAmbientAudio.rolloffMode = AudioRolloffMode.Linear;
		dresSomethingAmbientAudio.minDistance = 8f;
		dresSomethingAmbientAudio.maxDistance = 5000f;
		dresSomethingAmbientAudio.volume = 0.28f;
		ambientSoundObject = new GameObject("EntityTracker_KerbinAmbient");
		UnityEngine.Object.DontDestroyOnLoad(ambientSoundObject);
		kerbinAmbientAudio = ambientSoundObject.AddComponent<AudioSource>();
		kerbinAmbientAudio.playOnAwake = false;
		kerbinAmbientAudio.loop = false;
		kerbinAmbientAudio.spatialBlend = 1f;
		kerbinAmbientAudio.rolloffMode = AudioRolloffMode.Logarithmic;
		kerbinAmbientAudio.minDistance = 300f;
		kerbinAmbientAudio.maxDistance = 6000f;
		kerbinAmbientAudio.volume = 0.38f;
		laytheAmbientAudio = ambientSoundObject.AddComponent<AudioSource>();
		laytheAmbientAudio.playOnAwake = false;
		laytheAmbientAudio.loop = false;
		laytheAmbientAudio.spatialBlend = 1f;
		laytheAmbientAudio.rolloffMode = AudioRolloffMode.Logarithmic;
		laytheAmbientAudio.minDistance = 300f;
		laytheAmbientAudio.maxDistance = 6000f;
		laytheAmbientAudio.volume = 0.34f;
		eveAmbientAudio = ambientSoundObject.AddComponent<AudioSource>();
		eveAmbientAudio.playOnAwake = false;
		eveAmbientAudio.loop = false;
		eveAmbientAudio.spatialBlend = 1f;
		eveAmbientAudio.rolloffMode = AudioRolloffMode.Logarithmic;
		eveAmbientAudio.minDistance = 300f;
		eveAmbientAudio.maxDistance = 6000f;
		eveAmbientAudio.volume = 0.32f;
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

	private IEnumerator LoadGroundSound(string filename, AudioSource targetAudio, bool isFootsteps)
	{
		string soundPath = Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "Kraken's Kreations", "Sounds", filename);
		if (!File.Exists(soundPath) || targetAudio == null)
		{
			yield break;
		}
		WWW request = new WWW(new Uri(soundPath).AbsoluteUri);
		yield return request;
		if (string.IsNullOrEmpty(request.error) && !(targetAudio == null))
		{
			targetAudio.clip = request.GetAudioClip(threeD: true, stream: false, AudioType.MPEG);
			if (isFootsteps)
			{
				groundFootstepsLoaded = targetAudio.clip != null;
			}
			else
			{
				groundScreamLoaded = targetAudio.clip != null;
			}
			UpdateGroundEntitySound();
		}
	}

	private IEnumerator LoadGroundSpawnSound(string filename, AudioSource targetAudio)
	{
		string soundPath = Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "Kraken's Kreations", "Sounds", filename);
		if (!File.Exists(soundPath) || targetAudio == null)
		{
			yield break;
		}
		WWW request = new WWW(new Uri(soundPath).AbsoluteUri);
		yield return request;
		if (string.IsNullOrEmpty(request.error) && !(targetAudio == null))
		{
			targetAudio.clip = request.GetAudioClip(threeD: false, stream: false, AudioType.MPEG);
			if (targetAudio.clip != null)
			{
				targetAudio.Play();
			}
		}
	}

	private IEnumerator LoadRakeChaseSound()
	{
		string soundPath = Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "Kraken's Kreations", "Sounds", "entity-chase-sound-1-(loud-ver)-made-with-Voicemod.mp3");
		if (File.Exists(soundPath) && !(rakeChaseAudio == null))
		{
			WWW request = new WWW(new Uri(soundPath).AbsoluteUri);
			yield return request;
			if (string.IsNullOrEmpty(request.error) && !(rakeChaseAudio == null))
			{
				rakeChaseAudio.clip = request.GetAudioClip(threeD: false, stream: false, AudioType.MPEG);
				rakeChaseSoundLoaded = rakeChaseAudio.clip != null;
				UpdateRakeChaseSound();
			}
		}
	}

	private IEnumerator LoadBulkFootsteps()
	{
		string soundPath = Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "Kraken's Kreations", "Sounds", "big-heavy-robot-footsteps-sound-effect-made-with-Voicemod.mp3");
		if (File.Exists(soundPath) && !(bulkFootstepsAudio == null))
		{
			WWW request = new WWW(new Uri(soundPath).AbsoluteUri);
			yield return request;
			if (string.IsNullOrEmpty(request.error) && !(bulkFootstepsAudio == null))
			{
				bulkFootstepsAudio.clip = request.GetAudioClip(threeD: false, stream: false, AudioType.MPEG);
				bulkFootstepsLoaded = bulkFootstepsAudio.clip != null;
				UpdateBulkFootsteps(bulkEntityChasing && !bulkEntityStandingDown && !bulkEntityRetreating);
			}
		}
	}

	private void UpdateBulkFootsteps(bool chasing)
	{
		UpdateGroundAudioSource(bulkFootstepsAudio, bulkFootstepsLoaded, chasing && Time.timeScale > 0f);
	}

	private void PlayRakeChaseSoundIfReady()
	{
		if (rakeChaseSoundRequested && rakeChaseSoundLoaded && rakeChaseAudio != null && !rakeChaseAudio.isPlaying)
		{
			rakeChaseAudio.Play();
		}
	}

	private void UpdateRakeChaseSound()
	{
		bool flag = rakeEntityActive && rakeEntityChasing && !rakeEntityStandingDown && !rakeEntityRetreating && !rakeEntityVisible && rakeChaseSoundRequested && rakeChaseSoundLoaded && Time.timeScale > 0f;
		if (rakeChaseAudio == null)
		{
			return;
		}
		if (flag)
		{
			if (!rakeChaseAudio.isPlaying)
			{
				rakeChaseAudio.Play();
			}
		}
		else if (rakeChaseAudio.isPlaying)
		{
			rakeChaseAudio.Pause();
		}
	}

	private IEnumerator LoadSeaGrowl()
	{
		string soundPath = Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "Kraken's Kreations", "Sounds", "freesound_community-underwater-creature-growl-100438.mp3");
		if (File.Exists(soundPath) && !(seaGrowlAudio == null))
		{
			WWW request = new WWW(new Uri(soundPath).AbsoluteUri);
			yield return request;
			if (string.IsNullOrEmpty(request.error) && !(seaGrowlAudio == null))
			{
				seaGrowlAudio.clip = request.GetAudioClip(threeD: false, stream: false, AudioType.MPEG);
				seaGrowlLoaded = seaGrowlAudio.clip != null;
				UpdateSeaGrowl();
			}
		}
	}

	private void UpdateSeaGrowl()
	{
		if (seaGrowlAudio == null)
		{
			return;
		}
		Vessel activeVessel = FlightGlobals.ActiveVessel;
		if (activeVessel != null && seaEntityBody != null)
		{
			Vector3d vector3d = seaEntityBody.position + seaEntityRelativePosition;
			double magnitude = (vector3d - activeVessel.GetWorldPos3D()).magnitude;
			float t = Mathf.InverseLerp(1800f, 100f, (float)magnitude);
			seaGrowlAudio.volume = Mathf.SmoothStep(0.04f, 0.8f, t);
		}
		if (seaEntityActive && seaGrowlLoaded)
		{
			if (!seaGrowlAudio.isPlaying)
			{
				seaGrowlAudio.Play();
			}
		}
		else if (seaGrowlAudio.isPlaying)
		{
			seaGrowlAudio.Pause();
		}
	}

	private IEnumerator LoadBloopBurrowSound()
	{
		string soundPath = Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "Kraken's Kreations", "Sounds", "earth-leviathan-burrow-1-made-with-Voicemod.mp3");
		if (File.Exists(soundPath) && !(bloopBurrowAudio == null))
		{
			WWW request = new WWW(new Uri(soundPath).AbsoluteUri);
			yield return request;
			if (string.IsNullOrEmpty(request.error) && !(bloopBurrowAudio == null))
			{
				bloopBurrowAudio.clip = request.GetAudioClip(threeD: false, stream: false, AudioType.MPEG);
				bloopBurrowLoaded = bloopBurrowAudio.clip != null;
			}
		}
	}

	private void UpdateBloopBurrowSound(double distance)
	{
		if (bloopBurrowAudio == null || !bloopBurrowLoaded || !bloopEntityChasing || Time.timeScale <= 0f)
		{
			return;
		}
		if (distance <= 1000.0)
		{
			if (!bloopCloseCuePlayed)
			{
				bloopBurrowAudio.Play();
				bloopCloseCuePlayed = true;
			}
			return;
		}
		bloopCloseCuePlayed = false;
		bloopBurrowTimer -= TimeWarp.deltaTime;
		if (!(bloopBurrowTimer > 0f))
		{
			bloopBurrowTimer = UnityEngine.Random.Range(12f, 30f);
			if (!bloopBurrowAudio.isPlaying && UnityEngine.Random.value <= 0.35f)
			{
				bloopBurrowAudio.Play();
			}
		}
	}

	private IEnumerator LoadEntitySound(string filename, bool isDistantTrack, bool interplanetaryTrack)
	{
		string soundPath = Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "Kraken's Kreations", "Sounds", filename);
		if (!File.Exists(soundPath))
		{
			yield break;
		}
		WWW request = new WWW(new Uri(soundPath).AbsoluteUri);
		yield return request;
		AudioSource targetAudio = (isDistantTrack ? distantAudio : nearbyAudio);
		if (!string.IsNullOrEmpty(request.error) || targetAudio == null)
		{
			yield break;
		}
		AudioClip clip = request.GetAudioClip(threeD: true, stream: false, AudioType.MPEG);
		if (isDistantTrack)
		{
			if (interplanetaryTrack)
			{
				interplanetaryDistantClip = clip;
			}
			else
			{
				normalDistantClip = clip;
			}
			ApplyDistantSoundForEncounter();
		}
		else
		{
			if (interplanetaryTrack)
			{
				interplanetaryNearbyClip = clip;
			}
			else
			{
				normalNearbyClip = clip;
			}
			ApplyNearbySoundForEncounter();
		}
		UpdateSound();
	}

	private IEnumerator LoadEyeSound(string filename, bool isChaseSound)
	{
		string soundPath = Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "Kraken's Kreations", "Sounds", filename);
		AudioSource targetAudio = (isChaseSound ? eyeChaseAudio : eyeWarningAudio);
		if (!File.Exists(soundPath) || targetAudio == null)
		{
			yield break;
		}
		WWW request = new WWW(new Uri(soundPath).AbsoluteUri);
		yield return request;
		if (string.IsNullOrEmpty(request.error) && !(targetAudio == null))
		{
			targetAudio.clip = request.GetAudioClip(threeD: true, stream: false, AudioType.MPEG);
			if (isChaseSound)
			{
				eyeChaseSoundLoaded = targetAudio.clip != null;
			}
			else
			{
				eyeWarningSoundLoaded = targetAudio.clip != null;
			}
		}
	}

	private IEnumerator LoadEyeSpawnSound()
	{
		string soundPath = Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "Kraken's Kreations", "Sounds", "jool-eye-spawn.mp3");
		if (File.Exists(soundPath) && !(eyeSpawnAudio == null))
		{
			WWW request = new WWW(new Uri(soundPath).AbsoluteUri);
			yield return request;
			if (string.IsNullOrEmpty(request.error))
			{
				eyeSpawnAudio.clip = request.GetAudioClip(threeD: false, stream: false, AudioType.MPEG);
				eyeSpawnSoundLoaded = eyeSpawnAudio.clip != null;
			}
		}
	}

	private IEnumerator LoadRandomHorrorSound()
	{
		string path = Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "Kraken's Kreations", "Sounds", "williams-horror-made-with-Voicemod.mp3");
		if (File.Exists(path) && !(randomHorrorAudio == null))
		{
			WWW request = new WWW("file:///" + path.Replace("\\", "/"));
			yield return request;
			if (string.IsNullOrEmpty(request.error))
			{
				randomHorrorAudio.clip = request.GetAudioClip(threeD: false, stream: false, AudioType.MPEG);
				randomHorrorLoaded = randomHorrorAudio.clip != null;
			}
		}
	}

	private void UpdateRandomHorrorMusicEvent()
	{
		if (!HighLogic.LoadedSceneIsFlight || Time.timeScale <= 0f || !randomHorrorLoaded || randomHorrorAudio == null || DreadSuppressesRandomCue())
		{
			return;
		}
		randomHorrorCheckTimer += Time.unscaledDeltaTime;
		if (randomHorrorCheckTimer < 30f || randomHorrorAudio.isPlaying)
		{
			return;
		}
		randomHorrorCheckTimer = 0f;
		if (UnityEngine.Random.value > 0.025f)
		{
			return;
		}
		mutedMusicSources.Clear();
		AudioSource[] array = UnityEngine.Object.FindObjectsOfType<AudioSource>();
		foreach (AudioSource audioSource in array)
		{
			if (!(audioSource == randomHorrorAudio) && !(audioSource.gameObject == entitySoundObject))
			{
				string text = audioSource.gameObject.name.ToLowerInvariant();
				if (text.Contains("music"))
				{
					mutedMusicSources[audioSource] = audioSource.volume;
					audioSource.volume = 0f;
				}
			}
		}
		randomHorrorAudio.Play();
		StartCoroutine(RestoreMusicAfterHorror(randomHorrorAudio.clip.length));
	}

	private IEnumerator RestoreMusicAfterHorror(float seconds)
	{
		yield return new WaitForSecondsRealtime(seconds + 0.1f);
		foreach (KeyValuePair<AudioSource, float> mutedMusicSource in mutedMusicSources)
		{
			if (mutedMusicSource.Key != null)
			{
				mutedMusicSource.Key.volume = mutedMusicSource.Value;
			}
		}
		mutedMusicSources.Clear();
	}

	private IEnumerator LoadKerbinAmbientSound(string filename)
	{
		string path = Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "Kraken's Kreations", "Sounds", filename);
		if (!File.Exists(path) || kerbinAmbientAudio == null)
		{
			yield break;
		}
		WWW request = new WWW("file:///" + path.Replace("\\", "/"));
		yield return request;
		if (string.IsNullOrEmpty(request.error))
		{
			AudioClip audioClip = request.GetAudioClip(threeD: false, stream: false, AudioType.MPEG);
			if (audioClip != null)
			{
				kerbinAmbientClips.Add(audioClip);
				kerbinAmbientLoaded = true;
			}
		}
	}

	private void SelectKerbinAmbientClip()
	{
		if (!(kerbinAmbientAudio == null) && kerbinAmbientClips.Count != 0)
		{
			kerbinAmbientAudio.clip = kerbinAmbientClips[UnityEngine.Random.Range(0, kerbinAmbientClips.Count)];
		}
	}

	private void UpdateKerbinNightAmbience()
	{
		if (!HighLogic.LoadedSceneIsFlight || Time.timeScale <= 0f || !kerbinAmbientLoaded || kerbinAmbientAudio == null || kerbinAmbientAudio.isPlaying)
		{
			return;
		}
		Vessel activeVessel = FlightGlobals.ActiveVessel;
		if (activeVessel == null || activeVessel.mainBody == null || !string.Equals(activeVessel.mainBody.bodyName, "Kerbin", StringComparison.OrdinalIgnoreCase) || !IsNightAtVessel(activeVessel))
		{
			return;
		}
		kerbinAmbientCheckTimer += Time.unscaledDeltaTime;
		if (kerbinAmbientCheckTimer < 45f)
		{
			return;
		}
		kerbinAmbientCheckTimer = 0f;
		if (!(UnityEngine.Random.value > 0.06f))
		{
			Vector3d normalized = (activeVessel.GetWorldPos3D() - activeVessel.mainBody.position).normalized;
			Vector3 vector = activeVessel.transform.forward - (Vector3)normalized * Vector3.Dot(activeVessel.transform.forward, normalized);
			if (vector.sqrMagnitude < 0.01f)
			{
				vector = activeVessel.transform.right;
			}
			float num = UnityEngine.Random.Range(2200f, 4200f);
			ambientSoundObject.transform.position = activeVessel.GetWorldPos3D() + (Vector3d)vector.normalized * (double)num;
			SelectKerbinAmbientClip();
			kerbinAmbientAudio.Play();
		}
	}

	private void UpdateAmbientPauseState()
	{
		bool paused = Time.timeScale <= 0f;
		PauseOrResumeAmbient(kerbinAmbientAudio, paused, ref kerbinAmbientPausedByGame);
		PauseOrResumeAmbient(laytheAmbientAudio, paused, ref laytheAmbientPausedByGame);
		PauseOrResumeAmbient(eveAmbientAudio, paused, ref eveAmbientPausedByGame);
	}

	private static void PauseOrResumeAmbient(AudioSource source, bool paused, ref bool pausedByGame)
	{
		if (source == null)
		{
			return;
		}
		if (paused)
		{
			if (source.isPlaying)
			{
				source.Pause();
				pausedByGame = true;
			}
		}
		else if (pausedByGame)
		{
			source.UnPause();
			pausedByGame = false;
		}
	}

	private void ForceLocalAmbientSound()
	{
		Vessel activeVessel = FlightGlobals.ActiveVessel;
		if (activeVessel == null || activeVessel.mainBody == null || ambientSoundObject == null)
		{
			return;
		}
		AudioSource audioSource = null;
		string a = activeVessel.mainBody.bodyName ?? string.Empty;
		if (string.Equals(a, "Kerbin", StringComparison.OrdinalIgnoreCase) && kerbinAmbientLoaded)
		{
			audioSource = kerbinAmbientAudio;
		}
		else if (string.Equals(a, "Laythe", StringComparison.OrdinalIgnoreCase) && laytheAmbientLoaded)
		{
			audioSource = laytheAmbientAudio;
		}
		else if (string.Equals(a, "Eve", StringComparison.OrdinalIgnoreCase) && eveAmbientLoaded)
		{
			audioSource = eveAmbientAudio;
		}
		if (!(audioSource == null))
		{
			Vector3d normalized = (activeVessel.GetWorldPos3D() - activeVessel.mainBody.position).normalized;
			Vector3 vector = activeVessel.transform.forward - (Vector3)normalized * Vector3.Dot(activeVessel.transform.forward, normalized);
			if (vector.sqrMagnitude < 0.01f)
			{
				vector = activeVessel.transform.right;
			}
			ambientSoundObject.transform.position = activeVessel.GetWorldPos3D() + (Vector3d)vector.normalized * 1500.0;
			if (audioSource == kerbinAmbientAudio)
			{
				SelectKerbinAmbientClip();
			}
			audioSource.Stop();
			audioSource.Play();
		}
	}

	private IEnumerator LoadLaytheAmbientSound()
	{
		string path = Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "Kraken's Kreations", "Sounds", "alesiadavina-horror-sound-lurking-horror-monster-189948.mp3");
		if (File.Exists(path) && !(laytheAmbientAudio == null))
		{
			WWW request = new WWW("file:///" + path.Replace("\\", "/"));
			yield return request;
			if (string.IsNullOrEmpty(request.error))
			{
				laytheAmbientAudio.clip = request.GetAudioClip(threeD: false, stream: false, AudioType.MPEG);
				laytheAmbientLoaded = laytheAmbientAudio.clip != null;
			}
		}
	}

	private void UpdateLaytheNightAmbience()
	{
		if (!HighLogic.LoadedSceneIsFlight || Time.timeScale <= 0f || !laytheAmbientLoaded || laytheAmbientAudio == null || laytheAmbientAudio.isPlaying)
		{
			return;
		}
		Vessel activeVessel = FlightGlobals.ActiveVessel;
		if (activeVessel == null || activeVessel.mainBody == null || !string.Equals(activeVessel.mainBody.bodyName, "Laythe", StringComparison.OrdinalIgnoreCase) || !IsNightAtVessel(activeVessel))
		{
			return;
		}
		laytheAmbientCheckTimer += Time.unscaledDeltaTime;
		if (laytheAmbientCheckTimer < 45f)
		{
			return;
		}
		laytheAmbientCheckTimer = 0f;
		if (!(UnityEngine.Random.value > 0.06f))
		{
			Vector3d normalized = (activeVessel.GetWorldPos3D() - activeVessel.mainBody.position).normalized;
			Vector3 vector = activeVessel.transform.forward - (Vector3)normalized * Vector3.Dot(activeVessel.transform.forward, normalized);
			if (vector.sqrMagnitude < 0.01f)
			{
				vector = activeVessel.transform.right;
			}
			ambientSoundObject.transform.position = activeVessel.GetWorldPos3D() + (Vector3d)vector.normalized * (double)UnityEngine.Random.Range(2200f, 4200f);
			laytheAmbientAudio.Play();
		}
	}

	private IEnumerator LoadEveAmbientSound()
	{
		string path = Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "Kraken's Kreations", "Sounds", "dragon-studio-cemetery-wind-467488.mp3");
		if (File.Exists(path) && !(eveAmbientAudio == null))
		{
			WWW request = new WWW("file:///" + path.Replace("\\", "/"));
			yield return request;
			if (string.IsNullOrEmpty(request.error))
			{
				eveAmbientAudio.clip = request.GetAudioClip(threeD: false, stream: false, AudioType.MPEG);
				eveAmbientLoaded = eveAmbientAudio.clip != null;
			}
		}
	}

	private void UpdateEveNightAmbience()
	{
		if (!HighLogic.LoadedSceneIsFlight || Time.timeScale <= 0f || !eveAmbientLoaded || eveAmbientAudio == null || eveAmbientAudio.isPlaying)
		{
			return;
		}
		Vessel activeVessel = FlightGlobals.ActiveVessel;
		if (activeVessel == null || activeVessel.mainBody == null || !string.Equals(activeVessel.mainBody.bodyName, "Eve", StringComparison.OrdinalIgnoreCase) || !IsNightAtVessel(activeVessel))
		{
			return;
		}
		eveAmbientCheckTimer += Time.unscaledDeltaTime;
		if (eveAmbientCheckTimer < 45f)
		{
			return;
		}
		eveAmbientCheckTimer = 0f;
		if (!(UnityEngine.Random.value > 0.06f))
		{
			Vector3d normalized = (activeVessel.GetWorldPos3D() - activeVessel.mainBody.position).normalized;
			Vector3 vector = activeVessel.transform.forward - (Vector3)normalized * Vector3.Dot(activeVessel.transform.forward, normalized);
			if (vector.sqrMagnitude < 0.01f)
			{
				vector = activeVessel.transform.right;
			}
			ambientSoundObject.transform.position = activeVessel.GetWorldPos3D() + (Vector3d)vector.normalized * (double)UnityEngine.Random.Range(2200f, 4200f);
			eveAmbientAudio.Play();
		}
	}

	private void ApplyDistantSoundForEncounter()
	{
		if (distantAudio == null)
		{
			return;
		}
		AudioClip audioClip = (IsInterplanetaryEncounter() ? (interplanetaryDistantClip ?? normalDistantClip) : normalDistantClip);
		if (audioClip != null && distantAudio.clip != audioClip)
		{
			bool isPlaying = distantAudio.isPlaying;
			distantAudio.Stop();
			distantAudio.clip = audioClip;
			if (isPlaying && Time.timeScale > 0f)
			{
				distantAudio.Play();
			}
		}
		distantSoundLoaded = distantAudio.clip != null;
	}

	private void ApplyNearbySoundForEncounter()
	{
		if (nearbyAudio == null)
		{
			return;
		}
		AudioClip audioClip = (IsInterplanetaryEncounter() ? (interplanetaryNearbyClip ?? normalNearbyClip) : normalNearbyClip);
		if (audioClip != null && nearbyAudio.clip != audioClip)
		{
			bool isPlaying = nearbyAudio.isPlaying;
			nearbyAudio.Stop();
			nearbyAudio.clip = audioClip;
			if (isPlaying && Time.timeScale > 0f)
			{
				nearbyAudio.Play();
			}
		}
		nearbySoundLoaded = nearbyAudio.clip != null;
	}

	private void UpdateDresSomethingAudio(Vector3d position)
	{
		if (!(dresSomethingSoundObject == null) && !(dresSomethingAmbientAudio == null))
		{
			dresSomethingSoundObject.transform.position = position;
			bool flag = Time.timeScale <= 0f;
			PauseOrResumeAmbient(dresSomethingAmbientAudio, flag, ref dresSomethingAmbientPausedByGame);
			if (!flag && dresSomethingAmbientSoundLoaded && !dresSomethingAmbientAudio.isPlaying && !dresSomethingAmbientPausedByGame)
			{
				dresSomethingAmbientAudio.Play();
			}
		}
	}

	private IEnumerator LoadDresSomethingSound(string filename, AudioSource targetAudio, bool ambient)
	{
		string soundPath = Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "Kraken's Kreations", "Sounds", filename);
		if (!File.Exists(soundPath) || targetAudio == null)
		{
			yield break;
		}
		WWW request = new WWW(new Uri(soundPath).AbsoluteUri);
		yield return request;
		if (string.IsNullOrEmpty(request.error) && !(targetAudio == null))
		{
			targetAudio.clip = request.GetAudioClip(ambient, stream: false, ambient ? AudioType.WAV : AudioType.MPEG);
			if (ambient)
			{
				dresSomethingAmbientSoundLoaded = targetAudio.clip != null;
			}
			else
			{
				dresSomethingOpenSoundLoaded = targetAudio.clip != null;
			}
		}
	}

	private IEnumerator LoadDresSomethingVisual(string filename, int frame, bool opening = false, bool holding = false)
	{
		string imagePath = Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "Kraken's Kreations", "Visuals", filename);
		if (!File.Exists(imagePath))
		{
			yield break;
		}
		WWW request = new WWW(new Uri(imagePath).AbsoluteUri);
		yield return request;
		if (string.IsNullOrEmpty(request.error) && request.texture != null)
		{
			if (opening)
			{
				dresSomethingOpenFrames[frame] = request.texture;
			}
			else if (holding)
			{
				dresSomethingHoldFrames[frame] = request.texture;
			}
			else
			{
				dresSomethingFrames[frame] = request.texture;
			}
		}
	}

	private Texture2D GetDresSomethingTexture()
	{
		if (dresSomethingOpening)
		{
			return dresSomethingOpenFrames[Mathf.Min(2, Mathf.FloorToInt(dresSomethingOpeningTimer / 0.15f))];
		}
		if (dresSomethingDropping)
		{
			return dresSomethingHoldFrames[Mathf.FloorToInt(dresSomethingFrameTimer / 0.16f) % dresSomethingHoldFrames.Length];
		}
		return dresSomethingFrames[Mathf.FloorToInt(dresSomethingFrameTimer / 0.14f) % dresSomethingFrames.Length];
	}

	private void UpdateDresSomethingBillboard()
	{
		if (!dresSomethingActive || dresSomethingBody == null)
		{
			return;
		}
		Texture2D dresSomethingTexture = GetDresSomethingTexture();
		if (dresSomethingTexture == null)
		{
			return;
		}
		if (dresSomethingBillboardObject == null)
		{
			dresSomethingBillboardObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
			dresSomethingBillboardObject.name = "EntityTracker_DresSomething";
			Collider component = dresSomethingBillboardObject.GetComponent<Collider>();
			if (component != null)
			{
				UnityEngine.Object.Destroy(component);
			}
			Shader shader = Shader.Find("Unlit/Transparent") ?? Shader.Find("Legacy Shaders/Transparent/Diffuse");
			if (shader == null)
			{
				UnityEngine.Object.Destroy(dresSomethingBillboardObject);
				dresSomethingBillboardObject = null;
				return;
			}
			dresSomethingBillboardMaterial = new Material(shader);
			Renderer component2 = dresSomethingBillboardObject.GetComponent<Renderer>();
			component2.material = dresSomethingBillboardMaterial;
			component2.shadowCastingMode = ShadowCastingMode.Off;
			component2.receiveShadows = false;
		}
		Camera camera = ((FlightCamera.fetch != null) ? FlightCamera.fetch.mainCamera : Camera.main);
		if (!(camera == null) && !(dresSomethingBillboardMaterial == null))
		{
			dresSomethingBillboardMaterial.mainTexture = dresSomethingTexture;
			float a = (dresSomethingDropping ? Mathf.Clamp01(dresSomethingFadeTimer / 7f) : 1f);
			dresSomethingBillboardMaterial.color = new Color(1f, 1f, 1f, a);
			Vector3d vector3d = dresSomethingBody.position + dresSomethingRelativePosition;
			float unscaledTime = Time.unscaledTime;
			Vector3 vector = camera.transform.right * Mathf.Sin(unscaledTime * 2.1f) * 0.06f + camera.transform.up * Mathf.Cos(unscaledTime * 2.7f) * 0.04f;
			dresSomethingBillboardObject.transform.position = (Vector3)vector3d + vector;
			Vector3 vector2 = camera.transform.position - dresSomethingBillboardObject.transform.position;
			if (vector2.sqrMagnitude > 0.001f)
			{
				dresSomethingBillboardObject.transform.rotation = Quaternion.LookRotation(vector2.normalized, camera.transform.up) * Quaternion.Euler(0f, 0f, Mathf.Sin(unscaledTime * 3.5f) * 1.2f);
			}
			dresSomethingBillboardObject.transform.localScale = new Vector3(10f, 10f, 1f);
		}
	}

	private IEnumerator LoadVisual()
	{
		string imagePath = Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "Kraken's Kreations", "Visuals", "black-box.png");
		if (File.Exists(imagePath))
		{
			WWW request = new WWW(new Uri(imagePath).AbsoluteUri);
			yield return request;
			if (string.IsNullOrEmpty(request.error))
			{
				visualTexture = request.texture;
			}
		}
	}

	private IEnumerator LoadInterplanetaryVisual()
	{
		string imagePath = Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "Kraken's Kreations", "Visuals", "red-box.png");
		if (File.Exists(imagePath))
		{
			WWW request = new WWW(new Uri(imagePath).AbsoluteUri);
			yield return request;
			if (string.IsNullOrEmpty(request.error))
			{
				interplanetaryVisualTexture = request.texture;
			}
		}
	}

	private IEnumerator LoadEyeVisual(string filename, bool blink)
	{
		string imagePath = Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "Kraken's Kreations", "Visuals", filename);
		if (!File.Exists(imagePath))
		{
			yield break;
		}
		WWW request = new WWW(new Uri(imagePath).AbsoluteUri);
		yield return request;
		if (string.IsNullOrEmpty(request.error) && !(request.texture == null))
		{
			Texture2D texture = request.texture;
			MakeNearWhiteTransparent(texture);
			if (blink)
			{
				eyeBlinkTexture = texture;
			}
			else
			{
				eyeTexture = texture;
			}
		}
	}

	private IEnumerator LoadEyeWarningVisual()
	{
		string imagePath = Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "Kraken's Kreations", "Visuals", "dont-go-there-big-dawg.png");
		if (File.Exists(imagePath))
		{
			WWW request = new WWW(new Uri(imagePath).AbsoluteUri);
			yield return request;
			if (string.IsNullOrEmpty(request.error) && !(request.texture == null))
			{
				eyeWarningTexture = request.texture;
			}
		}
	}

	private IEnumerator LoadGrabberVisual(string filename, bool grabbing)
	{
		string imagePath = Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "Kraken's Kreations", "Visuals", filename);
		if (!File.Exists(imagePath))
		{
			yield break;
		}
		WWW request = new WWW(new Uri(imagePath).AbsoluteUri);
		yield return request;
		if (string.IsNullOrEmpty(request.error) && !(request.texture == null))
		{
			Texture2D texture = request.texture;
			if (grabbing)
			{
				grabberGrabTexture = texture;
			}
			else
			{
				grabberIdleTexture = texture;
			}
		}
	}

	private static void MakeNearWhiteTransparent(Texture2D texture)
	{
		Color[] pixels = texture.GetPixels();
		for (int i = 0; i < pixels.Length; i++)
		{
			Color color = pixels[i];
			if (color.r > 0.92f && color.g > 0.92f && color.b > 0.92f)
			{
				color.a = 0f;
			}
			pixels[i] = color;
		}
		texture.SetPixels(pixels);
		texture.Apply();
	}

	private void UpdateSound()
	{
		if (distantAudio == null || nearbyAudio == null)
		{
			return;
		}
		if (!tracking || FlightGlobals.ActiveVessel == null || Time.timeScale <= 0f)
		{
			PauseAudio(distantAudio);
			PauseAudio(nearbyAudio);
			return;
		}
		entitySoundObject.transform.position = entityPosition;
		double magnitude = (entityPosition - FlightGlobals.ActiveVessel.GetWorldPos3D()).magnitude;
		float value = Mathf.Clamp01(1f - (float)(magnitude / (double)currentSpawnDistanceMetres));
		float num = Mathf.SmoothStep(0f, 0.7f, Mathf.InverseLerp(0f, 0.6f, value));
		if (IsInterplanetaryEncounter())
		{
			distantAudio.spatialBlend = 0f;
			nearbyAudio.spatialBlend = 0f;
			float t = Mathf.InverseLerp(50000f, 0f, (float)magnitude);
			distantAudio.volume = num * (1f - Mathf.SmoothStep(0f, 1f, t));
			nearbyAudio.volume = Mathf.SmoothStep(0f, 0.95f, t);
		}
		else
		{
			distantAudio.spatialBlend = 1f;
			nearbyAudio.spatialBlend = 1f;
			distantAudio.volume = num * (1f - Mathf.SmoothStep(0f, 0.75f, Mathf.InverseLerp(0.55f, 1f, value)));
			nearbyAudio.volume = Mathf.SmoothStep(0f, 0.95f, Mathf.InverseLerp(0.35f, 1f, value));
		}
		PlayIfLoaded(distantAudio, distantSoundLoaded);
		PlayIfLoaded(nearbyAudio, nearbySoundLoaded);
	}

	private void UpdateGroundEntitySound()
	{
		bool shouldPlay = groundEntityActive && groundEntityChasing && !groundEntityStandingDown && !groundEntityRetreating && Time.timeScale > 0f;
		UpdateGroundAudioSource(groundFootstepsAudio, groundFootstepsLoaded, shouldPlay);
		UpdateGroundAudioSource(groundScreamAudio, groundScreamLoaded, DreadAllowScream(DreadScreenGateKind.Ground, shouldPlay));
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

	private void TriggerWindowJolt()
	{
		if (survivalJolt && !Screen.fullScreen)
		{
			IntPtr mainWindowHandle = Process.GetCurrentProcess().MainWindowHandle;
			if (!(mainWindowHandle == IntPtr.Zero) && GetWindowRect(mainWindowHandle, out originalWindowRect))
			{
				hasOriginalWindowRect = true;
				joltFramesRemaining = 8;
			}
		}
	}

	private void UpdateWindowJolt()
	{
		if (joltFramesRemaining <= 0)
		{
			return;
		}
		IntPtr mainWindowHandle = Process.GetCurrentProcess().MainWindowHandle;
		if (mainWindowHandle == IntPtr.Zero)
		{
			joltFramesRemaining = 0;
			return;
		}
		joltFramesRemaining--;
		if (joltFramesRemaining == 0)
		{
			RestoreWindowPosition();
			return;
		}
		int x = originalWindowRect.Left + UnityEngine.Random.Range(-32, 33);
		int y = originalWindowRect.Top + UnityEngine.Random.Range(-32, 33);
		SetWindowPos(mainWindowHandle, IntPtr.Zero, x, y, 0, 0, 21u);
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

	private void RestoreWindowPosition()
	{
		if (hasOriginalWindowRect)
		{
			IntPtr mainWindowHandle = Process.GetCurrentProcess().MainWindowHandle;
			if (mainWindowHandle != IntPtr.Zero)
			{
				SetWindowPos(mainWindowHandle, IntPtr.Zero, originalWindowRect.Left, originalWindowRect.Top, 0, 0, 21u);
			}
			hasOriginalWindowRect = false;
			joltFramesRemaining = 0;
		}
	}

	[DllImport("user32.dll")]
	private static extern bool GetWindowRect(IntPtr handle, out WindowRect rect);

	[DllImport("user32.dll")]
	private static extern bool SetWindowPos(IntPtr handle, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

	private void DrawEyeOverlay()
	{
		if (!eyeEntityActive || eyeTexture == null || FlightGlobals.ActiveVessel == null)
		{
			return;
		}
		Camera camera = ((FlightCamera.fetch != null) ? FlightCamera.fetch.mainCamera : Camera.main);
		if (camera == null)
		{
			return;
		}
		Vector3 vector = camera.WorldToScreenPoint(eyeEntityPosition);
		if (!(vector.z <= 0f))
		{
			float num = (float)Screen.height / (2f * Mathf.Tan(camera.fieldOfView * ((float)Math.PI / 180f) * 0.5f));
			float a = 6f * num / Mathf.Max(0.01f, vector.z);
			a = Mathf.Min(a, 900f);
			if (!(vector.x < (0f - a) * 0.5f) && !(vector.x > (float)Screen.width + a * 0.5f) && !(vector.y < (0f - a) * 0.5f) && !(vector.y > (float)Screen.height + a * 0.5f))
			{
				Texture2D image = ((Mathf.Repeat(Time.realtimeSinceStartup, 3f) < 0.3f && eyeBlinkTexture != null) ? eyeBlinkTexture : eyeTexture);
				GUI.DrawTexture(new Rect(vector.x - a * 0.5f, (float)Screen.height - vector.y - a * 0.5f, a, a), image, ScaleMode.ScaleToFit, alphaBlend: true);
			}
		}
	}

	private void DrawGrabberOverlay()
	{
		if (!grabberEntityActive || grabberIdleTexture == null || FlightGlobals.ActiveVessel == null)
		{
			return;
		}
		Camera camera = ((FlightCamera.fetch != null) ? FlightCamera.fetch.mainCamera : Camera.main);
		if (camera == null)
		{
			return;
		}
		Vector3 vector = camera.WorldToScreenPoint(grabberVisualPosition);
		if (!(vector.z <= 0f))
		{
			float num = (float)Screen.height / (2f * Mathf.Tan(camera.fieldOfView * ((float)Math.PI / 180f) * 0.5f));
			float a = 12f * num / Mathf.Max(0.01f, vector.z);
			a = Mathf.Min(a, 1100f);
			if (!(vector.x < (0f - a) * 0.5f) && !(vector.x > (float)Screen.width + a * 0.5f) && !(vector.y < (0f - a) * 0.5f) && !(vector.y > (float)Screen.height + a * 0.5f))
			{
				Texture2D image = ((grabberEntityGrabbing && grabberGrabTexture != null) ? grabberGrabTexture : grabberIdleTexture);
				GUI.DrawTexture(new Rect(vector.x - a * 0.5f, (float)Screen.height - vector.y - a * 0.5f, a, a), image, ScaleMode.ScaleToFit, alphaBlend: true);
			}
		}
	}

	private void DrawDresSomethingOverlay()
	{
		if (!dresSomethingActive || dresSomethingBody == null)
		{
			return;
		}
		Texture2D dresSomethingTexture = GetDresSomethingTexture();
		if (dresSomethingTexture == null)
		{
			return;
		}
		Camera camera = ((FlightCamera.fetch != null) ? FlightCamera.fetch.mainCamera : Camera.main);
		if (camera == null)
		{
			return;
		}
		Vector3d vector3d = dresSomethingBody.position + dresSomethingRelativePosition;
		Vector3 vector = camera.WorldToScreenPoint(vector3d);
		if (!(vector.z <= 0f))
		{
			float num = (float)Screen.height / (2f * Mathf.Tan(camera.fieldOfView * ((float)Math.PI / 180f) * 0.5f));
			float a = 10f * num / Mathf.Max(0.01f, vector.z);
			a = Mathf.Min(a, 900f);
			float num2 = a * (float)dresSomethingTexture.height / Mathf.Max(1f, dresSomethingTexture.width);
			if (!(vector.x < (0f - a) * 0.5f) && !(vector.x > (float)Screen.width + a * 0.5f) && !(vector.y < (0f - num2) * 0.5f) && !(vector.y > (float)Screen.height + num2 * 0.5f))
			{
				Vector2 zero = Vector2.zero;
				float a2 = (dresSomethingDropping ? Mathf.Clamp01(dresSomethingFadeTimer / 7f) : 1f);
				Color color = GUI.color;
				GUI.color = new Color(1f, 1f, 1f, a2);
				GUI.DrawTexture(new Rect(vector.x - a * 0.5f + zero.x, (float)Screen.height - vector.y - num2 * 0.5f + zero.y, a, num2), dresSomethingTexture, ScaleMode.ScaleToFit, alphaBlend: true);
				GUI.color = color;
			}
		}
	}

	private void UpdateEyeBillboard()
	{
		if (!eyeEntityActive || eyeTexture == null)
		{
			return;
		}
		if (eyeBillboardObject == null)
		{
			eyeBillboardObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
			eyeBillboardObject.name = "EntityTracker_JoolEye";
			Collider component = eyeBillboardObject.GetComponent<Collider>();
			if (component != null)
			{
				UnityEngine.Object.Destroy(component);
			}
			Shader shader = Shader.Find("Unlit/Transparent");
			if (shader == null)
			{
				shader = Shader.Find("Legacy Shaders/Transparent/Diffuse");
			}
			if (shader == null)
			{
				UnityEngine.Object.Destroy(eyeBillboardObject);
				eyeBillboardObject = null;
				return;
			}
			eyeBillboardMaterial = new Material(shader);
			Renderer component2 = eyeBillboardObject.GetComponent<Renderer>();
			component2.material = eyeBillboardMaterial;
			component2.shadowCastingMode = ShadowCastingMode.Off;
			component2.receiveShadows = false;
			component2.enabled = false;
		}
		Camera camera = ((FlightCamera.fetch != null) ? FlightCamera.fetch.mainCamera : Camera.main);
		if (!(camera == null) && !(eyeBillboardMaterial == null))
		{
			bool flag = Mathf.Repeat(Time.realtimeSinceStartup, 3f) < 0.3f;
			eyeBillboardMaterial.mainTexture = ((flag && eyeBlinkTexture != null) ? eyeBlinkTexture : eyeTexture);
			eyeBillboardObject.transform.position = eyeEntityPosition;
			Vector3 vector = camera.transform.position - eyeBillboardObject.transform.position;
			if (vector.sqrMagnitude > 0.001f)
			{
				eyeBillboardObject.transform.rotation = Quaternion.LookRotation(vector.normalized, camera.transform.up);
			}
			eyeBillboardObject.transform.localScale = new Vector3(6f, 3.6757345f, 1f);
		}
	}

	private void UpdateGrabberBillboard()
	{
		if (!grabberEntityActive || grabberIdleTexture == null)
		{
			return;
		}
		if (grabberBillboardObject == null)
		{
			grabberBillboardObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
			grabberBillboardObject.name = "EntityTracker_Grabber";
			Collider component = grabberBillboardObject.GetComponent<Collider>();
			if (component != null)
			{
				UnityEngine.Object.Destroy(component);
			}
			Shader shader = Shader.Find("Unlit/Transparent");
			if (shader == null)
			{
				shader = Shader.Find("Legacy Shaders/Transparent/Diffuse");
			}
			if (shader == null)
			{
				UnityEngine.Object.Destroy(grabberBillboardObject);
				grabberBillboardObject = null;
				return;
			}
			grabberBillboardMaterial = new Material(shader);
			Renderer component2 = grabberBillboardObject.GetComponent<Renderer>();
			component2.material = grabberBillboardMaterial;
			component2.shadowCastingMode = ShadowCastingMode.Off;
			component2.receiveShadows = false;
			component2.enabled = false;
		}
		Camera camera = ((FlightCamera.fetch != null) ? FlightCamera.fetch.mainCamera : Camera.main);
		if (!(camera == null) && !(grabberBillboardMaterial == null))
		{
			grabberBillboardMaterial.mainTexture = ((grabberEntityGrabbing && grabberGrabTexture != null) ? grabberGrabTexture : grabberIdleTexture);
			grabberBillboardObject.transform.position = grabberEntityPosition;
			Vector3 vector = camera.transform.position - grabberBillboardObject.transform.position;
			if (vector.sqrMagnitude > 0.001f)
			{
				grabberBillboardObject.transform.rotation = Quaternion.LookRotation(vector.normalized, camera.transform.up);
			}
			grabberBillboardObject.transform.localScale = new Vector3(12f, 6.7605634f, 1f);
		}
	}

	private void DrawEyeWarning()
	{
		if (!(eyeWarningTextTimer <= 0f))
		{
			float a = Mathf.Clamp01(eyeWarningTextTimer / 10f);
			if (eyeWarningTexture == null)
			{
				GUIStyle gUIStyle = new GUIStyle(GUI.skin.label);
				gUIStyle.fontSize = Mathf.RoundToInt((float)Mathf.Min(Screen.width, Screen.height) * 0.055f);
				gUIStyle.fontStyle = FontStyle.Bold;
				gUIStyle.alignment = TextAnchor.MiddleCenter;
				gUIStyle.normal.textColor = new Color(1f, 0.08f, 0.08f, a);
				GUI.Label(new Rect(0f, (float)Screen.height * 0.62f, Screen.width, (float)gUIStyle.fontSize * 1.7f), "DON'T GO TO BOP", gUIStyle);
			}
			else
			{
				float num = Mathf.Min((float)Screen.width * 0.92f, 1020f);
				float height = num * (float)eyeWarningTexture.height / (float)eyeWarningTexture.width;
				float num2 = Time.unscaledTime * 17f;
				float num3 = (Mathf.PerlinNoise(num2, 0.13f) - 0.5f) * 5f;
				float num4 = (Mathf.PerlinNoise(0.57f, num2) - 0.5f) * 4f;
				Rect position = new Rect(((float)Screen.width - num) * 0.5f + num3, (float)Screen.height * 0.62f + num4, num, height);
				Color color = GUI.color;
				GUI.color = new Color(1f, 1f, 1f, a);
				GUI.DrawTexture(position, eyeWarningTexture, ScaleMode.ScaleToFit, alphaBlend: true);
				GUI.color = color;
			}
		}
	}

	private void DrawVisualOverlay()
	{
		if (visualTexture == null || FlightGlobals.ActiveVessel == null)
		{
			return;
		}
		Camera camera = ((FlightCamera.fetch != null) ? FlightCamera.fetch.mainCamera : Camera.main);
		if (camera == null)
		{
			return;
		}
		Vector3 vector = camera.WorldToScreenPoint(entityPosition);
		if (!(vector.z <= 0f))
		{
			float num = (float)Screen.height / (2f * Mathf.Tan(camera.fieldOfView * ((float)Math.PI / 180f) * 0.5f));
			double magnitude = (entityPosition - FlightGlobals.ActiveVessel.GetWorldPos3D()).magnitude;
			float num2 = 6f * (IsInterplanetaryEncounter() ? 100f : 1f);
			float a = num2 * num / Mathf.Max(0.01f, (float)magnitude);
			if (alwaysVisible)
			{
				a = Mathf.Max(a, 3f);
			}
			a = Mathf.Min(a, 900f);
			float num3 = a * 0.5f;
			float x = vector.x;
			float y = vector.y;
			if (!(x < 0f - num3) && !(x > (float)Screen.width + num3) && !(y < 0f - num3) && !(y > (float)Screen.height + num3))
			{
				Rect position = new Rect(x - num3, (float)Screen.height - y - num3, a, a);
				Texture2D image = ((IsInterplanetaryEncounter() && interplanetaryVisualTexture != null) ? interplanetaryVisualTexture : visualTexture);
				GUI.DrawTexture(position, image, ScaleMode.ScaleToFit, alphaBlend: true);
			}
		}
	}

	public void OnDestroy()
	{
		if (instance == this)
		{
			instance = null;
		}
		RestoreCrewCourage();
		RestoreEvaPanicSpeed();
		RestoreWindowPosition();
		CancelAllAttackSequences();
		ShutdownScares();
		DestroyDread();
		DestroyHunt();
		SetDebugWaterFogDisabled(disabled: false);
		DespawnGroundEntity();
		DespawnBulkEntity();
		DespawnSeaEntity();
		DespawnBloopEntity();
		DespawnRakeEntity();
		DespawnEyeEntity();
		DespawnGrabberEntity();
		if (bdArmoryGpsBridge != null)
		{
			bdArmoryGpsBridge.Clear();
		}
		if (bdArmoryGroundRadarBridge != null)
		{
			bdArmoryGroundRadarBridge.Clear();
		}
		DestroyGroundEntityDebugBeacons();
		GameEvents.onGUIApplicationLauncherReady.Remove(AddToolbarButton);
		GameEvents.onGUIApplicationLauncherDestroyed.Remove(RemoveToolbarButton);
		GameEvents.onGameStateLoad.Remove(OnGameStateLoaded);
		GameEvents.OnScienceRecieved.Remove(OnScienceReceived);
		GameEvents.OnExperimentDeployed.Remove(OnExperimentDeployed);
		RemoveToolbarButton();
		if (entitySoundObject != null)
		{
			UnityEngine.Object.Destroy(entitySoundObject);
		}
		if (ambientSoundObject != null)
		{
			UnityEngine.Object.Destroy(ambientSoundObject);
		}
		if (dresSomethingSoundObject != null)
		{
			UnityEngine.Object.Destroy(dresSomethingSoundObject);
		}
	}

	private int BearingTo(Vessel vessel)
	{
		Vector3 normalized = ((Vector3)(entityPosition - vessel.GetWorldPos3D())).normalized;
		float num = Vector3.SignedAngle(vessel.transform.forward, normalized, vessel.transform.up);
		return Mathf.RoundToInt((num + 360f) % 360f);
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
