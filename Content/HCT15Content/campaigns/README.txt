HCT 1.6 - campaigns
===================
Every folder here with a campaign.json is a campaign: its own blade in the Campaigns menu, its own
save slot, its own rounds. Edit campaign.json with any text editor; the game reads it at start-up.

Keys in campaign.json
  "Name", "NameLangs"/"NameTexts"              campaign name (+ translations)
  "ModeName", "ModeNameLangs"/"ModeNameTexts"  the mode shown on the race panel
  "Formula1": true/false                       Grand Prix 2026 grid, drivers' championship, Grand Prix opponents
  "FreePlay": true/false                       every round open from the start (no "finish the
                                               previous round in the top 3"). Chile: true, for testing.
  "Pits": true/false                           false = no pit garages and no pit stops on this campaign's
                                               tracks (the tracks keep their pit lane definition for the
                                               editor); all four shipped campaigns say false (aventura_chile
                                               too, since 2026-09-24)
  "DefaultRace": "<race id>"                   the round the campaign screen opens on. That round is
                                               ALWAYS playable, whatever the progress - the quick way to
                                               test one track. Empty = round 1.
  "CarIds": [...]                              optional: restrict the cars (empty = all)
  "Order": 30                                  place among the campaign blades, lower first (chile 10,
                                               aventura_chile 20, top_gear 30; unset: f1_2026 leads, others 50)
  "MenuMusic": "Menu/TopGear/01_title_theme.wav"  the menu track that starts the moment the campaign
                                               screen opens (a file under HCT15Music); the rotation follows it
  "NowPlaying": false                          no "now playing" card with the track name inside this
                                               campaign (top_gear; default true)
  "LoadingLogoScale": 1.5                      the loading screen's logo this much bigger, about its centre
                                               (top_gear 1.5; default 1); a logo that would leave the top
                                               of the screen is slid down just enough to stay on it. Every
                                               UI override is undone when the campaign is left.
  assets\ui\<sprite>.png                       replaces that UI image on the skinned screens (campaign
                                               selection, campaign, car selection, mode selection, LOADING);
                                               originals\INDEX.txt lists the names. top_gear: english.png
                                               (loading-screen logo) + a transparent english_turbo.png
  "DonorDecor": 0.0                            how much of the donor stage's own decor stays along the lap
                                               (1 = the settings' density; top_gear 0: only its pixel sprites)
  "Grandstands": 0                             grandstands on the start straight / bends (-1 = settings; top_gear 0)
  "PitOverhead": true                          the pit stop seen from above (top_gear)
  "PitCrew": false                             no 3-D mechanics at the stop (top_gear: the 16-bit pit had none)
  "BubbleScale": 2.0                           speech bubbles this much bigger in this campaign (top_gear: 2,
                                               its 224-line world made them unreadable; default 1)
  "Blade", "Logo"                              images (1024x1024)
  "Rounds": [{ "RaceId", "Title", ... }]       the rounds, in order; RaceId = a folder in ..\tracks\

Per-round keys, inside "Rounds"
  "RaceId"                                     a folder under ..\tracks\
  "Title", "TitleLangs"/"TitleTexts"           the round name (+ translations)
  "Subtitle", "Description", "Card"            the race panel
  "Laps"                                       laps for this round
  "Music": "<file under ..\..\HCT15Music>"   optional: this race's own music. The path under
                                               HCT15Music ("Race/Agregados/Song.wav") or just the
                                               file name; case and slashes do not matter, and a name
                                               ending .mp3 finds the .ogg/.wav it was converted to.
                                               The race opens with that track and repeats it to the
                                               finish instead of the rotation; F11 still skips out.
                                               chile\campaign.json round 1 (Illapel - city sights)
                                               plays "Solo el primero sera feliz".
  Challenge rules (2026-09-24, the retail Adventures' knobs; aventura_chile uses them):
  "Cars": ["cl_taxi"]                          only these cars can be picked for this round; when every car
                                               on the list is one of the mod's own, they are the rival grid
                                               too (Aventura Chile's all-micro opener: ["cl_micro"], and the
                                               rivals draw its liveries at random as their colours)
  "NoRivals": true                             no opponents - a time trial
  "NoFuelPickups" / "NoNitroPickups": true     no fuel cans / no nitro pickups on the road
  "Fuel": 45                                   starting fuel in tank units (0 = full tank)
  "Nitros": 0                                  starting nitros (-1 = the game's count)
  "NoHud": true                                hide the HUD
  "Difficulty": 1..3                           rival strength (0 = the campaign's own ramp)

Campaigns shipped: chile (73 city laps, FreePlay), f1_2026 (the 2026 grid), top_gear (the SNES cars as
sprites) and aventura_chile (50 challenges in 10 chapters on the Chile laps, top-3 to advance, no pits;
built by Tools\aventura_chile_build.py - see its README.txt and ..\AVENTURA-CHILE-PROPUESTA.txt for the
version with new tracks that is planned). A lap can belong to several campaigns: the one the player is
inside decides its pits, drivers, taunts and rules.

Current default tracks (picked at random on 2026-09-05 - change them here whenever you like)
  f1_2026\campaign.json   "DefaultRace": "f1_2026_r02_shanghai"    (round 2, Chinese Grand Prix)
  chile\campaign.json     "DefaultRace": "cl_alto_hospicio"        (round 16, Alto Hospicio)
The race ids are the "RaceId" values in the same file's "Rounds" list (also the folder names under
..\tracks\). The build scripts (Tools\f1_2026_build.py, Tools\chile_build.py) keep DefaultRace and
FreePlay when they regenerate campaign.json.

Other files per campaign
  taunts.txt                 speech-bubble lines; taunts.<lang>.txt (es, pt, fr, de, it) is used
                             while the game runs in that language (Grand Prix 2026 ships all six; Chile is
                             Chilean Spanish in every language on purpose - add taunts.en.txt to change that)
  drivers.txt                names for the rival drivers on the results board and podium (Chile:
                             48 Chilean names, written on first run; edit freely)
  assets\ui\                 screen images (background.png, sprite overrides), originals\ = dumps
