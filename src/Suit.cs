using System.Collections.Generic;
using MSCLoader;
using UnityEngine;

namespace IronManSuit
{
    // What one suit piece is doing (saved): on the hoist, its mount bolts' tightness, its solder joints (0 bare,
    // 1 cold, 2 good) and how far its plate has been hammered on (0..1).
    public class PieceState
    {
        public string Id;
        public int[] Bolts;
        public int[] Joints;
        public float Plate;
    }

    // The suits (Phase 3, and the Mk3 of Phase 5): ten pieces each, in five regions, built on the hoist like the reactor
    // was built in the chuck. Two sets share the code: the cave Mk1 (ids "m1_helmet", "m1_chest", ...; meshes
    // suit1_*, from tools/suit/build_suit.py) and the red and gold Mk3 (ids "m3_helmet", ...; tools/suit/build_mk3.py). Unity space,
    // the body facing +Z, each piece's origin its joint on the body; the feet are at y 0. Each piece is a frame from the
    // catalogue; on the hoist it is bolted on with the player's spanner, its wiring soldered with the iron, and (most
    // pieces) a plate of scrap sheet steel hammered on. One suit at a time is on the hoist.
    static class Suit
    {
        public class PieceDef
        {
            public string Id, Name, File;
            public int Variant = 1;       // 1: the cave Mk1, 3: the Mk3
            public string Role, Slot;     // helmet, chest, pauldron, gauntlet, thigh, boot; the slot also has the side ("boot_r")
            public Vector3 Socket;        // the chest: where the arc reactor sits in it (chest space)
            public int Region;            // 0 head, 1 forearms, 2 shoulders and chest, 3 thighs, 4 boots (Phase 3.1-3.5)
            public Vector3 Joint;         // on the body (feet origin, facing +Z)
            public int BoltMm;
            public Vector3[] BoltPos, BoltDir;   // piece space
            public Vector3[] JointPos;           // piece space
            public bool Plated;           // needs a sheet of steel hammered on
            public bool Faceplate;        // the Mk3 helmet: File is its shell, and it carries a Faceplate (Mk3.cs) that opens
            public bool Hollow;           // the Mk3 but its chest: the armour closes it, so its bare frame is gone once the plate is on (all but its lights)
            public float Mass;
            public string Item { get { return "suit_" + Id; } }
        }

        public static readonly List<PieceDef> Pieces = new List<PieceDef>();
        public static readonly Dictionary<string, PieceDef> ById = new Dictionary<string, PieceDef>();
        public const string SheetId = "sheet_steel";
        public static readonly string[] RegionNames = { "HEAD", "FOREARMS", "SHOULDERS AND CHEST", "THIGHS", "BOOTS" };

        static int variant = 1;
        static string prefix = "m1_";
        static void Def(string id, string name, string file, int region, Vector3 joint, int mm, Vector3[] bolts, Vector3[] dirs, Vector3[] joints, bool plated, float mass, bool pair)
        {
            var d = new PieceDef { Id = prefix + id, Variant = variant, Role = id, Slot = id, Name = name, File = file, Region = region, Joint = joint, BoltMm = mm, BoltPos = bolts, BoltDir = dirs, JointPos = joints, Plated = plated, Mass = mass, Socket = Socket1 };
            if (!pair) { Add(d); return; }
            Add(new PieceDef { Id = prefix + id + "_r", Variant = variant, Role = id, Slot = id + "_r", Name = "RIGHT " + name, File = file + "_r.obj", Region = region, Joint = joint, BoltMm = mm, BoltPos = bolts, BoltDir = dirs, JointPos = joints, Plated = plated, Mass = mass });
            Add(new PieceDef { Id = prefix + id + "_l", Variant = variant, Role = id, Slot = id + "_l", Name = "LEFT " + name, File = file + "_l.obj", Region = region, Joint = Mx(joint), BoltMm = mm, BoltPos = Mx(bolts), BoltDir = Mx(dirs), JointPos = Mx(joints), Plated = plated, Mass = mass });
        }
        static void Add(PieceDef d) { Pieces.Add(d); ById[d.Id] = d; }
        static Vector3 Mx(Vector3 v) { return new Vector3(-v.x, v.y, v.z); }
        static Vector3[] Mx(Vector3[] a) { var r = new Vector3[a.Length]; for (int i = 0; i < a.Length; i++) r[i] = Mx(a[i]); return r; }
        static Vector3 V(float x, float y, float z) { return new Vector3(x, y, z); }

        // The arc reactor's seat in the Mk1 chest (88 mm across, 54 mm deep; its front, local +Y, out of the chest, +Z)
        // and in the Mk3's (the modelled reactor opening).
        public static readonly Vector3 Socket1 = new Vector3(0f, 0.467f, 0.181f), Socket3 = new Vector3(0f, 0.4511f, 0.080f);
        // The Mk3's diffuser plate (suit3_diffuser.obj, tools/suit/mk3_diffuser.py): the frosted lens that clips into the
        // chest's reactor slot over the reactor. Its place in the chest's space (its lens plane 13 mm inside the slot).
        public const string DiffuserFile = "suit3_diffuser.obj";
        public static readonly Vector3 DiffuserAt = new Vector3(0f, 0.4511f, 0.160f);
        // The right palm in the right forearm's space: where the repulsor beam starts.
        public static readonly Vector3 Palm1 = new Vector3(-0.03f, -0.312f, 0.004f), Palm3 = new Vector3(-0.03f, -0.337f, 0.04f);

        static Suit()
        {
            var back = V(0f, 0f, -1f);
            var backOut = new Vector3(0.7f, 0f, -0.7f).normalized;
            // Bolts sit where the hoist's jig meets each piece (mostly at its back); joints where its wiring ends.
            Def("helmet", "MK1 HELMET", "suit1_helmet.obj", 0, V(0f, 1.62f, -0.02f), 8,
                new[] { V(0.03f, 0.08f, -0.112f), V(-0.03f, 0.08f, -0.112f) }, new[] { back, back },
                new[] { V(0.094f, 0.06f, -0.01f), V(-0.094f, 0.06f, -0.01f) }, true, 3f, false);
            Def("chest", "MK1 CHEST PLATE", "suit1_chest.obj", 2, V(0f, 1.00f, 0f), 12,
                new[] { V(0.05f, 0.50f, -0.234f), V(-0.05f, 0.50f, -0.234f) }, new[] { back, back },
                new[] { V(0.053f, 0.52f, 0.206f), V(-0.053f, 0.52f, 0.206f), V(0.053f, 0.415f, 0.245f), V(-0.053f, 0.415f, 0.245f) }, true, 9f, false);
            Def("pauldron", "MK1 SHOULDER", "suit1_pauldron", 2, V(0.27f, 1.56f, 0f), 10,
                new[] { V(0.0f, 0.04f, -0.105f), V(0.035f, 0.027f, -0.100f) }, new[] { back, back },
                new[] { V(-0.022f, -0.063f, 0.097f), V(0.06f, -0.20f, 0.082f) }, true, 3.5f, true);
            Def("gauntlet", "MK1 FOREARM", "suit1_gauntlet", 1, V(0.33f, 1.27f, 0f), 8,
                new[] { V(0.0f, -0.037f, -0.071f), V(0.045f, -0.037f, -0.052f) }, new[] { back, backOut },
                new[] { V(0.0f, -0.237f, 0.058f), V(0.04f, -0.237f, 0.043f), V(0.057f, -0.237f, 0.0f) }, false, 2.5f, true);
            Def("thigh", "MK1 THIGH", "suit1_thigh", 3, V(0.17f, 0.95f, 0f), 10,
                new[] { V(0.0f, -0.03f, -0.127f), V(0.045f, -0.03f, -0.118f) }, new[] { back, backOut },
                new[] { V(0.125f, 0.08f, 0.02f) }, true, 4f, true);
            Def("boot", "MK1 BOOT", "suit1_boot", 4, V(0.17f, 0.56f, 0f), 10,
                new[] { V(0.005f, -0.06f, -0.104f), V(0.05f, -0.06f, -0.096f) }, new[] { back, backOut },
                new[] { V(0.135f, -0.20f, 0.02f), V(0.135f, -0.25f, 0.02f), V(0.125f, -0.41f, 0.0f) }, true, 5f, true);

            // The Mk3: the same slots, joints and mounts on the hoist's jig (body.py), its own meshes (suit3_*). The armour
            // covers every piece, so the forearms take a plate too. Solder joints are the ends of each frame's wiring.
            variant = 3; prefix = "m3_";
            Def("helmet", "MK3 HELMET", Faceplate.ShellFile, 0, V(0f, 1.62f, -0.02f), 8,
                new[] { V(0.03f, 0.08f, -0.112f), V(-0.03f, 0.08f, -0.112f) }, new[] { back, back },
                new[] { V(0.02f, 0.145f, -0.08f), V(-0.03f, 0.145f, -0.08f) }, true, 3f, false);
            ById["m3_helmet"].Faceplate = ById["m3_helmet"].Hollow = true;
            Def("chest", "MK3 CHEST PLATE", "suit3_chest.obj", 2, V(0f, 1.00f, 0f), 12,
                new[] { V(0.05f, 0.50f, -0.234f), V(-0.05f, 0.50f, -0.234f) }, new[] { back, back },
                new[] { V(0.058f, 0.440f, 0.119f), V(-0.058f, 0.440f, 0.119f), V(0.133f, 0.480f, 0.015f), V(-0.133f, 0.480f, 0.015f) }, true, 9f, false);
            Def("pauldron", "MK3 SHOULDER", "suit3_pauldron", 2, V(0.27f, 1.56f, 0f), 10,
                new[] { V(0.0f, 0.04f, -0.105f), V(0.035f, 0.027f, -0.100f) }, new[] { back, back },
                new[] { V(-0.048f, 0.000f, -0.044f), V(-0.027f, -0.180f, -0.036f) }, true, 3.5f, true);
            Def("gauntlet", "MK3 FOREARM", "suit3_gauntlet", 1, V(0.33f, 1.27f, 0f), 8,
                new[] { V(0.0f, -0.037f, -0.071f), V(0.045f, -0.037f, -0.052f) }, new[] { back, backOut },
                new[] { V(-0.021f, -0.237f, 0.068f), V(0.005f, -0.237f, 0.054f), V(0.016f, -0.237f, 0.013f) }, true, 2.5f, true);
            Def("thigh", "MK3 THIGH", "suit3_thigh", 3, V(0.17f, 0.95f, 0f), 10,
                new[] { V(0.0f, -0.03f, -0.127f), V(0.045f, -0.03f, -0.118f) }, new[] { back, backOut },
                new[] { V(-0.060f, -0.100f, 0.075f) }, true, 4f, true);
            Def("boot", "MK3 BOOT", "suit3_boot", 4, V(0.17f, 0.56f, 0f), 10,
                new[] { V(0.005f, -0.06f, -0.104f), V(0.05f, -0.06f, -0.096f) }, new[] { back, backOut },
                new[] { V(-0.054f, -0.240f, -0.097f), V(-0.036f, -0.360f, -0.063f), V(0.015f, -0.420f, 0.014f) }, true, 5f, true);
            // The modelled armour closes the limbs too, and the frames under it were drawn for other outlines (the forearm's is
            // the Mk1's, glove and all): they came through at the fingers, wrists, elbows and shins. The chest keeps its frame,
            // seen at the waist and the armholes (build_mk3.py tucker()).
            foreach (var d in Pieces) if (d.Variant == 3) { d.Socket = Socket3; if (d.Role != "chest") d.Hollow = true; }
        }

        // The pieces of one suit (variant 1 or 3).
        public static List<PieceDef> Set(int v)
        {
            var r = new List<PieceDef>();
            foreach (var d in Pieces) if (d.Variant == v) r.Add(d);
            return r;
        }
        public static int VariantOf(string id) { var d = Of(id); return d != null ? d.Variant : 1; }
        public static string SuitName(int v) { return v == 3 ? "MK3" : "MK1"; }

        public static PieceDef Of(string id) { PieceDef d; return id != null && ById.TryGetValue(id, out d) ? d : null; }
        // Saves from before the Mk1 had its own prefix: its pieces were "helmet", "pauldron_r"... and its items "suit_helmet"...
        // Maps one id or item kind, or a "kind:qty,kind:qty" list, to the "m1_" ids; ids already mapped (and the Mk3's) stay.
        static readonly string[] OldRoles = { "helmet", "chest", "pauldron", "gauntlet", "thigh", "boot" };
        static string Mk1Key(string k)
        {
            string bare = k.StartsWith("suit_") ? k.Substring(5) : k, side = bare.EndsWith("_r") || bare.EndsWith("_l") ? bare.Substring(bare.Length - 2) : "";
            if (System.Array.IndexOf(OldRoles, side.Length > 0 ? bare.Substring(0, bare.Length - 2) : bare) < 0) return k;
            return (k.StartsWith("suit_") ? "suit_m1_" : "m1_") + bare;
        }
        public static string Mk1Ids(string list)
        {
            if (string.IsNullOrEmpty(list)) return list;
            var parts = list.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                int c = parts[i].IndexOf(':');
                parts[i] = c < 0 ? Mk1Key(parts[i]) : Mk1Key(parts[i].Substring(0, c)) + parts[i].Substring(c);
            }
            return string.Join(",", parts);
        }
        public static PieceDef OfItem(string kind) { return kind != null && kind.StartsWith("suit_") ? Of(kind.Substring(5)) : null; }

        // ------------------------------------------------------------------------------------------------------------
        // Materials, one per class written by build_suit.py
        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
        public static Material GlowOn, GlowOff, LensOn;

        public static void InitMaterials()
        {
            mats.Clear();
            // scrap sheet steel: the loose sheet, and a plate laid on but not yet hammered to shape
            mats["sheet"] = Res.Mat("sheet_steel", new Color(0.62f, 0.58f, 0.54f), "suit_plate.png", null, 0.55f, 0.3f);
            // (in game the dents at 0.8 / gloss 0.52 caught the strip light as bright streaks: toned down)
            var plate = Res.Mat("suit_plate", new Color(0.84f, 0.85f, 0.88f), "suit_plate.png", null, 0.7f, 0.4f);
            plate.mainTextureScale = new Vector2(0.6f, 0.6f);
            if (plate.HasProperty("_BumpMap"))
            {
                plate.SetTexture("_BumpMap", Res.Tex("suit_plate_n.png", true, TextureWrapMode.Repeat));
                plate.SetFloat("_BumpScale", 0.35f);
                plate.EnableKeyword("_NORMALMAP");
            }
            mats["plate"] = plate;
            // the Mk3's armour (tools/suit/mk3_plates.py): red and gold enamel, dark joints, a steel back plate
            mats["plate_red"] = Res.Mat("mk3_red", new Color(0.55f, 0.04f, 0.05f), null, null, 0.6f, 0.78f);
            mats["plate_gold"] = Res.Mat("mk3_gold", new Color(0.93f, 0.71f, 0.27f), null, null, 0.6f, 0.7f);
            mats["plate_dark"] = Res.Mat("mk3_dark", new Color(0.035f, 0.035f, 0.04f), null, null, 0.3f, 0.35f);
            mats["plate_steel"] = Res.Mat("mk3_steel", new Color(0.72f, 0.74f, 0.78f), "reactor_steel_albedo.png", null, 0.85f, 0.55f);
            mats["frame"] = Res.Mat("suit_frame", new Color(0.78f, 0.79f, 0.82f), "reactor_steel_albedo.png", null, 0.85f, 0.55f);
            mats["dark"] = Res.Mat("suit_dark", new Color(0.25f, 0.26f, 0.28f), "reactor_steel_albedo.png", null, 0.7f, 0.4f);
            mats["neck"] = mats["dark"];   // the helmet's neck ring: same steel, its own class so a loose helmet can drop it
            mats["chrome"] = Res.Mat("suit_chrome", new Color(0.95f, 0.95f, 0.97f), null, null, 1f, 0.85f);
            mats["brass"] = Res.ReactorMaterial("brass");
            mats["copper"] = Res.Mat("suit_copper", new Color(0.86f, 0.5f, 0.3f), null, null, 0.9f, 0.6f);
            mats["black"] = Res.Mat("suit_black", new Color(0.06f, 0.06f, 0.065f), null, null, 0f, 0.3f);
            mats["skin"] = Res.Mat("suit_skin", new Color(0.82f, 0.62f, 0.5f), null, null, 0f, 0.25f);
            mats["ribbon"] = Res.Mat("suit_ribbon", new Color(0.72f, 0.08f, 0.06f), null, null, 0f, 0.55f);
            mats["w_red"] = Res.Mat("suit_w_red", new Color(0.7f, 0.08f, 0.06f), null, null, 0f, 0.5f);
            mats["w_yel"] = Res.Mat("suit_w_yel", new Color(0.85f, 0.72f, 0.1f), null, null, 0f, 0.5f);
            mats["w_blu"] = Res.Mat("suit_w_blu", new Color(0.12f, 0.28f, 0.75f), null, null, 0f, 0.5f);
            mats["w_grn"] = Res.Mat("suit_w_grn", new Color(0.12f, 0.55f, 0.18f), null, null, 0f, 0.5f);
            mats["w_wht"] = Res.Mat("suit_w_wht", new Color(0.85f, 0.85f, 0.85f), null, null, 0f, 0.5f);
            // Honeycomb: a cut-out shader shows the dark lining through its cells; opaque if the game lacks one.
            var cut = Res.FindShader("Legacy Shaders/Transparent/Cutout/Diffuse") ?? Res.FindShader("Transparent/Cutout/Diffuse");
            var honey = new Material(cut ?? Res.Diffuse) { name = "ironman_suit_honey", color = new Color(0.55f, 0.55f, 0.58f), mainTexture = Res.Tex("suit_honey.png", false, TextureWrapMode.Repeat) };
            honey.mainTextureScale = new Vector2(1.6f, 1.6f);
            if (cut != null && honey.HasProperty("_Cutoff")) honey.SetFloat("_Cutoff", 0.5f);
            mats["honey"] = honey;
            // the same grille at the diffuser hub's size (its cells a quarter as big; the diffuser's UVs are planar,
            // 0..1 over 90 mm: build_mk3.py DIFF_UV)
            mats["honey_fine"] = new Material(honey) { name = "ironman_suit_honey_fine", mainTextureScale = new Vector2(4.8f, 4.8f) };
            Debug.Log("IronManSuit: honeycomb shader " + honey.shader.name);
            // Eye slit, palm repulsors and boot thrusters: the reactor's blue, lit only while the suit is powered.
            GlowOn = new Material(Res.FindShader("Unlit/Texture") ?? Res.SelfIllum) { name = "ironman_suit_glow", color = new Color(0.45f, 0.85f, 1f), mainTexture = Res.Tex("reactor_glow.png") };
            GlowOff = Res.Mat("suit_glow_off", new Color(0.16f, 0.2f, 0.23f), null, null, 0.2f, 0.7f);
            mats["glow"] = mats["plate_glow"] = GlowOff;
            // The Mk3 diffuser: frosted glass, lit by the reactor behind it while the suit is powered; its smoked rim.
            // suit_lens.png is the plate head-on (planar UVs): the reactor's segments and the hub's shadow on the glass.
            // Unlit, it's tinted grey-blue and glassy (a matte lens took the garage's warm light as yellow-green); lit,
            // it shows as drawn (its own pale cyan).
            mats["lens"] = Res.Mat("mk3_lens", new Color(0.52f, 0.62f, 0.78f), "suit_lens.png", null, 0.3f, 0.82f);
            mats["lens_rim"] = Res.Mat("mk3_lens_rim", new Color(0.2f, 0.22f, 0.25f), null, null, 0.3f, 0.85f);
            LensOn = new Material(GlowOn.shader) { name = "ironman_lens_on", color = Color.white, mainTexture = Res.Tex("suit_lens.png") };
        }

        // The diffuser plate's materials: its lens lit or not.
        public static void PaintDiffuser(GameObject go, bool lit)
        {
            var names = Res.SubNames(DiffuserFile);
            var ms = new Material[names.Length];
            for (int i = 0; i < names.Length; i++) ms[i] = names[i] == "lens" && lit ? LensOn : Mat(names[i]);
            go.GetComponent<MeshRenderer>().sharedMaterials = ms;
        }

        public static Material Mat(string cls) { Material m; return mats.TryGetValue(cls, out m) ? m : mats["frame"]; }

        // A piece's mesh object: one material per submesh class. plate: 0 = bare frame (no plate), 0..1 = a sheet laid
        // on and being hammered to shape (raw sheet steel), 1 = finished armour.
        // mounts: draw the Mk3's jig studs (class "mount"): only on the hoist and on a loose piece.
        public static GameObject PieceObject(PieceDef d, Transform parent, float plate, bool mounts = true)
        {
            var go = Res.MeshObject(d.Id, Res.Mesh(d.File), null, parent);
            if (d.Faceplate) Faceplate.Attach(go);
            ShowPlate(go, d, plate, mounts);
            return go;
        }
        public static GameObject PieceObject(PieceDef d, Transform parent, bool plated) { return PieceObject(d, parent, plated ? 1f : 0f); }

        // Armour classes: "plate" (the Mk1's) and "plate_*" (the Mk3's colours).
        public static bool IsPlate(string cls) { return cls.StartsWith("plate"); }
        // A mesh's bare frame: every class that isn't armour or a jig stud.
        public static string[] FrameClasses(string file)
        {
            var r = new List<string>();
            foreach (var n in Res.SubNames(file)) if (!IsPlate(n) && n != "mount") r.Add(n);
            return r.ToArray();
        }
        // The classes of a piece's mesh that aren't drawn at this stage: the armour until a plate is laid on, the bare
        // frame of a hollow piece once one is (its lights stay: the palm repulsor, the sole thruster), the jig studs unless asked for, and whatever else the caller names.
        static string[] Hidden(string file, PieceDef d, float plate, bool mounts, params string[] more)
        {
            bool plated = !d.Plated || plate > 0f;
            var r = new List<string>(more);
            if (!mounts) r.Add("mount");
            foreach (var n in Res.SubNames(file))
                if (n != "mount" && !r.Contains(n) && (IsPlate(n) ? !plated : d.Hollow && plated && !IsGlow(n))) r.Add(n);
            return r.ToArray();
        }

        public static void ShowPlate(GameObject go, PieceDef d, float plate, bool mounts = true)
        {
            if (go == null) return;
            foreach (var m in Meshes(go, d)) ShowPlate(m.Key, m.Value, d, plate, mounts);
        }
        static void ShowPlate(GameObject go, string file, PieceDef d, float plate, bool mounts)
        {
            var hide = Hidden(file, d, plate, mounts);
            go.GetComponent<MeshFilter>().sharedMesh = hide.Length == 0 ? Res.Mesh(file) : Res.MeshWithout(file, hide);
            var r = go.GetComponent<MeshRenderer>();
            var names = Res.SubNames(file);
            var ms = r.sharedMaterials.Length == names.Length ? r.sharedMaterials : new Material[names.Length];
            for (int i = 0; i < names.Length; i++)
                if (IsPlate(names[i])) ms[i] = d.Plated && plate < 1f ? Mat("sheet") : Mat(names[i]);
                else if (ms[i] == null) ms[i] = Mat(names[i]);
            r.sharedMaterials = ms;
        }

        // A piece's mesh objects with their files: the piece itself and, on the Mk3 helmet, its faceplate and buttons.
        public static IEnumerable<KeyValuePair<GameObject, string>> Meshes(GameObject go, PieceDef d) { return Faceplate.Meshes(go, d.File); }

        // The glow classes (eye lenses, palms, soles) of a piece lit or dark.
        public static void PaintGlow(GameObject go, PieceDef d, bool lit)
        {
            foreach (var m in Meshes(go, d))
            {
                var r = m.Key.GetComponent<MeshRenderer>();
                var names = Res.SubNames(m.Value);
                var ms = r.sharedMaterials;
                for (int i = 0; i < names.Length; i++) if (IsGlow(names[i])) ms[i] = lit ? GlowOn : GlowOff;
                r.sharedMaterials = ms;
            }
        }

        // A helmet off the suit stands on its own, for display: no dark neck ring under it, and its eyes lit or dark by
        // right-click (kept as a ";lit" tag on the item's Extra, which the piece state ignores). The Mk3's also keeps
        // its faceplate up or down (";open"; the release buttons on its sides, Mk3.cs Faceplate).
        public static bool IsGlow(string cls) { return cls == "glow" || cls == "plate_glow"; }
        public const string EyesTag = ";lit", OpenTag = ";open";
        public static bool EyesLit(string extra) { return extra != null && extra.Contains(EyesTag); }
        public static bool FaceOpen(string extra) { return extra != null && extra.Contains(OpenTag); }
        // A hollow helmet's eyes are its faceplate's lenses: a bare frame has none to switch.
        static bool HasEyes(PieceDef d, PieceState s) { return d.Role == "helmet" && (!d.Hollow || s.Plate > 0f); }
        static string SetTag(string extra, string tag, bool on) { return (extra ?? "").Replace(tag, "") + (on ? tag : ""); }
        static void LooseHelmet(GameObject o, PieceDef d, float plate, bool lit)
        {
            ShowPlate(o, d, plate);
            o.GetComponent<MeshFilter>().sharedMesh = Res.MeshWithout(d.File, Hidden(d.File, d, plate, true, "neck"));
            PaintGlow(o, d, lit);
        }
        // The release button of a finished loose helmet under the view (null: no faceplate, or not looking at a button).
        static Faceplate ButtonLooked(Items.Item it, PieceDef d)
        {
            if (!d.Faceplate || !Finished(d, Parse(d, it.Extra))) return null;
            var fp = it.Go.GetComponentInChildren<Faceplate>();
            return fp != null && fp.LookingAtButton() ? fp : null;
        }

        // ------------------------------------------------------------------------------------------------------------
        // Piece state, as carried in a piece item's Extra: "<joint digits>;<plate>" (e.g. "221;0.5").
        public static PieceState Parse(PieceDef d, string extra)
        {
            var s = new PieceState { Id = d.Id, Bolts = new int[d.BoltPos.Length], Joints = new int[d.JointPos.Length] };
            if (string.IsNullOrEmpty(extra)) return s;
            var p = extra.Split(';');
            for (int i = 0; i < s.Joints.Length && i < p[0].Length; i++) s.Joints[i] = Mathf.Clamp(p[0][i] - '0', 0, 2);
            if (p.Length > 1) float.TryParse(p[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out s.Plate);
            s.Plate = Mathf.Clamp01(s.Plate);
            return s;
        }

        public static string Extra(PieceState s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var j in s.Joints) sb.Append(j);
            return sb + ";" + s.Plate.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }

        public static bool Soldered(PieceState s) { foreach (var j in s.Joints) if (j != 2) return false; return true; }
        public static bool Finished(PieceDef d, PieceState s) { return Soldered(s) && (!d.Plated || s.Plate >= 1f); }

        public static PieceState Done(PieceDef d)
        {
            var s = new PieceState { Id = d.Id, Bolts = new int[d.BoltPos.Length], Joints = new int[d.JointPos.Length], Plate = 1f };
            for (int i = 0; i < s.Joints.Length; i++) s.Joints[i] = 2;
            for (int i = 0; i < s.Bolts.Length; i++) s.Bolts[i] = Bolt.Steps;
            return s;
        }

        // ------------------------------------------------------------------------------------------------------------
        // Items: a piece (frame from the catalogue, or finished and taken off the hoist) and scrap sheet steel.
        public static void InitItems()
        {
            foreach (var d in Pieces)
            {
                var def = d;
                Items.Add(new Items.Kind
                {
                    Id = def.Item, Name = def.Name.ToLowerInvariant() + "(itemx)", Mass = def.Mass, AngularDrag = 2f,
                    Build = (go, extra) =>
                    {
                        var s = Parse(def, extra);
                        var o = PieceObject(def, go.transform, s.Plate);
                        o.transform.localPosition = Vector3.zero;
                        if (def.Role == "helmet") LooseHelmet(o, def, s.Plate, EyesLit(extra));
                        if (def.Faceplate) o.GetComponent<Faceplate>().Snap(FaceOpen(extra));
                        return Items.Fit(go);
                    },
                    Prompt = it =>
                    {
                        var s = Parse(def, it.Extra);
                        var fp = ButtonLooked(it, def);
                        if (fp != null) return fp.Prompt();
                        if (HasEyes(def, s)) return def.Name + " - EYES: " + (EyesLit(it.Extra) ? "ON" : "OFF");
                        if (Finished(def, s)) return def.Name + " - USE TO WEAR IT";
                        return def.Name + " FRAME - BUILD IT ON THE HOIST";
                    },
                    Alt = def.Role != "helmet" ? (System.Action<Items.Item>)null : it =>
                    {
                        if (!HasEyes(def, Parse(def, it.Extra))) return;
                        bool on = !EyesLit(it.Extra);
                        it.Extra = SetTag(it.Extra, EyesTag, on);
                        LooseHelmet(it.Go.transform.Find(def.Id).gameObject, def, Parse(def, it.Extra).Plate, on);
                        G.Sound("CarBuilding", "assemble", it.Go.transform, 0.4f);
                    },
                    Use = it =>
                    {
                        var fp = ButtonLooked(it, def);
                        if (fp != null) { fp.Press(); it.Extra = SetTag(it.Extra, OpenTag, fp.IsOpen); return false; }
                        if (!Finished(def, Parse(def, it.Extra))) { G.Flash("FINISH IT ON THE HOIST FIRST", 2f); return false; }
                        return Wear.PutOn(def, it);
                    },
                });
            }
            var sheetMat = Mat("sheet");
            Items.Add(new Items.Kind
            {
                Id = SheetId, Name = "sheet steel(itemx)", Mass = 3f, AngularDrag = 2f,
                Build = (go, x) =>
                {
                    Res.Box(go.transform, "sheet", new Vector3(0f, 0.0015f, 0f), new Vector3(0.5f, 0.003f, 0.36f), sheetMat);
                    Res.Box(go.transform, "bent_edge", new Vector3(0f, 0.012f, 0.178f), new Vector3(0.5f, 0.024f, 0.004f), sheetMat, Quaternion.Euler(-12f, 0f, 0f));
                    return Items.Fit(go);
                },
                Prompt = it => G.InHand(it.Go.transform) ? null : "SCRAP SHEET STEEL",
            });
        }

        // Scrap sheet steel at the landfill, once the hoist has come: a stack of sheets beside where the converter lay.
        // Topped up on load (and when a parcel of pieces is opened) to what the unplated pieces still need, for the Mk1
        // and, once any Mk3 piece is owned, the Mk3 (a Mk3 only player is not given the Mk1's sheets), so a lost sheet
        // costs time, never the build.
        public static int SheetsFor(int v) { int n = 0; foreach (var d in Pieces) if (d.Variant == v && d.Plated) n++; return n; }
        public static void PlaceSheets()
        {
            var d = IronManSuit.Data;
            if (!d.HoistGiven) return;
            int have = 0;
            var done = new Dictionary<int, int> { { 1, 0 }, { 3, 0 } };
            var owns = new Dictionary<int, bool> { { 1, false }, { 3, false } };
            foreach (var it in Items.Instance.Live)
            {
                if (it.Kind.Id == SheetId) have++;
                var pd = OfItem(it.Kind.Id);
                if (pd == null) continue;
                owns[pd.Variant] = true;
                if (pd.Plated && Parse(pd, it.Extra).Plate >= 1f) done[pd.Variant]++;
            }
            foreach (var s in d.Hoist) { var pd = Of(s.Id); if (pd == null) continue; owns[pd.Variant] = true; if (pd.Plated && s.Plate >= 1f) done[pd.Variant]++; }
            foreach (var id in Wear.WornIds()) { var pd = Of(id); if (pd == null) continue; owns[pd.Variant] = true; if (pd.Plated) done[pd.Variant]++; }
            if (d.SuitActive || d.InSuit) { int v = ActiveVariant(d); owns[v] = true; done[v] = SheetsFor(v); }
            int want = -have;
            foreach (int v in new[] { 1, 3 })
                if (v == 1 ? (owns[1] || !owns[3]) : owns[3]) want += SheetsFor(v) - done[v];
            if (want <= 0) { d.SheetsPlaced = true; return; }
            var sp = G.Find("LANDFILL/LandfillSpawn");
            var p = (sp != null ? sp.position : new Vector3(-780.118f, 13.322f, -644.396f)) + new Vector3(2.2f, 0.4f, 1.5f);
            for (int i = 0; i < want; i++)
                Items.Instance.Spawn(SheetId, p + Vector3.up * (0.05f * (i % 12)) + new Vector3(0.02f * (i % 3) + 0.5f * (i / 12), 0f, 0f), Quaternion.Euler(0f, 20f + 7f * i, 0f));
            d.SheetsPlaced = true;
            ModConsole.Log("IronManSuit: " + want + " sheet(s) of scrap steel at the landfill");
        }

        // Which suit is on the hoist, standing or worn (saved with the suit being built; the Mk1 for older saves).
        public static int ActiveVariant(SaveData d) { return d.SuitVariant == 3 ? 3 : 1; }
    }

    // A set of suit pieces shown together in body space (feet origin, facing +Z): on the hoist, standing, or worn. It
    // also carries the arc reactor in the chest, and lights the glow class (eye slit, palms, soles) when powered.
    class SuitBody : MonoBehaviour
    {
        public readonly Dictionary<string, GameObject> Parts = new Dictionary<string, GameObject>();
        GameObject reactor, diffuser;
        bool reactorLit;
        Glow reactorGlow;
        float glow;
        public float ThrustGlow;   // boots' soles (flight)
        public bool Mounts;        // the hoist's body: its pieces show the studs the jig takes them by (Mk3)
        Light sole;

        public static SuitBody Make(string name, Transform parent)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            return go.AddComponent<SuitBody>();
        }

        public bool Has(string id) { return Parts.ContainsKey(id); }
        public int Count { get { return Parts.Count; } }

        // The part of a role ("chest", "helmet"...; sided roles: the first found), and the chest's definition.
        public GameObject Role(string role)
        {
            foreach (var kv in Parts) { var d = Suit.Of(kv.Key); if (d != null && d.Role == role) return kv.Value; }
            return null;
        }
        public GameObject Chest { get { return Role("chest"); } }
        public Vector3 ChestSocket { get { var d = ChestDef; return d != null ? d.Socket : Suit.Socket1; } }
        Suit.PieceDef ChestDef { get { foreach (var id in Parts.Keys) { var d = Suit.Of(id); if (d != null && d.Role == "chest") return d; } return null; } }

        public GameObject Add(Suit.PieceDef d, bool plated) { return Add(d, plated ? 1f : 0f); }
        public GameObject Add(Suit.PieceDef d, float plate)
        {
            Remove(d.Id);
            var go = Suit.PieceObject(d, transform, plate, Mounts);
            go.transform.localPosition = d.Joint;
            foreach (var m in Suit.Meshes(go, d)) m.Key.layer = gameObject.layer;
            Parts[d.Id] = go;
            ApplyGlow(go);
            if (d.Role == "chest" && reactor != null)
            {
                reactor.transform.SetParent(go.transform, false);
                reactor.transform.localPosition = d.Socket;
                reactor.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }
            if (d.Role == "chest") SyncDiffuser();
            return go;
        }

        public void Remove(string id)
        {
            GameObject go;
            if (!Parts.TryGetValue(id, out go)) return;
            if (reactor != null && reactor.transform.IsChildOf(go.transform)) reactor.transform.SetParent(transform, true);
            Destroy(go);
            Parts.Remove(id);
        }

        public void Clear() { foreach (var id in new List<string>(Parts.Keys)) Remove(id); SetReactor(false, 0f, false); }

        // The arc reactor in the chest socket (Suit.Socket1 / Socket3: its front, local +Y, out of the chest, +Z).
        public void SetReactor(bool on, float charge, bool lit)
        {
            if (!on) { if (reactor != null) Destroy(reactor); reactor = null; reactorGlow = null; SyncDiffuser(); return; }
            if (reactor == null)
            {
                reactor = new GameObject("chest_reactor");
                reactorGlow = Glow.Add(reactor.transform);
                foreach (var p in Reactor.Parts) reactorGlow.AddPart(p);
                var chest = Chest;
                reactor.transform.SetParent(chest != null ? chest.transform : transform, false);
                reactor.transform.localPosition = chest != null ? ChestDef.Socket : Suit.Socket1 + new Vector3(0f, 1f, 0f);
                reactor.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                Reactor.SetLayer(reactor, gameObject.layer);
            }
            reactorGlow.Set(lit, charge, lit);
            reactorLit = lit;
            SyncDiffuser();
        }

        // The Mk3's diffuser plate, while it's clipped on over its reactor (SaveData.SuitDiffuser), lights with it.
        void SyncDiffuser()
        {
            var chest = Chest;
            var cd = ChestDef;
            if (reactor == null || chest == null || cd.Variant != 3 || !IronManSuit.Data.SuitDiffuser) { if (diffuser != null) Destroy(diffuser); diffuser = null; return; }
            if (diffuser == null)
            {
                diffuser = Res.MeshObject("diffuser", Res.Mesh(Suit.DiffuserFile), null, chest.transform);
                diffuser.transform.localPosition = Suit.DiffuserAt;
                diffuser.layer = gameObject.layer;
                diffuser.GetComponent<MeshRenderer>().enabled = chest.GetComponent<MeshRenderer>().enabled;
            }
            Suit.PaintDiffuser(diffuser, reactorLit);
        }
        public bool HasReactor { get { return reactor != null; } }
        public Vector3 ReactorWorld { get { return reactor != null ? reactor.transform.TransformPoint(new Vector3(0f, 0.03f, 0f)) : transform.TransformPoint(Suit.Socket1 + new Vector3(0f, 1f, 0f)); } }

        // 0 = dark, 1 = fully lit
        public void SetGlow(float level)
        {
            if (Mathf.Approximately(level, glow)) return;
            glow = level;
            foreach (var go in Parts.Values) ApplyGlow(go);
        }

        void ApplyGlow(GameObject go)
        {
            var d = Suit.Of(go.name);
            Suit.PaintGlow(go, d, (d.Role == "boot" ? ThrustGlow : glow) > 0.5f);
        }

        // A child of a piece that is part of the suit itself (the reactor, the diffuser plate, the Mk3 helmet's
        // faceplate and buttons), not something hung on it by whoever shows the body (the hoist's bolts and joints).
        public bool Owns(Transform child)
        {
            if (reactor != null && child == reactor.transform || diffuser != null && child == diffuser.transform) return true;
            var fp = child.parent != null ? child.parent.GetComponent<Faceplate>() : null;
            return fp != null && fp.Owns(child);
        }

        // The Mk3 helmet's faceplate (null: no helmet on, or the Mk1's), and opening or closing it (snap: at once).
        public Faceplate Faceplate { get { var h = Role("helmet"); return h != null ? h.GetComponent<Faceplate>() : null; } }
        public void SetFaceplate(bool open, bool snap = false)
        {
            var fp = Faceplate;
            if (fp == null) return;
            if (snap) fp.Snap(open); else fp.Set(open);
        }

        public void SetThrust(float t)
        {
            if (Mathf.Approximately(t, ThrustGlow)) return;
            bool was = ThrustGlow > 0.5f;
            ThrustGlow = t;
            if (was != (t > 0.5f)) foreach (var kv in Parts) if (Suit.Of(kv.Key).Role == "boot") ApplyGlow(kv.Value);
            if (sole == null)
            {
                var lg = new GameObject("thrust_light");
                lg.transform.SetParent(transform, false);
                lg.transform.localPosition = new Vector3(0f, -0.15f, 0.03f);
                sole = lg.AddComponent<Light>();
                sole.type = LightType.Point;
                sole.color = new Color(0.55f, 0.85f, 1f);
                sole.range = 3f;
            }
            sole.enabled = t > 0.01f;
            sole.intensity = 2.5f * t * Random.Range(0.85f, 1.1f);
        }

        // World position of a point in a piece's space (e.g. the right palm), or the body's own.
        public Vector3 PiecePoint(string id, Vector3 local)
        {
            GameObject go;
            return Parts.TryGetValue(id, out go) ? go.transform.TransformPoint(local) : transform.position;
        }

        // Show or hide every part of a role ("helmet", "chest").
        public void SetRenderers(string role, bool on)
        {
            foreach (var kv in Parts)
            {
                var d = Suit.Of(kv.Key);
                if (d == null || d.Role != role) continue;
                foreach (var m in Suit.Meshes(kv.Value, d)) m.Key.GetComponent<MeshRenderer>().enabled = on;
                // the reactor sits in the chest socket: hidden with it, or it fills the view when looking down
                if (role == "chest" && reactor != null) foreach (var r in reactor.GetComponentsInChildren<Renderer>()) r.enabled = on;
                if (role == "chest" && diffuser != null) diffuser.GetComponent<MeshRenderer>().enabled = on;
            }
        }
    }
}
