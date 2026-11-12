using System;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>Owner decision O5: light kill tasks for solo gamebots at levels 4-19, paid the player's reward.</summary>
[TestFixture]
public sealed class UT_BotKillTasks165
{
    [Test]
    public void TasksAreForSoloBotsAtTaskLevelsOncePerLevel()
    {
        Assert.Multiple(() =>
        {
            Assert.That(BotKillTasks165.MayTake(true, true, true, 4, false, 0, 0), Is.True);
            Assert.That(BotKillTasks165.MayTake(true, true, true, 3, false, 0, 0), Is.False, "below level 4");
            Assert.That(BotKillTasks165.MayTake(true, true, true, BotKillTasks165.MaximumLevel + 1, false, 0, 0), Is.False, "past the task levels");
            Assert.That(BotKillTasks165.MayTake(true, true, false, 10, false, 0, 0), Is.False, "grouped");
            Assert.That(BotKillTasks165.MayTake(true, true, true, 10, true, 0, 0), Is.False, "already has one");
            Assert.That(BotKillTasks165.MayTake(true, true, true, 10, false, 10, 0), Is.False, "one per level");
            Assert.That(BotKillTasks165.MayTake(true, true, true, 10, false, 0, BotKillTasks165.TakeChancePercent), Is.False, "roll");
            Assert.That(BotKillTasks165.MayTake(false, true, true, 10, false, 0, 0), Is.False, "switched off");
            Assert.That(BotKillTasks165.MayTake(true, false, true, 10, false, 0, 0), Is.False, "normal mode");
        });
    }

    [Test]
    public void TasksExpireAfterTwoHoursOrPastTheLevels()
    {
        Assert.That(BotKillTasks165.Expired(0, BotKillTasks165.TaskLifetimeMilliseconds, 10), Is.False);
        Assert.That(BotKillTasks165.Expired(0, BotKillTasks165.TaskLifetimeMilliseconds + 1, 10), Is.True);
        Assert.That(BotKillTasks165.Expired(0, 1, BotKillTasks165.MaximumLevel + 1), Is.True);
    }

    [Test]
    public void MonsterLevelsFollowKillTask()
    {
        Assert.That(BotKillTasks165.MonsterLevels(10), Is.EqualTo((7, 11)));
        Assert.That(BotKillTasks165.MonsterLevels(2), Is.EqualTo((1, 3)));
    }

    [Test]
    public void MoneyFollowsTheTableWithItsSpread()
    {
        int[] table = { 28, 57, 77, 105, 140 };
        var random = new Random(3);
        for (int i = 0; i < 200; i++)
        {
            long money = BotKillTasks165.MoneyFrom(table, 5, random);
            Assert.That(money, Is.InRange(140 - 3, 140 + 3));
        }
        Assert.That(BotKillTasks165.MoneyFrom(table, 6, random), Is.Zero);
        Assert.That(BotKillTasks165.MoneyFrom(table, 0, random), Is.Zero);
    }

    [Test]
    public void TheServerTaskTableIsFound()
    {
        Assert.That(BotKillTasks165.Money(1, new Random(1)), Is.InRange(28, 28), "KillTask.MoneyReward[0] = 28, spread 0");
        Assert.That(BotKillTasks165.Money(19, new Random(1)), Is.InRange(2407 - 72, 2407 + 72));
    }

    [Test]
    public void FinishedTaskIsPaidOnlyNearItsGuard()
    {
        const long bot = 987_654_321;
        BotKillTasks165.Give(bot, new BotKillTasks165.Task { MonsterName = "hill toad", GuardName = "Sir Test", Region = 1,
            X = 10_000, Y = 10_000, Level = 6, TakenTick = 1_000 });
        Assert.That(BotKillTasks165.HuntedMonster(bot, 2_000, 6), Is.EqualTo("hill toad"));
        Assert.That(BotKillTasks165.TakeFinished(bot, 1, 10_000, 10_000, 2_000, 6), Is.Null, "not killed yet");
        Assert.That(BotKillTasks165.LastTaskLevel(bot), Is.EqualTo(6));
    }
}
