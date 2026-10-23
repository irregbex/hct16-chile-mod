HCT 1.6 custom tracks - one folder per track
=============================================
<race id>\race.json      the track definition the mod loads (see readme_HCT1.5.txt section 6)
<race id>\city.json      optional: the city the race is filed under (menus)
<race id>\card.png       optional: campaign card / thumbnail
<race id>\layout.png     preview (yellow = in-game line, grey = real centreline, green = start)
<race id>\source\       the real-world centreline the track came from
<race id>\*.png          textures named in race.json (RoadStripTexture, DayTexture, ...)

The loader scans every sub-folder, so a track is added by dropping its folder here and
removed by deleting it. Flat files (my_track.race.json) are still accepted.
Campaigns live in ..\campaigns\<name>\ (campaign.json, cup.json, blade.png).
