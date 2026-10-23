using System;
using System.Collections.Generic;
using System.IO;
using Aquiris.HorizonChase.Data;
using Aquiris.HorizonChase.LiveTrials;
using Aquiris.HorizonChase.Model.Game;
using Aquiris.HorizonChase.Input.Model;
using Aquiris.HorizonChase.NewUI.Component;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// Cars of our own: a Chilean taxi and one of the old Santiago micros.
    ///
    /// A retail car prefab is  &lt;car&gt;/root/sub_root/{high, med, low, first_person}  - the body as three
    /// levels of detail - plus the wheels, the shadow, the brake lights, the colliders and the nitro
    /// effects. So a new car does not need a new prefab: one of the retail cars is cloned, and the body
    /// mesh of every level of detail is replaced by a model from the asset library
    /// (HCT15Content\assets\models\&lt;id&gt;, built by Tools\asset_kit.py). Everything the game expects -
    /// wheels that spin, the shadow, the lights, the collision box - is the donor's and keeps working.
    ///
    /// The model is rotated (its nose is modelled towards +X, the car's is +Z), scaled to the donor's
    /// own length times "Length", and set down on the donor's ground plane. CarColorDataList must not
    /// swap the mesh back for a colour skin, so LoadMeshFilters refuses these cars (IsStreetCarId).
    ///
    /// The list is HCT15Content\cars\streetcars.json - written on the first run and editable: model,
    /// donor, performance, length, and whether the car may show up as an opponent.
    /// </summary>
    public static class StreetCars
    {
        [Serializable]
        public class CarDef
        {
            public string Id = "";            // car id / prefab name (the race views know cars by this)
            public string Model = "";         // asset library id of the body (no wheels)
            public string Donor = "";         // retail car to clone: ItemId or Name; empty = the first suitable one
            public string NameKey = "";       // HCT15\Loc.cs key for the name shown in car selection
            public string DescKey = "";
            public bool Enabled = true;
            public bool AsEnemy;              // may be drawn as an opponent in the mod campaigns
            public float Performance = 0.85f; // top speed / acceleration against the donor's
            public float Length = 1f;         // body length against the donor's
            public float Width = 1f;          // body width against the donor's
            public float Height = 1f;         // body height against the donor's (1 = as low as the retail cars)
            public float WheelSpread;         // wheelbase against the donor's (0 = follow Length)
            public float[] Axles;             // 2.0.14: the body model's own axle positions along its nose axis (the model's units, nose positive): each wheel group is put on its arch; WheelSpread is then moot
            public float WheelScale = 1f;     // bigger wheels for a bus
            public float Lift;                // raise the body off the donor's ground plane (units)
            public float Yaw = 90f;           // the model's nose (+X in the file, -X after the importer's mirror) to +Z
            public float CameraUp, CameraBack;// chase camera, for a tall body
            public float BubbleUp;            // extra height for the speech bubble, on top of the lift the race
                                              // works out for itself (BubbleLift)
            public bool BubbleEdge;           // hang the bubble at the body edge nearest the camera instead of lifting it clear (the micro: a long body put it in the sky)
            public bool BubbleCorner;         // 2.0.14: the PLAYER's own bubbles at the top left of their view instead of over the car (the micro: too big to see them over)
            public float BubbleClear;         // also slide the bubble this far towards the camera; negative turns
                                              // the whole thing off and leaves it where the donor had it
            // An empty Model keeps the donor's own body, and these repaint it in two tones (RepaintBody):
            public string PaintLow = "";      // the paint below PaintSplit, "#RRGGBB" as the mesh stores it
            public string PaintHigh = "";     // the paint above it (roof and pillars)
            public float PaintSplit = 0.72f;  // where the two meet, as a fraction of the body's height
            public float PaintRoof;           // a glass roof becomes painted above this fraction (0 = leave it)
            // Only Tools\car_repaint.py reads these (the game loads its bake): exact colour cuts and the plate.
            public string PaintCuts = "";     // "y > 0.825 - 0.04*z; z > -1.42": PaintHigh where all of them hold
            public string Plate = "";         // painted in PaintHigh on the flanks and in PaintLow on the roof
            public string PlateSide = "";     // "z, y, letter height" of the flank lettering
            public string PlateRoof = "";     // "z, text width" of the roof lettering
            // A SPRITE car (SpriteCars.cs): the donor keeps its wheels, shadow and collider, the body is a quad that
            // faces the camera and shows the frames of cars\sprites\<Sprite>\ - the four retro sprite cars.
            public string Sprite = "";        // folder under cars\sprites, empty = a mesh body
            public string Campaigns = "";     // "top_gear": only selectable (and a rival) inside these mod campaigns; empty = everywhere
            public float LeanStep = 5f;       // degrees of heading against the camera per lean step (the camera fallback; 3 steps)
            public float LeanSign = 1f;       // -1 if the lean frames turn the wrong way
            public float SpriteScale = 1f;    // 1 = the sprite car as big as its 3-D car in the world (the sheet's 64 px frame = 2.0 units), every car by the same law; 1.5 = half again
            public float SpriteDown;          // lowers the sprite this many units below the car's ground point (0 = its wheels on the ground)
            public string Taunts = "";        // another car whose taunts.<id>.txt this one speaks with (the micro's colour variants: "cl_micro")
            public string VariantOf = "";     // this entry is a COLOUR of that car, not a car of its own: its body is one of the bodies the
                                              // player flips through with up/down in car selection (the micro's liveries, the taxi's V16)
            public float Shift;               // 2.0.11: move the fitted body forward (units; the carreta's horse runs ahead of the donor's box, its cart sits where the van's body was)
            public string NitroModel = "";    // 2.0.11: asset-library model this car's driver sees instead of the nitro bottles (the carreta: fardo_nitro, glowing green)
            public bool NitroOwnColours;      // 2.1.5: the NitroModel keeps its own colours (the andas' bottle: white label, black X) instead of being drawn green; the halos stay
            public string FuelModel = "";     // 2.0.11: the same for the fuel cans (the carreta: fardo_nitro in its own colours)
            public string CoinModel = "";     // 2.0.15: the same for the coins (the carreta: zanahoria - 2026-10-08: for the carretas the coins are carrots)
            public string EngineSound = "";   // 2.0.11: "gallop" - synthesised hooves instead of the engine (GallopSound.cs)
            public bool HideWheels;
            public float Bob;                 // 2.0.11: the body bobs this many units at the trot, faster with speed (the carreta; 0 = still)           // 2.0.11: the body brings its own wheels (the carreta's two wooden ones): the donor's tyres are not drawn
            public float WheelTrack;          // 0 = a Model body gets the donor's wheels moved out to its own flank; 1 = the donor's track; else a multiplier
            public float Lean;                // 2.0.24: degrees the body leans into a bend at full lock, and it rides the swell (WaterRide; the jet skis 16, 0 = a car)
            public bool OnlyListed;           // 2.0.24: selectable only in a round whose "Cars" names it (the jet skis: the beaches of the Quinta Región)
            public float Spray;               // 2.0.24: the rooster tail, side spray and wake of a jet ski (WaterRide; 1 = a jet ski's, 0 = none)
            public float SelectScale = 1f;    // 2.0.13: how big the car shows on the car-selection turntable against a retail car (the micro 1.2: half again the bus it was, yet inside the screen's stats); 1 in split screen
            // 2.0.28 (the andas walk, no more than 5 km/h; each anda is a colour variant with its own stats):
            public float TopSpeedKph;         // the game's own top speed (km/h before its x0.55 masking), unclamped; 0 = donor x Performance
            public float ZeroToMax;           // seconds from standstill to top speed; 0 = donor / Performance^2
            public float Grip = 1f;           // x the turning strength
            public float Tank;                // fuel tank capacity; 0 = the donor's
            public float ShownSpeed;          // x the speed the HUD and car selection show (the andas 0.16: their true walking pace); 0 = as the game shows it
        }

        [Serializable]
        public class CarsFile
        {
            public string Note = "Edit and restart the game. Model is a folder under HCT15Content\\assets\\models (the body without wheels), or empty to keep the donor's own body - PaintLow/PaintHigh (\"#RRGGBB\", the vertex colour the game stores, which renders about twice as bright) then repaint its body colour in two tones meeting at PaintSplit (a fraction of the body height), and PaintRoof above 0 paints a glass roof above that fraction; glass, lights, grille and tyres keep theirs. Donor is a retail car (ItemId or Name), empty = the first suitable one - pick one shaped like the car you are building ('kombi' is the van, 'legend' a saloon), because the wheels, the shadow and the lights stay the donor's. Performance ~0.7..1.0 scales top speed and acceleration. Length, Width and Height each scale the body against the donor's own box, so Height 1 sits as low as every retail car and 1.5 is half again as tall; Lift raises it off the ground, Yaw turns the model (90 = a model whose nose is +X in the .obj). The speech bubble lifts itself over the body as the race camera sees it, so a big body cannot be drawn over its own insults; BubbleUp adds to that lift, BubbleClear also slides the bubble towards the camera, and -1 turns both off and leaves the bubble where the donor had it; BubbleEdge true hangs it at the roof edge nearest the camera instead (the micro). Cars\\_donors.json and cars\\_bubbles.json are written beside this file with the measurements the Car Editor reads. Sprite = a folder under cars\\sprites (the retro sprite cars): the body is then a camera-facing pixel sprite instead of a mesh, LeanStep degrees of heading per lean frame, LeanSign -1 if it leans the wrong way, SpriteScale 1 = the sprite as big as its 3-D car in the world (every car by the same law, by its distance), 1.5 = half again; Campaigns = the mod campaigns the car is selectable in and races as a rival (empty = everywhere).";
            public List<CarDef> Cars = new List<CarDef>();
        }

        private static List<CarModel> _cars;
        private static bool _tried;
        private static readonly Dictionary<CarModelDataList, CarModelDataList> _merged = new Dictionary<CarModelDataList, CarModelDataList>();
        private static readonly HashSet<string> _ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, float> _rise = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, CarDef> _def = new Dictionary<string, CarDef>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Bounds> _body = new Dictionary<string, Bounds>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Vector3> _cam = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> _placed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> _donorName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // id -> the retail car it was built on (CarSting)
        private static GameObject _holder;

        /// <summary>The retail car this car was built on (its Name, as the sound groups are named), or null.</summary>
        public static string DonorNameOf(string carId)
        {
            string d;
            if (string.IsNullOrEmpty(carId)) return null;
            string id = BaseId(carId);
            if (_donorName.TryGetValue(id, out d)) return d;
            CarDef def;
            if (_def.TryGetValue(id, out def) && def != null && !string.IsNullOrEmpty(def.VariantOf) && _donorName.TryGetValue(def.VariantOf, out d)) return d;
            return null;
        }

        /// <summary>
        /// The race camera behind the car, from camera_prefab/camera in game.unity: 1.76 up, 5.21 back, level,
        /// FOV 60. Only the bubble and the Car Editor use it - the game itself reads the prefab.
        /// </summary>
        public static readonly Vector3 RaceCamera = new Vector3(0f, 1.76f, -5.21f);

        private static readonly CarDef[] Builtin =
        {
            // Donors are chosen by shape, not by speed: the wheels, the shadow, the lights and the collider stay
            // the donor's, so a saloon sits on a saloon and the micro on the van. Length/Width/Height are against
            // that donor's own body box - Height 1 keeps our cars as low as the rest of the grid.
            // The taxi is the retail Specter ('ghost') itself, repainted black below the beltline and yellow above it.
            new CarDef { Id = "cl_taxi", Model = "", Donor = "ghost", NameKey = "car_taxi_name", DescKey = "car_taxi_desc",
                         Performance = 0.89f, PaintLow = "#0F1114", PaintHigh = "#8C6B04", PaintSplit = 0.72f, PaintRoof = 0.88f,
                         PaintCuts = "y > 0.825 - 0.04*z; z < 0.92 - 0.22*abs(x); z > -1.42",
                         Plate = "DKKD·56", PlateSide = "-0.35, 0.52, 0.09", PlateRoof = "-0.30, 0.80", AsEnemy = false },
            new CarDef { Id = "cl_micro", Model = "micro_amarilla_body", Donor = "kombi", NameKey = "car_micro_name", DescKey = "car_micro_desc",
                         Performance = 0.84f, Length = 2.03f, Width = 1.53f, Height = 1.68f, WheelSpread = 1f, Axles = new[] { 2.9f, -3.05f }, WheelScale = 1.65f, SelectScale = 1.2f,
                         CameraUp = 0.7f, CameraBack = 2.0f, AsEnemy = false, BubbleEdge = true, BubbleCorner = true },
            // The same micro in the other liveries of the old lines (2026-09-24): the same body shape from
            // asset_kit's MICRO_LIVERIES, the same donor and fit, the yellow one's taunts. Aventura Chile's first
            // race is a grid of nothing but these.
            // A second taxi body (2026-09-24): a textured asset-library body
            // (its roof painted yellow), on the Specter's wheels like the first taxi.
            new CarDef { Id = "cl_taxi_v16", Model = "nissan_v16_body", Donor = "ghost", NameKey = "car_taxi_v16_name", DescKey = "car_taxi_v16_desc",
                         VariantOf = "cl_taxi", Taunts = "cl_taxi", Performance = 0.89f, Length = 1f, Width = 1f, Height = 1f, AsEnemy = false },
            // The carreta (2026-10-05: a horse-drawn cart, generated from scratch to match the game's style):
            // asset_kit's carreta_body - cart, horse and huaso, its own two wooden wheels, so the van's tyres are hidden.
            // The grid of the Los Vilos -> Illapel stage of Rutas de Chile is nothing but these (its round's Cars list).
            new CarDef { Id = "cl_carreta", Model = "carreta_body", Donor = "kombi", NameKey = "car_carreta_name", DescKey = "car_carreta_desc",
                         Performance = 0.8f, Length = 1.45f, Width = 1.22f, Height = 1.25f, HideWheels = true, Shift = 0.9f, Bob = 0.07f, NitroModel = "fardo_nitro", FuelModel = "fardo_nitro", CoinModel = "zanahoria", EngineSound = "gallop",
                         CameraUp = 0.4f, CameraBack = 1.6f, AsEnemy = false, BubbleEdge = true },
            // ten more carretas (2026-10-06: at least 10 variants, with different colours, horse types, wheels and speeds)
            CarretaVariant("potro", 0.87f),
            CarretaVariant("alazan", 0.84f),
            CarretaVariant("bayo", 0.82f),
            CarretaVariant("tordillo", 0.82f),
            CarretaVariant("overo", 0.83f),
            CarretaVariant("mula", 0.78f),
            CarretaVariant("percheron", 0.76f),
            CarretaVariant("bueyes", 0.70f),
            CarretaVariant("fonda", 0.83f),
            CarretaVariant("lechero", 0.81f),
            // The jet skis (2026-10-10: a track in Viña del Mar, on the beach's waves, in custom jet skis): asset_kit's
            // jetski_body - hull, rider, race number - on the Mini's engine and shadow, its tyres hidden; WaterRide leans it, heaves it
            // on the swell and throws the spray. The grid of "Viña del Mar en jetski" (campaigns\chile round 116) is nothing but these.
            // 2026-10-11 (not variants of one another - each has its own stats): eight cars side by side
            // in car selection, each on a donor of its own - its top speed, acceleration, grip, tank and engine sound - with
            // the body fitted to the same size on every donor (Length/Width/Height against that donor's box)
            Jetski("", "mini", 0.90f, 3.10f, 1.45f, 1.32f), Jetski("turquesa", "beetle", 0.92f, 3.34f, 1.42f, 1.40f),
            Jetski("neon", "fury", 0.90f, 3.74f, 1.75f, 1.00f), Jetski("atardecer", "trueno", 0.91f, 3.71f, 1.74f, 1.21f),
            Jetski("negro", "haze", 0.91f, 3.98f, 1.82f, 1.03f), Jetski("rosa", "mgtc", 0.92f, 3.70f, 1.51f, 1.42f),
            Jetski("oro_cielo", "summer", 0.91f, 3.88f, 1.88f, 1.05f), Jetski("verde", "dustdriver", 0.91f, 3.60f, 1.68f, 1.11f),
            MicroVariant("roja"), MicroVariant("blanca"), MicroVariant("turquesa"), MicroVariant("azul"),
            MicroVariant("verde"), MicroVariant("naranja"), MicroVariant("multicolor"),
            // The four retro (16-bit retro) cars as sprites, only in the retro campaign, where they are also the
            // rivals. Donors matched to the SNES menu stats over all four axes (campaigns\top_gear\README.txt):
            // car 1 fastest/thirstiest/least grip, car 2 slowest but quickest off the line, grips and sips.
            new CarDef { Id = "tg_car1", Sprite = "tg_car1", SpriteScale = 1f, Donor = "madbot", NameKey = "car_tg1_name", DescKey = "car_tg1_desc", Performance = 1f, Campaigns = "top_gear", AsEnemy = true },
            new CarDef { Id = "tg_car2", Sprite = "tg_car2", SpriteScale = 1f, Donor = "mini", NameKey = "car_tg2_name", DescKey = "car_tg2_desc", Performance = 1f, Campaigns = "top_gear", AsEnemy = true },
            new CarDef { Id = "tg_car3", Sprite = "tg_car3", SpriteScale = 1f, Donor = "eclipse", NameKey = "car_tg3_name", DescKey = "car_tg3_desc", Performance = 1f, Campaigns = "top_gear", AsEnemy = true },
            new CarDef { Id = "tg_car4", Sprite = "tg_car4", SpriteScale = 1f, Donor = "explorer", NameKey = "car_tg4_name", DescKey = "car_tg4_desc", Performance = 1f, Campaigns = "top_gear", AsEnemy = true },
        };

        /// <summary>A carreta of asset_kit's CARRETA_VARIANTS: its own body, horse and load, its own speed; the first one's taunts.</summary>
        private static CarDef CarretaVariant(string v, float performance)
        {
            // a COLOUR of cl_carreta (2026-10-06: variants of the SAME car, with the same stats): up / down in car
            // selection, a random one for each rival; the first carreta's speed (performance kept only as a note)
            return new CarDef { Id = "cl_carreta_" + v, Model = "carreta_" + v + "_body", Donor = "kombi", NameKey = "car_carreta_" + v + "_name", DescKey = "car_carreta_" + v + "_desc",
                                VariantOf = "cl_carreta", Taunts = "cl_carreta", Performance = 0.8f, Length = 1.45f, Width = 1.22f, Height = 1.25f, HideWheels = true, Shift = 0.9f, Bob = 0.07f,
                                CameraUp = 0.4f, CameraBack = 1.6f, AsEnemy = false, BubbleEdge = true };
        }

        /// <summary>Every carreta, the first one first - the grid of the Los Vilos -> Illapel stage.</summary>
        public static readonly string[] CarretaIds = { "cl_carreta", "cl_carreta_potro", "cl_carreta_alazan", "cl_carreta_bayo", "cl_carreta_tordillo", "cl_carreta_overo", "cl_carreta_mula", "cl_carreta_percheron", "cl_carreta_bueyes", "cl_carreta_fonda", "cl_carreta_lechero" };

        /// <summary>A jet ski of asset_kit's JETSKI_LIVERIES ("" = the red "Viña" one): a car of its own on its donor, the body
        /// fitted to 1.785 x 0.725 x 0.955 whatever the donor's box (dl, dw, dh) - 2.0.25: half the 2.0.24 size (they looked almost
        /// twice as big as they should; streetcars.json carries the halved numbers).</summary>
        private static CarDef Jetski(string livery, string donor, float performance, float dl, float dw, float dh)
        {
            bool first = livery == "";
            string id = first ? "cl_jetski" : "cl_jetski_" + livery;
            return new CarDef { Id = id, Model = first ? "jetski_body" : "jetski_" + livery + "_body", Donor = donor,
                                NameKey = "car_" + id.Substring(3) + "_name", DescKey = "car_" + id.Substring(3) + "_desc", Taunts = first ? "" : "cl_jetski",
                                Campaigns = "chile", Performance = performance, Length = 1.785f / dl, Width = 0.725f / dw, Height = 0.955f / dh,
                                HideWheels = true, Lean = 30f, Spray = 1f, CameraBack = 0.4f, AsEnemy = false, OnlyListed = true };
        }

        /// <summary>The eight jet skis - the grid of the beaches of the Quinta Región.</summary>
        public static readonly string[] JetskiIds = { "cl_jetski", "cl_jetski_turquesa", "cl_jetski_neon", "cl_jetski_atardecer", "cl_jetski_negro", "cl_jetski_rosa", "cl_jetski_oro_cielo", "cl_jetski_verde" };

        private static CarDef MicroVariant(string livery)
        {
            return new CarDef { Id = "cl_micro_" + livery, Model = "micro_" + livery + "_body", Donor = "kombi",
                                NameKey = "car_micro_" + livery + "_name", DescKey = "car_micro_variant_desc", VariantOf = "cl_micro", Taunts = "cl_micro",
                                Performance = 0.84f, Length = 2.03f, Width = 1.53f, Height = 1.68f, WheelSpread = 1f, Axles = new[] { 2.9f, -3.05f }, WheelScale = 1.65f, SelectScale = 1.2f,
                                CameraUp = 0.7f, CameraBack = 2.0f, AsEnemy = false, BubbleEdge = true, BubbleCorner = true };
        }

        /// <summary>The ids of every micro livery, the yellow one first - Aventura Chile's all-micro grid.</summary>
        public static readonly string[] MicroIds = { "cl_micro", "cl_micro_roja", "cl_micro_blanca", "cl_micro_turquesa", "cl_micro_azul", "cl_micro_verde", "cl_micro_naranja", "cl_micro_multicolor" };

        /// <summary>The model this car sees its nitro pickups as (CarDef.NitroModel; a colour asks its car), or null.</summary>
        public static string NitroModelOf(string carId) { return OwnOrBase(carId, d => d.NitroModel); }

        /// <summary>2.1.5: CarDef.NitroOwnColours of this car or the car it is a colour of.</summary>
        public static bool NitroOwnColoursOf(string carId) { return OwnOrBase(carId, d => d.NitroOwnColours ? "1" : null) != null; }

        /// <summary>The model this car sees its fuel cans as (CarDef.FuelModel), or null.</summary>
        public static string FuelModelOf(string carId) { return OwnOrBase(carId, d => d.FuelModel); }

        /// <summary>The model this car sees its coins as (CarDef.CoinModel), or null.</summary>
        public static string CoinModelOf(string carId) { return OwnOrBase(carId, d => d.CoinModel); }

        /// <summary>CarDef.EngineSound ("gallop"), or null for the donor's engine.</summary>
        public static string EngineSoundOf(string carId) { return OwnOrBase(carId, d => d.EngineSound); }

        /// <summary>A string field of a car - a colour asks its car; a streetcars.json written before the field existed,
        /// the built-in entry.</summary>
        private static string OwnOrBase(string carId, Func<CarDef, string> f)
        {
            CarDef d;
            if (string.IsNullOrEmpty(carId) || !_def.TryGetValue(BaseId(carId), out d) || d == null) return null;
            if (string.IsNullOrEmpty(f(d)) && !string.IsNullOrEmpty(d.VariantOf) && _def.TryGetValue(d.VariantOf, out d) && d != null) { }
            if (d != null && !string.IsNullOrEmpty(f(d))) return f(d);
            string id = d != null ? d.Id : BaseId(carId);
            foreach (CarDef bi in Builtin) if (bi != null && string.Equals(bi.Id, id, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(f(bi))) return f(bi);
            return null;
        }

        /// <summary>CarDef.SelectScale of a car on the selection turntable (a colour asks its car), 1 when unknown.</summary>
        public static float SelectScaleOf(CarModel car)
        {
            try
            {
                if (car == null) return 1f;
                CarDef d = null;
                string id = BaseId(car.ItemId ?? "");
                if (!_def.TryGetValue(id, out d) || d == null)
                {
                    id = BaseId(car.ItemName ?? "");
                    if (id.StartsWith("hct15_car_")) id = id.Substring(10);
                    if (!_def.TryGetValue(id, out d) || d == null) return 1f;
                }
                if (Mathf.Abs(d.SelectScale - 1f) < 0.01f && !string.IsNullOrEmpty(d.VariantOf)) { CarDef b; if (_def.TryGetValue(d.VariantOf, out b) && b != null) d = b; }
                return d.SelectScale > 0.1f ? d.SelectScale : 1f;
            }
            catch (Exception) { return 1f; }
        }

        /// <summary>2.0.26: the car a colour id belongs to (CarDef.VariantOf), or the id itself.</summary>
        public static string VariantBase(string carId)
        {
            CarDef d;
            return carId != null && _def.TryGetValue(carId, out d) && d != null && !string.IsNullOrEmpty(d.VariantOf) ? d.VariantOf : carId;
        }

        /// <summary>The car whose taunt file this car speaks with (CarDef.Taunts), or null for its own.</summary>
        public static string TauntsOf(string carId)
        {
            CarDef d;
            return carId != null && _def.TryGetValue(carId, out d) && d != null && !string.IsNullOrEmpty(d.Taunts) && !string.Equals(d.Taunts, carId, StringComparison.OrdinalIgnoreCase) ? d.Taunts : null;
        }

        public static string CarsDir
        {
            get
            {
                string d = Path.Combine(ContentRegistry.ContentDir ?? "", "cars");
                try { Directory.CreateDirectory(d); } catch (Exception) { }
                return d;
            }
        }

        /// <summary>The mod's own road cars, built once from the retail list.</summary>
        public static List<CarModel> Cars(CarModelDataList main)
        {
            if (_cars == null && !_tried) Build(main);
            return _cars ?? new List<CarModel>();
        }

        public static bool IsStreetCarId(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            int paren = id.IndexOf('(');
            if (paren > 0) id = id.Substring(0, paren).Trim();
            return _ids.Contains(id);
        }

        /// <summary>The ids of the cars we added - Taunts.Refresh re-registers their lines on a language change.</summary>
        public static IEnumerable<string> Ids { get { return _ids; } }

        public static bool IsStreetCar(CarModel car)
        {
            return car != null && _cars != null && _cars.Contains(car);
        }

        /// <summary>The list the game asks for, with our cars added (cached per source list).</summary>
        public static CarModelDataList MergedList(CarModelDataList main)
        {
            if (main == null) return null;
            CarModelDataList hit;
            if (_merged.TryGetValue(main, out hit) && hit != null) return hit;
            List<CarModel> ours = Cars(main);
            if (ours.Count == 0) return main;
            CarModelDataList merged = UnityEngine.Object.Instantiate(main);
            merged.name = "CarModelDataList_HCT15_Street";
            merged.CarModels = new List<CarModel>(main.CarModels);
            foreach (CarModel c in ours)
            {
                bool present = false;
                foreach (CarModel m in merged.CarModels) if (m != null && m.ItemId == c.ItemId) { present = true; break; }
                if (!present) merged.CarModels.Add(c);
            }
            merged.IgnoredCarModelIds = new List<string>(main.IgnoredCarModelIds ?? new List<string>());
            merged.PlayerOnlyCars = new List<string>(main.PlayerOnlyCars ?? new List<string>());
            merged.DLCCars = new List<string>(main.DLCCars ?? new List<string>());
            _merged[main] = merged;
            _merged[merged] = merged;
            Debug.Log("[HCT15] street cars: merged car list has " + merged.CarModels.Count + " cars (" + ours.Count + " of ours)");
            return merged;
        }

        // ------------------------------------------------------------------ building

        // 2.0.28: stats per car and per colour -------------------------------------------------------------------------
        private static readonly Dictionary<string, float[]> _donorStats = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);   // base id -> donor max, 0-max, turning, tank

        /// <summary>Top speed, acceleration, turning and tank of 'car' from entry d over its donor's numbers.</summary>
        private static void SetStats(CarModel car, CarDef d, float[] ds)
        {
            float p = Mathf.Clamp(d.Performance, 0.5f, 1.2f);
            car.MaxSpeedInKPH = d.TopSpeedKph > 0f ? d.TopSpeedKph : ds[0] * p;
            car.ZeroToMaxSpeedInSeconds = d.ZeroToMax > 0f ? d.ZeroToMax : ds[1] / (p * p);
            car.TurningStrength = ds[2] * (0.92f + 0.08f * p) * (d.Grip > 0f ? d.Grip : 1f);
            car.TankCapacity = d.Tank > 0f ? d.Tank : ds[3];
        }

        /// <summary>2.0.28: the colour this model wears gives the car its stats, when that colour's entry has stats of its own
        /// (TopSpeedKph): car selection shows them and the race uses them (BaseCarPropertyManager reads the CarModel).</summary>
        public static void ApplyColourStats(CarModel car, Transform model)
        {
            try
            {
                if (car == null || model == null || !IsStreetCarId(car.ItemId)) return;
                List<Variant> list; float[] ds;
                if (!_variants.TryGetValue(car.ItemId, out list) || list == null || list.Count < 2 || !_donorStats.TryGetValue(car.ItemId, out ds)) return;
                CarDef d;
                if (!_def.TryGetValue(list[CurrentVariant(model, list)].Id, out d) || d == null || d.TopSpeedKph <= 0f) return;
                SetStats(car, d, ds);
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] colour stats: " + ex.Message); }
        }

        /// <summary>2.0.28: rivals in a car with a top speed of its own (TopSpeedKph: the andas) drive the track's AI speed
        /// otherwise (~150 km/h against the player's 57): each is held to that car's top speed, 90-100 % of it.</summary>
        private static readonly HashSet<BaseCar> _capped = new HashSet<BaseCar>();
        // 2.1.2: the rivals held to the player's walk - no rubber band, no lap/track balance, no slower corners for them
        // (StreetCarWalkerRubberBandPatch, StreetCarWalkerSpeedPatch): the game's position rubber band slowed them to half
        // speed while the player was behind his "expected" place, so the player's anda still walked away from the pack
        private static readonly HashSet<BaseCar> _walkers = new HashSet<BaseCar>();
        public static bool IsWalker(BaseCar c) { return c != null && _walkers.Contains(c); }
        private static System.Reflection.FieldInfo _allCars;

        /// <summary>Every frame of a race (Tolls' update): a rival whose body is a car with a top speed of its own (the andas;
        /// a rival has no CarModel - its view knows the id) is held once to 90-100 % of that top speed.</summary>
        public static void CapRivalsTick()
        {
            try
            {
                var game = RRApplication.Instance.Game;
                if (game == null || !game.HasGameStarted) return;
                if (_allCars == null) _allCars = HarmonyLib.AccessTools.Field(typeof(GameManager), "m_allCars");
                var all = _allCars != null && _allCars.DeclaringType.IsInstanceOfType(game) ? _allCars.GetValue(game) as List<BaseCar> : null;
                if (all == null) return;
                if (_capped.Count > 400) { _capped.Clear(); _walkers.Clear(); }
                int n = 0;
                foreach (BaseCar c in all)
                {
                    var e = c as EnemyCar;
                    if (e == null || _capped.Contains(e) || e.View == null || e.CarEngine == null) continue;
                    _capped.Add(e);
                    if (Traffic.IsTrafficCar(e)) continue;
                    CarDef d;
                    string id = VariantBase(BaseId(e.View.CarModelId ?? ""));
                    if (!_def.TryGetValue(id, out d) || d == null || d.TopSpeedKph <= 0f) continue;
                    // 2.1.1 (the player's anda went too fast - all should walk at a speed balanced between them): a rival
                    // walks at the PLAYER's own top speed (2.1.2: 95-102 % of it, steady - see _walkers) - no slower grid for the player to leave behind
                    float own = 0f;
                    try { var pc = game.PlayerCar; if (pc != null && pc.CarModel != null && VariantBase(BaseId(pc.CarModel.ItemId)) == id) own = pc.CarModel.MaxSpeedInKPH; } catch (Exception) { }
                    float cur = e.CarEngine.MaxSpeedInKPH, target = (own > 0.1f ? own : d.TopSpeedKph) * UnityEngine.Random.Range(0.95f, 1.02f);
                    if (cur > 0.1f) { e.CarEngine.ApplyMaxSpeedFactor(target / cur); _walkers.Add(e); n++; }
                }
                if (n > 0) Debug.Log("[HCT15] street cars: " + n + " rival(s) held to their car's own top speed (" + all.Count + " cars)");
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] rival caps: " + ex.Message); }
        }

        /// <summary>2.0.28: the entry of the colour this model wears, when that colour is an entry of its own (VariantOf), else null.</summary>
        public static CarDef ColourDefOf(CarModel car, Transform model)
        {
            try
            {
                List<Variant> list; CarDef d;
                if (car == null || model == null || !_variants.TryGetValue(car.ItemId, out list) || list == null || list.Count < 2) return null;
                return _def.TryGetValue(list[CurrentVariant(model, list)].Id, out d) ? d : null;
            }
            catch (Exception) { return null; }
        }

        /// <summary>2.0.28: the top speed this car model runs at when its entry sets one (TopSpeedKph: the andas), else 0.</summary>
        public static float OwnTopSpeedOf(CarModel car)
        {
            CarDef d;
            if (car == null || string.IsNullOrEmpty(car.ItemId) || !_def.TryGetValue(car.ItemId, out d) || d == null || d.TopSpeedKph <= 0f) return 0f;
            return car.MaxSpeedInKPH;
        }

        /// <summary>2.0.28: CarDef.ShownSpeed of a car (a colour asks its car), 0 = none.</summary>
        public static float ShownSpeedOf(string carId)
        {
            CarDef d;
            if (string.IsNullOrEmpty(carId) || !_def.TryGetValue(BaseId(carId), out d) || d == null) return 0f;
            if (d.ShownSpeed <= 0f && !string.IsNullOrEmpty(d.VariantOf) && _def.TryGetValue(d.VariantOf, out d) && d != null) { }
            return d != null ? d.ShownSpeed : 0f;
        }

        private static List<CarDef> LoadDefs()
        {
            string path = Path.Combine(CarsDir, "streetcars.json");
            try
            {
                if (File.Exists(path))
                {
                    CarsFile file = Json.FromJson<CarsFile>(File.ReadAllText(path));
                    if (file != null && file.Cars != null && file.Cars.Count > 0)
                    {
                        // a file written by an older build knows nothing of the cars added since (the micro's
                        // liveries): they are appended with their built-in values, and the file is rewritten so
                        // it keeps listing every car there is. An edited car keeps every edit.
                        var have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (CarDef c in file.Cars) if (c != null && !string.IsNullOrEmpty(c.Id)) have.Add(c.Id);
                        int added = 0;
                        foreach (CarDef b in Builtin) if (!have.Contains(b.Id)) { file.Cars.Add(b); added++; }
                        // entries that became colours of another car (VariantOf) after the file was written follow suit
                        foreach (CarDef c in file.Cars)
                        {
                            if (c == null || !string.IsNullOrEmpty(c.VariantOf)) continue;
                            foreach (CarDef b in Builtin)
                                if (!string.IsNullOrEmpty(b.VariantOf) && string.Equals(b.Id, c.Id, StringComparison.OrdinalIgnoreCase)) { c.VariantOf = b.VariantOf; added++; }
                        }
                        if (added > 0)
                        {
                            File.WriteAllText(path, Json.ToJson(file, true));
                            Debug.Log("[HCT15] street cars: " + added + " built-in car(s) appended to streetcars.json");
                        }
                        return file.Cars;
                    }
                }
                CarsFile def = new CarsFile();
                def.Cars.AddRange(Builtin);
                File.WriteAllText(path, Json.ToJson(def, true));
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] street cars: streetcars.json problem, using the built-in list: " + ex.Message); }
            return new List<CarDef>(Builtin);
        }

        private static void Build(CarModelDataList main)
        {
            _tried = true;
            if (main == null || main.CarModels == null) return;
            var cars = new List<CarModel>();
            foreach (CarDef d in LoadDefs())
            {
                if (d == null || !d.Enabled || string.IsNullOrEmpty(d.Id)) continue;
                try
                {
                    CarModel donor = FindDonor(main, d.Donor);
                    if (donor == null) { Debug.LogWarning("[HCT15] street car '" + d.Id + "': no donor car with a body mesh"); continue; }
                    bool sprite = !string.IsNullOrEmpty(d.Sprite);
                    bool ownBody = !sprite && string.IsNullOrEmpty(d.Model);
                    AssetLibrary.AssetDef body = ownBody || sprite ? null : AssetLibrary.Get(d.Model);
                    if (!ownBody && !sprite && (body == null || !AssetLibrary.EnsureLoaded(body) || body.Mesh == null))
                    {
                        Debug.LogWarning("[HCT15] street car '" + d.Id + "': model '" + d.Model + "' is not in the asset library");
                        continue;
                    }
                    float bubbleRise;
                    Vector3 cameraOffset;
                    GameObject prefab = sprite ? SpriteBodyPrefab(donor, d, out bubbleRise, out cameraOffset)
                                       : ownBody ? DonorBodyPrefab(donor, d, out bubbleRise, out cameraOffset)
                                                 : BodyPrefab(donor, body, d, out bubbleRise, out cameraOffset);
                    if (prefab == null) continue;
                    if (!string.IsNullOrEmpty(d.VariantOf))
                    {
                        // one of another car's colours (2026-09-24: "they are variants of the same vehicle, switched with
                        // up and down"): its fitted body is kept and swapped in by the game's own colour system
                        AddVariant(d.VariantOf, Capture(prefab, d.Id), false);
                        _def[d.Id] = d;
                        Debug.Log("[HCT15] street car '" + d.Id + "': a colour of '" + d.VariantOf + "' (" + (string.IsNullOrEmpty(d.Model) ? "the donor's body" : "model '" + d.Model + "'") + ")");
                        continue;
                    }
                    CarModel car = CloneCar(donor);
                    car.ItemId = d.Id;
                    car.Name = d.Id;
                    car.ItemName = "hct15_car_" + d.Id;
                    car.ModelPrefab = prefab;
                    car.TokenCost = 0;
                    car.UnlockedBy = string.Empty;
                    car.CustomUnlockKey = string.Empty;
                    car.ShowAsEnemyOnlyInCups = new List<string>();
                    car.AllowAsEnemyInExtraCampaign = d.AsEnemy;
                    car.HasSirenSkin = false;
                    car.SirenSkinsName = new string[0];
                    car.Helmets = new List<string>();
                    _donorStats[d.Id] = new[] { donor.MaxSpeedInKPH, donor.ZeroToMaxSpeedInSeconds, donor.TurningStrength, donor.TankCapacity };
                    SetStats(car, d, _donorStats[d.Id]);
                    car.CameraPrefabPositionOffset = donor.CameraPrefabPositionOffset + cameraOffset;
                    car.CarUIOffset = donor.CarUIOffset + new Vector3(0f, bubbleRise, 0f);
                    RegisterStrings(car, d);
                    _ids.Add(d.Id);
                    _rise[d.Id] = bubbleRise;
                    _def[d.Id] = d;
                    _donorName[d.Id] = donor.Name;
                    cars.Add(car);
                    if (!sprite) AddVariant(d.Id, Capture(prefab, d.Id), true);   // colour 0: the car as built
                    Taunts.RegisterFor(car.Name, "");   // cars\taunts.<id>.txt first, then the default file - not the F1 pit radio
                    Debug.Log("[HCT15] street car '" + d.Id + "' built on '" + donor.Name + "' with "
                              + (sprite ? "the sprite set '" + d.Sprite + "'" : ownBody ? "the donor's own body" : "model '" + d.Model + "'") + " ("
                              + car.MaxSpeedInKPH.ToString("0") + " km/h, 0-max in " + car.ZeroToMaxSpeedInSeconds.ToString("0.0") + " s)");
                }
                catch (Exception ex) { Debug.LogWarning("[HCT15] street car '" + d.Id + "' failed: " + ex.Message); }
            }
            _cars = cars;
            DumpDonors(main, cars);
        }

        // ------------------------------------------------------------------ the speech bubbles

        /// <summary>
        /// Called by CarUIViewModel once the bubbles exist - the first moment their real place is known.
        ///
        /// The bubble is authored for the donor: for the micro's donor it hangs 1.60 up and 0.71 out, which is
        /// under the roof of a body 1.97 tall and well inside its width, so the bus is simply drawn over what
        /// it is shouting. Here we know all three things at once - where the bubble hangs, how big the body
        /// turned out, and where the race camera sits - so the bubble is lifted until the line of sight from
        /// that camera passes over the body (its own height included), and slid towards the camera if lifting
        /// alone cannot do it. Retail cars are not touched at all, and neither is a car whose BubbleClear is
        /// negative ("leave it where the donor had it").
        /// </summary>
        public static void PrepareSpeechBubbles(string carId, params GameObject[] bubbles)
        {
            try
            {
                if (bubbles == null || !IsStreetCarId(carId)) return;
                // the campaign's own bubble size ("BubbleScale" in campaign.json): the retro campaign draws the
                // world at 224 lines and its bubbles came out unreadable, so it asks for twice the size (2026-09-24).
                // It goes on the lift holder (BubbleLift.Scale), whose scale is written every frame - a scale put on
                // the bubble object itself was overwritten and changed nothing.
                ModCampaign active = ModCampaigns.Active;
                float bs = active != null && active.Def != null && active.Def.BubbleScale > 0.01f ? active.Def.BubbleScale : 1f;
                string id = BaseId(carId);
                CarDef def;
                Bounds body;
                Vector3 cam;
                if (!_def.TryGetValue(id, out def) || !_body.TryGetValue(id, out body) || !_cam.TryGetValue(id, out cam)) return;
                if (def.BubbleClear < 0f) return;
                foreach (GameObject go in bubbles)
                {
                    if (go == null || go.transform.parent == null) continue;
                    if (go.transform.parent.GetComponent<BubbleLift>() != null) continue;   // already done
                    Transform bubble = go.transform;
                    Transform car = CarRoot(bubble);
                    if (car == null) continue;
                    Vector3 anchor = car.InverseTransformPoint(bubble.position);   // the bubble in the car's own space
                    float drop = Drop(bubble, car, anchor);      // how far the bubble hangs below that point
                    Bounds shaped = BodyInCarSpace(car, body);   // the body, where the car view actually holds it
                    float guess = ClearingRise(shaped, cam, anchor, drop);   // what the race camera would ask for
                    GameObject holder = new GameObject("HCT15_bubble_lift");
                    holder.transform.SetParent(bubble.parent, false);   // identity: the bubble keeps its own place
                    holder.transform.SetSiblingIndex(bubble.GetSiblingIndex());
                    bubble.SetParent(holder.transform, false);          // ...and its own position and scale, free
                    BubbleLift lift = holder.AddComponent<BubbleLift>();
                    lift.Bubble = bubble;
                    lift.Car = car;
                    lift.Body = shaped;
                    lift.Drop = drop;
                    lift.Extra = def.BubbleUp;
                    lift.Clear = Mathf.Max(0f, def.BubbleClear);
                    lift.CarId = id;
                    lift.Scale = bs;
                    lift.Edge = def.BubbleEdge;
                    // the player's own car only: a rival micro's bubble stays over the rival (its camera is not this view's)
                    lift.Corner = def.BubbleCorner && car.GetComponent<PlayerCarTrackView>() != null;
                    if (def.BubbleEdge) EdgeTops(car, shaped, out lift.TopAtMin, out lift.TopAtMax);
                    Debug.Log("[HCT15] speech bubble '" + bubble.name + "' of '" + id + "': anchor " + anchor.ToString("0.00")
                              + (Mathf.Abs(bs - 1f) > 0.01f ? ", drawn x" + bs.ToString("0.0") + " (the campaign's BubbleScale)" : "")
                              + " (hangs " + drop.ToString("0.00") + " below it) under a body " + shaped.min.ToString("0.00")
                              + " to " + shaped.max.ToString("0.00")
                              + "; the race camera at " + cam.ToString("0.00") + " would ask for +" + guess.ToString("0.00")
                              + " - the camera actually used is measured every frame");
                }
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] speech bubble lift: " + ex.Message); }
        }

        /// <summary>The car view the bubble hangs off - its space is the one the body box is measured in.</summary>
        private static Transform CarRoot(Transform t)
        {
            while (t != null && t.GetComponent<PlayerCarTrackView>() == null && t.GetComponent<AnotherPlayerCarTrackView>() == null)
                t = t.parent;
            return t;
        }

        /// <summary>
        /// How far the bubble reaches BELOW the point it hangs from, in the car's space. Measured from the
        /// rectangles themselves (the bubble and everything drawn inside it), because that lower edge is what
        /// dips behind a roof - guessing it is how the first attempt left the text half covered.
        /// </summary>
        private static float Drop(Transform bubble, Transform car, Vector3 anchor)
        {
            float low = anchor.y;
            var corners = new Vector3[4];
            foreach (RectTransform rt in bubble.GetComponentsInChildren<RectTransform>(true))
            {
                if (rt.rect.width < 0.0001f || rt.rect.height < 0.0001f) continue;
                rt.GetWorldCorners(corners);
                for (int i = 0; i < 4; i++)
                {
                    float y = car.InverseTransformPoint(corners[i]).y;
                    if (y < low) low = y;
                }
            }
            float drop = anchor.y - low;
            return drop > 0.02f && drop < 3f ? drop : 0.45f;   // the retail bubble is about 1.8 x 0.9 units
        }

        /// <summary>
        /// The body box in the CAR VIEW's space - measured from the mesh that is actually in the scene, not from
        /// the mesh asset. PlayerCarTrackView parks the model at the game's CarModelOffset and the race scales
        /// the whole car, so the mesh's own bounds are neither in the right place nor the right size: with them
        /// the bubble was lifted a third of what it needed. Falls back to the built mesh if the car is not there.
        /// </summary>
        /// <summary>
        /// The height of the body's ROOF at each end, in the car view's space: the highest vertex within half a
        /// unit of the body's front and of its back. The box top is not it - the micro carries an air
        /// conditioner on the roof, and a bubble hung from the box top floated 60 px over the roof edge.
        /// </summary>
        private static void EdgeTops(Transform car, Bounds body, out float atMin, out float atMax)
        {
            atMin = atMax = body.max.y;
            try
            {
                float lowMin = float.MinValue, lowMax = float.MinValue;
                foreach (MeshFilter mf in car.GetComponentsInChildren<MeshFilter>(false))
                {
                    string n = mf.name.ToLowerInvariant();
                    if (n != "high" && n != "med" && n != "medium") continue;
                    if (mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
                    Vector3[] v = mf.sharedMesh.vertices;
                    for (int i = 0; i < v.Length; i++)
                    {
                        Vector3 p = car.InverseTransformPoint(mf.transform.TransformPoint(v[i]));
                        if (p.z < body.min.z + 0.5f && p.y > lowMin) lowMin = p.y;
                        if (p.z > body.max.z - 0.5f && p.y > lowMax) lowMax = p.y;
                    }
                }
                if (lowMin > float.MinValue) atMin = lowMin;
                if (lowMax > float.MinValue) atMax = lowMax;
            }
            catch (Exception) { }
        }

        private static Bounds BodyInCarSpace(Transform car, Bounds fallback)
        {
            try
            {
                bool any = false;
                Bounds box = new Bounds();
                foreach (MeshFilter mf in car.GetComponentsInChildren<MeshFilter>(true))
                {
                    string n = mf.name.ToLowerInvariant();
                    if (n != "high" && n != "med" && n != "medium") continue;   // the body, at its best detail
                    if (mf.sharedMesh == null) continue;
                    Bounds mb = mf.sharedMesh.bounds;
                    for (int i = 0; i < 8; i++)
                    {
                        Vector3 p = car.InverseTransformPoint(mf.transform.TransformPoint(Corner(mb, i)));
                        if (!any) { box = new Bounds(p, Vector3.zero); any = true; } else box.Encapsulate(p);
                    }
                }
                if (any) return box;
            }
            catch (Exception) { }
            return fallback;
        }

        /// <summary>
        /// How much higher a bubble has to hang for the line from the camera to clear the body, with its own
        /// bottom edge included. 0 when nothing is in the way - a car no bigger than its donor keeps the
        /// shipped placement exactly.
        /// </summary>
        internal const float Air = 0.12f;   // a hand's width of daylight between the roof and the bubble

        public static float ClearingRise(Bounds body, Vector3 cam, Vector3 anchor, float drop)
        {
            float dz = cam.z - anchor.z;
            if (Mathf.Abs(dz) < 0.01f) return 0f;
            float needed = -9999f;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = Corner(body, i);
                float t = (cam.z - c.z) / dz;          // 0 at the camera, 1 at the bubble
                if (t <= 0.05f || t > 1f) continue;    // that corner is not between the two
                float y = cam.y + (c.y - cam.y) / t;   // where the bubble would have to be to graze it
                if (y > needed) needed = y;
            }
            if (needed < -9000f) return 0f;                 // nothing of the body is between us and the bubble
            return Mathf.Max(0f, needed + drop + Air - anchor.y);
        }

        private static Vector3 Corner(Bounds b, int i)
        {
            return new Vector3((i & 1) == 0 ? b.min.x : b.max.x,
                               (i & 2) == 0 ? b.min.y : b.max.y,
                               (i & 4) == 0 ? b.min.z : b.max.z);
        }

        private static readonly HashSet<string> _measured = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The one honest report of what a race actually does with a speech bubble, sent by BubbleLift on the
        /// first frame it places one: where the camera is in the car's space, where the bubble hangs THEN (not
        /// at initialisation - the car UI is still being put together at that point and the anchor moves), how
        /// far it hangs below that, the body it has to clear, and the lift it got.
        ///
        /// None of it can be worked out from the files, so the browser Car Editor reads this instead of
        /// guessing - it is what makes its "Race" view the same picture the player sees.
        /// </summary>
        public static void ReportBubble(string carId, string bubbleName, Vector3 camLocal, Vector3 anchor,
                                        Bounds body, float drop, float rise, float clear)
        {
            try
            {
                string id = BaseId(carId);
                string key = id + "/" + bubbleName;
                if (!_measured.Add(key)) return;
                _placed[key] = "  {\"Key\": " + Q(key) + ", \"Car\": " + Q(id) + ", \"Bubble\": " + Q(bubbleName)
                             + ", \"Local\": " + V3(anchor) + ", \"Camera\": " + V3(camLocal)
                             + ", \"BodyMin\": " + V3(body.min) + ", \"BodyMax\": " + V3(body.max)
                             + ", \"Drop\": " + F(drop, "0.###") + ", \"Rise\": " + F(rise, "0.###")
                             + ", \"Clear\": " + F(clear, "0.###") + "}";
                WriteBubbles();
                Debug.Log("[HCT15] speech bubble '" + bubbleName + "' of '" + id + "' in the race: hangs at "
                          + anchor.ToString("0.00") + " (" + drop.ToString("0.00") + " below it), camera at "
                          + camLocal.ToString("0.00") + ", body " + body.min.ToString("0.00") + " to "
                          + body.max.ToString("0.00") + " -> lifted " + rise.ToString("0.00")
                          + (clear > 0.001f ? ", pulled " + clear.ToString("0.00") : ""));
            }
            catch (Exception) { }
        }

        private static void WriteBubbles()
        {
            var rows = new List<string>(_placed.Values);
            File.WriteAllText(Path.Combine(CarsDir, "_bubbles.json"),
                              "{\n \"Note\": \"Written by the game: where each speech bubble hangs in the car's own space,"
                              + " and what StreetCars did about it. The Car Editor reads it.\",\n \"Bubbles\": [\n"
                              + string.Join(",\n", rows.ToArray()) + "\n ]\n}\n");
        }

        /// <summary>"cl_micro (Clone)" and the like -> "cl_micro".</summary>
        internal static string BaseId(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            int paren = id.IndexOf('(');
            return paren > 0 ? id.Substring(0, paren).Trim() : id;
        }

        /// <summary>
        /// Every retail car's body box, bubble anchor and camera offset, written next to streetcars.json so the
        /// browser Car Editor can show a new body against its donor without opening Unity. Measured here because
        /// the car list is a Unity asset: this is the only place the numbers exist.
        /// </summary>
        private static void DumpDonors(CarModelDataList main, List<CarModel> ours)
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.Append("{\n \"Note\": \"Written by the game on every run - measurements the Car Editor reads. Do not edit.\",\n");
                sb.Append(" \"RaceCamera\": ").Append(V3(RaceCamera)).Append(",\n \"Donors\": [\n");
                bool first = true;
                foreach (CarModel c in main.CarModels)
                {
                    if (c == null || c.ModelPrefab == null) continue;
                    MeshFilter mf = BodyFilter(c.ModelPrefab);
                    if (mf == null) continue;
                    Bounds b = mf.sharedMesh.bounds;
                    if (!first) sb.Append(",\n");
                    first = false;
                    sb.Append("  {\"Id\": ").Append(Q(c.ItemId)).Append(", \"Name\": ").Append(Q(c.Name))
                      .Append(", \"Prefab\": ").Append(Q(c.ModelPrefab.name))
                      .Append(", \"BoxMin\": ").Append(V3(b.min)).Append(", \"BoxMax\": ").Append(V3(b.max))
                      .Append(", \"CarUIOffset\": ").Append(V3(c.CarUIOffset))
                      .Append(", \"CameraOffset\": ").Append(V3(c.CameraPrefabPositionOffset))
                      .Append(", \"MaxSpeedInKPH\": ").Append(F(c.MaxSpeedInKPH, "0.#"))
                      .Append(", \"ZeroToMaxSpeedInSeconds\": ").Append(F(c.ZeroToMaxSpeedInSeconds, "0.##"))
                      .Append(", \"TurningStrength\": ").Append(F(c.TurningStrength, "0.##"))
                      .Append(", \"TankCapacity\": ").Append(F(c.TankCapacity, "0.#"))
                      .Append(", \"Wheels\": [");
                    bool fw = true;
                    foreach (Transform t in c.ModelPrefab.GetComponentsInChildren<Transform>(true))
                    {
                        string n = t.name.ToLowerInvariant();
                        if (!n.StartsWith("wheels_") || n.Contains("detector")) continue;
                        Vector3 p = c.ModelPrefab.transform.InverseTransformPoint(t.position);
                        if (!fw) sb.Append(", ");
                        fw = false;
                        sb.Append("{\"Name\": ").Append(Q(t.name)).Append(", \"Pos\": ").Append(V3(p)).Append("}");
                    }
                    sb.Append("]}");
                }
                sb.Append("\n ],\n \"Built\": [\n");
                first = true;
                foreach (CarModel c in ours)
                {
                    MeshFilter mf = c.ModelPrefab != null ? BodyFilter(c.ModelPrefab) : null;
                    if (mf == null) continue;
                    Bounds b = mf.sharedMesh.bounds;
                    CarDef cd;
                    _def.TryGetValue(c.ItemId, out cd);
                    if (!first) sb.Append(",\n");
                    first = false;
                    sb.Append("  {\"Id\": ").Append(Q(c.ItemId))
                      .Append(", \"BoxMin\": ").Append(V3(b.min)).Append(", \"BoxMax\": ").Append(V3(b.max))
                      .Append(", \"CarUIOffset\": ").Append(V3(c.CarUIOffset))
                      .Append(", \"CameraOffset\": ").Append(V3(c.CameraPrefabPositionOffset))
                      .Append(", \"BubbleClear\": ").Append(F(cd != null ? cd.BubbleClear : 0f, "0.###"))
                      .Append(", \"MaxSpeedInKPH\": ").Append(F(c.MaxSpeedInKPH, "0.#"))
                      .Append(", \"ZeroToMaxSpeedInSeconds\": ").Append(F(c.ZeroToMaxSpeedInSeconds, "0.##"))
                      .Append("}");
                }
                sb.Append("\n ]\n}\n");
                File.WriteAllText(Path.Combine(CarsDir, "_donors.json"), sb.ToString());
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] street cars: could not write _donors.json: " + ex.Message); }
        }

        private static string Q(string s) { return "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""; }

        private static string V3(Vector3 v)
        {
            return "[" + F(v.x, "0.####") + ", " + F(v.y, "0.####") + ", " + F(v.z, "0.####") + "]";
        }

        /// <summary>A number a JSON reader understands wherever the game runs (a Spanish Windows writes 1,5).</summary>
        private static string F(float v, string fmt)
        {
            return v.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>The car-selection screen localises "&lt;Name&gt;_name" and "&lt;Name&gt;_description".</summary>
        private static void RegisterStrings(CarModel car, CarDef d)
        {
            string title = string.IsNullOrEmpty(d.NameKey) ? d.Id : Loc.Get(d.NameKey);
            string info = string.IsNullOrEmpty(d.DescKey) ? "" : Loc.Get(d.DescKey);
            ContentRegistry.Register(car.Name + "_name", title);
            ContentRegistry.Register(car.Name + "_description", info);
            ContentRegistry.Register(car.ItemName, title);
            ContentRegistry.Register(car.ItemName + "_name", title);
            ContentRegistry.Register(car.ItemName + "_description", info);
        }

        /// <summary>Called when the player changes the game language.</summary>
        public static void RefreshStrings()
        {
            try
            {
                if (_cars == null) return;
                List<CarDef> defs = LoadDefs();
                foreach (CarModel c in _cars)
                    foreach (CarDef d in defs)
                        if (d != null && d.Id == c.ItemId) RegisterStrings(c, d);
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] street car strings: " + ex.Message); }
        }

        private static CarModel FindDonor(CarModelDataList main, string wanted)
        {
            CarModel first = null;
            foreach (CarModel c in main.CarModels)
            {
                if (c == null || c.ModelPrefab == null) continue;
                if (BodyFilter(c.ModelPrefab) == null) continue;
                if (!string.IsNullOrEmpty(wanted) && (c.ItemId == wanted || c.Name == wanted)) return c;
                if (first == null) first = c;
            }
            // A named donor that is not in the list falls back to the first, but silently doing so is how both
            // cars ended up on a low wide muscle car: say it, the shape of the donor decides how the car sits.
            if (!string.IsNullOrEmpty(wanted))
                Debug.LogWarning("[HCT15] street cars: no retail car called '" + wanted + "', falling back to '"
                                 + (first != null ? first.Name : "none") + "'");
            return first;
        }

        private static readonly string[] BodyParts = { "high", "med", "medium", "low", "first_person" };

        private static MeshFilter BodyFilter(GameObject prefab)
        {
            Transform sub = prefab.transform.Find("root/sub_root");
            if (sub == null) return null;
            Transform hi = sub.Find("high");
            if (hi == null) return null;
            MeshFilter mf = hi.GetComponent<MeshFilter>();
            return mf != null && mf.sharedMesh != null ? mf : null;
        }

        /// <summary>A copy of the donor prefab, parked under the far-away holder (so its own local position stays zero).</summary>
        private static GameObject DonorCopy(CarModel donor, CarDef d)
        {
            if (_holder == null)
            {
                _holder = new GameObject("HCT15_street_cars");
                _holder.transform.position = new Vector3(0f, -4000f, -4200f);
                _holder.hideFlags = HideFlags.HideInHierarchy;
                UnityEngine.Object.DontDestroyOnLoad(_holder);
            }
            GameObject inst = UnityEngine.Object.Instantiate(donor.ModelPrefab);
            inst.name = d.Id;
            inst.transform.SetParent(_holder.transform, false);
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localRotation = Quaternion.identity;
            return inst;
        }

        /// <summary>
        /// A car that keeps the donor's own body (Model empty) - the taxi is the retail Specter. Nothing is
        /// fitted or moved; the body is only repainted when PaintLow/PaintHigh say so. Every level of detail
        /// gets its own repainted copy of its own mesh, the renderers keep the donor's materials.
        /// </summary>
        private static GameObject DonorBodyPrefab(CarModel donor, CarDef d, out float bubbleRise, out Vector3 camera)
        {
            bubbleRise = d.BubbleUp;
            camera = new Vector3(0f, d.CameraUp, -d.CameraBack);
            GameObject inst = DonorCopy(donor, d);
            MeshFilter donorBody = BodyFilter(inst);
            if (donorBody == null) { UnityEngine.Object.Destroy(inst); return null; }
            Bounds db = donorBody.sharedMesh.bounds;
            _body[d.Id] = db;
            _cam[d.Id] = RaceCamera + donor.CameraPrefabPositionOffset + camera;
            MoveWheels(inst, d);
            Color low, high;
            bool paint = ParseColour(d.PaintLow, out low) | ParseColour(d.PaintHigh, out high);
            if (string.IsNullOrEmpty(d.PaintLow)) low = new Color(-1f, 0f, 0f);    // "keep this half as it is"
            if (string.IsNullOrEmpty(d.PaintHigh)) high = new Color(-1f, 0f, 0f);
            float split = db.min.y + Mathf.Clamp01(d.PaintSplit) * db.size.y;   // the same height on every LOD
            float roofY = d.PaintRoof > 0f ? db.min.y + Mathf.Clamp01(d.PaintRoof) * db.size.y : float.PositiveInfinity;
            // The retail meshes ship non-readable, so their colours cannot be touched here: Tools\car_repaint.py
            // bakes the same repaint from the decoded car into cars\bodies\<id>.<part>.mesh. A readable donor
            // (a DLC car) is repainted on the spot, which also keeps an edited colour working without a bake.
            int painted = 0, baked = 0, missing = 0;
            Transform sub = inst.transform.Find("root/sub_root");
            if (paint && sub != null)
            {
                foreach (string part in BodyParts)
                {
                    Transform t = sub.Find(part);
                    MeshFilter mf = t != null ? t.GetComponent<MeshFilter>() : null;
                    if (mf == null || mf.sharedMesh == null) continue;
                    Mesh done = RepaintBody(mf.sharedMesh, low, high, split, roofY);
                    if (done != null) painted++;
                    else if ((done = LoadBakedBody(Path.Combine(Path.Combine(CarsDir, "bodies"), d.Id + "." + part + ".mesh"))) != null) baked++;
                    else { missing++; continue; }
                    done.name = mf.sharedMesh.name + "_" + d.Id;
                    mf.sharedMesh = done;
                }
                if (missing > 0)
                    Debug.LogWarning("[HCT15] street car '" + d.Id + "': " + missing + " level(s) of detail of '" + donor.ModelPrefab.name
                                     + "' keep the retail paint - the mesh is not readable and cars\\bodies has no bake (run Tools\\car_repaint.py)");
            }
            Debug.Log("[HCT15] street car '" + d.Id + "': the body of '" + donor.ModelPrefab.name + "' kept (" + db.size.ToString("0.00")
                      + "), " + painted + " level(s) of detail repainted here, " + baked + " from cars\\bodies"
                      + " (" + d.PaintLow + " below " + split.ToString("0.00") + ", " + d.PaintHigh + " above)" + WheelInfo(inst));
            return inst;
        }

        /// <summary>
        /// A body baked by Tools\car_repaint.py: "HCTMESH1", vertex count, has-normals, has-uv, submesh count,
        /// then positions, normals, uvs and colours as little-endian floats, then per submesh its index count
        /// and indices. Already in the car's own space. Null when the file is not there or not ours.
        /// </summary>
        private static Mesh LoadBakedBody(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                using (var r = new BinaryReader(File.OpenRead(path)))
                {
                    if (new string(r.ReadChars(8)) != "HCTMESH1") return null;
                    int n = r.ReadInt32();
                    bool hasN = r.ReadInt32() != 0, hasU = r.ReadInt32() != 0;
                    int subs = r.ReadInt32();
                    var P = new Vector3[n];
                    for (int i = 0; i < n; i++) P[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                    Vector3[] N = null;
                    if (hasN) { N = new Vector3[n]; for (int i = 0; i < n; i++) N[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); }
                    Vector2[] U = null;
                    if (hasU) { U = new Vector2[n]; for (int i = 0; i < n; i++) U[i] = new Vector2(r.ReadSingle(), r.ReadSingle()); }
                    var C = new Color[n];
                    for (int i = 0; i < n; i++) C[i] = new Color(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                    Mesh m = new Mesh();
                    if (n > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                    m.vertices = P;
                    if (hasN) m.normals = N;
                    if (hasU) m.uv = U;
                    m.colors = C;
                    m.subMeshCount = subs;
                    for (int s = 0; s < subs; s++)
                    {
                        int count = r.ReadInt32();
                        var tri = new int[count];
                        for (int i = 0; i < count; i++) tri[i] = r.ReadInt32();
                        m.SetTriangles(tri, s);
                    }
                    if (!hasN) m.RecalculateNormals();
                    m.RecalculateBounds();
                    return m;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[HCT15] street cars: could not read " + Path.GetFileName(path) + ": " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// A sprite car (the retro sprite four): the donor copy with its body, lights and wheels emptied and the
        /// SpriteCar quad hung under it (SpriteCars.Attach). Nothing is fitted - the sprite sizes itself to the
        /// camera every frame. Falls back to the donor's own body when the sprite set is not on disk.
        /// </summary>
        private static GameObject SpriteBodyPrefab(CarModel donor, CarDef d, out float bubbleRise, out Vector3 camera)
        {
            if (SpriteCars.Get(d.Sprite) == null)
            {
                Debug.LogWarning("[HCT15] street car '" + d.Id + "': sprite set '" + d.Sprite + "' is not under cars\\sprites (its frames.json is missing; frames_from_rom.py) - keeping the donor's body");
                return DonorBodyPrefab(donor, d, out bubbleRise, out camera);
            }
            bubbleRise = d.BubbleUp;
            camera = new Vector3(0f, d.CameraUp, -d.CameraBack);
            GameObject inst = DonorCopy(donor, d);
            MeshFilter donorBody = BodyFilter(inst);
            if (donorBody == null) { UnityEngine.Object.Destroy(inst); return null; }
            _body[d.Id] = donorBody.sharedMesh.bounds;
            _cam[d.Id] = RaceCamera + donor.CameraPrefabPositionOffset + camera;
            SpriteCars.Attach(inst, d.Sprite, d);
            return inst;
        }

        /// <summary>
        /// Whether a street car belongs in the car-selection screen right now: a car bound to campaigns (the
        /// the retro campaign four) only inside one of them, every other car everywhere. Asked when the screen OPENS
        /// (CarSelectionViewModel), not when the views are built - those are built once, before any campaign.
        /// </summary>
        public static bool SelectableNow(CarModel car)
        {
            if (car == null) return false;
            CarDef d;
            if (!_def.TryGetValue(car.ItemId ?? "", out d) || d == null) return true;
            if (OnlyListedCar(d) && !ListedNow(d)) return false;
            if (string.IsNullOrEmpty(d.Campaigns)) return true;
            return InCampaigns(d);
        }

        /// <summary>2.0.24: CarDef.OnlyListed of a car (a colour asks its car).</summary>
        private static bool OnlyListedCar(CarDef d)
        {
            if (d.OnlyListed) return true;
            CarDef b;
            return !string.IsNullOrEmpty(d.VariantOf) && _def.TryGetValue(d.VariantOf, out b) && b != null && b.OnlyListed;
        }

        /// <summary>2.0.24: whether the round being raced names this car (or the car it is a colour of) in a "Cars" list of
        /// its own - one made of our cars only (RoundCars): a round without a list gets every car there is, ours included.</summary>
        private static bool ListedNow(CarDef d)
        {
            try
            {
                List<string> cars = RoundCars();
                if (cars == null) return false;
                foreach (string id in cars)
                    if (string.Equals(id, d.Id, StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrEmpty(d.VariantOf) && string.Equals(id, d.VariantOf, StringComparison.OrdinalIgnoreCase))) return true;
            }
            catch (Exception) { }
            return false;
        }

        private static bool InCampaigns(CarDef d)
        {
            try
            {
                ModCampaign active = ModCampaigns.Active;
                if (active == null) return false;
                foreach (string c in d.Campaigns.Split(',', ';'))
                    if (c.Trim().Equals(active.Folder, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch (Exception) { }
            return false;
        }

        /// <summary>
        /// The rivals of the active mod campaign, when it has cars of its own: the street cars bound to it with
        /// AsEnemy, minus what the players drive (all of them if that leaves none). Empty for every other race,
        /// and CarModelManager then fills the field the retail way. This is how the retro grid is retro cars.
        /// </summary>
        /// <summary>How many of the mod's own cars the last CampaignEnemies call handed the grid (0 = a retail grid).</summary>
        public static int LastCampaignGrid;

        public static List<CarModel> CampaignEnemies(IEnumerable<string> playerCars)
        {
            var result = new List<CarModel>();
            LastCampaignGrid = 0;
            try
            {
                if (_cars == null || ModCampaigns.Active == null) return result;
                var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (playerCars != null) foreach (string p in playerCars) if (!string.IsNullOrEmpty(p)) taken.Add(p);
                // A round whose "Cars" rule names nothing but our cars (Aventura Chile's all-micro race): those ARE
                // the grid, whatever their AsEnemy says - the player's own colour is left out while others remain.
                List<string> roundCars = RoundCars();
                if (roundCars != null)
                {
                    var byId = new Dictionary<string, CarModel>(StringComparer.OrdinalIgnoreCase);
                    foreach (CarModel c in _cars) if (c != null && !string.IsNullOrEmpty(c.ItemId)) byId[c.ItemId] = c;
                    var others = new List<CarModel>();
                    var every = new List<CarModel>();
                    foreach (string id in roundCars)
                    {
                        CarModel c;
                        if (!byId.TryGetValue(id, out c)) continue;
                        every.Add(c);
                        if (!taken.Contains(c.ItemId) && !taken.Contains(c.Name)) others.Add(c);
                    }
                    result.AddRange(others.Count > 0 ? others : every);
                    LastCampaignGrid = result.Count;
                    if (result.Count > 0) Debug.Log("[HCT15] street cars: the round's own car list is the grid - " + result.Count + " of our cars");
                    return result;
                }
                var all = new List<CarModel>();
                foreach (CarModel c in _cars)
                {
                    CarDef d;
                    if (c == null || !_def.TryGetValue(c.ItemId, out d) || d == null || !d.AsEnemy || string.IsNullOrEmpty(d.Campaigns) || !InCampaigns(d)) continue;
                    all.Add(c);
                    if (!taken.Contains(c.ItemId) && !taken.Contains(c.Name)) result.Add(c);
                }
                if (result.Count == 0) result.AddRange(all);
                LastCampaignGrid = result.Count;
                if (result.Count > 0) Debug.Log("[HCT15] street cars: the '" + ModCampaigns.Active.Folder + "' grid is " + result.Count + " of our cars");
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] street cars: campaign enemies: " + ex.Message); }
            return result;
        }

        /// <summary>
        /// The active round's "Cars" list when every car on it is one of ours (the round is then a race of our
        /// cars alone, rivals included); null when there is no such list or a retail car is on it.
        /// </summary>
        private static List<string> RoundCars()
        {
            try
            {
                if (StaticData.SelectedGameMode != EGameMode.ExtraCampaign) return null;
                ExtraCampaignGameModeData gm = ExtraCampaignGameModeData.GetInstance();
                LiveTrialsRace race = gm != null ? gm.SelectedLiveTrialRace : null;
                if (race == null || !race.EditCarsAvailability || race.AvailableCars == null || race.AvailableCars.Count == 0) return null;
                foreach (string id in race.AvailableCars) if (string.IsNullOrEmpty(id) || !_def.ContainsKey(id)) return null;
                return race.AvailableCars;
            }
            catch (Exception) { return null; }
        }

        // ------------------------------------------------------------------ colours = bodies (VariantOf)

        /// <summary>
        /// The game's "colours" are whole meshes per index (CarColorDataList.CarMeshes: high/med/low per colour,
        /// flipped through with up/down in car selection, a random one for a rival). A street car's colours are
        /// the bodies of the entries that name it in VariantOf - colour 0 is the car as built - so the micro's
        /// liveries and the taxi's two shapes are one car each. CarColorDataList hands every colour call for a
        /// street car to these methods (they return false for a retail car, which keeps its own path).
        /// </summary>
        private class Variant
        {
            public string Id; public readonly Dictionary<string, Mesh> Mesh = new Dictionary<string, Mesh>(); public readonly Dictionary<string, Material[]> Mats = new Dictionary<string, Material[]>();
            // 2.0.11: the moving legs of this body (AddLegs), swapped with it - a carreta colour is another animal
            public readonly List<Vector3> LegPos = new List<Vector3>(); public Mesh LegMesh; public Material[] LegMats; public float[] LegPhase = new float[0]; public float Swing;
            public Mesh ShinMesh; public Material[] ShinMats; public Vector3 ShinAt; public float KneeBend, Cadence;   // 2.0.28: the knees
            public bool Flag; public Vector3 FlagAt, FlagL, FlagH;   // its waving flag (FlagWave), if it has one
            public readonly List<Vector3> HeadPos = new List<Vector3>(); public Mesh HeadMesh; public Material[] HeadMats;   // the turning head(s)
        }
        private static readonly Dictionary<string, List<Variant>> _variants = new Dictionary<string, List<Variant>>(StringComparer.OrdinalIgnoreCase);
        private static readonly string[] Lods = { "high", "med", "medium", "low", "first_person" };

        private static Variant Capture(GameObject prefab, string id)
        {
            var v = new Variant { Id = id };
            Transform sub = prefab != null ? prefab.transform.Find("root/sub_root") : null;
            if (sub == null) return v;
            foreach (string lod in Lods)
            {
                Transform t = sub.Find(lod);
                if (t == null) continue;
                MeshFilter mf = t.GetComponent<MeshFilter>();
                Renderer r = t.GetComponent<Renderer>();
                if (mf != null) v.Mesh[lod] = mf.sharedMesh;
                if (r != null) v.Mats[lod] = r.sharedMaterials;
                if (!v.Flag)
                {
                    Transform fl = t.Find("HCT15_flag");
                    FlagWave fw = fl != null ? fl.GetComponent<FlagWave>() : null;
                    if (fw != null) { v.Flag = true; v.FlagAt = fl.localPosition; v.FlagL = fw.AxisL; v.FlagH = fw.AxisH; }
                }
                if (v.HeadPos.Count == 0)
                    for (int i = 0; ; i++)
                    {
                        Transform hd = t.Find("HCT15_head_" + i);
                        if (hd == null) break;
                        v.HeadPos.Add(hd.localPosition);
                        MeshFilter hf = hd.GetComponent<MeshFilter>(); Renderer hr = hd.GetComponent<Renderer>();
                        if (hf != null) v.HeadMesh = hf.sharedMesh;
                        if (hr != null) v.HeadMats = hr.sharedMaterials;
                    }
                if (v.LegPos.Count == 0)
                    for (int i = 0; ; i++)
                    {
                        Transform leg = t.Find("HCT15_leg_" + i);
                        if (leg == null) break;
                        v.LegPos.Add(leg.localPosition);
                        MeshFilter lf = leg.GetComponent<MeshFilter>(); Renderer lr = leg.GetComponent<Renderer>();
                        if (lf != null) v.LegMesh = lf.sharedMesh;
                        if (lr != null) v.LegMats = lr.sharedMaterials;
                        Transform shin = leg.Find("HCT15_shin");
                        if (shin != null && v.ShinMesh == null)
                        {
                            v.ShinAt = shin.localPosition;
                            MeshFilter sf = shin.GetComponent<MeshFilter>(); Renderer sr = shin.GetComponent<Renderer>();
                            if (sf != null) v.ShinMesh = sf.sharedMesh;
                            if (sr != null) v.ShinMats = sr.sharedMaterials;
                        }
                    }
            }
            TrotBob tb = prefab.GetComponent<TrotBob>();
            if (tb != null) { v.LegPhase = tb.LegPhase ?? new float[0]; v.Swing = tb.Swing; v.KneeBend = tb.KneeBend; v.Cadence = tb.Cadence; }
            return v;
        }

        private static void AddVariant(string baseId, Variant v, bool first)
        {
            List<Variant> list;
            if (!_variants.TryGetValue(baseId, out list)) _variants[baseId] = list = new List<Variant>();
            if (first) list.Insert(0, v); else list.Add(v);
        }

        private static bool VariantsOf(string name, out List<Variant> list)
        {
            list = null;
            string id = BaseId(name);
            if (!IsStreetCarId(id)) return false;
            _variants.TryGetValue(id, out list);
            return true;
        }

        private static int CurrentVariant(Transform model, List<Variant> list)
        {
            if (list == null || list.Count == 0) return 0;
            Transform hi = model.Find("root/sub_root/high");
            MeshFilter mf = hi != null ? hi.GetComponent<MeshFilter>() : null;
            if (mf == null) return 0;
            Mesh cur = DoorNumbers.BaseOf(mf.sharedMesh);   // 2.0.26: a numbered body copy stands for its colour mesh
            for (int i = 0; i < list.Count; i++) { Mesh m; if (list[i].Mesh.TryGetValue("high", out m) && m == cur) return i; }
            return 0;
        }

        /// <summary>Put colour 'index' on this street car's model; playerIndex &gt;= 0 also records it as that player's choice.</summary>
        public static bool ApplyVariant(Transform model, int index, int playerIndex)
        {
            try
            {
                List<Variant> list;
                if (model == null || !VariantsOf(model.name, out list)) return false;
                if (list == null || list.Count == 0) return true;
                // a rival gets a colour of its own: the track's authored index is 0 for every rival (retail rivals
                // are coloured per track), which made an all-micro grid all yellow (2026-09-24)
                if (playerIndex < 0 && list.Count > 1 && index == 0 && model.GetComponentInParent<EnemyCarTrackView>() != null)
                {
                    // 2.0.26: a round whose Cars list names some of this car's colours (the D-81's red F-150s) keeps the
                    // rivals and the traffic to those; else any colour
                    List<int> allowed = AllowedVariants(BaseId(model.name), list);
                    int fixedColour = Traffic.FixedColour(model, allowed.Count);   // a traffic micro: the line's one livery on this road
                    index = allowed[fixedColour >= 0 ? fixedColour : UnityEngine.Random.Range(0, allowed.Count)];
                }
                index = ((index % list.Count) + list.Count) % list.Count;
                Variant v = list[index];
                Transform sub = model.Find("root/sub_root");
                if (sub != null)
                    foreach (var kv in v.Mesh)
                    {
                        Transform t = sub.Find(kv.Key);
                        if (t == null) continue;
                        MeshFilter mf = t.GetComponent<MeshFilter>();
                        if (mf != null) mf.sharedMesh = kv.Value;
                        Material[] mats;
                        Renderer r = t.GetComponent<Renderer>();
                        if (r != null && v.Mats.TryGetValue(kv.Key, out mats) && mats != null) r.sharedMaterials = mats;
                    }
                SwapLegs(model, v);
                if (playerIndex >= 0 && _cars != null)
                {
                    // 2.0.28: a colour with stats of its own (the andas) - the car's model takes them, and a player's race car too
                    CarModel cm = _cars.Find(c => c != null && string.Equals(c.ItemId, BaseId(model.name), StringComparison.OrdinalIgnoreCase));
                    ApplyColourStats(cm, model);
                    PlayerCarTrackView pv = model.GetComponentInParent<PlayerCarTrackView>();
                    if (cm != null && pv != null && pv.Controller != null && pv.Controller.PropertyManager != null) pv.Controller.PropertyManager.OverrideCarModelValues(cm);
                }
                DoorNumbers.Apply(model, BaseId(model.name), playerIndex);   // 2.0.26: the mine fleet's door numbers (F-150, fleet rounds)
                if (playerIndex >= 0 && StaticData.GameModeData != null && StaticData.GameModeData.PlayerCarColorIndex != null && playerIndex < StaticData.GameModeData.PlayerCarColorIndex.Count)
                    StaticData.GameModeData.PlayerCarColorIndex[playerIndex] = index;
                return true;
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] street car colour: " + ex.Message); return true; }
        }

        /// <summary>The legs of colour v on this model: the old ones out, the new ones at their hips, TrotBob told.</summary>
        private static void SwapLegs(Transform model, Variant v)
        {
            Transform host = null;
            Transform sub = model.Find("root/sub_root");
            if (sub != null) foreach (string lod in Lods) { host = sub.Find(lod); if (host != null) break; }
            if (host == null) return;
            var old = new List<Transform>();
            foreach (Transform c in host) if (c.name == "HCT15_flag") old.Add(c);
            foreach (Transform c in old) { c.name = "HCT15_old_flag"; c.SetParent(null, false); UnityEngine.Object.Destroy(c.gameObject); }
            old.Clear();
            if (v.Flag) FlagWave.Create(host, v.FlagAt, v.FlagL, v.FlagH, PropDresser.PropMaterial());
            foreach (Transform c in host) if (c.name.StartsWith("HCT15_head_")) old.Add(c);
            foreach (Transform c in old) { c.name = "HCT15_old_head"; c.SetParent(null, false); UnityEngine.Object.Destroy(c.gameObject); }
            old.Clear();
            for (int i = 0; i < v.HeadPos.Count && v.HeadMesh != null; i++)
            {
                var go = new GameObject("HCT15_head_" + i);
                go.transform.SetParent(host, false);
                go.transform.localPosition = v.HeadPos[i];
                go.layer = host.gameObject.layer;
                go.AddComponent<MeshFilter>().sharedMesh = v.HeadMesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterials = v.HeadMats; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            foreach (Transform c in host) if (c.name.StartsWith("HCT15_leg_")) old.Add(c);
            if (old.Count == 0 && v.LegPos.Count == 0) return;
            int layer = host.gameObject.layer;
            foreach (Transform c in old) { c.name = "HCT15_old_leg"; c.SetParent(null, false); UnityEngine.Object.Destroy(c.gameObject); }
            for (int i = 0; i < v.LegPos.Count && v.LegMesh != null; i++)
            {
                var go = new GameObject("HCT15_leg_" + i);
                go.transform.SetParent(host, false);
                go.transform.localPosition = v.LegPos[i];
                go.layer = layer;
                go.AddComponent<MeshFilter>().sharedMesh = v.LegMesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterials = v.LegMats; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (v.ShinMesh != null) Shin(go.transform, v.ShinMesh, v.ShinMats, v.ShinAt, layer);
            }
            TrotBob tb = model.GetComponentInParent<TrotBob>() ?? model.GetComponentInChildren<TrotBob>();
            if (tb != null) { tb.LegPhase = v.LegPhase; tb.Swing = v.Swing; tb.KneeBend = v.KneeBend; tb.Cadence = v.Cadence; tb.Rescan(); }
        }

        /// <summary>
        /// 2.0.26: the colour indices of this car a race may wear - all of them, unless the active round's "Cars" list names
        /// one or more of its colours (a colour's id, or the car's own for colour 0): then only those (the Ruta D-81 lists
        /// the four red F-150s, so the grid, the traffic and the player's up/down never show a white or blue one).
        /// </summary>
        private static List<int> AllowedVariants(string baseId, List<Variant> list)
        {
            var all = new List<int>();
            for (int i = 0; i < list.Count; i++) all.Add(i);
            try
            {
                List<string> round = RoundCars();
                if (round == null) return all;
                var named = new List<int>();
                for (int i = 0; i < list.Count; i++)
                {
                    string id = list[i].Id ?? "";
                    foreach (string r in round)
                        if (string.Equals(r, id, StringComparison.OrdinalIgnoreCase) || (i == 0 && string.Equals(r, baseId, StringComparison.OrdinalIgnoreCase))) { named.Add(i); break; }
                }
                // the base car alone on the list = every colour (the carretas: "cl_carreta" lists all eleven)
                if (named.Count == 0 || (named.Count == 1 && named[0] == 0)) return all;
                return named;
            }
            catch (Exception) { return all; }
        }

        /// <summary>Up/down in car selection.</summary>
        public static bool NextVariant(Transform model, IInputUser user, bool next, bool persist)
        {
            List<Variant> list;
            if (model == null || !VariantsOf(model.name, out list)) return false;
            int n = list != null ? list.Count : 1;
            if (n <= 1) return true;
            int idx = (CurrentVariant(model, list) + (next ? 1 : n - 1)) % n;
            List<int> allowed = AllowedVariants(BaseId(model.name), list);   // 2.0.26: skip the colours the round leaves out
            if (allowed.Count < n) { int cur = CurrentVariant(model, list); int at = allowed.IndexOf(cur); idx = allowed[((at < 0 ? 0 : at) + (next ? 1 : allowed.Count - 1)) % allowed.Count]; }
            ApplyVariant(model, idx, user != null ? user.PlayerIndex : -1);
            try { if (persist && user != null && CarSelectionManager.Instance != null) CarSelectionManager.Instance.AddColorPersistence(user.Id, BaseId(model.name), idx); }
            catch (Exception) { }
            Debug.Log("[HCT15] street car '" + BaseId(model.name) + "': colour " + idx + " of " + n + " (" + list[idx].Id + ")");
            return true;
        }

        public static bool RefreshVariantIndex(Transform model, IInputUser user)
        {
            List<Variant> list;
            if (model == null || !VariantsOf(model.name, out list)) return false;
            if (user != null) ApplyVariant(model, CurrentVariant(model, list), user.PlayerIndex);
            return true;
        }

        public static bool VariantCount(string carName, out int count)
        {
            List<Variant> list;
            count = 1;
            if (!VariantsOf(carName, out list)) return false;
            count = list != null && list.Count > 0 ? list.Count : 1;
            return true;
        }

        public static bool VariantSkinName(string carName, int index, out string skin)
        {
            List<Variant> list;
            skin = null;
            if (!VariantsOf(carName, out list)) return false;
            skin = list != null && list.Count > 0 ? list[((index % list.Count) + list.Count) % list.Count].Id : BaseId(carName);
            return true;
        }

        private static bool ParseColour(string s, out Color c)
        {
            c = Color.black;
            return !string.IsNullOrEmpty(s) && ColorUtility.TryParseHtmlString(s.Trim(), out c);
        }

        /// <summary>
        /// The retail bodies carry their paint in the VERTEX COLOURS (no texture), with the light baked in:
        /// one hue, from dark sills to a bright roof. That hue is found as the most common saturated one, and
        /// only those vertices are repainted - glass, lights, chrome, grille and tyres are black, grey, blue
        /// or red and keep their colour. Each triangle takes the colour of the side of the split its centre is
        /// on (the mesh is unwelded for that, so the line is sharp instead of a smear across a big panel), and
        /// the new colour keeps the old brightness relative to the paint's median, so the baked shading stays.
        /// A colour with r below zero means "leave this half alone". Above roofY, the triangles that face up
        /// take the high colour even if they are glass (the Specter's glass roof becomes a taxi's yellow one),
        /// lit as brightly as the painted roof around them. Null when the mesh cannot be read.
        /// Tools\car_repaint.py does exactly the same from the decoded car, for the meshes that are not readable.
        /// </summary>
        public static Mesh RepaintBody(Mesh src, Color low, Color high, float splitY, float roofY)
        {
            if (src == null || !src.isReadable) return null;
            Color[] cols = src.colors;
            Vector3[] pos = src.vertices;
            if (cols == null || cols.Length != pos.Length || pos.Length == 0) return null;

            // the paint: the busiest 10-degree hue bin among the saturated vertices
            int n = pos.Length;
            float[] hue = new float[n], sat = new float[n], val = new float[n];
            int[] bins = new int[36];
            for (int i = 0; i < n; i++)
            {
                Color.RGBToHSV(cols[i], out hue[i], out sat[i], out val[i]);
                if (sat[i] > 0.4f && val[i] > 0.06f) bins[Mathf.Min(35, (int)(hue[i] * 36f))]++;
            }
            int peak = 0;
            for (int b = 1; b < 36; b++) if (bins[b] > bins[peak]) peak = b;
            if (bins[peak] == 0) return null;
            float centre = (peak + 0.5f) / 36f;
            bool[] isPaint = new bool[n];
            var paintVals = new List<float>();
            for (int i = 0; i < n; i++)
            {
                float dh = Mathf.Abs(hue[i] - centre);
                dh = Mathf.Min(dh, 1f - dh);
                isPaint[i] = sat[i] > 0.4f && val[i] > 0.02f && dh < 0.11f;
                if (isPaint[i]) paintVals.Add(val[i]);
            }
            paintVals.Sort();
            float median = Mathf.Max(0.02f, paintVals[paintVals.Count / 2]);
            float lh = 0f, ls = 0f, lv = 0f, hh = 0f, hs = 0f, hv = 0f;
            Color.RGBToHSV(low, out lh, out ls, out lv);
            Color.RGBToHSV(high, out hh, out hs, out hv);

            // unweld: three fresh vertices per triangle, every attribute the source has
            Vector3[] nrm = src.normals;
            Vector4[] tan = src.tangents;
            Vector2[] uv = src.uv, uv2 = src.uv2;
            bool hasN = nrm != null && nrm.Length == n, hasT = tan != null && tan.Length == n;
            bool hasU = uv != null && uv.Length == n, hasU2 = uv2 != null && uv2.Length == n;
            var subs = new List<int[]>();
            int total = 0;
            for (int s = 0; s < src.subMeshCount; s++) { int[] tri = src.GetTriangles(s); subs.Add(tri); total += tri.Length; }
            var P = new Vector3[total]; var C = new Color[total];
            var N = hasN ? new Vector3[total] : null; var T = hasT ? new Vector4[total] : null;
            var U = hasU ? new Vector2[total] : null; var U2 = hasU2 ? new Vector2[total] : null;
            // how bright the painted roof is against the median: what a painted-over glass roof gets
            var lit = new List<float>();
            foreach (int[] tri in subs)
                for (int t = 0; t + 2 < tri.Length; t += 3)
                    if (FacesUp(pos, tri, t) && (pos[tri[t]].y + pos[tri[t + 1]].y + pos[tri[t + 2]].y) / 3f > splitY)
                        for (int c = 0; c < 3; c++) if (isPaint[tri[t + c]]) lit.Add(val[tri[t + c]]);
            lit.Sort();
            float roofV = lit.Count > 0 ? lit[lit.Count / 2] / median : 1.5f;
            var newSubs = new List<int[]>();
            int k = 0, repainted = 0, roofed = 0;
            foreach (int[] tri in subs)
            {
                int[] idx = new int[tri.Length];
                for (int t = 0; t + 2 < tri.Length; t += 3)
                {
                    float cy = (pos[tri[t]].y + pos[tri[t + 1]].y + pos[tri[t + 2]].y) / 3f;
                    bool top = cy > splitY;
                    Color target = top ? high : low;
                    float th = top ? hh : lh, ts = top ? hs : ls, tv = top ? hv : lv;
                    bool roof = cy > roofY && high.r >= 0f && FacesUp(pos, tri, t);
                    for (int c = 0; c < 3; c++)
                    {
                        int v = tri[t + c];
                        P[k] = pos[v];
                        if (hasN) N[k] = nrm[v];
                        if (hasT) T[k] = tan[v];
                        if (hasU) U[k] = uv[v];
                        if (hasU2) U2[k] = uv2[v];
                        Color col = cols[v];
                        if (roof && !isPaint[v])
                        {
                            col = Color.HSVToRGB(hh, hs, Mathf.Clamp01(hv * roofV));
                            col.a = 0f;   // the paint mask: this is body now, not glass
                            roofed++;
                        }
                        else if (isPaint[v] && target.r >= 0f)
                        {
                            Color nc = Color.HSVToRGB(th, ts, Mathf.Clamp01(tv * val[v] / median));
                            nc.a = col.a;
                            col = nc;
                            repainted++;
                        }
                        C[k] = col;
                        idx[t + c] = k++;
                    }
                }
                newSubs.Add(idx);
            }
            Mesh m = new Mesh();
            if (total > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.vertices = P;
            if (hasN) m.normals = N;
            if (hasT) m.tangents = T;
            if (hasU) m.uv = U;
            if (hasU2) m.uv2 = U2;
            m.colors = C;
            m.subMeshCount = newSubs.Count;
            for (int s = 0; s < newSubs.Count; s++) m.SetTriangles(newSubs[s], s);
            m.RecalculateBounds();
            Debug.Log("[HCT15] repainted '" + src.name + "': paint hue " + (centre * 360f).ToString("0") + " deg, "
                      + paintVals.Count + " of " + n + " vertices, median brightness " + median.ToString("0.00")
                      + " -> " + repainted + " unwelded vertices recoloured, " + roofed / 3 + " roof glass triangles painted");
            return m;
        }

        private static bool FacesUp(Vector3[] pos, int[] tri, int t)
        {
            Vector3 f = Vector3.Cross(pos[tri[t + 1]] - pos[tri[t]], pos[tri[t + 2]] - pos[tri[t]]);
            return Mathf.Abs(f.y) > 0.9f * (f.magnitude + 1e-12f);
        }

        private static GameObject BodyPrefab(CarModel donor, AssetLibrary.AssetDef body, CarDef d, out float bubbleRise, out Vector3 camera)
        {
            bubbleRise = 0f;
            camera = Vector3.zero;
            GameObject inst = DonorCopy(donor, d);
            MeshFilter donorBody = BodyFilter(inst);
            if (donorBody == null) { UnityEngine.Object.Destroy(inst); return null; }
            Bounds db = donorBody.sharedMesh.bounds;
            float fittedHeight;
            FitXf xf;
            Mesh baked = Fit(body.Mesh, db, d, out fittedHeight, out xf);
            // The speech bubble hangs off CarModel.CarUIOffset, which is authored for the donor's own roof
            // (CarUIViewModel.Initialize). A body taller than the donor's swallows it, so lift it by as much
            // as our roof stands above the donor's - never lower, a shorter body keeps the retail position.
            float tallerBy = Mathf.Max(0f, d.Lift + fittedHeight - db.size.y);
            bubbleRise = tallerBy + d.BubbleUp;
            // Same idea for the chase camera: a body longer than the donor's grows backwards into the lens,
            // so pull back by the growth and lift by the height. CameraUp/CameraBack are then a nudge on top
            // of that, not a number that has to be re-measured every time a car changes shape.
            float longerBy = Mathf.Max(0f, Mathf.Max(baked.bounds.size.x, baked.bounds.size.z)
                                           - Mathf.Max(db.size.x, db.size.z));
            camera = new Vector3(0f, tallerBy + d.CameraUp, -(longerBy * 1.6f + d.CameraBack));
            // Height alone does not save the speech bubble, and it cannot be settled here: the bubble hangs
            // at a spot authored inside the car UI prefab (for the micro's donor, 1.60 up and 0.71 out - under
            // the roof of a body 1.97 tall and inside its width). Where it ends up is only known once the
            // bubble exists, so keep what the body and the camera are, and let PrepareSpeechBubbles place it.
            _body[d.Id] = baked.bounds;
            _cam[d.Id] = RaceCamera + donor.CameraPrefabPositionOffset + camera;
            MoveWheels(inst, d, db.size.x * d.Width * 0.5f, xf, baked);
            if (d.HideWheels) { HideDonorWheels(inst, d); HideDonorLights(inst, d); }
            float[] legPhase; float legSwing;
            int legs = AddLegs(inst, body, d, xf, out legPhase, out legSwing);
            AddWhip(inst, body, d, xf);
            AddRider(inst, body, d, xf);
            if (d.Lean > 0.01f || d.Spray > 0.01f)
            {
                // 2.0.24: on the water - the fitted body's own extent in sub_root's space places the spray and the wake
                var ride = inst.AddComponent<WaterRide>();
                Bounds bb = baked.bounds;
                ride.Lean = d.Lean; ride.Spray = d.Spray;
                ride.Stern = bb.min.z; ride.Nose = bb.max.z; ride.HalfWidth = bb.extents.x; ride.Deck = bb.max.y * 0.35f;
            }
            if (d.Bob > 0.001f || legs > 0) { var bob = inst.AddComponent<TrotBob>(); bob.Amplitude = d.Bob; bob.LegPhase = legPhase; bob.Swing = legSwing; bob.KneeBend = _lastKnee; bob.Cadence = _lastCadence; bob.TopKph = d.TopSpeedKph; }
            baked.name = "HCT15_car_" + d.Id;
            Transform sub = inst.transform.Find("root/sub_root");
            int swapped = 0;
            foreach (string part in BodyParts)
            {
                Transform t = sub.Find(part);
                if (t == null) continue;
                MeshFilter mf = t.GetComponent<MeshFilter>();
                if (mf != null) { mf.sharedMesh = baked; swapped++; }
                Renderer r = t.GetComponent<Renderer>();
                if (r != null) { r.sharedMaterials = body.Materials; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
            }
            Debug.Log("[HCT15] street car '" + d.Id + "': donor body " + db.size.ToString("0.00") + " (" + donor.ModelPrefab.name
                      + "), model " + body.Size.ToString("0.00") + " -> fitted " + baked.bounds.size.ToString("0.00")
                      + ", bubble +" + bubbleRise.ToString("0.00") + ", camera " + camera.ToString("0.00")
                      + ", " + swapped + " level(s) of detail swapped" + WheelInfo(inst));
            return inst;
        }

        /// <summary>How far a box sticks out past a point in one direction.</summary>
        private static float Reach(Bounds b, Vector3 from, Vector3 dir)
        {
            float best = 0f;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x,
                                        (i & 2) == 0 ? b.min.y : b.max.y,
                                        (i & 4) == 0 ? b.min.z : b.max.z);
                float p = Vector3.Dot(c - from, dir);
                if (p > best) best = p;
            }
            return best;
        }

        /// <summary>A longer body needs a longer wheelbase: the donor's wheel groups are moved along it (and a bus gets bigger wheels).</summary>
        private static void MoveWheels(GameObject inst, CarDef d, float bodyHalfWidth = 0f, FitXf xf = null, Mesh baked = null)
        {
            float spread = d.WheelSpread > 0.01f ? d.WheelSpread : d.Length;
            // 2.0.14: CarDef.Axles - the model's arches, through the same turn / scale / shift the body got (FitXf), decide
            // where each wheel group goes: the group whose wheels sit ahead takes the front one. WheelSpread only scaled the
            // groups' z (the kombi's sit at +-1.05, so x1.95 reached 2.05 for arches at 2.14, and x1.53 left the wheels
            // half a unit inside the arches - 2026-10-07: "two times further away than before").
            float axleFront = float.NaN, axleBack = float.NaN;
            if (d.Axles != null && d.Axles.Length >= 2 && xf != null)
            {
                float a0 = AlongLength(xf, d.Axles[0]), a1 = AlongLength(xf, d.Axles[1]);
                axleFront = Mathf.Max(a0, a1); axleBack = Mathf.Min(a0, a1);
            }
            // The track: the donor's wheel groups sit where the donor's flanks are; a wider body (the micro's,
            // 1.59 on a 1.56 van whose wheels are at +-0.58) showed them tucked under it (2026-09-24: "off their
            // axis, they should be aligned with the edge"). WheelTrack 0 moves them out so the tyre's outer
            // face sits at the body's flank: the groups hold both wheels of an axle, so their X scale is the lever.
            // 2.0.13 (WheelTrack 0): each axle's tyres get their outer face a little proud of the body's flank
            // (WheelProud), measured from that wheel group's own renderers after WheelScale - the van's rear wheels
            // reach further out than its front ones, so each group gets its own factor. The old law - the detector's
            // centre minus a guessed tyre half-width - left the face flush with the flank, where the arches and mud
            // flaps modelled on the micro's flank hid it (2026-10-07: the wheels sat away from the micro's
            // outer edge; they should be level with it).
            // 2.0.14c: the body's BOX half-width counts the mirrors and boards (the micro's model is 3.32 wide for a 2.50 body),
            // so the tyres were set 0.35 outside the real flank - reported as "the wheels are away from the bus's outer edge" (2.0.12:
            // 0.19 out), then "two times further away" (2.0.13/14). The flank is measured on the fitted mesh at wheel height round
            // each axle (FlankHalfWidth); the tyre's outer face goes WheelProud past it.
            bool auto = d.WheelTrack < 0.01f && bodyHalfWidth > 0.01f;
            float track = auto ? 1f : (d.WheelTrack < 0.01f ? 1f : d.WheelTrack);
            if (!auto && Mathf.Abs(spread - 1f) < 0.01f && Mathf.Abs(d.WheelScale - 1f) < 0.01f && Mathf.Abs(track - 1f) < 0.01f) return;
            float trackSum = 0f; int groups = 0; var faces = new System.Text.StringBuilder();
            foreach (Transform t in inst.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name.ToLowerInvariant();
                if (n.Contains("wheels_detector") || !n.StartsWith("wheels_")) continue;
                float tr = track;
                Vector3 p = t.localPosition;
                t.localPosition = new Vector3(p.x, p.y, p.z * spread);
                float axleZ = WheelCentreZ(inst, t);
                if (!float.IsNaN(axleFront))
                {
                    float target = axleZ >= 0f ? axleFront : axleBack;
                    t.position += inst.transform.TransformVector(new Vector3(0f, 0f, target - axleZ));
                    faces.Append(" ").Append(t.name).Append(" z ").Append(axleZ.ToString("0.00")).Append("->").Append(target.ToString("0.00"));
                    axleZ = target;
                }
                if (auto)
                {
                    float flank = baked != null ? FlankHalfWidth(baked, axleZ, 0.7f, 0.1f, 1.1f) : 0f;
                    float edge = flank > 0.2f ? flank : bodyHalfWidth;
                    float outer = WheelOuterX(inst, t) * d.WheelScale;
                    tr = outer > 0.05f ? Mathf.Clamp((edge + WheelProud) / outer, 0.5f, 1.8f) : 1f;
                    faces.Append(" ").Append(t.name).Append(" x ").Append((outer / d.WheelScale).ToString("0.00")).Append("->").Append((outer * tr).ToString("0.00")).Append(" (flank ").Append(edge.ToString("0.00")).Append(")");
                }
                trackSum += tr; groups++;
                Vector3 sc = t.localScale * d.WheelScale;
                sc.x *= tr;
                t.localScale = sc;
            }
            float mean = groups > 0 ? trackSum / groups : track;
            if (Mathf.Abs(mean - 1f) > 0.01f)
                foreach (Transform t in inst.GetComponentsInChildren<Transform>(true))
                    if (t.name.ToLowerInvariant().Contains("wheels_detector")) { Vector3 q = t.localPosition; t.localPosition = new Vector3(q.x * mean, q.y, q.z); }
            if (Mathf.Abs(mean - 1f) > 0.01f) Debug.Log("[HCT15] street car '" + d.Id + "': wheel track x" + mean.ToString("0.00") + " (body half-width " + bodyHalfWidth.ToString("0.00") + "; tyre faces" + faces + ")");
        }
        private const float WheelProud = 0.02f;   // units the tyre's outer face stands past the body's flank (the arches drawn on it are 0.02 proud themselves)

        /// <summary>The fitted body's half-width at wheel height round an axle: the largest |x| of the mesh's vertices with |z - axleZ| &lt; zHalf and yLo &lt; y &lt; yHi (the flank, its skirt and arches - not the mirrors up front).</summary>
        private static float FlankHalfWidth(Mesh baked, float axleZ, float zHalf, float yLo, float yHi)
        {
            // each side on its own, the smaller one wins: a body is symmetric, its fittings (a door step, a board) are not
            float right = 0f, left = 0f;
            try
            {
                Vector3[] v = baked.vertices;
                for (int i = 0; i < v.Length; i++)
                {
                    if (Mathf.Abs(v[i].z - axleZ) >= zHalf || v[i].y <= yLo || v[i].y >= yHi) continue;
                    if (v[i].x >= 0f) right = Mathf.Max(right, v[i].x); else left = Mathf.Max(left, -v[i].x);
                }
            }
            catch (Exception) { }
            return right > 0f && left > 0f ? Mathf.Min(right, left) : Mathf.Max(right, left);
        }

        /// <summary>A model-unit position along the body's nose axis (nose positive) -> the car's length axis, as Fit placed the body (the mesh is mirrored by the importer, so the nose is -X in it).</summary>
        private static float AlongLength(FitXf xf, float along)
        {
            Vector3 q = xf.Point(new Vector3(-along, 0f, 0f));
            return xf.ZIsLength ? q.z : q.x;
        }

        /// <summary>The centre (z in the car's space) of a wheel group's renderers, as they are now.</summary>
        private static float WheelCentreZ(GameObject inst, Transform group)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            try
            {
                foreach (Renderer r in group.GetComponentsInChildren<Renderer>(true))
                {
                    Vector3 a = inst.transform.InverseTransformPoint(r.bounds.min), b = inst.transform.InverseTransformPoint(r.bounds.max);
                    lo = Mathf.Min(lo, a.z, b.z); hi = Mathf.Max(hi, a.z, b.z);
                }
            }
            catch (Exception) { }
            return hi > lo ? (lo + hi) * 0.5f : inst.transform.InverseTransformPoint(group.position).z;
        }

        /// <summary>How far out a wheel group's renderers reach (|x| in the car's space), as they are now.</summary>
        private static float WheelOuterX(GameObject inst, Transform group)
        {
            float outer = 0f;
            try
            {
                foreach (Renderer r in group.GetComponentsInChildren<Renderer>(true))
                {
                    Vector3 a = inst.transform.InverseTransformPoint(r.bounds.min), b = inst.transform.InverseTransformPoint(r.bounds.max);
                    outer = Mathf.Max(outer, Mathf.Abs(a.x), Mathf.Abs(b.x));
                }
            }
            catch (Exception) { }
            return outer;
        }

        public class LegsJson { public string Leg = ""; public List<List<float>> Hips = new List<List<float>>(); public List<float> Phase = new List<float>(); public float Swing = 30f; public FlagJson Flag;
            public string Shin = ""; public float Knee; public float KneeBend = 45f; public float Cadence;   // 2.0.28: a shin hung at the knee (Knee m below the hip, file units) that bends as the leg swings forward; Cadence = strides a second at full speed (0 = the trot law)
            public string Head = ""; public List<List<float>> Necks = new List<List<float>>(); }
        public class FlagJson { public List<float> Pole = new List<float>(); public float Length = 0.5f, Height = 0.3f; }
        public class RiderJson { public string Model = ""; public List<float> Seat = new List<float>(); public string Comment = ""; }

        /// <summary>
        /// 2.0.24 (the rider does tricks when the jet ski jumps): the body model's rider.json {Model, Seat}
        /// -> 'HCT15_rider' under every level of detail at the seat (hips) as the body was fitted; WaterRide turns and lifts
        /// it in the air (superman, standing, leaning out, a kick) - the body itself flies and now and then spins.
        /// </summary>
        private static void AddRider(GameObject inst, AssetLibrary.AssetDef body, CarDef d, FitXf xf)
        {
            try
            {
                string file = Path.Combine(body.Dir ?? "", "rider.json");
                if (!File.Exists(file)) return;
                RiderJson rj = Newtonsoft.Json.JsonConvert.DeserializeObject<RiderJson>(File.ReadAllText(file));
                if (rj == null || rj.Seat == null || rj.Seat.Count < 3) return;
                AssetLibrary.AssetDef rider = AssetLibrary.Get(rj.Model);
                if (rider == null || !AssetLibrary.EnsureLoaded(rider) || rider.Mesh == null) { Debug.LogWarning("[HCT15] street car '" + d.Id + "': rider model '" + rj.Model + "' not found"); return; }
                Mesh m = UnityEngine.Object.Instantiate(rider.Mesh);
                Vector3[] v = m.vertices;
                for (int i = 0; i < v.Length; i++) v[i] = xf.Dir(v[i]);
                m.vertices = v; m.RecalculateNormals(); m.RecalculateBounds(); m.name = "HCT15_rider_" + d.Id;
                Vector3 seat = xf.Point(body.FileToMesh(new Vector3(rj.Seat[0], rj.Seat[1], rj.Seat[2])));
                Transform sub = inst.transform.Find("root/sub_root");
                int n = 0;
                foreach (string part in BodyParts)
                {
                    if (part == "first_person") continue;
                    Transform host = sub != null ? sub.Find(part) : null;
                    if (host == null) continue;
                    var go = new GameObject("HCT15_rider");
                    go.transform.SetParent(host, false);
                    go.transform.localPosition = seat;
                    go.layer = host.gameObject.layer;
                    go.AddComponent<MeshFilter>().sharedMesh = m;
                    var r = go.AddComponent<MeshRenderer>();
                    r.sharedMaterials = rider.Materials; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    n++;
                }
                Debug.Log("[HCT15] street car '" + d.Id + "': rider '" + rj.Model + "' on " + n + " level(s) of detail, free to move");
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] street car '" + d.Id + "': rider: " + ex.Message); }
        }

        public class WhipJson { public string Model = ""; public List<float> Base = new List<float>(); public float Height = 3f, FlagLength = 0.5f, FlagHeight = 0.3f; public string FlagStyle = ""; }

        /// <summary>
        /// 2.0.22: a whip that sways (the mines' F-150: the pole moves with realistic physics and the
        /// reflective flag flutters). whip.json beside a body model names the pole model (its origin at the base,
        /// standing up) and the base in the body's file coordinates: the pole becomes a child of the body's first level of
        /// detail at the base as the body was fitted (like the carreta's legs), WhipSway springs it about its base from the
        /// truck's acceleration, bends and the road, and a FlagWave cloth flies from its tip.
        /// </summary>
        private static void AddWhip(GameObject inst, AssetLibrary.AssetDef body, CarDef d, FitXf xf)
        {
            try
            {
                string file = Path.Combine(body.Dir ?? "", "whip.json");
                if (!File.Exists(file)) return;
                WhipJson wj = Newtonsoft.Json.JsonConvert.DeserializeObject<WhipJson>(File.ReadAllText(file));
                if (wj == null || wj.Base == null || wj.Base.Count < 3) return;
                AssetLibrary.AssetDef pole = AssetLibrary.Get(wj.Model);
                if (pole == null || !AssetLibrary.EnsureLoaded(pole) || pole.Mesh == null) { Debug.LogWarning("[HCT15] street car '" + d.Id + "': whip model '" + wj.Model + "' not found"); return; }
                Transform sub = inst.transform.Find("root/sub_root");
                Transform host = null;
                foreach (string part in BodyParts) { host = sub != null ? sub.Find(part) : null; if (host != null) break; }
                if (host == null) return;
                Mesh m = UnityEngine.Object.Instantiate(pole.Mesh);
                Vector3[] v = m.vertices;
                for (int i = 0; i < v.Length; i++) v[i] = xf.Dir(v[i]);
                m.vertices = v; m.RecalculateNormals(); m.RecalculateBounds(); m.name = "HCT15_whip_" + d.Id;
                var b0 = new Vector3(wj.Base[0], wj.Base[1], wj.Base[2]);
                Vector3 basePos = xf.Point(body.FileToMesh(b0));
                var go = new GameObject("HCT15_whip");
                go.transform.SetParent(host, false);
                go.transform.localPosition = basePos;
                go.AddComponent<MeshFilter>().sharedMesh = m;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterials = pole.Materials; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                go.layer = host.gameObject.layer;
                // the axes in the host's space, through the same fit: up the pole, towards the nose
                Vector3 top = b0 + new Vector3(0f, wj.Height, 0f);
                Vector3 tip = xf.Point(body.FileToMesh(top)) - basePos;
                Vector3 nose = xf.Point(body.FileToMesh(b0 + new Vector3(1f, 0f, 0f))) - basePos;
                var sway = go.AddComponent<WhipSway>();
                sway.Up = tip.normalized; sway.Forward = Vector3.ProjectOnPlane(nose, sway.Up).normalized;
                // the flag at the tip, a little under it, flying back along -X of the file (like the carreta's)
                Vector3 flagTop = top - new Vector3(0f, 0.05f, 0f);
                Vector3 o = xf.Point(body.FileToMesh(flagTop)) - basePos;
                Vector3 al = xf.Point(body.FileToMesh(flagTop + new Vector3(-wj.FlagLength, 0f, 0f))) - xf.Point(body.FileToMesh(flagTop));
                Vector3 ah = xf.Point(body.FileToMesh(flagTop + new Vector3(0f, -wj.FlagHeight, 0f))) - xf.Point(body.FileToMesh(flagTop));
                FlagWave.Create(go.transform, o, al, ah, PropDresser.PropMaterial(), wj.FlagStyle == "reflective");
                Debug.Log("[HCT15] street car '" + d.Id + "': whip '" + wj.Model + "' at the bed, " + wj.Height.ToString("0.0") + " m, swaying, with a " + (wj.FlagStyle == "reflective" ? "reflective " : "") + "flag");
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] street car '" + d.Id + "': whip: " + ex.Message); }
        }

        /// <summary>
        /// 2.0.11: legs that move (the horse's legs). legs.json beside a body model names a leg model
        /// (its origin at the hip, hanging straight down) and the hips in the body's file coordinates: each becomes a
        /// child of the body's first level of detail at the hip as the body was fitted (FileToMesh, then FitXf), its
        /// mesh fitted with the same turn and scale; TrotBob swings them about the axis across the car.
        /// </summary>
        private static float _lastKnee, _lastCadence;   // 2.0.28: what the last AddLegs read (the knees, the cadence) for its TrotBob

        /// <summary>2.0.28: a shin hung at the knee of a leg (TrotBob bends it).</summary>
        private static void Shin(Transform leg, Mesh mesh, Material[] mats, Vector3 at, int layer)
        {
            var go = new GameObject("HCT15_shin");
            go.transform.SetParent(leg, false);
            go.transform.localPosition = at;
            go.layer = layer;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = mats; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private static int AddLegs(GameObject inst, AssetLibrary.AssetDef body, CarDef d, FitXf xf, out float[] phase, out float swing)
        {
            _lastKnee = 0f; _lastCadence = 0f;
            phase = new float[0]; swing = 0f;
            try
            {
                string file = Path.Combine(body.Dir ?? "", "legs.json");
                if (!File.Exists(file)) return 0;
                LegsJson lj = Newtonsoft.Json.JsonConvert.DeserializeObject<LegsJson>(File.ReadAllText(file));
                AssetLibrary.AssetDef leg = lj != null ? AssetLibrary.Get(lj.Leg) : null;
                if (leg == null || !AssetLibrary.EnsureLoaded(leg) || leg.Mesh == null) { Debug.LogWarning("[HCT15] street car '" + d.Id + "': leg model '" + (lj != null ? lj.Leg : "?") + "' not found"); return 0; }
                Transform sub = inst.transform.Find("root/sub_root");
                Transform host = null;
                foreach (string part in BodyParts) { host = sub != null ? sub.Find(part) : null; if (host != null) break; }
                if (host == null) return 0;
                Mesh m = UnityEngine.Object.Instantiate(leg.Mesh);
                Vector3[] v = m.vertices;
                for (int i = 0; i < v.Length; i++) v[i] = xf.Dir(v[i]);
                m.vertices = v; m.RecalculateNormals(); m.RecalculateBounds(); m.name = "HCT15_leg_" + d.Id;
                // 2.0.28: a shin below the knee (the andas' bearers walk: the knee bends as the leg comes forward)
                AssetLibrary.AssetDef shinDef = !string.IsNullOrEmpty(lj.Shin) ? AssetLibrary.Get(lj.Shin) : null;
                Mesh sm = null; Vector3 kneeAt = Vector3.zero;
                if (shinDef != null && AssetLibrary.EnsureLoaded(shinDef) && shinDef.Mesh != null)
                {
                    sm = UnityEngine.Object.Instantiate(shinDef.Mesh);
                    Vector3[] sv = sm.vertices;
                    for (int i = 0; i < sv.Length; i++) sv[i] = xf.Dir(sv[i]);
                    sm.vertices = sv; sm.RecalculateNormals(); sm.RecalculateBounds(); sm.name = "HCT15_shin_" + d.Id;
                    kneeAt = xf.Dir(new Vector3(0f, -lj.Knee, 0f));
                }
                _lastKnee = sm != null ? lj.KneeBend : 0f; _lastCadence = lj.Cadence;
                int n = 0;
                foreach (List<float> h in lj.Hips)
                {
                    if (h == null || h.Count < 3) continue;
                    var go = new GameObject("HCT15_leg_" + n);
                    go.transform.SetParent(host, false);
                    go.transform.localPosition = xf.Point(body.FileToMesh(new Vector3(h[0], h[1], h[2])));
                    go.AddComponent<MeshFilter>().sharedMesh = m;
                    var r = go.AddComponent<MeshRenderer>();
                    r.sharedMaterials = leg.Materials; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    go.layer = host.gameObject.layer;
                    if (sm != null) Shin(go.transform, sm, shinDef.Materials, kneeAt, go.layer);
                    n++;
                }
                // the neck and head that turn towards the bend (TrotBob): hung at each neck's base like the legs
                AssetLibrary.AssetDef head = !string.IsNullOrEmpty(lj.Head) ? AssetLibrary.Get(lj.Head) : null;
                if (head != null && AssetLibrary.EnsureLoaded(head) && head.Mesh != null && lj.Necks != null)
                {
                    Mesh hm = UnityEngine.Object.Instantiate(head.Mesh);
                    Vector3[] hv = hm.vertices;
                    for (int i = 0; i < hv.Length; i++) hv[i] = xf.Dir(hv[i]);
                    hm.vertices = hv; hm.RecalculateNormals(); hm.RecalculateBounds(); hm.name = "HCT15_head_" + d.Id;
                    int k = 0;
                    foreach (List<float> h in lj.Necks)
                    {
                        if (h == null || h.Count < 3) continue;
                        var go = new GameObject("HCT15_head_" + k++);
                        go.transform.SetParent(host, false);
                        go.transform.localPosition = xf.Point(body.FileToMesh(new Vector3(h[0], h[1], h[2])));
                        go.AddComponent<MeshFilter>().sharedMesh = hm;
                        var r = go.AddComponent<MeshRenderer>();
                        r.sharedMaterials = head.Materials; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        go.layer = host.gameObject.layer;
                    }
                }
                else if (!string.IsNullOrEmpty(lj.Head)) Debug.LogWarning("[HCT15] street car '" + d.Id + "': head model '" + lj.Head + "' not found");
                if (lj.Flag != null && lj.Flag.Pole != null && lj.Flag.Pole.Count >= 3)
                {
                    // the flag that flies (FlagWave): its pole top, free end and bottom through the body's fit
                    var top = new Vector3(lj.Flag.Pole[0], lj.Flag.Pole[1], lj.Flag.Pole[2]);
                    Vector3 o = xf.Point(body.FileToMesh(top));
                    Vector3 al = xf.Point(body.FileToMesh(top + new Vector3(-lj.Flag.Length, 0f, 0f))) - o;
                    Vector3 ah = xf.Point(body.FileToMesh(top + new Vector3(0f, -lj.Flag.Height, 0f))) - o;
                    FlagWave.Create(host, o, al, ah, PropDresser.PropMaterial());
                }
                phase = new float[n];
                for (int i = 0; i < n; i++) phase[i] = lj.Phase != null && i < lj.Phase.Count ? lj.Phase[i] : (i % 4 == 0 || i % 4 == 3 ? 0f : 0.5f);
                swing = lj.Swing;
                Debug.Log("[HCT15] street car '" + d.Id + "': " + n + " moving leg(s) of '" + lj.Leg + "', swing " + swing.ToString("0") + " deg");
                return n;
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] street car '" + d.Id + "': legs: " + ex.Message); return 0; }
        }

        /// <summary>The donor's tyres off: every renderer under its wheels_* groups (not the detectors, which are not drawn).</summary>
        private static void HideDonorWheels(GameObject inst, CarDef d)
        {
            int n = 0;
            foreach (Transform t in inst.GetComponentsInChildren<Transform>(true))
            {
                string name = t.name.ToLowerInvariant();
                if (!name.StartsWith("wheels_") || name.Contains("detector")) continue;
                foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true)) { r.enabled = false; n++; }
            }
            Debug.Log("[HCT15] street car '" + d.Id + "': the donor's wheels hidden (" + n + " renderer(s)), the body has its own");
        }

        /// <summary>
        /// 2.0.18 (with the carretas, the grid showed the carretas but also the rear lights of an
        /// invisible car, gone once the race started): a car that hides the donor's wheels (the carreta: a horse
        /// and a cart, no car under it) hides the donor's lights too - the brake lights the game lights on the grid and
        /// turns off once the race is on, glowing in the air behind the cart. Their meshes are emptied (the objects stay
        /// for the game's own switches, as SpriteCars does), and any other lamp renderer is switched off.
        /// </summary>
        private static readonly string[] LightParts = { "brake_lights", "lights", "night_lights", "head_lights", "tail_lights" };

        private static void HideDonorLights(GameObject inst, CarDef d)
        {
            int n = 0;
            foreach (MeshFilter mf in inst.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                for (Transform p = mf.transform; p != null && p != inst.transform; p = p.parent)
                    if (Array.IndexOf(LightParts, p.name.ToLowerInvariant()) >= 0) { mf.sharedMesh = null; n++; break; }
            }
            foreach (Renderer r in inst.GetComponentsInChildren<Renderer>(true))
            {
                string nm = r.name.ToLowerInvariant();
                if (nm.Contains("nitro")) continue;
                if (nm.Contains("brake") || nm.Contains("taillight") || nm.Contains("tail_light") || nm.Contains("exhaust") || nm.Contains("backfire"))
                { r.enabled = false; n++; }
            }
            Debug.Log("[HCT15] street car '" + d.Id + "': the donor's lights hidden (" + n + " part(s)) - no car under it");
        }

        /// <summary>Where the donor keeps its wheels - logged once so a body can be modelled around them.</summary>
        private static string WheelInfo(GameObject inst)
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                foreach (Transform t in inst.GetComponentsInChildren<Transform>(true))
                {
                    string n = t.name.ToLowerInvariant();
                    if (n.Contains("wheel") || n.Contains("tire") || n.Contains("tyre") || n.Contains("roda"))
                        sb.Append("  ").Append(t.name).Append(inst.transform.InverseTransformPoint(t.position).ToString("0.00"));
                }
                // the renderers under the wheel groups, with their box in the car's space (the car stands unturned here)
                foreach (Transform g in inst.GetComponentsInChildren<Transform>(true))
                {
                    string gn = g.name.ToLowerInvariant();
                    if (!gn.StartsWith("wheels_") || gn.Contains("detector")) continue;
                    foreach (Renderer r in g.GetComponentsInChildren<Renderer>(true))
                    {
                        Vector3 a = inst.transform.InverseTransformPoint(r.bounds.min), b = inst.transform.InverseTransformPoint(r.bounds.max);
                        sb.Append("\n    ").Append(g.name).Append("/").Append(r.transform.parent == g ? "" : r.transform.parent.name + "/").Append(r.name)
                          .Append(" local ").Append(r.transform.localPosition.ToString("0.00")).Append(" scale ").Append(r.transform.localScale.ToString("0.00"))
                          .Append(" box x ").Append(Mathf.Min(a.x, b.x).ToString("0.00")).Append("..").Append(Mathf.Max(a.x, b.x).ToString("0.00"))
                          .Append(" y ").Append(Mathf.Min(a.y, b.y).ToString("0.00")).Append("..").Append(Mathf.Max(a.y, b.y).ToString("0.00"))
                          .Append(" z ").Append(Mathf.Min(a.z, b.z).ToString("0.00")).Append("..").Append(Mathf.Max(a.z, b.z).ToString("0.00"));
                    }
                }
                return sb.Length > 0 ? "; wheels:" + sb : "";
            }
            catch (Exception) { return ""; }
        }

        /// <summary>
        /// A copy of the model's mesh turned to face the car's forward, fitted to the donor's own body box and
        /// set on its ground plane. Length, width and height are scaled independently against that box, so the
        /// author sets the car's proportions outright: Height 1 is as low as every retail car (the game's cars
        /// are stylised and flat - a true-scale bus towers over the grid and out of the camera), Length 1.35 is
        /// a third longer than the donor. The model's own metres never leak through.
        /// </summary>
        /// <summary>What Fit did to the model: turn, per-axis scale, then shift - so points of the model (a leg's hip) follow the body.</summary>
        public class FitXf
        {
            public Quaternion Rot = Quaternion.identity; public float Kx = 1f, Ky = 1f, Kz = 1f; public bool ZIsLength = true; public Vector3 Shift;
            public Vector3 Dir(Vector3 v) { v = Rot * v; return ZIsLength ? new Vector3(v.x * Kx, v.y * Ky, v.z * Kz) : new Vector3(v.x * Kz, v.y * Ky, v.z * Kx); }
            public Vector3 Point(Vector3 v) { return Dir(v) + Shift; }
        }

        private static Mesh Fit(Mesh src, Bounds db, CarDef d, out float fittedHeight)
        {
            FitXf xf;
            return Fit(src, db, d, out fittedHeight, out xf);
        }

        private static Mesh Fit(Mesh src, Bounds db, CarDef d, out float fittedHeight, out FitXf xf)
        {
            Mesh m = UnityEngine.Object.Instantiate(src);
            Vector3[] v = m.vertices;
            Quaternion rot = Quaternion.Euler(0f, d.Yaw, 0f);
            for (int i = 0; i < v.Length; i++) v[i] = rot * v[i];
            Bounds rb = Box(v);
            float donorLen = Mathf.Max(db.size.x, db.size.z), donorWide = Mathf.Min(db.size.x, db.size.z);
            float myLen = Mathf.Max(rb.size.x, rb.size.z), myWide = Mathf.Min(rb.size.x, rb.size.z);
            float kz = myLen > 0.001f ? donorLen * Mathf.Max(0.2f, d.Length) / myLen : 1f;
            float kx = myWide > 0.001f ? donorWide * Mathf.Max(0.2f, d.Width) / myWide : 1f;
            float ky = rb.size.y > 0.001f ? db.size.y * Mathf.Max(0.2f, d.Height) / rb.size.y : 1f;
            bool zIsLength = db.size.z >= db.size.x;
            for (int i = 0; i < v.Length; i++)
                v[i] = zIsLength ? new Vector3(v[i].x * kx, v[i].y * ky, v[i].z * kz)
                                 : new Vector3(v[i].x * kz, v[i].y * ky, v[i].z * kx);
            rb = Box(v);
            fittedHeight = rb.size.y;
            Vector3 shift = new Vector3(db.center.x - rb.center.x + (zIsLength ? 0f : d.Shift), db.min.y + d.Lift - rb.min.y, db.center.z - rb.center.z + (zIsLength ? d.Shift : 0f));
            xf = new FitXf { Rot = rot, Kx = kx, Ky = ky, Kz = kz, ZIsLength = zIsLength, Shift = shift };
            for (int i = 0; i < v.Length; i++) v[i] += shift;
            m.vertices = v;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        private static Bounds Box(Vector3[] v)
        {
            if (v.Length == 0) return new Bounds();
            Bounds b = new Bounds(v[0], Vector3.zero);
            for (int i = 1; i < v.Length; i++) b.Encapsulate(v[i]);
            return b;
        }

        /// <summary>CarModel is a plain serialisable class, not a Unity object: copy every field, then override.</summary>
        private static CarModel CloneCar(CarModel source)
        {
            CarModel clone = new CarModel();
            Type t = typeof(CarModel);
            while (t != null && t != typeof(object))
            {
                foreach (System.Reflection.FieldInfo f in t.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly))
                {
                    if (f.IsInitOnly || f.IsLiteral) continue;
                    f.SetValue(clone, f.GetValue(source));
                }
                t = t.BaseType;
            }
            return clone;
        }
    }

    /// <summary>
    /// 2.0.11: the carreta's trot - the bodies under root/sub_root (every level of detail) rise and fall in a gallop
    /// rhythm that quickens with the car's speed, measured from its own movement. Added to the parked prefab, so every
    /// copy the game makes of it (the player's, the rivals', the car selection's) carries it.
    /// </summary>
    public class TrotBob : MonoBehaviour
    {
        public float Amplitude = 0.07f;
        public float[] LegPhase = new float[0];   // per leg: its share of a stride (diagonal pairs at the trot)
        public float Swing = 30f;                 // degrees fore and aft at full speed
        public float KneeBend;                    // 2.0.28: degrees a shin (HCT15_shin under a leg) folds back while that leg swings forward
        public float Cadence;                     // 2.0.28: strides a second at full speed - a walk (the andas' bearers 0.95); 0 = the trot law
        public float TopKph;                      // 2.1.4: the car's own top speed (CarDef.TopSpeedKph), what a walk is measured against - a rival's
                                                  // controller reports its donor's (the kombi, ~300 km/h): its bearers stood still at 2 % of a stride
        private Transform[] _parts, _legs, _shins = new Transform[0];
        private float[] _baseY;
        private Vector3 _last;
        private float _phase, _speed, _lookAt, _max;   // _max: 2.0.28, the car's own top speed (a walk is measured against it)
        private bool _ready;
        private ICarTrackView _view;
        private float _logAt;
        private static bool? _trotLog;
        private static bool TrotLog { get { if (_trotLog == null) _trotLog = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-hct15-trotlog") >= 0; return _trotLog.Value; } }

        /// <summary>The car's speed in km/h from the race view that owns this body (player or rival), else -1.</summary>
        private float Kph()
        {
            if (_view == null && Time.unscaledTime >= _lookAt)
            {
                _lookAt = Time.unscaledTime + 1f;
                foreach (MonoBehaviour mb in GetComponentsInParent<MonoBehaviour>(true))
                    if (mb is ICarTrackView) { _view = (ICarTrackView)mb; break; }
            }
            try { if (_view != null && _view.Controller != null) { _max = _view.Controller.MaxSpeedKPH; return _view.Controller.CurrentSpeedKPH; } return -1f; }
            catch (System.Exception) { _view = null; return -1f; }
        }

        /// <summary>A colour change put other legs on the body: find them again.</summary>
        public void Rescan() { if (_parts == null) return; var legs = new List<Transform>(); foreach (Transform p in _parts) for (int i = 0; ; i++) { Transform l = p.Find("HCT15_leg_" + i); if (l == null) break; legs.Add(l); } _legs = legs.ToArray(); FindShins(); _heads = Find("HCT15_head_"); }

        private void FindShins() { _shins = new Transform[_legs.Length]; for (int i = 0; i < _legs.Length; i++) _shins[i] = _legs[i] != null ? _legs[i].Find("HCT15_shin") : null; }

        private Transform[] Find(string prefix)
        {
            var list = new List<Transform>();
            foreach (Transform p in _parts) for (int i = 0; ; i++) { Transform l = p.Find(prefix + i); if (l == null) break; list.Add(l); }
            return list.ToArray();
        }

        // 2.0.11: the head turns towards the bend (the horse's head leans to the side being turned to)
        private Transform[] _heads = new Transform[0];
        private float _turn;
        private int _viewKind = -1;
        private PlayerCarTrackView _pv; private AnotherPlayerCarTrackView _av; private EnemyCarTrackView _ev;
        public const float HeadYaw = 28f, HeadRoll = 10f;

        /// <summary>The steering the game feeds the car body's tilt: the player's stick, a rival's road curvature (-1..1, right +).</summary>
        private float Turning()
        {
            if (_viewKind < 0)
            {
                _pv = GetComponentInParent<PlayerCarTrackView>();
                _av = _pv == null ? GetComponentInParent<AnotherPlayerCarTrackView>() : null;
                _ev = _pv == null && _av == null ? GetComponentInParent<EnemyCarTrackView>() : null;
                _viewKind = _pv != null ? 1 : _av != null ? 2 : _ev != null ? 3 : 0;
            }
            try
            {
                switch (_viewKind)
                {
                    case 1: return GameAccess.TurningDirection(_pv);
                    case 2: return GameAccess.TurningDirection(_av);
                    case 3: return GameAccess.TurningDirection(_ev);
                }
            }
            catch (System.Exception) { }
            return 0f;
        }

        private void Start()
        {
            var list = new List<Transform>();
            Transform sub = transform.Find("root/sub_root");
            if (sub != null)
                foreach (string n in new[] { "high", "med", "medium", "low", "first_person" })
                {
                    Transform p = sub.Find(n);
                    if (p != null) list.Add(p);
                }
            _parts = list.ToArray();
            var legs = new List<Transform>();
            foreach (Transform p in _parts)
                for (int i = 0; ; i++) { Transform l = p.Find("HCT15_leg_" + i); if (l == null) break; legs.Add(l); }
            _legs = legs.ToArray();
            FindShins();
            _heads = Find("HCT15_head_");
            _baseY = new float[_parts.Length];
            for (int i = 0; i < _parts.Length; i++) _baseY[i] = _parts[i].localPosition.y;
            _baseRot = new Quaternion[_parts.Length];
            for (int i = 0; i < _parts.Length; i++) _baseRot[i] = _parts[i].localRotation;
            _last = transform.position;
            _ready = _parts.Length > 0;
        }

        private void LateUpdate()
        {
            if (!_ready) return;
            float dt = Mathf.Max(1e-4f, Time.deltaTime);
            float kph = Kph();
            if (kph < 0f)
            {
                // no race view (car selection): from its own movement, units/s ~ m/s
                float v = (transform.position - _last).magnitude / dt;
                kph = v > 200f ? 0f : v * 3.6f;                          // > 200 u/s is a teleport, not speed
            }
            _last = transform.position;
            _speed = Mathf.Lerp(_speed, kph, Mathf.Clamp01(dt * 4f));
            float k = Mathf.Clamp01(_speed / 40f);
            float top = TopKph > 0f ? TopKph : _max > 0.5f ? _max : 45f;
            float walk = Cadence > 0f ? Mathf.Clamp01(_speed / top) : 0f;   // 2.0.28: a walk, as a share of the car's own top speed (the andas: ~5 km/h)
            if (Cadence > 0f) { _phase += dt * 2f * Cadence * (0.3f + 0.7f * walk); k = walk; }   // two bobs a stride
            else _phase += dt * (1.6f + _speed * 0.012f);                // strides per second: a trot at 40, a gallop at 200
            float y = Mathf.Abs(Mathf.Sin(_phase * Mathf.PI)) * Amplitude * k;
            // the legs: one full swing per stride (two bobs); standing still they settle straight
            float stand = Cadence > 0f ? Mathf.Clamp01(walk * 4f) : Mathf.Clamp01(_speed / 15f);
            for (int i = 0; i < _legs.Length; i++)
            {
                float ph = i < LegPhase.Length ? LegPhase[i] : 0f;
                float th = (_phase * 0.5f + ph) * 2f * Mathf.PI;
                float a = Swing * stand * Mathf.Sin(th);
                _legs[i].localRotation = Quaternion.AngleAxis(a, Vector3.right);
                // the knee: a leg coming forward (its angle falling: cos < 0) folds its shin back, then straightens to land
                if (KneeBend > 0f && i < _shins.Length && _shins[i] != null)
                    _shins[i].localRotation = Quaternion.AngleAxis(stand * (4f + KneeBend * Mathf.Max(0f, -Mathf.Cos(th))), Vector3.right);
            }
            _turn = Mathf.Lerp(_turn, Mathf.Clamp(Turning(), -1f, 1f), Mathf.Clamp01(dt * 5f));
            float nod = Mathf.Sin(_phase * 2f * Mathf.PI) * 3f * k;                // a little nod with each bob
            for (int i = 0; i < _heads.Length; i++)
                if (_heads[i] != null) _heads[i].localRotation = Quaternion.AngleAxis(_turn * HeadYaw, Vector3.up) * Quaternion.AngleAxis(-_turn * HeadRoll, Vector3.forward) * Quaternion.AngleAxis(nod, Vector3.right);
            if (_legs.Length > 1 && Time.unscaledTime >= _logAt && _view != null && TrotLog)
            {
                _logAt = Time.unscaledTime + 1f;
                Debug.Log("[HCT15] trot '" + name + "' (" + (_viewKind == 1 ? "player" : _viewKind == 2 ? "player 2" : _viewKind == 3 ? "rival" : "?") + "): " + _speed.ToString("0") + " km/h, turn " + _turn.ToString("0.00") + " (" + _heads.Length + " head(s)), legs " + _legs[0].localEulerAngles.x.ToString("0") + " / " + _legs[1].localEulerAngles.x.ToString("0") + " deg");
            }
            // 2.1.5 (the andas with their images on top lean a little to the side they are turning, as an unstable load
            // would): a walker's whole body (bearers, litter, image) rolls into the bend, as the horse's head does
            float lean = Cadence > 0f ? -_turn * WalkLean * Mathf.Clamp01(walk * 2f) : 0f;
            // 2.2.2: the andas hop a little as they walk, for comic effect: one small hop a stride, a parabola off the
            // ground and back, the whole anda rocking forward on the way up and back on landing
            float pitch = 0f;
            if (Cadence > 0f)
            {
                float hp = Mathf.Repeat(_phase * 0.5f, 1f);
                float go = Mathf.Clamp01(walk * 3f);
                y += WalkHop * go * 4f * hp * (1f - hp);
                pitch = Mathf.Sin(hp * 2f * Mathf.PI) * WalkRock * go;
            }
            for (int i = 0; i < _parts.Length; i++)
            {
                Vector3 p = _parts[i].localPosition;
                _parts[i].localPosition = new Vector3(p.x, _baseY[i] + y, p.z);
                if (Cadence > 0f) _parts[i].localRotation = _baseRot[i] * Quaternion.AngleAxis(lean, Vector3.forward) * Quaternion.AngleAxis(pitch, Vector3.right);
            }
        }

        public const float WalkLean = 25f;   // degrees an anda rolls into a full bend (2.1.5: 6; 2.2.1: 18; 2.2.2: 25)
        public const float WalkHop = 0.3f;   // 2.2.2: the hop's height (body units) at a full walk, once a stride
        public const float WalkRock = 4f;    // 2.2.2: degrees the anda rocks forward and back with each hop
        private Quaternion[] _baseRot = new Quaternion[0];
    }
}
