HCT 1.6 Track Editor
====================
A browser editor for the mod's tracks, scenery, assets and campaigns: the same files the game reads, so what you
save is what you race. It runs locally (a small Python server plus a page in Chrome or Edge); nothing is uploaded.

Like the rest of HCT 1.6 it is FULLY VIBECODED (made with AI) - we need your help in refining it. Report problems or
send improvements on GitHub (Issues / pull requests).


START
  1. Install HCT 1.6 into your game first (INSTALL.cmd from the release).
  2. Install Python 3 if you do not have it: https://www.python.org/downloads/ (tick "Add python.exe to PATH").
     Nothing else is needed - no extra packages.
  3. Double-click Track-Editor.cmd. It finds the game in your Steam libraries and opens http://127.0.0.1:8360 in
     your browser. Keep the black window open while you edit; close it to stop the editor.

  Other ways to start it:
    Track-Editor.cmd "D:\Games\Horizon Chase Turbo"            a game folder it did not find by itself
    Track-Editor.cmd --content "..\Content\HCT15Content"       this repository's content (to prepare a pull request)
    options: --port 8360   --no-browser
  Never open index.html by double-click: the page needs the server.


WHAT IT EDITS (inside the game folder's HCT15Content\, or the --content folder)
  tracks\<id>\race.json        the lap: waypoints, width, pit lane, names in six languages, laps, nitros, coins...
  tracks\<id>\scenery.json     buildings, trees, landmarks, assets and photo slabs along the road
  tracks\<id>\environment.json, weather.json, nightfx.json   time of day, rain/snow, night lights (Ambience tab)
  tracks\<id>\history\         a dated snapshot of every save (the last 20) - History tab to go back
  assets\models\<id>\          the shared library of 3D assets (.obj + .mtl + asset.json + thumb.png)
  campaigns\<folder>\          campaign.json + cup.json: rounds, order, titles, laps
  Deleted tracks and assets are moved to TrackEditor\trash\, never erased.
  The game's own files are never touched.


THE SCREEN
  Top      the track list (grouped by campaign), File menu (new, save as, import, test in game), undo/redo,
           Map / 3D / Drive, Assets, Save
  Left     the tools (1-7: move road, height, start line, pit lane, scenery, scatter, erase), the brush and what
           Shift+click places (a building kind, a tree, a library asset, a photo slab)
  Centre   Map: north up; wheel zooms, right/middle drag pans
           3D: right-drag looks, WASD/QE flies, F frames the selection
           Drive: the lap as the race draws it - the road bends ahead, props grow as they come closer, the race
           camera. Space pauses, C changes camera (race / hood / helicopter / world), +/- speed.
           The ground either side takes the colours of the track's own strip textures (sea, sand, grass, snow...).
  Right    Track (stats, fix spacing, names, race settings), Selection, Scenery list, Ambience, History, Campaign
  Bottom   status, progress bar, the log

  "Test in game" starts Horizon Chase Turbo straight into the track you are editing (close the game first; Steam
  must be running).


IMPORTING YOUR OWN 3D ASSETS
  Assets -> Import .obj: pick an .obj (with its .mtl), set name, category and scale, see it in 3D, save. It lands in
  assets\models\<id>\ and can be placed on any track. Convention: the model's front faces +X in the file.


NOT IN THIS COPY
  Two features of the original working copy read data extracted from the game itself, so they are left out of the
  public editor: loading the game's own trackside props as 3D meshes (they show as boxes), and starting a new track
  from one of the game's stages (a new track starts as a plain oval or as a copy of one of the mod's tracks).


Code under the MIT licence (see LICENSE.txt at the root). three.js r128 (lib\three.min.js) (c) three.js authors, MIT.
