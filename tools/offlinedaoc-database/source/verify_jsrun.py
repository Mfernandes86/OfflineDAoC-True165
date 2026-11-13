"""Run one of the test pages in source/tests/ in headless Edge and return what it wrote (lane D-V helper).

A test page loads the tool's real scripts with <script src="../../app/..."> and writes its result as JSON into
<pre id="out">. There is no Node on this PC, so the browser the tool is used in is also the JavaScript engine the
checks run in.

    from verify_jsrun import run_page
    result = run_page("con_eval.html")          # -> the parsed JSON, or raises RuntimeError
"""
import html, json, os, re, shutil, subprocess, tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
EDGE = r"C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe"
CHROME = r"C:/Program Files/Google/Chrome/Application/chrome.exe"


def browser():
    for exe in (EDGE, CHROME):
        if os.path.exists(exe):
            return exe
    return None


def run_page(name, wait_ms=8000, timeout=180, query=""):
    exe = browser()
    if not exe:
        raise RuntimeError("no Edge or Chrome found: cannot run the JavaScript checks")
    page = os.path.join(HERE, "tests", name)
    if not os.path.exists(page):
        raise RuntimeError("test page missing: " + page)
    url = "file:///" + page.replace("\\", "/").replace(" ", "%20") + query
    prof = tempfile.mkdtemp(prefix="wgverify_")
    try:
        r = subprocess.run([exe, "--headless=new", "--disable-gpu", "--no-first-run", "--no-default-browser-check",
                            "--user-data-dir=" + prof, "--virtual-time-budget=%d" % wait_ms, "--dump-dom", url],
                           capture_output=True, timeout=timeout)
    finally:
        shutil.rmtree(prof, ignore_errors=True)
    dom = r.stdout.decode("utf-8", "replace")
    m = re.search(r'<pre id="out">(.*?)</pre>', dom, re.S)
    if not m or not m.group(1).strip():
        raise RuntimeError("the test page %s wrote no result (script error?)\n%s" % (name, dom[-600:]))
    return json.loads(html.unescape(m.group(1)))
