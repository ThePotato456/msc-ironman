using System.Collections.Generic;
using MSCLoader;
using UnityEngine;

namespace IronManSuit
{
    // Everything the mod keeps in the game's own save (SaveLoad). Add fields over time; older saves load the new
    // ones with their defaults.
    public class SaveData
    {
        public List<WorldItem> Items = new List<WorldItem>();   // loose mod items in the world

        // Mail order (Shop.cs)
        public float GameHours;        // game time counted by the mod
        public bool CatalogueGiven;    // the catalogue was put in the player's mailbox
        public bool ChargerSent;       // the free charger went out with an order
        public int OrderState;         // Shop: 0 none, 1 form filled, 2 posted, 3 waiting at the post office
        public string Order = "";      // "kind:qty,kind:qty"
        public float OrderPrice;       // cash on delivery
        public float OrderArrival;     // GameHours when the parcel reaches the post office

        // Workbench (Bench.cs)
        public int BenchStage;         // parts on the pad: 0 none, 1 cage, 2 + core, 3 + coil
        public float CoreCharge;       // the core on the pad, 0..1 (1 = charged)
        public int Clamps;             // charger clamps on the core's terminals: bit 0 red, bit 1 black
        public bool ChuckTight;        // the chuck's jaws closed on the cage (open whenever the chuck is empty)

        // Cave-style build (Phase 2.2): what's been done to the parts on the chuck
        public int BuildVersion;       // 0: saved before the cave build; Migrate() brings it to 3 (1: the cave build; 2: the Mk1's ids are "m1_*"; 3: the Mk3's diffuser plate is a part)
        public bool Wafer;             // the palladium wafer is in the core
        public int[] CoreBolts = new int[3];   // tightness 0..Bolt.Steps (8 mm)
        public int[] Joints = new int[6];      // solder joints: 0 bare, 1 cold, 2 good
        public int[] Turns = new int[10];      // coil turns per block of the ring on the chuck
        public int[] RingBolts = new int[3];   // tightness 0..Bolt.Steps (7 mm)
        public bool Lit;               // first light passed: the reactor on the chuck is finished
        public bool ChargerOn;         // the charger's power switch
        public bool IronSent;          // the free soldering iron went out with an order
        public bool ConverterPlaced;   // the scrap catalytic converter was put at the landfill
        public bool Salvaged;          // its palladium was taken out

        // The suit (Phase 3 / 4: Suit.cs, Hoist.cs, Armor.cs)
        public bool HoistOrdered;      // the free hoist went out with a suit order
        public bool HoistGiven;        // the hoist stands in the garage
        public bool SheetsPlaced;      // scrap sheet steel was put at the landfill
        public List<PieceState> Hoist = new List<PieceState>();   // pieces hanging on the hoist
        public bool SuitReactor;       // the arc reactor is in the suit's chest
        public float SuitCharge;       // its charge, 0..1 (flight, repulsors)
        public bool SuitDiffuser;      // the Mk3's diffuser plate is clipped on over its reactor (Mk3.DiffuserId)
        public List<string> Worn = new List<string>();            // finished pieces worn for testing (not the suit)
        public int SuitVariant;        // which suit this is: 0 or 1 the cave Mk1, 3 the Mk3 (set when its first piece is hung)
        public bool SuitActive;        // the suit has been powered up off the hoist: it stands (or is worn) whole
        public bool InSuit;            // the player is in it
        public float StandX, StandY, StandZ, StandYaw;            // where the empty suit stands

        // The Mk3 (Mk3.cs)
        public bool Mk3HelmetPlaced;   // its helmet was set on the garage workbench

        public void Migrate()
        {
            if (Hoist == null) Hoist = new List<PieceState>();
            if (Worn == null) Worn = new List<string>();
            if (CoreBolts == null || CoreBolts.Length != 3) CoreBolts = new int[3];
            if (Joints == null || Joints.Length != 6) Joints = new int[6];
            if (Turns == null || Turns.Length != 10) Turns = new int[10];
            if (RingBolts == null || RingBolts.Length != 3) RingBolts = new int[3];
            if (BuildVersion < 2)
            {
                // the Mk1's pieces and items got the "m1_" prefix (Suit.Mk1Ids)
                Order = Suit.Mk1Ids(Order);
                for (int i = 0; i < Worn.Count; i++) Worn[i] = Suit.Mk1Ids(Worn[i]);
                foreach (var h in Hoist) h.Id = Suit.Mk1Ids(h.Id);
                foreach (var w in Items) { w.Kind = Suit.Mk1Ids(w.Kind); if (w.Extra != null && w.Extra.Contains("suit_")) w.Extra = Suit.Mk1Ids(w.Extra); }
            }
            // the Mk3's diffuser plate became a part of its own: before, it showed whenever a reactor was in the Mk3
            if (BuildVersion < 3 && SuitReactor && SuitVariant == 3) SuitDiffuser = true;
            if (BuildVersion >= 1) { BuildVersion = 3; return; }
            // Saves from before the cave build: a core on the chuck was a finished part, and a reactor with its coil
            // on was a finished reactor.
            if (BenchStage >= 2) { Wafer = true; Fill(CoreBolts, 8); Fill(Joints, 2); }
            if (BenchStage >= 3) { Fill(Turns, 30); Fill(RingBolts, 8); Lit = true; }
            if (Clamps != 0) ChargerOn = true;
            BuildVersion = 3;
        }
        static void Fill(int[] a, int v) { for (int i = 0; i < a.Length; i++) a[i] = v; }
    }

    public class WorldItem
    {
        public string Kind;            // Items.Kinds id
        public float X, Y, Z, RX, RY, RZ;
        public string Extra;           // per-item data (a parcel's contents, a core's charge)
    }

    public class IronManSuit : Mod
    {
        public override string ID => "IronManSuit";
        public override string Name => "Iron Man Suit";
        public override string Version => "0.1.0";
        public override string Author => "glizzy5oblin";
        public override string Description => "Order arc reactor and suit parts from the Teho-Tarvike catalogue in your mailbox, build the reactor at the garage workbench and the cave Mk1 suit on a hoist, then fly it.";

        public static IronManSuit Instance;
        public static SaveData Data = new SaveData();

        bool ready;

        // Other hooks: Setup.OnNewGame, OnMenuLoad, PreLoad, PostLoad, OnGUI, FixedUpdate, ModSettings.
        public override void ModSetup()
        {
            Instance = this;
            SetupFunction(Setup.OnLoad, OnLoad);
            SetupFunction(Setup.OnSave, OnSave);
            SetupFunction(Setup.Update, OnUpdate);
            SetupFunction(Setup.ModSettings, Settings);
        }

        // The suit's keys (Phase 4), rebindable in the mod's settings. None of them does anything on foot in vanilla.
        public static SettingsKeybind KeyFly, KeyRepulsor, KeyLeave, KeyFaceplate, KeyTakeOff;
        void Settings()
        {
            Keybind.AddHeader("Suit");
            KeyFly = Keybind.Add("suitFly", "Fly / land (in the suit)", KeyCode.V);
            KeyRepulsor = Keybind.Add("suitRepulsor", "Repulsor (in the suit)", KeyCode.R);
            KeyLeave = Keybind.Add("suitLeave", "Get out of the suit", KeyCode.G);
            KeyFaceplate = Keybind.Add("suitFaceplate", "Faceplate up / down", KeyCode.Y);
            KeyTakeOff = Keybind.Add("suitTakeOff", "Take off worn test pieces", KeyCode.Z);
        }

        void OnLoad()
        {
            Data = SaveLoad.ValueExists(this, "state") ? SaveLoad.DeserializeClass<SaveData>(this, "state") : new SaveData();
            Data.Migrate();
            G.Init();
            G.ResetClock();
            Res.Init(ModLoader.GetModAssetsFolder(this));
            Items.Kinds.Clear();
            new Items();
            Parts.Init();
            Suit.InitMaterials();
            Suit.InitItems();
            Mk3.Init();
            Stand.Init();
            Shop.Init();
            Bench.Init();
            Items.Instance.Restore(Data.Items);
            Parts.PlaceConverter();   // the landfill's scrap converter, once per save (and back if it went missing)
            Hoist.Init();
            Wear.Init();
            Armor.Init();
            SuitHud.Make();
            Mk3.PlaceHelmet();        // the Mk3 helmet on the garage workbench, once per save
            Suit.PlaceSheets();       // scrap sheet steel at the landfill once the hoist is in (topped up to what's needed)
            ready = true;
            // tools/launch.sh waits for this line to know the save is in.
            ModConsole.Print("<color=cyan>Iron Man Suit loaded:</color> version " + Version);
        }

        internal void OnSave()
        {
            if (!ready) return;
            Data.Items = Items.Instance.Snapshot();
            SaveLoad.SerializeClass(this, Data, "state");
        }

        void OnUpdate()
        {
            if (!ready) return;
            Shop.Tick();
            Bench.Tick();
            Hoist.Tick();
            Stand.Tick();
            Armor.Tick();
            Wear.Tick();
            Items.Instance.Tick(Shop.Prompting || Bench.Prompting || Hoist.Prompting || Stand.Prompting || Armor.Prompting || CatalogueUI.Open);
            G.LateTick();
        }
    }
}
