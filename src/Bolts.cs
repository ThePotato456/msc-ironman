using HutongGames.PlayMaker;
using UnityEngine;

namespace IronManSuit
{
    // A bolt the game's own spanners work on. The player's spanner (FPSCamera/2Spanner/Raycast) mouse-picks anything
    // named "BoltPM" within 1 m, reads the bolt's size from its x scale (spanner size in cm: 0.8 = 8 mm), highlights
    // the bolt's first child when the spanner fits (Check FSM "Set Material"), and turns the scroll wheel into
    // tighten/loosen steps: 0.28 s apart with a spanner, 0.08 s with the ratchet. Those events would go to a vanilla
    // bolt's own FSM; ours has none, so this reads the same scroll while the spanner's Check FSM has picked this bolt
    // with a matching size, at the same pace. Tightness runs 0 (out) to Steps (tight); the head turns and sinks.
    class Bolt : MonoBehaviour
    {
        public const int Steps = 8;
        public int Tight;
        public System.Action<Bolt> Changed;
        Transform head;
        float rise;

        static PlayMakerFSM check;
        static FsmGameObject checkBolt;
        static FsmBool ratchetSwitch;
        static float nextAt;

        // A bolt at pos (parent space), its head's axis along rot * up, of sizeMm (5..15). headD / headH: the head's
        // look in metres (small, to suit the reactor). rise: how far the head stands proud when fully out. colR: the
        // radius the spanner can catch it within.
        public static Bolt Make(Transform parent, string label, Vector3 pos, Quaternion rot, int sizeMm, int tight, Material mat, float headD = 0.006f, float headH = 0.003f, float rise = 0.004f, float colR = 0.008f)
        {
            float s = sizeMm / 10f;
            var go = new GameObject("BoltPM");   // the name the spanner looks for
            go.layer = 12;                       // as the game's bolts
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.transform.localScale = Vector3.one * s;
            var col = go.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = colR / s;
            var b = go.AddComponent<Bolt>();
            b.rise = rise;
            b.Tight = Mathf.Clamp(tight, 0, Steps);
            // the head: first child, so the spanner's highlight lands on it (a hex prism: a 6-sided cylinder look)
            var hgo = new GameObject(label);
            hgo.layer = 19;
            hgo.transform.SetParent(go.transform, false);
            b.head = hgo.transform;
            var hex = Res.Cylinder(hgo.transform, "hex", Vector3.zero, headD / s, headH / s, mat);
            hex.layer = 19;
            var washer = Res.Cylinder(hgo.transform, "washer", new Vector3(0f, -headH / s * 0.55f, 0f), headD * 1.35f / s, headH * 0.25f / s, mat);
            washer.layer = 19;
            b.Pose();
            return b;
        }

        void Pose()
        {
            float out01 = 1f - Tight / (float)Steps;
            head.localPosition = Vector3.up * (rise * out01 / transform.localScale.x);
            head.localRotation = Quaternion.Euler(0f, -Tight * 45f, 0f);
        }

        public void Set(int tight) { Tight = Mathf.Clamp(tight, 0, Steps); Pose(); }

        static void Find()
        {
            var t = G.FpsCam.Find("2Spanner/Raycast");
            if (t == null) return;
            foreach (var f in t.GetComponents<PlayMakerFSM>()) if (f.FsmName == "Check") check = f;
            if (check != null) checkBolt = check.FsmVariables.FindFsmGameObject("Bolt");
            // the ratchet's direction switch (an FSM named "Switch" with a bool "Switch") somewhere under the tool
            foreach (var f in G.FpsCam.Find("2Spanner").GetComponentsInChildren<PlayMakerFSM>(true))
                if (f.FsmName == "Switch") { ratchetSwitch = f.FsmVariables.FindFsmBool("Switch"); break; }
        }

        public static void ResetScene() { check = null; checkBolt = null; ratchetSwitch = null; nextAt = 0f;
        }


        // The spanner is on this bolt and fits it.
        public bool SpannerOn(out bool ratchet)
        {
            ratchet = false;
            if (check == null) { Find(); if (check == null) return false; }
            if (!check.gameObject.activeInHierarchy || checkBolt == null || checkBolt.Value != gameObject) return false;
            var st = check.ActiveStateName;
            ratchet = st.EndsWith(" 2");
            return st.StartsWith("Get Mouse scroll") || st.StartsWith("Delay");
        }

        void Update()
        {
            bool ratchet;
            if (!SpannerOn(out ratchet)) return;
            float s = Input.GetAxis("Mouse ScrollWheel");
            if (s == 0f || Time.time < nextAt) return;
            int dir = s > 0f ? 1 : -1;
            if (ratchet && ratchetSwitch != null) dir = ratchetSwitch.Value ? 1 : -1;
            nextAt = Time.time + (ratchet ? 0.08f : 0.28f);
            int n = Mathf.Clamp(Tight + dir, 0, Steps);
            if (n == Tight) return;
            Tight = n;
            Pose();
            G.Sound("CarBuilding", "bolt_screw", transform);
            if (Changed != null) Changed(this);
        }
    }
}
