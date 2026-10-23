using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// Custom scenery assets: Wavefront OBJ models (plus .mtl and optional textures) under
    /// HCT15Content\assets\models\&lt;id&gt;\ (a library shared by every track) and tracks\&lt;race&gt;\assets\&lt;id&gt;\
    /// (private to one track). An asset.json beside the .obj carries the id, a display name, the category
    /// (tree / building / landmark / prop), a scale, a default rotation and the ground offset.
    ///
    /// Loading: faces are fan-triangulated and unwelded so every triangle is flat shaded; each material's
    /// Kd colour becomes the vertex colour (the game's vertex-colour props shader, like the box buildings);
    /// a material with map_Kd gets its own sub-mesh with an unlit texture. X is mirrored like Unity's own
    /// importers, so a model whose front faces +X in the file faces -X in the game, i.e. towards the road
    /// when it stands on the right; left-side copies are turned 180 degrees so they face the road too.
    ///
    /// Import: copies the .obj, its .mtl and the textures into the library, writes asset.json, re-centres the
    /// footprint on the origin, puts the lowest point on the ground, guesses centimetres when the model is
    /// hundreds of units tall and reports the style checks (see assets\STYLE.txt).
    /// </summary>
    public static class AssetLibrary
    {
        [Serializable]
        public class AssetMeta
        {
            public string Id = "";
            public string Name = "";
            public string Category = "prop";     // tree | building | landmark | prop
            public string Description = "";
            public float Scale = 1f;
            public float Yaw = 0f;
            public float GroundOffset = 0f;
            public bool Recenter = true;
            public bool DoubleSided = false;
            public string Anim = "";             // 2.0.24: "rollwave" = RollingWave draws and moves a wave on it (asset_kit ola_rompiente); 2.0.26: "smoke" = SmokePlume off its top
        }

        public class AssetDef
        {
            public AssetMeta Meta = new AssetMeta();
            public string Id { get { return Meta.Id; } }
            public string Dir, ObjPath, TrackId;      // TrackId != null: private to that track
            public Vector3 Size;                      // after scale, in game units (metres)
            public Vector3 RawSize;                   // as in the file, before scale
            public DateTime ObjTime;
            public int Tris, MaterialCount;
            public bool Textured;
            public readonly List<string> Warnings = new List<string>();
            public Mesh Mesh; public Material[] Materials;
            public Texture2D Thumb;                   // editor browser thumbnail (thumb.png beside the .obj)
            public bool Loaded, Failed; public string Error = "";
            // skeleton (<id>.rig.json beside the .obj) and what RiggedFigure needs to skin the imported mesh
            public RigDef Rig;
            public int[] CornerFileVertex;            // for every mesh vertex: the 'v' line (0-based) it came from
            public Vector3 FileShift; public float FileScale = 1f; public float FileYaw;
            /// <summary>A point in the .obj's own coordinates -> the imported mesh's coordinates (recentre, scale, yaw, mirror X).</summary>
            public Vector3 FileToMesh(Vector3 p)
            {
                Vector3 q = Quaternion.Euler(0f, FileYaw, 0f) * ((p + FileShift) * (FileScale <= 0f ? 1f : FileScale));
                return new Vector3(-q.x, q.y, q.z);
            }
            public string Label { get { return string.IsNullOrEmpty(Meta.Name) ? Meta.Id : Meta.Name; } }
        }

        [Serializable] public class RigBone { public string Name = ""; public string Parent = ""; public float[] Head = new float[3]; public float[] Tail = new float[3]; }
        [Serializable] public class RigDef { public string Note = ""; public string Forward = ""; public float HeightM; public List<RigBone> Bones = new List<RigBone>(); public int[] VertexBone = new int[0]; public float[] TintColor = new float[0]; }

        public static readonly List<AssetDef> All = new List<AssetDef>();
        private static readonly Dictionary<string, AssetDef> _byId = new Dictionary<string, AssetDef>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, GameObject> _prototypes = new Dictionary<string, GameObject>();
        private static readonly Dictionary<string, Texture2D> _textures = new Dictionary<string, Texture2D>();
        private static bool _scanned;
        public const int TriBudgetTree = 300, TriBudgetProp = 600, TriBudgetBuilding = 2000, TriBudgetLandmark = 5000;

        public static string ModelsDir { get { return Path.Combine(Path.Combine(ContentRegistry.ContentDir ?? "", "assets"), "models"); } }

        public static AssetDef Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (!_scanned) Scan();
            AssetDef d; return _byId.TryGetValue(id, out d) ? d : null;
        }

        /// <summary>Re-reads the library folders; meshes already loaded are kept when the file did not change.</summary>
        public static void Scan()
        {
            _scanned = true;
            var old = new Dictionary<string, AssetDef>(_byId, StringComparer.OrdinalIgnoreCase);
            All.Clear(); _byId.Clear();
            try
            {
                if (string.IsNullOrEmpty(ContentRegistry.ContentDir)) return;
                Directory.CreateDirectory(ModelsDir);
                foreach (string dir in Directory.GetDirectories(ModelsDir)) AddFolder(dir, null, old);
                string tracks = Path.Combine(ContentRegistry.ContentDir, "tracks");
                if (Directory.Exists(tracks))
                    foreach (string t in Directory.GetDirectories(tracks))
                    {
                        string a = Path.Combine(t, "assets");
                        if (Directory.Exists(a)) foreach (string dir in Directory.GetDirectories(a)) AddFolder(dir, Path.GetFileName(t), old);
                    }
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] asset scan failed: " + ex.Message); }
            All.Sort((a, b) => string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase));
            Debug.Log("[HCT15] assets: " + All.Count + " model(s) in the library");
        }

        private static void AddFolder(string dir, string trackId, Dictionary<string, AssetDef> old)
        {
            string[] objs = Directory.GetFiles(dir, "*.obj");
            if (objs.Length == 0) return;
            var def = new AssetDef { Dir = dir, ObjPath = objs[0], TrackId = trackId };
            string metaPath = Path.Combine(dir, "asset.json");
            try { if (File.Exists(metaPath)) def.Meta = Json.FromJson<AssetMeta>(File.ReadAllText(metaPath)) ?? def.Meta; }
            catch (Exception ex) { def.Warnings.Add("asset.json unreadable: " + ex.Message); }
            if (string.IsNullOrEmpty(def.Meta.Id)) def.Meta.Id = Path.GetFileName(dir);
            if (trackId != null) def.Meta.Id = trackId + "/" + def.Meta.Id;
            if (_byId.ContainsKey(def.Meta.Id)) return;
            AssetDef prev;
            if (old.TryGetValue(def.Meta.Id, out prev) && prev.Loaded && prev.Mesh != null && prev.ObjPath == def.ObjPath && File.GetLastWriteTimeUtc(def.ObjPath) == prev.ObjTime)
            {
                prev.Meta = def.Meta;  // keep the mesh, refresh the metadata
                def = prev;
            }
            All.Add(def); _byId[def.Meta.Id] = def;
        }

        // ------------------------------------------------------------------ loading

        public static bool EnsureLoaded(AssetDef def)
        {
            if (def == null) return false;
            if (def.Loaded) return !def.Failed;
            def.Loaded = true;
            try
            {
                LoadObj(def);
                def.ObjTime = File.GetLastWriteTimeUtc(def.ObjPath);
                LoadRig(def);
                StyleChecks(def);
                Debug.Log("[HCT15] asset '" + def.Id + "': " + def.Tris + " tris, " + def.MaterialCount + " material(s), " + def.Size.x.ToString("0.0") + " x " + def.Size.y.ToString("0.0") + " x " + def.Size.z.ToString("0.0") + " m" + (def.Warnings.Count > 0 ? "  [" + string.Join("; ", def.Warnings.ToArray()) + "]" : ""));
                return true;
            }
            catch (Exception ex)
            {
                def.Failed = true; def.Error = ex.Message;
                Debug.LogWarning("[HCT15] asset '" + def.Id + "' failed to load: " + ex.Message);
                return false;
            }
        }

        private class Mtl { public string Name = ""; public Color Kd = new Color(0.8f, 0.8f, 0.8f); public string Map = ""; }

        private static void LoadObj(AssetDef def)
        {
            var ci = CultureInfo.InvariantCulture;
            var pos = new List<Vector3>(); var uvs = new List<Vector2>();
            var mtls = new Dictionary<string, Mtl>(StringComparer.OrdinalIgnoreCase);
            var faces = new List<KeyValuePair<Mtl, int[][]>>();   // material, [vertex index, uv index] per corner
            var cur = new Mtl { Name = "default" };
            foreach (string raw in File.ReadAllLines(def.ObjPath))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                string[] t = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                switch (t[0])
                {
                    case "v": pos.Add(new Vector3(F(t[1], ci), F(t[2], ci), F(t[3], ci))); break;
                    case "vt": uvs.Add(new Vector2(F(t[1], ci), t.Length > 2 ? F(t[2], ci) : 0f)); break;
                    case "mtllib":
                        {
                            string mpath = Path.Combine(Path.GetDirectoryName(def.ObjPath), line.Substring(6).Trim());
                            if (File.Exists(mpath)) ReadMtl(mpath, mtls, ci);
                            break;
                        }
                    case "usemtl":
                        {
                            string name = line.Substring(6).Trim();
                            if (!mtls.TryGetValue(name, out cur)) { cur = new Mtl { Name = name }; mtls[name] = cur; }
                            break;
                        }
                    case "f":
                        {
                            var corners = new List<int[]>();
                            for (int i = 1; i < t.Length; i++)
                            {
                                string[] p = t[i].Split('/');
                                int vi = int.Parse(p[0], ci); vi = vi < 0 ? pos.Count + vi : vi - 1;
                                int ti = -1;
                                if (p.Length > 1 && p[1].Length > 0) { ti = int.Parse(p[1], ci); ti = ti < 0 ? uvs.Count + ti : ti - 1; }
                                corners.Add(new[] { vi, ti });
                            }
                            if (corners.Count >= 3) faces.Add(new KeyValuePair<Mtl, int[][]>(cur, corners.ToArray()));
                            break;
                        }
                }
            }
            if (pos.Count == 0 || faces.Count == 0) throw new Exception("no geometry in " + Path.GetFileName(def.ObjPath));

            // ---- transform: centre the footprint, ground the lowest point, scale, default yaw, mirror X
            var b = new Bounds(pos[0], Vector3.zero); foreach (var p in pos) b.Encapsulate(p);
            float scale = def.Meta.Scale <= 0f ? 1f : def.Meta.Scale;
            Vector3 shift = def.Meta.Recenter ? new Vector3(-b.center.x, -b.min.y, -b.center.z) : Vector3.zero;
            var rot = Quaternion.Euler(0f, def.Meta.Yaw, 0f);
            def.FileShift = shift; def.FileScale = scale; def.FileYaw = def.Meta.Yaw;
            var world = new Vector3[pos.Count];
            for (int i = 0; i < pos.Count; i++)
            {
                Vector3 p = rot * ((pos[i] + shift) * scale);
                world[i] = new Vector3(-p.x, p.y, p.z);
            }

            // ---- sub-meshes: slot 0 = every plain material (vertex colours), one slot per texture
            var slotOfTexture = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var slotTris = new List<List<int>> { new List<int>() };
            var slotTex = new List<string> { null };
            var V = new List<Vector3>(); var C = new List<Color>(); var N = new List<Vector3>(); var UV = new List<Vector2>();
            var cornerOf = new List<int>();
            int tris = 0;
            foreach (var kv in faces)
            {
                Mtl m = kv.Key; int[][] c = kv.Value;
                int slot = 0;
                if (!string.IsNullOrEmpty(m.Map))
                {
                    if (!slotOfTexture.TryGetValue(m.Map, out slot)) { slot = slotTris.Count; slotOfTexture[m.Map] = slot; slotTris.Add(new List<int>()); slotTex.Add(m.Map); }
                }
                Color col = m.Kd; col.a = 1f;
                for (int i = 1; i + 1 < c.Length; i++)
                {
                    int[] a = c[0], bb = c[i + 1], cc = c[i];   // mirrored X -> reverse the winding
                    if (a[0] < 0 || bb[0] < 0 || cc[0] < 0 || a[0] >= world.Length || bb[0] >= world.Length || cc[0] >= world.Length) continue;
                    Vector3 pa = world[a[0]], pb = world[bb[0]], pc = world[cc[0]];
                    Vector3 nrm = Vector3.Cross(pb - pa, pc - pa);
                    if (nrm.sqrMagnitude < 1e-12f) continue;
                    nrm.Normalize();
                    int i0 = V.Count;
                    V.Add(pa); V.Add(pb); V.Add(pc);
                    cornerOf.Add(a[0]); cornerOf.Add(bb[0]); cornerOf.Add(cc[0]);
                    for (int k = 0; k < 3; k++) { C.Add(col); N.Add(nrm); }
                    UV.Add(Uv(uvs, a[1])); UV.Add(Uv(uvs, bb[1])); UV.Add(Uv(uvs, cc[1]));
                    slotTris[slot].Add(i0); slotTris[slot].Add(i0 + 1); slotTris[slot].Add(i0 + 2);
                    if (def.Meta.DoubleSided) { slotTris[slot].Add(i0); slotTris[slot].Add(i0 + 2); slotTris[slot].Add(i0 + 1); }
                    tris++;
                }
            }
            var mesh = new Mesh { name = "HCT15_asset_" + def.Id };
            mesh.indexFormat = V.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(V); mesh.SetColors(C); mesh.SetNormals(N); mesh.SetUVs(0, UV);
            var usedSlots = new List<int>(); for (int s = 0; s < slotTris.Count; s++) if (slotTris[s].Count > 0) usedSlots.Add(s);
            if (usedSlots.Count == 0) throw new Exception("no valid faces");
            mesh.subMeshCount = usedSlots.Count;
            var mats = new Material[usedSlots.Count];
            for (int k = 0; k < usedSlots.Count; k++)
            {
                int s = usedSlots[k];
                mesh.SetTriangles(slotTris[s], k);
                mats[k] = slotTex[s] == null ? PropDresser.PropMaterial() : TextureMaterial(Path.Combine(Path.GetDirectoryName(def.ObjPath), slotTex[s]));
            }
            mesh.RecalculateBounds();
            def.Mesh = mesh; def.Materials = mats; def.CornerFileVertex = cornerOf.ToArray();
            def.Size = mesh.bounds.size; def.Tris = tris; def.MaterialCount = mtls.Count == 0 ? 1 : mtls.Count; def.Textured = slotOfTexture.Count > 0;
            def.RawSize = b.size;
        }

        /// <summary>&lt;obj name&gt;.rig.json beside the model: bones + the bone of every 'v' line (Tools\make_mechanic.py writes one).</summary>
        private static void LoadRig(AssetDef def)
        {
            def.Rig = null;
            try
            {
                string path = Path.Combine(Path.GetDirectoryName(def.ObjPath), Path.GetFileNameWithoutExtension(def.ObjPath) + ".rig.json");
                if (!File.Exists(path)) return;
                RigDef rig = Json.FromJson<RigDef>(File.ReadAllText(path));
                if (rig == null || rig.Bones == null || rig.Bones.Count == 0) return;
                def.Rig = rig;
                Debug.Log("[HCT15] asset '" + def.Id + "': skeleton with " + rig.Bones.Count + " bones");
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] asset '" + def.Id + "': rig.json unreadable: " + ex.Message); }
        }

        private static float F(string s, CultureInfo ci) { float v; return float.TryParse(s, NumberStyles.Float, ci, out v) ? v : 0f; }
        private static Vector2 Uv(List<Vector2> uvs, int i) { return i >= 0 && i < uvs.Count ? uvs[i] : Vector2.zero; }

        private static void ReadMtl(string path, Dictionary<string, Mtl> mtls, CultureInfo ci)
        {
            Mtl cur = null;
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                string[] t = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (t[0] == "newmtl") { cur = new Mtl { Name = line.Substring(6).Trim() }; mtls[cur.Name] = cur; }
                else if (cur == null) continue;
                else if (t[0] == "Kd" && t.Length >= 4) cur.Kd = new Color(F(t[1], ci), F(t[2], ci), F(t[3], ci));
                else if (t[0] == "map_Kd") cur.Map = t[t.Length - 1];
            }
        }

        private static Material TextureMaterial(string path)
        {
            Texture2D tex;
            if (!_textures.TryGetValue(path, out tex))
            {
                tex = null;
                try
                {
                    if (File.Exists(path))
                    {
                        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                        if (t.LoadImage(File.ReadAllBytes(path))) { t.wrapMode = TextureWrapMode.Repeat; t.filterMode = FilterMode.Point; t.name = "HCT15:asset:" + Path.GetFileName(path); tex = t; }
                    }
                }
                catch (Exception ex) { Debug.LogWarning("[HCT15] asset texture unreadable " + path + ": " + ex.Message); }
                _textures[path] = tex;
            }
            if (tex == null) return PropDresser.PropMaterial();
            Shader sh = Shader.Find("Unlit/Texture") ?? Shader.Find("Standard");
            return new Material(sh) { mainTexture = tex, name = "HCT15_asset_tex" };
        }

        private static void StyleChecks(AssetDef def)
        {
            def.Warnings.Clear();
            int budget = def.Meta.Category == "tree" ? TriBudgetTree : def.Meta.Category == "building" ? TriBudgetBuilding : def.Meta.Category == "landmark" ? TriBudgetLandmark : TriBudgetProp;
            if (def.Tris > budget) def.Warnings.Add(def.Tris + " triangles, budget for '" + def.Meta.Category + "' is " + budget);
            float maxDim = Mathf.Max(def.Size.x, def.Size.y, def.Size.z);
            if (maxDim > 150f) def.Warnings.Add("about " + maxDim.ToString("0") + " m across - modelled in centimetres? set Scale 0.01 in asset.json");
            if (maxDim < 0.4f) def.Warnings.Add("smaller than 0.4 m - modelled in kilometres or inches? check Scale");
            if (def.Textured) def.Warnings.Add("uses textures; flat material colours are the house style");
            if (def.MaterialCount > 16) def.Warnings.Add(def.MaterialCount + " materials; the palette has 24 colours, most assets need 3-6");
            if (!def.Meta.Recenter && def.Mesh != null && Mathf.Abs(def.Mesh.bounds.min.y) > 0.3f) def.Warnings.Add("lowest point is " + def.Mesh.bounds.min.y.ToString("0.0") + " m from the ground (Recenter is off)");
        }

        // ------------------------------------------------------------------ thumbnail

        /// <summary>The browser thumbnail: thumb.png beside the .obj when it is newer than the model, else rendered now and saved.</summary>
        public static Texture2D Thumbnail(AssetDef def)
        {
            if (def == null) return null;
            if (def.Thumb != null) return def.Thumb;
            if (!EnsureLoaded(def)) return null;
            string png = Path.Combine(def.Dir, "thumb.png");
            try
            {
                if (File.Exists(png) && File.GetLastWriteTimeUtc(png) >= File.GetLastWriteTimeUtc(def.ObjPath))
                {
                    var t = new Texture2D(2, 2, TextureFormat.RGB24, false);
                    if (t.LoadImage(File.ReadAllBytes(png))) { t.name = "HCT15:thumb:" + def.Id; def.Thumb = t; return t; }
                }
                def.Thumb = Thumbnails.RenderMesh(def.Mesh, def.Materials, 112);
                if (def.Thumb != null) File.WriteAllBytes(png, def.Thumb.EncodeToPNG());
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] asset '" + def.Id + "': thumbnail failed: " + ex.Message); }
            return def.Thumb;
        }

        // ------------------------------------------------------------------ runtime prototype

        /// <summary>A parked, active prototype (like the box buildings) for the given side; PropView adds the mover.</summary>
        public static GameObject Prototype(AssetDef def, float yaw, bool leftSide)
        {
            if (!EnsureLoaded(def)) return null;
            int yawKey = Mathf.RoundToInt(yaw / 5f) * 5;
            string key = def.Id + "|" + yawKey + (leftSide ? "|L" : "|R");
            GameObject go;
            if (_prototypes.TryGetValue(key, out go) && go != null) return go;
            go = new GameObject("HCT15_asset_" + def.Id.Replace('/', '_') + "_" + yawKey + (leftSide ? "L" : "R"));
            go.transform.SetParent(PropDresser.Holder(), false);
            var child = MeshObject(def, go.transform);
            child.transform.localRotation = Quaternion.Euler(0f, yawKey + (leftSide ? 180f : 0f), 0f);
            child.transform.localPosition = new Vector3(0f, def.Meta.GroundOffset, 0f);
            if (def.Meta.Anim == "rollwave") child.AddComponent<RollingWave>();   // copies of the prototype carry it
            else if (def.Meta.Anim == "smoke") child.AddComponent<SmokePlume>();   // 2.0.26: the mine's chimneys
            else if (def.Meta.Anim == "confetti") child.AddComponent<Confetti>();   // 2.0.28: the Plaza de Illapel's crowd
            else if (def.Meta.Anim == "crowd") CrowdJump.Build(child, def);           // 2.0.28: the crowd jumps and dances
            _prototypes[key] = go;
            return go;
        }

        /// <summary>A plain mesh object (filter + renderer, shadows off) on 'parent' - used by the prototype and the editor preview.</summary>
        public static GameObject MeshObject(AssetDef def, Transform parent)
        {
            var child = new GameObject("mesh") { layer = parent.gameObject.layer };
            child.transform.SetParent(parent, false);
            child.AddComponent<MeshFilter>().sharedMesh = def.Mesh;
            var r = child.AddComponent<MeshRenderer>();
            r.sharedMaterials = def.Materials;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return child;
        }

        // ------------------------------------------------------------------ import

        /// <summary>Copies an .obj (with its .mtl and textures) into the library, writes asset.json, loads and checks it.</summary>
        public static AssetDef Import(string sourceObj, string trackId, out string message)
        {
            message = "";
            try
            {
                sourceObj = (sourceObj ?? "").Trim().Trim('"');
                if (!File.Exists(sourceObj)) { message = "file not found: " + sourceObj; return null; }
                if (!sourceObj.EndsWith(".obj", StringComparison.OrdinalIgnoreCase)) { message = "only Wavefront .obj files (export one from Blender, MagicaVoxel, Blockbench...)"; return null; }
                string baseId = SafeId(Path.GetFileNameWithoutExtension(sourceObj));
                string root = trackId == null ? ModelsDir : Path.Combine(Path.Combine(Path.Combine(ContentRegistry.ContentDir, "tracks"), trackId), "assets");
                Directory.CreateDirectory(root);
                string id = baseId; int k = 2;
                while (Directory.Exists(Path.Combine(root, id))) id = baseId + "_" + (k++);
                string dir = Path.Combine(root, id);
                Directory.CreateDirectory(dir);
                string srcDir = Path.GetDirectoryName(sourceObj);
                File.Copy(sourceObj, Path.Combine(dir, id + ".obj"));
                int copied = 1;
                // the .mtl files named in the obj, and the textures named in those
                foreach (string raw in File.ReadAllLines(sourceObj))
                {
                    string line = raw.Trim();
                    if (!line.StartsWith("mtllib ")) continue;
                    string mtlName = line.Substring(7).Trim();
                    string mtlSrc = Path.Combine(srcDir, mtlName);
                    if (!File.Exists(mtlSrc)) continue;
                    File.Copy(mtlSrc, Path.Combine(dir, Path.GetFileName(mtlName)), true); copied++;
                    foreach (string mraw in File.ReadAllLines(mtlSrc))
                    {
                        string ml = mraw.Trim();
                        if (!ml.StartsWith("map_")) continue;
                        string[] t = ml.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        string texName = t[t.Length - 1];
                        string texSrc = Path.Combine(srcDir, texName);
                        if (!File.Exists(texSrc)) texSrc = Path.Combine(Path.GetDirectoryName(mtlSrc), texName);
                        if (File.Exists(texSrc)) { File.Copy(texSrc, Path.Combine(dir, Path.GetFileName(texName)), true); copied++; }
                    }
                }
                if (copied > 1) FixRelativeNames(Path.Combine(dir, id + ".obj"), dir);
                var meta = new AssetMeta { Id = id, Name = Pretty(baseId), Category = GuessCategory(baseId) };
                // unit guess: hundreds of units tall means centimetres
                var probe = new AssetDef { Meta = meta, Dir = dir, ObjPath = Path.Combine(dir, id + ".obj") };
                LoadObj(probe);
                if (Mathf.Max(probe.RawSize.x, probe.RawSize.y, probe.RawSize.z) > 400f) meta.Scale = 0.01f;
                File.WriteAllText(Path.Combine(dir, "asset.json"), Json.ToJson(meta, true));
                Scan();
                var def = Get(trackId == null ? id : trackId + "/" + id);
                if (def == null) { message = "imported but not found after rescan"; return null; }
                EnsureLoaded(def);
                if (def.Failed) { message = "could not load: " + def.Error; return def; }
                message = def.Label + ": " + def.Tris + " tris, " + def.Size.x.ToString("0.0") + " x " + def.Size.y.ToString("0.0") + " x " + def.Size.z.ToString("0.0") + " m, " + def.MaterialCount + " material(s)" + (meta.Scale != 1f ? ", scale " + meta.Scale : "") + (def.Warnings.Count > 0 ? "  -  " + string.Join("; ", def.Warnings.ToArray()) : "  -  ok");
                Debug.Log("[HCT15] asset imported: " + message);
                return def;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                Debug.LogWarning("[HCT15] asset import failed: " + ex);
                return null;
            }
        }

        /// <summary>mtllib / map_Kd lines may carry folders; inside the library everything sits flat beside the .obj.</summary>
        private static void FixRelativeNames(string objPath, string dir)
        {
            try
            {
                var lines = File.ReadAllLines(objPath);
                for (int i = 0; i < lines.Length; i++)
                    if (lines[i].TrimStart().StartsWith("mtllib ")) lines[i] = "mtllib " + Path.GetFileName(lines[i].Trim().Substring(7).Trim());
                File.WriteAllLines(objPath, lines);
                foreach (string mtl in Directory.GetFiles(dir, "*.mtl"))
                {
                    var ml = File.ReadAllLines(mtl);
                    for (int i = 0; i < ml.Length; i++)
                        if (ml[i].TrimStart().StartsWith("map_"))
                        {
                            string[] t = ml[i].Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                            ml[i] = t[0] + " " + Path.GetFileName(t[t.Length - 1]);
                        }
                    File.WriteAllLines(mtl, ml);
                }
            }
            catch (Exception) { }
        }

        public static string SafeId(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char ch in (s ?? "").ToLowerInvariant()) sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');
            string r = sb.ToString().Trim('_');
            return r.Length == 0 ? "asset" : r;
        }

        private static string Pretty(string id) { return id.Replace('_', ' ').Trim(); }

        private static string GuessCategory(string id)
        {
            string s = id.ToLowerInvariant();
            foreach (string w in new[] { "tree", "pine", "palm", "bush", "arbol", "cactus", "araucaria", "shrub" }) if (s.Contains(w)) return "tree";
            foreach (string w in new[] { "house", "building", "bld", "tower", "church", "chapel", "shop", "casa", "edificio", "iglesia", "station", "garage", "hotel" }) if (s.Contains(w)) return "building";
            foreach (string w in new[] { "monument", "statue", "moai", "landmark", "castle", "cathedral", "arch", "fountain" }) if (s.Contains(w)) return "landmark";
            return "prop";
        }

        // ------------------------------------------------------------------ Windows file dialog

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class OpenFileName
        {
            public int structSize; public IntPtr dlgOwner; public IntPtr instance;
            public string filter; public string customFilter; public int maxCustFilter; public int filterIndex;
            public string file; public int maxFile; public string fileTitle; public int maxFileTitle;
            public string initialDir; public string title; public int flags; public short fileOffset; public short fileExtension;
            public string defExt; public IntPtr custData; public IntPtr hook; public string templateName;
            public IntPtr reservedPtr; public int reservedInt; public int flagsEx;
        }

        [DllImport("Comdlg32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool GetOpenFileName([In, Out] OpenFileName ofn);

        /// <summary>The Windows "Open" dialog for an .obj; null when cancelled or unavailable (the game window may hide it in exclusive full screen).</summary>
        public static string BrowseForObj()
        {
            try
            {
                var ofn = new OpenFileName();
                ofn.structSize = Marshal.SizeOf(ofn);
                ofn.filter = "Wavefront OBJ (*.obj)\0*.obj\0All files (*.*)\0*.*\0\0";
                ofn.file = new string('\0', 2048); ofn.maxFile = ofn.file.Length;
                ofn.fileTitle = new string('\0', 256); ofn.maxFileTitle = ofn.fileTitle.Length;
                ofn.title = "HCT 1.6 - import a model (.obj)";
                ofn.flags = 0x00080000 | 0x00001000 | 0x00000800 | 0x00000008;   // explorer, file must exist, path must exist, no change dir
                ofn.defExt = "obj";
                if (!GetOpenFileName(ofn)) return null;
                return ofn.file.TrimEnd('\0');
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] file dialog unavailable: " + ex.Message); return null; }
        }
    }
}
