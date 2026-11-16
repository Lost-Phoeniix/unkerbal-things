# Laythe leviathan

The Bloop's model, sounds and build. Code: `Dread/BloopLogic.cs` (brain), `EntityTracker.Bloop.cs` (game side).

## Model

"Alien Fish (animated)" by Jungle Jim, CC BY 4.0, see `LICENSE-model.txt`. The credit is also in the package `CREDITS.md` and on a button in the debug window.

- `src/` (not in git): the Sketchfab download. Put `alien-fish-animated.zip` (FBX) and `alien_fish_animated.zip` (glTF) there unpacked as `fbxzip/` and `gltfzip/`.
- `export.py`: the first, lean `leviathan.fbx` (mesh, rig, the one clip, no embedded textures). The FBX is now exported from `leviathan_rig.blend` instead (below).
- `leviathan_rig.blend` (made from that FBX): the working file. The backbone has six joints instead of three (`Bone` split in three, `Bone.001` in two; new bones `Bone.a`, `Bone.b`, `Bone.001b`; the weights of the split bones are shared out by position along the fish, at most four bones per vertex), so the old clip deforms the mesh exactly as before. Actions: `swim_classic` (the model's own clip), `swim`, `sprint`, `turn_left`, `turn_right`, `attack`.
- `anim_build.py`: writes the five new actions into the open rig file. Every pose comes from one function: how far the backbone is pushed sideways at each place along the fish (a wave for swimming, the arc of a circle for turning). Paste it into Blender's text editor (it has no lambdas, file access or imports beyond `bpy`/`mathutils`, so the MCP's safe mode takes it). Then export: `bpy.ops.export_scene.fbx(filepath=..., object_types={'ARMATURE','MESH'}, path_mode='STRIP', add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False, bake_anim_simplify_factor=0)`. Never call `read_factory_settings` over the MCP: it unloads the MCP addon and the connection dies.
- Bones: `Bone` > `.a` > `.b` > `.001` > `.001b` > `.002` backbone; `.003`/`.004` upper and lower tail lobe; `.005` head with `.006` skull and `.007` lower jaw (+X turn in Blender opens it; the lure bulb hangs on `Bone.007_end`); `.008/.009` and `.010/.011` pectoral fins (the fish's left and right); `.012` with `.013/.016` and `.014/.015` ventral fins. `Bone`, `.005`, `.008`, `.010`, `.012` are separate roots at one pivot: turning the last four together turns the whole front of the fish.
- `dark.py`: the two darkened skins A and B (2048 px) and the comparison renders. A is the chosen one (`leviathanLook = A`).
- `tex.py`: normal map at 2048 px and the glow maps (only the brightest spots), prints the bone positions.
- Run each headless: `blender -b --factory-startup --python <script> -- "<src\fbxzip\source\Alien Fish (animated).fbx>" <out dir>`. Importing the GLB into a live Blender 5.2 crashed it (action slots); the FBX in background mode is fine.
- `unity/run-build.ps1`: builds `unity/leviathan.bundle` (model with a legacy Animation and all six clips, both skins, both glow maps, the normal map). Its `PoseCheck` lays the poses over the swim loop the way the game does and prints where tail, nose and jaw end up (`[KKLev] pose ...` lines): the turn clips must move tail and nose to the same side (left is -X), the attack clip must drop the jaw.

In Unity the fish faces +Z with its back up +Y. Length 1.173 model units, scaled to `leviathanLengthMetres` (45) in the game.

## Sounds

`sounds/`, CC0 from Freesound, see `SOUNDS.txt`. Picked from eleven candidates: #2 as the jumpscare, #3 as the distant growl. Played 2D through a low-pass, pitched down.

## Behaviour

It approaches, circles 25-40 m below the craft (75-100 m out, tightening to 28-40 m: the water at depth shows only about 60 m), then comes up to the craft's depth and glides slowly past it (11 m/s, 15-25 m beside it; the run-up starts out of sight on its own side), circles below again (sometimes one round more), dives 120-180 m under it and rises straight up at up to 48 m/s. Every round has its pass, so a kill comes after about 5-8 minutes (offline median 6 min).
A real rise starts in silence; the jumpscare (`leviathan-jumpscare.wav`: the scream from its loudest point, an octave-lower copy and a boom, limited) comes the moment it is near enough to be seen, about 1.5 s before the hit, wherever the camera looks. It destroys the craft only if it reaches it at `bloopKillSpeed` (34 m/s) or more; hard turns in the rise cost it speed, and a slow contact does nothing: it overshoots and comes round again. A late sideways dodge only saves a very fast craft (offline: at 12 m/s the hit still lands at about 39 m/s, at 20 m/s it always misses).
Before the kill opens (at least 180 s after it noticed the craft, plus a random spread, and after at least one feint) every rise is a silent feint that passes 30 m beside the craft.
It moves only along its heading. Out of sight it turns at up to 110 deg/s (limited by a sideways pull of 32 m/s2, so wide arcs at speed); in sight (within `leviathanSightMetres` and near the camera view) it turns at most 30 deg/s and eases into turns, so no sharp turn ever happens on screen.
Nothing pushes or drags the craft any more: the old pressure wave, hull shove and downward drag are gone.

### A craft under way (`bloopReactToMotion`)

The brain knows how the craft moves while it senses it (`PreyVelocity`; out of its senses it expects the craft to carry on, fading with a time constant of about 8 s).
- Search: it heads for where the craft will be, and chases a running craft at its speed + 10 m/s, up to `bloopChaseSpeed` (30).
- Circle: laid around where the craft will be; it swims the craft's speed on top of its own (up to `bloopPaceMaxSpeed`, 38). From 6 m/s it no longer circles: it goes the craft's way underneath it and weaves from side to side (further behind the faster the craft, half the ring radius at 16 m/s). The weave is slow enough to be swum in sight (sharpest bend 20 deg/s, about 6 swings a minute), it steers for where the weave will be in 3 s or more, and it keeps level with the craft by speed, not by turning. The first version (a ring squashed around the moving craft) made it snake left and right every second or two: 33-36 swings a minute offline, seen in KSP run `tv-20261115-204021`; now 5-9 offline and 7-12 in KSP (`tv-20261115-205140`).
- Close pass: beside the craft wherever it has got to; a craft above 4 m/s is overtaken from behind along its course.
- Dive: to a point under where the craft will be when the rise arrives.
- Close pass and a craft that turns: the line of the pass turns with the craft's course (at most 30 deg/s), the fish follows it 70 m ahead of itself, and being behind the craft near that line counts as the start. One pass keeps it within 60 m for at most 14 s (`InspectShowSeconds`), then it drops away: a craft that keeps turning away would otherwise hold it at its side for a minute.
- Scares: the bubble column starts 4 s ahead on the craft's course (bubbles need that long to rise 25 m), so the craft meets it; the drifting specks stream past a craft under way and wrap round inside their 45 m ball, so the "water moves" cue reads as a change of that stream. Eyes, lure, dimming, echo and flicker were already tied to the camera, the fish or the screen.
- Manoeuvres tested offline (40 seeds each, 8 and 16 m/s): straight, zigzag, circle of 120 m, turning back, stop and go, always sideways to the fish, random headings. In all of them it is within 150 m for 87-97 % of the time, makes about two close passes a run (12-31 s beside the craft in total; at rest 15 s), its feints pass within sight (at least 39 of 40) and the cues come as often as at rest. Before this pass the close pass happened in only 24-31 of 40 runs for zigzag and circle and lasted 1-5 s.
- A craft faster than the pace gets away (offline at 42 m/s: 0-3 kills in 60 runs). At rest nothing changed (same numbers as before in `tests/last-run.txt`).
Offline (`tests`, 60 seeds each, straight course): at 10 / 16 / 24 m/s it stays within 150 m for 93-95 % of the time and gets its close pass in 60/60 runs; without the change 63 / 34 / 23 % and 7 / 0 / 0 passes.

### In KSP (2026-11-15)

Scene `uw_move_day` (`run-ksp-test-tv.ps1 -Scenes uw_move_day`, 9 min; `EntityTracker.TestUnderwater.cs` `UwMoveScene`): the held craft is driven at rest, 10 m/s straight, zigzag, 12 m/s in a circle, turning back, 16 m/s; the kill is switched off in the scene; then each pose is put in front of the camera. Run `tv-20261115-205140` (with `dreadScreen = false` through `KK_TEST_SETTINGS`, otherwise the screen effect blacks the fish out): within 150 m in 588 of 592 samples while the craft moved, 4 close passes, 3 feints, 0-3 hard turns (over 45 deg/s) per phase, no exception from the mod. Pose shots: `poses-sheet.png` there; the craft stands in front of the fish and the head-on shots at 75 m are too murky to judge, so the bend and the open jaws are confirmed by the side shots and the logged weights only. The bubble column and the streaming specks were not looked at.

### Movement (`leviathanAnimations`)

`LeviathanRig.Animate` in `EntityTracker.Bloop.cs` sets every clip's weight and time itself each frame (no `Play`/`CrossFade`):
- Tail beat: beats per second = `leviathanTailStrouhal` (0.22) x speed / tail swing. The tail swings 17 % of the body length in `swim`, 21 % in `sprint`; the two loops are mixed between 22 and 40 m/s and kept in step. At 18 m/s that is about 0.5 beats a second, in the rise about 1.1.
- Bend: the backbone lies on the arc the fish swims: angle from nose to tail = body length x turn rate / speed. `turn_left`/`turn_right` hold 100 degrees and are laid over the loop additively with the matching weight (a 34 m circle: 76 degrees). A positive `TurnRate` of the brain is a right turn on screen.
- Jaws: the brain's `MouthOpen` (wide from 170 m to 130 m in a rise and until it is past; a third open on the close pass within 90 m) drives the additive `attack` pose: jaw 40 degrees down, skull 13 up, fins flared.
