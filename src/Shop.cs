using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using MSCLoader;
using UnityEngine;
using Object = UnityEngine.Object;

namespace IronManSuit
{
    // Teho-Tarvike mail order (a made-up supplier), run beside the game's own: catalogue in the player's mailbox ->
    // fill the order form -> post it at the mailbox -> a day or two later the parcel waits on Teimo's counter (the
    // post office) and the phone rings -> pay cash on delivery -> carry it home and open it. The first order also
    // brings the charger, free. Same flow as mysummermeth's Kemi-Kauppa, whose code this follows.
    static class Shop
    {
        public const int None = 0, Filled = 1, Posted = 2, Waiting = 3;
        public const float Delivery = 85f;

        // Pair: a second item kind that comes with each one ordered (the suit's left and right pieces, the Mk3 chest's
        // diffuser plate). Page: 0 the
        // reactor page, 1 the cave Mk1's page, 2 the Mk3's.
        public class Entry { public string Kind, Pair, Line; public float Price; public int Page; }
        // Catalogue page order (tools/reactor/make_print.py PRODUCTS / SUIT_PRODUCTS): line = how it reads on the form.
        public static readonly Entry[] Catalogue =
        {
            new Entry { Kind = Parts.CageId, Line = "Reaktorin kotelo", Price = 650f },
            new Entry { Kind = Parts.HousingId, Line = "Ytimen runko", Price = 1500f },
            new Entry { Kind = Parts.RingId, Line = "Valojohderengas", Price = 950f },
            new Entry { Kind = Parts.WireId, Line = "Kuparilanka, rulla", Price = 500f },
            new Entry { Kind = Stand.Id, Line = "Reaktoriteline", Price = 1200f },
            new Entry { Kind = "suit_m1_helmet", Line = "Kypärän runko", Price = 2400f, Page = 1 },
            new Entry { Kind = "suit_m1_chest", Line = "Rintapanssarin runko", Price = 4200f, Page = 1 },
            new Entry { Kind = "suit_m1_pauldron_r", Pair = "suit_m1_pauldron_l", Line = "Olkapäät, pari", Price = 2800f, Page = 1 },
            new Entry { Kind = "suit_m1_gauntlet_r", Pair = "suit_m1_gauntlet_l", Line = "Kyynärvarret, pari", Price = 3600f, Page = 1 },
            new Entry { Kind = "suit_m1_thigh_r", Pair = "suit_m1_thigh_l", Line = "Reidet, pari", Price = 2200f, Page = 1 },
            new Entry { Kind = "suit_m1_boot_r", Pair = "suit_m1_boot_l", Line = "Saappaat, pari", Price = 3800f, Page = 1 },
            new Entry { Kind = "suit_m3_helmet", Line = "Kypärä MK3", Price = 3200f, Page = 2 },
            new Entry { Kind = "suit_m3_chest", Pair = Mk3.DiffuserId, Line = "Rintapanssari MK3", Price = 5600f, Page = 2 },   // with its diffuser plate
            new Entry { Kind = "suit_m3_pauldron_r", Pair = "suit_m3_pauldron_l", Line = "Olkapäät MK3, pari", Price = 3600f, Page = 2 },
            new Entry { Kind = "suit_m3_gauntlet_r", Pair = "suit_m3_gauntlet_l", Line = "Kyynärv. MK3, pari", Price = 4400f, Page = 2 },
            new Entry { Kind = "suit_m3_thigh_r", Pair = "suit_m3_thigh_l", Line = "Reidet MK3, pari", Price = 3000f, Page = 2 },
            new Entry { Kind = "suit_m3_boot_r", Pair = "suit_m3_boot_l", Line = "Saappaat MK3, pari", Price = 4600f, Page = 2 },
        };
        public const string HoistFree = "suit_hoist";   // not an item: opening the parcel sets the hoist up in the garage
        // Free with the first order (and, for saves from before the iron, the next one): the charger and the iron; with
        // the first suit order, the hoist.
        public static readonly string[][] Freebies = { new[] { Parts.ChargerId, "laturi (kaupan päälle)" }, new[] { Parts.IronId, "juotin (kaupan päälle)" },
                                                       new[] { HoistFree, "nosturi + teline (kaupan päälle)" } };
        public static bool FreeDue(string kind, IEnumerable<string> ordering = null)
        {
            var d = IronManSuit.Data;
            if (kind == HoistFree)
            {
                if (d.HoistOrdered || d.HoistGiven || ordering == null) return false;
                foreach (var k in ordering) if (k.StartsWith("suit_")) return true;
                return false;
            }
            return kind == Parts.ChargerId ? !d.ChargerSent : !d.IronSent;
        }
        public static Entry Find(string kind) { foreach (var e in Catalogue) if (e.Kind == kind) return e; return null; }
        public static bool HasSuit(string order) { return order != null && order.Contains("suit_"); }

        static Transform mailbox, counterParcel;
        static Collider counterCol;
        static Material cardboard, tape, label, cover, pages;

        public static bool Prompting;   // a mail prompt is on screen this frame: item prompts stand down

        // Teimo's counter, measured in mysummermeth (STORE/MESH/Collider, yaw 57.4): top y 4.624, centre C, Front =
        // outward normal of the customer side, Along = toward the register. Its own parcel sits at along +0.28 and
        // its pipe display at -0.31, so ours goes between them.
        static readonly Vector3 CounterC = new Vector3(-1551.142f, 4.624f, 1182.817f);
        static readonly Vector3 Front = new Vector3(0.539f, 0f, -0.842f), Along = new Vector3(-0.842f, 0f, -0.539f);
        static Vector3 OnCounter(float front, float along) { return CounterC + Front * front + Along * along; }

        public static void Init()
        {
            cardboard = Res.Mat("cardboard", Color.white, "im_cardboard.png");
            cardboard.mainTextureScale = new Vector2(2f, 2f);
            tape = Res.Mat("tape", new Color(0.62f, 0.45f, 0.26f), null, null, 0f, 0.7f);
            label = Res.Mat("parcel_label", Color.white, "im_parcel_label.png");
            cover = Res.Mat("catalogue_cover", Color.white, "im_cat_cover.png");
            pages = Res.Mat("catalogue_pages", new Color(0.95f, 0.93f, 0.88f));

            Items.Add(new Items.Kind
            {
                Id = "catalogue", Name = "teho-tarvike catalogue(itemx)", Mass = 0.15f,
                Build = (go, x) =>
                {
                    Res.Box(go.transform, "pages", new Vector3(0f, 0.003f, 0f), new Vector3(0.15f, 0.006f, 0.21f), pages);
                    Res.Quad(go.transform, "cover", new Vector3(0f, 0.0062f, 0f), new Vector2(0.15f, 0.21f), cover, Quaternion.Euler(90f, 0f, 0f));
                    return Items.Fit(go);
                },
                Prompt = it => "READ TEHO-TARVIKE CATALOGUE",
                Use = it => { OpenCatalogue(); return false; },
            });
            Items.Add(new Items.Kind
            {
                Id = "parcel", Name = "teho-tarvike parcel(itemx)", Mass = 3f,
                Build = (go, x) => BuildParcel(go.transform),
                Prompt = it => "OPEN PARCEL",
                Use = it => { Unpack(it); return true; },
            });

            mailbox = G.Find("YARD/PlayerMailBox");
            counterParcel = null;
            var d = IronManSuit.Data;
            if (!d.CatalogueGiven && mailbox != null)
            {
                // On the mailbox floor under the hatch, a little above where mysummermeth's catalogue lies.
                Items.Instance.Spawn("catalogue", mailbox.position + new Vector3(0f, 0.075f, 0f), Quaternion.Euler(0f, 90f, 0f), Items.Pinned);
                d.CatalogueGiven = true;
            }
        }

        static Collider BuildParcel(Transform t)
        {
            Res.Box(t, "box", new Vector3(0f, 0.13f, 0f), new Vector3(0.40f, 0.26f, 0.30f), cardboard);
            Res.Box(t, "tape", new Vector3(0f, 0.2605f, 0f), new Vector3(0.405f, 0.002f, 0.05f), tape);
            Res.Quad(t, "label", new Vector3(0.09f, 0.2625f, 0.07f), new Vector2(0.16f, 0.10f), label, Quaternion.Euler(90f, 0f, 0f));
            return Items.Fit(t.gameObject);
        }

        public static void Tick()
        {
            var d = IronManSuit.Data;
            G.CountTime(ref d.GameHours);
            Prompting = false;
            if (CatalogueUI.Open) { G.Interaction(""); return; }

            if (d.OrderState == Filled && mailbox != null && LookingNear(mailbox.position + Vector3.up * 0.2f, 2.2f, 0.35f))
            {
                Prompting = true;
                G.Interaction("POST TEHO-TARVIKE ORDER (" + d.OrderPrice.ToString("0") + " MK ON PICKUP)", false, true);
                if (G.UsePressed) Post(d);
            }
            if (d.OrderState == Posted && d.GameHours >= d.OrderArrival) Arrive(d);
            if (d.OrderState == Waiting)
            {
                if (counterParcel == null) PlaceCounterParcel();
                if (counterCol != null && G.LookingAt(counterCol, 1.5f))
                {
                    Prompting = true;
                    G.Interaction("TEHO-TARVIKE PARCEL " + d.OrderPrice.ToString("0.00") + " MK", true);
                    if (G.Click) Pay(d);
                }
            }
        }

        static void Post(SaveData d)
        {
            d.OrderState = Posted;
            d.OrderArrival = d.GameHours + UnityEngine.Random.Range(20f, 40f);
            G.Sound("GUI", "page_turn", G.FpsCam);
            G.Flash("ORDER POSTED. PICK IT UP AT TEIMO'S IN A DAY OR TWO", 3f);
            ModConsole.Log("IronManSuit: order posted (" + d.Order + "), arrives at hour " + d.OrderArrival.ToString("0.0"));
        }

        public static void Arrive(SaveData d)
        {
            d.OrderState = Waiting;
            ModConsole.Log("IronManSuit: order arrived at the post office");
            try   // the game's own "your order has arrived" phone call
            {
                var ring = G.Find("YARD/Building/LIVINGROOM/Telephone/Logic/PhoneLogic").GetPlayMaker("Ring");
                ring.FsmVariables.FindFsmBool("OrderReady").Value = true;
            }
            catch (Exception e) { ModConsole.Log("IronManSuit: no phone call for the order: " + e.Message); }
        }

        static void PlaceCounterParcel()
        {
            var store = G.Find("STORE");
            if (store == null) return;
            var go = new GameObject("TehoTarvikeCounterParcel");
            var c = BuildParcel(go.transform);   // built at the origin so its collider fits
            go.transform.SetParent(store, true);
            go.transform.position = OnCounter(0.12f, 0.0f);
            go.transform.rotation = Quaternion.LookRotation(-Front, Vector3.up);
            c.isTrigger = true;
            counterParcel = go.transform;
            counterCol = c;
        }

        static void Pay(SaveData d)
        {
            if (G.Money < d.OrderPrice) { G.Fail("NOT ENOUGH MONEY"); return; }
            G.Money -= d.OrderPrice;
            var at = counterParcel.position;
            var rot = counterParcel.rotation;
            Object.Destroy(counterParcel.gameObject);
            counterParcel = null;
            counterCol = null;
            Items.Instance.Spawn("parcel", at + Vector3.up * 0.02f, rot, d.Order);
            G.Sound("GUI", "buy", G.FpsCam, 0.6f);
            ModConsole.Log("IronManSuit: parcel paid " + d.OrderPrice + " MK, money now " + G.Money);
            d.OrderState = None;
            d.Order = "";
            d.OrderPrice = 0f;
            G.Flash("PARCEL PAID", 1.5f);
        }

        // Out of the box, laid out in a row on the player's side of it (rows of four, further rows nearer the player).
        static void Unpack(Items.Item it)
        {
            var at = it.Go.transform;
            var toward = G.Cam.transform.position - at.position;
            toward.y = 0f;
            toward = toward.sqrMagnitude > 0.001f ? toward.normalized : -at.forward;
            var side = Vector3.Cross(Vector3.up, toward);
            var rot = Quaternion.LookRotation(toward, Vector3.up);
            // suit pieces are big: wider rows, a bit higher so they settle without knocking into each other
            bool big = HasSuit(it.Extra);
            float sx = big ? 0.45f : 0.2f, sz = big ? 0.5f : 0.22f;
            int n = 0;
            foreach (var kv in Parse(it.Extra))
                for (int i = 0; i < kv.Value; i++, n++)
                {
                    var pos = at.position + side * (-1.5f * sx + sx * (n % 4)) + toward * (0.25f + sz * (n / 4)) + Vector3.up * (big ? 0.35f : 0.1f);
                    Items.Instance.Spawn(kv.Key, pos, rot);
                }
            G.Sound("CarBuilding", "disassemble", at);
            if (it.Extra != null && it.Extra.Contains(HoistFree)) Hoist.Give();
            if (big) Suit.PlaceSheets();   // plates for the pieces that just arrived (a Mk3 owner needs ten)
        }

        public static void Fill(Dictionary<string, int> lines)
        {
            var d = IronManSuit.Data;
            var parts = new List<string>();
            float price = Delivery;
            var kinds = new List<string>();
            foreach (var e in Catalogue)
            {
                int q;
                if (!lines.TryGetValue(e.Kind, out q) || q <= 0) continue;
                parts.Add(e.Kind + ":" + q);
                if (e.Pair != null) parts.Add(e.Pair + ":" + q);
                kinds.Add(e.Kind);
                price += e.Price * q;
            }
            if (parts.Count == 0) return;
            if (!d.ChargerSent) { parts.Add(Parts.ChargerId + ":1"); d.ChargerSent = true; }   // free with the first order
            if (!d.IronSent) { parts.Add(Parts.IronId + ":1"); d.IronSent = true; }
            if (FreeDue(HoistFree, kinds)) { parts.Add(HoistFree + ":1"); d.HoistOrdered = true; }
            d.Order = string.Join(",", parts.ToArray());
            d.OrderPrice = price;
            d.OrderState = Filled;
            G.Flash("ORDER FORM FILLED. POST IT AT YOUR MAILBOX", 3f);
        }

        public static Dictionary<string, int> Parse(string order)
        {
            var r = new Dictionary<string, int>();
            if (string.IsNullOrEmpty(order)) return r;
            foreach (var p in order.Split(','))
            {
                var kv = p.Split(':');
                int q;
                if (kv.Length == 2 && Items.Kinds.ContainsKey(kv[0]) && int.TryParse(kv[1], out q)) r[kv[0]] = q;   // (the hoist isn't an item: left out)
            }
            return r;
        }

        static bool LookingNear(Vector3 p, float range, float slack)
        {
            var ray = new Ray(G.Cam.transform.position, G.Cam.transform.forward);
            var to = p - ray.origin;
            float along = Vector3.Dot(to, ray.direction);
            return along > 0 && along < range && Vector3.Cross(ray.direction, to).magnitude < slack;
        }

        public static void OpenCatalogue()
        {
            if (Object.FindObjectOfType<CatalogueUI>() != null) return;
            new GameObject("TehoTarvikeCatalogue").AddComponent<CatalogueUI>();
        }

    }

    // The open catalogue: the product page on the left, the order form on the right (art: tools/reactor/make_print.py,
    // whose layout fractions are mirrored here). Click a product to add one, right-click to take one off, TILAA NYT
    // fills in the order. Frees the mouse and stops the player while open, as the game's own sheets do.
    class CatalogueUI : MonoBehaviour
    {
        // make_print.py: PRODUCTS_Y, ROW_H (per page); ROW_Y, ROW_STEP, NROWS; COL_QTY, COL_SUM; TOTAL_Y; BUTTON; the
        // name lines; the page-turn corners
        static readonly float[] ProductsY = { 0.155f, 0.155f, 0.155f }, RowH = { 0.14f, 0.118f, 0.118f };
        const float FormRowY = 0.285f, FormRowStep = 0.029f, ColQty = 0.62f, ColSum = 0.76f, TotalY = 0.715f, NameY = 0.15f, AddressY = 0.19f;
        static readonly Rect Button = Rect.MinMaxRect(0.27f, 0.78f, 0.73f, 0.87f);
        static readonly Rect NextCorner = Rect.MinMaxRect(0.72f, 0.95f, 0.97f, 0.995f), PrevCorner = Rect.MinMaxRect(0.03f, 0.95f, 0.28f, 0.995f);
        const int FormRows = 13;
        public static int PageNo;   // 0 reactor, 1 cave Mk1 suit, 2 Mk3 suit (kept while the game runs)

        public static bool Open { get; private set; }

        readonly Dictionary<string, int> qty = new Dictionary<string, int>();
        readonly List<Behaviour> frozen = new List<Behaviour>();
        FsmBool playerStop, inMenu;
        bool wasStopped, wasInMenu, closing;
        PlayMakerFSM pauseMenu;
        Texture2D[] pagesTex;
        Texture2D page, form, white;
        GUIStyle pen, hint;
        float closeAt = -1f;

        void Start()
        {
            Open = true;
            pagesTex = new[] { Res.Tex("im_cat_page.png"), Res.Tex("im_cat_page2.png"), Res.Tex("im_cat_page3.png") };
            page = pagesTex[PageNo];
            form = Res.Tex("im_cat_form.png");
            white = new Texture2D(1, 1); white.SetPixel(0, 0, Color.white); white.Apply();
            foreach (var t in new[] { G.Player, G.FpsCam })
                foreach (var b in t.GetComponents<Behaviour>())
                    if (b.enabled && b.GetType().Name.IndexOf("MouseLook", StringComparison.OrdinalIgnoreCase) >= 0) { b.enabled = false; frozen.Add(b); }
            playerStop = FsmVariables.GlobalVariables.FindFsmBool("PlayerStop");
            wasStopped = playerStop.Value; playerStop.Value = true;
            // PlayerInMenu keeps PLAYER's "Update Cursor" FSM from turning the mouse look back on (mysummermeth).
            inMenu = FsmVariables.GlobalVariables.FindFsmBool("PlayerInMenu");
            wasInMenu = inMenu.Value; inMenu.Value = true;
            var options = G.Find("Systems/Options");
            pauseMenu = options != null ? options.GetPlayMaker("Open Menu") : null;
            if (pauseMenu != null) pauseMenu.enabled = false;
            G.Sound("GUI", "page_turn", G.FpsCam);
        }

        void OnDestroy() { Open = false;
            if (white != null) Destroy(white); }

        void Update()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (closeAt > 0f && Time.realtimeSinceStartup > closeAt) { closeAt = -1f; closing = true; }
            if (closing) { Close(); return; }   // a frame after Esc, so the pause menu doesn't see the same press
            if (Input.GetKeyDown(KeyCode.Escape)) closing = true;
            if (Input.GetKeyDown(KeyCode.RightArrow) && PageNo < 2) Turn(PageNo + 1);
            if (Input.GetKeyDown(KeyCode.LeftArrow) && PageNo > 0) Turn(PageNo - 1);
        }

        void Turn(int to) { PageNo = to; page = pagesTex[to]; G.Sound("GUI", "page_turn", G.FpsCam); }

        // The order form has FormRows lines, and the free charger, iron and hoist take theirs: adding one more kind of
        // product must still leave every line (and the freebies) a row.
        bool FormRoom(string kind, int have)
        {
            if (have > 0) return true;
            var ordering = new List<string> { kind };
            foreach (var kv in qty) if (kv.Value > 0 && kv.Key != kind) ordering.Add(kv.Key);
            int free = 0;
            foreach (var fr in Shop.Freebies) if (Shop.FreeDue(fr[0], ordering)) free++;
            if (ordering.Count + free <= FormRows) return true;
            G.Flash("THE ORDER FORM IS FULL - POST THIS ORDER FIRST", 2.5f);
            return false;
        }

        public void CloseSoon() { closing = true; }

        void Close()
        {
            foreach (var b in frozen) if (b != null) b.enabled = true;
            playerStop.Value = wasStopped;
            inMenu.Value = wasInMenu;
            if (pauseMenu != null) pauseMenu.enabled = true;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            Destroy(gameObject);
        }

        static Rect On(Rect page, float x0, float y0, float x1, float y1) { return new Rect(page.x + x0 * page.width, page.y + y0 * page.height, (x1 - x0) * page.width, (y1 - y0) * page.height); }

        void Tint(Rect r, Color c)
        {
            if (Event.current.type != EventType.Repaint) return;
            var old = GUI.color; GUI.color = c; GUI.DrawTexture(r, white); GUI.color = old;
        }

        void Write(Rect r, string text, TextAnchor a = TextAnchor.LowerLeft, float size = 0.75f)
        {
            pen.fontSize = Mathf.Max(9, Mathf.RoundToInt(r.height * size));
            pen.alignment = a;
            GUI.Label(r, text, pen);
        }

        // A click (left 0, right 1) on a spot this event.
        int Clicked(string key, Rect r)
        {
            var e = Event.current;
            if (e.type != EventType.MouseDown || !r.Contains(e.mousePosition) || e.button > 1) return -1;
            e.Use();
            return e.button;
        }

        void OnGUI()
        {
            if (pen == null)
            {
                pen = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, clipping = TextClipping.Overflow, wordWrap = false };
                pen.normal.textColor = new Color(0.1f, 0.18f, 0.55f);   // ballpoint blue
                hint = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                hint.normal.textColor = new Color(1f, 0.9f, 0.3f);
            }
            GUI.depth = -50;
            var d = IronManSuit.Data;
            float aspect = (float)page.width / page.height;
            float h = Mathf.Min(Screen.height * 0.94f, Screen.width * 0.96f / (2f * aspect));
            float w = h * aspect;
            var L = new Rect(Screen.width / 2f - w, (Screen.height - h) / 2f, w, h);
            var R = new Rect(Screen.width / 2f, L.y, w, h);
            Tint(new Rect(L.x + 6, L.y + 8, w * 2, h), new Color(0, 0, 0, 0.35f));
            GUI.DrawTexture(L, page);
            GUI.DrawTexture(R, form);
            var e = Event.current;
            bool editable = d.OrderState == Shop.None && closeAt < 0f;

            int prow = 0;
            float py = ProductsY[PageNo], rh = RowH[PageNo];
            for (int i = 0; i < Shop.Catalogue.Length; i++)
            {
                var en = Shop.Catalogue[i];
                if (en.Page != PageNo) continue;
                var r = On(L, 0.033f, py + prow * rh + 0.008f, 0.967f, py + (prow + 1) * rh - 0.008f);
                prow++;
                int q; qty.TryGetValue(en.Kind, out q);
                if (editable && r.Contains(e.mousePosition)) Tint(r, new Color(1f, 0.95f, 0.4f, 0.16f));
                int b = editable ? Clicked(en.Kind, r) : -1;
                if (b == 0 && q < 5 && FormRoom(en.Kind, q)) { q++; G.Sound("GUI", "buy", G.FpsCam, 0.6f); }
                if (b == 1 && q > 0) { q--; G.Sound("GUI", "buy", G.FpsCam, 0.6f); }
                qty[en.Kind] = q;
                if (q > 0) Write(new Rect(r.x + r.width * 0.43f, r.y + r.height * (PageNo == 0 ? 0.62f : 0.55f), r.width * 0.2f, r.height * (PageNo == 0 ? 0.25f : 0.32f)), q + " kpl", TextAnchor.MiddleLeft, 0.8f);
            }
            // turning the page: the printed corner tabs (back at the left, on from page 1 at the right)
            for (int side = -1; side <= 1; side += 2)
            {
                if (PageNo + side < 0 || PageNo + side > 2) continue;
                var cr = side > 0 ? NextCorner : PrevCorner;
                var corner = On(L, cr.xMin, cr.yMin, cr.xMax, cr.yMax);
                if (corner.Contains(e.mousePosition)) Tint(corner, new Color(1f, 0.95f, 0.4f, 0.2f));
                if (Clicked(side > 0 ? "page" : "pageback", corner) >= 0) Turn(PageNo + side);
            }

            // the form: the player's name, then the lines, delivery and total
            Write(On(R, 0.19f, NameY - 0.035f, 0.9f, NameY - 0.002f), FsmVariables.GlobalVariables.FindFsmString("PlayerName").Value);
            Write(On(R, 0.19f, AddressY - 0.035f, 0.9f, AddressY - 0.002f), "Peräjärvi");
            var lines = new List<KeyValuePair<Shop.Entry, int>>();
            if (d.OrderState == Shop.None) { foreach (var en in Shop.Catalogue) { int q; if (qty.TryGetValue(en.Kind, out q) && q > 0) lines.Add(new KeyValuePair<Shop.Entry, int>(en, q)); } }
            else foreach (var kv in Shop.Parse(d.Order)) { var en = Shop.Find(kv.Key); if (en != null) lines.Add(new KeyValuePair<Shop.Entry, int>(en, kv.Value)); }
            float total = Shop.Delivery;
            for (int i = 0; i < lines.Count && i < FormRows; i++)
            {
                var en = lines[i].Key; int q = lines[i].Value;
                float y0 = FormRowY + i * FormRowStep, y1 = y0 + FormRowStep;
                var row = On(R, 0.055f, y0, 0.945f, y1);
                Write(On(R, 0.06f, y0, ColQty - 0.02f, y1 - 0.006f), en.Line.ToLowerInvariant());
                Write(On(R, ColQty, y0, ColSum - 0.02f, y1 - 0.006f), q.ToString());
                Write(On(R, ColSum, y0, 0.94f, y1 - 0.006f), (en.Price * q).ToString("0") + ",-");
                total += en.Price * q;
                if (editable && row.Contains(e.mousePosition)) Tint(row, new Color(1f, 0.3f, 0.2f, 0.08f));
                if (editable && Clicked("line " + en.Kind, row) >= 0) { qty[en.Kind] = q - 1; G.Sound("GUI", "buy", G.FpsCam, 0.6f); }
            }
            if (lines.Count > 0) Write(On(R, 0.7f, TotalY - 0.005f, 0.94f, TotalY + 0.03f), total.ToString("0") + ",-", TextAnchor.LowerRight, 0.9f);
            // the free charger, iron and hoist on the next empty form rows (FormRoom keeps them a row)
            int row2 = lines.Count;
            var ordering = new List<string>();
            foreach (var kv in lines) ordering.Add(kv.Key.Kind);
            if (d.OrderState == Shop.None && lines.Count > 0)
                foreach (var fr in Shop.Freebies)
                {
                    if (!Shop.FreeDue(fr[0], ordering) || row2 >= FormRows) continue;
                    float y = FormRowY + row2++ * FormRowStep;
                    Write(On(R, 0.06f, y, ColQty - 0.02f, y + FormRowStep - 0.006f), fr[1]);
                    Write(On(R, ColQty, y, ColSum - 0.02f, y + FormRowStep - 0.006f), "1");
                    Write(On(R, ColSum, y, 0.94f, y + FormRowStep - 0.006f), "0,-");
                }

            var btn = On(R, Button.xMin, Button.yMin, Button.xMax, Button.yMax);
            if (editable && btn.Contains(e.mousePosition)) Tint(btn, new Color(1f, 1f, 1f, 0.15f));
            if (Clicked("order", btn) >= 0 && editable)
            {
                if (lines.Count == 0) G.Flash("MARK SOMETHING TO ORDER FIRST", 2f);
                else { Shop.Fill(qty); G.Sound("GUI", "buy", G.FpsCam, 0.6f); closeAt = Time.realtimeSinceStartup + 1.2f; }
            }
            if (d.OrderState != Shop.None)
                Write(On(R, 0.3f, 0.88f, 0.75f, 0.92f), d.OrderState == Shop.Filled ? "TILATTU - postita lomake" : "POSTITETTU", TextAnchor.MiddleCenter, 0.8f);

            var hr = new Rect(0, Screen.height - 24, Screen.width, 22);
            GUI.Label(hr, (editable ? "CLICK: ADD ONE   RIGHT-CLICK: TAKE ONE OFF   " : "") + "ARROWS / CORNER: TURN THE PAGE   ESC: CLOSE", hint);
        }
    }
}
