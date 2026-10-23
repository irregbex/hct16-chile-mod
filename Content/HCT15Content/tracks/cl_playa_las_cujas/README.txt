Playa Las Cujas (Cachagua · frente a la Isla de los Pingüinos) - Chile campaign, region Playas de la Quinta Región, point to point, jet skis only.
race.json      the beach's waterline from OpenStreetMap (way 316612990), end to end on the water, scaled up to ~560 raced
               units, then a return leg over the sea that is never raced (EndPercentage 0.630); swells, buoy slalom
road_strip.png water: open sea on one side, the course between float ropes, shallows, surf and sand on the beach side
environment    day, the sun held over the road ahead (SunAhead); scenery: the tessellated beach with animated waves
Regenerate:    python Tools\playas_v.py --lap --only cl_playa_las_cujas ; python Tools\chile_build.py --only cl_playa_las_cujas ;
               python Tools\horizon.py cl_playa_las_cujas --force ; python Tools\backdrop.py cl_playa_las_cujas ; python Tools\playas_v.py --dress --only cl_playa_las_cujas
