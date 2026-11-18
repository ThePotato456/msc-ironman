using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace IronManSuit
{
    // Physical mod items in the world (reactor parts, the charger, the catalogue, parcels). The player picks them up
    // like any vanilla part (tag PART, layer Parts, a rigidbody); kinds with a Use action show a prompt when looked
    // at. Ported from mysummermeth's Items, trimmed to what this mod needs.
    class Items
    {
        public class Kind
        {
            public string Id;
            public string Name;                            // GameObject name; the game prints it as the item label
            public float Mass, AngularDrag = 1f;
            public Quaternion Rest = Quaternion.identity;  // applied on top of the spawn rotation
            public Func<GameObject, string, Collider> Build;   // meshes under the root + its collider (root at origin); gets Extra
            public Func<Item, string> Prompt;              // null: nothing to Use, only carried around
            public Func<Item, bool> Use;                   // true: the item is used up and removed
            public float Reach = 2.4f;                      // how far away its prompt shows, metres
            public Action<Item> Alt;                       // right-click while looking at it (the helmets' eyes)
            public Action<Item> Spawned;
        }

        public class Item { public Kind Kind; public GameObject Go; public Collider Col; public Rigidbody Rb; public Vector3 Safe, Prev; public string Extra; public float HoldBy; }

        public static readonly Dictionary<string, Kind> Kinds = new Dictionary<string, Kind>();
        public static Items Instance;

        readonly List<Item> live = new List<Item>();
        readonly List<KeyValuePair<float, Item>> looked = new List<KeyValuePair<float, Item>>();
        public IEnumerable<Item> Live { get { return live; } }
        public Item LookedAt { get; private set; }

        public const string Pinned = "pinned";   // Extra for an item held in place without physics (in the mailbox)

        public Items() { Instance = this; }

        public static void Add(Kind k) { Kinds[k.Id] = k; }

        public Item HeldItem()
        {
            foreach (var it in live) if (it.Go != null && G.InHand(it.Go.transform)) return it;
            return null;
        }

        public Item First(string kind)
        {
            foreach (var it in live) if (it.Go != null && it.Kind.Id == kind) return it;
            return null;
        }

        public Item Find(GameObject go)
        {
            foreach (var it in live) if (it.Go == go) return it;
            return null;
        }

        public Item Spawn(string kind, Vector3 pos, Quaternion rot, string extra = null)
        {
            Kind k;
            if (!Kinds.TryGetValue(kind, out k)) { MSCLoader.ModConsole.Error("IronManSuit: unknown item kind " + kind); return null; }
            var go = new GameObject(k.Name);
            var col = k.Build(go, extra);
            go.transform.position = pos;
            go.transform.rotation = rot * k.Rest;
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = k.Mass;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.angularDrag = k.AngularDrag;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            rb.isKinematic = extra == Pinned;
            go.layer = LayerMask.NameToLayer("Parts");
            go.tag = "PART";
            var item = new Item { Kind = k, Go = go, Col = col, Rb = rb, Safe = pos, Prev = pos, Extra = extra == Pinned ? Pinned : extra };
            live.Add(item);
            if (k.Spawned != null) k.Spawned(item);
            return item;
        }

        // A new item straight into the empty hand (a part taken off something). It is made in front of the view and held
        // still until the hand has it, so nothing it was inside of can throw it. With a busy hand it is set down gently.
        public Item SpawnInHand(string kind, Quaternion rot, string extra = null)
        {
            var ct = G.Cam.transform;
            var it = Spawn(kind, ct.position + ct.forward * 0.5f, rot, extra);
            if (it == null) return null;
            it.Rb.isKinematic = true;
            if (G.GiveToHand(it.Go)) it.HoldBy = Time.time + 1f;
            else { it.Rb.isKinematic = false; it.Go.transform.position = ct.position + ct.forward * 0.7f; }
            return it;
        }

        static void Release(Item it)
        {
            it.Rb.isKinematic = false;
            it.Rb.velocity = Vector3.zero;
            it.Rb.angularVelocity = Vector3.zero;
        }

        public void Remove(Item it)
        {
            if (it.Go != null) Object.Destroy(it.Go);
            live.Remove(it);
        }

        // Box collider around all renderers (the root is at the origin while it is built), at least 2 cm thick.
        public static Collider Fit(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            var c = go.AddComponent<BoxCollider>();
            c.center = b.center;
            c.size = Vector3.Max(b.size, Vector3.one * 0.02f);
            return c;
        }

        static bool SolidHit(Vector3 from, Vector3 dir, float dist, out RaycastHit best)
        {
            best = default(RaycastHit);
            float bd = float.MaxValue;
            foreach (var h in Physics.RaycastAll(from, dir, dist))
                if (!h.collider.isTrigger && h.rigidbody == null && h.distance < bd) { bd = h.distance; best = h; }
            return bd < float.MaxValue;
        }

        // An item falling fast that passed through a solid surface since the last frame went through the floor: put
        // it back on top of that surface (mysummermeth's fix for small items tunnelling through the ground).
        void Rescue(Item it)
        {
            if (!it.Go.activeInHierarchy || it.Rb == null || it.Rb.isKinematic) { it.Prev = it.Go.transform.position; return; }
            var p = it.Go.transform.position;
            var prev = it.Prev;
            it.Prev = p;
            if (it.Rb.velocity.sqrMagnitude < 0.01f) { it.Safe = p + Vector3.up * 0.02f; return; }
            RaycastHit h;
            var step = p - prev;
            bool under = it.Rb.velocity.y < -5f && step.y < 0f && step.magnitude < 5f &&
                         SolidHit(prev + Vector3.up * 0.05f, step.normalized, step.magnitude + 0.05f, out h) && h.point.y > p.y + 0.02f;
            if (under || p.y < -50f)
            {
                SolidHit(prev + Vector3.up * 0.05f, step.normalized, step.magnitude + 0.05f, out h);
                it.Go.transform.position = under ? h.point + Vector3.up * 0.05f : it.Safe;
                it.Rb.velocity = Vector3.zero;
                it.Rb.angularVelocity = Vector3.zero;
            }
        }

        public void Tick(bool promptsBlocked)
        {
            LookedAt = null;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var it = live[i];
                if (it.Go == null) { live.RemoveAt(i); continue; }
                if (it.Extra == Pinned && it.Go.transform.parent != null)   // picked up out of the mailbox
                {
                    it.Extra = null;
                    it.Rb.isKinematic = false;
                }
                // A part made straight into the hand (SpawnInHand) stays kinematic while the hand carries it, as the game's
                // own pickup holds it, and gets physics back only once it is let go (or if the hand never took it).
                if (it.HoldBy > 0f)
                {
                    if (it.Go.transform.parent != null) it.HoldBy = -1f;   // the hand has it
                    else if (Time.time > it.HoldBy) { it.HoldBy = 0f; Release(it); }
                }
                else if (it.HoldBy < 0f && it.Go.transform.parent == null) { it.HoldBy = 0f; Release(it); }
                Rescue(it);
            }
            if (promptsBlocked) return;
            // The nearest item along the view with something to say; what the hand carries only answers last.
            looked.Clear();
            foreach (var it in live)
            {
                if (it.Kind.Prompt == null) continue;
                float d = G.LookDistance(it.Col, it.Kind.Reach, 0.07f);
                if (d >= 0f) looked.Add(new KeyValuePair<float, Item>(G.InHand(it.Go.transform) ? d + 10f : d, it));
            }
            looked.Sort((x, y) => x.Key.CompareTo(y.Key));
            foreach (var kv in looked)
            {
                var it = kv.Value;
                var prompt = it.Kind.Prompt(it);
                if (prompt == null) continue;
                G.Interaction(prompt, false, it.Kind.Use != null);
                LookedAt = it;
                if (G.RightClick && it.Kind.Alt != null) it.Kind.Alt(it);
                if (G.UsePressed && it.Kind.Use != null && it.Kind.Use(it)) Remove(it);
                break;
            }
        }

        public List<WorldItem> Snapshot()
        {
            var list = new List<WorldItem>();
            foreach (var it in live)
            {
                if (it.Go == null) continue;
                var p = it.Go.transform.position;
                var r = (it.Go.transform.rotation * Quaternion.Inverse(it.Kind.Rest)).eulerAngles;
                list.Add(new WorldItem { Kind = it.Kind.Id, X = p.x, Y = p.y, Z = p.z, RX = r.x, RY = r.y, RZ = r.z, Extra = it.Extra });
            }
            return list;
        }

        public void Restore(List<WorldItem> saved)
        {
            foreach (var w in saved)
                if (Kinds.ContainsKey(w.Kind)) Spawn(w.Kind, new Vector3(w.X, w.Y + 0.02f, w.Z), Quaternion.Euler(w.RX, w.RY, w.RZ), w.Extra);
        }
    }
}
