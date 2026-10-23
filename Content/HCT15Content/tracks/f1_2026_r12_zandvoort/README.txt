Dutch Grand Prix - Circuit Zandvoort (Zandvoort, Netherlands), round 12 of the 2026 season, 23 August 2026.
race.json   the track (waypoints in game units, donor scene brighton_beach_01)
city.json   its entry in cup 99_f1_2026
card.png    campaign card, layout.png preview (yellow = in-game line, grey = real centreline)
source\     the real centreline this was generated from (OpenStreetMap, ODbL)
scenery.json  OpenStreetMap buildings/trees/landmarks around the circuit, projected onto the
              track (P = lap fraction, Side, Dist and sizes in game units); the game turns them
              into crude blocks. Hills come from Reference\Circuits\<id>\elevation.json (SRTM).
Regenerate: python Tools\f1_2026_build.py --only r12
