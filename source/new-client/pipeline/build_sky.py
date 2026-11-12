"""Export the DAoC client's skies for MMOEngine: every zones/sky/sky.mpk sky_*.dat as JSON, plus its textures.

    python build_sky.py

Writes godot/content/sky/:
  <name>.json               one per sky definition (sky_hibernia, sky_muspelheim, ...), values as the client has them:
                            [main] sun / moon / stars / cloud layers, [lights_and_fog_clear|stormy] ambient, dynamic
                            (sun) light and distance fog per dawn/day/dusk/night, [canopy_color_*] sky dome colour
                            rings (zenith, east1-4, west1-4, nadir), [cloud_color_*] cloud colour rings,
                            [storm_system], [rain], [snow]
  textures/original/*.png   the textures those files name (clouds, sun disk/flare/glow, moon, stars, snowflake)
  textures/x4plus/*.png     Real-ESRGAN x4plus, 2x (owner rule), alpha kept
  zone_skies.json           zone id -> sky name (zones.dat skydome=, else the realm's sky)
  sky164_<name>.json        the REAL 1.64 client's skies (owner 2026-11-12, issue #50: "make sure you get the skybox
                            from the 1.64 client"): its zones/sky/sky.mpk holds one skyNNN.dat per REGION (NNN = region
                            id; 1.64 zones.dat has no skydome= key), five distinct skies: Albion, Avalon City, Avalon
                            Island, Midgard, Hibernia. Every zone of a region that has a 1.64 sky file uses it, so the
                            sky never changes at a seamless zone line (Muspelheim, zone 104, stands under the Midgard
                            sky as in 1.64; its red is the 1.64 area fog, build_area_fog_164.py). Only the zones whose
                            region has no 1.64 sky file (the battlegrounds 250-253, zones the 1.64 client lacks) keep
                            the later client's mapping. The 1.64 sky textures overwrite the later client's where they
                            differ (only whitestar32.tga: 32x32 in 1.64, 16x16 later).

The later (1.127-era) client's sky_*.dat files are kept for those remaining zones only. History: until 2026-11-12 every
zone used the later client's skies, Muspelheim the red sky_muspelheim.dat that 1.64 does not have (built 2026-10-09,
before the 1.64 client was on hand).
"""
import json, re, sys
from pathlib import Path
from PIL import Image

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from daoc_mpk import read_mpk
from assets_pipeline import CLIENT, ROOT
from build_zone import upscale_dir, save_if_changed
import paths as _paths
import io, os, tempfile, zipfile

OUT = ROOT / 'godot/content/sky'


def parse_value(v):
    v = v.split(';', 1)[0].strip()
    parts = [p.strip() for p in v.split(',')]
    try:
        nums = [float(p) for p in parts]
        return nums if len(nums) > 1 else nums[0]
    except ValueError:
        return v


def parse_dat(text):
    data, section = {}, 'main'
    for line in text.splitlines():
        line = line.strip()
        if not line or line.startswith(';'):
            continue
        m = re.match(r'^\[(.+?)\]', line)
        if m:
            section = m.group(1).strip().lower()
            data.setdefault(section, {})
            continue
        if '=' in line:
            k, v = line.split('=', 1)
            data.setdefault(section, {})[k.strip().lower()] = parse_value(v)
    return data


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    (ROOT / 'godot/content/.gdignore').touch()
    sky = read_mpk(str(CLIENT / 'zones/sky/sky.mpk'))
    textures = set()
    for name, raw in sorted(sky.items()):
        if not name.endswith('.dat'):
            continue
        data = parse_dat(raw.decode('latin1').lstrip('﻿'))
        for k, v in data.get('main', {}).items():
            if k.endswith('_texture') or k.endswith('_texture2'):
                if isinstance(v, str) and v:
                    textures.add(v.lower())
        for sec in ('snow',):
            t = data.get(sec, {}).get('flake_texture')
            if isinstance(t, str):
                textures.add(t.lower())
        (OUT / f'{name[:-4]}.json').write_text(json.dumps(data, indent=1), encoding='utf-8')
    odir = OUT / 'textures/original'
    odir.mkdir(parents=True, exist_ok=True)
    missing = []
    # (sundisk.bmp and sundisk.tga share one png name: the .bmp, which 26 of the 27 skies name, is written last)
    for t in sorted(textures, key=lambda t: (t.endswith('.bmp'), t)):
        src = next((p for p in (CLIENT / 'zones/sky').iterdir() if p.name.lower() == t), None)
        if src is None:
            missing.append(t)
            continue
        save_if_changed(Image.open(src).convert('RGBA'), odir / f'{Path(t).stem.lower()}.png')
    upscale_dir(odir, OUT / 'textures/x4plus')
    # which sky each zone uses: zones.dat skydome=, otherwise the realm's own sky
    zones = read_mpk(str(CLIENT / 'zones/zones.mpk'))['zones.dat'].decode('latin1')
    zone_skies = {}
    for m in re.finditer(r'\[zone(\d+)\](.*?)(?=\n\[|\Z)', zones, re.S | re.I):
        zid = int(m.group(1))
        dome = re.search(r'skydome\s*=\s*(\S+)', m.group(2))
        if dome:
            zone_skies[zid] = dome.group(1).lower().rsplit('.', 1)[0]
        else:
            zone_skies[zid] = {0: 'sky_albion', 1: 'sky_midgard', 2: 'sky_hibernia'}.get(zid // 100, 'sky_default')
    zone_skies = skies_164(zone_skies, zones)
    (OUT / 'zone_skies.json').write_text(json.dumps(zone_skies, indent=1), encoding='utf-8')
    print(f'{len([n for n in sky if n.endswith(".dat")])} skies, {len(textures)} textures ({missing} missing), '
          f'{len(zone_skies)} zones mapped; zone 200 -> {zone_skies.get(200)}, 104 -> {zone_skies.get(104)}')


def _mpk_bytes(raw):
    """read_mpk on archive bytes (the 1.64 client is read inside its zip, nothing extracted)."""
    fd, tmp = tempfile.mkstemp(suffix='.mpk')
    try:
        os.write(fd, raw)
        os.close(fd)
        return read_mpk(tmp)
    finally:
        os.remove(tmp)


def skies_164(zone_skies, zones_text):
    """The real 1.64 client's skies: sky164_<name>.json per distinct skyNNN.dat, its textures, and every zone whose
    region has a 1.64 sky file mapped to it (region from zones.dat region=)."""
    z = zipfile.ZipFile(_paths.need(_paths.ZIP_164, 'ZIP_164'))
    sky = _mpk_bytes(z.read('zones/sky/sky.mpk'))
    region_sky, textures = {}, set()
    for name, raw in sorted(sky.items()):
        m = re.match(r'sky(\d+)\.dat$', name)
        text = raw.decode('latin1').lstrip('﻿')
        if not m or not text.strip():
            continue                                   # (sky000.dat is empty)
        head = re.search(r'^;\s*([A-Za-z][A-Za-z ]*?)\s*$', text, re.M)
        nm = 'sky164_' + head.group(1).lower().replace(' ', '')
        data = parse_dat(text)
        prev = OUT / f'{nm}.json'
        body = json.dumps(data, indent=1)
        if nm in region_sky.values() and prev.read_text(encoding='utf-8') != body:
            raise SystemExit(f'{name}: same heading as another 1.64 sky but different values - name them apart')
        prev.write_text(body, encoding='utf-8')
        region_sky[int(m.group(1))] = nm
        for k, v in data.get('main', {}).items():
            if (k.endswith('_texture') or k.endswith('_texture2')) and isinstance(v, str) and v:
                textures.add(v.lower())
        t = data.get('snow', {}).get('flake_texture')
        if isinstance(t, str):
            textures.add(t.lower())
    odir = OUT / 'textures/original'
    files = {Path(i.filename).name.lower(): i.filename for i in z.infolist() if i.filename.lower().startswith('zones/sky/')}
    for t in sorted(textures):
        if t not in files:
            raise SystemExit(f'1.64 sky texture {t} not in the 1.64 client')
        save_if_changed(Image.open(io.BytesIO(z.read(files[t]))).convert('RGBA'), odir / f'{Path(t).stem.lower()}.png')
    upscale_dir(odir, OUT / 'textures/x4plus')
    moved = 0
    for m in re.finditer(r'\[zone(\d+)\](.*?)(?=\n\[|\Z)', zones_text, re.S | re.I):
        r = re.search(r'^\s*region\s*=\s*(\d+)', m.group(2), re.M)
        if r and int(r.group(1)) in region_sky:
            zone_skies[int(m.group(1))] = region_sky[int(r.group(1))]
            moved += 1
    print(f'1.64 skies: {sorted(set(region_sky.values()))} for regions {sorted(region_sky)}; {moved} zones mapped to them')
    return zone_skies


if __name__ == '__main__':
    main()
