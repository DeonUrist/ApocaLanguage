# ApocaLanguage

Full translation support for **Apocalypter** (BepInEx 5 plugin). Every text the game puts on screen — menus, HUD, prompts,
item names, creature names, codex, trader prices — is looked up in a translation file of the chosen language and replaced
just before it is drawn. The game's own data (item IDs, FSM variables, saves) is never changed, so saves work in any language.

Ships with **RU**, **DE** and **FR** folders (starter files; the full translations are being written).

## Installation
Copy the `ApocaLanguage` folder into `BepInEx\plugins\`:

```
BepInEx\plugins\ApocaLanguage\
    ApocaLanguage.dll
    RU\RU.json
    DE\DE.json
    FR\FR.json
```

## Choosing a language
- **LANGUAGE** button in the bottom-right corner of the title screen and the ESC menu → click it, pick a language from the list.
- Or in the Apocasetter **Mods** menu (`[General] Language`), or in `BepInEx\config\com.denis.apocalypter.apocalanguage.cfg`.

The change is instant — no restart.

## Translation files
One folder per language (the folder name is the language code: `RU`, `DE`, `FR`, `ES`, `PL`, `PT-BR` ...). Every `*.json` file in
the folder is loaded (alphabetical order), so a big translation can be split into `ui.json`, `items.json`, `codex.json` ... .
Files whose name starts with `_` are tool output and are not loaded. English (`EN`) is the game itself and has no folder.

```json
{
  "version": "1.0.0",
  "language": "RU",
  "Hello": "Здравствуйте",
  "First Aid": "Аптечка",
  "Health + {0}": "Здоровье + {0}",
  "Take Weapon to Slot {0} (F)": "Взять оружие в слот {0} (F)"
}
```

- `version` — version of the translation. `language` — must be the folder name (a mismatch is reported in the log).
- Optional `name` — the language's name in the language list (default: built in, e.g. `Русский`, `Deutsch`, `Français`).
- Every other pair is `"English text": "translation"`. An empty translation (`"Hello": ""`) means *not translated yet*.
- Matching: exact text first; then ignoring spaces around it; then ignoring case (an ALL-CAPS game text gets an ALL-CAPS
  translation); then **numbers as placeholders** — `"Day {0}"` matches `Day 1`, `Day 2` ...; `{0}`, `{1}` can be moved around in
  the translation. Multi-line texts are also matched line by line.
- `\n` = new line. Unity rich-text tags (`<b>`, `<color=red>`) can be used. `//` comments and trailing commas are allowed.
- The files are re-read automatically within 2 seconds when they change, while the game runs.

### Fonts
If the game's font lacks a letter of the translation (Cyrillic, ä, é ...), that text is drawn with a Windows font (Arial).
Put a `.ttf` / `.otf` file into the language folder to use that font for all translated texts of that language instead.

### Pictures with text (tutorial pages, signs)
`<LANG>\Textures\<texture name>.png` replaces the UI picture with that name while the language is active.
Use `ExportUiTextures` (below) to get the originals and their names.

## Tools for translators (`[Translators]` in the config / Mods menu)
| Setting | What it does |
|---|---|
| `CollectStrings` | While on, every English text that appears on screen is added to `_collected.json`; the ones without a translation in the current language go to `<LANG>\_missing.json` (empty values, ready to fill and paste). |
| `DumpAllTexts` | One shot: every text of the loaded game, also hidden menus and prefabs, item names, PlayMaker text-action literals → `_dump.json` (+ `_dump_where.txt`: where each text is used, `<LANG>\_untranslated.json`: what is still missing). Load a save first so the world is loaded. |
| `ExportUiTextures` | One shot: the UI pictures as PNG → `_textures\` (+ `_where_used.txt`). |

## Config
| Section | Key | Default | |
|---|---|---|---|
| General | `Language` | `EN` | current language (`EN` + the installed folders) |
| General | `ShowLanguageButton` | `true` | the button in the title/ESC menu |
| Translators | `CollectStrings` | `false` | see above |
| Translators | `DumpAllTexts` | `false` | see above (switches itself off) |
| Translators | `ExportUiTextures` | `false` | see above (switches itself off) |

## For other mods
`ApocaLanguage.Api.T("English text")` returns the translation in the player's language (or the text itself),
`Api.Language` the current code, `Api.LanguageChanged` fires on a switch. IMGUI windows of other mods are not translated automatically.

## How it works
Harmony prefixes on the text setters of `UnityEngine.UI.Text` and `TMPro.TMP_Text` (all PlayMaker text actions — `UiTextSetText`,
`SetProperty`, `setTextmeshProText` — end there) and postfixes on their `OnEnable` (texts built into scenes and prefabs). Each
component remembers its English original, so switching language re-translates everything on screen. 3D `TextMesh` texts and
anything that slipped through are caught by a refresh pass every 2 s and after each scene load. TextMeshPro gets the fallback
font as a TMP fallback font asset. `RawImage.texture` / `Image.sprite` setters swap the language's replacement pictures.

## Build
`sh build.sh` (mono `mcs`, against the game's own `Apocalypter_Data\Managed` DLLs + `BepInEx\core`; override the paths with
`MANAGED=... BEPCORE=...`), or `dotnet build` with `ApocaLanguage.csproj` (deploys the DLL and the `Languages\` folders to
`BepInEx\plugins\ApocaLanguage`).
