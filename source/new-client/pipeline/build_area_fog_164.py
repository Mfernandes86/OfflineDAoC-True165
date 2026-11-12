"""The real 1.64 client's area fog: sector.dat [forest] of every zone, read inside DAoC 1.64.zip (owner 2026-11-12,
issue #50: Muspelheim's red came from this fog over set areas of the zone, not from a red sky).

    python build_area_fog_164.py

sector.dat [forest] (zones/zoneNNN/datNNN.mpk):
    red, green, blue    the fog colour
    clip                the fog distance (DAoC units; 16000 = 400 m, as in the [fog] section)
    fog                 the fog amount 0-100 (as in [fog])
    transition          how far inside an area the fog takes to come in fully (units; 2000 = 50 m)
    numrects, rectNN    rectangles x1, y1, x2, y2 in zone units
    numcircles, circleNN  circles x, y, radius in zone units
The counts decide which rows are read (a guess: the client's numbered keys are read up to their count). Seven zones
list more rows than their count (zone 6: numrects=0 with 5 rect rows; also 9, 12, 101, 103, 105, 208); the extra
rows are left out.

Writes godot/content/sky/area_fog_164.json: zone id -> {color 0-1, clip_m, amount, transition_m, rects [[x1, z1, x2, z2]]
and circles [[x, z, r]] in metres from the zone's corner (the client's zone-local x / z)}. game/sky_system.gd fades the
fog colour and distance toward it by how far inside the nearest area you stand (0 at the edge, full at transition_m).
Only zones the 1.64 client has are here (no Shrouded Isles zones: the 1.64 client copy has none).
"""
import json, os, re, sys, tempfile, zipfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from daoc_mpk import read_mpk
import paths as _paths

OUT = _paths.ROOT / 'godot/content/sky/area_fog_164.json'
U = 0.025                                              # DAoC units to metres


def _ints(v):
    return [int(float(p)) for p in v.split(';', 1)[0].split(',') if p.strip()]


def main():
    z = zipfile.ZipFile(_paths.need(_paths.ZIP_164, 'ZIP_164'))
    out = {}
    for info in z.infolist():
        m = re.match(r'zones/zone(\d+)/dat\d+\.mpk$', info.filename, re.I)
        if not m:
            continue
        fd, tmp = tempfile.mkstemp(suffix='.mpk')
        try:
            os.write(fd, z.read(info))
            os.close(fd)
            files = read_mpk(tmp)
        finally:
            os.remove(tmp)
        sector = files.get('sector.dat')
        if sector is None:
            continue
        sec = re.search(r'^\s*\[forest\](.*?)(?=^\s*\[|\Z)', sector.decode('latin1'), re.S | re.I | re.M)
        if not sec:
            continue
        kv = {}
        for line in sec.group(1).splitlines():
            line = line.split(';', 1)[0]
            if '=' in line:
                k, v = line.split('=', 1)
                kv[k.strip().lower()] = v.strip()
        num = lambda k: int(float(kv.get(k, '0') or 0))
        rects, circles = [], []
        for i in range(num('numrects')):
            r = _ints(kv.get(f'rect{i:02d}', ''))
            if len(r) >= 4:
                rects.append([round(v * U, 2) for v in (min(r[0], r[2]), min(r[1], r[3]), max(r[0], r[2]), max(r[1], r[3]))])
        for i in range(num('numcircles')):
            c = _ints(kv.get(f'circle{i:02d}', ''))
            if len(c) >= 3 and c[2] > 0:
                circles.append([round(v * U, 2) for v in c[:3]])
        if not rects and not circles:
            continue
        out[str(int(m.group(1)))] = {
            'color': [round(num(k) / 255.0, 4) for k in ('red', 'green', 'blue')],
            'clip_m': num('clip') * U, 'amount': num('fog'), 'transition_m': num('transition') * U,
            'rects': rects, 'circles': circles}
    out = dict(sorted(out.items(), key=lambda kv: int(kv[0])))
    OUT.write_text(json.dumps(out, indent=1), encoding='utf-8')
    print(f'{len(out)} zones with 1.64 area fog: ' + ', '.join(
        f'{k} ({len(v["rects"])}r {len(v["circles"])}c amt {v["amount"]})' for k, v in out.items()))
    print('104:', out.get('104'))


if __name__ == '__main__':
    main()
