"""0.7 quest-fix merge (owner 2026-11-11: fix every finding of the 10-part quest audit + the two GitHub issues for 0.7).

Reads every fixer fragment (questfix07/<fixer>/*.json, format in FIX-GUIDE-07.md), validates them against the content
files and a read-only world database, and merges them into the True 1.65 content the launcher applies:

  true165-rules.json   updates + remove.where entries tagged "True 1.65 0.7 questfix07 <finding> [<fixer>]: <why>"
                       (the block is replaced on every run; it sits after every other update)
  add-questfix07-<table>.json   new rows, one file per table (rewritten every run; a table that loses all its rows
                       keeps an empty file so the launcher removes the rows it added before)
  add-*.json           "add_edit" entries change the row inside the add-file that creates it (updates run BEFORE
                       add-files, so an update cannot change those rows)
  drops165.json        "drop" entries (items added to / removed from a monster, its drop_count)
  runtime\\server\\classic-quests.json (and classic-quest-guides.json)   "quest" entries

Rerunnable: the previous run's edits to files it does not own (add_edit, drop, quest) are recorded in a ledger and
taken back out before the fragments are applied again, so a second run writes the same bytes. Real mode writes BOTH
content copies (runtime\\true165 and tools\\claude-version\\true165; they must be identical first) and the ledger
questfix07\\_state\\ledger.json.

    python -I build_questfix07_165.py --check                 validate only, write nothing (exit 1 on errors)
    python -I build_questfix07_165.py --out <dir> [--reseed]  write into a copy: <dir>\\true165 + <dir>\\server (seeded
                                                              from the real files when missing or with --reseed)
    python -I build_questfix07_165.py                         write the real content files (integrator only)
  options: --fragments <dir> (default: this folder), --db <world db> (default: the live dev 165 world, read-only)

Never writes a game database. Re-run it after any generator that rewrites a file a fragment edits (it says which).
"""
import argparse
import datetime
import glob
import json
import os
import shutil
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.dont_write_bytecode = True
import qf07common as Q  # noqa: E402


def seed(out_dir, reseed):
    content = os.path.join(out_dir, 'true165')
    server = os.path.join(out_dir, 'server')
    if reseed:
        for p in (content, server):
            if os.path.isdir(p):
                shutil.rmtree(p)
        led = os.path.join(out_dir, 'questfix07-ledger.json')
        if os.path.exists(led):
            os.remove(led)
    led = os.path.join(out_dir, 'questfix07-ledger.json')
    if not os.path.isdir(content) and not os.path.isdir(server) and not os.path.exists(led) \
            and os.path.exists(Q.REAL_LEDGER):
        # The real files carry the last real run's edits: seed its ledger too, so the undo takes them back out
        # (and its own_files list lets this run rewrite or empty the own add-files) instead of applying on top.
        os.makedirs(out_dir, exist_ok=True)
        shutil.copy2(Q.REAL_LEDGER, led)
    if not os.path.isdir(content):
        os.makedirs(content)
        for p in glob.glob(os.path.join(Q.RUNTIME_CONTENT, '*.json')):
            shutil.copy2(p, content)
    if not os.path.isdir(server):
        os.makedirs(server)
        for name in Q.SERVER_FILES:
            shutil.copy2(os.path.join(Q.SERVER_DIR, name), server)
    return content, server


def run(check=False, out=None, reseed=False, fragments=HERE, db=None, quiet=False):
    """Returns (report, good entries, summary dict). Writes nothing when check is set or there are errors."""
    t0 = time.time()
    rep = Q.Report()
    if out:
        out = os.path.abspath(out)
        real_roots = [os.path.normcase(os.path.abspath(p)) for p in (Q.RUNTIME_CONTENT, Q.TRUE165_TOOLS, Q.SERVER_DIR, Q.CV)]
        if os.path.normcase(out) in real_roots:
            raise SystemExit(f'--out {out} is a real content folder; give a scratch folder')
        if check and not os.path.isdir(os.path.join(out, 'true165')):
            roots, server, ledger_path = [Q.RUNTIME_CONTENT], Q.SERVER_DIR, None
        else:
            content, server = seed(out, reseed and not check)
            roots, ledger_path = [content], os.path.join(out, 'questfix07-ledger.json')
    else:
        roots, server, ledger_path = [Q.RUNTIME_CONTENT, Q.TRUE165_TOOLS], Q.SERVER_DIR, Q.REAL_LEDGER
    db = db or (Q.LIVE_DB if os.path.exists(Q.LIVE_DB) else Q.V06_DB)

    entries = Q.load_fragments(fragments, rep)
    primary = Q.Content(roots[0], server)
    ledger = json.load(open(ledger_path, encoding='utf-8')) if ledger_path and os.path.exists(ledger_path) else {}
    Q.undo(primary, ledger, rep)
    con = Q.open_ro(db)
    facts = Q.DbFacts(con, primary)
    good = Q.validate(entries, primary, facts, rep)
    new_ops = Q.apply_entries(primary, [e for e in good if e.kind in ('add_edit', 'drop', 'quest')], rep)
    updates, removes = Q.build_rules_entries(good)
    rules = primary.get('content:true165-rules.json').data
    old_tagged = sum(1 for u in rules['updates'] if str(u.get('reason', '')).startswith(Q.TAG)) + \
        sum(1 for w in rules['remove']['where'] if str(w.get('reason', '')).startswith(Q.TAG))
    rules['updates'] = [u for u in rules['updates'] if not str(u.get('reason', '')).startswith(Q.TAG)] + updates
    rules['remove']['where'] = [w for w in rules['remove']['where'] if not str(w.get('reason', '')).startswith(Q.TAG)] + removes
    own = Q.build_own_files(good, facts.schema)
    con.close()

    counts = {}
    for e in good:
        counts.setdefault(e.fixer, {}).setdefault(e.kind, 0)
        counts[e.fixer][e.kind] += 1
    summary = {'entries': len(entries), 'valid': len(good), 'updates': len(updates), 'removes': len(removes),
               'own_files': {k: len(v['rows']) for k, v in own.items()}, 'by_fixer': counts, 'db': db,
               'roots': roots, 'server': server, 'written': [], 'previous_tagged_rules': old_tagged}

    if rep.errors or check:
        summary['seconds'] = round(time.time() - t0, 1)
        return rep, good, summary

    # ---- write
    dirty = {'content:true165-rules.json'} | set(ledger.get('files', {})) | set(new_ops)
    content_dirty = sorted(x for x in dirty if x.startswith('content:'))
    server_dirty = sorted(x for x in dirty if x.startswith('server:'))
    own_names = set(own) | set(ledger.get('own_files', []))
    for root in roots:
        own_names |= {os.path.basename(p) for p in glob.glob(os.path.join(root, Q.OWN_PREFIX + '*.json'))}
    if len(roots) > 1:
        for logical in content_dirty:
            name = logical.split(':', 1)[1]
            a = open(os.path.join(roots[0], name), 'rb').read()
            for other in roots[1:]:
                p = os.path.join(other, name)
                if not os.path.exists(p) or open(p, 'rb').read() != a:
                    rep.err('copies', f'{name} differs between {roots[0]} and {other}: sync the two content copies first')
        for name in own_names:
            files = [os.path.join(r, name) for r in roots if os.path.exists(os.path.join(r, name))]
            if files and len({open(p, 'rb').read() for p in files}) > 1:
                rep.err('copies', f'{name} differs between the content copies')
        if rep.errors:
            return rep, good, summary
    # a sweep (lootprices, trashlevel, stacks, itemmodels, merchantrealm) may have edited our own rows: say so
    for name, doc in own.items():
        p = os.path.join(roots[0], name)
        if os.path.exists(p):
            old = json.load(open(p, encoding='utf-8'))
            oldrows = {str(r.get(doc['key'])): r for r in old.get('rows', [])}
            for r in doc['rows']:
                o = oldrows.get(str(r[doc['key']]))
                if o:
                    diff = [c for c in set(o) | set(r) if c not in ('_qf07', 'LastTimeRowUpdated') and not Q.same(o.get(c), r.get(c))]
                    if diff:
                        rep.note(f'{name} {r[doc["key"]]}: {", ".join(sorted(diff))} differed from the fragment (a sweep or an '
                                 'old run?); the fragment value is written - put a wanted value into the fragment')
    for root in roots:
        for logical in content_dirty:
            jf = primary.get(logical)
            path = os.path.join(root, logical.split(':', 1)[1])
            if Q.write_atomic(path, jf.dumps()):
                summary['written'].append(path)
            if not jf.style['exact'] and root == roots[0]:
                rep.warn(logical, 'its formatting could not be matched exactly; the file was rewritten with indent 1')
        for name in sorted(own_names):
            doc = own.get(name)
            if doc is None:
                old = json.load(open(os.path.join(root, name), encoding='utf-8')) if os.path.exists(os.path.join(root, name)) else None
                table = old['table'] if old else None
                if table is None:
                    continue
                doc = {'table': table, 'key': old['key'], 'reason': Q.own_reason(table), 'rows': []}
            path = os.path.join(root, name)
            if Q.write_atomic(path, Q.dump_json(doc, Q.OWN_STYLE)):
                summary['written'].append(path)
    for logical in server_dirty:
        jf = primary.get(logical)
        path = primary.logical_path(logical)
        if Q.write_atomic(path, jf.dumps()):
            summary['written'].append(path)
    new_ledger = {'_about': 'questfix07 ledger: edits build_questfix07_165.py made to files it does not own, so a rerun can '
                            'take them back out first. Do not edit by hand.',
                  'tag': Q.TAG, 'written': datetime.datetime.now().strftime('%Y-%m-%d %H:%M:%S'),
                  'own_files': sorted(own_names), 'files': new_ops}
    if {k: v for k, v in ledger.items() if k != 'written'} == json.loads(json.dumps(
            {k: v for k, v in new_ledger.items() if k != 'written'})):
        new_ledger['written'] = ledger.get('written', new_ledger['written'])     # unchanged run: same bytes
    os.makedirs(os.path.dirname(ledger_path), exist_ok=True)
    Q.write_atomic(ledger_path, (json.dumps(new_ledger, indent=1, ensure_ascii=True) + '\n').encode('utf-8'))
    summary['ledger'] = ledger_path
    summary['seconds'] = round(time.time() - t0, 1)
    return rep, good, summary


def print_summary(rep, summary, check):
    rep.print()
    print('\nSUMMARY')
    print(f'  world db checked (read-only): {summary["db"]}')
    print(f'  content: {", ".join(summary["roots"])}; server json: {summary["server"]}')
    print(f'  fragment entries: {summary["entries"]} ({summary["valid"]} well-formed)')
    for fixer, kinds in summary['by_fixer'].items():
        print(f'    {fixer:10} ' + ', '.join(f'{k} {n}' for k, n in sorted(kinds.items())))
    print(f'  rules: {summary["updates"]} updates, {summary["removes"]} removes (tag "{Q.TAG}"; '
          f'{summary["previous_tagged_rules"]} tagged entries were there before)')
    for name, n in sorted(summary['own_files'].items()):
        print(f'  {name}: {n} rows')
    if rep.errors:
        print(f'\n{len(rep.errors)} ERRORS - nothing written.')
    elif check:
        print('\n--check: valid, nothing written.')
    else:
        print(f'\nwritten ({len(summary["written"])} files changed):')
        for p in summary['written']:
            print('  ' + p)
        print(f'  ledger: {summary.get("ledger")}')
    print(f'({summary.get("seconds")} s)')


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('--check', action='store_true', help='validate only; write nothing')
    ap.add_argument('--out', help='write into this scratch folder instead of the real content')
    ap.add_argument('--reseed', action='store_true', help='with --out: copy the real files in fresh first')
    ap.add_argument('--fragments', default=HERE, help='folder holding the <fixer> folders (default: questfix07)')
    ap.add_argument('--db', help='world database to check against, opened read-only (default: live dev 165 world)')
    a = ap.parse_args()
    rep, good, summary = run(check=a.check, out=a.out, reseed=a.reseed, fragments=os.path.abspath(a.fragments), db=a.db)
    print_summary(rep, summary, a.check)
    sys.exit(1 if rep.errors else 0)


if __name__ == '__main__':
    main()
