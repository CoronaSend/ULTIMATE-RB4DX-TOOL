"""Prints 'duration_seconds<TAB>bitrate_kbps' for an audio file (uses the bundled ffmpeg)."""
import re
import subprocess
import sys


def main():
    import imageio_ffmpeg
    ff = imageio_ffmpeg.get_ffmpeg_exe()
    r = subprocess.run([ff, "-hide_banner", "-i", sys.argv[1]], capture_output=True,
                       text=True, encoding="utf-8", errors="replace", stdin=subprocess.DEVNULL)
    t = r.stderr
    dur = br = 0
    m = re.search(r"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)", t)
    if m:
        dur = int(m.group(1)) * 3600 + int(m.group(2)) * 60 + float(m.group(3))
    m = re.search(r"bitrate:\s*(\d+)\s*kb/s", t)
    if m:
        br = int(m.group(1))
    sys.stdout.write(f"{dur:.1f}\t{br}")


if __name__ == "__main__":
    try:
        main()
    except Exception as e:
        sys.stderr.write(str(e))
