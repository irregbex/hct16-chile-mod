using System.Collections.Generic;

namespace HCT15
{
    /// <summary>
    /// The mod's own strings in the game's languages. The game stores the chosen language as a code
    /// (TextLocalization.CurrentLanguage: "en", "es", "pt", "fr", "de", "it", "ru", ...) - the mod
    /// follows it and falls back to English for anything it has no translation for.
    /// Keys are English-ish identifiers; Get(key) returns the text for the current language.
    /// Grand Prix names are built from the country ("Gran Premio de Australia") by GrandPrix().
    /// </summary>
    public static class Loc
    {
        private static readonly Dictionary<string, Dictionary<string, string>> T = new Dictionary<string, Dictionary<string, string>>
        {
            ["en"] = new Dictionary<string, string>
            {
                ["car_anda_andacollo_desc"] = "The heaviest: the great crown of Andacollo. Slow to start, steady in the bends, the most candles for the road (fuel).", ["car_anda_purisima_desc"] = "The lightest: white and sky blue. Quick off the line, loose in the bends, few candles.", ["car_anda_dolores_desc"] = "Black and purple, bearers in mourning. Unhurried, sure-footed, a big supply of candles.", ["car_anda_rosario_desc"] = "Pink and white roses. Balanced, a little quicker than Carmen to get going.", ["car_anda_san_rafael_desc"] = "Illapel's patron, wings and staff. The quickest start, the least grip and the fewest candles.", ["car_anda_candelaria_desc"] = "Teal robe, gold cape and a candle in her hand: Copiapó's Virgin. Steady, sure in the bends, plenty of candles.", ["car_anda_tirana_desc"] = "Red and gold, bearers in the colours of the diabladas. The highest top speed, the least grip, few candles.", ["car_anda_santa_rosa_desc"] = "White habit, black cloak, a crown of roses: Pelequén's saint. The quickest start and the best grip, the lowest top speed.", ["car_anda_san_pedro_desc"] = "The fishermen's patron with his keys, carried by men of the coves. Slow to start, the biggest supply of candles.",
                ["car_anda_name"] = "Anda of Our Lady of Mount Carmel", ["car_anda_andacollo_name"] = "Anda of Our Lady of Andacollo", ["car_anda_purisima_name"] = "Anda of the Immaculate", ["car_anda_dolores_name"] = "Anda of Our Lady of Sorrows", ["car_anda_rosario_name"] = "Anda of Our Lady of the Rosary", ["car_anda_san_rafael_name"] = "Anda of St Raphael the Archangel", ["car_anda_candelaria_name"] = "Anda of Our Lady of Candlemas", ["car_anda_tirana_name"] = "Anda of Our Lady of La Tirana", ["car_anda_santa_rosa_name"] = "Anda of St Rose of Lima", ["car_anda_san_pedro_name"] = "Anda of St Peter", ["car_anda_desc"] = "A procession anda of Illapel: eight bearers in white with the poles on their shoulders, the platform full of flowers and candles, the Virgin of Mount Carmel on top.", ["car_anda_variant_desc"] = "Another anda of the procession, another image on top - the same eight bearers.",
                ["car_g151_name"] = "Mine pickup", ["car_g151_blanca_name"] = "Mine pickup (Glaciar)", ["car_g151_negra_name"] = "Mine pickup (Obsidiana)", ["car_g151_gris_name"] = "Mine pickup (Grafito)", ["car_g151_azul_name"] = "Mine pickup (Pacífico)", ["car_g151_race_name"] = "Mine pickup (Carmín)", ["car_g151_rubi_name"] = "Mine pickup (Granate)", ["car_g151_pimienta_name"] = "Mine pickup (Ají)", ["car_g151_variant_desc"] = "The mines' in another factory colour.", ["car_g151_desc"] = "The Choapa mines' red 4x4 crew cab: amber beacon on the roof and the reflective whip flag the haul trucks look for.", ["car_taxi_name"] = "Chilean taxi", ["car_micro_name"] = "Old Santiago micro",
                ["car_taxi_v16_name"] = "Classic sedan taxi", ["car_taxi_v16_desc"] = "The classic sedan that drove Santiago for twenty years, black with the yellow roof: light, nimble and never in a hurry to stop.",
                ["car_micro_roja_name"] = "Micro - red",
                ["car_jetski_name"] = "Jet ski Viña - No. 1", ["car_jetski_turquesa_name"] = "Jet ski Turquoise - No. 7", ["car_jetski_neon_name"] = "Jet ski Neon - No. 23", ["car_jetski_atardecer_name"] = "Jet ski Sunset - No. 17", ["car_jetski_negro_name"] = "Jet ski Black and gold - No. 9", ["car_jetski_rosa_name"] = "Jet ski Pink - No. 44", ["car_jetski_oro_cielo_name"] = "Jet ski Gold and sky - No. 5", ["car_jetski_verde_name"] = "Jet ski Sea green - No. 31", ["car_jetski_desc"] = "Balanced and forgiving, a big tank: the one everybody learns on at Caleta Abarca.", ["car_jetski_turquesa_desc"] = "Shoots off the line and turns on a coin; not the fastest at the end of a straight.", ["car_jetski_neon_desc"] = "The fastest of them all flat out, but it slides wide in the bends.", ["car_jetski_atardecer_desc"] = "Quick to pick up speed and steady in the swell.", ["car_jetski_negro_desc"] = "A rocket on the straights that drinks fuel like water.", ["car_jetski_rosa_desc"] = "Sticks to its line through any slalom; slow to get going.", ["car_jetski_oro_cielo_desc"] = "Fast and sure-footed: a summer machine under the gold and sky.", ["car_jetski_verde_desc"] = "Grips and accelerates well: the all-rounder of the bay.",
                ["car_micro_blanca_name"] = "Micro - white",
                ["car_micro_turquesa_name"] = "Micro - turquoise",
                ["car_micro_azul_name"] = "Micro - blue",
                ["car_micro_verde_name"] = "Micro - green",
                ["car_micro_naranja_name"] = "Micro - orange",
                ["car_micro_multicolor_name"] = "Micro - regional lines",
                ["car_taxi_desc"] = "Black with a yellow roof, like every taxi in Santiago. Slow off the line, but it knows every street.",
                ["car_micro_desc"] = "The yellow micro of the old city lines: heavy, loud, and impossible to pass in a corner.",
                ["car_micro_variant_desc"] = "The same micro of the old city lines in another livery: heavy, loud, and impossible to pass in a corner.",
                ["car_carreta_name"] = "Horse cart", ["car_carreta_desc"] = "The horse-drawn cart of the Choapa valley, wooden wheels and a huaso at the reins: slow off the line, unstoppable on the old road to Illapel.",
                ["car_carreta_potro_name"] = "Black stallion cart", ["car_carreta_potro_desc"] = "A black stallion on a light red cart with iron tyres: the fastest carreta of the valley, nothing on board to slow it.",
                ["car_carreta_alazan_name"] = "Sorrel cart", ["car_carreta_alazan_desc"] = "A sorrel on a blue cart with red wheels, loaded with watermelons from the Choapa.",
                ["car_carreta_bayo_name"] = "Palomino cart", ["car_carreta_bayo_desc"] = "A palomino with a white mane on a green cart carrying three barrels of pipeño.",
                ["car_carreta_tordillo_name"] = "Grey horse cart", ["car_carreta_tordillo_desc"] = "A dappled grey on iron tyres with a load of firewood for the winter.",
                ["car_carreta_overo_name"] = "Pinto cart", ["car_carreta_overo_desc"] = "A pinto with white socks on a yellow and red cart full of flour sacks.",
                ["car_carreta_mula_name"] = "Mule cart", ["car_carreta_mula_desc"] = "A stubborn mule, solid wooden wheels and sacks of beans: slow, but it never gives up.",
                ["car_carreta_percheron_name"] = "Draft horse cart", ["car_carreta_percheron_desc"] = "A huge percheron pulling a cart piled high with hay: heavy off the line, unstoppable once rolling.",
                ["car_carreta_bueyes_name"] = "Ox cart", ["car_carreta_bueyes_desc"] = "The old Chilean carreta: two oxen under a yoke, solid wheels and firewood. The slowest thing on the road.",
                ["car_carreta_fonda_name"] = "Fiesta cart", ["car_carreta_fonda_desc"] = "Dressed for the fiestas patrias: a ramada of branches, flags and a barrel of chicha.",
                ["car_carreta_lechero_name"] = "Milk cart", ["car_carreta_lechero_desc"] = "The milkman's blue cart with its churns rattling on iron tyres, at dawn on the way to town.",
                ["opt_loudness"] = "Loudness (compressed)", ["opt_loudness_off"] = "off", ["opt_tolls"] = "Tolls (Rutas de Chile)", ["opt_traffic"] = "Road traffic", ["opt_traffic_on"] = "yes",
                ["championship"] = "DRIVERS' CHAMPIONSHIP", ["of_rounds"] = "{0} of {1} rounds", ["pts"] = "pts",
                ["season_line"] = "2026 GRAND PRIX SEASON  -  {0} GRANDS PRIX", ["no_results"] = "No results yet - win races to move up the table",
                ["loading"] = "LOADING", ["preparing"] = "preparing", ["armed"] = "custom race armed", ["scene_loaded"] = "race scene loaded",
                ["game_loaded"] = "game scene loaded", ["geometry"] = "track geometry rebuilt", ["baked"] = "track baked", ["initialised"] = "race initialised",
                ["scenery"] = "scenery placed", ["power_unit"] = "power unit", ["pit_stop"] = "PIT STOP", ["refuelled"] = "REFUELLED",
                ["pit_lane"] = "PIT LANE", ["round"] = "Round {0}", ["you"] = "you", ["now_playing"] = "NOW PLAYING",
                ["ed_menu"] = "TRACK EDITOR", ["ed_title"] = "HCT 1.6 TRACK EDITOR", ["ed_close"] = "Close", ["ed_tracks"] = "Tracks", ["ed_tools"] = "Tools",
                ["ed_move"] = "Move road", ["ed_height"] = "Height (hills)", ["ed_scenery"] = "Scenery", ["ed_pits"] = "Pit lane", ["ed_start"] = "Start / finish",
                ["ed_radius"] = "Brush radius", ["ed_fix"] = "Fix spacing & bends", ["ed_flatten"] = "Flatten everything", ["ed_fit"] = "Fit view",
                ["ed_undo"] = "Undo", ["ed_save"] = "Save", ["ed_test"] = "Save & test drive", ["ed_delete"] = "Delete", ["ed_delete_around"] = "Delete everything within",
                ["ed_pit_start_here"] = "Pit lane starts at selected waypoint", ["ed_pit_end_here"] = "Pit lane ends at selected waypoint", ["ed_pit_side"] = "Pit side: right / left / none",
                ["ed_start_here"] = "Start/finish line at selected waypoint", ["ed_saved"] = "Saved", ["ed_fixed"] = "Fixed", ["ed_deleted"] = "deleted", ["ed_start_set"] = "Start/finish moved",
                ["ed_help"] = "Left mouse: select/drag  -  wheel: zoom  -  middle mouse or arrows: pan  -  Ctrl+Z undo  -  Ctrl+S save  -  F7 close",
                ["ed_legend"] = "Road: blue = low, white = high, red = bend too tight for the engine.  Green = start/finish.  Orange = pit lane.  Squares = buildings, dots = trees, yellow = landmarks.",
                ["ed_help_move"] = "Drag a waypoint; neighbours within the brush radius follow. Then 'Fix spacing & bends' before saving.",
                ["ed_help_height"] = "Drag up/down on a waypoint to raise or lower the road within the brush radius.",
                ["ed_help_scenery"] = "Click an item to select, drag to move, Delete to remove. Shift+click adds the chosen kind, Ctrl+click adds a tree.",
                ["ed_help_add"] = "Kind for Shift+click:", ["ed_help_pits"] = "Click a waypoint, then set the pit lane start / end there. The pit side is the side of the garages.",
                ["ed_help_start"] = "Click a waypoint and make it the start/finish line (the lap is rotated, scenery and pits follow).",
                ["ed_saving"] = "Saving", ["ed_unsaved"] = "unsaved changes", ["ed_save_as"] = "Save as new track", ["ed_history"] = "Saved versions",
                ["ed_revert"] = "Back to the original (as generated)", ["ed_snapshot"] = "a dated copy is kept in history\\", ["ed_restored"] = "Loaded",
                ["ed_save_to_keep"] = "press Save to keep it", ["ed_no_original"] = "no original saved yet (nothing was overwritten)", ["ed_original"] = "the original",
                ["ed_exists"] = "a track with that name exists", ["ed_saved_as"] = "Saved as new track", ["ed_add_to_campaign"] = "add its id to a campaign.json to race it in a campaign; F9 lists it now",
                ["ed_unsaved_q"] = "The track has unsaved changes. Save them?", ["ed_discard"] = "Discard", ["ed_cancel"] = "Cancel",
                ["ed_scatter"] = "Scatter (many)", ["ed_place"] = "Place:", ["ed_place_kind"] = "building", ["ed_place_tree"] = "tree", ["ed_place_asset"] = "asset",
                ["ed_assets"] = "Asset library", ["ed_import"] = "Import", ["ed_browse"] = "Browse...", ["ed_rescan"] = "Rescan", ["ed_imported"] = "Imported:", ["ed_import_failed"] = "Import failed:",
                ["ed_import_hint"] = "Path of an .obj (Blender, MagicaVoxel, Blockbench, Tools\\asset_kit.py). Style: HCT15Content\\assets\\STYLE.txt", ["ed_import_track"] = "only for this track",
                ["ed_osm"] = "OSM map (real streets)", ["ed_osm_opacity"] = "Opacity", ["ed_osm_fit"] = "map fitted to the track, +/- {0} m where the road was smoothed",
                ["ed_osm_none"] = "No osm_overlay.json for this track - run Tools\\osm_overlay.py to draw the real streets under the road.",
                ["ed_asset_scale"] = "Scale", ["ed_asset_yaw"] = "Rotation", ["ed_asset_missing"] = "model not found in the library - shown as a magenta block", ["ed_no_assets"] = "No models yet: import one below or run Tools\\asset_kit.py",
                ["ed_help_scatter"] = "Drag to paint many items inside the brush (off the road, apart from each other). Shift+click a waypoint, then Shift+click another: a row along that side of the road. Ctrl+drag erases.",
                ["ed_scatter_count"] = "Items per stroke", ["ed_scatter_spacing"] = "Minimum spacing", ["ed_row_spacing"] = "Row spacing", ["ed_row_dist"] = "from the road edge", ["ed_random_yaw"] = "random rotation for trees",
                ["ed_row_end_hint"] = "Row start set - Shift+click the end waypoint (the click's side of the road is used)", ["ed_placed"] = "placed",
                ["ed_browser"] = "Asset browser", ["ed_cat_all"] = "All models", ["ed_cat_tree"] = "Trees", ["ed_cat_building"] = "Buildings", ["ed_cat_landmark"] = "Landmarks", ["ed_cat_prop"] = "Props", ["ed_cat_game"] = "Game props",
                ["ed_browser_hint"] = "Click a card to choose it (Shift+click on the map/3D view then places it; Scatter paints it). Double-click, or the Add buttons, put one beside the selected waypoint.",
                ["ed_chosen"] = "Chosen", ["ed_pick_hint"] = "Nothing chosen yet - click a card.", ["ed_add_here"] = "Add beside waypoint", ["ed_add_nearest"] = "nearest the view", ["ed_add_left"] = "left of the road", ["ed_add_right"] = "right of the road",
                ["ed_use_shift"] = "Close and place with Shift+click", ["ed_place_game"] = "game prop",
                ["ed_load_game"] = "Load the game's props of stage {0}", ["ed_game_loading"] = "loading the stage's props...", ["ed_game_loaded"] = "props of {0} loaded",
                ["ed_game_none"] = "No props captured for this stage yet. Press the button (the stage is loaded in the background for a moment) or drive the track once; the list and thumbnails are then kept in assets\\game_props\\.",
                ["ed_game_preview_hint"] = "Thumbnails from the last capture; press the button to load the real models for the 3D view (otherwise placed game props show as teal blocks).",
                ["ed_game_rotation"] = "Rotation in game", ["ed_game_note"] = "Game props follow the road like the stage's own scenery; SideDependent ones turn to face the traffic on the left. Scale applies, rotation does not.",
                ["ed_pits_off"] = "Pits are switched off for the campaign '{0}' (campaign.json \"Pits\": false) - the lane below stays in the file but is not built.",
                ["ed_finish_p2p"] = "Point-to-point: the race ends at the magenta finish line; the grey road after it is the return leg (not raced).",
                ["ed_view_map"] = "2D map", ["ed_view_3d"] = "3D view", ["ed_view_drive"] = "Drive-by",
                ["ed_drive_speed"] = "Speed", ["ed_drive_pause"] = "Pause", ["ed_drive_resume"] = "Resume", ["ed_drive_restart"] = "Restart",
                ["ed_cam_chase"] = "Chase", ["ed_cam_hood"] = "Hood", ["ed_cam_side"] = "Helicopter", ["ed_focus_hint"] = "F frames the selection",
                ["ed_help_3d"] = "Right mouse: look  -  WASD / QE: fly (Shift = fast)  -  wheel: forward  -  F: frame selection  -  left mouse: select / drag  -  click the inset map to jump",
                ["ed_help_drive"] = "Space: pause  -  + / -: speed  -  Home: restart  -  C: camera  -  arrows: scrub  -  click the inset map to jump  -  left mouse edits while it drives",
                ["ed_legend_3d"] = "Crude preview. Red road = bend too tight for the engine. Chequered band = start/finish. Grey strip + garages = pit lane. Yellow post = selection, yellow ring = brush.",
            },
            ["es"] = new Dictionary<string, string>
            {
                ["car_anda_andacollo_desc"] = "La más pesada: la gran corona de Andacollo. Lenta para partir, firme en las curvas y con más velas para el camino (bencina).", ["car_anda_purisima_desc"] = "La más liviana: blanco y celeste. Parte rápido, se suelta en las curvas, pocas velas.", ["car_anda_dolores_desc"] = "Negro y morado, cargadores de luto. Sin apuro, pisada segura, mucha reserva de velas.", ["car_anda_rosario_desc"] = "Rosas rosadas y blancas. Equilibrada, parte un poco más rápido que la del Carmen.", ["car_anda_san_rafael_desc"] = "El patrono de Illapel, con alas y bastón. La que mejor parte, la que menos se agarra y la de menos velas.", ["car_anda_candelaria_desc"] = "Manto turquesa, capa dorada y una vela en la mano: la Virgen de Copiapó. Constante, firme en las curvas, bastantes velas.", ["car_anda_tirana_desc"] = "Roja y dorada, cargadores con los colores de las diabladas. La de más velocidad máxima, la que menos se agarra, pocas velas.", ["car_anda_santa_rosa_desc"] = "Hábito blanco, manto negro y corona de rosas: la santa de Pelequén. La que mejor parte y más se agarra, la de menor velocidad máxima.", ["car_anda_san_pedro_desc"] = "El patrono de los pescadores con sus llaves, cargado por gente de las caletas. Lenta para partir, la mayor reserva de velas.",
                ["car_anda_name"] = "Anda de la Virgen del Carmen", ["car_anda_andacollo_name"] = "Anda de la Virgen de Andacollo", ["car_anda_purisima_name"] = "Anda de la Purísima", ["car_anda_dolores_name"] = "Anda de la Dolorosa", ["car_anda_rosario_name"] = "Anda de la Virgen del Rosario", ["car_anda_san_rafael_name"] = "Anda de San Rafael Arcángel", ["car_anda_candelaria_name"] = "Anda de la Virgen de la Candelaria", ["car_anda_tirana_name"] = "Anda de la Virgen de La Tirana", ["car_anda_santa_rosa_name"] = "Anda de Santa Rosa de Lima", ["car_anda_san_pedro_name"] = "Anda de San Pedro", ["car_anda_desc"] = "Un anda de la procesión de Illapel: ocho cargadores de blanco con las varas al hombro, la parihuela llena de flores y velas, y la Virgen del Carmen arriba.", ["car_anda_variant_desc"] = "Otra anda de la procesión, otra imagen arriba: los mismos ocho cargadores.",
                ["car_g151_name"] = "Camioneta minera", ["car_g151_blanca_name"] = "Camioneta minera (Glaciar)", ["car_g151_negra_name"] = "Camioneta minera (Obsidiana)", ["car_g151_gris_name"] = "Camioneta minera (Grafito)", ["car_g151_azul_name"] = "Camioneta minera (Pacífico)", ["car_g151_race_name"] = "Camioneta minera (Carmín)", ["car_g151_rubi_name"] = "Camioneta minera (Granate)", ["car_g151_pimienta_name"] = "Camioneta minera (Ají)", ["car_g151_variant_desc"] = "La de las mineras en otro color de fábrica.", ["car_g151_desc"] = "La 4x4 doble cabina roja de las mineras del Choapa: baliza ámbar en el techo y la banderilla reflectante que buscan los camiones.", ["car_taxi_name"] = "Taxi chileno", ["car_micro_name"] = "Micro antigua",
                ["car_taxi_v16_name"] = "Taxi sedán clásico", ["car_taxi_v16_desc"] = "El sedán clásico que recorrió Santiago durante veinte años, negro con techo amarillo: liviano, ágil y sin apuro para frenar.",
                ["car_micro_roja_name"] = "Micro roja",
                ["car_jetski_name"] = "Jetski Viña - N.º 1", ["car_jetski_turquesa_name"] = "Jetski Turquesa - N.º 7", ["car_jetski_neon_name"] = "Jetski Neón - N.º 23", ["car_jetski_atardecer_name"] = "Jetski Atardecer - N.º 17", ["car_jetski_negro_name"] = "Jetski Negro y oro - N.º 9", ["car_jetski_rosa_name"] = "Jetski Rosa - N.º 44", ["car_jetski_oro_cielo_name"] = "Jetski Oro y cielo - N.º 5", ["car_jetski_verde_name"] = "Jetski Verde mar - N.º 31", ["car_jetski_desc"] = "Equilibrado y noble, con estanque grande: en el que todos aprenden en Caleta Abarca.", ["car_jetski_turquesa_desc"] = "Sale como bala y dobla en una baldosa; al final de la recta no es el más rápido.", ["car_jetski_neon_desc"] = "El más rápido de todos a fondo, pero se abre en las curvas.", ["car_jetski_atardecer_desc"] = "Agarra velocidad rápido y va firme en la marejada.", ["car_jetski_negro_desc"] = "Un cohete en las rectas que se toma la bencina como agua.", ["car_jetski_rosa_desc"] = "No se sale de su línea en ningún slalom; le cuesta arrancar.", ["car_jetski_oro_cielo_desc"] = "Rápido y seguro: una máquina de verano bajo el oro y el cielo.", ["car_jetski_verde_desc"] = "Buen agarre y buena aceleración: el todoterreno de la bahía.",
                ["car_micro_blanca_name"] = "Micro blanca",
                ["car_micro_turquesa_name"] = "Micro turquesa",
                ["car_micro_azul_name"] = "Micro azul",
                ["car_micro_verde_name"] = "Micro verde",
                ["car_micro_naranja_name"] = "Micro naranja",
                ["car_micro_multicolor_name"] = "Micro multicolor (lineas de regiones)",
                ["car_taxi_desc"] = "Negro con techo amarillo, como todos los taxis de Santiago. Parte lento, pero se sabe todas las calles.",
                ["car_micro_desc"] = "La micro amarilla de las antiguas lineas: pesada, ruidosa e imposible de pasar en una curva.",
                ["car_micro_variant_desc"] = "La misma micro de las antiguas lineas con otra pintura: pesada, ruidosa e imposible de pasar en una curva.",
                ["car_carreta_name"] = "Carreta", ["car_carreta_desc"] = "La carreta tirada por caballo del valle del Choapa, ruedas de palo y un huaso en las riendas: lenta al partir, imparable en el camino viejo a Illapel.",
                ["car_carreta_potro_name"] = "Carreta del potro negro", ["car_carreta_potro_desc"] = "Un potro negro en una carreta roja y liviana con llantas de fierro: la más rápida del valle, no lleva nada que la frene.",
                ["car_carreta_alazan_name"] = "Carreta del alazán", ["car_carreta_alazan_desc"] = "Un alazán en una carreta azul de ruedas rojas, cargada de sandías del Choapa.",
                ["car_carreta_bayo_name"] = "Carreta del bayo", ["car_carreta_bayo_desc"] = "Un bayo de crin blanca en una carreta verde con tres pipas de pipeño.",
                ["car_carreta_tordillo_name"] = "Carreta del tordillo", ["car_carreta_tordillo_desc"] = "Un tordillo con llantas de fierro y una carga de leña para el invierno.",
                ["car_carreta_overo_name"] = "Carreta del overo", ["car_carreta_overo_desc"] = "Un overo de patas blancas en una carreta amarilla y roja llena de sacos de harina.",
                ["car_carreta_mula_name"] = "Carreta de la mula", ["car_carreta_mula_desc"] = "Una mula porfiada, ruedas macizas y sacos de porotos: lenta, pero nunca se rinde.",
                ["car_carreta_percheron_name"] = "Carreta del percherón", ["car_carreta_percheron_desc"] = "Un percherón enorme con una carreta llena de pasto: pesada al partir, imparable andando.",
                ["car_carreta_bueyes_name"] = "Carreta de bueyes", ["car_carreta_bueyes_desc"] = "La carreta chilena de siempre: dos bueyes bajo el yugo, ruedas macizas y leña. Lo más lento del camino.",
                ["car_carreta_fonda_name"] = "Carreta fondera", ["car_carreta_fonda_desc"] = "Vestida para las fiestas patrias: ramada de ramas, banderitas y una pipa de chicha.",
                ["car_carreta_lechero_name"] = "Carretón lechero", ["car_carreta_lechero_desc"] = "El carretón azul del lechero con los tarros sonando sobre llantas de fierro, de madrugada camino al pueblo.",
                ["opt_loudness"] = "Volumen comprimido", ["opt_loudness_off"] = "no", ["opt_tolls"] = "Peajes en Rutas de Chile", ["opt_traffic"] = "Tránsito en las rutas", ["opt_traffic_on"] = "sí",
                ["championship"] = "CAMPEONATO DE PILOTOS", ["of_rounds"] = "{0} de {1} carreras", ["pts"] = "pts",
                ["season_line"] = "TEMPORADA 2026 DE GRAN PREMIO  -  {0} GRANDES PREMIOS", ["no_results"] = "Aún sin resultados: gana carreras para subir en la tabla",
                ["loading"] = "CARGANDO", ["preparing"] = "preparando", ["armed"] = "carrera personalizada lista", ["scene_loaded"] = "escena de carrera cargada",
                ["game_loaded"] = "escena de juego cargada", ["geometry"] = "geometría del circuito reconstruida", ["baked"] = "circuito calculado", ["initialised"] = "carrera inicializada",
                ["scenery"] = "escenario colocado", ["power_unit"] = "unidad de potencia", ["pit_stop"] = "PARADA EN BOXES", ["refuelled"] = "REPOSTADO",
                ["pit_lane"] = "CALLE DE BOXES", ["round"] = "Carrera {0}", ["you"] = "tú", ["now_playing"] = "SONANDO",
                ["ed_menu"] = "EDITOR DE PISTAS", ["ed_title"] = "HCT 1.6 EDITOR DE PISTAS", ["ed_close"] = "Cerrar", ["ed_tracks"] = "Pistas", ["ed_tools"] = "Herramientas",
                ["ed_move"] = "Mover trazado", ["ed_height"] = "Altura (colinas)", ["ed_scenery"] = "Escenario", ["ed_pits"] = "Calle de boxes", ["ed_start"] = "Salida / meta",
                ["ed_radius"] = "Radio del pincel", ["ed_fix"] = "Corregir espaciado y curvas", ["ed_flatten"] = "Aplanar todo", ["ed_fit"] = "Ajustar vista",
                ["ed_undo"] = "Deshacer", ["ed_save"] = "Guardar", ["ed_test"] = "Guardar y probar", ["ed_delete"] = "Borrar", ["ed_delete_around"] = "Borrar todo en un radio de",
                ["ed_pit_start_here"] = "Boxes empiezan en el punto seleccionado", ["ed_pit_end_here"] = "Boxes terminan en el punto seleccionado", ["ed_pit_side"] = "Lado de boxes: derecha / izquierda / sin boxes",
                ["ed_start_here"] = "Línea de meta en el punto seleccionado", ["ed_saved"] = "Guardado", ["ed_fixed"] = "Corregido", ["ed_deleted"] = "borrado(s)", ["ed_start_set"] = "Meta movida",
                ["ed_help"] = "Botón izquierdo: seleccionar/arrastrar  -  rueda: zoom  -  botón central o flechas: desplazar  -  Ctrl+Z deshacer  -  Ctrl+S guardar  -  F7 cerrar",
                ["ed_legend"] = "Trazado: azul = bajo, blanco = alto, rojo = curva demasiado cerrada para el motor.  Verde = meta.  Naranja = boxes.  Cuadrados = edificios, puntos = árboles, amarillo = monumentos.",
                ["ed_help_move"] = "Arrastra un punto; los vecinos dentro del radio lo siguen. Luego 'Corregir espaciado y curvas' antes de guardar.",
                ["ed_help_height"] = "Arrastra arriba/abajo sobre un punto para subir o bajar la carretera dentro del radio.",
                ["ed_help_scenery"] = "Clic en un elemento para seleccionarlo, arrastrar para moverlo, Supr para borrarlo. Mayús+clic añade el tipo elegido, Ctrl+clic añade un árbol.",
                ["ed_help_add"] = "Tipo para Mayús+clic:", ["ed_help_pits"] = "Clic en un punto y fija ahí el inicio / fin de boxes. El lado de boxes es el de los garajes.",
                ["ed_help_start"] = "Clic en un punto y conviértelo en la línea de meta (la vuelta se rota; escenario y boxes la siguen).",
                ["ed_saving"] = "Guardar", ["ed_unsaved"] = "cambios sin guardar", ["ed_save_as"] = "Guardar como pista nueva", ["ed_history"] = "Versiones guardadas",
                ["ed_revert"] = "Volver al original (como se generó)", ["ed_snapshot"] = "queda una copia con fecha en history\\", ["ed_restored"] = "Cargado",
                ["ed_save_to_keep"] = "pulsa Guardar para conservarlo", ["ed_no_original"] = "aún no hay original guardado (no se sobrescribió nada)", ["ed_original"] = "el original",
                ["ed_exists"] = "ya existe una pista con ese nombre", ["ed_saved_as"] = "Guardada como pista nueva", ["ed_add_to_campaign"] = "añade su id a un campaign.json para correrla en una campaña; F9 ya la lista",
                ["ed_unsaved_q"] = "La pista tiene cambios sin guardar. ¿Guardarlos?", ["ed_discard"] = "Descartar", ["ed_cancel"] = "Cancelar",
                ["ed_scatter"] = "Sembrar (varios)", ["ed_place"] = "Colocar:", ["ed_place_kind"] = "edificio", ["ed_place_tree"] = "árbol", ["ed_place_asset"] = "modelo",
                ["ed_assets"] = "Biblioteca de modelos", ["ed_import"] = "Importar", ["ed_browse"] = "Buscar...", ["ed_rescan"] = "Releer", ["ed_imported"] = "Importado:", ["ed_import_failed"] = "No se pudo importar:",
                ["ed_import_hint"] = "Ruta de un .obj (Blender, MagicaVoxel, Blockbench, Tools\\asset_kit.py). Estilo: HCT15Content\\assets\\STYLE.txt", ["ed_import_track"] = "solo para esta pista",
                ["ed_osm"] = "Mapa OSM (calles reales)", ["ed_osm_opacity"] = "Opacidad", ["ed_osm_fit"] = "mapa ajustado a la pista, +/- {0} m donde se suavizó el camino",
                ["ed_osm_none"] = "No hay osm_overlay.json para esta pista - ejecuta Tools\\osm_overlay.py para dibujar las calles reales bajo el camino.",
                ["ed_asset_scale"] = "Escala", ["ed_asset_yaw"] = "Giro", ["ed_asset_missing"] = "modelo no encontrado en la biblioteca: se muestra como bloque magenta", ["ed_no_assets"] = "Aún no hay modelos: importa uno abajo o ejecuta Tools\\asset_kit.py",
                ["ed_help_scatter"] = "Arrastra para sembrar varios elementos dentro del pincel (fuera de la calle, separados). Shift+clic en un punto y Shift+clic en otro: una fila a ese lado de la calle. Ctrl+arrastrar borra.",
                ["ed_scatter_count"] = "Elementos por trazo", ["ed_scatter_spacing"] = "Separación mínima", ["ed_row_spacing"] = "Separación en fila", ["ed_row_dist"] = "desde el borde", ["ed_random_yaw"] = "giro aleatorio de los árboles",
                ["ed_row_end_hint"] = "Inicio de fila fijado: Shift+clic en el punto final (se usa el lado de la calle donde haces clic)", ["ed_placed"] = "colocados",
                ["ed_browser"] = "Explorador de modelos", ["ed_cat_all"] = "Todos", ["ed_cat_tree"] = "Árboles", ["ed_cat_building"] = "Edificios", ["ed_cat_landmark"] = "Monumentos", ["ed_cat_prop"] = "Objetos", ["ed_cat_game"] = "Objetos del juego",
                ["ed_browser_hint"] = "Clic en una tarjeta para elegirla (luego Mayús+clic en el mapa/vista 3D la coloca; Sembrar la pinta). Doble clic, o los botones Añadir, ponen una junto al punto seleccionado.",
                ["ed_chosen"] = "Elegido", ["ed_pick_hint"] = "Nada elegido aún: haz clic en una tarjeta.", ["ed_add_here"] = "Añadir junto al punto", ["ed_add_nearest"] = "el más cercano a la vista", ["ed_add_left"] = "izquierda de la calle", ["ed_add_right"] = "derecha de la calle",
                ["ed_use_shift"] = "Cerrar y colocar con Mayús+clic", ["ed_place_game"] = "objeto del juego",
                ["ed_load_game"] = "Cargar los objetos del juego del escenario {0}", ["ed_game_loading"] = "cargando los objetos del escenario...", ["ed_game_loaded"] = "objetos de {0} cargados",
                ["ed_game_none"] = "Aún no se han capturado objetos de este escenario. Pulsa el botón (el escenario se carga un momento en segundo plano) o corre la pista una vez; la lista y las miniaturas quedan en assets\\game_props\\.",
                ["ed_game_preview_hint"] = "Miniaturas de la última captura; pulsa el botón para cargar los modelos reales en la vista 3D (si no, los objetos del juego colocados se ven como bloques turquesa).",
                ["ed_game_rotation"] = "Giro en el juego", ["ed_game_note"] = "Los objetos del juego siguen la calle como el escenario propio; los SideDependent se giran hacia el tráfico a la izquierda. La escala se aplica, el giro no.",
                ["ed_pits_off"] = "Los boxes están desactivados para la campaña '{0}' (campaign.json \"Pits\": false): la calle de abajo queda en el archivo pero no se construye.",
                ["ed_finish_p2p"] = "Punto a punto: la carrera termina en la línea de meta magenta; la calle gris posterior es el regreso (no se corre).",
                ["ed_view_map"] = "Mapa 2D", ["ed_view_3d"] = "Vista 3D", ["ed_view_drive"] = "Recorrido",
                ["ed_drive_speed"] = "Velocidad", ["ed_drive_pause"] = "Pausa", ["ed_drive_resume"] = "Seguir", ["ed_drive_restart"] = "Reiniciar",
                ["ed_cam_chase"] = "Trasera", ["ed_cam_hood"] = "Capó", ["ed_cam_side"] = "Helicóptero", ["ed_focus_hint"] = "F encuadra la selección",
                ["ed_help_3d"] = "Botón der.: mirar  -  WASD / QE: volar (Shift = rápido)  -  rueda: avanzar  -  F: encuadrar  -  botón izq.: seleccionar / arrastrar  -  clic en el minimapa: saltar",
                ["ed_help_drive"] = "Espacio: pausa  -  + / -: velocidad  -  Inicio: reiniciar  -  C: cámara  -  flechas: avanzar / retroceder  -  clic en el minimapa: saltar  -  botón izq.: editar",
                ["ed_legend_3d"] = "Vista aproximada. Trazado rojo = curva demasiado cerrada para el motor. Banda a cuadros = meta. Franja gris + garajes = boxes. Poste amarillo = selección, anillo = pincel.",
            },
            ["pt"] = new Dictionary<string, string>
            {
                ["car_anda_andacollo_desc"] = "A mais pesada: a grande coroa de Andacollo. Lenta na largada, firme nas curvas, mais velas para o caminho.", ["car_anda_purisima_desc"] = "A mais leve: branco e azul-celeste. Larga rápido, solta nas curvas, poucas velas.", ["car_anda_dolores_desc"] = "Preto e roxo, carregadores de luto. Sem pressa, passo firme, muitas velas.", ["car_anda_rosario_desc"] = "Rosas cor-de-rosa e brancas. Equilibrada, larga um pouco mais rápido.", ["car_anda_san_rafael_desc"] = "O padroeiro de Illapel, com asas e cajado. A melhor largada, a menor aderência e menos velas.", ["car_anda_candelaria_desc"] = "Manto azul-petróleo, capa dourada e uma vela na mão: a Virgem de Copiapó. Constante, firme nas curvas, bastantes velas.", ["car_anda_tirana_desc"] = "Vermelho e dourado, carregadores nas cores das diabladas. A maior velocidade máxima, a menor aderência, poucas velas.", ["car_anda_santa_rosa_desc"] = "Hábito branco, manto preto e coroa de rosas: a santa de Pelequén. A melhor largada e a melhor aderência, a menor velocidade máxima.", ["car_anda_san_pedro_desc"] = "O padroeiro dos pescadores com suas chaves, levado por gente das enseadas. Lento na largada, a maior reserva de velas.",
                ["car_anda_name"] = "Andor de Nossa Senhora do Carmo", ["car_anda_andacollo_name"] = "Andor de Nossa Senhora de Andacollo", ["car_anda_purisima_name"] = "Andor da Imaculada", ["car_anda_dolores_name"] = "Andor de Nossa Senhora das Dores", ["car_anda_rosario_name"] = "Andor de Nossa Senhora do Rosário", ["car_anda_san_rafael_name"] = "Andor de São Rafael Arcanjo", ["car_anda_candelaria_name"] = "Andor de Nossa Senhora da Candelária", ["car_anda_tirana_name"] = "Andor de Nossa Senhora de La Tirana", ["car_anda_santa_rosa_name"] = "Andor de Santa Rosa de Lima", ["car_anda_san_pedro_name"] = "Andor de São Pedro", ["car_anda_desc"] = "Um andor da procissão de Illapel: oito carregadores de branco com as varas nos ombros, flores, velas e Nossa Senhora do Carmo no alto.", ["car_anda_variant_desc"] = "Outro andor da procissão, outra imagem no alto - os mesmos oito carregadores.",
                ["car_g151_name"] = "Picape da mina", ["car_g151_blanca_name"] = "Picape da mina (Glaciar)", ["car_g151_negra_name"] = "Picape da mina (Obsidiana)", ["car_g151_gris_name"] = "Picape da mina (Grafito)", ["car_g151_azul_name"] = "Picape da mina (Pacífico)", ["car_g151_race_name"] = "Picape da mina (Carmín)", ["car_g151_rubi_name"] = "Picape da mina (Granate)", ["car_g151_pimienta_name"] = "Picape da mina (Ají)", ["car_g151_variant_desc"] = "A das minas em outra cor de fábrica.", ["car_g151_desc"] = "A 4x4 cabine dupla vermelha das minas do Choapa: giroflex âmbar no teto e a bandeirola refletiva.", ["car_taxi_name"] = "Taxi chileno", ["car_micro_name"] = "Micro antigo de Santiago",
                ["car_taxi_v16_name"] = "Táxi sedã clássico", ["car_taxi_v16_desc"] = "O sedã clássico que rodou por Santiago vinte anos, preto com teto amarelo: leve, ágil e sem pressa para frear.",
                ["car_micro_roja_name"] = "Micro vermelho",
                ["car_micro_blanca_name"] = "Micro branco",
                ["car_micro_turquesa_name"] = "Micro turquesa",
                ["car_micro_azul_name"] = "Micro azul",
                ["car_micro_verde_name"] = "Micro verde",
                ["car_micro_naranja_name"] = "Micro laranja",
                ["car_micro_multicolor_name"] = "Micro multicolorido (linhas regionais)",
                ["car_taxi_desc"] = "Preto com teto amarelo, como todo taxi de Santiago. Sai devagar, mas conhece cada rua.",
                ["car_micro_desc"] = "O onibus amarelo das antigas linhas: pesado, barulhento e impossivel de ultrapassar numa curva.",
                ["car_micro_variant_desc"] = "O mesmo onibus das antigas linhas com outra pintura: pesado, barulhento e impossivel de ultrapassar numa curva.",
                ["car_carreta_name"] = "Carroca", ["car_carreta_desc"] = "A carroca puxada a cavalo do vale do Choapa, rodas de madeira e um huaso nas redeas: lenta na largada, imparavel na estrada velha para Illapel.",
                ["opt_loudness"] = "Volume comprimido", ["opt_loudness_off"] = "não", ["opt_tolls"] = "Pedágios (Rutas de Chile)", ["opt_traffic"] = "Trânsito nas estradas", ["opt_traffic_on"] = "sim",
                ["championship"] = "CAMPEONATO DE PILOTOS", ["of_rounds"] = "{0} de {1} etapas", ["pts"] = "pts",
                ["season_line"] = "TEMPORADA 2026 DE GRAN PREMIO  -  {0} GRANDES PRÊMIOS", ["no_results"] = "Ainda sem resultados: vença corridas para subir na tabela",
                ["loading"] = "CARREGANDO", ["preparing"] = "preparando", ["armed"] = "corrida personalizada pronta", ["scene_loaded"] = "cena da corrida carregada",
                ["game_loaded"] = "cena do jogo carregada", ["geometry"] = "geometria da pista reconstruída", ["baked"] = "pista calculada", ["initialised"] = "corrida inicializada",
                ["scenery"] = "cenário posicionado", ["power_unit"] = "unidade de potência", ["pit_stop"] = "PIT STOP", ["refuelled"] = "REABASTECIDO",
                ["pit_lane"] = "PIT LANE", ["round"] = "Etapa {0}", ["you"] = "você", ["now_playing"] = "TOCANDO AGORA",
            },
            ["fr"] = new Dictionary<string, string>
            {
                ["car_anda_andacollo_desc"] = "Le plus lourd : la grande couronne d'Andacollo. Lent au départ, stable en virage, le plus de cierges.", ["car_anda_purisima_desc"] = "Le plus léger : blanc et bleu ciel. Rapide au départ, glissant en virage, peu de cierges.", ["car_anda_dolores_desc"] = "Noir et violet, porteurs en deuil. Sans hâte, pas sûr, beaucoup de cierges.", ["car_anda_rosario_desc"] = "Roses roses et blanches. Équilibré, un peu plus vif au départ.", ["car_anda_san_rafael_desc"] = "Le patron d'Illapel, ailes et bâton. Le meilleur départ, le moins d'adhérence, le moins de cierges.", ["car_anda_candelaria_desc"] = "Robe bleu canard, cape dorée et un cierge à la main : la Vierge de Copiapó. Régulier, sûr en virage, beaucoup de cierges.", ["car_anda_tirana_desc"] = "Rouge et or, porteurs aux couleurs des diabladas. La plus grande vitesse de pointe, le moins d'adhérence, peu de cierges.", ["car_anda_santa_rosa_desc"] = "Habit blanc, manteau noir et couronne de roses : la sainte de Pelequén. Le meilleur départ et la meilleure adhérence, la vitesse la plus basse.", ["car_anda_san_pedro_desc"] = "Le patron des pêcheurs avec ses clés, porté par les gens des criques. Lent au départ, la plus grande réserve de cierges.",
                ["car_anda_name"] = "Brancard de Notre-Dame du Carmel", ["car_anda_andacollo_name"] = "Brancard de Notre-Dame d'Andacollo", ["car_anda_purisima_name"] = "Brancard de l'Immaculée", ["car_anda_dolores_name"] = "Brancard de Notre-Dame des Douleurs", ["car_anda_rosario_name"] = "Brancard de Notre-Dame du Rosaire", ["car_anda_san_rafael_name"] = "Brancard de saint Raphaël archange", ["car_anda_candelaria_name"] = "Brancard de Notre-Dame de la Chandeleur", ["car_anda_tirana_name"] = "Brancard de Notre-Dame de La Tirana", ["car_anda_santa_rosa_name"] = "Brancard de sainte Rose de Lima", ["car_anda_san_pedro_name"] = "Brancard de saint Pierre", ["car_anda_desc"] = "Un brancard de procession d'Illapel : huit porteurs en blanc, les brancards sur l'épaule, des fleurs, des cierges et Notre-Dame du Carmel au sommet.", ["car_anda_variant_desc"] = "Un autre brancard de la procession, une autre statue - les mêmes huit porteurs.",
                ["car_g151_name"] = "Pick-up de la mine", ["car_g151_blanca_name"] = "Pick-up de la mine (Glaciar)", ["car_g151_negra_name"] = "Pick-up de la mine (Obsidiana)", ["car_g151_gris_name"] = "Pick-up de la mine (Grafito)", ["car_g151_azul_name"] = "Pick-up de la mine (Pacífico)", ["car_g151_race_name"] = "Pick-up de la mine (Carmín)", ["car_g151_rubi_name"] = "Pick-up de la mine (Granate)", ["car_g151_pimienta_name"] = "Pick-up de la mine (Ají)", ["car_g151_variant_desc"] = "Le des mines dans une autre teinte d'usine.", ["car_g151_desc"] = "Le 4x4 double cabine rouge des mines du Choapa : gyrophare orange sur le toit et le fanion réfléchissant.", ["car_taxi_name"] = "Taxi chilien", ["car_micro_name"] = "Vieux micro de Santiago",
                ["car_taxi_v16_name"] = "Taxi berline classique", ["car_taxi_v16_desc"] = "La berline classique qui a roulé vingt ans dans Santiago, noire au toit jaune : légère, agile et jamais pressée de freiner.",
                ["car_micro_roja_name"] = "Micro rouge",
                ["car_micro_blanca_name"] = "Micro blanc",
                ["car_micro_turquesa_name"] = "Micro turquoise",
                ["car_micro_azul_name"] = "Micro bleu",
                ["car_micro_verde_name"] = "Micro vert",
                ["car_micro_naranja_name"] = "Micro orange",
                ["car_micro_multicolor_name"] = "Micro multicolore (lignes regionales)",
                ["car_taxi_desc"] = "Noir a toit jaune, comme tous les taxis de Santiago. Lent au depart, mais il connait chaque rue.",
                ["car_micro_desc"] = "Le bus jaune des anciennes lignes : lourd, bruyant et impossible a doubler en virage.",
                ["car_micro_variant_desc"] = "Le meme bus des anciennes lignes dans une autre livree : lourd, bruyant et impossible a doubler en virage.",
                ["car_carreta_name"] = "Charrette", ["car_carreta_desc"] = "La charrette tiree par un cheval de la vallee du Choapa, roues en bois et un huaso aux renes : lente au depart, inarretable sur la vieille route d'Illapel.",
                ["opt_loudness"] = "Volume compresse", ["opt_loudness_off"] = "non", ["opt_tolls"] = "Péages (Rutas de Chile)", ["opt_traffic"] = "Circulation sur les routes", ["opt_traffic_on"] = "oui",
                ["championship"] = "CHAMPIONNAT DES PILOTES", ["of_rounds"] = "{0} sur {1} manches", ["pts"] = "pts",
                ["season_line"] = "SAISON 2026 DE GRAND PRIX  -  {0} GRANDS PRIX", ["no_results"] = "Pas encore de résultats : gagnez des courses pour grimper au classement",
                ["loading"] = "CHARGEMENT", ["preparing"] = "préparation", ["armed"] = "course personnalisée prête", ["scene_loaded"] = "scène de course chargée",
                ["game_loaded"] = "scène de jeu chargée", ["geometry"] = "tracé reconstruit", ["baked"] = "tracé calculé", ["initialised"] = "course initialisée",
                ["scenery"] = "décor placé", ["power_unit"] = "unité de puissance", ["pit_stop"] = "ARRÊT AUX STANDS", ["refuelled"] = "RAVITAILLÉ",
                ["pit_lane"] = "VOIE DES STANDS", ["round"] = "Manche {0}", ["you"] = "vous", ["now_playing"] = "EN LECTURE",
            },
            ["de"] = new Dictionary<string, string>
            {
                ["car_anda_andacollo_desc"] = "Der schwerste: die große Krone von Andacollo. Langsam beim Start, sicher in Kurven, die meisten Kerzen.", ["car_anda_purisima_desc"] = "Der leichteste: Weiß und Himmelblau. Schnell beim Start, rutschig in Kurven, wenige Kerzen.", ["car_anda_dolores_desc"] = "Schwarz und Violett, Träger in Trauer. Gemächlich, trittsicher, viele Kerzen.", ["car_anda_rosario_desc"] = "Rosa und weiße Rosen. Ausgewogen, etwas flinker beim Start.", ["car_anda_san_rafael_desc"] = "Illapels Schutzpatron mit Flügeln und Stab. Der beste Start, der wenigste Grip, die wenigsten Kerzen.", ["car_anda_candelaria_desc"] = "Petrolfarbenes Gewand, goldener Mantel, eine Kerze in der Hand: die Jungfrau von Copiapó. Gleichmäßig, sicher in Kurven, viele Kerzen.", ["car_anda_tirana_desc"] = "Rot und Gold, Träger in den Farben der Diabladas. Die höchste Spitze, der wenigste Grip, wenige Kerzen.", ["car_anda_santa_rosa_desc"] = "Weißes Habit, schwarzer Mantel, Rosenkranz: die Heilige von Pelequén. Der beste Start und der beste Grip, die niedrigste Spitze.", ["car_anda_san_pedro_desc"] = "Der Patron der Fischer mit seinen Schlüsseln, getragen von Leuten der Buchten. Langsam beim Start, der größte Kerzenvorrat.",
                ["car_anda_name"] = "Tragaltar Unserer Lieben Frau vom Karmel", ["car_anda_andacollo_name"] = "Tragaltar der Jungfrau von Andacollo", ["car_anda_purisima_name"] = "Tragaltar der Unbefleckten", ["car_anda_dolores_name"] = "Tragaltar der Schmerzensmutter", ["car_anda_rosario_name"] = "Tragaltar der Rosenkranzkönigin", ["car_anda_san_rafael_name"] = "Tragaltar des Erzengels Raphael", ["car_anda_candelaria_name"] = "Tragaltar der Jungfrau von Candelaria", ["car_anda_tirana_name"] = "Tragaltar der Jungfrau von La Tirana", ["car_anda_santa_rosa_name"] = "Tragaltar der heiligen Rosa von Lima", ["car_anda_san_pedro_name"] = "Tragaltar des heiligen Petrus", ["car_anda_desc"] = "Ein Prozessionsaltar aus Illapel: acht Träger in Weiß, die Stangen auf den Schultern, Blumen, Kerzen und die Jungfrau vom Karmel obenauf.", ["car_anda_variant_desc"] = "Ein anderer Tragaltar der Prozession, ein anderes Bild obenauf - dieselben acht Träger.",
                ["car_g151_name"] = "Minen-Pick-up", ["car_g151_blanca_name"] = "Minen-Pick-up (Glaciar)", ["car_g151_negra_name"] = "Minen-Pick-up (Obsidiana)", ["car_g151_gris_name"] = "Minen-Pick-up (Grafito)", ["car_g151_azul_name"] = "Minen-Pick-up (Pacífico)", ["car_g151_race_name"] = "Minen-Pick-up (Carmín)", ["car_g151_rubi_name"] = "Minen-Pick-up (Granate)", ["car_g151_pimienta_name"] = "Minen-Pick-up (Ají)", ["car_g151_variant_desc"] = "Der der Minen in einer anderen Werksfarbe.", ["car_g151_desc"] = "Der rote 4x4 mit Doppelkabine der Minen im Choapa-Tal: gelbe Rundumleuchte und die reflektierende Fahne.", ["car_taxi_name"] = "Chilenisches Taxi", ["car_micro_name"] = "Alter Micro aus Santiago",
                ["car_taxi_v16_name"] = "Klassisches Limousinen-Taxi", ["car_taxi_v16_desc"] = "Die klassische Limousine, die zwanzig Jahre durch Santiago fuhr, schwarz mit gelbem Dach: leicht, wendig und nie in Eile beim Bremsen.",
                ["car_micro_roja_name"] = "Micro rot",
                ["car_micro_blanca_name"] = "Micro weiss",
                ["car_micro_turquesa_name"] = "Micro tuerkis",
                ["car_micro_azul_name"] = "Micro blau",
                ["car_micro_verde_name"] = "Micro gruen",
                ["car_micro_naranja_name"] = "Micro orange",
                ["car_micro_multicolor_name"] = "Micro bunt (Regionallinien)",
                ["car_taxi_desc"] = "Schwarz mit gelbem Dach, wie jedes Taxi in Santiago. Traege beim Start, kennt aber jede Strasse.",
                ["car_micro_desc"] = "Der gelbe Bus der alten Linien: schwer, laut und in der Kurve nicht zu ueberholen.",
                ["car_micro_variant_desc"] = "Derselbe Bus der alten Linien in anderer Lackierung: schwer, laut und in der Kurve nicht zu ueberholen.",
                ["car_carreta_name"] = "Pferdekarren", ["car_carreta_desc"] = "Der Pferdekarren aus dem Choapa-Tal, Holzraeder und ein Huaso an den Zuegeln: langsam am Start, unaufhaltsam auf der alten Strasse nach Illapel.",
                ["opt_loudness"] = "Lautheit (komprimiert)", ["opt_loudness_off"] = "aus", ["opt_tolls"] = "Mautstellen (Rutas de Chile)", ["opt_traffic"] = "Verkehr auf den Straßen", ["opt_traffic_on"] = "an",
                ["championship"] = "FAHRERWERTUNG", ["of_rounds"] = "{0} von {1} Rennen", ["pts"] = "Pkt.",
                ["season_line"] = "GRAND-PRIX-SAISON 2026  -  {0} GRANDS PRIX", ["no_results"] = "Noch keine Ergebnisse - gewinne Rennen, um aufzusteigen",
                ["loading"] = "LADEN", ["preparing"] = "Vorbereitung", ["armed"] = "eigene Strecke bereit", ["scene_loaded"] = "Rennszene geladen",
                ["game_loaded"] = "Spielszene geladen", ["geometry"] = "Streckengeometrie neu gebaut", ["baked"] = "Strecke berechnet", ["initialised"] = "Rennen initialisiert",
                ["scenery"] = "Kulisse platziert", ["power_unit"] = "Antriebseinheit", ["pit_stop"] = "BOXENSTOPP", ["refuelled"] = "AUFGETANKT",
                ["pit_lane"] = "BOXENGASSE", ["round"] = "Rennen {0}", ["you"] = "du", ["now_playing"] = "JETZT LÄUFT",
            },
            ["it"] = new Dictionary<string, string>
            {
                ["car_anda_andacollo_desc"] = "La più pesante: la grande corona di Andacollo. Lenta a partire, stabile in curva, più candele per la strada.", ["car_anda_purisima_desc"] = "La più leggera: bianco e celeste. Scatta rapida, scivola in curva, poche candele.", ["car_anda_dolores_desc"] = "Nero e viola, portatori a lutto. Senza fretta, passo sicuro, molte candele.", ["car_anda_rosario_desc"] = "Rose rosa e bianche. Equilibrata, parte un po' più svelta.", ["car_anda_san_rafael_desc"] = "Il patrono di Illapel, ali e bastone. La partenza migliore, la minore aderenza, meno candele.", ["car_anda_candelaria_desc"] = "Manto verde petrolio, cappa dorata e una candela in mano: la Vergine di Copiapó. Costante, sicura in curva, molte candele.", ["car_anda_tirana_desc"] = "Rossa e oro, portatori nei colori delle diabladas. La velocità massima più alta, la minore aderenza, poche candele.", ["car_anda_santa_rosa_desc"] = "Abito bianco, mantello nero e corona di rose: la santa di Pelequén. La partenza migliore e la migliore aderenza, la velocità più bassa.", ["car_anda_san_pedro_desc"] = "Il patrono dei pescatori con le sue chiavi, portato dalla gente delle calette. Lento a partire, la riserva di candele più grande.",
                ["car_anda_name"] = "Portantina della Madonna del Carmine", ["car_anda_andacollo_name"] = "Portantina della Madonna di Andacollo", ["car_anda_purisima_name"] = "Portantina dell'Immacolata", ["car_anda_dolores_name"] = "Portantina dell'Addolorata", ["car_anda_rosario_name"] = "Portantina della Madonna del Rosario", ["car_anda_san_rafael_name"] = "Portantina di san Raffaele arcangelo", ["car_anda_candelaria_name"] = "Portantina della Madonna della Candelora", ["car_anda_tirana_name"] = "Portantina della Madonna di La Tirana", ["car_anda_santa_rosa_name"] = "Portantina di santa Rosa da Lima", ["car_anda_san_pedro_name"] = "Portantina di san Pietro", ["car_anda_desc"] = "Una portantina della processione di Illapel: otto portatori in bianco con le stanghe in spalla, fiori, candele e la Madonna del Carmine in cima.", ["car_anda_variant_desc"] = "Un'altra portantina della processione, un'altra immagine in cima - gli stessi otto portatori.",
                ["car_g151_name"] = "Pick-up della miniera", ["car_g151_blanca_name"] = "Pick-up della miniera (Glaciar)", ["car_g151_negra_name"] = "Pick-up della miniera (Obsidiana)", ["car_g151_gris_name"] = "Pick-up della miniera (Grafito)", ["car_g151_azul_name"] = "Pick-up della miniera (Pacífico)", ["car_g151_race_name"] = "Pick-up della miniera (Carmín)", ["car_g151_rubi_name"] = "Pick-up della miniera (Granate)", ["car_g151_pimienta_name"] = "Pick-up della miniera (Ají)", ["car_g151_variant_desc"] = "L'delle miniere in un altro colore di fabbrica.", ["car_g151_desc"] = "Il 4x4 doppia cabina rosso delle miniere del Choapa: lampeggiante ambra sul tetto e la bandierina riflettente.", ["car_taxi_name"] = "Taxi cileno", ["car_micro_name"] = "Vecchio micro di Santiago",
                ["car_taxi_v16_name"] = "Taxi berlina classica", ["car_taxi_v16_desc"] = "La berlina classica che ha girato Santiago per vent'anni, nera con il tetto giallo: leggera, agile e mai di fretta a frenare.",
                ["car_micro_roja_name"] = "Micro rosso",
                ["car_micro_blanca_name"] = "Micro bianco",
                ["car_micro_turquesa_name"] = "Micro turchese",
                ["car_micro_azul_name"] = "Micro blu",
                ["car_micro_verde_name"] = "Micro verde",
                ["car_micro_naranja_name"] = "Micro arancione",
                ["car_micro_multicolor_name"] = "Micro multicolore (linee regionali)",
                ["car_taxi_desc"] = "Nero con il tetto giallo, come ogni taxi di Santiago. Parte piano, ma conosce ogni strada.",
                ["car_micro_desc"] = "Il bus giallo delle vecchie linee: pesante, rumoroso e impossibile da passare in curva.",
                ["car_micro_variant_desc"] = "Lo stesso bus delle vecchie linee con un altra livrea: pesante, rumoroso e impossibile da passare in curva.",
                ["car_carreta_name"] = "Carretto", ["car_carreta_desc"] = "Il carretto trainato da un cavallo della valle del Choapa, ruote di legno e un huaso alle redini: lento in partenza, inarrestabile sulla vecchia strada per Illapel.",
                ["opt_loudness"] = "Volume compresso", ["opt_loudness_off"] = "no", ["opt_tolls"] = "Pedaggi (Rutas de Chile)", ["opt_traffic"] = "Traffico sulle strade", ["opt_traffic_on"] = "sì",
                ["championship"] = "CAMPIONATO PILOTI", ["of_rounds"] = "{0} di {1} gare", ["pts"] = "pt",
                ["season_line"] = "STAGIONE 2026 DI GRAN PREMIO  -  {0} GRAN PREMI", ["no_results"] = "Ancora nessun risultato: vinci le gare per salire in classifica",
                ["loading"] = "CARICAMENTO", ["preparing"] = "preparazione", ["armed"] = "gara personalizzata pronta", ["scene_loaded"] = "scena di gara caricata",
                ["game_loaded"] = "scena di gioco caricata", ["geometry"] = "tracciato ricostruito", ["baked"] = "tracciato calcolato", ["initialised"] = "gara inizializzata",
                ["scenery"] = "scenario posizionato", ["power_unit"] = "power unit", ["pit_stop"] = "PIT STOP", ["refuelled"] = "RIFORNITO",
                ["pit_lane"] = "CORSIA BOX", ["round"] = "Gara {0}", ["you"] = "tu", ["now_playing"] = "IN RIPRODUZIONE",
            },
        };

        /// <summary>"Grand Prix of X" patterns; {0} is the country/place name in that language.</summary>
        private static readonly Dictionary<string, string> GpPattern = new Dictionary<string, string>
        {
            ["en"] = "{0} Grand Prix", ["es"] = "Gran Premio de {0}", ["pt"] = "Grande Prêmio de {0}", ["fr"] = "Grand Prix de {0}",
            ["de"] = "Großer Preis von {0}", ["it"] = "Gran Premio d'{0}",
        };

        public static string Lang
        {
            get
            {
                try
                {
                    string l = TextLocalization.CurrentLanguage;
                    if (string.IsNullOrEmpty(l)) return "en";
                    l = l.ToLowerInvariant();
                    if (l.StartsWith("pt")) return "pt";
                    if (l.Length > 2) l = l.Substring(0, 2);
                    return T.ContainsKey(l) ? l : "en";
                }
                catch (System.Exception) { return "en"; }
            }
        }

        /// <summary>2.0.28: whether the key has a text (in English at least).</summary>
        public static bool Has(string key) { LoadCampaignTexts(); return key != null && T["en"].ContainsKey(key); }

        private static bool _campaignTexts;

        /// <summary>2.4.3: a campaign's own texts - HCT15Content\campaigns\<folder>\texts.json, {"en": {"key": "text", ...},
        /// "es": {...}, ...} - are added to the mod's (the names of a campaign's own cars, for example), so no campaign's
        /// texts live in the code. A key the mod already has keeps the mod's text.</summary>
        private static void LoadCampaignTexts()
        {
            if (_campaignTexts || string.IsNullOrEmpty(ContentRegistry.ContentDir)) return;
            _campaignTexts = true;
            try
            {
                string camps = System.IO.Path.Combine(ContentRegistry.ContentDir, "campaigns");
                if (!System.IO.Directory.Exists(camps)) return;
                foreach (string dir in System.IO.Directory.GetDirectories(camps))
                {
                    string f = System.IO.Path.Combine(dir, "texts.json");
                    if (!System.IO.File.Exists(f)) continue;
                    try
                    {
                        var m = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, string>>>(System.IO.File.ReadAllText(f));
                        if (m == null) continue;
                        int n = 0;
                        foreach (var lang in m)
                        {
                            if (lang.Value == null) continue;
                            Dictionary<string, string> d;
                            if (!T.TryGetValue(lang.Key, out d)) T[lang.Key] = d = new Dictionary<string, string>();
                            foreach (var kv in lang.Value) if (!d.ContainsKey(kv.Key)) { d[kv.Key] = kv.Value; n++; }
                        }
                        UnityEngine.Debug.Log("[HCT15] texts: " + n + " from campaigns\\" + System.IO.Path.GetFileName(dir) + "\\texts.json");
                    }
                    catch (System.Exception ex) { UnityEngine.Debug.LogWarning("[HCT15] texts: " + f + ": " + ex.Message); }
                }
            }
            catch (System.Exception) { }
        }

        public static string Get(string key)
        {
            LoadCampaignTexts();
            Dictionary<string, string> d;
            string s;
            if (T.TryGetValue(Lang, out d) && d.TryGetValue(key, out s)) return s;
            if (T["en"].TryGetValue(key, out s)) return s;
            return key;
        }

        public static string Get(string key, params object[] args)
        {
            try { return string.Format(Get(key), args); } catch (System.Exception) { return Get(key); }
        }

        /// <summary>
        /// Picks the translation of a race/city name from a "DisplayNames" map ({"en":..,"es":..});
        /// falls back to the English/plain display name.
        /// </summary>
        public static string Pick(Dictionary<string, string> names, string fallback)
        {
            if (names != null)
            {
                string s;
                if (names.TryGetValue(Lang, out s) && !string.IsNullOrEmpty(s)) return s;
                if (names.TryGetValue("en", out s) && !string.IsNullOrEmpty(s)) return s;
            }
            return fallback;
        }
    }
}
