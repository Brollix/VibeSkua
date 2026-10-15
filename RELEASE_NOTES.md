## VibeSkua v2.0.1

This release delivers the modern Butler V5 Avalonia UI upgrade, fixes custom directory loading in the Script Repository, introduces a dedicated local script filter, and resolves ghost online account detection caused by operating system process ID recycling.

---

### What's Changed

- **Butler V5 Avalonia Upgrade (v1.2.6)**: Fully integrated the native Avalonia port of the Butler V5 army synchronization plugin. All interfaces, including the Follower Roster and Settings windows, have been rewritten in pure Avalonia C# for instant rendering, modern styling, and fluid responsive scaling.
- **Script Repository Local Filter**: Resolved an issue where selecting a custom script directory did not populate the repository list. Added a dedicated **Local** category filter to quickly view and run scripts from your custom directory.
- **Process Lifecycle & PID Recycling Hardening**: Fixed a bug where terminated or crashed clients could falsely appear as "online" in the Butler V5 roster due to Windows process ID recycling. Butler V5 now verifies executable names and process start timestamps against file metadata.
- **Shortcut Management Refinements**: Refined launcher logic to ensure desktop shortcuts are not recreated if the user deliberately removed or relocated them.

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
