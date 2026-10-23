HCT 1.6 custom music
====================
Menu\   tracks for the title screen and menus
Race\   tracks for the races
Files directly in this folder play in both.

Formats: .ogg (Vorbis) or .wav. MP3 cannot be decoded by this Unity version -
convert with any audio tool (e.g. ffmpeg -i song.mp3 -q:a 5 song.ogg).
Tracks play in file-name order (prefix 01_, 02_, ... to control it), or shuffled
with "ShuffleMusic": true in HCT15Content\hct15.settings.json.

A race can ask for its own track: put its name in the round's "Music" in the campaign's
campaign.json, e.g. "Music": "Race/Agregados/Song.wav" (the path under this folder, or just
the file name). That race opens with the track and repeats it to the finish; F11 still skips
out of it. Chile round 1 (Illapel - city sights) is set up that way.

In game: F11 = next track, F12 = custom music on/off (saved to settings).
The game's own music volume slider still applies.
