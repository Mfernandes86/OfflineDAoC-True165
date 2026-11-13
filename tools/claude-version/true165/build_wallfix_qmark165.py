"""Wall audit lane QMARK (True 1.65 0.8, 2026-11-12): quest map points that follow the NPCs the other wall-audit lanes
move out from behind the client's invisible zone walls (owner: "move the npcs and the quest markers accordingly").

Which quest points store a coordinate (and so must be moved here) and which follow the NPC by themselves:
  stored   runtime/server/classic-quests.json  Quests.<id>.Steps[n].Marker (the red step marker), .Spawn (where an event
           monster appears) and .Custom (Region/X/Y of a use_item / travel / say step). The quest generator copied the
           step NPC's Mob X/Y/Z into most markers, so when an NPC moves its markers must move with it.
  stored   DataQuest SEARCH areas (SourceName "SEARCH;step;text;region;x;y;radius;time") - none points at a moved NPC.
  live     quest-giver "!" markers (mode165/QuestGiverMarkers165.cs), the inn courier markers (InnQuestMarkers165.cs),
           the kill-step marker of a drop step (ClassicQuests.StepMarker: the live SpawnPoint of the nearest dropper),
           patrol route lines (QuestPatrolRoutes.cs: the Path table, lane PATHS), bounty / reputation markers (built from
           the Mob table; their runtimes are off in True 1.65 Mode anyway). The client stores none (it draws what the
           server sends).

What this does: reads every other lane's Mob moves from true165-rules.json (updates whose reason starts "True 1.65 0.8
wallfix <LANE>: " and whose sql is "UPDATE Mob SET X = .., Y = .., Z = .. WHERE Mob_ID = '..' ... AND X = <old> AND
Y = <old>"), plus wallfix_qmark/moves.json (moves of rows an add-file creates, which the lanes message to QMARK), and
moves every stored classic-quests.json point that copies a moved row's old X/Y (within MATCH units, same region) to the
row's new X/Y (the same offset) and new Z (a point stored with Z 0 - a walkthrough loc without height - keeps Z 0).
Nothing else in the file changes. Rerunnable: the previous run's edits are recorded in wallfix_qmark/ledger.json and
taken back out first (only where the value is still what this script wrote), so a second run writes the same bytes.
Run it AFTER questfix07/build_questfix07_165.py whenever that one is re-run (it re-sets whole Marker objects).

    python -I build_wallfix_qmark165.py --out <dir>   write into a copy (<dir>/classic-quests.json + <dir>/ledger.json,
                                                      seeded from the real file and ledger when missing)
    python -I build_wallfix_qmark165.py --check       show what would change, write nothing
    python -I build_wallfix_qmark165.py               write the real runtime/server/classic-quests.json (backup first)
"""
import argparse, datetime, json, os, re, shutil, sqlite3, sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, '..', '..', '..'))
RULES = os.path.join(ROOT, 'runtime', 'true165', 'true165-rules.json')
QUESTS = os.path.join(ROOT, 'runtime', 'server', 'classic-quests.json')
STATE = os.path.join(HERE, 'wallfix_qmark')
LEDGER = os.path.join(STATE, 'ledger.json')
MOVES = os.path.join(STATE, 'moves.json')
CLEAN = os.environ.get("OFFLINE_DAOC_CLEAN_DB") or os.path.join(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."), "runtime", "data", "opendaoc-165.sqlite3.db")   # the clean world (database/unpack_db.py)
sys.path.insert(0, os.path.join(HERE, 'questfix07'))
sys.dont_write_bytecode = True
import qf07common as Q  # noqa: E402  (JSON files rewritten with their own formatting)

LANE = 'QMARK'
WALLFIX = 'True 1.65 0.8 wallfix '
MATCH = 2          # a stored point within this many units of the old X and Y is a copy of the row's position
KINDS = ('Marker', 'Spawn', 'Custom')

UPD = re.compile(r"UPDATE\s+Mob\s+SET\s+X\s*=\s*(-?\d+)\s*,\s*Y\s*=\s*(-?\d+)\s*,\s*Z\s*=\s*(-?\d+)\s+WHERE\s+(.*)$",
                 re.I | re.S)


def parse_moves(rules, clean_db):
    """[(lane, mob_id, region, old_x, old_y, new_x, new_y, new_z, why)] from the other lanes' wallfix updates."""
    moves = []
    con = None
    for u in rules.get('updates') or []:
        reason = u.get('reason', '')
        if not reason.startswith(WALLFIX) or reason.startswith(WALLFIX + LANE + ':'):
            continue
        m = UPD.match((u.get('sql') or '').strip())
        if not m:
            continue
        nx, ny, nz, where = int(m.group(1)), int(m.group(2)), int(m.group(3)), m.group(4)
        mid = re.search(r"Mob_ID\s*=\s*'([^']+)'", where)
        ox = re.search(r"\bX\s*=\s*(-?\d+)", where)
        oy = re.search(r"\bY\s*=\s*(-?\d+)", where)
        rg = re.search(r"\bRegion\s*=\s*(\d+)", where)
        if not (mid and ox and oy):
            continue
        region = int(rg.group(1)) if rg else None
        if region is None:
            if con is None:
                con = sqlite3.connect('file:' + clean_db.replace(' ', '%20') + '?mode=ro', uri=True)
            row = con.execute('select Region from Mob where Mob_ID = ?', (mid.group(1),)).fetchone()
            region = row[0] if row else None
        if region is None:
            continue
        lane = reason[len(WALLFIX):].split(':', 1)[0]
        moves.append((lane, mid.group(1), region, int(ox.group(1)), int(oy.group(1)), nx, ny, nz, reason))
    return moves


def load_extra_moves(path):
    if not os.path.exists(path):
        return []
    out = []
    for m in json.load(open(path, encoding='utf-8')).get('moves', []):
        out.append((m['lane'], m['mob_id'], m['region'], m['old'][0], m['old'][1], m['new'][0], m['new'][1], m['new'][2],
                    m.get('why', '')))
    return out


def undo(doc, ledger):
    lost = 0
    for op in reversed(ledger.get('ops', [])):
        cur = Q.jget(doc, op['path'])
        if Q.same(cur, op['after']):
            Q.jset(doc, op['path'], op['before'])
        else:
            lost += 1
    return lost


def apply(doc, moves):
    """Moves the stored points; returns (ops, hits) - ops for the ledger, hits for the report."""
    ops, hits = [], []
    for qid, info in doc['Quests'].items():
        for n, st in enumerate(info.get('Steps') or []):
            if not st:
                continue
            for kind in KINDS:
                p = st.get(kind)
                if not isinstance(p, dict) or not p.get('Region') or 'X' not in p or 'Y' not in p:
                    continue
                for lane, mid, region, ox, oy, nx, ny, nz, why in moves:
                    if p['Region'] != region or abs(p['X'] - ox) > MATCH or abs(p['Y'] - oy) > MATCH:
                        continue
                    new = {'X': p['X'] + (nx - ox), 'Y': p['Y'] + (ny - oy)}
                    if p.get('Z'):
                        new['Z'] = nz
                    base = ['Quests', qid, 'Steps', n, kind]
                    for key, val in new.items():
                        if p.get(key) != val:
                            ops.append({'path': base + [key], 'before': p.get(key), 'after': val})
                            p[key] = val
                    hits.append({'quest': qid, 'step': n, 'kind': kind, 'lane': lane, 'mob_id': mid,
                                 'old': [ox, oy], 'new': [new['X'], new['Y'], new.get('Z', p.get('Z'))]})
                    break
    return ops, hits


def run(out=None, check=False):
    rules = json.load(open(RULES, encoding='utf-8-sig'))
    moves = parse_moves(rules, CLEAN) + load_extra_moves(MOVES)
    if out:
        os.makedirs(out, exist_ok=True)
        target, ledger_path = os.path.join(out, 'classic-quests.json'), os.path.join(out, 'ledger.json')
        if not os.path.exists(target):
            shutil.copy2(QUESTS, target)
            if os.path.exists(LEDGER):
                shutil.copy2(LEDGER, ledger_path)
    else:
        target, ledger_path = QUESTS, LEDGER
    jf = Q.JFile(target)
    ledger = json.load(open(ledger_path, encoding='utf-8')) if os.path.exists(ledger_path) else {}
    lost = undo(jf.data, ledger)
    ops, hits = apply(jf.data, moves)
    print(f'moves read: {len(moves)} (lanes: {sorted({m[0] for m in moves})}); quest points moved: {len(hits)}, '
          f'values changed: {len(ops)}; earlier edits no longer present: {lost}')
    for h in hits:
        print('  quest %s step %s %s <- %s %s %s -> %s' % (h['quest'], h['step'], h['kind'], h['lane'], h['mob_id'],
                                                         h['old'], h['new']))
    if check:
        return hits
    data = jf.dumps()
    if data != jf.raw and not out:
        stamp = datetime.datetime.now().strftime('%Y%m%d-%H%M%S')
        shutil.copy2(QUESTS, os.path.join(os.path.dirname(QUESTS), f'classic-quests.before-wallfix-qmark-{stamp}.json'))
    Q.write_atomic(target, data)
    os.makedirs(os.path.dirname(ledger_path), exist_ok=True)
    led = {'_about': 'build_wallfix_qmark165.py: the edits it made to classic-quests.json (undone before each run)',
           'written': datetime.datetime.now().isoformat(timespec='seconds'), 'ops': ops, 'hits': hits}
    Q.write_atomic(ledger_path, (json.dumps(led, indent=1) + '\n').encode('utf-8'))
    json.loads(open(target, encoding='utf-8-sig').read())     # still valid JSON
    return hits


if __name__ == '__main__':
    ap = argparse.ArgumentParser()
    ap.add_argument('--out')
    ap.add_argument('--check', action='store_true')
    a = ap.parse_args()
    run(a.out, a.check)
