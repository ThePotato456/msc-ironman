using System.Collections.Generic;
using MSCLoader;
using UnityEngine;

namespace IronManSuit
{
    // The Mk3 (the red and gold suit): its pieces are Suit.cs's (ids "m3_*", meshes suit3_*). This is its helmet as a
    // display piece: it stands on its neck rim on the garage workbench, can be carried like any part, right-click
    // switches its eyes on and off, and Use on the disc on either side (the release button) opens the faceplate.
    // The meshes are assets/models/suit3_helmet_shell.obj and the Faceplate's (tools/suit/mk3_helmet.py). The helmet is
    // empty inside: the shell's mesh is drawn without the bare cage it also holds, and the eyes are the faceplate's.
    static class Mk3
    {
        public const string HelmetId = "mk3_helmet";
        // Its arc reactor's diffuser plate (suit3_diffuser.obj, Suit.PaintDiffuser): a loose part that clips on over the
        // reactor in the chest (Hoist.cs) and comes in the parcel with the chest (Shop.cs Pair). It lies front up.
        public const string DiffuserId = "mk3_diffuser";
        const string HelmetFile = Faceplate.ShellFile;
        const string Lit = "lit", Open = "open";        // tags in the item's Extra ("lit,open"): eyes on, faceplate up

        // The helmet's mesh has its origin at the base of the neck and its rim cut on a plane rising to the back
        // (y + 0.30 z = 0.0448 in mesh space). Turned by RimTilt it rests on that rim, which is then RimDrop above
        // the mesh origin.
        const float RimTilt = 16.7f, RimDrop = 0.0429f;

        public static void Init()
        {
            Items.Add(new Items.Kind
            {
                Id = HelmetId, Name = "mk3 helmet(itemx)", Mass = 3f, AngularDrag = 2f,
                Build = (go, extra) =>
                {
                    var o = Res.MeshObject("helmet", Res.MeshWithout(HelmetFile, Suit.FrameClasses(HelmetFile)), null, go.transform);
                    o.transform.localRotation = Quaternion.Euler(-RimTilt, 0f, 0f);
                    o.transform.localPosition = new Vector3(0f, -RimDrop, 0.0105f);
                    Faceplate.Attach(o).Snap(Has(extra, Open));
                    Paint(o, Has(extra, Lit));
                    var c = go.AddComponent<BoxCollider>();   // the helmet standing on its rim (measured from the mesh)
                    c.center = new Vector3(0f, 0.1095f, 0f);
                    c.size = new Vector3(0.212f, 0.219f, 0.291f);
                    return c;
                },
                Prompt = it =>
                {
                    var fp = it.Go.GetComponentInChildren<Faceplate>();
                    if (fp != null && fp.LookingAtButton()) return fp.Prompt();
                    return Has(it.Extra, Lit) ? "MK3 HELMET - EYES: ON" : "MK3 HELMET - EYES: OFF";
                },
                Use = it =>
                {
                    var fp = it.Go.GetComponentInChildren<Faceplate>();
                    if (fp == null || !fp.LookingAtButton()) return false;
                    fp.Press();
                    it.Extra = Tag(it.Extra, Open, fp.IsOpen);
                    return false;
                },
                Alt = it =>
                {
                    it.Extra = Tag(it.Extra, Lit, !Has(it.Extra, Lit));
                    Paint(it.Go.transform.Find("helmet").gameObject, Has(it.Extra, Lit));
                    G.Sound("CarBuilding", "assemble", it.Go.transform, 0.4f);
                },
            });
            Items.Add(new Items.Kind
            {
                Id = DiffuserId, Name = "diffuser plate(itemx)", Mass = 0.3f, AngularDrag = 2f,
                Build = (go, extra) =>
                {
                    var o = Res.MeshObject("diffuser", Res.Mesh(Suit.DiffuserFile), null, go.transform);
                    o.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);   // its front (+Z) up
                    Suit.PaintDiffuser(o, false);
                    var c = go.AddComponent<BoxCollider>();   // 88 mm across its gasket, 2 cm thick (Items.Fit's least)
                    c.center = new Vector3(0f, 0.0013f, 0f);
                    c.size = new Vector3(0.088f, 0.02f, 0.088f);
                    return c;
                },
            });
        }

        // One material per class of each mesh (the shell, the faceplate, the buttons): the armour's red, gold, dark and
        // eye lenses, the cage's own from Suit.
        static void Paint(GameObject o, bool lit)
        {
            foreach (var m in Faceplate.Meshes(o, HelmetFile))
            {
                var names = Res.SubNames(m.Value);
                var ms = new Material[names.Length];
                for (int i = 0; i < names.Length; i++)
                    ms[i] = Suit.IsGlow(names[i]) ? (lit ? Suit.GlowOn : Suit.GlowOff) : Suit.Mat(names[i]);   // plate_red / gold / dark are the suit's (Suit.InitMaterials)
                m.Key.GetComponent<MeshRenderer>().sharedMaterials = ms;
            }
        }

        // Tags in the helmet's Extra, comma separated (old saves have "lit" alone).
        static bool Has(string extra, string tag) { return extra != null && System.Array.IndexOf(extra.Split(','), tag) >= 0; }
        static string Tag(string extra, string tag, bool on)
        {
            var l = new List<string>(string.IsNullOrEmpty(extra) ? new string[0] : extra.Split(','));
            l.Remove(tag);
            if (on) l.Add(tag);
            return l.Count > 0 ? string.Join(",", l.ToArray()) : null;
        }

        // Once per save the helmet is set on the garage workbench: on the first clear patch of its top, looking from
        // the end away from the reactor's chuck, and facing the open side of the table.
        public static void PlaceHelmet(bool again = false)
        {
            var d = IronManSuit.Data;
            if (d.Mk3HelmetPlaced && !again) return;
            var table = G.Find("YARD/Building/Garage/garage_table");
            if (table == null) { ModConsole.Error("IronManSuit: garage workbench not found, no Mk3 helmet"); return; }
            var col = table.GetComponentInChildren<Collider>();
            var b = col != null ? col.bounds : table.GetComponent<Renderer>().bounds;
            var at = new Vector3(b.center.x - Bench.PadOffset.x, b.max.y + 0.02f, b.center.z - Bench.PadOffset.z);
            if (again) foreach (var it in new System.Collections.Generic.List<Items.Item>(Items.Instance.Live)) if (it.Kind.Id == HelmetId) Items.Instance.Remove(it);
            // the table is the player's own shelf of parts: step along it until nothing stands where the helmet goes
            var along = b.size.x < b.size.z ? Vector3.forward : Vector3.right;
            float half = Mathf.Max(b.extents.x, b.extents.z) - 0.2f;
            float away = Vector3.Dot(Bench.PadOffset, along) > 0f ? -1f : 1f;
            for (float t = half; t >= -half; t -= 0.1f)
            {
                var p = new Vector3(b.center.x, b.max.y + 0.02f, b.center.z) + along * (away * t);
                if (Vector3.Distance(p, Bench.PadPos) < 0.3f || !Clear(p)) continue;
                at = p; break;
            }
            // across the table's short side, toward whichever way is open (the other is the wall)
            var across = b.size.x < b.size.z ? Vector3.right : Vector3.forward;
            var eye = new Vector3(b.center.x, b.max.y + 0.35f, b.center.z);
            if (Free(eye, -across) > Free(eye, across)) across = -across;
            Items.Instance.Spawn(HelmetId, at, Quaternion.LookRotation(across));
            d.Mk3HelmetPlaced = true;
            ModConsole.Log("IronManSuit: Mk3 helmet on the workbench at " + at.ToString("F2"));
        }

        // Nothing solid in the space the helmet would stand in (the sphere clears the table top itself).
        static bool Clear(Vector3 onTable)
        {
            foreach (var c in Physics.OverlapSphere(onTable + Vector3.up * 0.17f, 0.14f)) if (!c.isTrigger) return false;
            return true;
        }

        static float Free(Vector3 from, Vector3 dir)
        {
            float best = 3f;
            foreach (var h in Physics.RaycastAll(from, dir, 3f))
                if (!h.collider.isTrigger && h.rigidbody == null && h.distance < best) best = h.distance;
            return best;
        }
    }

    // The Mk3 helmet's faceplate and its release buttons, as children of a helmet whose own mesh is the shell (piece
    // space: the helmet's joint at the origin, facing +Z). The faceplate is suit3_helmet_face.obj, cut as the suit model
    // cuts it (reference/models/ironman_mk3/mk3_suit_model.blend: maska.003 and maska.002, the gold brow, face and chin
    // with the eye and mouth recesses). The buttons are the raised discs on the ear housings (suit3_helmet_button.obj,
    // the right one; the left is its mirror). Pressing one sinks it and opens or closes the plate: it slides Lift forward
    // off the shell, then turns OpenAngle degrees, front up, about Hinge, coming to rest just over the crown.
    // tools/suit/mk3_helmet.py prints these numbers (HINGE, LIFT, OPEN, PRESS and the button's frame).
    class Faceplate : MonoBehaviour
    {
        public const string ShellFile = "suit3_helmet_shell.obj", FaceFile = "suit3_helmet_face.obj", ButtonFile = "suit3_helmet_button.obj";
        static readonly Vector3 Hinge = new Vector3(0f, 0.172f, -0.004f), Lift = new Vector3(0f, 0f, 0.012f);
        const float OpenAngle = 80f, Seconds = 1.1f;
        static readonly Vector3 ButtonAt = new Vector3(0.0919f, 0.1125f, -0.0056f), ButtonNormal = new Vector3(0.9828f, -0.1552f, -0.1003f);
        const float PressDepth = 0.003f, ButtonReach = 1.6f;

        public GameObject Face;
        public readonly GameObject[] Buttons = new GameObject[2];   // right, left
        readonly SphereCollider[] looks = new SphereCollider[2];
        Transform hinge;
        public bool IsOpen { get; private set; }
        float t, pressed;
        AudioSource servo;

        // The faceplate and buttons onto a helmet object (its mesh: the shell). Painting is the caller's (Meshes).
        public static Faceplate Attach(GameObject helmet)
        {
            var fp = helmet.AddComponent<Faceplate>();
            fp.hinge = new GameObject("face_hinge").transform;
            fp.hinge.SetParent(helmet.transform, false);
            fp.Face = Res.MeshObject("face", Res.Mesh(FaceFile), null, fp.hinge);
            fp.Face.transform.localPosition = -Hinge;
            for (int i = 0; i < 2; i++)
            {
                var b = Res.MeshObject(i == 0 ? "button_r" : "button_l", Res.Mesh(ButtonFile), null, helmet.transform);
                if (i == 1) b.transform.localScale = new Vector3(-1f, 1f, 1f);
                fp.Buttons[i] = b;
                var g = new GameObject("button_look") { layer = 2 };   // Ignore Raycast: only the view ray asks it
                g.transform.SetParent(helmet.transform, false);
                g.transform.localPosition = i == 0 ? ButtonAt : Mirror(ButtonAt);
                fp.looks[i] = g.AddComponent<SphereCollider>();
                fp.looks[i].isTrigger = true;
                fp.looks[i].radius = 0.032f;
            }
            fp.Pose();
            return fp;
        }

        // The helmet's meshes with their files: the shell (the helmet object itself, when its file is given), the
        // faceplate and the buttons. A helmet without a faceplate gives the shell alone.
        public static IEnumerable<KeyValuePair<GameObject, string>> Meshes(GameObject helmet, string shellFile)
        {
            if (shellFile != null) yield return new KeyValuePair<GameObject, string>(helmet, shellFile);
            var fp = helmet.GetComponent<Faceplate>();
            if (fp == null) yield break;
            yield return new KeyValuePair<GameObject, string>(fp.Face, FaceFile);
            foreach (var b in fp.Buttons) yield return new KeyValuePair<GameObject, string>(b, ButtonFile);
        }

        static Vector3 Mirror(Vector3 v) { return new Vector3(-v.x, v.y, v.z); }

        // A child of the helmet that is this faceplate's: its hinge, a button, or a button's look sphere.
        public bool Owns(Transform child)
        {
            if (child == hinge) return true;
            for (int i = 0; i < 2; i++)
                if (Buttons[i] != null && child == Buttons[i].transform || looks[i] != null && child == looks[i].transform) return true;
            return false;
        }

        // Straight to open or closed (a helmet just built, or a suit that stood with its faceplate up).
        public void Snap(bool open) { IsOpen = open; t = open ? 1f : 0f; Pose(); }

        // Open or close it from where it is now, with the servo.
        public void Set(bool open)
        {
            if (open == IsOpen) return;
            IsOpen = open;
            if (servo == null) { servo = gameObject.AddComponent<AudioSource>(); servo.spatialBlend = 1f; servo.minDistance = 0.6f; servo.maxDistance = 12f; }
            servo.pitch = open ? 1.35f : 1.2f;
            servo.PlayOneShot(Res.Clip("suit_servo.wav"), 0.5f);
        }

        // A press on a button: it sinks and springs back, and the faceplate goes the other way.
        public void Press() { pressed = 1f; G.Sound("CarBuilding", "assemble", transform, 0.25f); Set(!IsOpen); }

        public bool LookingAtButton()
        {
            foreach (var c in looks) if (c != null && G.LookingAt(c, ButtonReach)) return true;
            return false;
        }
        public string Prompt() { return IsOpen ? "PRESS THE RELEASE: CLOSE THE FACEPLATE" : "PRESS THE RELEASE: OPEN THE FACEPLATE"; }

        void Update()
        {
            float to = IsOpen ? 1f : 0f;
            if (Mathf.Approximately(t, to) && pressed <= 0f) return;
            t = Mathf.MoveTowards(t, to, Time.deltaTime / Seconds);
            pressed = Mathf.Max(0f, pressed - Time.deltaTime / 0.35f);
            Pose();
        }

        // t 0..1: the slide off the shell over the first quarter, the turn over the rest (overlapping a little).
        void Pose()
        {
            float slide = Smooth(t / 0.25f), turn = Smooth((t - 0.15f) / 0.85f);
            hinge.localPosition = Hinge + Lift * slide;
            hinge.localRotation = Quaternion.Euler(-OpenAngle * turn, 0f, 0f);
            float push = PressDepth * Mathf.Sin(Mathf.Clamp01(pressed) * Mathf.PI);
            Buttons[0].transform.localPosition = -ButtonNormal * push;
            Buttons[1].transform.localPosition = -Mirror(ButtonNormal) * push;
        }
        static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
    }
}
