using MSCLoader;
using UnityEngine;

namespace IronManSuit
{
    // The reactor stand: a tripod with a ball joint and three claws, bought from the catalogue (page 1). It holds one
    // spare arc reactor, front out, between the claws. It snaps on like a vanilla part (see Tick): the check mark,
    // then click; right-click takes the reactor out again, charge and all. Mesh: assets/models/reactor_stand.obj (tools/reactor/build_stand.py).
    static class Stand
    {
        public const string Id = "reactor_stand";
        const string MeshFile = "reactor_stand.obj";

        // From build_stand.py: the reactor's front points along Axis, and its middle sits at Cradle (stand space, metres).
        static readonly Vector3 Axis = new Vector3(0f, 0.7071f, 0.7071f);
        static readonly Vector3 Cradle = new Vector3(0f, 0.1364f, 0.0368f);
        const float HalfDepth = 0.027f;   // the reactor's own origin is the back of its cage, which is 54 mm deep

        static Material steel, dark, ball, brass;

        public static void Init()
        {
            steel = Res.Mat("stand_steel", new Color(0.72f, 0.73f, 0.76f), "reactor_steel_albedo.png", null, 0.8f, 0.5f);
            dark = Res.Mat("stand_dark", new Color(0.16f, 0.17f, 0.19f), null, null, 0.4f, 0.35f);
            ball = Res.Mat("stand_ball", new Color(0.88f, 0.89f, 0.92f), null, null, 1f, 0.8f);
            brass = Res.ReactorMaterial("brass");
            Items.Add(new Items.Kind
            {
                Id = Id, Name = "reactor stand(itemx)", Mass = 2f, AngularDrag = 2f,
                Build = (go, extra) =>
                {
                    var o = Res.MeshObject("stand", Res.Mesh(MeshFile), null, go.transform);
                    var names = Res.SubNames(MeshFile);
                    var ms = new Material[names.Length];
                    for (int i = 0; i < names.Length; i++)
                        switch (names[i])
                        {
                            case "stand_dark": ms[i] = dark; break;
                            case "stand_ball": ms[i] = ball; break;
                            case "stand_brass": ms[i] = brass; break;
                            default: ms[i] = steel; break;
                        }
                    o.GetComponent<MeshRenderer>().sharedMaterials = ms;
                    var col = Items.Fit(go);   // the box round the stand itself, before a reactor is seated
                    if (extra != null) Seat(go.transform, Charge(extra));
                    return col;
                },
            });
        }

        // Like any vanilla part: carry a reactor near the claws and the check mark shows, click to snap it in. On a
        // full stand, look at it with an empty hand and right-click to take the reactor out (the icon shows it).
        const float SnapRadius = 0.45f;   // how near the held reactor must be to the cradle's middle
        const float LookRange = 1.6f;     // how far away a full stand can be looked at to take its reactor out
        public static bool Prompting;

        public static void Tick()
        {
            Prompting = false;
            if (Items.Instance == null || CatalogueUI.Open) return;
            var held = Items.Instance.HeldItem();
            bool reactor = held != null && held.Kind.Id == Parts.ReactorId;
            foreach (var st in new System.Collections.Generic.List<Items.Item>(Items.Instance.Live))
            {
                if (st.Kind.Id != Id || st.Go == null) continue;
                if (st.Extra == null)
                {
                    if (!reactor || Vector3.Distance(held.Col.bounds.center, st.Go.transform.TransformPoint(Cradle)) > SnapRadius) continue;
                    Prompting = true;
                    G.WantAssemble = true;
                    G.Interaction("SET THE ARC REACTOR IN THE STAND");
                    if (G.Click) PutIn(st, held);
                    return;
                }
                if (held != null || G.HoldingObject || G.LookDistance(st.Col, LookRange, 0.1f) < 0f) continue;
                Prompting = true;
                G.WantDisassemble = true;
                G.Interaction("ARC REACTOR " + Mathf.RoundToInt(Charge(st.Extra) * 100f) + "%");
                if (G.RightClick) TakeOut(st);
                return;
            }
        }

        static float Charge(string extra)
        {
            float c = 1f;
            if (!string.IsNullOrEmpty(extra)) float.TryParse(extra, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out c);
            return Mathf.Clamp01(c);
        }

        // The reactor in the claws: its back is half a depth behind the cradle's middle, and its front (local +Y) points
        // out along the axis.
        static void Seat(Transform stand, float charge)
        {
            var r = new GameObject("reactor");
            r.transform.SetParent(stand, false);
            r.transform.localRotation = Quaternion.FromToRotation(Vector3.up, Axis);
            r.transform.localPosition = Cradle - Axis * HalfDepth;
            var g = Glow.Add(r.transform);
            foreach (var p in Reactor.Parts) g.AddPart(p);
            g.Set(charge > 0.01f, charge, charge > 0.01f);
        }

        static void PutIn(Items.Item stand, Items.Item reactor)
        {
            string charge = string.IsNullOrEmpty(reactor.Extra) ? "1" : reactor.Extra;
            G.DropFromHand();
            Items.Instance.Remove(reactor);
            stand.Extra = charge;
            Seat(stand.Go.transform, Charge(charge));
            G.Sound("CarBuilding", "assemble", stand.Go.transform, 0.5f);
        }

        static void TakeOut(Items.Item stand)
        {
            var t = stand.Go.transform;
            var r = t.Find("reactor");
            if (r != null) Object.Destroy(r.gameObject);
            // set down just in front of the stand (clear of its box), face up, to be picked up from there
            Items.Instance.Spawn(Parts.ReactorId, t.TransformPoint(new Vector3(0f, 0.05f, 0.2f)), t.rotation, stand.Extra);
            stand.Extra = null;
            G.Sound("CarBuilding", "disassemble", t, 0.5f);
        }

    }
}
