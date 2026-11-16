# Unkerbal Things: credits

## Laythe leviathan model (CC BY 4.0, credit required)

This work is based on ["Alien Fish (animated)"](https://sketchfab.com/3d-models/alien-fish-animated-f7f672a642fe4734a889d9482a3d8144) by [Jungle Jim](https://sketchfab.com/jungle_jim), licensed under [CC BY 4.0](http://creativecommons.org/licenses/by/4.0/). Changes: textures darkened, three joints added to the backbone, new swim, turn and attack animations.
Changes: textures darkened and reduced to 2048 px, a glow map made from the brightest spots, re-exported without embedded textures (`Leviathan/leviathan.bundle`).

## Moho creature model

A hand-made model (`Moho/moho.bundle`); without it the creature uses its procedural skin, made by the mod's code.

## Sounds (all CC0 1.0 from Freesound; credit is not required, given here anyway)

Leviathan:
- `Sounds/leviathan-jumpscare.wav`: ["Sea Creature Roar"](https://freesound.org/people/Bikkit99/sounds/837799/) by Bikkit99, mixed with a boom from [Freesound sound 209772](https://freesound.org/people/Johnnyfarmer/sounds/209772/) by Johnnyfarmer
- `Sounds/leviathan-growl.mp3`: ["Underwater Crab-like Monster Growl"](https://freesound.org/people/ItsThatRoboGuy/sounds/435683/) by ItsThatRoboGuy

Soundscape (`DreadSounds/`, cut and processed from these recordings):
- [Freesound sound 232021](https://freesound.org/people/Ubehag/sounds/232021/) by Ubehag (hull creaks and groans)
- [Freesound sound 172411](https://freesound.org/people/EagleStealthTeam/sounds/172411/) by EagleStealthTeam (slow creaks)
- [Freesound sound 209772](https://freesound.org/people/Johnnyfarmer/sounds/209772/) by Johnnyfarmer (booms)
- [Freesound sound 870786](https://freesound.org/people/CHallSmith/sounds/870786/) by CHallSmith (metal hits)
- [Freesound sound 482167](https://freesound.org/people/Tim_Verberne/sounds/482167/) by Tim_Verberne (water)
- [Freesound sound 82325](https://freesound.org/people/KEVOY/sounds/82325/) by KEVOY (whales)

Moho worm (`DreadSounds/vac-rumble-a`, `vac-erupt-*`, `vac-seize-*`, `vac-kill-*`), cut, slowed, layered and played through the hull recordings above. Credit required (CC BY 4.0, changed as described):
- ["Retreating Earthworms.wav"](https://freesound.org/s/82390/) by Benboncan, CC BY 4.0
- ["Wet Meat [clean]"](https://freesound.org/s/264509/) by RICHERlandTV, CC BY 4.0
- ["Rockfall (7lrs,grnlzr,Eq) 2.wav"](https://freesound.org/s/497206/) by newlocknew, CC BY 4.0
- ["Landmass / Earth Rumble"](https://freesound.org/s/363122/) by el-bee (Arto Koivisto), CC BY 4.0
- ["Rockslide.wav"](https://freesound.org/s/77931/) by juskiddink, CC BY 4.0
- ["1 Can crush.wav"](https://freesound.org/s/563224/) by Brown_Sound, CC BY 4.0

CC0, credit not required, given anyway: [worm eating](https://freesound.org/s/872508/) by elektrokaiser, [Rocks.wav](https://freesound.org/s/336023/) by adamgryu, [Gravel Impacts and Falls](https://freesound.org/s/179341/) by lolamadeus, [Cracking Earthquake](https://freesound.org/s/222521/) by uagadugu, [d0 Maggots](https://freesound.org/s/223964/) by dav0r, [slime - squishing sounds](https://freesound.org/s/680447/) by rubberduck9999, [Slime 7](https://freesound.org/s/433827/) by Archos, [Bending Metal](https://freesound.org/s/556714/) by NachtmahrTV, [S35-22 Steel girders wrench and tear](https://freesound.org/s/675486/) by craigsmith, [Scrap metal dropping / crashing](https://freesound.org/s/587443/) by SamsterBirdies, [Car Crash (with Glass)](https://freesound.org/s/592388/) by magnuswaker, [hissing_gas](https://freesound.org/s/327534/) by Taberius, [Tractor2 FF655](https://freesound.org/s/199457/) by martinimeniscus, [Salt pour](https://freesound.org/s/137259/) by xenognosis.

The synthesised drone, creaks, thumps and static, the footsteps and the screen effect are made by the mod's own code.

## Code

MIT licence, see `LICENSE`. Unkerbal Things grew out of the MIT-licensed source of an earlier KSP horror mod; that copyright notice is kept in `LICENSE` as the licence requires.

The Moho kill blast's sizing (`Dread/KillBlast.cs`: how much propellant burns in vacuum, the fireball and blast radius, the wreckage speed) and the colour and range of its light (`WormBlastFx.cs`) are ported from Volumetric Explosion FX 1.1.1 by Warnix (MIT, Copyright (c) 2026 Warnix, https://github.com/warnix91/Volumetric-Explosion-FX); the licence text is at the top of `Dread/KillBlast.cs`. Volumetric Explosion FX itself is not included: if it is installed, it draws the blast (a soft dependency, never required).

The timed worm experiments (a run that needs its condition for its whole duration, then completes through the stock experiment) follow the idea of the Time For Science mod by Rjoande (MIT, https://github.com/Rjoande/TimeForScience). No code from it is used; the idea only.

## Science part models

The six science parts (the five worm instruments and the ground proximity radar, `Parts/*.mu`) and their two palette textures (`Parts/ut_science_palette.png`, `Parts/ut_science_emissive.png`) are the mod's own work, MIT like the code. Their geometry is generated by a script; no outside model or texture is used.

The imager camera feed imitates the field-sequential colour TV of Apollo 10-17 (colour wheel, 59.94 fields/s, field-store colour conversion), from published descriptions: Wikipedia "Apollo TV camera"; P. P. Coan, NASA TN D-7476 (1973); S. Lebar, "The Color War Goes to the Moon" (Invention & Technology, 1997). The colour order red, blue, green is that of the CBS system it derived from (Wetmore 1969); the Apollo order is not documented. No footage or image is used.
