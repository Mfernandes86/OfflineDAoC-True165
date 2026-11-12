"""Builds a SCRATCH True 1.65 world with the questfix07 fragments applied, the way the launcher does, and checks that
every fragment entry really landed. Never touches the real databases or the real content files.

    python -I verify_questfix07.py            full: copy of the shipped v0.6 world (clean, no progress) + the WHOLE
                                              content with the fragments merged, applied with a Python copy of the
                                              launcher's content refresh (TrueMode.Refresh) - what a player updating
                                              from v0.6 gets. About 2-5 minutes.
    python -I verify_questfix07.py --quick    quick: copy of the live dev 165 world + ONLY the questfix07 output (its
                                              tagged rules entries, its add-files, the add-files it edits, the drops of
                                              the monsters it touches). Well under a minute after the copy.
  options: --world <dir> (default: the session scratchpad\\scratchworld), --base <db> (another base world),
           --fragments <dir> (default: this folder), --quick-check (also PRAGMA quick_check, ~1 min)

Steps: 1. build_questfix07_165.py --out <world>\\content --reseed (the real content copied in fresh, fragments merged;
stops on validation errors). 2. base world copied to <world>\\opendaoc-165.sqlite3.db (read-only reads of the
original). 3. the content applied. 4. checks: each update / row / add_edit / drop / remove / quest entry is in the
world; our rows are not hit by a remove rule (they would be archived and re-added every refresh); the DataQuests the
fragments touch have their start NPC, targets and items. Prints where the world is.
"""
import argparse
import json
import os
import shutil
import sqlite3
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.dont_write_bytecode = True
import qf07common as Q  # noqa: E402
import build_questfix07_165 as B  # noqa: E402

SCRATCH_WORLD = os.path.join(os.path.dirname(os.path.abspath(__file__)), '_scratchworld')   # or pass --world


def copy_base(base, target, live):
    for ext in ('', '-wal', '-shm', '-journal'):
        if os.path.exists(target + ext):
            os.remove(target + ext)
    real = {os.path.normcase(os.path.abspath(p)) for p in (Q.LIVE_DB, Q.V06_DB)}
    if os.path.normcase(os.path.abspath(target)) in real:
        raise SystemExit('refusing: the scratch world would be a real database')
    if live:
        src = Q.open_ro(base)                       # consistent copy that includes the WAL, read-only on the source
        dst = sqlite3.connect(target)
        src.backup(dst)
        src.close()
        dst.close()
    else:
        shutil.copyfile(base, target)               # the frozen v0.6 world: a plain byte copy, never opened by sqlite


def norm(v):
    if isinstance(v, bool):
        return int(v)
    return v


def eq(a, b):
    a, b = norm(a), norm(b)
    if isinstance(a, (int, float)) and isinstance(b, (int, float)):
        return abs(a - b) < 1e-6
    if a is None or b is None:
        return a is None and b is None
    return str(a) == str(b)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('--quick', action='store_true')
    ap.add_argument('--world', default=SCRATCH_WORLD)
    ap.add_argument('--base')
    ap.add_argument('--db', help='world the fragments are validated against (default: the live dev world; once the live '
                                 'world holds the fixes, pass a world from before them)')
    ap.add_argument('--fragments', default=HERE)
    ap.add_argument('--quick-check', action='store_true')
    a = ap.parse_args()
    t0 = time.time()
    world = os.path.abspath(a.world)
    os.makedirs(world, exist_ok=True)
    out = os.path.join(world, 'content')
    target = os.path.join(world, 'opendaoc-165.sqlite3.db')
    base = a.base or (Q.LIVE_DB if a.quick else Q.V06_DB)

    print(f'1. merging the fragments into a fresh copy of the content: {out}')
    rep, good, summary = B.run(out=out, reseed=True, fragments=os.path.abspath(a.fragments), db=a.db or (Q.LIVE_DB if os.path.exists(Q.LIVE_DB) else Q.V06_DB))
    if rep.errors:
        B.print_summary(rep, summary, False)
        sys.exit('validation errors: no scratch world built')
    t1 = time.time()
    print(f'   {len(good)} entries, {summary["updates"]} updates, {summary["removes"]} removes, own files '
          f'{summary["own_files"]} ({t1 - t0:.0f} s)')

    print(f'2. copying the base world {base}\n   -> {target}')
    copy_base(base, target, live=os.path.normcase(os.path.abspath(base)) != os.path.normcase(os.path.abspath(Q.V06_DB)))
    t2 = time.time()
    print(f'   ({t2 - t1:.0f} s)')

    folder = os.path.join(out, 'true165')
    c = sqlite3.connect(target)
    c.execute('PRAGMA journal_mode=MEMORY')
    c.execute('PRAGMA synchronous=OFF')
    c.execute('PRAGMA foreign_keys=OFF')
    rules = json.load(open(os.path.join(folder, 'true165-rules.json'), encoding='utf-8'))
    edited_files = sorted({e.d['file'] for e in good if e.kind == 'add_edit'})
    own_files = sorted(f for f in os.listdir(folder) if f.startswith(Q.OWN_PREFIX))
    drops = json.load(open(os.path.join(folder, 'drops165.json'), encoding='utf-8'))['monsters']
    drop_mobs = {e.d['mob'].lower() for e in good if e.kind == 'drop'}
    print('3. applying the content ' + ('(quick: questfix07 output only)' if a.quick else '(full launcher refresh)'))
    c.execute('BEGIN')
    if a.quick:
        only = {'updates': [u for u in rules['updates'] if u['reason'].startswith(Q.TAG)],
                'removes': [w for w in rules['remove']['where'] if w['reason'].startswith(Q.TAG)],
                'drop_mobs': drop_mobs, 'add_files': own_files + edited_files}
        stats = Q.launcher_refresh(c, folder, progress=lambda m: print('   ' + m), only=only)
    else:
        stats = Q.launcher_refresh(c, folder, progress=lambda m: print('   ' + m))
    c.execute('COMMIT')
    t3 = time.time()
    for k, v in stats.items():
        print(f'   {v:7} {k}')
    print(f'   ({t3 - t2:.0f} s)')

    print('4. checking every fragment entry in the scratch world')
    ok, problems, infos = 0, [], []
    schema = Q.load_schema(c)
    dq_ids = set()
    for e in good:
        d = e.d
        try:
            if e.kind == 'update':
                t, setcols, where = Q.parse_update(Q.update_sql(e))
                n = c.execute(f'SELECT count(*) FROM {Q.qi(t)} WHERE {where}').fetchone()[0]
                if 'set' in d and 'where' in d and not set(d['set']) & set(d['where']):
                    cond = ' OR '.join(f'NOT ({Q.qi(col)} IS {Q.sql_val(v)})' for col, v in d['set'].items())
                    bad = c.execute(f'SELECT count(*) FROM {Q.qi(t)} WHERE ({where}) AND ({cond})').fetchone()[0]
                    if n == 0:
                        problems.append(f'{e.label()} matches no {t} row in the built world')
                    elif bad:
                        problems.append(f'{e.label()} {bad} of {n} rows do not hold the new values (overwritten later?)')
                    else:
                        ok += 1
                else:
                    infos.append(f'{e.label()} {n} {t} rows match its WHERE after the build')
                    ok += 1
                if t == 'DataQuest':
                    dq_ids |= {r[0] for r in c.execute(f'SELECT ID FROM DataQuest WHERE {where}')}
            elif e.kind == 'row':
                t, row = d['table'], d['row']
                pk = Q.pk_of(schema, t)
                cur = c.execute(f'SELECT * FROM {Q.qi(t)} WHERE {Q.qi(pk)} = ?', (row[pk],))
                got = cur.fetchone()
                if got is None:
                    problems.append(f'{e.label()} {t} {row[pk]} is not in the built world')
                    continue
                rec = dict(zip([x[0] for x in cur.description], got))
                diff = [k for k, v in row.items() if not eq(rec.get(k), v)]
                if diff:
                    problems.append(f'{e.label()} {t} {row[pk]} differs in {diff}')
                else:
                    ok += 1
                if t == 'DataQuest':
                    dq_ids.add(row['ID'])
            elif e.kind == 'add_edit':
                doc = json.load(open(os.path.join(folder, d['file']), encoding='utf-8'))
                t, key = doc['table'], doc['key']
                cur = c.execute(f'SELECT * FROM {Q.qi(t)} WHERE {Q.qi(key)} = ?', (str(d['id']),))
                got = cur.fetchone()
                if d.get('delete'):
                    if got is not None:
                        problems.append(f'{e.label()} {t} {d["id"]} is still in the built world')
                    else:
                        ok += 1
                    continue
                if got is None:
                    problems.append(f'{e.label()} {t} {d["id"]} is not in the built world')
                    continue
                rec = dict(zip([x[0] for x in cur.description], got))
                diff = [k for k, v in d['set'].items() if not eq(rec.get(k), v)]
                if diff:
                    problems.append(f'{e.label()} {t} {d["id"]} differs in {diff}')
                else:
                    ok += 1
                if t == 'DataQuest':
                    dq_ids.add(int(d['id']))
            elif e.kind == 'drop':
                mob = d['mob']
                bad = []
                for item, chance in d.get('add', []):
                    r = c.execute('SELECT Chance FROM LootTemplate WHERE lower(TemplateName)=lower(?) AND lower(ItemTemplateID)=lower(?)',
                                  (mob, item)).fetchall()
                    if not r:
                        bad.append(f'{item} missing')
                    elif chance not in [x[0] for x in r]:
                        bad.append(f'{item} at {[x[0] for x in r]}% (a world loot row already had it; drops165 skips '
                                   f'items the template has), not {chance}%')
                for item in d.get('remove', []):
                    lkeys = [x[0] for x in c.execute('SELECT LootTemplate_ID FROM LootTemplate WHERE lower(TemplateName)=lower(?) '
                                                     'AND lower(ItemTemplateID)=lower(?)', (mob, item))]
                    r = sum(1 for k in lkeys if c.execute("SELECT 1 FROM offline_true165_added WHERE SourceTable='LootTemplate' "
                                                          "AND RowKey=? AND Reason LIKE 'period drops:%'", (k,)).fetchone())
                    if r:
                        bad.append(f'{item} still a period drop')
                    base_rows = c.execute('SELECT count(*) FROM LootTemplate WHERE lower(TemplateName)=lower(?) AND '
                                          'lower(ItemTemplateID)=lower(?)', (mob, item)).fetchone()[0]
                    if base_rows > r:
                        infos.append(f'{e.label()} {item} still drops from a base-world loot row of {mob!r} '
                                     '(not a drops165 row: needs a "remove" entry on LootTemplate)')
                if 'drop_count' in d:
                    r = c.execute('SELECT DropCount FROM MobXLootTemplate WHERE lower(MobName)=lower(?) AND lower(LootTemplateName)=lower(?)',
                                  (mob, mob)).fetchone()
                    if r is None or (r[0] or 0) < d['drop_count']:
                        bad.append(f'drop cap {r[0] if r else None} < {d["drop_count"]}')
                if bad:
                    problems.append(f'{e.label()} {mob!r}: ' + '; '.join(bad))
                else:
                    ok += 1
            elif e.kind == 'remove':
                n = c.execute(f'SELECT count(*) FROM {Q.qi(d["table"])} WHERE {d["where"]}').fetchone()[0]
                if n:
                    problems.append(f'{e.label()} {n} rows still match')
                else:
                    ok += 1
            elif e.kind == 'quest':
                f = d.get('file', 'classic-quests.json')
                doc = json.load(open(os.path.join(out, 'server', f), encoding='utf-8'))
                for op, path, value in Q.expand_quest(e):
                    cur = Q.jget(doc, path)
                    good_now = {'set': Q.same(cur, value), 'delete': cur is Q.MISSING,
                                'add_unique': isinstance(cur, list) and value in cur,
                                'remove_value': not (isinstance(cur, list) and value in cur)}[op]
                    if not good_now:
                        problems.append(f'{e.label()} {path} does not hold the {op} result')
                if f == 'classic-quests.json' and d['path'][0] == 'Quests' and len(d['path']) > 1:
                    dq_ids.add(int(d['path'][1]))
                ok += 1
        except (sqlite3.Error, KeyError, ValueError) as ex:
            problems.append(f'{e.label()} check failed: {ex}')

    # our rows hit by remove rules (archived and re-added on every refresh)
    rm = rules['remove']
    for name in own_files:
        doc = json.load(open(os.path.join(folder, name), encoding='utf-8'))
        t, key = doc['table'], doc['key']
        keys = [str(r[key]) for r in doc['rows']]
        if not keys:
            continue
        marks = ','.join('?' * len(keys))
        conds = [(w['where'], (), w['reason']) for w in rm.get('where') or [] if w['table'] == t]
        if t == 'Mob':
            conds += [('ClassType = ?', (k,), v) for k, v in rm['mob_classes'].items()]
            conds += [('PackageID = ?', (k,), v) for k, v in rm['mob_packages'].items()]
            conds += [("Name = ? AND COALESCE(PackageID,'') <> ?", (k, rm['mob_names_keep_package']), v) for k, v in rm['mob_names'].items()]
            conds += [('Guild = ?', (k,), v) for k, v in rm['mob_guilds'].items()]
        for where, args, reason in conds:
            try:
                hits = c.execute(f'SELECT {Q.qi(key)} FROM {Q.qi(t)} WHERE ({where}) AND {Q.qi(key)} IN ({marks})',
                                 tuple(args) + tuple(keys)).fetchall()
            except sqlite3.Error:
                continue
            for (k,) in hits:
                problems.append(f'{name} row {k} matches the remove rule "{reason[:90]}": it is archived and re-added on every '
                                'refresh - fix the row or narrow the rule')

    # the DataQuests the fragments touch
    item_ids = {r[0].lower() for r in c.execute('SELECT Id_nb FROM ItemTemplate') if r[0]}
    for qid in sorted(dq_ids):
        cur = c.execute('SELECT * FROM DataQuest WHERE ID = ?', (qid,))
        got = cur.fetchone()
        if got is None:
            infos.append(f'DataQuest {qid}: not in the built world')
            continue
        q = dict(zip([x[0] for x in cur.description], got))
        who = f'DataQuest {qid} "{q.get("Name")}"'
        if q.get('StartName') and q.get('StartType') not in (None,):
            if not c.execute('SELECT 1 FROM Mob WHERE lower(Name)=lower(?) AND (Region=? OR ?=0)',
                             (q['StartName'], q.get('StartRegionID') or 0, q.get('StartRegionID') or 0)).fetchone():
                problems.append(f'{who}: start NPC {q["StartName"]!r} is not in region {q.get("StartRegionID")}')
        for part in (q.get('TargetName') or '').split('|'):
            if not part.strip():
                continue
            name, _, region = part.partition(';')
            region = int(region) if region.strip().isdigit() else 0
            if name.strip() and not c.execute('SELECT 1 FROM Mob WHERE lower(Name)=lower(?) AND (Region=? OR ?=0)',
                                              (name.strip(), region, region)).fetchone():
                infos.append(f'{who}: target {name.strip()!r} has no spawn in region {region} (fine for event spawns in classic-quests.json)')
        for item in Q.dq_items(q):
            if item.lower() not in item_ids:
                problems.append(f'{who}: item {item!r} has no ItemTemplate')

    if a.quick_check:
        print('   PRAGMA quick_check ...')
        r = c.execute('PRAGMA quick_check').fetchone()[0]
        (infos if r == 'ok' else problems).append(f'quick_check: {r}')
    c.close()
    t4 = time.time()

    for title, items in (('INFO', infos), ('PROBLEMS', problems)):
        if items:
            print(f'\n{title} ({len(items)}):')
            for line in items[:300]:
                print('  ' + line)
    print(f'\n{ok} of {len(good)} entries verified, {len(problems)} problems.')
    print(f'SCRATCH WORLD: {target}')
    print(f'  content used: {folder}  (server json: {os.path.join(out, "server")})')
    print(f'  timings: merge {t1 - t0:.0f} s, copy {t2 - t1:.0f} s, apply {t3 - t2:.0f} s, checks {t4 - t3:.0f} s, total {t4 - t0:.0f} s')
    sys.exit(1 if problems else 0)


if __name__ == '__main__':
    main()
