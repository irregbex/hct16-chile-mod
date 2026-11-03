using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// 2.3.5: a readable copy of one of the game's own meshes, read from the player's installed game files
    /// (HorizonChase_Data\resources.assets and its .resS) - nothing of the game is shipped with the mod. The game's meshes are
    /// loaded unreadable, so their triangles cannot be touched at run time; the podium needs them to let a car's own driver
    /// get off (PodiumDrivers). Unity 2018.4 serialized file, version 17, no type tree: the object table, then a Mesh's fields
    /// in their 2018.4 order (sub-meshes, blend shapes, bind poses, flags, index buffer, vertex data with its channels, the
    /// compressed-mesh block, bounds, collision bakes, metrics, stream data). Positions/normals/uvs may be half floats,
    /// colours 8-bit; the vertex data is inline or in the .resS at the stream offset.
    /// </summary>
    public static class GameMeshReader
    {
        private static Dictionary<string, KeyValuePair<long, int>> _index;   // mesh name -> object (offset, size) in resources.assets
        private static readonly Dictionary<string, Mesh> _copies = new Dictionary<string, Mesh>();
        private static string _assets;

        public static Mesh Copy(Mesh src)
        {
            if (src == null) return null;
            if (src.isReadable) return src;
            Mesh m;
            if (_copies.TryGetValue(src.name, out m)) return m;
            try { m = Read(src.name); }
            catch (Exception ex) { Debug.LogWarning("[HCT15] game mesh '" + src.name + "': " + ex.Message); m = null; }
            if (m != null && m.vertexCount != src.vertexCount) { Debug.LogWarning("[HCT15] game mesh '" + src.name + "': " + m.vertexCount + " vertices read, the game has " + src.vertexCount + " - not used"); m = null; }
            _copies[src.name] = m;
            return m;
        }

        private sealed class Rd
        {
            public readonly byte[] B; public int P;
            public Rd(byte[] b) { B = b; }
            public byte U8() { return B[P++]; }
            public int I32() { int v = BitConverter.ToInt32(B, P); P += 4; return v; }
            public uint U32() { uint v = BitConverter.ToUInt32(B, P); P += 4; return v; }
            public float F32() { float v = BitConverter.ToSingle(B, P); P += 4; return v; }
            public void Align() { P = (P + 3) & ~3; }
            public byte[] Bytes() { int n = I32(); var v = new byte[n]; Buffer.BlockCopy(B, P, v, 0, n); P += n; Align(); return v; }
            public string Str() { return System.Text.Encoding.UTF8.GetString(Bytes()); }
            public void Skip(int n) { P += n; }
        }

        private static int BE(byte[] b, int at) { return (b[at] << 24) | (b[at + 1] << 16) | (b[at + 2] << 8) | b[at + 3]; }

        private static void BuildIndex()
        {
            _index = new Dictionary<string, KeyValuePair<long, int>>();
            _assets = Path.Combine(Application.dataPath, "resources.assets");
            using (var fs = File.OpenRead(_assets))
            {
                var head = new byte[20]; fs.Read(head, 0, 20);
                int metaSize = BE(head, 0), version = BE(head, 8); long dataOff = (uint)BE(head, 12);
                if (version != 17) throw new Exception("serialized file version " + version + " (17 expected)");
                var meta = new byte[metaSize]; fs.Read(meta, 0, metaSize);
                var r = new Rd(meta);
                while (meta[r.P] != 0) r.P++; r.P++;          // unity version
                r.I32(); r.U8();                              // platform, type tree flag (none in the player)
                int types = r.I32(); var cls = new int[types];
                for (int i = 0; i < types; i++)
                {
                    cls[i] = r.I32(); r.U8(); short script = BitConverter.ToInt16(meta, r.P); r.P += 2;
                    if (cls[i] == 114 || script >= 0) r.Skip(16);
                    r.Skip(16);
                }
                int count = r.I32();
                var name = new byte[256];
                for (int i = 0; i < count; i++)
                {
                    r.Align(); r.Skip(8);                     // path id
                    uint start = r.U32(); int size = r.I32(); int ti = r.I32();
                    if (ti < 0 || ti >= types || cls[ti] != 43) continue;     // Mesh
                    long at = dataOff + start;
                    fs.Position = at; fs.Read(name, 0, 4);
                    int nl = BitConverter.ToInt32(name, 0);
                    if (nl <= 0 || nl > 250) continue;
                    fs.Read(name, 0, nl);
                    string n = System.Text.Encoding.UTF8.GetString(name, 0, nl);
                    if (!_index.ContainsKey(n)) _index[n] = new KeyValuePair<long, int>(at, size);
                }
            }
            Debug.Log("[HCT15] game meshes: " + _index.Count + " indexed in resources.assets (read on demand for the podium)");
        }

        private static float Half(ushort h)
        {
            int s = (h >> 15) & 1, e = (h >> 10) & 31, f = h & 1023;
            float v = e == 0 ? f / 1024f * Mathf.Pow(2, -14) : e == 31 ? float.PositiveInfinity : (1f + f / 1024f) * Mathf.Pow(2, e - 15);
            return s == 1 ? -v : v;
        }

        private static readonly int[] FmtSize = { 4, 2, 1, 1, 2, 2, 1, 1, 2, 2, 4, 4 };

        private static float Comp(byte[] d, int at, int fmt)
        {
            switch (fmt)
            {
                case 0: return BitConverter.ToSingle(d, at);
                case 1: return Half(BitConverter.ToUInt16(d, at));
                case 2: return d[at] / 255f;
                case 3: return Mathf.Max(-1f, (sbyte)d[at] / 127f);
                case 4: return BitConverter.ToUInt16(d, at) / 65535f;
                case 5: return Mathf.Max(-1f, BitConverter.ToInt16(d, at) / 32767f);
                default: return 0f;
            }
        }

        private static Mesh Read(string name)
        {
            if (_index == null) BuildIndex();
            KeyValuePair<long, int> loc;
            if (!_index.TryGetValue(name, out loc)) throw new Exception("not in resources.assets");
            byte[] blob = new byte[loc.Value];
            using (var fs = File.OpenRead(_assets)) { fs.Position = loc.Key; fs.Read(blob, 0, blob.Length); }
            var r = new Rd(blob);
            r.Str();
            int subCount = r.I32();
            var subs = new int[subCount, 4];          // firstByte, indexCount, topology, baseVertex
            for (int i = 0; i < subCount; i++)
            {
                subs[i, 0] = (int)r.U32(); subs[i, 1] = (int)r.U32(); subs[i, 2] = r.I32(); subs[i, 3] = (int)r.U32();
                r.U32(); r.U32(); r.Skip(24);         // firstVertex, vertexCount, bounds
            }
            r.Skip(r.I32() * 40);                     // blend shape vertices
            int shapes = r.I32(); for (int i = 0; i < shapes; i++) { r.Skip(8); r.U8(); r.U8(); r.Align(); }
            int chans = r.I32(); for (int i = 0; i < chans; i++) { r.Str(); r.Skip(12); }
            r.Skip(r.I32() * 4);                      // full weights
            r.Skip(r.I32() * 64);                     // bind poses
            r.Skip(r.I32() * 4);                      // bone name hashes
            r.U32();                                  // root bone hash
            byte compression = r.U8(); r.U8(); r.U8(); r.U8(); r.Align();
            if (compression != 0) throw new Exception("compressed mesh");
            int indexFormat = r.I32();
            byte[] ib = r.Bytes();
            int vcount = (int)r.U32();
            int nch = r.I32(); var ch = new byte[nch, 4];
            for (int i = 0; i < nch; i++) for (int k = 0; k < 4; k++) ch[i, k] = r.U8();
            byte[] data = r.Bytes();
            for (int i = 0; i < 4; i++) { r.U32(); r.F32(); r.F32(); r.Bytes(); r.U8(); r.Align(); }   // vertices, uv, normals, tangents
            for (int i = 0; i < 3; i++) { r.U32(); r.Bytes(); r.U8(); r.Align(); }                     // weights, normal signs, tangent signs
            r.U32(); r.F32(); r.F32(); r.Bytes(); r.U8(); r.Align();                                    // float colours
            for (int i = 0; i < 2; i++) { r.U32(); r.Bytes(); r.U8(); r.Align(); }                     // bone indices, triangles
            r.U32();                                  // uv info
            r.Skip(24); r.I32();                      // bounds, usage flags
            r.Bytes(); r.Bytes();                     // baked collision meshes
            r.Skip(8);                                // mesh metrics
            uint soff = r.U32(), ssize = r.U32(); string spath = r.Str();
            if (data.Length == 0 && ssize > 0)
            {
                string res = Path.Combine(Application.dataPath, Path.GetFileName(spath));
                data = new byte[ssize];
                using (var fs = File.OpenRead(res)) { fs.Position = soff; fs.Read(data, 0, (int)ssize); }
            }
            // streams: each stream's stride from its channels; streams follow each other, 16-byte aligned
            var stride = new int[4];
            for (int i = 0; i < nch; i++) { int dim = ch[i, 3] & 15; if (dim == 0) continue; int s = ch[i, 0]; stride[s] = Math.Max(stride[s], ch[i, 1] + dim * FmtSize[ch[i, 2]]); }
            var baseOf = new int[4]; int off = 0;
            for (int s = 0; s < 4; s++) { baseOf[s] = off; off += stride[s] * vcount; off = (off + 15) & ~15; }
            Func<int, int, int, float> get = (c, v, k) => Comp(data, baseOf[ch[c, 0]] + v * stride[ch[c, 0]] + ch[c, 1] + k * FmtSize[ch[c, 2]], ch[c, 2]);
            Func<int, bool> has = c => c < nch && (ch[c, 3] & 15) > 0;
            var pos = new Vector3[vcount]; for (int v = 0; v < vcount; v++) pos[v] = new Vector3(get(0, v, 0), get(0, v, 1), get(0, v, 2));
            var mesh = new Mesh { name = name + " (HCT15 copy)" };
            mesh.indexFormat = vcount > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.vertices = pos;
            if (has(1)) { var n = new Vector3[vcount]; for (int v = 0; v < vcount; v++) n[v] = new Vector3(get(1, v, 0), get(1, v, 1), get(1, v, 2)); mesh.normals = n; }
            if (has(3)) { var c = new Color[vcount]; int dim = ch[3, 3] & 15; for (int v = 0; v < vcount; v++) c[v] = new Color(get(3, v, 0), get(3, v, 1), get(3, v, 2), dim > 3 ? get(3, v, 3) : 1f); mesh.colors = c; }
            if (has(4)) { var u = new Vector2[vcount]; for (int v = 0; v < vcount; v++) u[v] = new Vector2(get(4, v, 0), get(4, v, 1)); mesh.uv = u; }
            mesh.subMeshCount = subCount;
            for (int s = 0; s < subCount; s++)
            {
                int first = subs[s, 0], n = subs[s, 1], bv = subs[s, 3];
                if (subs[s, 2] != 0) throw new Exception("not triangles");
                var t = new int[n];
                for (int i = 0; i < n; i++) t[i] = bv + (indexFormat == 0 ? BitConverter.ToUInt16(ib, first + i * 2) : (int)BitConverter.ToUInt32(ib, first + i * 4));
                mesh.SetTriangles(t, s);
            }
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
