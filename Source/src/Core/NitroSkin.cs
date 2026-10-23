using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace HCT15
{
    /// <summary>
    /// 2.0.11: a car may see its pickups as something of its own (2026-10-06: a player driving a cart sees the nitro
    /// 3D model as a bale of hay; then the fuel too, as hay bales in their normal colour, with the nitro
    /// bales glowing an intense green). CarDef.NitroModel / FuelModel name an asset-library model
    /// (the carreta: fardo_nitro for both). The pickups on the road come from a pool: PropView.SpawnProp takes one
    /// (prefab.Spawn), gives it the player's track and layer - each screen has its own copy of the props -, so the swap is
    /// per player, there: on the track of a player whose car has a model, the bottle's (can's) renderers go off and the
    /// model stands in their place under m_goMesh (the game's show / hide / scaling still applies). A pooled pickup that
    /// comes back for a player without one gets its bottle back. The bottle's bounds take its glow in (4 units tall), so
    /// the model has a fixed size: a bale beside a 2-unit-wide cart. A nitro bale is drawn in green (its vertex colours
    /// pushed to green) inside two additive green halos that pulse; a fuel bale is the plain bale.
    /// </summary>
    public static class NitroSkin
    {
        private static readonly Dictionary<RaceCollectible, string> _done = new Dictionary<RaceCollectible, string>();
        private static readonly System.Reflection.FieldInfo _mesh = AccessTools.Field(typeof(RaceCollectible), "m_goMesh");
        private const float Size = 1.6f;
        private const float CoinSize = 1.3f;   // 2.0.15: a carrot as tall as the coin it replaces is wide
        private static readonly Color Green = new Color(0.25f, 1f, 0.15f);
        private static readonly HashSet<string> _logged = new HashSet<string>();

        internal static void Apply(RaceCollectible c)
        {
            try
            {
                bool nitro = c is RaceNitro, coin = c is RaceCoin;
                if (!nitro && !coin && !(c is RaceFuel)) return;
                TrackManager track = c.Track;
                PlayerCar pc = track != null ? track.PlayerCar : null;
                string carId = pc != null && pc.CarModel != null ? pc.CarModel.ItemId : null;
                string model = nitro ? StreetCars.NitroModelOf(carId) : coin ? StreetCars.CoinModelOf(carId) : StreetCars.FuelModelOf(carId);
                string had;
                if (_done.TryGetValue(c, out had) && had == (model ?? "")) return;
                _done[c] = model ?? "";
                var goMesh = _mesh != null ? _mesh.GetValue(c) as GameObject : null;
                if (goMesh == null) return;
                Transform old = goMesh.transform.Find("HCT15_nitro_skin");
                if (old != null) { old.SetParent(null, false); UnityEngine.Object.Destroy(old.gameObject); }
                var own = new List<Renderer>(goMesh.GetComponentsInChildren<Renderer>(true));   // the old skin is already detached
                if (string.IsNullOrEmpty(model)) { foreach (Renderer r in own) r.enabled = true; return; }
                AssetLibrary.AssetDef def = AssetLibrary.Get(model);
                if (def == null || !AssetLibrary.EnsureLoaded(def) || def.Mesh == null) return;
                // the bottle's footing in goMesh's own space
                Bounds b = new Bounds(); bool any = false;
                foreach (MeshFilter mf in goMesh.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null) continue;
                    Bounds mb = mf.sharedMesh.bounds;
                    for (int i = 0; i < 8; i++)
                    {
                        Vector3 corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                        Vector3 p = goMesh.transform.InverseTransformPoint(mf.transform.TransformPoint(corner));
                        if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                    }
                }
                foreach (Renderer r in own) r.enabled = false;
                var holder = new GameObject("HCT15_nitro_skin") { layer = goMesh.layer };
                holder.transform.SetParent(goMesh.transform, false);
                GameObject mesh = AssetLibrary.MeshObject(def, holder.transform);
                mesh.layer = goMesh.layer;
                Bounds mbnd = def.Mesh.bounds;
                if (nitro)
                {
                    var mf = mesh.GetComponent<MeshFilter>();
                    if (mf != null && !StreetCars.NitroOwnColoursOf(carId)) mf.sharedMesh = GreenCopy(def);   // 2.1.5: the andas' bottle keeps its white label and black X
                    foreach (var g in new[] { new { n = "glow_in", k = 1.25f, s = 0.55f }, new { n = "glow_out", k = 1.7f, s = 0.25f } })
                    {
                        Vector3 sz = mbnd.size * g.k;
                        GameObject box = NightFx.GlowBox(g.n, holder.transform, sz.x, sz.y, sz.z, HaloMaterial(g.s), new Vector3(mbnd.center.x, mbnd.min.y - (sz.y - mbnd.size.y) * 0.3f, mbnd.center.z));
                        box.layer = goMesh.layer;
                    }
                    if (holder.GetComponent<GreenPulse>() == null) holder.AddComponent<GreenPulse>();
                }
                Vector3 size = mbnd.size;
                float s = (coin ? CoinSize : Size) / Mathf.Max(0.05f, Mathf.Max(size.x, Mathf.Max(size.y, size.z)));
                holder.transform.localScale = Vector3.one * s;
                holder.transform.localPosition = any ? new Vector3(b.center.x, b.min.y, b.center.z) : Vector3.zero;
                holder.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
                string what = nitro ? "nitro" : coin ? "coin" : "fuel";
                if (_logged.Add(what)) Debug.Log("[HCT15] pickup skin: " + what + " as '" + model + "'" + (nitro ? " (green glow)" : "") + " for '" + carId + "' on player " + (track != null ? track.PlayerId : -1) + "'s road");
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] pickup skin: " + ex.Message); }
        }

        // the model's mesh with its vertex colours pushed to an intense green (one copy per model)
        private static readonly Dictionary<string, Mesh> _green = new Dictionary<string, Mesh>();
        private static Mesh GreenCopy(AssetLibrary.AssetDef def)
        {
            Mesh m;
            if (_green.TryGetValue(def.Id, out m) && m != null) return m;
            m = UnityEngine.Object.Instantiate(def.Mesh);
            m.name = def.Mesh.name + "_green";
            Color[] cols = m.colors;
            if (cols == null || cols.Length != m.vertexCount) { cols = new Color[m.vertexCount]; for (int i = 0; i < cols.Length; i++) cols[i] = Color.white; }
            for (int i = 0; i < cols.Length; i++)
            {
                float lum = 0.3f * cols[i].r + 0.59f * cols[i].g + 0.11f * cols[i].b;    // keep the bale's shading, in green
                Color g = Green * (0.55f + 0.75f * lum);
                cols[i] = new Color(Mathf.Min(1f, g.r), Mathf.Min(1f, g.g), Mathf.Min(1f, g.b), 1f);
            }
            m.colors = cols;
            _green[def.Id] = m;
            return m;
        }

        // additive green halos (shared materials; GreenPulse writes their brightness)
        private static readonly Dictionary<float, Material> _halo = new Dictionary<float, Material>();
        private static Shader _haloShader;
        private static Material HaloMaterial(float strength)
        {
            Material m;
            if (_halo.TryGetValue(strength, out m) && m != null) return m;
            if (_haloShader == null) _haloShader = Shader.Find("Hidden/Internal-Colored");
            m = new Material(_haloShader) { name = "HCT15_nitro_halo_" + strength.ToString("0.00"), hideFlags = HideFlags.DontSave };
            m.SetInt("_SrcBlend", (int)BlendMode.One);
            m.SetInt("_DstBlend", (int)BlendMode.One);
            m.SetInt("_Cull", (int)CullMode.Off);
            m.SetInt("_ZWrite", 0);
            m.SetInt("_ZTest", (int)CompareFunction.LessEqual);
            m.renderQueue = 3100;
            m.color = Green * strength;
            _halo[strength] = m;
            return m;
        }

        /// <summary>The halos breathe (once a frame for all of them).</summary>
        private class GreenPulse : MonoBehaviour
        {
            private static int _frame = -1;
            private void Update()
            {
                if (_frame == Time.frameCount) return;
                _frame = Time.frameCount;
                float k = 0.7f + 0.3f * Mathf.Sin(Time.time * 6.5f);
                foreach (var kv in _halo) if (kv.Value != null) { Color c = Green * (kv.Key * k); c.a = 1f; kv.Value.color = c; }
            }
        }

        // -hct15-shotnitro (tests): one frame when a skinned nitro is 40-70 units ahead (it is on screen from ~250)
        private static bool? _shotFlag, _coinFlag; private static bool _shot;
        internal static void TestShot()
        {
            if (_shotFlag == null) _shotFlag = Array.IndexOf(Environment.GetCommandLineArgs(), "-hct15-shotnitro") >= 0 || Array.IndexOf(Environment.GetCommandLineArgs(), "-hct15-shotcoin") >= 0;
            if (!_shotFlag.Value || _shot) return;
            if (_coinFlag == null) _coinFlag = Array.IndexOf(Environment.GetCommandLineArgs(), "-hct15-shotcoin") >= 0;
            foreach (var kv in _done)
            {
                bool wantCoin = _coinFlag.Value;   // -hct15-shotcoin: the same frame for a skinned coin (2.0.15)
                if (kv.Key == null || !(wantCoin ? kv.Key is RaceCoin : kv.Key is RaceNitro) || string.IsNullOrEmpty(kv.Value) || !kv.Key.gameObject.activeInHierarchy) continue;
                float z = kv.Key.transform.position.z;
                if (z > 25f && z < 55f) { _shot = true; DebugShot.Capture(wantCoin ? "hct15-coin.png" : "hct15-nitro.png"); Debug.Log("[HCT15] pickup skin: shot at z " + z.ToString("0.0")); return; }
            }
        }
    }

    [HarmonyPatch(typeof(PropView), nameof(PropView.SpawnProp))]
    internal static class NitroSkinSpawnPatch
    {
        private static readonly System.Reflection.FieldInfo _go = AccessTools.Field(typeof(PropView), "propGO");
        private static readonly System.Reflection.FieldInfo _mesh = AccessTools.Field(typeof(RaceCollectible), "m_goMesh");
        private static readonly Dictionary<int, float> _baseY = new Dictionary<int, float>();
        private static int _airLogged;

        private static void Postfix(PropView __instance, PropData p_prop)
        {
            var go = _go != null ? _go.GetValue(__instance) as GameObject : null;
            RaceCollectible c = go != null ? go.GetComponent<RaceCollectible>() : null;
            if (c is RaceNitro || c is RaceFuel || c is RaceCoin) NitroSkin.Apply(c);
            // 2.0.24: a nitro over a jet ski ramp hangs in the air at the arc's height (a pooled bottle comes back down)
            if (c is RaceNitro)
            {
                try
                {
                    var goMesh = _mesh != null ? _mesh.GetValue(c) as GameObject : null;
                    if (goMesh == null) return;
                    int key = goMesh.GetInstanceID();
                    float y0;
                    if (!_baseY.TryGetValue(key, out y0)) { y0 = goMesh.transform.localPosition.y; _baseY[key] = y0; }
                    float air = p_prop != null && JetskiRamps.Current.Count > 0 ? JetskiRamps.AirAt(p_prop.SplinePercentage) : 0f;
                    Vector3 lp = goMesh.transform.localPosition;
                    goMesh.transform.localPosition = new Vector3(lp.x, y0 + air, lp.z);
                    if (air > 0f && _airLogged++ < 3) Debug.Log("[HCT15] nitro at " + p_prop.SplinePercentage.ToString("0.0000") + " hangs " + air.ToString("0.0") + " units up over a jet ski ramp");
                }
                catch (Exception) { }
            }
        }
    }
}
