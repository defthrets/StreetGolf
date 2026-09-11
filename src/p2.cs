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
        b.Velocity = vel;
        Function.Call(Hash.SET_ENTITY_MAX_SPEED, b.Handle, ModeTopSpeed());
        b.SetNoCollision(ped, true);

        Shot s = new Shot();
        s.ball = b;
        s.origin = origin;
        s.born = Game.GameTime;
        s.prevVz = vel.Z;
        s.mode = ballMode;
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

    Vector3 ShapeLaunch(Vector3 vel, Vector3 dir, float speed)
    {
        if (ballMode == BallMode.Super) return vel * superMult;
        return vel;
    }

    float ModeTopSpeed()
    {
        // the ceiling has to come off entirely or the multiplier is thrown away
        if (ballMode == BallMode.Super) return 150f * superMult;
        return 150f;
    }

    // =====================================================================
    //  the settings list in the left panel
    // =====================================================================
    string MenuLabel(int i)
    {
        switch (i)
        {
            case 0: return "BALL";
            case 1: return "POLICE";
            case 2: return "BATONS";
            case 3: return "IMPACT";
            case 4: return "CAR DAMAGE";
            case 5: return "WALL MARKS";
            case 6: return "TRAIL";
            case 7: return "AIM LINE";
            case 8: return "AFTERTOUCH";
            case 9: return "UNITS";
        }
        return "";
    }

    static string OnOff(bool v) { return v ? "ON" : "OFF"; }

    string MenuIcon(int i)
    {
        switch (i)
        {
            case 0: return MODE_ICONS[(int)ballMode];
            case 1: return "badge";
            case 2: return "baton";
            case 3: return "impact";
            case 4: return "dent";
            case 5: return "crack";
            case 6: return "trail";
            case 7: return "aim";
            case 8: return "curve";
            case 9: return "ruler";
        }
        return "ball";
    }

    // one line under the list, for the row that is selected
    string MenuHint(int i)
    {
        switch (i)
        {
            case 0: return "plain, on fire, explosive, or just absurd";
            case 1: return "the master switch for police interest";
            case 2: return "low stars bring sticks and tasers";
            case 3: return "how hard the ball hits everything";
            case 4: return "dents, glass and tyres where it lands";
            case 5: return "chips and cracks in whatever it strikes";
            case 6: return "the ribbon the ball leaves behind";
            case 7: return "the arc and the ring where it lands";
            case 8: return "lean on the ball in flight with the stick";
            case 9: return "yards, metres, or the game's own setting";
        }
        return "";
    }

    // rows that are a switch are drawn as one, the rest show their value
    static bool MenuIsToggle(int i)
    {
        return i == 1 || i == 2 || i == 4 || i == 5 || i == 6 || i == 7 || i == 8;
    }

    bool MenuBool(int i)
    {
        switch (i)
        {
            case 1: return policeWanted;
            case 2: return lessLethalCops;
            case 4: return carDamage;
            case 5: return impactMarks;
            case 6: return trailEnabled;
            case 7: return aimLine;
            case 8: return airControl;
        }
        return false;
    }

    void PoliceToast()
    {
        if (policeWanted) Toast("badge", "POLICE ON", "they can take an interest again", C_AMBER, 2200);
        else Toast("badge", "POLICE OFF", "nobody is coming, swing away", C_SKY, 2200);
    }

    string MenuValue(int i)
    {
        switch (i)
        {
            case 0: return MODE_NAMES[(int)ballMode];
            case 1: return OnOff(policeWanted);
            case 2: return OnOff(lessLethalCops);
            case 3: return "x" + impactPower.ToString("0.00");
            case 4: return OnOff(carDamage);
            case 5: return OnOff(impactMarks);
            case 6: return OnOff(trailEnabled);
            case 7: return OnOff(aimLine);
            case 8: return OnOff(airControl);
            case 9: return unitMode == 1 ? "YARDS" : (unitMode == 2 ? "METRES" : "AUTO");
        }
        return "";
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
                policeWanted = !policeWanted;
                if (policeWanted) noticedNotified = false;
                else RestorePolice();
                break;
            case 2:
                lessLethalCops = !lessLethalCops;
                if (!lessLethalCops) ReleaseCops();
                break;
            case 3:
                impactPower += 0.25f * dir;
                if (impactPower < 0f) impactPower = 0f;
                if (impactPower > 3f) impactPower = 3f;
                break;
            case 4: carDamage = !carDamage; break;
            case 5: impactMarks = !impactMarks; break;
            case 6: trailEnabled = !trailEnabled; break;
            case 7: aimLine = !aimLine; break;
            case 8: airControl = !airControl; break;
            case 9: unitMode = (unitMode + dir + 3) % 3; break;
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
        if (SkipPressed() || CancelPressed() || gone)
        {
            FinishWatch();
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

