"""Shared locks and the safe rules-file editor for the gamefix agents (lead, 2026-11-12).

Python:
    import sys; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__))); import locks
    with locks.lock("build"):   ...                       # one dotnet build / test at a time
    locks.edit_rules("PROCS", lambda rules: ...)          # read-modify-write true165-rules.json, both copies, under a lock
Shell:
    python -I <lead workspace>/gamefix/locks.py run build -- dotnet build "Dawn of Light.sln" -c Release
Lock names: build (dotnet build/test), rules (true165-rules.json), derive (deriving a test world), git (commits).
"""
import contextlib, json, os, subprocess, sys, time

LOCKDIR = os.path.join(__import__("tempfile").gettempdir(), "true165-locks")
RT = (os.environ.get("OFFLINE_DAOC_ROOT") or os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", ".."))).replace(os.sep, "/")
RULES = [RT + "/runtime/true165/true165-rules.json", RT + "/tools/claude-version/true165/true165-rules.json"]
TAG = "True 1.65 0.8 gamefix %s: "


@contextlib.contextmanager
def lock(name, wait=3600):
    os.makedirs(LOCKDIR, exist_ok=True)
    path = os.path.join(LOCKDIR, name + ".lock")
    t0 = time.time()
    while True:
        try:
            fd = os.open(path, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
            os.write(fd, ("%d %s" % (os.getpid(), time.strftime("%H:%M:%S"))).encode())
            os.close(fd)
            break
        except FileExistsError:
            try:
                if time.time() - os.path.getmtime(path) > 3 * 3600:   # stale lock from a crashed run
                    os.remove(path)
                    continue
            except OSError:
                pass
            if time.time() - t0 > wait:
                raise TimeoutError("lock %s held: %s" % (name, open(path).read() if os.path.exists(path) else "?"))
            time.sleep(2)
    try:
        yield
    finally:
        try:
            os.remove(path)
        except OSError:
            pass


def server_running():
    out = subprocess.run(["tasklist"], capture_output=True, text=True).stdout.lower()
    return "coreserver.exe" in out


def edit_rules(lane, fn):
    """fn(rules_dict) mutates the dict. Both copies are written with identical bytes, keeping the file's format
    (indent 1, no ASCII escaping, original newline). Every entry you add must have a 'reason' starting with
    TAG % lane, so a re-run can first drop its own old entries:  rules['updates'] = [u for u in rules['updates']
    if not u.get('reason', '').startswith(locks.TAG % 'PROCS')]."""
    with lock("rules"):
        raw = open(RULES[0], "rb").read()
        assert raw == open(RULES[1], "rb").read(), "the two true165-rules.json copies differ: stop and tell the lead"
        nl = b"\r\n" if b"\r\n" in raw else b"\n"
        rules = json.loads(raw.decode("utf-8"))
        fn(rules)
        out = json.dumps(rules, indent=1, ensure_ascii=False).encode("utf-8").replace(b"\n", nl)
        if raw.endswith(nl):
            out += nl
        for p in RULES:
            open(p, "wb").write(out)
        return len(out)


if __name__ == "__main__":
    a = sys.argv[1:]
    if len(a) >= 3 and a[0] == "run" and "--" in a:
        name, cmd = a[1], a[a.index("--") + 1:]
        if name == "build" and server_running():
            raise SystemExit("CoreServer.exe is running: no builds or tests now (owner rule). Wait and retry later.")
        with lock(name):
            sys.exit(subprocess.call(cmd))
    print(__doc__)
