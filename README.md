# Unkerbal Things

A horror mod for Kerbal Space Program 1.12. It adds two creatures, one on Moho and Eve and one in Laythe's ocean, plus a handful of science parts for poking at the first one.

This is an early playtest build. Things will break. If something does, please open an issue and attach your `KSP.log`.

## What's in it

### The Moho creature

There is something in the ground on Moho and Eve. It's a swarm of a few thousand worms that moves like one animal. It is blind, so it can't see your craft, but it feels the ground. Landings, engines running near the surface, wheels, drills and impacts all give you away.

It travels underground and comes up underneath you when it has found you. You can get away if you are high enough or fast enough at that moment. If it catches the craft, the craft is gone.

### The Laythe leviathan

Below about 400 m in Laythe's sea there is a 45 m fish. It circles under you for a while and comes up close for a look. Later it attacks from far below at full speed. It can only destroy a craft it hits fast enough, and it never attacks in the first three minutes, so you have time to notice it and leave.

### How you notice them

You usually feel them before you see them. The picture starts to look like a bad video feed, the hull creaks, the music drops out and the instruments glitch. Close to the Moho creature the outside view goes completely. Then you only see through a camera part on the craft, or through a kerbal's eyes on EVA.

## Parts

Six parts for studying the Moho creature. They are in the science and utility tabs.

| Part | Tech node | What it does |
|---|---|---|
| Swarm Spotter | Electronics | Rough bearing and distance to whatever is moving under the ground. |
| Ground Proximity Radar | Electronics | Height and terrain ahead, with a warning beep. Meant for flying when the outside view is gone. |
| Geophone Array | Scientific Tech | Seismic recordings, has to sit on the ground. |
| Heavy Proximity Scanner | Advanced Science Tech | Scans the creature from close by. |
| Swarm Imager | Advanced Exploration | A big camera head for the top of a craft. It has its own feed (right mouse button aims, wheel zooms) and takes photographs and energy recordings from the air. |
| Seismic Signal Generator | Experimental Science | Hits the ground hard. The creature notices. |

## Install

1. Download `UnkerbalThings.zip` from the [releases page](../../releases).
2. Unzip it and copy `GameData/UnkerbalThings` into the `GameData` folder of your KSP install.

You need KSP 1.12 and Direct3D 11. The screen effect and the worm swarm don't work without it. The mod doesn't need ModuleManager or any other mod.

BDArmory Continued is optional. With it installed the creatures show up as targets and you can shoot at them.

## About the download

The DLL in the release zip is compiled by GitHub Actions from the source in this repo, not on my PC. You can open the build log behind each release and see what went in.

The models, sounds and shader bundles are a different story. They need Unity and Blender to build, so I make them at home and commit the finished files (the `assets` folder and `underwater/leviathan`).

## Settings

Everything is in `GameData/UnkerbalThings/settings.cfg`, and each option has a comment explaining it. If you want to play around, set `debugTools = true`. That adds a toolbar button with spawn buttons and some other testing tools.

## Building it yourself

    dotnet build UnkerbalThings.csproj -c Release

The project looks for KSP's DLLs in the default Steam folder. If your KSP is somewhere else, set the `KSP_ROOT` environment variable to that folder first.

## Credits and licence

This mod started from the code of Kraken's Kreations (MIT) and was cut down and rebuilt around these two creatures. The older monsters are still in `legacy/`, but they are not built.

The code is MIT, see `LICENSE`. The leviathan model and some of the Moho creature's sounds are CC BY 4.0 and have to stay credited. All sources are listed in `CREDITS.md`.
