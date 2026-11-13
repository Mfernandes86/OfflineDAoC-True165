"""Open a page of the tool in headless Edge and save a screenshot (or the rendered HTML).

    python -I source/shot.py "hunt?level=30" source/scratch/M-U/hunt.png [--size 1500x1000] [--wait 6000] [--dom]

Give the route WITHOUT the leading "#/" (Git Bash rewrites arguments that start with "#/" or "/" into Windows paths).

Each call uses its own throw-away browser profile, so several agents can run it at the same time.
Owned by the lead.
"""
import os, shutil, subprocess, sys, tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
EDGE = r"C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe"
CHROME = r"C:/Program Files/Google/Chrome/Application/chrome.exe"


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 2
    route, out = "#/" + argv[0].lstrip("#/"), os.path.abspath(argv[1])
    size, wait, dom = "1500x1000", "6000", False
    i = 2
    while i < len(argv):
        if argv[i] == "--size":
            size = argv[i + 1]; i += 2
        elif argv[i] == "--wait":
            wait = argv[i + 1]; i += 2
        elif argv[i] == "--dom":
            dom = True; i += 1
        else:
            raise SystemExit("unknown option " + argv[i])
    exe = EDGE if os.path.exists(EDGE) else CHROME
    url = "file:///" + ROOT.replace("\\", "/").replace(" ", "%20") + "/index.html" + route
    prof = tempfile.mkdtemp(prefix="wgshot_")
    os.makedirs(os.path.dirname(out), exist_ok=True)
    cmd = [exe, "--headless=new", "--disable-gpu", "--no-first-run", "--no-default-browser-check", "--hide-scrollbars",
           "--run-all-compositor-stages-before-draw",   # else the map canvas is sometimes captured before it is drawn
           "--user-data-dir=" + prof, "--window-size=" + size.replace("x", ","), "--virtual-time-budget=" + wait]
    try:
        if dom:
            r = subprocess.run(cmd + ["--dump-dom", url], capture_output=True, timeout=120)
            open(out, "wb").write(r.stdout)
        else:
            r = subprocess.run(cmd + ["--screenshot=" + out, url], capture_output=True, timeout=120)
        ok = os.path.exists(out) and os.path.getsize(out) > 0
        print(("saved " if ok else "FAILED ") + out + ("" if ok else "\n" + r.stderr.decode("utf-8", "replace")[-800:]))
        return 0 if ok else 1
    finally:
        shutil.rmtree(prof, ignore_errors=True)


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
