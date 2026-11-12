"""Dungeon pieces' ground-pick targets added to the converted models in place (GitHub issue #42, 2026-11-11).

The real client's ground pick of a dungeon piece sees the piece's hidden "dungswitch/pickee" node (or its
"dungswitch/Collidee" node when it has no pickee) and its "dungswitch/waterproxy" (nif_static.find_pick / pick_parts;
1.64 game.dll 0x4be202 / 0x4be379-0x4be470). The drawn geometry is never a floor: Spindelhalla's spider room
(cave_1spider1) lifted you onto its cobweb sheets, which climb to the ceiling, and you could not get back out.
nif_static.convert now writes the floor (hidden "pickee_<n>" / "pickwater_<n>" meshes and extras.nif_pick); this adds
the same to every dungeon zone's models converted before, and
changes nothing else in them, so a patch carries only what changed:
  - pickee: its shapes appended as hidden pickee_<n> meshes (POSITION, NORMAL, indices; no material), and the drawn
    copies of its textured shapes taken out - the client hides the pickee node (0x4bde85), so they were never drawn
    (the "pick01" ice sheets of Krondon's and Tuscaran Glacier's halls, Iarnvidiur's Lair / Galladoria roothall09). A drawn node is only taken
    out when its name AND its vertex positions are those of a pickee shape the converter drew.
  - Collidee: extras.nif_pick lists the collision_ meshes under dungswitch/Collidee (matched by their vertex
    positions) - a floor with or without a pickee (nif_static.pick_parts: Caer Sidi's galleries); ladder and door
    collidees are left out, as the client's lookup does.
  - waterproxy: its shapes appended as hidden pickwater_<n> copies (the client adds it to the picker; the water stays
    drawn).
Every one of these is listed in extras.nif_pick. The model's source is the dungeon build's (build_zone.build_dungeon:
the client's zones/Dnifs). Re-running is safe.

    python add_dungeon_pick.py [--dry-run] [zone ids ...]      (none: every dungeon zone)
"""
import base64, hashlib, json, sys
from pathlib import Path
import numpy as np
from assets_pipeline import CLIENT, ROOT
from daoc_mpk import read_mpk
from netimmerse import read_nif, spec
import nif_static

ZONES = ROOT / 'godot/content/zones'
NOT_DRAWN = ('collision_', 'climb_', 'pickee_', 'helper_', 'doorcol')


def _pos_key(pos):
    return hashlib.md5(np.asarray(pos, dtype=np.float32).tobytes()).hexdigest()


def drawn_pickee_shapes(node, parent_m):
    """{(shape name, position key): count} of the pickee shapes nif_static.collect drew before it left the pickee out:
    shown (no hidden node on the way down) and textured or mapped (see convert's untextured helper rule)."""
    sp = spec()
    out = {}

    def walk(b, pm, props):
        if b is None or any(sp.is_a(b.type, t) for t in nif_static.SKIP_TYPES):
            return
        if b.get('flags', 0) & 1:
            return
        m = nif_static.local_matrix(b) @ pm
        props = dict(props)
        props.update(nif_static.props_of(b))
        if sp.is_a(b.type, 'NiTriBasedGeom'):
            data = b.get('data')
            if data is None or not data.get('num_vertices'):
                return
            pos, nrm, uv, col, tri = nif_static.shape_arrays(data, m)
            if len(tri) and (uv or nif_static.texture_name(props.get('NiTexturingProperty')) is not None):
                k = (b.get('name') or '', _pos_key(pos))
                out[k] = out.get(k, 0) + 1
            return
        for c in b.get('children') or []:
            walk(c, m, props)
    walk(node, parent_m, {})
    return out


class Doc:
    """A converted model's glTF (one embedded buffer), edited in place."""

    def __init__(self, path):
        self.path = path
        self.text = path.read_text(encoding='utf-8')
        self.d = json.loads(self.text)
        uri = self.d['buffers'][0]['uri']
        self.buf = bytearray(base64.b64decode(uri.split(',', 1)[1]))

    def pos_key(self, node):
        a = self.d['accessors'][self.d['meshes'][node['mesh']]['primitives'][0]['attributes']['POSITION']]
        bv = self.d['bufferViews'][a['bufferView']]
        off = bv.get('byteOffset', 0) + a.get('byteOffset', 0)
        return hashlib.md5(bytes(self.buf[off:off + a['count'] * 12])).hexdigest()

    def remove_nodes(self, drop):
        """Take out nodes (and their meshes) by index; references renumbered. Their buffer bytes stay (unused)."""
        if not drop:
            return
        d = self.d
        drop_mesh = {d['nodes'][i]['mesh'] for i in drop if 'mesh' in d['nodes'][i]}
        keep_mesh = [i for i in range(len(d['meshes'])) if i not in drop_mesh]
        mmap = {o: n for n, o in enumerate(keep_mesh)}
        keep = [i for i in range(len(d['nodes'])) if i not in drop]
        nmap = {o: n for n, o in enumerate(keep)}
        d['nodes'] = [d['nodes'][i] for i in keep]
        d['meshes'] = [d['meshes'][i] for i in keep_mesh]
        for n in d['nodes']:
            if 'mesh' in n:
                n['mesh'] = mmap[n['mesh']]
            if 'children' in n:
                n['children'] = [nmap[c] for c in n['children'] if c in nmap]
        for s in d['scenes']:
            s['nodes'] = [nmap[c] for c in s['nodes'] if c in nmap]

    def _accessor(self, arr, comp, kind, target):
        while len(self.buf) % 4:
            self.buf.append(0)
        raw = arr.tobytes()
        self.d['bufferViews'].append({'buffer': 0, 'byteOffset': len(self.buf), 'byteLength': len(raw), 'target': target})
        self.buf += raw
        acc = {'bufferView': len(self.d['bufferViews']) - 1, 'componentType': comp, 'count': len(arr), 'type': kind}
        if kind == 'VEC3' and comp == 5126:
            acc['min'], acc['max'] = arr.min(0).tolist(), arr.max(0).tolist()
        self.d['accessors'].append(acc)
        return len(self.d['accessors']) - 1

    def add_hidden_mesh(self, name, pos, nrm, tri):
        """As nif_static.Gltf.mesh does for collision_ meshes: no material, a scene-root node."""
        attrs = {'POSITION': self._accessor(pos.astype(np.float32), 5126, 'VEC3', 34962),
                 'NORMAL': self._accessor(nrm.astype(np.float32), 5126, 'VEC3', 34962)}
        idx = self._accessor(tri.reshape(-1).astype(np.uint32), 5125, 'SCALAR', 34963)
        self.d['meshes'].append({'name': name, 'primitives': [{'attributes': attrs, 'indices': idx}]})
        self.d['nodes'].append({'name': name, 'mesh': len(self.d['meshes']) - 1})
        self.d['scenes'][0]['nodes'].append(len(self.d['nodes']) - 1)

    def dumps(self):
        self.d['buffers'][0]['byteLength'] = len(self.buf)
        self.d['buffers'][0]['uri'] = 'data:application/octet-stream;base64,' + base64.b64encode(bytes(self.buf)).decode()
        return json.dumps(self.d)


def fix_model(path, nif):
    """Returns (changed?, summary, doc) for one converted dungeon piece."""
    doc = Doc(path)
    d = doc.d
    hidden = ('pickee_', 'pickwater_')
    # a re-run: the earlier floor meshes out first (and the file left as it is when nothing would change)
    old_pick = d.get('extras', {}).get('nif_pick')
    old_keys = sorted(doc.pos_key(n) for n in d['nodes'] if str(n.get('name', '')).startswith(hidden))
    doc.remove_nodes({i for i, n in enumerate(d['nodes']) if str(n.get('name', '')).startswith(hidden)})
    d.get('extras', {}).pop('nif_pick', None)
    pp = nif_static.pick_parts(nif)
    kind = pp['kind']
    info = {'pick': kind, 'removed': [], 'meshes': 0, 'triangles': 0}
    if kind is None:
        return doc.dumps() != doc.text, info, doc
    names, ntri = [], 0
    # the Collidee's collision_ meshes, matched by their vertex positions
    by_key = {}
    for n in d['nodes']:
        nm = str(n.get('name', ''))
        if 'mesh' in n and nm.startswith('collision_'):
            by_key.setdefault(doc.pos_key(n), []).append(nm)
    cnode, cm = nif_static.find_dungswitch_child(nif, 'collidee')
    for shape, data, mm in (nif_static.subtree_shapes(cnode, cm) if cnode is not None else []):
        pos, nrm, uv, col, tri = nif_static.shape_arrays(data, mm)
        if not len(tri):
            continue
        hit = by_key.get(_pos_key(pos))
        if not hit:
            info['unmatched'] = info.get('unmatched', 0) + 1
            continue
        names.append(hit.pop(0))
        ntri += len(tri)
    if kind == 'pickee':
        # the drawn copies of the pickee's textured shapes out (the client hides the pickee node)
        pnode, pm = nif_static.find_dungswitch_child(nif, 'pickee')
        want = drawn_pickee_shapes(pnode, pm)
        drop = set()
        for i, n in enumerate(d['nodes']):
            nm = str(n.get('name', ''))
            if 'mesh' not in n or nm.startswith(NOT_DRAWN) or nif_static_door(nm):
                continue
            k = (nm, doc.pos_key(n))
            if want.get(k, 0) > 0:
                want[k] -= 1
                drop.add(i)
                info['removed'].append(nm)
        doc.remove_nodes(drop)
    for prefix in ('pickee', 'pickwater'):
        for i, (shape, data, mm) in enumerate(pp[prefix]):
            pos, nrm, uv, col, tri = nif_static.shape_arrays(data, mm)
            if len(tri):
                doc.add_hidden_mesh(f'{prefix}_{i}', pos, nrm, tri)
                names.append(f'{prefix}_{i}')
                ntri += len(tri)
    d.setdefault('extras', {})['nif_pick'] = {'node': kind, 'meshes': names, 'triangles': ntri}
    info['meshes'], info['triangles'] = len(names), ntri
    new_keys = sorted(doc.pos_key(n) for n in d['nodes'] if str(n.get('name', '')).startswith(hidden))
    if old_pick == d['extras']['nif_pick'] and old_keys == new_keys and not info['removed']:
        return False, info, doc                            # (already done)
    return doc.dumps() != doc.text, info, doc


def nif_static_door(name):
    """A door leaf's mesh ("door<k>_..."): never a pickee copy."""
    import re
    return re.match(r'^door(col)?[0-9]+_', name) is not None


def main():
    args = [a for a in sys.argv[1:] if not a.startswith('--')]
    dry = '--dry-run' in sys.argv
    files = {p.name.lower(): p for p in (CLIENT / 'zones/Dnifs').iterdir()}
    nifs = {}
    total = {'models': 0, 'changed': 0, 'pickee': 0, 'collidee': 0, 'none': 0}
    for zj in sorted(ZONES.glob('zone*/zone.json')):
        zid = int(zj.parent.name[4:])
        if args and str(zid) not in args:
            continue
        info = json.loads(zj.read_text(encoding='utf-8'))
        if not info.get('dungeon'):
            continue
        for mdl in info['models']:
            path = zj.parent / mdl['file']
            base = mdl['nif']
            if not path.exists():
                continue
            if base not in nifs:
                src = files.get(base + '.npk')
                nifs[base] = read_nif(next(x for k, x in read_mpk(str(src)).items() if k.endswith('.nif'))) if src else None
            if nifs[base] is None:
                print(f'zone{zid:03d} {base}: no source NIF, left alone')
                continue
            changed, s, doc = fix_model(path, nifs[base])
            total['models'] += 1
            total[s['pick'] or 'none'] += 1
            if changed:
                total['changed'] += 1
                if not dry:
                    path.write_text(doc.dumps(), encoding='utf-8')
            if s['removed'] or s.get('unmatched') or not s['pick']:
                print(f'zone{zid:03d} {base}: {s}')
    print(('(dry run) ' if dry else '') + str(total))


if __name__ == '__main__':
    main()
