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
            ProcessImpact(s, pos, vel, speed, now, me);

            // A mode can destroy the ball outright at the moment of contact -
            // Boom does exactly that - so nothing below may touch it again
            // without checking that there is still a ball there.
            if (s.ball == null || !s.ball.Exists())
            {
                s.prevPos = pos;
                return false;
            }

            if (!s.splashed && (s.ball.IsInWater || BelowWater(pos)))
            {
                s.splashed = true;
                PlaySoundOn("GOLF_BALL_IN_WATER_MASTER", s.ball);
                PlayFxAt("scr_golf_landing_water", pos, 0f);
                FinishShot(s);
            }

            if (s.ball == null || !s.ball.Exists())
            {
                s.prevPos = pos;
                return false;
            }

            float hag = 0f;
            try { hag = s.ball.HeightAboveGround; }
            catch { }
            if (speed < 0.2f && hag < 0.6f) s.rest += dt; else s.rest = 0f;
            if (s.rest > 0.7f || s.age > life || pos.Z < -80f) FinishShot(s);
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
            int handle = Function.Call<int>(Hash.START_EXPENSIVE_SYNCHRONOUS_SHAPE_TEST_LOS_PROBE,
                a.X, a.Y, a.Z, b.X, b.Y, b.Z, (int)IntersectFlags.Everything, ignoreH, 7);
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

    // Second and third opinions, so a hit is never silently dropped: bodies
    // standing exactly where the ball is, and the physics collision record.
    // Backstop for the rare contact the swept probe misses: someone or
    // something standing exactly where the ball is.
    //
    // This used to consult HasCollided as well, which was a mistake. That
    // flag stays true once an entity has touched anything at all, so from
    // the moment a ball brushed the tee it reported a fresh collision on
    // every later frame. That is why a Boom ball detonated in mid air the
    // instant it armed. Contact now comes only from the probe and from
    // these two proximity checks.
    void FallbackImpact(Shot s, Vector3 cur, Vector3 vel, float speed, Vector3 velN, int now, Ped me)
    {
        if (speed < minImpactSpeed) return;
        if ((shotTickCounter % 2) != 0) return;
        Vector3 back = Vector3.Zero - velN;

        try
        {
            Ped[] near = World.GetNearbyPeds(cur, 1.2f);
            if (near != null)
            {
                for (int i = 0; i < near.Length; i++)
                {
                    Ped pd = near[i];
                    if (pd == null || !pd.Exists()) continue;
                    if (me != null && pd.Handle == me.Handle) continue;
                    Apply(s, pd, cur, back, default(MaterialHash), vel, speed, now, me);
                    return;
                }
            }
        }
        catch { }

        try
        {
            Vehicle[] vs = World.GetNearbyVehicles(cur, 2.2f);
            if (vs != null)
            {
                float best = 2.2f * 2.2f;
                Vehicle hit = null;
                for (int i = 0; i < vs.Length; i++)
                {
                    if (vs[i] == null || !vs[i].Exists()) continue;
                    float dd = vs[i].Position.DistanceToSquared(cur);
                    if (dd < best) { best = dd; hit = vs[i]; }
                }
                if (hit != null) Apply(s, hit, cur, back, default(MaterialHash), vel, speed, now, me);
            }
        }
        catch { }
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
                if (s.spent) break;
                // An ordinary golf ball until it has genuinely got away from
                // the tee. Without this it detonated on the very first contact
                // the probe found, which is the ground under the golfer's own
                // feet, a fraction of a second after the club met the ball.
                if (s.age < 0.25f || s.origin.DistanceTo(p) < 6f) break;
                s.spent = true;
                try { World.AddExplosion(p, ExplosionType.Grenade, 4.0f, 1.5f, me, true, false); }
                catch { }
                Jolt(p, 1.1f);
                Rumble(340, 250);
                Toast("bomb", "BOOM", "", C_RED, 1600);
                try { if (s.ball != null && s.ball.Exists()) s.ball.Delete(); }
                catch { }
                s.ball = null;
                FinishShot(s);
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

    // Picks the pane the ball actually went through from where it landed in the
    // car body space, so a shot through the driver door does not blow out the
    // rear screen. Glass goes whenever the ball struck glass, and only needs a
    // hard hit when it struck bodywork near the windows.
    void SmashGlass(Vehicle v, Vector3 loc, MaterialHash mat, float pw)
    {
        bool hitGlass = IsGlass(mat);
        if (!hitGlass && (pw < 0.5f || loc.Z < 0.2f)) return;

        int win;
        if (loc.Y > 1.15f) win = 6;                       // windscreen
        else if (loc.Y < -1.15f) win = 7;                 // rear screen
        else if (loc.Y >= 0f) win = loc.X > 0f ? 1 : 0;   // front side
        else win = loc.X > 0f ? 3 : 2;                    // rear side
        try { Function.Call(Hash.SMASH_VEHICLE_WINDOW, v.Handle, win); }
        catch { }
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

        try
        {
            if (carDamage && heavy)
            {
                v.IsInvincible = false;
                Function.Call(Hash.SET_ENTITY_CAN_BE_DAMAGED, v.Handle, true);
                Function.Call(Hash.SET_VEHICLE_CAN_BE_VISIBLY_DAMAGED, v.Handle, true);

                // Three passes at the contact point. The last argument decides
                // whether the deformation is focused on the model, and which
                // value actually works varies by build - the wrong one silently
                // does nothing at all - so both are sent, then a wider, softer
                // pass so the metal around the crater pulls in with it.
                float dmgBoost = hitBoost > 3f ? 3f : hitBoost;
                float dmg = (900f + 3200f * pw) * dmgBoost;
                float tight = 0.30f + 0.35f * pw;
                Function.Call(Hash.SET_VEHICLE_DAMAGE, v.Handle, loc.X, loc.Y, loc.Z, dmg, tight, true);
                Function.Call(Hash.SET_VEHICLE_DAMAGE, v.Handle, loc.X, loc.Y, loc.Z, dmg, tight, false);
                Function.Call(Hash.SET_VEHICLE_DAMAGE, v.Handle, loc.X, loc.Y, loc.Z, dmg * 0.5f, 1.0f + 1.1f * pw, false);

                float bh = Function.Call<float>(Hash.GET_VEHICLE_BODY_HEALTH, v.Handle);
                float nh = bh - (60f + 220f * pw) * hitBoost;
                if (nh < 60f) nh = 60f;
                Function.Call(Hash.SET_VEHICLE_BODY_HEALTH, v.Handle, nh);

                // a super shot counts as a full strike whatever the meter said
                float pwx = pw * hitBoost;
                if (pwx > 1.4f) pwx = 1.4f;
                pw = pwx;

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

                SmashGlass(v, loc, mat, pw);
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
        Toast("car", pw > 0.7f ? "SMASH!" : "DINGER!", nm, C_AMBER, 2000);
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
            Function.Call(Hash.RENDER_SCRIPT_CAMS, true, true, 350, true, false);
        }
        catch { cam = null; }
    }

    void TrackCam(Vector3 ballPos, Vector3 vel, float dt)
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
            Function.Call(Hash.RENDER_SCRIPT_CAMS, false, true, 350, true, false);
            if (cam != null && cam.Exists()) cam.Delete();
        }
        catch { }
        cam = null;
    }

