using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HCT15
{
    /// <summary>
    /// The game's own props - the trees, lamps, signs, billboards, huts, rocks... a donor stage streams beside
    /// its road - as placeable assets for the track editor. A stage's set is captured whenever a custom race
    /// on that donor loads (PropDresser.Dress) or on demand from the editor (LoadFromDonor: the stage scene is
    /// loaded additively for a moment, its cameras/lights/sound switched off, and unloaded again). Names,
    /// category, size and a rendered thumbnail go to HCT15Content\assets\game_props\&lt;donor&gt;\ (index.json +
    /// PNGs) so the editor lists them without the stage; the prefabs themselves stay referenced while the same
    /// donor is the loaded one. A scenery item uses one with Asset = "game:&lt;prefab name&gt;"; at race time the
    /// prefab is taken from the donor's own PropData, so the game's PropAutoMover (side-dependent rotation,
    /// road following) applies unchanged.
    /// </summary>
    public static class GameProps
    {
        public const string Prefix = "game:";

        [Serializable] public class PropInfo { public string Name = ""; public string Category = "prop"; public float W, H, D; public string Rotation = ""; public int Tris; }
        [Serializable] public class Index { public string Donor = ""; public List<PropInfo> Props = new List<PropInfo>(); }

        /// <summary>Donor stage whose prefabs are resolvable right now (last race dressed or last LoadFromDonor).</summary>
        public static string LoadedDonor;
        public static bool Loading;
        public static string LastMessage = "";

        private static readonly Dictionary<string, GameObject> _prefabs = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Index> _indexes = new Dictionary<string, Index>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Texture2D> _thumbs = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private static readonly string[] TreeWords = { "tree", "palm", "pine", "cactus", "bush", "flower", "plant", "shrub", "arvore", "baobab", "cypress" };
        private static readonly string[] SignWords = { "sign_", "chevron", "arrow", "placa" };
        private static readonly string[] LampWords = { "lamp", "light", "pole", "post", "poste" };
        private static readonly string[] BillboardWords = { "plate", "billboard", "banner", "outdoor", "ads", "advert" };
        private static readonly string[] BuildingWords = { "house", "hut", "build", "tower", "shop", "church", "bar", "hotel", "tent", "casa", "store", "station", "garage", "barn", "temple", "shrine", "kiosk", "stand" };
        private static readonly string[] RockWords = { "rock", "stone", "hill", "mountain", "boulder", "dune", "cliff", "pedra" };

        public static bool IsGameProp(string asset) { return !string.IsNullOrEmpty(asset) && asset.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase); }
        public static string NameOf(string asset) { return IsGameProp(asset) ? asset.Substring(Prefix.Length) : asset ?? ""; }
        public static bool IsLoaded(string donor) { return !string.IsNullOrEmpty(donor) && string.Equals(LoadedDonor, donor, StringComparison.OrdinalIgnoreCase) && _prefabs.Count > 0; }
        public static int LoadedCount { get { return _prefabs.Count; } }

        public static string Dir(string donor)
        {
            ContentRegistry.EnsureLoaded();
            return Path.Combine(Path.Combine(Path.Combine(ContentRegistry.ContentDir ?? "", "assets"), "game_props"), donor ?? "");
        }

        /// <summary>The prefab for a "game:&lt;name&gt;" item: from the donor's own prop list first, then the loaded set.</summary>
        public static GameObject Resolve(string asset, Dictionary<string, GameObject> donorPrefabs)
        {
            string name = NameOf(asset);
            GameObject go;
            if (donorPrefabs != null && donorPrefabs.TryGetValue(name, out go) && go != null) return go;
            if (_prefabs.TryGetValue(name, out go) && go != null) return go;
            return null;
        }

        public static GameObject Prefab(string nameOrAsset)
        {
            GameObject go;
            return _prefabs.TryGetValue(NameOf(nameOrAsset), out go) ? go : null;
        }

        // ------------------------------------------------------------------ index on disk

        public static Index Load(string donor)
        {
            if (string.IsNullOrEmpty(donor)) return null;
            Index idx;
            if (_indexes.TryGetValue(donor, out idx)) return idx;
            try
            {
                string path = Path.Combine(Dir(donor), "index.json");
                if (File.Exists(path)) idx = Json.FromJson<Index>(File.ReadAllText(path));
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] game props: index of '" + donor + "' unreadable: " + ex.Message); }
            if (idx != null) _indexes[donor] = idx;
            return idx;
        }

        public static PropInfo Info(string donor, string nameOrAsset)
        {
            var idx = Load(donor);
            if (idx == null) return null;
            string name = NameOf(nameOrAsset);
            foreach (var p in idx.Props) if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p;
            return null;
        }

        public static Texture2D Thumb(string donor, string nameOrAsset)
        {
            string name = NameOf(nameOrAsset);
            string key = donor + "|" + name;
            Texture2D t;
            if (_thumbs.TryGetValue(key, out t)) return t;
            t = null;
            try
            {
                string path = Path.Combine(Dir(donor), SafeFile(name) + ".png");
                if (File.Exists(path))
                {
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (tex.LoadImage(File.ReadAllBytes(path))) { tex.name = "HCT15:gameprop:" + name; t = tex; }
                }
            }
            catch (Exception) { }
            _thumbs[key] = t;
            return t;
        }

        private static string SafeFile(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in s ?? "") sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
            return sb.Length == 0 ? "prop" : sb.ToString();
        }

        // ------------------------------------------------------------------ capture

        /// <summary>Remembers the donor's prefabs for this session and writes the index + thumbnails once per donor.</summary>
        public static void Capture(string donor, IEnumerable<GameObject> prefabs) { Capture(donor, prefabs, false); }

        public static void Capture(string donor, IEnumerable<GameObject> prefabs, bool force)
        {
            if (string.IsNullOrEmpty(donor) || prefabs == null) return;
            LoadedDonor = donor;
            _prefabs.Clear();
            var list = new List<GameObject>();
            foreach (var p in prefabs)
            {
                if (p == null || _prefabs.ContainsKey(p.name)) continue;
                _prefabs[p.name] = p;
                if (Category(p) == null) continue;   // collectibles and the finish gantry are not scenery
                list.Add(p);
            }
            Index have = force ? null : Load(donor);
            if (have != null && have.Props.Count >= list.Count)
            {
                LastMessage = donor + ": " + _prefabs.Count + " prop(s) available";
                return;
            }
            float t0 = Time.realtimeSinceStartup;
            string dir = Dir(donor);
            try { Directory.CreateDirectory(dir); } catch (Exception ex) { Debug.LogWarning("[HCT15] game props: " + ex.Message); return; }
            var idx = new Index { Donor = donor };
            int thumbs = 0;
            foreach (var p in list)
            {
                var info = new PropInfo { Name = p.name, Category = Category(p), Rotation = RotationOf(p) };
                Bounds b; int tris;
                if (MeshBounds(p, out b, out tris))
                {
                    // the prop frame: +x across the road, +z along it
                    info.D = Mathf.Round(b.size.x * 100f) / 100f; info.H = Mathf.Round(b.size.y * 100f) / 100f; info.W = Mathf.Round(b.size.z * 100f) / 100f; info.Tris = tris;
                    try
                    {
                        var tex = Thumbnails.RenderPrefab(p, b, 112);
                        if (tex != null)
                        {
                            if (SavePaths.DumpGameAllowed) File.WriteAllBytes(Path.Combine(dir, SafeFile(p.name) + ".png"), tex.EncodeToPNG());
                            _thumbs[donor + "|" + p.name] = tex;
                            thumbs++;
                        }
                    }
                    catch (Exception ex) { Debug.LogWarning("[HCT15] game props: thumbnail of '" + p.name + "' failed: " + ex.Message); }
                }
                idx.Props.Add(info);
            }
            idx.Props.Sort((a, c) => string.Compare(a.Category + a.Name, c.Category + c.Name, StringComparison.OrdinalIgnoreCase));
            try { File.WriteAllText(Path.Combine(dir, "index.json"), Json.ToJson(idx, true)); } catch (Exception ex) { Debug.LogWarning("[HCT15] game props: index write failed: " + ex.Message); }
            _indexes[donor] = idx;
            LastMessage = donor + ": " + idx.Props.Count + " prop(s) captured";
            Debug.Log("[HCT15] game props: '" + donor + "' -> " + idx.Props.Count + " prop(s), " + thumbs + " thumbnail(s) in " + ((Time.realtimeSinceStartup - t0) * 1000f).ToString("0") + " ms (" + dir + ")");
        }

        /// <summary>Category by name and components; null = not a scenery prop (coins, nitros, fuel, the finish gantry).</summary>
        public static string Category(GameObject prefab)
        {
            if (prefab == null) return null;
            if (prefab.GetComponent<RaceCollectible>() != null) return null;
            string n = prefab.name.ToLowerInvariant();
            if (n.Contains("finish")) return null;
            if (Has(n, TreeWords)) return "tree";
            if (Has(n, SignWords)) return "sign";
            if (Has(n, BillboardWords)) return "billboard";
            if (Has(n, LampWords)) return "lamp";
            if (Has(n, BuildingWords)) return "building";
            if (Has(n, RockWords)) return "rock";
            return "decor";
        }

        private static bool Has(string n, string[] words) { foreach (var w in words) if (n.Contains(w)) return true; return false; }

        public static string RotationOf(GameObject prefab)
        {
            try
            {
                var mover = prefab.GetComponent<PropAutoMover>();
                if (mover == null) return "Static";
                var f = typeof(PropAutoMover).GetField("m_rotationTreatment", BindingFlags.Instance | BindingFlags.NonPublic);
                return f == null ? "?" : f.GetValue(mover).ToString();
            }
            catch (Exception) { return "?"; }
        }

        /// <summary>Signs, billboards and flower beds turn 180 degrees on the left side of the road (the game's SideDependent rotation).</summary>
        public static bool IsSideDependent(GameObject prefab) { return RotationOf(prefab) == "SideDependent"; }

        /// <summary>Bounds of every mesh under the prefab in the prefab root's frame (a prefab asset is never instantiated for this).</summary>
        public static bool MeshBounds(GameObject prefab, out Bounds bounds, out int tris)
        {
            bounds = new Bounds(); tris = 0;
            bool any = false;
            Matrix4x4 rootInv = prefab.transform.worldToLocalMatrix;
            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                any = Accumulate(mf.sharedMesh, rootInv * mf.transform.localToWorldMatrix, ref bounds, any);
                tris += TriCount(mf.sharedMesh);
            }
            foreach (var sk in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (sk.sharedMesh == null) continue;
                any = Accumulate(sk.sharedMesh, rootInv * sk.transform.localToWorldMatrix, ref bounds, any);
                tris += TriCount(sk.sharedMesh);
            }
            return any;
        }

        /// <summary>Triangles from the index buffers (build meshes are not CPU-readable, so .triangles would be empty).</summary>
        private static int TriCount(Mesh mesh)
        {
            try
            {
                long n = 0;
                for (int sub = 0; sub < mesh.subMeshCount; sub++) n += (long)mesh.GetIndexCount(sub);
                return (int)(n / 3);
            }
            catch (Exception) { return 0; }
        }

        private static bool Accumulate(Mesh mesh, Matrix4x4 m, ref Bounds bounds, bool any)
        {
            Bounds mb = mesh.bounds;
            for (int c = 0; c < 8; c++)
            {
                var corner = new Vector3((c & 1) == 0 ? mb.min.x : mb.max.x, (c & 2) == 0 ? mb.min.y : mb.max.y, (c & 4) == 0 ? mb.min.z : mb.max.z);
                var p = m.MultiplyPoint3x4(corner);
                if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; } else bounds.Encapsulate(p);
            }
            return any;
        }

        // ------------------------------------------------------------------ on-demand load of a donor stage

        /// <summary>
        /// Loads the donor stage additively (its cameras, lights and sound switched off before the first frame),
        /// captures its props and unloads it. TrackRebuilder ignores this load (Loading is true).
        /// </summary>
        public static IEnumerator LoadFromDonor(string donor, Action<bool, string> done)
        {
            if (Loading || string.IsNullOrEmpty(donor)) yield break;
            if (IsLoaded(donor))
            {
                LastMessage = donor + ": " + _prefabs.Count + " prop(s) available";
                if (done != null) done(true, LastMessage);
                yield break;
            }
            Loading = true;
            LastMessage = Loc.Get("ed_game_loading");
            Debug.Log("[HCT15] game props: loading stage '" + donor + "' additively to capture its props");
            AsyncOperation op = null;
            try { op = SceneManager.LoadSceneAsync(donor, LoadSceneMode.Additive); }
            catch (Exception ex) { Debug.LogWarning("[HCT15] game props: " + ex.Message); }
            if (op == null)
            {
                Loading = false;
                LastMessage = "stage '" + donor + "' cannot be loaded (not in this build)";
                if (done != null) done(false, LastMessage);
                yield break;
            }
            while (!op.isDone) yield return null;
            Scene sc = SceneManager.GetSceneByName(donor);
            bool ok = false;
            string msg;
            try
            {
                if (!sc.IsValid() || !sc.isLoaded) throw new Exception("the stage scene did not load");
                var roots = sc.GetRootGameObjects();
                foreach (var r in roots)
                {
                    foreach (var c in r.GetComponentsInChildren<Camera>(true)) c.enabled = false;
                    foreach (var l in r.GetComponentsInChildren<AudioListener>(true)) l.enabled = false;
                    foreach (var l in r.GetComponentsInChildren<Light>(true)) l.enabled = false;
                    foreach (var a in r.GetComponentsInChildren<AudioSource>(true)) a.enabled = false;
                }
                RaceDataManager rdm = null;
                foreach (var r in roots) { rdm = r.GetComponentInChildren<RaceDataManager>(true); if (rdm != null) break; }
                if (rdm == null || rdm.Data == null || rdm.Data.PropData == null) throw new Exception("no prop list in the stage");
                var list = new List<GameObject>();
                foreach (var p in rdm.Data.PropData) if (p != null && p.Prefab != null && !list.Contains(p.Prefab)) list.Add(p.Prefab);
                Capture(donor, list, true);
                ok = true;
                msg = Loc.Get("ed_game_loaded", donor) + "  (" + _prefabs.Count + ")";
            }
            catch (Exception ex)
            {
                msg = "game props: " + ex.Message;
                Debug.LogWarning("[HCT15] " + msg);
            }
            if (sc.IsValid() && sc.isLoaded)
            {
                var un = SceneManager.UnloadSceneAsync(sc);
                while (un != null && !un.isDone) yield return null;
            }
            Loading = false;
            LastMessage = msg;
            if (done != null) done(ok, msg);
        }
    }

    /// <summary>
    /// Small renders of a mesh or a prefab for the editor's asset browser: a private camera on a spare layer
    /// draws the meshes with Graphics.DrawMesh far below the world and reads the pixels back.
    /// </summary>
    public static class Thumbnails
    {
        private const int Layer = 30;
        private static readonly Vector3 Origin = new Vector3(0f, -7000f, 7000f);

        private struct Draw { public Mesh Mesh; public Matrix4x4 Matrix; public Material[] Materials; }

        public static Texture2D RenderMesh(Mesh mesh, Material[] materials, int size)
        {
            if (mesh == null) return null;
            var draws = new List<Draw> { new Draw { Mesh = mesh, Matrix = Matrix4x4.identity, Materials = materials } };
            return Render(draws, mesh.bounds, size);
        }

        public static Texture2D RenderPrefab(GameObject prefab, Bounds bounds, int size)
        {
            if (prefab == null) return null;
            var draws = new List<Draw>();
            Matrix4x4 rootInv = prefab.transform.worldToLocalMatrix;
            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var mr = mf.GetComponent<MeshRenderer>();
                draws.Add(new Draw { Mesh = mf.sharedMesh, Matrix = rootInv * mf.transform.localToWorldMatrix, Materials = mr != null ? mr.sharedMaterials : null });
            }
            foreach (var sk in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (sk.sharedMesh == null) continue;
                draws.Add(new Draw { Mesh = sk.sharedMesh, Matrix = rootInv * sk.transform.localToWorldMatrix, Materials = sk.sharedMaterials });
            }
            if (draws.Count == 0) return null;
            return Render(draws, bounds, size);
        }

        private static Texture2D Render(List<Draw> draws, Bounds b, int size)
        {
            GameObject camGo = null;
            RenderTexture rt = null;
            try
            {
                camGo = new GameObject("HCT15_thumb_cam") { hideFlags = HideFlags.HideAndDontSave };
                var cam = camGo.AddComponent<Camera>();
                cam.enabled = false;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.17f, 0.18f, 0.22f, 1f);
                cam.cullingMask = 1 << Layer;
                cam.fieldOfView = 28f;
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 4000f;
                cam.allowHDR = false; cam.allowMSAA = false; cam.useOcclusionCulling = false;
                cam.renderingPath = RenderingPath.Forward;
                float radius = Mathf.Max(0.4f, b.extents.magnitude);
                // from the road side (-x is the front of imported assets), a little above and in front
                Vector3 dir = new Vector3(-0.8f, 0.5f, -0.95f).normalized;
                float dist = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.02f;
                Vector3 centre = Origin + b.center;
                cam.transform.position = centre + dir * dist;
                cam.transform.LookAt(centre, Vector3.up);
                rt = RenderTexture.GetTemporary(size, size, 16);
                cam.targetTexture = rt;
                var fallback = PropDresser.PropMaterial();
                var shift = Matrix4x4.TRS(Origin, Quaternion.identity, Vector3.one);
                foreach (var d in draws)
                {
                    int subs = Mathf.Max(1, d.Mesh.subMeshCount);
                    for (int s = 0; s < subs; s++)
                    {
                        Material m = d.Materials != null && d.Materials.Length > 0 ? d.Materials[Mathf.Min(s, d.Materials.Length - 1)] : null;
                        Graphics.DrawMesh(d.Mesh, shift * d.Matrix, m ?? fallback, Layer, cam, s);
                    }
                }
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(size, size, TextureFormat.RGB24, false) { name = "HCT15:thumb" };
                tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                cam.targetTexture = null;
                return tex;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[HCT15] thumbnail render failed: " + ex.Message);
                return null;
            }
            finally
            {
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (camGo != null) UnityEngine.Object.DestroyImmediate(camGo);
            }
        }
    }
}
