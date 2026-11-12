using System;
using System.Collections.Generic;
using System.Globalization;
using DOL.Database;

namespace DOL.GS;

/// <summary>
/// What a player sees when browsing the Realm Exchange (2026-11-11 overhaul plan, phase 6). Pure functions over the
/// search results, run only when a player searches (one human, a few searches a minute); bots never call them.
/// <list type="bullet">
/// <item>One row per item: copies of the same item (name, level, quality, crafted, type, stack or single, and the same
/// stats: bonuses, procs and charges, condition within 10%) become one row showing the cheapest copy (by unit price) that
/// the viewer may buy, with the number of copies. Half of the A-Z pages used to show one item 20 times. An enchanted or
/// better copy is its own row, never hidden behind a cheaper plain one (review 2026-11-11).</item>
/// <item>A default order about the player ("fit"): items at or below his level + 3 first, closest to his level first,
/// then price. "newest" orders by listing time.</item>
/// <item>A status line with the number of different items and of listings new in the last hour.</item>
/// </list>
/// Protocol (search request, the byte after "item ability"): <see cref="FlagUsableOnly"/> hides items the player cannot
/// use, <see cref="FlagEveryCopy"/> shows every copy as before. An older client sends 0: grouped rows, every item shown.
/// In grouped rows the per-row u16 "lot" field of the market window carries the copy count (1-65000) instead of the
/// exchange lot (65001-65003); an older client ignores that field.
/// </summary>
public static class RealmExchangeBrowse
{
    /// <summary>Search flag: only items the player can use (GameLiving.HasAbilityToUseItem).</summary>
    public const byte FlagUsableOnly = 0x01;
    /// <summary>Search flag: every copy is its own row (the old view).</summary>
    public const byte FlagEveryCopy = 0x02;
    /// <summary>The largest copy count sent in the lot field (65001-65003 are the exchange lots).</summary>
    public const ushort MaxCopyLot = 65000;
    /// <summary>Items up to this many levels above the player count as "for him" in the fit order.</summary>
    public const int FitLevelsAbove = 3;

    /// <summary>A browse row: the copy a purchase takes, how many copies the row stands for, its newest listing time.</summary>
    public sealed record Row(DbInventoryItem Item, int Copies, long NewestTicks);

    /// <summary>The rows of one search, plus the counts the status line shows.</summary>
    public sealed record View(List<Row> Rows, int Listings, int Distinct, int Fresh, bool Grouped);

    private readonly record struct GroupKey(string Name, int Level, int Quality, bool Crafted, int ObjectType, bool Stack, string Stats);

    /// <summary>
    /// Builds the rows of a search. <paramref name="grouped"/> makes one row per item; the distinct count is the same
    /// either way. <paramref name="viewerId"/>'s own copies are shown only when no other copy exists (a player cannot
    /// buy his own listing).
    /// </summary>
    public static View Build(IReadOnlyList<DbInventoryItem> items, bool grouped, string viewerId, DateTime utcNow)
    {
        long freshSince = utcNow.AddHours(-1).Ticks;
        var groups = new Dictionary<GroupKey, int>();
        var rows = new List<Row>(items.Count);
        var single = grouped ? null : new List<Row>(items.Count);
        int fresh = 0;
        foreach (DbInventoryItem item in items)
        {
            if (item == null)
                continue;
            long listed = ListedTicks(item);
            if (listed >= freshSince)
                fresh++;
            single?.Add(new Row(item, 1, listed));
            GroupKey key = KeyOf(item);
            if (!groups.TryGetValue(key, out int index))
            {
                groups[key] = rows.Count;
                rows.Add(new Row(item, 1, listed));
                continue;
            }
            Row row = rows[index];
            rows[index] = new Row(Better(item, row.Item, viewerId) ? item : row.Item, row.Copies + 1, Math.Max(row.NewestTicks, listed));
        }
        return new View(grouped ? rows : single, items.Count, rows.Count, fresh, grouped);
    }

    private static GroupKey KeyOf(DbInventoryItem item) => new(
        (item.Name ?? string.Empty).ToLowerInvariant(), item.Level, item.Quality, item.IsCrafted, item.Object_Type,
        item.Count > 1 || item.PackSize > 1, StatsOf(item));

    /// <summary>
    /// What makes two copies of one item really differ: the Bonus1-10 and extra bonus type/value pairs, the spells, procs
    /// and charges, and the condition in 10% steps. Copies with different stats are different rows.
    /// </summary>
    public static string StatsOf(DbInventoryItem item) => string.Join(',',
        item.Bonus1Type, item.Bonus1, item.Bonus2Type, item.Bonus2, item.Bonus3Type, item.Bonus3, item.Bonus4Type, item.Bonus4,
        item.Bonus5Type, item.Bonus5, item.Bonus6Type, item.Bonus6, item.Bonus7Type, item.Bonus7, item.Bonus8Type, item.Bonus8,
        item.Bonus9Type, item.Bonus9, item.Bonus10Type, item.Bonus10, item.ExtraBonusType, item.ExtraBonus,
        item.SpellID, item.SpellID1, item.ProcSpellID, item.ProcSpellID1, item.Charges, item.Charges1,
        Math.Clamp((int)item.ConditionPercent, 0, 100) / 10);

    /// <summary>Whether <paramref name="candidate"/> should stand for its row instead of <paramref name="current"/>.</summary>
    public static bool Better(DbInventoryItem candidate, DbInventoryItem current, string viewerId)
    {
        bool candidateOwn = !string.IsNullOrEmpty(viewerId) && candidate.OwnerID == viewerId;
        bool currentOwn = !string.IsNullOrEmpty(viewerId) && current.OwnerID == viewerId;
        if (candidateOwn != currentOwn)
            return currentOwn;
        // the cheaper unit price (a stack's price per item), then the cheaper ask, then a stable order
        long left = (long)candidate.SellPrice * Math.Max(1, current.Count);
        long right = (long)current.SellPrice * Math.Max(1, candidate.Count);
        if (left != right)
            return left < right;
        if (candidate.SellPrice != current.SellPrice)
            return candidate.SellPrice < current.SellPrice;
        return string.CompareOrdinal(candidate.ObjectId, current.ObjectId) < 0;
    }

    /// <summary>When the listing went up (its RealmExchangeListedUtc), or 0.</summary>
    public static long ListedTicks(DbInventoryItem item) =>
        DateTime.TryParse(item?.RealmExchangeListedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime listed)
            ? listed.ToUniversalTime().Ticks
            : 0;

    /// <summary>
    /// Orders rows in place by the player's column ("name|level|quality|price|newest|fit asc|desc"); no column, or an
    /// unknown one, is the fit order. Every order ends on price, then name, then object id, so pages never shuffle.
    /// </summary>
    public static List<Row> Order(List<Row> rows, string sort, int viewerLevel)
    {
        string[] parts = (sort ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string key = parts.Length == 2 ? parts[0] : "fit";
        bool down = parts.Length == 2 && parts[1] == "desc";
        Comparison<Row> primary = key switch
        {
            "name" => (a, b) => string.Compare(a.Item.Name, b.Item.Name, StringComparison.OrdinalIgnoreCase),
            "level" => (a, b) => a.Item.Level.CompareTo(b.Item.Level),
            "quality" => (a, b) => a.Item.Quality.CompareTo(b.Item.Quality),
            "price" => (a, b) => a.Item.SellPrice.CompareTo(b.Item.SellPrice),
            "newest" => (a, b) => a.NewestTicks.CompareTo(b.NewestTicks),
            _ => (a, b) => FitRank(a.Item.Level, viewerLevel).CompareTo(FitRank(b.Item.Level, viewerLevel)),
        };
        rows.Sort((a, b) =>
        {
            int order = primary(a, b);
            if (order != 0)
                return down ? -order : order;
            order = a.Item.SellPrice.CompareTo(b.Item.SellPrice);
            if (order != 0)
                return order;
            order = string.Compare(a.Item.Name, b.Item.Name, StringComparison.OrdinalIgnoreCase);
            return order != 0 ? order : string.CompareOrdinal(a.Item.ObjectId, b.Item.ObjectId);
        });
        return rows;
    }

    /// <summary>
    /// The fit rank of an item level for a player (lower first): items at or below his level + 3 come first, the
    /// closest to his level first; items further above him follow, the nearest first.
    /// </summary>
    public static int FitRank(int itemLevel, int viewerLevel)
    {
        int distance = Math.Abs(itemLevel - viewerLevel);
        return itemLevel <= viewerLevel + FitLevelsAbove ? distance : 1_000 + distance;
    }

    /// <summary>The u16 lot field per row: the copy count for grouped rows, null (each item's own lot) otherwise.</summary>
    public static ushort[] Lots(IReadOnlyList<Row> page, bool grouped)
    {
        if (!grouped)
            return null;
        var lots = new ushort[page.Count];
        for (int i = 0; i < page.Count; i++)
            lots[i] = (ushort)Math.Clamp(page[i].Copies, 1, MaxCopyLot);
        return lots;
    }

    /// <summary>
    /// The page-0 status line. It starts as it always did ("Realm Exchange returned N real listing(s)"): older clients
    /// read N as the fourth word.
    /// </summary>
    public static string Status(int listings, int distinct, int fresh) =>
        $"Realm Exchange returned {listings} real listing(s) ({distinct} different item(s), {fresh} new in the last hour).";
}
