Playa Luna (Horcón · la playa nudista) - Chile campaign, region Playas de la Quinta Región, point to point, jet skis only.
race.json      the beach's waterline from OpenStreetMap (way 152162973), end to end on the water, scaled up to ~560 raced
               units, then a return leg over the sea that is never raced (EndPercentage 0.620); swells, buoy slalom
road_strip.png water: open sea on one side, the course between float ropes, shallows, surf and sand on the beach side
environment    golden, the sun held over the road ahead (SunAhead); scenery: the tessellated beach with animated waves
Regenerate:    python Tools\playas_v.py --lap --only cl_playa_luna ; python Tools\chile_build.py --only cl_playa_luna ;
               python Tools\horizon.py cl_playa_luna --force ; python Tools\backdrop.py cl_playa_luna ; python Tools\playas_v.py --dress --only cl_playa_luna
