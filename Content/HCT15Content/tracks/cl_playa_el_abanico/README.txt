Playa El Abanico (Maitencillo · surf bajo los acantilados) - Chile campaign, region Playas de la Quinta Región, point to point, jet skis only.
race.json      the beach's waterline from OpenStreetMap (way 221693568), end to end on the water, scaled up to ~560 raced
               units, then a return leg over the sea that is never raced (EndPercentage 0.657); swells, buoy slalom
road_strip.png water: open sea on one side, the course between float ropes, shallows, surf and sand on the beach side
environment    golden, the sun held over the road ahead (SunAhead); scenery: the tessellated beach with animated waves
Regenerate:    python Tools\playas_v.py --lap --only cl_playa_el_abanico ; python Tools\chile_build.py --only cl_playa_el_abanico ;
               python Tools\horizon.py cl_playa_el_abanico --force ; python Tools\backdrop.py cl_playa_el_abanico ; python Tools\playas_v.py --dress --only cl_playa_el_abanico
