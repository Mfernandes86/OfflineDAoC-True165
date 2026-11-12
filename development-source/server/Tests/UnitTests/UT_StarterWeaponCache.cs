using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using DOL.GS.GameEvents;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Q11 (veteran review 2026-11-11): bots ask for their class's starter weapon on every swing, so the answer is cached
    /// per class - including "none" - and dropped whenever the starter equipment is reloaded.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_StarterWeaponCache
    {
        private static readonly BindingFlags Hidden = BindingFlags.Static | BindingFlags.NonPublic;

        public class StarterReads : DispatchProxy
        {
            public static int StarterLoads;
            public static List<StarterEquipment> Rows = new();

            protected override object Invoke(MethodInfo method, object[] args)
            {
                Type type = method.ReturnType;
                if (method.Name == "SelectAllObjects" && type.IsGenericType && type.GetGenericArguments()[0] == typeof(StarterEquipment))
                {
                    StarterLoads++;
                    return new List<StarterEquipment>(Rows);
                }
                return type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type)
                    ? Activator.CreateInstance(typeof(List<>).MakeGenericType(type.GetGenericArguments()[0])) : null;
            }
        }

        private sealed class Server : GameServer
        {
            private static readonly IObjectDatabase Db = DispatchProxy.Create<IObjectDatabase, StarterReads>();
            protected override IObjectDatabase DataBaseImpl => Db;
        }

        private GameServer _previous;

        [SetUp]
        public void SetUp()
        {
            _previous = GameServer.Instance;
            GameServer.LoadTestDouble((Server)RuntimeHelpers.GetUninitializedObject(typeof(Server)));
            StarterReads.StarterLoads = 0;
            StarterReads.Rows = new List<StarterEquipment> { Row("2", "armsman_sword") };
        }

        [TearDown]
        public void TearDown()
        {
            // leave no test rows behind: the next caller loads the starter equipment again
            ((IDictionary)typeof(CreationStartupEquipment).GetField("m_cachedClassEquipment", Hidden).GetValue(null)).Clear();
            ((IDictionary)typeof(CreationStartupEquipment).GetField("s_starterWeapons", Hidden).GetValue(null)).Clear();
            GameServer.LoadTestDouble(_previous);
        }

        private static StarterEquipment Row(string classes, string id) => new()
        {
            Class = classes,
            TemplateID = id,
            Template = new DbItemTemplate { Id_nb = id, Item_Type = Slot.RIGHTHAND, Object_Type = (int)eObjectType.SlashingWeapon },
        };

        [Test]
        public void TheSameTemplateComesBackWithoutAnotherLookUp()
        {
            CreationStartupEquipment.InitStarterEquipment();
            DbItemTemplate first = CreationStartupEquipment.GetStarterWeaponTemplate(eCharacterClass.Armsman);
            Assert.Multiple(() =>
            {
                Assert.That(first?.Id_nb, Is.EqualTo("armsman_sword"));
                Assert.That(CreationStartupEquipment.GetStarterWeaponTemplate(eCharacterClass.Armsman), Is.SameAs(first));
                Assert.That(StarterReads.StarterLoads, Is.EqualTo(1), "no reload per swing");
            });
        }

        [Test]
        public void AClassWithNoStarterWeaponIsCachedAsNone()
        {
            CreationStartupEquipment.InitStarterEquipment();
            Assert.That(CreationStartupEquipment.GetStarterWeaponTemplate(eCharacterClass.Paladin), Is.Null);
            int loads = StarterReads.StarterLoads;
            for (int swing = 0; swing < 5; swing++)
                Assert.That(CreationStartupEquipment.GetStarterWeaponTemplate(eCharacterClass.Paladin), Is.Null);
            Assert.That(StarterReads.StarterLoads, Is.EqualTo(loads), "a class without one does not reload the table on every swing");
        }

        [Test]
        public void ReloadingTheStarterEquipmentDropsTheCache()
        {
            CreationStartupEquipment.InitStarterEquipment();
            DbItemTemplate before = CreationStartupEquipment.GetStarterWeaponTemplate(eCharacterClass.Armsman);
            StarterReads.Rows = new List<StarterEquipment> { Row("2", "armsman_new_sword") };
            CreationStartupEquipment.InitStarterEquipment();
            DbItemTemplate after = CreationStartupEquipment.GetStarterWeaponTemplate(eCharacterClass.Armsman);
            Assert.Multiple(() =>
            {
                Assert.That(before?.Id_nb, Is.EqualTo("armsman_sword"));
                Assert.That(after?.Id_nb, Is.EqualTo("armsman_new_sword"), "the /refresh reload is seen at once");
            });
        }
    }
}
