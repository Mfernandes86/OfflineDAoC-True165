# Notes from lane D-D (drop rates)

Updated 2026-11-12. Data shape: `source/contract/drops.md`. Rules with `file:line`: `source/droprules.md`.

## Decided

- **Key** = the runtime monster name, lowercased, not trimmed (the server looks loot up with `mob.Name.ToLower()`).
  Runtime name = the NpcTemplate's name when the template has ReplaceMobValues, else `Mob.Name`. When an NpcTemplate
  id has several rows the server picks one at random per spawn, so every replacing variant counts as a kind.
- **Spawned** = a `Mob` row in a region the player can enter (not in `disabled_regions`, not of a disabled
  expansion). Agreed with D-V; same rule as P-D and V-D. 124 names exist only in closed regions and are left out.
- `mobs` in drops.js has every spawned name (7,177) because coin applies to all of them; only 2,491 have a loot table.
- **chance** is the real per-kill chance (DropCount competition included), for the natural killer realm; `byRealm`
  gives the three realms when they differ. Realm-locked items are removed from the roll for other realms, so the
  other items get slightly better chances for them: that is why `byRealm` also appears on items with no realm.
- **Quest-gated loot** (28 rows, flag 2): chance while on the quest. The quest item uses up a pick either way.
- **One-time drops** are a separate list (`otd`), not a chance.
- **Quest hand-overs** (`q`) are the classic-quest script's kill drops only. The `DataQuest` table's own kill steps
  (2,853 kill steps, some with "item;chance") are quest progress and are not in drops.js. No lane covers quests.
- Random-object generation: off in True 1.65 (no generator row). Nothing to show.
- `none` uses the class a spawn runs as (`Region.cs:745-773`: NPC guild script, then the template's ClassType when
  the template replaces values, then `Mob.ClassType`), as D-V and M-D read it.

- **Script-spawned kinds** (`s: 1`): taken from `data/mobs.js`, table `scripted` (rows whose key is no open-region
  spawn name: 143). `body` there is used verbatim for coin (0 = the "None" rate, null = no figure).
  This is the one place my builder reads another lane's file; `build_all.py` already runs mobs before drops.
- **`raid`**: the kind is a scripted epic encounter; with a realm expedition of gamebots in the fight a player gets
  one in four of the items that drop and a quarter of the coin (droprules.md section 8a). Not a chance.
- No script hands out loot on a kill outside the loot tables (scan in `scratch/D-D/scan_death_scripts.py`).

## For the page agents (I-U, M-U)

- Read a chance with: `(Array.isArray(r[4]) && WG.me().realm) ? r[4][WG.me().realm - 1] : r[1]`.
- Show `WG.pct(chance)`; "about 1 in N kills" = `Math.round(1 / chance)`.
- Flags: 1 always, 2 quest only, 4 never, 8 realm-locked. `n` > 1 = a stack (arrows and the like).
- A drops key that is not in mobs.js is an NPC that M-D does not list as a monster: no link.
- `s: 1` on a kind: say "summoned by a script, encounter or quest" where a spawn map would be; `coin` can be
  missing on such a kind.
- `raid` on a kind: one line, "With a realm expedition you get one in four of the items that drop.".
- Lines worth showing once near any drop table: "Nothing drops when the monster is grey to you or to anyone who
  damaged it." and "In a group the leader's realm decides realm-locked loot."
- Coin: `coin` percent (or `[min, max]`), amount from `meta.coin.amount` with the level from mobs.js / camps.js:
  `min = 2 + Math.floor(Math.pow(L + 1, 3) / 8)`, `max = min + Math.floor(min / 2)`.

## Needs from others

- Nothing open. M-D keeps the key untrimmed ("snemel den forsumlig " ends in a space): all 4,129 kinds of mobs.js
  join. D-V's `verify_drops.py` passes on every line (chances, DropCount, pool, realm, pieces, flags, quest ids,
  kinds, one-time drops, coin, `q`, `none`, byItem).

## Open problems

- Keep guards take their in-game name from the keep, not from the `Mob` row or its template (691 of them carry the
  template name "New Mob"). They never drop anything, so no drop number is affected; their kinds are marked `none`.
- `q` lists the lowest chance when the same monster / item / quest appears on several steps with different chances
  (none differ today).
