using System;
using System.Reflection;
using BepInEx.Bootstrap;
using UnityEngine;

namespace VanityWardrobe;

// Einherjar Bodies' look API, found by name when that mod is installed: its eye and underwear colours. Without the mod, Available is
// false and the appearance panel leaves those parts out. (VillagePlebs finds the same API the same way.) A look is the eyes as text; its
// fields (EyeHue, EyeSaturation, EyeValue, Glow, IrisSize, HideIris) are each from 0 to 1. An underwear colour is its hue (0),
// saturation (1) or brightness (2), each from 0 to 1.
internal static class EinherjarLink
{
    private const string Guid = "MagicMike.EinherjarBodies";
    private const int ApiVersion = 1;

    // Where the mod keeps each character's eyes and underwear colour, with the character (the names carry its own version)
    private const string EyesKey = "einherjar_eyes_v1";
    private const string UnderwearKey = "einherjar_underwear_v1";

    private static bool s_looked;
    private static Func<string> s_defaultLook;
    private static Func<string, string, float> s_getField;
    private static Func<string, string, float, string> s_setField;
    private static Action<GameObject, string> s_applyLook;
    private static Func<string> s_underwearDefault;
    private static Func<string, int, float> s_underwearPart;
    private static Func<string, int, float, string> s_setUnderwearPart;
    private static Action<GameObject, string> s_applyUnderwear;

    public static bool Available
    {
        get
        {
            Find();
            return s_applyLook != null;
        }
    }

    private static void Find()
    {
        if (s_looked)
        {
            return;
        }
        s_looked = true;
        try
        {
            if (!Chainloader.PluginInfos.TryGetValue(Guid, out BepInEx.PluginInfo plugin) || plugin.Instance == null)
            {
                return;
            }
            Type api = plugin.Instance.GetType().Assembly.GetType("EinherjarBodies.EinherjarLookApi");
            FieldInfo version = api?.GetField("Version", BindingFlags.Public | BindingFlags.Static);
            if (version == null || (int)version.GetValue(null) != ApiVersion)
            {
                Debug.Log("[VanityWardrobe] Einherjar Bodies is installed without a look API this version understands: the eye and underwear settings are left out.");
                return;
            }
            s_defaultLook = Bind<Func<string>>(api, "DefaultLook");
            s_getField = Bind<Func<string, string, float>>(api, "GetField");
            s_setField = Bind<Func<string, string, float, string>>(api, "SetField");
            s_underwearDefault = Bind<Func<string>>(api, "UnderwearDefault");
            s_underwearPart = Bind<Func<string, int, float>>(api, "UnderwearPart");
            s_setUnderwearPart = Bind<Func<string, int, float, string>>(api, "SetUnderwearPart");
            s_applyUnderwear = Bind<Action<GameObject, string>>(api, "ApplyUnderwear");
            s_applyLook = Bind<Action<GameObject, string>>(api, "ApplyLook");   // (last: it is what Available checks)
            Debug.Log("[VanityWardrobe] Einherjar Bodies found: the appearance panel has its eye and underwear settings.");
        }
        catch (Exception e)
        {
            s_applyLook = null;
            Debug.LogWarning("[VanityWardrobe] Could not use Einherjar Bodies' look API: " + e.Message);
        }
    }

    private static T Bind<T>(Type api, string name) where T : Delegate
    {
        MethodInfo method = api.GetMethod(name, BindingFlags.Public | BindingFlags.Static) ?? throw new MissingMethodException(api.FullName, name);
        return (T)Delegate.CreateDelegate(typeof(T), method);
    }

    // ---- eyes

    public static string Eyes(Player player)
    {
        return Available && player.m_customData.TryGetValue(EyesKey, out string look) && !string.IsNullOrEmpty(look) ? look : DefaultLook();
    }

    public static string DefaultLook() => Available ? s_defaultLook() : "";

    public static float Get(string look, string field) => Available ? s_getField(look, field) : 0f;

    // The eyes with one field changed (a look is text; nothing is stored anywhere)
    public static string EditEye(string look, string field, float value) => Available ? s_setField(look, field, value) : look;

    // Shows these eyes on a character. For the fitting-room model this is only kept with that model, not with the player.
    public static void ShowEyes(GameObject target, string look)
    {
        if (Available && target != null)
        {
            s_applyLook(target, look);
        }
    }

    // ---- underwear

    // Gives one player the same eyes and underwear colour as another, by copying what the mod keeps with the player. Used for the fitting-room model.
    public static void CopyLook(Player from, Player to)
    {
        foreach (string key in new[] { EyesKey, UnderwearKey })
        {
            if (from.m_customData.TryGetValue(key, out string value))
            {
                to.m_customData[key] = value;
            }
            else
            {
                to.m_customData.Remove(key);
            }
        }
    }

    public static string Underwear(Player player)
    {
        return Available && player.m_customData.TryGetValue(UnderwearKey, out string colour) && !string.IsNullOrEmpty(colour) ? colour : UnderwearDefault();
    }

    public static string UnderwearDefault() => Available ? s_underwearDefault() : "";

    public static float UnderwearPart(string colour, int part) => Available ? s_underwearPart(colour, part) : 0f;

    public static string EditUnderwear(string colour, int part, float value) => Available ? s_setUnderwearPart(colour, part, value) : colour;

    public static void ShowUnderwear(GameObject target, string colour)
    {
        if (Available && target != null)
        {
            s_applyUnderwear(target, colour);
        }
    }
}
