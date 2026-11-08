Bahía de Quintero (Quintero · granelero y metanero) - Chile campaign, region Playas de la Quinta Región, 3 laps on the water, jet skis only.
race.json      a stadium drawn on the bay by puertos_v.py (not OSM: no road on the water, no ships in OSM), checked
               against the OSM coastline; 680 units a lap; a light harbour chop
road_strip.png harbour water on both sides, the course between float ropes
scenery.json   ships moored end to end inside both straights (18-unit segments, asset_kit), the quay with gantry
               cranes and containers, a ship at anchor, the breakwater and lighthouse, tugs, buoys, ramps, gulls
environment    sunset, the sun held over the road ahead (SunAhead)
Regenerate:    python Tools\puertos_v.py --lap --only cl_playa_los_enamorados ; python Tools\chile_build.py --only cl_playa_los_enamorados ;
               python Tools\horizon.py cl_playa_los_enamorados --force ; python Tools\backdrop.py cl_playa_los_enamorados --sea-front ; python Tools\puertos_v.py --dress --only cl_playa_los_enamorados
