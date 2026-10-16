## VibeSkua v2.0.2

This release resolves the `ScriptMap.FullName` room number bug, fixes first-run dependency compilation errors when running complex script packages, and enhances script path resolution to support unzipped GitHub repository archives.

---

### What's Changed

- **Room Number Resolution (`ScriptMap.FullName`)**: Fixed a bug where `ScriptMap.FullName` would return SmartFoxServer's internal socket session room ID (e.g. `ascendeclipse-8912378`) instead of the user-facing room number (`ascendeclipse-9123`). Restored accurate parsing from the client area banner, ensuring compatibility with scripts and plugins that validate room names.
- **First-Run Script Include Compilation**: Fixed the `CS0246: The type or namespace name 'CoreBots' could not be found` error that occurred when running complex script packages for the first time. Included libraries are now compiled directly into the script assembly context without requiring background helper classes to be instantiated ahead of time.
- **GitHub ZIP Archive Compatibility**: Enhanced `ScriptPathResolver` to automatically discover `//cs_include` paths within folders unzipped directly from GitHub (handling `-main` and `-master` repository suffixes as well as relative subfolder structures) without requiring users to manually rename script folders.

---

### Installation & Updates

- **Automatic Update (Existing Users)**: If you installed VibeSkua using `VibeSkua-win-Setup.exe`, the update will download automatically in the background and apply upon restarting the application.
- **VibeSkua-win-Setup.exe (New Installs)**: Recommended 1-click Windows installer with automatic desktop shortcuts and background auto-updates.
- **VibeSkua-win-Portable.zip**: Zero-install standalone archive. Extract to any directory and launch `VibeSkua.Manager.exe` or `VibeSkua.exe`.

---

### Credits

- **auqw/skua** — Original framework & botting API.
- **ArrowDev (BlackArrow on Discord)** — Early Avalonia port foundation.
- **l0newolf12** — Butler V5 Avalonia port foundation.
- **NinjaXz** — Complete Avalonia overhaul, army controls, stability engineering, and release pipeline.
