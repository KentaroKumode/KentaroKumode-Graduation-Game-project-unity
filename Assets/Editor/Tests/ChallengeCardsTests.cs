#if UNITY_EDITOR
using System.Collections.Generic;
using CombatSystem;
using GameLoop;
using MetaProgression;
using NUnit.Framework;

namespace AutoTest.EditorTests
{
    /// <summary>挑戦カードのフルリワーク (2026-11-22・docs/challenge-cards-rework-spec.md) の契約: 引き方の表・札の数・配り方 (出現の枠)・
    /// 古いセーブの知らない id・カードを使う式 (CardRules) が札の無い時に何も変えないこと。</summary>
    [TestFixture]
    public sealed class ChallengeCardsTests
    {
        [TearDown]
        public void Clear()
        {
            CardRules.MaxWiredDice = CardRules.JustCounter = CardRules.JustWidthAdd = CardRules.DetonateMulTenths = 0;
            CardRules.BurstPenAddPct = CardRules.BurstBaseAdd = CardRules.CritExcessPct = CardRules.FirstStrikeBleed = 0;
            CardRules.DisposeBlock = CardRules.RoleBlock = CardRules.BladeAtk = CardRules.PursuitAtk = CardRules.PursuitHand = 0;
            CardRules.RebootCostCut = CardRules.OpeningDraw = 0;
            BossDown.TakenMulCard = BossDown.TakenMul;
        }

        [Test]
        public void Schedule_OnePerSlot_MonotoneRowsAndColumns_SinOnlyAtD10()
        {
            int[] prev = new int[ChallengeCards.Slots];
            for (int d = 0; d <= ChallengeCards.MaxDifficulty; d++)
            {
                var row = new int[ChallengeCards.Slots];
                foreach (var e in ChallengeCards.Schedule(d)) { Assert.That(row[e.slot], Is.EqualTo(0), $"Lv{d} 場所{e.slot} に 2 回"); row[e.slot] = e.tier; }
                for (int s = 1; s < ChallengeCards.Slots; s++) if (row[s - 1] > 0 && row[s] > 0) Assert.That(row[s - 1], Is.LessThanOrEqualTo(row[s]), $"Lv{d} 左 ≤ 右");
                for (int s = 0; s < ChallengeCards.Slots; s++)
                {
                    Assert.That(row[s], Is.GreaterThanOrEqualTo(prev[s]), $"Lv{d} 場所{s} が前の難易度より軽い");
                    Assert.That(row[s] == ChallengeCards.SinTier, Is.EqualTo(d == 10 && s == 4), $"Lv{d} 場所{s} の大罪");
                }
                prev = row;
            }
            Assert.That(ChallengeCards.Schedule(0).Count, Is.EqualTo(0));
            Assert.That(ChallengeCards.Schedule(5).Count, Is.EqualTo(5));
        }

        [Test]
        public void Catalog_16Debuffs_14Buffs_7Sins()
        {
            var deb = new HashSet<string>(); var buf = new HashSet<string>(); var sin = new HashSet<string>();
            var names = new HashSet<string>();
            int[] perTier = new int[5];
            foreach (var c in ChallengeCards.All)
            {
                if (ChallengeCards.IsSin(c.kind)) { sin.Add(c.kind); Assert.That(c.tier, Is.EqualTo(ChallengeCards.SinTier)); }
                else if (c.isBuff) buf.Add(c.kind); else { deb.Add(c.kind); perTier[c.tier]++; }
                Assert.That(ChallengeCards.Effect(c), Is.Not.Empty.And.Not.EqualTo(c.kind), c.id);
                names.Add(ChallengeCards.Name(c));
                if (c.isBuff) Assert.That(c.axis == null || System.Array.IndexOf(ItemSystem.PassiveSkills.AxisRules.Ranked, c.axis) >= 0, c.id);
            }
            Assert.That(deb.Count, Is.EqualTo(16)); Assert.That(buf.Count, Is.EqualTo(14)); Assert.That(sin.Count, Is.EqualTo(7));
            Assert.That(names.Count, Is.EqualTo(37));
            Assert.That(perTier[1], Is.EqualTo(11)); Assert.That(perTier[3], Is.EqualTo(13));
            foreach (var id in PermanentDebuffIds.AllIds) Assert.That(names.Contains(id), id);   // 大罪の名前は正本の正式名
        }

        [Test]
        public void Deal_Sin_TwoPillarsWithoutBuff()
        {
            var offer = ChallengeCards.Deal(ChallengeCards.SinTier, new List<string>());
            Assert.That(offer.Count, Is.EqualTo(2));
            foreach (var id in offer)
            {
                Assert.That(id.Contains("|"), Is.False);
                ChallengeCards.Split(id, out var d, out var b);
                Assert.That(d, Is.Not.Null); Assert.That(b, Is.Null); Assert.That(ChallengeCards.IsSin(d.kind), Is.True);
            }
            Assert.That(offer[0], Is.Not.EqualTo(offer[1]));
        }

        [Test]
        public void Deal_BuffsNeverOutsideLockedAxes_DebuffFamiliesDiffer()
        {
            ChallengeCards.ResetStats();
            var run = new RunState();
            run.lockedAxes = new List<string> { ItemSystem.PassiveSkills.AxisRules.Poison, ItemSystem.PassiveSkills.AxisRules.Crit };
            for (int i = 0; i < 400; i++)
            {
                int tier = 1 + i % 3;
                var offer = ChallengeCards.Deal(tier, new List<string>(), run);
                Assert.That(offer.Count, Is.EqualTo(2));
                ChallengeCards.Split(offer[0], out var d0, out var b0);
                ChallengeCards.Split(offer[1], out var d1, out var b1);
                Assert.That(d0.family, Is.Not.EqualTo(d1.family));
                Assert.That(b0.kind, Is.Not.EqualTo(b1.kind));
                Assert.That(b0.axis, Is.Not.Null, "1 枚目は枠の軸のバフ");
                foreach (var b in new[] { b0, b1 }) Assert.That(b.axis == null || run.lockedAxes.Contains(b.axis), b.id);
            }
            Assert.That(ChallengeCards.DealtOutsideLock, Is.EqualTo(0));
            Assert.That(ChallengeCards.DealtAxisBuffsWithLock, Is.GreaterThan(0));
            // 枠が空なら 8 軸のどれも出る
            var seen = new HashSet<string>();
            var empty = new RunState(); empty.lockedAxes = new List<string>();
            for (int i = 0; i < 600; i++)
                foreach (var o in ChallengeCards.Deal(1, new List<string>(), empty)) { ChallengeCards.Split(o, out _, out var b); if (b.axis != null) seen.Add(b.axis); }
            Assert.That(seen.Count, Is.EqualTo(8));
        }

        [Test]
        public void Deal_OwnedKindsAreNotDealtAgain()
        {
            var run = new RunState(); run.lockedAxes = new List<string>();
            var picks = new List<string> { "D:ehp:1|B:blade:1" };
            for (int i = 0; i < 200; i++)
                foreach (var o in ChallengeCards.Deal(2, picks, run))
                {
                    ChallengeCards.Split(o, out var d, out var b);
                    Assert.That(d.kind, Is.Not.EqualTo(ChallengeCards.EnemyHp));
                    Assert.That(b.kind, Is.Not.EqualTo(ChallengeCards.Blade));
                }
        }

        [Test]
        public void OldSaveIds_AreIgnored()
        {
            var picks = new List<string> { "D:judge:2|B:cat:3", "D:erode:3|B:punch:3", "D:ehp:1|B:gold:1", "D:sin_lust:4" };
            var map = ChallengeCards.Collect(picks);
            Assert.That(map.ContainsKey(ChallengeCards.EnemyHp), Is.True);
            Assert.That(map.ContainsKey(ChallengeCards.SinLust), Is.True);
            Assert.That(map.Count, Is.EqualTo(2));
            Assert.That(ChallengeCards.IsKnown("D:judge:2|B:cat:3"), Is.False);
            Assert.DoesNotThrow(() => ChallengeCards.Deal(2, picks, new RunState()));
        }

        [Test]
        public void CardRules_ZeroChangesNothing_AndEachRuleWorks()
        {
            // 色欲: 3 個目の出目は挿さらない
            var board = new List<string> { TerminalIds.Attack, TerminalIds.Attack, TerminalIds.Block };
            var env = new TerminalEnv { chargeCap = 30, hp = 50, maxHp = 50, enemyAttack = 10 };
            var free = TerminalCards.Resolve(board, new[] { 0, 1, 2 }, new[] { 3, 3, 3 }, env, new TerminalBattleState());
            Assert.That(free.blockDice + free.attackDice, Is.EqualTo(3));
            CardRules.MaxWiredDice = 2;
            var lust = TerminalCards.Resolve(board, new[] { 0, 1, 2 }, new[] { 3, 3, 3 }, env, new TerminalBattleState());
            Assert.That(lust.blockDice + lust.attackDice, Is.EqualTo(2));
            Assert.That(lust.slotOfDie[2], Is.EqualTo(-1));
            foreach (var w in TerminalWiring.Enumerate(board, 3)) { int n = 0; foreach (int x in w) if (x >= 0) n++; Assert.That(n, Is.LessThanOrEqualTo(2)); }
            CardRules.MaxWiredDice = 0;
            // 刃研ぎ: 撃ごとの攻撃 +N
            var plain = TerminalCards.Resolve(board, new[] { 0, 1, -1 }, new[] { 3, 3, 3 }, env, new TerminalBattleState());
            CardRules.BladeAtk = 4;
            var blade = TerminalCards.Resolve(board, new[] { 0, 1, -1 }, new[] { 3, 3, 3 }, env, new TerminalBattleState());
            CardRules.BladeAtk = 0;
            Assert.That(plain.attackHits.Count, Is.EqualTo(2));
            Assert.That(blade.attackAdd - plain.attackAdd, Is.EqualTo(4 * plain.attackHits.Count));
            // 手馴染み: 役が立ったターン ブロック +N (3 3 3 はゾロ目)
            CardRules.RoleBlock = 5;
            var role = TerminalCards.Resolve(board, new[] { -1, -1, 2 }, new[] { 3, 3, 3 }, env, new TerminalBattleState());
            CardRules.RoleBlock = 0;
            var roleOff = TerminalCards.Resolve(board, new[] { -1, -1, 2 }, new[] { 3, 3, 3 }, env, new TerminalBattleState());
            if (roleOff.roleNow > 0) Assert.That(role.blockAdd - roleOff.blockAdd, Is.EqualTo(5));
            // 初太刀: 1 ターン目だけ撃ごとに出血
            CardRules.FirstStrikeBleed = 3;
            var st1 = new TerminalBattleState(); st1.BeginTurn();
            var b1 = TerminalCards.Resolve(board, new[] { 0, -1, -1 }, new[] { 3, 3, 3 }, env, st1);
            Assert.That(b1.attackHitBleed[0], Is.EqualTo(3));
            var st2 = new TerminalBattleState(); st2.BeginTurn(); st2.BeginTurn();
            var b2 = TerminalCards.Resolve(board, new[] { 0, -1, -1 }, new[] { 3, 3, 3 }, env, st2);
            Assert.That(b2.attackHitBleed[0], Is.EqualTo(0));
            CardRules.FirstStrikeBleed = 0;
            // 毒仕込み・見切り・予熱・急所読み・予備電源
            Assert.That(PoisonCore.Damage(10, PoisonItems.None, 0, false), Is.EqualTo(10 * PoisonCore.DetonateMul));
            CardRules.DetonateMulTenths = 15;
            Assert.That(PoisonCore.Damage(10, PoisonItems.None, 0, false), Is.EqualTo(10 * PoisonCore.DetonateMul + 15));
            Assert.That(ShieldCore.Width(ShieldItems.None), Is.EqualTo(ShieldCore.JustWidth));
            CardRules.JustWidthAdd = 1;
            Assert.That(ShieldCore.Width(ShieldItems.None), Is.EqualTo(ShieldCore.JustWidth + 1));
            int vs0 = HeatCore.VsDefense(100, 40, HeatItems.None);
            CardRules.BurstPenAddPct = 50;
            Assert.That(HeatCore.VsDefense(100, 40, HeatItems.None), Is.EqualTo(100));   // 計 100% 無視
            Assert.That(vs0, Is.LessThan(100));
            float ex0 = CritCore.ExcessMul(1.4f, false);
            CardRules.CritExcessPct = 25;
            Assert.That(CritCore.ExcessMul(1.4f, false), Is.EqualTo(ex0 * 1.25f).Within(1e-4f));
            Assert.That(CardRules.RebootCost(5), Is.EqualTo(5));
            CardRules.RebootCostCut = 1;
            Assert.That(CardRules.RebootCost(5), Is.EqualTo(4)); Assert.That(CardRules.RebootCost(1), Is.EqualTo(1));
        }

        [Test]
        public void Ukemi_TakenMulAndWiderNotches()
        {
            Assert.That(BossDown.ApplyTaken(100, true), Is.EqualTo(150));
            BossDown.TakenMulCard = 1.20f;
            Assert.That(BossDown.ApplyTaken(100, true), Is.EqualTo(120));
            var n = new[] { 0.75f, 0.5f, 0.25f };
            Assert.That(BossDown.WidenNotches(n, 0), Is.SameAs(n));
            var w = BossDown.WidenNotches(n, 15);
            Assert.That(w.Length, Is.EqualTo(3));
            Assert.That(w[0], Is.EqualTo(1f - 0.25f * 1.15f).Within(1e-4f));
            Assert.That(w[2], Is.EqualTo(1f - 0.75f * 1.15f).Within(1e-4f));
            Assert.That(BossDown.WidenNotches(new[] { 0.1f }, 15), Is.Null);   // 0 以下になった目盛りは消える
        }

        [Test]
        public void Clog_TerminalsStoppedInDrawOrder()
        {
            var dk = new TerminalDeckState();
            dk.BeginBattle(TerminalCards.StartingDeck());
            dk.DrawTurn(TerminalCards.HandSize, null, 0f, 2);
            Assert.That(dk.LastStoppedCount, Is.EqualTo(System.Math.Min(2, dk.hand.Count)));
            var dk2 = new TerminalDeckState();
            dk2.BeginBattle(TerminalCards.StartingDeck());
            dk2.DrawTurn(TerminalCards.HandSize);
            Assert.That(dk2.LastStoppedCount, Is.EqualTo(0));
        }
    }
}
#endif
