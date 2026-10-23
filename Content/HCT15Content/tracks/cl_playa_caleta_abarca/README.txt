Playa Caleta Abarca (Viña del Mar · bajo el Reloj de Flores) - Chile campaign, region Playas de la Quinta Región, point to point, jet skis only.
race.json      the beach's waterline from OpenStreetMap (way 27710762), end to end on the water, scaled up to ~560 raced
               units, then a return leg over the sea that is never raced (EndPercentage 0.632); swells, buoy slalom
road_strip.png water: open sea on one side, the course between float ropes, shallows, surf and sand on the beach side
environment    sunset, the sun held over the road ahead (SunAhead); scenery: the tessellated beach with animated waves
Regenerate:    python Tools\playas_v.py --lap --only cl_playa_caleta_abarca ; python Tools\chile_build.py --only cl_playa_caleta_abarca ;
               python Tools\horizon.py cl_playa_caleta_abarca --force ; python Tools\backdrop.py cl_playa_caleta_abarca ; python Tools\playas_v.py --dress --only cl_playa_caleta_abarca
