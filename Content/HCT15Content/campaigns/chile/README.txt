HCT 1.6 - Chile campaign
========================
126 laps through real Chilean cities (the 50 biggest cities, the 19 most populous comunas of
Santiago, the Americo Vespucio ring road and Illapel's city-sights lap), routed on the real streets
(OpenStreetMap), with the real terrain (SRTM) as hills and the real buildings as crude blocks.
Order: Illapel's sights lap, Costanera Center -> La Moneda, the Santiago comunas A-Z, the other cities A-Z.
No pit lane in this campaign ("Pits": false in campaign.json; the tracks keep their pit definition).
campaign.json / cup.json  the rounds and the cup;  blade.png / logo.png  the menu art;
assets\ui\background.png  the campaign screen background (edit freely, never overwritten).
Tracks live in ..\..\tracks\cl_*\ and can be edited in the game's track editor (F7).
Rebuild: python Tools\chile_build.py   (data: Tools\osm_city_loop.py, elevation.py, osm_scenery.py)
