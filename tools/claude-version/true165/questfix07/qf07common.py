"""Shared code of the 0.7 quest-fix pipeline (questfix07): fragment loading, validation, the merge into the content
files, and a Python port of the launcher's True 1.65 content refresh (TrueMode.Refresh) for scratch worlds.

Used by build_questfix07_165.py (merge) and verify_questfix07.py (scratch world). See FIX-GUIDE-07.md.

Never opens a game database for writing except the scratch world copy verify_questfix07.py makes.
"""
import copy
import datetime
import glob
import json
import os
import pathlib
import re
import sqlite3
import uuid

HERE = os.path.dirname(os.path.abspath(__file__))
TRUE165_TOOLS = os.path.dirname(HERE)                                   # tools\claude-version\true165
CV = os.path.normpath(os.path.join(TRUE165_TOOLS, '..', '..', '..'))    # new class test - CLAUDE VERSION
RUNTIME_CONTENT = os.path.join(CV, 'runtime', 'true165')
SERVER_DIR = os.path.join(CV, 'runtime', 'server')
LIVE_DB = os.path.join(CV, 'runtime', 'data', 'opendaoc-165.sqlite3.db')
V06_DB = os.path.normpath(os.path.join(CV, '..', 'Claude Takeover GitHub', 'release-true165-v0.6-server', 'runtime', 'data',
                                       'opendaoc-165.sqlite3.db'))
REAL_LEDGER = os.path.join(HERE, '_state', 'ledger.json')

TAG = 'True 1.65 0.7 questfix07'          # reason prefix of every rules entry this pipeline writes (no other tag is a prefix)
OWN_PREFIX = 'add-questfix07-'            # its own add-files: add-questfix07-<table>.json
FIXERS = ['part%02d' % i for i in range(1, 11)] + ['patrols', 'integrator']
FIXER_INDEX = {name: i + 1 for i, name in enumerate(FIXERS)}            # part01=1 .. part10=10, patrols=11, integrator=12
SERVER_FILES = ('classic-quests.json', 'classic-quest-guides.json')     # quest-entry targets (runtime\server)
KINDS = ('update', 'remove', 'row', 'add_edit', 'drop', 'quest')
FINDING_RE = re.compile(r'^[A-Za-z0-9][A-Za-z0-9._#:/-]{0,47}$')
MISSING = {'$missing': True}              # ledger form of "the key was not there"

# GUID-keyed tables a fixer may add rows to; the key must be 0007<fixer 2 digits><table code>-0000-4000-8000-<12 digits>.
TABLE_CODES = {'Mob': 1, 'NpcTemplate': 2, 'LootTemplate': 3, 'MobXLootTemplate': 4, 'Path': 5, 'PathPoints': 6,
               'MerchantItem': 7, 'LootOTD': 8, 'NpcEquipment': 9, 'WorldObject': 10, 'Spell': 11, 'LineXSpell': 12,
               'Door': 13}
# Tables keyed by something else: ItemTemplate (Id_nb, a name: unique, period id first) and DataQuest (ID, a range).
OTHER_ROW_TABLES = ('ItemTemplate', 'DataQuest')
# Integer id columns with a range per fixer: (base, width); the fixer's range is base + (index-1)*width .. +width-1.
INT_RANGES = {('DataQuest', 'ID'): (27000, 100), ('NpcTemplate', 'TemplateId'): (970701000, 1000),
              ('Spell', 'SpellID'): (970100, 100)}
DATAQUEST_ITEM_COLUMNS = ('StepItemTemplates', 'CollectItemTemplate', 'FinalRewardItemTemplates',
                          'OptionalRewardItemTemplates')


def guid_pattern(fixer, table):
    return re.compile(r'^0007%02d%02d-0000-4000-8000-\d{12}$' % (FIXER_INDEX[fixer], TABLE_CODES[table]))


def guid_example(fixer, table, n=1):
    return '0007%02d%02d-0000-4000-8000-%012d' % (FIXER_INDEX[fixer], TABLE_CODES[table], n)


def int_range(table, col, fixer):
    base, width = INT_RANGES[(table, col)]
    lo = base + (FIXER_INDEX[fixer] - 1) * width
    return lo, lo + width - 1


def own_file_name(table):
    return f'{OWN_PREFIX}{table.lower()}.json'


def own_reason(table):
    # Constant per table: the launcher removes rows an add-file stops listing by (table, reason).
    return (f'{TAG}: {table} rows for the 0.7 quest audit fixes (written by questfix07/build_questfix07_165.py '
            'from the fixer fragments)')


# ---------------------------------------------------------------- JSON files with their own formatting

class JFile:
    """A JSON file read with its formatting remembered, so a rewrite only changes what changed."""

    def __init__(self, path):
        self.path = path
        raw = open(path, 'rb').read()
        self.raw = raw
        text = raw.decode('utf-8')
        self.bom = text.startswith('﻿')
        text = text.lstrip('﻿')
        self.data = json.loads(text)
        self.style = detect_style(text, self.data)

    def dumps(self, data=None):
        return dump_json(self.data if data is None else data, self.style, self.bom)


def detect_style(text, data):
    nl = '\r\n' if '\r\n' in text[:4096] else '\n'
    tail = nl if text.endswith(nl) else ''
    m = re.match(r'[\[{]\r?\n([ \t]*)\S', text)
    guesses = []
    if m:
        ws = m.group(1)
        guesses.append('\t' if ws.startswith('\t') else len(ws))
    guesses += [1, 0, 2, 4, None]
    ascii_first = all(ord(ch) < 128 for ch in text)
    for ea in ((True, False) if ascii_first else (False, True)):
        for ind in dict.fromkeys(guesses):
            t = json.dumps(data, ensure_ascii=ea, indent=ind)
            if nl != '\n':
                t = t.replace('\n', nl)
            if t + tail == text:
                return {'ensure_ascii': ea, 'indent': ind, 'nl': nl, 'tail': tail, 'exact': True}
    return {'ensure_ascii': ascii_first, 'indent': guesses[0], 'nl': nl, 'tail': tail, 'exact': False}


def dump_json(data, style, bom=False):
    t = json.dumps(data, ensure_ascii=style['ensure_ascii'], indent=style['indent'])
    if style['nl'] != '\n':
        t = t.replace('\n', style['nl'])
    t += style['tail']
    return (('﻿' if bom else '') + t).encode('utf-8')


def write_atomic(path, data_bytes):
    if os.path.exists(path) and open(path, 'rb').read() == data_bytes:
        return False
    tmp = path + '.qf07tmp'
    with open(tmp, 'wb') as f:
        f.write(data_bytes)
    os.replace(tmp, path)
    return True


# ---------------------------------------------------------------- SQL helpers

def sql_val(v):
    if v is None:
        return 'NULL'
    if isinstance(v, bool):
        return '1' if v else '0'
    if isinstance(v, (int, float)):
        return repr(v)
    if isinstance(v, str):
        return "'" + v.replace("'", "''") + "'"
    raise ValueError(f'value {v!r} is not a SQL scalar')


def qi(name):
    return '"' + name.replace('"', '""') + '"'


def split_top(text, sep_char=None, keyword=None):
    """Positions of a top-level separator character (outside quotes and brackets) or of a top-level keyword."""
    out, depth, i, n = [], 0, 0, len(text)
    while i < n:
        ch = text[i]
        if ch in "'\"`[":
            close = ']' if ch == '[' else ch
            i += 1
            while i < n:
                if text[i] == close:
                    if close != ']' and i + 1 < n and text[i + 1] == close:
                        i += 2
                        continue
                    break
                i += 1
        elif ch == '(':
            depth += 1
        elif ch == ')':
            depth -= 1
        elif depth == 0:
            if sep_char is not None and ch == sep_char:
                out.append(i)
            elif keyword is not None and text[i:i + len(keyword)].upper() == keyword and \
                    (i == 0 or not (text[i - 1].isalnum() or text[i - 1] == '_')) and \
                    (i + len(keyword) >= n or not (text[i + len(keyword)].isalnum() or text[i + len(keyword)] == '_')):
                out.append(i)
        i += 1
    return out


def parse_update(sql):
    """'UPDATE t SET a = 1, b = 'x' WHERE cond' -> (table, {col: expr}, where). Raises ValueError otherwise."""
    if split_top(sql, sep_char=';'):
        if sql.rstrip().endswith(';') and len(split_top(sql, sep_char=';')) == 1:
            sql = sql.rstrip()[:-1]
        else:
            raise ValueError('one statement only (no ";")')
    m = re.match(r'\s*UPDATE\s+(?:"([^"]+)"|`([^`]+)`|\[([^\]]+)\]|(\w+))\s+SET\s+', sql, re.I | re.S)
    if not m:
        raise ValueError('must be "UPDATE <table> SET ... WHERE ..."')
    table = next(g for g in m.groups() if g)
    rest = sql[m.end():]
    wheres = split_top(rest, keyword='WHERE')
    if not wheres:
        raise ValueError('an UPDATE needs a WHERE (no table-wide updates from quest fixes)')
    set_part, where = rest[:wheres[0]], rest[wheres[0] + 5:].strip()
    cols = {}
    start = 0
    for pos in split_top(set_part, sep_char=',') + [len(set_part)]:
        piece = set_part[start:pos].strip()
        start = pos + 1
        eq = split_top(piece, sep_char='=')
        if not eq:
            raise ValueError(f'cannot read the assignment {piece!r}')
        col = piece[:eq[0]].strip().strip('"`[]')
        cols[col] = piece[eq[0] + 1:].strip()
    return table, cols, where


# ---------------------------------------------------------------- JSON paths (quest entries)

def jget(doc, path):
    cur = doc
    for k in path:
        if isinstance(cur, dict):
            if k not in cur:
                return MISSING
            cur = cur[k]
        elif isinstance(cur, list):
            if not isinstance(k, int) or not 0 <= k < len(cur):
                return MISSING
            cur = cur[k]
        else:
            return MISSING
    return cur


def jset(doc, path, value, create=False):
    cur = doc
    for i, k in enumerate(path[:-1]):
        nxt = jget(cur, [k])
        if nxt is MISSING or nxt is None:
            if not create:
                raise KeyError(f'path {path[:i + 1]} does not exist (set "create": true to make it)')
            nxt = [] if isinstance(path[i + 1], int) else {}
            _assign(cur, k, nxt, create)
        cur = nxt
    _assign(cur, path[-1], copy.deepcopy(value), create)


def _assign(cur, k, value, create):
    if isinstance(cur, dict):
        cur[k] = value
    elif isinstance(cur, list) and isinstance(k, int):
        if k < len(cur):
            cur[k] = value
        elif create or k == len(cur):
            cur.extend([None] * (k - len(cur)))
            cur.append(value)
        else:
            raise KeyError(f'list index {k} past the end ({len(cur)})')
    else:
        raise KeyError(f'cannot set {k!r} inside a {type(cur).__name__}')


def jdel(doc, path):
    parent = jget(doc, path[:-1])
    if isinstance(parent, dict) and path[-1] in parent:
        del parent[path[-1]]
    elif isinstance(parent, list):
        raise KeyError('list items are not deleted (it would renumber the steps); set them to null instead')


def same(a, b):
    return json.dumps(a, sort_keys=True) == json.dumps(b, sort_keys=True)


# classic-quests.json / classic-quest-guides.json schema (ClassicQuests.cs / QuestGuide.cs). 'X?' = may be null.
QSCHEMA = {
    'classic-quests.json': {'Quests': ('map', 'QuestInfo'), 'QuestMonsterIds': ('list', 'str'),
                            'Chat': ('map', ('map', 'str')), 'ClassRewards': ('map', ('map', 'str')),
                            'Fellows': ('map', 'str'), 'HourWindows': ('map', 'HourWindow')},
    'classic-quest-guides.json': {'_about': 'str', 'Guides': ('map', 'Guide')},
    'QuestInfo': {'Steps': ('list', 'StepInfo?'), 'Races': ('list', 'str'), 'XpShares': ('list', 'num'),
                  'OnlyDuring': ('list', 'str'), 'ExcludedBy': ('list', 'str'), 'CollectRewards': 'CollectRewards',
                  'SideTurnIns': ('list', 'SideTurnIn')},
    # an optional hand-in while the quest runs (ClassicQuests.SideTurnIn) and an HourWindowSpawn's game hours
    'SideTurnIn': {'Item': 'str', 'Target': 'str', 'Reward': 'str', 'FromStep': 'int', 'ToStep': 'int', 'DropFrom': 'str',
                   'DropChance': 'int', 'Thanks': 'str'},
    'HourWindow': {'Start': 'int', 'End': 'int'},
    # a collect task's random hand-in reward (ClassicQuests.CollectRewards, quest audit P01-D)
    'CollectRewards': {'XpWeight': 'int', 'MoneyWeight': 'int', 'MoneyMin': 'int', 'MoneyMax': 'int', 'ItemWeight': 'int',
                       'Items': ('list', 'str'), 'HostileWeight': 'int', 'HostileName': 'str', 'HostileTemplateId': 'int',
                       'HostileLevel': 'int', 'HostileModel': 'int'},
    'StepInfo': {'Custom': 'CustomStep', 'Marker': 'Point', 'Spawn': 'EventSpawn', 'NeedsItem': 'str', 'Issuer': 'str',
                 'Grant': ('list', 'str'), 'GrantFrom': 'str', 'Greet': 'str', 'Wearing': ('list', 'str'),
                 'OwnSpawnOnly': 'bool'},
    'Point': {'Region': 'int', 'X': 'int', 'Y': 'int', 'Z': 'int'},
    'EventSpawn': {'Name': 'str', 'TemplateId': 'int', 'Level': 'int', 'Model': 'int', 'Region': 'int', 'X': 'int',
                   'Y': 'int', 'Z': 'int', 'TriggerRadius': 'int', 'Count': 'int', 'DespawnSeconds': 'int',
                   'Peaceful': 'bool', 'PathId': 'str', 'Escort': ('list', 'EscortSpawn')},
    'EscortSpawn': {'Name': 'str', 'TemplateId': 'int', 'Level': 'int', 'Model': 'int', 'Count': 'int', 'Peaceful': 'bool'},
    'CustomStep': {'Kind': 'str', 'Item': 'str', 'Consume': 'bool', 'Target': 'str', 'Quest': 'str', 'Region': 'int',
                   'X': 'int', 'Y': 'int', 'Z': 'int', 'Radius': 'int', 'Count': 'int', 'Give': 'bool',
                   'Trades': ('map', 'str'), 'Choices': ('map', 'str'), 'Drops': ('list', 'StepDrop'), 'Words': 'str',
                   'NightOnly': 'bool'},
    'StepDrop': {'Mob': 'str', 'Item': 'str', 'Chance': 'int'},
    'Guide': {'Name': 'str', 'Realm': 'str', 'Source': 'str', 'Updated': 'str', 'Level': 'int', 'Lines': ('list', 'str')},
}
CUSTOM_KINDS = {'use_item', 'travel', 'has_item', 'interact', 'quest', 'group', 'die_to', 'trade', 'choose', 'say',
                'whisper'}


def schema_at(file, path):
    """The schema type at a JSON path, or raise ValueError naming the unknown part."""
    t = file
    for i, k in enumerate(path):
        if t == file or isinstance(t, str) and t.rstrip('?') in QSCHEMA:
            props = QSCHEMA[t.rstrip('?')]
            if not isinstance(k, str) or k not in props:
                near = [p for p in props if isinstance(k, str) and p.lower() == k.lower()]
                raise ValueError(f'unknown property {k!r} at {path[:i]}' + (f' (did you mean {near[0]!r}?)' if near else
                                 f'; known: {", ".join(props)}'))
            t = props[k]
        elif isinstance(t, tuple) and t[0] == 'map':
            if not isinstance(k, str):
                raise ValueError(f'map keys are strings (quest ids too: "20213"), got {k!r} at {path[:i]}')
            t = t[1]
        elif isinstance(t, tuple) and t[0] == 'list':
            if not isinstance(k, int):
                raise ValueError(f'list index must be an integer, got {k!r} at {path[:i]}')
            t = t[1]
        else:
            raise ValueError(f'{path[:i]} is a {t} value; nothing below it')
    return t


def schema_check(t, value, where, problems):
    if isinstance(t, str) and t.endswith('?'):
        if value is None:
            return
        t = t[:-1]
    if t == 'str':
        if not isinstance(value, str):
            problems.append(f'{where}: expected text, got {value!r}')
    elif t == 'int':
        if not isinstance(value, int) or isinstance(value, bool):
            problems.append(f'{where}: expected a whole number, got {value!r}')
    elif t == 'num':
        if not isinstance(value, (int, float)) or isinstance(value, bool):
            problems.append(f'{where}: expected a number, got {value!r}')
    elif t == 'bool':
        if not isinstance(value, bool):
            problems.append(f'{where}: expected true/false, got {value!r}')
    elif isinstance(t, tuple) and t[0] == 'map':
        if not isinstance(value, dict):
            problems.append(f'{where}: expected an object, got {type(value).__name__}')
            return
        for k, v in value.items():
            schema_check(t[1], v, f'{where}.{k}', problems)
    elif isinstance(t, tuple) and t[0] == 'list':
        if not isinstance(value, list):
            problems.append(f'{where}: expected a list, got {type(value).__name__}')
            return
        for i, v in enumerate(value):
            schema_check(t[1], v, f'{where}[{i}]', problems)
    elif t in QSCHEMA:
        if not isinstance(value, dict):
            problems.append(f'{where}: expected a {t} object, got {type(value).__name__}')
            return
        for k, v in value.items():
            if k not in QSCHEMA[t]:
                near = [p for p in QSCHEMA[t] if p.lower() == k.lower()]
                problems.append(f'{where}: unknown {t} property {k!r}' + (f' (did you mean {near[0]!r}?)' if near else ''))
            else:
                schema_check(QSCHEMA[t][k], v, f'{where}.{k}', problems)
        if t == 'CustomStep' and isinstance(value.get('Kind'), str) and value['Kind'] not in CUSTOM_KINDS:
            problems.append(f'{where}: Custom Kind {value["Kind"]!r} is not one ClassicQuests knows ({", ".join(sorted(CUSTOM_KINDS))})')


def items_in_quest_value(t, value, out):
    """Collect item ids named inside a classic-quests value (NeedsItem, Grant, Custom.Item/Trades/Choices, Drops)."""
    if value is None:
        return
    if isinstance(t, str):
        t = t.rstrip('?')
    if t == 'StepInfo' and isinstance(value, dict):
        if isinstance(value.get('NeedsItem'), str):
            out.add(value['NeedsItem'])
        for g in value.get('Grant') or []:
            if isinstance(g, str):
                out.add(g)
        items_in_quest_value('CustomStep', value.get('Custom'), out)
    elif t == 'CustomStep' and isinstance(value, dict):
        if isinstance(value.get('Item'), str) and value.get('Kind') not in ('quest',):
            out.add(value['Item'])
        for k, v in (value.get('Trades') or {}).items():
            out.add(k)
            if v:
                out.add(v)
        for v in (value.get('Choices') or {}).values():
            out.add(v)
        for d in value.get('Drops') or []:
            if isinstance(d, dict) and isinstance(d.get('Item'), str):
                out.add(d['Item'])
    elif t == 'StepDrop' and isinstance(value, dict) and isinstance(value.get('Item'), str):
        out.add(value['Item'])
    elif t == 'QuestInfo' and isinstance(value, dict):
        for s in value.get('Steps') or []:
            items_in_quest_value('StepInfo', s, out)
    elif isinstance(t, tuple) and t[0] == 'list' and isinstance(value, list):
        for v in value:
            items_in_quest_value(t[1], v, out)
    elif t in ('NeedsItem',):
        out.add(value)


# ---------------------------------------------------------------- fragments

class Entry:
    def __init__(self, fixer, file, index, data):
        self.fixer, self.file, self.index, self.d = fixer, file, index, data
        self.kind = data.get('kind')
        self.finding = data.get('finding')

    def label(self):
        return f'[{self.fixer}/{os.path.basename(self.file)} #{self.index} {self.finding or "?"} {self.kind or "?"}]'

    def reason(self):
        return f'{TAG} {self.finding} [{self.fixer}]: {self.d.get("why", "").strip()}'


class Report:
    def __init__(self):
        self.errors, self.warnings, self.notes = [], [], []

    def err(self, who, msg):
        self.errors.append(f'{who.label() if isinstance(who, Entry) else who} {msg}')

    def warn(self, who, msg):
        self.warnings.append(f'{who.label() if isinstance(who, Entry) else who} {msg}')

    def note(self, msg):
        self.notes.append(msg)

    def print(self, limit=400):
        for title, items in (('NOTES', self.notes), ('WARNINGS', self.warnings), ('ERRORS', self.errors)):
            if items:
                print(f'\n{title} ({len(items)}):')
                for line in items[:limit]:
                    print('  ' + line)
                if len(items) > limit:
                    print(f'  ... and {len(items) - limit} more')


def load_fragments(root, rep):
    """Every questfix07/<fixer>/*.json fragment, in fixer order then file name order then entry order."""
    entries = []
    if not os.path.isdir(root):
        rep.err('fragments', f'{root} is not a folder')
        return entries
    for name in sorted(os.listdir(root)):
        full = os.path.join(root, name)
        if not os.path.isdir(full) or name.startswith(('_', '.')):
            continue
        if name not in FIXER_INDEX:
            rep.err(name, f'unknown fixer folder (allowed: {", ".join(FIXERS)}; folders starting with "_" are ignored)')
    for fixer in FIXERS:
        folder = os.path.join(root, fixer)
        if not os.path.isdir(folder):
            continue
        for path in sorted(glob.glob(os.path.join(folder, '*.json'))):
            try:
                doc = json.loads(open(path, 'rb').read().decode('utf-8').lstrip('﻿'))
            except Exception as ex:          # noqa: BLE001 - report every bad file, keep going
                rep.err(f'[{fixer}/{os.path.basename(path)}]', f'not valid JSON: {ex}')
                continue
            if not isinstance(doc, dict) or not isinstance(doc.get('entries'), list):
                rep.err(f'[{fixer}/{os.path.basename(path)}]', 'a fragment is {"fixer": "...", "entries": [...]}')
                continue
            if doc.get('fixer') != fixer:
                rep.err(f'[{fixer}/{os.path.basename(path)}]', f'"fixer" is {doc.get("fixer")!r}, must be {fixer!r} (its folder)')
                continue
            extra = set(doc) - {'fixer', 'entries', 'about'}
            if extra:
                rep.err(f'[{fixer}/{os.path.basename(path)}]', f'unknown top-level keys {sorted(extra)}')
            for i, e in enumerate(doc['entries']):
                if not isinstance(e, dict):
                    rep.err(f'[{fixer}/{os.path.basename(path)} #{i}]', 'an entry is a JSON object')
                    continue
                entries.append(Entry(fixer, path, i, e))
    return entries


ALLOWED_KEYS = {
    'update': {'table', 'set', 'where', 'where_sql', 'sql'},
    'remove': {'table', 'where'},
    'row': {'table', 'row', 'keep_key'},
    'add_edit': {'file', 'id', 'set', 'delete'},
    'drop': {'mob', 'add', 'remove', 'drop_count', 'replace'},
    'quest': {'file', 'op', 'path', 'value', 'create'},
}
COMMON_KEYS = {'kind', 'finding', 'why', 'source', 'note'}


def check_shape(e, rep):
    d = e.d
    if e.kind not in KINDS:
        rep.err(e, f'"kind" must be one of {", ".join(KINDS)}')
        return False
    if not isinstance(e.finding, str) or not FINDING_RE.match(e.finding):
        rep.err(e, '"finding" is required: the audit finding id, e.g. "P01-A2" (letters, digits, . _ # : / -)')
        return False
    if not isinstance(d.get('why'), str) or len(d['why'].strip()) < 8:
        rep.err(e, '"why" is required: one sentence with the evidence (it becomes the rules reason)')
        return False
    extra = set(d) - COMMON_KEYS - ALLOWED_KEYS[e.kind]
    if extra:
        rep.err(e, f'unknown keys {sorted(extra)} for kind {e.kind!r} (allowed: {sorted(ALLOWED_KEYS[e.kind] | COMMON_KEYS)})')
        return False
    k = e.kind
    if k == 'update':
        if 'sql' in d:
            if set(d) & {'table', 'set', 'where', 'where_sql'}:
                rep.err(e, 'give either "sql" or "table"+"set"+"where"/"where_sql", not both')
                return False
            if not isinstance(d['sql'], str):
                rep.err(e, '"sql" is text')
                return False
        else:
            if not isinstance(d.get('table'), str) or not isinstance(d.get('set'), dict) or not d['set']:
                rep.err(e, 'needs "table" and a non-empty "set" object')
                return False
            if ('where' in d) == ('where_sql' in d):
                rep.err(e, 'needs exactly one of "where" (object: column -> value) or "where_sql" (text)')
                return False
            if 'where' in d and (not isinstance(d['where'], dict) or not d['where']):
                rep.err(e, '"where" is a non-empty object (column -> value)')
                return False
            if 'where_sql' in d and not isinstance(d['where_sql'], str):
                rep.err(e, '"where_sql" is text')
                return False
            for v in list(d['set'].values()) + list((d.get('where') or {}).values()):
                if isinstance(v, (dict, list)):
                    rep.err(e, f'values are scalars, got {v!r}')
                    return False
    elif k == 'remove':
        if not isinstance(d.get('table'), str) or not isinstance(d.get('where'), str) or not d['where'].strip():
            rep.err(e, 'needs "table" and a "where" SQL condition (text)')
            return False
    elif k == 'row':
        if not isinstance(d.get('table'), str) or not isinstance(d.get('row'), dict) or not d['row']:
            rep.err(e, 'needs "table" and a "row" object')
            return False
    elif k == 'add_edit':
        if not isinstance(d.get('file'), str) or 'id' not in d:
            rep.err(e, 'needs "file" (an add-*.json name) and "id" (that row\'s key value)')
            return False
        if ('set' in d) == bool(d.get('delete')):
            rep.err(e, 'needs exactly one of "set" (object) or "delete": true')
            return False
        if 'set' in d and (not isinstance(d['set'], dict) or not d['set']):
            rep.err(e, '"set" is a non-empty object')
            return False
    elif k == 'drop':
        if not isinstance(d.get('mob'), str) or not d['mob'].strip():
            rep.err(e, 'needs "mob" (the monster name, as drops165.json keys it)')
            return False
        adds, rems = d.get('add', []), d.get('remove', [])
        if not isinstance(adds, list) or not all(isinstance(a, list) and len(a) == 2 and isinstance(a[0], str) and
                                                 isinstance(a[1], int) and not isinstance(a[1], bool) and 1 <= a[1] <= 100
                                                 for a in adds):
            rep.err(e, '"add" is a list of ["Item_Id_nb", chance] with a whole-number chance 1-100')
            return False
        if not isinstance(rems, list) or not all(isinstance(r, str) for r in rems):
            rep.err(e, '"remove" is a list of item ids')
            return False
        if 'drop_count' in d and (not isinstance(d['drop_count'], int) or isinstance(d['drop_count'], bool) or d['drop_count'] < 1):
            rep.err(e, '"drop_count" is a whole number >= 1')
            return False
        if not adds and not rems and 'drop_count' not in d:
            rep.err(e, 'nothing to do: give "add", "remove" and/or "drop_count"')
            return False
    elif k == 'quest':
        if d.get('file', 'classic-quests.json') not in SERVER_FILES:
            rep.err(e, f'"file" must be one of {SERVER_FILES} (default classic-quests.json)')
            return False
        if d.get('op') not in ('set', 'merge', 'delete', 'add_unique', 'remove_value'):
            rep.err(e, '"op" is set, merge, delete, add_unique or remove_value')
            return False
        p = d.get('path')
        if not isinstance(p, list) or not p or not all(isinstance(x, str) or (isinstance(x, int) and not isinstance(x, bool))
                                                      for x in p):
            rep.err(e, '"path" is a non-empty list of keys (text) and list indexes (whole numbers), e.g. '
                       '["Quests", "20213", "Steps", 2, "Marker"]')
            return False
        if d['op'] != 'delete' and 'value' not in d:
            rep.err(e, f'op {d["op"]!r} needs "value"')
            return False
        if d['op'] == 'merge' and not isinstance(d.get('value'), dict):
            rep.err(e, 'op "merge" needs an object "value"')
            return False
    return True


# ---------------------------------------------------------------- content state

class Content:
    """The content files one target holds (a content folder + a server folder), loaded lazily."""

    def __init__(self, content_dir, server_dir):
        self.content_dir, self.server_dir = content_dir, server_dir
        self.files = {}          # logical name -> JFile
        self._addindex = None

    def logical_path(self, logical):
        where, name = logical.split(':', 1)
        return os.path.join(self.content_dir if where == 'content' else self.server_dir, name)

    def get(self, logical):
        if logical not in self.files:
            self.files[logical] = JFile(self.logical_path(logical))
        return self.files[logical]

    def add_files(self):
        return sorted(f for f in os.listdir(self.content_dir) if f.startswith('add-') and f.endswith('.json'))

    def add_index(self):
        """{(table, str(key)): file} over every add-file except this pipeline's own; plus per-table metadata."""
        if self._addindex is None:
            idx, meta = {}, {}
            for f in self.add_files():
                if f.startswith(OWN_PREFIX):
                    continue
                d = self.get('content:' + f).data
                meta[f] = (d['table'], d['key'], d['reason'])
                for r in d['rows']:
                    idx.setdefault((d['table'], str(r.get(d['key']))), f)
            self._addindex = (idx, meta)
        return self._addindex


def find_row(rows, key, ident):
    for i, r in enumerate(rows):
        if str(r.get(key)) == str(ident):
            return i, r
    return None, None


# ---------------------------------------------------------------- undo (ledger) and apply

def undo(content, ledger, rep):
    """Takes this pipeline's previous edits back out of the in-memory files, newest first."""
    for logical, ops in (ledger.get('files') or {}).items():
        path = content.logical_path(logical)
        if not os.path.exists(path):
            rep.note(f'ledger: {logical} no longer exists; its {len(ops)} recorded edits are skipped')
            continue
        doc = content.get(logical).data
        lost = 0
        for op in reversed(ops):
            if not _undo_one(doc, op):
                lost += 1
        if lost:
            rep.note(f'ledger: {lost} of {len(ops)} earlier edits in {logical} were already gone (file regenerated since?)')


def _unmissing(v):
    return MISSING if isinstance(v, dict) and v.get('$missing') is True else v


def _undo_one(doc, op):
    o = op['op']
    if o in ('row_set', 'row_delete'):
        rows, key = doc['rows'], doc['key']
        i, r = find_row(rows, key, op['id'])
        if o == 'row_set':
            if r is None or not same(r.get(op['col'], MISSING), op['after']):
                return False
            before = _unmissing(op['before'])
            if before is MISSING:
                r.pop(op['col'], None)
            else:
                r[op['col']] = before
            return True
        if r is not None:
            return False
        rows.insert(min(op['index'], len(rows)), op['row'])
        return True
    if o.startswith('drop_'):
        mons = doc['monsters']
        ent = mons.get(op['mob'])
        if o == 'drop_mob_created':
            if ent is not None and not ent.get('items'):
                del mons[op['mob']]
                return True
            return False
        if ent is None:
            return False
        items = ent['items']
        pos = next((i for i, it in enumerate(items) if it[0].lower() == op['item'].lower()), None) if 'item' in op else None
        if o == 'drop_add':
            if pos is None or items[pos][1] != op['chance']:
                return False
            del items[pos]
            return True
        if o == 'drop_replace':
            if pos is None or items[pos][1] != op['after']:
                return False
            items[pos][1] = op['before']
            return True
        if o == 'drop_remove':
            if pos is not None:
                return False
            items.insert(min(op['index'], len(items)), [op['item'], op['chance']])
            return True
        if o == 'drop_count':
            if ent.get('drop_count') != op['after']:
                return False
            before = _unmissing(op['before'])
            if before is MISSING:
                ent.pop('drop_count', None)
            else:
                ent['drop_count'] = before
            return True
    if o == 'q_set':
        if not same(jget(doc, op['path']), op['after']):
            return False
        before = _unmissing(op['before'])
        if before is MISSING:
            parent = jget(doc, op['path'][:-1])
            if isinstance(parent, list) and isinstance(op['path'][-1], int):
                # The set appended a new list slot (a new quest step). Undo runs newest first, so take the slot
                # back off the end, plus any None padding the set made; never renumber earlier steps.
                k = op['path'][-1]
                if k == len(parent) - 1:
                    parent.pop()
                    while len(parent) > 1 and parent[-1] is None:
                        parent.pop()
                else:
                    parent[k] = None
            else:
                jdel(doc, op['path'])
        else:
            jset(doc, op['path'], before, create=True)
        return True
    if o == 'q_delete':
        if jget(doc, op['path']) is not MISSING:
            return False
        jset(doc, op['path'], op['before'], create=True)
        return True
    if o == 'q_add_unique':
        lst = jget(doc, op['path'])
        if not isinstance(lst, list):
            return False
        if not op['was_present'] and op['value'] in lst:
            lst.remove(op['value'])
        if op['created'] and not lst:
            jdel(doc, op['path'])
        return True
    if o == 'q_remove_value':
        lst = jget(doc, op['path'])
        if not isinstance(lst, list) or op['value'] in lst:
            return False
        lst.insert(min(op['index'], len(lst)), op['value'])
        return True
    return False


def expand_quest(e):
    """A quest entry as (op, path, value) steps; merge becomes one set per key."""
    d = e.d
    if d['op'] == 'merge':
        return [('set', list(d['path']) + [k], v) for k, v in d['value'].items()]
    return [(d['op'], list(d['path']), d.get('value'))]


def apply_entries(content, entries, rep):
    """Applies add_edit, drop and quest entries to the in-memory files. Returns the new ledger 'files' dict."""
    ops = {}
    for e in entries:
        d = e.d
        try:
            if e.kind == 'add_edit':
                logical = 'content:' + d['file']
                doc = content.get(logical).data
                rows, key = doc['rows'], doc['key']
                i, r = find_row(rows, key, d['id'])
                if r is None:
                    rep.err(e, f'{d["file"]} has no row with {key} = {d["id"]!r}')
                    continue
                if d.get('delete'):
                    ops.setdefault(logical, []).append({'op': 'row_delete', 'id': d['id'], 'index': i, 'row': copy.deepcopy(r)})
                    del rows[i]
                else:
                    for col, v in d['set'].items():
                        before = r.get(col, MISSING)
                        ops.setdefault(logical, []).append({'op': 'row_set', 'id': d['id'], 'col': col, 'before': before,
                                                            'after': v})
                        r[col] = v
            elif e.kind == 'drop':
                logical = 'content:drops165.json'
                mons = content.get(logical).data['monsters']
                mob = next((m for m in mons if m.lower() == d['mob'].lower()), None)
                if mob is None:
                    mob = d['mob']
                    mons[mob] = {'drop_count': d.get('drop_count', 1), 'items': []}
                    ops.setdefault(logical, []).append({'op': 'drop_mob_created', 'mob': mob})
                    ops[logical].append({'op': 'drop_count', 'mob': mob, 'before': MISSING, 'after': mons[mob]['drop_count']})
                elif mob != d['mob']:
                    rep.warn(e, f'drops165.json keys this monster as {mob!r}; used that')
                ent = mons[mob]
                items = ent['items']
                for rid in d.get('remove', []):
                    pos = next((i for i, it in enumerate(items) if it[0].lower() == rid.lower()), None)
                    if pos is None:
                        rep.warn(e, f'{rid} is not a drops165.json drop of {mob!r} (nothing to remove; a base-world drop needs '
                                    'a "remove" entry on LootTemplate)')
                        continue
                    ops.setdefault(logical, []).append({'op': 'drop_remove', 'mob': mob, 'item': items[pos][0],
                                                        'chance': items[pos][1], 'index': pos})
                    del items[pos]
                for item, chance in d.get('add', []):
                    pos = next((i for i, it in enumerate(items) if it[0].lower() == item.lower()), None)
                    if pos is not None:
                        if items[pos][1] == chance:
                            rep.warn(e, f'{item} already drops from {mob!r} at {chance}% (no change)')
                        elif d.get('replace'):
                            ops.setdefault(logical, []).append({'op': 'drop_replace', 'mob': mob, 'item': items[pos][0],
                                                                'before': items[pos][1], 'after': chance})
                            items[pos][1] = chance
                        else:
                            rep.err(e, f'{item} already drops from {mob!r} at {items[pos][1]}%; set "replace": true to change '
                                       f'it to {chance}%')
                        continue
                    items.append([item, chance])
                    ops.setdefault(logical, []).append({'op': 'drop_add', 'mob': mob, 'item': item, 'chance': chance})
                if 'drop_count' in d and ent.get('drop_count') != d['drop_count']:
                    ops.setdefault(logical, []).append({'op': 'drop_count', 'mob': mob, 'before': ent.get('drop_count', MISSING),
                                                        'after': d['drop_count']})
                    ent['drop_count'] = d['drop_count']
            elif e.kind == 'quest':
                logical = 'server:' + d.get('file', 'classic-quests.json')
                doc = content.get(logical).data
                for op, path, value in expand_quest(e):
                    cur = jget(doc, path)
                    if op == 'set':
                        jset(doc, path, value, create=bool(d.get('create')))
                        ops.setdefault(logical, []).append({'op': 'q_set', 'path': path, 'before': copy.deepcopy(cur),
                                                            'after': value})
                    elif op == 'delete':
                        if cur is MISSING:
                            rep.warn(e, f'{path} is not there (nothing to delete)')
                            continue
                        jdel(doc, path)
                        ops.setdefault(logical, []).append({'op': 'q_delete', 'path': path, 'before': copy.deepcopy(cur)})
                    elif op == 'add_unique':
                        created = cur is MISSING or cur is None
                        if created:
                            jset(doc, path, [], create=bool(d.get('create', True)))
                            cur = jget(doc, path)
                        if not isinstance(cur, list):
                            rep.err(e, f'{path} is not a list')
                            continue
                        was = value in cur
                        if not was:
                            cur.append(copy.deepcopy(value))
                        ops.setdefault(logical, []).append({'op': 'q_add_unique', 'path': path, 'value': value,
                                                            'was_present': was, 'created': created})
                    elif op == 'remove_value':
                        if not isinstance(cur, list) or value not in cur:
                            rep.warn(e, f'{value!r} is not in {path} (nothing to remove)')
                            continue
                        idx = cur.index(value)
                        cur.remove(value)
                        ops.setdefault(logical, []).append({'op': 'q_remove_value', 'path': path, 'value': value, 'index': idx})
        except (KeyError, ValueError, TypeError) as ex:
            rep.err(e, f'cannot apply: {ex}')
    return ops


def build_rules_entries(entries):
    """(updates, removes) for true165-rules.json, in fragment order."""
    updates, removes = [], []
    for e in entries:
        if e.kind == 'update':
            updates.append({'sql': update_sql(e), 'reason': e.reason()})
        elif e.kind == 'remove':
            removes.append({'table': e.d['table'], 'key': e.pk, 'where': e.d['where'].strip(), 'reason': e.reason()})
    return updates, removes


def update_sql(e):
    d = e.d
    if 'sql' in d:
        return d['sql'].strip().rstrip(';').strip()
    sets = ', '.join(f'{qi(c)} = {sql_val(v)}' for c, v in d['set'].items())
    if 'where' in d:
        where = ' AND '.join(f'{qi(c)} IS NULL' if v is None else f'{qi(c)} = {sql_val(v)}' for c, v in d['where'].items())
    else:
        where = d['where_sql'].strip()
    return f'UPDATE {qi(d["table"])} SET {sets} WHERE {where}'


def build_own_files(entries, schema):
    """{file name: {table, key, reason, rows}} of this pipeline's own add-files."""
    by_table = {}
    for e in entries:
        if e.kind != 'row':
            continue
        t = e.d['table']
        row = dict(e.d['row'])
        cols = {c[0] for c in schema.get(t, [])}
        if 'LastTimeRowUpdated' in cols and 'LastTimeRowUpdated' not in row:
            row['LastTimeRowUpdated'] = '2026-11-11 00:00:00'
        row['_qf07'] = f'{e.finding} {e.fixer}'
        by_table.setdefault(t, []).append(row)
    out = {}
    for t, rows in by_table.items():
        pk = pk_of(schema, t)
        out[own_file_name(t)] = {'table': t, 'key': pk, 'reason': own_reason(t), 'rows': rows}
    return out


OWN_STYLE = {'ensure_ascii': True, 'indent': 1, 'nl': '\n', 'tail': '\n', 'exact': True}


# ---------------------------------------------------------------- database checks

def open_ro(path):
    return sqlite3.connect(pathlib.Path(path).resolve().as_uri() + '?mode=ro', uri=True)


def load_schema(con):
    out = {}
    for (t,) in con.execute("SELECT name FROM sqlite_master WHERE type='table'"):
        out[t] = [(r[1], (r[2] or '').upper(), r[3], r[4], r[5]) for r in con.execute(f'PRAGMA table_info({qi(t)})')]
    return out


def pk_of(schema, table):
    pks = [c[0] for c in schema.get(table, []) if c[4]]
    return pks[0] if len(pks) == 1 else None


def table_name(schema, t):
    if t in schema:
        return t
    near = [s for s in schema if s.lower() == (t or '').lower()]
    return near


def int_type(sqltype):
    return 'INT' in sqltype


class DbFacts:
    def __init__(self, con, content):
        self.con = con
        self.schema = load_schema(con)
        self.tracked = {}
        if 'offline_true165_added' in self.schema:
            for t, k, r in con.execute('SELECT SourceTable, RowKey, Reason FROM offline_true165_added'):
                self.tracked[(t, k)] = r
        self._items = None
        self._templates = None
        self._paths = None
        self._mobnames = None
        self.content = content

    def known_items(self, extra=()):
        if self._items is None:
            s = {r[0].lower() for r in self.con.execute('SELECT Id_nb FROM ItemTemplate') if r[0]}
            idx, meta = self.content.add_index()
            s |= {k[1].lower() for k in idx if k[0] == 'ItemTemplate'}
            self._items = s
        return self._items | {x.lower() for x in extra}

    def known_templates(self, extra=()):
        if self._templates is None:
            s = {r[0] for r in self.con.execute('SELECT TemplateId FROM NpcTemplate')}
            for f, (t, key, reason) in self.content.add_index()[1].items():
                if t == 'NpcTemplate':
                    s |= {r.get('TemplateId') for r in self.content.get('content:' + f).data['rows']}
            self._templates = s
        return self._templates | set(extra)

    def known_paths(self, extra=()):
        if self._paths is None:
            s = {r[0].lower() for r in self.con.execute('SELECT PathID FROM Path') if r[0]}
            for f, (t, key, reason) in self.content.add_index()[1].items():
                if t == 'Path':
                    s |= {str(r.get('PathID', '')).lower() for r in self.content.get('content:' + f).data['rows']}
            self._paths = s
        return self._paths | {x.lower() for x in extra}

    def mob_names(self):
        if self._mobnames is None:
            s = {r[0].lower() for r in self.con.execute('SELECT DISTINCT Name FROM Mob') if r[0]}
            for f, (t, key, reason) in self.content.add_index()[1].items():
                if t == 'Mob':
                    s |= {str(r.get('Name', '')).lower() for r in self.content.get('content:' + f).data['rows']}
            self._mobnames = s
        return self._mobnames


def validate(entries, content, facts, rep):
    """Every check that needs no writing. Fills e.pk for update/remove/row entries."""
    schema = facts.schema
    con = facts.con
    idx, meta = content.add_index()
    reason_files = {}
    for f, (t, key, reason) in meta.items():
        reason_files.setdefault((t, reason), []).append(f)
    own_reasons = {own_reason(t) for t in schema}

    new_rows = {}                 # (table, key) -> entry
    new_items, new_templates, new_paths = set(), set(), set()
    cells = {}                    # (table, rowid, col) -> [(entry, expr)]
    removed_rowids = {}           # (table, rowid) -> entry

    good = [e for e in entries if check_shape(e, rep)]

    # rows first (later checks know the new items, templates, paths)
    for e in good:
        e.pk = None
        if e.kind != 'row':
            continue
        t = e.d['table']
        tn = table_name(schema, t)
        if tn != t:
            rep.err(e, f'no table {t!r}' + (f' (did you mean {tn[0]!r}?)' if tn else ''))
            continue
        if t not in TABLE_CODES and t not in OTHER_ROW_TABLES:
            rep.err(e, f'new {t} rows are not part of the questfix07 format (tables: {", ".join(list(TABLE_CODES) + list(OTHER_ROW_TABLES))}); '
                       'ask the integrator')
            continue
        pk = e.pk = pk_of(schema, t)
        row = e.d['row']
        cols = {c[0]: c for c in schema[t]}
        lower = {c.lower(): c for c in cols}
        for c, v in row.items():
            if c not in cols:
                hint = f' (the column is {lower[c.lower()]!r}; names are case-sensitive for the launcher)' if c.lower() in lower else ''
                rep.err(e, f'unknown column {t}.{c}{hint}')
            elif int_type(cols[c][1]) and v is not None and (not isinstance(v, int) and not isinstance(v, bool)):
                rep.err(e, f'{t}.{c} is an integer column, got {v!r}')
            elif isinstance(v, (dict, list)):
                rep.err(e, f'{t}.{c}: values are scalars')
        if pk not in row or row[pk] in (None, ''):
            rep.err(e, f'the row needs its key {pk}')
            continue
        key = str(row[pk])
        fixer = e.fixer
        if t in TABLE_CODES:
            if not guid_pattern(fixer, t).match(key):
                if isinstance(e.d.get('keep_key'), str) and len(e.d['keep_key']) > 8:
                    rep.warn(e, f'{t} key {key} kept outside the {fixer} range: {e.d["keep_key"]}')
                else:
                    rep.err(e, f'{t}.{pk} {key!r} is outside {fixer}\'s range: use {guid_example(fixer, t)} .. '
                               f'{guid_example(fixer, t, 999999999999)} (or "keep_key": "<why>" to restore a period row with its own key)')
        for (rt, rc), _ in INT_RANGES.items():
            if rt == t and rc in row and row[rc] is not None:
                lo, hi = int_range(rt, rc, fixer)
                if not isinstance(row[rc], int) or not lo <= row[rc] <= hi:
                    rep.err(e, f'{t}.{rc} {row[rc]!r} is outside {fixer}\'s range {lo}-{hi}')
        if (t, key) in new_rows:
            rep.err(e, f'{t} {key} is also added by {new_rows[(t, key)].label()}')
        new_rows[(t, key)] = e
        if (t, key) in idx:
            rep.err(e, f'{t} {key} is already created by {idx[(t, key)]} (change it there with an add_edit entry)')
        hit = con.execute(f'SELECT 1 FROM {qi(t)} WHERE {qi(pk)} = ?', (row[pk],)).fetchone()
        if hit is not None and facts.tracked.get((t, key)) not in own_reasons:
            rep.err(e, f'{t} {key} already exists in the world database (a base row: change it with an update entry)')
        if t == 'ItemTemplate':
            new_items.add(key)
            if not isinstance(e.d.get('source'), str) or len(e.d['source']) < 8:
                rep.err(e, 'a new ItemTemplate needs "source": the period item data its stats come from (Allakhazam/Uthgard/Prima)')
        if t == 'NpcTemplate' and isinstance(row.get('TemplateId'), int):
            new_templates.add(row['TemplateId'])
        if t == 'Path' and row.get('PathID'):
            if str(row['PathID']).lower() in facts.known_paths():
                rep.err(e, f'Path name {row["PathID"]!r} already exists; use a new one (qf07_{fixer}_<name>)')
            new_paths.add(str(row['PathID']))
        if t in ('Mob', 'NpcTemplate', 'ItemTemplate', 'DataQuest'):
            missing = [c for c in cols if c not in row and c not in ('LastTimeRowUpdated', pk)]
            if len(missing) > len(cols) // 2:
                rep.warn(e, f'only {len(row)} of {len(cols)} {t} columns given; the rest take their defaults '
                            f'(e.g. {", ".join(missing[:6])})')

    items = facts.known_items(new_items)
    templates = facts.known_templates(new_templates)
    paths = facts.known_paths(new_paths)

    def check_items(e, ids, what):
        for i in ids:
            if i and i.lower() not in items:
                rep.warn(e, f'{what} names item {i!r}, which no ItemTemplate (world or add-file) has')

    for e in good:
        d = e.d
        if e.kind == 'row':
            t, row = d['table'], d['row']
            if t == 'Mob':
                if row.get('NPCTemplateID') not in (None, 0, -1) and row['NPCTemplateID'] not in templates:
                    rep.warn(e, f'Mob NPCTemplateID {row["NPCTemplateID"]} is no known NpcTemplate')
                if row.get('PathID') and str(row['PathID']).lower() not in paths:
                    rep.warn(e, f'Mob PathID {row["PathID"]!r} is no known Path')
            elif t in ('LootTemplate', 'MerchantItem', 'LootOTD'):
                check_items(e, [row.get('ItemTemplateID')], f'{t} row')
            elif t == 'PathPoints' and row.get('PathID') and str(row['PathID']).lower() not in paths:
                rep.warn(e, f'PathPoints PathID {row["PathID"]!r} is no known Path')
            elif t == 'DataQuest':
                check_items(e, dq_items(row), 'DataQuest row')
        elif e.kind in ('update', 'remove'):
            if e.kind == 'update':
                sql = update_sql(e)
                try:
                    t, setcols, where = parse_update(sql)
                except ValueError as ex:
                    rep.err(e, f'SQL not understood: {ex}')
                    continue
            else:
                t, where, setcols = d['table'], d['where'], {}
            tn = table_name(schema, t)
            if tn != t:
                rep.err(e, f'no table {t!r}' + (f' (did you mean {tn[0]!r}?)' if tn else ''))
                continue
            e.pk = pk_of(schema, t)
            if e.kind == 'remove' and not e.pk:
                rep.err(e, f'{t} has no single key column, so the launcher cannot archive its rows')
                continue
            cols = {c[0] for c in schema[t]}
            lower = {c.lower(): c for c in cols}
            bad = [c for c in setcols if c not in cols and c.lower() not in lower]
            if bad:
                rep.err(e, f'unknown columns {bad} in {t}')
                continue
            if e.kind == 'update' and 'where' in d:
                badw = [c for c in d['where'] if c not in cols]
                if badw:
                    rep.err(e, f'unknown where columns {badw} in {t}')
                    continue
            try:
                con.execute('EXPLAIN ' + (sql if e.kind == 'update' else f'SELECT * FROM {qi(t)} WHERE {where}')).fetchall()
            except sqlite3.Error as ex:
                rep.err(e, f'SQL does not compile: {ex}')
                continue
            try:
                hits = con.execute(f'SELECT rowid, {qi(e.pk) if e.pk else "NULL"} FROM {qi(t)} WHERE {where}').fetchall()
            except sqlite3.Error as ex:
                rep.err(e, f'WHERE does not run: {ex}')
                continue
            if not hits:
                rep.warn(e, f'matches no {t} row in the checked world (removed by a rule? created by an add-file that is not '
                            'applied yet? a typo?)')
            owned = {}
            for rowid, key in hits:
                r = facts.tracked.get((t, str(key)))
                if (t, str(key)) in new_rows or r in own_reasons:
                    owned.setdefault('a questfix07 "row" entry', []).append(str(key))
                elif r is not None and not r.startswith('period drops:'):
                    for f in reason_files.get((t, r), ['an add-file (reason ' + r[:60] + '...)']):
                        owned.setdefault(f, []).append(str(key))
                elif r is not None and r.startswith('period drops:'):
                    owned.setdefault('drops165.json', []).append(str(key))
            for f, keys in owned.items():
                if f == 'drops165.json':
                    rep.err(e, f'{len(keys)} matched {t} rows come from drops165.json (rebuilt every refresh): use a "drop" entry')
                elif f.startswith('a questfix07'):
                    rep.err(e, f'{len(keys)} matched {t} rows are added by {f} (e.g. {keys[0]}): change that row entry instead')
                else:
                    rep.err(e, f'{len(keys)} matched {t} rows are created by {f} (e.g. {keys[0]}); updates run BEFORE '
                               'add-files, so this would be overwritten: use an "add_edit" entry on that file')
            if e.kind == 'update':
                for rowid, _ in hits:
                    for c, expr in setcols.items():
                        cells.setdefault((t, rowid, lower.get(c.lower(), c)), []).append((e, expr))
                if t == 'DataQuest' and 'set' in d:
                    check_items(e, dq_items(d['set']), 'DataQuest update')
                if t in ('LootTemplate', 'MerchantItem', 'LootOTD') and 'set' in d and 'ItemTemplateID' in d['set']:
                    check_items(e, [d['set']['ItemTemplateID']], f'{t} update')
            else:
                if len(hits) > 200:
                    rep.warn(e, f'removes {len(hits)} {t} rows; is the condition narrow enough?')
                for rowid, _ in hits:
                    removed_rowids[(t, rowid)] = e
        elif e.kind == 'add_edit':
            f = d['file']
            if f.startswith(OWN_PREFIX):
                rep.err(e, f'{f} is this pipeline\'s own output: change the "row" entry instead')
                continue
            if f not in meta:
                rep.err(e, f'no add-file {f!r} in {content.content_dir}')
                continue
            t, key, reason = meta[f]
            if (t, str(d['id'])) not in idx or idx[(t, str(d['id']))] != f:
                rep.err(e, f'{f} has no row with {key} = {d["id"]!r}')
                continue
            if 'set' in d:
                cols = {c[0]: c for c in schema.get(t, [])}
                for c, v in d['set'].items():
                    if c not in cols:
                        rep.err(e, f'unknown column {t}.{c}')
                    elif c == key:
                        rep.err(e, f'the key column {key} cannot be changed (delete the row and add a new one)')
                    elif int_type(cols[c][1]) and v is not None and not isinstance(v, (int, bool)):
                        rep.err(e, f'{t}.{c} is an integer column, got {v!r}')
                    elif isinstance(v, (dict, list)):
                        rep.err(e, f'{t}.{c}: values are scalars')
                if t == 'DataQuest':
                    check_items(e, dq_items(d['set']), 'DataQuest edit')
                if t in ('LootTemplate', 'MerchantItem', 'LootOTD') and 'ItemTemplateID' in d['set']:
                    check_items(e, [d['set']['ItemTemplateID']], f'{t} edit')
            gen = GENERATORS.get(f)
            if gen:
                rep.note(f'{e.label()} edits {f}, which {gen} writes: re-run build_questfix07_165.py after that generator')
        elif e.kind == 'drop':
            mob = d['mob']
            if mob.lower() not in facts.mob_names():
                rep.warn(e, f'no monster named {mob!r} in the world or the add-files: this drop would never happen')
            check_items(e, [a[0] for a in d.get('add', [])], 'drop')
        elif e.kind == 'quest':
            file = d.get('file', 'classic-quests.json')
            for op, path, value in expand_quest(e):
                try:
                    t = schema_at(file, path)
                except ValueError as ex:
                    rep.err(e, str(ex))
                    continue
                if op in ('set', 'add_unique', 'remove_value'):
                    probs = []
                    if op == 'set':
                        schema_check(t, value, '.'.join(map(str, path)), probs)
                    else:
                        if not (isinstance(t, tuple) and t[0] == 'list'):
                            probs.append(f'{path} is not a list ({op} works on lists)')
                        else:
                            schema_check(t[1], value, '.'.join(map(str, path)) + '[]', probs)
                    for p in probs:
                        rep.err(e, p)
                    found = set()
                    items_in_quest_value(t[1] if op != 'set' and isinstance(t, tuple) else t, value, found)
                    if path[-1] in ('NeedsItem', 'Item') and isinstance(value, str):
                        found.add(value)
                    if path[-1] == 'Grant':
                        found |= {v for v in (value if isinstance(value, list) else [value]) if isinstance(v, str)}
                    check_items(e, sorted(found), 'quest entry')
                if file == 'classic-quests.json' and path[0] == 'Quests' and len(path) > 1:
                    if not re.fullmatch(r'\d+', str(path[1])):
                        rep.err(e, f'quest ids are numbers written as text, got {path[1]!r}')
                    elif not dq_exists(con, idx, new_rows, int(path[1])):
                        rep.warn(e, f'no DataQuest {path[1]} in the world, the add-files or the fragments')
                touches_text = (file == 'classic-quests.json' and (path[0] == 'Chat' or 'Greet' in path or
                                (op == 'set' and isinstance(value, dict) and 'Greet' in value))) or \
                               file == 'classic-quest-guides.json'
                if touches_text and (not isinstance(d.get('source'), str) or len(d['source']) < 8):
                    rep.err(e, 'NPC speech / guide text needs "source": the recorded period line it comes from (never invent dialogue)')

    # conflicts between entries -------------------------------------------------------------
    for (t, rowid, c), lst in cells.items():
        exprs = {x[1] for x in lst}
        if len(lst) > 1:
            who = ' and '.join(x[0].label() for x in lst)
            if len(exprs) > 1:
                rep.err(lst[0][0], f'CONFLICT: {t} rowid {rowid} column {c} is set to {sorted(exprs)} by {who}')
            elif len({x[0].fixer for x in lst}) > 1:
                rep.warn(lst[0][0], f'duplicate: {t} rowid {rowid} column {c} = {lst[0][1]} set by {who}')
        if (t, rowid) in removed_rowids:
            rep.warn(lst[0][0], f'updates a {t} row that {removed_rowids[(t, rowid)].label()} removes')
    by_row = {}
    for e in good:
        if e.kind == 'add_edit':
            by_row.setdefault((e.d['file'], str(e.d['id'])), []).append(e)
    for (f, ident), lst in by_row.items():
        for i, a in enumerate(lst):
            for b in lst[:i]:
                if a.d.get('delete') or b.d.get('delete'):
                    rep.err(a, f'CONFLICT: {f} row {ident} is deleted by one of this entry and {b.label()} and edited by the other')
                    continue
                for c in set(a.d['set']) & set(b.d['set']):
                    if same(a.d['set'][c], b.d['set'][c]):
                        rep.warn(a, f'duplicate: {f} {ident} {c} also set to the same value by {b.label()}')
                    else:
                        rep.err(a, f'CONFLICT: {f} {ident} {c} = {a.d["set"][c]!r} here but {b.d["set"][c]!r} in {b.label()}')
    drop_items, drop_counts = {}, {}
    for e in good:
        if e.kind != 'drop':
            continue
        mob = e.d['mob'].lower()
        for item in [a[0] for a in e.d.get('add', [])] + e.d.get('remove', []):
            o = drop_items.get((mob, item.lower()))
            if o is not None and o is not e:
                rep.err(e, f'CONFLICT: drop {item} on {mob!r} is also changed by {o.label()}')
            drop_items[(mob, item.lower())] = e
        if 'drop_count' in e.d:
            o = drop_counts.get(mob)
            if o is not None and o.d['drop_count'] != e.d['drop_count']:
                rep.err(e, f'CONFLICT: drop_count of {mob!r} is {e.d["drop_count"]} here and {o.d["drop_count"]} in {o.label()}')
            drop_counts[mob] = e
    qpaths = []
    for e in good:
        if e.kind != 'quest':
            continue
        f = e.d.get('file', 'classic-quests.json')
        for op, path, value in expand_quest(e):
            for (of, oop, opath, ovalue, oe) in qpaths:
                if of != f or oe is e:
                    continue
                n = min(len(path), len(opath))
                if path[:n] != opath[:n]:
                    continue
                if op in ('add_unique', 'remove_value') and oop in ('add_unique', 'remove_value') and path == opath:
                    if op != oop and same(value, ovalue):
                        rep.err(e, f'CONFLICT: {path} {op} {value!r} vs {oop} in {oe.label()}')
                    continue
                if op == oop == 'set' and path == opath and same(value, ovalue):
                    rep.warn(e, f'duplicate: {path} set to the same value by {oe.label()}')
                    continue
                rep.err(e, f'CONFLICT: {f} {path} ({op}) overlaps {opath} ({oop}) in {oe.label()}')
            qpaths.append((f, op, path, value, e))
    return good


def dq_items(row):
    out = []
    for c in DATAQUEST_ITEM_COLUMNS:
        v = row.get(c)
        if isinstance(v, str):                       # "|item;count|item|..." (a step's item, ";n" = how many)
            out += [x.split(';')[0].strip() for x in v.split('|') if x.split(';')[0].strip()]
    return out


def dq_exists(con, idx, new_rows, qid):
    if ('DataQuest', str(qid)) in idx or ('DataQuest', str(qid)) in new_rows:
        return True
    return con.execute('SELECT 1 FROM DataQuest WHERE ID = ?', (qid,)).fetchone() is not None


# Which generator rewrites which add-file / content file (a fix inside it is lost when that generator runs again).
GENERATORS = {
    'add-primaquests165.json': 'build_primaquests165.py (and build_dfspawntrim165.py)',
    'add-primaquestitems165.json': 'build_primaquests165.py (prices: build_lootprices165.py)',
    'add-primaitems165.json': 'prima_batch_2026-11-07/build_prima_batch165.py (sweeps: lootprices, stacks, trashlevel)',
    'add-primaspawns165.json': 'prima_batch_2026-11-07/build_prima_batch165.py',
    'add-primatemplates165.json': 'prima_batch_2026-11-07/build_prima_batch165.py',
    'add-primaencounters165.json': 'build_encounters165.py', 'add-primaenctemplates165.json': 'build_encounters165.py',
    'add-primapaths165.json': 'build_encounters165.py', 'add-primapathpoints165.json': 'build_encounters165.py',
    'add-restoredspawns165.json': 'build_twins165.py / build_dfspawntrim165.py / build_encounters165.py / fix_offmesh_spawns165.py',
    'add-periodspawns165.json': 'fix_offmesh_spawns165.py', 'add-cornwallprima165.json': 'build_cornwall165.py',
    'add-inns165.json': 'build_inns165.py', 'add-paths165.json': 'build_inns165.py',
    'add-pathpoints165.json': 'build_inns165.py / fix_huginfell_route165.py',
    'add-routefloor165.json': 'build_routefloor165.py / refine_routefloor165.py / fix_routebridges165.py / fix_innisbridge165.py',
    'add-droptwins165.json': 'build_dropfix165.py', 'add-gems165.json': 'build_moonstone165.py',
    'add-bgspawns165.json': 'build_bgspawns165.py', 'add-bgkeeps165.json': 'build_bgkeeps165.py',
    'add-keepguards165.json': 'build_keepguards165.py', 'add-gates165.json': 'build_gates165.py',
    'add-recipes165-items.json': 'build_recipes165.py', 'add-recipes165-crafteditem.json': 'build_recipes165.py',
    'add-recipes165-craftedxitem.json': 'build_recipes165.py', 'add-goodsitems165.json': 'build_goodsitems165.py',
    'add-vendoritems165.json': 'build_vendoritems165.py / vendor_work scripts',
    'add-merchantgoods165.json': 'vendor_work/make_goods_content.py + build_merchantfix2_165.py',
    'add-accountvaults165.json': 'build_accountvaults165.py', 'add-objects165.json': 'build_objects165.py',
    'add-rules165-hasteners.json': 'build_rules165.py',
}


# ---------------------------------------------------------------- launcher port (TrueMode.Refresh), for scratch worlds

def _exec(c, sql, args=()):
    return c.execute(sql, args).rowcount


def launcher_refresh(c, folder, progress=print, only=None):
    """Python copy of TrueMode.Refresh (launcher, development-source/tools/OfflineDaoc.Launcher/TrueMode.cs): settings,
    removals, rules updates, period drops replaced, every add-*.json (update-if-ours, stale removal). `only` (quick
    mode) = dict(updates=[...], removes=[...], drop_mobs=set(lowercase names), add_files=[names]) to apply a subset."""
    now = datetime.datetime.now().strftime('%Y-%m-%d %H:%M:%S')
    rules = json.load(open(os.path.join(folder, 'true165-rules.json'), encoding='utf-8'))
    c.execute('CREATE TABLE IF NOT EXISTS offline_true165_removed (Id INTEGER PRIMARY KEY AUTOINCREMENT, SourceTable TEXT NOT NULL, '
              'RowKey TEXT, Reason TEXT NOT NULL, RowJson TEXT NOT NULL, RemovedAt TEXT NOT NULL)')
    c.execute('CREATE TABLE IF NOT EXISTS offline_true165_added (Id INTEGER PRIMARY KEY AUTOINCREMENT, SourceTable TEXT NOT NULL, '
              'RowKey TEXT NOT NULL, Reason TEXT NOT NULL, AddedAt TEXT NOT NULL)')
    c.execute('CREATE INDEX IF NOT EXISTS ix_offline_true165_added_key ON offline_true165_added(SourceTable, RowKey)')
    stats = {}
    if only is None:
        for key, value in rules['settings'].items():
            n = _exec(c, 'UPDATE ServerProperty SET Value=? WHERE lower(`Key`)=?', (value, key))
            if n == 0:
                c.execute('INSERT INTO ServerProperty (`Key`,Value,DefaultValue,Category,Description,ServerProperty_ID,LastTimeRowUpdated) '
                          "VALUES (?,?,?,'world','True 1.65 Mode setting',?,?)", (key, value, value, key, now))
        progress('  removals...')
        rm = rules['remove']
        arch = 0
        for cls, why in rm['mob_classes'].items():
            arch += _archive(c, 'Mob', 'Mob_ID', 'ClassType = ?', (cls,), why, now)
        for pkg, why in rm['mob_packages'].items():
            arch += _archive(c, 'Mob', 'Mob_ID', 'PackageID = ?', (pkg,), why, now)
        for name, why in rm['mob_names'].items():
            arch += _archive(c, 'Mob', 'Mob_ID', "Name = ? AND COALESCE(PackageID,'') <> ?", (name, rm['mob_names_keep_package']), why, now)
        for guild, why in rm['mob_guilds'].items():
            arch += _archive(c, 'Mob', 'Mob_ID', 'Guild = ?', (guild,), why, now)
        for cls, why in rm['loot_generators'].items():
            arch += _archive(c, 'LootGenerator', 'LootGenerator_ID', 'LootGeneratorClass = ?', (cls,), why, now)
        for w in rm.get('where') or []:
            arch += _archive(c, w['table'], w['key'], w['where'], (), w['reason'], now)
        stats['rows archived'] = arch
        progress(f'  {len(rules["updates"])} rules updates...')
        for u in rules['updates']:
            c.execute(u['sql'])
        progress('  period drops...')
        c.execute("DELETE FROM LootTemplate WHERE LootTemplate_ID IN (SELECT RowKey FROM offline_true165_added "
                  "WHERE SourceTable='LootTemplate' AND Reason LIKE 'period drops:%')")
        c.execute("DELETE FROM offline_true165_added WHERE SourceTable='LootTemplate' AND Reason LIKE 'period drops:%'")
        stats.update(_add_drops(c, os.path.join(folder, 'drops165.json'), now, None))
        progress('  add-files...')
        names = sorted(f for f in os.listdir(folder) if f.startswith('add-') and f.endswith('.json'))
    else:
        arch = 0
        for w in only['removes']:
            arch += _archive(c, w['table'], w['key'], w['where'], (), w['reason'], now)
        stats['rows archived'] = arch
        for u in only['updates']:
            c.execute(u['sql'])
        mobs = only['drop_mobs']
        if mobs:
            marks = ','.join('?' * len(mobs))
            # (two steps: a join here makes SQLite scan LootTemplate once per added row - minutes)
            cand = [r[0] for r in c.execute(f'SELECT LootTemplate_ID FROM LootTemplate WHERE lower(TemplateName) IN ({marks})',
                                            sorted(mobs))]
            keys = [k for k in cand if c.execute("SELECT 1 FROM offline_true165_added WHERE SourceTable='LootTemplate' AND RowKey=? "
                                                 "AND Reason LIKE 'period drops:%'", (k,)).fetchone()]
            for k in keys:
                c.execute('DELETE FROM LootTemplate WHERE LootTemplate_ID=?', (k,))
                c.execute("DELETE FROM offline_true165_added WHERE SourceTable='LootTemplate' AND RowKey=?", (k,))
            stats.update(_add_drops(c, os.path.join(folder, 'drops165.json'), now, mobs))
        names = sorted(only['add_files'])
    stats.update(_add_rows(c, folder, names, now))
    return stats


def _archive(c, table, key_col, where, args, reason, now):
    cur = c.execute(f'SELECT * FROM {qi(table)} WHERE {where}', args)
    cols = [x[0] for x in cur.description]
    rows = cur.fetchall()
    for row in rows:
        rec = dict(zip(cols, row))
        c.execute('INSERT INTO offline_true165_removed (SourceTable,RowKey,Reason,RowJson,RemovedAt) VALUES (?,?,?,?,?)',
                  (table, str(rec.get(key_col) if rec.get(key_col) is not None else ''), reason,
                   json.dumps(rec, default=str), now))
    if rows:
        c.execute(f'DELETE FROM {qi(table)} WHERE {where}', args)
    return len(rows)


def _add_drops(c, path, now, mobs):
    if not os.path.exists(path):
        return {}
    data = json.load(open(path, encoding='utf-8'))
    reason = 'period drops: ' + data.get('source', 'drops165.json')
    added = capped = 0
    for mob, entry in data['monsters'].items():
        if mobs is not None and mob.lower() not in mobs:
            continue
        have = {r[0].lower() for r in c.execute('SELECT ItemTemplateID FROM LootTemplate WHERE lower(TemplateName)=lower(?)', (mob,))
                if r[0] is not None}
        for idnb, chance in entry['items']:
            if idnb.lower() in have:
                continue
            have.add(idnb.lower())
            key = str(uuid.uuid4())
            c.execute('INSERT INTO LootTemplate (TemplateName,ItemTemplateID,Chance,Count,LastTimeRowUpdated,LootTemplate_ID) '
                      'VALUES (?,?,?,1,?,?)', (mob, idnb, int(chance), now, key))
            c.execute('INSERT INTO offline_true165_added (SourceTable,RowKey,Reason,AddedAt) VALUES (?,?,?,?)',
                      ('LootTemplate', key, reason, now))
            added += 1
        cap = int(entry['drop_count'])
        row = c.execute('SELECT MobXLootTemplate_ID, DropCount FROM MobXLootTemplate WHERE lower(MobName)=lower(?) '
                        'AND lower(LootTemplateName)=lower(?)', (mob, mob)).fetchone()
        if row is None:
            key = str(uuid.uuid4())
            c.execute('INSERT INTO MobXLootTemplate (MobName,LootTemplateName,DropCount,LastTimeRowUpdated,MobXLootTemplate_ID) '
                      'VALUES (?,?,?,?,?)', (mob, mob, cap, now, key))
            c.execute('INSERT INTO offline_true165_added (SourceTable,RowKey,Reason,AddedAt) VALUES (?,?,?,?)',
                      ('MobXLootTemplate', key, reason, now))
            capped += 1
        elif (row[1] or 0) < cap:
            c.execute('UPDATE MobXLootTemplate SET DropCount=? WHERE MobXLootTemplate_ID=?', (cap, row[0]))
            capped += 1
    return {'period drop rows added': added, 'monster drop caps set': capped}


def _to_db(v):
    if isinstance(v, bool):
        return 1 if v else 0
    return v


def _add_rows(c, folder, names, now):
    inserted = updated = stale_removed = 0
    for name in names:
        doc = json.load(open(os.path.join(folder, name), encoding='utf-8'))
        table, key, reason = doc['table'], doc['key'], doc['reason']
        cols = {r[1] for r in c.execute(f'PRAGMA table_info({qi(table)})')}
        listed = set()
        for row in doc['rows']:
            ident = row[key]
            sid = _json_tostring(ident)
            listed.add(sid)
            use = [k for k in row if k in cols]
            present = c.execute(f'SELECT 1 FROM {qi(table)} WHERE {qi(key)}=?', (sid,)).fetchone() is not None
            if present:
                ours = c.execute('SELECT 1 FROM offline_true165_added WHERE SourceTable=? AND RowKey=?', (table, sid)).fetchone()
                if ours is None:
                    continue
                c.execute(f'UPDATE {qi(table)} SET {",".join(qi(k) + "=?" for k in use)} WHERE {qi(key)}=?',
                          [_to_db(row[k]) for k in use] + [sid])
                updated += 1
                continue
            c.execute(f'INSERT INTO {qi(table)} ({",".join(qi(k) for k in use)}) VALUES ({",".join("?" * len(use))})',
                      [_to_db(row[k]) for k in use])
            c.execute('INSERT INTO offline_true165_added (SourceTable,RowKey,Reason,AddedAt) VALUES (?,?,?,?)',
                      (table, sid, reason, now))
            inserted += 1
        stale = [r[0] for r in c.execute('SELECT RowKey FROM offline_true165_added WHERE SourceTable=? AND Reason=?', (table, reason))
                 if r[0] not in listed]
        for gone in stale:
            c.execute(f'DELETE FROM {qi(table)} WHERE {qi(key)}=?', (gone,))
            c.execute('DELETE FROM offline_true165_added WHERE SourceTable=? AND RowKey=? AND Reason=?', (table, gone, reason))
            stale_removed += 1
    return {'add-file rows inserted': inserted, 'add-file rows rewritten': updated, 'add-file rows no longer listed, removed': stale_removed}


def _json_tostring(v):
    """JsonElement.ToString() as the launcher uses it for the key: numbers as written, strings as is."""
    if isinstance(v, bool):
        return 'True' if v else 'False'
    if isinstance(v, float) and v.is_integer():
        return repr(v)
    return str(v)
