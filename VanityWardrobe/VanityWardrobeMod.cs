using BepInEx;
using HarmonyLib;
using Jotunn.Utils;
using System.Reflection;

namespace VanityWardrobe;

[BepInPlugin(PluginGUID, PluginName, PluginVersion)]
[BepInDependency("com.jotunn.jotunn", BepInDependency.DependencyFlags.HardDependency)]
[NetworkCompatibility(CompatibilityLevel.NotEnforced, VersionStrictness.None)]
public class VanityWardrobeMod : BaseUnityPlugin
{
    public const string PluginName = "VanityWardrobe";
    public const string PluginVersion = "1.0.0";
    public const string PluginAuthor = "MagicMike";
    internal const string PluginGUID = "MagicMike.VanityWardrobe";
    internal const string PluginCopy = "Copyright © MagicMike 2026";
    public const string PluginLink = "https://discord.gg/mnr36G6J4x";

    private Harmony harmony;

    public void Awake()
    {
        MainConfig.Init(Config);
        MagicMike.ModSettings.ModSettings.Init(this);

        if (!MainConfig.modEnabled.Value)
        {
            return;
        }

        Translations.Load(this);
        harmony = Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), PluginGUID);
    }

    public void Update()
    {
        WardrobePanel.Tick();
        Biomes.Tick(this);
        Colors.Publish();
    }

    public void OnDestroy()
    {
        harmony?.UnpatchSelf();
        WardrobePanel.Shutdown();
        Recolor.Clear();
    }
}
