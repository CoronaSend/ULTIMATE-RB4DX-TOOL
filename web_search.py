"""web_search.py - search/download freely licensed music.

  web_search.py search "<query>"          -> JSON list on stdout
  web_search.py get <source> <id> <outdir> "<artist>" "<title>"  -> prints mp3 path

Sources: archive (Internet Archive, no key) and jamendo (Creative Commons,
needs a free client id in jamendo_key.txt next to this script).
"""
import json
import os
import re
import sys
import urllib.parse
import urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
UA = {"User-Agent": "SongTool/1.0 (CoronaSend)"}
try:
    import truststore
    truststore.inject_into_ssl()
except ImportError:
    pass


def http(url, timeout=30):
    req = urllib.request.Request(url, headers=UA)
    return urllib.request.urlopen(req, timeout=timeout)


def jget(url):
    with http(url) as r:
        return json.load(r)


def jamendo_key():
    p = os.path.join(HERE, "jamendo_key.txt")
    return open(p).read().strip() if os.path.exists(p) else ""


def search_archive(q):
    query = f'({q}) AND mediatype:audio AND format:MP3 AND licenseurl:*'
    params = [("q", query), ("fl[]", "identifier"), ("fl[]", "title"),
              ("fl[]", "creator"), ("fl[]", "licenseurl"), ("rows", "25"),
              ("output", "json"), ("sort[]", "downloads desc")]
    d = jget("https://archive.org/advancedsearch.php?" +
             urllib.parse.urlencode(params))
    out = []
    for x in d["response"]["docs"]:
        creator = x.get("creator", "Unknown")
        if isinstance(creator, list):
            creator = ", ".join(creator)
        out.append({"source": "archive", "id": x["identifier"],
                    "title": str(x.get("title", x["identifier"])),
                    "artist": creator,
                    "license": x.get("licenseurl", "see archive.org page")})
    return out


def search_jamendo(q):
    key = jamendo_key()
    if not key:
        return []
    params = {"client_id": key, "format": "json", "limit": 25,
              "search": q, "audiodlformat": "mp32"}
    d = jget("https://api.jamendo.com/v3.0/tracks/?" +
             urllib.parse.urlencode(params))
    out = []
    for x in d.get("results", []):
        if not x.get("audiodownload_allowed", True):
            continue
        out.append({"source": "jamendo", "id": x["audiodownload"],
                    "title": x["name"], "artist": x["artist_name"],
                    "license": x.get("license_ccurl", "Creative Commons")})
    return out


def safe(s):
    return re.sub(r'[<>:"/\\|?*]', "", s).strip() or "song"


def download(url, path):
    with http(url, 120) as r, open(path, "wb") as f:
        while True:
            b = r.read(1 << 16)
            if not b:
                break
            f.write(b)


def get(source, ident, outdir, artist, title):
    os.makedirs(outdir, exist_ok=True)
    path = os.path.join(outdir, f"{safe(artist)} - {safe(title)}.mp3")
    if source == "jamendo":
        url = ident
    else:
        meta = jget("https://archive.org/metadata/" + urllib.parse.quote(ident))
        mp3s = [f for f in meta.get("files", [])
                if f["name"].lower().endswith(".mp3")]
        if not mp3s:
            raise SystemExit("No MP3 file in that item")
        best = max(mp3s, key=lambda f: int(f.get("size", 0) or 0))
        url = "https://archive.org/download/%s/%s" % (
            urllib.parse.quote(ident), urllib.parse.quote(best["name"]))
    download(url, path)
    return path


def meta(query):
    """Best iTunes match -> artist, title, year."""
    d = jget("https://itunes.apple.com/search?" +
             urllib.parse.urlencode({"term": query, "entity": "song", "limit": 1}))
    if not d.get("results"):
        return ""
    x = d["results"][0]
    return "\t".join([x.get("artistName", ""), x.get("trackName", ""),
                      x.get("releaseDate", "")[:4], x.get("primaryGenreName", "")])


def main():
    cmd = sys.argv[1]
    if cmd == "meta":
        try:
            sys.stdout.buffer.write(meta(sys.argv[2]).encode("utf-8"))
        except Exception as e:
            print("[warn] meta", e, file=sys.stderr)
    elif cmd == "search":
        res = []
        for fn in (search_archive, search_jamendo):
            try:
                res += fn(sys.argv[2])
            except Exception as e:
                print("[warn]", fn.__name__, e, file=sys.stderr)
        for r in res:
            line = "\t".join(str(r[k]).replace("\t", " ").replace("\n", " ")
                             for k in ("source", "id", "artist", "title", "license"))
            sys.stdout.buffer.write((line + "\n").encode("utf-8"))
    elif cmd == "get":
        src, ident, outdir, artist, title = sys.argv[2:7]
        print(get(src, ident, outdir, artist, title))


if __name__ == "__main__":
    main()
