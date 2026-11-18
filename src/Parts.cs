using System.Collections.Generic;
using MSCLoader;
using UnityEngine;

namespace IronManSuit
{
    // The reactor's loose parts as items, and the cave-style build's materials and tools. Reactor parts lie front-up
    // (reactor local +Y) like on a workbench.
    //  - reactor_cage: the cage.
    //  - core_housing / reactor_core: the core. Extra "charge;wafer;joints" (e.g. "0.4;1;222210"): its charge, whether
    //    the palladium wafer is in, and its 6 solder joints (0 bare, 1 cold, 2 good). A finished core (wafer in, all
    //    joints good) is a reactor_core; one that isn't is a core_housing. A legacy reactor_core's Extra is just its
    //    charge: a finished core.
    //  - reactor_ring / reactor_coil: the light-guide ring. Extra = turns per coil block ("30,29,0,..."). A ring wound
    //    in every block is a reactor_coil; a legacy reactor_coil (no Extra) is fully wound.
    //  - copper_wire (the spool the coils are wound from), soldering_iron, cat_converter (scrap, at the landfill:
    //    Extra = its two flange bolts' tightness), palladium_wafer (from inside the converter).
    static class Parts
    {
        public const string CageId = "reactor_cage", CoreId = "reactor_core", CoilId = "reactor_coil", ReactorId = "arc_reactor", ChargerId = "reactor_charger";
        public const string HousingId = "core_housing", RingId = "reactor_ring", WireId = "copper_wire", IronId = "soldering_iron",
                            ConverterId = "cat_converter", WaferId = "palladium_wafer";
        static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

        // --- core state -------------------------------------------------------------------------------------------
        public class CoreState { public float Charge; public bool Wafer; public int[] Joints = new int[6]; }
        public static CoreState ParseCore(string kind, string extra)
        {
            var s = new CoreState();
            if (extra != null && extra.IndexOf(';') >= 0)
            {
                var p = extra.Split(';');
                float.TryParse(p[0], System.Globalization.NumberStyles.Float, Inv, out s.Charge);
                s.Wafer = p.Length > 1 && p[1] == "1";
                if (p.Length > 2) for (int i = 0; i < 6 && i < p[2].Length; i++) s.Joints[i] = Mathf.Clamp(p[2][i] - '0', 0, 2);
            }
            else if (kind == CoreId)   // legacy: a finished core, the Extra its charge
            {
                float c;
                if (extra != null && float.TryParse(extra, System.Globalization.NumberStyles.Float, Inv, out c)) s.Charge = c;
                s.Wafer = true;
                for (int i = 0; i < 6; i++) s.Joints[i] = 2;
            }
            s.Charge = Mathf.Clamp01(s.Charge);
            return s;
        }
        public static string CoreExtra(float charge, bool wafer, int[] joints)
        {
            var j = new System.Text.StringBuilder();
            for (int i = 0; i < 6; i++) j.Append(joints != null && i < joints.Length ? joints[i] : 0);
            return charge.ToString("0.###", Inv) + ";" + (wafer ? "1" : "0") + ";" + j;
        }
        public static bool CoreFinished(bool wafer, int[] joints) { if (!wafer) return false; foreach (var j in joints) if (j != 2) return false; return true; }
        public static float ChargeOf(string extra) { return ParseCore(CoreId, extra).Charge; }

        // --- ring state -------------------------------------------------------------------------------------------
        public static int[] ParseTurns(string kind, string extra)
        {
            var t = new int[Res.CoilBlocks];
            if (string.IsNullOrEmpty(extra)) { if (kind == CoilId) for (int i = 0; i < t.Length; i++) t[i] = 30; return t; }
            var p = extra.Split(',');
            for (int i = 0; i < t.Length && i < p.Length; i++) int.TryParse(p[i], out t[i]);
            return t;
        }
        public static string TurnsExtra(int[] t) { var s = new string[t.Length]; for (int i = 0; i < t.Length; i++) s[i] = t[i].ToString(); return string.Join(",", s); }
        public static bool RingWound(int[] t) { foreach (var n in t) if (n < Bench.TurnsMin || n > Bench.TurnsMax) return false; return true; }

        static Material steel, rust, plastic, bakelite, pd, copperMat;

        public static void Init()
        {
            steel = Res.Mat("part_steel", new Color(0.72f, 0.73f, 0.76f), "reactor_steel_albedo.png", null, 0.8f, 0.5f);
            rust = Res.Mat("converter_shell", new Color(0.5f, 0.42f, 0.36f), "reactor_steel_albedo.png", null, 0.45f, 0.2f);
            plastic = Res.Mat("spool_plastic", new Color(0.1f, 0.1f, 0.11f), null, null, 0f, 0.45f);
            bakelite = Res.Mat("iron_handle", new Color(0.35f, 0.08f, 0.05f), null, null, 0f, 0.55f);
            pd = Res.Mat("palladium", new Color(0.86f, 0.87f, 0.9f), null, null, 0.95f, 0.75f);
            copperMat = Res.ReactorMaterial("copper");

            Part(CageId, "reactor cage(itemx)", 0.6f, Reactor.Cage);
            CorePart(CoreId, "reactor core(itemx)");
            CorePart(HousingId, "reactor core housing(itemx)");
            RingPart(CoilId, "reactor coil ring(itemx)");
            RingPart(RingId, "light guide ring(itemx)");
            Items.Add(new Items.Kind
            {
                Id = ReactorId, Name = "arc reactor(itemx)", Mass = 2f,
                Build = (go, x) =>
                {
                    var g = Glow.Add(go.transform);
                    foreach (var p in Reactor.Parts) g.AddPart(p);
                    g.Set(true, 1f, true);
                    return Items.Fit(go);
                },
            });
            Items.Add(new Items.Kind
            {
                Id = ChargerId, Name = "battery charger(itemx)", Mass = 3f,
                Build = (go, x) => Charger.BuildBody(go.transform),
                Prompt = it => Charger.Prompt(it.Go.GetComponent<Charger>()),
                Use = it => { Charger.Toggle(); return false; },
                Spawned = it => it.Go.AddComponent<Charger>().Attach(it),
            });
            Items.Add(new Items.Kind
            {
                Id = WireId, Name = "copper wire spool(itemx)", Mass = 1.2f,
                Build = (go, x) =>
                {
                    var t = go.transform;
                    Res.Cylinder(t, "flange_bottom", new Vector3(0f, 0.0025f, 0f), 0.085f, 0.005f, plastic);
                    Res.Cylinder(t, "wire", new Vector3(0f, 0.0325f, 0f), 0.072f, 0.055f, copperMat);
                    Res.Cylinder(t, "flange_top", new Vector3(0f, 0.0625f, 0f), 0.085f, 0.005f, plastic);
                    Res.Cylinder(t, "hub", new Vector3(0f, 0.0655f, 0f), 0.02f, 0.002f, Res.Mat("spool_hole", new Color(0.03f, 0.03f, 0.03f)));
                    return Items.Fit(go);
                },
            });
            Items.Add(new Items.Kind
            {
                Id = IronId, Name = "soldering iron(itemx)", Mass = 0.3f,
                Build = (go, x) =>
                {
                    var t = go.transform;
                    var along = Quaternion.Euler(0f, 0f, 90f);   // cylinders along local X
                    Res.Cylinder(t, "handle", new Vector3(-0.06f, 0.012f, 0f), 0.022f, 0.10f, bakelite, along);
                    Res.Cylinder(t, "grip_ring", new Vector3(-0.012f, 0.012f, 0f), 0.024f, 0.006f, plastic, along);
                    Res.Cylinder(t, "ferrule", new Vector3(0.002f, 0.012f, 0f), 0.014f, 0.022f, steel, along);
                    Res.Cylinder(t, "shaft", new Vector3(0.05f, 0.012f, 0f), 0.006f, 0.075f, steel, along);
                    Res.Cylinder(t, "tip", new Vector3(0.094f, 0.012f, 0f), 0.0025f, 0.014f, copperMat, along);
                    Res.Cylinder(t, "cable", new Vector3(-0.125f, 0.012f, 0f), 0.006f, 0.03f, plastic, along);
                    return Items.Fit(go);
                },
            });
            Items.Add(new Items.Kind
            {
                Id = WaferId, Name = "palladium wafer(itemx)", Mass = 0.08f,
                Build = (go, x) =>
                {
                    for (int i = 0; i < 3; i++) Res.Cylinder(go.transform, "disc" + i, new Vector3(0f, 0.0015f + i * 0.0035f, 0f), 0.03f - i * 0.003f, 0.003f, pd);
                    return Items.Fit(go);
                },
                Prompt = it => "PALLADIUM WAFER",
            });
            Items.Add(new Items.Kind
            {
                Id = ConverterId, Name = "catalytic converter(itemx)", Mass = 4f, AngularDrag = 2f,
                Build = (go, x) => BuildConverter(go.transform),
                Spawned = it => ConverterBolts(it),
                Prompt = it => ConverterPrompt(it),
                Use = it => Salvage(it),
            });
        }

        // One reactor part: its mesh with its own glow state (unlit while loose).
        static void Part(string id, string name, float mass, string file)
        {
            Items.Add(new Items.Kind
            {
                Id = id, Name = name, Mass = mass, AngularDrag = 2f,
                Build = (go, extra) =>
                {
                    var g = Glow.Add(go.transform);
                    g.AddPart(file);
                    g.Set(false, 0f, false);
                    return Items.Fit(go);
                },
            });
        }

        // The core: the palladium discs show once the wafer is in, and glow with its charge.
        static void CorePart(string id, string name)
        {
            Items.Add(new Items.Kind
            {
                Id = id, Name = name, Mass = 0.9f, AngularDrag = 2f,
                Build = (go, extra) =>
                {
                    var s = ParseCore(id, extra);
                    var g = Glow.Add(go.transform);
                    g.AddPart(Reactor.Core);
                    g.ShowClass(Reactor.Core, "glowdisc", s.Wafer);
                    g.Set(false, s.Wafer ? s.Charge : 0f, false);
                    return Items.Fit(go);
                },
                Prompt = it =>
                {
                    var s = ParseCore(id, it.Extra);
                    return !s.Wafer ? "REACTOR CORE HOUSING (NO PALLADIUM)" : s.Charge >= 1f ? "REACTOR CORE (CHARGED)" : null;
                },
            });
        }

        // The ring: its coil blocks show as wound so far.
        static void RingPart(string id, string name)
        {
            Items.Add(new Items.Kind
            {
                Id = id, Name = name, Mass = 0.5f, AngularDrag = 2f,
                Build = (go, extra) =>
                {
                    var g = Glow.Add(go.transform);
                    g.AddPart(Reactor.Coil);
                    if (id == RingId || !string.IsNullOrEmpty(extra)) g.SetWinding(ParseTurns(id, extra));
                    g.Set(false, 0f, false);
                    return Items.Fit(go);
                },
            });
        }

        // --- the scrap catalytic converter ------------------------------------------------------------------------
        // A rusty can along local X: shell, cones, pipe stubs and a diamond flange at each end with one 10 mm bolt
        // pointing out of each flange's face (so the spanner reaches it past the item's own collider).
        const int ConverterBoltMm = 10;
        static readonly float FlangeX = 0.196f;

        static Collider BuildConverter(Transform t)
        {
            var along = Quaternion.Euler(0f, 0f, 90f);
            Res.Cylinder(t, "shell", new Vector3(0f, 0.06f, 0f), 0.115f, 0.22f, rust, along);
            Res.Box(t, "heat_shield", new Vector3(0f, 0.115f, 0f), new Vector3(0.2f, 0.006f, 0.09f), rust);
            foreach (float sx in new[] { -1f, 1f })
            {
                Res.Cylinder(t, "cone", new Vector3(sx * 0.125f, 0.06f, 0f), 0.08f, 0.03f, rust, along);
                Res.Cylinder(t, "pipe", new Vector3(sx * 0.16f, 0.06f, 0f), 0.045f, 0.045f, rust, along);
                Res.Box(t, "flange", new Vector3(sx * (FlangeX - 0.004f), 0.06f, 0f), new Vector3(0.008f, 0.07f, 0.07f), rust, Quaternion.Euler(45f, 0f, 0f));
            }
            return Items.Fit(t.gameObject);
        }

        static void ConverterBolts(Items.Item it)
        {
            var tight = (it.Extra ?? "8,8").Split(',');
            for (int i = 0; i < 2; i++)
            {
                float sx = i == 0 ? -1f : 1f;
                int t0; if (i >= tight.Length || !int.TryParse(tight[i], out t0)) t0 = Bolt.Steps;
                // on the flange's outer face (the head stands 3.5 mm proud of it when tight)
                var b = Bolt.Make(it.Go.transform, "flange_bolt" + i, new Vector3(sx * (FlangeX + 0.0035f), 0.06f + 0.026f, 0f),
                                  Quaternion.FromToRotation(Vector3.up, new Vector3(sx, 0f, 0f)), ConverterBoltMm, t0, steel, 0.013f, 0.007f, 0.009f, 0.014f);
                b.Changed = x => it.Extra = string.Join(",", new[] { BoltOf(it, 0), BoltOf(it, 1) });
            }
        }
        static string BoltOf(Items.Item it, int i)
        {
            var bs = it.Go.GetComponentsInChildren<Bolt>();
            return i < bs.Length ? bs[i].Tight.ToString() : "8";
        }
        static bool BoltsOut(Items.Item it) { foreach (var b in it.Go.GetComponentsInChildren<Bolt>()) if (b.Tight > 0) return false; return true; }

        static string ConverterPrompt(Items.Item it)
        {
            if (G.InHand(it.Go.transform)) return null;
            return BoltsOut(it) ? "PULL OUT THE HONEYCOMB" : "SCRAP CATALYTIC CONVERTER - UNDO ITS FLANGE BOLTS (" + ConverterBoltMm + " MM)";
        }

        static bool Salvage(Items.Item it)
        {
            if (!BoltsOut(it)) { G.Flash("UNDO BOTH FLANGE BOLTS FIRST", 2f); return false; }
            Items.Instance.Spawn(WaferId, it.Go.transform.position + Vector3.up * 0.15f, it.Go.transform.rotation);
            IronManSuit.Data.Salvaged = true;
            G.Sound("CarBuilding", "disassemble", it.Go.transform);
            G.Flash("PALLADIUM SALVAGED FROM THE HONEYCOMB", 3f);
            ModConsole.Log("IronManSuit: palladium salvaged");
            return true;   // the empty shell goes
        }

        // Once per save the converter waits at the landfill (LANDFILL/LandfillSpawn, where the game itself puts lost
        // things); if it's gone missing before its palladium came out, it's put back.
        // The palladium is lost too (no wafer, no core with it in, no reactor anywhere): the converter comes back, so a
        // mistake costs time, never the part.
        public static void PlaceConverter()
        {
            var d = IronManSuit.Data;
            if (d.Salvaged && !PalladiumAnywhere()) { d.Salvaged = false; d.ConverterPlaced = false; ModConsole.Log("IronManSuit: the palladium went missing"); }
            if (d.Salvaged || (d.ConverterPlaced && Items.Instance.First(ConverterId) != null)) return;
            var sp = G.Find("LANDFILL/LandfillSpawn");
            var p = sp != null ? sp.position : new Vector3(-780.118f, 13.322f, -644.396f);
            Items.Instance.Spawn(ConverterId, p + new Vector3(0f, 0.3f, 0f), Quaternion.Euler(0f, 37f, 0f), "8,8");
            d.ConverterPlaced = true;
            ModConsole.Log("IronManSuit: scrap catalytic converter at the landfill " + p.ToString("F1"));
        }

        static bool PalladiumAnywhere()
        {
            if (IronManSuit.Data.BenchStage >= Bench.CoreOn && IronManSuit.Data.Wafer) return true;
            foreach (var it in Items.Instance.Live)
            {
                var k = it.Kind.Id;
                if (k == WaferId || k == ReactorId || k == CoreId) return true;
                if (k == HousingId && ParseCore(k, it.Extra).Wafer) return true;
                if (k == "parcel" && it.Extra != null && it.Extra.Contains(CoreId)) return true;   // an old order's finished core
            }
            return false;
        }
    }

    // The battery charger: a steel box with a gauge and an LED on its front panel and two clamps on leads. On the
    // bench the clamps go onto the core's terminals (Bench drives that); the needle shows the core's charge.
    class Charger : MonoBehaviour
    {
        // Every charger in the world; the one standing nearest the bench pad is the one whose clamps reach the core.
        public static readonly List<Charger> All = new List<Charger>();
        public static Charger Current;
        public static Charger Nearest(Vector3 p)
        {
            Charger best = null; float bd = float.MaxValue;
            foreach (var c in All) { if (c == null) continue; float dd = (c.transform.position - p).sqrMagnitude; if (dd < bd) { bd = dd; best = c; } }
            return best;
        }

        public class Clamp
        {
            public string Name; public GameObject Go; public Collider Col; public LineRenderer Cable;
            public Vector3 RestLocal, SocketLocal; public bool Attached; public int Bit;
            public bool Placed; public Vector3 From;   // PlaceClamp: where the charger stood when the lead was last laid
        }
        public readonly List<Clamp> Clamps = new List<Clamp>();
        public Items.Item Item;
        Transform needle;
        Material led;
        static Material body, panel, red, black, metal, cableRed, cableBlack;

        // Front panel (tools/reactor/make_print.py im_charger_panel): 0.20 x 0.10 m on the -Z face; dial pivot at
        // px (150,150), LED at (408,144), + and - posts at (310,215) and (450,215) of 512 x 256.
        static Vector3 PanelPoint(float px, float py, float z = -0.0712f) { return new Vector3(-0.10f + px / 512f * 0.20f, 0.105f - py / 256f * 0.10f, z); }

        public static Collider BuildBody(Transform t)
        {
            if (body == null)
            {
                body = Res.Mat("charger_body", new Color(0.30f, 0.33f, 0.36f), null, null, 0.6f, 0.45f);
                panel = Res.Mat("charger_panel", Color.white, "im_charger_panel.png");
                red = Res.Mat("clamp_red", new Color(0.75f, 0.08f, 0.06f), null, null, 0f, 0.5f);
                black = Res.Mat("clamp_black", new Color(0.06f, 0.06f, 0.06f), null, null, 0f, 0.5f);
                metal = Res.Mat("clamp_metal", new Color(0.75f, 0.72f, 0.65f), null, null, 1f, 0.6f);
                cableRed = Res.Mat("cable_red", new Color(0.6f, 0.07f, 0.05f));
                cableBlack = Res.Mat("cable_black", new Color(0.05f, 0.05f, 0.05f));
            }
            Res.Box(t, "body", new Vector3(0f, 0.055f, 0f), new Vector3(0.20f, 0.11f, 0.14f), body);
            Res.Quad(t, "panel", new Vector3(0f, 0.055f, -0.0702f), new Vector2(0.20f, 0.10f), panel, Quaternion.identity);
            Res.Box(t, "handle", new Vector3(0f, 0.135f, 0f), new Vector3(0.12f, 0.012f, 0.02f), black);
            Res.Box(t, "post_l", new Vector3(-0.055f, 0.122f, 0f), new Vector3(0.012f, 0.026f, 0.016f), black);
            Res.Box(t, "post_r", new Vector3(0.055f, 0.122f, 0f), new Vector3(0.012f, 0.026f, 0.016f), black);
            Res.Cylinder(t, "post_plus", PanelPoint(310, 215, -0.074f), 0.009f, 0.008f, red, Quaternion.Euler(90f, 0f, 0f));
            Res.Cylinder(t, "post_minus", PanelPoint(450, 215, -0.074f), 0.009f, 0.008f, black, Quaternion.Euler(90f, 0f, 0f));
            var c = t.gameObject.AddComponent<BoxCollider>();
            c.center = new Vector3(0f, 0.07f, 0f);
            c.size = new Vector3(0.20f, 0.14f, 0.14f);
            return c;
        }

        public void Attach(Items.Item it)
        {
            Item = it;
            All.Add(this);
            var pivot = new GameObject("needle_pivot").transform;
            pivot.SetParent(transform, false);
            pivot.localPosition = PanelPoint(150, 150, -0.0716f);
            needle = pivot;
            Res.Box(pivot, "needle", new Vector3(0f, 0.022f, 0f), new Vector3(0.0018f, 0.044f, 0.0012f), black);
            led = new Material(Res.SelfIllum) { name = "ironman_charger_led", color = new Color(0.15f, 0.03f, 0.02f) };
            var l = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(l.GetComponent<Collider>());
            l.transform.SetParent(transform, false);
            l.transform.localPosition = PanelPoint(408, 144, -0.0725f);
            l.transform.localScale = Vector3.one * 0.011f;
            l.GetComponent<MeshRenderer>().sharedMaterial = led;
            Clamps.Add(MakeClamp("RED CLAMP", red, cableRed, new Vector3(-0.045f, 0.143f, 0.035f), PanelPoint(310, 215, -0.078f), 1));
            Clamps.Add(MakeClamp("BLACK CLAMP", black, cableBlack, new Vector3(0.045f, 0.143f, 0.035f), PanelPoint(450, 215, -0.078f), 2));
            foreach (var cl in Clamps) cl.Attached = (IronManSuit.Data.Clamps & cl.Bit) != 0;   // as saved; Bench lets go if the core isn't there
            SetNeedle(0f);
        }

        // A spring clamp: insulated handle in the lead's colour, copper jaws at its front (+X) end, a trigger
        // collider a little larger than it so it's easy to aim at.
        Clamp MakeClamp(string name, Material handle, Material cable, Vector3 rest, Vector3 socket, int bit)
        {
            var go = new GameObject("charger " + name.ToLowerInvariant());
            Res.Box(go.transform, "handle", new Vector3(-0.008f, 0f, 0f), new Vector3(0.045f, 0.012f, 0.016f), handle);
            Res.Box(go.transform, "jaw_top", new Vector3(0.022f, 0.004f, 0f), new Vector3(0.018f, 0.004f, 0.014f), metal);
            Res.Box(go.transform, "jaw_bottom", new Vector3(0.022f, -0.004f, 0f), new Vector3(0.018f, 0.004f, 0.014f), metal);
            var col = go.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = new Vector3(0.07f, 0.035f, 0.035f);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = cable;
            lr.SetWidth(0.006f, 0.006f);
            lr.SetVertexCount(12);
            lr.useWorldSpace = true;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return new Clamp { Name = name, Go = go, Col = col, Cable = lr, RestLocal = rest, SocketLocal = socket, Bit = bit };
        }

        public void SetNeedle(float charge) { if (needle != null) needle.localRotation = Quaternion.Euler(0f, 0f, 70f - 140f * Mathf.Clamp01(charge)); }

        public void SetLed(Color c) { if (led != null) led.color = c; }

        // Where a clamp is put: resting on the charger, or on a terminal (Bench passes its world pose).
        public void PlaceClamp(Clamp c, Vector3 pos, Quaternion rot)
        {
            // nothing moved since last time: the lead's already right (its drape costs a few raycasts)
            if (c.Placed && c.Go.transform.position == pos && c.Go.transform.rotation == rot && c.From == transform.position) return;
            c.Placed = true;
            c.From = transform.position;
            c.Go.transform.position = pos;
            c.Go.transform.rotation = rot;
            // the lead: from the panel post to the clamp's tail, sagging under its own weight
            var a = transform.TransformPoint(c.SocketLocal);
            var b = c.Go.transform.TransformPoint(new Vector3(-0.03f, 0f, 0f));
            // ...but lying on whatever is under it (the table top) rather than sinking through it
            float sag = Mathf.Clamp(Vector3.Distance(a, b) * 0.35f, 0.02f, 0.25f);
            for (int i = 0; i < 12; i++)
            {
                float s = i / 11f;
                var p = Vector3.Lerp(a, b, s) + Vector3.down * sag * 4f * s * (1f - s);
                if (i > 0 && i < 11) p.y = Mathf.Max(p.y, SurfaceBelow(Vector3.Lerp(a, b, s)) + 0.004f);
                c.Cable.SetPosition(i, p);
            }
        }

        // Height of the first solid surface under p (ignoring triggers and our own clamps), or far below.
        static float SurfaceBelow(Vector3 p)
        {
            float best = -1000f, bd = float.MaxValue;
            foreach (var h in Physics.RaycastAll(p + Vector3.up * 0.05f, Vector3.down, 1.5f))
                if (!h.collider.isTrigger && h.distance < bd) { bd = h.distance; best = h.point.y; }
            return best;
        }

        public void RestClamp(Clamp c)
        {
            c.Attached = false;
            PlaceClamp(c, transform.TransformPoint(c.RestLocal), transform.rotation);
        }

        // Use on the charger flips its power switch (it charges the core and powers the first light only when on).
        public static void Toggle()
        {
            var d = IronManSuit.Data;
            d.ChargerOn = !d.ChargerOn;
            if (Current != null) G.Sound("CarBuilding", "assemble", Current.transform, 0.4f);
        }

        public static string Prompt(Charger ch)
        {
            var d = IronManSuit.Data;
            if (!d.ChargerOn) return "BATTERY CHARGER - OFF (SWITCH ON)";
            if (ch == null || ch != Current) return "BATTERY CHARGER - ON (SWITCH OFF)";
            bool on = ch.Clamps.TrueForAll(c => c.Attached);
            if (on && d.BenchStage == Bench.RingOn)
                return d.Lit ? "BATTERY CHARGER - REACTOR RUNNING" : Bench.Testing ? "BATTERY CHARGER - POWERING UP" :
                       Bench.TestFailed ? "BATTERY CHARGER - NO LIGHT (FIX IT, SWITCH OFF AND ON)" : "BATTERY CHARGER - ON";
            if (on && d.CoreCharge >= 1f) return "BATTERY CHARGER - CORE CHARGED";
            if (on) return "BATTERY CHARGER - CHARGING " + Mathf.FloorToInt(d.CoreCharge * 100f) + "%";
            return "BATTERY CHARGER - ON (SWITCH OFF)";
        }

        void OnDestroy()
        {
            foreach (var c in Clamps) if (c.Go != null) Destroy(c.Go);
            if (led != null) Destroy(led);
            All.Remove(this);
            if (Current == this) Current = null;
        }
    }
}
