using HutongGames.PlayMaker;
using MSCLoader;
using UnityEngine;

namespace IronManSuit
{
    // Thin wrappers over the game's PlayMaker globals and common objects (names from the scene dump).
    static class G
    {
        public static Transform Player, FpsCam, FpsCamInner;
        public static Camera Cam;

        static FsmFloat money;
        static FsmBool handLeft, handRight, helmet, seated, drinkOn, guiBuy, guiUse;
        static FsmString guiInteraction, guiSubtitle;
        static FsmBool handEmpty;
        static PlayMakerFSM pickUp;

        // GUIinteraction is cleared every frame by whatever the player looks at, so our messages are re-set
        // each frame until they expire.
        static string flash;
        static float flashUntil;

        public static void Init()
        {
            Player = GameObject.Find("PLAYER").transform;
            FpsCam = Player.Find("Pivot/AnimPivot/Camera/FPSCamera");
            FpsCamInner = FpsCam.Find("FPSCamera");
            Cam = FpsCamInner.GetComponent<Camera>();
            var g = FsmVariables.GlobalVariables;
            money = g.FindFsmFloat("PlayerMoney");
            handLeft = g.FindFsmBool("PlayerHandLeft");
            handRight = g.FindFsmBool("PlayerHandRight");
            helmet = g.FindFsmBool("PlayerHelmet");
            seated = g.FindFsmBool("PlayerSeated");
            drinkOn = g.FindFsmBool("PlayerDrinkOn");
            guiBuy = g.FindFsmBool("GUIbuy");
            guiUse = g.FindFsmBool("GUIuse");
            guiInteraction = g.FindFsmString("GUIinteraction");
            guiSubtitle = g.FindFsmString("GUIsubtitle");
            var hand = FpsCam.Find("1Hand_Assemble/Hand");
            pickUp = hand != null ? hand.GetPlayMaker("PickUp") : null;
            handEmpty = pickUp != null ? pickUp.FsmVariables.FindFsmBool("HandEmpty") : null;
        }

        public static float Money { get { return money.Value; } set { money.Value = value; } }
        public static bool HandLeft { get { return handLeft.Value; } set { handLeft.Value = value; } }
        public static bool HandRight { get { return handRight.Value; } set { handRight.Value = value; } }
        public static bool Helmet { get { return helmet.Value; } }
        public static bool Seated { get { return seated.Value; } }
        public static bool Drinking { get { return drinkOn.Value; } }
        public static bool HoldingObject { get { return handEmpty != null && !handEmpty.Value; } }

        // Put a loose part into the empty hand as if it were clicked: the PickUp FSM takes its RaycastHitObject from
        // "State 1" on (the part checks, the pivot, "Part picked"). The part is held where it is, so it's set in front
        // of the view first. False if the hand is busy.
        public static bool GiveToHand(GameObject go)
        {
            if (pickUp == null || HoldingObject || pickUp.ActiveStateName != "Look for object") return false;
            var ct = Cam.transform;
            go.transform.position = ct.position + ct.forward * 0.5f;
            pickUp.FsmVariables.FindFsmGameObject("RaycastHitObject").Value = go;
            Goto(pickUp, "State 1");
            return true;
        }

        // An FSM into a state by name (this PlayMaker has no SetState; Fsm.SwitchState isn't public).
        static readonly System.Reflection.MethodInfo switchState =
            typeof(Fsm).GetMethod("SwitchState", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        static void Goto(PlayMakerFSM f, string state)
        {
            foreach (var st in f.FsmStates) if (st.Name == state) { switchState.Invoke(f.Fsm, new object[] { st }); return; }
        }

        // Let go of what the hand holds, as the game's own drop does (tests).
        public static void DropFromHand() { if (pickUp != null && HoldingObject) pickUp.SendEvent("PROCEED Drop"); }   // "Part picked" -> "Drop part"

        // In the player's hand (the game parents a picked-up item to its ItemPivot).
        public static bool InHand(Transform t) { return t.parent != null && t.parent.name == "ItemPivot"; }

        public static bool UsePressed { get { return cInput.GetKeyDown("Use"); } }
        public static bool UseHeld { get { return cInput.GetKey("Use"); } }
        public static bool Click { get { return Input.GetMouseButtonDown(0); } }
        public static bool RightClick { get { return Input.GetMouseButtonDown(1); } }
        public static bool MouseHeld { get { return Input.GetMouseButton(0); } }

        // The game's assemble / disassemble hand icons (vanilla Assembly and Removal FSMs set these). Ours only clears
        // them if it set them, so a vanilla part's icon in view isn't switched off.
        static bool assembleOurs, disassembleOurs;
        public static void AssembleIcon(bool on)
        {
            var v = FsmVariables.GlobalVariables.FindFsmBool("GUIassemble");
            if (on) { v.Value = true; assembleOurs = true; }
            else if (assembleOurs) { v.Value = false; assembleOurs = false; }
        }
        public static void DisassembleIcon(bool on)
        {
            var v = FsmVariables.GlobalVariables.FindFsmBool("GUIdisassemble");
            if (on) { v.Value = true; disassembleOurs = true; }
            else if (disassembleOurs) { v.Value = false; disassembleOurs = false; }
        }

        // Game time in hours, counted by the mod (the clock wraps at 24 h; sleeping jumps it forward). At the top of
        // an hour the minutes reset a frame before the hour ticks over, so a small step back is that glitch (keep the
        // last reading) and a big one is midnight. From mysummermeth's MailOrder.
        // Game time passing, in hours. GlobalHour only steps every 2 hours (the sun's Color blocks), so the fine count
        // comes from the master clock's hour hand (SuomiClock "Time" FSM publishes TimeRotationHour; it reads
        // 60 - 30 * hour, mod 360, so it turns backwards as time goes on). The hand repeats every 12 hours; a sleep
        // longer than that is recovered from GlobalHour. A hand nudged back a little by a RESETCLOCK snap would read
        // as ~12 hours: that's ignored when GlobalHour hasn't moved.
        // While a save loads, the game sets its clocks from the save (and the hands snap 2 s after a WAKEUP): none of
        // that is time passing, so for the first seconds after load the count only follows along.
        static float lastHand = -1f, settleUntil, sleptAt = -100f;
        static int lastHour;
        public static void CountTime(ref float hours)
        {
            var g = FsmVariables.GlobalVariables;
            float hand = g.FindFsmFloat("TimeRotationHour").Value;
            int hour = g.FindFsmInt("GlobalHour").Value;
            if (g.FindFsmBool("PlayerSleeps").Value) sleptAt = Time.time;
            if (lastHand < 0f || Time.time < settleUntil) { lastHand = hand; lastHour = hour; return; }
            float step = Mathf.Repeat(lastHand - hand, 360f) / 30f;
            float coarse = Mathf.Repeat(hour - lastHour, 24f);
            if (step > 6f && coarse < 3f) step = 0f;
            else if (coarse >= step + 10f) step += 12f;
            // Time only leaps when the player sleeps (the clock hands snap a couple of seconds after waking). Any other
            // leap is a clock being set (a slow save load, another mod) and doesn't count.
            if (step > 0.5f)
            {
                bool slept = Time.time - sleptAt < 15f;
                ModConsole.Log("IronManSuit: game clock jumped " + step.ToString("0.0") + " h" + (slept ? " (slept)" : ", ignored"));
                if (!slept) step = 0f;
            }
            hours += step;
            lastHand = hand; lastHour = hour;
        }
        public static void ResetClock() { lastHand = -1f; settleUntil = Time.time + 8f; }

        public static void Interaction(string text, bool buy = false, bool use = false)
        {
            // A message (Flash/Fail) outranks a prompt while it lasts: the HUD reads the text before LateTick runs.
            guiInteraction.Value = flash != null && Time.time <= flashUntil ? flash : text;
            guiBuy.Value = buy;
            guiUse.Value = use;
        }

        public static void Subtitle(string text) { guiSubtitle.Value = text; }

        public static void Flash(string text, float seconds = 2.5f)
        {
            flash = text;
            flashUntil = Time.time + seconds;
        }

        // Called from Update after everything else so the flash wins over the vanilla "nothing in view" clear.
        // Bench and hoist both ask for the hand icons during the frame; LateTick shows what anyone asked for.
        public static bool WantAssemble, WantDisassemble;

        public static void LateTick()
        {
            AssembleIcon(WantAssemble);
            DisassembleIcon(WantDisassemble);
            WantAssemble = WantDisassemble = false;
            if (flash == null) return;
            if (Time.time > flashUntil) { flash = null; guiInteraction.Value = ""; return; }
            guiInteraction.Value = flash;
        }

        public static void Swear()
        {
            MasterAudio.PlaySound3DAtTransformAndForget("Swearing", FpsCam);
        }

        public static void Sound(string group, string variation, Transform at, float vol = 1f)
        {
            MasterAudio.PlaySound3DAtTransformAndForget(group, at, vol, null, 0f, variation);
        }

        // Fail message: text on screen + the player cusses, like "NOT ENOUGH MONEY".
        public static void Fail(string text)
        {
            Flash(text, 2.5f);
            Swear();
        }

        public static bool LookingAt(Collider c, float range, out RaycastHit hit)
        {
            var ray = new Ray(Cam.transform.position, Cam.transform.forward);
            return c.Raycast(ray, out hit, range);
        }

        // Forgiving check for small items: the view ray passes within `slack` metres of the collider's centre.
        public static bool LookingNear(Collider c, float range, float slack)
        {
            if (LookingAt(c, range)) return true;
            var ray = new Ray(Cam.transform.position, Cam.transform.forward);
            var to = c.bounds.center - ray.origin;
            float along = Vector3.Dot(to, ray.direction);
            return along > 0 && along < range && Vector3.Cross(ray.direction, to).magnitude < slack;
        }

        // How far along the view a collider is when LookingNear would accept it, else -1.
        public static float LookDistance(Collider c, float range, float slack)
        {
            RaycastHit h;
            if (LookingAt(c, range, out h)) return h.distance;
            var ray = new Ray(Cam.transform.position, Cam.transform.forward);
            var to = c.bounds.center - ray.origin;
            float along = Vector3.Dot(to, ray.direction);
            return along > 0 && along < range && Vector3.Cross(ray.direction, to).magnitude < slack ? along : -1f;
        }

        public static bool LookingAt(Collider c, float range)
        {
            RaycastHit h;
            return LookingAt(c, range, out h);
        }

        public static Transform Find(string path)
        {
            var parts = path.Split('/');
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go.transform.parent != null || go.name != parts[0] || go.hideFlags != HideFlags.None) continue;
                var t = go.transform;
                for (int i = 1; i < parts.Length && t != null; i++) t = t.Find(parts[i]);
                if (t != null) return t;
            }
            return null;
        }


        public static void SetLayerRecursive(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayerRecursive(t.GetChild(i), layer);
        }

        public static void StripFsms(GameObject go)
        {
            foreach (var f in go.GetComponentsInChildren<PlayMakerFSM>(true)) Object.Destroy(f);
        }

        public static float F(string s) { return float.Parse(s, System.Globalization.CultureInfo.InvariantCulture); }
    }
}
