<p align="center">
  <img src="src/Lveno.App/Assets/lveno-icon.png" width="128" alt="InkDot icon">
</p>

<h1 align="center">InkDot</h1>
<p align="center"><b>A calmer, smarter pen tablet driver for macOS and Windows.</b><br>By Lveno.</p>

<p align="center">
  <a href="https://github.com/ayaanhaider-dev/inkdot/releases/download/v0.1.0/inkdot-launch.mp4"><img src="docs/launch.gif" width="560" alt="InkDot launch video"></a><br>
  <sub>▶ <a href="https://github.com/ayaanhaider-dev/inkdot/releases/download/v0.1.0/inkdot-launch.mp4">Watch with sound</a> (33 s)</sub>
</p>

InkDot replaces the stock XP-Pen driver with a fast, clean app that lives in your menu bar (or the Windows tray).
It runs on [OpenTabletDriver](https://github.com/OpenTabletDriver/OpenTabletDriver) underneath,
so it supports the same wide range of tablets, with Lveno's own features and UI on top.

## Features

- **Drawing area and screens.** Map the pen to any screen, seen as a live map with your real wallpapers, in left-to-right order. Screens plugged in or out are picked up without restarting the driver.
- **Push to switch screens.** Rest the pen on a screen edge for a second and it hops to the next screen, with a smooth glide and a small overlay animation.
- **Pressure curve.** Drag a three-point curve like XP-Pen's, or pick one of seven presets. Test it live on a scribble pad with a pressure readout, and calibrate it to your hand.
- **Mouse mode.** Use the pen like a trackpad (relative) instead of pointing at the screen (absolute). Applies instantly.
- **Pen buttons, per app.** Set the pen buttons differently for each app, chosen from tabs with the apps' own icons.
- **Pen scrolling.** Hold the tip still for 0.4 s, then drag to scroll pages like a phone. It switches off on its own in drawing apps.
- **Presets.** Quick presets for sketching, drawing and inking, plus your own saved setups.
- **Menu bar app.** Switch screens and presets from the menu bar. Optional start at login and left-handed mode.
- **Friendly onboarding.** A short, interactive setup: permissions, a pen test, your screen and how the pen should feel.

## Install

### macOS

1. Download `InkDot-<version>.dmg` from [Releases](../../releases) and drag **InkDot** into **Applications**.
2. Open InkDot. macOS warns that it can't verify the developer, because InkDot isn't notarized. Click **Done**.
3. Open **System Settings → Privacy & Security**, scroll down and click **Open Anyway** next to InkDot.
4. InkDot opens on its welcome setup and asks for **Input Monitoring** (to read the pen) and **Accessibility** (to move the cursor).

If macOS says InkDot "is damaged", that's the download quarantine flag. Clear it with:

```bash
xattr -dr com.apple.quarantine /Applications/InkDot.app
```

Quit XP-Pen's own driver (PenTablet) first: two drivers can't read the tablet at once. InkDot warns you if it's running.

**Requirements:** macOS 12 or later, Apple Silicon. Tested with XP-Pen tablets; any tablet OpenTabletDriver supports should work.

### Windows

1. Download `InkDot-<version>-win-x64.zip` from [Releases](../../releases), right-click it and choose **Extract All**.
2. Open **InkDot.exe** in the extracted folder. Windows SmartScreen may say "Windows protected your PC", because InkDot isn't code-signed. Click **More info → Run anyway**.
3. Quit XP-Pen's own driver first, from the tray or Task Manager.

**Requirements:** Windows 10 or 11, 64-bit. Pressure in drawing apps (Windows Ink) isn't in this version yet: the pen moves and clicks, but apps don't see pressure.

## Build from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download). For the installer, also `rsvg-convert` (`brew install librsvg`).

```bash
./scripts/package-mac.sh   # dist/InkDot.app: app, driver and plugin, .NET runtime included
./scripts/make-dmg.sh      # dist/InkDot-<version>.dmg: builds the app, then the installer
```

For Windows, build on any OS:

```bash
./scripts/package-win.sh   # dist/InkDot-<version>-win-x64.zip
```

Then open `installer/inkdot.iss` in [Inno Setup](https://jrsoftware.org/isinfo.php) on Windows to make a setup .exe.

Set `LVENO_SIGN_IDENTITY` to a code-signing certificate name to sign with it. Otherwise the app is signed
ad-hoc, and macOS asks for the permissions again after every rebuild.

### Developing

```bash
./scripts/dev.sh           # live-reloading app, attached to the driver of the installed InkDot
./scripts/dev.sh plugin    # rebuild the driver plugin and restart the driver
```

The installed `/Applications/InkDot.app` owns the driver, because it holds the macOS permissions. The dev
copy only talks to it, so you can change the UI without granting permissions again.

## How it's put together

| Folder | What's in it |
| --- | --- |
| `src/Lveno.App` | The Avalonia app: window, pages, menu bar, overlays and onboarding. It controls the driver over OpenTabletDriver's RPC pipe. |
| `src/Lveno.Plugins` | Driver plugins: pressure curve, screen switching, push to switch, pen scrolling and a fix for macOS screen layout changes. |
| `external/OpenTabletDriver` | OpenTabletDriver 0.6.8.1, the driver itself. |
| `src/*/Platform` | The macOS and Windows parts: screens, scrolling, apps and icons, overlays. |
| `scripts` | Dev mode, app packaging, the DMG installer (`scripts/dmg`) and the Windows package. |
| `installer` | The Windows installer (Inno Setup). |

Settings live in `~/Library/Application Support/InkDot`.

## License

InkDot's code is [MIT](LICENSE). OpenTabletDriver is LGPL-3.0. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
The Lveno name and logo are not covered by the code license.
