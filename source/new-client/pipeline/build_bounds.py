"""Zone boundary walls (bound.csv) for every built outdoor zone (GitHub issue #50 item 4: in Muspelheim the player
walked up the mountains out of the lava bowl; the new client had no boundary walls, only the zone square).

    python build_bounds.py              # all built zones
    python build_bounds.py 104 100      # some zones

The real 1.64 client (game.dll, read 2026-11-12 for issue #50):
  - loader 0x464605 (and its twin 0x4649ae): per zone it opens csvNNN.mpk, and only when that archive can't be
    opened datNNN.mpk; from the one it opened it reads "bound.csv" (no bound.csv there -> no walls for the zone,
    no fall back to the dat archive). The housing zones are read from phousing/zones/zoneNNN.
  - each line is one wall: "closed, count, x1, y1, ..., xN, yN" in zone units (0..65536, x east, y south). count must
    be 1..98 ("Bad BoundarySegment!" otherwise, the line is skipped); a non-zero first value closes the line by
    repeating its first point (every 1.64 file has 0; the closed loops repeat their first point themselves).
  - 0x500c60 builds each segment as an invisible quad named "boundary" (vertices point i low / high, point i+1
    low / high, 200 units above and below a node that 0x4f4dc6 keeps at the player's height: in effect no top);
    0x45e325 puts the segments of the player's zone (and of the zone 1024 units across the nearest border) whose box
    comes within 1024 units of the player into the movement collision group 0x1018774; the camera and the ground
    pick never meet them.
  - The playable side is on the right of each line (x east, y south: the side (-dy, dx)), the side the quads' faces
    look to. Checked with the world database: of 47,124 monster spawns in the 1.64 zones, 47,018 are on that side
    of the nearest wall (the rest stand on peaks just past it); every zone point, zone-in spot and bind point is on
    it; the closed loops inside Dartmoor, Malmohus and Sheeroe Hills are wound the other way (mountains in the
    middle of the zone: playable outside them) and the Battleground loops (250-253) enclose the battleground.
  - Lines end on the zone's edge where the walls of the next zone go on; between those ends the border is open,
    and the openings match on both sides (Vale of Mularn's east edge 2560-10240 = Muspelheim's west edge
    35328-43008, its south edge 2048-18432 = East Svealand's north edge 34816-51200).
The 1.64 client copy has no Shrouded Isles zones: those (and any other built zone it lacks) take the later client's
bound.csv by the same rule (paths.zone_dir) - the server's navmeshes were built with those same walls.

Writes godot/content/zones/zoneNNN/bound.json (only where the zone has walls; a stale one is removed):
    {"source": where it came from, "lines": [[x1, y1, x2, y2, ...], ...]}   zone units as in bound.csv
game/world/zone_bounds.gd builds them as one-sided collision walls on ZoneLoader.LAYER_BOUND when a zone loads.
Also reports a zone exit (zone.json "exits") on the wrong side of a wall (none on 2026-11-12).
"""
import json, math, os, re, sys, tempfile, zipfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from daoc_mpk import read_mpk
import paths as _paths

ZONES = _paths.ROOT / 'godot/content/zones'
MAX_POINTS = 98                                        # game.dll: count >= 99 (0x63) is a bad segment


def parse(raw):
    """bound.csv -> [[x1, y1, x2, y2, ...]] as the 1.64 loader reads it."""
    lines = []
    for row in raw.decode('latin1').splitlines():
        v = [p.strip() for p in row.split(',') if p.strip() != '']
        if len(v) < 2:
            continue
        closed, count = int(float(v[0])), int(float(v[1]))
        if count <= 0 or count > MAX_POINTS:
            print(f'   bad line skipped (count {count})')
            continue
        nums = [int(float(p)) for p in v[2:2 + 2 * count]]
        if len(nums) < 2 * count:
            print(f'   short line: {count} points listed, {len(nums) // 2} given')
            nums = nums[:len(nums) // 2 * 2]
        if closed and len(nums) >= 2:
            nums += nums[:2]
        if len(nums) >= 4:
            lines.append(nums)
    return lines


def _mpk_from_zip(z, name):
    fd, tmp = tempfile.mkstemp(suffix='.mpk')
    try:
        os.write(fd, z.read(name))
        os.close(fd)
        return read_mpk(tmp)
    finally:
        os.remove(tmp)


def from_164(z, names, zone_id):
    """(source, bound.csv bytes or None) from the 1.64 zip, or None when the zip has no archive for the zone."""
    for pre in ('csv', 'dat'):
        pat = re.compile(rf'(phousing/)?zones/zone0*{zone_id}/{pre}0*{zone_id}\.mpk$' if zone_id else
                         rf'(phousing/)?zones/zone0+/{pre}0+\.mpk$', re.I)
        hit = next((n for n in names if pat.match(n)), None)
        if hit:
            return f'1.64 {hit}', _mpk_from_zip(z, hit).get('bound.csv')
    return None


def from_later(zone_id):
    d = _paths.zone_dir(zone_id)
    if not d.is_dir():
        return None
    files = {p.name.lower(): p for p in d.iterdir()}
    for pre in ('csv', 'dat'):
        p = files.get(f'{pre}{zone_id:03d}.mpk')
        if p:
            return f'later client {d.name}/{p.name}', read_mpk(str(p)).get('bound.csv')
    return None


def side(lines, x, y):
    """(distance to the nearest wall, True when on its playable side) - the nearest segment decides; at a shared
    corner both segments' normals are summed."""
    best = None
    for ln in lines:
        for i in range(0, len(ln) - 2, 2):
            ax, ay, bx, by = ln[i], ln[i + 1], ln[i + 2], ln[i + 3]
            dx, dy = bx - ax, by - ay
            l2 = dx * dx + dy * dy
            if l2 == 0:
                continue
            t = max(0.0, min(1.0, ((x - ax) * dx + (y - ay) * dy) / l2))
            px, py = ax + t * dx, ay + t * dy
            d = math.hypot(x - px, y - py)
            n = (-dy / math.sqrt(l2), dx / math.sqrt(l2))
            if best is None or d < best[0] - 1e-6:
                best = [d, n[0], n[1], px, py]
            elif abs(d - best[0]) <= 1e-6:
                best[1] += n[0]
                best[2] += n[1]
    if best is None:
        return None
    d, nx, ny, px, py = best
    return d, (x - px) * nx + (y - py) * ny >= 0.0


def check_exits(zone_id, info, lines):
    for e in info.get('exits', []):
        a = e.get('a') or [e['min'][0], e['min'][2]]
        b = e.get('b') or [e['max'][0], e['max'][2]]
        if not any(a) and not any(b):
            continue                                   # (placeholder rows at 0, 0: no jump there)
        ok = any((s := side(lines, (a[0] + (b[0] - a[0]) * t) / 0.025, (a[1] + (b[1] - a[1]) * t) / 0.025))
                 and s[1] for t in (0.0, 0.25, 0.5, 0.75, 1.0))
        if not ok:
            print(f'   WARNING zone {zone_id}: exit {e.get("name")} is past a boundary wall')
    # (zone.json "jump_points" are not checked: most zones carry Camelot Hills' test spots at its x / z)


def main():
    want = {int(a) for a in sys.argv[1:]}
    z = zipfile.ZipFile(_paths.need(_paths.ZIP_164, 'ZIP_164'))
    names = z.namelist()
    total = written = 0
    for zdir in sorted(ZONES.glob('zone[0-9][0-9][0-9]')):
        zone_id = int(zdir.name[4:])
        if want and zone_id not in want or not (zdir / 'zone.json').exists():
            continue
        info = json.loads((zdir / 'zone.json').read_text(encoding='utf-8'))
        found = None if info.get('city') or info.get('dungeon') else (from_164(z, names, zone_id) or from_later(zone_id))
        out = zdir / 'bound.json'
        lines = parse(found[1]) if found and found[1] else []
        if not lines:
            if out.exists():
                out.unlink()
                print(f'{zone_id:3d} {info.get("name")}: no walls - removed the old bound.json')
            continue
        data = {'source': f'{found[0]} bound.csv', 'lines': lines}
        text = json.dumps(data, separators=(',', ':'))
        total += 1
        if not out.exists() or out.read_text(encoding='utf-8') != text:
            out.write_text(text, encoding='utf-8')
            written += 1
        pts = sum(len(ln) // 2 for ln in lines)
        print(f'{zone_id:3d} {info.get("name", "")[:22]:22s} {len(lines):2d} lines {pts:4d} points '
              f'{pts - len(lines):4d} walls  {len(text):6d} bytes  <- {found[0]}')
        check_exits(zone_id, info, lines)
    print(f'{total} zones with walls, {written} bound.json written')


if __name__ == '__main__':
    main()
