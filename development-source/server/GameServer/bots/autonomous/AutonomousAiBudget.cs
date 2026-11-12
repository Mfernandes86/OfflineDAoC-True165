using System;
using System.Diagnostics;
using System.Threading;
using DOL.GS.ServerProperties;

namespace DOL.GS;

public enum eAutonomousThinkMode
{
    Combat,
    PlayerLed,
    NearbyInteraction,
    Travel,
    Resting,
    Planning,
}

/// <summary>
/// Lightweight scheduling policy for live bot actors. Slower thinking never
/// grants progress or teleports work: movement, attacks and interactions still
/// occur through the normal world. Stable offsets prevent synchronized spikes.
/// </summary>
public static class AutonomousAiBudget
{
    public static int IntervalMilliseconds(eAutonomousThinkMode mode) => mode switch
    {
        eAutonomousThinkMode.Combat => 250,
        eAutonomousThinkMode.PlayerLed => 300,
        eAutonomousThinkMode.NearbyInteraction => 600,
        eAutonomousThinkMode.Travel => 900,
        eAutonomousThinkMode.Resting => 1800,
        _ => 2500,
    };

    public static int StableOffsetMilliseconds(long botId, eAutonomousThinkMode mode)
    {
        int interval = IntervalMilliseconds(mode);
        ulong mixed = unchecked((ulong)botId) * 11400714819323198485UL;
        return (int)(mixed % (uint)interval);
    }

    public static int MaximumDecisionsThisTick(double recentTickMilliseconds, double budgetMilliseconds, int queueCount)
    {
        if (queueCount <= 0 || budgetMilliseconds <= 0 || recentTickMilliseconds >= budgetMilliseconds)
            return 0;

        double headroom = budgetMilliseconds - recentTickMilliseconds;
        int allowance = Math.Max(1, (int)Math.Floor(headroom / 0.12));
        return Math.Min(queueCount, allowance);
    }

    // ------------------------------------------------------------------ veteran review M4: per-tick scale guards

    [ServerProperty("bots", "bot_ai_tick_budget",
        "Veteran review M4 (2026-11-11): a per-tick cap on decisions of gamebots that are not fighting (travel, rest, planning) and a per-tick budget for new route plans, so slow ticks spread work over the next ticks instead of stalling. Combat thinking is never capped.", true)]
    public static bool TickBudgetEnabled = true;

    /// <summary>
    /// The NPC-service time (ms per tick, of 33) up to which non-combat decisions are uncapped in practice: the cap is an
    /// overload guard that bites only when the service averages well over half a tick, not a constant throttle (a
    /// deferred bot re-runs the start of its think, so a cap that bit all the time would cost more than it saved).
    /// </summary>
    public const double DecisionBudgetMilliseconds = 20;

    /// <summary>Non-combat decisions always allowed per tick, however slow the last ticks were (720 a second).</summary>
    public const int MinimumDecisionsPerTick = 24;

    /// <summary>Worker time (ms, summed over threads) that new route plans may use in one tick.</summary>
    public const double RoutePlanBudgetMilliseconds = 40;

    /// <summary>New route plans always allowed per tick.</summary>
    public const int MinimumRoutePlansPerTick = 4;

    private static double _recentNpcTickMilliseconds;
    private static int _decisionsLeft = int.MaxValue;
    private static int _routePlansStarted;
    private static long _routePlanTimestampTicks;
    private static long _npcTickStartTimestamp;
    private static int _deferredDecisions, _deferredRoutePlans;

    /// <summary>Pure rule: the non-combat decisions allowed this tick, from the recent NPC-service time.</summary>
    public static int DecisionAllowance(double recentTickMilliseconds) =>
        Math.Max(MinimumDecisionsPerTick,
            MaximumDecisionsThisTick(recentTickMilliseconds, DecisionBudgetMilliseconds, int.MaxValue));

    /// <summary>Pure rule: a new route plan may start (the first few always may, then while the tick's plan time lasts).</summary>
    public static bool RoutePlanAllowed(int plansAlreadyStarted, double planMillisecondsUsed) =>
        plansAlreadyStarted < MinimumRoutePlansPerTick || planMillisecondsUsed < RoutePlanBudgetMilliseconds;

    /// <summary>Pure rule: a smoothed tick time (recent ticks weigh 20%).</summary>
    public static double Smooth(double previous, double sample) => previous <= 0 ? sample : previous * 0.8 + sample * 0.2;

    /// <summary>Called by the NPC service before it runs the brains of a tick (single-threaded).</summary>
    public static void BeginNpcTick()
    {
        _npcTickStartTimestamp = Stopwatch.GetTimestamp();
        Volatile.Write(ref _decisionsLeft, TickBudgetEnabled ? DecisionAllowance(_recentNpcTickMilliseconds) : int.MaxValue);
        Volatile.Write(ref _routePlansStarted, 0);
        Volatile.Write(ref _routePlanTimestampTicks, 0);
    }

    /// <summary>Called by the NPC service after the brains of a tick ran (single-threaded).</summary>
    public static void EndNpcTick()
    {
        if (_npcTickStartTimestamp == 0)
            return;
        double elapsed = Stopwatch.GetElapsedTime(_npcTickStartTimestamp).TotalMilliseconds;
        _recentNpcTickMilliseconds = Smooth(_recentNpcTickMilliseconds, elapsed);
    }

    /// <summary>
    /// Pure rule (performance review 2026-11-11): a decision goes ahead when the allowance had one left, or when the bot
    /// was deferred on its last think - its priority pass. Each decision is deferred at most once, so under overload a
    /// deferred bot costs one extra think prefix, not a retry every few ticks that kept the service slow and the cap on.
    /// </summary>
    public static bool DecisionAllowed(bool priority, int leftAfterTaking) => priority || leftAfterTaking >= 0;

    /// <summary>
    /// A gamebot that is not fighting takes one decision from this tick's allowance; false = think again soon.
    /// <paramref name="priority"/>: it was deferred last time, so it goes ahead now (still counted against the allowance).
    /// </summary>
    public static bool TryTakeDecision(bool priority = false)
    {
        // nothing is capped until the NPC service runs ticks (tools and unit tests drive bots directly)
        if (!TickBudgetEnabled || _npcTickStartTimestamp == 0 || DecisionAllowed(priority, Interlocked.Decrement(ref _decisionsLeft)))
            return true;
        Interlocked.Increment(ref _deferredDecisions);
        return false;
    }

    /// <summary>
    /// A new route plan may start this tick; false = keep the current movement order and plan next think.
    /// <paramref name="priority"/>: its plan was deferred last time, so it starts now (still counted against the budget).
    /// </summary>
    public static bool TryBeginRoutePlan(bool priority = false)
    {
        if (!TickBudgetEnabled || _npcTickStartTimestamp == 0)
            return true;
        int started = Interlocked.Increment(ref _routePlansStarted) - 1;
        double used = Stopwatch.GetElapsedTime(0, Volatile.Read(ref _routePlanTimestampTicks)).TotalMilliseconds;
        if (priority || RoutePlanAllowed(started, used))
            return true;
        Interlocked.Increment(ref _deferredRoutePlans);
        return false;
    }

    /// <summary>The time a route plan took (Stopwatch timestamp units), counted against this tick's plan budget.</summary>
    public static void EndRoutePlan(long startTimestamp) =>
        Interlocked.Add(ref _routePlanTimestampTicks, Math.Max(0, Stopwatch.GetTimestamp() - startTimestamp));

    /// <summary>How soon a deferred bot thinks again (5-12 ticks, staggered so deferred bots do not return together).</summary>
    public static int DeferredThinkMilliseconds(long stableId) => 150 + (int)((ulong)Math.Abs(stableId % 251));

    /// <summary>Deferred decisions and route plans since the last call (for the once-a-minute work summary).</summary>
    public static (int Decisions, int RoutePlans) TakeDeferredCounts() =>
        (Interlocked.Exchange(ref _deferredDecisions, 0), Interlocked.Exchange(ref _deferredRoutePlans, 0));
}
