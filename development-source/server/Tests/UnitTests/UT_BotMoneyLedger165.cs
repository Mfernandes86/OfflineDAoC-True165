using System;
using System.Collections.Generic;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests.Gameserver;

/// <summary>BOT_MONEY_LEDGER_165 (veteran-bots review M9): one line an hour of where the gamebots' coin came from and went.</summary>
[TestFixture, NonParallelizable]
public class UT_BotMoneyLedger165
{
    private Action<string> _sink;
    private Func<long> _clock;

    [SetUp]
    public void SetUp()
    {
        _sink = BotMoneyLedger165.Sink;
        _clock = BotMoneyLedger165.Clock;
        BotMoneyLedger165.Reset();
    }

    [TearDown]
    public void TearDown()
    {
        BotMoneyLedger165.Reset();
        BotMoneyLedger165.Sink = _sink;
        BotMoneyLedger165.Clock = _clock;
    }

    [Test]
    public void LedgerWritesOneLinePerHourWithKindsAndTotals()
    {
        long now = 5_000;
        var lines = new List<string>();
        BotMoneyLedger165.Clock = () => now;
        BotMoneyLedger165.Sink = lines.Add;

        BotMoneyLedger165.Credit(BotMoneyLedger165.Kind.LootCoin, 300);
        BotMoneyLedger165.Credit(BotMoneyLedger165.Kind.LootCoin, 200);
        BotMoneyLedger165.Debit(BotMoneyLedger165.Kind.Repair, 120);
        BotMoneyLedger165.Debit(BotMoneyLedger165.Kind.Healer, 80);
        now += BotMoneyLedger165.WindowMilliseconds - 1;
        BotMoneyLedger165.Credit(BotMoneyLedger165.Kind.VendorSale, 40);
        Assert.That(lines, Is.Empty, "nothing before the hour ends");

        now += 1;
        BotMoneyLedger165.Debit(BotMoneyLedger165.Kind.Dye, 10);
        Assert.That(lines, Has.Count.EqualTo(1));
        Assert.That(lines[0], Does.StartWith("BOT_MONEY_LEDGER_165 window=3600s in_total=540 out_total=210 net=330"));
        Assert.That(lines[0], Does.Contain(" in_lootcoin=500/2"));
        Assert.That(lines[0], Does.Contain(" out_repair=120/1"));
        Assert.That(lines[0], Does.Contain(" out_healer=80/1"));
        Assert.That(lines[0], Does.Contain(" in_vendorsale=40/1"));
        Assert.That(lines[0], Does.Contain(" out_dye=10/1"));
    }

    [Test]
    public void LedgerIgnoresZeroAndNegativeAmountsAndWritesNothingWhenIdle()
    {
        BotMoneyLedger165.Credit(BotMoneyLedger165.Kind.LootCoin, 0);
        BotMoneyLedger165.Debit(BotMoneyLedger165.Kind.Repair, -5);
        Assert.That(BotMoneyLedger165.Flush(1_000), Is.Null);
    }

    [Test]
    public void FlushResetsTheSums()
    {
        var lines = new List<string>();
        BotMoneyLedger165.Sink = lines.Add;
        BotMoneyLedger165.Credit(BotMoneyLedger165.Kind.CraftSale, 75);
        Assert.That(BotMoneyLedger165.Flush(60_000), Does.Contain("in_craftsale=75/1"));
        Assert.That(BotMoneyLedger165.Flush(60_000), Is.Null);
    }
}
