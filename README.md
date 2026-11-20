# VanityWardrobe

Valheim mod: an in-game wardrobe and fitting room. **F9** opens a window listing every armor set, cape, weapon and shield in
the item database (so items from other mods too), with a model of the player's own character that wears the chosen item.
Client side only. The window is built from `Shared/ModSettings` (`Theme` and `Widgets`), so it looks like the Mod Settings panel.

## How it works

- `Catalog.cs` builds the item list from `ObjectDB.instance.m_items` when the window opens (again if the item count or language
  changed). Armor is grouped into sets by `m_setName`, or, where there is none, by the words left in the prefab name once piece
  words ("Armor", "Chest", "Legs", "Helmet"...) are taken out.
- `FittingRoom.cs` makes the model the way the game's character-select screen does: it instantiates the `Player` prefab with
  `ZNetView.m_forceDisableInit` set, so it has no network object, far above the world (y = 9000), and removes it from
  `Character.s_characters` and `Player.s_players`. Its own camera draws it into a `RenderTexture` shown by a `RawImage`.
  Items are put on with the `VisEquipment.Set*Item` methods, which work from local fields when there is no ZDO.
- `WardrobePanel.cs` is the window. `Outfit.cs` holds what the model wears and the saved outfits, kept in
  `BepInEx/config/VanityWardrobe/outfits.txt`.
- Crafting costs come from `ObjectDB.GetRecipe` and `Piece.Requirement.GetAmount(level)`, totalled up to the chosen quality.

## Building

1. Clone with the submodule: `git clone --recurse-submodules`.
2. Copy `Environment.props` from another of the mods, or edit the paths in it (it is not in git).
3. Build in Visual Studio: `VanityWardrobe.sln`. A Release build writes the version into `Package/manifest.json`, makes the
   Nexus readme and a Thunderstore zip in `VanityWardrobe/ThunderStore/`.

The mod reads private members of the game (`Character.s_characters`, `Player.s_players`, `Container.m_nview`), so
`Properties/IgnoreAccessModifiers.cs` is kept.
