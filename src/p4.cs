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
    //  Built to match BareMinimum's inventory screen: its palette to the
    //  byte, its rounded panels and left accent stripe, its letter tracked
    //  titles in the label font, everything sized off screen HEIGHT, and its
    //  arrive / chase / pulse animation feel.
    //
    //  Two pieces. Session figures sit in a small panel top left. The golf
    //  itself - club, reach, power - rides in a chip under the golfer's feet,
    //  where you are already looking.
    // =====================================================================

    // BareMinimum.UI.Palette, verbatim
    static readonly Color P_TEXT = Color.FromArgb(245, 240, 240, 246);
    static readonly Color P_DIM = Color.FromArgb(200, 190, 190, 198);
    static readonly Color P_OFF = Color.FromArgb(160, 150, 150, 158);
    static readonly Color P_BRAND = Color.FromArgb(255, 240, 170, 56);
    static readonly Color P_DEEP = Color.FromArgb(255, 206, 96, 24);
    static readonly Color P_CASH = Color.FromArgb(255, 126, 190, 79);
    static readonly Color P_WARN = Color.FromArgb(255, 232, 177, 44);
    static readonly Color P_DANGER = Color.FromArgb(255, 214, 69, 58);
    static readonly Color P_COLD = Color.FromArgb(255, 120, 198, 226);
    static readonly Color P_BODY = Color.FromArgb(240, 10, 11, 14);
    static readonly Color P_TRACK = Color.FromArgb(225, 4, 5, 7);

    // BareMinimum.UI.Draw / Kit / Glide constants, converted from screen
    // height fractions into the 720 tall canvas the scaled draw calls use
    const float CANVAS_H = 720f;
    const float PANEL_ROUND = 0.013f * CANVAS_H;
    const float PANEL_STRIPE = 0.0028f * CANVAS_H;
    const float TITLE_TRACK = 0.0024f * CANVAS_H;
    const float GLIDE_CHASE = 0.26f;
    const int PULSE_MS = 1500;
    static readonly GTA.UI.Font FONT_LABEL = GTA.UI.Font.ChaletComprimeCologne;
    static readonly GTA.UI.Font FONT_BODY = GTA.UI.Font.ChaletLondon;

    int rectBudget;
    Dictionary<string, float> advCache = new Dictionary<string, float>();

    // animation state
    float hudArrive;        // 0 closed, 1 fully open
    float powerShown;       // chases the real power, so the bar never snaps
    float clubPop;          // brief swell when the club changes
    float strikePop;        // brief flare on contact
    float footFade;         // the chip under the golfer
    float footX, footY;
    bool footHas;

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

    static Color Fade(Color c, float k)
    {
        if (k <= 0f) return Color.FromArgb(0, c.R, c.G, c.B);
        if (k >= 1f) return c;
        return Color.FromArgb((int)(c.A * k), c.R, c.G, c.B);
    }

    void Bar(float left, float top, float w, float h, Color c)
    {
        if (rectBudget <= 0 || w <= 0.01f || h <= 0.01f || c.A <= 1) return;
        rectBudget--;
        try { new GTA.UI.ContainerElement(new PointF(left, top), new SizeF(w, h), c).ScaledDraw(); }
        catch { }
    }

    // Square. This used to build rounded corners out of a stack of sub pixel
    // bands; GTA snapped each band to a different pixel from one frame to the
    // next and every edge crawled. The radius and step arguments are kept so
    // the call sites read the same, and ignored.
    void RoundRect(float left, float top, float w, float h, float r, Color c, int steps)
    {
        Bar(left, top, w, h, c);
    }

    void Panel(float left, float top, float w, float h, Color body, Color accent, float k)
    {
        RoundRect(left, top, w, h, PANEL_ROUND, Fade(body, k), 16);
        Bar(left, top + PANEL_ROUND * 0.6f, PANEL_STRIPE, h - PANEL_ROUND * 1.2f, Fade(accent, k));
    }

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

    // Letter spaced titles, the way their headers are set
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

    void UpdateHudAnim(float dt)
    {
        bool open = (mode != Mode.Off);
        float want = open ? 1f : 0f;
        hudArrive += (want - hudArrive) * (1f - (float)Math.Exp(-dt * 9f));
        if (hudArrive < 0.002f) hudArrive = 0f;
        if (hudArrive > 0.998f) hudArrive = 1f;

        bool wantFoot = (mode == Mode.Ready || mode == Mode.Backswing || mode == Mode.Swing);
        footFade += ((wantFoot ? 1f : 0f) - footFade) * (1f - (float)Math.Exp(-dt * 10f));

        float pTarget = (mode == Mode.Backswing || mode == Mode.Swing) ? power : 0f;
        powerShown += (pTarget - powerShown) * GLIDE_CHASE;

        if (clubPop > 0f) clubPop -= dt * 3.2f;
        if (clubPop < 0f) clubPop = 0f;
        if (strikePop > 0f) strikePop -= dt * 2.4f;
        if (strikePop < 0f) strikePop = 0f;
    }

    // =====================================================================
    //  session panel, top left: the figures, then the settings list
    // =====================================================================
    void DrawHud()
    {
        float k = Ease(hudArrive);
        if (k <= 0.01f) return;
        rectBudget = 320;

        const float rowH = 19f;
        const float listTop = 84f;      // from the top of the panel
        float left = 22f;
        float w = 210f;
        float top = 66f - (1f - k) * 14f;
        float pad = 13f;
        float vx = left + w - 26f;      // value column, clear of the panel edge
        float h = listTop + MENU_COUNT * rowH + 9f;

        Panel(left, top, w, h, P_BODY, P_BRAND, k);
        Tracked("STREET GOLF", left + pad, top + 9f, 0.34f, Fade(P_BRAND, k), FONT_LABEL, TITLE_TRACK, false);
        Bar(left + pad, top + 31f, w - pad * 2f, 1f, Fade(P_OFF, k * 0.4f));

        Txt(shots + " BALLS    " + sessionCars + " CARS    " + sessionPeds + " PEDS",
            left + pad, top + 36f, 0.225f, Fade(P_DIM, k), GTA.UI.Alignment.Left);
        Txt("BEST " + Dist(bestShotDist), left + pad, top + 55f, 0.22f, Fade(P_CASH, k), GTA.UI.Alignment.Left);
        DrawHeatTag(vx + 14f, top + 55f, k);
        Bar(left + pad, top + 78f, w - pad * 2f, 1f, Fade(P_OFF, k * 0.4f));

        // selection rim, gliding onto the row it is on
        float selY = top + listTop + menuIndex * rowH;
        if (menuRimY <= 0.1f) menuRimY = selY;
        menuRimY += (selY - menuRimY) * GLIDE_CHASE;
        if (Math.Abs(selY - menuRimY) < 0.4f) menuRimY = selY;   // land, do not hover
        RoundRect(left + 5f, menuRimY - 2f, w - 10f, rowH,
            4f, Fade(P_BRAND, k * (0.11f + 0.07f * Pulse())), 3);
        Bar(left + 5f, menuRimY - 2f, 2f, rowH, Fade(P_BRAND, k * (0.55f + 0.45f * Pulse())));

        for (int i = 0; i < MENU_COUNT; i++)
        {
            float ry = top + listTop + i * rowH;
            bool sel = (i == menuIndex);
            Txt(MenuLabel(i), left + pad, ry + 1f, 0.235f,
                Fade(sel ? P_TEXT : P_DIM, k), GTA.UI.Alignment.Left);

            string v = MenuValue(i);
            float vw = TxtWidth(v, 0.245f);
            Txt(v, vx - vw, ry, 0.245f, Fade(sel ? P_BRAND : P_DIM, k), GTA.UI.Alignment.Left);

            if (sel)
            {
                float chev = 0.55f + 0.45f * Pulse();
                Txt("<", vx - vw - 11f, ry, 0.245f, Fade(P_BRAND, k * chev), GTA.UI.Alignment.Left);
                Txt(">", vx + 3f, ry, 0.245f, Fade(P_BRAND, k * chev), GTA.UI.Alignment.Left);
            }
        }

        bool watching = (mode == Mode.Watch) || (liveDist > 1f && NewestShot() != null);
        if (watching)
        {
            float cx = CanvasW() * 0.5f;
            Tracked(Dist(liveDist), cx, 46f, 0.72f, Fade(P_BRAND, k), FONT_LABEL, TITLE_TRACK * 1.6f, true);
            Tracked("CARRY", cx, 92f, 0.24f, Fade(P_DIM, k), FONT_LABEL, TITLE_TRACK * 2f, true);
        }

        DrawPrompts(k);

        if (Game.GameTime < flashUntil && flashText.Length > 0)
        {
            float fk = k * (0.7f + 0.3f * Pulse());
            Txt(flashText, CanvasW() * 0.5f, 552f, 0.38f, Fade(P_TEXT, fk),
                GTA.UI.Alignment.Center, FONT_LABEL, true);
        }

        if (debugHud)
            Txt("probes " + dbgProbes + "  hits " + dbgHits + "  impacts " + dbgImpacts +
                "  last " + dbgLast + "  RT " + TriggerValue().ToString("0.00"),
                22f, 30f, 0.22f, Fade(P_OFF, k), GTA.UI.Alignment.Left);
    }

    float TxtWidth(string t, float scale)
    {
        try { return GTA.UI.TextElement.GetScaledStringWidth(t, FONT_LABEL, scale); }
        catch { return t.Length * 6f; }
    }

    void DrawHeatTag(float rightX, float y, float k)
    {
        string t;
        Color c;
        if (!policeWanted) { t = "NO COPS"; c = P_COLD; }
        else if (policeGrace > 0f && graceLeft > 0f)
        {
            t = Clock(graceLeft);
            c = graceLeft < 30f ? Fade(P_WARN, 0.6f + 0.4f * Pulse()) : P_COLD;
        }
        else if (unseenActive)
        {
            t = recentPedHits + "/" + heatAfterPeds;
            c = recentPedHits >= heatAfterPeds - 1 ? P_WARN : P_COLD;
        }
        else if (lessLethalOn) { t = "BATONS"; c = Fade(P_WARN, 0.6f + 0.4f * Pulse()); }
        else { t = "HEAT"; c = Fade(P_DANGER, 0.55f + 0.45f * Pulse()); }

        Txt(t, rightX - TxtWidth(t, 0.22f), y, 0.22f, Fade(c, k), GTA.UI.Alignment.Left);
    }

    void DrawPrompts(float k)
    {
        bool pad = UsingPad();
        string hint;
        if (mode == Mode.Watch)
            hint = airControl
                ? (pad ? "L STICK   steer      A   next ball" : "W A S D   steer      SPACE   next ball")
                : (pad ? "A   next ball" : "SPACE   next ball");
        else if (mode == Mode.Backswing) hint = pad ? "release RT to hit" : "release SPACE to hit";
        else if (reloadTimer > 0f) hint = "teeing up";
        else hint = pad
            ? "RT  swing     LB RB  club     D-PAD  settings     B  quit"
            : "SPACE  swing     Q E  club     ARROWS  settings     BACKSPACE  quit";
        float cx2 = CanvasW() * 0.5f;
        float hw = TrackedWidth(hint, 0.25f, FONT_LABEL, TITLE_TRACK);
        RoundRect(cx2 - hw * 0.5f - 12f, 676f, hw + 24f, 22f, 8f, Fade(P_BODY, k * 0.85f), 4);
        Tracked(hint, cx2, 680f, 0.25f, Fade(P_TEXT, k * 0.92f), FONT_LABEL, TITLE_TRACK, true);
    }

    // =====================================================================
    //  the golf chip, under the golfer's feet
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
            footX += (sx - footX) * 0.35f;
            footY += (sy - footY) * 0.35f;
        }
        sx = (float)Math.Round(footX);
        sy = (float)Math.Round(footY);

        rectBudget = 200;

        float pop = clubPop * 0.06f + strikePop * 0.05f;
        float w = 150f * (1f + pop);
        float h = 54f * (1f + pop);
        float left = sx - w * 0.5f;
        float top = sy + 16f + (1f - k) * 12f;     // rises up into place

        Panel(left, top, w, h, P_BODY, P_BRAND, k * 0.94f);

        // club, letter spaced, with a warm flare just after a change or a strike
        Color clubInk = P_TEXT;
        float flare = clubPop > 0f ? clubPop : strikePop;
        if (flare > 0f) clubInk = Blend(P_TEXT, P_BRAND, flare);
        Tracked(CLUB_NAMES[clubIndex], left + w * 0.5f, top + 6f, 0.30f,
            Fade(clubInk, k), FONT_LABEL, TITLE_TRACK, true);

        string reach = hasPrediction ? Dist(predictedDist) : Dist(ClubReach(clubIndex));
        Tracked(reach, left + w * 0.5f, top + 26f, 0.22f, Fade(P_BRAND, k * 0.9f), FONT_LABEL, TITLE_TRACK, true);

        // tags above the chip: the ball mode, and whether the law is switched off
        float tagY = top - 17f;
        if (ballMode != BallMode.Normal)
        {
            float mk = k * (0.72f + 0.28f * Pulse());
            string mt = ballMode == BallMode.Super
                ? MODE_NAMES[(int)ballMode] + "  x" + ((int)superMult)
                : MODE_NAMES[(int)ballMode];
            Tracked(mt, left + w * 0.5f, tagY, 0.24f,
                Fade(P_WARN, mk), FONT_LABEL, TITLE_TRACK * 1.4f, true);
            tagY -= 15f;
        }
        if (!policeWanted)
            Tracked("NO COPS", left + w * 0.5f, tagY, 0.21f,
                Fade(P_COLD, k * 0.85f), FONT_LABEL, TITLE_TRACK * 1.4f, true);

        // meter: power while swinging, remaining after-touch while watching
        float mx = left + 12f, mw = w - 24f, my = top + h - 13f, mh = 5f;
        RoundRect(mx, my, mw, mh, mh * 0.5f, Fade(P_TRACK, k), 3);

        float sweetX = mx + mw * sweetLo;
        float sweetW = mw * (sweetHi - sweetLo);
        bool swinging = (mode == Mode.Backswing || mode == Mode.Swing);
        Bar(sweetX, my - 1.5f, sweetW, mh + 3f,
            Fade(P_WARN, k * (swinging ? 0.30f + 0.30f * Pulse() : 0.12f)));

        float shown = powerShown < 0f ? 0f : (powerShown > 1f ? 1f : powerShown);
        if (shown > 0.002f)
        {
            bool sweet = shown >= sweetLo && shown <= sweetHi;
            Color fill = sweet ? P_CASH : Blend(P_BRAND, P_DEEP, shown);
            RoundRect(mx, my, mw * shown, mh, mh * 0.5f, Fade(fill, k), 3);
            Bar(mx + mw * shown - 1f, my - 3f, 2f, mh + 6f, Fade(P_TEXT, k));
        }
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
            22f, 30f, 0.22f, P_OFF, GTA.UI.Alignment.Left);
    }

    float ClubReach(int c)
    {
        float loft = clubLoft[c] * (float)(Math.PI / 180.0);
        float v = clubSpeed[c];
        if (ballMode == BallMode.Super) v *= superMult;
        return (float)(v * v * Math.Sin(2.0 * loft) / 9.8);
    }

    void Flash(string s, int ms)
    {
        flashText = s;
        flashUntil = Game.GameTime + ms;
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
        if (UseImperial()) return ((int)Math.Round(metres * 1.09361f)).ToString() + " yd";
        return ((int)Math.Round(metres)).ToString() + " m";
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
