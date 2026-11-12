using System;
using DOL.AI;
using DOL.AI.Brain;
using DOL.GS;

namespace DOL.GS
{
    public abstract class TimeDependentSpawnNpc : GameNPC
    {
        private const eFlags INACTIVE_FLAGS = eFlags.CANTTARGET | eFlags.DONTSHOWNAME | eFlags.PEACE;

        public new TimeDependentSpawnBrain Brain => base.Brain as TimeDependentSpawnBrain;
        public override eFlags Flags => Brain != null && Brain.IsVisible ? base.Flags : base.Flags | INACTIVE_FLAGS;

        // Saved without the asleep flags, so a spawn saved at the wrong time of day still wakes up.
        protected override eFlags FlagsToSave => base.Flags;
        public override ushort Model => Brain != null && Brain.IsVisible ? base.Model : (ushort) 1;

        public override bool AddToWorld()
        {
            // The spawn's own brain replaces the one LoadFromDatabase made, and that one carried the aggression of the
            // Mob row or its NPC template. Without the hand-over every night and day spawn came up with aggro 0 and
            // stood passive whatever its row said (owner 2026-11-11: "fix them" - 486 of the 547 night spawns have an
            // aggressive row: disturbed presences, giant skeletons, deamhan aeir, drowned souls, the Undead Patrol).
            // While it is hidden the spawn has the PEACE flag (Flags above), so it attacks nobody then.
            ABrain fresh = CreateBrain();
            CarryAggro(base.Brain as IOldAggressiveBrain, fresh as IOldAggressiveBrain);
            SetOwnBrain(fresh);
            return base.AddToWorld();
        }

        /// <summary>The new brain takes over the old brain's aggro level and range (nothing when either is missing).</summary>
        public static void CarryAggro(IOldAggressiveBrain from, IOldAggressiveBrain to)
        {
            if (from == null || to == null || ReferenceEquals(from, to))
                return;
            to.AggroLevel = from.AggroLevel;
            to.AggroRange = from.AggroRange;
        }

        protected virtual ABrain CreateBrain()
        {
            throw new NotImplementedException();
        }
    }

    public class DaySpawn : TimeDependentSpawnNpc
    {
        protected override ABrain CreateBrain()
        {
            return new DaySpawnBrain();
        }
    }

    public class NightSpawn : TimeDependentSpawnNpc
    {
        protected override ABrain CreateBrain()
        {
            return new NightSpawnBrain();
        }
    }

    /// <summary>
    /// A spawn that shows only during its own hours of the game day (quest audit 2026-11-11, P08-E7: the skull post
    /// rotation of the period walkthroughs, Thraxia 8-16, Terje 16-24, Lord Lauur 0-8; DaySpawn/NightSpawn can only split
    /// the day in two). The hours come from <see cref="HourWindows"/> by name; a name without hours shows all day.
    /// </summary>
    public class HourWindowSpawn : TimeDependentSpawnNpc
    {
        protected override ABrain CreateBrain()
        {
            return new HourWindowSpawnBrain();
        }
    }

    /// <summary>The game hours of each <see cref="HourWindowSpawn"/> by name: [Start, End), End may be 24.</summary>
    public static class HourWindows
    {
        private static readonly System.Collections.Generic.Dictionary<string, (int Start, int End)> ByName =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["Thraxia"] = (8, 16),
                ["Terje"] = (16, 24),
                ["Lord Lauur"] = (0, 8),
            };

        /// <summary>The hours from classic-quests.json (HourWindows) when it names the NPC, else the built-in ones.</summary>
        public static bool TryGet(string name, out (int Start, int End) window) =>
            DOL.GS.Quests.ClassicQuests.TryHourWindow(name, out window) || ByName.TryGetValue(name ?? string.Empty, out window);

        /// <summary>Is the hour (0-23) inside [start, end)? A window that wraps past midnight (start above end) works too.</summary>
        public static bool Contains(int hour, int start, int end) =>
            start <= end ? hour >= start && hour < end : hour >= start || hour < end;
    }
}

namespace DOL.AI.Brain
{
    public abstract class TimeDependentSpawnBrain : StandardMobBrain
    {
        public bool IsVisible { get; private set; }

        public override void Think()
        {
            if (!Body.InCombat)
            {
                bool previousVisibility = IsVisible;
                IsVisible = ShouldBeVisible();

                if (previousVisibility != IsVisible)
                    ClientService.CreateObjectForPlayers(Body);
            }

            base.Think();
        }

        protected virtual bool ShouldBeVisible()
        {
            throw new NotImplementedException();
        }
    }

    public class DaySpawnBrain : TimeDependentSpawnBrain
    {
        protected override bool ShouldBeVisible()
        {
            return !Body.CurrentRegion.IsNightTime;
        }
    }

    public class NightSpawnBrain : TimeDependentSpawnBrain
    {
        protected override bool ShouldBeVisible()
        {
            return Body.CurrentRegion.IsNightTime;
        }
    }

    public class HourWindowSpawnBrain : TimeDependentSpawnBrain
    {
        private (int Start, int End)? _window;
        private int _lookedAt = -1;

        protected override bool ShouldBeVisible()
        {
            // looked up again when the quest file is (re)loaded: its hours can come from there
            int version = DOL.GS.Quests.ClassicQuests.ConfigVersion;
            if (_lookedAt != version)
            {
                _window = HourWindows.TryGet(Body.Name, out var window) ? window : null;
                _lookedAt = version;
            }
            if (_window is not { } hours)
                return true;
            int hour = (int)(Body.CurrentRegion.GameTime / 1000 / 60 / 60) % 24;
            return HourWindows.Contains(hour, hours.Start, hours.End);
        }
    }
}
