"""The Hero's Stag form (Spirit of the Hunt, StagECSEffect) from the real 1.64 client (owner 2026-11-12: "a Hero's Stag
has never been drawn ... does the 1.64 client not have it?").

The 1.64 client has it: gamedata monsters.csv 583 "Stag Man" and 859 "Lurikeen Stag Man" (scale 140 / 70), both
monnifs 291 = figures/Stag_man.NIF (anim set 35), skin 1162 = stag_man.dds in figures/skins/skin012.mpk. Its ids 3, 4,
12 and 13 are "empty" rows: the server's Stag ids 4 / 13 (and Vendo 3 / 12) are the later client's copies of those
rows, so monsters.json gets the 1.64 build under the server's ids too.

Same steps as build_monsters.py --models=583 (nif_character.build, the anim set's clips, the skin, Real-ESRGAN x4plus),
but every input from paths.ZIP_164: the files it needs are extracted (once, read-only from the zip) into
paths.WORK_164 (gamedata.mpk, anims/, figures/Stag_man.NIF + animnode.dat, figures/skins/skin012.mpk) and that folder
is the client for this run. Built in a staging folder (its own monsters.json and texture index - the shipped
monsters.json and assets/texture_index.json are not touched), then:
    python build_stag_164.py               # build into pipeline/_work/stag_164 (staging)
    python build_stag_164.py --install     # copy it to content/monsters/stag_man__stag_man + monsters.json rows
                                           # 4, 13, 583, 859 (scale / name from each 1.64 row)
"""
import json, os, shutil, sys, zipfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import paths as _paths

FILES = ('gamedata.mpk', 'figures/stag_man.nif', 'figures/animnode.dat', 'figures/skins/skin012.mpk')
STAGE = HERE / '_work/stag_164'
KEY = 'stag_man__stag_man'
MONSTERS = HERE.parent / 'godot/content/monsters'
# monsters.json id -> the 1.64 monsters.csv row it is drawn from (4 / 13: the server's StagECSEffect ids)
ROWS = {'583': '583', '859': '859', '4': '583', '13': '859'}


def extract(dst):
    with zipfile.ZipFile(_paths.need(_paths.ZIP_164, 'ZIP_164')) as z:
        for i in z.infolist():
            n = i.filename.lower()
            if i.is_dir() or not (n in FILES or n.startswith('anims/')):
                continue
            p = dst / i.filename
            if not p.exists() or p.stat().st_size != i.file_size:
                p.parent.mkdir(parents=True, exist_ok=True)
                p.write_bytes(z.read(i))
    (dst / 'effects').mkdir(exist_ok=True)              # (build_monsters lists it; the stag has no effect set)


def build():
    client = _paths.WORK_164
    extract(client)
    os.environ['DAOC_CLIENT'] = str(client)
    _paths.CLIENT = client                               # (read by assets_pipeline at its import, below)
    STAGE.mkdir(parents=True, exist_ok=True)
    import texture_index
    texture_index.CACHE = STAGE / 'texture_index_164.json'
    import build_monsters as bm
    assert str(bm.CLIENT) == str(client), bm.CLIENT
    bm.OUT = STAGE
    sys.argv = [sys.argv[0], '--models=' + ','.join(sorted(set(ROWS.values())))]
    bm.main()


def install():
    staged = json.loads((STAGE / 'monsters.json').read_text(encoding='utf-8'))
    src = STAGE / KEY
    if not (src / 'model.gltf').exists() or any('missing' in staged.get(r, {'missing': 1}) for r in ROWS.values()):
        raise SystemExit('staging build incomplete - run without --install first')
    dst = MONSTERS / KEY
    for f in ('model.gltf',):
        shutil.copyfile(src / f, dst / f)
    for sub in ('original', 'x4plus'):
        (dst / 'textures' / sub).mkdir(parents=True, exist_ok=True)
        for p in (src / 'textures' / sub).glob('*.png'):
            shutil.copyfile(p, dst / 'textures' / sub / p.name)
    mj = MONSTERS / 'monsters.json'
    meta = json.loads(mj.read_text(encoding='utf-8'))
    for k, r in ROWS.items():
        meta[k] = dict(staged[r])
    mj.write_text(json.dumps(meta, indent=1), encoding='utf-8')
    print('installed', KEY, 'rows', sorted(ROWS, key=int))


if __name__ == '__main__':
    install() if '--install' in sys.argv else build()
