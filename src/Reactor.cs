using System.Collections.Generic;
using UnityEngine;

namespace IronManSuit
{
    // The assembled arc reactor: the three CAD components share one origin (reactor axis, back of the cage), so they
    // sit at the same transform. Local +Y is the reactor's front (coils and rim), and it is 86 mm across, 54 mm deep.
    static class Reactor
    {
        // Assembly order on the bench: the cage goes on the pad, the core seats into it, the coil ring goes on top.
        public const string Cage = "reactor_upper.obj", Core = "reactor_base.obj", Coil = "reactor_coil.obj";
        public static readonly string[] Parts = { Cage, Core, Coil };
        public const float RingPlaneY = 0.047f;   // middle of the coil ring, metres from the back

        // The whole reactor, fully powered (studio, the finished item).
        public static GameObject Build(string name = "arc_reactor")
        {
            var root = new GameObject(name);
            var g = Glow.Add(root.transform);
            foreach (var p in Parts) g.AddPart(p);
            g.Set(true, 1f, true);
            return root;
        }

        public static void SetLayer(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform c in go.transform) SetLayer(c.gameObject, layer);
        }
    }

    // A reactor's lights, on whatever parts it has: the blue glow ring (lit only once the reactor is complete and
    // powered, pale clear plastic before), the core's discs and grille (fade in with its charge), and the blue
    // point light spilling onto the copper, rim and cage (arc_reactor_mk1_reference_2).
    class Glow : MonoBehaviour
    {
        struct Slot { public Renderer R; public int I; public string Cls; public Material Mat; public Color Full; }
        readonly List<Slot> slots = new List<Slot>();
        readonly Dictionary<string, GameObject> parts = new Dictionary<string, GameObject>();
        Light light;
        bool ring = true; float core = 1f; bool lightOn = true;

        static readonly Color CoreOff = new Color(0.09f, 0.11f, 0.14f);

        public static Glow Add(Transform root)
        {
            var g = root.gameObject.AddComponent<Glow>();
            var lgo = new GameObject("glow_light");
            lgo.transform.SetParent(root, false);
            lgo.transform.localPosition = new Vector3(0f, Reactor.RingPlaneY, 0f);
            g.light = lgo.AddComponent<Light>();
            g.light.type = LightType.Point;
            g.light.color = new Color(0.45f, 0.82f, 1f);
            g.light.intensity = 2.2f;
            g.light.range = 0.22f;
            g.light.renderMode = LightRenderMode.ForcePixel;
            Res.WorkshopProbe(root, 0.4f).transform.localPosition = new Vector3(0f, 0.027f, 0f);
            return g;
        }

        public bool Has(string part) { return parts.ContainsKey(part); }
        public GameObject Part(string part) { GameObject go; return parts.TryGetValue(part, out go) ? go : null; }

        public GameObject AddPart(string file)
        {
            if (parts.ContainsKey(file)) return parts[file];
            var go = Res.ReactorPart(file, transform);
            go.layer = gameObject.layer;
            parts[file] = go;
            var r = go.GetComponent<MeshRenderer>();
            var names = Res.SubNames(file);
            var mats = r.sharedMaterials;
            for (int i = 0; i < names.Length; i++)
            {
                if (names[i] != "glowring" && names[i] != "glowcore" && !Res.IsCoreGlow(names[i])) continue;
                var s = new Slot { R = r, I = i, Cls = names[i] };
                if (Res.IsCoreGlow(names[i])) { s.Mat = new Material(mats[i]); s.Full = mats[i].color; mats[i] = s.Mat; }   // own copy: fades per reactor
                slots.Add(s);
            }
            r.sharedMaterials = mats;
            Apply();
            return go;
        }

        public void RemovePart(string file)
        {
            GameObject go;
            if (!parts.TryGetValue(file, out go)) return;
            slots.RemoveAll(s => s.R == null || s.R.gameObject == go);
            if (file == Reactor.Coil) blocks = null;   // they're children of the part, going with it
            foreach (var s in go.GetComponent<MeshRenderer>().sharedMaterials) if (s != null && s.name.EndsWith("(Clone)")) Destroy(s);
            Destroy(go);
            parts.Remove(file);
        }

        // Show or hide one material class of a part (the core housing's palladium discs before the wafer goes in).
        public void ShowClass(string file, string cls, bool show)
        {
            var go = Part(file);
            if (go == null) return;
            go.GetComponent<MeshFilter>().sharedMesh = show ? Res.Mesh(file) : Res.MeshWithout(file, cls);
        }

        // The coil ring's windings, per block: null = the finished coil (its own copper), else turns per block (0 =
        // bare, the block grows to full size at Bench.TurnsMin turns).
        GameObject[] blocks;
        public void SetWinding(int[] turns)
        {
            var go = Part(Reactor.Coil);
            if (go == null) return;
            if (turns == null)
            {
                go.GetComponent<MeshFilter>().sharedMesh = Res.Mesh(Reactor.Coil);
                if (blocks != null) foreach (var b in blocks) if (b != null) Destroy(b);
                blocks = null;
                return;
            }
            go.GetComponent<MeshFilter>().sharedMesh = Res.MeshWithout(Reactor.Coil, "copper");
            if (blocks == null || blocks[0] == null)
            {
                blocks = new GameObject[Res.CoilBlocks];
                for (int i = 0; i < blocks.Length; i++)
                {
                    var b = Res.MeshObject("coil_block" + i, Res.CoilBlock(i), Res.ReactorMaterial("copper"), go.transform);
                    b.transform.localPosition = Res.CoilBlockCentre[i];
                    b.layer = go.layer;
                    blocks[i] = b;
                }
            }
            for (int i = 0; i < blocks.Length; i++)
            {
                int t = i < turns.Length ? turns[i] : 0;
                blocks[i].SetActive(t > 0);
                blocks[i].transform.localScale = Vector3.one * Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(t / (float)Bench.TurnsMin));
            }
        }

        // ring: the glow ring and LED strip lit; core: 0..1 charge glow of the core's discs and grille; lamp: the light.
        public void Set(bool ring, float core, bool lamp) { this.ring = ring; this.core = Mathf.Clamp01(core); lightOn = lamp; Apply(); }

        void Apply()
        {
            foreach (var s in slots)
            {
                if (s.R == null) continue;
                var mats = s.R.sharedMaterials;
                if (s.Mat != null) s.Mat.color = Color.Lerp(CoreOff, s.Full, core);
                else mats[s.I] = ring ? Res.ReactorMaterial(s.Cls) : Res.GlowOff;
                s.R.sharedMaterials = mats;
            }
            if (light != null) light.enabled = lightOn && ring;
        }

        void OnDestroy()
        {
            foreach (var s in slots) if (s.Mat != null) Destroy(s.Mat);
        }
    }
}
