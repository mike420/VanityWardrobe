using System.IO;
using BepInEx;
using Jotunn.Managers;
using UnityEngine;

namespace VanityWardrobe;

// The text shown in the game (item and button names). English is built in; every other language is read
// from BepInEx/config/VanityWardrobe/Translations/<Language>/VanityWardrobe.json, and a file there for English replaces the
// built-in text, so any of them can be edited or added to.
internal static class Translations
{
    public static void Load(BaseUnityPlugin plugin)
    {
        string mod = plugin.Info.Metadata.Name;
        var localization = LocalizationManager.Instance.GetLocalization();
        string folder = Path.Combine(Paths.ConfigPath, mod, "Translations");

        bool englishFromFile = false;
        if (Directory.Exists(folder))
        {
            foreach (string languageFolder in Directory.GetDirectories(folder))
            {
                string language = Path.GetFileName(languageFolder);
                foreach (string file in Directory.GetFiles(languageFolder, "*.json"))
                {
                    try
                    {
                        localization.AddJsonFile(language, File.ReadAllText(file));
                        englishFromFile |= language == "English";
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogWarning($"[{mod}] Could not read the translation file {file}: {e.Message}");
                    }
                }
            }
        }

        if (!englishFromFile)
        {
            using Stream resource = plugin.GetType().Assembly.GetManifestResourceStream("translations/English.json");
            if (resource != null)
            {
                localization.AddJsonFile("English", new StreamReader(resource).ReadToEnd());
            }
        }
    }

    // A translated text, with its $1, $2... filled in with the values given
    public static string T(string key, params string[] values)
    {
        Localization game = Localization.instance;
        return game != null ? game.Localize("$" + key, values) : key;
    }
}
