[size=6][b]Vanity Wardrobe[/b][/size]

An in-game wardrobe and fitting room. Press [b]F9[/b] to browse every armor set, cape, weapon and shield in your game, including everything your other mods add. Try them on a model of your own character and see exactly what it takes to craft them.

[size=5][b]Donations[/b][/size]

If you enjoy using this Valheim mod, please consider making a small donation to help support its development and future updates.
Even £1 or $1 would be greatly appreciated and would help more than you might think!
Thank you for your support, and happy gaming! ...... (A Paypal account is NOT required!)
[url=https://www.paypal.com/donate/?hosted_button_id=BN8HEWHRMAREU][img]https://imgup.uk/i/n7Sg7WK9.png[/img][/url]

[size=5][b]Features[/b][/size]

[list]
[*][b]Everything in your game, automatically.[/b] The list is built from the game's item database after every mod has loaded, so items from other mods show up on their own.
[*][b]Fitting room.[/b] A model with your character's body, skin, hair and beard wears what you pick. Drag it to turn it, scroll over it to zoom. Your real character and inventory are never touched.
[*][b]Sets.[/b] Armor is grouped into sets, by the set name the game gives it or by matching item names, with total armor, weight, movement speed, eitr regeneration and the set bonus.
[*][b]Mix and match.[/b] Wear a helmet from one set, a chest from another, any cape, and a weapon with a shield. Click a slot to take that item off.
[*][b]Outfit totals.[/b] The Outfit tab shows the combined stats, which set bonuses you have, and the materials for the whole look.
[*][b]Saved outfits.[/b] Give an outfit a name, save it, and put it back on later.
[*][b]Appearance preview.[/b] A panel beside the window lets you try a different look on the model: male or female, skin tone, hair, beard and hair colour. With <a href="https://valheim.hexium.gg/mods/MagicMike/EinherjarBodies">Einherjar Bodies</a> installed it also has the eye glow, iris and underwear colour settings. It is only a preview: nothing is saved to your character, and the next time you open the window it starts from your real look again. It works without Einherjar Bodies.
[*][b]Recolor.[/b] Pick one of 11 colours (red, orange, yellow, green, teal, blue, purple, pink, black, white, grey) for an item, a set or a whole outfit. The mod finds the item's main colour and changes only that, so metal, leather and trim keep theirs. The colour shows on the preview and on your own character, and other players who have the mod see it too (it is stored with your character, no server install needed). Players without the mod see the original colours. Items on the ground or on armor stands keep their own colours, and items with unusual shaders may change only partly. Your choices are kept in [b]BepInEx/config/VanityWardrobe/colors.txt[/b].
[*][b]Biome filter.[/b] Every item and set is placed in the biome its materials come from, worked out from the world itself (trees, rocks, creatures, dungeons and recipes), so items from other mods are placed too. Pick a biome, or switch on "and earlier" to see everything up to it. The details show the biome. The first time you load a world it reads the locations in the background for about two minutes and keeps the result in [b]BepInEx/config/VanityWardrobe/biomes.cache[/b].
[*][b]Role and source filters.[/b] Show only Mage, Tank, Damage or Archer gear, worked out from each item's stats, and only items from Valheim or from one particular mod.
[*][b]Weapons and shields.[/b] Filter by weapon type, and see damage, block and what each attack costs.
[*][b]Crafting costs.[/b] The materials to craft an item or a whole set, up to any quality, with a have / need count (your inventory and nearby chests), the station and level for each upgrade, and whether you know the recipe yet.
[*][b]My gear and No armor.[/b] Put on what you are wearing now, or take the armor off.
[*]Uses the same panel style as the Mod Settings window.
[*]Client side: you do not need the server to have it, and it does not change gameplay.
[*][b]Pinned recipes.[/b] Middle-click an item or a set in the list to pin its crafting recipe (at the quality chosen in the window). A panel under the minimap shows the materials with how many you have out of how many you need, counting nearby chests, green when you have enough and red while short. Middle-click again to unpin; with the mouse pointer free (inventory open), left-click a pin to fold its materials, right-click to remove it, and drag the panel to move it (the position is remembered). Pins are saved with the character.
[*][b]Keyboard and mouse only.[/b] There is no game controller support: the window is opened with a key and used with the mouse.
[/list]

[size=5][b]Using it[/b][/size]

[list]
[*][b]Action[/b] (default How): 
[*][b]Open or close[/b] (default [b]F9[/b] (can be changed), or [b]Esc[/b]): 
[*][b]Turn the model and move the camera up or down[/b] (default Drag on it with the left or right mouse button (sideways turns, up and down moves the camera), or use the [b]<[/b] and [b]>[/b] buttons to turn): 
[*][b]Zoom[/b] (default Mouse wheel over the model): 
[*][b]Attack or block[/b] (default Left click or right click the model): 
[*][b]Pin or unpin a recipe[/b] (default Middle-click an item or set in the list): 
[*][b]Wear something[/b] (default Click it in the list. A set puts on all its pieces.): 
[*][b]Take something off[/b] (default Click its slot under the model): 
[*][b]Crafting quality[/b] (default The [b]<[/b] and [b]>[/b] buttons above the details): 
[/list]

[size=5][b]Settings[/b][/size]

Press [b]F7[/b] in game to open the settings panel, or edit [b]BepInEx/config/MagicMike.VanityWardrobe.cfg[/b].
[list]
[*][b]Enable Plugin[/b] (default On): Turns the mod on or off. Needs a restart.
[*][b]Open Key[/b] (default F9): The key that opens and closes the wardrobe.
[*][b]Chest Radius[/b] (default 30): Chests within this many metres count towards the have / need totals. 0 counts only your inventory.
[*][b]Start With My Gear[/b] (default On): The model wears what you are wearing each time the wardrobe opens. If Off, it keeps the last outfit you tried.
[*][b]Show Pinned Recipes[/b] (default On): Show the pinned recipes panel under the minimap.
[*][b]Panel Scale[/b] (default 1): Size of the pinned recipes panel.
[*][b]Max Pins Shown[/b] (default 6): How many pins the panel shows; the rest are counted in a "+N more" line.
[*][b]Vertical Offset[/b] (default 0): Moves the panel down or up from just under the minimap, in pixels. Dragging the panel sets this.
[*][b]Horizontal Offset[/b] (default 0): Moves the panel right or left from the minimap's right edge, in pixels. Dragging the panel sets this.
[*][b]Debug Log[/b] (default Off): If On, the game's log gets lines starting [b][VanityWardrobe][/b] about colours: the ones your game sends, the ones it receives from other players, and each item recoloured. Turn it on when asking about a problem seeing other players' colours.
[/list]

[size=5][b]Translations[/b][/size]

The translation files are in [b]BepInEx/config/VanityWardrobe/Translations/<Language>/VanityWardrobe.json[/b]. Edit them, or add a language, and restart the game. English, Chinese, French, German, Polish, Portuguese (Brazil), Russian, Spanish and Turkish come with the mod. Saved outfits are kept in [b]BepInEx/config/VanityWardrobe/outfits.txt[/b].

[size=5][b]Installation[/b][/size]

### Automatic Install
Plugin is created to be installed with r2modman or Gale.
For Nexus downloads you will need to install manually.
### Manual Install
[list]
[*]Un-zip and drag the [b]VanityWardrobe[/b] folder in your [b]BepInEx/plugins/[/b] folder.
[/list]

[size=5][b]Plugins Created by Magic Mike[/b][/size]

[list]
[*]<a href="https://valheim.hexium.gg/mods/MagicMike/AuroraBorealis">Aurora Borealis</a>
[*]<a href="https://valheim.hexium.gg/mods/MagicMike/BetterBuildMenu">Better Build Menu</a>
[*]<a href="https://valheim.hexium.gg/mods/MagicMike/BetterEquipSlots">Better Equip Slots</a>
[*]<a href="https://valheim.hexium.gg/mods/MagicMike/CorewoodAdditions">Corewood Additions</a>
[*]<a href="https://valheim.hexium.gg/mods/MagicMike/CoreWoodExtras">Corewood Extras</a>
[*]<a href="https://valheim.hexium.gg/mods/MagicMike/CorewoodFortified">Corewood Fortified</a>
[*]<a href="https://valheim.hexium.gg/mods/MagicMike/ChestPreview">Chest Preview</a>
[*]<a href="https://valheim.hexium.gg/mods/MagicMike/DestroyIt">Destroy It</a>
[*]<a href="https://valheim.hexium.gg/mods/MagicMike/EinherjarBodies">Einherjar Bodies</a>
[*]<a href="https://valheim.hexium.gg/mods/MagicMike/ExtraInventorySlots">Extra Inventory Slots</a>
[*]<a href="https://valheim.hexium.gg/mods/MagicMike/MyDirtyHoe">My Dirty Hoe</a>
[*]<a href="https://valheim.hexium.gg/mods/MagicMike/ValheimSkies">Valheim Skies</a>
[*]<a href="https://valheim.hexium.gg/mods/MagicMike/VanityWardrobe">Vanity Wardrobe</a>
[*]<a href="https://valheim.hexium.gg/mods/MagicMike/VillagePlebs">Village Plebs</a>
[*]<a href="https://valheim.hexium.gg/mods/MagicMike/YabbaDabbaDoo">YabbaDabbaDoo</a>
[/list]

[size=5][b]Comments/Suggestions/Help: <a href="https://www.nexusmods.com/valheim/mods/2806">Nexus</a>, <a href="https://discord.gg/mnr36G6J4x">Discord</a>.[/b][/size]

