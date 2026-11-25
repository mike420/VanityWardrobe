using System.Collections.Generic;
using System.IO;
using Jotunn.Managers;
using TMPro;
using UnityEngine;

namespace VanityWardrobe;

// Valheim's Norse font, for titles. It does not have every character (Cyrillic, Chinese, some accents), so a label whose text it cannot
// draw keeps the normal font.
internal static class UiFont
{
    public static void Norse(TMP_Text label)
    {
        TMP_FontAsset norse = GUIManager.Instance != null ? GUIManager.Instance.TMP_Norse : null;
        if (norse == null || label == null || string.IsNullOrEmpty(label.text))
        {
            return;
        }
        // It is a capitals font, so the text is tried in capitals first. A font that fills its table as it is used has only the characters
        // drawn so far, so the ones the text needs are asked for (they are added if the font file has them).
        string upper = label.text.ToUpperInvariant();
        if (Has(norse, upper))
        {
            label.text = upper;
            label.font = norse;
        }
        else if (Has(norse, label.text))
        {
            label.font = norse;
        }
    }

    private static bool Has(TMP_FontAsset font, string text)
    {
        return font.HasCharacters(text) || font.TryAddCharacters(text, out _);
    }

    private static Sprite s_hanger;
    private static readonly Dictionary<string, Sprite> s_game = new Dictionary<string, Sprite>();

    // The hanger for the title bar, built into the mod
    public static Sprite Hanger()
    {
        if (s_hanger == null)
        {
            using (Stream stream = typeof(UiFont).Assembly.GetManifestResourceStream("icons/hanger.png"))
            {
                if (stream != null)
                {
                    MemoryStream memory = new MemoryStream();
                    stream.CopyTo(memory);
                    byte[] bytes = memory.ToArray();
                    Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "VanityWardrobe hanger", filterMode = FilterMode.Bilinear };
                    texture.LoadImage(bytes);
                    s_hanger = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
                }
            }
        }
        return s_hanger;
    }

    private static Sprite s_pin;

    // The red pin shown on a pinned list row, built into the mod
    public static Sprite Pin()
    {
        if (s_pin == null)
        {
            using (Stream stream = typeof(UiFont).Assembly.GetManifestResourceStream("icons/pin.png"))
            {
                if (stream != null)
                {
                    MemoryStream memory = new MemoryStream();
                    stream.CopyTo(memory);
                    Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "VanityWardrobe pin", filterMode = FilterMode.Bilinear };
                    texture.LoadImage(memory.ToArray());
                    s_pin = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
                }
            }
        }
        return s_pin;
    }

    // One of the game's own sprites by name (the mouse button icons), or null
    public static Sprite GameSprite(string name)
    {
        if (s_game.TryGetValue(name, out Sprite known) && known != null)
        {
            return known;
        }
        foreach (Sprite sprite in Resources.FindObjectsOfTypeAll<Sprite>())
        {
            if (sprite != null && sprite.name == name)
            {
                s_game[name] = sprite;
                return sprite;
            }
        }
        return null;
    }

    /// <summary>
    /// Cuts overflowing text off with "…" when the font can draw it, and plainly otherwise. The game's font has no "…",
    /// and asking TextMeshPro for an ellipsis anyway logs a warning for every text object before it truncates instead.
    /// </summary>
    public static void SetEllipsisOverflow(this TMP_Text text)
    {
        bool hasEllipsis = text.font && text.font.HasCharacter((char)0x2026, searchFallbacks: true);
        text.overflowMode = hasEllipsis ? TextOverflowModes.Ellipsis : TextOverflowModes.Truncate;
    }
}
