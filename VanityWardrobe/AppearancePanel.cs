using System;
using System.Collections.Generic;
using System.Linq;
using MagicMike.ModSettings;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VanityWardrobe;

// The look of the player's own character, in a panel beside the wardrobe: sex, skin, hair, beard and hair colour, and, when Einherjar
// Bodies is installed, the eyes and underwear. Changes are made to the character at once, as they are in Einherjar Bodies' own window,
// and the fitting-room model follows.
internal sealed class AppearancePanel
{
    private const int MaleModel = 0;
    private const int FemaleModel = 1;
    private const int FitSteps = 32;

    public const float Width = 280f;
    private const float Pad = 10f;
    private const float TitleHeight = 38f;
    private const float HeaderHeight = 26f;
    private const float RowHeight = 30f;
    private const float RowGap = 2f;
    private const float SectionGap = 6f;
    private const float ButtonHeight = 30f;
    private const float PickerHeight = 96f;
    private const float HueWidth = 20f;
    private const float ArrowSize = 24f;
    private const float ResetSize = 24f;
    private const float LabelShare = 0.36f;

    private sealed class Part
    {
        public GameObject Box;
        public float Height;
        public float Before;
        public Func<bool> Shown;
    }

    private sealed class Choice
    {
        public TMP_Text Label;
        public List<ItemDrop> Items;
        public Func<string> Current;
    }

    private sealed class SliderRow
    {
        public Slider Slider;
        public Button Reset;
        public float Default;

        public void Show(float value)
        {
            Slider.SetValueWithoutNotify(value);
            Refresh();
        }

        public void Refresh()
        {
            if (Reset != null)
            {
                Reset.gameObject.SetActive(!Mathf.Approximately(Slider.value, Default));
            }
        }
    }

    // The arrows are drawn, so they do not depend on the font having the characters
    private abstract class ChevronIcon : MaskableGraphic
    {
        private const float Stroke = 3f;

        protected abstract bool PointsRight { get; }

        private void AddQuad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            int start = vh.currentVertCount;
            UIVertex vertex = UIVertex.simpleVert;
            vertex.color = color;
            foreach (Vector2 corner in new[] { a, b, c, d })
            {
                vertex.position = corner;
                vh.AddVert(vertex);
            }
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            float width = r.height * 0.5f;
            float left = r.center.x - width / 2f;
            float right = r.center.x + width / 2f;
            float tip = PointsRight ? right : left;
            float ends = PointsRight ? left : right;
            float inward = PointsRight ? -Stroke : Stroke;
            AddQuad(vh, new Vector2(ends, r.yMax), new Vector2(ends - inward, r.yMax), new Vector2(tip, r.center.y), new Vector2(tip + inward, r.center.y));
            AddQuad(vh, new Vector2(tip + inward, r.center.y), new Vector2(tip, r.center.y), new Vector2(ends - inward, r.yMin), new Vector2(ends, r.yMin));
        }
    }

    private sealed class LeftChevronIcon : ChevronIcon
    {
        protected override bool PointsRight => false;
    }

    private sealed class RightChevronIcon : ChevronIcon
    {
        protected override bool PointsRight => true;
    }

    // Reports a click or drag over a picture as where in it, from 0 to 1 across and up
    private sealed class PickArea : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        public Action<Vector2> OnPick;

        public void OnPointerDown(PointerEventData eventData) => Pick(eventData);

        public void OnDrag(PointerEventData eventData) => Pick(eventData);

        private void Pick(PointerEventData eventData)
        {
            RectTransform rect = (RectTransform)transform;
            if (eventData.button == PointerEventData.InputButton.Left && RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, eventData.position, eventData.pressEventCamera, out Vector2 point))
            {
                Rect area = rect.rect;
                OnPick?.Invoke(new Vector2(Mathf.Clamp01((point.x - area.xMin) / area.width), Mathf.Clamp01((point.y - area.yMin) / area.height)));
            }
        }
    }

    // The game's ranges for skin and hair colour. The character creator holds them and exists only at the main menu, so they are read
    // from it when it shows (see Patches); these are its usual values until then.
    private static Color s_skin0 = new Color(1f, 0.87f, 0.77f);
    private static Color s_skin1 = new Color(0.3f, 0.2f, 0.14f);
    private static Color s_hair0 = new Color(0.95f, 0.8f, 0.55f);
    private static Color s_hair1 = new Color(0.45f, 0.18f, 0.08f);
    private static string s_noBeard = "BeardNone";
    private static string s_noHair = "HairNone";

    public static void ReadRanges(PlayerCustomizaton creator)
    {
        s_skin0 = creator.m_skinColor0;
        s_skin1 = creator.m_skinColor1;
        s_hair0 = creator.m_hairColor0;
        s_hair1 = creator.m_hairColor1;
        if (creator.m_noBeard != null)
        {
            s_noBeard = creator.m_noBeard.name;
        }
        if (creator.m_noHair != null)
        {
            s_noHair = creator.m_noHair.name;
        }
    }

    public RectTransform Panel { get; private set; }

    // The panel is at least this tall (the height of the window beside it)
    public float MinHeight { get; set; }

    // Called after a change, so the fitting-room model can show it. The panel keeps its own copy of the look (read from the player each time the
    // window opens) and changes only that: nothing here is written to the player's character.
    public Action Changed;
    public Func<GameObject> Target;

    public int Model { get; private set; }
    public Vector3 Skin { get; private set; }
    public Vector3 HairColor { get; private set; }
    public string Hair { get; private set; } = "";
    public string Beard { get; private set; } = "";
    private string _eyes = "";
    private string _underwearLook = "";

    private readonly List<Part> _parts = new List<Part>();
    private readonly List<Choice> _choices = new List<Choice>();
    private readonly List<ItemDrop> _hairs = new List<ItemDrop>();
    private readonly List<ItemDrop> _beards = new List<ItemDrop>();

    private Button _male;
    private Button _female;
    private SliderRow _skinTone;
    private SliderRow _eyeHue;
    private SliderRow _eyeSaturation;
    private SliderRow _eyeBrightness;
    private SliderRow _eyeGlow;
    private SliderRow _irisSize;
    private Toggle _hideIris;
    private Image _eyeSwatch;
    private readonly SliderRow[] _underwear = new SliderRow[3];
    private Image _underwearSwatch;
    private Image _hairSwatch;
    private Button _hairReset;
    private RawImage _hairShades;
    private RectTransform _shadeMarker;
    private Image _shadeMarkerFill;
    private RectTransform _hueMarker;
    private Texture2D _shadeTexture;
    private Texture2D _hueTexture;
    private float _shadesHue = -1f;
    private float _hairHue;
    private float _hairSaturation;
    private float _hairValue;
    private int _laidOutModel = -1;

    private static string T(string key) => Translations.T("vanitywardrobe_ap_" + key);

    private static Player Me => Player.m_localPlayer;

    // ---- building ---------------------------------------------------------------------------------------------------------

    public void Build(Transform parent)
    {
        Panel = (RectTransform)Widgets.Box(parent, "Appearance", Theme.Panel, Theme.PanelOutline, 2f).transform;
        Panel.sizeDelta = new Vector2(Width, 400f);

        Func<bool> einherjar = () => EinherjarLink.Available;
        Title(T("appearance"));

        _eyeSwatch = Swatch(Header("eye_glow", 0f, einherjar));
        string eyes = EinherjarLink.DefaultLook();
        _eyeHue = Slider("hue", false, EinherjarLink.Get(eyes, "EyeHue"), v => EditEye("EyeHue", v), einherjar);
        _eyeSaturation = Slider("saturation", true, EinherjarLink.Get(eyes, "EyeSaturation"), v => EditEye("EyeSaturation", v), einherjar);
        _eyeBrightness = Slider("brightness", false, EinherjarLink.Get(eyes, "EyeValue"), v => EditEye("EyeValue", v), einherjar);
        _eyeGlow = Slider("strength", true, EinherjarLink.Get(eyes, "Glow"), v => EditEye("Glow", v), einherjar);

        Header("iris", SectionGap, einherjar);
        _irisSize = Slider("size", false, EinherjarLink.Get(eyes, "IrisSize"), v => EditEye("IrisSize", v), einherjar);
        _hideIris = ToggleRow("hide_iris", true, einherjar);
        _hideIris.onValueChanged.AddListener(on => EditEye("HideIris", on ? 1f : 0f));

        _underwearSwatch = Swatch(Header("underwear", SectionGap, einherjar));
        string underwear = EinherjarLink.UnderwearDefault();
        string[] parts = { "hue", "saturation", "brightness" };
        for (int i = 0; i < 3; i++)
        {
            int part = i;
            _underwear[i] = Slider(parts[i], i == 1, EinherjarLink.UnderwearPart(underwear, i), v => EditUnderwear(part, v), einherjar);
        }

        Header("character", SectionGap);
        GameObject row = Widgets.Child(Panel, "Character Buttons");
        Add(row, ButtonHeight, 0f);
        _female = HalfButton(row.transform, T("female"), 0f, () => SetBody(FemaleModel));
        _male = HalfButton(row.transform, T("male"), 0.5f, () => SetBody(MaleModel));

        Header("skin", SectionGap);
        _skinTone = Slider("tone", false, 0f, v =>
        {
            Skin = Utils.ColorToVec3(Color.Lerp(s_skin0, s_skin1, v));
            Changed?.Invoke();
        }, null, false);

        Header("hair", SectionGap);
        ChoiceRow(_hairs, () => Hair, name => Hair = name, null);
        Func<bool> male = () => Model == MaleModel;
        Header("beard", SectionGap, male);
        ChoiceRow(_beards, () => Beard, name => Beard = name, male);
        RectTransform hairHeader = Header("hair_color", SectionGap);
        _hairSwatch = Swatch(hairHeader);
        _hairReset = ResetButton(hairHeader, 36f);
        _hairReset.onClick.AddListener(ResetHair);
        BuildHairPicker();
        Image footLine = Widgets.Box(Panel, "FooterLine", Theme.Accent);
        footLine.raycastTarget = false;
        Widgets.Place(footLine.gameObject, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(12f, 43f), new Vector2(-12f, 45f));
        TMP_Text note = Widgets.Label(Panel, Translations.T("vanitywardrobe_not_saved"), 14f, Theme.Muted, TextAlignmentOptions.Center);
        note.enableAutoSizing = true;
        note.fontSizeMin = 10f;
        note.fontSizeMax = 14f;
        Widgets.Place(note.gameObject, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(8f, 8f), new Vector2(-8f, 30f));
        Panel.gameObject.SetActive(false);
    }

    private void Add(GameObject box, float height, float before, Func<bool> shown = null)
    {
        _parts.Add(new Part { Box = box, Height = height, Before = before, Shown = shown });
    }

    private void Title(string text)
    {
        Image bar = Widgets.Box(Panel, "TitleBar", Theme.Header);
        Add(bar.gameObject, TitleHeight, 0f);
        Accent(bar.transform);
        TMP_Text title = Widgets.Label(bar.transform, text, 21f, Theme.TextBright, TextAlignmentOptions.Center, bold: true);
        UiFont.Norse(title);
        Widgets.Fill(title.gameObject, 4f);
    }

    private static void Accent(Transform bar)
    {
        Image accent = Widgets.Box(bar, "Accent", Theme.Accent);
        Widgets.Place(accent.gameObject, Vector2.zero, new Vector2(0f, 1f), Vector2.zero, new Vector2(4f, 0f));
        accent.raycastTarget = false;
    }

    private RectTransform Header(string key, float before, Func<bool> shown = null)
    {
        Image header = Widgets.Box(Panel, key, Theme.Header);
        Add(header.gameObject, HeaderHeight, before, shown);
        Accent(header.transform);
        TMP_Text label = Widgets.Label(header.transform, T(key), 16f, Theme.TextBright, TextAlignmentOptions.MidlineLeft, bold: true);
        UiFont.Norse(label);
        Widgets.Place(label.gameObject, Vector2.zero, Vector2.one, new Vector2(16f, 0f), new Vector2(-8f, 0f));
        return (RectTransform)header.transform;
    }

    private Image Stripe(string key, bool odd, Func<bool> shown)
    {
        Image background = Widgets.Box(Panel, key, odd ? Theme.RowOdd : Theme.RowEven);
        Add(background.gameObject, RowHeight, 0f, shown);
        TMP_Text label = Widgets.Label(background.transform, T(key), 16f, Theme.Text);
        label.enableAutoSizing = true;
        label.fontSizeMin = 12f;
        label.fontSizeMax = 16f;
        Widgets.Place(label.gameObject, Vector2.zero, new Vector2(LabelShare, 1f), new Vector2(12f, 2f), new Vector2(-6f, -2f));
        return background;
    }

    private SliderRow Slider(string key, bool odd, float reset, Action<float> changed, Func<bool> shown, bool resettable = true)
    {
        Image background = Stripe(key, odd, shown);
        Slider slider = Widgets.Slider(background.transform);
        Widgets.Place(slider.gameObject, new Vector2(LabelShare, 0f), Vector2.one, new Vector2(0f, 5f), new Vector2(-(ResetSize + 16f), -5f));
        slider.minValue = 0f;
        slider.maxValue = 1f;
        SliderRow row = new SliderRow { Slider = slider, Default = reset };
        if (resettable)
        {
            row.Reset = ResetButton((RectTransform)background.transform, 8f);
            row.Reset.onClick.AddListener(() => slider.value = reset);
        }
        slider.onValueChanged.AddListener(value =>
        {
            row.Refresh();
            changed(value);
        });
        return row;
    }

    private Toggle ToggleRow(string key, bool odd, Func<bool> shown)
    {
        Image background = Stripe(key, odd, shown);
        Toggle toggle = Widgets.Toggle(background.transform);
        Widgets.Place(toggle.gameObject, new Vector2(LabelShare, 0.5f), new Vector2(LabelShare, 0.5f), new Vector2(0f, -11f), new Vector2(22f, 11f));
        return toggle;
    }

    private static Button ResetButton(RectTransform row, float right)
    {
        Button button = Widgets.IconButton<ResetIcon>(row, 3f);
        Widgets.Place(button.gameObject, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-(ResetSize + right), -ResetSize / 2f), new Vector2(-right, ResetSize / 2f));
        return button;
    }

    private static Image Swatch(RectTransform header)
    {
        Image image = Widgets.Box(header, "Swatch", Color.white, new Color(0f, 0f, 0f, 0.8f), 1.5f);
        Widgets.Place(image.gameObject, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-28f, -8f), new Vector2(-12f, 8f));
        image.raycastTarget = false;
        return image;
    }

    // A name with an arrow each side to step through the choices
    private void ChoiceRow(List<ItemDrop> items, Func<string> current, Action<string> apply, Func<bool> shown)
    {
        Image background = Widgets.Box(Panel, "Choice", Theme.RowEven);
        Add(background.gameObject, RowHeight, 0f, shown);
        RectTransform row = (RectTransform)background.transform;
        Arrow(row, 0f, 8f, -1, items, current, apply);
        Arrow(row, 1f, -8f - ArrowSize, 1, items, current, apply);
        TMP_Text label = Widgets.Label(row, "", 16f, Theme.TextBright, TextAlignmentOptions.Center);
        Widgets.Place(label.gameObject, Vector2.zero, Vector2.one, new Vector2(ArrowSize + 14f, 0f), new Vector2(-(ArrowSize + 14f), 0f));
        _choices.Add(new Choice { Label = label, Items = items, Current = current });
    }

    private void Arrow(RectTransform row, float anchor, float x, int step, List<ItemDrop> items, Func<string> current, Action<string> apply)
    {
        Button button = step < 0 ? Widgets.IconButton<LeftChevronIcon>(row, 6f) : Widgets.IconButton<RightChevronIcon>(row, 6f);
        Widgets.Place(button.gameObject, new Vector2(anchor, 0.5f), new Vector2(anchor, 0.5f), new Vector2(x, -ArrowSize / 2f), new Vector2(x + ArrowSize, ArrowSize / 2f));
        button.onClick.AddListener(() =>
        {
            if (items.Count > 0)
            {
                int index = Mathf.Max(items.FindIndex(item => item.name == current()), 0);
                apply(items[(index + step + items.Count) % items.Count].name);
                Changed?.Invoke();
            }
        });
    }

    private static Button HalfButton(Transform row, string text, float start, UnityEngine.Events.UnityAction clicked)
    {
        Button button = Widgets.Button(row, text, 16f);
        float half = SectionGap / 2f;
        Widgets.Place(button.gameObject, new Vector2(start, 0f), new Vector2(start + 0.5f, 1f), new Vector2(start > 0f ? half : 0f, 0f), new Vector2(start > 0f ? 0f : -half, 0f));
        button.onClick.AddListener(clicked);
        return button;
    }

    private void BuildHairPicker()
    {
        GameObject picker = Widgets.Child(Panel, "Hair Picker");
        Add(picker, PickerHeight, 0f);
        Image shades = Widgets.Box(picker.transform, "Shades", Theme.Field, Theme.FieldOutline);
        Widgets.Place(shades.gameObject, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-(HueWidth + SectionGap), 0f));
        _hairShades = Picture(shades.transform, ShadeTexture(), at => SetHair(_hairHue, at.x, at.y));
        _shadeMarker = (RectTransform)Widgets.Box(_hairShades.transform, "Marker", Color.white, Color.black, 1.5f).transform;
        _shadeMarker.sizeDelta = new Vector2(12f, 12f);
        _shadeMarker.GetComponent<Image>().raycastTarget = false;
        _shadeMarkerFill = Widgets.Box(_shadeMarker, "Fill", Color.white);
        Widgets.Fill(_shadeMarkerFill.gameObject, 2f);
        _shadeMarkerFill.raycastTarget = false;

        Image hues = Widgets.Box(picker.transform, "Hues", Theme.Field, Theme.FieldOutline);
        Widgets.Place(hues.gameObject, new Vector2(1f, 0f), Vector2.one, new Vector2(-HueWidth, 0f), Vector2.zero);
        RawImage huePicture = Picture(hues.transform, HueTexture(), at => SetHair(at.y, _hairSaturation, _hairValue));
        Image marker = Widgets.Box(huePicture.transform, "Marker", Color.white, Color.black, 1.5f);
        Widgets.Place(marker.gameObject, Vector2.zero, new Vector2(1f, 0f), new Vector2(-3f, -2f), new Vector2(3f, 2f));
        marker.raycastTarget = false;
        _hueMarker = (RectTransform)marker.transform;
    }

    private static RawImage Picture(Transform frame, Texture texture, Action<Vector2> onPick)
    {
        GameObject item = Widgets.Child(frame, "Picture");
        Widgets.Fill(item, 1f);
        RawImage image = item.AddComponent<RawImage>();
        image.texture = texture;
        item.AddComponent<PickArea>().OnPick = onPick;
        return image;
    }

    private Texture2D ShadeTexture()
    {
        if (_shadeTexture == null)
        {
            _shadeTexture = new Texture2D(32, 32, TextureFormat.RGBA32, false) { name = "VanityWardrobe hair shades", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
        }
        _shadesHue = _hairHue;
        Color[] pixels = new Color[32 * 32];
        for (int y = 0; y < 32; y++)
        {
            for (int x = 0; x < 32; x++)
            {
                pixels[y * 32 + x] = Color.HSVToRGB(_shadesHue, x / 31f, y / 31f);
            }
        }
        _shadeTexture.SetPixels(pixels);
        _shadeTexture.Apply(false);
        return _shadeTexture;
    }

    private Texture2D HueTexture()
    {
        if (_hueTexture == null)
        {
            _hueTexture = new Texture2D(1, 64, TextureFormat.RGBA32, false) { name = "VanityWardrobe hair hues", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            for (int y = 0; y < 64; y++)
            {
                _hueTexture.SetPixel(0, y, Color.HSVToRGB(y / 63f, 1f, 1f));
            }
            _hueTexture.Apply(false);
        }
        return _hueTexture;
    }

    public void Dispose()
    {
        if (_shadeTexture != null)
        {
            UnityEngine.Object.Destroy(_shadeTexture);
        }
        if (_hueTexture != null)
        {
            UnityEngine.Object.Destroy(_hueTexture);
        }
    }

    // ---- showing the character --------------------------------------------------------------------------------------------

    public void SetActive(bool active)
    {
        if (Panel != null)
        {
            Panel.gameObject.SetActive(active);
        }
    }

    // Reads the character's look into the controls. Called each time the window opens.
    public void Open()
    {
        Player player = Me;
        if (player == null)
        {
            return;
        }
        LoadStyles();
        // Start from the player's own look; from here on only the model's copy changes
        Model = player.GetPlayerModel();
        Skin = player.m_skinColor;
        HairColor = player.m_hairColor;
        Hair = player.GetHair();
        Beard = player.GetBeard();
        _skinTone.Show(NearestTone(s_skin0, s_skin1, Skin));
        if (EinherjarLink.Available)
        {
            _eyes = EinherjarLink.Eyes(player);
            _hideIris.SetIsOnWithoutNotify(EinherjarLink.Get(_eyes, "HideIris") > 0.5f);
            _eyeHue.Show(EinherjarLink.Get(_eyes, "EyeHue"));
            _eyeSaturation.Show(EinherjarLink.Get(_eyes, "EyeSaturation"));
            _eyeBrightness.Show(EinherjarLink.Get(_eyes, "EyeValue"));
            _eyeGlow.Show(EinherjarLink.Get(_eyes, "Glow"));
            _irisSize.Show(EinherjarLink.Get(_eyes, "IrisSize"));
            _underwearLook = EinherjarLink.Underwear(player);
            for (int i = 0; i < 3; i++)
            {
                _underwear[i].Show(EinherjarLink.UnderwearPart(_underwearLook, i));
            }
            ShowEyes();
            ShowUnderwear();
        }
        Color.RGBToHSV(ToColor(HairColor), out _hairHue, out _hairSaturation, out _hairValue);
        ShowHair();
        _laidOutModel = -1;
        Sync();
    }

    // Keeps the names, the highlighted button and the layout in step with the character
    public void Sync()
    {
        Player player = Me;
        if (player == null || Panel == null || !Panel.gameObject.activeSelf)
        {
            return;
        }
        foreach (Choice choice in _choices)
        {
            string current = choice.Current();
            ItemDrop item = choice.Items.Find(i => i.name == current);
            string text = item != null ? Label(item) : "-";
            if (choice.Label.text != text)
            {
                choice.Label.text = text;
            }
        }
        int model = Model;
        if (model != _laidOutModel)
        {
            _laidOutModel = model;
            Highlight(_male, model == MaleModel);
            Highlight(_female, model == FemaleModel);
            Layout();
        }
    }

    private void Layout()
    {
        float y = Pad;
        foreach (Part part in _parts)
        {
            bool shown = part.Shown == null || part.Shown();
            part.Box.SetActive(shown);
            if (shown)
            {
                y += part.Before;
                Widgets.Place(part.Box, new Vector2(0f, 1f), Vector2.one, new Vector2(Pad, -(y + part.Height)), new Vector2(-Pad, -y));
                y += part.Height + RowGap;
            }
        }
        Panel.sizeDelta = new Vector2(Width, Mathf.Max(y - RowGap + Pad, MinHeight));
    }

    private static void Highlight(Button button, bool selected)
    {
        button.colors = Theme.Colors(selected ? Theme.Selected : Theme.Button, Theme.ButtonHover, Theme.ButtonPressed, Theme.ButtonDisabled);
        Widgets.Snap(button);
        button.GetComponentInChildren<TMP_Text>().color = selected ? Theme.TextBright : Theme.Text;
    }

    private void LoadStyles()
    {
        if (ObjectDB.instance == null)
        {
            return;
        }
        _hairs.Clear();
        _beards.Clear();
        FillStyles(_hairs, "Hair", s_noHair);
        FillStyles(_beards, "Beard", s_noBeard);
    }

    private static void FillStyles(List<ItemDrop> list, string prefix, string none)
    {
        list.AddRange(ObjectDB.instance.GetAllItems(ItemDrop.ItemData.ItemType.Customization, prefix));
        list.RemoveAll(item => item.name.Contains("_"));
        list.Sort((a, b) => string.Compare(Label(a), Label(b), StringComparison.CurrentCulture));
        int empty = list.FindIndex(item => item.name == none);
        if (empty > 0)
        {
            ItemDrop item = list[empty];
            list.RemoveAt(empty);
            list.Insert(0, item);
        }
    }

    private static string Label(ItemDrop item) => Localization.instance.Localize(item.m_itemData.m_shared.m_name);

    // The slider position whose skin colour is nearest the character's
    private static float NearestTone(Color from, Color to, Vector3 target)
    {
        float best = 0f;
        float bestError = float.MaxValue;
        for (int i = 0; i <= FitSteps; i++)
        {
            float t = i / (float)FitSteps;
            float error = (Utils.ColorToVec3(Color.Lerp(from, to, t)) - target).sqrMagnitude;
            if (error < bestError)
            {
                bestError = error;
                best = t;
            }
        }
        return best;
    }

    private static Color ToColor(Vector3 value) => new Color(Mathf.Clamp01(value.x), Mathf.Clamp01(value.y), Mathf.Clamp01(value.z));

    private static Vector3 DefaultHair => Utils.ColorToVec3(Color.Lerp(s_hair0, s_hair1, 0.5f));

    // ---- changing it ------------------------------------------------------------------------------------------------------

    private void SetBody(int model)
    {
        Model = model;
        if (model == FemaleModel)
        {
            Beard = s_noBeard;
        }
        Changed?.Invoke();
    }

    private void EditEye(string field, float value)
    {
        _eyes = EinherjarLink.EditEye(_eyes, field, value);
        EinherjarLink.ShowEyes(Target?.Invoke(), _eyes);
        ShowEyes();
    }

    private void ShowEyes()
    {
        if (EinherjarLink.Available)
        {
            _eyeSwatch.color = Color.HSVToRGB(EinherjarLink.Get(_eyes, "EyeHue"), EinherjarLink.Get(_eyes, "EyeSaturation"), EinherjarLink.Get(_eyes, "EyeValue"));
        }
    }

    private void EditUnderwear(int part, float value)
    {
        _underwearLook = EinherjarLink.EditUnderwear(_underwearLook, part, value);
        EinherjarLink.ShowUnderwear(Target?.Invoke(), _underwearLook);
        ShowUnderwear();
    }

    private void ShowUnderwear()
    {
        if (EinherjarLink.Available)
        {
            _underwearSwatch.color = Color.HSVToRGB(EinherjarLink.UnderwearPart(_underwearLook, 0), EinherjarLink.UnderwearPart(_underwearLook, 1), EinherjarLink.UnderwearPart(_underwearLook, 2));
        }
    }

    private void SetHair(float hue, float saturation, float value)
    {
        _hairHue = hue;
        _hairSaturation = saturation;
        _hairValue = value;
        HairColor = Utils.ColorToVec3(Color.HSVToRGB(hue, saturation, value));
        ShowHair();
        Changed?.Invoke();
    }

    private void ResetHair()
    {
        Color.RGBToHSV(ToColor(DefaultHair), out float hue, out float saturation, out float value);
        SetHair(hue, saturation, value);
    }

    private void ShowHair()
    {
        Color colour = Color.HSVToRGB(_hairHue, _hairSaturation, _hairValue);
        _hairSwatch.color = colour;
        _shadeMarkerFill.color = colour;
        if (!Mathf.Approximately(_shadesHue, _hairHue))
        {
            _hairShades.texture = ShadeTexture();
        }
        _shadeMarker.anchorMin = _shadeMarker.anchorMax = new Vector2(_hairSaturation, _hairValue);
        _shadeMarker.anchoredPosition = Vector2.zero;
        _hueMarker.anchorMin = new Vector2(0f, _hairHue);
        _hueMarker.anchorMax = new Vector2(1f, _hairHue);
        _hairReset.gameObject.SetActive((Utils.ColorToVec3(colour) - DefaultHair).sqrMagnitude > 0.0001f);
    }
}
