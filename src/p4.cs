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
    const float CARD_W = 238f;
    const float CARD_PAD = 12f;
    const float ROW_H = 20f;
    const float H_HEAD = 40f;
    const float H_CLUB = 94f;
    const float H_BALL = 44f;
    const float H_STATS = 72f;
    const float H_COPS = 28f;
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
        float h = H_HEAD + H_CLUB + H_BALL + H_STATS + H_COPS + H_DRAWER + listH * dk + 4f;

        Bar(x, y, w, h, Fade(C_INK, a));
        Bar(x, y + 2f, w, H_HEAD - 2f, Fade(C_INK2, a));
        Bar(x, y, w, 2f, Fade(C_GREEN, a));

        // header
        Icon("ball", ix + 11f, y + 21f, 22f, Fade(C_TEXT, a));
        Txt("STREET GOLF", ix + 30f, y + 5f, 0.36f, Fade(C_TEXT, a), GTA.UI.Alignment.Left, FONT_TITLE);
        string ver = "v" + VERSION;
        Txt(ver, ix + iw - TxtW(ver, 0.18f, FONT_LABEL), y + 14f, 0.18f, Fade(C_DIMM, a), GTA.UI.Alignment.Left);

        float cy = y + H_HEAD;
        Rule(x, cy, w, a);
        cy = ClubBlock(ix, cy, iw, a);
        Rule(x, cy, w, a);
        cy = BallBlock(ix, cy, iw, a);
        Rule(x, cy, w, a);
        cy = StatsBlock(ix, cy, iw, a);
        Rule(x, cy, w, a);
        cy = CopsBlock(ix, cy, iw, a);
        Rule(x, cy, w, a);
        DrawerBlock(x, ix, cy, w, iw, a, dk);
    }

    float ClubBlock(float ix, float y, float iw, float a)
    {
        float ty = y + 10f;
        float tile = 46f;
        float flare = clubPop > strikePop ? clubPop : strikePop;
        Color ink = Blend(C_TEXT, C_AMBER, flare);

        Bar(ix, ty, tile, tile, Fade(C_INK2, a));
        Icon(CLUB_ICONS[Base()], ix + tile * 0.5f, ty + tile * 0.5f, 34f * (1f + clubPop * 0.15f), Fade(ink, a));

        float tx = ix + tile + 10f;
        Txt(CLUB_NAMES[clubIndex], tx, ty - 5f, 0.40f, Fade(ink, a), GTA.UI.Alignment.Left);
        Txt(SET_TAGS[Set()], tx, ty + 21f, 0.20f, Fade(C_MUTE, a), GTA.UI.Alignment.Left);
        float lw = TxtW("CARRY", 0.20f, FONT_LABEL);
        Txt("CARRY", tx, ty + 35f, 0.20f, Fade(C_MUTE, a), GTA.UI.Alignment.Left);
        Txt(CarryText(), tx + lw + 6f, ty + 33f, 0.25f, Fade(C_GREEN, a), GTA.UI.Alignment.Left);

        // the rail: the four clubs, and which of the three sets is in hand
        float ry = ty + tile + 10f;
        for (int b = 0; b < 4; b++)
        {
            float sx = ix + 4f + b * 26f + 9f;
            bool on = (b == Base());
            Icon(CLUB_ICONS[b], sx, ry + 7f, 15f, Fade(on ? C_TEXT : C_DIMM, a));
            for (int s = 0; s < 3; s++)
            {
                bool lit = on && s == Set();
                Bar(sx - 6f + s * 5f, ry + 18f, 3f, 3f, Fade(lit ? C_AMBER : C_DIMM, a * (lit ? 1f : 0.55f)));
            }
        }
        StatePill(ix + iw, ry + 2f, a);
        return y + H_CLUB;
    }

    // what the golfer is doing right now, in the corner of the club block
    void StatePill(float rightX, float y, float a)
    {
        string t;
        Color c;
        bool live = false;
        switch (mode)
        {
            case Mode.Watch: t = "WATCHING"; c = C_SKY; live = true; break;
            case Mode.Backswing: t = "BACKSWING"; c = C_AMBER; break;
            case Mode.Swing: t = "SWING"; c = C_AMBER; break;
            case Mode.Ready:
                if (reloadTimer > 0f) { t = "TEEING UP"; c = C_MUTE; }
                else { t = "READY"; c = C_GREEN; live = true; }
                break;
            default: t = "OFF"; c = C_DIMM; break;
        }
        float tw = TxtW(t, 0.20f, FONT_LABEL);
        float pw = tw + 24f, ph = 16f;
        float px = rightX - pw;
        Bar(px, y, pw, ph, Fade(c, a * 0.14f));
        float dot = live ? 0.55f + 0.45f * Pulse() : 1f;
        Bar(px + 7f, y + 6f, 4f, 4f, Fade(c, a * dot));
        Txt(t, px + 16f, y - 1f, 0.20f, Fade(c, a), GTA.UI.Alignment.Left);
    }

    float BallBlock(float ix, float y, float iw, float a)
    {
        float ty = y + 8f;
        Color tint = MODE_TINT[(int)ballMode];
        Bar(ix, ty, 28f, 28f, Fade(tint, a * 0.16f));
        float glow = ballMode == BallMode.Normal ? 1f : 0.8f + 0.2f * Pulse();
        Icon(MODE_ICONS[(int)ballMode], ix + 14f, ty + 14f, 20f, Fade(tint, a * glow));

        float tx = ix + 38f;
        float lw = TxtW("BALL", 0.20f, FONT_LABEL);
        Txt("BALL", tx, ty - 1f, 0.20f, Fade(C_MUTE, a), GTA.UI.Alignment.Left);
        Txt(ModeTitle(), tx + lw + 7f, ty - 3f, 0.27f, Fade(tint, a), GTA.UI.Alignment.Left);
        Txt(ModeShort(), tx, ty + 14f, 0.20f, Fade(C_MUTE, a), GTA.UI.Alignment.Left);
        return y + H_BALL;
    }

    float StatsBlock(float ix, float y, float iw, float a)
    {
        float ty = y + 8f;
        float gap = 6f;
        float tw = (iw - gap * 2f) / 3f;
        StatTile(ix, ty, tw, "ball", shots.ToString(), "BALLS", a);
        StatTile(ix + tw + gap, ty, tw, "car", sessionCars.ToString(), "CARS", a);
        StatTile(ix + (tw + gap) * 2f, ty, tw, "ped", sessionPeds.ToString(), "PEDS", a);

        float ry = ty + 46f;
        Icon("trophy", ix + 8f, ry + 8f, 15f, Fade(C_AMBER, a));
        float lw = TxtW("BEST", 0.20f, FONT_LABEL);
        Txt("BEST", ix + 20f, ry, 0.20f, Fade(C_MUTE, a), GTA.UI.Alignment.Left);
        Txt(Dist(bestShotDist), ix + 20f + lw + 6f, ry - 2f, 0.25f, Fade(C_GREEN, a), GTA.UI.Alignment.Left);

        string last = Dist(lastShotDist);
        float vw = TxtW(last, 0.25f, FONT_LABEL);
        float l2 = TxtW("LAST", 0.20f, FONT_LABEL);
        Txt(last, ix + iw - vw, ry - 2f, 0.25f, Fade(C_TEXT, a), GTA.UI.Alignment.Left);
        Txt("LAST", ix + iw - vw - 6f - l2, ry, 0.20f, Fade(C_MUTE, a), GTA.UI.Alignment.Left);
        return y + H_STATS;
    }

    void StatTile(float x, float y, float w, string icon, string n, string label, float a)
    {
        Bar(x, y, w, 40f, Fade(C_INK2, a));
        Icon(icon, x + 12f, y + 20f, 15f, Fade(C_MUTE, a));
        Txt(n, x + 24f, y + 1f, 0.33f, Fade(C_TEXT, a), GTA.UI.Alignment.Left);
        Txt(label, x + 24f, y + 24f, 0.17f, Fade(C_DIMM, a), GTA.UI.Alignment.Left);
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

    float CopsBlock(float ix, float y, float iw, float a)
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
        float w = 176f * (1f + pop);
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
        Icon(CLUB_ICONS[Base()], left + 18f, top + 20f, 22f * (1f + clubPop * 0.2f), Fade(ink, k));

        float cx = left + w * 0.5f + 9f;
        Tracked(CLUB_NAMES[clubIndex], cx, top + 6f, 0.30f, Fade(ink, k), FONT_LABEL, TITLE_TRACK, true);
        Txt(CarryText(), cx, top + 25f, 0.23f, Fade(C_GREEN, k * 0.95f), GTA.UI.Alignment.Center, FONT_LABEL);

        if (ballMode != BallMode.Normal)
            Icon(MODE_ICONS[(int)ballMode], left + w - 15f, top + 15f, 14f,
                Fade(MODE_TINT[(int)ballMode], k * (0.75f + 0.25f * Pulse())));

        if (reloadTimer > 0f && mode == Mode.Ready)
        {
            Icon("tee", left + w * 0.5f - 34f, top - 9f, 12f, Fade(C_MUTE, k));
            Tracked("TEEING UP", left + w * 0.5f + 6f, top - 17f, 0.20f, Fade(C_MUTE, k), FONT_LABEL, TITLE_TRACK, true);
        }

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
