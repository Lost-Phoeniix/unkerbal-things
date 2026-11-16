using System;
using System.IO;
using UnityEngine;

[KSPAddon(KSPAddon.Startup.MainMenu, true)]
public class KrakensKreationsWelcome : MonoBehaviour
{
	private const string ConfigFileName = "krakens-kreations-welcome.cfg";

	private const string ReleaseVersion = "alpha-0.1";

	private Rect window = new Rect(0f, 0f, 530f, 290f);

	private bool positioned;

	private bool showWelcome;

	private static string ConfigPath => Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "Kraken's Kreations", "krakens-kreations-welcome.cfg");

	public void Awake()
	{
		showWelcome = !WasDismissed();
	}

	public void OnGUI()
	{
		if (showWelcome && HighLogic.LoadedScene == GameScenes.MAINMENU)
		{
			if (!positioned)
			{
				window.x = ((float)Screen.width - window.width) * 0.5f;
				window.y = ((float)Screen.height - window.height) * 0.5f;
				positioned = true;
			}
			window = GUI.Window(871442, window, DrawWindow, "Kraken's Kreations — Alpha 0.1");
		}
	}

	private void DrawWindow(int id)
	{
		GUILayout.Space(8f);
		GUIStyle gUIStyle = new GUIStyle(GUI.skin.label);
		gUIStyle.wordWrap = true;
		GUILayout.Label("Welcome to the first public alpha of Kraken's Kreations: a KSP horror mod filled with hostile and unexplained entities across the Kerbol system.\n\nThis release includes the Black Box and interplanetary Red Box, the Kerbal Mimic and its variants, the Moho/Eve creature, and an Eve sea creature. Entities have their own spawning conditions, sounds, chase behavior, escape mechanics, and science-report interactions.\n\nThis is an early alpha. Bugs, strange physics, balancing issues, and occasional deeply cursed Kerbal anatomy are expected. Please report problems on the Discord.", gUIStyle, GUILayout.Width(495f));
		GUILayout.FlexibleSpace();
		if (GUILayout.Button("Open Kraken's Kreations Discord"))
		{
			Application.OpenURL("https://discord.gg/pWRtp38dP");
		}
		if (GUILayout.Button("Do not show again"))
		{
			SaveDismissal();
			showWelcome = false;
		}
		GUI.DragWindow(new Rect(0f, 0f, 10000f, 25f));
	}

	private static bool WasDismissed()
	{
		if (!File.Exists(ConfigPath))
		{
			return false;
		}
		ConfigNode configNode = ConfigNode.Load(ConfigPath);
		string a = configNode?.GetValue("dismissedVersion");
		bool result;
		return configNode != null && bool.TryParse(configNode.GetValue("dismissed"), out result) && result && string.Equals(a, "alpha-0.1", StringComparison.Ordinal);
	}

	private static void SaveDismissal()
	{
		ConfigNode configNode = new ConfigNode("KRAKENS_KREATIONS_WELCOME");
		configNode.AddValue("dismissed", value: true);
		configNode.AddValue("dismissedVersion", "alpha-0.1");
		configNode.Save(ConfigPath);
	}
}
