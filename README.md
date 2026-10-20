# ULTIMATE RB4DX TOOL

Turn any MP3 into a playable **Rock Band 4 (RB4DX)** custom song for a jailbroken PS4 - drag, drop, play.

`MP3 -> auto-made chart -> Rock Band 3 CON (Onyx) -> PS4 .pkg (Forge) -> optional FTP send`

Made by **CoronaSend**. Use the HELP / REPORT BUG button in the app (or the Discord link in `discord.txt`) for help.

## Download

Get the latest zip from the **Releases** page, extract it anywhere (keep the folder together) and run `ULTIMATE RB4DX TOOL.exe`.
No Python or other installs needed. Windows 10/11 only.

See **[HOW TO USE.md](HOW%20TO%20USE.md)** for the full guide.

## Features

- Drag in MP3s or whole folders; per-song artist, title, genre, year, preview start (with PLAY preview) and chart level (Easier / Normal / Busier).
- Cover art from iTunes or your own image; LOOK UP fills song details automatically.
- Warnings for very short / long / low-bitrate MP3s.
- Bundle many songs into one package; skips songs that are already built.
- Sends the .pkg to your PS4 over FTP (port 2121), with a connection / game check and a PS4 song manager.
- WEB SEARCH for free / Creative Commons music (Internet Archive, Jamendo).
- Cancel, copy log, one-click bug report.
- **GAME CUSTOMIZATION** page: make custom RB4DX highways from your own photos (3 large photos, collage or backdrop), send them to the PS4, and manage / delete highways already on the console. Also: a Backgrounds tab, a Skins tab (crowd meter, score, solo box), a Backup / restore tab, a PS4 IP dropdown that remembers consoles, and a startup update check.

## Limitations

- Charts are generated from the audio: playable and synced, but not hand-charted quality. No vocals track.
- Songs shorter than about 30 seconds fail to build.
- Bundle packs and EU packages are untested in-game.
- US Rock Band 4 (CUSA02084) packages; the EU option is experimental.

## Building from source

The repository contains the app and scripts only. The release zip additionally bundles `runtime\` (portable Python 3.14 with librosa, mido, numpy, scipy, soundfile, imageio-ffmpeg, Pillow), `onyx\` and `forge_official\`.

```
csc.exe /target:winexe /win32icon:songtool.ico /out:"ULTIMATE RB4DX TOOL.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll UltimateTool.cs HighwayMaker.cs CustomizeExtras.cs
```

(C# 5 / .NET Framework 4 compiler.) Command-line build of a single song:
`runtime\python.exe build_song.py song.mp3 --title "Title" --artist "Artist"`

## Credits

This tool would not exist without these projects - huge thanks to their authors:

- **[Onyx](https://github.com/mtolly/onyx)** by Michael Tolly (mtolly) - builds the Rock Band 3 CON files (GPL v3).
- **[LibForge / ForgeTool](https://github.com/mtolly/LibForge)** by Maxton, with the fork maintained by LlysiX and mtolly - converts CON files to PS4 packages (LGPL/GPL).
- **RB4DX** and the Rock Band modding community.
- [librosa](https://librosa.org), numpy, scipy, mido, Pillow, FFmpeg and the other open-source libraries listed in `THIRD-PARTY-NOTICES.md`.
- Cover art lookup uses the iTunes Search API; free music search uses the Internet Archive and Jamendo.

## Legal

- Use only music you own or have the right to use. This tool does not provide or download copyrighted commercial music; WEB SEARCH lists only free / Creative Commons sources.
- You are responsible for complying with the laws and terms that apply to you, including for jailbroken consoles and game modification.
- Not affiliated with or endorsed by Harmonix, Sony or the RB4DX team. Rock Band and RB4DX are trademarks of their owners.
- Provided "as is" without warranty. Code is MIT licensed (see `LICENSE`); bundled third-party tools keep their own licenses (see `THIRD-PARTY-NOTICES.md`).
