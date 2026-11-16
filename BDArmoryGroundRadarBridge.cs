using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public sealed class BDArmoryGroundRadarBridge
{
	private sealed class ContactState
	{
		public Vector3 position;

		public bool hasPosition;
	}

	private readonly Dictionary<string, ContactState> contacts = new Dictionary<string, ContactState>();

	private Type vesselRadarDataType;

	private Type moduleRadarType;

	private ConstructorInfo targetSignatureConstructor;

	private MethodInfo addRadarContact;

	private bool addRadarContactUsesReceivedDataFlag;

	private FieldInfo myVesselField;

	private FieldInfo availableRadarsField;

	private FieldInfo radarGroundClutterFactorField;

	private bool unavailable;

	private float nextLookupTime;

	public void Sync(string name, bool active, Vector3 position, float radarSignature)
	{
		if (!active)
		{
			contacts.Remove(name);
		}
		else if (EnsureAvailable())
		{
			if (!contacts.TryGetValue(name, out var value))
			{
				value = new ContactState();
				contacts.Add(name, value);
			}
			float num = Mathf.Max(Time.unscaledDeltaTime, 0.02f);
			Vector3 velocity = (value.hasPosition ? ((position - value.position) / num) : Vector3.zero);
			value.position = position;
			value.hasPosition = true;
			PublishToActiveAirToGroundRadars(position, velocity, radarSignature);
		}
	}

	public void Clear()
	{
		contacts.Clear();
	}

	private bool EnsureAvailable()
	{
		if (unavailable)
		{
			return false;
		}
		if (addRadarContact != null && targetSignatureConstructor != null)
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
				if (assembly2.GetType("BDArmory.Radar.VesselRadarData", throwOnError: false) != null)
				{
					assembly = assembly2;
					break;
				}
			}
			if (assembly == null)
			{
				return false;
			}
			vesselRadarDataType = assembly.GetType("BDArmory.Radar.VesselRadarData", throwOnError: false);
			moduleRadarType = assembly.GetType("BDArmory.Modules.ModuleRadar", throwOnError: false) ?? assembly.GetType("BDArmory.Radar.ModuleRadar", throwOnError: false);
			Type type = assembly.GetType("BDArmory.Targeting.TargetSignatureData", throwOnError: false);
			targetSignatureConstructor = ((type != null) ? type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[5]
			{
				typeof(Vector3),
				typeof(Vector3),
				typeof(Vector3),
				typeof(bool),
				typeof(float)
			}, null) : null);
			addRadarContact = ((vesselRadarDataType != null && moduleRadarType != null && type != null) ? vesselRadarDataType.GetMethod("AddRadarContact", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[4]
			{
				moduleRadarType,
				type,
				typeof(bool),
				typeof(bool)
			}, null) : null);
			addRadarContactUsesReceivedDataFlag = addRadarContact != null;
			if (addRadarContact == null && vesselRadarDataType != null && moduleRadarType != null && type != null)
			{
				addRadarContact = vesselRadarDataType.GetMethod("AddRadarContact", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[3]
				{
					moduleRadarType,
					type,
					typeof(bool)
				}, null);
			}
			myVesselField = ((vesselRadarDataType != null) ? vesselRadarDataType.GetField("myVessel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) : null);
			availableRadarsField = ((vesselRadarDataType != null) ? vesselRadarDataType.GetField("availableRadars", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) : null);
			radarGroundClutterFactorField = ((moduleRadarType != null) ? moduleRadarType.GetField("radarGroundClutterFactor", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) : null);
			return targetSignatureConstructor != null && addRadarContact != null && myVesselField != null && availableRadarsField != null && radarGroundClutterFactorField != null;
		}
		catch (Exception)
		{
			unavailable = true;
			return false;
		}
	}

	private void PublishToActiveAirToGroundRadars(Vector3 position, Vector3 velocity, float radarSignature)
	{
		Vessel activeVessel = FlightGlobals.ActiveVessel;
		if (activeVessel == null)
		{
			return;
		}
		UnityEngine.Object[] array = Resources.FindObjectsOfTypeAll(vesselRadarDataType);
		UnityEngine.Object[] array2 = array;
		foreach (UnityEngine.Object obj in array2)
		{
			if (obj == null || myVesselField.GetValue(obj) as Vessel != activeVessel || !(availableRadarsField.GetValue(obj) is IEnumerable enumerable))
			{
				continue;
			}
			foreach (object item in enumerable)
			{
				if (item != null && moduleRadarType.IsInstanceOfType(item) && !(Convert.ToSingle(radarGroundClutterFactorField.GetValue(item)) < 1f))
				{
					try
					{
						object obj2 = targetSignatureConstructor.Invoke(new object[5]
						{
							velocity,
							position,
							Vector3.zero,
							true,
							radarSignature
						});
						object[] parameters = (addRadarContactUsesReceivedDataFlag ? new object[4] { item, obj2, false, false } : new object[3] { item, obj2, false });
						addRadarContact.Invoke(obj, parameters);
					}
					catch (Exception)
					{
					}
				}
			}
		}
	}
}
