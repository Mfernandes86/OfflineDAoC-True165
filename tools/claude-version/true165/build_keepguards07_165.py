"""True 1.65 0.7: the old-frontier keep and relic keep garrisons come back (realm-event audit 2026-11-11: keeps\\REPORT.md
B1, relics\\REPORT.md B1 and L5; owner: "fix every issue they found").

What went wrong. true165-rules.json "remove" -> "mob_names" -> "new mob": "Atlas placeholder spawn" (2026-10-30) archived
every Mob row named "new mob". Keep guards are stored under that placeholder name (the server names, levels and realms
them from their keep at load: GameKeepGuard.LoadFromDatabase -> RefreshTemplate), so the rule took 606 keep guard rows
with it: 13 of the 21 frontier keeps lost their lord (a keep changes hands only when its lord dies, so they could never
be captured) and the six relic keeps lost nearly all their guards (five of them could therefore never open). The rows
are still in the world's archive table offline_true165_removed.

What this pass restores (and how it tells the rows apart). A row comes back when ALL of these hold:
  1. it is an archived Mob row with Reason exactly "Atlas placeholder spawn" (the "new mob" rule; rows archived for any
     other reason - e.g. the 6 "freeshard keep merchant" GuardMerchants, the 44 "frontier hastener (New Frontiers)"
     rows - are left alone; the hasteners have their own add-file, add-rules165-hasteners.json);
  2. its ClassType is a keep class (DOL.GS.Keeps.*) and not GuardMerchant (still removed by mob_classes);
  3. it stands in region 1, 100 or 200 within 3,000 units (the server's KeepArea radius, keeps\\KeepArea.cs KEEP_RADIUS:
     the guard joins that keep's Guards) of one of the 21 frontier keeps or 6 relic keeps (KEEPS below). The 90 other
     archived placeholder rows (Thidranki 252: 44, of which build_bgkeeps165.py brought 12 back under new keys; Caer
     Caledon 250: 31; Molvik Faste 241: 13; the old region 165: 2) are not frontier keeps and stay archived;
  4. nothing replaced it later: its key is not in Mob (unless this file added it), no content file names its key, and no
     other Mob row of the world stands within 40 units of its spot (checked here on every run; 0 found 2026-11-12).
Every column is restored as archived and the original Mob_ID is kept. The one change of form: LastTimeRowUpdated comes
back as "YYYY-MM-DD HH:MM:SS" (how the base world stored it) instead of the archive's JSON form "YYYY-MM-DDTHH:MM:SS".
Level 1 / Realm 0 / Name "new mob" are the original values: the server sets name, level and realm from the keep.
Relic keep sentinels (GuardFighterRK) are spawned by code (events\\keep\\RelicGuardsOnKeepTaken.cs), not stored, so
there is nothing to restore for them.
NOTE for the owner: each keep had one GuardCorpseSummoner (24 rows). They are restored with the rest (they were keep
NPCs of these keeps); the server shows them only at keep level 10 (keeps\\Gameobjects\\Guards\\CorspeSummoner.cs), i.e.
at the six relic keeps. Set RESTORE_CORPSE_SUMMONERS = False and re-run to leave them out if they are judged post-1.65.

Keeping the remove rule from catching them again. The launcher's mob_names rule can exempt only one PackageID, and these
rows must keep their own columns, so the "new mob" entry moves from remove.mob_names to the front of remove.where with
the same reason and the same effect, except for the restored keys:
  Name = 'new mob' AND COALESCE(PackageID,'') <> '<mob_names_keep_package>' AND Mob_ID NOT IN (<the restored keys>)
(a remove rule must never match a row an add-file adds: it would be archived and re-added on every refresh). On a world
that still has the rows (a fresh Derive from the original world) they now simply stay, and the add-file leaves rows it
did not add alone.

python -I build_keepguards07_165.py [--db PATH] [--check]
    --db     world to read the archive from (read-only; default runtime\\data\\opendaoc-165.sqlite3.db)
    --check  compute everything, report what would change, write nothing (exit 1 if a file would change)
Rerunnable: a second run writes nothing. Refuses to run when the two content copies of a file it writes differ, and
refuses to drop a key the current add-file already lists (an archive that lost rows must not shrink the garrisons).
Out (both copies: tools\\claude-version\\true165 and runtime\\true165): add-keepguards07-165.json (the rows),
true165-rules.json (the exemption).
"""
import argparse, json, math, os, re, sqlite3, sys

HERE = os.path.dirname(os.path.abspath(__file__))
CV = os.path.normpath(os.path.join(HERE, '..', '..', '..'))
COPIES = [HERE, os.path.join(CV, 'runtime', 'true165')]
DB = os.path.join(CV, 'runtime', 'data', 'opendaoc-165.sqlite3.db')
ADDNAME = 'add-keepguards07-165.json'
RULESNAME = 'true165-rules.json'

PLACEHOLDER_NAME = 'new mob'
PLACEHOLDER_REASON = 'Atlas placeholder spawn'
WHERE_PREFIX = "Name = 'new mob' AND "          # identifies this pass's remove.where entry on a rerun
RESTORE_CORPSE_SUMMONERS = True
KEEP_RADIUS = 3000                              # keeps\KeepArea.cs KEEP_RADIUS (relic keeps are GameKeep too)
DUP_RADIUS = 40
# the 21 capturable frontier keeps and the 6 relic keeps (Keep table KeepID)
FRONTIER = [50, 51, 52, 53, 54, 55, 56, 75, 76, 77, 78, 79, 80, 81, 100, 101, 102, 103, 104, 105, 106]
RELIC = [57, 58, 82, 198, 110, 111]
KEEPS = FRONTIER + RELIC

ADD_REASON = ('True 1.65 keep garrisons 0.7: the old-frontier keep and relic keep guards, lords and commanders that the '
              '"new mob" placeholder rule archived on 2026-10-30 (keep guards are stored under that name; the server names, '
              'levels and realms them from their keep), back exactly as archived with their own keys '
              '(offline_true165_removed); the rule now exempts them; build_keepguards07_165.py')


def open_ro(path):
    return sqlite3.connect('file:' + os.path.abspath(path).replace('\\', '/') + '?mode=ro', uri=True)


def dump(data, ensure_ascii):
    """The content files' own style: indent 1, CRLF line ends, one trailing CRLF."""
    return (json.dumps(data, indent=1, ensure_ascii=ensure_ascii) + '\n').replace('\n', '\r\n').encode('utf-8')


def read_copies(name, must_exist=True):
    """(bytes or None) of one content file; both copies must be byte-identical."""
    blobs = []
    for d in COPIES:
        p = os.path.join(d, name)
        blobs.append(open(p, 'rb').read() if os.path.exists(p) else None)
    if blobs[0] != blobs[1]:
        raise SystemExit(f'refusing: the two copies of {name} differ ({COPIES[0]} / {COPIES[1]}); make them identical first')
    if must_exist and blobs[0] is None:
        raise SystemExit(f'missing {name}')
    return blobs[0]


def select_rows(con, current_add):
    """The archived rows to restore, plus a report of what was considered and why it was left out."""
    keeps = {r[0]: r for r in con.execute('SELECT KeepID, Name, Region, X, Y FROM Keep')}
    missing = [k for k in KEEPS if k not in keeps]
    if missing:
        raise SystemExit(f'Keep table lacks KeepIDs {missing}')
    ours = {r[0] for r in con.execute("SELECT RowKey FROM offline_true165_added WHERE SourceTable='Mob' AND Reason=?",
                                      (ADD_REASON,))} if con.execute(
        "SELECT 1 FROM sqlite_master WHERE name='offline_true165_added'").fetchone() else set()
    mob_ids = {r[0] for r in con.execute('SELECT Mob_ID FROM Mob')}
    grid = {}                                   # (region, x // 256, y // 256) -> Mob rows, for the spot check
    for o in con.execute('SELECT Mob_ID, Name, ClassType, Region, X, Y, Z FROM Mob WHERE Region IN (1,100,200)'):
        grid.setdefault((o[3], o[4] // 256, o[5] // 256), []).append(o)
    # content files naming a key (another fix that already used or replaced the row); this pass's own files excluded
    uuid = re.compile(r'[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}')
    texts = []
    for f in sorted(os.listdir(HERE)):
        if f.endswith('.json') and f not in (ADDNAME, RULESNAME):
            texts.append((f, set(uuid.findall(open(os.path.join(HERE, f), encoding='utf-8', errors='replace').read()))))
    rules_text = open(os.path.join(HERE, RULESNAME), encoding='utf-8').read()
    rules_wo_ours = re.sub(re.escape(WHERE_PREFIX) + r"[^\"]*", '', rules_text)
    texts.append((RULESNAME, set(uuid.findall(rules_wo_ours))))

    seen, picked, report = set(), [], {'other region / not a target keep': {}, 'left out': []}
    for rid, key, js in con.execute("SELECT Id, RowKey, RowJson FROM offline_true165_removed WHERE SourceTable='Mob' "
                                    "AND Reason=? ORDER BY Id", (PLACEHOLDER_REASON,)):
        row = json.loads(js)
        if key in seen:
            continue
        seen.add(key)
        cls = row.get('ClassType') or ''
        if row.get('Mob_ID') != key or row.get('Name') != PLACEHOLDER_NAME:
            report['left out'].append((key, 'archive row does not match its key/name'))
            continue
        if not cls.startswith('DOL.GS.Keeps.') or cls == 'DOL.GS.Keeps.GuardMerchant':
            report['left out'].append((key, f'not a keep guard class: {cls}'))
            continue
        if not RESTORE_CORPSE_SUMMONERS and cls == 'DOL.GS.Keeps.GuardCorpseSummoner':
            report['left out'].append((key, 'corpse summoner (RESTORE_CORPSE_SUMMONERS = False)'))
            continue
        best = None
        for k in KEEPS:
            kk = keeps[k]
            if kk[2] == row['Region']:
                d = math.hypot(kk[3] - row['X'], kk[4] - row['Y'])
                if d <= KEEP_RADIUS and (best is None or d < best[1]):
                    best = (k, d)
        if best is None:
            r = report['other region / not a target keep']
            r[row['Region']] = r.get(row['Region'], 0) + 1
            continue
        if key in mob_ids and key not in ours:
            report['left out'].append((key, 'key is in Mob already (another fix re-added it)'))
            continue
        named = [f for f, t in texts if key in t]
        if named:
            report['left out'].append((key, f'named by {", ".join(named)}'))
            continue
        cell = [o for gx in (-1, 0, 1) for gy in (-1, 0, 1)
                for o in grid.get((row['Region'], row['X'] // 256 + gx, row['Y'] // 256 + gy), ())]
        near = [o for o in cell if o[0] != key and o[0] not in ours
                and math.dist((o[4], o[5], o[6]), (row['X'], row['Y'], row['Z'])) < DUP_RADIUS]
        if near:
            report['left out'].append((key, f'another Mob row stands within {DUP_RADIUS}: {near[0][1]} {near[0][0]}'))
            continue
        out = dict(row)
        ts = out.get('LastTimeRowUpdated')
        if isinstance(ts, str) and re.fullmatch(r'\d{4}-\d\d-\d\d[T ]\d\d:\d\d:\d\d', ts):
            out['LastTimeRowUpdated'] = ts.replace('T', ' ')
        picked.append((best[0], out))
    picked.sort(key=lambda p: (KEEPS.index(p[0]), p[1]['ClassType'], p[1]['Mob_ID']))
    lost = sorted(set(current_add) - {r['Mob_ID'] for _, r in picked})
    if lost:
        raise SystemExit(f'refusing: {len(lost)} keys the current {ADDNAME} lists are not restorable from this world '
                         f'(e.g. {lost[:3]}); use the world that holds the archive')
    return keeps, picked, report


def build_rules(rules_blob, keys):
    rules = json.loads(rules_blob.decode('utf-8'))
    if dump(rules, True) != rules_blob:
        raise SystemExit(f'refusing: {RULESNAME} does not round-trip byte for byte (style changed?)')
    rm = rules['remove']
    old = rm['mob_names'].pop(PLACEHOLDER_NAME, None)
    if old is not None and old != PLACEHOLDER_REASON:
        raise SystemExit(f'unexpected reason for mob_names "{PLACEHOLDER_NAME}": {old!r}')
    keep_pkg = rm.get('mob_names_keep_package') or ''
    where = (WHERE_PREFIX + f"COALESCE(PackageID,'') <> '{keep_pkg}' AND Mob_ID NOT IN ("
             + ','.join("'" + k + "'" for k in sorted(keys)) + ')')
    entry = {'table': 'Mob', 'key': 'Mob_ID', 'where': where, 'reason': PLACEHOLDER_REASON}
    idx = [i for i, w in enumerate(rm['where']) if str(w.get('where', '')).startswith(WHERE_PREFIX)]
    if len(idx) > 1:
        raise SystemExit(f'{len(idx)} remove.where entries start with {WHERE_PREFIX!r}; expected at most one')
    if old is None and not idx:
        raise SystemExit(f'neither remove.mob_names "{PLACEHOLDER_NAME}" nor this pass\'s remove.where entry is in {RULESNAME}')
    if idx:
        rm['where'][idx[0]] = entry
    else:
        rm['where'].insert(0, entry)     # the old mob_names step ran before every where rule: keep that order
    return dump(rules, True)


def main():
    ap = argparse.ArgumentParser(description='restore the archived old-frontier and relic keep garrisons')
    ap.add_argument('--db', default=DB)
    ap.add_argument('--check', action='store_true')
    a = ap.parse_args()

    add_blob = read_copies(ADDNAME, must_exist=False)
    rules_blob = read_copies(RULESNAME)
    current_add = [r['Mob_ID'] for r in json.loads(add_blob.decode('utf-8'))['rows']] if add_blob else []

    con = open_ro(a.db)
    keeps, picked, report = select_rows(con, current_add)
    con.close()
    rows = [r for _, r in picked]
    add_new = dump({'table': 'Mob', 'key': 'Mob_ID', 'reason': ADD_REASON, 'rows': rows}, False)
    rules_new = build_rules(rules_blob, [r['Mob_ID'] for r in rows])

    # summary
    per = {}
    for k, r in picked:
        c = r['ClassType'].split('.')[-1]
        per.setdefault(k, {})
        per[k][c] = per[k].get(c, 0) + 1
    print(f'{len(rows)} keep NPC rows restored from the archive of {a.db}')
    for k in KEEPS:
        kind = 'relic' if k in RELIC else 'keep '
        d = per.get(k, {})
        print(f'  {kind} {k:3} {keeps[k][1]:<18} {sum(d.values()):3}  ' + ', '.join(f'{c} {n}' for c, n in sorted(d.items())))
    print(f'  lords restored: {sum(d.get("GuardLord", 0) for d in per.values())}')
    print(f'  archived placeholder rows outside the 27 keeps (not restored): {report["other region / not a target keep"]}')
    for key, why in report['left out']:
        print(f'  left out {key}: {why}')

    changes = [(name, blob, new) for name, blob, new in ((ADDNAME, add_blob, add_new), (RULESNAME, rules_blob, rules_new))
               if blob != new]
    if not changes:
        print('content is up to date: nothing written')
        return 0
    for name, blob, new in changes:
        print(f'{"would write" if a.check else "writing"} {name} ({len(new):,} bytes; was '
              f'{"absent" if blob is None else format(len(blob), ",") + " bytes"}) in both copies')
    if a.check:
        return 1
    for name, _, new in changes:
        for d in COPIES:
            p = os.path.join(d, name)
            tmp = p + '.tmp-keepguards07'
            with open(tmp, 'wb') as f:
                f.write(new)
            os.replace(tmp, p)
    for name, _, new in changes:
        if read_copies(name) != new:
            raise SystemExit(f'{name}: copies differ after writing')
    return 0


if __name__ == '__main__':
    sys.exit(main())
