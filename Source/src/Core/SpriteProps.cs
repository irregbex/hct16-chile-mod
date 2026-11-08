using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// Scenery drawn as FLAT PIXEL SPRITES, the way a 16-bit racer drew its roadside: a scenery.json item with a
    /// "Sprite" (a PNG under HCT15Content\assets\sprites\, e.g. "retro/cactus.png") becomes a camera-facing quad
    /// carrying that image, point-filtered, one unit tall in the prototype and scaled by the item's H x S like
    /// the box prototypes are (PropDresser.PlaceScenery). The the retro campaign campaign uses it for every object
    /// (a sprite tool draws the set); any track can.
    ///
    /// The quad turns to the camera's rotation every frame (screen-aligned, like the sprite cars, so the texels
    /// sit on the pixel grid of the 224-line render) and undoes a mirrored parent, so a sign reads the right way
    /// on both sides of the road. Nothing else about the prop changes: PropAutoMover streams and places it.
    /// </summary>
    public static class SpriteProps
    {
        private static readonly Dictionary<string, GameObject> _protos = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _warned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static string SpritesDir { get { return Path.Combine(Path.Combine(ContentRegistry.ContentDir, "assets"), "sprites"); } }

        /// <summary>The prototype for one sprite file (cached); null, once warned, when the PNG is missing.</summary>
        public static GameObject Prototype(string file, string hit = "")
        {
            if (string.IsNullOrEmpty(file)) return null;
            GameObject go;
            string key = string.IsNullOrEmpty(hit) ? file : file + "|" + hit;
            if (_protos.TryGetValue(key, out go) && go != null) return go;
            string path = Path.Combine(SpritesDir, file.Replace('/', Path.DirectorySeparatorChar));
            Texture2D tex = Load(path);
            if (tex == null)
            {
                if (_warned.Add(file)) Debug.LogWarning("[HCT15] sprite prop: no " + path);
                return null;
            }
            go = new GameObject("HCT15_sprite_" + Path.GetFileNameWithoutExtension(file));
            go.transform.SetParent(PropDresser.Holder(), false);
            var quad = new GameObject("quad");
            quad.transform.SetParent(go.transform, false);
            float aspect = tex.width / (float)Mathf.Max(1, tex.height);
            var mesh = new Mesh();
            mesh.name = "HCT15_sprite_quad";
            float hw = aspect * 0.5f;
            mesh.vertices = new[] { new Vector3(-hw, 0f, 0f), new Vector3(hw, 0f, 0f), new Vector3(hw, 1f, 0f), new Vector3(-hw, 1f, 0f) };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2, 0, 1, 2, 0, 2, 3 };   // both windings: seen from either side
            mesh.RecalculateBounds();
            quad.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = quad.AddComponent<MeshRenderer>();
            mr.sharedMaterial = new Material(SpriteCars.SpriteShader()) { mainTexture = tex, name = "HCT15_sprite_" + tex.name };
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            quad.AddComponent<SpriteProp>();
            if (!string.IsNullOrEmpty(hit)) MakeSolid(go, aspect, hit);
            _protos[key] = go;
            return go;
        }

        private static System.Reflection.FieldInfo _fSpeed, _fOverride, _fLateral, _fForce;

        /// <summary>
        /// 2.4.1 (the user: "when I should crash into the objects beside the track - lamp posts, the direction signs in the
        /// bends - nothing happens, they practically do not exist"): the sprite gets the game's own prop collision - a box
        /// collider and a PropCollidable on its root (PropAutoMover links it, PropView scales it, the car's
        /// SplineCollisionDetector hits it and plays the game's crash). The original's crash classes : REV bounces
        /// the car back - here it is stopped dead; EDGE / SIDE halve the speed and push it towards the road - here the speed
        /// is cut to 180 km/h (the game's units, about half a retro cruise) and the lateral impact is on.
        /// </summary>
        private static void MakeSolid(GameObject go, float aspect, string hit)
        {
            try
            {
                var box = go.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, 0.5f, 0f);
                box.size = new Vector3(Mathf.Max(0.2f, aspect * 0.8f), 1f, 0.5f);   // across the road the sprite's own width, along it half a unit
                box.isTrigger = true;
                var pc = go.AddComponent<PropCollidable>();
                if (_fSpeed == null)
                {
                    _fSpeed = HarmonyLib.AccessTools.Field(typeof(Collidable), "m_speedBoost");
                    _fOverride = HarmonyLib.AccessTools.Field(typeof(Collidable), "m_overrideSpeed");
                    _fLateral = HarmonyLib.AccessTools.Field(typeof(Collidable), "m_applyLateralImpact");
                    _fForce = HarmonyLib.AccessTools.Field(typeof(Collidable), "m_lateralForceOnHit");
                }
                bool rev = string.Equals(hit, "REV", StringComparison.OrdinalIgnoreCase);
                _fOverride.SetValue(pc, true);
                _fSpeed.SetValue(pc, rev ? 0f : 180f);
                _fLateral.SetValue(pc, true);
                _fForce.SetValue(pc, rev ? 12f : 20f);
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] sprite prop collider: " + ex.Message); }
        }

        private static Texture2D Load(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!t.LoadImage(File.ReadAllBytes(path))) return null;
                t.filterMode = FilterMode.Point;
                t.wrapMode = TextureWrapMode.Clamp;
                t.anisoLevel = 0;
                t.name = "HCT15:" + Path.GetFileName(path);
                return t;
            }
            catch (Exception) { return null; }
        }
    }

    /// <summary>Keeps a sprite prop's quad facing the world camera and the right way round.</summary>
    public class SpriteProp : MonoBehaviour
    {
        private static Camera _cam;
        private static int _camFrame = -1;

        private void LateUpdate()
        {
            if (Time.frameCount != _camFrame)
            {
                _camFrame = Time.frameCount;
                _cam = Pixelate.SceneCamera();
                if (_cam == null) _cam = Camera.main;
            }
            if (_cam == null) return;
            transform.rotation = _cam.transform.rotation;
            Transform p = transform.parent;
            Vector3 ls = transform.localScale;
            float sx = Mathf.Abs(ls.x);
            if (p != null && p.lossyScale.x < 0f) sx = -sx;   // a mirrored parent would mirror the picture
            if (Mathf.Abs(ls.x - sx) > 1e-5f) { ls.x = sx; transform.localScale = ls; }
        }
    }
}
