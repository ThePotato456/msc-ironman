using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace IronManSuit
{
    // Meshes, textures and sounds from Mods/Assets/IronManSuit (a deploy copies assets/models, tex and sfx there, flat).
    // Unity objects die with the scene, so everything loads once per game scene: Init clears the caches.
    static class Res
    {
        static string dir;
        static readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();
        static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
        static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();

        public static Material Metal, Glow, ReactorHousing;

        // Arc reactor materials by class name: the `usemtl` names written by tools/reactor/build_reactor.py.
        static readonly Dictionary<string, Material> reactorMats = new Dictionary<string, Material>();
        // Submesh material names per loaded OBJ, in submesh order.
        static readonly Dictionary<string, string[]> subNames = new Dictionary<string, string[]>();

        public static void Init(string assetsDir)
        {
            dir = assetsDir;
            meshes.Clear();
            textures.Clear();
            clips.Clear();
            without.Clear();
            coilBlocks = null;
            CoilBlockCentre = null;
            Bolt.ResetScene();

            var diffuse = Shader.Find("Legacy Shaders/Diffuse");
            var selfIllum = Shader.Find("Legacy Shaders/Self-Illumin/Diffuse");
            var standard = FindShader("Standard") ?? diffuse;

            Metal = new Material(standard) { name = "ironman_reactor_metal", color = new Color(0.68f, 0.7f, 0.74f) };
            if (Metal.HasProperty("_Metallic")) { Metal.SetFloat("_Metallic", 0.85f); Metal.SetFloat("_Glossiness", 0.55f); }

            // Blue glow ring: the clear light-guide ring under the coils renders self-lit in this cyan (matches the
            // original look in rec/shots/driveway_reactor.png, which the user wants kept). Keep it blue.
            // Unlit, so it reads the same blue in daylight, at night and under studio lights (Self-Illum adds the
            // lighting on top and washes it to white under bright light). The texture is a radial gradient: whiter
            // through the ring's middle, deeper blue at the edges (tools/reactor/make_textures.py).
            var unlit = FindShader("Unlit/Texture");
            Glow = new Material(unlit ?? selfIllum ?? diffuse) { name = "ironman_reactor_glow", color = new Color(0.35f, 0.8f, 1f), mainTexture = Tex("reactor_glow.png") };
            if (unlit == null) Glow.color = Color.white * 0.75f;
            Debug.Log("IronManSuit: glow shader " + Glow.shader.name);

            // Silvery brushed steel (the user asked for metal-grey, not black): the brushed-metal albedo lifted bright
            // (make_textures.py reactor_steel_albedo), satin. Metals only read as metal with something to reflect:
            // see WorkshopProbe.
            ReactorHousing = new Material(standard) { name = "ironman_reactor_housing", color = new Color(0.82f, 0.83f, 0.86f), mainTexture = Tex("reactor_steel_albedo.png", false, TextureWrapMode.Repeat) };
            Pbr(ReactorHousing, 0.7f, 0.55f);
            workshopRefl = null;

            // One material per part class (arc_reactor_mk1_reference_4 / _2): copper windings on the glowing ring,
            // white corner clips, steel rim/brackets/cage (class "black" from the CAD pass, now silvery), brass + chrome stepped rings, woven centre grille,
            // aluminium heatsink, and the LED strip and stacked discs glowing whiter than the ring.
            reactorMats.Clear();
            reactorMats["glowring"] = Glow;
            reactorMats["glowcore"] = new Material(selfIllum ?? diffuse) { name = "ironman_reactor_glowcore", color = new Color(0.75f, 0.95f, 1f) };
            reactorMats["glowdisc"] = new Material(selfIllum ?? diffuse) { name = "ironman_reactor_glowdisc", color = new Color(0.55f, 0.88f, 1f) };
            reactorMats["black"] = ReactorHousing;
            reactorMats["copper"] = Pbr(new Material(standard) { name = "ironman_reactor_copper", color = new Color(0.95f, 0.88f, 0.85f), mainTexture = Tex("reactor_winding.png", false, TextureWrapMode.Repeat) }, 0.45f, 0.72f);
            reactorMats["clip"] = Pbr(new Material(standard) { name = "ironman_reactor_clip", color = new Color(0.86f, 0.87f, 0.88f) }, 0f, 0.45f);
            reactorMats["brass"] = Pbr(new Material(standard) { name = "ironman_reactor_brass", color = new Color(1f, 0.78f, 0.36f) }, 0.85f, 0.75f);
            reactorMats["chrome"] = Pbr(new Material(standard) { name = "ironman_reactor_chrome", color = new Color(0.95f, 0.95f, 0.97f) }, 1f, 0.85f);
            reactorMats["steel"] = Pbr(new Material(standard) { name = "ironman_reactor_steel", color = new Color(0.6f, 0.62f, 0.65f) }, 1f, 0.6f);
            reactorMats["alu"] = Pbr(new Material(standard) { name = "ironman_reactor_alu", color = new Color(0.72f, 0.74f, 0.77f) }, 0.8f, 0.45f);
            // Centre grille, lit from behind like both references: self-lit weave, pale blue-white.
            var grille = new Material(selfIllum ?? diffuse) { name = "ironman_reactor_mesh", color = new Color(0.5f, 0.6f, 0.68f), mainTexture = Tex("reactor_mesh.png", false, TextureWrapMode.Repeat) };
            grille.mainTextureScale = new Vector2(120f, 120f);   // planar UVs span 9 cm: ~0.75 mm per weave cell
            reactorMats["mesh"] = grille;

            // The ring unpowered (a loose coil, or the reactor before its core is charged): pale blue clear plastic,
            // lit by the scene, not glowing. Reactor.Glow switches between this and Glow.
            GlowOff = new Material(diffuse) { name = "ironman_reactor_glow_off", color = new Color(0.5f, 0.56f, 0.62f), mainTexture = Tex("reactor_glow.png") };

            Diffuse = diffuse; SelfIllum = selfIllum ?? diffuse; Standard = standard;
        }

        public static Material GlowOff;

        // Standard-shader metals reflect the game's sky, which in the garage (and at night) comes out black, so every
        // reactor and the chuck carry a small reflection probe with a neutral workshop environment: bright ceiling
        // with a strip light, grey walls darkening down, dark floor (MODLOG gotcha 18: a Custom probe is what works).
        static Cubemap workshopRefl;
        public static ReflectionProbe WorkshopProbe(Transform parent, float size)
        {
            if (workshopRefl == null)
            {
                const int N = 32;
                workshopRefl = new Cubemap(N, TextureFormat.RGBA32, false) { name = "ironman_workshop_refl" };
                var faces = new[] { CubemapFace.PositiveX, CubemapFace.NegativeX, CubemapFace.PositiveY, CubemapFace.NegativeY, CubemapFace.PositiveZ, CubemapFace.NegativeZ };
                foreach (var f in faces)
                {
                    var px = new Color[N * N];
                    for (int y = 0; y < N; y++)
                        for (int x = 0; x < N; x++)
                        {
                            float v;
                            if (f == CubemapFace.PositiveY) v = Mathf.Abs(x - N / 2f) < N * 0.12f ? 1.5f : 0.9f;   // strip light
                            else if (f == CubemapFace.NegativeY) v = 0.25f;
                            else v = Mathf.Lerp(0.95f, 0.4f, y / (float)(N - 1));   // rows run top -> bottom
                            if ((f == CubemapFace.PositiveX || f == CubemapFace.NegativeZ) && Mathf.Abs(x - N * 0.35f) < 2f && y < N * 0.7f) v = 1.4f;
                            px[y * N + x] = new Color(v, v * 0.99f, v * 0.97f, 1f);
                        }
                    workshopRefl.SetPixels(px, f);
                }
                workshopRefl.Apply();
            }
            var go = new GameObject("workshop_probe");
            go.transform.SetParent(parent, false);
            var p = go.AddComponent<ReflectionProbe>();
            p.mode = UnityEngine.Rendering.ReflectionProbeMode.Custom;
            p.customBakedTexture = workshopRefl;
            p.size = Vector3.one * size;
            p.importance = 50;
            return p;
        }
        public static Shader Diffuse, SelfIllum, Standard;

        // A plain material for props (charger, parcel, pad), optionally textured.
        public static Material Mat(string name, Color c, string tex = null, Shader sh = null, float metallic = -1f, float gloss = -1f)
        {
            var m = new Material(sh ?? (metallic >= 0f ? Standard : Diffuse)) { name = "ironman_" + name, color = c };
            if (tex != null) m.mainTexture = Tex(tex, false, TextureWrapMode.Repeat);
            if (metallic >= 0f) Pbr(m, metallic, gloss < 0f ? 0.4f : gloss);
            return m;
        }

        // A box (a primitive cube without its collider) under parent, local centre and size in metres.
        public static GameObject Box(Transform parent, string name, Vector3 centre, Vector3 size, Material mat, Quaternion? rot = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            UnityEngine.Object.Destroy(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = centre;
            go.transform.localRotation = rot ?? Quaternion.identity;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        public static GameObject Cylinder(Transform parent, string name, Vector3 centre, float diameter, float height, Material mat, Quaternion? rot = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            UnityEngine.Object.Destroy(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = centre;
            go.transform.localRotation = rot ?? Quaternion.identity;
            go.transform.localScale = new Vector3(diameter, height * 0.5f, diameter);   // the primitive is 2 units tall
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        // A flat printed quad (texture on its +Z face... Unity's Quad faces -Z: it is seen looking along +Z).
        public static GameObject Quad(Transform parent, string name, Vector3 centre, Vector2 size, Material mat, Quaternion rot)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            UnityEngine.Object.Destroy(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = centre;
            go.transform.localRotation = rot;
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        // A part's mesh with one material class's submesh emptied (same submesh order, so material slots still line
        // up): the core housing before its palladium goes in is reactor_base.obj without "glowdisc".
        static readonly Dictionary<string, Mesh> without = new Dictionary<string, Mesh>();
        public static Mesh MeshWithout(string file, params string[] classes)
        {
            Mesh m;
            var key = file + "#" + string.Join("+", classes);
            if (without.TryGetValue(key, out m)) return m;
            var src = Mesh(file);
            var names = subNames[file];
            m = new Mesh { name = src.name + "_no_" + string.Join("_", classes), vertices = src.vertices, uv = src.uv, normals = src.normals, subMeshCount = src.subMeshCount };
            for (int i = 0; i < names.Length; i++) m.SetTriangles(System.Array.IndexOf(classes, names[i]) >= 0 ? new int[0] : src.GetTriangles(i), i);
            // bounds of the vertices still used (the dropped classes' vertices stay in the array, and would keep the box too big)
            var vs = src.vertices; bool any = false; var bnd = new Bounds();
            for (int i = 0; i < m.subMeshCount; i++) foreach (int t in m.GetTriangles(i)) { if (!any) { bnd = new Bounds(vs[t], Vector3.zero); any = true; } else bnd.Encapsulate(vs[t]); }
            if (any) m.bounds = bnd; else m.RecalculateBounds();
            return without[key] = m;
        }

        // The coil ring's copper windings as 10 separate blocks, so a bare light-guide ring shows each block as it is
        // wound. Blocks sit every 36 deg from 21.85 deg around the axis (measured from reactor_coil.obj); each block
        // mesh has its own vertices around its centroid, which is its pivot (CoilBlockCentre, coil space).
        public const int CoilBlocks = 10;
        public const float CoilBlock0 = 21.85f;
        static Mesh[] coilBlocks;
        public static Vector3[] CoilBlockCentre;
        public static Mesh CoilBlock(int i)
        {
            if (coilBlocks == null)
            {
                var src = Mesh(Reactor.Coil);
                var names = subNames[Reactor.Coil];
                var v = src.vertices; var uv = src.uv; var n = src.normals;
                var tris = new List<int>[CoilBlocks];
                for (int b = 0; b < CoilBlocks; b++) tris[b] = new List<int>();
                for (int s = 0; s < names.Length; s++)
                {
                    if (names[s] != "copper") continue;
                    var t = src.GetTriangles(s);
                    for (int k = 0; k < t.Length; k += 3)
                    {
                        var c = (v[t[k]] + v[t[k + 1]] + v[t[k + 2]]) / 3f;
                        float a = Mathf.Repeat(Mathf.Atan2(c.z, c.x) * Mathf.Rad2Deg - (CoilBlock0 - 18f), 360f);
                        int b = Mathf.Clamp(Mathf.FloorToInt(a / 36f), 0, CoilBlocks - 1);
                        tris[b].Add(t[k]); tris[b].Add(t[k + 1]); tris[b].Add(t[k + 2]);
                    }
                }
                coilBlocks = new Mesh[CoilBlocks];
                CoilBlockCentre = new Vector3[CoilBlocks];
                for (int b = 0; b < CoilBlocks; b++)
                {
                    var map = new Dictionary<int, int>();
                    var bv = new List<Vector3>(); var buv = new List<Vector2>(); var bn = new List<Vector3>();
                    var bt = new int[tris[b].Count];
                    var centre = Vector3.zero;
                    foreach (var idx in tris[b]) centre += v[idx];
                    if (tris[b].Count > 0) centre /= tris[b].Count;
                    for (int k = 0; k < bt.Length; k++)
                    {
                        int o = tris[b][k], ni;
                        if (!map.TryGetValue(o, out ni)) { ni = bv.Count; map[o] = ni; bv.Add(v[o] - centre); buv.Add(uv[o]); bn.Add(n[o]); }
                        bt[k] = ni;
                    }
                    var m = new Mesh { name = "coil_block" + b, vertices = bv.ToArray(), uv = buv.ToArray(), normals = bn.ToArray() };
                    m.triangles = bt;
                    m.RecalculateBounds();
                    coilBlocks[b] = m;
                    CoilBlockCentre[b] = centre;
                }
            }
            return coilBlocks[i];
        }

        // The glow classes (Reactor.Glow drives their state per reactor).
        public static bool IsCoreGlow(string cls) { return cls == "glowdisc" || cls == "mesh"; }

        public static string[] SubNames(string file) { Mesh(file); return subNames[file]; }

        static Material Pbr(Material m, float metallic, float gloss)
        {
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", gloss);
            return m;
        }

        public static Material ReactorMaterial(string cls)
        {
            Material m;
            return reactorMats.TryGetValue(cls, out m) ? m : ReactorHousing;
        }

        // A mesh object with one material per OBJ submesh, picked by the submesh's `usemtl` class name.
        public static GameObject ReactorPart(string file, Transform parent = null)
        {
            var mesh = Mesh(file);
            var go = MeshObject(Path.GetFileNameWithoutExtension(file), mesh, null, parent);
            var names = subNames[file];
            var mats = new Material[names.Length];
            for (int i = 0; i < names.Length; i++) mats[i] = ReactorMaterial(names[i]);
            go.GetComponent<MeshRenderer>().sharedMaterials = mats;
            return go;
        }

        public static Mesh Mesh(string file)
        {
            Mesh m;
            if (!meshes.TryGetValue(file, out m))
            {
                string[] names;
                meshes[file] = m = LoadObj(Path.Combine(dir, file), out names);
                subNames[file] = names;
            }
            return m;
        }

        public static Texture2D Tex(string file, bool linear = false, TextureWrapMode wrap = TextureWrapMode.Clamp)
        {
            Texture2D t;
            if (textures.TryGetValue(file, out t)) return t;
            t = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear);
            t.LoadImage(File.ReadAllBytes(Path.Combine(dir, file)));
            t.wrapMode = wrap;
            t.anisoLevel = 4;
            t.name = file;
            return textures[file] = t;
        }

        public static AudioClip Clip(string file)
        {
            AudioClip c;
            if (clips.TryGetValue(file, out c)) return c;
            return clips[file] = LoadWav(Path.Combine(dir, file));
        }

        public static Material FindMaterial(string name)
        {
            return Resources.FindObjectsOfTypeAll<Material>().FirstOrDefault(m => m.name == name);
        }

        public static Shader FindShader(string name)
        {
            var s = Shader.Find(name);
            return s != null ? s : Resources.FindObjectsOfTypeAll<Shader>().FirstOrDefault(x => x.name == name);
        }

        public static GameObject MeshObject(string name, Mesh mesh, Material mat, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        // OBJ already in Unity space: x right, y up, z forward, left-handed winding. Convert in the asset generator.
        // Each `usemtl` name becomes one submesh (faces with the same name are merged, in first-seen order); `names`
        // returns those names in submesh order. An OBJ with no usemtl is one submesh named "".
        static Mesh LoadObj(string path, out string[] names)
        {
            var v = new List<Vector3>();
            var vt = new List<Vector2>();
            var vn = new List<Vector3>();
            var outV = new List<Vector3>();
            var outT = new List<Vector2>();
            var outN = new List<Vector3>();
            var subs = new List<List<int>>();
            var subOrder = new List<string>();
            var tris = new List<int>();
            subs.Add(tris); subOrder.Add("");
            var cache = new Dictionary<string, int>();
            var ci = CultureInfo.InvariantCulture;
            foreach (var raw in File.ReadAllLines(path))
            {
                if (raw.Length < 2 || raw[0] == '#') continue;
                var p = raw.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                switch (p[0])
                {
                    case "v": v.Add(new Vector3(float.Parse(p[1], ci), float.Parse(p[2], ci), float.Parse(p[3], ci))); break;
                    case "vt": vt.Add(new Vector2(float.Parse(p[1], ci), float.Parse(p[2], ci))); break;
                    case "vn": vn.Add(new Vector3(float.Parse(p[1], ci), float.Parse(p[2], ci), float.Parse(p[3], ci))); break;
                    case "usemtl":
                        {
                            var nm = p.Length > 1 ? p[1] : "";
                            int si = subOrder.IndexOf(nm);
                            if (si < 0) { si = subOrder.Count; subOrder.Add(nm); subs.Add(new List<int>()); }
                            tris = subs[si];
                            break;
                        }
                    case "f":
                        var idx = new int[p.Length - 1];
                        for (int i = 1; i < p.Length; i++)
                        {
                            int id;
                            if (!cache.TryGetValue(p[i], out id))
                            {
                                var s = p[i].Split('/');
                                id = outV.Count;
                                outV.Add(v[int.Parse(s[0]) - 1]);
                                outT.Add(s.Length > 1 && s[1].Length > 0 ? vt[int.Parse(s[1]) - 1] : Vector2.zero);
                                outN.Add(s.Length > 2 && s[2].Length > 0 ? vn[int.Parse(s[2]) - 1] : Vector3.up);
                                cache[p[i]] = id;
                            }
                            idx[i - 1] = id;
                        }
                        for (int i = 1; i + 1 < idx.Length; i++) { tris.Add(idx[0]); tris.Add(idx[i]); tris.Add(idx[i + 1]); }
                        break;
                }
            }
            var mesh = new Mesh { name = Path.GetFileNameWithoutExtension(path) };
            if (outV.Count > 65000) Debug.LogWarning("IronManSuit: " + path + " has " + outV.Count + " vertices");
            mesh.vertices = outV.ToArray();
            mesh.uv = outT.ToArray();
            mesh.normals = outN.ToArray();
            // drop empty submeshes (the implicit "" one when every face sits under a usemtl)
            var keep = new List<int>();
            for (int i = 0; i < subs.Count; i++) if (subs[i].Count > 0) keep.Add(i);
            mesh.subMeshCount = keep.Count;
            names = new string[keep.Count];
            for (int i = 0; i < keep.Count; i++) { mesh.SetTriangles(subs[keep[i]].ToArray(), i); names[i] = subOrder[keep[i]]; }
            mesh.RecalculateBounds();
            return mesh;
        }

        // 16-bit PCM WAV, mono or stereo.
        static AudioClip LoadWav(string path)
        {
            var b = File.ReadAllBytes(path);
            int ch = 1, rate = 44100, bits = 16, pos = 12, dataAt = -1, dataLen = 0;
            while (pos + 8 <= b.Length)
            {
                var id = System.Text.Encoding.ASCII.GetString(b, pos, 4);
                int len = BitConverter.ToInt32(b, pos + 4);
                if (id == "fmt ") { ch = BitConverter.ToInt16(b, pos + 10); rate = BitConverter.ToInt32(b, pos + 12); bits = BitConverter.ToInt16(b, pos + 22); }
                else if (id == "data") { dataAt = pos + 8; dataLen = Math.Min(len, b.Length - dataAt); break; }
                pos += 8 + len + (len & 1);
            }
            if (dataAt < 0 || bits != 16) throw new Exception("unsupported wav " + path);
            int n = dataLen / 2;
            var d = new float[n];
            for (int i = 0; i < n; i++) d[i] = BitConverter.ToInt16(b, dataAt + i * 2) / 32768f;
            var clip = AudioClip.Create(Path.GetFileNameWithoutExtension(path), n / ch, ch, rate, false);
            clip.SetData(d, 0);
            return clip;
        }
    }
}
