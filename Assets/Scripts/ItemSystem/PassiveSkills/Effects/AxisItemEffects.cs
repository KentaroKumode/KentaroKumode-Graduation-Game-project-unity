using ItemSystem.PassiveSkills.Keywords;

namespace ItemSystem.PassiveSkills.Effects
{
    // ============================================================
    //  軸の品 (2026-11-06 → 11-07 組み直し)。 設計は docs/GAME.md §8-7。
    //
    //  再編 (2026-10-31) の品は大半が「軸 X を軸 Y に換える」変換器で、 安い変換器を全部買うと
    //  毒・出血・臨界・主攻撃が 1 つの機関に繋がった (クリアしたランの 64% が 3 経路を同時に使っていた)。
    //  軸 (会心・出血・毒・臨界・充電・シールド) ごとに 4 種の品を置く:
    //    燃料   — 配線を払って資源を得る。
    //    ① 数を参照 — その軸の品の数 (心眼 / 血晶 / 瘴気 / 炉温 / 電流 / 堅牢) に応じて資源が増える。
    //    ②【規則】— その軸の性質を変える。 強さは同じ数で決まる (① と連動して、 深く積むほど効く)。
    //    誓約   — 【規則】自分の軸を強める代わりに、 他の軸の入口を封じる。 封じは OathSeals が戦闘の情報を作った直後に立てる。
    //  出目 (攻撃に挿した出目 +%) はどの構成にも効くので軸にしない。
    // ============================================================

    internal static class Ax
    {
        public static int Count(CombatContext ctx, string key) => (int)ctx.GetAccumulated(key);
        public static int AtkDice(CombatContext ctx) => Count(ctx, MutualKeys.AttackDiceCount);
        public static int BlockDice(CombatContext ctx) => Count(ctx, MutualKeys.BlockDiceCount);
        public static int DiceTotal(CombatContext ctx) => ctx.playerDice != null ? ctx.playerDice.Length : 3;
        public static int HalfUp(int n) => (n + 1) / 2;
    }

    /// <summary>誓約の封じ。 <b>戦闘の情報を作った直後</b>に所持品から立てる (PassiveSkillManager)。
    /// 品の OnBattleStart で立てると、 遺物の開幕の毒・出血・充電や臨界の持ち越しがその前に入って漏れた。
    /// 封じは「その軸の変換器の出口」を塞ぐ形で、 誓約どうしで重ならないように割り当てた。</summary>
    public static class OathSeals
    {
        /// <summary>誓約 → 封じる軸。 <see cref="Apply"/> の割り当てと揃えること。</summary>
        public static readonly System.Collections.Generic.Dictionary<string, string[]> SealsOf = new System.Collections.Generic.Dictionary<string, string[]>
        {
            [PinpointEye.Id] = new string[0],   // 会心は他の軸と排他にしない (2026-11-07)。 代価は非会心の与ダメ −30%
            [RustOath.Id]    = new[] { AxisRules.Poison, AxisRules.Shield },
            [DregsOath.Id]   = new[] { AxisRules.Bleed,  AxisRules.Heat },
            [CoreOath.Id]    = new[] { AxisRules.Charge, AxisRules.Shield },
            [SpriteOath.Id]  = new[] { AxisRules.Heat,   AxisRules.Shield },
            [WardenOath.Id]  = new[] { AxisRules.Poison, AxisRules.Heat },
        };

        /// <summary>持っている誓約が封じる軸 (店で買っても働かない軸)。 BOT の購入判断が読む ── 人は封じた軸の品を買わない。</summary>
        public static System.Collections.Generic.HashSet<string> SealedAxes(GameLoop.RunState run)
        {
            var s = new System.Collections.Generic.HashSet<string>();
            if (run == null) return s;
            foreach (var kv in SealsOf)
                if (AxisRules.Owns(run, kv.Key)) foreach (var a in kv.Value) s.Add(a);
            return s;
        }

        public static void Apply(CombatContext ctx, GameLoop.RunState run)
        {
            if (ctx == null || run == null) return;
            if (AxisRules.Owns(run, PinpointEye.Id))  { ctx.accumulatedValues[PinpointEye.NonCritCutKey] = 1; }
            if (AxisRules.Owns(run, RustOath.Id))     { ctx.poisonSealed = true; ctx.shieldSealed = true; ctx.bleedDoubled = true; }
            if (AxisRules.Owns(run, DregsOath.Id))    { ctx.bleedSealed = true;  ctx.heatSealed = true; }
            if (AxisRules.Owns(run, CoreOath.Id))     { ctx.chargeSealed = true; ctx.shieldSealed = true; ctx.burstDoubled = true; }
            if (AxisRules.Owns(run, SpriteOath.Id))   { ctx.heatSealed = true;   ctx.shieldSealed = true; ctx.chargeDoubled = true; }
            if (AxisRules.Owns(run, WardenOath.Id))   { ctx.poisonSealed = true; ctx.heatSealed = true; }
        }
    }

    /// <summary>軸の品の種類 (BOT の購入判断が読む・2026-11-07)。 ITT は品をランダムに配った平均で序列を作るので、
    /// 絞った構成でだけ価値が出る ① 数を参照・②【規則】・誓約は低く出る。 Super はこの表で「軸が深ければ買う」を判断する。</summary>
    public static class AxisCatalog
    {
        public enum Kind { Count, Rule, Oath }
        public static readonly System.Collections.Generic.Dictionary<string, (string axis, Kind kind)> Items
            = new System.Collections.Generic.Dictionary<string, (string, Kind)>
        {
            ["研ぎ師の見立て書"] = (AxisRules.Crit, Kind.Count),   ["重ね斬りの型"] = (AxisRules.Crit, Kind.Rule),     [PinpointEye.Id] = (AxisRules.Crit, Kind.Oath),
            ["血止め知らずの針箱"] = (AxisRules.Bleed, Kind.Count), ["凝血の小瓶"] = (AxisRules.Bleed, Kind.Rule),      [RustOath.Id] = (AxisRules.Bleed, Kind.Oath),
            ["瘴気溜まりの壺"] = (AxisRules.Poison, Kind.Count),   ["腐れ沼の苔玉"] = (AxisRules.Poison, Kind.Rule),   [DregsOath.Id] = (AxisRules.Poison, Kind.Oath),
            ["焚き足しの炭"] = (AxisRules.Heat, Kind.Count),         ["火花読みの眼鏡"] = (AxisRules.Heat, Kind.Rule),    [CoreOath.Id] = (AxisRules.Heat, Kind.Oath),
            ["漏電する銅線"] = (AxisRules.Charge, Kind.Count),     ["二股の端子"] = (AxisRules.Charge, Kind.Rule),     [SpriteOath.Id] = (AxisRules.Charge, Kind.Oath),
            ["油を引いた盾革"] = (AxisRules.Shield, Kind.Count),   [ShieldPushArt.Id] = (AxisRules.Shield, Kind.Rule), [WardenOath.Id] = (AxisRules.Shield, Kind.Oath),
            // 追加 34 品のうち軸の品の数を読む品 (AxisItemEffects2.cs)
            ["千里眼の水晶"] = (AxisRules.Crit, Kind.Count), ["雷鳴の太鼓"] = (AxisRules.Charge, Kind.Count),
            ["涸れぬ泉の血晶"] = (AxisRules.Bleed, Kind.Count), ["瘴気の外套"] = (AxisRules.Poison, Kind.Count),
        };
    }

    /// <summary><b>入り口 (2026-11-07)。</b> 毒・臨界の軸の既存品に「決まった機会に資源を少し足す」効果を重ねる包み。
    /// 毒は 1 スタックが現在 HP の 0.1% と細かいので、 高い割合を出すには入り口 (毒の品) を何個も積む ── 品の数がそのまま溜まる速さになる。
    /// 品のクラスには手を入れず、 レジストリが登録の後で包む (<see cref="PassiveSkillRegistry"/>)。 品の本来の効果は内側でそのまま動く。</summary>
    public sealed class EntryEffect : IPassiveSkillEffect
    {
        public enum When { TurnStart, PlayerHit, EnemyAttacked, BlockedAll, PerBlockDie }
        /// <summary>偏重の条件 (2026-11-10)。 機会の多い入り口ほど厳しく、 少ない入り口は無条件。
        /// Free = 無条件 / AtLeast = 軸の品が n 個以上 / Top = 偏重の順位が上から n 番目まで (同数は満たす)。</summary>
        public enum Gate { Free, AtLeast, Top }
        private readonly IPassiveSkillEffect _inner;
        private readonly When _when;
        private readonly bool _heat;
        private readonly int _amount;
        private readonly Gate _gate;
        private readonly int _gateN;
        private readonly PassiveSkillTrigger[] _triggers;
        private readonly PassiveSkillTrigger _entryTrigger;

        public EntryEffect(IPassiveSkillEffect inner, When when, bool heat, int amount, Gate gate = Gate.Free, int gateN = 0)
        {
            _inner = inner; _when = when; _heat = heat; _amount = amount; _gate = gate; _gateN = gateN;
            _entryTrigger = when switch
            {
                When.TurnStart   => PassiveSkillTrigger.OnTurnStart,
                When.PlayerHit   => PassiveSkillTrigger.OnPostDealDamage,
                When.PerBlockDie => PassiveSkillTrigger.OnWired,
                _                => PassiveSkillTrigger.OnTurnEnd,   // 敵の攻撃の解決後
            };
            var list = new System.Collections.Generic.List<PassiveSkillTrigger>(inner.Triggers);
            if (!list.Contains(_entryTrigger)) list.Add(_entryTrigger);
            if (heat && !list.Contains(PassiveSkillTrigger.OnBattleStart)) list.Add(PassiveSkillTrigger.OnBattleStart);
            _triggers = list.ToArray();
        }

        public string SkillId => _inner.SkillId;
        public PassiveSkillTrigger[] Triggers => _triggers;

        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (_heat && t == PassiveSkillTrigger.OnBattleStart) Kw.EnableHeat(ctx);
            if (t == _entryTrigger)
            {
                int n = GateOpen(ctx) ? Times(ctx) : 0;
                if (n > 0)
                {
                    if (_heat) Kw.AddHeat(ctx, _amount * n);
                    else Rw.Poison(ctx, _amount * n);
                }
            }
            if (System.Array.IndexOf(_inner.Triggers, t) >= 0) _inner.Execute(t, ctx);
        }

        private bool GateOpen(CombatContext ctx)
        {
            string axis = _heat ? AxisRules.Heat : AxisRules.Poison;
            switch (_gate)
            {
                case Gate.AtLeast: return ctx.AxisDepth(axis) >= _gateN;
                case Gate.Top:     return ctx.AxisRank(axis) < _gateN;
                default:           return true;
            }
        }

        private int Times(CombatContext ctx)
        {
            switch (_when)
            {
                case When.TurnStart:   return 1;
                case When.PlayerHit:   return Rw.PlayerHit(ctx) && Rw.OncePerTurn(ctx, "ax.entry." + SkillId) ? 1 : 0;
                case When.PerBlockDie: return Ax.BlockDice(ctx);
                case When.EnemyAttacked:
                    return Ax.Count(ctx, MutualKeys.EnemyAtkPreDefense) > 0 ? 1 : 0;
                default: // BlockedAll: 敵が攻撃してきて、 HP を 1 も減らさずに受けきった
                    return Ax.Count(ctx, MutualKeys.EnemyAtkPreDefense) > 0 && Ax.Count(ctx, MutualKeys.HpLossFromEnemy) <= 0 ? 1 : 0;
            }
        }

        /// <summary>入り口の一覧 (品の id, 機会, 臨界か, 量, 偏重の条件, 条件の数)。 説明文 (items.json) と揃えること。
        /// <para>条件の付け方 (2026-11-10): 機会が多い (ターン開始・ブロックのダイスごと・敵の攻撃のたび) ほど厳しく、
        /// 出目を払う (攻撃が通る) か機会が少ない (受けきり) 入り口は無条件。 数は一律にしない ── 一律だと超えた瞬間に全部が点く。
        /// 絶対数の線は 2026-11-10 の実測 (配布なしの終了時: 瘴気 中央 5・P90 9 / 炉温 中央 6・P90 10) から置いた。</para></summary>
        public static readonly (string id, When when, bool heat, int amount, Gate gate, int gateN)[] Table =
        {
            // 毒の軸 (瘴気)
            ("毒固めの腕環", When.TurnStart,     false, 1, Gate.Top, 1),
            ("主より長い香炉", When.TurnStart,     false, 1, Gate.Top, 2),
            ("廃庭の霧吹き", When.EnemyAttacked, false, 1, Gate.AtLeast, 8),
            ("石抜きの毒指輪", When.EnemyAttacked, false, 1, Gate.AtLeast, 6),
            ("岸上げの麻痺瓶", When.BlockedAll,    false, 2, Gate.Free, 0),
            ("百一人切りの鉈", When.PlayerHit,     false, 1, Gate.Free, 0),
            ("腐し刃", When.PlayerHit,     false, 1, Gate.Free, 0),
            ("遅効の毒刃", When.PlayerHit,     false, 1, Gate.Free, 0),
            // 臨界の軸 (炉温・自分では臨界を作らない品)
            ("工廠の材料箱", When.TurnStart,     true, 3, Gate.Top, 1),
            ("希望の灯片", When.TurnStart,     true, 3, Gate.Top, 2),
            ("帳簿外の爆薬", When.TurnStart,     true, 3, Gate.AtLeast, 10),
            ("恒温の竈", When.TurnStart,     true, 3, Gate.AtLeast, 8),
            ("無害灯", When.TurnStart,     true, 3, Gate.AtLeast, 6),
            ("恒熱の炉壁", When.EnemyAttacked, true, 3, Gate.Top, 2),
            ("早沸かし釜", When.EnemyAttacked, true, 3, Gate.AtLeast, 7),
            ("八日目の灰燼", When.EnemyAttacked, true, 3, Gate.AtLeast, 5),
            ("溢れ取りの鋳型", When.PlayerHit,     true, 3, Gate.Free, 0),
            ("烈刃", When.PlayerHit,     true, 3, Gate.Free, 0),
            ("二度目の打金", When.PlayerHit,     true, 3, Gate.Free, 0),
            ("焦げ柄の点火スパナ", When.PlayerHit,     true, 3, Gate.Free, 0),
        };
    }

    // ============================================================
    //  会心 (心眼)
    // ============================================================

    /// <summary>鷹匠の革手袋 (燃料): 会心率 +15% (2026-11-07: 旧 +8%)・会心倍率 +0.25 (倍率は 2026-11-07 に追加・+0.15 → +0.25)。</summary>
    public class FalconerGlove : IPassiveSkillEffect
    {
        public string SkillId => "鷹匠の革手袋";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired, PassiveSkillTrigger.OnCriticalCheck };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnWired) ctx.critRatePctAdd += 0.15f;
            else ctx.criticalMultiplier += 0.25f;
        }
    }

    // 〈研ぎ師の見立て書〉(①: 心眼 1 につき会心倍率 +0.15) は 2026-11-11 に items.json の stats へ移した (段 3・per=axis:会心)。

    /// <summary>重ね斬りの型 (②【規則】): 会心率が 100% を超えた分の確率で、 会心がもう一度重なる (会心倍率 +1.0)。
    /// 判定は PassiveSkillManager の会心倍率を掛ける地点。</summary>
    public class LayeredCutForm : IPassiveSkillEffect
    {
        public string SkillId => "重ね斬りの型";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => ctx.critOverflowStack = true;
    }

    /// <summary>斬り重ねの鞘: 会心を出すたび、 会心倍率 +0.25 が 1 つ積もる (2026-11-07: 旧 +0.15)。 ターン開始時に積もった分の 1/4 を失う。
    /// 2026-11-07: 減衰を足した (旧は戦闘の間ずっと積もり、 ヴェスカ 4 段で 30〜40 回分 = 倍率 +4.5〜6 になり得た)。</summary>
    public class LayeredScabbard : IPassiveSkillEffect
    {
        public string SkillId => "斬り重ねの鞘";
        private const string Key = "ax.scabbard";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnCriticalDamage, PassiveSkillTrigger.OnTurnStart, PassiveSkillTrigger.OnCriticalCheck };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            int n = Ax.Count(ctx, Key);
            switch (t)
            {
                case PassiveSkillTrigger.OnCriticalDamage: ctx.accumulatedValues[Key] = n + 1; break;
                case PassiveSkillTrigger.OnTurnStart:      ctx.accumulatedValues[Key] = n - n / 4; break;
                default:                                   ctx.criticalMultiplier += 0.25f * n; break;
            }
        }
    }

    /// <summary>一点読みの片眼【規則・誓約】: 会心率 +30%。 代わりに会心しなかった攻撃の与ダメ −30%。
    /// 2026-11-07: 代価を「充電・臨界を封じる」から変更 ── 会心はどの軸にも重ねて使う横断の軸で、 他の軸と排他にしない。
    /// 代価は PassiveSkillManager の会心判定の後で掛ける (OathSeals.Apply が <see cref="NonCritCutKey"/> を立てる)。</summary>
    public class PinpointEye : IPassiveSkillEffect
    {
        public const string Id = "一点読みの片眼";
        public const string NonCritCutKey = "ax.pinpoint.noncrit";
        public const int NonCritKeepPct = 70;
        public string SkillId => Id;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => ctx.critRatePctAdd += 0.30f;
    }

    // ============================================================
    //  出血 (血晶)
    // ============================================================

    /// <summary>返し刃の留め金 (燃料): 攻撃に 2 個以上挿したターン、 攻撃が通ると敵に出血 +2。</summary>
    public class ReturnEdgeClasp : IPassiveSkillEffect
    {
        public string SkillId => "返し刃の留め金";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (!Rw.PlayerHit(ctx) || Ax.AtkDice(ctx) < 2) return;
            if (Rw.OncePerTurn(ctx, "ax.clasp")) ctx.AddEnemyBleed(2);
        }
    }

    /// <summary>血止め知らずの針箱 (①): 血晶 1 につき、 出血のダメージ +3%。</summary>
    public class UnstanchedNeedles : IPassiveSkillEffect
    {
        public string SkillId => "血止め知らずの針箱";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
            => ctx.bleedDamagePct += 0.03f * ctx.AxisDepth(AxisRules.Bleed);
    }

    /// <summary>凝血の小瓶 (②【規則】): 出血の減り方が「1 + 出血 / 10」から「1 + 出血 / (10 + 血晶の半分)」になる。</summary>
    public class ClotVial : IPassiveSkillEffect
    {
        public string SkillId => "凝血の小瓶";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
            => ctx.bleedDecayDivisorBonus += ctx.AxisDepth(AxisRules.Bleed) / 2;   // 〈止血禁じの軟膏〉の +3 と足し合わせる
    }

    /// <summary>赤錆の誓約【規則・誓約】: 敵に与える出血が 2 倍になる。 代わりに毒を与えられず、 シールドを得られない (OathSeals・2026-11-07 に「会心が出ず」から変更)。</summary>
    public class RustOath : IPassiveSkillEffect
    {
        public const string Id = "赤錆の誓約";
        public string SkillId => Id;
        public PassiveSkillTrigger[] Triggers => new PassiveSkillTrigger[0];
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) { }
    }

    // ============================================================
    //  毒 (瘴気)
    // ============================================================

    /// <summary>毒見役の小匙 (燃料): ブロックに 2 個以上挿し、 攻撃が通ったターン、 敵に毒 +3。
    /// 2026-11-07: 旧「3 個ともブロック」は殴らずに守り続けて毒で削る形 (§24〈反攻〉の棄却理由と同じ) になり得た。</summary>
    public class TasterSpoon : IPassiveSkillEffect
    {
        public string SkillId => "毒見役の小匙";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (!Rw.PlayerHit(ctx) || Ax.BlockDice(ctx) < 2) return;
            if (Rw.OncePerTurn(ctx, "ax.spoon")) Rw.Poison(ctx, 3);
        }
    }

    /// <summary>瘴気溜まりの壺 (①): ターン開始時、 敵に毒 + 瘴気。 2026-11-07 に「瘴気の半分 (切り上げ)」から引き上げ。</summary>
    public class MiasmaJar : IPassiveSkillEffect
    {
        public string SkillId => "瘴気溜まりの壺";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
            => Rw.Poison(ctx, ctx.AxisDepth(AxisRules.Poison));
    }

    /// <summary>腐れ沼の苔玉 (②【規則】): 毒がダメージを出したあとに残る割合が 50% から「50% + 瘴気 × 2%」(最大 70%) になる。</summary>
    public class RotMossBall : IPassiveSkillEffect
    {
        public string SkillId => "腐れ沼の苔玉";
        public const int MaxKeepPct = 70;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
            => ctx.poisonKeepPct = System.Math.Min(MaxKeepPct, 50 + 2 * ctx.AxisDepth(AxisRules.Poison));
    }

    /// <summary>澱の誓約【規則・誓約】: 敵の毒が 5 増えるたびシールド +2。 代わりに出血を与えられず、 臨界が溜まらない (OathSeals)。
    /// 2026-11-07: 旧「増えた量 × 2」は毒の付与量が約 5 倍になったので、 1 ターン +40〜60 シールドになり得た。</summary>
    public class DregsOath : IPassiveSkillEffect
    {
        public const string Id = "澱の誓約";
        public string SkillId => Id;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPoisonGain };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
            => Rw.Shield(ctx, Kw.Tally(ctx, "ax.dregs", Kw.EventAmount(ctx), 5) * 2, SkillId);
    }

    // ============================================================
    //  臨界 (炉温)
    // ============================================================

    /// <summary>火守りの火搔き棒 (燃料): 攻撃の端子を挿さなかったダイス 1 個につき臨界 +4。</summary>
    public class FirekeeperPoker : IPassiveSkillEffect
    {
        public string SkillId => "火守りの火搔き棒";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart) { Kw.EnableHeat(ctx); return; }
            int idle = Ax.DiceTotal(ctx) - Ax.AtkDice(ctx);
            if (idle > 0) Kw.AddHeat(ctx, 4 * idle);
        }
    }

    /// <summary>焚き足しの炭 (①・旧〈継ぎ目の鞴〉): 臨界を得るたび、 臨界 +1 (Kw.AddHeat の heatGainBonus)。 炉温の品が多いほど得る機会が増える。</summary>
    public class CharcoalFeeder : IPassiveSkillEffect
    {
        public string SkillId => "焚き足しの炭";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            Kw.EnableHeat(ctx);
            ctx.heatGainBonus = System.Math.Max(ctx.heatGainBonus, 1);
        }
    }

    /// <summary>火花読みの眼鏡 (②【規則】): 爆発に会心が**会心倍率の半分**で乗る (2026-11-10: 旧 会心倍率そのまま・倍率が 2 以下なら増えない)。 代わりに爆発の基礎が 30 から「15 + 炉温の半分」になる (Kw.BurstBase)。</summary>
    public class SparkReadingGlasses : IPassiveSkillEffect
    {
        public string SkillId => "火花読みの眼鏡";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            Kw.EnableHeat(ctx);
            ctx.burstCritRule = true;
        }
    }

    /// <summary>焼き増しの雷管: 爆発のたび、 爆発のダメージ +5 が 1 つ積もる (2026-11-10: 旧 +6)。 ターン開始時に積もった分の 1/4 を失う (CombatContext.burstStackBonus)。</summary>
    public class RefiredPrimer : IPassiveSkillEffect
    {
        public string SkillId => "焼き増しの雷管";
        public const int PerBurst = 5;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnRinkaiBurst };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            Kw.EnableHeat(ctx);
            if (t == PassiveSkillTrigger.OnRinkaiBurst) ctx.burstStackBonus += PerBurst;
        }
    }

    /// <summary>炉心の誓約【規則・誓約】: 爆発の基礎が 2 倍になる。 代わりに充電とシールドを得られない (OathSeals)。</summary>
    public class CoreOath : IPassiveSkillEffect
    {
        public const string Id = "炉心の誓約";
        public string SkillId => Id;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => Kw.EnableHeat(ctx);
    }

    // ============================================================
    //  充電 (電流)
    // ============================================================

    /// <summary>継ぎ足しの蓄電瓶 (燃料): 充電の端子を挿したダイス 1 個につき、 充電 +2。</summary>
    public class ToppedJar : IPassiveSkillEffect
    {
        public string SkillId => "継ぎ足しの蓄電瓶";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            int n = Ax.Count(ctx, MutualKeys.ChargeDiceCount);
            if (n > 0) ctx.AddCharge(2 * n);
        }
    }

    /// <summary>漏電する銅線 (①): ターン開始時、 充電 + 電流 (2026-11-07: 旧 電流の半分)。 上限で溢れた分は捨てるだけ (〈過充電〉の出来事を配らない)。</summary>
    public class LeakyCopperWire : IPassiveSkillEffect
    {
        public string SkillId => "漏電する銅線";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
            => ctx.AddCharge(ctx.AxisDepth(AxisRules.Charge), raiseOverflow: false);
    }

    /// <summary>二股の端子 (②【規則】): 充電の端子を挿した出目の「電流 × 12%」(最大 80%) が攻撃にも加わる (2026-11-07: 旧 ×8%) (CombatManager の配線集計)。</summary>
    public class ForkedTerminal : IPassiveSkillEffect
    {
        public string SkillId => "二股の端子";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
            => ctx.chargeToAttackPct = System.Math.Min(0.8f, 0.12f * ctx.AxisDepth(AxisRules.Charge));
    }

    /// <summary>雷精の誓約【規則・誓約】: 得る充電が 2 倍になる。 代わりに臨界が溜まらず、 シールドを得られない (OathSeals)。</summary>
    public class SpriteOath : IPassiveSkillEffect
    {
        public const string Id = "雷精の誓約";
        public string SkillId => Id;
        public PassiveSkillTrigger[] Triggers => new PassiveSkillTrigger[0];
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) { }
    }

    // ============================================================
    //  シールド (堅牢)
    // ============================================================

    /// <summary>当て布の籠手 (燃料): ブロック端子に 2 個以上挿したターン、 シールド +7 (2026-11-07: 旧 +3)。</summary>
    public class PaddedBracer : IPassiveSkillEffect
    {
        public const string Id = "当て布の籠手";
        public const int MinBlockDice = 2, Shield = 7;
        public string SkillId => Id;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (Ax.BlockDice(ctx) >= MinBlockDice) Rw.Shield(ctx, Shield, SkillId);
        }
    }

    /// <summary>油を引いた盾革 (①): 堅牢 1 につき、 ターン開始の減衰で残るシールド +5% (残る割合は最大 80%・2026-11-07: 旧 +3%)。</summary>
    public class OiledShieldLeather : IPassiveSkillEffect
    {
        public string SkillId => "油を引いた盾革";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
            => ctx.shieldKeepBonusPct = System.Math.Max(ctx.shieldKeepBonusPct, 5 * ctx.AxisDepth(AxisRules.Shield));
    }

    /// <summary>盾押しの心得 (②【規則】): 攻撃の端子を挿したターン、 シールドの「堅牢 × 5%」が攻撃に加わる (2026-11-11: 10% → 5%・2026-11-07: 2% → 10%)。 シールドは減らない。
    /// 2026-11-11: ヴェスカ戦の主攻撃の基礎の 31% をこの 1 品が出していた (シールドは減らず、 毎ターン満額が乗る)。</summary>
    public class ShieldPushArt : IPassiveSkillEffect
    {
        public const string Id = "盾押しの心得";
        public const float PctPerItem = 0.05f;
        public string SkillId => Id;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (Ax.AtkDice(ctx) <= 0 || ctx.consShield <= 0) return;
            int add = UnityEngine.Mathf.FloorToInt(ctx.consShield * PctPerItem * ctx.AxisDepth(AxisRules.Shield));
            if (add > 0) ctx.AddPlayerAttackOrDiceBonus(add);
        }
    }

    /// <summary>城番の誓約【規則・誓約】: ターン終了時、 そのターンのブロックの半分がシールドとして残る。 代わりに毒を与えられず、 臨界が溜まらない (OathSeals)。</summary>
    public class WardenOath : IPassiveSkillEffect
    {
        public const string Id = "城番の誓約";
        public string SkillId => Id;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => Rw.Shield(ctx, ctx.blockSumThisTurn / 2, SkillId);
    }
}
