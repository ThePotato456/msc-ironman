using System.Collections.Generic;
using HutongGames.PlayMaker;
using MSCLoader;
using UnityEngine;

namespace IronManSuit
{
    // Finished pieces worn for testing (Phase 3): hold one and press Use to put it on, Z (a keybind) takes them all off.
    // They're only armour here: no power, no flight. The helmet's inside shows as the visor overlay (SuitHud).
    static class Wear
    {
        static SuitBody body;

        public static void Init() { body = null; Sync(); }

        public static IEnumerable<string> WornIds() { return IronManSuit.Data.Worn; }
        public static bool Wearing(string id) { return IronManSuit.Data.Worn.Contains(id); }
        // A worn piece in the same place on the body (the Mk1 and the Mk3 pieces of a slot exclude each other).
        public static Suit.PieceDef InSlot(string slot) { foreach (var id in IronManSuit.Data.Worn) { var w = Suit.Of(id); if (w != null && w.Slot == slot) return w; } return null; }
        public static bool Any { get { return IronManSuit.Data.Worn.Count > 0; } }

        public static bool PutOn(Suit.PieceDef d, Items.Item it)
        {
            var data = IronManSuit.Data;
            if (data.InSuit) { G.Flash("YOU'RE IN THE SUIT", 2f); return false; }
            var there = InSlot(d.Slot);
            if (there != null) { G.Flash("YOU'RE ALREADY WEARING A " + there.Name, 2f); return false; }
            G.DropFromHand();
            data.Worn.Add(d.Id);
            Sync();
            Hoist.Play("suit_servo.wav", 0.6f, 1.2f);
            G.Flash(d.Name + " ON. " + TakeOffKey() + " TAKES THE PIECES OFF", 2.5f);
            return true;
        }

        public static string TakeOffKey() { return IronManSuit.KeyTakeOff != null ? IronManSuit.KeyTakeOff.GetKeyValue.ToString().ToUpperInvariant() : "Z"; }

        public static void TakeAllOff()
        {
            var data = IronManSuit.Data;
            if (data.Worn.Count == 0) return;
            var p = G.Player.position + G.Player.forward * 0.6f;
            int n = 0;
            foreach (var id in data.Worn)
            {
                var d = Suit.Of(id);
                if (d == null) continue;
                Items.Instance.Spawn(d.Item, p + G.Player.right * (-0.5f + 0.25f * (n % 5)) + Vector3.up * (0.3f + 0.2f * (n / 5)), G.Player.rotation, Suit.Extra(Suit.Done(d)));
                n++;
            }
            data.Worn.Clear();
            Sync();
            G.Sound("CarBuilding", "disassemble", G.Player);
        }

        public static void Sync()
        {
            var data = IronManSuit.Data;
            if (data.Worn.Count == 0 || data.InSuit) { if (body != null) Object.Destroy(body.gameObject); body = null; return; }
            if (body == null) body = SuitBody.Make("worn_pieces", G.Player);
            foreach (var id in new List<string>(body.Parts.Keys)) if (!data.Worn.Contains(id)) body.Remove(id);
            foreach (var id in data.Worn) { var d = Suit.Of(id); if (d != null && !body.Has(id)) body.Add(d, true); }
            body.SetRenderers("helmet", false);   // the camera is inside it: the visor overlay stands in
            body.SetRenderers("chest", false);    // its collar fills the view from the eyes
            Armor.Attach(body);
        }

        public static void Tick()
        {
            if (body != null) { Armor.Attach(body); body.gameObject.SetActive(!G.Seated); }
            var k = IronManSuit.KeyTakeOff;
            if (Any && !CatalogueUI.Open && (k != null ? k.GetKeybindDown() : Input.GetKeyDown(KeyCode.Z))) TakeAllOff();
        }

        public static bool HelmetOn { get { return InSlot("helmet") != null && !IronManSuit.Data.InSuit; } }
    }

    // The finished suit (Phase 4): standing on its own, or worn. Use at it to get in (it opens round the player and
    // closes up); in it: V flies, R fires the right palm's repulsor, G gets out (on the ground), Y lifts the faceplate.
    // Flight and repulsors run off the arc reactor in its chest; standing near the charger (switched on) recharges it.
    static class Armor
    {
        public const float FlightSeconds = 900f;   // a full reactor flies ~15 real minutes (twice as fast on boost)
        const float RepulsorCost = 0.004f, RepulsorCooldown = 0.6f, ChargeHours = 1f, ChargerReach = 2f;
        const float Cruise = 8f, Boost = 25f, Climb = 4f, Accel = 10f, BoostAccel = 22f;

        static SuitBody standing, worn;
        static CapsuleCollider standCol;
        static float lastHours = -1f;
        static float enterT = -1f;
        static readonly Dictionary<string, Vector3> openOffset = new Dictionary<string, Vector3>();

        public static bool Flying { get; private set; }
        static bool landing;
        static Vector3 vel;
        static float repulsorReady;
        public static bool FaceplateUp;
        static AudioSource thrust, oneShot;
        static LineRenderer beam;
        static Light palmLight;
        static float beamUntil;
        static Behaviour motor, input;
        static PlayMakerFSM crouch;
        public static bool Prompting;

        public static bool InSuit { get { return IronManSuit.Data.InSuit; } }
        public static float Altitude { get; private set; }
        public static float Speed { get { return vel.magnitude; } }
        public static bool RepulsorReady { get { return Time.time >= repulsorReady; } }

        public static void Init()
        {
            standing = worn = null; standCol = null; lastHours = -1f; enterT = -1f; Flying = false; landing = false; vel = Vector3.zero;
            thrust = oneShot = null; beam = null; palmLight = null; motor = input = null; crouch = null; FaceplateUp = false;
            openOffset.Clear();
            foreach (var d in Suit.Pieces)
            {
                float sx = Mathf.Sign(d.Joint.x);
                Vector3 o;
                switch (d.Role)
                {
                    case "helmet": o = new Vector3(0f, 0.22f, -0.12f); break;
                    case "chest": o = new Vector3(0f, 0.04f, 0.3f); break;
                    case "pauldron": o = new Vector3(sx * 0.22f, 0.12f, 0f); break;
                    case "gauntlet": o = new Vector3(sx * 0.25f, 0f, 0.12f); break;
                    case "thigh": o = new Vector3(sx * 0.16f, 0f, 0.14f); break;
                    default: o = new Vector3(sx * 0.12f, 0f, 0.2f); break;
                }
                openOffset[d.Id] = o;
            }
            foreach (var b in G.Player.GetComponents<Behaviour>())
            {
                var n = b.GetType().Name;
                if (n == "CharacterMotor") motor = b;
                else if (n == "FPSInputController") input = b;
            }
            foreach (var f in G.Player.GetComponents<PlayMakerFSM>()) if (f.FsmName == "Crouch") crouch = f;
            var d0 = IronManSuit.Data;
            if (d0.InSuit && d0.SuitActive) PutOnBody(false);
            else { d0.InSuit = false; if (d0.SuitActive) Stand(); }
        }

        // the empty suit standing where it was left
        public static void Stand()
        {
            var d = IronManSuit.Data;
            if (standing != null) Object.Destroy(standing.gameObject);
            standing = SuitBody.Make("IronManSuitStanding", null);
            standing.transform.position = new Vector3(d.StandX, d.StandY, d.StandZ);
            standing.transform.rotation = Quaternion.Euler(0f, d.StandYaw, 0f);
            foreach (var def in Suit.Set(Suit.ActiveVariant(d))) standing.Add(def, true);
            standing.SetReactor(d.SuitReactor, d.SuitCharge, d.SuitReactor && d.SuitCharge > 0f);
            standing.SetGlow(d.SuitCharge > 0f ? 1f : 0f);
            standing.SetFaceplate(FaceplateUp, true);
            Res.WorkshopProbe(standing.transform, 3f).transform.localPosition = new Vector3(0f, 1f, 0f);
            var c = new GameObject("suit_col");
            c.transform.SetParent(standing.transform, false);
            standCol = c.AddComponent<CapsuleCollider>();
            standCol.center = new Vector3(0f, 0.95f, 0f);
            standCol.height = 1.9f;
            standCol.radius = 0.27f;
        }

        // the empty standing suit (null when absent or worn), and taking it off its feet to rehang it on the hoist
        public static bool IsStanding { get { return standing != null && !IronManSuit.Data.InSuit; } }
        public static Vector3 StandingPos { get { return standing.transform.position; } }
        public static void Unstand()
        {
            if (standing != null) Object.Destroy(standing.gameObject);
            standing = null; standCol = null;
        }

        // Feet of the player's capsule, in PLAYER space.
        public static void Attach(SuitBody b)
        {
            var cc = G.Player.GetComponent<CharacterController>();
            float foot = cc != null ? cc.center.y - cc.height / 2f : -0.3f;
            if (b.transform.parent != G.Player) b.transform.SetParent(G.Player, false);
            b.transform.localPosition = new Vector3(0f, foot, 0f);
            b.transform.localRotation = Quaternion.identity;
        }

        static void PutOnBody(bool animate)
        {
            var d = IronManSuit.Data;
            if (worn != null) Object.Destroy(worn.gameObject);
            worn = SuitBody.Make("IronManSuitWorn", G.Player);
            foreach (var def in Suit.Set(Suit.ActiveVariant(d))) worn.Add(def, true);
            worn.SetReactor(d.SuitReactor, d.SuitCharge, d.SuitReactor && d.SuitCharge > 0f);
            worn.SetGlow(d.SuitCharge > 0f ? 1f : 0f);
            worn.SetRenderers("helmet", false);
            worn.SetRenderers("chest", false);   // collar fills the view from the eyes
            Attach(worn);
            if (animate) { enterT = 0f; Pose(0f); }
        }

        static void Pose(float closed)
        {
            float e = closed * closed * (3f - 2f * closed);
            foreach (var kv in worn.Parts)
            {
                var def = Suit.Of(kv.Key);
                kv.Value.transform.localPosition = def.Joint + openOffset[kv.Key] * (1f - e);
            }
        }

        static void Enter()
        {
            var d = IronManSuit.Data;
            if (Wear.Any) { G.Flash("TAKE THE TEST PIECES OFF FIRST (" + Wear.TakeOffKey() + ")", 2.5f); return; }
            if (G.HoldingObject) { G.Flash("PUT DOWN WHAT YOU'RE CARRYING", 2f); return; }
            var cc = G.Player.GetComponent<CharacterController>();
            float foot = cc != null ? cc.center.y - cc.height / 2f : -0.3f;
            var feet = standing.transform.position;
            G.Player.position = feet - Vector3.up * foot + Vector3.up * 0.02f;
            G.Player.rotation = Quaternion.Euler(0f, d.StandYaw, 0f);
            Object.Destroy(standing.gameObject);
            standing = null; standCol = null;
            d.InSuit = true;
            PutOnBody(true);
            Play("suit_servo.wav", 1f, 0.9f);
            ModConsole.Log("IronManSuit: in the suit");
        }

        static void Leave()
        {
            var d = IronManSuit.Data;
            if (Flying) { G.Flash("LAND FIRST", 1.5f); return; }
            var cc = G.Player.GetComponent<CharacterController>();
            if (cc != null && !cc.isGrounded && !Grounded()) { G.Flash("LAND FIRST", 1.5f); return; }
            var p = worn.transform.position;
            d.StandX = p.x; d.StandY = p.y; d.StandZ = p.z; d.StandYaw = G.Player.eulerAngles.y;
            Object.Destroy(worn.gameObject);
            worn = null;
            d.InSuit = false;
            // step back out of it
            var back = -G.Player.forward;
            if (cc != null) cc.Move(back * 0.65f); else G.Player.position += back * 0.65f;
            Stand();
            Play("suit_servo.wav", 1f, 1.1f);
            ModConsole.Log("IronManSuit: out of the suit");
        }

        static bool Grounded()
        {
            return Physics.Raycast(G.Player.position, Vector3.down, 1.3f, ~(1 << 2 | 1 << 12 | 1 << LayerMask.NameToLayer("Player")));
        }

        static void Play(string clip, float vol = 1f, float pitch = 1f)
        {
            if (oneShot == null)
            {
                oneShot = G.Player.gameObject.AddComponent<AudioSource>();
                oneShot.spatialBlend = 0f;
            }
            oneShot.pitch = pitch;
            oneShot.PlayOneShot(Res.Clip(clip), vol);
        }

        // ------------------------------------------------------------------------------------------------------------
        public static void Tick()
        {
            Prompting = false;
            var d = IronManSuit.Data;
            if (!d.SuitActive) return;
            float dh = 0f;
            if (lastHours >= 0f) dh = Mathf.Max(0f, d.GameHours - lastHours);
            lastHours = d.GameHours;

            if (standing != null) StandingTick(d, dh);
            if (worn == null || !d.InSuit) return;

            Attach(worn);
            worn.gameObject.SetActive(!G.Seated);
            if (enterT >= 0f)
            {
                enterT += Time.deltaTime / 1.1f;
                Pose(Mathf.Clamp01(enterT));
                if (enterT >= 1f) { enterT = -1f; G.Flash("SUIT ON. " + Keys(), 4f); }
                return;
            }
            if (G.Seated || CatalogueUI.Open) { if (Flying) EndFlight(); return; }

            if (Down(IronManSuit.KeyFaceplate, KeyCode.Y)) { FaceplateUp = !FaceplateUp; Play("suit_servo.wav", 0.5f, 1.4f); }
            if (Down(IronManSuit.KeyLeave, KeyCode.G)) { Leave(); return; }
            if (Down(IronManSuit.KeyFly, KeyCode.V))
            {
                if (Flying) landing = true;
                else if (d.SuitCharge <= 0f) G.Fail("NO POWER - CHARGE THE REACTOR");
                else StartFlight();
            }
            if (Down(IronManSuit.KeyRepulsor, KeyCode.R)) Repulsor(d);
            if (Flying) FlyTick(d);
            else worn.SetThrust(0f);
            if (beam != null && Time.time > beamUntil) { beam.enabled = false; if (palmLight != null) palmLight.enabled = false; }
            RaycastHit gh;
            Altitude = Physics.Raycast(G.Player.position, Vector3.down, out gh, 500f, ~(1 << 2 | 1 << 12 | 1 << LayerMask.NameToLayer("Player"))) ? gh.distance : 500f;
            worn.SetGlow(d.SuitCharge > 0f ? 1f : 0f);
        }

        public static string Keys()
        {
            return Name(IronManSuit.KeyFly, "V") + " FLY  " + Name(IronManSuit.KeyRepulsor, "R") + " REPULSOR  " + Name(IronManSuit.KeyLeave, "G") + " GET OUT  " + Name(IronManSuit.KeyFaceplate, "Y") + " FACEPLATE";
        }
        static string Name(SettingsKeybind k, string dflt) { return k != null ? k.GetKeyValue.ToString().ToUpperInvariant() : dflt; }
        static bool Down(SettingsKeybind k, KeyCode dflt) { return k != null ? k.GetKeybindDown() : Input.GetKeyDown(dflt); }

        static void StandingTick(SaveData d, float dh)
        {
            // recharge: the charger (switched on, not in hand) standing beside it
            Charger near = null;
            foreach (var c in Charger.All)
                if (c != null && !G.InHand(c.transform) && Vector3.Distance(c.transform.position, standing.transform.position + Vector3.up * 0.5f) < ChargerReach) near = c;
            bool charging = near != null && d.ChargerOn && d.SuitReactor && d.SuitCharge < 1f;
            if (charging)
            {
                bool was = d.SuitCharge > 0f;
                d.SuitCharge = Mathf.Min(1f, d.SuitCharge + dh * Bench.ChargeSpeed / ChargeHours);
                if (!was && d.SuitCharge > 0f) { standing.SetGlow(1f); }
                standing.SetReactor(true, d.SuitCharge, d.SuitCharge > 0f);
                if (d.SuitCharge >= 1f) G.Flash("SUIT CHARGED", 2.5f);
            }
            if (G.HoldingObject && Items.Instance.HeldItem() == null) return;
            // the Mk3's faceplate release: the disc on either side of the helmet (asked before the suit's own capsule,
            // which the view misses at head height)
            var fp = standing.Faceplate;
            if (fp != null && fp.LookingAtButton())
            {
                Prompting = true;
                G.Interaction(fp.Prompt(), false, true);
                if (G.UsePressed) { fp.Press(); FaceplateUp = fp.IsOpen; }
                return;
            }
            if (standCol == null || !G.LookingAt(standCol, 2f)) return;
            Prompting = true;
            string power = d.SuitReactor ? (charging ? "CHARGING " : "POWER ") + Mathf.FloorToInt(d.SuitCharge * 100f) + "%" : "NO REACTOR";
            G.Interaction("GET IN THE SUIT (" + power + ")", false, true);
            if (G.UsePressed) Enter();
        }

        // ------------------------------------------------------------------------------------------------------------
        static void StartFlight()
        {
            Flying = true; landing = false;
            vel = Vector3.up * 3f;
            if (motor != null) motor.enabled = false;
            if (input != null) input.enabled = false;
            if (crouch != null) crouch.enabled = false;
            if (thrust == null)
            {
                thrust = G.Player.gameObject.AddComponent<AudioSource>();
                thrust.clip = Res.Clip("suit_thrust.wav");
                thrust.loop = true;
                thrust.spatialBlend = 0f;
            }
            thrust.volume = 0.3f;
            thrust.Play();
            Play("suit_repulsor.wav", 0.4f, 0.6f);
            G.Flash("FLIGHT - SPACE UP, C DOWN, SHIFT BOOST. " + Name(IronManSuit.KeyFly, "V") + " TO LAND", 3f);
        }

        static void EndFlight()
        {
            Flying = false; landing = false; vel = Vector3.zero;
            if (motor != null)
            {
                motor.enabled = true;
                var m = motor.GetType().GetMethod("SetVelocity");
                if (m != null) try { m.Invoke(motor, new object[] { Vector3.zero }); } catch { }
            }
            if (input != null) input.enabled = true;
            if (crouch != null) crouch.enabled = true;
            if (thrust != null) thrust.Stop();
            if (worn != null) worn.SetThrust(0f);
        }

        static float Axis(string name, KeyCode pos, KeyCode neg)
        {
            float v = 0f;
            try { v = cInput.GetAxis(name); } catch { v = (Input.GetKey(pos) ? 1f : 0f) - (Input.GetKey(neg) ? 1f : 0f); }
            return Mathf.Clamp(v, -1f, 1f);
        }
        static bool Key(string name, KeyCode k) { try { return cInput.GetKey(name); } catch { return Input.GetKey(k); } }

        static void FlyTick(SaveData d)
        {
            var cc = G.Player.GetComponent<CharacterController>();
            var cam = G.Cam.transform;
            bool boost = Key("Run", KeyCode.LeftShift);
            if (d.SuitCharge <= 0f && !landing) { landing = true; G.Fail("POWER FAILURE"); }
            Vector3 want;
            if (landing) want = Vector3.down * 3f + Vector3.ProjectOnPlane(vel, Vector3.up) * 0.5f;
            else
            {
                float v = Axis("PlayerVertical", KeyCode.W, KeyCode.S), h = Axis("PlayerHorizontal", KeyCode.D, KeyCode.A);
                var flat = Vector3.ProjectOnPlane(cam.right, Vector3.up).normalized;
                want = (cam.forward * v + flat * h) * (boost ? Boost : Cruise);
                if (Key("Jump", KeyCode.Space)) want.y += Climb * (boost ? 2f : 1f);
                if (Key("Crouch", KeyCode.C)) want.y -= Climb * (boost ? 2f : 1f);
            }
            vel = Vector3.MoveTowards(vel, want, (boost ? BoostAccel : Accel) * Time.deltaTime);
            var flags = cc != null ? cc.Move(vel * Time.deltaTime) : CollisionFlags.None;
            if (cc == null) G.Player.position += vel * Time.deltaTime;
            if ((flags & CollisionFlags.Below) != 0 && vel.y < 0f) { vel.y = 0f; if (landing) { EndFlight(); Play("suit_servo.wav", 0.8f, 0.8f); return; } }
            if ((flags & CollisionFlags.Above) != 0 && vel.y > 0f) vel.y = 0f;
            // power: a full charge lasts FlightSeconds (twice as fast on boost)
            d.SuitCharge = Mathf.Max(0f, d.SuitCharge - Time.deltaTime / FlightSeconds * (boost && !landing ? 2f : 1f));
            worn.SetReactor(true, d.SuitCharge, d.SuitCharge > 0f);
            float s01 = Mathf.Clamp01(vel.magnitude / Boost);
            worn.SetThrust(landing ? 0.6f : 0.8f + 0.2f * s01);
            if (thrust != null) { thrust.volume = 0.35f + 0.55f * s01; thrust.pitch = 0.85f + 0.45f * s01; }
        }

        static void Repulsor(SaveData d)
        {
            if (Time.time < repulsorReady) return;
            if (d.SuitCharge < RepulsorCost) { G.Fail("NO POWER"); return; }
            repulsorReady = Time.time + RepulsorCooldown;
            d.SuitCharge -= RepulsorCost;
            var cam = G.Cam.transform;
            bool mk3 = Suit.ActiveVariant(d) == 3;
            var palm = worn.PiecePoint((mk3 ? "m3_" : "m1_") + "gauntlet_r", mk3 ? Suit.Palm3 : Suit.Palm1);
            var dir = cam.forward;
            var end = cam.position + dir * 60f;
            RaycastHit best = default(RaycastHit); bool hit = false; float bd = 61f;
            foreach (var h in Physics.RaycastAll(cam.position + dir * 0.4f, dir, 60f))
            {
                if (h.collider.isTrigger || h.transform.IsChildOf(G.Player) || h.distance >= bd) continue;
                bd = h.distance; best = h; hit = true;
            }
            if (hit)
            {
                end = best.point;
                var rb = best.rigidbody;
                if (rb != null && !rb.isKinematic)
                    rb.AddForceAtPosition(dir * Mathf.Min(2500f, rb.mass * 14f), best.point, ForceMode.Impulse);
            }
            if (beam == null)
            {
                var g = new GameObject("repulsor_beam");
                beam = g.AddComponent<LineRenderer>();
                beam.material = Suit.GlowOn;
                beam.SetVertexCount(2);
                beam.SetWidth(0.06f, 0.02f);
                beam.useWorldSpace = true;
                var lg = new GameObject("palm_light");
                palmLight = lg.AddComponent<Light>();
                palmLight.type = LightType.Point;
                palmLight.color = new Color(0.55f, 0.85f, 1f);
                palmLight.range = 6f;
                palmLight.intensity = 4f;
            }
            beam.SetPosition(0, palm);
            beam.SetPosition(1, end);
            beam.enabled = true;
            palmLight.transform.position = palm + dir * 0.2f;
            palmLight.enabled = true;
            beamUntil = Time.time + 0.22f;
            Play("suit_repulsor.wav", 1f, Random.Range(0.95f, 1.05f));
        }

    }

    // The view from inside the helmet: its dark inside round the eye slit, and in the powered suit the amber readouts.
    // The game's own prompt line is under the visor's dark band, so it's repeated above it.
    class SuitHud : MonoBehaviour
    {
        Texture2D visor, white;
        GUIStyle big, small, prompt;
        FsmString interaction;
        static readonly Color Amber = new Color(1f, 0.72f, 0.2f);

        public static void Make() { if (FindObjectOfType<SuitHud>() == null) new GameObject("IronManSuitHud").AddComponent<SuitHud>(); }

        void Start()
        {
            visor = Res.Tex("suit_visor.png");
            white = new Texture2D(1, 1); white.SetPixel(0, 0, Color.white); white.Apply();
            interaction = FsmVariables.GlobalVariables.FindFsmString("GUIinteraction");
        }

        void OnDestroy() { if (white != null) Destroy(white); }

        void OnGUI()
        {
            var d = IronManSuit.Data;
            bool inSuit = d.InSuit && !G.Seated;
            bool helmet = inSuit ? !Armor.FaceplateUp : Wear.HelmetOn && !G.Seated;
            if (!helmet && !inSuit) return;
            if (big == null)
            {
                big = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
                small = new GUIStyle(big);
                prompt = new GUIStyle(big) { alignment = TextAnchor.MiddleCenter };
                big.normal.textColor = small.normal.textColor = prompt.normal.textColor = Amber;
            }
            GUI.depth = 10;
            float W = Screen.width, H = Screen.height;
            if (helmet && Event.current.type == EventType.Repaint)
            {
                var c = GUI.color; GUI.color = new Color(1f, 1f, 1f, 0.94f);
                GUI.DrawTexture(new Rect(0, 0, W, H), visor, ScaleMode.StretchToFill);
                GUI.color = c;
                var p = interaction != null ? interaction.Value : "";
                if (!string.IsNullOrEmpty(p))
                {
                    prompt.fontSize = Mathf.RoundToInt(H * 0.028f);
                    GUI.Label(new Rect(0, H * 0.66f, W, H * 0.05f), p.ToUpperInvariant(), prompt);
                }
            }
            if (!inSuit) return;
            big.fontSize = Mathf.RoundToInt(H * 0.03f);
            small.fontSize = Mathf.RoundToInt(H * 0.02f);
            float x0 = W * (helmet ? 0.12f : 0.03f), y0 = H * (helmet ? 0.2f : 0.05f);
            float pw = d.SuitCharge;
            GUI.Label(new Rect(x0, y0, W * 0.3f, H * 0.04f), "PWR " + Mathf.FloorToInt(pw * 100f) + "%", big);
            // power bar
            if (Event.current.type == EventType.Repaint)
            {
                var c = GUI.color;
                GUI.color = new Color(Amber.r, Amber.g, Amber.b, 0.25f);
                GUI.DrawTexture(new Rect(x0, y0 + H * 0.045f, W * 0.14f, H * 0.008f), white);
                GUI.color = pw < 0.15f && Mathf.Repeat(Time.time, 0.6f) < 0.3f ? new Color(1f, 0.2f, 0.1f) : Amber;
                GUI.DrawTexture(new Rect(x0, y0 + H * 0.045f, W * 0.14f * pw, H * 0.008f), white);
                GUI.color = c;
            }
            var mode = Armor.Flying ? "FLIGHT" : "WALK";
            GUI.Label(new Rect(x0, y0 + H * 0.06f, W * 0.3f, H * 0.03f), mode + "   ALT " + Armor.Altitude.ToString("0.0") + " M   SPD " + Mathf.RoundToInt(Armor.Speed * 3.6f) + " KM/H", small);
            GUI.Label(new Rect(x0, y0 + H * 0.09f, W * 0.3f, H * 0.03f), Armor.RepulsorReady ? "REPULSOR READY" : "REPULSOR CHARGING", small);
            GUI.Label(new Rect(x0, H * (helmet ? 0.62f : 0.94f), W * 0.8f, H * 0.03f), Armor.Keys(), small);
            // crosshair
            if (Event.current.type == EventType.Repaint)
            {
                var c = GUI.color; GUI.color = new Color(Amber.r, Amber.g, Amber.b, 0.7f);
                float s = H * 0.012f;
                GUI.DrawTexture(new Rect(W / 2 - s, H / 2 - 1, s * 0.7f, 2), white);
                GUI.DrawTexture(new Rect(W / 2 + s * 0.3f, H / 2 - 1, s * 0.7f, 2), white);
                GUI.DrawTexture(new Rect(W / 2 - 1, H / 2 - s, 2, s * 0.7f), white);
                GUI.DrawTexture(new Rect(W / 2 - 1, H / 2 + s * 0.3f, 2, s * 0.7f), white);
                GUI.color = c;
            }
        }
    }
}
