# Third-party software

The release zip bundles these programs. Each keeps its own license (see the license files inside each folder). They run as separate programs; ULTIMATE RB4DX TOOL only launches them.

| Component | Folder | License | Source |
|---|---|---|---|
| Onyx Music Game Toolkit (Michael Tolly) | `onyx\` | GNU GPL v3 | https://github.com/mtolly/onyx |
| ForgeTool / LibForge fork (Maxton, LlysiX, mtolly) | `forge_official\` | GNU LGPL v3 / GPL v3 | https://github.com/mtolly/LibForge |
| LibForge / ForgeTool build used by Highway Maker (+ DtxCS, MidiCS, GameArchives) | `highway_lib\` | GNU LGPL v3 / GPL v3 (see `highway_lib\LICENSE`) | https://github.com/mtolly/LibForge |
| Python 3 (embedded runtime) | `runtime\` | PSF License | https://www.python.org |
| librosa, numpy, scipy, scikit-learn, numba, llvmlite, soundfile, soxr, mido, Pillow, requests, imageio-ffmpeg and their dependencies | `runtime\Lib\site-packages\` | BSD / MIT / ISC / HPND / Apache-2.0 / LGPL (see each package's `dist-info` folder) | PyPI |
| FFmpeg (via imageio-ffmpeg, and bundled in Onyx) | | LGPL / GPL | https://ffmpeg.org |

Source code for the GPL components is available at the links above.

Rock Band, Rock Band 4 and RB4DX are trademarks of their respective owners. This project is not affiliated with or endorsed by Harmonix, Sony or the RB4DX team.
