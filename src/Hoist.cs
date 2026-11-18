using System.Collections.Generic;
using MSCLoader;
using UnityEngine;

namespace IronManSuit
{
    // The suit hoist in the garage (Phase 3): a steel A-frame gantry with a chain block and, hanging from it, the jig the
    // suit is built on (tools/suit/build_suit.py: suit_hoist.obj, suit_rig.obj). It comes free with the first suit order.
    // Hoist space = the suit's body space: origin on the floor where its feet go, facing +Z.
    //
    // Per piece, the reactor bench's way:
    //  1. hold a piece's frame at the hoist, left-click: it hangs on its bracket of the jig
    //  2. bolt it to the jig with the player's spanner (its mount bolts at the back)
    //  3. solder its wiring with the iron (look at a joint, hold Use; let go early and it's a cold joint)
    //  4. plating (most pieces): hold a sheet of scrap steel at it, left-click to lay it on, then hold Use with an empty
    //     hand to hammer it to shape
    // Undo every bolt and right-click to take a piece off again (it keeps its joints and plate). Once all ten are done,
    // the finished arc reactor goes into the chest socket (left-click with it in hand) and the switch on the hoist's
    // leg powers the suit up: it lights, steps off the jig and stands on its own (Armor.cs).
    static class Hoist
    {
        public static Vector3 Pos = new Vector3(-17.95f, -0.6f, -1.05f);   // feet spot on the garage floor (dev `hoist pad`)
        public static float Yaw = 90f;                                      // the suit faces the workbench (east)

        const float SolderSeconds = 1.6f, HammerSeconds = 5f, Reach = 1.6f;
        static Transform root;
        static SuitBody body;
        static GameObject rig, lever;
        static BoxCollider zone, switchCol;
        static SphereCollider reactorCol;
        static Material boltMat, solderGood, solderCold, copperTab;
        static AudioSource sfx;
        public static bool Prompting;

        class Work { public Suit.PieceDef Def; public PieceState S; public GameObject Go; public BoxCollider Look; public Bolt[] Bolts; public Transform[] Joints; }
        static readonly Dictionary<string, Work> work = new Dictionary<string, Work>();

        static string solderId; static int solderJoint = -1; static float solderT;
        static float clangAt;
        static float activateT = -1f, returnT = -1f;

        public static bool Built { get { return root != null; } }
        public static Transform Root { get { return root; } }

        public static void Init()
        {
            root = null; body = null; work.Clear(); solderId = null; solderJoint = -1; activateT = -1f; returnT = -1f;
            if (IronManSuit.Data.HoistGiven) Build();
        }

        // The hoist arrives (first suit parcel opened) or is rebuilt on load.
        public static void Give()
        {
            var d = IronManSuit.Data;
            if (d.HoistGiven) return;
            d.HoistGiven = true;
            Build();
            Suit.PlaceSheets();
            G.Flash("THE SUIT HOIST IS SET UP IN THE GARAGE. SCRAP SHEET STEEL AT THE LANDFILL", 5f);
            ModConsole.Log("IronManSuit: hoist set up at " + root.position.ToString("F2"));
        }

        static void Build()
        {
            if (root != null) Object.Destroy(root.gameObject);
            work.Clear();
            root = new GameObject("IronManSuitHoist").transform;
            Place();
            var hoistGo = Res.MeshObject("hoist", Res.Mesh("suit_hoist.obj"), null, root);
            Materials(hoistGo, "suit_hoist.obj");
            rig = Res.MeshObject("rig", Res.Mesh("suit_rig.obj"), null, root);
            Materials(rig, "suit_rig.obj");
            Res.WorkshopProbe(root, 3f).transform.localPosition = new Vector3(0f, 1.2f, 0f);
            // solid legs (the A-frames) so the player walks round them; the beam is above head height
            foreach (var sx in new[] { 1f, -1f })
            {
                var leg = new GameObject("leg_col").AddComponent<BoxCollider>();
                leg.transform.SetParent(root, false);
                leg.center = new Vector3(sx * 0.78f, 1.1f, -0.32f);
                leg.size = new Vector3(0.08f, 2.2f, 0.9f);
            }
            // the power switch: a lever on the control box (on the right leg, facing in)
            lever = Res.Box(root, "switch_lever", new Vector3(0.735f, 1.07f, 0.03f), new Vector3(0.03f, 0.012f, 0.012f), Suit.Mat("black"));
            var sw = new GameObject("switch_look") { layer = 2 };
            sw.transform.SetParent(root, false);
            switchCol = sw.AddComponent<BoxCollider>();
            switchCol.isTrigger = true;
            switchCol.center = new Vector3(0.77f, 1.05f, 0.03f);
            switchCol.size = new Vector3(0.12f, 0.2f, 0.14f);
            // the space a piece is aimed into to hang it
            var z = new GameObject("hang_zone") { layer = 2 };
            z.transform.SetParent(root, false);
            zone = z.AddComponent<BoxCollider>();
            zone.isTrigger = true;
            zone.center = new Vector3(0f, 0.95f, 0f);
            zone.size = new Vector3(0.85f, 1.9f, 0.55f);
            sfx = root.gameObject.AddComponent<AudioSource>();
            sfx.spatialBlend = 1f; sfx.minDistance = 1.5f; sfx.maxDistance = 25f;

            if (boltMat == null)
            {
                boltMat = Res.Mat("suit_bolt", new Color(0.78f, 0.79f, 0.82f), "reactor_steel_albedo.png", null, 0.85f, 0.6f);
                solderGood = Res.Mat("suit_solder_good", new Color(0.86f, 0.87f, 0.89f), null, null, 0.9f, 0.85f);
                solderCold = Res.Mat("suit_solder_cold", new Color(0.5f, 0.5f, 0.48f), null, null, 0.4f, 0.12f);
                copperTab = Res.ReactorMaterial("copper");
            }
            body = SuitBody.Make("hoist_suit", root);
            body.Mounts = true;
            var d = IronManSuit.Data;
            if (!d.SuitActive) foreach (var s in d.Hoist) Hang(s, false);
            SyncReactor();
            SetLever(false);
        }

        static void Place()
        {
            // stand on the floor under the given spot
            var p = Pos;
            float best = float.MaxValue;
            foreach (var h in Physics.RaycastAll(new Vector3(p.x, p.y + 1.2f, p.z), Vector3.down, 2.5f))
                if (!h.collider.isTrigger && h.rigidbody == null && !h.transform.IsChildOf(root) && h.distance < best) { best = h.distance; p.y = h.point.y; }
            root.position = p;
            root.rotation = Quaternion.Euler(0f, Yaw, 0f);
        }


        static void Materials(GameObject go, string file)
        {
            var names = Res.SubNames(file);
            var ms = new Material[names.Length];
            for (int i = 0; i < names.Length; i++) ms[i] = Suit.Mat(names[i]);
            go.GetComponent<MeshRenderer>().sharedMaterials = ms;
        }

        static void SetLever(bool on) { if (lever != null) lever.transform.localRotation = Quaternion.Euler(0f, 0f, on ? 35f : -35f); }

        // ------------------------------------------------------------------------------------------------------------
        // A piece on its bracket, with its bolts, joints and look collider.
        static void Hang(PieceState s, bool sound)
        {
            var def = Suit.Of(s.Id);
            if (def == null) return;
            if (s.Bolts == null || s.Bolts.Length != def.BoltPos.Length) s.Bolts = new int[def.BoltPos.Length];
            if (s.Joints == null || s.Joints.Length != def.JointPos.Length) s.Joints = new int[def.JointPos.Length];
            var go = body.Add(def, s.Plate);
            var w = new Work { Def = def, S = s, Go = go };
            var mb = go.GetComponent<MeshFilter>().sharedMesh.bounds;
            var look = new GameObject("look") { layer = 2 };
            look.transform.SetParent(go.transform, false);
            w.Look = look.AddComponent<BoxCollider>();
            w.Look.isTrigger = true;
            w.Look.center = mb.center; w.Look.size = mb.size;
            float mm = def.BoltMm;
            w.Bolts = new Bolt[def.BoltPos.Length];
            for (int i = 0; i < w.Bolts.Length; i++)
            {
                int n = i;
                w.Bolts[i] = Bolt.Make(go.transform, def.Id + "_bolt" + i, def.BoltPos[i], Quaternion.FromToRotation(Vector3.up, def.BoltDir[i]), def.BoltMm, s.Bolts[i], boltMat,
                                       mm * 0.0016f, mm * 0.0007f, 0.01f, 0.016f);
                w.Bolts[i].Changed = b => { s.Bolts[n] = b.Tight; };
            }
            w.Joints = new Transform[def.JointPos.Length];
            for (int i = 0; i < w.Joints.Length; i++)
            {
                var j = new GameObject("joint" + i).transform;
                j.SetParent(go.transform, false);
                j.localPosition = def.JointPos[i];
                w.Joints[i] = j;
                ShowJoint(j, s.Joints[i]);
            }
            work[s.Id] = w;
            if (sound) G.Sound("CarBuilding", "assemble", go.transform);
        }

        static void ShowJoint(Transform j, int state)
        {
            for (int c = j.childCount - 1; c >= 0; c--) Object.Destroy(j.GetChild(c).gameObject);
            Res.Box(j, "tab", Vector3.zero, new Vector3(0.008f, 0.008f, 0.008f), copperTab);
            if (state == 1) { Res.Cylinder(j, "blob", new Vector3(0.001f, 0.004f, 0f), 0.011f, 0.006f, solderCold); Res.Cylinder(j, "lump", new Vector3(-0.002f, 0.007f, 0.002f), 0.006f, 0.004f, solderCold); }
            else if (state == 2) { Res.Cylinder(j, "fillet", new Vector3(0f, 0.002f, 0f), 0.012f, 0.006f, solderGood); Res.Cylinder(j, "crown", new Vector3(0f, 0.005f, 0f), 0.007f, 0.003f, solderGood); }
        }

        static void Unhang(Work w)
        {
            body.Remove(w.Def.Id);
            work.Remove(w.Def.Id);
            IronManSuit.Data.Hoist.RemoveAll(x => x.Id == w.Def.Id);
        }

        static void SyncReactor()
        {
            var d = IronManSuit.Data;
            var chest = body.Chest;
            body.SetReactor(d.SuitReactor && chest != null, d.SuitCharge, activateT >= 0f);
            if (reactorCol != null) Object.Destroy(reactorCol.gameObject);
            reactorCol = null;
            if (chest == null) return;
            var g = new GameObject("reactor_look") { layer = 2 };
            g.transform.SetParent(chest.transform, false);
            g.transform.localPosition = body.ChestSocket + new Vector3(0f, 0f, 0.03f);
            reactorCol = g.AddComponent<SphereCollider>();
            reactorCol.isTrigger = true;
            reactorCol.radius = 0.06f;
        }

        // ------------------------------------------------------------------------------------------------------------
        static bool AllTight(int[] a) { foreach (var v in a) if (v < Bolt.Steps) return false; return true; }
        static bool AnyTight(int[] a) { foreach (var v in a) if (v > 0) return true; return false; }
        static int Count(int[] a, int v) { int n = 0; foreach (var x in a) if (x == v) n++; return n; }
        static int TightCount(int[] a) { int n = 0; foreach (var x in a) if (x >= Bolt.Steps) n++; return n; }

        // The piece the view is on (nearest look collider hit), with the hit point in its own space.
        static Work LookPiece(out Vector3 local, out float dist)
        {
            local = Vector3.zero; dist = -1f;
            Work best = null; float bd = Reach;
            foreach (var w in work.Values)
            {
                RaycastHit h;
                if (w.Look == null || !G.LookingAt(w.Look, Reach, out h) || h.distance >= bd) continue;
                // a ray through several boxes: prefer the smaller piece it passes through
                float score = h.distance + w.Look.size.magnitude * 0.15f;
                if (best != null && score >= bd) continue;
                bd = score; best = w; local = w.Go.transform.InverseTransformPoint(h.point); dist = h.distance;
            }
            return best;
        }

        static int NearestJoint(Work w)
        {
            // along the view ray: the joint closest to it, within 4 cm
            var ray = new Ray(G.Cam.transform.position, G.Cam.transform.forward);
            int bi = -1; float bd = 0.04f;
            for (int i = 0; i < w.Joints.Length; i++)
            {
                var p = w.Joints[i].position;
                var to = p - ray.origin;
                float along = Vector3.Dot(to, ray.direction);
                if (along < 0f || along > Reach) continue;
                float off = Vector3.Cross(ray.direction, to).magnitude;
                if (off < bd) { bd = off; bi = i; }
            }
            return bi;
        }

        static string Status(Work w)
        {
            var s = w.S; var def = w.Def;
            var t = def.Name + ": BOLTS " + TightCount(s.Bolts) + "/" + s.Bolts.Length + " (" + def.BoltMm + " MM)  JOINTS " + Count(s.Joints, 2) + "/" + s.Joints.Length;
            if (def.Plated) t += "  PLATE " + Mathf.FloorToInt(s.Plate * 100f) + "%";
            return t;
        }

        // ------------------------------------------------------------------------------------------------------------
        public static void Tick()
        {
            Prompting = false;
            if (root == null || CatalogueUI.Open) return;
            var d = IronManSuit.Data;
            if (activateT >= 0f) { Activating(d); Prompting = G.LookingAt(zone, 3f); return; }
            if (returnT >= 0f) { Returning(d); Prompting = G.LookingAt(zone, 3f); return; }
            if (Vector3.Distance(G.Player.position, root.position) > 4f) { EndSolder(); return; }
            if (Bench.ToolsOut()) { EndSolder(); return; }   // the spanner is out: its bolt highlight speaks

            var held = Items.Instance.HeldItem();
            string hk = held != null ? held.Kind.Id : null;
            Vector3 local; float dist;
            var w = LookPiece(out local, out dist);

            // --- the power switch ---
            if (G.LookingAt(switchCol, Reach))
            {
                Prompting = true;
                bool near = d.SuitActive && Armor.IsStanding && Vector3.Distance(Armor.StandingPos, root.TransformPoint(0f, 0f, StepOut)) < 2.2f;
                if (near)
                {
                    G.Interaction("HOIST POWER - RETURN THE SUIT TO THE HOIST", false, true);
                    if (G.Click || G.UsePressed) Rehang(d);
                    return;
                }
                G.Interaction(d.SuitActive ? "HOIST POWER - THE SUIT IS OFF THE HOIST (STAND IT NEAR THE HOIST TO RETURN IT)" : "HOIST POWER - START THE SUIT", false, true);
                if ((G.Click || G.UsePressed) && !d.SuitActive) TryPower(d);
                return;
            }

            // --- hanging a piece ---
            var pd = Suit.OfItem(hk);
            if (pd != null && (G.LookingNear(zone, 2.2f, 0.2f) || w != null))
            {
                Prompting = true;
                if (d.SuitActive) { G.Interaction("THE SUIT IS ALREADY BUILT"); return; }
                if (work.ContainsKey(pd.Id)) { G.Interaction("THE " + pd.Name + " IS ALREADY ON THE HOIST"); return; }
                if (work.Count > 0 && HoistVariant() != pd.Variant) { G.Interaction("THE HOIST HOLDS A " + Suit.SuitName(HoistVariant()) + " - TAKE IT OFF FIRST"); return; }
                G.Interaction("HANG THE " + pd.Name + " ON THE HOIST");
                G.WantAssemble = true;
                if (G.Click)
                {
                    var s = Suit.Parse(pd, held.Extra);
                    G.DropFromHand();
                    Items.Instance.Remove(held);
                    d.Hoist.Add(s);
                    d.SuitVariant = pd.Variant;
                    Hang(s, true);
                    if (pd.Role == "chest") SyncReactor();
                }
                return;
            }

            // --- the arc reactor into the chest / out of it ---
            var cw = ChestWork();
            if (hk == Parts.ReactorId && cw != null && (w != null && w.Def.Role == "chest" || reactorCol != null && G.LookingAt(reactorCol, Reach)))
            {
                Prompting = true;
                if (d.SuitReactor) G.Interaction("THERE'S A REACTOR IN THE CHEST ALREADY");
                else if (!Suit.Finished(cw.Def, cw.S)) G.Interaction("FINISH THE CHEST FIRST");
                else
                {
                    G.Interaction("FIT THE ARC REACTOR IN THE CHEST");
                    G.WantAssemble = true;
                    if (G.Click)
                    {
                        float c = 1f;
                        if (!string.IsNullOrEmpty(held.Extra)) float.TryParse(held.Extra, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out c);
                        G.DropFromHand();
                        Items.Instance.Remove(held);
                        d.SuitReactor = true; d.SuitCharge = Mathf.Clamp01(c);
                        SyncReactor();
                        G.Sound("CarBuilding", "assemble", root);
                        G.Flash("ARC REACTOR IN", 1.5f);
                    }
                }
                return;
            }
            // --- the Mk3's diffuser plate on over the reactor: it clips into the slot in front of it ---
            if (hk == Mk3.DiffuserId && cw != null && (w != null && w.Def.Role == "chest" || reactorCol != null && G.LookingAt(reactorCol, Reach)))
            {
                Prompting = true;
                if (cw.Def.Variant != 3) G.Interaction("THE DIFFUSER PLATE FITS THE MK3");
                else if (!d.SuitReactor) G.Interaction("FIT THE ARC REACTOR FIRST");
                else if (d.SuitDiffuser) G.Interaction("THERE'S A DIFFUSER PLATE ON ALREADY");
                else
                {
                    G.Interaction("FIT THE DIFFUSER PLATE");
                    G.WantAssemble = true;
                    if (G.Click)
                    {
                        G.DropFromHand();
                        Items.Instance.Remove(held);
                        d.SuitDiffuser = true;
                        SyncReactor();
                        G.Sound("CarBuilding", "assemble", root);
                    }
                }
                return;
            }
            // --- with an empty hand, right-click takes them out again: the plate first, then the reactor. They drop
            // in front of the chest, not into the hand: the game's pick-up throws what's held on right mouse, so a part
            // given to the hand on this click went flying (MODLOG 38) ---
            if (d.SuitReactor && reactorCol != null && G.LookingAt(reactorCol, Reach) && held == null && !G.HoldingObject)
            {
                Prompting = true;
                bool plate = d.SuitDiffuser && cw != null && cw.Def.Variant == 3;
                G.Interaction(plate ? "DIFFUSER PLATE" : "ARC REACTOR " + Mathf.RoundToInt(d.SuitCharge * 100f) + "%");
                G.WantDisassemble = true;
                if (G.RightClick)
                {
                    var at = reactorCol.transform.position;
                    var toward = G.Cam.transform.position - at; toward.y = 0f;
                    toward = toward.sqrMagnitude > 0.001f ? toward.normalized : root.forward;
                    var rot = Quaternion.LookRotation(toward, Vector3.up);
                    var drop = at + toward * 0.2f;
                    var side = Vector3.Cross(Vector3.up, toward) * 0.12f;   // the plate to one side, the reactor the other
                    if (plate)
                    {
                        d.SuitDiffuser = false;
                        Items.Instance.Spawn(Mk3.DiffuserId, drop + side, rot);
                    }
                    else
                    {
                        Items.Instance.Spawn(Parts.ReactorId, drop - side, rot, d.SuitCharge.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                        d.SuitReactor = false; d.SuitDiffuser = false;
                        d.SuitCharge = 0f;
                    }
                    SyncReactor();
                    G.Sound("CarBuilding", "disassemble", root);
                }
                return;
            }

            if (w == null) { EndSolder(); return; }
            var st = w.S; var def = w.Def;

            // --- soldering: the iron in hand ---
            if (hk == Parts.IronId)
            {
                Prompting = true;
                int j = NearestJoint(w);
                if (j < 0) { EndSolder(); G.Interaction(Status(w) + " - AIM AT A JOINT"); return; }
                if (!AllTight(st.Bolts)) { EndSolder(); G.Interaction("BOLT IT TO THE RIG FIRST (" + def.BoltMm + " MM)"); return; }
                if (st.Joints[j] == 2 && !(solderId == def.Id && solderJoint == j)) { G.Interaction("GOOD JOINT"); return; }
                if (G.UseHeld)
                {
                    if (solderId != def.Id || solderJoint != j) { EndSolder(); solderId = def.Id; solderJoint = j; solderT = 0f; }
                    solderT += Time.deltaTime / SolderSeconds;
                    if (Random.value < Time.deltaTime * 6f) G.Sound("CarBuilding", "bolt_screw", w.Joints[j], 0.15f);
                    if (solderT >= 1f)
                    {
                        st.Joints[j] = 2; ShowJoint(w.Joints[j], 2); solderId = null; solderJoint = -1; solderT = 0f;
                        G.Flash(Suit.Soldered(st) ? def.Name + " WIRED UP" : "GOOD JOINT", 1.2f);
                    }
                    else G.Interaction("SOLDERING " + Mathf.FloorToInt(solderT * 100f) + "%");
                    return;
                }
                EndSolder();
                G.Interaction(st.Joints[j] == 1 ? "COLD JOINT - RESOLDER IT (HOLD USE)" : "SOLDER JOINT " + (j + 1) + "/" + st.Joints.Length + " (HOLD USE)", false, true);
                return;
            }
            EndSolder();

            // --- plating: a sheet of steel in hand lays on; an empty hand hammers it ---
            if (hk == Suit.SheetId)
            {
                Prompting = true;
                if (!def.Plated) G.Interaction("THE " + def.Name + " TAKES NO PLATE");
                else if (st.Plate > 0f) G.Interaction(st.Plate >= 1f ? "PLATED" : "HAMMER THE PLATE ON FIRST (EMPTY HAND, HOLD USE)");
                else if (!AllTight(st.Bolts)) G.Interaction("BOLT IT TO THE RIG FIRST (" + def.BoltMm + " MM)");
                else if (!Suit.Soldered(st)) G.Interaction("SOLDER ITS WIRING FIRST");
                else
                {
                    G.Interaction("LAY THE SHEET ON THE " + def.Name);
                    G.WantAssemble = true;
                    if (G.Click)
                    {
                        G.DropFromHand();
                        Items.Instance.Remove(held);
                        st.Plate = 0.02f;
                        Suit.ShowPlate(w.Go, def, st.Plate);
                        G.Sound("CarBuilding", "assemble", w.Go.transform);
                    }
                }
                return;
            }
            if (held == null && !G.HoldingObject && def.Plated && st.Plate > 0f && st.Plate < 1f)
            {
                Prompting = true;
                if (G.UseHeld)
                {
                    st.Plate = Mathf.Min(1f, st.Plate + Time.deltaTime / HammerSeconds);
                    if (Time.time >= clangAt) { clangAt = Time.time + Random.Range(0.28f, 0.4f); Clang(w.Go.transform.position); }
                    if (st.Plate >= 1f) { Suit.ShowPlate(w.Go, def, 1f); G.Flash(Suit.Finished(def, st) ? def.Name + " FINISHED" : "PLATED", 2f); }
                    else G.Interaction("HAMMERING " + Mathf.FloorToInt(st.Plate * 100f) + "%");
                    return;
                }
                G.Interaction("HAMMER THE PLATE TO SHAPE (HOLD USE) " + Mathf.FloorToInt(st.Plate * 100f) + "%", false, true);
                return;
            }

            // --- looking at a piece: its state; with every bolt out, right-click takes it off ---
            Prompting = true;
            if (AnyTight(st.Bolts)) { G.Interaction(Status(w)); return; }
            if (def.Role == "chest" && d.SuitReactor) { G.Interaction("TAKE THE ARC REACTOR OUT FIRST"); return; }
            G.Interaction("TAKE OFF THE " + def.Name + "   (" + Status(w).Substring(def.Name.Length + 2) + ")");
            G.WantDisassemble = true;
            if (G.RightClick)
            {
                var at = w.Go.transform.position;
                var rot = w.Go.transform.rotation;
                var extra = Suit.Extra(st);
                Unhang(w);
                if (def.Role == "chest") SyncReactor();
                G.Sound("CarBuilding", "disassemble", root);
                var toward = G.Cam.transform.position - at; toward.y = 0f;
                Items.Instance.Spawn(def.Item, at + toward.normalized * 0.25f + Vector3.up * 0.05f, rot, extra);
            }
        }

        // The suit on the hoist (the variant of the first piece hung), and the chest's work if it is up.
        static int HoistVariantOf(SaveData d) { return d.Hoist.Count > 0 ? Suit.VariantOf(d.Hoist[0].Id) : Suit.ActiveVariant(d); }
        static int HoistVariant() { return HoistVariantOf(IronManSuit.Data); }
        static Work ChestWork() { foreach (var w in work.Values) if (w.Def.Role == "chest") return w; return null; }

        static void EndSolder()
        {
            if (solderId != null && solderJoint >= 0 && solderT > 0.1f)
            {
                Work w;
                if (work.TryGetValue(solderId, out w) && w.S.Joints[solderJoint] != 2)
                {
                    w.S.Joints[solderJoint] = 1; ShowJoint(w.Joints[solderJoint], 1);
                    G.Flash("COLD JOINT - HOLD THE IRON ON LONGER", 2f);
                }
            }
            solderId = null; solderJoint = -1; solderT = 0f;
        }

        static void Clang(Vector3 at)
        {
            var c = Res.Clip("suit_clang.wav");
            if (sfx != null) { sfx.transform.position = root.position; sfx.pitch = Random.Range(0.85f, 1.15f); sfx.PlayOneShot(c, 0.9f); }
        }

        public static void Play(string clip, float vol = 1f, float pitch = 1f)
        {
            if (sfx == null) return;
            sfx.pitch = pitch;
            sfx.PlayOneShot(Res.Clip(clip), vol);
        }

        // ------------------------------------------------------------------------------------------------------------
        // Power-up: everything finished and bolted, the reactor in and charged.
        static string NotReady(SaveData d)
        {
            var set = Suit.Set(HoistVariant());
            int onHoist = 0;
            foreach (var def in set) if (work.ContainsKey(def.Id)) onHoist++;
            if (onHoist < set.Count) return "THE SUIT NEEDS ALL ITS PIECES ON THE HOIST (" + onHoist + "/" + set.Count + ")";
            foreach (var def in set)
            {
                var w = work[def.Id];
                if (!AllTight(w.S.Bolts)) return "THE " + def.Name + " ISN'T BOLTED ON (" + def.BoltMm + " MM)";
                if (!Suit.Soldered(w.S)) return "THE " + def.Name + "'S WIRING ISN'T SOLDERED";
                if (def.Plated && w.S.Plate < 1f) return "THE " + def.Name + " ISN'T PLATED";
            }
            if (!d.SuitReactor) return "NO ARC REACTOR IN THE CHEST";
            if (d.SuitCharge < 0.05f) return "THE REACTOR IS FLAT - CHARGE IT";
            return null;
        }

        static void TryPower(SaveData d)
        {
            G.Sound("CarBuilding", "assemble", lever.transform, 0.6f);
            var why = NotReady(d);
            SetLever(true);
            if (why != null) { G.Fail(why); SetLever(false); return; }
            activateT = 0f;
            foreach (var w in work.Values)   // the work goes: bolts out of the jig, look colliders off
                for (int c = w.Go.transform.childCount - 1; c >= 0; c--)
                {
                    var ch = w.Go.transform.GetChild(c);
                    if (!body.Owns(ch)) Object.Destroy(ch.gameObject);   // the piece's own stay: reactor, diffuser, faceplate
                }
            work.Clear();
            Play("suit_powerup.wav");
            ModConsole.Log("IronManSuit: suit power-up");
        }

        const float StepOut = 0.85f;
        const float LiftClear = 0.07f;       // how high it lifts to clear the jig's brackets
        const float Walk = 2.6f;         // seconds from hanging to standing (the return plays it backwards)
        static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * x * (x * (x * 6f - 15f) + 10f); }

        // The suit's place on its way off the brackets (u 0) to its feet out front (u 1): it lifts straight up clear of
        // the brackets, steps forward while held up, then lowers onto its feet with a small servo settle.
        static Vector3 WalkPose(float u)
        {
            float up = Smooth(u / 0.25f) * (1f - Smooth((u - 0.8f) / 0.2f));
            float settle = Mathf.Sin(Mathf.Clamp01((u - 0.9f) / 0.1f) * Mathf.PI) * -0.006f;
            return new Vector3(0f, LiftClear * up + settle, StepOut * Smooth((u - 0.2f) / 0.65f));
        }

        static void Activating(SaveData d)
        {
            activateT += Time.deltaTime;
            float t = activateT;
            body.SetReactor(true, d.SuitCharge, t > 0.4f || Random.value < t * 2f);
            body.SetGlow(t > 1.0f ? 1f : (Random.value < t ? 1f : 0f));
            const float Go = 1.5f;           // the lights are on: now it moves
            float prev = t - Time.deltaTime;
            if (t > Go && prev <= Go) Play("suit_servo.wav", 0.9f, 0.9f);                      // lifting off the brackets
            if (t > Go + 0.9f && prev <= Go + 0.9f) Play("suit_servo.wav", 0.9f, 1.05f);       // the step out
            if (t > Go + Walk * 0.9f && prev <= Go + Walk * 0.9f) Play("suit_servo.wav", 0.8f, 0.7f);   // weight on its feet
            float u = Mathf.Clamp01((t - Go) / Walk);
            body.transform.localPosition = WalkPose(u);
            if (t < Go + Walk + 0.3f) { G.Interaction("POWERING UP"); return; }
            activateT = -1f;
            SetLever(false);
            var p = body.transform.position;
            d.SuitActive = true;
            d.SuitVariant = HoistVariantOf(d);
            d.Hoist.Clear();
            d.StandX = p.x; d.StandY = p.y; d.StandZ = p.z; d.StandYaw = Yaw;
            Object.Destroy(body.gameObject);
            body = SuitBody.Make("hoist_suit", root);
            body.Mounts = true;
            reactorCol = null;
            Armor.Stand();
            G.Flash("THE SUIT IS ALIVE. USE IT TO GET IN", 4f);
            ModConsole.Log("IronManSuit: suit standing at " + p.ToString("F2"));
        }

        // The standing suit goes back on the hoist (maintenance or display): its pieces hang finished and bolted, the
        // reactor and its charge stay in, and it walks back to the brackets.
        static void Rehang(SaveData d)
        {
            if (Armor.IsStanding == false) return;
            Armor.Unstand();
            d.SuitActive = false;
            d.Hoist.Clear();
            foreach (var def in Suit.Set(Suit.ActiveVariant(d))) { var s = Suit.Done(def); d.Hoist.Add(s); Hang(s, false); }
            SyncReactor();
            body.transform.localPosition = WalkPose(1f);
            returnT = 0f;
            G.Sound("CarBuilding", "assemble", lever.transform, 0.6f);
            Play("suit_servo.wav", 0.9f, 1.05f);
            SetLever(true);
            ModConsole.Log("IronManSuit: suit returned to the hoist");
        }

        static void Returning(SaveData d)
        {
            returnT += Time.deltaTime;
            body.transform.localPosition = WalkPose(1f - Mathf.Clamp01(returnT / Walk));   // the power-up, played backwards
            if (returnT < Walk + 0.2f) { G.Interaction("RETURNING TO THE HOIST"); return; }
            returnT = -1f;
            body.transform.localPosition = Vector3.zero;
            SetLever(false);
            G.Flash("THE SUIT IS BACK ON THE HOIST", 3f);
        }




    }
}
