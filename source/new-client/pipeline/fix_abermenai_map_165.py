"""Abermenai's map picture (content/zones/zone253/map.png, the Map window's picture) with its ground (gamefix ABER,
2026-11-12).

Why: the zone map pictures were rendered on 2026-10-09 (godot/tests/zone_map_render.gd). Abermenai ships no ground
archive of its own - the period client paints it with Caledonia's (sector.dat use_texture=250, unmirrored; the 1.64
client's dat253.mpk has the same terrain.pcx, offset.pcx and water.pcx as dat250.mpk) - and the content got that ground
only later (2026-11-03 tiles, 2026-11-09 blend). So its picture showed trees and keeps on flat fog colour, no ground.

The real fix is a fresh render (a windowed run, the lead's call):
    Godot --path godot -- --skip-login --zone=253 --probe=zone_map_render --mute
Until then this script builds the same picture from renders that exist. Abermenai is Caledonia with one fixture
changed: the same heights, ground tiles, ground blend, water, sky and 414 of 415 fixtures (checked below), and the
2026-10-09 renders agree pixel for pixel on every tree and building outside the centre (99.8% of object pixels within
8 levels). So: Caledonia's render, with its central keep (bfrontkeep1 as rendered then: the dat placement, A=90) taken
out and Abermenai's (Hfrontkeep) put in at its period placement (fixture 3 of zone253/zone.json, fix_bg_keeps_165.py).
The two keep pictures come from Abermenai's own 2026-10-09 render, where they stand on flat colour (cut out once into
pipeline/data/abermenai_keeps.png: Hfrontkeep at A=90, bfrontkeep1 at A=0); turning a keep picture in the image is
the same as turning the model about its vertical axis under the straight-down camera (checked: Abermenai's A=90
Hfrontkeep turned 40 degrees matches its A=130 Hfrontkeep with 0.97 overlap). Ground left bare by the old keep and not
covered by the new one is filled from around it (OpenCV inpaint, a few pixels).

    python pipeline/fix_abermenai_map_165.py                    write zone253/map.png
    python pipeline/fix_abermenai_map_165.py --cut <old map>    cut the two keep pictures out of the old render first
Stops (changes nothing) if Caledonia's picture or the two zones' ground no longer match what this was made for.
"""
import hashlib, json, sys
from pathlib import Path
import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
ZONES = HERE.parent / 'godot/content/zones'
SPRITES = HERE / 'data' / 'abermenai_keeps.png'
CALEDONIA_RENDER_MD5 = 'dca261191f59c91be807b3aea62d3fe4'   # zone250/map.png as rendered 2026-10-09
PX_PER_M = 2048 / 1638.4                                    # the 2048 px picture of a 1638.4 m zone, north up
R = 80                                                      # keep picture half size, px
FLAT = np.array([(133, 173, 196), (114, 153, 177)])         # the old render's bare land / water colours
KEEP_AT_RENDER = (627.2, 752.0, 90.0)                       # the central keeps as the 2026-10-09 renders show them
OLD_BFRONT = (732.8, 1113.6, 0.0)                           # Abermenai's other bfrontkeep1 (fixture 1), A=0


def md5(p):
    return hashlib.md5(Path(p).read_bytes()).hexdigest()


def keep_cutout(img, x_m, z_m):
    """RGBA picture of the keep standing at (x_m, z_m) on flat colour: the object pixels connected to anything within
    40 px of the spot (the keep's open courtyard is flat colour too, so not only the centre pixel)."""
    from scipy import ndimage
    cx, cy = round(x_m * PX_PER_M), round(z_m * PX_PER_M)
    reg = img[cy - R:cy + R, cx - R:cx + R].astype(int)
    solid = np.min([np.abs(reg - c).max(axis=2) for c in FLAT], axis=0) > 12
    lab, _ = ndimage.label(ndimage.binary_dilation(solid, iterations=2))
    yy, xx = np.mgrid[-R:R, -R:R]
    near = set(np.unique(lab[(xx * xx + yy * yy <= 40 * 40) & solid])) - {0}
    keep = np.isin(lab, list(near)) & solid
    assert keep.sum() > 1000, 'no keep at %s' % ((x_m, z_m),)
    # edge pixels are part flat colour (the old render had no ground): a soft edge by distance from that colour, and
    # the flat colour taken back out of the pixel, so no blue rim comes along onto the ground
    dist = np.min([np.abs(reg - c).max(axis=2) for c in FLAT], axis=0)
    flat = FLAT[np.argmin([np.abs(reg - c).max(axis=2) for c in FLAT], axis=0)]
    a = np.clip((dist - 10) / 40.0, 0, 1) * keep
    rgb = np.clip((reg - (1 - a[..., None]) * flat) / np.maximum(a, 0.25)[..., None], 0, 255)
    return np.dstack([rgb, a * 255]).round().astype(np.uint8)


def cut(old_map):
    img = np.asarray(Image.open(old_map).convert('RGB'))
    h = keep_cutout(img, *KEEP_AT_RENDER[:2])
    b = keep_cutout(img, *OLD_BFRONT[:2])
    SPRITES.parent.mkdir(exist_ok=True)
    Image.fromarray(np.concatenate([h, b], axis=1), 'RGBA').save(SPRITES)
    print('cut', SPRITES)


def turned(sprite, deg_cw):
    """The keep picture turned clockwise in the image (a fixture yaw of -A turns the model A degrees clockwise seen
    from above, north up)."""
    return np.asarray(Image.fromarray(sprite, 'RGBA').rotate(-deg_cw, resample=Image.BICUBIC, center=(R, R)))


def check_same_ground():
    a, b = ZONES / 'zone250', ZONES / 'zone253'
    for f in ('heights_2x.f32', 'water.png', 'terrain/original/tex165.png', 'terrain/original/lod.png'):
        if md5(a / f) != md5(b / f):
            raise SystemExit(f'zone250 and zone253 differ in {f}: render Abermenai instead (see the docstring)')
    za, zb = (json.loads((d / 'zone.json').read_text(encoding='utf-8')) for d in (a, b))
    water = lambda z: [{k: v for k, v in w.items() if v is not None} for w in z['water']]   # (base_color: null is newer)
    if za['ground_splat'] != zb['ground_splat'] or water(za) != water(zb) or za['sky'] != zb['sky']:
        raise SystemExit('zone250 and zone253 differ in ground blend / water / sky: render Abermenai instead')
    key = lambda z, f: (z['models'][f['m']]['file'], tuple(round(v, 1) for v in f['pos']), f['yaw'], f['scale'])
    diff = {key(za, f) for f in za['fixtures']} ^ {key(zb, f) for f in zb['fixtures']}
    if any(not k[0].endswith(('bfrontkeep1.gltf', 'hfrontkeep.gltf')) for k in diff) or len(diff) > 2:
        raise SystemExit(f'zone250 and zone253 differ in more than the central keep: {sorted(diff)[:6]}')
    return next(f for f in zb['fixtures'] if f.get('id') == 3 and f.get('source') is None)


def build():
    if md5(ZONES / 'zone250/map.png') != CALEDONIA_RENDER_MD5:
        raise SystemExit('zone250/map.png is not the 2026-10-09 render this was made for: render Abermenai instead')
    keep = check_same_ground()
    sp = np.asarray(Image.open(SPRITES).convert('RGBA'))
    hkeep, bkeep = sp[:, :2 * R], sp[:, 2 * R:]
    out = np.asarray(Image.open(ZONES / 'zone250/map.png').convert('RGB')).copy()

    def place(sprite, x_m, z_m):
        """(full-size RGBA layer) the sprite centred on the fixture spot."""
        layer = np.zeros((2048, 2048, 4), np.uint8)
        cx, cy = round(x_m * PX_PER_M), round(z_m * PX_PER_M)
        layer[cy - R:cy + R, cx - R:cx + R] = sprite
        return layer

    from scipy import ndimage
    import cv2
    # Caledonia's keep as rendered: bfrontkeep1 at A=90 (its A=0 picture turned 90 degrees clockwise)
    old = place(turned(bkeep, KEEP_AT_RENDER[2] - OLD_BFRONT[2]), *KEEP_AT_RENDER[:2])[..., 3] > 0
    old = ndimage.binary_dilation(old, iterations=5)
    # Abermenai's keep at its period placement (zone.json fixture 3: yaw = -A)
    new = place(turned(hkeep, -keep['yaw'] - KEEP_AT_RENDER[2]), keep['pos'][0], keep['pos'][2])
    alpha = new[..., 3:4].astype(np.float32) / 255.0
    hole = old & (alpha[..., 0] < 0.98)
    # the keep stands mid-island on a round dirt mound: the ground a quarter / half turn round the mound's middle is
    # the same ground at the same distance, so the hole takes it from there (keeps the grain; inpaint only what is left)
    cx, cy = round(KEEP_AT_RENDER[0] * PX_PER_M), round(KEEP_AT_RENDER[1] * PX_PER_M)
    left = hole.copy()
    for turn in (90, 180, 270):
        mat = cv2.getRotationMatrix2D((cx, cy), turn, 1.0)
        src = cv2.warpAffine(out, mat, (2048, 2048), flags=cv2.INTER_LINEAR)
        bad = cv2.warpAffine(old.astype(np.uint8), mat, (2048, 2048), flags=cv2.INTER_NEAREST) > 0
        take = left & ~bad
        out[take] = src[take]
        left &= ~take
    out = cv2.inpaint(out, left.astype(np.uint8) * 255, 6, cv2.INPAINT_TELEA)
    out = (out * (1 - alpha) + new[..., :3] * alpha).round().astype(np.uint8)
    Image.fromarray(out).save(ZONES / 'zone253/map.png')
    print('zone253/map.png: Caledonia render, keep', keep['pos'], 'yaw', keep['yaw'], '- filled', int(hole.sum()), 'px')


if __name__ == '__main__':
    if len(sys.argv) == 3 and sys.argv[1] == '--cut':
        cut(sys.argv[2])
    elif len(sys.argv) == 1:
        build()
    else:
        raise SystemExit(__doc__)
