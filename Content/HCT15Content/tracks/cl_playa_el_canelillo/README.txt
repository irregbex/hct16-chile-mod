Puerto de San Antonio (San Antonio · el puerto más grande de Chile) - Chile campaign, region Playas de la Quinta Región, 3 laps on the water, jet skis only.
race.json      a stadium drawn on the bay by puertos_v.py (not OSM: no road on the water, no ships in OSM), checked
               against the OSM coastline; 680 units a lap; a light harbour chop
road_strip.png harbour water on both sides, the course between float ropes
scenery.json   ships moored end to end inside both straights (18-unit segments, asset_kit), the quay with gantry
               cranes and containers, a ship at anchor, the breakwater and lighthouse, tugs, buoys, ramps, gulls
environment    day, the sun held over the road ahead (SunAhead)
Regenerate:    python Tools\puertos_v.py --lap --only cl_playa_el_canelillo ; python Tools\chile_build.py --only cl_playa_el_canelillo ;
               python Tools\horizon.py cl_playa_el_canelillo --force ; python Tools\backdrop.py cl_playa_el_canelillo --sea-front ; python Tools\puertos_v.py --dress --only cl_playa_el_canelillo
