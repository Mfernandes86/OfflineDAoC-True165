using System;
using System.Collections.Generic;
using System.Diagnostics;
using DOL.Database;

namespace DOL.GS;

/// <summary>
/// The staggered market pass (2026-11-11 exchange plan, section 2.6). Every 30 s, on the exchange timer, a round-robin
/// cursor per realm walks the bot sellers - logged in or offline, as their listings are only rows - and does at most one
/// thing for each:
/// - a copy over the limits (a name with more than <see cref="AutonomousBotEconomy.MaxRealmBotCopies"/> bot copies in the
///   realm, or more than <see cref="AutonomousBotEconomy.MaxBotCopiesPerName"/> from one seller) that is among the dearest
///   goes back to its owner (or is paid out at the merchant value), so the old walls of one item drain away without a
///   wipe;
/// - otherwise a listing unsold for 12 h is re-undercut once (Exchange165.TryUndercut).
/// At most <see cref="SellersPerPass"/> sellers per realm and <see cref="BudgetMilliseconds"/> ms per realm: a full cycle
/// over ~1,700 sellers takes ~21 minutes, short against 24 h listings. Reprices go out through the coalesced write queue.
/// </summary>
public static class RealmMarketPass
{
    public const int SellersPerPass = 40;
    public const double BudgetMilliseconds = 2.0;
    /// <summary>Most bumped copies paid out per realm and pass (each payout writes to the database under the locks).</summary>
    public const int MaxCreditsPerPass = 5;

    private static readonly int[] Cursor = new int[4];

    public static int Run(DateTime utcNow)
    {
        int actions = 0;
        foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
            actions += RunRealm(realm, utcNow);
        return actions;
    }

    public static int RunRealm(eRealm realm, DateTime utcNow)
    {
        long start = Stopwatch.GetTimestamp();
        long budget = (long)(BudgetMilliseconds * Stopwatch.Frequency / 1000);
        string[] sellers = MarketCache.ReadBook(book => book.Sellers(realm));
        if (sellers.Length == 0)
            return 0;
        int actions = 0;
        if (!RealmExchangeLocks.TryEnter(0, true, out RealmExchangeLocks.Scope scope))
            return 0;
        using (scope)
        {
            int count = Math.Min(SellersPerPass, sellers.Length);
            int creditsAtStart = Exchange165.CreditCount;
            for (int i = 0; i < count; i++)
            {
                if (Exchange165.CreditCount - creditsAtStart >= MaxCreditsPerPass)
                    break;
                int index = (Cursor[(int)realm] = (Cursor[(int)realm] + 1) % sellers.Length);
                if (Step(sellers[index], utcNow))
                    actions++;
                if (Stopwatch.GetTimestamp() - start > budget)
                    break;
            }
        }
        RealmExchangeStats.NotePass(Stopwatch.GetTimestamp() - start);
        return actions;
    }

    /// <summary>One seller's turn: at most one bump or one reprice. True when something changed.</summary>
    public static bool Step(string seller, DateTime utcNow)
    {
        if (!AutonomousBotEconomy.TryParseOwnerId(seller, out long botId))
            return false;
        MarketBook.Listing[] listings = MarketCache.ReadBook(book => book.OwnerListings(seller));
        if (listings.Length == 0)
            return false;
        // copies over the limits first: the dearest go
        foreach (MarketBook.Listing listing in listings)
        {
            if (!OverCopyLimit(listing, listings))
                continue;
            if (Exchange165.Pull(listing.Item, botId, (int)listing.Price, RealmExchangeStats.Counter.Bumped))
            {
                AutonomousBotEconomy.MarkEconomyChanged(botId);
                return true;
            }
        }
        // then the oldest listing unsold for 12 h is re-undercut once (an expired one is left to the expiry sweep)
        MarketBook.Listing oldest = null;
        foreach (MarketBook.Listing listing in listings)
            if (listing.ListedTicks > 0 && (oldest == null || listing.ListedTicks < oldest.ListedTicks) &&
                (listing.ExpiresTicks <= 0 || listing.ExpiresTicks > utcNow.Ticks) &&
                Exchange165.RelistOf(listing.Item) == null && utcNow.Ticks - listing.ListedTicks >= Exchange165.UndercutAge.Ticks)
                oldest = listing;
        return oldest != null && Exchange165.TryUndercut(oldest.Item, utcNow);
    }

    /// <summary>
    /// A listing over the copy limits: this seller holds more than its share of the name and this is its dearest copy, or
    /// the realm holds more than its share of bot copies and at least that many are cheaper than this one.
    /// </summary>
    private static bool OverCopyLimit(MarketBook.Listing listing, MarketBook.Listing[] own)
    {
        int ownCopies = 0;
        bool dearestOwn = true;
        foreach (MarketBook.Listing other in own)
        {
            if (!string.Equals(other.NameKey, listing.NameKey, StringComparison.OrdinalIgnoreCase))
                continue;
            ownCopies++;
            if (!ReferenceEquals(other, listing) && MarketBook.UnitCheaper(listing, other))
                dearestOwn = false;   // per unit: stacks of different sizes compare per item
        }
        if (ownCopies > AutonomousBotEconomy.MaxBotCopiesPerName && dearestOwn)
            return true;
        MarketBook.NameStats stats = MarketCache.ReadBook(book => book.Name(listing.Realm, listing.NameKey, null));
        if (stats.BotCopies <= AutonomousBotEconomy.MaxRealmBotCopies)
            return false;
        int cheaper = MarketCache.ReadBook(book => book.CheaperBotCopies(listing));
        return cheaper >= AutonomousBotEconomy.MaxRealmBotCopies;
    }
}
