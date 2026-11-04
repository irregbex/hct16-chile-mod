![HCT 1.6 - a Chilean fan mod for Horizon Chase Turbo](https://raw.githubusercontent.com/irregbex/hct16-chile-mod/main/docs/banner.png)

# HCT 1.6 2.3.5 - a Chilean fan mod for Horizon Chase Turbo

> **FULLY VIBECODED - WE NEED YOUR HELP IN REFINING IT.** All of this mod's code and content - music included - were made by an AI (Claude, by Anthropic) from one player's directions in plain language. Nobody wrote or reviewed the code by hand, so expect rough edges. Play it, report bugs in [Issues](https://github.com/irregbex/hct16-chile-mod/issues), suggest improvements or send pull requests.

A free, non-commercial fan mod for Horizon Chase Turbo (PC, Steam). It runs on top of the original game without changing any of its files, and contains no file of the game: you need your own copy, bought on Steam.

## Trailer

[![Watch the trailer on YouTube](https://img.youtube.com/vi/x8EvNXgQcAc/maxresdefault.jpg)](https://youtu.be/x8EvNXgQcAc)

## Install

1. Download **HCT16-2.3.5-Installer.zip** below and unzip it anywhere.
2. Close the game and double-click **INSTALL.cmd** (it finds the game in your Steam libraries).
3. Start Horizon Chase Turbo from Steam. The title screen shows "HCT 1.6 v2.3.5 MOD ACTIVE".

To remove it: **UNINSTALL.cmd**. Installing over 2.2.x or 2.3.x is fine.
Track editor: **HCT16-Track-Editor.zip** below (unchanged since 2.2.4).

## What is in it

- **Five campaigns:**
  - **Chile:** 164 races through Chilean cities, tourist places from Arica to Puerto Williams, observatories, volcanoes, Easter Island, Antarctic bases, beaches on jet skis, the procession round the Plaza de Illapel and a flooded Santiago.
  - **Rutas de Chile:** point-to-point stages on real roads (their traffic and toll plazas are switched off for now).
  - **Aventura Chile** and **Competencia Chile 2026**.
  - **Grand Prix 2026:** a fictional season with the game's formula cars (needs the Senna Forever DLC).
- **The mod's own vehicles:** Chilean taxi, micro bus, horse carts, jet skis and procession andas.
- Chilean voices, weather and time of day per track, night lights.
- Split-screen for up to 4 players, each in a different vehicle.

## New in 2.3.2 - 2.3.5

**The podium**
- A beach podium: jet skis arrive over the sea at sunset and get their prizes on a stage on the sand - the crowd in swimsuits and bikinis, the riders in wetsuits and life vests, umbrellas, palms, a lifeguard tower, beach balls.
- Each vehicle's own driver gets off and takes the prize:
  - the carreta's huaso;
  - one of an anda's bearers (the image stays on the anda);
  - François off his two motorbikes;
  - Breeze's driver (the co-driver stays), each in the outfit of that variant;
  - a police officer from The Fuzz;
  - stout drivers for the micro and the taxi.
- The other drivers wear racing helmets, take them off (80s hair) and walk with bending knees.
- A carreta gallops in on hooves and an anda is marched in by its bearers, instead of an engine's sound.
- The andas park straight and level.
- The podium music no longer cuts out.

**Tracks and races**
- Plaza de Illapel is now 3 laps, and the andas pick up glasses of water instead of fuel.
- Playa Luna's intro is a sunny summer afternoon with waves on the shore.
- Jet skis throw up white sea foam behind them, not brown chunks.
- Road traffic and toll plazas are switched off for now (they could make a stage impossible to win); their options are greyed out.
- A new mix of the song "Más bencina".

**Fixes**
- A pink marker over the pickups.
- Light glints floating round the jet skis on the car selection.
- The andas too small with one player.
- Long speech bubbles that flickered, also on the screen's edge.

## New in 2.3.1

- **traffic.json "Colours":** the colours a road's traffic wears, so traffic of the same model as the rivals never looks like one of them.
- **The mod's own 13 songs** come with it (HCT15Music\Race\Agregados and Race\Illapel), played by the Chile campaigns.
- **New podium music:** a 61-second cut of one of those songs, its big hit when the champion arrives.

## New in 2.3.0

- **A podium ceremony after every race of the mod's campaigns:** one minute of live 3D before the results.
  - The top three arrive on the stage in their own vehicles; promo models receive them.
  - The presenters call out the real names and times.
  - Photographers' flashes, CO2, metallic confetti and fireworks for the champion, the cup and champagne.
  - Stands full of fans jumping and waving Chilean flags.
  - The track's own sky and scenery behind it; on the Santiago tracks, Costanera Center, La Moneda lit up and Torre Entel.
  - Enter / Space / gamepad A skip it.
- **Editable with free tools:**
  - its texts, times and buildings are in `HCT15Content\podium\podio.json`;
  - its sounds are one file each in `HCT15Content\podium\sonidos`;
  - which game cars send their own driver is in `HCT15Content\podium\drivers.json`;
  - the models are OBJ (Blender).
- **Playa Luna's intro is a live 3D scene:** a jet ski along the shore, a jump over the bathers in slow motion, the pack and the title.

## Also since 2.2.5

- 37 new Chile rounds on the map: the tourist places of Arica y Parinacota, Tarapacá, Ñuble, Los Ríos, Aysén and Magallanes (a first draft - help to polish them is welcome).
- Winner's stickers per podium place (bring your own: `HCT15Content\stickers\oro`, `plata`, `bronce`).
- Santiago inundado laps the Estadio Nacional (a concert on the pitch) and the polideportivo; lightning and mud.
- The map of Chile shows each track's outline; the stage list wraps round.
- **Fixes:**
  - the Chilean speech bubbles stay in the mod's campaigns;
  - Plaza de Illapel's buildings stay fixed and full size;
  - no stall at the finish;
  - Grand Prix 2026 works with two players.

---

Built with BepInEx 5 (LGPL-2.1) and HarmonyX (MIT). Code under MIT; content under the licences of its open data (OpenStreetMap ODbL, Sentinel-2 CC BY-NC-SA - non-commercial). See CREDITS.txt.
Horizon Chase Turbo is a trademark of Aquiris Game Studio; this mod is not affiliated with or endorsed by Aquiris.
