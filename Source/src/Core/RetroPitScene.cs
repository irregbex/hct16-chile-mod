using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// 2.4.1 - the Top Gear pit scene (the user's video of the San Francisco pit, 2026-11-07: "on entering, the screen fades;
    /// this animation of the car stopping and the speed going down; no control while the fuel goes in EXCEPT accelerate to
    /// take the car out, which stops the refuel and starts the animation of the car driving off the screen; then the race
    /// comes back with a fade in"). PitLane runs the stop (phases, speed, fuel, the accelerate press); this draws it over the
    /// race for player 0, under the HUD, at the race's own pixel size:
    ///   the pit lane seen from above (fx\pit_lane.png, Herramientas-TopGear\tg_pitlane.py: Claude's redraw from the video)
    ///   scrolling under the car's top sprite (cars\sprites\&lt;car&gt;\top.png, from the ROM, nose to the left), which comes in
    ///   from the right braking (PITSTOP, SLOW), stands at the STOP line, and drives off to the left past GO;
    ///   the fades: a blue flash then black on the way in, black between the scene and the race on the way out.
    /// The view is the pit lane's 112 lines across the screen's height (the SNES showed it in its 112-line half).
    /// </summary>
    public static class RetroPitScene
    {
        [Serializable] private class Meta { public float WRight = 900f, WLeft = -420f, StopNoseW = 300f, LaneY = 60f, Height = 112f; }

        private static Texture2D _lane, _white;
        private static Meta _meta;
        private static string _loadedFor;
        private static readonly Dictionary<string, Texture2D> _tops = new Dictionary<string, Texture2D>();

        private static void Load()
        {
            var c = ModCampaigns.Active;
            string folder = c != null ? c.Folder : "";
            if (_loadedFor == folder) return;
            _loadedFor = folder; _lane = null; _meta = new Meta();
            try
            {
                string fx = Path.Combine(Path.Combine(Path.Combine(ContentRegistry.ContentDir, "campaigns"), folder), "fx");
                string png = Path.Combine(fx, "pit_lane.png"), js = Path.Combine(fx, "pit_lane.json");
                if (File.Exists(js)) _meta = JsonUtility.FromJson<Meta>(File.ReadAllText(js)) ?? new Meta();
                if (File.Exists(png))
                {
                    _lane = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (_lane.LoadImage(File.ReadAllBytes(png))) { _lane.filterMode = FilterMode.Point; _lane.wrapMode = TextureWrapMode.Clamp; }
                    else _lane = null;
                }
                Debug.Log("[HCT15] retro pit scene: " + (_lane != null ? "pit_lane.png " + _lane.width + "x" + _lane.height : "no pit_lane.png") + " in " + fx);
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] retro pit scene: " + ex.Message); }
        }

        private static Texture2D Top(PlayerCar car)
        {
            string id = null;
            try { id = car.View.Controller.CarModel.ItemId; } catch (Exception) { }
            if (string.IsNullOrEmpty(id)) id = "tg_car1";
            Texture2D t;
            if (_tops.TryGetValue(id, out t)) return t;
            t = null;
            try
            {
                string f = Path.Combine(Path.Combine(Path.Combine(StreetCars.CarsDir, "sprites"), id), "top.png");
                if (File.Exists(f)) { t = new Texture2D(2, 2, TextureFormat.RGBA32, false); if (t.LoadImage(File.ReadAllBytes(f))) t.filterMode = FilterMode.Point; else t = null; }
            }
            catch (Exception) { t = null; }
            _tops[id] = t;
            return t;
        }

        private static float Smooth(float u) { u = Mathf.Clamp01(u); return u * u * (3f - 2f * u); }

        /// <summary>Draws the scene behind the HUD while player 0 is in the pit's scene part. True = it covered the race.</summary>
        public static bool Draw(PlayerCar car, float W, float H)
        {
            PitLane.RetroScene s;
            if (car == null || !PitLane.Scene(car.PlayerId, out s)) return false;
            if (s.Phase != PitLane.SceneBraking && s.Phase != PitLane.SceneServing && s.Phase != PitLane.SceneLeaving) return false;
            Load();
            if (_lane == null) return false;
            float sc = H / Mathf.Max(1f, _meta.Height);       // screen pixels per scene pixel
            float viewW = W / sc;
            float stopNose = 40f;
            float carW, nose;
            if (s.Phase == PitLane.SceneBraking)
            {
                float u = Mathf.Clamp01(s.T / PitLane.SceneBrake);
                carW = _meta.StopNoseW - 340f * Mathf.Pow(1f - u, 3f);            // the distance the braking curve covers
                nose = stopNose + (viewW + 20f - stopNose) * (1f - u) * (1f - u); // in from beyond the right edge
            }
            else if (s.Phase == PitLane.SceneServing) { carW = _meta.StopNoseW; nose = stopNose; }
            else
            {
                float u = Mathf.Clamp01(s.T / PitLane.SceneLeave);
                carW = _meta.StopNoseW + 420f * u * u;
                nose = stopNose - (stopNose + 150f) * u * u;                     // off past the left edge
            }
            float camW = carW - viewW + nose;                                      // the W at the screen's right edge
            float x0 = _meta.WRight - camW - viewW;                                // the strip column at the screen's left edge
            x0 = Mathf.Clamp(x0, 0f, Mathf.Max(0f, _lane.width - viewW));
            GUI.DrawTextureWithTexCoords(new Rect(0f, 0f, W, H), _lane, new Rect(x0 / _lane.width, 0f, viewW / _lane.width, 1f));
            Texture2D top = Top(car);
            if (top != null)
                GUI.DrawTexture(new Rect(Mathf.Round(nose * sc), Mathf.Round((_meta.LaneY - top.height * 0.5f) * sc), top.width * sc, top.height * sc), top);
            return true;
        }

        /// <summary>The fades, over everything (the HUD too): blue flash then black on the way in, black between the scene and
        /// the race on the way out.</summary>
        public static void DrawFade(PlayerCar car, float W, float H)
        {
            PitLane.RetroScene s;
            if (car == null || !PitLane.Scene(car.PlayerId, out s)) return;
            Color c = Color.clear;
            if (s.Phase == PitLane.SceneFade)
            {
                float u = Mathf.Clamp01(s.T / PitLane.SceneFadeOut);
                c = Color.Lerp(new Color(0.12f, 0.24f, 0.70f, 1f), Color.black, Smooth((u - 0.3f) / 0.5f));
                c.a = Smooth(u / 0.35f);
            }
            else if (s.Phase == PitLane.SceneBraking) c = new Color(0f, 0f, 0f, 1f - Smooth(s.T / 0.3f));
            else if (s.Phase == PitLane.SceneLeaving) c = new Color(0f, 0f, 0f, Smooth((s.T - (PitLane.SceneLeave - 0.3f)) / 0.3f));
            else if (s.Phase == PitLane.SceneBack) c = new Color(0f, 0f, 0f, 1f - Smooth((s.T - 0.2f) / (PitLane.SceneBackSeconds - 0.2f)));
            if (c.a <= 0.001f) return;
            if (_white == null) { _white = new Texture2D(1, 1); _white.SetPixel(0, 0, Color.white); _white.Apply(); }
            Color keep = GUI.color; GUI.color = c; GUI.DrawTexture(new Rect(0f, 0f, W, H), _white); GUI.color = keep;
        }
    }
}
