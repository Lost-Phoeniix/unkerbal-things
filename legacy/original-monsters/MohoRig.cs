using System;
using UnityEngine;

// Moho/Eve creature, part 1 of 3: the skeleton contract. No geometry knowledge lives here.
// MohoCreature (gait, IK, heading, footfall) writes a MohoPose; any skin consumes it:
//   MohoSkinProcedural  builds the body from lofted tubes at runtime (shipped now),
//   MohoBoneSkin        drives a skinned mesh loaded from an asset bundle by the bone names below.
// Limb order everywhere: 0 = front left (arm), 1 = front right, 2 = hind left (leg), 3 = hind right.
public enum MohoJoint
{
	Tail,
	Pelvis,
	SpineMid,
	Chest,
	NeckBase,
	NeckMid,
	Head,
	// per limb, 3 joints each, starting at LimbFirst: Upper (shoulder/hip), Lower (elbow/knee), Hand (wrist/ankle; the hand/foot bone)
	LimbFirst
}

public static class MohoRigInfo
{
	public const int LimbCount = 4;
	public const int JointCount = (int)MohoJoint.LimbFirst + LimbCount * 3;

	public static int LimbJoint(int limb, int part) { return (int)MohoJoint.LimbFirst + limb * 3 + part; }   // part 0 upper, 1 lower, 2 hand

	// Bone names a skinned mesh must provide (case-insensitive). Finger bones are optional: <hand>_f<1..5>_<1..3>, e.g. hand_l_f2_1.
	// Bone axes expected: +Z along the bone towards its child (limbs, spine: towards the head), +Y dorsal / away from the ground when planted.
	public static readonly string[] JointNames = BuildNames();

	static string[] BuildNames()
	{
		string[] n = new string[JointCount];
		n[(int)MohoJoint.Tail] = "tail";
		n[(int)MohoJoint.Pelvis] = "pelvis";
		n[(int)MohoJoint.SpineMid] = "spine";
		n[(int)MohoJoint.Chest] = "chest";
		n[(int)MohoJoint.NeckBase] = "neck1";
		n[(int)MohoJoint.NeckMid] = "neck2";
		n[(int)MohoJoint.Head] = "head";
		string[] lr = { "l", "r", "l", "r" };
		for (int i = 0; i < LimbCount; i++)
		{
			string kind = i < 2 ? "arm" : "leg";
			string end = i < 2 ? "hand" : "foot";
			n[LimbJoint(i, 0)] = kind + "_" + lr[i] + "_upper";
			n[LimbJoint(i, 1)] = kind + "_" + lr[i] + "_lower";
			n[LimbJoint(i, 2)] = end + "_" + lr[i];
		}
		return n;
	}

	public static string FingerBoneName(int limb, int finger, int phalanx)
	{
		return JointNames[LimbJoint(limb, 2)] + "_f" + (finger + 1) + "_" + (phalanx + 1);
	}
}

// Expected proportions (metres, rest pose, feet planted). A replacement mesh should be built to these; the rig scales nothing.
public static class MohoProportions
{
	public const float StandHeight = 31f;       // chest/pelvis centre above the planted palms and soles
	public const float TorsoLength = 18f;       // pelvis bone to chest bone
	public const float ArmUpper = 33f, ArmLower = 35f, HandLength = 23f;    // palm 6.5 + fingers about 16
	public const float LegUpper = 35f, LegLower = 37f, FootLength = 26f;    // sole 8 + toes about 18
	public const float ShoulderHalfSpan = 3.2f, HipHalfSpan = 2.6f;         // socket offset from the spine, lateral
	public const float NeckLength = 11f;
	public const float HeadHeight = 9.5f, HeadWidth = 5.6f, HeadDepth = 7.2f;
	public const float HandPalm = 6.5f, FootPalm = 8f, WristLift = 1.6f, AnkleLift = 2.2f, KnuckleLift = 0.6f;
	public const float HeadPivotAboveChin = 0.28f * HeadHeight;               // Head joint = neck end, inside the skull
	public const float HeadPivotBehindFace = 2.0f;
	public const float ChestWidth = 8.9f, WaistWidth = 3.7f;
	public const float ReachWithHands = 68f;    // shoulder to wrist at full stretch is ArmUpper + ArmLower
}

// The hand-made skinned model (bundle Moho/moho.bundle). Metres. Blender axes of the model: x right, y forward, z up.
// shoulder socket (+-10.5, 13, 48.3), elbow (+-18, 28, 26), wrist (+-16.5, 52, 6.5), palm (+-16.5, 57.5, 3), hip (+-3.2, -14.5, 39.6),
// knee (+-16, 7, 20), ankle (+-13, -23, 8), sole (+-13, -30, 2.6); the hind toes point backward; the head bone drives a closed neck stump.
public static class MohoModelProportions
{
	public const float ArmUpper = 27.9f, ArmLower = 31.0f, LegUpper = 31.8f, LegLower = 32.4f;
	public const float ShoulderHalfSpan = 10.5f, HipHalfSpan = 3.2f;
	public const float TorsoLength = 18.04f;        // pelvis bone to chest bone (the shoulder sockets sit 10 m ahead of the chest bone)
	public const float RestPitchDeg = 14.1f;        // the model's torso axis rises this much toward the shoulders in its rest stance
	public const float StandHeight = 40.5f;         // body centre (middle of pelvis and chest bones) above the planted palms and soles while walking; the rest stance is 42.2
	public const float HandPalm = 7.0f, FootPalm = 8.5f, WristLift = 6.5f, AnkleLift = 8.0f, KnuckleLift = 2.0f;
	public const float RestFront = 61.25f, RestHind = -21.25f, RestBodyHeight = 42.2f;   // contact centres ahead of / behind the body centre in the rest stance
}

// The pose a skin consumes: world-space joint positions and rotations, per-limb planting and finger curl.
public sealed class MohoPose
{
	public readonly Vector3[] Position = new Vector3[MohoRigInfo.JointCount];
	public readonly Quaternion[] Rotation = new Quaternion[MohoRigInfo.JointCount];
	public readonly bool[] Planted = new bool[MohoRigInfo.LimbCount];
	public readonly float[] Curl = new float[MohoRigInfo.LimbCount];                // 0 = fingers laid out on the ground, 1 = curled
	public readonly Vector3[] Contact = new Vector3[MohoRigInfo.LimbCount];         // ground point under the palm/sole centre
	public readonly Vector3[] HandForward = new Vector3[MohoRigInfo.LimbCount];     // palm axis wrist -> knuckles
	public readonly Vector3[] HandNormal = new Vector3[MohoRigInfo.LimbCount];      // palm normal (away from the ground when planted)
	public Vector3 Origin;                                                         // ground reference below the body
	public Quaternion Frame = Quaternion.identity;                                 // body frame (forward, up)
	public float Time;

	public Vector3 P(MohoJoint j) { return Position[(int)j]; }
}

// Loads the hand-made model and binds it to a creature: the bundle, the profile switch, the rest-stance capture. Shared by the game and the offline rig.
public static class MohoModelLoader
{
	// Returns null (failure says why) when the bundle is missing or unusable; the caller then keeps the procedural skin.
	// Call right after the creature's GameObject is made, while its rotation is still the identity: the bind is a rotation difference.
	public static MohoBoneSkin Create(MohoCreature creature, string bundlePath, out string failure)
	{
		MohoBoneSkin skin = MohoBoneSkin.FromBundle(bundlePath, creature.transform, out failure);
		if (skin == null) return null;
		creature.UseModelProfile();
		creature.Position = Vector3.zero; creature.Up = Vector3.up; creature.Heading = Vector3.forward;
		MohoPose rest = creature.BuildRestPose();
		skin.CaptureBind(rest);
		return skin;
	}
}

// A skin takes the pose each frame.
public interface IMohoSkin
{
	void Apply(MohoPose pose);
}

// Drives a skinned mesh (e.g. loaded from an asset bundle) from the pose by bone name. Joint rotations are applied as world rotations
// with a per-bone offset (bind rotation relative to the +Z/+Y convention), so the artist's rest pose just has to follow MohoProportions.
public sealed class MohoBoneSkin : IMohoSkin
{
	readonly Transform[] bones = new Transform[MohoRigInfo.JointCount];
	readonly Quaternion[] bindOffset = new Quaternion[MohoRigInfo.JointCount];
	readonly Transform[,,] fingers = new Transform[MohoRigInfo.LimbCount, 5, 3];
	readonly float[] fingerRest = new float[MohoRigInfo.LimbCount];
	public int Found;

	static AssetBundle cachedBundle;
	static string cachedPath;
	int[] order;                      // joint indices, parents before children
	public SkinnedMeshRenderer Renderer;
	public Material Material;
	public string ShaderName = "?";
	public int VertexCount, TriangleCount;

	public MohoBoneSkin(Transform root)
	{
		Transform[] all = root.GetComponentsInChildren<Transform>(true);
		for (int j = 0; j < MohoRigInfo.JointCount; j++)
		{
			foreach (Transform t in all)
			{
				if (string.Equals(t.name, MohoRigInfo.JointNames[j], StringComparison.OrdinalIgnoreCase)) { bones[j] = t; Found++; break; }
			}
		}
		for (int l = 0; l < MohoRigInfo.LimbCount; l++)
			for (int f = 0; f < 5; f++)
				for (int p = 0; p < 3; p++)
				{
					string n = MohoRigInfo.FingerBoneName(l, f, p);
					foreach (Transform t in all) if (string.Equals(t.name, n, StringComparison.OrdinalIgnoreCase)) { fingers[l, f, p] = t; break; }
				}
	}

	void BuildOrder()
	{
		System.Collections.Generic.List<int> l = new System.Collections.Generic.List<int>();
		for (int j = 0; j < MohoRigInfo.JointCount; j++) if (bones[j] != null) l.Add(j);
		int[] depth = new int[MohoRigInfo.JointCount];
		for (int j = 0; j < MohoRigInfo.JointCount; j++)
		{
			int d = 0;
			for (Transform t = bones[j]; t != null; t = t.parent) d++;
			depth[j] = d;
		}
		l.Sort((x, y) => depth[x].CompareTo(depth[y]));
		order = l.ToArray();
	}

	// Loads the model from an asset bundle. Returns null (and says why) when the bundle or the model is missing; the caller then uses the procedural skin.
	public static MohoBoneSkin FromBundle(string path, Transform parent, out string failure)
	{
		failure = null;
		try
		{
			if (!System.IO.File.Exists(path)) { failure = "no bundle at " + path; return null; }
			// the bundle is loaded once and kept: a second LoadFromFile of the same file fails while the first is alive
			if (cachedBundle == null || cachedPath != path)
			{
				AssetBundle fresh = AssetBundle.LoadFromFile(path);
				if (fresh == null) { failure = "bundle did not load"; return null; }
				cachedBundle = fresh; cachedPath = path;
			}
			AssetBundle ab = cachedBundle;
			GameObject prefab = ab.LoadAsset<GameObject>("moho");
			if (prefab == null) { string[] names = ab.GetAllAssetNames(); failure = "no 'moho' model in the bundle (" + string.Join(", ", names) + ")"; return null; }
			GameObject go = (GameObject)UnityEngine.Object.Instantiate(prefab);
			go.name = "MohoModel";
			go.transform.SetParent(parent, false);
			go.transform.localPosition = Vector3.zero;
			go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);   // the model faces -Z after the import; the rig faces +Z
			MohoBoneSkin skin = new MohoBoneSkin(go.transform);
			if (skin.Found < MohoRigInfo.JointCount) { failure = "only " + skin.Found + " of " + MohoRigInfo.JointCount + " bones found"; UnityEngine.Object.Destroy(go); return null; }
			skin.Setup(go);
			return skin;
		}
		catch (System.Exception ex) { failure = ex.Message; return null; }
	}

	void Setup(GameObject go)
	{
		BuildOrder();
		Renderer = go.GetComponentInChildren<SkinnedMeshRenderer>(true);
		if (Renderer == null) return;
		VertexCount = Renderer.sharedMesh != null ? Renderer.sharedMesh.vertexCount : 0;
		TriangleCount = Renderer.sharedMesh != null ? Renderer.sharedMesh.triangles.Length / 3 : 0;
		Renderer.updateWhenOffscreen = true;      // the bones move in world space far from the root: never let the bounds cull it
		Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
		Renderer.receiveShadows = false;
		// pale, matte, lit by the sun. On airless Moho the night side is black, so where KSP's emissive-diffuse shader exists a faint self-light floor keeps it readable.
		Shader shader = Shader.Find("KSP/Emissive/Diffuse");
		bool fill = shader != null;
		if (shader == null) shader = Shader.Find("KSP/Diffuse");
		if (shader == null) shader = Shader.Find("Legacy Shaders/Diffuse");
		if (shader == null) shader = Shader.Find("Diffuse");
		Texture2D tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
		Color pale = new Color(0.80f, 0.78f, 0.74f);
		tex.SetPixels(new Color[] { pale, pale, pale, pale }); tex.Apply();
		Material = new Material(shader);
		Material.mainTexture = tex;
		if (fill && Material.HasProperty("_Emissive") && Material.HasProperty("_EmissiveColor"))
		{
			Material.SetTexture("_Emissive", tex);
			Material.SetColor("_EmissiveColor", new Color(0.10f, 0.10f, 0.11f, 1f));
		}
		if (Material.HasProperty("_Color")) Material.color = Color.white;
		ShaderName = shader.name;
		Material[] mats = new Material[Mathf.Max(1, Renderer.sharedMaterials.Length)];
		for (int i = 0; i < mats.Length; i++) mats[i] = Material;
		Renderer.sharedMaterials = mats;
		Renderer.gameObject.layer = 0;
		Transform[] all = go.GetComponentsInChildren<Transform>(true);
		for (int i = 0; i < all.Length; i++) all[i].gameObject.layer = 0;
	}

	public GameObject ModelObject { get { return Renderer != null ? Renderer.transform.root.gameObject : null; } }

	// Moves the whole model (a rest-stance capture is made with its body centre over the origin).
	public Vector3 RestCentre()
	{
		Transform p = bones[(int)MohoJoint.Pelvis], c = bones[(int)MohoJoint.Chest];
		return p != null && c != null ? (p.position + c.position) * 0.5f : Vector3.zero;
	}

	// Tip of the middle finger or toe (the last bone of it that exists), for planted-foot tests.
	public Vector3 TipWorld(int limb)
	{
		for (int p = 2; p >= 0; p--)
		{
			Transform t = fingers[limb, 2, p];
			if (t != null) return t.position;
		}
		return bones[MohoRigInfo.LimbJoint(limb, 2)] != null ? bones[MohoRigInfo.LimbJoint(limb, 2)].position : Vector3.zero;
	}

	public void Destroy()
	{
		if (Renderer != null && Renderer.transform.root != null) UnityEngine.Object.Destroy(Renderer.transform.root.gameObject);
	}

	// Call once with the model in its bind pose, matching a pose built with the same convention (rotation world, +Z along bone).
	public void CaptureBind(MohoPose rest)
	{
		for (int j = 0; j < MohoRigInfo.JointCount; j++)
			if (bones[j] != null) bindOffset[j] = Quaternion.Inverse(rest.Rotation[j]) * bones[j].rotation;
	}

	public void Apply(MohoPose pose)
	{
		if (order == null) BuildOrder();
		for (int k = 0; k < order.Length; k++)
		{
			int j = order[k];
			Transform t = bones[j];
			t.position = pose.Position[j];
			t.rotation = pose.Rotation[j] * bindOffset[j];
		}
		for (int l = 0; l < MohoRigInfo.LimbCount; l++)
		{
			float c = pose.Curl[l];
			for (int f = 0; f < 5; f++)
				for (int p = 0; p < 3; p++)
				{
					Transform t = fingers[l, f, p];
					if (t != null) t.localRotation = Quaternion.AngleAxis(c * (25f + 15f * p), Vector3.right);   // curl about the bone's local X
				}
		}
	}
}
