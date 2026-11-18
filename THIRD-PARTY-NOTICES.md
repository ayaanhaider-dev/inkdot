# Third-party notices

InkDot is built on these projects. Thank you to their authors.

| Project | Used for | License |
| --- | --- | --- |
| [OpenTabletDriver](https://github.com/OpenTabletDriver/OpenTabletDriver) 0.6.8.1 | The driver that reads the tablet (in `external/OpenTabletDriver`) | LGPL-3.0, see `external/OpenTabletDriver/LICENSE` |
| [Avalonia](https://github.com/AvaloniaUI/Avalonia) | The app's UI | MIT |
| [HotAvalonia](https://github.com/Kir-Antipov/HotAvalonia) | Live UI reloading while developing | MIT |
| [Geist and Geist Mono](https://github.com/vercel/geist-font) | Fonts (in `src/Lveno.App/Assets/Fonts`) | SIL Open Font License 1.1 |
| [dmgbuild](https://github.com/dmgbuild/dmgbuild) | Laying out the installer window (build time only) | MIT |

OpenTabletDriver ships unchanged in source form here, and InkDot talks to it over its own RPC
interface and plugin API. You can swap in your own build of it: see "Build from source" in the README.

The Lveno name, wordmark and the InkDot dot character are Lveno's brand assets. The code is MIT
licensed; the brand is not. Forks should use their own name and logo.
