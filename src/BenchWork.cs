using MSCLoader;
using UnityEngine;

namespace IronManSuit
{
    // The hand work on the reactor in the chuck (cave Mk1 build): its bolts, solder joints, coil winding and the first
    // light. Positions are in the reactor's own space (stackT: origin at the cage's back, +Y out of its front; the
    // direction at angle a around the axis is (cos a, 0, sin a)).
    static partial class Bench
    {
        public const int TurnsMin = 28, TurnsMax = 32, TurnsMost = 40;
        const int CoreBoltMm = 8, RingBoltMm = 7;
        static readonly float[] CoreBoltAngles = { 331f, 91f, 211f };            // on the core's top ring, between its joints
        static readonly float[] JointAngles = { 11f, 51f, 131f, 171f, 251f, 291f };
        static readonly float[] RingBoltAngles = { 39.85f, 147.85f, 255.85f };   // on the ring's top band, between coil blocks
        const float CoreTopR = 0.0235f, CoreTopY = 0.0541f, RingBoltR = 0.030f, RingTopY = 0.0537f;
        const float SolderSeconds = 1.5f, WindPerSecond = 3.5f, TestSeconds = 2.6f;

        static Transform work;                 // the bolts and joints of what's in the chuck, rebuilt by SyncWork
        static Bolt[] coreBolts, ringBolts;
        static Transform[] joints;
        static Material solderGood, solderCold, copperTab, boltMat;
        static int solderJoint = -1; static float solderT;
        static float windAcc;
        static bool lastPowered;
        static float testEnd;
        static string testFault;
        static bool flickerOn;
        public static bool Testing { get { return testEnd > 0f; } }
        public static bool TestFailed;

        static void ResetWork()
        {
            work = null; coreBolts = ringBolts = null; joints = null; solderGood = null;   // materials go with the scene
            solderJoint = -1; solderT = 0f; windAcc = 0f; lastPowered = false; testEnd = 0f; testFault = null; flickerOn = false; TestFailed = false;
            spannerTool = null;
        }

        static Vector3 Around(float deg, float r, float y)
        {
            float a = deg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
        }

        // (Re)build the bolts and joints to match the saved state of the parts in the chuck.
        static void SyncWork()
        {
            if (stackT == null) return;
            if (work != null) Object.Destroy(work.gameObject);
            coreBolts = ringBolts = null; joints = null; solderJoint = -1;
            var d = IronManSuit.Data;
            work = new GameObject("reactor_work").transform;
            work.SetParent(stackT, false);
            if (solderGood == null)
            {
                solderGood = Res.Mat("solder_good", new Color(0.86f, 0.87f, 0.89f), null, null, 0.9f, 0.85f);
                solderCold = Res.Mat("solder_cold", new Color(0.5f, 0.5f, 0.48f), null, null, 0.4f, 0.12f);
                copperTab = Res.ReactorMaterial("copper");
                boltMat = Res.Mat("reactor_bolt", new Color(0.78f, 0.79f, 0.82f), "reactor_steel_albedo.png", null, 0.85f, 0.6f);
            }

            if (d.BenchStage >= CoreOn)
            {
                coreBolts = new Bolt[3];
                for (int i = 0; i < 3; i++)
                {
                    int n = i;
                    coreBolts[i] = Bolt.Make(work, "core_bolt" + i, Around(CoreBoltAngles[i], CoreTopR, CoreTopY), Quaternion.identity, CoreBoltMm, d.CoreBolts[i],
                                             boltMat, 0.0055f, 0.0028f, 0.004f, 0.006f);
                    coreBolts[i].Changed = b => IronManSuit.Data.CoreBolts[n] = b.Tight;
                }
                joints = new Transform[6];
                for (int i = 0; i < 6; i++)
                {
                    var j = new GameObject("joint" + i).transform;
                    j.SetParent(work, false);
                    j.localPosition = Around(JointAngles[i], CoreTopR, CoreTopY);
                    j.localRotation = Quaternion.Euler(0f, 90f - JointAngles[i], 0f);
                    joints[i] = j;
                    ShowJoint(i, d.Joints[i]);
                }
            }
            if (d.BenchStage >= RingOn)
            {
                ringBolts = new Bolt[3];
                for (int i = 0; i < 3; i++)
                {
                    int n = i;
                    ringBolts[i] = Bolt.Make(work, "ring_bolt" + i, Around(RingBoltAngles[i], RingBoltR, RingTopY), Quaternion.identity, RingBoltMm, d.RingBolts[i],
                                             boltMat, 0.005f, 0.0025f, 0.004f, 0.006f);
                    ringBolts[i].Changed = b => IronManSuit.Data.RingBolts[n] = b.Tight;
                }
            }
        }

        // A joint: a bare copper tab (0), a dull lumpy blob (1, cold) or a bright smooth fillet (2).
        static void ShowJoint(int i, int state)
        {
            var j = joints[i];
            for (int c = j.childCount - 1; c >= 0; c--) Object.Destroy(j.GetChild(c).gameObject);
            Res.Box(j, "tab", new Vector3(0f, 0.0004f, 0f), new Vector3(0.0022f, 0.0008f, 0.0045f), copperTab);
            if (state == 1)
            {
                Res.Cylinder(j, "blob", new Vector3(0.0003f, 0.0011f, 0f), 0.0036f, 0.0016f, solderCold);
                Res.Cylinder(j, "lump", new Vector3(-0.0006f, 0.0019f, 0.0005f), 0.002f, 0.0012f, solderCold);
            }
            else if (state == 2)
            {
                Res.Cylinder(j, "fillet", new Vector3(0f, 0.0009f, 0f), 0.0034f, 0.0012f, solderGood);
                Res.Cylinder(j, "crown", new Vector3(0f, 0.0016f, 0f), 0.0022f, 0.0008f, solderGood);
            }
        }

        // Where the view meets the reactor in the chuck, in its own space (false: not looking at it).
        static bool LookPoint(out Vector3 p)
        {
            p = Vector3.zero;
            RaycastHit h;
            if (stackCol == null || !stackCol.enabled || !G.LookingAt(stackCol, 1.2f, out h)) return false;
            p = stackT.InverseTransformPoint(h.point);
            return true;
        }

        static int NearestJoint(Vector3 p)
        {
            int best = -1; float bd = 0.008f;
            for (int i = 0; i < 6; i++)
            {
                var q = Around(JointAngles[i], CoreTopR, CoreTopY);
                float dd = new Vector2(p.x - q.x, p.z - q.z).magnitude;
                if (dd < bd) { bd = dd; best = i; }
            }
            return best;
        }

        static int BlockAt(Vector3 p)
        {
            float a = Mathf.Atan2(p.z, p.x) * Mathf.Rad2Deg;
            return Mathf.FloorToInt(Mathf.Repeat(a - (Res.CoilBlock0 - 18f), 360f) / 36f) % Res.CoilBlocks;
        }

        static bool SpoolNear()
        {
            var s = Items.Instance.First(Parts.WireId);
            return s != null && s.Go != null && !G.InHand(s.Go.transform) && Vector3.Distance(s.Go.transform.position, root.position) < ChargerReach;
        }

        // Soldering (soldering iron in hand) and winding (empty hand, the wire spool on the bench). True: took the look.
        static bool Work(SaveData d)
        {
            Vector3 p;
            bool looking = LookPoint(out p);
            var held = Items.Instance.HeldItem();

            // --- soldering: look at a joint on the core's top ring with the iron in hand, hold Use ---
            if (held != null && held.Kind.Id == Parts.IronId && d.BenchStage >= CoreOn && joints != null)
            {
                int j = looking ? NearestJoint(p) : -1;
                if (j < 0 && solderJoint >= 0) EndSolder(d);
                if (j < 0) return false;
                if (!d.Wafer) { G.Interaction("THE PALLADIUM GOES IN FIRST"); return true; }
                if (d.Joints[j] == 2 && solderJoint != j) { G.Interaction("GOOD JOINT"); return true; }
                if (G.UseHeld)
                {
                    if (solderJoint != j) { if (solderJoint >= 0) EndSolder(d); solderJoint = j; solderT = 0f; }
                    solderT += Time.deltaTime / SolderSeconds;
                    if (Random.value < Time.deltaTime * 6f) G.Sound("CarBuilding", "bolt_screw", stackT, 0.15f);
                    if (solderT >= 1f)
                    {
                        d.Joints[j] = 2; ShowJoint(j, 2); solderJoint = -1; solderT = 0f;
                        G.Flash("GOOD JOINT", 1.2f);
                    }
                    else G.Interaction("SOLDERING " + Mathf.FloorToInt(solderT * 100f) + "%");
                    return true;
                }
                if (solderJoint >= 0) EndSolder(d);
                G.Interaction(d.Joints[j] == 1 ? "COLD JOINT - RESOLDER IT (HOLD USE)" : "SOLDER JOINT " + (j + 1) + " (HOLD USE)", false, true);
                return true;
            }
            if (solderJoint >= 0) EndSolder(d);

            // --- winding: empty hand, look at a coil block on the ring, hold the left button; right-click unwinds ---
            if (d.BenchStage == RingOn && !d.Lit && held == null && !G.HoldingObject && looking &&
                new Vector2(p.x, p.z).magnitude >= 0.027f && SpoolNear())
            {
                int b = BlockAt(p);
                int t = d.Turns[b];
                if (G.MouseHeld && t < TurnsMost)
                {
                    windAcc += Time.deltaTime * WindPerSecond;
                    while (windAcc >= 1f && t < TurnsMost)
                    {
                        windAcc -= 1f; t++;
                        G.Sound("CarBuilding", "bolt_screw", stackT, 0.25f);
                    }
                }
                else windAcc = 0f;
                if (G.RightClick && t > 0) { t = Mathf.Max(0, t - 3); G.Sound("CarBuilding", "disassemble", stackT, 0.4f); }
                if (t != d.Turns[b]) { d.Turns[b] = t; stack.SetWinding(d.Turns); }
                string state = t > TurnsMax ? "TOO MANY - UNWIND" : t >= TurnsMin ? "GOOD" : t > 0 ? "KEEP WINDING" : "HOLD LEFT MOUSE TO WIND";
                G.Interaction("COIL " + (b + 1) + ": " + t + " TURNS (" + TurnsMin + "-" + TurnsMax + ") - " + state);
                return true;
            }
            windAcc = 0f;
            return false;
        }

        // Let go of the iron's button before the joint took: the solder sets dull and grainy, a cold joint.
        static void EndSolder(SaveData d)
        {
            if (solderJoint >= 0 && solderT > 0.1f && d.Joints[solderJoint] != 2)
            {
                d.Joints[solderJoint] = 1; ShowJoint(solderJoint, 1);
                G.Flash("COLD JOINT - HOLD THE IRON ON LONGER", 2f);
            }
            solderJoint = -1; solderT = 0f;
        }

        // What keeps the reactor from lighting, as a hint (null: nothing, it lights).
        static string Fault(SaveData d)
        {
            for (int i = 0; i < 6; i++) if (d.Joints[i] != 2) return "COLD SOLDER JOINT - RESOLDER IT";
            if (!AllTight(d.CoreBolts)) return "A CORE BOLT IS LOOSE (" + CoreBoltMm + " MM)";
            for (int i = 0; i < d.Turns.Length; i++)
                if (d.Turns[i] < TurnsMin || d.Turns[i] > TurnsMax) return "COIL " + (i + 1) + " IS WOUND WRONG (" + TurnsMin + "-" + TurnsMax + " TURNS)";
            if (!AllTight(d.RingBolts)) return "A RING BOLT IS LOOSE (" + RingBoltMm + " MM)";
            if (d.CoreCharge < 1f) return "THE CORE ISN'T CHARGED";
            return null;
        }

        // First light: ring on, both clamps on the core, charger switched on. The ring flickers as it powers up, then
        // either holds (done) or dies with a hint; switching the charger (or a clamp) off and on tries again.
        static void FirstLight(SaveData d)
        {
            var ch = Charger.Current;
            bool powered = d.BenchStage == RingOn && !d.Lit && d.ChargerOn && d.Clamps == 3 && ch != null &&
                           Vector3.Distance(ch.transform.position, root.position) < ChargerReach;
            if (powered && !lastPowered)
            {
                testEnd = Time.time + TestSeconds;
                testFault = Fault(d);
                TestFailed = false;
                G.Sound("CarBuilding", "assemble", stackT, 0.5f);
            }
            lastPowered = powered;
            if (!powered && Testing) { testEnd = 0f; SetFlicker(false); }
            if (!Testing) return;

            float left = testEnd - Time.time;
            // flickering on more and more as it powers up; a faulty one stutters out toward the end
            float on01 = testFault == null ? Mathf.Lerp(0.3f, 0.95f, 1f - left / TestSeconds) : (left > TestSeconds * 0.4f ? 0.5f : 0.15f);
            SetFlicker(Random.value < on01);
            if (left > 0f) return;

            testEnd = 0f;
            SetFlicker(false);
            if (testFault == null)
            {
                d.Lit = true;
                Refresh();
                G.Sound("CarBuilding", "assemble", stackT);
                G.Flash("ARC REACTOR COMPLETE", 4f);
                ModConsole.Log("IronManSuit: first light, arc reactor complete");
            }
            else
            {
                TestFailed = true;
                G.Flash("IT DIES: " + testFault, 4f);
                G.Swear();
                ModConsole.Log("IronManSuit: first light failed: " + testFault);
            }
        }

        static void SetFlicker(bool on)
        {
            if (flickerOn == on) return;
            flickerOn = on;
            Refresh();
        }

    }
}
