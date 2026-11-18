using MSCLoader;
using UnityEngine;

namespace IronManSuit
{
    // Building the arc reactor at the garage workbench, cave Mk1 style (Phase 2.2 build spec), the game's way: hold a
    // part over the chuck and the assemble hand shows -> left-click fits it; look at the stack and the disassemble
    // hand shows -> right-click takes the top part off (the vanilla Assembly / Removal look: GUIassemble,
    // GUIdisassemble, CarBuilding assemble / disassemble sounds). Real tools for the rest (BenchWork.cs).
    //
    // The stand is a 3-jaw lathe chuck: look at it and left-click to tighten its jaws on the cage, right-click to loosen.
    // Order:
    //  1. cage into the chuck, tighten the jaws
    //  2. core housing into the cage, the palladium wafer (salvaged from the landfill's catalytic converter) into it
    //  3. bolt the core down: 3 x 8 mm bolts with the player's own spanner
    //  4. solder its 6 joints with the soldering iron (hold Use; let go early and it's a cold joint, redo it)
    //  5. charge it: the charger switched on, both clamps on the core's rim, ~30 game minutes
    //  6. clamps off, the bare light-guide ring on, its 10 coil blocks wound by hand from the copper wire spool
    //     (hold the left mouse button, 28-32 turns each; right-click unwinds)
    //  7. bolt the ring down: 3 x 7 mm bolts
    //  8. first light: clamps on the core's top, charger on. A cold joint, a badly wound coil or a loose ring bolt
    //     makes it flicker and die, with a hint; fix it and try again. Then it's done: loosen the chuck, lift it out.
    // A finished reactor put back on the chuck can be taken apart again.
    static partial class Bench
    {
        public const int Empty = 0, CageOn = 1, CoreOn = 2, RingOn = 3;
        const float ChargeHours = 0.5f;       // a full charge takes half a game hour
        const float ChargerReach = 1.6f;      // the charger (and the wire spool) must stand this close to the chuck
        public const float ChargeSpeed = 1f;

        static Transform root, stackT;
        static Glow stack;
        static BoxCollider stackCol, zoneCol;
        static float lastHours = -1f, shownCharge = -1f;
        public static bool Prompting;

        // The chuck on the garage workbench (YARD/Building/Garage/garage_table): on its top, measured from its collider.
        public static Vector3 PadPos { get { return root != null ? root.position : Vector3.zero; } }

        public static void Init()
        {
            lastHours = -1f; shownCharge = -1f;
            ResetWork();
            var table = G.Find("YARD/Building/Garage/garage_table");
            if (table == null) { ModConsole.Error("IronManSuit: garage workbench not found, no reactor bench"); return; }
            var col = table.GetComponentInChildren<Collider>();
            var b = col != null ? col.bounds : table.GetComponent<Renderer>().bounds;
            root = new GameObject("IronManReactorBench").transform;
            root.SetParent(table, true);
            root.position = new Vector3(b.center.x + PadOffset.x, b.max.y, b.center.z + PadOffset.z);
            root.rotation = Quaternion.Euler(0f, PadYaw, 0f);
            BuildChuck(root);
            stackT = new GameObject("reactor_stack").transform;
            stackT.SetParent(root, false);
            stackT.localPosition = new Vector3(0f, ChuckTop + StackLift, 0f);
            stack = Glow.Add(stackT);
            // Our look triggers sit on Ignore Raycast (layer 2): our own tests use Collider.Raycast, which ignores
            // layers, while the game's spanner and hand mouse-picks look past them to the bolts inside.
            var look = new GameObject("stack_look");
            look.layer = 2;
            look.transform.SetParent(stackT, false);
            stackCol = look.AddComponent<BoxCollider>();
            stackCol.isTrigger = true;
            // the space over the chuck a part is aimed into
            var zone = new GameObject("fit_zone");
            zone.layer = 2;
            zone.transform.SetParent(root, false);
            zoneCol = zone.AddComponent<BoxCollider>();
            zoneCol.isTrigger = true;
            // about the chuck's own footprint (it was 20 x 22 cm, and a slanted look past the chuck clipped its top)
            zoneCol.center = new Vector3(0f, ChuckTop / 2f + 0.03f, 0f);
            zoneCol.size = new Vector3(0.14f, ChuckTop + 0.06f, 0.14f);
            var d = IronManSuit.Data;
            if (d.BenchStage >= CageOn) stack.AddPart(Reactor.Cage);
            if (d.BenchStage >= CoreOn) stack.AddPart(Reactor.Core);
            if (d.BenchStage >= RingOn) stack.AddPart(Reactor.Coil);
            if (d.BenchStage == Empty) d.ChuckTight = false;
            else if (d.Clamps != 0) d.ChuckTight = true;   // clamps only go on a gripped cage (saves from before the chuck)
            SetJaws(d.ChuckTight ? 0f : 1f);
            SyncWork();
            Refresh();
            ModConsole.Log("IronManSuit: reactor bench at " + root.position.ToString("F3") + " (table top " + b.max.y.ToString("F3") + ", stage " + d.BenchStage + ")");
        }

        // The build/charging stand: a 3-jaw lathe chuck lying face up on the table, the reactor's back seated on its
        // face and its cage gripped by the three stepped jaws. Jaws at 90/210/330 deg around the axis, clear of the
        // charger clamps at 30/150 deg (Terminal). Body 160 mm across, face 60 mm above the table.
        public const float ChuckTop = 0.060f, StackLift = 0.0015f;
        static readonly float[] JawAngles = { 90f, 210f, 330f };

        static void BuildChuck(Transform t)
        {
            var tex = "reactor_steel_albedo.png";
            var cast = Res.Mat("chuck_body", new Color(0.8f, 0.82f, 0.85f), tex, null, 0.7f, 0.45f);
            var bright = Res.Mat("chuck_jaw", new Color(0.86f, 0.87f, 0.89f), tex, null, 0.85f, 0.65f);
            var dark = Res.Mat("chuck_slot", new Color(0.2f, 0.2f, 0.21f), null, null, 0.7f, 0.35f);
            var probe = Res.WorkshopProbe(t, 0.5f);
            probe.transform.localPosition = new Vector3(0f, 0.08f, 0f);

            // Back plate, body, a narrower step, the machined face. Every visible top sits at its own height (no two
            // coplanar faces: they z-fight and flicker): backplate 12, body 54, step 57, face 60 (ChuckTop), the dark
            // inlays proud of the face at 60.5-61 mm, and the reactor's back 1.5 mm above the face (StackLift).
            Res.Cylinder(t, "chuck_backplate", new Vector3(0f, 0.006f, 0f), 0.15f, 0.012f, cast);
            Res.Cylinder(t, "chuck_body", new Vector3(0f, 0.033f, 0f), 0.16f, 0.042f, cast);
            Res.Cylinder(t, "chuck_chamfer", new Vector3(0f, 0.0555f, 0f), 0.152f, 0.003f, bright);
            Res.Cylinder(t, "chuck_face", new Vector3(0f, 0.0585f, 0f), 0.142f, 0.003f, bright);
            Res.Cylinder(t, "chuck_bore_lip", new Vector3(0f, 0.0597f, 0f), 0.044f, 0.002f, dark);   // top 60.7
            Res.Cylinder(t, "chuck_bore", new Vector3(0f, 0.0600f, 0f), 0.036f, 0.002f, dark);       // top 61.0

            for (int i = 0; i < 3; i++)
            {
                float a = JawAngles[i];
                var rot = Quaternion.Euler(0f, 90f - a, 0f);   // local +Z points outward along angle a
                var dir = new Vector3(Mathf.Cos(a * Mathf.Deg2Rad), 0f, Mathf.Sin(a * Mathf.Deg2Rad));

                // the jaw's guide slot cut across the face
                Res.Box(t, "chuck_slot" + i, dir * 0.052f + Vector3.up * 0.0595f, new Vector3(0.024f, 0.002f, 0.056f), dark, rot);   // top 60.5
                // The jaw slides along its slot as one group (its local +Z = outward; SetJaws moves it). Stepped down
                // toward the axis like a real chuck jaw, and closed it hugs the cage's profile (reactor_upper.obj:
                // a 26 mm flange at its back, the 37 mm wall from 10 mm up): the low step stops just outside the
                // flange, and the tall step's serrated face grips the wall 0.5 mm off.
                var jaw = new GameObject("chuck_jaw" + i).transform;
                jaw.SetParent(t, false);
                jaw.localRotation = rot;
                jaws[i] = jaw;
                jawDirs[i] = dir;
                Res.Box(jaw, "low", new Vector3(0f, ChuckTop + 0.005f, (0.027f + 0.076f) / 2f), new Vector3(0.02f, 0.010f, 0.076f - 0.027f), bright);
                // tall step: 10-26 mm above the face, over both 37 mm flanges of the cage (10-12 and 22-26 mm up its side)
                Res.Box(jaw, "high", new Vector3(0f, ChuckTop + 0.018f, (0.0375f + 0.076f) / 2f), new Vector3(0.02f, 0.016f, 0.076f - 0.0375f), bright);
                // serrations on the gripping face
                for (int s = 0; s < 5; s++)
                    Res.Box(jaw, "tooth" + s, new Vector3(0f, ChuckTop + 0.0125f + s * 0.003f, 0.0371f), new Vector3(0.02f, 0.0008f, 0.0008f), dark);

                // key socket on the rim between jaws (square hole for the chuck key)
                float k = (a + 60f) * Mathf.Deg2Rad;
                var kd = new Vector3(Mathf.Cos(k), 0f, Mathf.Sin(k));
                Res.Box(t, "chuck_key" + i, kd * 0.0745f + Vector3.up * 0.0565f, new Vector3(0.008f, 0.002f, 0.008f), dark, Quaternion.Euler(0f, 90f - (a + 60f), 0f));   // on the step, top 57.5
                // mounting bolts in the back plate
                Res.Cylinder(t, "chuck_bolt" + i, kd * 0.069f + Vector3.up * 0.0125f, 0.008f, 0.003f, dark);
            }

            // solid, so things put down on it rest on top instead of sinking through; also what "look at the chuck" hits
            chuckCol = t.gameObject.AddComponent<BoxCollider>();
            chuckCol.center = new Vector3(0f, ChuckTop / 2f, 0f);
            chuckCol.size = new Vector3(0.15f, ChuckTop, 0.15f);
        }

        // ------------------------------------------------------------------------------------------------------------
        // The chuck's jaws. Open (10 mm back from the rim) whenever the chuck is empty, so a cage drops in; tightened by
        // hand (look at the chuck, left-click; right-click loosens) they slide in and grip it. Parts above the cage
        // only go in or come out with the cage gripped, the charger clamps only go on with it gripped, and the cage
        // (or a finished reactor) only comes out with the jaws open.
        const float JawTravel = 0.010f, JawTime = 0.6f;   // metres open, seconds for a full stroke
        static readonly Transform[] jaws = new Transform[3];
        static readonly Vector3[] jawDirs = new Vector3[3];
        static BoxCollider chuckCol;
        static float jawOpen = 1f;   // 0 closed .. 1 open, as shown

        static void SetJaws(float open)
        {
            jawOpen = Mathf.Clamp01(open);
            // ease in and out so the jaws start and stop softly
            float e = jawOpen * jawOpen * (3f - 2f * jawOpen);
            for (int i = 0; i < 3; i++)
                if (jaws[i] != null) jaws[i].localPosition = jawDirs[i] * (JawTravel * e);
        }

        static void AnimateJaws(SaveData d)
        {
            float target = d.ChuckTight ? 0f : 1f;
            if (jawOpen != target) SetJaws(Mathf.MoveTowards(jawOpen, target, Time.deltaTime / JawTime));
        }

        static void Tighten(bool tight)
        {
            var d = IronManSuit.Data;
            if (d.ChuckTight == tight) return;
            d.ChuckTight = tight;
            G.Sound("CarBuilding", tight ? "assemble" : "disassemble", root);
        }

        // Look at the chuck (not the reactor on it) to work its jaws. Returns whether it took the look.
        static bool ChuckLook(SaveData d, ref bool assemble, ref bool disassemble)
        {
            if (chuckCol == null || d.BenchStage == Empty || !G.LookingAt(chuckCol, 1.2f) || LookingAtClamp()) return false;
            Prompting = true;
            if (!d.ChuckTight)
            {
                G.Interaction("TIGHTEN CHUCK");
                assemble = true;
                if (G.Click) Tighten(true);
            }
            else if (AnyClamp()) G.Interaction("TAKE THE CHARGER CLAMPS OFF FIRST");
            else
            {
                G.Interaction("LOOSEN CHUCK");
                disassemble = true;
                if (G.RightClick) Tighten(false);
            }
            return true;
        }

        // Tuned in game so the chuck lands on a clear patch of the table top (dev `bench`).
        public static Vector3 PadOffset = new Vector3(0f, 0f, 0.3f);   // 0.3 m to the left (+Z) of the table's middle, as the user asked
        public static float PadYaw = 0f;

        // Glow, the core's palladium, the ring's windings and the look collider follow what's on the chuck.
        static void Refresh()
        {
            var d = IronManSuit.Data;
            bool lit = d.BenchStage >= RingOn && d.Lit;
            stack.Set(lit || flickerOn, d.BenchStage >= CoreOn && d.Wafer ? d.CoreCharge : 0f, lit || flickerOn);
            if (d.BenchStage >= CoreOn) stack.ShowClass(Reactor.Core, "glowdisc", d.Wafer);
            if (d.BenchStage >= RingOn) stack.SetWinding(d.Turns);
            shownCharge = d.CoreCharge;
            float h = d.BenchStage == Empty ? 0.001f : d.BenchStage == CageOn ? 0.041f : 0.055f;
            stackCol.center = new Vector3(0f, h / 2f, 0f);
            stackCol.size = new Vector3(0.09f, h, 0.09f);
            stackCol.enabled = d.BenchStage != Empty;
        }

        // The game's spanner, ratchet or screwdriver is out: the bench stays quiet so its bolt highlight shows.
        static Transform spannerTool;
        public static bool ToolsOut()
        {
            if (spannerTool == null) spannerTool = G.FpsCam.Find("2Spanner");
            return spannerTool != null && spannerTool.gameObject.activeInHierarchy;
        }

        public static void Tick()
        {
            Prompting = false;
            if (root == null || CatalogueUI.Open) return;
            var d = IronManSuit.Data;
            bool assemble = false, disassemble = false;

            if (ToolsOut()) { }                 // nothing of ours to say while a spanner is out
            else if (Work(d)) Prompting = true; // soldering a joint / winding a coil took the look
            else
            {
                // --- fitting: the part in hand, held over the chuck ---
                // The hand carries a part about half a metre out, so aiming at the chuck from arm's reach counts too.
                var held = Items.Instance.HeldItem();
                // (Trimmed from 0.45 m / 1.6 m / 8 cm: it reached a bit too far out, fitting a part held beside the chuck.)
                if (held != null && (Vector3.Distance(held.Col.bounds.center, stackT.position + Vector3.up * 0.03f) < 0.12f ||
                                     G.LookingNear(zoneCol, 1.3f, 0.05f)))
                {
                    string why;
                    bool fits = Fits(d, held, out why);
                    if (fits)
                    {
                        assemble = true;
                        Prompting = true;
                        if (G.Click) Fit(held);
                    }
                    else if (why != null) { Prompting = true; G.Interaction(why); }
                }

                // --- taking off: look at the stack, empty hand or not, as vanilla ---
                if (!assemble && d.BenchStage != Empty && G.LookingAt(stackCol, 1f) && !LookingAtClamp())
                {
                    Prompting = true;
                    string why = TakeOffBlocked(d);
                    if (why != null) G.Interaction(why);
                    else
                    {
                        disassemble = true;
                        if (d.BenchStage == RingOn && d.Lit && !d.ChuckTight) G.Interaction("LIFT OUT THE ARC REACTOR");
                        if (G.RightClick) TakeOff();
                    }
                }
                // --- the chuck's jaws: look at the chuck body ---
                else if (!assemble && !Prompting) ChuckLook(d, ref assemble, ref disassemble);
            }

            AnimateJaws(d);
            Clamps(d);
            FirstLight(d);
            G.WantAssemble |= assemble;
            G.WantDisassemble |= disassemble || clampDisassemble;
        }

        static bool IsReactorPart(string k)
        {
            return k == Parts.CageId || k == Parts.CoreId || k == Parts.HousingId || k == Parts.CoilId || k == Parts.RingId ||
                   k == Parts.ReactorId || k == Parts.WaferId;
        }

        // What the held item can do at the chuck now, or why not.
        static bool Fits(SaveData d, Items.Item held, out string why)
        {
            why = null;
            string k = held.Kind.Id;
            if (!IsReactorPart(k)) return false;
            switch (d.BenchStage)
            {
                case Empty:
                    if (k == Parts.CageId || k == Parts.ReactorId) return true;
                    why = "PUT THE CAGE IN THE CHUCK FIRST"; return false;
                case CageOn:
                    if (k != Parts.CoreId && k != Parts.HousingId) { why = "THE CORE GOES IN NEXT"; return false; }
                    if (!d.ChuckTight) { why = "TIGHTEN THE CHUCK FIRST"; return false; }
                    return true;
                case CoreOn:
                    if (!d.ChuckTight) { why = "TIGHTEN THE CHUCK FIRST"; return false; }
                    if (k == Parts.WaferId)
                    {
                        if (d.Wafer) { why = "THE PALLADIUM IS ALREADY IN"; return false; }
                        return true;
                    }
                    if (k == Parts.RingId || k == Parts.CoilId)
                    {
                        if (!d.Wafer) why = "THE CORE NEEDS ITS PALLADIUM FIRST";
                        else if (!AllTight(d.CoreBolts)) why = "BOLT THE CORE DOWN FIRST (8 MM)";
                        else if (!AllSoldered(d)) why = "SOLDER THE CORE'S JOINTS FIRST";
                        else if (AnyClamp()) why = "TAKE THE CHARGER CLAMPS OFF FIRST";
                        else if (d.CoreCharge < 1f) why = "CHARGE THE CORE FIRST";
                        return why == null;
                    }
                    why = d.Wafer ? "THE COIL RING GOES ON NEXT" : "THE PALLADIUM GOES IN NEXT"; return false;
                default:
                    why = "THE REACTOR IS ALL TOGETHER"; return false;
            }
        }

        static void Fit(Items.Item held)
        {
            var d = IronManSuit.Data;
            string kind = held.Kind.Id, extra = held.Extra;
            G.DropFromHand();   // let the hand's PickUp FSM let go the normal way before the part goes onto the chuck
            Items.Instance.Remove(held);
            G.Sound("CarBuilding", "assemble", stackT);
            if (kind == Parts.CageId) { stack.AddPart(Reactor.Cage); d.BenchStage = CageOn; }
            else if (kind == Parts.CoreId || kind == Parts.HousingId)
            {
                var s = Parts.ParseCore(kind, extra);
                stack.AddPart(Reactor.Core);
                d.BenchStage = CoreOn; d.CoreCharge = s.Charge; d.Wafer = s.Wafer;
                d.Joints = s.Joints; d.CoreBolts = new int[3];
            }
            else if (kind == Parts.WaferId) { d.Wafer = true; G.Flash("PALLADIUM IN", 1.5f); }
            else if (kind == Parts.RingId || kind == Parts.CoilId)
            {
                stack.AddPart(Reactor.Coil);
                d.BenchStage = RingOn; d.Turns = Parts.ParseTurns(kind, extra); d.RingBolts = new int[3]; d.Lit = false;
            }
            else if (kind == Parts.ReactorId)   // a finished reactor, back on to be taken apart
            {
                foreach (var p in Reactor.Parts) stack.AddPart(p);
                d.BenchStage = RingOn; d.CoreCharge = 1f; d.Wafer = true; d.Lit = true;
                Fill(d.CoreBolts, Bolt.Steps); Fill(d.Joints, 2); Fill(d.Turns, 30); Fill(d.RingBolts, Bolt.Steps);
            }
            SyncWork();
            Refresh();
        }

        static string TakeOffBlocked(SaveData d)
        {
            switch (d.BenchStage)
            {
                case CageOn: return d.ChuckTight ? "LOOSEN THE CHUCK FIRST" : null;
                case CoreOn:
                    if (!d.ChuckTight) return "TIGHTEN THE CHUCK FIRST";
                    if (AnyClamp()) return "TAKE THE CHARGER CLAMPS OFF FIRST";
                    return AnyTight(d.CoreBolts) ? "UNDO THE CORE BOLTS FIRST (8 MM)" : null;
                case RingOn:
                    if (AnyClamp()) return "TAKE THE CHARGER CLAMPS OFF FIRST";
                    if (d.Lit && !d.ChuckTight) return null;   // lift the finished reactor out
                    if (!d.ChuckTight) return "TIGHTEN THE CHUCK FIRST";
                    if (AnyTight(d.RingBolts)) return d.Lit ? "LOOSEN THE CHUCK TO LIFT THE REACTOR OUT" : "UNDO THE RING BOLTS FIRST (7 MM)";
                    return null;
            }
            return null;
        }

        static void TakeOff()
        {
            var d = IronManSuit.Data;
            G.Sound("CarBuilding", "disassemble", stackT);
            // the part comes off onto the table just in front of the chuck, toward the player (the table is 0.7 m deep)
            var toward = G.Cam.transform.position - stackT.position;
            toward.y = 0f;
            var at = stackT.position + Vector3.up * 0.06f + (toward.sqrMagnitude > 0.001f ? toward.normalized * 0.13f : Vector3.zero);
            if (d.BenchStage == RingOn && d.Lit && !d.ChuckTight)
            {
                // the finished reactor, lifted out whole
                foreach (var p in Reactor.Parts) stack.RemovePart(p);
                Items.Instance.Spawn(Parts.ReactorId, stackT.position + Vector3.up * 0.01f, stackT.rotation);
                ClearBuild(d);
                G.Flash("ARC REACTOR", 2f);
                ModConsole.Log("IronManSuit: finished arc reactor lifted out of the chuck");
            }
            else if (d.BenchStage == RingOn)
            {
                stack.SetWinding(null);
                stack.RemovePart(Reactor.Coil);
                Items.Instance.Spawn(Parts.RingWound(d.Turns) ? Parts.CoilId : Parts.RingId, at, stackT.rotation, Parts.TurnsExtra(d.Turns));
                d.BenchStage = CoreOn; d.Lit = false; d.Turns = new int[Res.CoilBlocks]; d.RingBolts = new int[3];
            }
            else if (d.BenchStage == CoreOn)
            {
                ReleaseClamps();
                stack.RemovePart(Reactor.Core);
                bool done = Parts.CoreFinished(d.Wafer, d.Joints);
                Items.Instance.Spawn(done ? Parts.CoreId : Parts.HousingId, at, stackT.rotation, Parts.CoreExtra(d.CoreCharge, d.Wafer, d.Joints));
                d.BenchStage = CageOn; d.CoreCharge = 0f; d.Wafer = false; d.Joints = new int[6]; d.CoreBolts = new int[3];
            }
            else if (d.BenchStage == CageOn)
            {
                stack.RemovePart(Reactor.Cage);
                d.BenchStage = Empty;
                Items.Instance.Spawn(Parts.CageId, at, stackT.rotation);
            }
            SyncWork();
            Refresh();
        }

        static void ClearBuild(SaveData d)
        {
            d.BenchStage = Empty; d.CoreCharge = 0f; d.Wafer = false; d.Lit = false; d.ChuckTight = false;
            d.CoreBolts = new int[3]; d.Joints = new int[6]; d.Turns = new int[Res.CoilBlocks]; d.RingBolts = new int[3];
        }

        static void Fill(int[] a, int v) { for (int i = 0; i < a.Length; i++) a[i] = v; }
        static bool AllTight(int[] a) { foreach (var v in a) if (v < Bolt.Steps) return false; return true; }
        static bool AnyTight(int[] a) { foreach (var v in a) if (v > 0) return true; return false; }
        static bool AllSoldered(SaveData d) { foreach (var j in d.Joints) if (j == 0) return false; return true; }

        // ------------------------------------------------------------------------------------------------------------
        // The charger. Charging: the clamps bite on the core's rim disc (r 27.5 mm, 49-51 mm up from the back), red at
        // 30 deg, black at 150 deg around the axis, jaws pointing in. With the coil ring on, that rim is covered, so for
        // the first light they bite the core's top ring (r 21.5-25.4 mm, 54 mm up) from above, between its joints.
        static bool clampDisassemble;

        static bool AnyClamp() { var c = Charger.Current; return c != null && c.Clamps.Exists(x => x.Attached); }

        static void ReleaseClamps() { var c = Charger.Current; if (c != null) foreach (var cl in c.Clamps) cl.Attached = false; IronManSuit.Data.Clamps = 0; }

        static void Terminal(Charger.Clamp cl, out Vector3 pos, out Quaternion rot)
        {
            if (IronManSuit.Data.BenchStage >= RingOn)
            {
                float a2 = (cl.Bit == 1 ? 31f : 151f) * Mathf.Deg2Rad;
                var outw = new Vector3(Mathf.Cos(a2), 0f, Mathf.Sin(a2));
                const float tilt = 40f * Mathf.Deg2Rad;
                var inDown = -outw * Mathf.Cos(tilt) - Vector3.up * Mathf.Sin(tilt);   // the clamp's +X: in and down
                var jaw = outw * 0.024f + Vector3.up * 0.0565f;                        // where its jaws bite
                pos = stackT.TransformPoint(jaw - inDown * 0.022f);
                // +X along inDown, +Y kept in the upright plane through it (as on the rim, where +Y is straight up)
                var upish = (Vector3.up - Vector3.Dot(Vector3.up, inDown) * inDown).normalized;
                rot = stackT.rotation * Quaternion.LookRotation(Vector3.Cross(inDown, upish), upish);
                return;
            }
            float a = (cl.Bit == 1 ? 30f : 150f) * Mathf.Deg2Rad;
            var outward = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            pos = stackT.TransformPoint(outward * 0.049f + Vector3.up * 0.050f);
            rot = stackT.rotation * Quaternion.FromToRotation(Vector3.right, -outward);
        }

        static bool LookingAtClamp()
        {
            var c = Charger.Current;
            if (c == null) return false;
            foreach (var cl in c.Clamps) if (G.LookingAt(cl.Col, 1.2f)) return true;
            return false;
        }

        // Why the clamps can't go on the part in the chuck now (null: they can).
        static string ClampBlocked(SaveData d)
        {
            if (d.BenchStage < CoreOn) return "NOTHING TO CHARGE";
            if (!d.ChuckTight) return "TIGHTEN THE CHUCK FIRST";
            if (d.BenchStage == CoreOn)
            {
                if (!d.Wafer) return "THE CORE NEEDS ITS PALLADIUM FIRST";
                if (!AllTight(d.CoreBolts)) return "BOLT THE CORE DOWN FIRST (8 MM)";
                if (!AllSoldered(d)) return "SOLDER THE CORE'S JOINTS FIRST";
            }
            return null;
        }

        static void Clamps(SaveData d)
        {
            clampDisassemble = false;
            var ch = Charger.Current = Charger.Nearest(root.position);
            foreach (var other in Charger.All) if (other != null && other != ch) foreach (var cl in other.Clamps) other.RestClamp(cl);
            if (ch == null || ch.Item == null || ch.Item.Go == null) { d.Clamps = 0; return; }
            bool inHand = G.InHand(ch.transform);
            bool near = !inHand && Vector3.Distance(ch.transform.position, root.position) < ChargerReach;
            if (!near || d.BenchStage < CoreOn) foreach (var cl in ch.Clamps) cl.Attached = false;

            int bits = 0;
            foreach (var cl in ch.Clamps)
            {
                Vector3 p; Quaternion r;
                if (cl.Attached) { Terminal(cl, out p, out r); ch.PlaceClamp(cl, p, r); bits |= cl.Bit; }
                else ch.RestClamp(cl);
            }
            d.Clamps = bits;

            // Clamps on: look at one, right-click takes it off. Off: look at one, left-click puts it on the core.
            if (!Prompting && !inHand)
                foreach (var cl in ch.Clamps)
                {
                    if (!G.LookingAt(cl.Col, 1.2f)) continue;
                    Prompting = true;
                    string why = ClampBlocked(d);
                    if (cl.Attached)
                    {
                        G.Interaction(cl.Name);
                        clampDisassemble = true;
                        if (G.RightClick) { cl.Attached = false; G.Sound("CarBuilding", "disassemble", cl.Go.transform); }
                    }
                    else if (!near && d.BenchStage >= CoreOn) G.Interaction("BRING THE CHARGER TO THE WORKBENCH");
                    else if (why != null) G.Interaction(why);
                    else
                    {
                        G.Interaction("CONNECT " + cl.Name + " TO THE CORE", false, true);
                        if (G.Click) { cl.Attached = true; G.Sound("CarBuilding", "assemble", cl.Go.transform); }
                    }
                    break;
                }

            // Both on and switched on: charge (core only), in game time.
            bool on = ch.Clamps.TrueForAll(x => x.Attached);
            bool powered = on && d.ChargerOn && ClampBlocked(d) == null;   // (a bolt undone mid-charge stops it)
            if (lastHours < 0f) lastHours = d.GameHours;
            float dh = Mathf.Max(0f, d.GameHours - lastHours);
            lastHours = d.GameHours;
            if (powered && d.BenchStage == CoreOn && d.CoreCharge < 1f)
            {
                d.CoreCharge = Mathf.Min(1f, d.CoreCharge + dh * ChargeSpeed / ChargeHours);
                if (d.CoreCharge >= 1f) { G.Flash("CORE CHARGED", 2.5f); ModConsole.Log("IronManSuit: reactor core charged"); }
            }
            ch.SetNeedle(powered ? (d.BenchStage == RingOn ? (d.Lit ? 1f : Random.Range(0.2f, 0.9f)) : d.CoreCharge) : 0f);
            ch.SetLed(!d.ChargerOn ? new Color(0.04f, 0.01f, 0.01f)
                      : !on ? new Color(0.15f, 0.03f, 0.02f)
                      : d.CoreCharge >= 1f || d.Lit ? new Color(0.2f, 1f, 0.25f)
                      : (Mathf.Repeat(Time.time, 1f) < 0.5f ? new Color(1f, 0.55f, 0.1f) : new Color(0.3f, 0.12f, 0.03f)));
            if (Mathf.Abs(d.CoreCharge - shownCharge) > 0.01f) Refresh();
        }




    }
}
