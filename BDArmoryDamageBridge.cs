using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public sealed class BDArmoryDamageBridge
{
	private sealed class Target
	{
		public string name;

		public GameObject root;

		public BDAEntityHitbox hitbox;

		public float health;

		public float maxHealth;

		public Action onDestroyed;
	}

	private readonly Dictionary<string, Target> targets = new Dictionary<string, Target>();

	private readonly HashSet<int> spentMissiles = new HashSet<int>();

	private Type weaponType;

	private Type missileType;

	private MemberInfo weaponRecentlyFiring;

	private MemberInfo weaponFinalFire;

	private MemberInfo weaponFireTransforms;

	private MemberInfo weaponCaliber;

	private MemberInfo weaponFinalAimTarget;

	private MemberInfo missileHasFired;

	private bool unavailable;

	private float nextLookupTime;

	private float nextScanTime;

	private float lastScanTime;

	public void Sync(string name, bool active, GameObject root, float maxHealth, Action onDestroyed)
	{
		Target value;
		if (!active || root == null)
		{
			targets.Remove(name);
		}
		else if (!targets.TryGetValue(name, out value) || value.root != root)
		{
			Target target = new Target();
			target.name = name;
			target.root = root;
			target.maxHealth = maxHealth;
			target.health = maxHealth;
			target.onDestroyed = onDestroyed;
			value = target;
			value.hitbox = AddHitbox(root, name);
			targets[name] = value;
		}
		else if (value.hitbox == null)
		{
			value.hitbox = AddHitbox(root, name);
		}
	}

	public void Clear()
	{
		targets.Clear();
		spentMissiles.Clear();
	}

	public void Update()
	{
		if (!EnsureAvailable() || targets.Count == 0 || Time.timeScale <= 0f || Time.realtimeSinceStartup < nextScanTime)
		{
			return;
		}
		float interval = ((lastScanTime > 0f) ? Mathf.Clamp(Time.realtimeSinceStartup - lastScanTime, 0.02f, 0.25f) : 0.1f);
		lastScanTime = Time.realtimeSinceStartup;
		nextScanTime = Time.realtimeSinceStartup + 0.1f;
		PartModule[] array = UnityEngine.Object.FindObjectsOfType<PartModule>();
		PartModule[] array2 = array;
		foreach (PartModule partModule in array2)
		{
			if (!(partModule == null))
			{
				Type type = partModule.GetType();
				if (missileType.IsAssignableFrom(type))
				{
					CheckMissile(partModule);
				}
				else if (weaponType.IsAssignableFrom(type))
				{
					CheckGun(partModule, interval);
				}
			}
		}
	}

	private bool EnsureAvailable()
	{
		if (unavailable)
		{
			return false;
		}
		if (weaponType != null && missileType != null)
		{
			return true;
		}
		if (Time.realtimeSinceStartup < nextLookupTime)
		{
			return false;
		}
		nextLookupTime = Time.realtimeSinceStartup + 5f;
		try
		{
			Assembly assembly = null;
			Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
			foreach (Assembly assembly2 in assemblies)
			{
				if (assembly2.GetType("BDArmory.Modules.ModuleWeapon", throwOnError: false) != null || assembly2.GetType("BDArmory.Weapons.ModuleWeapon", throwOnError: false) != null)
				{
					assembly = assembly2;
					break;
				}
			}
			if (assembly == null)
			{
				return false;
			}
			weaponType = assembly.GetType("BDArmory.Modules.ModuleWeapon", throwOnError: false) ?? assembly.GetType("BDArmory.Weapons.ModuleWeapon", throwOnError: false);
			missileType = assembly.GetType("BDArmory.Modules.MissileBase", throwOnError: false) ?? assembly.GetType("BDArmory.Weapons.Missiles.MissileBase", throwOnError: false);
			weaponRecentlyFiring = FindMember(weaponType, "recentlyFiring");
			weaponFinalFire = FindMember(weaponType, "finalFire");
			weaponFireTransforms = FindMember(weaponType, "fireTransforms");
			weaponCaliber = FindMember(weaponType, "caliber");
			weaponFinalAimTarget = FindMember(weaponType, "finalAimTarget");
			missileHasFired = FindMember(missileType, "HasFired");
			return weaponRecentlyFiring != null && weaponFinalFire != null && weaponFireTransforms != null && weaponCaliber != null && weaponFinalAimTarget != null && missileHasFired != null;
		}
		catch (Exception)
		{
			unavailable = true;
			return false;
		}
	}

	private static MemberInfo FindMember(Type type, string name)
	{
		PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		return (property != null) ? ((MemberInfo)property) : ((MemberInfo)type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
	}

	private static object GetMemberValue(MemberInfo member, object instance)
	{
		PropertyInfo propertyInfo = member as PropertyInfo;
		if (propertyInfo != null)
		{
			return propertyInfo.GetValue(instance, null);
		}
		FieldInfo fieldInfo = member as FieldInfo;
		return (fieldInfo != null) ? fieldInfo.GetValue(instance) : null;
	}

	private static bool GetMemberBool(MemberInfo member, object instance)
	{
		object memberValue = GetMemberValue(member, instance);
		return memberValue is bool && (bool)memberValue;
	}

	private static BDAEntityHitbox AddHitbox(GameObject root, string id)
	{
		BDAEntityHitbox bDAEntityHitbox = root.GetComponent<BDAEntityHitbox>();
		if (bDAEntityHitbox == null)
		{
			bDAEntityHitbox = root.AddComponent<BDAEntityHitbox>();
		}
		bDAEntityHitbox.targetId = id;
		BoxCollider boxCollider = root.GetComponent<BoxCollider>();
		if (boxCollider == null)
		{
			boxCollider = root.AddComponent<BoxCollider>();
		}
		Bounds bounds = new Bounds(root.transform.position, Vector3.one * 2f);
		bool flag = false;
		Renderer[] componentsInChildren = root.GetComponentsInChildren<Renderer>(includeInactive: true);
		foreach (Renderer renderer in componentsInChildren)
		{
			if (!(renderer == null))
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
		boxCollider.center = root.transform.InverseTransformPoint(bounds.center);
		Vector3 vector = root.transform.InverseTransformVector(bounds.size);
		boxCollider.size = new Vector3(Mathf.Abs(vector.x), Mathf.Abs(vector.y), Mathf.Abs(vector.z));
		// A trigger: bullets find it (CheckGun raycasts with triggers on), crafts pass through. A solid box the size of the monster
		// flung any craft that touched it away at absurd speed.
		boxCollider.isTrigger = true;
		return bDAEntityHitbox;
	}

	private void CheckGun(PartModule weapon, float interval)
	{
		if ((!GetMemberBool(weaponRecentlyFiring, weapon) && !GetMemberBool(weaponFinalFire, weapon)) || !(GetMemberValue(weaponFireTransforms, weapon) is IEnumerable enumerable))
		{
			return;
		}
		object memberValue = GetMemberValue(weaponCaliber, weapon);
		if (memberValue == null)
		{
			return;
		}
		float num = Convert.ToSingle(memberValue);
		float num2 = ((num >= 90f) ? 250f : (Mathf.Clamp(num * 0.09f, 1.2f, 14f) * interval));
		if (!(GetMemberValue(weaponFinalAimTarget, weapon) is Vector3 vector))
		{
			return;
		}
		foreach (object item in enumerable)
		{
			Transform transform = item as Transform;
			if (transform == null)
			{
				continue;
			}
			Vector3 vector2 = vector - transform.position;
			if (vector2.sqrMagnitude < 1f)
			{
				vector2 = transform.forward;
			}
			RaycastHit[] array = Physics.RaycastAll(transform.position, vector2.normalized, 20000f, -1, QueryTriggerInteraction.Collide);
			RaycastHit[] array2 = array;
			for (int i = 0; i < array2.Length; i++)
			{
				RaycastHit raycastHit = array2[i];
				BDAEntityHitbox bDAEntityHitbox = ((raycastHit.collider != null) ? raycastHit.collider.GetComponentInParent<BDAEntityHitbox>() : null);
				if (bDAEntityHitbox != null)
				{
					float amount = num2;
					Damage(bDAEntityHitbox.targetId, amount);
				}
			}
		}
	}

	private void CheckMissile(PartModule missile)
	{
		if (!GetMemberBool(missileHasFired, missile))
		{
			return;
		}
		int instanceID = missile.GetInstanceID();
		if (spentMissiles.Contains(instanceID))
		{
			return;
		}
		Vector3 position = missile.transform.position;
		foreach (Target item in new List<Target>(targets.Values))
		{
			if (!(item.root == null))
			{
				float num = ((item.name == "Laythe Leviathan") ? 140f : 24f);
				if ((item.root.transform.position - position).sqrMagnitude <= num * num)
				{
					spentMissiles.Add(instanceID);
					Damage(item.name, 250f);
					break;
				}
			}
		}
	}

	private void Damage(string id, float amount)
	{
		if (!targets.TryGetValue(id, out var value))
		{
			return;
		}
		value.health -= amount;
		if (!(value.health > 0f))
		{
			targets.Remove(id);
			if (value.onDestroyed != null)
			{
				value.onDestroyed();
			}
		}
	}
}
