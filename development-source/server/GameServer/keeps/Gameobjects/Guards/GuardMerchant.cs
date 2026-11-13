/*
 * DAWN OF LIGHT - The first free open source DAoC server emulator
 * 
 * This program is free software; you can redistribute it and/or
 * modify it under the terms of the GNU General Public License
 * as published by the Free Software Foundation; either version 2
 * of the License, or (at your option) any later version.
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU General Public License
 * along with this program; if not, write to the Free Software
 * Foundation, Inc., 59 Temple Place - Suite 330, Boston, MA  02111-1307, USA.
 */

using DOL.AI.Brain;
using DOL.Database;
using DOL.GS.PlayerClass;

namespace DOL.GS.Keeps
{
	public class GuardMerchant : GameGuardMerchant
	{
		// True 1.65 merchant keeps (Caer Benowyc, Bledmeer Faste, Dun Crauchon; battlegrounds fix 2026-11-12, lane BGCONTENT
		// R2): a Mob row may name the merchant per owning realm ("Alb|Mid|Hib"; an empty part keeps the server's name) and
		// give a list base ("true165MkSiege" -> "true165MkSiegeAlb/Mid/Hib"). A row without either behaves as before (the
		// realm's RvRCraftingList and the server's names). Both follow the keep's realm when it is captured: the guard
		// respawns for the new owner and RefreshTemplate -> SetName re-picks the name and the list.
		private string[] m_realmNames;
		private string m_listBase;

		public override void LoadFromDatabase(DataObject obj)
		{
			if (obj is DbMob mob)
			{
				string[] names = (mob.Name ?? string.Empty).Split('|');
				m_realmNames = names.Length == 3 ? names : null;
				m_listBase = string.IsNullOrWhiteSpace(mob.ItemsListTemplateID) ? null : mob.ItemsListTemplateID.Trim();
			}
			base.LoadFromDatabase(obj); // runs RefreshTemplate -> SetName, so the fields are set first
		}

		/// <summary>The merchant list for a realm: the row's list base plus the realm tag, or the realm's RvRCraftingList
		/// without one; null for no realm (the list is then left as it is).</summary>
		public static string ListFor(string listBase, eRealm realm)
		{
			string tag = realm switch { eRealm.Albion => "Alb", eRealm.Midgard => "Mid", eRealm.Hibernia => "Hib", _ => null };
			return tag == null ? null : listBase == null ? tag + "RvRCraftingList" : listBase + tag;
		}

		/// <summary>The row's own name for a realm, or null to keep the server's name.</summary>
		public static string NameFor(string[] realmNames, eRealm realm)
		{
			if (realmNames == null || realmNames.Length != 3) return null;
			string name = realmNames[realm == eRealm.Midgard ? 1 : realm == eRealm.Hibernia ? 2 : 0];
			return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
		}

		private void SetRealmList()
		{
			string list = ListFor(m_listBase, Realm);
			if (list != null && TradeItems?.ItemsListID != list)
				TradeItems = new MerchantTradeItems(list);
		}

		public override bool AddToWorld()
		{
			SetRealmList();

			GuildName = "Merchant";
			return base.AddToWorld();
		}

		public override double GetArmorAbsorb(eArmorSlot slot)
		{
			return base.GetArmorAbsorb(slot) - 0.05;
		}

		protected override KeepGuardBrain GetBrain() => new KeepGuardBrain();
		
		protected override ICharacterClass GetClass()
		{
			if (ModelRealm == eRealm.Albion) return new ClassArmsman();
			else if (ModelRealm == eRealm.Midgard) return new ClassWarrior();
			else if (ModelRealm == eRealm.Hibernia) return new ClassHero();
			return new DefaultCharacterClass();
		}
		protected override void SetName()
		{
			switch (ModelRealm)
			{
				case eRealm.None:
				case eRealm.Albion:
					if (IsPortalKeepGuard)
					{
						if (Gender == eGender.Female)
							Name = "Frida";
						else Name = "Frederic";
					}
					else
					{
						if (Gender == eGender.Female)
							Name = "Fabienne";
						else Name = "Francis";
					}
					GuildName = "Merchant";
					break;
				case eRealm.Midgard:
					if (IsPortalKeepGuard)
					{
						if (Gender == eGender.Female)
							Name = "Olga";
						else Name = "Odun";
					}
					else
					{
						if (Gender == eGender.Female)
							Name = "Rikke";
						else Name = "Rollo";
					}
					break;
				case eRealm.Hibernia:
					if (IsPortalKeepGuard)
					{
						if (Gender == eGender.Female)
							Name = "Alenja";
						else Name = "Airell";
					}
					else
					{
						if (Gender == eGender.Female)
							Name = "Arwen";
						else Name = "Aidan";
					}
					break;
			}

			if (Realm == eRealm.None)
			{
				if (Gender == eGender.Female)
					Name = "Finnja";
				else Name = "Fynn";
			}
			else
			{
				if (NameFor(m_realmNames, ModelRealm) is string own)
				{
					Name = own;
					GuildName = "Merchant";
				}
				// A respawn runs AddToWorld before RefreshTemplate gives the guard its new owner's realm: the list follows
				// the realm here as well, so a captured merchant keep sells the new owner's stock.
				SetRealmList();
			}
		}
	}
}
