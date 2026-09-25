# Street Golf 1.1.2

A driving range anywhere in Los Santos. You stand where you are and hit ball
after ball at the traffic. No hole, no course, no walking after the ball.

Built on GTA V's own golf minigame, so it looks and sounds like the real thing:
the actual swing animations, the four real club props, `prop_golf_ball` flown by
the game's physics, and the game's golf sound set and particles.

---

## Install

Unpack the zip and merge its `scripts` folder into your GTA V `scripts` folder.
You end up with:

```
scripts/StreetGolf.cs
scripts/StreetGolf.ini
scripts/StreetGolf/icons/*.png
```

ScriptHookVDotNet compiles the `.cs` when the game starts. Press `Insert` in
game to reload scripts without restarting. The icons folder holds the HUD's
pictures; without it the HUD still works, it just has words where the pictures
would be.

**Requirements**

- [Script Hook V](http://www.dev-c.com/gtav/scripthookv/)
- ScriptHookVDotNet v3 — tested against **3.6**, the **3.7 nightlies** and
  **3.9 Enhanced**

If you have both GTA V Legacy and Enhanced installed, they are separate folders
with separate `scripts` directories. Install into the one you actually launch.

---

## Controls

| Action | Controller | Keyboard / mouse |
| --- | --- | --- |
| Toggle Street Golf | hold `LT` + `A` | `F3` |
| Aim | right stick | mouse |
| Fine aim | left stick left/right | `A` / `D` (`Shift` = finer) |
| Change club | `LB` / `RB` | `Q` / `E`, mouse wheel |
| Swing | hold `RT`, release to hit | hold `Space` or `LMB`, release |
| Stop watching, next ball | `A` | `Space` |
| Steer the ball in flight | left stick | `W` `A` `S` `D` |
| Settings drawer | `D-pad` | arrow keys |
| Ball mode | — | `M` |
| Police on / off | — | `K` |
| Fresh ball | — | `N` or `R` |
| Quit | `B` | `Backspace` |

You aim by looking. Point the camera at whatever you want to hit and swing. The
button strip bottom right is the game's own, and shows the buttons for whichever
device you touched last.

---

## The HUD

**The card**, top left, is the range card for the session, one line each:

- the club in hand, which set it is from, and how far it carries
- the ball: normal, fireball, boom, or super shot and its multiplier
- the police: off, the grace clock, whether anyone can see you, how many people
  you have dropped, or your stars
- the session: balls, cars, pedestrians, and your best drive ever
- a **settings drawer** that slides open when you touch the `D-pad` or the
  arrow keys and closes again a few seconds after you stop. Up and down pick a
  row, left and right change it. Switches are drawn as switches; a line under
  the list says what the selected row does. `MenuAutoHide` in the ini sets how
  long it stays open, and `0` keeps it open all the time.

**The tee marker** sits under the golfer's feet: the club, the carry, and the
power meter, nothing else, right where you are looking when you swing. The meter runs amber
up to the sweet spot, green inside it and red past it, and the marker flares
when the club connects.

**The feed**, on the right, is where the shots land: a smash, a FORE!, a
longest drive, a boom, each with its own picture.

**The carry** sits top centre while the camera is on the ball, with a small bar
under it showing how much after-touch that shot has left.

The whole thing is sized off screen height, so it does not stretch on an
ultrawide, and every panel is a plain square on purpose: rounded corners drawn
from stacked bands crawl between frames in this engine.

---

## Playing

Hold the swing button and the power meter fills, then falls back, so you can
time it. Release inside the green band near the top for a clean strike and a
small distance bonus. Outside it the ball hooks or slices.

After the strike the camera follows the ball and **keeps following it**, even
once it has stopped rolling, until you press `A`. Then you are back at the tee
with a fresh ball.

While the ball is out you can lean on it. Full authority in the air, less once
it is bouncing, less again while rolling, and each shot gets a fixed allowance,
so it stays a nudge rather than a remote control.

Distances follow the game's own measurement setting, the same as vanilla golf.

---

## Twelve clubs

Three sets of four, cycled with the shoulder buttons.

| Set | Behaviour |
| --- | --- |
| Driver, Iron, Wedge, Putter | the standard four |
| Driver 2, Iron 2, Wedge 2, Power Putter | flatter and slightly further, so they run out along a street instead of arcing over it |
| Driver 3, Iron 3, Wedge 3, Putter 3 | the standard launch angles, far more power |

---

## Ball modes

| Mode | Behaviour |
| --- | --- |
| `Normal` | an ordinary golf ball |
| `Fire` | sets light to everything it touches and leaves fires burning |
| `Boom` | flies normally, then detonates the instant it touches down, or hits anything, or lands in water |
| `Super` | carries fifty times as far by default, and hits like it. The multiplier is a row in the drawer, from x2 to x200 |

Set the starting mode with `BallMode` in the ini, or cycle with `M`.

---

## Impacts

Everything scales with how fast the ball is going when it connects, so a putt
rolls harmlessly and a full driver does real harm.

- **Walls and roads** take the game's own impact damage: chipped concrete,
  cracked render, spidered glass, and a puff of dust.
- **Cars** get a panel caved in exactly where the ball lands, lose body health,
  set off the alarm and rock on their springs. A hard hit above the waistline
  knocks a window out, and a low one at a corner takes a tyre off the rim.
- **Pedestrians** ragdoll, take damage that scales with the strike, and are
  thrown along the ball's line.

Contact is found with a synchronous shape probe swept along the path the ball
covered since the last frame, extended past its current position. The extension
matters: physics halts the ball a fraction short of what it hits, so a probe
drawn only between two frame positions runs entirely outside every wall.

`ImpactPower` scales the whole system from `0` to `3`. `CarKnockback` scales
just the shove a car takes.

---

## Police

Three stages, all optional.

1. For the first three minutes they ignore you completely.
2. After that they still do not care about a man hitting golf balls, until you
   knock down three people inside forty-five seconds. Older hits are forgotten,
   so the occasional casualty never builds up.
3. Being unseen overrides everything. If nobody has line of sight to you, there
   is no heat regardless.

`PoliceWanted=false` switches the lot off, as does `K` in game.

**Batons and tasers.** A man with a golf club is a nuisance, not a gunfight. At
one and two stars the responders carry a nightstick close in and a taser further
back, so being caught means a beating or a stun rather than being shot off the
tee. Draw an actual firearm and it lifts at once: they answer whatever you are
holding. Their guns are never taken away, only holstered, so they are their
normal selves the moment it stops applying. `LessLethalCops` and
`LessLethalMaxStars` control it, and `BATONS` is on the in-game drawer.

---

## Settings

Everything lives in `StreetGolf.ini`. The eleven you reach for most are in the
settings drawer on the card, under the `D-pad` or the arrow keys: ball mode,
the super shot multiplier, police, batons, impact power, car damage, wall marks,
trail, aim line, after-touch and units.

Changes made in the drawer last for the session. The ini holds what it starts as.

Records are kept in `StreetGolf.records.txt` beside the script.

`Debug=true` adds a diagnostic line and writes `StreetGolf.log`. Leave it off
unless something needs chasing down.

---

## Notes

- Balls persist so you can rapid-fire. Up to ten stay in the world at once.
- If you are wasted, busted, tased or pulled into a cutscene, Street Golf packs
  itself up and hands the golfer back to the game rather than leaving him locked
  in his stance.

Credit to Rockstar for the golf assets. This only lets you use them in the street.

---

## Building from source

`StreetGolf.cs` is the deliverable and is committed, but it is assembled from
the parts in `src/`. Edit those, not the single file.

```
./tools/get-apiref.sh          # once: fetch the SHVDN reference assemblies
./build.sh                     # assemble src/ and compile-check every version
./build.sh --install           # ...and copy into the game scripts folder
./build.sh --zip               # ...and build release/StreetGolf-<version>.zip
python tools/make_icons.py     # redraw StreetGolf/icons (needs Pillow)
```

The build fails if the script stops compiling against SHVDN 3.6, the 3.7
nightlies or 3.9 Enhanced, which is how cross-version support is kept honest.
The reference assemblies are third party binaries and are not committed.

The icons are drawn by `tools/make_icons.py` as white silhouettes and tinted
by the script at draw time, so a new icon is a few lines of Python and a name.
