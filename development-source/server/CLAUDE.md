> **OfflineDAoC True 1.65:** this folder is the OfflineDAoC True 1.65 server. Read the repository root `README.md` and `AGENTS.md` first: it has the project rules (period sources first, no difficulty changes, bots on player rules). These notes are the inherited OpenDAoC-Core-Bots ones; the server targets .NET 10.

# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

OpenDAoC-Core-Bots is a fork of [OpenDAoC-Core](https://github.com/OpenDAoC/OpenDAoC-Core), a Dark Age of Camelot (DAoC) server emulator built on .NET 10. This fork adds **player-controlled bot companions** that can join groups, follow players, and execute role-based AI (melee, tank, healer, caster, ranged).

## Build & Run Commands

```bash
# Build the solution
dotnet build "Dawn of Light.sln"

# Build in Release mode
dotnet build "Dawn of Light.sln" -c Release

# Run the server
dotnet run --project CoreServer

# Run all tests (NUnit)
dotnet test Tests/Tests.csproj

# Run a single test by name
dotnet test Tests/Tests.csproj --filter "FullyQualifiedName~TestClassName.TestMethodName"

# Docker (local dev with MariaDB)
docker-compose up
```

## Solution Structure

- **CoreBase/** — Shared utilities: logging (log4net), networking, threading, MPK file handling
- **CoreDatabase/** — ORM layer with `DataObject` base class, supports MySQL and SQLite. Query building via `WhereClause`
- **CoreServer/** — Entry point (`MainClass.cs`). Boots the server via `dotnet CoreServer.dll`
- **GameServer/** — All game logic (~2300 files). This is where nearly all development happens
- **Tests/** — NUnit tests split into `UnitTests/` and `IntegrationTests/`
- **Pathing/** — Detour-based pathfinding library

## Architecture

### Game Object Hierarchy

`GameObject` → `GameLiving` → `GameNPC` → specialized NPCs (merchants, guards, pets, etc.)
`GameObject` → `GameLiving` → `GamePlayer` (player characters)
`GameObject` → `GameLiving` → `GameNPC` → `GameBot` (player-controlled bots)

### ECS System

The server uses Entity-Component-System architecture under `GameServer/ECS-*/`:
- **ECS-Components/** — Data components (AttackComponent, CastingComponent, EffectListComponent, etc.)
- **ECS-Services/** — Processing services that operate on components each tick (AttackService, CastingService, MovementService, EffectService, etc.)
- **ECS-Effects/** — Effect lifecycle management (buffs, debuffs, crowd control)

Services run on `GameLoop` ticks. Use `GameLoop.GetListForTick<T>()` to get entities for processing.

### Bot System (`GameServer/bots/`)

Two kinds of bot share one actor and one brain: **autonomous gamebots** (the world population, thousands per realm) and **hired companions** (bots a player recruits into a group). Class AI improvements must reach both.
- **GameBot.cs** — Extends `GameNPC` and implements `IGamePlayer`: real inventory (`BotInventory`), level, experience, specs, death and release, Constitution loss, stable routes. `PersistentRecord` (`OfflineWorldBotRecord`, table `offline_world_bots`) holds a gamebot's durable state.
- **BotBrain.cs** (+ `BotBrain.*.cs` partials) — The one brain for every bot: an ordered list of early returns in `Think()` (companion upkeep, stuck watchdog, buffs, songs, pets, merchant/exchange), then `AutonomousWorldBotController.Tick` for gamebots, then the combat FSM. Spells `CheckSpells`, heals `CheckHeals`, melee styles `BotMeleeStylePolicy`.
- **autonomous/** — The gamebot world: `AutonomousWorldBotController` (+ partials: camp choice `SelectCamp`, camp work `WorkCamp`/`FindCampTarget`, travel `IssuePath`, RvR, sieges, services), `AutonomousBotGroupCoordinator` (party sessions), `AutonomousRvrEventLayer` (sieges, defence rallies), `AutonomousObjectiveAssignments` (solo/group/RvR tasks), `AutonomousBotEconomy` (coin, vendors, Realm Exchange listing), `AutonomousCampPlanningPipeline` (the background camp catalog builder: snapshots only, never live actors). Rules are small static policy classes with pure functions and unit tests (`Tests/UnitTests/UT_*.cs`).
- **Scale rules** — Target 1,000-2,000 bots per realm (6,000+). Cadence: `AutonomousAiBudget` / `AutonomousFidelityPolicy` (combat 250-500 ms, never slowed by population; non-combat slower far from players and at high population; a per-tick decision cap and route-plan budget for non-combat bots). No new per-think area scans or navmesh queries: use caches, attacker lists, the background builder, rate limits and event wake-ups.
- **mode165/** — True 1.65 Mode rules for bots and players (`Classic165Mode.IsOn`): death penalties, wear and repairs, town errands, hired companions (`HiredCompanions165`, `CompanionOptions165`), crafter bots, kill tasks.
- **specs/** — `BotSpec` per class: weapon types, `SpecType` (drives which weapons are equipped) and the spec-line plan; `BuildLabel` names the build by its two highest lines.
- **BotManager.cs / BotCommand.cs** — The original fork's `/bot` companions (create, spawn, despawn, follow, ...). `database/` — `BotProfile` and `BotSettings` DataObjects.
- Combat and difficulty numbers come from the period server and are never changed by bot work.

### AI / Brain System (`GameServer/ai/brain/`)

NPCs use a "brain" pattern for AI behavior. Key brains: `StandardMobBrain`, `ControlledNPCBrain` (pets), specialized class brains. The bot AI system is separate from the brain system but coexists with it.

### Key Patterns

- **Static singletons** — `GameServer.Instance`, `BotManager` methods are static
- **Database objects** — Inherit from `DataObject`, use `[DataTable]` and `[DataElement]` attributes. Persist via `GameServer.Database.SaveObject()`/`AddObject()`
- **Command system** — Classes with `[CmdAttribute]` in `GameServer/commands/`. Player commands go in `playercommands/`, GM in `gmcommands/`, admin in `admincommands/`
- **Event system** — `GameServer/events/` with event handlers registered via `GameEventMgr`

## Code Style

- **Indentation**: 4 spaces, CRLF line endings
- **Naming**: PascalCase for types, properties, methods. Interfaces prefixed with `I`
- **Braces**: Prefer braces when multiline (`when_multiline`)
- **Namespaces**: Block-scoped (not file-scoped)
- **Expression bodies**: Use for properties, indexers, accessors, lambdas; avoid for methods, constructors, operators
