using System;
using System.Collections.Generic;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// A library asset with a skeleton (&lt;id&gt;.rig.json beside the .obj, made by Tools\make_mechanic.py) turned into a
    /// posable figure: one Transform per bone, a SkinnedMeshRenderer over a copy of the asset mesh with one bone per
    /// vertex (rigid low-poly skinning), and the shirt colour replaced by a tint per figure. Bones are plain
    /// transforms whose rest rotation is identity in the figure's space, so a pose is just local rotations:
    /// Pose("spine", Quaternion.Euler(0, 0, 30)) bends the spine 30 degrees forward. Figure space: after the OBJ
    /// importer's mirror the model FACES -X; +Y is up; +Z is its left side. Bend forward = rotate about +Z,
    /// raise an arm sideways = rotate about X, twist = rotate about Y.
    /// </summary>
    public class RiggedFigure : MonoBehaviour
    {
        public AssetLibrary.AssetDef Def;
        public SkinnedMeshRenderer Renderer;
        private readonly Dictionary<string, Transform> _bones = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Quaternion> _rest = new Dictionary<string, Quaternion>(StringComparer.OrdinalIgnoreCase);
        private Mesh _mesh;

        /// <summary>The direction the model looks along in figure space (the importer mirrors the file's +X).</summary>
        public static readonly Vector3 Forward = Vector3.left;

        public static bool HasRig(AssetLibrary.AssetDef def) { return def != null && AssetLibrary.EnsureLoaded(def) && def.Rig != null && def.Rig.Bones != null && def.Rig.Bones.Count > 0; }

        /// <summary>Builds the figure at the origin (parent it afterwards). Returns null when the asset has no rig.</summary>
        public static RiggedFigure Create(AssetLibrary.AssetDef def, Color? tint, string name = "figure")
        {
            if (!HasRig(def)) return null;
            try
            {
                var go = new GameObject(name);
                var fig = go.AddComponent<RiggedFigure>();
                fig.Def = def;
                fig.Build(tint);
                return fig;
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] rigged figure '" + def.Id + "' failed: " + ex.Message); return null; }
        }

        private void Build(Color? tint)
        {
            var rig = Def.Rig;
            // ---- bones: file space -> mesh space, hierarchy by parent name
            var heads = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);
            foreach (var b in rig.Bones) heads[b.Name] = Def.FileToMesh(new Vector3(b.Head[0], b.Head[1], b.Head[2]));
            var order = new List<AssetLibrary.RigBone>();
            var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int guard = 0;
            while (order.Count < rig.Bones.Count && guard++ < 100)
                foreach (var b in rig.Bones)
                    if (!placed.Contains(b.Name) && (string.IsNullOrEmpty(b.Parent) || placed.Contains(b.Parent))) { order.Add(b); placed.Add(b.Name); }
            var boneTransforms = new Transform[rig.Bones.Count];
            var indexOf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < rig.Bones.Count; i++) indexOf[rig.Bones[i].Name] = i;
            foreach (var b in order)
            {
                var t = new GameObject(b.Name).transform;
                Transform parent = string.IsNullOrEmpty(b.Parent) || !_bones.ContainsKey(b.Parent) ? transform : _bones[b.Parent];
                t.SetParent(parent, false);
                t.position = transform.TransformPoint(heads[b.Name]);   // figure space -> world (the figure sits at the origin now)
                t.rotation = transform.rotation;
                _bones[b.Name] = t; _rest[b.Name] = t.localRotation;
                boneTransforms[indexOf[b.Name]] = t;
            }
            // ---- mesh copy with one bone per vertex (through the OBJ vertex each corner came from)
            _mesh = UnityEngine.Object.Instantiate(Def.Mesh);
            _mesh.name = Def.Mesh.name + "_rigged";
            int vcount = _mesh.vertexCount;
            var weights = new BoneWeight[vcount];
            int[] corner = Def.CornerFileVertex;
            int fallback = indexOf.ContainsKey("pelvis") ? indexOf["pelvis"] : 0;
            for (int i = 0; i < vcount; i++)
            {
                int bone = fallback;
                if (corner != null && i < corner.Length && rig.VertexBone != null && corner[i] >= 0 && corner[i] < rig.VertexBone.Length)
                {
                    int bi = rig.VertexBone[corner[i]];
                    if (bi >= 0 && bi < boneTransforms.Length) bone = bi;
                }
                weights[i] = new BoneWeight { boneIndex0 = bone, weight0 = 1f };
            }
            _mesh.boneWeights = weights;
            var bind = new Matrix4x4[boneTransforms.Length];
            for (int i = 0; i < boneTransforms.Length; i++) bind[i] = boneTransforms[i].worldToLocalMatrix * transform.localToWorldMatrix;
            _mesh.bindposes = bind;
            if (tint.HasValue && rig.TintColor != null && rig.TintColor.Length >= 3)
            {
                var cols = _mesh.colors;
                var key = new Color(rig.TintColor[0], rig.TintColor[1], rig.TintColor[2], 1f);
                int n = 0;
                for (int i = 0; i < cols.Length; i++)
                    if (Mathf.Abs(cols[i].r - key.r) < 0.02f && Mathf.Abs(cols[i].g - key.g) < 0.02f && Mathf.Abs(cols[i].b - key.b) < 0.02f) { cols[i] = tint.Value; n++; }
                if (n > 0) _mesh.colors = cols;
            }
            _mesh.RecalculateBounds();
            // ---- renderer
            var holder = new GameObject("mesh");
            holder.transform.SetParent(transform, false);
            Renderer = holder.AddComponent<SkinnedMeshRenderer>();
            Renderer.sharedMesh = _mesh;
            Renderer.bones = boneTransforms;
            Renderer.rootBone = boneTransforms[0];
            Renderer.sharedMaterials = Def.Materials;
            Renderer.updateWhenOffscreen = true;
            Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Renderer.receiveShadows = false;
            Renderer.quality = SkinQuality.Bone1;
        }

        public Transform Bone(string name) { Transform t; return _bones.TryGetValue(name, out t) ? t : null; }

        /// <summary>Sets a bone's rotation relative to its rest pose.</summary>
        public void Pose(string bone, Quaternion relative)
        {
            Transform t; Quaternion rest;
            if (_bones.TryGetValue(bone, out t) && _rest.TryGetValue(bone, out rest)) t.localRotation = rest * relative;
        }

        public void Pose(string bone, float x, float y, float z) { Pose(bone, Quaternion.Euler(x, y, z)); }

        /// <summary>Puts every bone back to the rest pose.</summary>
        public void Reset() { foreach (var kv in _bones) kv.Value.localRotation = _rest[kv.Key]; }

        /// <summary>Turns the figure so it looks along a horizontal direction (figure space forward is -X).</summary>
        public void Face(Vector3 dir)
        {
            dir.y = 0f; if (dir.sqrMagnitude < 1e-6f) return;
            transform.localRotation = Quaternion.FromToRotation(Forward, dir.normalized);
        }

        /// <summary>A small prop (a box) held in a hand: parented to the bone, offset in the bone's frame.</summary>
        public GameObject Hold(string bone, Vector3 size, Vector3 offset, Color color)
        {
            Transform t = Bone(bone); if (t == null) return null;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var col = go.GetComponent<Collider>(); if (col != null) UnityEngine.Object.Destroy(col);
            go.transform.SetParent(t, false);
            go.transform.localPosition = offset; go.transform.localScale = size;
            var r = go.GetComponent<Renderer>();
            var m = new Material(Shader.Find("Standard") ?? Shader.Find("Diffuse")) { color = color };
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.15f);
            r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        private void OnDestroy() { if (_mesh != null) UnityEngine.Object.Destroy(_mesh); }
    }
}
