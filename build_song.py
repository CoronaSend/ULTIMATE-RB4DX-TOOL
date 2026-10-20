#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
build_song.py - MP3 -> RB3 custom song .con, built by Onyx.

Pipeline:
  1. rb3_chart.py  MP3 -> auto-chart MIDI
  2. iTunes cover art lookup (optional)
  3. onyx import + onyx build --target rb3 -> .con

Usage:
  python build_song.py "song.mp3" --title "Weight of Sound" --artist "Stick Figure" \
      [--genre rock] [--year 2011]
"""

import argparse
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
VERSION = "1.1.3"

try:
    import truststore
    truststore.inject_into_ssl()
except ImportError:
    pass


def stem_for(mp3):
    stem = os.path.splitext(os.path.basename(mp3))[0]
    return stem.replace("(mp3.pm)", "").strip("_- ")


def shortname_for(title):
    s = re.sub(r"[^a-z0-9]", "", title.lower())
    if len(s) <= 12:
        return s or "customsong"
    return s[:8] + hashlib.md5(s.encode()).hexdigest()[:4]


def fetch_cover(title, artist, folder):
    """Cover art from the iTunes Search API; returns a jpg path or None."""
    import urllib.request, urllib.parse
    try:
        q = urllib.parse.urlencode({"term": artist + " " + title,
                                    "entity": "song", "limit": 1})
        with urllib.request.urlopen("https://itunes.apple.com/search?" + q,
                                    timeout=15) as r:
            res = json.load(r)["results"]
        if not res:
            return None
        url = res[0]["artworkUrl100"].replace("100x100bb", "600x600bb")
        path = os.path.join(folder, "cover.jpg")
        with urllib.request.urlopen(url, timeout=20) as r, open(path, "wb") as f:
            f.write(r.read())
        print("[art] cover found")
        return path
    except Exception as e:
        print("[art] no cover:", e)
        return None


def main():
    ap = argparse.ArgumentParser(description="MP3 -> RB3 custom .con (Onyx)")
    ap.add_argument("mp3")
    ap.add_argument("--title", required=True)
    ap.add_argument("--artist", required=True)
    ap.add_argument("--genre", default="rock")
    ap.add_argument("--year", type=int, default=2000)
    ap.add_argument("--density", type=float, default=1.0)
    ap.add_argument("--cover", default=None)
    ap.add_argument("--preview", type=float, default=0.0, help="preview start, seconds")
    ap.add_argument("--force", action="store_true")
    args = ap.parse_args()

    mp3 = os.path.abspath(args.mp3)
    stem = stem_for(mp3)
    dirname = stem
    if len(dirname) > 40:
        import hashlib
        dirname = stem[:30].rstrip("._ ") + "_" + hashlib.md5(stem.encode()).hexdigest()[:8]
    short = shortname_for(args.title)
    outdir = os.path.join(HERE, "build", dirname)
    os.makedirs(outdir, exist_ok=True)
    py = sys.executable

    con_path = os.path.join(outdir, short + ".con")
    sig_path = os.path.join(outdir, "build.sig")
    st = os.stat(mp3)
    sig = json.dumps([st.st_size, int(st.st_mtime), args.title, args.artist, args.genre,
                      args.year, args.density, args.preview, VERSION,
                      bool(args.cover and os.path.exists(args.cover)) and
                      [os.path.getsize(args.cover), int(os.path.getmtime(args.cover))]])
    if (not args.force and os.path.isfile(con_path) and os.path.getsize(con_path) > 100000
            and os.path.isfile(sig_path) and open(sig_path).read() == sig):
        print("[cache] already built with the same settings - reusing")
        print(f"[verify] OK: {con_path}")
        return

    print("== chart ==")
    mid = os.path.join(outdir, short + ".mid")
    subprocess.run([py, os.path.join(HERE, "rb3_chart.py"), mp3,
                    "-o", outdir, "--density", str(args.density)], check=True)
    os.replace(os.path.join(outdir, stem + "_chart.mid"), mid)

    print("== onyx ==")
    onyx = os.path.join(HERE, "onyx", "onyx.exe")
    work = os.path.join(outdir, "onyx_work")
    src = os.path.join(outdir, "onyx_src")
    for d in (work, src):
        shutil.rmtree(d, ignore_errors=True)
    os.makedirs(src)
    shutil.copy(mid, os.path.join(src, "notes.mid"))
    shutil.copy(mp3, os.path.join(src, "song.mp3"))
    with open(os.path.join(src, "song.ini"), "w", encoding="utf-8") as f:
        f.write(f"[song]\nname = {args.title}\nartist = {args.artist}\n"
                f"album = \ngenre = {args.genre}\nyear = {args.year}\n"
                "diff_guitar = 3\ndiff_bass = 3\ndiff_drums = 3\n"
                + (f"preview_start_time = {int(args.preview * 1000)}\n" if args.preview > 0 else ""))
    cover = None
    if args.cover and os.path.exists(args.cover):
        try:
            from PIL import Image
            cover = os.path.join(src, "cover.jpg")
            im = Image.open(args.cover).convert("RGB")
            im.thumbnail((600, 600))
            im.save(cover, quality=92)
            print("[art] using your image")
        except Exception as e:
            print("[art] could not use your image:", e)
            cover = None
    if not cover:
        cover = fetch_cover(args.title, args.artist, src)
        if not cover:
            print("[art] no cover found - building without art")
    subprocess.run([onyx, "import", src, "--to", work], check=True,
                   stdin=subprocess.DEVNULL)
    yml = os.path.join(work, "song.yml")
    txt = open(yml, encoding="utf-8").read()
    if cover:
        shutil.copy(cover, os.path.join(work, "cover.jpg"))
        txt = txt.replace("metadata:\n", "metadata:\n  file-album-art: cover.jpg\n", 1)
    txt = txt.replace("targets:\n", "targets:\n  rb3:\n    game: rb3\n    plan: fof\n", 1)
    open(yml, "w", encoding="utf-8").write(txt)
    if os.path.exists(con_path):
        os.remove(con_path)
    subprocess.run([onyx, "build", yml, "--target", "rb3", "--to", con_path],
                   check=True, cwd=work, stdin=subprocess.DEVNULL)
    if not os.path.isfile(con_path) or os.path.getsize(con_path) < 100000:
        raise SystemExit("Onyx did not produce a CON")
    open(sig_path, "w").write(sig)
    print(f"[verify] OK: {con_path}")
    print(json.dumps({"con": con_path,
                      "size_mb": round(os.path.getsize(con_path) / 1e6, 2),
                      "title": args.title, "artist": args.artist,
                      "shortname": short}, indent=2))


if __name__ == "__main__":
    main()
