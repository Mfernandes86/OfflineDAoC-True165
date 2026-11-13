using System;
using System.Collections.Generic;

namespace DOL.GS.Quests
{
    /// <summary>
    /// How scripted quests find their NPCs in the world. The scripts used to accept only an NPC at one
    /// exact X/Y. The classic quest world (0.35) placed several of the same NPCs a few units away
    /// (Brigit in Tir na Nog, Danica and Elizabeth in Mularn), and some scripts look at a spot other than
    /// where they create the NPC; the script then spawned a twin beside the world NPC. Only the twin
    /// answered the epic quest's whispers and gave its rewards, so players talking to the other one found
    /// the epic dead. A same-named NPC of the realm in the region within SearchRadius is the quest's NPC.
    /// Gamefix QNPC (2026-11-12): Lost Stone of Arawn (N'chever) and The Lost Seed (Emolia, Jandros) searched a
    /// spot copied from another NPC (O'honat's X/Y, or zone-local numbers), never the spot they create the NPC at,
    /// so a world NPC there would never be found and a twin would stand beside it. Find takes the same spot the
    /// script creates the NPC at; the unit test UT_QuestNpcPlacement checks every quest script for this.
    /// </summary>
    public static class QuestNpcPlacement
    {
        public const int SearchRadius = 3000;

        public static bool IsAt(GameNPC npc, ushort region, int x, int y) =>
            npc != null && npc.CurrentRegionID == region &&
            Math.Abs(npc.X - x) <= SearchRadius && Math.Abs(npc.Y - y) <= SearchRadius;

        /// <summary>
        /// The first NPC of type T among <paramref name="npcs"/> that IsAt the spot, or null (the script then creates
        /// it at that same spot). An NPC of another class is skipped instead of failing a hard cast at script load.
        /// </summary>
        public static T Find<T>(IEnumerable<GameNPC> npcs, ushort region, int x, int y) where T : GameNPC
        {
            if (npcs == null)
                return null;
            foreach (GameNPC npc in npcs)
                if (npc is T match && IsAt(npc, region, x, y))
                    return match;
            return null;
        }
    }
}
