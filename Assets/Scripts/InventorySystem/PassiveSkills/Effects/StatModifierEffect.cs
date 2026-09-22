using System.Collections.Generic;

namespace InventorySystem.PassiveSkills
{
    /// <summary>
    /// items.json の <c>passiveSkills[].stats[].stat</c> に書けるステータスのキー (2026-09-19)。
    ///
    /// <para><b>値は画面に出す単位のまま書く</b>: 攻撃 +5 → 5 / 与ダメ +10% → 10 /
    /// 会心率 +5% → 5 / 被ダメ −3 → −3 / 被ダメ −15% → −15 / 会心倍率 −0.5 → −0.5。</para>
    /// </summary>
    public static class StatKeys
    {
        public const string Attack         = "atk";         // 攻撃 +N
        public const string DamagePct      = "dmgPct";      // 与ダメ +N%
        public const string NonCritPct     = "nonCritPct";  // 非会心ダメ +N% (非会心の判定はシステム側)
        public const string CritPct        = "critPct";     // 会心率 +N%
        public const string CritMul        = "critMul";     // 会心倍率 +N
        public const string CritSure       = "critSure";    // 会心確定 (value は 1 を書く・会心率と天井を無視)
        public const string DamageTaken    = "dmgTaken";    // 被ダメ N (負で軽減)
        public const string DamageTakenPct = "dmgTakenPct"; // 被ダメ N% (負で軽減・端数切り上げ)
        public const string ShieldStart    = "shieldStart"; // 開幕シールド +N
        public const string Regen          = "regen";       // ターン終了時 HP +N
        public const string Lifesteal      = "lifesteal";   // 与ダメの N% を回復

        public static readonly HashSet<string> All = new HashSet<string>
        { Attack, DamagePct, NonCritPct, CritPct, CritMul, CritSure, DamageTaken, DamageTakenPct, ShieldStart, Regen, Lifesteal };
    }
}

namespace InventorySystem.PassiveSkills.Effects
{
    /// <summary>
    /// <b>ステータスで表せる効果の汎用実装</b> (2026-09-19)。 items.json の <c>stats</c> から作る。
    ///
    /// <para><b>表示は合算、計算は 1 件ずつ。</b> 被ダメ軽減は効果ごとに端数を処理してから
    /// 次へ渡しているので、 合算して一度に掛けると結果が変わる。 そこで旧クラス
    /// (MightI / SwordReachIII / BulwarkIII …) と<b>同じトリガー・同じ式</b>をステータス 1 件ずつ当てる。</para>
    ///
    /// <para><b>条件 (<c>when</c>) はステータスが効くトリガーの時点で判定する</b> ── 旧クラスと同じ。
    /// 〈切り返し〉の「前のターンに被弾」と〈一心不乱〉の「非会心の連続」だけは状態を持つので、
    /// そのための追加トリガー (OnPreReceiveDamage / OnPostDealDamage) で記録する。</para>
    ///
    /// <para><b>固有部分を持つスキル</b> (〈処刑〉のダイス潰し等) は専用クラスを <c>inner</c> に包む。
    /// 同じトリガーでは inner → ステータスの順に当てる。</para>
    ///
    /// <para><b>パッシブ ID は旧クラスのものを引き継ぐ</b> (例 "間合III")。 発火順は登録順
    /// (＝所持品とその passiveSkills の並び) で決まり、 〈迷妄〉は ID の集合から乱数で無効化するので、
    /// ID を変えると結果が変わる。 置き換え前後で決定性ダイジェストが一致することを合格条件にしている。</para>
    /// </summary>
    public sealed class StatModifierEffect : IPassiveSkillEffect
    {
        private readonly string _id;
        private readonly StatJson[] _stats;
        private readonly StatWhen[] _when;   // 条件なしは null
        private readonly int[] _hopeMin;     // 希望段階の下限・上限 (-1 = 使わない)。 生成時に名前から引く
        private readonly int[] _hopeBelow;
        private readonly IPassiveSkillEffect _inner;
        private readonly PassiveSkillTrigger[] _triggers;
        private readonly bool _tracksHit;
        private readonly bool _tracksStreak;
        private readonly string _armedKey;
        private readonly string _streakKey;
        /// <summary>計算に使う量。 **% のステータスは生成時に 1 度だけ float へ確定させる。**
        ///
        /// <para>Mono は float の演算を double の精度のまま進めることがあり、 実行時に
        /// <c>v / 100f</c> を足すと float の <c>0.2f</c> ではなく double の <c>0.2</c> が足される。
        /// 差は最下位ビット 1 つだが、 旧クラス (<c>+= 0.20f</c>) と端数処理の境目で結果が割れ、
        /// 2026-09-19 の段 1 で 10,000 ラン中 2 ランの決定性ダイジェストがずれた。
        /// float の配列に格納すれば旧クラスの定数と同じ値になる。</para></summary>
        private readonly float[] _amount;

        public StatModifierEffect(string skillId, StatJson[] stats, IPassiveSkillEffect inner = null)
        {
            _id = skillId;
            _stats = stats;
            _inner = inner;
            _armedKey = "hitArmed:" + skillId;
            _streakKey = "nonCritStreak:" + skillId;
            _amount = new float[stats.Length];
            _when = new StatWhen[stats.Length];
            _hopeMin = new int[stats.Length];
            _hopeBelow = new int[stats.Length];
            var ts = new List<PassiveSkillTrigger>();
            if (inner != null)
                foreach (var t in inner.Triggers) if (!ts.Contains(t)) ts.Add(t);
            for (int i = 0; i < stats.Length; i++)
            {
                var s = stats[i];
                _amount[i] = Amount(s.stat, s.value);
                _when[i] = s.when == null || s.when.IsEmpty ? null : s.when;
                _hopeMin[i] = HopeTierOf(skillId, s.when?.hopeTierMin);
                _hopeBelow[i] = HopeTierOf(skillId, s.when?.hopeTierBelow);
                if (_when[i] != null)
                {
                    _tracksHit    |= _when[i].hitLastTurn;
                    _tracksStreak |= _when[i].nonCritStreakMin > 0;
                }
                var t = TriggerOf(s.stat);
                if (!ts.Contains(t)) ts.Add(t);
            }
            if (_tracksHit && !ts.Contains(PassiveSkillTrigger.OnPreReceiveDamage))
                ts.Add(PassiveSkillTrigger.OnPreReceiveDamage);
            if (_tracksStreak && !ts.Contains(PassiveSkillTrigger.OnPostDealDamage))
                ts.Add(PassiveSkillTrigger.OnPostDealDamage);
            _triggers = ts.ToArray();
        }

        private static int HopeTierOf(string skillId, string name)
        {
            if (string.IsNullOrEmpty(name)) return -1;
            if (System.Enum.TryParse(name, out GameLoop.HopeTier t)) return (int)t;
            UnityEngine.Debug.LogError($"[StatModifierEffect] {skillId}: 未知の希望段階 '{name}'");
            return -1;
        }

        /// <summary>画面の値 → 計算に使う float。 % は小数 (20 → 0.2)、 被ダメ% は掛ける係数 (−15 → 0.85)。</summary>
        private static float Amount(string stat, float v)
        {
            switch (stat)
            {
                case StatKeys.DamagePct:
                case StatKeys.NonCritPct:
                case StatKeys.CritPct:
                case StatKeys.Lifesteal:      return (float)(v / 100.0);
                case StatKeys.DamageTakenPct: return (float)(1.0 + v / 100.0);
                default:                      return v;
            }
        }

        public string SkillId => _id;
        public PassiveSkillTrigger[] Triggers => _triggers;
        public IReadOnlyList<StatJson> Stats => _stats;

        /// <summary>ステータスが効くタイミング。 旧クラスと同じ。</summary>
        public static PassiveSkillTrigger TriggerOf(string stat)
        {
            switch (stat)
            {
                case StatKeys.Attack:         return PassiveSkillTrigger.OnPostRoll;
                case StatKeys.DamagePct:
                case StatKeys.NonCritPct:     return PassiveSkillTrigger.OnPreDealDamage;
                case StatKeys.CritPct:
                case StatKeys.CritMul:
                case StatKeys.CritSure:       return PassiveSkillTrigger.OnCriticalCheck;
                case StatKeys.DamageTaken:
                case StatKeys.DamageTakenPct: return PassiveSkillTrigger.OnPreReceiveDamage;
                case StatKeys.ShieldStart:    return PassiveSkillTrigger.OnBattleStart;
                case StatKeys.Regen:          return PassiveSkillTrigger.OnTurnEnd;
                case StatKeys.Lifesteal:      return PassiveSkillTrigger.OnTurnStart;
                default:
                    throw new System.ArgumentException("未知のステータス: " + stat);
            }
        }

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (_inner != null && System.Array.IndexOf(_inner.Triggers, trigger) >= 0)
                _inner.Execute(trigger, ctx);

            // 状態を持つ条件の記録。 自分の軽減より前の被ダメで判定する。
            if (_tracksHit && trigger == PassiveSkillTrigger.OnPreReceiveDamage && ctx.finalDamage > 0)
                ctx.nextTurnBuffs[_armedKey] = 1f;   // BeginNewTurn が currentBuffs へ運ぶ

            // 同じトリガーのステータスは items.json に書いた順に当てる。
            for (int i = 0; i < _stats.Length; i++)
            {
                if (TriggerOf(_stats[i].stat) != trigger) continue;
                if (_when[i] != null && !Holds(i, ctx)) continue;
                Apply(_stats[i].stat, _amount[i], ctx);
            }

            // 連続の更新は適用の後 (その攻撃の結果で数える)。
            if (_tracksStreak && trigger == PassiveSkillTrigger.OnPostDealDamage)
                ctx.accumulatedValues[_streakKey] = ctx.isCritical ? 0 : ctx.GetAccumulated(_streakKey) + 1;
        }

        /// <summary>条件の判定。 式は旧クラスのものをそのまま写している (比較の向き・整数か浮動小数か まで)。</summary>
        private bool Holds(int i, CombatContext ctx)
        {
            var w = _when[i];
            if (w.hit && ctx.finalDamage <= 0) return false;
            if (w.solo && ctx.isPairEncounter) return false;
            if (w.turnMax > 0 && ctx.currentTurn > w.turnMax) return false;
            if (w.firstRoll && !ctx.isFirstRoll) return false;
            if (w.hpPctMax > 0 || w.hpPctAbove > 0)
            {
                if (ctx.playerMaxHP <= 0) return false;
                if (w.hpPctMax > 0 && ctx.playerCurrentHP * 100 > ctx.playerMaxHP * w.hpPctMax) return false;
                if (w.hpPctAbove > 0 && ctx.playerCurrentHP * 100 <= ctx.playerMaxHP * w.hpPctAbove) return false;
            }
            if (w.enemyHpPctMax > 0)
            {
                if (ctx.enemyMaxHP <= 0) return false;
                if (ctx.enemyCurrentHP * 100 > ctx.enemyMaxHP * w.enemyHpPctMax) return false;
            }
            if (w.behind)
            {
                if (ctx.playerMaxHP <= 0 || ctx.enemyMaxHP <= 0) return false;
                float me = ctx.playerCurrentHP / (float)ctx.playerMaxHP;
                float foe = ctx.enemyCurrentHP / (float)ctx.enemyMaxHP;
                if (me >= foe) return false;
            }
            if (w.hitLastTurn
                && !(ctx.currentBuffs != null && ctx.currentBuffs.TryGetValue(_armedKey, out float armed) && armed > 0f))
                return false;
            if (w.enemyPoisoned && ctx.GetStatus(StatusTarget.Enemy, "poison") <= 0) return false;
            if (w.enemyBleedMin > 0 && ctx.enemyBleedStacks < w.enemyBleedMin) return false;
            if (w.overcharged && !(CombatSystem.CombatManager.UseMutualAttackPipeline && ctx.IsOvercharged())) return false;
            if (w.allEven && !AllEven(ctx.playerDice)) return false;
            if (w.kaleido && !Kaleido(ctx.playerDice)) return false;
            if (w.weaponPlusMin > 0 && (GameLoop.GameManager.Instance?.Run?.weaponPlus ?? 0) < w.weaponPlusMin) return false;
            if (w.nonCritStreakMin > 0 && ctx.GetAccumulated(_streakKey) < w.nonCritStreakMin) return false;
            if (w.strongFoe)
            {
                var type = MapSystem.MapManager.Instance?.CurrentNode?.type;
                if (type != MapSystem.TileType.EliteBattle && type != MapSystem.TileType.Boss) return false;
            }
            if (_hopeMin[i] >= 0 || _hopeBelow[i] >= 0)
            {
                int tier = (int)GameLoop.HopeSystem.GetTier(GameLoop.GameManager.Instance?.Run);
                if (_hopeMin[i] >= 0 && tier < _hopeMin[i]) return false;     // 段階は数字が大きいほど希望が低い
                if (_hopeBelow[i] >= 0 && tier >= _hopeBelow[i]) return false;
            }
            return true;
        }

        private static bool AllEven(int[] dice)
        {
            if (dice == null || dice.Length < 1) return false;
            foreach (var d in dice) if (d % 2 != 0) return false;
            return true;
        }

        /// <summary>全て同値 / 全て異なる / 連続 (3 個以上) のいずれか。 2 個未満は不成立。</summary>
        private static bool Kaleido(int[] dice)
        {
            if (dice == null || dice.Length < 2) return false;
            bool allSame = true;
            for (int i = 1; i < dice.Length; i++) if (dice[i] != dice[0]) { allSame = false; break; }
            bool allDistinct = true;
            var seen = new HashSet<int>();
            foreach (var d in dice) if (!seen.Add(d)) { allDistinct = false; break; }
            bool straight = dice.Length >= 3;
            if (straight)
            {
                var sorted = (int[])dice.Clone();
                System.Array.Sort(sorted);
                for (int i = 1; i < sorted.Length; i++)
                    if (sorted[i] != sorted[i - 1] + 1) { straight = false; break; }
            }
            return allSame || allDistinct || straight;
        }

        /// <param name="v">% のステータスは既に小数 (0.2 = 20%)、 被ダメ% は係数 (0.85)、 それ以外は画面の値そのまま。</param>
        private static void Apply(string stat, float v, CombatContext ctx)
        {
            switch (stat)
            {
                case StatKeys.Attack:
                    // ダイスが null のまま OnPostRoll が来る経路がある (2026-09-19 に判明)。
                    //   旧〈剛力I〉はそこで加算せず、 旧〈間合〉は加算していた ── 旧実装どうしが食い違っていた。
                    //   ステータスとしては「発火したら足す」に揃える (〈間合〉側)。
                    ctx.AddPlayerAttackOrDiceBonus((int)v);
                    break;
                case StatKeys.DamagePct:
                    if (ctx.outgoingDamageMultiplier <= 0f) ctx.outgoingDamageMultiplier = 1f;
                    ctx.outgoingDamageMultiplier += v;
                    break;
                case StatKeys.NonCritPct:
                    if (ctx.finalDamage > 0) ctx.nonCritOutgoingMultiplier += v;
                    break;
                case StatKeys.CritPct:
                    ctx.critRatePctAdd += v;
                    break;
                case StatKeys.CritMul:
                    ctx.criticalMultiplier += v;
                    break;
                case StatKeys.CritSure:
                    // 判定は ProcessDamage の最後 (乱数は通常どおり消費し、 そのあと確定で上書き)。
                    ctx.forceCritical = true;
                    break;
                case StatKeys.DamageTaken:
                    // 定額軽減 (下限 0)。 v は負の数 (−3 = 3 軽減)。
                    if (ctx.finalDamage > 0)
                        ctx.finalDamage = System.Math.Max(0, ctx.finalDamage + (int)v);
                    break;
                case StatKeys.DamageTakenPct:
                    // 割合軽減。 端数は切り上げ (軽減で 0 にはならない)。
                    if (ctx.finalDamage > 0)
                        ctx.finalDamage = System.Math.Max(0, UnityEngine.Mathf.CeilToInt(ctx.finalDamage * v));
                    break;
                case StatKeys.ShieldStart:
                    ctx.consShield += (int)v;
                    CombatSystem.ShieldDiag.Note("開幕シールド(ステータス)", (int)v);
                    ctx.shieldGainedTotal += (int)v;
                    break;
                case StatKeys.Regen:
                    ctx.playerCurrentHP = System.Math.Min(ctx.playerMaxHP, ctx.playerCurrentHP + (int)v);
                    break;
                case StatKeys.Lifesteal:
                    ctx.lifestealPct += v;
                    break;
            }
        }
    }
}
