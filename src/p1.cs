// =====================================================================
//  STREET GOLF  1.1.3  -  by spitmux
//
//  A driving range anywhere in Los Santos. You stand where you are and
//  hit ball after ball at the traffic. No hole, no course, no walking
//  after the ball.
//
//  Built on the game's own golf minigame assets:
//    animations : mini@golfai / mini@golf, the clips the minigame plays
//    clubs      : prop_golf_wood_01 / _iron_01 / _pitcher_01 / _putter_01
//    ball       : prop_golf_ball, flown by the game's own physics
//    audio + fx : the GOLF_* sound set and scr_golf_* particles
//
//  Requires ScriptHookV and ScriptHookVDotNet v3. Written in C# 5 syntax
//  so SHVDN's own compiler can build it from source, and checked to
//  compile against SHVDN 3.6, the 3.7 nightlies and 3.9 Enhanced.
//  Anything that moved between those versions is reached through a
//  native rather than a wrapper, because native names are stable across
//  them and wrapper names are not.
//
//  Aim with the camera. Everything works on pad and on keyboard.
// =====================================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using GTA;
using GTA.Math;
using GTA.Native;
using Control = GTA.Control;

public class StreetGolf : Script
{
    // ---------------- game assets ----------------
    const string VERSION = "1.1.3";
    const string AUTHOR = "spitmux";

    const string BALL_MODEL = "prop_golf_ball";
    const string DICT_AI = "mini@golfai";
    const string DICT_MP = "mini@golf";
    const int PH_R_HAND = 28422;
    const int SKEL_R_HAND = 57005;

    // Twelve clubs: the four the golf minigame has, then a flatter hitting
    // variant of each, then a third set back at the original launch angles
    // but swung a great deal harder. Every variant borrows its base club's
    // prop, animation and impact timing, so the extra sets cost nothing but
    // a launch angle and a speed.
    static readonly string[] CLUB_NAMES = { "DRIVER", "IRON", "WEDGE", "PUTTER",
                                            "DRIVER 2", "IRON 2", "WEDGE 2", "POWER PUTTER",
                                            "DRIVER 3", "IRON 3", "WEDGE 3", "PUTTER 3" };
    static readonly string[] CLUB_KEYS = { "Driver", "Iron", "Wedge", "Putter",
                                           "Driver2", "Iron2", "Wedge2", "PowerPutter",
                                           "Driver3", "Iron3", "Wedge3", "Putter3" };
    static readonly int[] CLUB_BASE = { 0, 1, 2, 3, 0, 1, 2, 3, 0, 1, 2, 3 };
    static readonly string[] BASE_PROPS = { "prop_golf_wood_01", "prop_golf_iron_01", "prop_golf_pitcher_01", "prop_golf_putter_01" };
    static readonly string[] BASE_ANIM = { "wood", "iron", "wedge", "putt" };
    // phase of <club>_swing_action where the minigame releases the ball
    static readonly float[] BASE_IMPACT = { 0.16f, 0.134f, 0.119f, 0.159f };
    static readonly string[] BASE_SOUND = { "GOLF_SWING_TEE_MASTER", "GOLF_SWING_FAIRWAY_IRON_MASTER", "GOLF_SWING_CHIP_MASTER", "GOLF_SWING_PUTT_MASTER" };
    const int CLUB_COUNT = 12;
    const int BASE_PUTTER = 3;
    // HUD copy for the clubs: the icon file for each base club, and a tag
    // for each set of four
    static readonly string[] CLUB_ICONS = { "driver", "iron", "wedge", "putter" };
    static readonly string[] SET_TAGS = { "SET 1 - STANDARD", "SET 2 - LOW AND LONG", "SET 3 - HEAVY" };

    enum Mode { Off, Ready, Backswing, Swing, Watch }

    // What the ball itself is. Every one of these keeps the same club, swing
    // and physics: only what leaves the tee changes.
    enum BallMode { Normal, Fire, Boom, Super }
    static readonly string[] MODE_NAMES = { "NORMAL", "FIREBALL", "BOOM", "SUPER SHOT" };
    static readonly string[] MODE_BLURB = {
        "an ordinary golf ball",
        "sets light to everything it touches",
        "flies like any other ball, then detonates where it lands",
        "every club multiplied, and it is not subtle" };
    const int MODE_COUNT = 4;
    static readonly string[] MODE_ICONS = { "ball", "flame", "bomb", "super" };
    // the same thing said in the width of the card
    static readonly string[] MODE_SHORT = {
        "a plain golf ball",
        "lights up whatever it touches",
        "detonates where it lands",
        "carries" };

    // ---------------- settings ----------------
    Keys keyToggle = Keys.F3;
    Keys keyNewBall = Keys.N;
    Keys keyBallMode = Keys.M;
    Keys keyPolice = Keys.K;
    BallMode ballMode = BallMode.Normal;
    float superMult = 50f;        // how many times further a SUPER SHOT carries
    // the stops the drawer steps through; the ini can hold anything in between
    static readonly float[] SUPER_STEPS = { 2f, 3f, 5f, 8f, 10f, 15f, 20f, 30f, 50f, 75f, 100f, 150f, 200f };
    Control padToggleHold = Control.FrontendLt;
    Control padTogglePress = Control.FrontendAccept;  // the d-pad belongs to the menu now
    int unitMode;                    // 0 auto (game setting), 1 yards, 2 metres
    float powerChargeTime = 1.15f;
    float fineAimSpeed = 45f;
    float reloadDelay = 0.55f;
    float trailSeconds = 2.0f;
    float trailWidth = 1.0f;    // multiplier on a core that is already paper thin
    float trailGap = 4.0f;      // metres of clear air between the ball and the ribbon
    float trailFadeIn = 6.0f;   // metres over which the head of the ribbon ramps up
    bool trailEnabled = true;
    bool aimLine = true;
    bool pedsRagdoll = true;
    bool golfSounds = true;
    bool showHud = true;
    float menuAutoHide = 6f;      // seconds the settings drawer stays open after the d-pad was last touched; 0 keeps it open
    int maxLiveBalls = 10;
    float ballLifetime = 22f;
    float sweetLo = 0.84f, sweetHi = 0.96f;
    // three sets of the same four clubs:
    //   standard | flatter and a touch further | standard arc, far more power
    float[] clubSpeed = { 72f, 54f, 38f, 14f,   93f, 66f, 43f, 24f,   88f, 66f, 48f, 34f };
    float[] clubLoft = { 13f, 24f, 44f, 1.5f,   8f, 16f, 30f, 1f,   13f, 24f, 44f, 1.5f };
    float ballForward = 0.58f;
    float ballSide = 0.06f;
    bool impactMarks = true;      // chip concrete and crack walls where the ball lands
    bool carDamage = true;        // dent panels and smash glass
    bool rumble = true;
    bool camShake = true;
    float impactPower = 1.0f;     // global multiplier on everything destructive
    float carKnockback = 0.8f;    // how hard a hit shoves the car, 1 is the original
    float carDentDamage = 220f;   // how deep a full strike dents a panel
    float carDentRadius = 160f;   // how far round the contact point the dent spreads
    float minImpactSpeed = 9f;    // below this the ball just bounces harmlessly
    bool policeWanted = true;     // master switch: false and the police never react at all
    bool lessLethalCops = true;   // batons and tasers at low stars, if you are not armed
    int lessLethalMaxStars = 2;   // up to and including this many stars
    float copRange = 90f;
    float policeGrace = 180f;     // seconds the police look the other way
    bool airControl = true;
    float airControlPower = 5.5f;  // sideways acceleration at full stick, m/s^2
    float airControlBudget = 6f;   // total change of velocity allowed per shot, m/s
    float rollControlPower = 3.5f; // gentler nudge once the ball is rolling
    float rollControlBudget = 5f;  // rolling gets its own allowance
    bool stealthWhenUnseen = true;
    float witnessRange = 60f;
    int heatAfterPeds = 3;        // bodies inside the window that finally gets you noticed
    float heatWindow = 45f;       // seconds after which a hit is forgotten
    string scriptsDir;

    // ---------------- state ----------------
    Mode mode = Mode.Off;
    Prop club;
    int clubIndex;
    float aimDeg;                    // world heading the shot goes towards
    float aimOffset;                 // fine adjustment on top of the camera heading
    float power;
    float charge;
    float stateTime;
    float reloadTimer;
    bool swingReleased;
    bool holdPlayed;
    bool launched;
    Prop teeBall;
    Vector3 teePos;
    int lastAnimPush;
    int lastClubSwitch;
    float noAnimFor;
    Shot watchedShot;           // the ball the camera is locked onto; never auto-deleted
    float graceLeft;
    int baseWanted;
    bool policeSuppressed;
    bool unseenActive;
    bool lastUnwitnessed;
    int witnessTick;
    bool noticedNotified;
    int recentPedHits;
    List<int> pedHitTimes = new List<int>();
    List<int> tamedCops = new List<int>();
    int copScanAt;
    bool lessLethalOn;
    bool debugHud;          // off unless the ini asks for it
    int offHintUntil;
    bool announced;
    int menuIndex;
    int lastMenuMove;
    const int MENU_COUNT = 11;
    int dbgProbes, dbgHits, dbgImpacts;
    string dbgLast = "-";
    string blockReason = "";

    // club head calibration, per club, in ped-local space
    Vector3[] headLocal = new Vector3[4];
    bool[] headKnown = new bool[4];
    float calibrateAt;

    // scoring
    int shots;
    int carsHit;
    int pedsHit;
    int sessionCars;
    int sessionPeds;
    float lastShotDist;
    float bestShotDist;
    float liveDist;

    // ---------------- flying balls ----------------
    class Shot
    {
        public Prop ball;
        public Vector3 origin;
        public int born;
        public float age;
        public float rest;
        public float dist;
        public bool done;
        public bool splashed;
        public int lastPedHit;
        public int lastPedTime;
        public int lastCarTime;
        public float prevVz;
        public Vector3 prevPos;
        public float steerUsed;
        public float rollSteerUsed;
        public BallMode mode;
        public float mult = 1f;     // the super shot multiplier this ball left the tee with
        public bool spent;          // one shot effects already used
        public int homeTarget;
        public int homeAt;
        public int lastImpact;
        public int lastEnt;
        public int lastEntTime;
        public List<Vector3> pts = new List<Vector3>();
        public List<int> times = new List<int>();
        public Vector3 lastPt;
        public Vector3 prevVel;
        public bool hasPrevVel;
        public bool airborne;       // has had clear air under it since it left the tee
        public Vector3 endPos;      // where a Boom ball went off, for the camera to stay on
        public bool hasEnd;
    }
    List<Shot> shotsInPlay = new List<Shot>();

    public StreetGolf()
    {
        scriptsDir = FindScriptsDir();
        LoadSettings();
        LoadRecords();
        Log("--- StreetGolf loaded. toggle=" + keyToggle + " newball=" + keyNewBall + " ---");
        Tick += OnTick;
        KeyDown += OnKeyDown;
        Aborted += OnAborted;
        Interval = 0;
    }

    // =====================================================================
    //  settings
    // =====================================================================
    string FindScriptsDir()
    {
        try
        {
            string a = AppDomain.CurrentDomain.BaseDirectory;
            if (!string.IsNullOrEmpty(a) && File.Exists(Path.Combine(a, "StreetGolf.ini"))) return a;
            string b = Path.Combine(Environment.CurrentDirectory, "scripts");
            if (File.Exists(Path.Combine(b, "StreetGolf.ini"))) return b;
            if (Directory.Exists(b)) return b;
            if (!string.IsNullOrEmpty(a) && Directory.Exists(a)) return a;
        }
        catch { }
        return "scripts";
    }

    void LoadSettings()
    {
        Dictionary<string, string> kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            string path = Path.Combine(scriptsDir, "StreetGolf.ini");
            if (File.Exists(path))
            {
                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0 || line[0] == ';' || line[0] == '#' || line[0] == '[') continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim();
                    string v = line.Substring(eq + 1).Trim();
                    int sc = v.IndexOf(';');
                    if (sc >= 0) v = v.Substring(0, sc).Trim();
                    kv[k] = v;
                }
            }
        }
        catch { }

        keyToggle = GetKey(kv, "ToggleKey", keyToggle);
        keyNewBall = GetKey(kv, "NewBallKey", keyNewBall);
        keyBallMode = GetKey(kv, "BallModeKey", keyBallMode);
        keyPolice = GetKey(kv, "PoliceKey", keyPolice);
        superMult = GetFloat(kv, "SuperShotMultiplier", superMult);
        if (superMult < 1f) superMult = 1f;
        if (superMult > 200f) superMult = 200f;
        string bm = GetStr(kv, "BallMode", "Normal");
        for (int i = 0; i < MODE_COUNT; i++)
        {
            if (string.Equals(bm, ((BallMode)i).ToString(), StringComparison.OrdinalIgnoreCase)
                || string.Equals(bm, MODE_NAMES[i], StringComparison.OrdinalIgnoreCase))
            {
                ballMode = (BallMode)i;
                break;
            }
        }
        padToggleHold = GetControl(kv, "PadToggleHold", padToggleHold);
        padTogglePress = GetControl(kv, "PadTogglePress", padTogglePress);

        string units = GetStr(kv, "Units", "auto").ToLowerInvariant();
        if (units == "yards" || units == "yd" || units == "imperial") unitMode = 1;
        else if (units == "metres" || units == "meters" || units == "m" || units == "metric") unitMode = 2;
        else unitMode = 0;

        powerChargeTime = GetFloat(kv, "PowerChargeTime", powerChargeTime);
        fineAimSpeed = GetFloat(kv, "FineAimSpeed", fineAimSpeed);
        reloadDelay = GetFloat(kv, "ReloadDelay", reloadDelay);
        trailSeconds = GetFloat(kv, "TrailSeconds", trailSeconds);
        trailWidth = GetFloat(kv, "TrailWidth", trailWidth);
        trailGap = GetFloat(kv, "TrailGap", trailGap);
        trailFadeIn = GetFloat(kv, "TrailFadeIn", trailFadeIn);
        if (trailGap < 0f) trailGap = 0f;
        if (trailFadeIn < 0.5f) trailFadeIn = 0.5f;
        trailEnabled = GetBool(kv, "Trail", trailEnabled);
        aimLine = GetBool(kv, "AimLine", aimLine);
        pedsRagdoll = GetBool(kv, "PedsRagdoll", pedsRagdoll);
        golfSounds = GetBool(kv, "Sounds", golfSounds);
        showHud = GetBool(kv, "Hud", showHud);
        menuAutoHide = GetFloat(kv, "MenuAutoHide", menuAutoHide);
        if (menuAutoHide < 0f) menuAutoHide = 0f;
        debugHud = GetBool(kv, "Debug", debugHud);
        maxLiveBalls = (int)GetFloat(kv, "MaxBalls", maxLiveBalls);
        if (maxLiveBalls < 1) maxLiveBalls = 1;
        if (maxLiveBalls > 24) maxLiveBalls = 24;
        ballLifetime = GetFloat(kv, "BallLifetime", ballLifetime);
        sweetLo = GetFloat(kv, "SweetSpotLow", sweetLo);
        sweetHi = GetFloat(kv, "SweetSpotHigh", sweetHi);
        for (int i = 0; i < CLUB_COUNT; i++)
        {
            clubSpeed[i] = GetFloat(kv, CLUB_KEYS[i] + "Speed", clubSpeed[i]);
            clubLoft[i] = GetFloat(kv, CLUB_KEYS[i] + "Loft", clubLoft[i]);
        }
        ballForward = GetFloat(kv, "BallForward", ballForward);
        ballSide = GetFloat(kv, "BallSide", ballSide);
        impactMarks = GetBool(kv, "ImpactMarks", impactMarks);
        carDamage = GetBool(kv, "CarDamage", carDamage);
        rumble = GetBool(kv, "Rumble", rumble);
        camShake = GetBool(kv, "CameraShake", camShake);
        impactPower = GetFloat(kv, "ImpactPower", impactPower);
        carKnockback = GetFloat(kv, "CarKnockback", carKnockback);
        if (carKnockback < 0f) carKnockback = 0f;
        carDentDamage = GetFloat(kv, "CarDentDamage", carDentDamage);
        carDentRadius = GetFloat(kv, "CarDentRadius", carDentRadius);
        if (carDentDamage < 0f) carDentDamage = 0f;
        if (carDentDamage > 2000f) carDentDamage = 2000f;
        if (carDentRadius < 1f) carDentRadius = 1f;
        if (carDentRadius > 2000f) carDentRadius = 2000f;
        minImpactSpeed = GetFloat(kv, "MinImpactSpeed", minImpactSpeed);
        policeWanted = GetBool(kv, "PoliceWanted", policeWanted);
        lessLethalCops = GetBool(kv, "LessLethalCops", lessLethalCops);
        lessLethalMaxStars = (int)GetFloat(kv, "LessLethalMaxStars", lessLethalMaxStars);
        copRange = GetFloat(kv, "LessLethalRange", copRange);
        if (lessLethalMaxStars < 0) lessLethalMaxStars = 0;
        if (lessLethalMaxStars > 5) lessLethalMaxStars = 5;
        if (copRange < 10f) copRange = 10f;
        policeGrace = GetFloat(kv, "PoliceGrace", policeGrace);
        if (policeGrace < 0f) policeGrace = 0f;
        airControl = GetBool(kv, "AirControl", airControl);
        airControlPower = GetFloat(kv, "AirControlPower", airControlPower);
        airControlBudget = GetFloat(kv, "AirControlBudget", airControlBudget);
        rollControlPower = GetFloat(kv, "RollControlPower", rollControlPower);
        rollControlBudget = GetFloat(kv, "RollControlBudget", rollControlBudget);
        if (rollControlPower < 0f) rollControlPower = 0f;
        if (rollControlBudget < 0f) rollControlBudget = 0f;
        if (airControlPower < 0f) airControlPower = 0f;
        if (airControlBudget < 0f) airControlBudget = 0f;
        stealthWhenUnseen = GetBool(kv, "StealthWhenUnseen", stealthWhenUnseen);
        witnessRange = GetFloat(kv, "WitnessRange", witnessRange);
        heatAfterPeds = (int)GetFloat(kv, "HeatAfterPeds", heatAfterPeds);
        heatWindow = GetFloat(kv, "HeatWindow", heatWindow);
        if (heatAfterPeds < 1) heatAfterPeds = 1;
        if (heatWindow < 5f) heatWindow = 5f;
        if (witnessRange < 5f) witnessRange = 5f;
        if (impactPower < 0f) impactPower = 0f;
        if (impactPower > 5f) impactPower = 5f;
        if (trailSeconds < 0.2f) trailSeconds = 0.2f;
        if (trailWidth < 0.05f) trailWidth = 0.05f;
    }

    static string GetStr(Dictionary<string, string> kv, string n, string d)
    {
        string s;
        if (kv.TryGetValue(n, out s) && s.Length > 0) return s;
        return d;
    }

    static Keys GetKey(Dictionary<string, string> kv, string n, Keys d)
    {
        string s;
        if (!kv.TryGetValue(n, out s)) return d;
        Keys k;
        if (Enum.TryParse<Keys>(s, true, out k)) return k;
        return d;
    }

    static Control GetControl(Dictionary<string, string> kv, string n, Control d)
    {
        string s;
        if (!kv.TryGetValue(n, out s)) return d;
        Control c;
        if (Enum.TryParse<Control>(s, true, out c)) return c;
        return d;
    }

    static bool GetBool(Dictionary<string, string> kv, string n, bool d)
    {
        string s;
        if (!kv.TryGetValue(n, out s)) return d;
        s = s.ToLowerInvariant();
        if (s == "true" || s == "1" || s == "yes" || s == "on") return true;
        if (s == "false" || s == "0" || s == "no" || s == "off") return false;
        return d;
    }

    static float GetFloat(Dictionary<string, string> kv, string n, float d)
    {
        string s;
        if (!kv.TryGetValue(n, out s)) return d;
        float f;
        if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out f)) return f;
        return d;
    }

    // Jenkins one-at-a-time, the hash the game uses for named lookups.
    // Done here so we need not lean on a helper that moved between versions.
    static uint Joaat(string text)
    {
        uint h = 0;
        for (int i = 0; i < text.Length; i++)
        {
            h += (uint)char.ToLowerInvariant(text[i]);
            h += h << 10;
            h ^= h >> 6;
        }
        h += h << 3;
        h ^= h >> 11;
        h += h << 15;
        return h;
    }

    string RecordsPath() { return Path.Combine(scriptsDir, "StreetGolf.records.txt"); }

    void LoadRecords()
    {
        try
        {
            string p = RecordsPath();
            if (!File.Exists(p)) return;
            string[] lines = File.ReadAllLines(p);
            for (int i = 0; i < lines.Length; i++)
            {
                string s = lines[i];
                if (s.StartsWith("Best=")) float.TryParse(s.Substring(5).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out bestShotDist);
                else if (s.StartsWith("Cars=")) int.TryParse(s.Substring(5).Trim(), out carsHit);
                else if (s.StartsWith("Peds=")) int.TryParse(s.Substring(5).Trim(), out pedsHit);
                // carry over records written by version 1
                else if (s.StartsWith("LongestDrive=")) float.TryParse(s.Substring(13).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out bestShotDist);
                else if (s.StartsWith("PedsBeaned=")) int.TryParse(s.Substring(11).Trim(), out pedsHit);
            }
        }
        catch { }
    }

    string LogPath() { return Path.Combine(scriptsDir, "StreetGolf.log"); }

    void Log(string msg)
    {
        if (!debugHud) return;
        try
        {
            File.AppendAllText(LogPath(),
                DateTime.Now.ToString("HH:mm:ss.fff") + "  " + msg + Environment.NewLine);
        }
        catch { }
    }

    void SaveRecords()
    {
        try
        {
            File.WriteAllText(RecordsPath(),
                "Best=" + bestShotDist.ToString("0.0", CultureInfo.InvariantCulture) + Environment.NewLine +
                "Cars=" + carsHit + Environment.NewLine +
                "Peds=" + pedsHit + Environment.NewLine);
        }
        catch { }
    }

