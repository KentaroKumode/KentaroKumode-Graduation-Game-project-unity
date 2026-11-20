using ItemSystem.PassiveSkills.Keywords;

namespace ItemSystem.PassiveSkills.Effects
{
    // ============================================================
    //  軸の品 (2026-11-06 → 11-07 組み直し)。 設計は docs/GAME.md §8-7。
    //
    //  再編 (2026-10-31) の品は大半が「軸 X を軸 Y に換える」変換器で、 安い変換器を全部買うと
    //  毒・出血・臨界・主攻撃が 1 つの機関に繋がった (クリアしたランの 64% が 3 経路を同時に使っていた)。
    //  軸 (会心・出血・毒・臨界・充電・シールド) ごとに 3 種の品を置く:
    //    燃料   — 配線を払って資源を得る。
    //    ① 数を参照 — その軸の品の数 (心眼 / 血晶 / 瘴気 / 炉温 / 電流 / 堅牢) に応じて資源が増える。
    //    ②【規則】— その軸の性質を変える。 強さは同じ数で決まる (① と連動して、 深く積むほど効く)。
    //  出目 (攻撃に挿した出目 +%) はどの構成にも効くので軸にしない。
    // ============================================================

    internal static class Ax
    {
        public static int Count(CombatContext ctx, string key) => (int)ctx.GetAccumulated(key);
        public static int AtkDice(CombatContext ctx) => Count(ctx, MutualKeys.AttackDiceCount);
        public static int BlockDice(CombatContext ctx) => Count(ctx, MutualKeys.BlockDiceCount);
        public static int DiceTotal(CombatContext ctx) => ctx.playerDice != null ? ctx.playerDice.Length : 3;
    }

    /// <summary>軸の品の種類 (BOT の購入判断が読む・2026-11-07)。 ITT は品をランダムに配った平均で序列を作るので、
    /// 絞った構成でだけ価値が出る ① 数を参照・②【規則】は低く出る。 Super はこの表で「軸が深ければ買う」を判断する。</summary>
    public static class AxisCatalog
    {
        public enum Kind { Count, Rule }
        public static readonly System.Collections.Generic.Dictionary<string, (string axis, Kind kind)> Items
            = new System.Collections.Generic.Dictionary<string, (string, Kind)>
        {
            ["研ぎ師の見立て書"] = (AxisRules.Crit, Kind.Count),   [PinpointEye.Id] = (AxisRules.Crit, Kind.Rule),
            ["血止め知らずの針箱"] = (AxisRules.Bleed, Kind.Count),
            // 〈重ね斬りの型〉〈凝血の小瓶〉は 2026-11-20 (段 1-a) に【規則】の品でなくなったので、 表から外した (軸の普通の品として買う)
            ["瘴気溜まりの壺"] = (AxisRules.Poison, Kind.Count),   ["腐れ沼の苔玉"] = (AxisRules.Poison, Kind.Rule),
            ["焚き足しの炭"] = (AxisRules.Heat, Kind.Count),         ["火花読みの眼鏡"] = (AxisRules.Heat, Kind.Rule),
            ["漏電する銅線"] = (AxisRules.Charge, Kind.Count),     ["二股の導線"] = (AxisRules.Charge, Kind.Rule),
            ["油染みの盾革"] = (AxisRules.Shield, Kind.Count),   [ShieldPushArt.Id] = (AxisRules.Shield, Kind.Rule),
            // 追加 34 品のうち軸の品の数を読む品 (AxisItemEffects2.cs)
            ["千里眼の水晶"] = (AxisRules.Crit, Kind.Count), ["雷鳴の太鼓"] = (AxisRules.Charge, Kind.Count),
            ["涸れぬ血晶"] = (AxisRules.Bleed, Kind.Count), ["瘴気の外套"] = (AxisRules.Poison, Kind.Count),
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
        /// <summary>包んでいる品の効果 (秘宝の列 #320 が見え方を読む)。</summary>
        public IPassiveSkillEffect Inner => _inner;
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
                case When.PlayerHit:   return Rw.PlayerHit(ctx) ? 1 : 0;
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
            ("百人切りの鉈", When.PlayerHit,     false, 1, Gate.Free, 0),
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
            ("点火スパナ", When.PlayerHit,     true, 2, Gate.Free, 0),   // 2026-11-20: 旧 +3
        };
    }

    // ============================================================
    //  会心 (心眼)
    // ============================================================

    /// <summary>鷹匠の革手袋 (燃料・2026-11-20 作り直し): 攻撃の端子に 4 以上の目を挿したターン、 会心率 +20%・会心倍率 +0.30。
    /// 旧: 条件なしで会心率 +15%・会心倍率 +0.25 (毎ターン必ず働く形だった)。</summary>
    public class FalconerGlove : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "鷹匠の革手袋";
        public const int MinFace = 4;
        /// <summary>秘宝の列 (#320) の見え方。</summary>
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(Ax.Count(ctx, MutualKeys.AttackDiceMax) >= MinFace);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired, PassiveSkillTrigger.OnCriticalCheck };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (Ax.Count(ctx, MutualKeys.AttackDiceMax) < MinFace) return;
            if (t == PassiveSkillTrigger.OnWired) ctx.critRatePctAdd += 0.20f;
            else ctx.criticalMultiplier += 0.30f;   // 会心倍率は撃ごとに戻るので、 撃ごとに足す
        }
    }

    // 〈研ぎ師の見立て書〉(①: 心眼 1 につき会心倍率 +0.15) は 2026-11-11 に items.json の stats へ移した (段 3・per=axis:会心)。

    /// <summary>重ね斬りの型 (2026-11-20 作り直し): 同じターンに会心が 2 撃続いたら、 次の撃は会心倍率 +1.0 (その撃が会心した時に乗る)。
    /// 3 撃続けば 4 撃目にも乗る (直前の 2 撃が会心か で決める)。 旧【規則】: 会心率が 100% を超えた分の確率で、 会心がもう一度重なる (会心倍率 +1.0)。</summary>
    public class LayeredCutForm : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "重ね斬りの型";
        public const float BonusMul = 1.0f;
        /// <summary>[perTurn 相当] 続いた会心の数 / その数を数えたターンの番号 (ターンが変わったら 0 から)。</summary>
        private const string StreakKey = "ax.layeredCut.streak", TurnKey = "ax.layeredCut.turn";
        /// <summary>秘宝の列 (#320) の見え方。</summary>
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(Streak(ctx) >= 2);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnCriticalCheck, PassiveSkillTrigger.OnPostDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (!ctx.IsRealHit) return;
            if (t == PassiveSkillTrigger.OnCriticalCheck)
            {
                if (Streak(ctx) >= 2) ctx.criticalMultiplier += BonusMul;   // 会心倍率は撃ごとに戻るので、 この撃だけに乗る
                return;
            }
            // OnPostDealDamage: この撃の結果で数え直す
            ctx.accumulatedValues[StreakKey] = ctx.isCritical && ctx.finalDamage > 0 ? Streak(ctx) + 1 : 0;
            ctx.accumulatedValues[TurnKey] = ctx.currentTurn;
        }

        private static int Streak(CombatContext ctx)
            => Ax.Count(ctx, TurnKey) == ctx.currentTurn ? Ax.Count(ctx, StreakKey) : 0;
    }

    /// <summary>斬り重ねの鞘: 会心を出すたび、 会心倍率 +0.25 が 1 つ積もる (2026-11-07: 旧 +0.15)。 ターン開始時に積もった分の 1/4 を失う。
    /// 2026-11-07: 減衰を足した (旧は戦闘の間ずっと積もり、 ヴェスカ 4 段で 30〜40 回分 = 倍率 +4.5〜6 になり得た)。</summary>
    public class LayeredScabbard : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "斬り重ねの鞘";
        /// <summary>秘宝の列 (#320) の見え方。</summary>
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Count(Ax.Count(ctx, Key));
        private const string Key = "ax.scabbard";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnCriticalDamage, PassiveSkillTrigger.OnTurnStart, PassiveSkillTrigger.OnCriticalCheck };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            int n = Ax.Count(ctx, Key);
            switch (t)
            {
                case PassiveSkillTrigger.OnCriticalDamage: ctx.accumulatedValues[Key] = n + 1; break;
                case PassiveSkillTrigger.OnTurnStart:      ctx.accumulatedValues[Key] = n - (n + 3) / 4; break;      // 積もった分の 1/4 を失う (端数は切り上げて減らす・2026-11-15 #320: 旧 n − n/4 は 3 から下へ減らなかった)
                default:                                   ctx.criticalMultiplier += 0.25f * n; break;
            }
        }
    }

    /// <summary>一点読みの片眼【規則】: 会心率 +30%。 代わりに会心しなかった攻撃の与ダメ −30%。
    /// 2026-11-07: 代価を「充電・臨界を封じる」から変更 ── 会心はどの軸にも重ねて使う横断の軸で、 他の軸と排他にしない。
    /// 代価は PassiveSkillManager の会心判定の後で掛ける (<see cref="ApplyCost"/> が <see cref="NonCritCutKey"/> を立てる)。</summary>
    public class PinpointEye : IPassiveSkillEffect
    {
        public const string Id = "一点読みの片眼";
        public const string NonCritCutKey = "ax.pinpoint.noncrit";
        public const int NonCritKeepPct = 70;
        public string SkillId => Id;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => ctx.critRatePctAdd += 0.30f;
        /// <summary>代価の印を<b>戦闘の情報を作った直後</b>に所持品から立てる (PassiveSkillManager.BeginCombat)。</summary>
        public static void ApplyCost(CombatContext ctx, GameLoop.RunState run)
        {
            if (ctx != null && AxisRules.Owns(run, Id)) ctx.accumulatedValues[NonCritCutKey] = 1;
        }
    }

    // ============================================================
    //  出血 (血晶)
    // ============================================================

    /// <summary>返し刃の留め金 (燃料): 攻撃に 2 個以上挿したターン、 攻撃が通ると敵に出血 +2。</summary>
    public class ReturnEdgeClasp : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "返し刃の留め金";
        /// <summary>秘宝の列 (#320) の見え方。</summary>
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(env.Atk(ctx) >= 2);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (!Rw.PlayerHit(ctx) || Ax.AtkDice(ctx) < 2) return;
            ctx.AddEnemyBleed(2);
        }
    }

    /// <summary>血止め知らずの針箱 (①・2026-11-20 作り直し): 血晶 5 以上なら、 出血のダメージに会心が乗る (会心倍率の半分・会心の判定は 1 回のダメージごと)。
    /// 判定と乗せるのは CombatManager の出血 (ctx.bleedCritRule)。 旧: 血晶 1 につき、 出血のダメージ +3%。</summary>
    public class UnstanchedNeedles : IPassiveSkillEffect
    {
        public string SkillId => "血止め知らずの針箱";
        public const int MinDepth = 5;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (ctx.AxisDepth(AxisRules.Bleed) >= MinDepth) ctx.bleedCritRule = true;
        }
    }

    /// <summary>凝血の小瓶 (2026-11-20 作り直し・【規則】を外した): 敵の出血が 20 以上の間、 敵の攻撃 −20% (ctx.enemyAtkCutPct・予告の前に掛かる。 判定はターン開始時)。
    /// 旧【規則】: 出血の減り方が「1 + 出血 / 10」から「1 + 出血 / (10 + 血晶の半分)」になる。</summary>
    public class ClotVial : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "凝血の小瓶";
        public const int MinBleed = 20;
        public const float Cut = 0.20f;
        /// <summary>秘宝の列 (#320) の見え方。</summary>
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(ctx.enemyBleedStacks >= MinBleed);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (ctx.enemyBleedStacks >= MinBleed) ctx.enemyAtkCutPct += Cut;
        }
    }

    // ============================================================
    //  毒 (瘴気)
    // ============================================================

    /// <summary>毒見役の小匙 (燃料): ブロックに 2 個以上挿し、 攻撃が通ったターン、 敵に毒 +3。
    /// 2026-11-07: 旧「3 個ともブロック」は殴らずに守り続けて毒で削る形 (§24〈反攻〉の棄却理由と同じ) になり得た。</summary>
    public class TasterSpoon : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "毒見役の小匙";
        /// <summary>秘宝の列 (#320) の見え方。</summary>
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(env.Block(ctx) >= 2);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (!Rw.PlayerHit(ctx) || Ax.BlockDice(ctx) < 2) return;
            Rw.Poison(ctx, 3);
        }
    }

    /// <summary>瘴気溜まりの壺 (①): ターン開始時、 敵に毒 + 瘴気。 2026-11-07 に「瘴気の半分 (切り上げ)」から引き上げ。</summary>
    public class MiasmaJar : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "瘴気溜まりの壺";
        /// <summary>秘宝の列 (#320) の見え方。</summary>
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(ctx.AxisDepth(AxisRules.Poison) > 0);
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

    /// <summary>火花読みの眼鏡 (②【規則】): 爆発のたびに会心の判定をし、 会心なら**会心倍率の半分**が乗る (CombatManager.LandBurst・2026-11-20 ADR-0016: 旧 主攻撃の会心に合わせて乗った。
    /// 2026-11-10: 旧 会心倍率そのまま・倍率が 2 以下なら増えない)。 代わりに爆発の基礎が 30 から「15 + 炉温の半分」になる (Kw.BurstBase)。</summary>
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

    /// <summary>二度詰めの雷管: 爆発のたび、 爆発のダメージ +8 が 1 つ積もる (2026-11-20: 旧 +5・2026-11-10: 旧 +6)。 ターン開始時に積もった分の 1/8 を失う (2026-11-20: 旧 1/4・CombatContext.burstStackBonus)。</summary>
    public class RefiredPrimer : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => "二度詰めの雷管";
        /// <summary>秘宝の列 (#320) の見え方。</summary>
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Count(ctx.burstStackBonus);
        public const int PerBurst = 8;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnRinkaiBurst };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            Kw.EnableHeat(ctx);
            if (t == PassiveSkillTrigger.OnRinkaiBurst) ctx.burstStackBonus += PerBurst;
        }
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

    /// <summary>漏電する銅線 (①): ターン開始時、 充電 + 電流の半分 (切り捨て・2026-11-20: 旧 電流・2026-11-07: 旧 電流の半分)。 上限で溢れた分は捨てるだけ (〈過充電〉の出来事を配らない)。</summary>
    public class LeakyCopperWire : IPassiveSkillEffect
    {
        public string SkillId => "漏電する銅線";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
            => ctx.AddCharge(ctx.AxisDepth(AxisRules.Charge) / 2, raiseOverflow: false);
    }

    /// <summary>二股の導線 (②【規則】): 充電の端子を挿した出目の「電流 × 12%」(最大 80%) が攻撃にも加わる (2026-11-07: 旧 ×8%) (CombatManager の配線集計)。</summary>
    public class ForkedTerminal : IPassiveSkillEffect
    {
        public string SkillId => "二股の導線";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
            => ctx.chargeToAttackPct = System.Math.Min(0.8f, 0.12f * ctx.AxisDepth(AxisRules.Charge));
    }

    // ============================================================
    //  シールド (堅牢)
    // ============================================================

    /// <summary>当て布の籠手 (燃料): ブロック端子に 2 個以上挿したターン、 シールド +7 (2026-11-07: 旧 +3)。</summary>
    public class PaddedBracer : IPassiveSkillEffect, IRelicDisplay
    {
        public const string Id = "当て布の籠手";
        /// <summary>秘宝の列 (#320) の見え方。</summary>
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(env.Block(ctx) >= MinBlockDice);
        public const int MinBlockDice = 2, Shield = 7;
        public string SkillId => Id;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (Ax.BlockDice(ctx) >= MinBlockDice) Rw.Shield(ctx, Shield, SkillId);
        }
    }

    /// <summary>油染みの盾革 (①): 堅牢 1 につき、 ターン開始の減衰で残るシールド +3% (残る割合は最大 80%・2026-11-20: 旧 +5%・2026-11-07: 旧 +3%)。</summary>
    public class OiledShieldLeather : IPassiveSkillEffect
    {
        public string SkillId => "油染みの盾革";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
            => ctx.shieldKeepBonusPct = System.Math.Max(ctx.shieldKeepBonusPct, 3 * ctx.AxisDepth(AxisRules.Shield));
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
}
