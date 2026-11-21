using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Jotunn.Managers;
using MagicMike.ModSettings;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VanityWardrobe;

// Turns the fitting-room model with a drag and zooms it with the wheel
internal sealed class PreviewInput : MonoBehaviour, IDragHandler, IScrollHandler, IPointerClickHandler
{
    public Action<float> OnTurn;
    public Action<float> OnZoom;
    public Action OnAttack;
    public Action OnBlock;

    // A left click plays the model's attack and a right click its block, as in the BetterEquipSlots preview
    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.dragging)
        {
            return;
        }
        if (eventData.button == PointerEventData.InputButton.Left)
        {
            OnAttack?.Invoke();
        }
        else if (eventData.button == PointerEventData.InputButton.Right)
        {
            OnBlock?.Invoke();
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        OnTurn?.Invoke(-eventData.delta.x * 0.45f);
    }

    public void OnScroll(PointerEventData eventData)
    {
        // The game reports only 0.05 per wheel notch, so use the direction and a fixed step
        OnZoom?.Invoke(Mathf.Sign(eventData.scrollDelta.y) * (eventData.scrollDelta.y == 0f ? 0f : 0.3f));
    }
}

// Scrolls a list a fixed distance for each notch of the wheel. The game reports a wheel notch as a small and unpredictable amount, so the
// list's own scrolling (which multiplies that amount) is switched off and this is used in its place.
internal sealed class StepScroll : MonoBehaviour, IScrollHandler
{
    public ScrollRect Target;
    public float Step;
    public System.Action Moved;

    public void Init(ScrollRect target, float step, System.Action moved)
    {
        Target = target;
        Step = step;
        Moved = moved;
    }

    public void OnScroll(PointerEventData eventData)
    {
        if (Target == null || eventData.scrollDelta.y == 0f)
        {
            return;
        }
        float room = Mathf.Max(0f, Target.content.rect.height - Target.viewport.rect.height);
        Vector2 position = Target.content.anchoredPosition;
        position.y = Mathf.Clamp(position.y - Mathf.Sign(eventData.scrollDelta.y) * Step, 0f, room);
        Target.content.anchoredPosition = position;
        Moved?.Invoke();
    }
}

// The wardrobe window, drawn in the Mod Settings panel's style: a list on the left, the fitting-room model in the middle
// with what it wears below it, and the details of the chosen item or outfit on the right.
internal sealed class WardrobePanel : MonoBehaviour
{
    private const float MaxWidth = 1320f;
    private const float MaxHeight = 890f;
    private const float AppearanceGap = 6f;   // between the window and the appearance panel beside it
    private const float Margin = 14f;
    private const float RowHeight = 46f;
    private const float RowStride = 48f;   // a row and the gap under it
    private const int PoolSize = 24;           // rows made, enough to fill the list; they are reused as it scrolls
    private const float RefreshInterval = 1f;
    private const float ColumnGap = 10f;

    private static WardrobePanel s_instance;

    public static bool IsOpen { get; private set; }

    private readonly FittingRoom _room = new FittingRoom();
    private readonly AppearancePanel _appearance = new AppearancePanel();
    private readonly Outfit _outfit = new Outfit();

    private RectTransform _panel;
    private TMP_Text _hint;
    private TMP_InputField _search;
    private TMP_Dropdown _weaponTypes;
    private TMP_Dropdown _roles;
    private TMP_Dropdown _sources;
    private TMP_Dropdown _biomes;
    private Button _earlier;
    private bool _andEarlier;
    private readonly List<Button> _swatches = new List<Button>();
    private int _biomeRevision = -1;
    private RectTransform _searchRect;
    private ScrollRect _list;
    private TMP_Text _listNote;
    private readonly List<Button> _categoryButtons = new List<Button>();
    private sealed class RowView
    {
        public GameObject Root;
        public RectTransform Rect;
        public Button Button;
        public TMP_Text Title;
        public TMP_Text Subtitle;
        public Image Icon;
        public object Key;
    }

    private struct ListItem
    {
        public object Key;
        public Sprite Icon;
        public string Title;
        public string Subtitle;
    }

    private readonly List<RowView> _pool = new List<RowView>();
    private readonly List<ListItem> _items = new List<ListItem>();
    private int _firstShown = -1;
    private readonly Button[] _slotButtons = new Button[Outfit.Slots.Length];
    private Button _itemModeButton;
    private Button _outfitModeButton;
    private TMP_Text _qualityLabel;
    private readonly List<GameObject> _cards = new List<GameObject>();
    private readonly List<TMP_Text> _cardTexts = new List<TMP_Text>();
    private ScrollRect _detailsScroll;
    private RawImage _preview;
    private TMP_InputField _outfitName;
    private TMP_Dropdown _savedOutfits;

    private Category _category = Category.Sets;
    private object _selected;
    private bool _outfitMode;
    private int _quality = 1;
    private float _nextRefresh;
    private string _chestKey = "";

    // ---- opening and closing -------------------------------------------------------------------------------------------

    public static void Tick()
    {
        if (IsOpen)
        {
            s_instance.CheckBiomes();
            s_instance._appearance.Sync();
        }
        ShortcutOf(out bool pressed);
        if (!pressed)
        {
            if (IsOpen && Input.GetKeyDown(KeyCode.Escape))
            {
                s_instance.Close();
            }
            return;
        }
        if (IsOpen)
        {
            s_instance.Close();
        }
        else if (CanOpen())
        {
            if (s_instance == null)
            {
                s_instance = Create();
            }
            s_instance.Open();
        }
    }

    public static void Shutdown()
    {
        if (s_instance != null)
        {
            s_instance.Close();
            s_instance._appearance.Dispose();
            Destroy(s_instance.gameObject);
        }
    }

    private static void ShortcutOf(out bool pressed)
    {
        pressed = MainConfig.openKey != null && MainConfig.openKey.Value.IsDown();
    }

    private static bool CanOpen()
    {
        if (Player.m_localPlayer == null || GUIManager.IsHeadless() || GUIManager.CustomGUIFront == null || SettingsPanel.IsOpen)
        {
            return false;
        }
        if (global::Console.IsVisible() || TextInput.IsVisible() || (Chat.instance != null && Chat.instance.HasFocus()))
        {
            return false;
        }
        GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        TMP_InputField field = selected != null ? selected.GetComponent<TMP_InputField>() : null;
        return field == null || !field.isFocused;
    }

    private static WardrobePanel Create()
    {
        Transform canvas = GUIManager.CustomGUIFront.transform;
        Image overlay = Widgets.Box(canvas, "VanityWardrobe", Theme.Dim);
        Widgets.Fill(overlay.gameObject);
        WardrobePanel panel = overlay.gameObject.AddComponent<WardrobePanel>();
        Vector2 size = ((RectTransform)canvas).rect.size;
        panel.Build(Mathf.Min(MaxWidth, size.x * 0.94f - AppearancePanel.Width - AppearanceGap), Mathf.Min(MaxHeight, size.y * 0.92f));
        overlay.gameObject.SetActive(false);
        return panel;
    }

    private void Open()
    {
        if (IsOpen)
        {
            return;
        }
        Catalog.EnsureBuilt();
        Canvas.ForceUpdateCanvases();
        Rect frame = _preview.rectTransform.rect;
        _room.Open(frame.height > 1f ? frame.width / frame.height : 0.73f);
        if (!_room.IsOpen)
        {
            return;
        }
        IsOpen = true;
        gameObject.SetActive(true);
        transform.SetAsLastSibling();
        GUIManager.BlockInput(true);

        if (MainConfig.startWithMyGear.Value || _outfit.Pieces.Count() == 0)
        {
            CopyOutfit(FittingRoom.MyGear());
        }
        _hint.text = Translations.T("vanitywardrobe_close_hint", MainConfig.openKey.Value.ToString());
        _preview.texture = _room.Texture;
        RefreshSaved();
        _appearance.SetActive(true);
        _appearance.Open();
        FillSources();
        FillBiomes();
        _biomeRevision = Biomes.Revision;
        RefreshList();
        ApplyOutfit();
        RefreshChests();
        RefreshDetails();
    }

    private void Close()
    {
        if (!IsOpen)
        {
            return;
        }
        IsOpen = false;
        GUIManager.BlockInput(false);
        _room.Close();
        _preview.texture = null;
        gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (IsOpen)
        {
            IsOpen = false;
            GUIManager.BlockInput(false);
        }
        _room.Close();
        if (s_instance == this)
        {
            s_instance = null;
        }
    }

    private void Update()
    {
        if (!IsOpen)
        {
            return;
        }
        if (!_room.IsOpen || Player.m_localPlayer == null)
        {
            Close();
            return;
        }
        _room.Update();
        if (Time.unscaledTime >= _nextRefresh)
        {
            _nextRefresh = Time.unscaledTime + RefreshInterval;
            RefreshChests();
            RefreshDetails();
        }
    }

    // ---- building the window -------------------------------------------------------------------------------------------

    private void Build(float width, float height)
    {
        Image panelImage = Widgets.Box(transform, "Panel", Theme.Panel, Theme.PanelOutline, 2f);
        RectTransform panelRect = (RectTransform)panelImage.transform;
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(width, height);
        panelRect.anchoredPosition = new Vector2(-(AppearancePanel.Width + AppearanceGap) / 2f, 0f);
        _panel = panelRect;
        Transform panel = panelImage.transform;
        DragMove drag = panelImage.gameObject.AddComponent<DragMove>();
        drag.Target = panelRect;

        Image titleBar = Widgets.Box(panel, "TitleBar", Theme.Header);
        Widgets.Place(titleBar.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(12f, -66f), new Vector2(-12f, -12f));
        Image titleAccent = Widgets.Box(titleBar.transform, "Accent", Theme.Accent);
        Widgets.Place(titleAccent.gameObject, new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(4f, 0f));
        Image hanger = Widgets.Box(titleBar.transform, "Hanger", Color.white);
        hanger.sprite = UiFont.Hanger();
        hanger.enabled = hanger.sprite != null;
        hanger.preserveAspect = true;
        hanger.raycastTarget = false;
        Widgets.Place(hanger.gameObject, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(18f, -17f), new Vector2(64f, 17f));
        TMP_Text title = Widgets.Label(titleBar.transform, Translations.T("vanitywardrobe_title"), 38f, Theme.TextBright, TextAlignmentOptions.MidlineLeft, bold: true);
        UiFont.Norse(title);
        Widgets.Place(title.gameObject, Vector2.zero, Vector2.one, new Vector2(76f, 0f), new Vector2(-440f, 0f));
        BuildColors(titleBar.transform);

        _hint = Widgets.Label(panel, "", 14f, Theme.Muted, TextAlignmentOptions.Center);
        _hint.enableAutoSizing = true;
        _hint.fontSizeMin = 10f;
        _hint.fontSizeMax = 14f;
        Widgets.Place(_hint.gameObject, new Vector2(0.30f, 0f), new Vector2(0.68f, 0f), new Vector2(0f, 8f), new Vector2(0f, 30f));
        TMP_Text pinHint = Widgets.Label(panel, Translations.T("vanitywardrobe_pin_hint"), 14f, Theme.Muted, TextAlignmentOptions.Center);
        pinHint.enableAutoSizing = true;
        pinHint.fontSizeMin = 10f;
        pinHint.fontSizeMax = 14f;
        Widgets.Place(pinHint.gameObject, new Vector2(0f, 0f), new Vector2(0.30f, 0f), new Vector2(Margin, 8f), new Vector2(0f, 30f));

        RectTransform left = Column(panel, "List", 0f, 0.30f);
        RectTransform middle = Column(panel, "Room", 0.30f, 0.68f);
        RectTransform right = Column(panel, "Details", 0.68f, 1f);
        BuildList(left);
        BuildRoom(middle);
        BuildDetails(right);

        // The appearance panel hangs off the window's top right corner, so it moves with it
        _appearance.Build(panel);
        _appearance.Panel.anchorMin = _appearance.Panel.anchorMax = Vector2.one;
        _appearance.Panel.pivot = new Vector2(0f, 1f);
        _appearance.Panel.anchoredPosition = new Vector2(AppearanceGap, 0f);
        _appearance.Changed = () => _room.ShowLook(_appearance.Model, _appearance.Skin, _appearance.HairColor, _appearance.Hair, _appearance.Beard);
        _appearance.Target = () => _room.Model;
        _appearance.MinHeight = height;
    }

    private static RectTransform Column(Transform panel, string name, float from, float to)
    {
        RectTransform column = (RectTransform)Widgets.Child(panel, name).transform;
        Widgets.Place(column.gameObject, new Vector2(from, 0f), new Vector2(to, 1f),
            new Vector2(from == 0f ? Margin : ColumnGap / 2f, 36f), new Vector2(to == 1f ? -Margin : -ColumnGap / 2f, -76f));
        return column;
    }

    private static RectTransform Strip(Transform parent, float fromX, float toX, float top, float height)
    {
        GameObject go = Widgets.Child(parent, "Strip");
        Widgets.Place(go, new Vector2(fromX, 1f), new Vector2(toX, 1f), new Vector2(fromX == 0f ? 0f : 2f, -(top + height)), new Vector2(toX == 1f ? 0f : -2f, -top));
        return (RectTransform)go.transform;
    }

    private static RectTransform BottomStrip(Transform parent, float fromX, float toX, float bottom, float height)
    {
        GameObject go = Widgets.Child(parent, "Strip");
        Widgets.Place(go, new Vector2(fromX, 0f), new Vector2(toX, 0f), new Vector2(fromX == 0f ? 0f : 2f, bottom), new Vector2(toX == 1f ? 0f : -2f, bottom + height));
        return (RectTransform)go.transform;
    }

    private void BuildList(RectTransform column)
    {
        Category[][] rows =
        {
            new[] { Category.Sets, Category.Helmets, Category.Chests, Category.Legs },
            new[] { Category.Capes, Category.Weapons, Category.Shields },
        };
        for (int row = 0; row < rows.Length; row++)
        {
            for (int i = 0; i < rows[row].Length; i++)
            {
                Category category = rows[row][i];
                Button button = Widgets.Button(Strip(column, (float)i / rows[row].Length, (float)(i + 1) / rows[row].Length, row * 34f, 30f), CategoryName(category), 15f);
                Widgets.Fill(button.gameObject);
                button.onClick.AddListener(() => SelectCategory(category));
                _categoryButtons.Add(button);
            }
        }

        _search = Widgets.Input(Strip(column, 0f, 1f, 72f, 30f), 16f, Translations.T("vanitywardrobe_search"));
        _searchRect = (RectTransform)_search.transform;
        Widgets.Fill(_search.gameObject);
        ((RectTransform)_search.textViewport).offsetMax = new Vector2(-40f, -2f);
        _search.onValueChanged.AddListener(_ => RefreshList());
        Button clear = Widgets.IconButton<CloseIcon>(_search.transform, 9f);
        Widgets.Place(clear.gameObject, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-32f, -13f), new Vector2(-4f, 13f));
        clear.onClick.AddListener(() => _search.text = "");

        _weaponTypes = Widgets.Dropdown(Strip(column, 0.58f, 1f, 72f, 30f), 14f);
        Widgets.Fill(_weaponTypes.gameObject);
        _weaponTypes.onValueChanged.AddListener(_ => RefreshList());
        _weaponTypes.gameObject.SetActive(false);

        // Role and source filters, under the search
        _roles = Widgets.Dropdown(Strip(column, 0f, 0.4f, 106f, 30f), 14f);
        Widgets.Fill(_roles.gameObject);
        _roles.options = new[] { "vanitywardrobe_all_roles", "vanitywardrobe_role_mage", "vanitywardrobe_role_tank", "vanitywardrobe_role_damage", "vanitywardrobe_role_archer" }
            .Select(key => new TMP_Dropdown.OptionData(Translations.T(key))).ToList();
        _roles.onValueChanged.AddListener(_ => RefreshList());
        _sources = Widgets.Dropdown(Strip(column, 0.4f, 1f, 106f, 30f), 14f);
        Widgets.Fill(_sources.gameObject);
        _sources.onValueChanged.AddListener(_ => RefreshList());

        // Biome filter, with a switch to include the biomes before it
        _biomes = Widgets.Dropdown(Strip(column, 0f, 0.66f, 140f, 30f), 14f);
        Widgets.Fill(_biomes.gameObject);
        _biomes.onValueChanged.AddListener(_ => RefreshList());
        _earlier = Widgets.Button(Strip(column, 0.66f, 1f, 140f, 30f), Translations.T("vanitywardrobe_and_earlier"), 13f);
        Widgets.Fill(_earlier.gameObject);
        _earlier.onClick.AddListener(() =>
        {
            _andEarlier = !_andEarlier;
            UpdateEarlierButton();
            RefreshList();
        });
        UpdateEarlierButton();

        _list = Widgets.ScrollView(column);
        Widgets.Place(_list.gameObject, Vector2.zero, Vector2.one, new Vector2(0f, 24f), new Vector2(0f, -176f));
        // The rows are placed here, not by the layout, so that only the ones in view exist
        _list.content.GetComponent<VerticalLayoutGroup>().enabled = false;
        _list.content.GetComponent<ContentSizeFitter>().enabled = false;
        _list.onValueChanged.AddListener(_ => ShowRows());
        _list.viewport.pivot = new Vector2(0f, 0.5f);   // so making room for the scrollbar takes from the right, not both sides
        _list.scrollSensitivity = 0f;
        _list.gameObject.AddComponent<StepScroll>().Init(_list, 3f * RowStride, ShowRows);
        _listNote = Widgets.Label(column, "", 13f, Theme.Muted, TextAlignmentOptions.Center);
        Widgets.Place(_listNote.gameObject, new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 22f));
        UpdateCategoryButtons();
    }

    // A mouse button icon and what it does, along the bottom of the preview
    private static void MakeHint(Transform frame, float across, string spriteName, string key, string fallback)
    {
        GameObject hint = Widgets.Child(frame, "Hint " + spriteName);
        Widgets.Place(hint, new Vector2(across, 0f), new Vector2(across, 0f), new Vector2(-50f, 6f), new Vector2(50f, 28f));
        Sprite sprite = UiFont.GameSprite(spriteName);
        if (sprite != null)
        {
            Image icon = Widgets.Box(hint.transform, "Icon", Color.white);
            icon.sprite = sprite;
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            Widgets.Place(icon.gameObject, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(2f, -10f), new Vector2(22f, 10f));
        }
        string text = Localization.instance.Localize(key);
        TMP_Text label = Widgets.Label(hint.transform, text.StartsWith("[") ? fallback : text, 16f, Theme.TextBright, TextAlignmentOptions.MidlineLeft, bold: true);
        label.raycastTarget = false;
        UiFont.Norse(label);
        Widgets.Place(label.gameObject, Vector2.zero, Vector2.one, new Vector2(28f, 0f), Vector2.zero);
    }

    private void BuildRoom(RectTransform column)
    {
        const float below = 240f;
        Image frame = Widgets.Box(column, "PreviewFrame", Theme.Field, Theme.FieldOutline, 2f);
        Widgets.Place(frame.gameObject, Vector2.zero, Vector2.one, new Vector2(0f, below), Vector2.zero);
        GameObject view = Widgets.Child(frame.transform, "Preview");
        Widgets.Fill(view, 2f);
        _preview = view.AddComponent<RawImage>();
        _preview.color = Color.white;
        PreviewInput input = view.AddComponent<PreviewInput>();
        input.OnTurn = degrees => _room.Rotate(degrees);
        input.OnZoom = amount => _room.Zoom(amount);
        input.OnAttack = () => _room.Attack();
        input.OnBlock = () => _room.Block();
        MakeHint(frame.transform, 0.39f, "mouse1_icon", "$settings_attack", "Attack");
        MakeHint(frame.transform, 0.61f, "mouse2_icon", "$settings_block", "Block");

        // Turn, my gear, no armor, clear
        RectTransform controls = BottomStrip(column, 0f, 1f, 202f, 32f);
        (string text, float from, float to, Action action)[] buttons =
        {
            ("<", 0f, 0.1f, () => _room.Rotate(-30f)),
            (">", 0.1f, 0.2f, () => _room.Rotate(30f)),
            (Translations.T("vanitywardrobe_my_gear"), 0.2f, 0.47f, () => { CopyOutfit(FittingRoom.MyGear()); AfterOutfitChange(); }),
            (Translations.T("vanitywardrobe_no_armor"), 0.47f, 0.74f, () => { ClearArmor(); AfterOutfitChange(); }),
            (Translations.T("vanitywardrobe_clear_all"), 0.74f, 1f, () => { _outfit.Clear(); AfterOutfitChange(); }),
        };
        foreach ((string text, float from, float to, Action action) in buttons)
        {
            Button button = Widgets.Button(controls, text, 15f);
            Widgets.Place(button.gameObject, new Vector2(from, 0f), new Vector2(to, 1f), new Vector2(2f, 0f), new Vector2(-2f, 0f));
            button.onClick.AddListener(() => action());
        }

        // What it wears: one button per slot, a click takes the item off
        for (int i = 0; i < Outfit.Slots.Length; i++)
        {
            Slot slot = Outfit.Slots[i];
            RectTransform cell = BottomStrip(column, (i % 3) / 3f, (i % 3 + 1) / 3f, i < 3 ? 156f : 112f, 42f);
            Button button = Widgets.Button(cell, "", 13f);
            Widgets.Fill(button.gameObject);
            TMP_Text label = button.GetComponentInChildren<TMP_Text>();
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Ellipsis;
            button.onClick.AddListener(() => { _outfit[slot] = null; AfterOutfitChange(); });
            _slotButtons[i] = button;
        }

        // Saving and loading outfits
        _outfitName = Widgets.Input(BottomStrip(column, 0f, 0.6f, 66f, 32f), 15f, Translations.T("vanitywardrobe_outfit_name"));
        Widgets.Fill(_outfitName.gameObject);
        Button save = Widgets.Button(BottomStrip(column, 0.6f, 1f, 66f, 32f), Translations.T("vanitywardrobe_save"), 15f);
        Widgets.Fill(save.gameObject);
        save.onClick.AddListener(SaveOutfit);

        _savedOutfits = Widgets.Dropdown(BottomStrip(column, 0f, 0.6f, 24f, 32f), 14f);
        Widgets.Fill(_savedOutfits.gameObject);
        Button load = Widgets.Button(BottomStrip(column, 0.6f, 0.8f, 24f, 32f), Translations.T("vanitywardrobe_load"), 15f);
        Widgets.Fill(load.gameObject);
        load.onClick.AddListener(LoadOutfit);
        Button delete = Widgets.Button(BottomStrip(column, 0.8f, 1f, 24f, 32f), Translations.T("vanitywardrobe_delete"), 15f);
        Widgets.Fill(delete.gameObject);
        delete.onClick.AddListener(DeleteOutfit);
    }

    // The colours, in the title bar: the original, then the presets. They apply to what the details show: the item, the set, or the whole outfit.
    private void BuildColors(Transform titleBar)
    {
        const float swatchWidth = 34f;
        int count = Colors.Presets.Length + 1;
        GameObject bar = Widgets.Child(titleBar, "Colors");
        Widgets.Place(bar, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-(12f + count * swatchWidth), 8f), new Vector2(-12f, -8f));
        for (int i = 0; i < count; i++)
        {
            int preset = i;
            Button swatch = Widgets.Button(bar.transform, preset == 0 ? "x" : "", 13f);
            Widgets.Place(swatch.gameObject, new Vector2((float)i / count, 0f), new Vector2((float)(i + 1) / count, 1f), new Vector2(1f, 0f), new Vector2(-1f, 0f));
            if (preset > 0)
            {
                Color color = Colors.Presets[preset - 1].Swatch;
                swatch.colors = Theme.Colors(color, Color.Lerp(color, Color.white, 0.25f), Color.Lerp(color, Color.black, 0.25f), color);
                Widgets.Snap(swatch);
            }
            swatch.onClick.AddListener(() => ChooseColor(preset));
            _swatches.Add(swatch);
        }
    }

    private void BuildDetails(RectTransform column)
    {
        _itemModeButton = Widgets.Button(Strip(column, 0f, 0.5f, 0f, 30f), Translations.T("vanitywardrobe_mode_item"), 15f);
        Widgets.Fill(_itemModeButton.gameObject);
        _itemModeButton.onClick.AddListener(() => SetMode(false));
        _outfitModeButton = Widgets.Button(Strip(column, 0.5f, 1f, 0f, 30f), Translations.T("vanitywardrobe_mode_outfit"), 15f);
        Widgets.Fill(_outfitModeButton.gameObject);
        _outfitModeButton.onClick.AddListener(() => SetMode(true));

        // Quality: the crafting costs are totalled up to the quality chosen here
        RectTransform quality = Strip(column, 0f, 1f, 36f, 30f);
        _qualityLabel = Widgets.Label(quality, "", 16f, Theme.Text);
        Widgets.Place(_qualityLabel.gameObject, Vector2.zero, new Vector2(1f, 1f), Vector2.zero, new Vector2(-76f, 0f));
        Button minus = Widgets.Button(quality, "<", 16f);
        Widgets.Place(minus.gameObject, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-70f, 0f), new Vector2(-38f, 0f));
        minus.onClick.AddListener(() => ChangeQuality(-1));
        Button plus = Widgets.Button(quality, ">", 16f);
        Widgets.Place(plus.gameObject, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-34f, 0f), Vector2.zero);
        plus.onClick.AddListener(() => ChangeQuality(1));

        _detailsScroll = Widgets.ScrollView(column);
        _detailsScroll.viewport.pivot = new Vector2(0f, 0.5f);
        _detailsScroll.scrollSensitivity = 0f;
        _detailsScroll.gameObject.AddComponent<StepScroll>().Init(_detailsScroll, 90f, null);
        Widgets.Place(_detailsScroll.gameObject, Vector2.zero, Vector2.one, new Vector2(0f, 24f), new Vector2(0f, -72f));   // (its bottom lines up with the list and the room)
        // The details are a stack of cards, one for each section
        VerticalLayoutGroup stack = _detailsScroll.content.GetComponent<VerticalLayoutGroup>();
        stack.spacing = 8f;
        stack.padding = new RectOffset(0, 0, 2, 6);
        UpdateModeButtons();
    }

    // ---- list ----------------------------------------------------------------------------------------------------------

    private static string CategoryName(Category category)
    {
        return Translations.T("vanitywardrobe_tab_" + category.ToString().ToLowerInvariant());
    }

    private void SelectCategory(Category category)
    {
        _category = category;
        UpdateCategoryButtons();
        _weaponTypes.gameObject.SetActive(category == Category.Weapons);
        Widgets.Place(_search.gameObject, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        _searchRect.parent.GetComponent<RectTransform>().anchorMax = new Vector2(category == Category.Weapons ? 0.56f : 1f, 1f);
        if (category == Category.Weapons)
        {
            FillWeaponTypes();
        }
        RefreshList();
    }

    private void UpdateEarlierButton()
    {
        _earlier.colors = Theme.Colors(_andEarlier ? Theme.Selected : Theme.Button, Theme.ButtonHover, Theme.ButtonPressed, Theme.ButtonDisabled);
        Widgets.Snap(_earlier);
    }

    // The biomes in the order a player reaches them, and "Unknown" for what could not be placed. Made again each time the window opens
    // because the names follow the game's language.
    private void FillBiomes()
    {
        int chosen = _biomes.options.Count > 0 ? _biomes.value : 0;
        List<TMP_Dropdown.OptionData> options = new List<TMP_Dropdown.OptionData> { new TMP_Dropdown.OptionData(Translations.T("vanitywardrobe_all_biomes")) };
        options.AddRange(Enumerable.Range(0, Biomes.Order.Length).Select(i => new TMP_Dropdown.OptionData(Biomes.Name(i))));
        options.Add(new TMP_Dropdown.OptionData(Translations.T("vanitywardrobe_biome_unknown")));
        _biomes.onValueChanged.RemoveAllListeners();
        _biomes.options = options;
        _biomes.value = Mathf.Clamp(chosen, 0, options.Count - 1);
        _biomes.RefreshShownValue();
        _biomes.onValueChanged.AddListener(_ => RefreshList());
        ((TMP_Text)_earlier.GetComponentInChildren<TMP_Text>()).text = Translations.T("vanitywardrobe_and_earlier");
    }

    // The tiers become fuller as the world's locations are read in the background, so the list is drawn again when they do
    private void CheckBiomes()
    {
        if (_biomeRevision != Biomes.Revision)
        {
            _biomeRevision = Biomes.Revision;
            RefreshList();
            RefreshDetails();
        }
    }

    // The sources can change as mods load, so the list is made again each time the window opens, keeping the choice if it is still there
    private void FillSources()
    {
        string chosen = _sources.options.Count > 0 && _sources.value > 0 ? _sources.options[_sources.value].text : null;
        List<TMP_Dropdown.OptionData> options = new List<TMP_Dropdown.OptionData> { new TMP_Dropdown.OptionData(Translations.T("vanitywardrobe_all_sources")) };
        options.AddRange(Catalog.Sources.Select(n => new TMP_Dropdown.OptionData(n)));
        _sources.onValueChanged.RemoveAllListeners();
        _sources.options = options;
        _sources.value = Mathf.Max(0, options.FindIndex(o => o.text == chosen));
        _sources.RefreshShownValue();
        _sources.onValueChanged.AddListener(_ => RefreshList());
    }

    private Role RoleFilter()
    {
        switch (_roles.value)
        {
            case 1: return Role.Mage;
            case 2: return Role.Tank;
            case 3: return Role.Damage;
            case 4: return Role.Archer;
            default: return Role.None;
        }
    }

    private void FillWeaponTypes()
    {
        List<TMP_Dropdown.OptionData> options = new List<TMP_Dropdown.OptionData> { new TMP_Dropdown.OptionData(Translations.T("vanitywardrobe_all_types")) };
        options.AddRange(Catalog.WeaponSkills.Select(s => new TMP_Dropdown.OptionData(Localization.instance.Localize("$skill_" + s.ToString().ToLowerInvariant()))));
        _weaponTypes.onValueChanged.RemoveAllListeners();
        _weaponTypes.options = options;
        _weaponTypes.value = 0;
        _weaponTypes.RefreshShownValue();
        _weaponTypes.onValueChanged.AddListener(_ => RefreshList());
    }

    private void UpdateCategoryButtons()
    {
        Category[] order = { Category.Sets, Category.Helmets, Category.Chests, Category.Legs, Category.Capes, Category.Weapons, Category.Shields };
        for (int i = 0; i < _categoryButtons.Count; i++)
        {
            Color normal = order[i] == _category ? Theme.Selected : Theme.Button;
            _categoryButtons[i].colors = Theme.Colors(normal, Theme.ButtonHover, Theme.ButtonPressed, Theme.ButtonDisabled);
            Widgets.Snap(_categoryButtons[i]);
        }
    }

    private void RefreshList()
    {
        _items.Clear();
        string text = _search.text.Trim().ToLowerInvariant();
        Role role = RoleFilter();
        string source = _sources.value > 0 && _sources.value < _sources.options.Count ? _sources.options[_sources.value].text : null;
        int biome = _biomes.value;   // 0 all, 1..N the biomes in order, N+1 unknown
        bool Passes(Role roles, string from, int tier)
        {
            if (role != Role.None && (roles & role) == 0)
            {
                return false;
            }
            if (source != null && from != source)
            {
                return false;
            }
            if (biome == 0)
            {
                return true;
            }
            if (biome > Biomes.Order.Length)
            {
                return tier < 0;
            }
            return tier >= 0 && (_andEarlier ? tier <= biome - 1 : tier == biome - 1);
        }
        if (_category == Category.Sets)
        {
            foreach (SetEntry set in Catalog.Sets.Where(s => (text.Length == 0 || s.SearchText.Contains(text)) && Passes(s.Roles, s.Source, s.Tier)))
            {
                _items.Add(new ListItem { Key = set, Icon = set.Icon, Title = set.Name, Subtitle = set.Pieces.Count + " " + Translations.T("vanitywardrobe_pieces") });
            }
        }
        else
        {
            Skills.SkillType? skill = null;
            if (_category == Category.Weapons && _weaponTypes.value > 0 && _weaponTypes.value - 1 < Catalog.WeaponSkills.Count)
            {
                skill = Catalog.WeaponSkills[_weaponTypes.value - 1];
            }
            foreach (Entry entry in Catalog.Items.Where(i => Catalog.CategoryOf(i) == _category && (text.Length == 0 || i.SearchText.Contains(text)) && (skill == null || i.Shared.m_skillType == skill.Value) && Passes(i.Roles, i.Source, i.Tier)))
            {
                _items.Add(new ListItem { Key = entry, Icon = entry.Icon, Title = entry.Name, Subtitle = Subtitle(entry) });
            }
        }
        _listNote.text = biome > 0 && !Biomes.Complete ? Translations.T("vanitywardrobe_biomes_pending")
            : _items.Count == 0 ? Translations.T("vanitywardrobe_no_results") : "";
        _list.content.sizeDelta = new Vector2(0f, _items.Count * RowStride + 4f);
        _list.verticalNormalizedPosition = 1f;
        _firstShown = -1;
        ShowRows();
    }

    private static string Subtitle(Entry entry)
    {
        if (entry.Slot == Slot.Weapon)
        {
            return entry.TypeName;
        }
        return string.IsNullOrEmpty(entry.Shared.m_setName) ? "" : Translations.T("vanitywardrobe_set_name", entry.Shared.m_setName);
    }

    private RowView MakeRow()
    {
        GameObject root = Widgets.Child(_list.content, "Row");
        RowView view = new RowView { Root = root, Rect = (RectTransform)root.transform };
        view.Button = Widgets.Button(root.transform, "", 16f);
        Widgets.Fill(view.Button.gameObject);
        view.Title = view.Button.GetComponentInChildren<TMP_Text>();
        view.Title.alignment = TextAlignmentOptions.TopLeft;
        Widgets.Place(view.Title.gameObject, Vector2.zero, Vector2.one, new Vector2(52f, 0f), new Vector2(-6f, -4f));
        view.Subtitle = Widgets.Label(view.Button.transform, "", 12f, Theme.Muted, TextAlignmentOptions.BottomLeft);
        Widgets.Place(view.Subtitle.gameObject, Vector2.zero, Vector2.one, new Vector2(52f, 4f), new Vector2(-6f, 0f));
        view.Icon = Widgets.Box(view.Button.transform, "Icon", Color.white);
        view.Icon.preserveAspect = true;
        view.Icon.raycastTarget = false;
        Widgets.Place(view.Icon.gameObject, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(6f, -18f), new Vector2(42f, 18f));
        view.Button.onClick.AddListener(() => Choose(view.Key));
        view.Button.gameObject.AddComponent<MiddleClick>().Action = () => PinRow(view.Key);
        return view;
    }

    // Middle click on an item or set pins its recipe under the minimap (at the quality chosen here), or unpins it if it is pinned
    private void PinRow(object key)
    {
        string title;
        List<string> prefabs;
        if (key is SetEntry set)
        {
            title = set.Name;
            prefabs = set.Pieces.Select(p => p.Prefab).ToList();
        }
        else if (key is Entry entry)
        {
            title = entry.Name;
            prefabs = new List<string> { entry.Prefab };
        }
        else
        {
            return;
        }
        bool was = Pins.IsPinned(prefabs);
        Pins.Toggle(title, prefabs, _quality);
        Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft, Translations.T(was ? "vanitywardrobe_pin_removed" : "vanitywardrobe_pin_added", title));
    }

    // Puts the rows that are in view on the list. A few rows are made once and reused as the list scrolls, so a list of any length
    // costs the same.
    private void ShowRows()
    {
        int first = Mathf.Max(0, Mathf.FloorToInt(_list.content.anchoredPosition.y / RowStride) - 1);
        if (first == _firstShown)
        {
            return;
        }
        _firstShown = first;
        while (_pool.Count < PoolSize)
        {
            _pool.Add(MakeRow());
        }
        for (int i = 0; i < PoolSize; i++)
        {
            RowView view = _pool[i];
            int index = first + i;
            if (index >= _items.Count)
            {
                view.Key = null;
                view.Root.SetActive(false);
                continue;
            }
            ListItem item = _items[index];
            view.Key = item.Key;
            view.Title.text = item.Title;
            view.Subtitle.text = item.Subtitle;
            view.Icon.sprite = item.Icon;
            float top = 2f + index * RowStride;
            view.Rect.anchorMin = new Vector2(0f, 1f);
            view.Rect.anchorMax = new Vector2(1f, 1f);
            view.Rect.pivot = new Vector2(0.5f, 1f);
            view.Rect.offsetMin = new Vector2(2f, -(top + RowHeight));
            view.Rect.offsetMax = new Vector2(-2f, -top);
            view.Root.SetActive(true);
            ColourRow(view);
        }
    }

    private void ColourRow(RowView view)
    {
        Color normal = view.Key != null && view.Key == _selected ? Theme.Selected : Theme.Button;
        view.Button.colors = Theme.Colors(normal, Theme.ButtonHover, Theme.ButtonPressed, Theme.ButtonDisabled);
        Widgets.Snap(view.Button);
    }

    private void ColourRow(object key)
    {
        foreach (RowView view in _pool)
        {
            if (view.Key != null && view.Key == key)
            {
                ColourRow(view);
            }
        }
    }

    // Choosing something from the list puts it on the model at once: a whole set, or a single item in its own slot
    private void Choose(object key)
    {
        object previous = _selected;
        _selected = key;
        if (previous != null)
        {
            ColourRow(previous);
        }
        ColourRow(key);
        if (key is SetEntry set)
        {
            foreach (Slot armor in new[] { Slot.Helmet, Slot.Chest, Slot.Legs })
            {
                _outfit[armor] = null;
            }
            foreach (Entry piece in set.Pieces)
            {
                _outfit.Wear(piece);
            }
        }
        else if (key is Entry entry)
        {
            _outfit.Wear(entry);
        }
        _quality = Mathf.Clamp(_quality, 1, MaxQuality());
        _outfitMode = false;
        UpdateModeButtons();
        AfterOutfitChange();
    }

    // ---- outfit --------------------------------------------------------------------------------------------------------

    private void CopyOutfit(Outfit from)
    {
        Array.Copy(from.Worn, _outfit.Worn, _outfit.Worn.Length);
    }

    private void ClearArmor()
    {
        foreach (Slot slot in new[] { Slot.Helmet, Slot.Chest, Slot.Legs, Slot.Cape })
        {
            _outfit[slot] = null;
        }
    }

    private void AfterOutfitChange()
    {
        ApplyOutfit();
        RefreshDetails();
    }

    private void ApplyOutfit()
    {
        _room.Apply(_outfit);
        for (int i = 0; i < Outfit.Slots.Length; i++)
        {
            Entry worn = _outfit.Worn[i];
            string slot = Translations.T("vanitywardrobe_slot_" + Outfit.Slots[i].ToString().ToLowerInvariant());
            TMP_Text label = _slotButtons[i].GetComponentInChildren<TMP_Text>();
            label.text = "<size=11><color=" + Theme.Tag(Theme.Muted) + ">" + slot + "</color></size>\n" + (worn != null ? worn.Name + "  <color=" + Theme.Tag(Theme.Accent) + ">x</color>" : "<color=" + Theme.Tag(Theme.Muted) + ">-</color>");
        }
    }

    private void RefreshSaved()
    {
        List<string> names = SavedOutfits.Names();
        _savedOutfits.options = names.Select(n => new TMP_Dropdown.OptionData(n)).ToList();
        if (_savedOutfits.value >= names.Count)
        {
            _savedOutfits.value = 0;
        }
        _savedOutfits.RefreshShownValue();
    }

    private void SaveOutfit()
    {
        string name = _outfitName.text.Trim();
        if (name.Length == 0)
        {
            return;
        }
        SavedOutfits.Save(name, _outfit);
        RefreshSaved();
        int index = SavedOutfits.Names().IndexOf(name.Replace("|", " "));
        if (index >= 0)
        {
            _savedOutfits.value = index;
            _savedOutfits.RefreshShownValue();
        }
    }

    private void LoadOutfit()
    {
        if (_savedOutfits.options.Count == 0)
        {
            return;
        }
        if (SavedOutfits.Load(_savedOutfits.options[_savedOutfits.value].text, _outfit))
        {
            AfterOutfitChange();
        }
    }

    private void DeleteOutfit()
    {
        if (_savedOutfits.options.Count == 0)
        {
            return;
        }
        SavedOutfits.Delete(_savedOutfits.options[_savedOutfits.value].text);
        RefreshSaved();
    }

    // ---- details -------------------------------------------------------------------------------------------------------

    private void SetMode(bool outfit)
    {
        _outfitMode = outfit;
        UpdateModeButtons();
        RefreshDetails();
    }

    private void UpdateModeButtons()
    {
        _itemModeButton.colors = Theme.Colors(_outfitMode ? Theme.Button : Theme.Selected, Theme.ButtonHover, Theme.ButtonPressed, Theme.ButtonDisabled);
        _outfitModeButton.colors = Theme.Colors(_outfitMode ? Theme.Selected : Theme.Button, Theme.ButtonHover, Theme.ButtonPressed, Theme.ButtonDisabled);
        Widgets.Snap(_itemModeButton);
        Widgets.Snap(_outfitModeButton);
    }

    private IEnumerable<Entry> Shown()
    {
        if (_outfitMode || _selected == null)
        {
            return _outfit.Pieces;
        }
        if (_selected is SetEntry set)
        {
            return set.Pieces;
        }
        return new[] { (Entry)_selected };
    }

    private int MaxQuality()
    {
        List<Entry> shown = Shown().ToList();
        return shown.Count == 0 ? 4 : Mathf.Max(1, shown.Max(e => e.Shared.m_maxQuality));
    }

    private void ChangeQuality(int step)
    {
        _quality = Mathf.Clamp(_quality + step, 1, MaxQuality());
        RefreshDetails();
    }

    // The inventories whose items count as "have": the player's, and every chest within reach
    private void RefreshChests() => Stock.Refresh();

    private void RefreshDetails()
    {
        if (_detailsScroll == null)
        {
            return;
        }
        _quality = Mathf.Clamp(_quality, 1, MaxQuality());
        _qualityLabel.text = Translations.T("vanitywardrobe_quality", _quality.ToString());
        StringBuilder text = new StringBuilder();
        List<Entry> pieces = Shown().ToList();
        if (_outfitMode || _selected == null)
        {
            AppendOutfit(text, pieces);
        }
        else if (_selected is SetEntry set)
        {
            AppendSet(text, set, pieces);
        }
        else
        {
            AppendItem(text, (Entry)_selected);
        }
        ShowCards(text.ToString());
        UpdateSwatches();
    }

    // A heading starts a new card: the details text is cut at these marks
    private const char SectionBreak = '\u0001';

    private static string Heading(string text)
    {
        return SectionBreak + "<size=18><b><color=" + Theme.Tag(Theme.TextBright) + ">" + text + "</color></b></size>\n";
    }

    private static readonly Color CardColor = new Color(0f, 0f, 0f, 0.4f);

    // Shows the details as one card for each section, with room inside each so the text does not touch its edge
    private void ShowCards(string all)
    {
        string[] sections = all.Split(SectionBreak).Select(section => section.Trim('\n', ' ')).Where(section => section.Length > 0).ToArray();
        while (_cards.Count < sections.Length)
        {
            Image card = Widgets.Box(_detailsScroll.content, "Card", CardColor);
            card.sprite = PinPanel.RoundedSprite();
            card.type = Image.Type.Sliced;
            VerticalLayoutGroup inside = card.gameObject.AddComponent<VerticalLayoutGroup>();
            inside.padding = new RectOffset(16, 16, 12, 14);
            inside.childControlWidth = true;
            inside.childControlHeight = true;
            inside.childForceExpandWidth = true;
            inside.childForceExpandHeight = false;
            TMP_Text text = Widgets.Label(card.transform, "", 15f, Theme.Text, TextAlignmentOptions.TopLeft);
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            _cards.Add(card.gameObject);
            _cardTexts.Add(text);
        }
        for (int i = 0; i < _cards.Count; i++)
        {
            bool used = i < sections.Length;
            _cards[i].SetActive(used);
            if (used)
            {
                _cardTexts[i].text = sections[i];
            }
        }
    }

    // The items the colour buttons act on: the one chosen, the pieces of the set chosen, or everything on the model
    private List<Entry> ColorTargets()
    {
        if (_outfitMode || _selected == null)
        {
            return Shown().ToList();
        }
        return _selected is SetEntry set ? set.Pieces : new List<Entry> { (Entry)_selected };
    }

    private void ChooseColor(int preset)
    {
        foreach (Entry entry in ColorTargets())
        {
            Colors.Set(entry.Prefab, preset);
        }
        RefreshDetails();
    }

    // Marks the colour button for the colour all the targets share, if they share one
    private void UpdateSwatches()
    {
        List<int> chosen = ColorTargets().Select(e => Colors.Get(e.Prefab)).Distinct().ToList();
        int common = chosen.Count == 1 ? chosen[0] : -1;
        for (int i = 0; i < _swatches.Count; i++)
        {
            Outline line = _swatches[i].GetComponent<Outline>();
            if (line != null)
            {
                line.effectColor = i == common ? Color.white : Theme.ButtonOutline;
                line.effectDistance = i == common ? new Vector2(2f, -2f) : new Vector2(1f, -1f);
            }
        }
    }

    private string ColorLine(IEnumerable<Entry> entries)
    {
        List<int> chosen = entries.Select(e => Colors.Get(e.Prefab)).Distinct().ToList();
        return chosen.Count == 1 && chosen[0] > 0 ? Line(Translations.T("vanitywardrobe_recolor"), Colors.Name(chosen[0])) : "";
    }

    // Which biome the materials come from, as far as the world tells
    private static string BiomeLine(int tier)
    {
        return Line(Translations.T("vanitywardrobe_biome"), tier >= 0 ? Biomes.Name(tier) : Translations.T("vanitywardrobe_biome_unknown"));
    }

    private static string Line(string name, string value)
    {
        return "<color=" + Theme.Tag(Theme.Muted) + ">" + name + "</color>  " + value + "\n";
    }

    private float Armor(Entry entry)
    {
        int quality = Mathf.Min(_quality, entry.Shared.m_maxQuality);
        return entry.Shared.m_armor + entry.Shared.m_armorPerLevel * (quality - 1);
    }

    private void AppendItem(StringBuilder text, Entry entry)
    {
        ItemDrop.ItemData.SharedData shared = entry.Shared;
        int quality = Mathf.Min(_quality, shared.m_maxQuality);
        text.Append("<size=22><b>").Append(entry.Name).Append("</b></size>\n");
        text.Append(BiomeLine(entry.Tier));
        text.Append(ColorLine(new[] { entry }));
        if (!string.IsNullOrEmpty(shared.m_setName))
        {
            text.Append(Line(Translations.T("vanitywardrobe_set"), shared.m_setName));
        }
        text.Append(Heading(Translations.T("vanitywardrobe_stats")));
        AppendStats(text, entry, quality);
        string description = Localization.instance.Localize(shared.m_description);
        if (!string.IsNullOrEmpty(description))
        {
            text.Append("\n<i>").Append(description).Append("</i>\n");
        }
        AppendSetBonus(text, shared);
        text.Append(Heading(Translations.T("vanitywardrobe_crafting")));
        AppendCosts(text, new List<Entry> { entry }, perPiece: false);
    }

    private void AppendSet(StringBuilder text, SetEntry set, List<Entry> pieces)
    {
        text.Append("<size=22><b>").Append(set.Name).Append("</b></size>\n");
        text.Append(BiomeLine(set.Tier));
        text.Append(ColorLine(set.Pieces));
        text.Append(Heading(Translations.T("vanitywardrobe_pieces_title")));
        foreach (Entry piece in pieces)
        {
            text.Append(Line(Translations.T("vanitywardrobe_slot_" + piece.Slot.ToString().ToLowerInvariant()), piece.Name));
        }
        text.Append(Heading(Translations.T("vanitywardrobe_stats")));
        AppendTotals(text, pieces);
        AppendSetBonus(text, pieces[0].Shared);
        text.Append(Heading(Translations.T("vanitywardrobe_crafting")));
        AppendCosts(text, pieces, perPiece: true);
    }

    private void AppendOutfit(StringBuilder text, List<Entry> pieces)
    {
        text.Append("<size=22><b>").Append(Translations.T("vanitywardrobe_mode_outfit")).Append("</b></size>\n");
        if (pieces.Count == 0)
        {
            text.Append("\n<color=").Append(Theme.Tag(Theme.Muted)).Append(">").Append(Translations.T("vanitywardrobe_outfit_empty")).Append("</color>\n");
            return;
        }
        text.Append(BiomeLine(pieces.Select(p => p.Tier).DefaultIfEmpty(-1).Max()));
        text.Append(ColorLine(pieces));
        text.Append(Heading(Translations.T("vanitywardrobe_pieces_title")));
        foreach (Entry piece in pieces)
        {
            text.Append(Line(Translations.T("vanitywardrobe_slot_" + piece.Slot.ToString().ToLowerInvariant()), piece.Name));
        }
        text.Append(Heading(Translations.T("vanitywardrobe_stats")));
        AppendTotals(text, pieces);
        foreach (IGrouping<string, Entry> set in pieces.Where(p => !string.IsNullOrEmpty(p.Shared.m_setName)).GroupBy(p => p.Shared.m_setName))
        {
            Entry first = set.First();
            bool complete = first.Shared.m_setSize > 0 && set.Count() >= first.Shared.m_setSize;
            text.Append(Line(Translations.T("vanitywardrobe_set"), set.Key + " " + set.Count() + "/" + first.Shared.m_setSize + (complete ? " <color=" + Theme.Tag(Theme.Accent) + ">+</color>" : "")));
        }
        text.Append(Heading(Translations.T("vanitywardrobe_crafting")));
        AppendCosts(text, pieces, perPiece: true);
    }

    private void AppendStats(StringBuilder text, Entry entry, int quality)
    {
        ItemDrop.ItemData.SharedData shared = entry.Shared;
        if (entry.Slot <= Slot.Cape)
        {
            text.Append(Line(Translations.T("vanitywardrobe_armor"), Armor(entry).ToString("0.#")));
            AppendModifiers(text, entry.Shared);
        }
        else
        {
            AppendDamage(text, shared, quality);
            if (shared.m_blockPower > 0f)
            {
                text.Append(Line(Translations.T("vanitywardrobe_block"), (shared.m_blockPower + shared.m_blockPowerPerLevel * (quality - 1)).ToString("0.#")));
            }
            AppendCosts(text, shared.m_attack);
        }
        text.Append(Line(Translations.T("vanitywardrobe_weight"), shared.m_weight.ToString("0.#")));
    }

    private static void AppendModifiers(StringBuilder text, ItemDrop.ItemData.SharedData shared)
    {
        if (shared.m_movementModifier != 0f)
        {
            text.Append(Line(Translations.T("vanitywardrobe_movement"), (shared.m_movementModifier * 100f).ToString("+0.#;-0.#") + "%"));
        }
        if (shared.m_eitrRegenModifier != 0f)
        {
            text.Append(Line(Translations.T("vanitywardrobe_eitr"), (shared.m_eitrRegenModifier * 100f).ToString("+0.#;-0.#") + "%"));
        }
    }

    private static void AppendDamage(StringBuilder text, ItemDrop.ItemData.SharedData shared, int quality)
    {
        HitData.DamageTypes damage = shared.m_damages;
        HitData.DamageTypes perLevel = shared.m_damagesPerLevel;
        (string token, float value, float step)[] types =
        {
            ("$inventory_damage", damage.m_damage, perLevel.m_damage),
            ("$inventory_blunt", damage.m_blunt, perLevel.m_blunt),
            ("$inventory_slash", damage.m_slash, perLevel.m_slash),
            ("$inventory_pierce", damage.m_pierce, perLevel.m_pierce),
            ("$inventory_chop", damage.m_chop, perLevel.m_chop),
            ("$inventory_pickaxe", damage.m_pickaxe, perLevel.m_pickaxe),
            ("$inventory_fire", damage.m_fire, perLevel.m_fire),
            ("$inventory_frost", damage.m_frost, perLevel.m_frost),
            ("$inventory_lightning", damage.m_lightning, perLevel.m_lightning),
            ("$inventory_poison", damage.m_poison, perLevel.m_poison),
            ("$inventory_spirit", damage.m_spirit, perLevel.m_spirit),
        };
        foreach ((string token, float value, float step) in types)
        {
            float amount = value + step * (quality - 1);
            if (amount > 0f)
            {
                text.Append(Line(Localization.instance.Localize(token), amount.ToString("0.#")));
            }
        }
    }

    private static void AppendCosts(StringBuilder text, Attack attack)
    {
        if (attack == null)
        {
            return;
        }
        if (attack.m_attackStamina > 0f)
        {
            text.Append(Line(Translations.T("vanitywardrobe_stamina"), attack.m_attackStamina.ToString("0.#")));
        }
        if (attack.m_attackEitr > 0f)
        {
            text.Append(Line(Translations.T("vanitywardrobe_eitr_cost"), attack.m_attackEitr.ToString("0.#")));
        }
        if (attack.m_attackHealth > 0f)
        {
            text.Append(Line(Translations.T("vanitywardrobe_health_cost"), attack.m_attackHealth.ToString("0.#")));
        }
    }

    private void AppendTotals(StringBuilder text, List<Entry> pieces)
    {
        float armor = pieces.Where(p => p.Slot <= Slot.Cape).Sum(Armor);
        float weight = pieces.Sum(p => p.Shared.m_weight);
        float movement = pieces.Sum(p => p.Shared.m_movementModifier);
        float eitr = pieces.Sum(p => p.Shared.m_eitrRegenModifier);
        text.Append(Line(Translations.T("vanitywardrobe_armor"), armor.ToString("0.#")));
        text.Append(Line(Translations.T("vanitywardrobe_weight"), weight.ToString("0.#")));
        if (movement != 0f)
        {
            text.Append(Line(Translations.T("vanitywardrobe_movement"), (movement * 100f).ToString("+0.#;-0.#") + "%"));
        }
        if (eitr != 0f)
        {
            text.Append(Line(Translations.T("vanitywardrobe_eitr"), (eitr * 100f).ToString("+0.#;-0.#") + "%"));
        }
    }

    private static void AppendSetBonus(StringBuilder text, ItemDrop.ItemData.SharedData shared)
    {
        if (shared.m_setStatusEffect == null)
        {
            return;
        }
        text.Append(Heading(Translations.T("vanitywardrobe_set_bonus", shared.m_setSize.ToString())));
        text.Append(Localization.instance.Localize(shared.m_setStatusEffect.m_name)).Append('\n');
        string tooltip = Localization.instance.Localize(shared.m_setStatusEffect.GetTooltipString());
        if (!string.IsNullOrEmpty(tooltip))
        {
            text.Append("<color=").Append(Theme.Tag(Theme.Muted)).Append(">").Append(tooltip).Append("</color>\n");
        }
    }

    // The materials to craft the pieces and upgrade them to the chosen quality, with how many of each you have
    private void AppendCosts(StringBuilder text, List<Entry> pieces, bool perPiece)
    {
        Dictionary<ItemDrop, int> need = new Dictionary<ItemDrop, int>();
        List<string> lines = new List<string>();
        bool anyRecipe = false;
        foreach (Entry piece in pieces)
        {
            Recipe recipe = ObjectDB.instance.GetRecipe(piece.PrefabObject.GetComponent<ItemDrop>().m_itemData);
            if (recipe == null)
            {
                lines.Add("<color=" + Theme.Tag(Theme.Muted) + ">" + piece.Name + ": " + Translations.T("vanitywardrobe_no_recipe") + "</color>");
                continue;
            }
            anyRecipe = true;
            int quality = Mathf.Min(_quality, piece.Shared.m_maxQuality);
            for (int level = 1; level <= quality; level++)
            {
                foreach (Piece.Requirement requirement in recipe.m_resources)
                {
                    if (requirement.m_resItem == null || (requirement.m_upgraderResource && level == 1))
                    {
                        continue;
                    }
                    need.TryGetValue(requirement.m_resItem, out int sofar);
                    need[requirement.m_resItem] = sofar + requirement.GetAmount(level);
                }
            }
            if (perPiece || pieces.Count == 1)
            {
                lines.Add(PieceCostLine(piece, recipe, quality));
            }
        }
        if (anyRecipe)
        {
            text.Append("<b>").Append(Translations.T("vanitywardrobe_materials", _quality.ToString())).Append("</b>\n");
            foreach (KeyValuePair<ItemDrop, int> pair in need.OrderBy(p => p.Key.name))
            {
                int have = Stock.Have(pair.Key);
                string colour = Theme.Tag(have >= pair.Value ? new Color(0.55f, 0.85f, 0.45f, 1f) : new Color(0.95f, 0.4f, 0.35f, 1f));
                text.Append("  ").Append(Localization.instance.Localize(pair.Key.m_itemData.m_shared.m_name)).Append("   <color=").Append(colour).Append(">").Append(have).Append(" / ").Append(pair.Value).Append("</color>\n");
            }
        }
        if (lines.Count > 0)
        {
            text.Append(Heading(Translations.T("vanitywardrobe_per_item")));
            foreach (string line in lines)
            {
                text.Append(line).Append('\n');
            }
        }
    }

    // One piece: its station and whether the recipe is known, then each upgrade step's materials
    private string PieceCostLine(Entry piece, Recipe recipe, int quality)
    {
        StringBuilder line = new StringBuilder();
        line.Append("<b>").Append(piece.Name).Append("</b>");
        Player player = Player.m_localPlayer;
        bool known = player != null && player.IsRecipeKnown(piece.Shared.m_name);
        line.Append("  <color=").Append(Theme.Tag(known ? new Color(0.55f, 0.85f, 0.45f, 1f) : Theme.Muted)).Append(">")
            .Append(Translations.T(known ? "vanitywardrobe_recipe_known" : "vanitywardrobe_recipe_unknown")).Append("</color>\n");
        for (int level = 1; level <= quality; level++)
        {
            string station = recipe.m_craftingStation != null
                ? Localization.instance.Localize(recipe.m_craftingStation.m_name) + " " + Translations.T("vanitywardrobe_station_level", recipe.GetRequiredStationLevel(level).ToString())
                : "";
            string materials = string.Join(", ", recipe.m_resources.Where(r => r.m_resItem != null && r.GetAmount(level) > 0 && !(r.m_upgraderResource && level == 1))
                .Select(r => r.GetAmount(level) + " " + Localization.instance.Localize(r.m_resItem.m_itemData.m_shared.m_name)));
            line.Append("  <color=").Append(Theme.Tag(Theme.Muted)).Append(">").Append(level).Append(".</color> ").Append(station).Append(station.Length > 0 && materials.Length > 0 ? ": " : "").Append(materials).Append('\n');
        }
        return line.ToString().TrimEnd('\n');
    }
}
