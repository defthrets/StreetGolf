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

    // =====================================================================
    //  input helpers - these read DISABLED controls, so they keep working
    //  while the stance has the normal controls switched off, and they
    //  cover pad and keyboard from the same call.
    // =====================================================================
    static bool CPressed(Control c)
    {
        return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 0, (int)c)
            || Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 2, (int)c);
    }

    static bool CJust(Control c)
    {
        return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 0, (int)c)
            || Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 2, (int)c);
    }

    static float CVal(Control c)
    {
        return Function.Call<float>(Hash.GET_DISABLED_CONTROL_NORMAL, 0, (int)c);
    }

    // Triggers can sit in either control group depending on context, so take
    // whichever reads stronger.
    static float CValAny(Control c)
    {
        float a = Function.Call<float>(Hash.GET_DISABLED_CONTROL_NORMAL, 0, (int)c);
        float b = Function.Call<float>(Hash.GET_DISABLED_CONTROL_NORMAL, 2, (int)c);
        return a > b ? a : b;
    }

    static bool UsingPad()
    {
        return !Function.Call<bool>(Hash.IS_USING_KEYBOARD_AND_MOUSE, 2);
    }

    // Swing is the trigger: RT on a pad, left mouse or space on a keyboard.
    // Kept separate from the skip button so A is free to mean "next ball".
    //
    // RT is an ANALOG axis, and the boolean pressed/just-pressed checks are
    // unreliable for analog inputs - which is why RT appeared to do nothing.
    // Read the axis value instead, across every control id RT reports through,
    // and work out the press edge here rather than trusting the game for it.
    bool swingNow, swingPrev;

    float TriggerValue()
    {
        float v = CValAny(Control.ScriptRT);
        float a = CValAny(Control.Attack);
        if (a > v) v = a;
        float b = CValAny(Control.Attack2);
        if (b > v) v = b;
        return v;
    }

    bool SwingInputRaw()
    {
        if (TriggerValue() > 0.2f) return true;
        if (CPressed(Control.Attack) || CPressed(Control.Attack2) || CPressed(Control.ScriptRT)) return true;
        if (!UsingPad() && CPressed(Control.Jump)) return true;
        return false;
    }

    void PollSwingInput()
    {
        swingPrev = swingNow;
        swingNow = SwingInputRaw();
    }

    bool SwingHeld() { return swingNow; }

    bool SwingJustPressed() { return swingNow && !swingPrev; }

    // A on a pad, space or enter on a keyboard.
    static bool SkipPressed()
    {
        return CJust(Control.Jump) || CJust(Control.FrontendAccept);
    }

    static bool CancelPressed()
    {
        return CJust(Control.FrontendCancel) || CJust(Control.VehicleExit);
    }

    // On a pad this is deliberately limited to the shoulder buttons and the
    // d-pad. The weapon wheel and context inputs were in here before and one
    // of them shares a binding with the right trigger, which is why pulling RT
    // was cycling clubs instead of swinging.
    static bool ClubNext()
    {
        if (UsingPad()) return CJust(Control.FrontendRb);
        return CJust(Control.Context) || CJust(Control.WeaponWheelNext) || CJust(Control.FrontendRb);
    }

    static bool ClubPrev()
    {
        if (UsingPad()) return CJust(Control.FrontendLb);
        return CJust(Control.Cover) || CJust(Control.WeaponWheelPrev) || CJust(Control.FrontendLb);
    }

    static bool NewBallPressed()
    {
        return CJust(Control.Reload);
    }

    // d-pad on a controller, arrow keys on a keyboard
    static bool MenuUp() { return CJust(Control.FrontendUp); }
    static bool MenuDown() { return CJust(Control.FrontendDown); }
    static bool MenuLeft() { return CJust(Control.FrontendLeft); }
    static bool MenuRight() { return CJust(Control.FrontendRight); }

    // =====================================================================
    //  main loop
    // =====================================================================
    Keys pendingKey = Keys.None;
    int lastKeyTime;
    int cleanupTicks;

    void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyCode == keyToggle || e.KeyCode == keyNewBall
            || e.KeyCode == keyBallMode || e.KeyCode == keyPolice)
            pendingKey = e.KeyCode;
    }

    void OnAborted(object sender, EventArgs e)
    {
        try { Shutdown(); } catch { }
        ReleasePrompts();
    }

    void OnTick(object sender, EventArgs e)
    {
        float dt = Game.LastFrameTime;
        if (dt <= 0f || dt > 0.25f) dt = 0.016f;

        // THE MARK, for the first few seconds. Every mod in the set draws the same row in
        // the same corner and they claim rows off each other through the AppDomain, so a
        // folder with eight of them installed gets one list rather than eight announcements.
        SplashRow();

        // THE TICKER IS GONE AND THE ROW SAYS IT. Eight mods each posting their own over
        // the same three seconds was a wall of text nobody read to the bottom of. The key it
        // named is in StreetGolf.ini and in the log, which is where a key belongs.
        if (!announced)
        {
            announced = true;
            Log("Street Golf " + VERSION + " by " + AUTHOR + " loaded. Toggle " + keyToggle + ".");
        }

        UpdateHudAnim(dt);
        HandleToggleInput();

        // balls keep flying and hitting things no matter what state we are in
        UpdateShots(dt);

        if (mode == Mode.Off)
        {
            // keep letting go of the golfer until he is genuinely out of it
            if (cleanupTicks > 0)
            {
                cleanupTicks--;
                Ped idle = Game.Player.Character;
                if (idle != null && idle.Exists() && !idle.IsDead && !idle.IsInjured && StillGolfing(idle))
                    ReleasePed(idle, cleanupTicks < 4);
            }
            DrawTrails();
            if (showHud && hudArrive > 0.01f) DrawHud();   // let it fade out
            DrawOffHud();
            return;
        }

        Ped ped = Game.Player.Character;
        if (Incapacitated(ped) || Game.IsCutsceneActive)
        {
            if (mode != Mode.Off)
            {
                Log("auto shutdown: " + (blockReason.Length > 0 ? blockReason : "cutscene"));
                Shutdown(true);
            }
            DrawTrails();
            return;
        }
        if (ped.IsInVehicle() || ped.IsRagdoll || ped.IsSwimming)
        {
            if (mode != Mode.Off)
            {
                Log("auto shutdown: " + (ped.IsInVehicle() ? "in vehicle" : (ped.IsRagdoll ? "ragdoll" : "swimming")));
                Shutdown(false);
            }
            DrawTrails();
            return;
        }
        if (Function.Call<bool>(Hash.IS_PAUSE_MENU_ACTIVE))
        {
            DrawTrails();
            return;
        }

        stateTime += dt;
        DisableControls();
        PollSwingInput();
        UpdatePolice(dt);

        UpdateMenu();
        UpdateCopResponse();
        if (mode != Mode.Watch && NewBallPressed()) ResetTee();
        if (mode != Mode.Watch && CancelPressed())
        {
            Shutdown();
            DrawTrails();
            return;
        }

        switch (mode)
        {
            case Mode.Ready: UpdateReady(ped, dt); break;
            case Mode.Backswing: UpdateBackswing(ped, dt); break;
            case Mode.Swing: UpdateSwing(ped, dt); break;
            case Mode.Watch: UpdateWatch(ped, dt); break;
        }

        DrawTrails();
        if (aimLine && (mode == Mode.Ready || mode == Mode.Backswing)) DrawAimLine();
        if (showHud)
        {
            DrawHud();
            DrawFootHud();
        }
    }

    void HandleToggleInput()
    {
        Keys k = pendingKey;
        pendingKey = Keys.None;
        bool wantToggle = false;
        bool wantNewBall = false;
        bool wantMode = false;
        bool wantPolice = false;

        if (k != Keys.None)
        {
            int now = Game.GameTime;
            if (now - lastKeyTime >= 250)
            {
                lastKeyTime = now;
                if (k == keyToggle) wantToggle = true;
                else if (k == keyNewBall) wantNewBall = true;
                else if (k == keyBallMode) wantMode = true;
                else if (k == keyPolice) wantPolice = true;
                Log("key " + k + " accepted");
            }
        }
        // pad: hold LB and tap RB
        if (!wantToggle && CPressed(padToggleHold) && CJust(padTogglePress))
        {
            int now = Game.GameTime;
            if (now - lastKeyTime >= 250) { lastKeyTime = now; wantToggle = true; }
        }

        if (wantToggle)
        {
            Log("toggle fired, mode was " + mode);
            if (mode == Mode.Off) Startup(); else Shutdown();
        }
        else if (wantNewBall && mode != Mode.Off && mode != Mode.Watch)
        {
            ResetTee();
        }
        else if (wantMode && mode != Mode.Off)
        {
            ballMode = (BallMode)(((int)ballMode + 1) % MODE_COUNT);
            clubPop = 1f;
            Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "NAV_UP_DOWN", "HUD_FRONTEND_DEFAULT_SOUNDSET", true);
            ModeToast();
        }
        else if (wantPolice && mode != Mode.Off)
        {
            policeWanted = !policeWanted;
            if (policeWanted) noticedNotified = false;
            else RestorePolice();
            Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "NAV_UP_DOWN", "HUD_FRONTEND_DEFAULT_SOUNDSET", true);
            PoliceToast();
        }
    }

    // =====================================================================
    //  start / stop
    // =====================================================================
    void Startup()
    {
        Ped ped = Game.Player.Character;
        Log("Startup requested");
        if (ped == null || !ped.Exists())
        {
            Log("  refused: no player ped");
            return;
        }
        if (ped.IsInVehicle())
        {
            Notify("~y~Street Golf:~s~ step out of the vehicle first.");
            Log("  refused: in a vehicle");
            return;
        }
        if (Incapacitated(ped))
        {
            Notify("~y~Street Golf:~s~ cannot start right now (" + blockReason + ").");
            offHintUntil = Game.GameTime + 5000;
            Log("  refused: " + blockReason);
            return;
        }
        if (!LoadAnims())
        {
            Notify("~r~Street Golf:~s~ the golf animations would not load.");
            Log("  refused: anim dictionaries would not load");
            return;
        }
        aimOffset = 0f;
        aimDeg = NormDeg(GameplayCamera.Rotation.Z);
        swingNow = swingPrev = true;   // ignore a trigger already held on entry
        graceLeft = policeGrace;
        noticedNotified = false;
        sessionCars = 0;
        sessionPeds = 0;
        pedHitTimes.Clear();
        recentPedHits = 0;
        try { baseWanted = Function.Call<int>(Hash.GET_PLAYER_WANTED_LEVEL, Game.Player.Handle); }
        catch { baseWanted = 0; }
        ped.Weapons.Select(WeaponHash.Unarmed, true);
        Function.Call(Hash.CLEAR_PED_TASKS, ped.Handle);
        ped.CanRagdoll = false;
        Function.Call(Hash.SET_PED_CAN_PLAY_AMBIENT_ANIMS, ped.Handle, false);
        AttachClub(ped, clubIndex);
        AnchorStance(ped, true);
        SpawnTeeBall(ped);
        SetMode(Mode.Ready);
        Log("  started");
        lastMenuMove = Game.GameTime;     // the settings drawer shows itself for a moment, so it gets found
        if (showHud)
            Toast("swing", "SWING AWAY", UsingPad() ? "hold RT to swing, A for the next ball" : "hold SPACE to swing", C_GREEN, 3600);
        else
            Notify("~g~Street Golf~s~ - swing away. " + (UsingPad() ? "hold ~b~RT~s~ to swing, ~b~A~s~ for the next ball." : "hold ~b~SPACE~s~ to swing."));
    }

    void Shutdown() { Shutdown(false); }

    // gentle: the game has taken the player away from us - wasted, busted,
    // tased, or a cutscene started. Pack up the golf gear and get out of the
    // way, but do NOT clear his tasks, because that is the animation the game
    // is now playing and fighting it is what leaves him standing in a stance.
    void Shutdown(bool gentle)
    {
        Ped ped = Game.Player.Character;
        ReleaseCops();
        RestorePolice();
        ReleaseCam();
        DetachClub();
        DeleteTeeBall();
        DeleteAllShots();

        if (gentle)
        {
            if (ped != null && ped.Exists())
            {
                try
                {
                    StopGolfAnims(ped);
                    ped.CanRagdoll = true;
                    ped.IsPositionFrozen = false;
                    Function.Call(Hash.SET_PED_CAN_PLAY_AMBIENT_ANIMS, ped.Handle, true);
                    Function.Call(Hash.SET_PED_CAN_PLAY_GESTURE_ANIMS, ped.Handle, true);
                }
                catch { }
            }
            cleanupTicks = 0;
        }
        else
        {
            ReleasePed(ped, true);
            cleanupTicks = 14;
        }

        if (mode != Mode.Off) SaveRecords();
        SetMode(Mode.Off);
    }

    // Anything that means the player is no longer ours to pose.
    bool Incapacitated(Ped ped)
    {
        blockReason = "";
        if (ped == null || !ped.Exists()) { blockReason = "no ped"; return true; }
        try
        {
            if (ped.IsDead) { blockReason = "dead"; return true; }
            if (Game.Player.IsDead) { blockReason = "player dead"; return true; }
            if (Function.Call<bool>(Hash.IS_PED_FATALLY_INJURED, ped.Handle)) { blockReason = "fatally injured"; return true; }
            if (ped.IsCuffed) { blockReason = "cuffed"; return true; }
            if (ped.IsBeingStunned) { blockReason = "tased"; return true; }
            if (Function.Call<bool>(Hash.IS_PED_RUNNING_ARREST_TASK, ped.Handle)) { blockReason = "arrest task"; return true; }
            if (Function.Call<bool>(Hash.IS_PLAYER_BEING_ARRESTED, Game.Player.Handle, false)) { blockReason = "being arrested"; return true; }
            if (Function.Call<bool>(Hash.IS_SCREEN_FADED_OUT)) { blockReason = "screen faded out"; return true; }
        }
        catch { blockReason = "state check threw"; return true; }
        return false;
    }

    // For the first few minutes of a session the police look the other way, so
    // you can tee off into traffic without the whole of Los Santos arriving.
    // Any stars you already had when you started are left alone: this stops the
    // mod CREATING heat, it does not wipe heat you earned yourself.
    void UpdatePolice(float dt)
    {
        unseenActive = false;
        int pnow = Game.GameTime;

        // forget bodies older than the window
        int cutoff = pnow - (int)(heatWindow * 1000f);
        for (int i = pedHitTimes.Count - 1; i >= 0; i--)
            if (pedHitTimes[i] < cutoff) pedHitTimes.RemoveAt(i);
        recentPedHits = pedHitTimes.Count;

        if (!policeWanted)
        {
            // switched off outright: they never take an interest
            Suppress();
            return;
        }

        if (graceLeft <= 0f)
        {
            // Grace is spent, but they still are not interested in a man
            // hitting golf balls. It takes a few bodies in quick succession,
            // or being seen doing it, before anyone calls it in.
            Ped me = Game.Player.Character;
            bool unseen = stealthWhenUnseen && me != null && me.Exists() && Unwitnessed(me);
            bool underTheRadar = recentPedHits < heatAfterPeds;

            if (unseen || underTheRadar)
            {
                unseenActive = true;
                Suppress();
            }
            else if (policeSuppressed)
            {
                RestorePolice();
                if (!noticedNotified)
                {
                    noticedNotified = true;
                    Toast("badge", "POLICE NOTICED", "that is too many people", C_RED, 3000);
                }
            }
            return;
        }
        if (policeGrace <= 0f) return;

        graceLeft -= dt;
        Suppress();

        if (graceLeft <= 0f)
        {
            Toast("clock", "GRACE OVER", "nobody cares until " + heatAfterPeds + " people go down", C_AMBER, 3200);
        }
    }

    void Suppress()
    {
        try
        {
            int ph = Game.Player.Handle;
            Function.Call(Hash.SET_POLICE_IGNORE_PLAYER, ph, true);
            policeSuppressed = true;
            if (Function.Call<int>(Hash.GET_PLAYER_WANTED_LEVEL, ph) > baseWanted)
            {
                Function.Call(Hash.SET_PLAYER_WANTED_LEVEL, ph, baseWanted, false);
                Function.Call(Hash.SET_PLAYER_WANTED_LEVEL_NOW, ph, false);
            }
        }
        catch { }
    }

    // No witness, no crime. Once the grace period is over the police still
    // stay out of it for as long as nobody has eyes on you: every living
    // person within range is checked for a clear line of sight to the golfer,
    // and one is enough to end it. Throttled, since it is a raycast per ped.
    bool Unwitnessed(Ped me)
    {
        witnessTick++;
        if ((witnessTick % 15) != 0) return lastUnwitnessed;
        lastUnwitnessed = true;
        try
        {
            Ped[] near = World.GetNearbyPeds(me.Position, witnessRange);
            if (near != null)
            {
                int checked_ = 0;
                for (int i = 0; i < near.Length && checked_ < 24; i++)
                {
                    Ped p = near[i];
                    if (p == null || !p.Exists()) continue;
                    if (p.Handle == me.Handle) continue;
                    if (p.IsDead || p.IsInjured) continue;
                    checked_++;
                    if (Function.Call<bool>(Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY, p.Handle, me.Handle, 17))
                    {
                        lastUnwitnessed = false;
                        break;
                    }
                }
            }
        }
        catch { lastUnwitnessed = false; }
        return lastUnwitnessed;
    }

    // A man with a golf club is a nuisance, not a gunfight. At one and two
    // stars the responders carry a nightstick up close and a taser further
    // back, so being caught means a beating or a stun rather than being shot
    // off the tee. Draw an actual firearm and this lifts at once: they answer
    // whatever you are holding.
    bool PlayerGunOut()
    {
        try
        {
            Ped p = Game.Player.Character;
            if (p == null || !p.Exists()) return false;
            // 6 = guns and throwables. A melee weapon or the club does not count.
            return Function.Call<bool>(Hash.IS_PED_ARMED, p.Handle, 6);
        }
        catch { return false; }
    }

    void UpdateCopResponse()
    {
        if (!lessLethalCops || !policeWanted) { ReleaseCops(); return; }

        int stars = 0;
        try { stars = Function.Call<int>(Hash.GET_PLAYER_WANTED_LEVEL, Game.Player.Handle); }
        catch { }

        bool want = stars >= 1 && stars <= lessLethalMaxStars && !PlayerGunOut();
        lessLethalOn = want;
        if (!want) { ReleaseCops(); return; }

        int now = Game.GameTime;
        if (now - copScanAt < 400) return;
        copScanAt = now;

        Ped me = Game.Player.Character;
        if (me == null || !me.Exists()) return;

        try
        {
            Ped[] near = World.GetNearbyPeds(me.Position, copRange);
            if (near == null) return;
            for (int i = 0; i < near.Length; i++)
            {
                Ped c = near[i];
                if (c == null || !c.Exists() || c.IsDead) continue;
                if (c.Handle == me.Handle) continue;

                int t = Function.Call<int>(Hash.GET_PED_TYPE, c.Handle);
                if (t != 6 && t != 27) continue;          // COP and SWAT only

                float d = c.Position.DistanceTo(me.Position);
                uint w = d < 9f ? (uint)WeaponHash.Nightstick : (uint)WeaponHash.StunGun;

                // Their firearms are left on them, only holstered. Nothing is
                // taken away, so the moment this lifts they are their normal
                // selves again.
                Function.Call(Hash.GIVE_WEAPON_TO_PED, c.Handle, w, 200, false, true);
                Function.Call(Hash.SET_CURRENT_PED_WEAPON, c.Handle, w, true);
                Function.Call(Hash.SET_PED_CAN_SWITCH_WEAPON, c.Handle, false);
                if (!tamedCops.Contains(c.Handle)) tamedCops.Add(c.Handle);
            }
        }
        catch { }
    }

    void ReleaseCops()
    {
        lessLethalOn = false;
        if (tamedCops.Count == 0) return;
        for (int i = 0; i < tamedCops.Count; i++)
        {
            try { Function.Call(Hash.SET_PED_CAN_SWITCH_WEAPON, tamedCops[i], true); }
            catch { }
        }
        tamedCops.Clear();
    }

    void RestorePolice()
    {
        policeSuppressed = false;
        try { Function.Call(Hash.SET_POLICE_IGNORE_PLAYER, Game.Player.Handle, false); }
        catch { }
    }

    bool StillGolfing(Ped ped)
    {
        for (int c = 0; c < 4; c++)
        {
            string a = BASE_ANIM[c];
            if (IsPlaying(ped, DICT_AI, a + "_idle_a")) return true;
            if (IsPlaying(ped, DICT_AI, c == BASE_PUTTER ? "putt_intro" : a + "_swing_intro")) return true;
            if (IsPlaying(ped, DICT_AI, c == BASE_PUTTER ? "putt_action" : a + "_swing_action")) return true;
            if (IsPlaying(ped, DICT_MP, c == BASE_PUTTER ? "putt_idle" : a + "_swing_idle")) return true;
        }
        return false;
    }

    // Freeing a ped from a scripted animation takes more than one call: stop
    // every clip, drop both task slots, clear the clip sets and put him back
    // into a normal motion state.
    void ReleasePed(Ped ped, bool hard)
    {
        if (ped == null || !ped.Exists()) return;
        try
        {
            StopGolfAnims(ped);
            Function.Call(Hash.CLEAR_PED_SECONDARY_TASK, ped.Handle);
            Function.Call(Hash.CLEAR_PED_TASKS, ped.Handle);
            if (hard)
            {
                Function.Call(Hash.CLEAR_PED_TASKS_IMMEDIATELY, ped.Handle);
                Function.Call(Hash.RESET_PED_MOVEMENT_CLIPSET, ped.Handle, 0.25f);
                Function.Call(Hash.RESET_PED_WEAPON_MOVEMENT_CLIPSET, ped.Handle);
            }
            Function.Call(Hash.FORCE_PED_MOTION_STATE, ped.Handle, Joaat("motionstate_idle"), false, 0, false);
            Function.Call(Hash.SET_PED_CAN_PLAY_AMBIENT_ANIMS, ped.Handle, true);
            Function.Call(Hash.SET_PED_CAN_PLAY_GESTURE_ANIMS, ped.Handle, true);
            ped.CanRagdoll = true;
            ped.IsPositionFrozen = false;
        }
        catch { }
    }

    void SetMode(Mode m)
    {
        mode = m;
        stateTime = 0f;
    }

    bool LoadAnims()
    {
        Function.Call(Hash.REQUEST_ANIM_DICT, DICT_AI);
        Function.Call(Hash.REQUEST_ANIM_DICT, DICT_MP);
        Function.Call(Hash.REQUEST_PTFX_ASSET);
        // used to stamp impact damage into walls where the ball lands
        Function.Call(Hash.REQUEST_WEAPON_ASSET, (uint)WeaponHash.Pistol, 31, 0);
        Function.Call(Hash.REQUEST_WEAPON_ASSET, (uint)WeaponHash.Pistol50, 31, 0);
        for (int i = 0; i < 120; i++)
        {
            bool a = Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, DICT_AI);
            bool b = Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, DICT_MP);
            if (a && b) return true;
            Wait(15);
        }
        return Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, DICT_AI);
    }

    // =====================================================================
    //  stance - the ped is anchored ONCE with an advanced anim task and then
    //  only ever turned with SET_ENTITY_HEADING.  Teleporting a ped every
    //  frame is what breaks the animation and leaves him standing in a
    //  T-pose, so it is never done here.
    // =====================================================================
    int Base() { return CLUB_BASE[clubIndex]; }
    bool IsPutter() { return CLUB_BASE[clubIndex] == BASE_PUTTER; }
    string IdleClip() { return BASE_ANIM[Base()] + "_idle_a"; }
    string IntroClip() { return IsPutter() ? "putt_intro" : BASE_ANIM[Base()] + "_swing_intro"; }
    string HoldClip() { return IsPutter() ? "putt_idle" : BASE_ANIM[Base()] + "_swing_idle"; }
    string ActionClip() { return IsPutter() ? "putt_action" : BASE_ANIM[Base()] + "_swing_action"; }

    float PedHeadingForAim() { return NormDeg(aimDeg - 90f); }

    bool IsPlaying(Ped ped, string dict, string clip)
    {
        return Function.Call<bool>(Hash.IS_ENTITY_PLAYING_ANIM, ped.Handle, dict, clip, 3);
    }

    float AnimPhase(Ped ped, string dict, string clip)
    {
        return Function.Call<float>(Hash.GET_ENTITY_ANIM_CURRENT_TIME, ped.Handle, dict, clip);
    }

    // Places and orients the golfer and starts the idle in a single task.
    void AnchorStance(Ped ped, bool force)
    {
        int now = Game.GameTime;
        if (!force && now - lastAnimPush < 400) return;
        lastAnimPush = now;

        string clip = IdleClip();
        float phase = 0f;
        if (IsPlaying(ped, DICT_AI, clip)) phase = AnimPhase(ped, DICT_AI, clip);

        Vector3 p = ped.Position;
        float h = PedHeadingForAim();
        Function.Call(Hash.TASK_PLAY_ANIM_ADVANCED, new InputArgument[] {
            ped.Handle, DICT_AI, clip,
            p.X, p.Y, p.Z,
            0f, 0f, h,
            4f, -4f, -1, (int)AnimationFlags.Loop, phase, 2, 0
        });
        noAnimFor = 0f;
    }

    void UpdateReady(Ped ped, float dt)
    {
        UpdateAim(ped, dt);
        KeepIdleAlive(ped, dt);
        PositionTeeBall(ped);
        Calibrate(ped, dt);

        if (ClubNext()) ChangeClub(ped, 1);
        else if (ClubPrev()) ChangeClub(ped, -1);

        if (reloadTimer > 0f)
        {
            reloadTimer -= dt;
            return;
        }
        if (SwingJustPressed()) StartBackswing(ped);
    }

    // aim follows wherever the camera looks, plus a fine offset on the stick
    void UpdateAim(Ped ped, float dt)
    {
        float fine = CVal(Control.MoveLeftRight);
        if (Math.Abs(fine) > 0.15f)
        {
            float speed = CPressed(Control.Sprint) ? fineAimSpeed * 0.3f : fineAimSpeed;
            aimOffset = NormDeg180(aimOffset - fine * speed * dt);
            if (aimOffset > 45f) aimOffset = 45f;
            if (aimOffset < -45f) aimOffset = -45f;
        }
        aimDeg = NormDeg(GameplayCamera.Rotation.Z + aimOffset);

        float want = PedHeadingForAim();
        float cur = ped.Heading;
        float diff = AngleDiff(want, cur);
        float step = 540f * dt;
        if (Math.Abs(diff) <= step) cur = want;
        else cur = NormDeg(cur + Math.Sign(diff) * step);
        Function.Call(Hash.SET_ENTITY_HEADING, ped.Handle, cur);
    }

    // watchdog: if the idle stops for any reason, push it again (rate limited
    // so it can never be re-issued every frame, which is what T-poses a ped)
    void KeepIdleAlive(Ped ped, float dt)
    {
        if (IsPlaying(ped, DICT_AI, IdleClip())) { noAnimFor = 0f; return; }
        noAnimFor += dt;
        if (noAnimFor > 0.35f) AnchorStance(ped, false);
    }

    void ChangeClub(Ped ped, int delta)
    {
        int now = Game.GameTime;
        if (now - lastClubSwitch < 150) return;
        lastClubSwitch = now;
        clubIndex = (clubIndex + delta + CLUB_COUNT) % CLUB_COUNT;
        clubPop = 1f;
        AttachClub(ped, clubIndex);
        AnchorStance(ped, true);
        calibrateAt = 0f;
        Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "NAV_UP_DOWN", "HUD_FRONTEND_DEFAULT_SOUNDSET", true);
    }

    // =====================================================================
    //  swing
    // =====================================================================
    void StartBackswing(Ped ped)
    {
        PlayAnim(ped, DICT_AI, IntroClip(), 4f, -4f, AnimationFlags.StayInEndFrame);
        PlaySoundOn("GOLF_BACK_SWING_HARD_MASTER", ped);
        charge = 0f;
        power = 0f;
        swingReleased = false;
        holdPlayed = false;
        SetMode(Mode.Backswing);
    }

    void UpdateBackswing(Ped ped, float dt)
    {
        UpdateAim(ped, dt);
        PositionTeeBall(ped);

        if (!holdPlayed)
        {
            string intro = IntroClip();
            if (IsPlaying(ped, DICT_AI, intro))
            {
                if (AnimPhase(ped, DICT_AI, intro) >= 0.95f)
                {
                    PlayAnim(ped, DICT_MP, HoldClip(), 4f, -4f, AnimationFlags.Loop);
                    holdPlayed = true;
                }
            }
            else if (stateTime > 0.35f)
            {
                PlayAnim(ped, DICT_MP, HoldClip(), 4f, -4f, AnimationFlags.Loop);
                holdPlayed = true;
            }
        }

        charge += dt;
        float t = charge / Math.Max(0.25f, powerChargeTime);
        float cycle = t % 2f;
        power = cycle <= 1f ? cycle : 2f - cycle;

        if (!SwingHeld()) swingReleased = true;
        if (swingReleased && (holdPlayed || stateTime > 0.6f)) StartSwing(ped);
    }

    void StartSwing(Ped ped)
    {
        PlayAnim(ped, DICT_AI, ActionClip(), 8f, -8f, AnimationFlags.None);
        PlaySoundOn("GOLF_FORWARD_SWING_VB_MASTER", ped);
        launched = false;
        SetMode(Mode.Swing);
    }

    void UpdateSwing(Ped ped, float dt)
    {
        PositionTeeBall(ped);
        if (launched)
        {
            // let the follow-through play out, then start watching the ball
            if (stateTime > 0.35f) BeginWatch();
            return;
        }

        bool hit = false;
        string act = ActionClip();
        if (IsPlaying(ped, DICT_AI, act))
        {
            if (AnimPhase(ped, DICT_AI, act) >= BASE_IMPACT[Base()]) hit = true;
        }
        else if (stateTime > 0.30f) hit = true;   // anim never started - hit anyway
        if (stateTime > 1.6f) hit = true;         // hard backstop

        if (hit) LaunchBall(ped);
    }

    void LaunchBall(Ped ped)
    {
        launched = true;
        if (teeBall == null || !teeBall.Exists())
        {
            SpawnTeeBall(ped);
            if (teeBall == null || !teeBall.Exists()) { BeginWatch(); return; }
        }

        Vector3 dir = HeadingToDir(aimDeg);
        float loft = clubLoft[clubIndex] * (float)(Math.PI / 180.0);
        bool sweet = power >= sweetLo && power <= sweetHi;
        float speed = clubSpeed[clubIndex] * (0.15f + 0.85f * power) * (sweet ? 1.06f : 1f);

        // missing the sweet spot bends the shot a little
        float bend = 0f;
        if (!sweet)
        {
            float err = power < sweetLo ? (sweetLo - power) : (power - sweetHi);
            bend = err * 9f * (power < sweetLo ? -1f : 1f);
        }
        Vector3 shotDir = HeadingToDir(NormDeg(aimDeg + bend));
        Vector3 vel = shotDir * (float)(speed * Math.Cos(loft)) + Vector3.WorldUp * (float)(speed * Math.Sin(loft));
        vel = ShapeLaunch(vel, shotDir, speed);

        Prop b = teeBall;
        teeBall = null;
        Vector3 origin = b.Position;

        b.IsPositionFrozen = false;
        b.IsCollisionEnabled = true;
        b.IsRecordingCollisions = true;
        Function.Call(Hash.ACTIVATE_PHYSICS, b.Handle);
        Function.Call(Hash.SET_OBJECT_PHYSICS_PARAMS, b.Handle, -1f, -1f, 0f, 0f, 0.01f, -1f, -1f, -1f, -1f, -1f, -1f);
        Function.Call(Hash.APPLY_FORCE_TO_ENTITY, b.Handle, 1, 0.001f, 0.001f, 0f, 0f, 0f, 0f, 0, false, false, true, false, true);
        // The cap comes off BEFORE the velocity goes on. The other way round
        // the velocity was clamped to the old cap as it was set, and lifting
        // the cap afterwards did nothing, which is why a super shot flew like
        // an ordinary one.
        Function.Call(Hash.SET_ENTITY_MAX_SPEED, b.Handle, ModeTopSpeed());
        b.Velocity = vel;
        b.SetNoCollision(ped, true);

        Shot s = new Shot();
        s.ball = b;
        s.origin = origin;
        s.born = Game.GameTime;
        s.prevVz = vel.Z;
        s.mode = ballMode;
        s.mult = ballMode == BallMode.Super ? superMult : 1f;
        s.prevPos = origin;
        s.lastPt = origin;
        s.pts.Add(origin);
        s.times.Add(s.born);
        shotsInPlay.Add(s);
        TrimShots();

        shots++;
        liveDist = 0f;
        strikePop = 1f;
        PlaySoundOn(BASE_SOUND[Base()], b);
        if (!IsPutter()) PlayFxAt("scr_golf_strike_fairway", origin, aimDeg);
        if (sweet) Toast("swing", "SWEET SPOT", "dead straight, and a little extra", C_GREEN, 1400);
    }

    // The multiplier is on the CARRY, not on the pace. Range goes with the
    // square of launch speed, so fifty times the distance is seven times the
    // speed, which the engine can still fly and the camera can still follow.
    // Fifty times the SPEED was three and a half kilometres a second: the
    // ball crossed the loaded world inside two frames and the game binned
    // it, which looked like nothing happening at all.
    float SuperSpeedFactor()
    {
        float f = (float)Math.Sqrt(superMult);
        if (f < 1f) f = 1f;
        if (f > 9.5f) f = 9.5f;
        return f;
    }

    Vector3 ShapeLaunch(Vector3 vel, Vector3 dir, float speed)
    {
        if (ballMode == BallMode.Super) return vel * SuperSpeedFactor();
        return vel;
    }

    float ModeTopSpeed()
    {
        if (ballMode == BallMode.Super) return 150f * SuperSpeedFactor() + 50f;
        return 150f;
    }

    // =====================================================================
    //  the settings drawer on the card
    //    0 BALL   1 SUPER SHOT   2 POLICE   3 BATONS   4 IMPACT
    //    5 CAR DAMAGE   6 WALL MARKS   7 TRAIL   8 AIM LINE
    //    9 AFTERTOUCH   10 UNITS
    // =====================================================================
    string MenuLabel(int i)
    {
        switch (i)
        {
            case 0: return "BALL";
            case 1: return "SUPER SHOT";
            case 2: return "POLICE";
            case 3: return "BATONS";
            case 4: return "IMPACT";
            case 5: return "CAR DAMAGE";
            case 6: return "WALL MARKS";
            case 7: return "TRAIL";
            case 8: return "AIM LINE";
            case 9: return "AFTERTOUCH";
            case 10: return "UNITS";
        }
        return "";
    }

    static string OnOff(bool v) { return v ? "ON" : "OFF"; }

    string MenuValue(int i)
    {
        switch (i)
        {
            case 0: return MODE_NAMES[(int)ballMode];
            case 1: return "x" + ((int)superMult);
            case 2: return OnOff(policeWanted);
            case 3: return OnOff(lessLethalCops);
            case 4: return "x" + impactPower.ToString("0.00");
            case 5: return OnOff(carDamage);
            case 6: return OnOff(impactMarks);
            case 7: return OnOff(trailEnabled);
            case 8: return OnOff(aimLine);
            case 9: return OnOff(airControl);
            case 10: return unitMode == 1 ? "YARDS" : (unitMode == 2 ? "METRES" : "AUTO");
        }
        return "";
    }

    string MenuIcon(int i)
    {
        switch (i)
        {
            case 0: return MODE_ICONS[(int)ballMode];
            case 1: return "super";
            case 2: return "badge";
            case 3: return "baton";
            case 4: return "impact";
            case 5: return "dent";
            case 6: return "crack";
            case 7: return "trail";
            case 8: return "aim";
            case 9: return "curve";
            case 10: return "ruler";
        }
        return "ball";
    }

    // one line under the list, for the row that is selected
    string MenuHint(int i)
    {
        switch (i)
        {
            case 0: return "plain, on fire, explosive, or just absurd";
            case 1: return "how many times further a super shot carries";
            case 2: return "the master switch for police interest";
            case 3: return "low stars bring sticks and tasers";
            case 4: return "how hard the ball hits everything";
            case 5: return "dents, glass and tyres where it lands";
            case 6: return "chips and cracks in whatever it strikes";
            case 7: return "the ribbon the ball leaves behind";
            case 8: return "the arc and the ring where it lands";
            case 9: return "lean on the ball in flight with the stick";
            case 10: return "yards, metres, or the game's own setting";
        }
        return "";
    }

    // rows that are a switch are drawn as one, the rest show their value
    static bool MenuIsToggle(int i)
    {
        return i == 2 || i == 3 || i == 5 || i == 6 || i == 7 || i == 8 || i == 9;
    }

    bool MenuBool(int i)
    {
        switch (i)
        {
            case 2: return policeWanted;
            case 3: return lessLethalCops;
            case 5: return carDamage;
            case 6: return impactMarks;
            case 7: return trailEnabled;
            case 8: return aimLine;
            case 9: return airControl;
        }
        return false;
    }

    // the next stop up or down from wherever the multiplier is now
    static float StepSuper(float cur, int dir)
    {
        if (dir > 0)
        {
            for (int i = 0; i < SUPER_STEPS.Length; i++)
                if (SUPER_STEPS[i] > cur + 0.01f) return SUPER_STEPS[i];
            return SUPER_STEPS[SUPER_STEPS.Length - 1];
        }
        for (int i = SUPER_STEPS.Length - 1; i >= 0; i--)
            if (SUPER_STEPS[i] < cur - 0.01f) return SUPER_STEPS[i];
        return SUPER_STEPS[0];
    }

    void PoliceToast()
    {
        if (policeWanted) Toast("badge", "POLICE ON", "they can take an interest again", C_AMBER, 2200);
        else Toast("badge", "POLICE OFF", "nobody is coming, swing away", C_SKY, 2200);
    }

    void MenuAdjust(int i, int dir)
    {
        switch (i)
        {
            case 0:
                ballMode = (BallMode)(((int)ballMode + dir + MODE_COUNT) % MODE_COUNT);
                clubPop = 1f;
                ModeToast();
                break;
            case 1:
                superMult = StepSuper(superMult, dir);
                if (ballMode == BallMode.Super) clubPop = 1f;
                break;
            case 2:
                policeWanted = !policeWanted;
                if (policeWanted) noticedNotified = false;
                else RestorePolice();
                break;
            case 3:
                lessLethalCops = !lessLethalCops;
                if (!lessLethalCops) ReleaseCops();
                break;
            case 4:
                impactPower += 0.25f * dir;
                if (impactPower < 0f) impactPower = 0f;
                if (impactPower > 3f) impactPower = 3f;
                break;
            case 5: carDamage = !carDamage; break;
            case 6: impactMarks = !impactMarks; break;
            case 7: trailEnabled = !trailEnabled; break;
            case 8: aimLine = !aimLine; break;
            case 9: airControl = !airControl; break;
            case 10: unitMode = (unitMode + dir + 3) % 3; break;
        }
    }

    void UpdateMenu()
    {
        int now = Game.GameTime;
        if (now - lastMenuMove < 160) return;

        if (MenuUp())
        {
            lastMenuMove = now;
            menuIndex = (menuIndex - 1 + MENU_COUNT) % MENU_COUNT;
            MenuBlip();
        }
        else if (MenuDown())
        {
            lastMenuMove = now;
            menuIndex = (menuIndex + 1) % MENU_COUNT;
            MenuBlip();
        }
        else if (MenuLeft())
        {
            lastMenuMove = now;
            MenuAdjust(menuIndex, -1);
            MenuBlip();
        }
        else if (MenuRight())
        {
            lastMenuMove = now;
            MenuAdjust(menuIndex, 1);
            MenuBlip();
        }
    }

    void MenuBlip()
    {
        Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "NAV_UP_DOWN", "HUD_FRONTEND_DEFAULT_SOUNDSET", true);
    }

    void BeginWatch()
    {
        Shot s = NewestShot();
        if (s == null || s.ball == null || !s.ball.Exists())
        {
            FinishWatch();
            return;
        }
        watchedShot = s;
        steerFrac = 1f;
        EnsureCam(s.ball.Position);
        SetMode(Mode.Watch);
    }

    void UpdateWatch(Ped ped, float dt)
    {
        Shot s = watchedShot;
        bool gone = (s == null || s.ball == null || !s.ball.Exists());

        // The camera stays on the ball for as long as you want it to, even
        // after the ball has stopped rolling. Only a button press ends it.
        // A Boom ball no longer exists the moment it goes off, and the camera
        // used to take that as its cue to go home, so the blast was never
        // seen. It stays on the blast now, pulled back a little, until the
        // same button press.
        if (SkipPressed() || CancelPressed() || (gone && (s == null || !s.hasEnd)))
        {
            FinishWatch();
            return;
        }
        if (gone)
        {
            TrackCam(s.endPos, Vector3.Zero, dt, 11f);
            return;
        }
        liveDist = s.dist;
        SteerBall(s, dt);
        TrackCam(s.ball.Position, s.ball.Velocity, dt);
    }

    // A little after-touch while the ball is up: the left stick leans on it
    // sideways and up or down. Authority is deliberately small and each shot
    // gets a fixed budget of it, so you can bend a ball round a bus but you
    // cannot fly it.
    void SteerBall(Shot s, float dt)
    {
        if (!airControl) return;
        if (s == null || s.ball == null || !s.ball.Exists()) return;

        float sx = CVal(Control.MoveLeftRight);
        float sy = CVal(Control.MoveUpDown);
        if (Math.Abs(sx) < 0.15f) sx = 0f;
        if (Math.Abs(sy) < 0.15f) sy = 0f;
        if (sx == 0f && sy == 0f) return;

        Vector3 vel = s.ball.Velocity;
        float speed = vel.Length();

        Vector3 flat = new Vector3(vel.X, vel.Y, 0f);
        if (flat.Length() < 0.02f) return;
        flat = flat.Normalized;
        Vector3 right = new Vector3(flat.Y, -flat.X, 0f);

        float hag = 0f;
        try { hag = s.ball.HeightAboveGround; }
        catch { }
        bool airborne = hag > 0.6f;

        if (airborne || speed > 2.5f)
        {
            // In the air, or skipping along at pace. Full authority while it is
            // flying, a good deal less once it is bouncing.
            if (airControlBudget <= 0f || s.steerUsed >= airControlBudget) return;
            if (speed < (airborne ? 6f : 2.5f)) return;

            float authority = airborne ? 1f : 0.45f;
            float lift = airControlPower * 0.6f * (airborne ? 1f : 0.3f);
            Vector3 dv = (right * (sx * airControlPower * authority)
                        + Vector3.WorldUp * (-sy * lift)) * dt;
            float mag = dv.Length();
            if (mag < 0.0001f) return;
            float room = airControlBudget - s.steerUsed;
            if (mag > room) { dv = dv.Normalized * room; mag = room; }
            s.steerUsed += mag;
            try { s.ball.Velocity = vel + dv; }
            catch { }
            steerFrac = 1f - (s.steerUsed / airControlBudget);
        }
        else if (speed > 0.35f)
        {
            // Rolling. Gentler again, kept flat so it cannot be lifted off the
            // road, and drawing on its own allowance so a shot that spent
            // everything bending through the air can still be curled into a
            // wheel arch at walking pace. Up and down lean on its pace, left
            // and right on its line.
            if (rollControlBudget <= 0f || s.rollSteerUsed >= rollControlBudget) return;

            Vector3 dv = (right * (sx * rollControlPower)
                        + flat * (-sy * rollControlPower * 0.7f)) * dt;
            dv.Z = 0f;
            float mag = dv.Length();
            if (mag < 0.0001f) return;
            float room = rollControlBudget - s.rollSteerUsed;
            if (mag > room) { dv = dv.Normalized * room; mag = room; }
            s.rollSteerUsed += mag;
            try { s.ball.Velocity = vel + dv; }
            catch { }
            steerFrac = 1f - (s.rollSteerUsed / rollControlBudget);
        }
    }

    float steerFrac = 1f;

    void FinishWatch()
    {
        watchedShot = null;
        ReleaseCam();
        Ped ped = Game.Player.Character;
        if (ped != null && ped.Exists())
        {
            // put the free camera back down the shot line so aim does not jump
            Function.Call(Hash.SET_GAMEPLAY_CAM_RELATIVE_HEADING, 90f - aimOffset);
            AnchorStance(ped, true);
            SpawnTeeBall(ped);
        }
        reloadTimer = reloadDelay;
        SetMode(Mode.Ready);
    }

    // =====================================================================
    //  club prop - attached to the hand bone exactly as the minigame does
    // =====================================================================
    void AttachClub(Ped ped, int index)
    {
        DetachClub();
        Model m = new Model(BASE_PROPS[CLUB_BASE[index]]);
        if (!m.IsValid || !m.Request(3000)) return;
        club = World.CreateProp(m, ped.Position + new Vector3(0f, 0f, 0.4f), false, false);
        m.MarkAsNoLongerNeeded();
        if (club == null || !club.Exists()) { club = null; return; }
        club.IsPersistent = true;
        int bone = Function.Call<int>(Hash.GET_PED_BONE_INDEX, ped.Handle, PH_R_HAND);
        Function.Call(Hash.ATTACH_ENTITY_TO_ENTITY, club.Handle, ped.Handle, bone,
            0f, 0f, 0f, 0f, 0f, 0f, false, false, false, false, 2, true, 0);
    }

    void DetachClub()
    {
        try
        {
            if (club != null && club.Exists()) { club.Detach(); club.Delete(); }
        }
        catch { }
        club = null;
    }

    // Works out where the club head actually sits, so the ball can be teed up
    // right under it instead of at a guessed offset.
    void Calibrate(Ped ped, float dt)
    {
        if (headKnown[Base()]) return;
        calibrateAt += dt;
        if (calibrateAt < 0.7f) return;
        calibrateAt = 0f;
        if (club == null || !club.Exists()) return;
        if (!IsPlaying(ped, DICT_AI, IdleClip())) return;

        OutputArgument omin = new OutputArgument();
        OutputArgument omax = new OutputArgument();
        Function.Call(Hash.GET_MODEL_DIMENSIONS, club.Model.Hash, omin, omax);
        Vector3 min = omin.GetResult<Vector3>();
        Vector3 max = omax.GetResult<Vector3>();
        Vector3 size = max - min;
        if (size.Length() < 0.3f) return;

        Vector3 a = (min + max) * 0.5f;
        Vector3 b = a;
        if (size.X >= size.Y && size.X >= size.Z) { a.X = min.X; b.X = max.X; }
        else if (size.Y >= size.Z) { a.Y = min.Y; b.Y = max.Y; }
        else { a.Z = min.Z; b.Z = max.Z; }

        Vector3 wa = Function.Call<Vector3>(Hash.GET_OFFSET_FROM_ENTITY_IN_WORLD_COORDS, club.Handle, a.X, a.Y, a.Z);
        Vector3 wb = Function.Call<Vector3>(Hash.GET_OFFSET_FROM_ENTITY_IN_WORLD_COORDS, club.Handle, b.X, b.Y, b.Z);
        Vector3 hand = Function.Call<Vector3>(Hash.GET_PED_BONE_COORDS, ped.Handle, SKEL_R_HAND, 0f, 0f, 0f);
        Vector3 head = (wa.DistanceToSquared(hand) > wb.DistanceToSquared(hand)) ? wa : wb;
        Vector3 loc = Function.Call<Vector3>(Hash.GET_OFFSET_FROM_ENTITY_GIVEN_WORLD_COORDS, ped.Handle, head.X, head.Y, head.Z);

        if (loc.Y < 0.15f || loc.Y > 1.5f || Math.Abs(loc.X) > 0.9f) return;
        headLocal[Base()] = loc;
        headKnown[Base()] = true;
    }

    // =====================================================================
    //  the ball on the tee
    // =====================================================================
    void SpawnTeeBall(Ped ped)
    {
        DeleteTeeBall();
        Model m = new Model(BALL_MODEL);
        if (!m.IsValid || !m.Request(3000)) return;
        teePos = TeeSpot(ped);
        teeBall = World.CreateProp(m, teePos, true, false);
        m.MarkAsNoLongerNeeded();
        if (teeBall == null || !teeBall.Exists()) { teeBall = null; return; }
        teeBall.IsPersistent = true;
        teeBall.LodDistance = 600;
        teeBall.IsCollisionEnabled = false;
        teeBall.IsPositionFrozen = true;
    }

    void DeleteTeeBall()
    {
        try
        {
            if (teeBall != null && teeBall.Exists()) teeBall.Delete();
        }
        catch { }
        teeBall = null;
    }

    void ResetTee()
    {
        Ped ped = Game.Player.Character;
        SpawnTeeBall(ped);
        AnchorStance(ped, true);
        reloadTimer = 0f;
        Toast("tee", "NEW BALL", "", C_MUTE, 900);
    }

    Vector3 TeeSpot(Ped ped)
    {
        Vector3 loc = headKnown[Base()] ? headLocal[Base()] : new Vector3(-ballSide, ballForward, 0f);
        Vector3 p = ped.GetOffsetPosition(new Vector3(loc.X, loc.Y, 0f));
        float gz;
        if (TryGround(new Vector3(p.X, p.Y, ped.Position.Z + 1.2f), out gz)) p.Z = gz + 0.03f;
        else p.Z = ped.Position.Z - 0.95f;
        return p;
    }

    void PositionTeeBall(Ped ped)
    {
        if (teeBall == null || !teeBall.Exists()) return;
        teePos = TeeSpot(ped);
        Function.Call(Hash.SET_ENTITY_COORDS_NO_OFFSET, teeBall.Handle, teePos.X, teePos.Y, teePos.Z, false, false, false);
    }

    // =====================================================================
    //  balls in the air
    // =====================================================================
    Shot NewestShot()
    {
        for (int i = shotsInPlay.Count - 1; i >= 0; i--)
        {
            if (shotsInPlay[i].ball != null && shotsInPlay[i].ball.Exists()) return shotsInPlay[i];
        }
        return null;
    }

    void TrimShots()
    {
        int guard = 0;
        while (shotsInPlay.Count > maxLiveBalls && guard < 64)
        {
            guard++;
            int idx = 0;
            if (shotsInPlay[0] == watchedShot)
            {
                if (shotsInPlay.Count < 2) break;
                idx = 1;
            }
            Shot old = shotsInPlay[idx];
            shotsInPlay.RemoveAt(idx);
            try { if (old.ball != null && old.ball.Exists()) old.ball.Delete(); }
            catch { }
        }
    }

    int shotTickCounter;

    int probesThisFrame;

    void UpdateShots(float dt)
    {
        shotTickCounter++;
        probesThisFrame = 0;
        int now = Game.GameTime;
        Ped me = Game.Player.Character;

        for (int i = shotsInPlay.Count - 1; i >= 0; i--)
        {
            Shot s = shotsInPlay[i];
            bool drop;
            try
            {
                drop = UpdateOneShot(s, dt, now, me, i == shotsInPlay.Count - 1);
            }
            catch (Exception ex)
            {
                // A ball going wrong must never take the whole script down with
                // it. Bin that one and carry on.
                Log("shot update failed: " + ex.Message);
                try { if (s.ball != null && s.ball.Exists()) s.ball.Delete(); }
                catch { }
                s.ball = null;
                drop = true;
            }
            if (drop) shotsInPlay.RemoveAt(i);
        }
    }

    // Returns true when this shot is finished with and should leave the list.
    bool UpdateOneShot(Shot s, float dt, int now, Ped me, bool newest)
    {
        if (s.ball == null || !s.ball.Exists())
        {
            // keep it only while its trail is still fading
            return s.pts.Count == 0 || s.times.Count == 0
                || now - s.times[s.times.Count - 1] > (int)(trailSeconds * 1000f);
        }

        s.age += dt;
        Vector3 pos = s.ball.Position;
        Vector3 vel = s.ball.Velocity;
        float speed = vel.Length();
        float life = ballLifetime * (s.mode == BallMode.Super ? 3f : 1f);

        // The world is streamed around the player, not the ball. Past a few
        // hundred metres there is no collision loaded where the ball is, and
        // it drops straight through the ground. Asking for collision at the
        // ball keeps a floor under it without moving the streaming focus
        // away from the golfer.
        if (newest || s == watchedShot)
        {
            try { Function.Call(Hash.REQUEST_COLLISION_AT_COORD, pos.X, pos.Y, pos.Z); }
            catch { }
        }

        float d = s.origin.DistanceTo2D(pos);
        if (d > s.dist) s.dist = d;
        if (!s.done && newest) liveDist = s.dist;

        if (trailEnabled && speed > 2f && pos.DistanceToSquared(s.lastPt) > 1.4f * 1.4f)
        {
            s.pts.Add(pos);
            s.times.Add(now);
            s.lastPt = pos;
            if (s.pts.Count > 80) { s.pts.RemoveAt(0); s.times.RemoveAt(0); }
        }

        if (!s.done)
        {
            Vector3 lastVel = s.prevVel;
            bool hadVel = s.hasPrevVel;
            s.prevVel = vel;
            s.hasPrevVel = true;

            ProcessImpact(s, pos, vel, speed, now, me);

            // A mode can destroy the ball outright at the moment of contact -
            // Boom does exactly that - so nothing below may touch it again
            // without checking that there is still a ball there.
            if (s.ball == null || !s.ball.Exists())
            {
                s.prevPos = pos;
                return false;
            }

            // a strike the forward probe did not see coming
            if (hadVel) KickCheck(s, lastVel, vel, dt, now, me);
            if (s.ball == null || !s.ball.Exists())
            {
                s.prevPos = pos;
                return false;
            }

            if (s.mode == BallMode.Boom && !s.spent && BoomTouchdown(s, pos, vel, dt, me))
            {
                s.prevPos = pos;
                return false;
            }

            if (!s.splashed && (s.ball.IsInWater || BelowWater(pos)))
            {
                s.splashed = true;
                PlaySoundOn("GOLF_BALL_IN_WATER_MASTER", s.ball);
                PlayFxAt("scr_golf_landing_water", pos, 0f);
                if (s.mode == BallMode.Boom && !s.spent)
                {
                    Detonate(s, pos, me);
                    s.prevPos = pos;
                    return false;
                }
                FinishShot(s);
            }

            if (s.ball == null || !s.ball.Exists())
            {
                s.prevPos = pos;
                return false;
            }

            // Still for most of a second is at rest. Speed alone decides it:
            // the height check that used to sit alongside asked the game how
            // high a golf ball was, which it does not answer reliably, and a
            // ball at the top of its arc is never slow for that long.
            if (speed < 0.2f) s.rest += dt; else s.rest = 0f;
            if (s.rest > 0.7f || s.age > life || pos.Z < -80f)
            {
                // a Boom ball that never met anything goes off where it stops
                if (s.mode == BallMode.Boom && !s.spent && pos.Z >= -80f)
                {
                    BoomAtRest(s, pos, me);
                    s.prevPos = pos;
                    return false;
                }
                FinishShot(s);
            }
        }

        s.prevPos = pos;

        if (s.ball != null && s != watchedShot
            && ((s.done && s.age > life) || s.age > life + 12f))
        {
            try { s.ball.Delete(); }
            catch { }
            s.ball = null;
        }
        return false;
    }

    void FinishShot(Shot s)
    {
        if (s.done) return;
        s.done = true;
        lastShotDist = s.dist;
        if (s.dist > bestShotDist)
        {
            bestShotDist = s.dist;
            if (shots > 1)
            {
                Toast("trophy", "LONGEST DRIVE", Dist(s.dist), C_GREEN, 2600);
                Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "GOLF_NEW_RECORD", "HUD_AWARDS", true);
            }
            SaveRecords();
        }
    }

    // =====================================================================
    //  IMPACTS
    //  A fast ball can pass straight through thin geometry in a single
    //  frame, so instead of trusting the collision flag we sweep a sphere
    //  along the path it travelled since the last frame.  That gives the
    //  exact contact point, the surface it struck and what was standing
    //  there, which is what everything below is scaled from.
    // =====================================================================
    // Synchronous probe: the result is available on the same frame it is
    // started, so a ball travelling 60 m/s never slips through a wall between
    // two ticks the way an async shape test would allow.
    const int PROBE_FLAGS = 1 | 2 | 4 | 8 | 16 | 64;

    bool SweepHit(Vector3 a, Vector3 b, Entity ignore, out Vector3 hitPos, out Vector3 normal,
                  out MaterialHash mat, out Entity hitEnt)
    {
        hitPos = Vector3.Zero;
        normal = Vector3.WorldUp;
        mat = default(MaterialHash);
        hitEnt = null;
        try
        {
            int ignoreH = (ignore != null && ignore.Exists()) ? ignore.Handle : 0;
            // Map, vehicles, people, objects and glass; not foliage, which the
            // ball passes through, and which a Boom ball has no business
            // going off in. The last argument used to be 7, which includes
            // "ignore glass": every probe went straight through a car window
            // as though it were not there, so glass was never what the ball
            // was found to have hit, and the window that broke was a guess.
            int handle = Function.Call<int>(Hash.START_EXPENSIVE_SYNCHRONOUS_SHAPE_TEST_LOS_PROBE,
                a.X, a.Y, a.Z, b.X, b.Y, b.Z, PROBE_FLAGS, ignoreH, 4);
            OutputArgument oHit = new OutputArgument();
            OutputArgument oPos = new OutputArgument();
            OutputArgument oNorm = new OutputArgument();
            OutputArgument oMat = new OutputArgument();
            OutputArgument oEnt = new OutputArgument();
            int status = Function.Call<int>(Hash.GET_SHAPE_TEST_RESULT_INCLUDING_MATERIAL,
                handle, oHit, oPos, oNorm, oMat, oEnt);
            if (status != 2) return false;
            if (!oHit.GetResult<bool>()) return false;
            hitPos = oPos.GetResult<Vector3>();
            normal = oNorm.GetResult<Vector3>();
            mat = (MaterialHash)oMat.GetResult<uint>();
            int eh = oEnt.GetResult<int>();
            if (eh != 0) hitEnt = Entity.FromHandle(eh);
            return true;
        }
        catch { return false; }
    }

    bool IsMine(Entity e, Ped me)
    {
        if (e == null) return false;
        int h = e.Handle;
        if (me != null && h == me.Handle) return true;
        if (club != null && club.Exists() && h == club.Handle) return true;
        if (teeBall != null && teeBall.Exists() && h == teeBall.Handle) return true;
        for (int i = 0; i < shotsInPlay.Count; i++)
        {
            Prop b = shotsInPlay[i].ball;
            if (b != null && b.Exists() && b.Handle == h) return true;
        }
        return false;
    }

    void ProcessImpact(Shot s, Vector3 cur, Vector3 vel, float speed, int now, Ped me)
    {
        if (speed < 2f) { s.prevVz = vel.Z; return; }
        if (now - s.lastImpact < 80) { s.prevVz = vel.Z; return; }
        if (probesThisFrame >= 4) { s.prevVz = vel.Z; return; }
        probesThisFrame++;

        Vector3 from = s.prevPos;
        if (from == Vector3.Zero) { s.prevPos = cur; s.prevVz = vel.Z; return; }

        Vector3 velN = vel / speed;

        // The ball never actually enters what it strikes: physics halts it a
        // few millimetres short of the surface. A probe drawn only between
        // last frame's position and this one therefore runs entirely OUTSIDE
        // every wall and reports nothing, which is why impacts were being
        // missed. Pushing the far end forward along the direction of travel
        // makes the probe reach into whatever the ball is up against.
        float lead = speed * 0.02f + 0.35f;
        if (lead > 1.4f) lead = 1.4f;
        Vector3 to = cur + velN * lead;

        dbgProbes++;
        Vector3 p, normal;
        MaterialHash mat;
        Entity e;
        bool hit = SweepHit(from, to, s.ball, out p, out normal, out mat, out e);
        if (hit && IsMine(e, me)) hit = false;
        if (hit)
        {
            dbgHits++;
            bool isEntity = (e != null && e.Exists());
            if (!isEntity)
            {
                // rolling along a surface is not a strike
                float approach = -Vector3.Dot(velN, normal);
                if (approach < 0.18f) hit = false;
            }
        }

        if (!hit)
        {
            FallbackImpact(s, cur, vel, speed, velN, now, me);
            s.prevVz = vel.Z;
            return;
        }

        Apply(s, e, p, normal, mat, vel, speed, now, me);
        s.prevVz = vel.Z;
    }

    // Backstop for the rare contact the probes miss: someone or something the
    // ball is actually touching.
    //
    // This used to take any car whose CENTRE was within a couple of metres of
    // the ball, which is a ball sailing a metre over the roof: a dent in a car
    // nothing touched, and a Boom ball going off in mid air over traffic. It
    // has to be inside the car's own box now, or inside a person's outline.
    // It also used to consult HasCollided, which stays true once a ball has
    // touched anything at all, and set Boom off the instant it armed.
    void FallbackImpact(Shot s, Vector3 cur, Vector3 vel, float speed, Vector3 velN, int now, Ped me)
    {
        if (speed < minImpactSpeed) return;
        if ((shotTickCounter % 2) != 0) return;
        if (s.age < 0.15f || s.origin.DistanceTo(cur) < 2f) return;   // still leaving the tee
        Vector3 back = Vector3.Zero - velN;

        try
        {
            Ped[] near = World.GetNearbyPeds(cur, 1.4f);
            if (near != null)
            {
                for (int i = 0; i < near.Length; i++)
                {
                    Ped pd = near[i];
                    if (pd == null || !pd.Exists()) continue;
                    if (me != null && pd.Handle == me.Handle) continue;
                    // a standing person, from the feet to the top of the head
                    Vector3 o = pd.Position;
                    float dx = cur.X - o.X, dy = cur.Y - o.Y, dz = cur.Z - o.Z;
                    if (dx * dx + dy * dy > 0.45f * 0.45f || dz < -1.1f || dz > 0.95f) continue;
                    Apply(s, pd, cur, back, default(MaterialHash), vel, speed, now, me);
                    return;
                }
            }
        }
        catch { }

        try
        {
            Vehicle[] vs = World.GetNearbyVehicles(cur, 8f);
            if (vs != null)
            {
                for (int i = 0; i < vs.Length; i++)
                {
                    Vehicle v = vs[i];
                    if (v == null || !v.Exists()) continue;
                    if (!InsideBox(v, cur, 0.15f)) continue;
                    Apply(s, v, cur, back, default(MaterialHash), vel, speed, now, me);
                    return;
                }
            }
        }
        catch { }
    }

    // Is a point inside the entity's model box, grown by a margin?
    static bool InsideBox(Entity ent, Vector3 world, float margin)
    {
        OutputArgument omin = new OutputArgument();
        OutputArgument omax = new OutputArgument();
        Function.Call(Hash.GET_MODEL_DIMENSIONS, ent.Model.Hash, omin, omax);
        Vector3 mn = omin.GetResult<Vector3>();
        Vector3 mx = omax.GetResult<Vector3>();
        Vector3 loc = Function.Call<Vector3>(Hash.GET_OFFSET_FROM_ENTITY_GIVEN_WORLD_COORDS,
            ent.Handle, world.X, world.Y, world.Z);
        return loc.X >= mn.X - margin && loc.X <= mx.X + margin
            && loc.Y >= mn.Y - margin && loc.Y <= mx.Y + margin
            && loc.Z >= mn.Z - margin && loc.Z <= mx.Z + margin;
    }

    // A strike the forward probe did not see coming. At a low frame rate or a
    // high speed a ball can reach a surface and come off it between two
    // frames, outside it in both, and a probe drawn between the two cuts the
    // corner and finds nothing. The bounce still shows in the velocity: the
    // ball has been knocked off the line gravity alone would have given it.
    // When that happens, look back along the line it arrived on.
    void KickCheck(Shot s, Vector3 lastVel, Vector3 vel, float dt, int now, Ped me)
    {
        if (now - s.lastImpact < 80) return;
        if (s.age < 0.12f || s.origin.DistanceTo(s.prevPos) < 1.5f) return;   // leaving the tee
        float lastSpeed = lastVel.Length();
        if (lastSpeed < 2f) return;

        Vector3 expect = lastVel + new Vector3(0f, 0f, -9.8f * dt);
        float kick = (vel - expect).Length();
        float need = lastSpeed * 0.18f;
        if (need < 2.5f) need = 2.5f;
        if (kick < need) return;

        Vector3 dir = lastVel / lastSpeed;
        float reach = lastSpeed * dt * 1.6f + 0.6f;
        if (reach > 12f) reach = 12f;
        dbgProbes++;
        Vector3 p, normal;
        MaterialHash mat;
        Entity e;
        if (!SweepHit(s.prevPos - dir * 0.25f, s.prevPos + dir * reach, s.ball, out p, out normal, out mat, out e)) return;
        if (IsMine(e, me)) return;
        if ((e == null || !e.Exists()) && -Vector3.Dot(dir, normal) < 0.12f) return;   // a graze
        dbgHits++;
        Apply(s, e, p, normal, mat, lastVel, lastSpeed, now, me);
    }

    float hitBoost = 1f;

    void Apply(Shot s, Entity e, Vector3 p, Vector3 normal, MaterialHash mat,
               Vector3 vel, float speed, int now, Ped me)
    {
        s.lastImpact = now;
        dbgImpacts++;

        float pw = (speed - 6f) / 48f;
        if (pw < 0f) pw = 0f;
        if (pw > 1.4f) pw = 1.4f;
        pw *= impactPower;
        bool heavy = speed >= minImpactSpeed;
        // a super shot hits as hard as it flies: the shove and the damage
        // grow with the square root of the multiplier, same as its pace
        hitBoost = s.mult > 1f ? (float)Math.Sqrt(s.mult) : 1f;

        if (!ModeImpact(s, e, p, normal, vel, speed, now, me)) return;

        Ped victim = e as Ped;
        Vehicle car = e as Vehicle;
        if (victim != null && victim.Exists()) { dbgLast = "ped"; BeanPed(s, victim, p, vel, pw, now); }
        else if (car != null && car.Exists()) { dbgLast = "car"; SmashCar(s, car, p, vel, mat, pw, now, heavy); }
        else { dbgLast = "world"; WorldImpact(s, e, p, normal, mat, vel, pw, now, heavy, me); }
    }

    // Returns false when the shot is over and nothing further should run.
    bool ModeImpact(Shot s, Entity e, Vector3 p, Vector3 normal, Vector3 vel, float speed, int now, Ped me)
    {
        switch (s.mode)
        {
            case BallMode.Boom:
                if (s.spent || !BoomArmed(s, p)) break;
                Detonate(s, p, me);
                return false;

            case BallMode.Fire:
                StartFireAt(p);
                if (e != null && e.Exists())
                {
                    try { Function.Call(Hash.START_ENTITY_FIRE, e.Handle); }
                    catch { }
                }
                break;

        }
        return true;
    }

    // An ordinary golf ball until it has genuinely got away from the tee.
    // Without this it went off on the very first contact found, which is
    // the ground under the golfer's own feet as the club meets the ball.
    static bool BoomArmed(Shot s, Vector3 at)
    {
        return s.age >= 0.25f && s.origin.DistanceTo(at) >= 6f;
    }

    // BOOM goes off the instant it comes down. A short probe straight down
    // from the ball, every frame: while there is clear air under it, it is
    // flying; once it has flown, the first frame the surface below it is
    // within one frame's fall is the landing, and the blast goes on that
    // surface. 1.1.2 asked the game whether the ball was in the air instead,
    // and the game only answers that for people and vehicles: for a golf
    // ball the answer was always no, so it went off a quarter of a second
    // after the club met it.
    bool BoomTouchdown(Shot s, Vector3 pos, Vector3 vel, float dt, Ped me)
    {
        float fall = vel.Z < 0f ? -vel.Z * dt : 0f;
        float reach = 0.6f + fall * 1.5f;
        Vector3 p, normal;
        MaterialHash mat;
        Entity e;
        bool below = SweepHit(pos + new Vector3(0f, 0f, 0.05f), pos - new Vector3(0f, 0f, reach), s.ball,
                              out p, out normal, out mat, out e);
        if (below && IsMine(e, me)) below = false;
        if (!below)
        {
            s.airborne = true;
            return false;
        }
        if (!s.airborne || !BoomArmed(s, pos)) return false;
        if (vel.Z > 0.5f) return false;                          // still climbing away from it
        if (pos.Z - p.Z > 0.18f + fall * 1.2f) return false;     // not down yet
        Detonate(s, p, me);
        return true;
    }

    // A Boom ball that never met anything worth going off on, a putt or a
    // shot that trickled to a stop, goes off where it stops. Unless that is
    // at the golfer's feet, when it is a dud.
    void BoomAtRest(Shot s, Vector3 pos, Ped me)
    {
        if (me != null && me.Exists() && me.Position.DistanceTo(pos) < 8f)
        {
            s.spent = true;
            Toast("bomb", "DUD", "too close to go off", C_MUTE, 1400);
            FinishShot(s);
            return;
        }
        Detonate(s, pos, me);
    }

    void Detonate(Shot s, Vector3 p, Ped me)
    {
        s.spent = true;
        s.endPos = p;
        s.hasEnd = true;
        try { World.AddExplosion(p, ExplosionType.Grenade, 4.0f, 1.5f, me, true, false); }
        catch { }
        Jolt(p, 1.1f);
        Rumble(340, 250);
        Toast("bomb", "BOOM", "", C_RED, 1600);
        try { if (s.ball != null && s.ball.Exists()) s.ball.Delete(); }
        catch { }
        s.ball = null;
        FinishShot(s);
    }

    int lastFireTime;

    void StartFireAt(Vector3 p)
    {
        int now = Game.GameTime;
        if (now - lastFireTime < 220) return;      // the game has a fire budget
        lastFireTime = now;
        try { Function.Call<int>(Hash.START_SCRIPT_FIRE, p.X, p.Y, p.Z, 2, false); }
        catch { }
    }

    // ---- pedestrians ------------------------------------------------------
    void BeanPed(Shot s, Ped victim, Vector3 p, Vector3 vel, float pw, int now)
    {
        if (victim.Handle == s.lastEnt && now - s.lastEntTime < 1500) return;
        s.lastEnt = victim.Handle;
        s.lastEntTime = now;
        if (!pedsRagdoll) return;

        try
        {
            victim.CanRagdoll = true;
            Function.Call(Hash.SET_PED_CAN_RAGDOLL, victim.Handle, true);
            int dur = (int)(1400 + 2600f * pw);
            Function.Call(Hash.SET_PED_TO_RAGDOLL, victim.Handle, dur, dur + 1500, 0, false, false, false);
            victim.ApplyDamage((int)((5f + 45f * pw) * hitBoost));
            if (pw > 0.35f)
                Function.Call(Hash.APPLY_PED_DAMAGE_PACK, victim.Handle, "BigHitByVehicle", 0f, 1f);

            Vector3 dir = vel.Length() > 0.01f ? vel.Normalized : Vector3.WorldNorth;
            float lift = hitBoost > 3f ? 3f : hitBoost;
            Vector3 push = dir * (3f + 14f * pw) * hitBoost + new Vector3(0f, 0f, (1.5f + 3f * pw) * lift);
            Function.Call(Hash.APPLY_FORCE_TO_ENTITY, victim.Handle, 1,
                push.X, push.Y, push.Z, 0f, 0f, 0f, 0, false, true, true, false, true);
        }
        catch { }

        pedsHit++;
        sessionPeds++;
        pedHitTimes.Add(now);
        PlaySoundOn("GOLF_BALL_IMPACT_CONCRETE_MASTER", s.ball);
        Jolt(p, 0.35f + 0.5f * pw);
        Rumble(180, (int)(90 + 120 * pw));
        Toast("ped", "FORE!", pw > 0.6f ? "clean headshot" : "pedestrian down", C_RED, 2000);
        SaveRecords();
    }

    static bool IsGlass(MaterialHash m)
    {
        switch (m)
        {
            case MaterialHash.CarGlassWeak:
            case MaterialHash.CarGlassMedium:
            case MaterialHash.CarGlassStrong:
            case MaterialHash.CarGlassBulletproof:
            case MaterialHash.CarGlassOpaque:
            case MaterialHash.GlassShootThrough:
            case MaterialHash.GlassBulletproof:
            case MaterialHash.GlassOpaque:
            case MaterialHash.EmissiveGlass:
                return true;
        }
        return false;
    }

    // The eight panes, in the order SMASH_VEHICLE_WINDOW numbers them, by the
    // name of the bone each one hangs on.
    static readonly string[] WINDOW_BONES = { "window_lf", "window_rf", "window_lr", "window_rr",
                                              "window_lm", "window_rm", "windscreen", "windscreen_r" };

    // The pane nearest the point the ball struck, if one is close enough and
    // still in one piece. Found from the car's own bones rather than guessed
    // from the shape of an average saloon, which put the windscreen out past
    // the bonnet and smashed a side window when the windscreen was hit.
    int NearestWindow(Vehicle v, Vector3 p, float within)
    {
        int best = -1;
        float bd = within * within;
        for (int i = 0; i < WINDOW_BONES.Length; i++)
        {
            try
            {
                int bone = Function.Call<int>(Hash.GET_ENTITY_BONE_INDEX_BY_NAME, v.Handle, WINDOW_BONES[i]);
                if (bone < 0) continue;
                if (!Function.Call<bool>(Hash.IS_VEHICLE_WINDOW_INTACT, v.Handle, i)) continue;
                Vector3 bp = Function.Call<Vector3>(Hash.GET_WORLD_POSITION_OF_ENTITY_BONE, v.Handle, bone);
                float d = bp.DistanceToSquared(p);
                if (d < bd) { bd = d; best = i; }
            }
            catch { }
        }
        return best;
    }

    // A dent where the ball struck, deeper the harder it hit. The last two
    // figures SET_VEHICLE_DAMAGE takes are not metres: the game's own scripts,
    // and the melee dents in Hoodrich, pass a hundred or more. The half a
    // metre this used to send made no visible mark at all, which is why a
    // car struck at sixty metres a second looked untouched.
    void Dent(Vehicle v, Vector3 loc, float pw, float boost)
    {
        float dmg = carDentDamage * (0.3f + 0.7f * pw) * boost;
        float rad = carDentRadius * (0.75f + 0.25f * pw);
        Function.Call(Hash.SET_VEHICLE_DAMAGE, v.Handle, loc.X, loc.Y, loc.Z, dmg, rad, true);
    }

    // ---- vehicles ---------------------------------------------------------
    void SmashCar(Shot s, Vehicle v, Vector3 p, Vector3 vel, MaterialHash mat, float pw, int now, bool heavy)
    {
        if (v.Handle == s.lastEnt && now - s.lastEntTime < 500) return;
        s.lastEnt = v.Handle;
        s.lastEntTime = now;

        Vector3 loc = Vector3.Zero;
        try { loc = Function.Call<Vector3>(Hash.GET_OFFSET_FROM_ENTITY_GIVEN_WORLD_COORDS, v.Handle, p.X, p.Y, p.Z); }
        catch { }

        // a super shot counts as a full strike whatever the meter said
        float pwx = pw * hitBoost;
        if (pwx > 1.4f) pwx = 1.4f;
        pw = pwx;

        bool glass = IsGlass(mat);
        bool pane = false;
        try
        {
            if (carDamage && heavy)
            {
                v.IsInvincible = false;
                Function.Call(Hash.SET_ENTITY_CAN_BE_DAMAGED, v.Handle, true);
                Function.Call(Hash.SET_VEHICLE_CAN_BE_VISIBLY_DAMAGED, v.Handle, true);

                // Glass the ball went into always goes. A firm hit on the frame
                // round a window, a pillar or the top of a door, takes that
                // window with it.
                int win = NearestWindow(v, p, glass ? 1.4f : 0.65f);
                if (win >= 0 && (glass || pw >= 0.3f))
                {
                    Function.Call(Hash.SMASH_VEHICLE_WINDOW, v.Handle, win);
                    pane = true;
                }

                // Bodywork dents where it was struck. Through the glass, only
                // a big hit carries on into the frame behind it.
                if (!glass || pw >= 0.6f)
                    Dent(v, loc, pw, hitBoost > 2f ? 2f : hitBoost);

                float bh = Function.Call<float>(Hash.GET_VEHICLE_BODY_HEALTH, v.Handle);
                float nh = bh - (60f + 220f * pw) * hitBoost;
                if (nh < 60f) nh = 60f;
                Function.Call(Hash.SET_VEHICLE_BODY_HEALTH, v.Handle, nh);

                // straight through the bonnet hurts the engine
                if (loc.Y > 1.0f && loc.Z < 0.45f && pw > 0.4f)
                {
                    float eh = Function.Call<float>(Hash.GET_VEHICLE_ENGINE_HEALTH, v.Handle);
                    float ne = eh - 200f * pw;
                    if (ne < 120f) ne = 120f;
                    Function.Call(Hash.SET_VEHICLE_ENGINE_HEALTH, v.Handle, ne);
                }

                // a low strike out at the corner takes the tyre off the rim
                if (pw > 0.55f && loc.Z < -0.05f && Math.Abs(loc.X) > 0.55f && Math.Abs(loc.Y) > 0.7f)
                {
                    int wheel = loc.Y > 0f ? (loc.X > 0f ? 1 : 0) : (loc.X > 0f ? 5 : 4);
                    Function.Call(Hash.SET_VEHICLE_TYRE_BURST, v.Handle, wheel, false, 1000f);
                }
            }

            Function.Call(Hash.START_VEHICLE_ALARM, v.Handle);

            // The jolt. Kept flat and applied only a third of the way out
            // towards the contact point: leverage at the full offset, plus
            // the upward component this used to carry, was enough to roll
            // cars onto their roofs. This still rocks them without lifting
            // or flipping. CarKnockback scales it.
            Vector3 dir = vel.Length() > 0.01f ? vel.Normalized : Vector3.WorldNorth;
            dir.Z = 0f;
            if (dir.Length() < 0.01f) dir = Vector3.WorldNorth;
            dir = dir.Normalized;
            float imp = (2f + 9f * pw) * carKnockback * hitBoost;
            Vector3 push = dir * imp;
            Vector3 arm = loc * 0.35f;
            Function.Call(Hash.APPLY_FORCE_TO_ENTITY, v.Handle, 1,
                push.X, push.Y, push.Z,
                arm.X, arm.Y, arm.Z,
                0, false, true, true, false, true);
        }
        catch { }

        carsHit++;
        sessionCars++;
        PlaySoundOn("GOLF_BALL_IMPACT_CONCRETE_MASTER", s.ball);
        Jolt(p, 0.55f + 0.75f * pw);
        Rumble(260, (int)(140 + 115 * pw));
        string nm = "car";
        try { nm = v.LocalizedName; } catch { }
        Toast("car", pane || pw > 0.7f ? "SMASH!" : "DINGER!", pane ? nm + ", window out" : nm, C_AMBER, 2000);
        SaveRecords();
    }

    // ---- walls, roads, props ---------------------------------------------
    void WorldImpact(Shot s, Entity e, Vector3 p, Vector3 normal, MaterialHash mat,
                     Vector3 vel, float pw, int now, bool heavy, Ped me)
    {
        // knock loose objects over
        Prop obj = e as Prop;
        if (obj != null && obj.Exists() && heavy)
        {
            try
            {
                Vector3 dir = vel.Length() > 0.01f ? vel.Normalized : Vector3.WorldNorth;
                Vector3 push = dir * (3f + 12f * pw) * hitBoost;
                Function.Call(Hash.APPLY_FORCE_TO_ENTITY, obj.Handle, 1,
                    push.X, push.Y, push.Z + 0.5f, 0f, 0f, 0f, 0, false, true, true, false, true);
            }
            catch { }
        }

        // Window panes are shattered by the impact bullet below rather than
        // by BREAK_ENTITY_GLASS, which was crashing the game.
        bool glass = IsGlass(mat);
        // glass gives way at any speed worth calling an impact, everything
        // else needs a proper strike before it will mark
        if (heavy || glass) ImpactMark(p, normal, vel, glass ? 1f : pw, me);

        if (glass) PlaySoundOn("GOLF_BALL_IMPACT_CONCRETE_MASTER", s.ball);
        else if (IsSand(mat)) { PlaySoundOn("GOLF_BALL_IMPACT_SAND_MASTER", s.ball); if (heavy) PlayFxAt("scr_golf_landing_bunker", p, 0f); }
        else if (IsSoftGround(mat)) { PlaySoundOn("GOLF_BALL_IMPACT_GRASS_MASTER", s.ball); if (heavy) PlayFxAt("scr_golf_landing_thick_grass", p, 0f); }
        else PlaySoundOn("GOLF_BALL_IMPACT_CONCRETE_MASTER", s.ball);

        if (heavy && pw > 0.45f)
        {
            Jolt(p, 0.18f + 0.3f * pw);
            Rumble(120, (int)(60 + 60 * pw));
        }
    }

    // A silent, invisible bullet at the contact point.  It leaves the game's
    // own impact damage on the surface - chipped concrete, cracked render,
    // spidered glass - and kicks up the matching dust, which is exactly the
    // mark a ball hammered into a wall should leave.
    int lastMarkTime;

    void ImpactMark(Vector3 p, Vector3 normal, Vector3 vel, float pw, Ped me)
    {
        if (!impactMarks || me == null || !me.Exists()) return;
        if (pw < 0.3f) return;
        int mnow = Game.GameTime;
        if (mnow - lastMarkTime < 110) return;
        lastMarkTime = mnow;
        if (p.DistanceToSquared(CamPos()) > 220f * 220f) return;

        // never let the tracer bullet find a bystander
        try
        {
            // the bullet only spans about three quarters of a metre around
            // the contact point, so this only has to keep it off someone
            // standing right on top of it
            Ped[] near = World.GetNearbyPeds(p, 1.6f);
            if (near != null)
            {
                for (int i = 0; i < near.Length; i++)
                {
                    if (near[i] != null && near[i].Exists() && near[i].Handle != me.Handle) return;
                }
            }
        }
        catch { return; }

        WeaponHash wh = pw > 0.75f ? WeaponHash.Pistol50 : WeaponHash.Pistol;
        try
        {
            if (!Function.Call<bool>(Hash.HAS_WEAPON_ASSET_LOADED, (uint)wh))
            {
                Function.Call(Hash.REQUEST_WEAPON_ASSET, (uint)wh, 31, 0);
                return;
            }
            Vector3 dir = vel.Length() > 0.01f ? vel.Normalized : (Vector3.Zero - normal);
            Vector3 a = p - dir * 0.5f;
            Vector3 b = p + dir * 0.25f;
            Function.Call(Hash.SHOOT_SINGLE_BULLET_BETWEEN_COORDS,
                a.X, a.Y, a.Z, b.X, b.Y, b.Z,
                0, true, (uint)wh, me.Handle, false, true, 600f);
        }
        catch { }
    }

    void Jolt(Vector3 at, float amount)
    {
        if (!camShake) return;
        try
        {
            float d = at.DistanceTo(CamPos());
            if (d > 70f) return;
            float k = amount * (1f - d / 70f);
            if (k <= 0.02f) return;
            if (k > 1.2f) k = 1.2f;
            if (cam != null && cam.Exists()) cam.Shake(CameraShake.Jolt, k);
            else Function.Call(Hash.SHAKE_GAMEPLAY_CAM, "JOLT_SHAKE", k);
        }
        catch { }
    }

    void Rumble(int ms, int strength)
    {
        if (!rumble) return;
        if (strength > 255) strength = 255;
        try { Function.Call(Hash.SET_CONTROL_SHAKE, 0, ms, strength); } catch { }
    }

    void DeleteAllShots()
    {
        for (int i = 0; i < shotsInPlay.Count; i++)
        {
            try { if (shotsInPlay[i].ball != null && shotsInPlay[i].ball.Exists()) shotsInPlay[i].ball.Delete(); }
            catch { }
        }
        shotsInPlay.Clear();
    }

    // =====================================================================
    //  ball camera
    // =====================================================================
    Camera cam;
    Vector3 camPos;
    Vector3 camDir = Vector3.WorldNorth;

    void EnsureCam(Vector3 ballPos)
    {
        camDir = HeadingToDir(aimDeg);
        camPos = ballPos - camDir * 6f + new Vector3(0f, 0f, 2.4f);
        try
        {
            if (cam == null || !cam.Exists())
                cam = World.CreateCamera(camPos, Vector3.Zero, 50f);
            cam.Position = camPos;
            cam.PointAt(ballPos);
            cam.IsActive = true;
            Function.Call(Hash.RENDER_SCRIPT_CAMS, true, true, 350, true, false, 0);
        }
        catch { cam = null; }
    }

    void TrackCam(Vector3 ballPos, Vector3 vel, float dt)
    {
        TrackCam(ballPos, vel, dt, 6.5f);
    }

    void TrackCam(Vector3 ballPos, Vector3 vel, float dt, float minBack)
    {
        if (cam == null || !cam.Exists()) return;
        Vector3 hv = new Vector3(vel.X, vel.Y, 0f);
        if (hv.Length() > 2f)
        {
            Vector3 nd = hv.Normalized;
            camDir = Vector3.Lerp(camDir, nd, 1f - (float)Math.Exp(-dt * 3.5f));
            if (camDir.Length() > 0.001f) camDir = camDir.Normalized;
        }
        // further back and much quicker on its feet when the ball is really
        // moving, or a super shot leaves the camera looking at empty road
        float speed = vel.Length();
        float back = 6.5f + (speed > 60f ? (speed - 60f) * 0.03f : 0f);
        if (back > 22f) back = 22f;
        if (back < minBack) back = minBack;
        float rate = speed > 80f ? 40f : 6f;
        Vector3 want = ballPos - camDir * back + new Vector3(0f, 0f, 2.6f + (back - 6.5f) * 0.25f);
        float gz;
        if (TryGround(new Vector3(want.X, want.Y, want.Z + 2f), out gz) && gz > want.Z - 0.8f) want.Z = gz + 0.8f;
        camPos = Vector3.Lerp(camPos, want, 1f - (float)Math.Exp(-dt * rate));
        cam.Position = camPos;
        cam.PointAt(ballPos + new Vector3(0f, 0f, 0.2f));
    }

    void ReleaseCam()
    {
        try
        {
            Function.Call(Hash.RENDER_SCRIPT_CAMS, false, true, 350, true, false, 0);
            if (cam != null && cam.Exists()) cam.Delete();
        }
        catch { }
        cam = null;
    }

    // =====================================================================
    //  ball trail - a soft, wide, see-through ribbon that fades out.
    //  Built from camera facing quads so it has real thickness, drawn in
    //  three overlapping layers so the edges melt away instead of ending
    //  on a hard line.
    // =====================================================================
    //  Four passes: three wide, very faint halo layers and one paper thin
    //  bright core. Overlapping translucent quads is the only glow the draw
    //  API allows, and it reads as one.  Half widths in metres at TrailWidth 1.
    static readonly float[] LAYER_W = { 0.220f, 0.100f, 0.045f, 0.012f };
    static readonly int[] LAYER_A = { 9, 18, 36, 165 };
    static readonly Color[] LAYER_C = {
        Color.FromArgb(255, 255, 140,  60),
        Color.FromArgb(255, 255, 180, 100),
        Color.FromArgb(255, 255, 220, 160),
        Color.FromArgb(255, 255, 250, 235)
    };
    int triBudget;
    float[] headDist = new float[128];

    Vector3 CamPos()
    {
        try
        {
            if (cam != null && cam.Exists()) return cam.Position;
        }
        catch { }
        return GameplayCamera.Position;
    }

    static float SmoothStep(float t)
    {
        if (t <= 0f) return 0f;
        if (t >= 1f) return 1f;
        return t * t * (3f - 2f * t);
    }

    void DrawTrails()
    {
        if (!trailEnabled || shotsInPlay.Count == 0) return;
        triBudget = 2000;
        int now = Game.GameTime;
        Vector3 eye = CamPos();
        float fadeMs = trailSeconds * 1000f;

        for (int i = 0; i < shotsInPlay.Count; i++)
        {
            Shot s = shotsInPlay[i];
            if (s.pts.Count < 2) continue;

            // drop points that have fully faded out at the tail
            while (s.times.Count > 0 && now - s.times[0] > fadeMs)
            {
                s.times.RemoveAt(0);
                s.pts.RemoveAt(0);
            }
            int n = s.pts.Count;
            if (n < 2) continue;
            if (s.pts[n - 1].DistanceToSquared(eye) > 400f * 400f) continue;
            if (n > headDist.Length) continue;

            // how far back along the ribbon each point sits from the ball
            float lead = 0f;
            try
            {
                if (s.ball != null && s.ball.Exists()) lead = s.ball.Position.DistanceTo(s.pts[n - 1]);
            }
            catch { }
            headDist[n - 1] = lead;
            for (int k = n - 2; k >= 0; k--)
                headDist[k] = headDist[k + 1] + s.pts[k].DistanceTo(s.pts[k + 1]);

            for (int L = 0; L < LAYER_W.Length; L++)
            {
                float w = LAYER_W[L] * trailWidth;
                for (int k = 1; k < n; k++)
                {
                    if (triBudget <= 0) return;

                    // tail: fades with age.  head: holds off for TrailGap
                    // metres behind the ball then ramps in over TrailFadeIn.
                    float f0 = 1f - (now - s.times[k - 1]) / fadeMs;
                    float f1 = 1f - (now - s.times[k]) / fadeMs;
                    if (f0 < 0f) f0 = 0f;
                    if (f1 < 0f) f1 = 0f;
                    f0 = SmoothStep(f0) * SmoothStep((headDist[k - 1] - trailGap) / trailFadeIn);
                    f1 = SmoothStep(f1) * SmoothStep((headDist[k] - trailGap) / trailFadeIn);
                    if (f0 <= 0.004f && f1 <= 0.004f) continue;

                    int alpha = (int)(LAYER_A[L] * (f0 + f1) * 0.5f);
                    if (alpha < 2) continue;
                    DrawSegment(s.pts[k - 1], s.pts[k],
                        w * Math.Max(0.35f, f0), w * Math.Max(0.35f, f1),
                        Color.FromArgb(alpha, LAYER_C[L].R, LAYER_C[L].G, LAYER_C[L].B), eye);
                }
            }
        }
    }

    void DrawSegment(Vector3 a, Vector3 b, float w0, float w1, Color col, Vector3 eye)
    {
        Vector3 d = b - a;
        float dl = d.Length();
        if (dl < 0.02f) return;
        Vector3 dn = d / dl;
        Vector3 toCam = eye - a;
        float tl = toCam.Length();
        if (tl < 0.05f) return;
        Vector3 tn = toCam / tl;
        Vector3 side = Vector3.Cross(dn, tn);
        float sl = side.Length();
        if (sl < 0.001f) return;
        side = side / sl;

        // a paper thin ribbon would drop below a pixel and start to shimmer
        // at range, so let it widen a little with distance
        float ds = 1f + tl / 140f;
        if (ds > 3.5f) ds = 3.5f;

        Vector3 s0 = side * (w0 * ds);
        Vector3 s1 = side * (w1 * ds);
        Vector3 p1 = a - s0, p2 = a + s0, p3 = b + s1, p4 = b - s1;
        World.DrawPolygon(p1, p2, p3, col);
        World.DrawPolygon(p1, p3, p4, col);
        triBudget -= 2;
    }

    // =====================================================================
    //  aim line and predicted landing spot
    // =====================================================================
    float predictedDist;
    Vector3 predictedLanding;
    bool hasPrediction;

    void DrawAimLine()
    {
        Ped ped = Game.Player.Character;
        Vector3 start = (teeBall != null && teeBall.Exists()) ? teeBall.Position : TeeSpot(ped);
        start = start + new Vector3(0f, 0f, 0.04f);
        Vector3 eye = CamPos();
        triBudget = 700;

        float pw = (mode == Mode.Backswing) ? power : 0.85f;
        Vector3 dir = HeadingToDir(aimDeg);
        float loft = clubLoft[clubIndex] * (float)(Math.PI / 180.0);
        float speed = clubSpeed[clubIndex] * (0.15f + 0.85f * pw);
        Vector3 v = dir * (float)(speed * Math.Cos(loft)) + Vector3.WorldUp * (float)(speed * Math.Sin(loft));
        Vector3 g = new Vector3(0f, 0f, -9.8f);
        Vector3 p = start;
        Color col = (mode == Mode.Backswing)
            ? Color.FromArgb(70, 255, 220, 120)
            : Color.FromArgb(45, 190, 235, 200);

        hasPrediction = false;
        const float step = 0.10f;
        for (int i = 0; i < 150; i++)
        {
            if (triBudget <= 0) break;
            Vector3 np = p + v * step;
            v = v + g * step;
            DrawSegment(p, np, 0.085f, 0.085f, col, eye);
            p = np;
            if (v.Z < 0f && (i % 2) == 0)
            {
                float gz;
                if (TryGround(new Vector3(np.X, np.Y, np.Z + 30f), out gz) && gz >= np.Z)
                {
                    predictedLanding = new Vector3(np.X, np.Y, gz);
                    predictedDist = start.DistanceTo2D(predictedLanding);
                    hasPrediction = true;
                    break;
                }
            }
        }
        if (hasPrediction)
        {
            World.DrawMarker(MarkerType.HorizontalCircleSkinny, predictedLanding + new Vector3(0f, 0f, 0.06f),
                Vector3.Zero, Vector3.Zero, new Vector3(2.4f, 2.4f, 0.6f),
                Color.FromArgb(90, 255, 225, 140), false, false, false, null, null, false);
        }
    }

    // =====================================================================
    //  HUD
    //
    //  Four pieces.
    //    THE CARD, top left. Title, the club in hand with its carry and a
    //      rail showing where it sits in the twelve, the ball, the session
    //      figures, the police, and a settings drawer the d-pad slides open.
    //    THE TEE MARKER, under the golfer's feet. Club, carry, and the power
    //      meter, right where you are looking when you swing.
    //    THE FEED, right hand side. A short stack of toasts for what the
    //      ball just did.
    //    THE CARRY, top centre, while the camera is on the ball.
    //  and a strip of button prompts along the bottom that uses the game's
    //  own button glyphs, so it shows the right buttons for a pad or a
    //  keyboard without being told.
    //
    //  Everything is laid out on a 720 tall canvas whose width follows the
    //  aspect ratio, so nothing stretches on an ultrawide. The icons are
    //  white silhouettes in StreetGolf/icons, tinted here; the fonts are the
    //  game's, with Pricedown for the title and the big number. Every
    //  animation runs off frame time, and every panel is a plain square:
    //  rounded corners built from sub pixel bands crawled from one frame to
    //  the next.
    // =====================================================================

    // ---- palette ----------------------------------------------------------
    static readonly Color C_INK = Color.FromArgb(232, 12, 15, 18);       // panel body
    static readonly Color C_INK2 = Color.FromArgb(255, 25, 30, 36);      // raised tiles
    static readonly Color C_LINE = Color.FromArgb(44, 255, 255, 255);    // hairlines
    static readonly Color C_TEXT = Color.FromArgb(255, 244, 240, 232);   // cream
    static readonly Color C_MUTE = Color.FromArgb(255, 168, 172, 178);
    static readonly Color C_DIMM = Color.FromArgb(255, 98, 104, 112);
    static readonly Color C_GREEN = Color.FromArgb(255, 106, 216, 118);  // fairway
    static readonly Color C_AMBER = Color.FromArgb(255, 255, 178, 56);   // selected, warned
    static readonly Color C_RED = Color.FromArgb(255, 234, 74, 62);
    static readonly Color C_SKY = Color.FromArgb(255, 112, 198, 238);
    static readonly Color C_FIRE = Color.FromArgb(255, 255, 128, 44);
    static readonly Color C_VIOLET = Color.FromArgb(255, 202, 140, 255);
    static readonly Color C_TRACK = Color.FromArgb(210, 0, 0, 0);
    static readonly Color[] MODE_TINT = { C_TEXT, C_FIRE, C_RED, C_VIOLET };

    // ---- layout, in canvas units --------------------------------------------
    const float CANVAS_H = 720f;
    const float CARD_X = 22f;
    const float CARD_Y = 54f;
    const float CARD_W = 236f;
    const float CARD_PAD = 12f;
    const float ROW_H = 20f;
    const float H_HEAD = 40f;
    const float H_CLUB = 54f;
    const float H_BALL = 30f;
    const float H_COPS = 28f;
    const float H_STATS = 26f;
    const float H_DRAWER = 24f;
    const float TITLE_TRACK = 1.7f;
    const int PULSE_MS = 1500;
    static readonly GTA.UI.Font FONT_LABEL = GTA.UI.Font.ChaletComprimeCologne;
    static readonly GTA.UI.Font FONT_TITLE = GTA.UI.Font.Pricedown;

    int rectBudget;
    int iconBudget;
    Dictionary<string, float> advCache = new Dictionary<string, float>();
    Dictionary<string, GTA.UI.CustomSprite> iconCache = new Dictionary<string, GTA.UI.CustomSprite>();
    bool iconsWarned;

    // animation state, all of it chased with frame time
    float hudArrive;        // 0 closed, 1 fully open
    float cardDim = 1f;     // the card steps back while the camera is on the ball
    float watchK;           // the carry readout
    float drawerK;          // the settings drawer
    float menuRimRow;       // the selection rim, in rows, gliding onto the row it is on
    float powerShown;       // chases the real power, so the meter never snaps
    float clubPop;          // brief swell when the club changes
    float strikePop;        // brief flare on contact
    float footFade;         // the marker under the golfer
    float footX, footY;
    bool footHas;
    float frameDt = 0.016f;

    // ---- the feed ------------------------------------------------------------
    class Feed
    {
        public string icon;
        public string title;
        public string sub;
        public Color tint;
        public int born;
        public int life;
    }
    List<Feed> feed = new List<Feed>();
    const int FEED_MAX = 3;
    const int FEED_IN_MS = 170;
    const int FEED_OUT_MS = 320;

    void Toast(string icon, string title, string sub, Color tint, int ms)
    {
        if (sub == null) sub = "";
        int now = Game.GameTime;
        // the same line twice in quick succession just refreshes the first
        for (int i = 0; i < feed.Count; i++)
        {
            if (feed[i].title == title && feed[i].sub == sub && now - feed[i].born < 400)
            {
                feed[i].born = now;
                feed[i].life = ms;
                return;
            }
        }
        Feed f = new Feed();
        f.icon = icon;
        f.title = title;
        f.sub = sub;
        f.tint = tint;
        f.born = now;
        f.life = ms;
        feed.Insert(0, f);
        while (feed.Count > FEED_MAX) feed.RemoveAt(feed.Count - 1);
    }

    void ModeToast()
    {
        Toast(MODE_ICONS[(int)ballMode], ModeTitle(), ModeShort(), MODE_TINT[(int)ballMode], 2200);
    }

    // ---- small helpers -------------------------------------------------------
    float CanvasW()
    {
        try { return GTA.UI.Screen.ScaledWidth; }
        catch { return 1280f; }
    }

    static float Pulse()
    {
        return 0.5f + 0.5f * (float)Math.Sin((Game.GameTime % PULSE_MS) / (double)PULSE_MS * 2.0 * Math.PI);
    }

    static float Ease(float t)
    {
        if (t <= 0f) return 0f;
        if (t >= 1f) return 1f;
        return t * t * (3f - 2f * t);
    }

    static float Chase(float cur, float target, float dt, float rate)
    {
        return cur + (target - cur) * (1f - (float)Math.Exp(-dt * rate));
    }

    static Color Fade(Color c, float k)
    {
        if (k <= 0f) return Color.FromArgb(0, c.R, c.G, c.B);
        if (k >= 1f) return c;
        return Color.FromArgb((int)(c.A * k), c.R, c.G, c.B);
    }

    static Color Blend(Color a, Color b, float t)
    {
        if (t <= 0f) return a;
        if (t >= 1f) return b;
        return Color.FromArgb(
            (int)(a.A + (b.A - a.A) * t),
            (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t));
    }

    void Bar(float left, float top, float w, float h, Color c)
    {
        if (rectBudget <= 0 || w <= 0.01f || h <= 0.01f || c.A <= 1) return;
        rectBudget--;
        try { new GTA.UI.ContainerElement(new PointF(left, top), new SizeF(w, h), c).ScaledDraw(); }
        catch { }
    }

    void Rule(float x, float y, float w, float a)
    {
        Bar(x, y, w, 1f, Fade(C_LINE, a));
    }

    // ---- icons ------------------------------------------------------------------
    //  White PNGs, tinted on the way out. A missing file is looked for once,
    //  remembered as missing, and simply not drawn: every icon sits next to
    //  a word that says the same thing, so the HUD reads without them.
    string IconPath(string name)
    {
        return Path.Combine(Path.Combine(Path.Combine(scriptsDir, "StreetGolf"), "icons"), name + ".png");
    }

    GTA.UI.CustomSprite GetIcon(string name)
    {
        GTA.UI.CustomSprite s;
        if (iconCache.TryGetValue(name, out s)) return s;
        s = null;
        try
        {
            string p = IconPath(name);
            if (File.Exists(p))
                s = new GTA.UI.CustomSprite(p, new SizeF(16f, 16f), new PointF(0f, 0f), Color.White, 0f, true);
            else if (!iconsWarned)
            {
                iconsWarned = true;
                Log("icons not found at " + p + " - the HUD will draw without them");
            }
        }
        catch (Exception ex)
        {
            Log("icon " + name + " failed to load: " + ex.Message);
            s = null;
        }
        iconCache[name] = s;
        return s;
    }

    // cx, cy is the centre
    void Icon(string name, float cx, float cy, float size, Color c)
    {
        if (iconBudget <= 0 || c.A <= 1 || size < 1f) return;
        GTA.UI.CustomSprite s = GetIcon(name);
        if (s == null) return;
        iconBudget--;
        try
        {
            s.Size = new SizeF(size, size);
            s.Position = new PointF(cx, cy);
            s.Color = c;
            s.ScaledDraw();
        }
        catch { }
    }

    // ---- text -------------------------------------------------------------------
    void Txt(string t, float x, float y, float scale, Color c, GTA.UI.Alignment a, GTA.UI.Font f)
    {
        Txt(t, x, y, scale, c, a, f, false);
    }

    void Txt(string t, float x, float y, float scale, Color c, GTA.UI.Alignment a, GTA.UI.Font f, bool shadow)
    {
        if (c.A <= 1 || string.IsNullOrEmpty(t)) return;
        try { new GTA.UI.TextElement(t, new PointF(x, y), scale, c, f, a, shadow, false).ScaledDraw(); }
        catch { }
    }

    void Txt(string t, float x, float y, float scale, Color c, GTA.UI.Alignment a)
    {
        Txt(t, x, y, scale, c, a, FONT_LABEL);
    }

    float TxtW(string t, float scale, GTA.UI.Font f)
    {
        if (string.IsNullOrEmpty(t)) return 0f;
        try { return GTA.UI.TextElement.GetScaledStringWidth(t, f, scale); }
        catch { return t.Length * scale * 24f; }
    }

    float Adv(char ch, float scale, GTA.UI.Font f)
    {
        string k = ch + "|" + scale.ToString("0.###") + "|" + (int)f;
        float w;
        if (advCache.TryGetValue(k, out w)) return w;
        try { w = GTA.UI.TextElement.GetScaledStringWidth(ch.ToString(), f, scale); }
        catch { w = scale * 14f; }
        advCache[k] = w;
        return w;
    }

    float TrackedWidth(string t, float scale, GTA.UI.Font f, float track)
    {
        float w = 0f;
        for (int i = 0; i < t.Length; i++) w += Adv(t[i], scale, f) + track;
        return w;
    }

    // letter spaced, for the short titles on the marker
    void Tracked(string t, float x, float y, float scale, Color c, GTA.UI.Font f, float track, bool centre)
    {
        if (string.IsNullOrEmpty(t) || c.A <= 1) return;
        float cx = centre ? x - TrackedWidth(t, scale, f, track) * 0.5f : x;
        for (int i = 0; i < t.Length; i++)
        {
            string ch = t[i].ToString();
            if (ch != " ") Txt(ch, cx, y, scale, c, GTA.UI.Alignment.Left, f);
            cx += Adv(t[i], scale, f) + track;
        }
    }

    static string Clock(float seconds)
    {
        if (seconds < 0f) seconds = 0f;
        int t = (int)seconds;
        return (t / 60).ToString() + ":" + (t % 60).ToString("00");
    }

    int Set() { return clubIndex / 4; }

    string ModeTitle()
    {
        if (ballMode == BallMode.Super) return MODE_NAMES[(int)ballMode] + "  x" + ((int)superMult);
        return MODE_NAMES[(int)ballMode];
    }

    string ModeShort()
    {
        if (ballMode == BallMode.Super) return MODE_SHORT[(int)ballMode] + " " + ((int)superMult) + " times as far";
        return MODE_SHORT[(int)ballMode];
    }

    // the carry shown for the club: the live prediction while lining up,
    // otherwise the club's own full swing figure
    string CarryText()
    {
        bool live = aimLine && hasPrediction && (mode == Mode.Ready || mode == Mode.Backswing);
        return Dist(live ? predictedDist : ClubReach(clubIndex));
    }

    int WantedStars()
    {
        try { return Function.Call<int>(Hash.GET_PLAYER_WANTED_LEVEL, Game.Player.Handle); }
        catch { return 0; }
    }

    bool DrawerWanted()
    {
        if (menuAutoHide <= 0f) return true;
        return Game.GameTime - lastMenuMove < (int)(menuAutoHide * 1000f);
    }

    // ---- per frame animation --------------------------------------------------------
    void UpdateHudAnim(float dt)
    {
        frameDt = dt;
        bool open = (mode != Mode.Off);
        hudArrive = Chase(hudArrive, open ? 1f : 0f, dt, 9f);
        if (hudArrive < 0.002f) hudArrive = 0f;
        if (hudArrive > 0.998f) hudArrive = 1f;

        cardDim = Chase(cardDim, mode == Mode.Watch ? 0.62f : 1f, dt, 6f);
        watchK = Chase(watchK, mode == Mode.Watch ? 1f : 0f, dt, 8f);

        bool wantFoot = (mode == Mode.Ready || mode == Mode.Backswing || mode == Mode.Swing);
        footFade = Chase(footFade, wantFoot ? 1f : 0f, dt, 10f);

        float pTarget = (mode == Mode.Backswing || mode == Mode.Swing) ? power : 0f;
        powerShown = Chase(powerShown, pTarget, dt, 26f);

        drawerK = Chase(drawerK, DrawerWanted() ? 1f : 0f, dt, 11f);
        if (drawerK < 0.002f) drawerK = 0f;
        if (drawerK > 0.998f) drawerK = 1f;

        menuRimRow = Chase(menuRimRow, menuIndex, dt, 18f);
        if (Math.Abs(menuRimRow - menuIndex) < 0.02f) menuRimRow = menuIndex;   // land, do not hover

        if (clubPop > 0f) clubPop -= dt * 3.2f;
        if (clubPop < 0f) clubPop = 0f;
        if (strikePop > 0f) strikePop -= dt * 2.4f;
        if (strikePop < 0f) strikePop = 0f;
    }

    // =====================================================================
    //  everything on the screen edges
    // =====================================================================
    void DrawHud()
    {
        float k = Ease(hudArrive);
        if (k <= 0.01f) return;
        rectBudget = 420;
        iconBudget = 72;

        DrawCard(k);
        DrawCarry(k);
        DrawFeed(k);
        DrawPrompts(k);

        if (debugHud)
            Txt("probes " + dbgProbes + "  hits " + dbgHits + "  impacts " + dbgImpacts +
                "  last " + dbgLast + "  RT " + TriggerValue().ToString("0.00") +
                "  icons " + iconCache.Count,
                CARD_X, 700f, 0.20f, Fade(C_DIMM, k), GTA.UI.Alignment.Left);
    }

    // ---- the card -----------------------------------------------------------------
    void DrawCard(float k)
    {
        float a = k * cardDim;
        float x = CARD_X, w = CARD_W;
        float y = CARD_Y - (1f - k) * 14f;
        float ix = x + CARD_PAD;
        float iw = w - CARD_PAD * 2f;
        float dk = Ease(drawerK);
        float listH = MENU_COUNT * ROW_H + 24f;
        float h = H_HEAD + H_CLUB + H_BALL + H_COPS + H_STATS + H_DRAWER + listH * dk + 4f;

        Bar(x, y, w, h, Fade(C_INK, a));
        Bar(x, y + 2f, w, H_HEAD - 2f, Fade(C_INK2, a));
        Bar(x, y, w, 2f, Fade(C_GREEN, a));

        Icon("ball", ix + 11f, y + 21f, 22f, Fade(C_TEXT, a));
        Txt("STREET GOLF", ix + 30f, y + 5f, 0.36f, Fade(C_TEXT, a), GTA.UI.Alignment.Left, FONT_TITLE);
        string ver = "v" + VERSION;
        Txt(ver, ix + iw - TxtW(ver, 0.18f, FONT_LABEL), y + 14f, 0.18f, Fade(C_DIMM, a), GTA.UI.Alignment.Left);

        // one line each, top to bottom: club, ball, police, session, settings
        float cy = y + H_HEAD;
        Rule(x, cy, w, a);
        cy = ClubRow(ix, cy, iw, a);
        Rule(x, cy, w, a);
        cy = BallRow(ix, cy, iw, a);
        Rule(x, cy, w, a);
        cy = CopsRow(ix, cy, iw, a);
        Rule(x, cy, w, a);
        cy = SessionRow(ix, cy, iw, a);
        Rule(x, cy, w, a);
        DrawerBlock(x, ix, cy, w, iw, a, dk);
    }

    // the club in hand, and how far it goes
    float ClubRow(float ix, float y, float iw, float a)
    {
        float ty = y + 10f;
        float tile = 34f;
        float flare = clubPop > strikePop ? clubPop : strikePop;
        Color ink = Blend(C_TEXT, C_AMBER, flare);

        Bar(ix, ty, tile, tile, Fade(C_INK2, a));
        Icon(CLUB_ICONS[Base()], ix + tile * 0.5f, ty + tile * 0.5f, 26f * (1f + clubPop * 0.15f), Fade(ink, a));

        float tx = ix + tile + 10f;
        Txt(CLUB_NAMES[clubIndex], tx, ty - 6f, 0.36f, Fade(ink, a), GTA.UI.Alignment.Left);
        Txt(SET_TAGS[Set()], tx, ty + 18f, 0.19f, Fade(C_MUTE, a), GTA.UI.Alignment.Left);

        string carry = CarryText();
        float cw = TxtW(carry, 0.32f, FONT_LABEL);
        Txt(carry, ix + iw - cw, ty - 5f, 0.32f, Fade(C_GREEN, a), GTA.UI.Alignment.Left);
        float lw = TxtW("CARRY", 0.17f, FONT_LABEL);
        Txt("CARRY", ix + iw - lw, ty + 19f, 0.17f, Fade(C_DIMM, a), GTA.UI.Alignment.Left);
        return y + H_CLUB;
    }

    // what leaves the tee
    float BallRow(float ix, float y, float iw, float a)
    {
        Color tint = MODE_TINT[(int)ballMode];
        float glow = ballMode == BallMode.Normal ? 1f : 0.8f + 0.2f * Pulse();
        Icon(MODE_ICONS[(int)ballMode], ix + 9f, y + 15f, 18f, Fade(tint, a * glow));
        Txt("BALL", ix + 24f, y + 5f, 0.24f, Fade(C_TEXT, a), GTA.UI.Alignment.Left);
        string t = ModeTitle();
        float tw = TxtW(t, 0.24f, FONT_LABEL);
        Txt(t, ix + iw - tw, y + 5f, 0.24f, Fade(tint, a), GTA.UI.Alignment.Left);
        return y + H_BALL;
    }

    void CopStatus(out string t, out Color c, out bool hot)
    {
        hot = false;
        if (!policeWanted) { t = "OFF"; c = C_SKY; return; }
        if (policeGrace > 0f && graceLeft > 0f)
        {
            t = "GRACE " + Clock(graceLeft);
            hot = graceLeft < 30f;
            c = hot ? C_AMBER : C_SKY;
            return;
        }
        if (unseenActive)
        {
            if (stealthWhenUnseen && lastUnwitnessed) { t = "NO WITNESS"; c = C_GREEN; return; }
            t = recentPedHits + " / " + heatAfterPeds + " HITS";
            hot = recentPedHits >= heatAfterPeds - 1;
            c = hot ? C_AMBER : C_GREEN;
            return;
        }
        if (lessLethalOn) { t = "BATONS OUT"; c = C_AMBER; hot = true; return; }
        t = "HEAT";
        c = C_RED;
        hot = true;
    }

    float CopsRow(float ix, float y, float iw, float a)
    {
        string t;
        Color c;
        bool hot;
        CopStatus(out t, out c, out hot);
        int stars = WantedStars();

        string ic = stars > 0 ? "badge" : (t == "NO WITNESS" ? "eye" : "badge");
        Icon(ic, ix + 9f, y + 14f, 18f, Fade(stars > 0 ? C_RED : c, a));
        Txt("POLICE", ix + 24f, y + 4f, 0.24f, Fade(C_TEXT, a), GTA.UI.Alignment.Left);
        if (stars > 0)
        {
            for (int i = 0; i < 5; i++)
            {
                bool lit = i < stars;
                float glow = lit ? 0.7f + 0.3f * Pulse() : 0.45f;
                Icon("star5", ix + iw - 8f - (4 - i) * 16f, y + 14f, 14f, Fade(lit ? C_RED : C_DIMM, a * glow));
            }
        }
        else
        {
            float tw = TxtW(t, 0.23f, FONT_LABEL);
            float glow = hot ? 0.65f + 0.35f * Pulse() : 1f;
            Txt(t, ix + iw - tw, y + 4f, 0.23f, Fade(c, a * glow), GTA.UI.Alignment.Left);
        }
        return y + H_COPS;
    }

    // the session in one line, and the best drive ever at the end of it
    float SessionRow(float ix, float y, float iw, float a)
    {
        string s = shots + " BALLS    " + sessionCars + " CARS    " + sessionPeds + " PEDS";
        Txt(s, ix, y + 5f, 0.21f, Fade(C_MUTE, a), GTA.UI.Alignment.Left);
        string best = Dist(bestShotDist);
        float bw = TxtW(best, 0.22f, FONT_LABEL);
        Txt(best, ix + iw - bw, y + 4f, 0.22f, Fade(C_GREEN, a), GTA.UI.Alignment.Left);
        Icon("trophy", ix + iw - bw - 12f, y + 13f, 13f, Fade(C_AMBER, a));
        return y + H_STATS;
    }

    void DrawerBlock(float x, float ix, float y, float w, float iw, float a, float dk)
    {
        Icon("dpad", ix + 8f, y + 12f, 15f, Fade(C_TEXT, a));
        Txt("SETTINGS", ix + 22f, y + 3f, 0.24f, Fade(C_TEXT, a), GTA.UI.Alignment.Left);

        // while it is shut, say what opens it
        if (dk < 0.98f)
        {
            string hint = UsingPad() ? "D-PAD" : "ARROW KEYS";
            float hk = a * (1f - dk) * (0.6f + 0.4f * Pulse());
            float hw = TxtW(hint, 0.20f, FONT_LABEL);
            Txt(hint, ix + iw - 18f - hw, y + 4f, 0.20f, Fade(C_AMBER, hk), GTA.UI.Alignment.Left);
            Icon("arrow_ud", ix + iw - 8f, y + 12f, 13f, Fade(C_AMBER, hk));
        }
        if (dk <= 0.02f) return;

        float la = a * dk;
        float listTop = y + H_DRAWER;
        float extent = (MENU_COUNT * ROW_H + 24f) * dk;   // rows past the drawer's edge are not drawn

        float rimY = listTop + menuRimRow * ROW_H;
        if (rimY + ROW_H <= listTop + extent + 0.5f)
        {
            Bar(x + 4f, rimY, w - 8f, ROW_H, Fade(C_AMBER, la * (0.10f + 0.05f * Pulse())));
            Bar(x + 4f, rimY, 2f, ROW_H, Fade(C_AMBER, la));
        }

        float vr = ix + iw - 14f;      // values end here, clear of the right chevron
        for (int i = 0; i < MENU_COUNT; i++)
        {
            float ry = listTop + i * ROW_H;
            if (ry + ROW_H > listTop + extent + 0.5f) break;
            bool sel = (i == menuIndex);

            Icon(MenuIcon(i), ix + 8f, ry + ROW_H * 0.5f, 13f, Fade(sel ? C_TEXT : C_DIMM, la));
            Txt(MenuLabel(i), ix + 22f, ry + 1f, 0.235f, Fade(sel ? C_TEXT : C_MUTE, la), GTA.UI.Alignment.Left);

            float vw;
            if (MenuIsToggle(i))
            {
                vw = 24f;
                Switch(vr - vw, ry + 5f, MenuBool(i), la);
            }
            else
            {
                string v = MenuValue(i);
                vw = TxtW(v, 0.235f, FONT_LABEL);
                Txt(v, vr - vw, ry + 1f, 0.235f, Fade(sel ? C_AMBER : C_MUTE, la), GTA.UI.Alignment.Left);
            }
            if (sel)
            {
                float ck = la * (0.5f + 0.5f * Pulse());
                Icon("arrow_l", vr - vw - 9f, ry + ROW_H * 0.5f, 10f, Fade(C_AMBER, ck));
                Icon("arrow_r", vr + 7f, ry + ROW_H * 0.5f, 10f, Fade(C_AMBER, ck));
            }
        }

        // one line on what the selected row does
        float hy = listTop + MENU_COUNT * ROW_H + 3f;
        if (hy + 18f <= listTop + extent + 1f)
            Txt(MenuHint(menuIndex), ix + 8f, hy, 0.20f, Fade(C_MUTE, la), GTA.UI.Alignment.Left);
    }

    // an on/off switch built from two squares
    void Switch(float x, float y, bool on, float a)
    {
        float w = 24f, h = 10f;
        Bar(x, y, w, h, Fade(on ? C_GREEN : C_DIMM, a * (on ? 0.5f : 0.35f)));
        float kx = on ? x + w - 9f : x + 1f;
        Bar(kx, y + 1f, 8f, h - 2f, Fade(on ? C_TEXT : C_MUTE, a));
    }

    // ---- the carry, top centre, while the camera is on the ball -----------------------------
    void DrawCarry(float k)
    {
        float wk = Ease(watchK) * k;
        if (wk <= 0.01f) return;
        float cx = CanvasW() * 0.5f;
        float y = 34f + (1f - wk) * 10f;

        string n = DistNum(liveDist);
        string u = DistUnit();
        float nw = TxtW(n, 0.78f, FONT_TITLE);
        float uw = TxtW(u, 0.30f, FONT_LABEL);
        float total = nw + 8f + uw;
        Txt(n, cx - total * 0.5f, y, 0.78f, Fade(C_TEXT, wk), GTA.UI.Alignment.Left, FONT_TITLE);
        Txt(u, cx - total * 0.5f + nw + 8f, y + 30f, 0.30f, Fade(C_MUTE, wk), GTA.UI.Alignment.Left);
        Tracked("CARRY", cx, y + 68f, 0.22f, Fade(C_MUTE, wk), FONT_LABEL, TITLE_TRACK * 2f, true);

        if (airControl)
        {
            float bw = 110f, bx = cx - bw * 0.5f, by = y + 92f;
            Icon("curve", bx - 12f, by + 2f, 14f, Fade(C_SKY, wk));
            Bar(bx, by, bw, 4f, Fade(C_TRACK, wk));
            float sf = steerFrac < 0f ? 0f : (steerFrac > 1f ? 1f : steerFrac);
            Bar(bx, by, bw * sf, 4f, Fade(C_SKY, wk));
            Tracked("AFTERTOUCH", cx, by + 8f, 0.18f, Fade(C_DIMM, wk), FONT_LABEL, TITLE_TRACK * 1.5f, true);
        }
    }

    // ---- the feed, right hand side ------------------------------------------------------
    void DrawFeed(float k)
    {
        int now = Game.GameTime;
        for (int i = feed.Count - 1; i >= 0; i--)
            if (now - feed[i].born > feed[i].life + FEED_OUT_MS) feed.RemoveAt(i);
        if (feed.Count == 0) return;

        float fw = 252f, fh = 44f;
        float fx = CanvasW() - 22f - fw;
        float fy = 236f;
        for (int i = 0; i < feed.Count; i++)
        {
            Feed f = feed[i];
            int age = now - f.born;
            float kin = Ease(age / (float)FEED_IN_MS);
            float kout = age > f.life ? 1f - Ease((age - f.life) / (float)FEED_OUT_MS) : 1f;
            float a = k * kin * kout;
            if (a <= 0.01f) continue;
            float x = fx + (1f - kin) * 28f;
            float y = fy + i * (fh + 6f);

            Bar(x, y, fw, fh, Fade(C_INK, a));
            Bar(x, y, 3f, fh, Fade(f.tint, a));
            Bar(x + 3f, y, fh, fh, Fade(f.tint, a * 0.16f));
            Icon(f.icon, x + 3f + fh * 0.5f, y + fh * 0.5f, 24f, Fade(f.tint, a));
            float tx = x + fh + 12f;
            if (f.sub.Length > 0)
            {
                Txt(f.title, tx, y + 3f, 0.30f, Fade(C_TEXT, a), GTA.UI.Alignment.Left);
                Txt(f.sub, tx, y + 23f, 0.21f, Fade(C_MUTE, a), GTA.UI.Alignment.Left);
            }
            else Txt(f.title, tx, y + 9f, 0.32f, Fade(C_TEXT, a), GTA.UI.Alignment.Left);
        }
    }

    // ---- button prompts --------------------------------------------------------
    //  Drawn by the game's own instructional_buttons scaleform, bottom right,
    //  the strip every menu in the game uses. It is the only thing that can
    //  turn a button into a picture: a ~INPUT_~ token in ordinary drawn text
    //  is swapped for the glyph's NAME, "b_2000" and the like, and that is
    //  what was printed along the bottom. The slots are only rebuilt when
    //  the state or the input device changes.
    GTA.Scaleform promptSf;
    string promptSig = "";

    void DrawPrompts(float k)
    {
        if (k <= 0.05f || mode == Mode.Off) return;
        try
        {
            if (promptSf == null)
            {
                promptSf = new GTA.Scaleform("instructional_buttons");
                promptSig = "";
            }
            if (!promptSf.IsLoaded) return;

            bool pad = UsingPad();
            string sig = mode.ToString() + (pad ? "p" : "k") + (airControl ? "a" : "-");
            if (sig != promptSig)
            {
                promptSig = sig;
                promptSf.CallFunction("CLEAR_ALL");
                promptSf.CallFunction("TOGGLE_MOUSE_BUTTONS", false);
                promptSf.CallFunction("CREATE_CONTAINER");
                int n = 0;
                Control swing = pad ? Control.Attack : Control.Jump;
                if (mode == Mode.Watch)
                {
                    Slot(ref n, Control.Jump, "NEXT BALL");
                    if (airControl) Slot(ref n, pad ? Control.MoveLeftRight : Control.MoveUpOnly, "STEER");
                }
                else if (mode == Mode.Backswing || mode == Mode.Swing)
                {
                    Slot(ref n, swing, "RELEASE TO HIT");
                }
                else
                {
                    Slot(ref n, swing, "SWING");
                    Slot(ref n, pad ? Control.FrontendRb : Control.Context, "CLUB");
                    Slot(ref n, Control.FrontendDown, "SETTINGS");
                    if (!pad) Slot(ref n, Control.Reload, "NEW BALL");
                    Slot(ref n, Control.FrontendCancel, "QUIT");
                }
                promptSf.CallFunction("DRAW_INSTRUCTIONAL_BUTTONS", -1);
                promptSf.CallFunction("SET_BACKGROUND_COLOUR", 0, 0, 0, 80);
            }
            Function.Call(Hash.DRAW_SCALEFORM_MOVIE_FULLSCREEN, promptSf.Handle, 255, 255, 255, (int)(255f * k), 0);
        }
        catch { }
    }

    // GET_CONTROL_INSTRUCTIONAL_BUTTONS_STRING, by hash because the wrapper
    // name has moved between SHVDN versions: the glyph for this control on
    // whatever the player is holding
    void Slot(ref int n, Control c, string label)
    {
        string glyph = Function.Call<string>((Hash)0x0499D7B09FC9B407UL, 2, (int)c, true);
        promptSf.CallFunction("SET_DATA_SLOT", n, glyph, label);
        n++;
    }

    void ReleasePrompts()
    {
        try { if (promptSf != null) promptSf.Dispose(); }
        catch { }
        promptSf = null;
        promptSig = "";
    }

    // =====================================================================
    //  the tee marker, under the golfer's feet
    // =====================================================================
    void DrawFootHud()
    {
        float k = Ease(footFade);
        if (k <= 0.02f) return;

        Ped ped = Game.Player.Character;
        if (ped == null || !ped.Exists()) return;

        float sx, sy;
        if (!WorldToCanvas(ped.Position + new Vector3(0f, 0f, -0.9f), out sx, out sy))
        {
            footHas = false;
            return;
        }
        if (!footHas) { footX = sx; footY = sy; footHas = true; }
        else
        {
            float c = 1f - (float)Math.Exp(-frameDt * 26f);
            footX += (sx - footX) * c;
            footY += (sy - footY) * c;
        }
        sx = (float)Math.Round(footX);
        sy = (float)Math.Round(footY);

        rectBudget = 200;
        iconBudget = 16;

        float pop = clubPop * 0.05f + strikePop * 0.04f;
        float w = 168f * (1f + pop);
        float h = 58f * (1f + pop);
        float left = sx - w * 0.5f;
        float top = sy + 14f + (1f - k) * 12f;     // rises up into place

        // Under his feet is very nearly the bottom of the screen at the
        // usual camera height, so the marker has a floor it cannot drop
        // through, clear of the button strip, and it never leaves the sides.
        float maxTop = CANVAS_H - 56f - h;
        if (top > maxTop) top = maxTop;
        float cw = CanvasW();
        if (left < 6f) left = 6f;
        if (left + w > cw - 6f) left = cw - 6f - w;

        Bar(left, top, w, h, Fade(C_INK, k * 0.92f));
        Bar(left, top, w, 2f, Fade(C_GREEN, k));
        // a bright line races out along the top edge when the club connects
        if (strikePop > 0f)
        {
            float sw = w * (1f - strikePop);
            Bar(left + (w - sw) * 0.5f, top, sw, 2f, Fade(C_TEXT, k * strikePop));
        }

        float flare = clubPop > strikePop ? clubPop : strikePop;
        Color ink = Blend(C_TEXT, C_AMBER, flare);

        float cx = left + w * 0.5f;
        Tracked(CLUB_NAMES[clubIndex], cx, top + 6f, 0.30f, Fade(ink, k), FONT_LABEL, TITLE_TRACK, true);
        Txt(CarryText(), cx, top + 25f, 0.23f, Fade(C_GREEN, k * 0.95f), GTA.UI.Alignment.Center, FONT_LABEL);

        // a special ball is worth a reminder in the corner, a plain one is not
        if (ballMode != BallMode.Normal)
            Icon(MODE_ICONS[(int)ballMode], left + w - 14f, top + 14f, 14f,
                Fade(MODE_TINT[(int)ballMode], k * (0.75f + 0.25f * Pulse())));

        Meter(left + 12f, top + h - 14f, w - 24f, 6f, k);
    }

    // sixteen cells: amber on the way up, green inside the sweet spot, red
    // once you have gone past it
    void Meter(float mx, float my, float mw, float mh, float k)
    {
        const int cells = 16;
        float gap = 2f;
        float cw = (mw - gap * (cells - 1)) / cells;
        bool swinging = (mode == Mode.Backswing || mode == Mode.Swing);
        float shown = powerShown < 0f ? 0f : (powerShown > 1f ? 1f : powerShown);
        bool sweet = shown >= sweetLo && shown <= sweetHi;

        for (int c = 0; c < cells; c++)
        {
            float f0 = c / (float)cells;
            float f1 = (c + 1) / (float)cells;
            bool zone = f1 > sweetLo && f0 < sweetHi;
            float cx = mx + c * (cw + gap);
            Color bg = zone ? Fade(C_AMBER, swinging ? 0.30f + 0.15f * Pulse() : 0.18f) : C_TRACK;
            Bar(cx, my, cw, mh, Fade(bg, k));
            if (shown > f0 + 0.001f)
            {
                float fill = shown >= f1 ? 1f : (shown - f0) / (f1 - f0);
                Color col = sweet ? C_GREEN : (f0 >= sweetHi ? C_RED : C_AMBER);
                Bar(cx, my, cw * fill, mh, Fade(col, k));
            }
        }
        if (shown > 0.002f) Bar(mx + mw * shown - 1f, my - 3f, 2f, mh + 6f, Fade(C_TEXT, k));
    }

    bool WorldToCanvas(Vector3 world, out float cx, out float cy)
    {
        cx = 0f; cy = 0f;
        try
        {
            OutputArgument ox = new OutputArgument();
            OutputArgument oy = new OutputArgument();
            bool ok = Function.Call<bool>(Hash.GET_SCREEN_COORD_FROM_WORLD_COORD, world.X, world.Y, world.Z, ox, oy);
            if (!ok) return false;
            float nx = ox.GetResult<float>();
            float ny = oy.GetResult<float>();
            if (nx < -0.2f || nx > 1.2f || ny < -0.2f || ny > 1.2f) return false;
            cx = nx * CanvasW();
            cy = ny * CANVAS_H;
            return true;
        }
        catch { return false; }
    }

    // Only ever a brief answer to a refused start, never a permanent fixture.
    void DrawOffHud()
    {
        if (!debugHud || Game.GameTime > offHintUntil) return;
        rectBudget = 40;
        Txt("Street Golf did not start: " + (blockReason.Length > 0 ? blockReason : "unknown"),
            CARD_X, 30f, 0.22f, C_DIMM, GTA.UI.Alignment.Left);
    }

    float ClubReach(int c)
    {
        float loft = clubLoft[c] * (float)(Math.PI / 180.0);
        float v = clubSpeed[c];
        if (ballMode == BallMode.Super) v *= SuperSpeedFactor();
        return (float)(v * v * Math.Sin(2.0 * loft) / 9.8);
    }

    // =====================================================================
    //  animation / audio / fx helpers
    // =====================================================================
    void PlayAnim(Ped ped, string dict, string clip, float blendIn, float blendOut, AnimationFlags flags)
    {
        Function.Call(Hash.TASK_PLAY_ANIM, ped.Handle, dict, clip, blendIn, blendOut, -1, (int)flags, 0f, false, false, false);
        lastAnimPush = Game.GameTime;
    }

    void StopGolfAnims(Ped ped)
    {
        if (ped == null || !ped.Exists()) return;
        for (int c = 0; c < 4; c++)
        {
            string a = BASE_ANIM[c];
            StopAnim(ped, DICT_AI, a + "_idle_a");
            StopAnim(ped, DICT_AI, c == BASE_PUTTER ? "putt_intro" : a + "_swing_intro");
            StopAnim(ped, DICT_AI, c == BASE_PUTTER ? "putt_action" : a + "_swing_action");
            StopAnim(ped, DICT_MP, c == BASE_PUTTER ? "putt_idle" : a + "_swing_idle");
        }
    }

    // Issued unconditionally. The playing check used to gate this, and an
    // advanced anim task does not always report through it, which is how the
    // golfer stayed locked in his stance after quitting.
    void StopAnim(Ped ped, string dict, string clip)
    {
        try { Function.Call(Hash.STOP_ANIM_TASK, ped.Handle, dict, clip, -8f); }
        catch { }
    }

    void PlaySoundOn(string name, Entity ent)
    {
        if (!golfSounds || ent == null) return;
        try
        {
            if (!ent.Exists()) return;
            Function.Call(Hash.PLAY_SOUND_FROM_ENTITY, -1, name, ent.Handle, 0, false, 0);
        }
        catch { }
    }

    void PlayFxAt(string name, Vector3 pos, float rotZ)
    {
        try
        {
            Function.Call(Hash.START_PARTICLE_FX_NON_LOOPED_AT_COORD, name, pos.X, pos.Y, pos.Z, 0f, 0f, rotZ, 1f, false, false, false);
        }
        catch { }
    }

    // =====================================================================
    //  surfaces
    // =====================================================================
    static bool IsSand(MaterialHash m)
    {
        switch (m)
        {
            case MaterialHash.SandCompact:
            case MaterialHash.SandLoose:
            case MaterialHash.SandWet:
            case MaterialHash.SandTrack:
            case MaterialHash.SandDryDeep:
            case MaterialHash.SandWetDeep:
                return true;
        }
        return false;
    }

    static bool IsSoftGround(MaterialHash m)
    {
        switch (m)
        {
            case MaterialHash.Grass:
            case MaterialHash.GrassLong:
            case MaterialHash.GrassShort:
            case MaterialHash.Soil:
            case MaterialHash.DirtTrack:
            case MaterialHash.Leaves:
            case MaterialHash.Bushes:
            case MaterialHash.Hay:
            case MaterialHash.MudHard:
            case MaterialHash.MudSoft:
            case MaterialHash.MudDeep:
            case MaterialHash.Woodchips:
            case MaterialHash.GravelSmall:
            case MaterialHash.GravelLarge:
            case MaterialHash.SnowCompact:
            case MaterialHash.SnowLoose:
                return true;
        }
        return IsSand(m);
    }

    bool BelowWater(Vector3 p)
    {
        try
        {
            OutputArgument oh = new OutputArgument();
            if (Function.Call<bool>(Hash.GET_WATER_HEIGHT, p.X, p.Y, p.Z, oh))
                return p.Z < oh.GetResult<float>() - 0.15f;
        }
        catch { }
        return false;
    }

    // =====================================================================
    //  misc helpers
    // =====================================================================
    static readonly Control[] BLOCKED = {
        Control.MoveLeftRight, Control.MoveUpDown, Control.Sprint, Control.Jump, Control.Enter,
        Control.Attack, Control.Attack2, Control.Aim, Control.Duck, Control.SelectWeapon,
        Control.Detonate, Control.Cover, Control.Reload, Control.Context, Control.ContextSecondary,
        Control.MeleeAttackLight, Control.MeleeAttackHeavy, Control.MeleeAttackAlternate,
        Control.VehicleExit, Control.Phone, Control.SpecialAbility, Control.CharacterWheel,
        Control.WeaponWheelUpDown, Control.WeaponWheelLeftRight, Control.WeaponWheelNext,
        Control.WeaponWheelPrev, Control.Pickup, Control.Talk
    };

    // Only the controls that would fight the stance are switched off, so the
    // camera keeps working - the camera is how you aim.
    void DisableControls()
    {
        for (int i = 0; i < BLOCKED.Length; i++)
            Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, (int)BLOCKED[i], true);
    }

    static Vector3 HeadingToDir(float headingDeg)
    {
        double r = headingDeg * Math.PI / 180.0;
        return new Vector3((float)-Math.Sin(r), (float)Math.Cos(r), 0f);
    }

    static float NormDeg(float a)
    {
        a = a % 360f;
        if (a < 0f) a += 360f;
        return a;
    }

    static float NormDeg180(float a)
    {
        a = NormDeg(a);
        if (a > 180f) a -= 360f;
        return a;
    }

    static float AngleDiff(float a, float b)
    {
        float d = NormDeg(a - b);
        if (d > 180f) d -= 360f;
        return d;
    }

    static bool TryGround(Vector3 p, out float z)
    {
        OutputArgument oz = new OutputArgument();
        bool ok = Function.Call<bool>(Hash.GET_GROUND_Z_FOR_3D_COORD, p.X, p.Y, p.Z, oz, false, false);
        z = ok ? oz.GetResult<float>() : 0f;
        return ok;
    }

    bool UseImperial()
    {
        if (unitMode == 1) return true;
        if (unitMode == 2) return false;
        try { return Game.MeasurementSystem == MeasurementSystem.Imperial; }
        catch { return true; }
    }

    string Dist(float metres)
    {
        return DistNum(metres) + " " + DistUnit().ToLowerInvariant();
    }

    string DistNum(float metres)
    {
        if (UseImperial()) return ((int)Math.Round(metres * 1.09361f)).ToString();
        return ((int)Math.Round(metres)).ToString();
    }

    string DistUnit()
    {
        return UseImperial() ? "YD" : "M";
    }

    void Notify(string s)
    {
        try { GTA.UI.Notification.Show(s, false); } catch { }
    }

    // ================================================================================
    //  THE SET'S ROW
    //
    //  Every mod in this set puts its name in the bottom corner for four seconds after
    //  load, stacked under one another with a single turning seal beside the column.
    //
    //  THE OTHERS SHARE A PAIR OF FILES. This one cannot: it is a source script, one .cs
    //  compiled by SHVDN at runtime, so there is nowhere to put UI\Splash.cs. What is
    //  below is the same behaviour written inline, and the only thing it does differently
    //  is not draw the seal -- an assembly compiled from source has no file on disk to
    //  look next to, so it can never find the art. That is precisely why the seal is
    //  claimed by the first mod that HAS art rather than the first to load: this one
    //  contributes its line and somebody else draws the mark.
    //
    //  The channel between the mods is the AppDomain. SHVDN loads every script into one
    //  and ticks them on a single thread, so a counter parked there is visible to all of
    //  them and safe to touch without locking. A reload throws it away, which resets the
    //  count, which is what should happen.
    // ================================================================================

    const int SPLASH_SHOW_MS = 4000;
    const int SPLASH_IN_MS = 420;
    const int SPLASH_OUT_MS = 800;
    const int SPLASH_WAIT_MS = 6000;

    const float SPLASH_RIGHT = 0.980f;
    const float SPLASH_BOTTOM = 0.930f;
    const float SPLASH_PITCH = 0.032f;
    const float SPLASH_MARK = 0.092f;
    const float SPLASH_TEXT = 0.30f;

    const string SPLASH_ROWKEY = "spitmux.splash.rows";

    int splashFrom;
    int splashSlot;
    bool splashDone;

    void SplashRow()
    {
        if (splashDone) return;

        if (splashFrom == 0)
        {
            if (Game.GameTime < SPLASH_WAIT_MS) return;

            try
            {
                Ped me = Game.Player.Character;
                if (me == null || !me.Exists()) return;
            }
            catch
            {
                splashDone = true;
                return;
            }

            splashFrom = Game.GameTime;
            splashSlot = SplashClaim();
        }

        int age = Game.GameTime - splashFrom;
        if (age >= SPLASH_SHOW_MS) { splashDone = true; return; }

        int a;
        if (age < SPLASH_IN_MS) a = (int)(255f * age / SPLASH_IN_MS);
        else if (SPLASH_SHOW_MS - age < SPLASH_OUT_MS) a = (int)(255f * (SPLASH_SHOW_MS - age) / SPLASH_OUT_MS);
        else a = 255;

        if (a <= 0) return;

        float y = SPLASH_BOTTOM - splashSlot * SPLASH_PITCH;

        // The name ends where the mark begins, whether or not anything is drawing one --
        // the column has to line up with the other mods' either way.
        float wide = SPLASH_MARK * 720f / System.Math.Max(1f, GTA.UI.Screen.ScaledWidth);
        float rightX = SPLASH_RIGHT - wide - 0.008f;

        try
        {
            GTA.UI.TextElement t = new GTA.UI.TextElement(
                "Street Golf  " + VERSION,
                new PointF(rightX * GTA.UI.Screen.ScaledWidth, y * 720f - 11f),
                SPLASH_TEXT,
                Color.FromArgb(a, 255, 148, 24),
                GTA.UI.Font.ChaletComprimeCologne,
                GTA.UI.Alignment.Right);

            t.Outline = true;
            t.ScaledDraw();
        }
        catch
        {
            // A greeting is not worth taking a frame down for.
        }
    }

    static int SplashClaim()
    {
        try
        {
            AppDomain domain = AppDomain.CurrentDomain;
            object taken = domain.GetData(SPLASH_ROWKEY);
            int row = taken is int ? (int)taken : 0;
            domain.SetData(SPLASH_ROWKEY, row + 1);
            return row;
        }
        catch
        {
            return 0;
        }
    }
}
