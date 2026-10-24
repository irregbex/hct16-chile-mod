HCT 1.6 - a Chilean fan mod for Horizon Chase Turbo
====================================================
Version 2.2.4. A free, non-commercial fan mod for Horizon Chase Turbo (PC, Steam). It runs on top of the original game
WITHOUT changing it: it is loaded by BepInEx and removed with one click.

This repository contains NO file of the game. You need your own copy of Horizon Chase Turbo, bought on Steam.
Horizon Chase Turbo belongs to Aquiris Game Studio; this mod is not affiliated with or endorsed by Aquiris.

*** FULLY VIBECODED - WE NEED YOUR HELP IN REFINING IT ***
  This mod is FULLY VIBECODED: all of its code, and its tracks, models, textures and sounds, were made by an AI
  (Claude, by Anthropic) from one player's directions in plain language and from open map data (see CREDITS.txt).
  Nobody wrote or reviewed the code by hand. It works and has been played a lot, but expect rough edges: bugs, odd physics, scenery out of place,
  translations and balance that need work.
  We need your help in refining it. Play it and report what is wrong (GitHub Issues), suggest improvements, or send
  fixes and new tracks as pull requests. Every report counts.



1. WHAT IT ADDS
---------------
  New campaigns (in the game's Campaigns menu; campaign and track names are Chilean Spanish place names):
    Chile                    127 races through Chilean cities, the comunas of Santiago, observatories (among them a
                             long lap round and INSIDE the Extremely Large Telescope under construction), ski
                             centres, volcanoes, Easter Island, Antarctic bases, the beaches of the Valparaíso region
                             (on jet skis), the procession of andas round the Plaza de Illapel and a jet ski race
                             through a Santiago flooded by a thunderstorm. A map of Chile to pick the region and the place.
    Rutas de Chile           point-to-point stages along real roads (Ruta 68, Ruta 47 Los Vilos - Illapel on a horse
                             cart, the coast of the Valparaíso region, Villarrica - Pucón, Coyhaique - Aysén...), with
                             traffic, toll plazas and roads that narrow and widen.
    Aventura Chile           challenges with the mod's own vehicles.
    Competencia Chile 2026   40 races in 8 cups, from easy to hard, and a bonus race.
    Grand Prix 2026          a fictional 2026 season with the game's formula cars (fictional teams and drivers).
                             Needs Horizon Chase Turbo's "Senna Forever" DLC (without it the campaign does not show).
  Vehicles of its own: a Chilean taxi, the yellow "micro" bus (8 routes), horse carts (11), jet skis (8) and
  procession andas carried by eight bearers (6 images, each with its own stats).
  Also: Chilean speech bubbles with voices for several vehicles, horns of their own (neighs, a whistle), weather and
  time of day per track, nights with lights and fireworks, music per race (bring your own songs, see 4).

  The tracks were built from open data: OpenStreetMap streets and buildings, SRTM terrain and Sentinel-2 imagery
  (see CREDITS.txt).


2. INSTALL (Windows)
--------------------
  You need Horizon Chase Turbo on Steam, installed and started at least once.

  The easy way - the ready-made installer:
  1. On this repository's page, open Releases (right-hand column) and download HCT16-<version>-Installer.zip.
  2. Unzip it anywhere, close the game and double-click INSTALL.cmd.
  3. Start Horizon Chase Turbo from Steam as usual. The title screen shows "HCT 1.6 ... MOD ACTIVE".
  To remove it: UNINSTALL.cmd from the same folder.

  Building it yourself from the source - also needs the .NET 10 SDK or newer
  (https://dotnet.microsoft.com/download - the "SDK", not just the "Runtime"):
  1. Download this repository (Code > Download ZIP, or git clone) and unzip it.
  2. Double-click Prepare-References.cmd
       It finds your game in Steam and copies into Source\game-managed the DLLs the mod is compiled against.
       If it cannot find it:  Prepare-References.cmd -GameDir "C:\...\steamapps\common\Horizon Chase"
       (those files stay on your computer only: .gitignore keeps them out of the repository).
  3. Double-click Build-Installer.cmd
       It builds HCT15.dll, downloads BepInEx 5.4.23.5 (the first time) and puts together the Installer\ folder.
  4. Close the game and double-click Installer\INSTALL.cmd
       It copies BepInEx, the plugin and the content into the game folder. No game file is changed.
  5. Start Horizon Chase Turbo from Steam as usual. The title screen shows "HCT 1.6 ... MOD ACTIVE".

  Uninstall: Installer\UNINSTALL.cmd (the game is left as it was). Your game saves are not touched: the mod keeps
  its own apart.


3. HOW TO PLAY
--------------
  Main menu > Campaigns: the mod's campaigns appear next to the game's.
  Map of Chile: it opens when you enter the Chile campaign. Arrows or stick to move, LB / RB change region,
  Enter / A picks the place and races, Esc / B goes back, M / Y opens and closes the map.
  Car selection: left / right changes car, up / down changes colour or variant (each anda variant has its own speed,
  acceleration, handling and fuel).
  During a race:
    Right Shift / controller Select    horn (a neigh on the carts, a whistle on the andas)
    F11                               next song (F12: your music / the game's)
    F9                                track list per campaign (to race any of them)
    F8                                the mod's console
    Jet ski in the air: E / Enter / RB barrel roll, Q / numpad 0 / LB spin (they score points)
  Settings: HCT15Content\hct15.settings.json in the game folder (player names, how often the bubbles show, voice
  volume, scenery density...). Edit it with Notepad while the game is closed.


4. YOUR MUSIC
-------------
  The repository carries no songs. Put yours (.ogg or .wav) in Content\HCT15Music\Race\ (races) and
  Content\HCT15Music\Menu\ (menus) BEFORE step 3, or later straight into <game folder>\HCT15Music\.
  Details in Content\HCT15Music\README.txt.


5. DEVELOPMENT
--------------
  docs\banner.png           the project's banner (rendered from the mod's own models)
  Source\src\               the plugin (C#, BepInEx 5 + HarmonyX, the game's .NET 3.5 through game-bcl)
  Source\package\           the players' installer (INSTALL.cmd / UNINSTALL.cmd / install.ps1)
  Content\HCT15Content\     all the content: campaigns\ (a campaign.json per campaign), tracks\ (one folder per track:
                            race.json, scenery.json, weather, sky, ground), cars\ (streetcars.json = the mod's own
                            vehicles, taunts.<car>.txt = speech bubbles), assets\models\ (the mod's OBJ models),
                            taunts_sfx\ (voices)
  Build and install into a copy of the game for tests:
    powershell -File Source\Install-Plugin.ps1 -GameDir "<a copy of the game>" -SyncContent
  The mod neither carries nor redistributes code or assets of the game: it compiles against your own DLLs (step 2)
  and refers to game objects by name at run time only.
  The track READMEs mention Tools\*.py: the Python tools that generated the content from open data are not part of
  this repository; the content they produced is.


6. LEGAL AND CREDITS
--------------------
  A free, non-commercial fan mod. Horizon Chase and Horizon Chase Turbo are trademarks of Aquiris Game Studio.
  Credits for data, photos, tools and voices: CREDITS.txt.
  The mod's code is under the MIT licence (LICENSE.txt). The content carries the licences of its data (CREDITS.txt):
  OpenStreetMap = ODbL 1.0, Sentinel-2 = CC BY-NC-SA 4.0 (which is why the mod is NON-COMMERCIAL). No third-party
  photos are included (CREDITS.txt, "Photographs").
