using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public sealed class BDArmoryGpsBridge
{
	private readonly Dictionary<string, object> targets = new Dictionary<string, object>();

	private Type gpsInfoType;

	private ConstructorInfo gpsInfoConstructor;

	private FieldInfo gpsCoordinatesField;

	private FieldInfo gpsTargetsField;

	private bool unavailable;

	private float nextLookupTime;

	public void Sync(string name, bool active, Vector3d position)
	{
		if (!EnsureAvailable())
		{
			return;
		}
		object value;
		if (!active)
		{
			if (targets.TryGetValue(name, out value))
			{
				RemoveFromBdaLists(value);
				targets.Remove(name);
			}
			return;
		}
		if (!targets.TryGetValue(name, out value))
		{
			try
			{
				value = gpsInfoConstructor.Invoke(new object[3] { position, name, null });
				targets.Add(name, value);
			}
			catch (Exception)
			{
				unavailable = true;
				return;
			}
		}
		gpsCoordinatesField.SetValue(value, position);
		AddToBdaLists(value);
	}

	public void Clear()
	{
		foreach (object value in targets.Values)
		{
			RemoveFromBdaLists(value);
		}
		targets.Clear();
	}

	private bool EnsureAvailable()
	{
		if (unavailable)
		{
			return false;
		}
		if (gpsInfoConstructor != null && gpsTargetsField != null)
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
				if (assembly2.GetType("BDArmory.Parts.GPSTargetInfo", throwOnError: false) != null || assembly2.GetType("BDArmory.Targeting.GPSTargetInfo", throwOnError: false) != null)
				{
					assembly = assembly2;
					break;
				}
			}
			if (assembly == null)
			{
				return false;
			}
			gpsInfoType = assembly.GetType("BDArmory.Parts.GPSTargetInfo", throwOnError: false) ?? assembly.GetType("BDArmory.Targeting.GPSTargetInfo", throwOnError: false);
			Type type = assembly.GetType("BDArmory.UI.BDATargetManager", throwOnError: false);
			gpsInfoConstructor = ((gpsInfoType != null) ? gpsInfoType.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[3]
			{
				typeof(Vector3d),
				typeof(string),
				typeof(Vessel)
			}, null) : null);
			gpsCoordinatesField = ((gpsInfoType != null) ? gpsInfoType.GetField("gpsCoordinates", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) : null);
			gpsTargetsField = ((type != null) ? type.GetField("GPSTargets", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) : null);
			return gpsInfoConstructor != null && gpsCoordinatesField != null && gpsTargetsField != null;
		}
		catch (Exception)
		{
			unavailable = true;
			return false;
		}
	}

	private IEnumerable GetTargetLists()
	{
		if (gpsTargetsField == null)
		{
			yield break;
		}
		object dictionary = gpsTargetsField.GetValue(null);
		if (!(dictionary is IEnumerable entries))
		{
			yield break;
		}
		foreach (object entry in entries)
		{
			PropertyInfo value = entry.GetType().GetProperty("Value");
			IList list = ((value != null) ? (value.GetValue(entry, null) as IList) : null);
			if (list != null)
			{
				yield return list;
			}
		}
	}

	private void AddToBdaLists(object target)
	{
		foreach (IList targetList in GetTargetLists())
		{
			if (!targetList.Contains(target))
			{
				targetList.Add(target);
			}
		}
	}

	private void RemoveFromBdaLists(object target)
	{
		foreach (IList targetList in GetTargetLists())
		{
			if (targetList.Contains(target))
			{
				targetList.Remove(target);
			}
		}
	}
}
