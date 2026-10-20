<<<<<<< HEAD
## ULTIMATE RB4DX TOOL v1.1.3
=======
## ULTIMATE RB4DX TOOL v1.1.4
>>>>>>> cd46517 (v1.1.4 - MP3 SEARCH overhaul + credits popup)

Drag an MP3 in, get a Rock Band 4 (RB4DX) song package for your jailbroken PS4.

### Install
<<<<<<< HEAD
1. Download `ULTIMATE-RB4DX-TOOL-v1.1.3.zip` and extract it anywhere (keep the folder together).
2. Run `ULTIMATE RB4DX TOOL.exe`. No Python or other installs needed. Windows 10/11.
3. Read `HOW TO USE.md`.

=======
1. Download `ULTIMATE-RB4DX-TOOL-v1.1.4.zip` and extract it anywhere (keep the folder together).
2. Run `ULTIMATE RB4DX TOOL.exe`. No Python or other installs needed. Windows 10/11.
3. Read `HOW TO USE.md`.

### What's new in 1.1.4
- **MP3 SEARCH (YouTube)**: fully rebuilt. Downloads now queue one after another, so grabbing several songs quickly doesn't cause failures or mix-ups.
- Live progress per row (`X%` / `CONV...` / `ADDED` / `RETRY`), multi-select rows with **DOWNLOAD SELECTED**, double-click to queue, and **OPEN FOLDER** button.
- More reliable detection of the downloaded MP3 file (uses yt-dlp's reported output path). Less chance of "Download failed" false positives, with clearer error messages.
- Deadlock fix that prevented some downloads from ever finishing.
- yt-dlp command hardening (`--retries`, `--fragment-retries`, `--force-overwrites`, `--no-part`) for fewer transient failures.

>>>>>>> cd46517 (v1.1.4 - MP3 SEARCH overhaul + credits popup)
### What's new in 1.1.3
- New left sidebar with **SONGS** and **GAME CUSTOMIZATION** pages.
- **Highway Maker**: build a custom RB4DX highway from your photos and send it straight to the PS4, plus a manager to list and delete highways on the console.
- **Background Maker**: make a 1920x1080 background from one photo (with a darken slider) and send it to the PS4, with its own manager.
- **Skin Maker**: export an existing crowd meter, score or solo box skin as a PNG, edit it, and import it back as a new skin.
- **Backup**: back up all custom textures from the PS4 to your PC and restore them later.
- **PS4 profiles**: the PS4 IP box remembers every IP you use; pick one from the dropdown.
- **Update check**: the tool tells you at startup when a newer version is on GitHub.
- Sending a highway, background or skin now checks the PS4 is reachable and asks before replacing a file with the same name.
- New name and icon: ULTIMATE RB4DX TOOL.

### In 1.1.2
- New Rock Band-style app icon.
- Every package gets a new number in its name (MYSONGPACK000001, 000002, ...) so each install is unique.
- Fixed builds failing for songs with very long file names (Windows path-length limit).

### In 1.1.1
- CREDITS button at the bottom of the window.
- Window fits smaller screens.

### Highlights
- Drag in MP3s or folders; per-song artist, title, genre, year, preview start (with PLAY) and chart level.
- Auto-chart for guitar, bass and drums; cover art from iTunes or your own image.
- Builds the PS4 `.pkg` and can send it over FTP, with a PS4 / game check and a PS4 song manager.
- Bundle packs, build cache, cancel button, copy log, bug-report button.
- WEB SEARCH for free / Creative Commons music.

### Known limitations
- Auto-generated charts, no vocals. Songs under ~30 s fail to build.
- Bundle packs and EU packages are untested in-game.

Bundles Onyx (GPL v3) and ForgeTool (LGPL/GPL); see `THIRD-PARTY-NOTICES.md`.

