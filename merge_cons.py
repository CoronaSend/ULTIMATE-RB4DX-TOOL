"""merge_cons.py <out.con> <pack name> <in1.con> <in2.con> ...
Merges several Onyx-built RB3 CONs into one multi-song CON (via Onyx)."""
import os
import re
import shutil
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
ONYX = os.path.join(HERE, "onyx", "onyx.exe")


def run(*a):
    subprocess.run(list(a), check=True, stdin=subprocess.DEVNULL,
                   stdout=subprocess.DEVNULL)


out, pack, ins = sys.argv[1], sys.argv[2], sys.argv[3:]
tmp = tempfile.mkdtemp(prefix="rb3pack_")
merged = os.path.join(tmp, "merged")
os.makedirs(os.path.join(merged, "songs"))
dta_parts = []
for i, p in enumerate(ins):
    ex = os.path.join(tmp, "ex%d" % i)
    run(ONYX, "extract", p, "--to", ex)
    with open(os.path.join(ex, "songs", "songs.dta"), encoding="utf-8",
              errors="replace") as f:
        dta_parts.append(f.read().strip())
    for name in os.listdir(os.path.join(ex, "songs")):
        if name != "songs.dta":
            shutil.copytree(os.path.join(ex, "songs", name),
                            os.path.join(merged, "songs", name))
    if i == 0:
        shutil.copytree(os.path.join(ex, "onyx-repack"),
                        os.path.join(merged, "onyx-repack"))
with open(os.path.join(merged, "songs", "songs.dta"), "w",
          encoding="utf-8", newline="\n") as f:
    f.write("\n".join(dta_parts) + "\n")

yml = os.path.join(merged, "onyx-repack", "repack-stfs.yaml")
txt = open(yml, encoding="utf-8").read()
txt = re.sub(r"(package-name:\n- ).*", lambda m: m.group(1) + pack, txt, count=1)
open(yml, "w", encoding="utf-8").write(txt)

if os.path.exists(out):
    os.remove(out)
run(ONYX, "stfs", merged, "--to", out, "--game", "rb3")
shutil.rmtree(tmp, ignore_errors=True)
print("merged %d songs -> %s" % (len(ins), out))
