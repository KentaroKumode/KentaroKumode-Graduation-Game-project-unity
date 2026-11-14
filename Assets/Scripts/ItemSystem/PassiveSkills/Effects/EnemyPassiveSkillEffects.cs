namespace ItemSystem.PassiveSkills.Effects
{
    // ============================================================
    //  敵専用パッシブスキル（EnemyPassiveSkillEffects）
    //  プレイヤーのスキルと同じ IPassiveSkillEffect を実装し、
    //  PassiveSkillRegistry に登録することで共通フレームワークで動作する。
    //
    //  ※ 敵スキルは「敵視点」で記述する。
    //     CombatManager が敵スキル発動時は context のプレイヤー/敵を
    //     入れ替えて呼び出す仕組みのため、ここでは「自分(=敵)が勝った」
    //     = playerWonRoll として統一的に記述できる。
    //
    //  ※ ロール勝負は無い (ADR-0009 相互攻撃モデル・GAME.md §6)。 敵の OnRollWin / OnRollLose は
    //     ターンの**解決後**に鳴る収支トリガーで、 OnRollWin = プレイヤーの収支マイナス /
    //     OnRollLose = プレイヤーの収支プラス (CombatManager.Turn.cs の収支トリガー)。
    //     下の要約に残る「ロール勝利時/敗北時」はこの意味に読み替える。
    //     敵視点の playerDiceTotal / enemyDiceTotalBonus への加算は、 敵の攻撃 +N としてそのまま入る (Escalation.EnemyAttackValue)。
    //  ※ 2026-11-11 (#30 / #104) の記述監査で、 相互攻撃モデルでの実挙動を追い切った印を付けた:
    //     [不発・確定] 読み手が無い・トリガーが敵の側で鳴らない・書いた値が読まれる前に消える /
    //     [向きが逆・確定] [漏れ・確定] 効き先が説明と逆、 またはプレイヤーにも乗る /
    //     [積み上がり・確定] 説明より多く積み上がる / [疑い] 実機で確かめないと決着しない。
    //  ※ 2026-11-11 (#104) に、 上の印を説明文どおりに直した (数値は変えていない)。 〈多頭攻撃〉だけは効果を置き換えた (クラスの注記)。
    //     読み替えの決まり (相互攻撃モデル):
    //       - OnRollWin / OnRollLose は収支トリガー (ターン解決の後) のまま。 「次ターン …」はその次のターンに効かせ、
    //         固定ダメージ・反撃はその場 (解決の後) で入れる (CombatManager.Turn.cs が収支トリガーの後に HP を拾う)。
    //       - ProcessDamage の中で鳴る OnPreDealDamage は「この陣営が攻撃しているダメージ計算」(playerWonRoll = 攻撃側)。
    //         敵の OnPreDealDamage は敵の攻撃が通る (ブロックの後に 1 以上残る) ときだけ、 敵の攻撃の計算の中で鳴る。
    //       - 次ターンへ持ち越す値は、 プレイヤーが読む共有キー (diceBonus / enemyDiceDebuff / damageBonus) に書かない。
    //         各クラスの専用キーで nextTurnBuffs に積み、 同じクラスが敵の側の計算で読む。
    // ============================================================

    // ----------------------------------------------------------
    //  1～3層: シンプルなステータス型
    // ----------------------------------------------------------

    /// <summary>罠師 — ロール勝利時、次ターン相手のダイス合計値-1
    /// <para>相互攻撃モデルでの効き方 (2026-11-11 #104 で修正): プレイヤーの収支マイナスのターン (敵視点 OnRollWin) の後、
    /// <b>次のターンのプレイヤーの攻撃 −<see cref="AttackDown"/></b>。 プレイヤーの「ダイス合計値」は攻撃に挿した出目として
    /// 攻撃へ入るので、 プレイヤーの〈呪縛〉(敵の攻撃値 −N・§9.6 D群) と同じ向きの読み替えにした。
    /// 次ターンの OnTurnStart で <c>playerAttackPowerPenalty</c> (視点スワップされない・perTurn) に足す。
    /// 消費は CombatManager.Turn.cs の atkBase からの減算 (下限 0)・予告の playerAttackPenalty にも出る。</para>
    /// <para>旧実装は共有バフ <c>nextTurnBuffs["enemyDiceDebuff"]</c> に書いており、 次ターンの RecomputeDiceTotals が
    /// <b>この敵自身</b>の出目の合計から引いていた (向きが逆)。 〈呪縛〉(Curse) も同じ形で直した。</para></summary>
    public class Trapper : IPassiveSkillEffect
    {
        public const int AttackDown = 1;
        /// <summary>nextTurnBuffs の専用キー (このクラスだけが読み書きする)。</summary>
        private const string NextTurnKey = "enemy.trapper.atkDown";
        public string SkillId => "Trapper";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRollWin, PassiveSkillTrigger.OnTurnStart };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
            => EnemyNextTurnAttackDown.Execute(trigger, ctx, NextTurnKey, AttackDown, "罠師");
    }

    /// <summary>〈罠師〉〈呪縛〉の共通処理: 収支で勝った (敵視点 OnRollWin) 次のターン、 プレイヤーの攻撃 −N。</summary>
    internal static class EnemyNextTurnAttackDown
    {
        public static void Execute(PassiveSkillTrigger trigger, CombatContext ctx, string key, int amount, string label)
        {
            if (trigger == PassiveSkillTrigger.OnRollWin)
            {
                ctx.nextTurnBuffs.TryGetValue(key, out float cur);
                ctx.nextTurnBuffs[key] = cur + amount;
                return;
            }
            // OnTurnStart: 前のターンに積んだ分 (BeginNewTurn が nextTurnBuffs → currentBuffs へ移したもの) を、 このターンの攻撃から引く
            int down = (int)ctx.GetBuff(key);
            if (down <= 0) return;
            ctx.playerAttackPowerPenalty += down;
            UnityEngine.Debug.Log($"[{label}] このターンのプレイヤーの攻撃 −{down}");
        }
    }

    /// <summary>不死者 — 毎ターン開始時、HP1回復</summary>
    public class Undying : IPassiveSkillEffect
    {
        public string SkillId => "Undying";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            ctx.playerCurrentHP = System.Math.Min(ctx.playerMaxHP, ctx.playerCurrentHP + ctx.ReduceEnemyHeal(1));
        }
    }

    /// <summary>疾駆 — 1 ターン目、 攻撃 +2 (敵視点の playerDiceTotal = 敵の攻撃値に入る出目の合計)</summary>
    public class Sprint : IPassiveSkillEffect
    {
        public string SkillId => "Sprint";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.isFirstRoll)
            {
                ctx.playerDiceTotal += 2;
            }
        }
    }

    /// <summary>剛力 — ロール勝利時、ダメージ+2
    /// <para>相互攻撃モデルでの効き方 (2026-11-11 #104 で修正): 「ロール勝利時」= この敵がダメージを与える計算。
    /// 敵の OnPreDealDamage は <c>PassiveSkillManager.ProcessDamage</c> の敵の攻撃の計算の中 (ブロックを引いた後に 1 以上残るとき)
    /// で鳴り、 敵視点の playerWonRoll が true になる。 ここで足した +2 はそのまま被ダメの補正チェーン (シールド等) を通る。
    /// プレイヤーの攻撃の計算では鳴らないので、 プレイヤー側へは漏れない。 〈闇技〉(EliteDarkKnight) も同じ。</para>
    /// <para>旧実装は敵の攻撃の前 (playerWonRoll がプレイヤー攻撃用のまま) に鳴り、 条件が立っても直後の
    /// <c>ProcessDamage(lossBase, …)</c> が finalDamage を上書きしていた。</para></summary>
    public class BruteForce : IPassiveSkillEffect
    {
        public string SkillId => "BruteForce";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.playerWonRoll)
            {
                ctx.finalDamage += 2;
            }
        }
    }

    /// <summary>飛翔 — 追撃ダメージを受けない
    /// <para>相互攻撃モデルでの効き方 (2026-11-11 #104 で修正): いまのゲームで「追撃」のダメージは役〈追撃〉(目 3・
    /// 与ダメの 30% / 100% を追加・<c>CombatManager.ApplyRoleExtraDamage</c>) だけ (<c>ctx.pursuitDamage</c> の書き手は無い)。
    /// 敵の OnPrePursuitDamage はプレイヤーの攻撃の <c>ProcessDamage</c> の中で鳴り、 ここで立てた
    /// <c>nullifyPursuitDamage</c> (perTurn) を CombatManager.Turn.cs が見て、 役〈追撃〉の追加ダメージを入れない。
    /// 〈不動〉(Immovable) も同じ。</para>
    /// <para>旧実装の時点では OnPrePursuitDamage が FireEnemyTrigger で一度も焚かれていなかった。</para></summary>
    public class Flight : IPassiveSkillEffect
    {
        public string SkillId => "Flight";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPrePursuitDamage };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            ctx.nullifyPursuitDamage = true;
        }
    }

    // ----------------------------------------------------------
    //  4～5層: 複合型
    // ----------------------------------------------------------

    /// <summary>硬鱗 — 防御 +4 (ADR-0013)。 旧: 受けるダメージ −2 (最低 0)。
    ///
    /// <para>固定減算は数十の攻撃に数 % しか効かない飾りで、 貫通の的にもならなかった。
    /// 防御値にしたので貫通で剥がせる。 敵視点のパッシブだが <c>enemyDefense</c> は視点スワップで入れ替わらないので、
    /// ここで足せば敵自身の防御になる (〈精鋭〉と同じ書き方)。</para></summary>
    public class HardScales : IPassiveSkillEffect
    {
        public const int Defense = 4;
        public string SkillId => "HardScales";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            ctx.enemyDefense += Defense;
        }
    }

    /// <summary>割れる装甲 — ダメージを受けたターンの終わりに、 自分の防御 −<see cref="Crack"/> (下限 0)。 2026-11-04。
    /// <para>初期の防御は敵データの <c>defense</c> が持つ。 早く・多く当てるほど柔らかくなる ── 貫通が無くても手数で割れる。
    /// 敵視点のパッシブだが <c>lastPlayerDamage</c> も <c>enemyDefense</c> も視点スワップで入れ替わらないので、
    /// 「プレイヤーがこのターン与えたダメージ」と「この敵の防御」をそのまま読む (〈鎧砕き〉と同じ読み方)。</para></summary>
    public class CrackingArmor : IPassiveSkillEffect
    {
        public const int Crack = 4;
        public string SkillId => "CrackingArmor";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.lastPlayerDamage > 0) ctx.enemyDefense = System.Math.Max(0, ctx.enemyDefense - Crack);
        }
    }

    /// <summary>積み上がる装甲 — 毎ターンの終わりに、 自分の防御 +<see cref="Grow"/> (<see cref="Cap"/> まで)。 2026-11-04。
    /// <para>始めは柔らかく、 長引くほど硬くなる。 早く倒すか、 貫通で上から剥がすかを問う。</para></summary>
    public class StackingArmor : IPassiveSkillEffect
    {
        public const int Grow = 3, Cap = 24;
        public string SkillId => "StackingArmor";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.enemyDefense < Cap) ctx.enemyDefense = System.Math.Min(Cap, ctx.enemyDefense + Grow);
        }
    }

    /// <summary>尾撃 — ロール敗北時、相手に1の固定ダメージ
    /// <para>相互攻撃モデルでの効き方 (2026-11-11 #104 で修正): プレイヤーの収支プラスのターン (敵視点 OnRollLose・ターン解決の後) に、
    /// その場で <see cref="Damage"/> の軽減無視ダメージを <c>EnemyDealUnmitigable</c> で入れる (シールドは肩代わりする・③-C)。
    /// CombatManager.Turn.cs が収支トリガーの後に ctx の HP を拾う (SyncHPFromContext)。 〈地獄の業火〉〈重甲〉の反射も同じ。</para>
    /// <para>旧実装は fixedDamageToEnemy (スワップ後の fixedDamageToPlayer) に積んでいたが、 その枠を読むのは敵攻撃の
    /// 解決中だけで、 読まれないまま次の BeginNewTurn で 0 に戻っていた。</para></summary>
    public class TailStrike : IPassiveSkillEffect
    {
        public const int Damage = 1;
        public string SkillId => "TailStrike";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRollLose };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            ctx.EnemyDealUnmitigable(Damage, null);   // 敵視点: enemyCurrentHP = 実プレイヤー (計上は他の軽減無視と同じく無し)
        }
    }

    /// <summary>暴走 — ロール敗北時、次ターンのダイス合計値+3
    /// <para>相互攻撃モデルでの効き方 (2026-11-11 #104 で修正): プレイヤーの収支プラスのターン (敵視点 OnRollLose) の後、
    /// <b>次のターンのこの敵の攻撃 +<see cref="Bonus"/></b>。 専用キーで nextTurnBuffs に積み、 次ターンの敵の OnPostRoll で
    /// 敵視点の playerDiceTotal (= この敵の出目の合計 → Escalation.EnemyAttackValue) に足す。 予告にもそのまま出る。</para>
    /// <para>旧実装は共有バフ <c>nextTurnBuffs["diceBonus"]</c> に書いており、 次ターンの RecomputeDiceTotals が
    /// <b>プレイヤーの</b> playerDiceTotal に足していた (向きが逆)。</para></summary>
    public class Rampage : IPassiveSkillEffect
    {
        public const int Bonus = 3;
        /// <summary>nextTurnBuffs の専用キー (書くのはこのクラスだけ。 BOT の先読みが読む)。</summary>
        public const string NextTurnKey = "enemy.rampage.atkUp";
        public string SkillId => "Rampage";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRollLose, PassiveSkillTrigger.OnPostRoll };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (trigger == PassiveSkillTrigger.OnRollLose)
            {
                ctx.nextTurnBuffs.TryGetValue(NextTurnKey, out float cur);
                ctx.nextTurnBuffs[NextTurnKey] = cur + Bonus;
                return;
            }
            // OnPostRoll (敵視点): playerDiceTotal = この敵の出目の合計
            int up = (int)ctx.GetBuff(NextTurnKey);
            if (up <= 0) return;
            ctx.playerDiceTotal += up;
            UnityEngine.Debug.Log($"[暴走] このターンの攻撃 +{up}");
        }
    }

    /// <summary>虚体 — 受ける攻撃のダメージを 50% 軽減 (主攻撃だけ。 固定ダメ・出血・毒には効かない)。
    /// 敵の OnPreReceiveDamage は CombatManager.Damage.cs の主攻撃の確定で鳴る。</summary>
    public class Ethereal : IPassiveSkillEffect
    {
        public string SkillId => "Ethereal";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreReceiveDamage };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.playerLostRoll && ctx.finalDamage > 0)
            {
                ctx.finalDamage = ctx.finalDamage / 2;
            }
        }
    }

    /// <summary>呪縛 — ロール勝利時、次ターン相手のダイス合計値-2
    /// <para>相互攻撃モデルでの効き方 (2026-11-11 #104 で修正): 〈罠師〉と同じ形で、 プレイヤーの収支マイナスのターンの後、
    /// 次のターンのプレイヤーの攻撃 −<see cref="AttackDown"/>。 旧実装は〈罠師〉と同じく、 この敵自身の攻撃を下げていた。</para></summary>
    public class Curse : IPassiveSkillEffect
    {
        public const int AttackDown = 2;
        /// <summary>nextTurnBuffs の専用キー (このクラスだけが読み書きする)。</summary>
        private const string NextTurnKey = "enemy.curse.atkDown";
        public string SkillId => "Curse";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRollWin, PassiveSkillTrigger.OnTurnStart };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
            => EnemyNextTurnAttackDown.Execute(trigger, ctx, NextTurnKey, AttackDown, "呪縛");
    }

    /// <summary>不動 — 追撃ダメージを受けない</summary>
    public class Immovable : IPassiveSkillEffect
    {
        public string SkillId => "Immovable";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPrePursuitDamage };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            ctx.nullifyPursuitDamage = true;
        }
    }

    /// <summary>反撃態勢 — ロール敗北時、次ターンのダメージ+3
    /// <para>相互攻撃モデルでの効き方 (2026-11-11 #104 で修正): プレイヤーの収支プラスのターン (敵視点 OnRollLose) の後、
    /// <b>次のターンのこの敵の攻撃のダメージ +<see cref="Bonus"/></b> (ブロックの後に 1 以上残ったとき・〈剛力〉と同じ位置)。
    /// 専用キーで nextTurnBuffs に積み、 次ターンの敵の OnPreDealDamage (敵の攻撃の ProcessDamage の中) で読む。</para>
    /// <para>旧実装は共有バフ <c>nextTurnBuffs["damageBonus"]</c> に書いており、 次ターンの ProcessDamage が
    /// <b>プレイヤーの主攻撃にも</b>敵の攻撃 (2 経路) にも +3 していた (漏れ)。</para></summary>
    public class CounterStance : IPassiveSkillEffect
    {
        public const int Bonus = 3;
        /// <summary>nextTurnBuffs の専用キー (書くのはこのクラスだけ。 BOT の先読みが読む)。</summary>
        public const string NextTurnKey = "enemy.counterStance.dmgUp";
        public string SkillId => "CounterStance";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRollLose, PassiveSkillTrigger.OnPreDealDamage };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (trigger == PassiveSkillTrigger.OnRollLose)
            {
                ctx.nextTurnBuffs.TryGetValue(NextTurnKey, out float cur);
                ctx.nextTurnBuffs[NextTurnKey] = cur + Bonus;
                return;
            }
            // OnPreDealDamage (敵視点): playerWonRoll = この敵が攻撃している計算
            if (!ctx.playerWonRoll) return;
            int up = (int)ctx.GetBuff(NextTurnKey);
            if (up <= 0) return;
            ctx.finalDamage += up;
            UnityEngine.Debug.Log($"[反撃態勢] 攻撃のダメージ +{up}");
        }
    }

    /// <summary>ダークナイト — 研ぎ澄まし: 決闘が長引くほど剣技が冴える。
    /// 3ターンごとに「ロール勝利時の与ダメージ」+1（累積）。相互火力不足
    /// によるこう着（長期戦）を、敵の決め手火力を逓増させて終局へ導く。
    /// T1-2:+0 / T3-5:+1 / T6-8:+2 / T9-11:+3 …
    /// <para>相互攻撃モデルでの効き方 (2026-11-11 #104 で修正): 「ロール勝利時の与ダメージ」は〈剛力〉〈闇技〉と同じ言い回しなので
    /// 同じ読み替え ── この敵の攻撃が通るたび (敵の OnPreDealDamage・ブロックの後に 1 以上残るとき)、 ダメージ +currentTurn/3。
    /// 旧実装の OnRollWin は「敵が与ダメするターン」の代わりとして使っていたもの (旧ロール勝負では勝った側だけがダメージを与えた)。</para>
    /// <para>旧実装は収支トリガー (ターン解決の後) で currentBuffs["damageBonus"] に書いており、 次の BeginNewTurn の
    /// currentBuffs.Clear() で捨てられていた。 書けていてもプレイヤーの主攻撃へ漏れるキーだった。</para></summary>
    public class HoningDuel : IPassiveSkillEffect
    {
        public string SkillId => "HoningDuel";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (!ctx.playerWonRoll) return;   // 敵視点: この敵が攻撃している計算だけ
            int bonus = System.Math.Max(0, ctx.currentTurn) / 3; // 3ターンごとに+1
            if (bonus <= 0) return;
            ctx.finalDamage += bonus;
            UnityEngine.Debug.Log($"[研ぎ澄まし] T{ctx.currentTurn} 攻撃のダメージ +{bonus}");
        }
    }

    /// <summary>精鋭 — enemies.json のエリート (独立した敵・GAME.md §5-4) の一部が持つ汎用強化。
    /// 防御 +2 (戦闘開始時) と「3 ターンごとに攻撃 +1 (累積)」。 HP などの数値は enemies.json が直接持つ
    /// (スポーン時に倍率を掛ける精鋭化は 2026-09-20 に廃止・GAME.md §24)。
    /// 敵視点で playerDiceTotal = 敵の攻撃値に入る出目の合計。
    /// T1-2:+0 / T3-5:+1 / T6-8:+2 / T9-11:+3 …</summary>
    public class EliteVigor : IPassiveSkillEffect
    {
        public string SkillId => "EliteVigor";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll, PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (trigger == PassiveSkillTrigger.OnBattleStart)
            {
                // エリート汎用: 防御 +2 (ADR-0013・旧 被ダメ −10%)。 貫通で剥がせる。
                ctx.enemyDefense += 2;
                return;
            }
            int bonus = System.Math.Max(0, ctx.currentTurn) / 3; // 3ターンごとに+1
            if (bonus > 0) ctx.playerDiceTotal += bonus; // 敵視点で自身のダイス合計
        }
    }

    // ==========================================================
    //  エリート固有パッシブ（基敵ごとに1種・逆スケール設計: 元々弱い敵ほど強力、強い敵ほど控えめ）。
    //  enemies.json が使っているのは EliteDecree13 / EliteLizard / EliteWraith /
    //  EliteGolem / EliteMinotaur / EliteDarkKnight。 どの敵も持っていなかった Slime/Goblin/Kobold/Skeleton/Wolf/Harpy/Orc は
    //  2026-11-12 に削除した (GAME.md §24)。
    //  敵視点: playerDiceTotal=自ダイス合計 / playerCurrentHP=自HP /
    //          enemyCurrentHP=実プレイヤーHP / finalDamage=与/被ダメ。
    // ==========================================================

    /// <summary>精鋭13番目の死 — 死の重圧: 13番目の宣告が灯ったターン中、
    /// 自ダイス合計+13（宣告フラグ decree13th_armed が立つターンのみ）。</summary>
    public class EliteDecree13 : IPassiveSkillEffect
    {
        public string SkillId => "EliteDecree13";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            // Decree13th が OnTurnStart に立てる宣告フラグ。立っているターンのみ+13。
            if (ctx.currentBuffs.TryGetValue("decree13th_armed", out var armed) && armed > 0f)
                ctx.playerDiceTotal += 13;
        }
    }

    /// <summary>精鋭リザードマン — 重甲: 防御さらに +4 (硬鱗と合わせて 8・ADR-0013)、 敗北時に固定 1 反射。
    /// 旧: 被弾時さらに −2 軽減。
    /// <para>反射 (2026-11-11 #104 で修正): 〈尾撃〉と同じく、 プレイヤーの収支プラスのターン (敵視点 OnRollLose・解決の後) に
    /// その場で <see cref="Reflect"/> の軽減無視ダメージ (シールドは肩代わりする)。 旧実装は fixedDamageToEnemy に積んで読まれずに消えていた。</para></summary>
    public class EliteLizard : IPassiveSkillEffect
    {
        public const int Defense = 4;
        public const int Reflect = 1;
        public string SkillId => "EliteLizard";
        public PassiveSkillTrigger[] Triggers => new[] {
            PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnRollLose };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (trigger == PassiveSkillTrigger.OnBattleStart) ctx.enemyDefense += Defense;
            else ctx.EnemyDealUnmitigable(Reflect, null);   // 敵視点: enemyCurrentHP = 実プレイヤー
        }
    }

    /// <summary>精鋭レイス — 霊体: 2ターンに1度（偶数ターン）「霊体状態」となり、
    /// すべての被ダメージを1に減少。霊体でないターンはダイス数+1。</summary>
    public class EliteWraith : IPassiveSkillEffect
    {
        public string SkillId => "EliteWraith";
        public PassiveSkillTrigger[] Triggers => new[] {
            PassiveSkillTrigger.OnTurnStart, PassiveSkillTrigger.OnPreReceiveDamage };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            bool ghost = ctx.currentTurn > 0 && ctx.currentTurn % 2 == 0; // 2ターンに1度
            if (trigger == PassiveSkillTrigger.OnTurnStart)
            {
                // 霊体でないときダイス数+1（NightLord と同じ extraDice 機構）
                ctx.accumulatedValues["extraDice"] = ghost ? 0f : 1f;
            }
            else if (ghost && ctx.playerLostRoll && ctx.finalDamage > 1)
            {
                ctx.finalDamage = 1; // 霊体: すべての被ダメージを1に
            }
        }
    }

    /// <summary>精鋭ストーンゴーレム — 巌の意志: 毎ターン意志スタック+1。
    /// 撃破された瞬間、スタック分の確定ダメージをプレイヤーへ。
    /// （硬鱗との二重軽減・×2反撃が過剰だったため緩和。精鋭勝率を25%域へ）。</summary>
    public class EliteGolem : IPassiveSkillEffect
    {
        public string SkillId => "EliteGolem";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        private const string WillKey = "egolem_will";
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            // OnTurnEnd: 意志+1。敵視点で playerCurrentHP=自HP。撃破時に反撃
            ctx.accumulatedValues.TryGetValue(WillKey, out var w);
            int will = (int)w + 1;
            ctx.accumulatedValues[WillKey] = will;
            if (ctx.playerCurrentHP <= 0)
            {
                int dmg = will;
                ctx.EnemyDealUnmitigable(dmg, null);   // シールドは肩代わりする (計上は従来どおり無し)
                UnityEngine.Debug.Log($"[精鋭ゴーレム・巌の意志] 撃破時反撃 意志{will} → プレイヤー残HP={ctx.enemyCurrentHP}");
            }
        }
    }

    /// <summary>精鋭ミノタウロス — 際限なき暴走: 毎ターン 攻撃 +1 (敵視点の playerDiceTotal へ・既に強い→控えめ)</summary>
    public class EliteMinotaur : IPassiveSkillEffect
    {
        public string SkillId => "EliteMinotaur";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => ctx.playerDiceTotal += 1;
    }

    /// <summary>精鋭ダークナイト — 闇技: 勝利時、与ダメ+2（4Fへ降格に伴いナーフ）
    /// <para>2026-11-11 #104 で修正: 〈剛力〉と同じ (この敵の攻撃が通る計算の中で敵の OnPreDealDamage が鳴り、 ダメージ +2)。</para></summary>
    public class EliteDarkKnight : IPassiveSkillEffect
    {
        public string SkillId => "EliteDarkKnight";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        { if (ctx.playerWonRoll) ctx.finalDamage += 2; }
    }

    // ----------------------------------------------------------
    //  6～7層: ユニーク型（高度な戦略を持つ敵専用スキル）
    // ----------------------------------------------------------

    /// <summary>多頭攻撃 — プレイヤーが攻撃よりブロックに多く挿したターン、 2 回攻撃する。
    /// <para>2026-11-11 (#104): 旧い説明「ロール勝利時、追撃ダイスを1個追加」は相互攻撃モデルに当たる物が無く不発だった
    /// (<c>extraPursuitDice</c> の読み手が無い)。 ユーザーの指定でこの効果に置き換えた。</para>
    /// <para>ここは印を立てるだけ。 2 回目の攻撃は CombatManager.Turn.cs が、 挿し方が決まった後に 1 回目と同じ攻撃値で入れる
    /// (ブロックは 2 回とも全額効く・挑戦カード〈連撃〉や 2 体戦で既に 2 回目がある時は増やさない)。</para></summary>
    public class MultiHead : IPassiveSkillEffect
    {
        public string SkillId => "MultiHead";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            ctx.accumulatedValues[MutualKeys.EnemyDoubleOnBlockHeavy] = 1f;
        }
    }

    /// <summary>再生 — 毎ターン開始時、HP1回復</summary>
    public class Regeneration : IPassiveSkillEffect
    {
        // キメラを 6F→4F に降格した際に +2→+1 へ控えめ化（現在唯一の利用元）。
        public string SkillId => "Regeneration";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            ctx.playerCurrentHP = System.Math.Min(ctx.playerMaxHP, ctx.playerCurrentHP + ctx.ReduceEnemyHeal(1));
        }
    }

    /// <summary>魔王の威圧 — 戦闘開始時、相手の最大HPを3減少</summary>
    public class DemonAura : IPassiveSkillEffect
    {
        public string SkillId => "DemonAura";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            // 敵スキルとして発動時、対象（相手）の最大HPを減少
            // CombatManager側で enemyMaxHPReduction として処理
            ctx.AddAccumulated("enemyMaxHPReduction", 3);
        }
    }

    /// <summary>地獄の業火 — ロール勝利時、追加で2の固定ダメージ
    /// <para>2026-11-11 #104 で修正: 〈尾撃〉と同じ形で、 プレイヤーの収支マイナスのターン (敵視点 OnRollWin・解決の後) に
    /// その場で <see cref="Damage"/> の軽減無視ダメージ (シールドは肩代わりする)。 旧実装は fixedDamageToEnemy に積んで読まれずに消えていた。</para></summary>
    public class Hellfire : IPassiveSkillEffect
    {
        public const int Damage = 2;
        public string SkillId => "Hellfire";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRollWin };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            ctx.EnemyDealUnmitigable(Damage, null);   // 敵視点: enemyCurrentHP = 実プレイヤー
        }
    }

    /// <summary>吸血 — ダメージを与えた時、その50%分HPを回復
    /// <para>2026-11-11 #104 で修正: 敵の OnPostDealDamage は CombatManager.Turn.cs の敵攻撃の末尾で鳴る。 その直前に
    /// ctx の HP を CombatManager の値へ揃え、 <c>ctx.finalDamage</c> に<b>この攻撃で実際に通った主攻撃のダメージ</b>
    /// (ブロック・シールド等の後) を入れてから鳴らし、 鳴らした後に ctx の HP を拾う (SyncHPFromContext)。</para>
    /// <para>旧実装は回復を ctx の HP にだけ書き、 同じターンのうちに <c>ctx.enemyCurrentHP = enemyHP</c> で上書きされて消えていた。</para></summary>
    public class Lifesteal : IPassiveSkillEffect
    {
        public string SkillId => "Lifesteal";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostDealDamage };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.playerWonRoll && ctx.finalDamage > 0)
            {
                int heal = ctx.ReduceEnemyHeal(ctx.finalDamage / 2);
                ctx.playerCurrentHP = System.Math.Min(ctx.playerMaxHP, ctx.playerCurrentHP + heal);
            }
        }
    }

    /// <summary>夜の王 — 5ターン目以降、 攻撃の出目を 1 つ多く振る (extraDice → RollEnemyAttack が攻撃ロールをもう 1 回引いて足す)</summary>
    public class NightLord : IPassiveSkillEffect
    {
        public string SkillId => "NightLord";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.currentTurn >= 5)
            {
                // extraDice をコンテキスト蓄積で管理（毎ターンリフレッシュ）
                ctx.accumulatedValues["extraDice"] = 1;
            }
        }
    }

    /// <summary>死の宣告 — 10ターン以内に倒さなければ、 11 ターン目に即死攻撃（999ダメージ）。
    /// 清算は敵攻撃の固定ダメ枠と、 FinishTurnCommon の「fixedDamageToPlayer ≥ 999 → HP 0」の 2 か所。</summary>
    public class DeathSentence : IPassiveSkillEffect
    {
        public string SkillId => "DeathSentence";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.currentTurn > 10)
            {
                // 即死級ダメージを固定ダメージとして付与
                ctx.fixedDamageToEnemy += 999;
            }
        }
    }

    /// <summary>威圧+: 戦闘開始時に脅威(threat)を +3 する (ボスは BossParam.IntimidateThreat・基準 3)。
    /// <para>相互攻撃モデルでの効き方 (2026-11-11 #104 で修正): 脅威の削りダメ (scratch) は廃止され、 脅威はいま
    /// 「基礎攻撃値」(EnemyData.EffectiveBaseAttack = baseAttack 未指定時の threat) としてしか使われない。
    /// DAMAGE_CALC_REFERENCE §9.6 の宿題「威圧の脅威加算を基礎攻撃値に統合」のとおり、 <b>この敵の攻撃 +N (戦闘を通して)</b>にした。
    /// 足し先は <c>enemyDiceTotalBonus</c> (視点スワップされない・persistent・形態の差し替えで 0 に戻り OnBattleStart で積み直す)。
    /// Escalation.EnemyAttackValue は (基礎攻撃 + 出目の合計) にまとめて倍率を掛けるので、 基礎攻撃 +N と同じ値になり、 予告にも出る。</para>
    /// <para>旧実装は <c>ctx.enemyThreat</c> に足していたが、 それを読む処理が無く何にも効いていなかった
    /// (<c>ctx.enemyThreat</c> へはもう書かない ── 戦場の修飾子〈逆境〉も同じ欄に書くので、 将来読み手を作ったとき二重にならないように)。
    /// 〈威圧++〉も同じ。</para></summary>
    public class IntimidatePlus : IPassiveSkillEffect
    {
        public string SkillId => "IntimidatePlus";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            // ボスの数値 (BossParams): 威圧の加算をボス別に直接調整 (基準3)。
            int add = UnityEngine.Mathf.Max(0, CombatSystem.BossParams.ParamInt(ctx.bossId, CombatSystem.BossParam.IntimidateThreat));
            ctx.enemyDiceTotalBonus += add;
            UnityEngine.Debug.Log($"[威圧+] 攻撃 +{add} (戦闘を通して)");
        }
    }

    /// <summary>嵐 (2026-11-13・GAME.md §5-12): 航行の地図で嵐の中の点に居る時に始まった戦闘で、 敵に付く (CombatManager.StartCombatInternal が付ける)。
    /// 戦闘開始時に、 この敵の攻撃 +<see cref="MapSystem.FreeMove.FreeMapParams.StormEnemyAttack"/> (戦闘を通して)。 足し先は威圧+ と同じ <c>enemyDiceTotalBonus</c>
    /// (persistent・Escalation.EnemyAttackValue で基礎攻撃と同じく倍率が掛かり、 予告に出る)。 隠し倍率ではなく、 敵のパッシブの欄に〈嵐〉として見える。</summary>
    public class StormEnemy : IPassiveSkillEffect
    {
        public const string Id = "Storm";
        public string SkillId => Id;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            ctx.enemyDiceTotalBonus += MapSystem.FreeMove.FreeMapParams.StormEnemyAttack;
        }
    }

    /// <summary>貪欲(偽の商人): 表示専用マーカー。実際のスケーリング(プレイヤー所持パッシブ数に応じた
    /// HP+2/個・攻撃+1/3個)は GameManager.StartFalseMerchantCombat が戦闘開始時に適用する
    /// （HP は複製データ、攻撃は ctx.enemyDiceTotalBonus 経由）。</summary>
    public class GreedyMerchant : IPassiveSkillEffect
    {
        public string SkillId => "GreedyMerchant";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx) { }
    }

    /// <summary>狂暴化: 全ボスに付与される時限エンレイジ。
    /// 50ターン経過後、エネミーのダイス合計+10、プレイヤーの回復を完全封印、
    /// エネミーが受けるダメージ3倍（プレイヤーが押し切る窓は残す）。</summary>
    public class Berserk : IPassiveSkillEffect
    {
        /// <summary>スキル id。 ボスのマスの敵に CombatManager が足す (enemies.json には書かない)。</summary>
        public const string Id = "Berserk";
        public const int EnrageTurn = 50;
        public const int DiceBonus = 10;
        public const float EnemyDamageTakenMult = 3f;

        public string SkillId => Id;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.currentTurn <= EnrageTurn) return;

            // 回復封印・敵被ダメ倍化は毎ターン再適用 (BeginNewTurn でリセットされるため)
            ctx.healBlocked = true;
            ctx.enemyDamageTakenMultiplier = EnemyDamageTakenMult;

            // ダイス合計+10 は一度だけ積む (enemyDiceTotalBonus は累積でリセットされない)
            if (ctx.GetAccumulated("berserk_dice_applied") == 0)
            {
                ctx.enemyDiceTotalBonus += DiceBonus;
                ctx.accumulatedValues["berserk_dice_applied"] = 1;
                UnityEngine.Debug.Log($"[Berserk] 狂暴化発動 (T{ctx.currentTurn}): 敵ダイス+{DiceBonus}, 回復封印, 敵被ダメ×{EnemyDamageTakenMult}");
            }
        }
    }

    // [削除 2026-10-17] 天衣無縫 (FlawlessRobe) ── 覚者の固有スキル。 覚者の廃止後、 どの敵も持っていなかった。

    // [廃止 2026-09-14] 旧 SinAltar 儀式 (血/貪欲/遺品) の敵専用パッシブ 3 種
    //   ── Boss6Golgotha / Boss6SeveredTime / Boss6Ashen。
    //   〈門〉リワークで罰はすべてプレイヤー側の規則になった (GameLoop.GateFlaws)。

    // ============================================================
    //  13番目の死: 13番目の宣告
    //  毎ターン開始時に 1/13 で宣告フラグを立て、
    //  ターン終了時に本体が生存していれば、(両者の出目の合計)×2 のダメージを
    //  プレイヤーに与える（軽減無効・シールドは肩代わり）。 ×2 は定数でプレイヤーの会心倍率ではない。
    // ============================================================

    // ============================================================
    //  各層ボス専用パッシブ
    // ============================================================

    /// <summary>ゴブリン王 — 号令: 毎ターン、 攻撃 +N (N = BossParam.GoblinCall・基準 3)</summary>
    public class GoblinKingsCall : IPassiveSkillEffect
    {
        public string SkillId => "GoblinKingsCall";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            // 敵視点なので playerDiceTotal = ボス自身のダイス合計。 加算量は BossParams.GoblinCall (基準3)。
            ctx.playerDiceTotal += UnityEngine.Mathf.Max(0, CombatSystem.BossParams.ParamInt(ctx.bossId, CombatSystem.BossParam.GoblinCall));
        }
    }

    /// <summary>毒沼の主 — 毒の侵蝕: 毎ターン終了時に毒スタック+1（上限5）、
    /// その後スタック分の固定ダメ（軽減無視）。長期戦ほど加速度的に蝕む。
    /// T1:1 T2:2 … T5:5 で頭打ち。累計はT5までで15＝速攻なら軽傷、長引けば致命。</summary>
    public class MiasmaCorrosion : IPassiveSkillEffect
    {
        public string SkillId => "MiasmaCorrosion";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        private const string Key = "miasma_poison_stack";
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            // ボスの数値 (BossParams): 毒スタック上限を直接調整 (低い=DOT緩和)。 基準5
            int cap = UnityEngine.Mathf.Max(1, CombatSystem.BossParams.ParamInt(ctx.bossId, CombatSystem.BossParam.PoisonStackCap));
            ctx.accumulatedValues.TryGetValue(Key, out var stack);
            int s = System.Math.Min(cap, (int)stack + 1);
            ctx.accumulatedValues[Key] = s;
            ctx.EnemyDealUnmitigable(s, DeathCause.Chip);   // 軽減は無視・シールドは肩代わりする
            UnityEngine.Debug.Log($"[毒の侵蝕] 毒×{s} ダメ (上限{cap}, 軽減無視) → プレイヤー残HP={ctx.enemyCurrentHP}");
        }
    }

    /// <summary>鏡の双子 — 鏡映の応答: 与ダメ (主攻撃+固定) が閾値 = max(12, ボス最大HP の 4%) 未満だったターン、
    /// 次ターン開始時に min(反射上限, 閾値 − 与ダメ) を軽減無視で返す (反射上限 = BossParam.MirrorReflectCap・基準 9)。
    /// 閾値以上の一撃は反射 0 ＝ 小突きを罰し、 重い一撃で鏡を割る。</summary>
    public class MirrorTwinsResponse : IPassiveSkillEffect
    {
        /// <summary>計装: 被弾イベント数 / そのうち反射が成立した数 / 反射の合計ダメージ。
        /// **閾値を勘で置き直さないため**に置いている ── 5% 版は 1 度も発火せず、
        /// 9,000 ラン 回して結果が 1 バイトも変わらないという形でしか気づけなかった。
        /// AutoRunner がバッチ頭で Reset し、 サマリへ 1 戦あたりの発火数を出す。</summary>
        public static long Hits, Fires, ReflectDamage;
        /// <summary>1 ヒットのダメージ分布 (最大HP に対する % で 1% 刻み・0..49、 50 以上は末尾)。
        /// **閾値を percentile で置くために要る** ── 平均だけでは分位が分からず、
        /// 5%→発火0 / 10%→発火87% と両極に振れた (2026-09-09 に 2 回外した)。</summary>
        public static readonly long[] HitPctHist = new long[51];
        public static void ResetStats()
        {
            Hits = Fires = ReflectDamage = 0;
            System.Array.Clear(HitPctHist, 0, HitPctHist.Length);
        }

        /// <summary>累積分布から「発火率 target% になる閾値(最大HP比%)」を引く。</summary>
        public static string DescribePercentiles()
        {
            long tot = 0;
            foreach (var v in HitPctHist) tot += v;
            if (tot == 0) return "ヒット記録なし";
            var sb = new System.Text.StringBuilder();
            long cum = 0; int[] want = { 10, 20, 30, 50 }; int wi = 0;
            for (int p = 0; p < HitPctHist.Length && wi < want.Length; p++)
            {
                cum += HitPctHist[p];
                while (wi < want.Length && 100.0 * cum / tot >= want[wi])
                { sb.Append($"P{want[wi]}={p}% "); wi++; }
            }
            return sb.ToString().TrimEnd();
        }

        public string SkillId => "MirrorTwinsResponse";
        public PassiveSkillTrigger[] Triggers => new[] {
            PassiveSkillTrigger.OnPostReceiveDamage, PassiveSkillTrigger.OnTurnStart,
        };
        private const string Key = "mirror_pending";

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (trigger == PassiveSkillTrigger.OnPostReceiveDamage)
            {
                // 敵視点で「自分が被ダメ受けた」 → finalDamage が受けた量。
                // 反転: 弱い小突きほど反射、 閾値以上の高火力は反射0＝鏡を叩き割る。
                //
                // **閾値は絶対値ではなくボス最大HPの割合 (2026-09-09 修正)。**
                //   旧: `const int Thr = 12`。 2026-08-10 の経済リスケール以降、 プレイヤーは
                //   4層ボス戦で 1 ターン約 190 出すので **12 未満は永久に成立せず、 この固有スキルは
                //   完全に不発だった**。 結果 4層ボスは実質パッシブ 1 つ (IntimidatePlus) だけになり、
                //   被ダメ 1.35/ターン ── 3層ボス 3.38・5層ボス 2.87 の半分以下で、
                //   実測の脅威が 1層ボス(41.7%HP) を大きく下回る 20.2%HP まで落ちていた。
                //   割合にしておけば、 今後ダメージ規模が動いても意図 (小突きを罰する) が保たれる。
                //   敵視点では ctx.playerMaxHP がボス自身の最大HP。
                //
                //   **5% では 1 度も発火しなかった** (2026-09-09 実測: 9,000 ラン で結果が
                //   1 バイトも変わらなかった)。 4層ボスは HP 2400 を 12.3 ターンで削られるので
                //   1 ターン平均 191、 ロールに勝ったターンだけに絞ると 1 ヒット 300 前後。
                //   閾値 120 では届かない。 **maxHP/10 = 「12 ターンで削り切るペースを下回るヒット」**
                //   **閾値は分位から選ぶ。 平均から推定すると外す。** 実測の分位 (2026-09-09):
                //     P10=2% P20=2% P30=3% P50=5%   ← 1 ヒットのボス最大HP比
                //   平均は 8% 付近だが分布は右に裾を引いていて、 中央値は 5%。 そのため
                //     5%  → 発火  0% (旧トリガー配線では死亡ターンのみ・実質不発)
                //     10% → 発火 87% / 被ダメ 77.0%HP (5層ボス 56.7% を追い越す)
                //     7%  → 発火 70% / 被ダメ 65.6%HP
                //   と両極に振れた。 **4% ＝ 発火 35〜40%** が狙い (被ダメ 45〜50%HP =
                //   3層ボス 41.9 と 5層ボス 56.7 の間)。 次に動かすときも P30/P50 を見ること。
                int Thr = UnityEngine.Mathf.Max(12, ctx.playerMaxHP * 4 / 100);
                // ボスの数値 (BossParams): 反射ダメ上限を直接調整 (低い=反射事故の緩和)。 基準9
                int reflectCap = UnityEngine.Mathf.Max(0, CombatSystem.BossParams.ParamInt(ctx.bossId, CombatSystem.BossParam.MirrorReflectCap));
                int dmg = ctx.finalDamage;
                if (dmg > 0)
                {
                    Hits++;
                    if (ctx.playerMaxHP > 0)
                    {
                        int bucket = (int)(100L * dmg / ctx.playerMaxHP);   // 敵視点: playerMaxHP = ボス最大HP
                        HitPctHist[UnityEngine.Mathf.Clamp(bucket, 0, HitPctHist.Length - 1)]++;
                    }
                }
                if (dmg > 0 && dmg < Thr && reflectCap > 0)
                {
                    int reflect = System.Math.Min(reflectCap, Thr - dmg);
                    Fires++; ReflectDamage += reflect;
                    ctx.accumulatedValues[Key] = reflect;
                    UnityEngine.Debug.Log($"[鏡映の応答] 蓄積: 次ターン{reflect}反射 (与ダメ{dmg}<{Thr}＝小突きを罰す, 上限{reflectCap})");
                }
            }
            else if (trigger == PassiveSkillTrigger.OnTurnStart)
            {
                if (!ctx.accumulatedValues.TryGetValue(Key, out var pending) || pending <= 0f) return;
                int dmg = (int)pending;
                ctx.EnemyDealUnmitigable(dmg, DeathCause.Reflect);   // 軽減は無視・シールドは肩代わりする
                ctx.accumulatedValues[Key] = 0f;
                UnityEngine.Debug.Log($"[鏡映の応答] 反射 {dmg}ダメ → プレイヤー残HP={ctx.enemyCurrentHP}");
            }
        }
    }

    /// <summary>審判官 — 審判の炎: 毎ターン終了時の確定ダメ（軽減無視）。
    /// = 1 + **経過ターン÷3(切上)** + 罪。罪 = ラン中の総戦闘回数/8（上限2）。 1 回あたりの上限は BossParam.ChipCap (基準 10)。
    /// 「速攻」かつ「無駄な戦闘を避けた」者ほど有利。
    ///
    /// 2026-07-28: ターン係数を **1/2** に緩和。 旧 `1+経過T+罪` は 5.3T の平均戦闘で
    /// 累計 30 前後の**軽減不可**が確定し、 配線・ブロックで一切対処できないまま
    /// 5層勝率 45% / 主死因 Chip 98% の壁になっていた。 軽減不可の継続ダメは
    /// ADR-0009 の「被弾を効率よく受けるレース判断」(§6-2) を無効化するため、 総量を抑える。</summary>
    public class JudgmentFlames : IPassiveSkillEffect
    {
        public string SkillId => "JudgmentFlames";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            var run = GameLoop.GameManager.Instance?.Run;
            int sin = System.Math.Min(2, (run?.totalBattles ?? 0) / 8); // 罪: プレイで操作可能
            // ボスの数値 (BossParams): 審判の炎の総ダメ上限を直接調整 (低い=不可避チップ緩和)。 基準13
            int cap = CombatSystem.BossParams.ParamInt(ctx.bossId, CombatSystem.BossParam.ChipCap);
            // 2026-08-15: ターン係数を 1/2 → **1/3**。 出現漏れ (怪しい商人) と裏ボス遮断漏れを
            //   直した土俵で測り直したところ、 この敵が単独の主死因 (致命 276 / 主死因 Chip 97%) だった。
            //   平均 14.7 ターンで累積 108 ＝ プレイヤー最大HP (93〜99) を上回り、
            //   **通常攻撃を全て捌いてもチップだけで死ぬ**規模になっていた。 1/3 で 15T 累積 90。
            //   上限 (ChipCap 10) に当たるのが T≥19 になるので、 実質ターン係数だけで決まる。
            int turnTerm = UnityEngine.Mathf.CeilToInt(System.Math.Max(1, ctx.currentTurn) / 3f);
            int dmg = System.Math.Min(cap, 1 + turnTerm + sin);
            ctx.EnemyDealUnmitigable(dmg, DeathCause.Chip);   // 軽減は無視・シールドは肩代わりする
            UnityEngine.Debug.Log($"[審判の炎] {dmg}ダメ (1+経過T{ctx.currentTurn}+罪{sin}, 上限{cap}, 軽減無視) → プレイヤー残HP={ctx.enemyCurrentHP}");
        }
    }

    /// <summary>灰燼の王 — 烈炎 (2026-08-04): **亀戦法への罰**。
    ///   ・2026-11-14〜 (作者の決定): ターン終了時、 そのターンに敵の主攻撃をシールドで肩代わりした量
    ///     (<see cref="CombatContext.shieldAbsorbedAttackThisTurn"/>) の BossParam.BlazePerShield (基準 10) ごとに烈炎 +1 (切り捨て・端数は持ち越さない・
    ///     式は <see cref="CombatSystem.HeavyCycle.BlazeGain"/>)。 軽減無視 (業火の烈炎の分など) をシールドが肩代わりした分は数えない
    ///   ・旧 (測定の口 blazeStackRule=Guard2): ブロック端子へ <see cref="BlockDiceThreshold"/> 本以上配線したターン終了時、 烈炎スタック +1
    ///   ・2026-11-14〜: 積もった烈炎は王の大技〈業火〉で「烈炎の数 × BlazeBurstPerStack」の軽減無視ダメージになって返り、 撃ったら 0
    ///     (CombatManager.ApplyBlazeBurst)。 業火がダウンで取り消された時は 0 に戻さない (次の業火へ持ち越す)。
    ///   ・旧: ターン開始時、 スタック分の軽減無視ダメージ (測定の口 blazeTickEachTurn=True で残せる)
    ///
    /// 導入の経緯: 6 層ボス戦は防御配線率 95%・遮断率 59% で「守れば止まる」が完全に成立しており、
    /// 層別死亡率 9.2% (目標 50%) と通過儀礼になっていた。 攻撃力や HP を上げても、
    /// ブロックは定額減算で出力上限 (~17) があるだけで、 その上限内に収まる限り無害なまま。
    /// 数値ではなく **「守ると損をする」構造** を足すことで、 速攻と耐久の二択を立てる。
    ///
    /// スタックは戦闘通して保持 (accumulatedValues)。
    /// テレグラフ (MutualTurnTelegraph.blazeStacks / blazePenalizesBlock) で配線前に開示するので、
    /// 「知らないうちに焼かれる」にはならない。</summary>
    public class BlazeBrand : IPassiveSkillEffect
    {
        /// <summary>パッシブの id (enemies.json の internalName)。 〈業火〉で烈炎を返す CombatManager も読む。</summary>
        public const string Id = "BlazeBrand";
        public string SkillId => Id;
        public PassiveSkillTrigger[] Triggers => new[] {
            PassiveSkillTrigger.OnTurnStart, PassiveSkillTrigger.OnTurnEnd,
        };
        /// <summary>この本数以上をブロック端子へ配線するとスタックが増える。</summary>
        public const int BlockDiceThreshold = 2;
        public const string StackKey = "blaze_stack";
        public const string ActiveKey = "blaze_active";
        /// <summary>[計装 2026-11-14] この戦闘で積もった烈炎の通算 (業火で 0 に戻しても減らない・accumulatedValues は戦闘ごとに作り直される)。</summary>
        public const string GainedKey = "blaze_gained_total";
        /// <summary>配線本数の参照先 (CombatManager が毎ターン書き込む)。</summary>
        private const string BlockCountKey = "mutualBlockDiceCount";

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (trigger == PassiveSkillTrigger.OnTurnStart)
            {
                // テレグラフ生成は OnTurnStart の後なので、 ここで所持フラグを立てておく。
                ctx.accumulatedValues[ActiveKey] = 1f;

                // 2026-11-14〜 既定: ターン開始時の削りは無し (積もった烈炎は王の大技〈業火〉で × BlazeBurstPerStack にまとめて返る・CombatManager.ApplyBlazeBurst)。
                //   測定の口 blazeTickEachTurn=True (GameHooks.BlazeTickEachTurn) で今までの毎ターンの削りを残す。
                if (!GameLoop.GameHooks.BlazeTickEachTurn) return;
                ctx.accumulatedValues.TryGetValue(StackKey, out var stack);
                int dmg = (int)stack;
                if (dmg > 0)
                {
                    ctx.EnemyDealUnmitigable(dmg, DeathCause.Chip);   // 軽減は無視・シールドは肩代わりする
                    UnityEngine.Debug.Log($"[烈炎] ×{dmg} ダメ (軽減無視) → プレイヤー残HP={ctx.enemyCurrentHP}");
                }
            }
            else if (trigger == PassiveSkillTrigger.OnTurnEnd)
            {
                // 2026-11-14〜 既定 (Shield): このターンに主攻撃をシールドで受けた量 N ごとに +1。 旧い条件 (Guard2) は防御の端子に 2 個以上
                ctx.accumulatedValues.TryGetValue(BlockCountKey, out var blk);
                int perShield = CombatSystem.BossParams.ParamInt(ctx.bossId, CombatSystem.BossParam.BlazePerShield);
                int gain = CombatSystem.HeavyCycle.BlazeGain(GameLoop.GameHooks.BlazeStack, ctx.shieldAbsorbedAttackThisTurn, perShield, (int)blk, BlockDiceThreshold);
                if (gain > 0)
                {
                    ctx.accumulatedValues.TryGetValue(StackKey, out var stack);
                    ctx.accumulatedValues[StackKey] = stack + gain;
                    ctx.accumulatedValues.TryGetValue(GainedKey, out var tot);
                    ctx.accumulatedValues[GainedKey] = tot + gain;
                    UnityEngine.Debug.Log(GameLoop.GameHooks.BlazeStack == CombatSystem.BlazeStackRule.Guard2
                        ? $"[烈炎] ブロック{(int)blk}本 → スタック {(int)stack}→{(int)stack + gain}"
                        : $"[烈炎] シールドで {ctx.shieldAbsorbedAttackThisTurn} 受けた ({perShield} ごとに +1) → スタック {(int)stack}→{(int)stack + gain}");
                }
            }
        }
    }

    // ============================================================
    //  灰燼の王 リワーク（見切り＆カウンター型）
    //  体感: 致命的な大技を最適行動(ロール勝利)で間一髪回避しつつ、
    //  一撃ずつ削る。敵視点: playerCurrentHP/Max=ボス自身HP /
    //  enemyCurrentHP=実プレイヤー / playerWonRoll=ボスがロール勝利。
    // ============================================================

    /// <summary>断罪周期はボスHP割合で短縮: >60%→3T / ≤60%→2T / ≤30%→毎T。
    /// 断罪ターン = currentTurn % 周期 == 0。</summary>
    internal static class EmberKing
    {
        public static int Period(CombatContext ctx)
        {
            if (ctx.playerMaxHP <= 0) return 3;
            float r = (float)ctx.playerCurrentHP / ctx.playerMaxHP;
            return r <= 0.30f ? 1 : (r <= 0.60f ? 2 : 3);
        }
        public static bool IsJudgment(CombatContext ctx)
        {
            int p = Period(ctx);
            return ctx.currentTurn > 0 && p > 0 && ctx.currentTurn % p == 0;
        }
    }

    /// <summary>業火の断罪 — 断罪ターンを予告（灰の予兆を統合）。
    /// 断罪ターンにボスがロール勝利すると、 ボス最終ダメージ ×8+8 の致命ダメ
    /// （軽減/シールド有効経路で適用）。 プレイヤーがロール勝利＝間一髪回避し、
    /// 鎧貫通の確定反撃18をボスへ。</summary>
    public class JudgmentBlaze : IPassiveSkillEffect
    {
        public string SkillId => "JudgmentBlaze";
        public PassiveSkillTrigger[] Triggers => new[] {
            PassiveSkillTrigger.OnTurnStart,
            PassiveSkillTrigger.OnPreDealDamage,
            PassiveSkillTrigger.OnRollLose,
            PassiveSkillTrigger.OnTurnEnd };
        private const string CtrKey = "ek_counter";
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            switch (trigger)
            {
                case PassiveSkillTrigger.OnTurnStart:
                    // 灰の予兆統合: 断罪ターンを予告（読める＝運でなく対応の問題）
                    if (EmberKing.IsJudgment(ctx))
                    {
                        // ボスの数値 (BossParams): 断罪Tの敵ダイス上乗せを直接調整
                        // (低い=見切りロールに勝ちやすく=易化)。 基準+10。
                        int jdice = CombatSystem.BossParams.ParamInt(ctx.bossId, CombatSystem.BossParam.JudgmentDice);
                        ctx.enemyDiceTotalBonus += jdice; // 断罪Tは敵ダイス上乗せ (見切り難度)
                        // 断罪Tの累積回数 (この戦闘で何回目か) をカウント
                        int n = (int)ctx.GetAccumulated("judg_counter") + 1;
                        ctx.accumulatedValues["judg_counter"] = n;
                        UnityEngine.Debug.Log($"[灰の予兆] 業火の断罪 第{n}回（T{ctx.currentTurn}・周期{EmberKing.Period(ctx)}）—敵ダイス+{jdice}、ロール勝利で間一髪回避");
                    }
                    break;

                case PassiveSkillTrigger.OnPreDealDamage:
                    // 敵視点: playerWonRoll = ボスがロール勝利 = プレイヤー敗北。
                    // 断罪Tに発動 → 係数は 1回目=4、 以降+2ずつ (2回目=6、 3回目=8、 4回目=10...)。
                    // ctx.finalDamage = base × coef + coef （軽減/シールド有効経路）。
                    if (ctx.playerWonRoll && EmberKing.IsJudgment(ctx))
                    {
                        int n = (int)ctx.GetAccumulated("judg_counter");
                        if (n < 1) n = 1;
                        // ボスの数値 (BossParams): 断罪係数の基礎を直接調整
                        // (低い=即死性が下がり、 断罪ロールに負けても耐えられる=易化)。 係数 = base + 2n、 基準base=2。
                        int coefBase = CombatSystem.BossParams.ParamInt(ctx.bossId, CombatSystem.BossParam.JudgmentCoefBase);
                        int coef = UnityEngine.Mathf.Max(1, coefBase + 2 * n); // 基準1回目=4、2回目=6...
                        int before = ctx.finalDamage;
                        ctx.finalDamage = ctx.finalDamage * coef + coef;
                        ctx.lastDamageCause = DeathCause.Judgment; // 死因タグ
                        UnityEngine.Debug.Log($"[業火の断罪] 致命の一撃 第{n}回 {before}→{ctx.finalDamage}（×{coef}+{coef}）");
                    }
                    break;
                case PassiveSkillTrigger.OnRollLose:
                    // ボスがロール敗北＝プレイヤーが見切った
                    if (EmberKing.IsJudgment(ctx))
                        ctx.accumulatedValues[CtrKey] = 18; // 鎧貫通カウンター予約（火力強化）
                    break;
                case PassiveSkillTrigger.OnTurnEnd:
                    if (ctx.accumulatedValues.TryGetValue(CtrKey, out var c) && c > 0f)
                    {
                        int dmg = (int)c;
                        ctx.playerCurrentHP = System.Math.Max(0, ctx.playerCurrentHP - dmg);
                        ctx.accumulatedValues[CtrKey] = 0f;
                        UnityEngine.Debug.Log($"[業火の断罪] 間一髪回避→反撃 {dmg} → 灰燼残HP={ctx.playerCurrentHP}");
                    }
                    break;
            }
        }
    }

    /// <summary>灰塵の鎧 — 被弾時、受けるダメージ-5。軽減後が10超なら10に丸める
    /// （通常打は「なんとか一撃」）。断罪ターンは鎧無効＝削りの本命窓。</summary>
    public class AshArmor : IPassiveSkillEffect
    {
        public string SkillId => "AshArmor";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreReceiveDamage };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            // 断罪ターンは鎧無効（ロール勝利の差分ダメが素通り＝削りの本命）
            if (EmberKing.IsJudgment(ctx)) return;

            if (ctx.playerLostRoll && ctx.finalDamage > 0)
            {
                // ボスの数値 (BossParams): 灰塵の鎧の軽減量を直接調整 (低い=タンク性弱化=ジリ貧緩和)。 基準-9
                int reduction = CombatSystem.BossParams.ParamInt(ctx.bossId, CombatSystem.BossParam.RegenReduction);
                const int cap = 10; // 軽減後キャップ (固定)
                int reduced = System.Math.Max(0, ctx.finalDamage - reduction);
                ctx.finalDamage = System.Math.Min(reduced, cap);
            }
        }
    }

    // ================================================================
    //  6層 灰燼の王 リワーク (ADR-0009 相互攻撃モデル対応・2026-07-16)
    //  旧 JudgmentBlaze/AshArmor/ImmortalEmber/EmberAura/StarfireProliferation/
    //  ScorchedEarth は OnPreDealDamage 依存で相互攻撃パイプ下では発火経路が壊れていた。
    //  以下 5 パッシブで再構築: OnTurnStart / OnPreReceiveDamage / OnRollWin に集約。
    // ================================================================

    // EmberOmenTiming (予兆 = 王の大技〈業火〉のターンの数え方) は EmberOmenTiming.cs へ移した (2026-11-14・Unity なしの検算 Tools/rules_check がコンパイルするため)。

    /// <summary>灰塵の外殻 — 被ダメが15を超えた分を半減 (例: 30→22, 50→32)。
    /// 大玉より小玉連打が有利という設計信号。 会心一撃を丸ごと通さず、 かつ通常火力は素通し。</summary>
    public class AshCarapace : IPassiveSkillEffect
    {
        public const int Threshold = 15;
        public string SkillId => "AshCarapace";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreReceiveDamage };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.finalDamage <= Threshold) return;
            int excess = ctx.finalDamage - Threshold;
            int reduced = Threshold + excess / 2;
            UnityEngine.Debug.Log($"[灰塵の外殻] 被ダメ {ctx.finalDamage}→{reduced} ({Threshold}超は半減)");
            ctx.finalDamage = reduced;
        }
    }

    /// <summary>業火の予兆 — HP>50% は T1/5/9/13... の周期 4T、 HP≤50% は周期 3T で発動。
    /// 予兆ターンは敵の攻撃を上乗せ (BossParam.JudgmentDice・基準 +12・測定の口 AutoRunner.bossParamSpec で差し替えられる)。 予告が前ターンに出るので配線で対策可能。
    /// <para>2026-11-11 #104 で修正: 上乗せは「予兆ターンだけ」になるよう、 〈焦土契約〉と同じ差分更新にした。
    /// 毎ターン頭に「このターン積むべき量」(予兆ターンなら JudgmentDice、 それ以外は 0) を出し、 前ターンに自分が
    /// enemyDiceTotalBonus へ積んだ量 (<see cref="KeyApplied"/>) との差だけを足し引きする (他のパッシブの寄与は消さない)。
    /// 旧実装は `+=` だけで戻す処理が無く、 予兆のたびに +8 が戦闘終了まで積み上がっていた。</para>
    /// 旧 JudgmentBlaze の即死一撃を「相互攻撃モデルで対応可能な予告火力」に転換。</summary>
    public class EmberOmen : IPassiveSkillEffect
    {
        /// <summary>前ターンに自分が enemyDiceTotalBonus へ積んだ量 (差分更新用)。 "ember_" 始まりなので形態の差し替えで消える。</summary>
        private const string KeyApplied = "ember_omen_applied";
        /// <summary>パッシブの id (enemies.json の internalName)。 ボスのダウンの判定 (CombatManager.Down) も読む。</summary>
        public const string Id = "EmberOmen";
        public string SkillId => Id;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            int bonus = CombatSystem.BossParams.ParamInt(ctx.bossId, CombatSystem.BossParam.JudgmentDice);
            float hpRatio = ctx.playerMaxHP > 0 ? (float)ctx.playerCurrentHP / ctx.playerMaxHP : 1f;
            // [計装] 発火したかではなく、 **何を計算したか**をそのまま残す。
            CombatSystem.BossCombatTrace.NoteEvent("omen",
                $"turn={ctx.currentTurn} bossId='{ctx.bossId}' bonus={bonus}"
                + $" ratio={hpRatio.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}"
                + $" period={EmberOmenTiming.PeriodFor(hpRatio)}"
                + $" fired={EmberOmenTiming.IsOmen(ctx.currentTurn, hpRatio)}");
            bool omen = EmberOmenTiming.IsOmen(ctx.currentTurn, hpRatio);
            // 2026-11-14〜 既定の形: 予兆のターンは王の大技〈業火〉(EnemyData.heavyCycle = ember・攻撃 ×heavyMul + 烈炎 × BlazeBurstPerStack)。 攻撃 +N は積まない。
            //   旧い形 (測定の口 ashKingHeavy=False・GameHooks.AshKingHeavyDisabled) だけ、 今までどおり予兆のターンに攻撃 +JudgmentDice。
            if (!GameLoop.GameHooks.AshKingHeavyDisabled)
            {
                EmberBonus.SetApplied(ctx, KeyApplied, 0);
                UnityEngine.Debug.Log(omen ? $"[業火] T{ctx.currentTurn} 業火のターン (周期{EmberOmenTiming.PeriodFor(hpRatio)}T)"
                                           : $"[業火] T{ctx.currentTurn} 次の業火はT{EmberOmenTiming.NextOmen(ctx.currentTurn, hpRatio)} (周期{EmberOmenTiming.PeriodFor(hpRatio)}T)");
                return;
            }
            EmberBonus.SetApplied(ctx, KeyApplied, omen ? bonus : 0);
            if (omen)
            {
                UnityEngine.Debug.Log($"[業火の予兆] T{ctx.currentTurn} 発動 → このターンの攻撃 +{bonus} (周期{EmberOmenTiming.PeriodFor(hpRatio)}T)");
            }
            else
            {
                int next = EmberOmenTiming.NextOmen(ctx.currentTurn, hpRatio);
                UnityEngine.Debug.Log($"[業火の予兆] T{ctx.currentTurn} 予告: 次はT{next} (周期{EmberOmenTiming.PeriodFor(hpRatio)}T)");
            }
        }
    }

    /// <summary>灰の再生 (2026-07-16 二相化 / 2026-08-11 に 3% → **2%**) —
    /// HP>50% で毎T 最大HP × <see cref="CombatSystem.BossParam.RegrowthRate"/> (基準 0.03 = 3%) 回復 (Phase1: 灰をまといながら耐える)。
    ///
    /// 5% の頃は HP1200 に対し 60/T の再生で、 これを上回る素ダメージが出ないビルドは
    /// **HP を 1 も削れない**二値の壁になっていた (6層勝率 5.7%)。 2% まで下げ、難度は攻撃側で担保する。
    /// HP≤50% で回復完全停止 (Phase2: 「決着期」に突入 = プレイヤーの削り勝ちが可視化)。
    /// 予兆ターンは回復なし = 集中砲火で HP50% ラインを越える窓。
    /// 2026-11-14〜 (大技〈業火〉の形): 業火のターンと、 その 1 ターン前のチャージのターンは回復なし (作者の決定)。 旧い形 (ashKingHeavy=False) は予兆ターンだけ。</summary>
    public class AshRegrowth : IPassiveSkillEffect
    {
        public string SkillId => "AshRegrowth";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.playerMaxHP <= 0) return;

            float r = (float)ctx.playerCurrentHP / ctx.playerMaxHP;
            if (r <= 0.5f) return; // Phase2: 再生停止

            if (EmberOmenTiming.IsOmen(ctx.currentTurn, r)) return; // 予兆T (= 業火のターン) は回復なし
            // 2026-11-14 (作者の決定): 大技〈業火〉の形では、 チャージのターン (業火の 1 ターン前) も回復なし。 旧い形 (口 ashKingHeavy=False) は今までどおり予兆Tだけ
            if (!GameLoop.GameHooks.AshKingHeavyDisabled && EmberOmenTiming.TurnsToOmen(ctx.currentTurn, r) == 1) return;

            // 割合は BossParam.RegrowthRate (基準 0.03f = 3%・2026-11-14 に直書き 0.02f から移し、 同日 0.03 へ・測定の口で差し替えられる)
            float rate = CombatSystem.BossParams.Param(ctx.bossId, CombatSystem.BossParam.RegrowthRate);
            int heal = ctx.ReduceEnemyHeal(System.Math.Max(1,
                UnityEngine.Mathf.FloorToInt(ctx.playerMaxHP * rate)));
            int after = System.Math.Min(ctx.playerMaxHP, ctx.playerCurrentHP + heal);
            if (after > ctx.playerCurrentHP)
            {
                UnityEngine.Debug.Log($"[灰の再生] Phase1 +{after - ctx.playerCurrentHP} ({ctx.playerCurrentHP}→{after})");
                ctx.playerCurrentHP = after;
            }
        }
    }

    /// <summary>憤怒の残滓 (EmberFury) — 2026-07-16 追加。 HP≤50% のターン、 敵の攻撃 +<see cref="CombatSystem.BossParam.FuryAttack"/> (基準 5・2026-11-14 に定数 Bonus = 3 から移し、 同日 5 へ)。
    /// Phase2 突入で「再生ゼロ・高火力連打」の後半戦を演出する。
    /// EmberOmen (周期3Tに加速) + AshRegrowth (停止) と合わせて瞬発力勝負を強制。
    /// <para>2026-11-11 #104 で修正: 〈業火の予兆〉と同じ差分更新。 ターン頭の HP が 50% 以下のターンは +3、 50% を超えるターンは 0
    /// (回復して戻れば外れる)。 旧実装は HP≤50% のターンごとに `+=` して +3 が積み上がっていた。</para></summary>
    public class EmberFury : IPassiveSkillEffect
    {
        /// <summary>前ターンに自分が enemyDiceTotalBonus へ積んだ量 (差分更新用)。 "ember_" 始まりなので形態の差し替えで消える。</summary>
        private const string KeyApplied = "ember_fury_applied";
        public string SkillId => "EmberFury";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.playerMaxHP <= 0) return;
            float r = (float)ctx.playerCurrentHP / ctx.playerMaxHP;
            bool phase2 = r <= 0.5f;   // Phase1 (HP>50%) は 0
            int bonus = CombatSystem.BossParams.ParamInt(ctx.bossId, CombatSystem.BossParam.FuryAttack);
            EmberBonus.SetApplied(ctx, KeyApplied, phase2 ? bonus : 0);
            if (phase2)
                UnityEngine.Debug.Log($"[憤怒の残滓] Phase2 このターンの攻撃 +{bonus} (HP {ctx.playerCurrentHP}/{ctx.playerMaxHP})");
        }
    }

    /// <summary>〈業火の予兆〉〈憤怒の残滓〉の差分更新: enemyDiceTotalBonus (persistent・他のパッシブと共有) に、
    /// 自分の寄与が <paramref name="want"/> になるよう前ターンとの差だけを足し引きする (〈焦土契約〉と同じ形)。</summary>
    internal static class EmberBonus
    {
        public static void SetApplied(CombatContext ctx, string keyApplied, int want)
        {
            int prev = (int)ctx.GetAccumulated(keyApplied);
            if (want == prev) return;
            ctx.enemyDiceTotalBonus += want - prev;
            ctx.accumulatedValues[keyApplied] = want;
        }
    }

    /// <summary>焦土契約 — プレイヤーが収支マイナスで終わったターン、 次ターンから敵攻撃+1 (最大5スタック=+5・<see cref="AttackPerStack"/>)。
    /// 収支トリガー (敵視点 OnRollWin = プレイヤーが収支負け) で発火。 一度得たスタックは戦闘終了まで持続。
    /// 旧 ScorchedEarth の「被ダメ×10%を最大HP削り＋シールド全破壊」を廃止し、
    /// 「収支負けの累積が持続圧に変わる」相互攻撃ネイティブの脅威に。</summary>
    public class ScorchedPact : IPassiveSkillEffect
    {
        public const int StackCap = 5;
        /// <summary>1 スタックあたりの敵攻撃加算。 2026-07-28 に 2 → 1。
        /// β (ダイス寄与率 0.3) 撤去で、 この値がそのまま攻撃値へ届くようになったため。</summary>
        public const int AttackPerStack = 1;
        public string SkillId => "ScorchedPact";
        public PassiveSkillTrigger[] Triggers => new[] {
            PassiveSkillTrigger.OnTurnStart,
            PassiveSkillTrigger.OnRollWin };
        private const string Key = "scorched_stack";
        /// <summary>前ターンに自分が enemyDiceTotalBonus へ積んだ量。 差分更新用。</summary>
        private const string KeyApplied = "scorched_applied";
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            switch (trigger)
            {
                case PassiveSkillTrigger.OnTurnStart:
                    // enemyDiceTotalBonus は「累積・BeginNewTurn でリセットしない」フィールドなので、
                    // 毎ターン `+= stack*N` すると雪だるまになる (T5 で +20、 T10 で +45…)。
                    // JSON の「最大+X」も効かなくなっていた。
                    // 他パッシブ (業火の予兆/憤怒の残滓) の寄与を消さないよう `=` は使わず、
                    // **自分が前ターンに積んだ分との差分だけ**を足し引きする。
                    int stack = (int)ctx.GetAccumulated(Key);
                    int now   = stack * AttackPerStack;
                    int prev  = (int)ctx.GetAccumulated(KeyApplied);
                    if (now != prev)
                    {
                        ctx.enemyDiceTotalBonus += (now - prev);
                        ctx.accumulatedValues[KeyApplied] = now;
                    }
                    if (now > 0)
                        UnityEngine.Debug.Log($"[焦土契約] T{ctx.currentTurn} スタック{stack}/{StackCap} → 敵攻撃+{now} (累積ではなく現在値)");
                    break;
                case PassiveSkillTrigger.OnRollWin:
                    // 敵視点 OnRollWin = 収支トリガーで敵勝利 = プレイヤーの収支マイナス
                    int s = System.Math.Min(StackCap, (int)ctx.GetAccumulated(Key) + 1);
                    ctx.accumulatedValues[Key] = s;
                    UnityEngine.Debug.Log($"[焦土契約] プレイヤー収支負け → スタック={s}/{StackCap}");
                    break;
            }
        }
    }

    /// <summary>13番目の死神の宣告。即死級ではないが層を問わず事故率が固定で残る。</summary>
    public class Decree13th : IPassiveSkillEffect
    {
        public string SkillId => "Decree13th";
        public PassiveSkillTrigger[] Triggers => new[]
        {
            PassiveSkillTrigger.OnTurnStart,
            PassiveSkillTrigger.OnTurnEnd,
        };

        // currentBuffs に格納するキー（同ターン内のみ有効）
        private const string FlagKey = "decree13th_armed";

        /// <summary>宣告ダメージの倍率。 **定数**であって、 プレイヤーの会心倍率ではない。
        /// 2.0 は <see cref="MetaProgression.MetaBuffApplicator.GetCriticalMultiplier"/> の
        /// 基準値と同じ ＝ 会心倍率を積んでいないプレイヤーに対する従来の実挙動を保つ値。</summary>
        private const float DecreeMultiplier = 2.0f;

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx == null) return;

            if (trigger == PassiveSkillTrigger.OnTurnStart)
            {
                // 1/13 で宣告フラグを立てる (2026-11-06・13% から)
                if (GameLoop.GameRng.Value("enemy.decree13") < 1f / 13f)
                {
                    ctx.currentBuffs[FlagKey] = 1f;
                    UnityEngine.Debug.Log($"[Decree13th] 死神の声が響く...（T{ctx.currentTurn}）");
                }
                return;
            }

            if (trigger == PassiveSkillTrigger.OnTurnEnd)
            {
                if (!ctx.currentBuffs.TryGetValue(FlagKey, out float armed) || armed <= 0f) return;

                // 敵視点のため playerCurrentHP = 13番目の死本体のHP
                if (ctx.playerCurrentHP <= 0)
                {
                    UnityEngine.Debug.Log("[Decree13th] 死神は既に倒れた。回避成功");
                    ctx.currentBuffs.Remove(FlagKey);
                    return;
                }

                int total = ctx.playerDiceTotal + ctx.enemyDiceTotal;
                // **プレイヤー側の ctx.criticalMultiplier を読んではいけない** (2026-08-08 修正)。
                //   敵パッシブの文脈では player/enemy の HP は入れ替わるが、
                //   criticalMultiplier は入れ替わらない単一フィールド ＝ プレイヤーの値。
                //   旧実装はこれを掛けていたため、 **プレイヤーが会心倍率を積むほど
                //   この軽減全バイパスの一撃が強くなる**という反転が起きていた。
                //   実測 (単軸スイープ 500ラン): 遺物〈会心倍率+135%〉で基準 2.0 → 4.7 になり、
                //   2層エリートのこの一撃が 2.35 倍化。 5層クリアが 53.6% → 32.8%、
                //   1〜3F の被ダメ/戦が 12.7 → 15.5 と全 16 軸で最悪になった。
                int dmg = UnityEngine.Mathf.CeilToInt(total * DecreeMultiplier);

                // 軽減フックは全バイパスするが、 シールドは肩代わりとして機能する (2026-08-15)
                ctx.EnemyDealUnmitigable(dmg, null);   // 計上は従来どおり無し
                UnityEngine.Debug.Log(
                    $"[Decree13th] 成就: ({ctx.playerDiceTotal}+{ctx.enemyDiceTotal})×{DecreeMultiplier:F1} = {dmg} ダメ → プレイヤー残HP={ctx.enemyCurrentHP}");

                ctx.currentBuffs.Remove(FlagKey);
            }
        }
    }

    /// <summary>シュヴァリエ・サン=ジョリオラ — 形態転換: 二形態を循環する5層裏ボス。
    /// 【形態1 コントラタック】シールド = BaseShield − プリオリテ×PrioriteShieldDecPer (最低 MinShield)。
    ///   開幕はプリオリテ 1 で 140−28 = 112。 主攻撃は全てシールドが吸い、 プレイヤーの収支プラスのターン
    ///   (敵視点 OnRollLose) にプレイヤー自身の出目の合計 + プリオリテ×7 の不可避反撃を受け、 さらにシールドへ 25 の確定ダメ。
    ///   シールドが0になったターンは無敵(あふれた分を無効化)、次T形態2へ。
    /// 【形態2 オポジション】殴り合い。 ボスが収支で 3 勝 (プレイヤーの収支マイナス 3 回) で形態1へ帰還。
    ///   プレイヤーが収支プラスのターンを取ると次Tボスの攻撃+4、 そのTもプレイヤーが収支プラスならボスへ 15 ダメ。
    ///   次Tが形態1帰還となる場合、報酬を確実に与えるため形態2を1T延長する。
    /// 【プリオリテ】形態2→1 に再突入する毎にスタック +1（戦闘内累積・上限なし・開幕 1）。
    ///   各スタック効果: 形態1シールド初期 −28 (最低 28) / 形態1反撃 +7。
    ///   シールドが薄くなるほど往復は速くなるが、毎サイクル受ける反撃が重くなる
    ///   = "粘る者ほど無駄に身を削る" 罠。
    /// <para>形態1の「ボスはダイスを振らない (0d0)」(2026-11-11 #104 で修正): <c>extraDice = −BaseDiceCount</c> を、
    /// 攻撃ロール方式の <c>EnemyData.RollAttack</c> が「引く回数 = max(0, 1 + extra)」として受けるようにした。
    /// 形態1は攻撃ロールを 1 回も引かない (攻撃値 = 基礎攻撃 × 大技・段階の倍率だけ)。 <b>基礎攻撃は残る</b> ── 「振らない」の字面どおりで、
    /// 「形態1は攻撃しない」とは読んでいない。 旧実装は引く回数が max(1, 1 + max(0, extra)) で負の値が効かず、 形態1でも攻撃ロールを引いていた。
    /// (BaseDiceCount = 4 は旧ダイス方式の「4d6」の名残で、 1 + extra ≦ 0 にするための値。)</para>
    /// <para>形態1の反撃 (EnemyDealUnmitigable) と形態2の「ボスへ 15」(2026-11-11 #104 で修正): OnRollLose (収支トリガー・ターン解決の後) で
    /// ctx の HP に書かれる。 CombatManager.Turn.cs が収支トリガーの直前に ctx の HP を CombatManager の値へ揃え、 直後に
    /// SyncHPFromContext で拾うようにしたので、 HP の増減が残る。 旧実装は直後の FinishTurnCommon の書き戻しで消えていた
    /// (シールドの肩代わり分・シールドへの 25・形態の遷移だけが残っていた)。</para></summary>
    public class SaintGeorgesPhases : IPassiveSkillEffect
    {
        public string SkillId => "SaintGeorgesPhases";
        public PassiveSkillTrigger[] Triggers => new[] {
            PassiveSkillTrigger.OnBattleStart,
            PassiveSkillTrigger.OnTurnStart,
            PassiveSkillTrigger.OnPreReceiveDamage,
            PassiveSkillTrigger.OnRollLose,   // 敵視点: ボスが敗北 = プレイヤー勝利
            PassiveSkillTrigger.OnRollWin,    // 敵視点: ボスが勝利 = プレイヤー敗北
            PassiveSkillTrigger.OnTurnEnd,
        };

        // accumulatedValues キー（perspective 非依存・共有スカラ）
        private const string KPhase           = "sg_phase";            // 1 or 2
        private const string KShield          = "sg_shield";           // 形態1シールド残量
        private const string KBossWins        = "sg_boss_wins";        // 形態2のボス累積勝利
        private const string KPendingP1ToP2   = "sg_pending_p1_to_p2"; // シールド破壊→次T形態2
        private const string KPendingP2ToP1   = "sg_pending_p2_to_p1"; // ボス3勝→次T形態1
        private const string KP2RewardPending = "sg_p2_reward_pending"; // P2: 連勝予約
        private const string KP2RewardActive  = "sg_p2_reward_active";  // P2: 当ターン報酬有効
        private const string KP2ExtendPending = "sg_p2_extend_pending"; // P2: 帰還1T延長予約
        private const string KPrioriteStacks  = "sg_priorite_stacks";   // プリオリテ累積（往復ペナルティ）

        private const int BaseDiceCount   = 4; // 形態1で extraDice = −4 → 攻撃ロールを引く回数 max(0, 1 − 4) = 0 (EnemyData.RollAttack)
        private const int BaseShield      = 140; // HP +40% に比例して上方修正
        private const int MinShield       = 28; // プリオリテ削減後の下限 (20→28, +40%)
        private const int PrioriteShieldDecPer = 28; // /stack でシールド初期量を減らす
        private const int PrioriteCounterPer   = 7;  // /stack で反撃ダメ加算

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            switch (trigger)
            {
                case PassiveSkillTrigger.OnBattleStart:
                    ctx.accumulatedValues[KPhase] = 1;
                    // 2026-05-31: プリオリテ初期+1 (BaseShield - PrioriteShieldDecPer = 140-28=112 で開始)
                    ctx.accumulatedValues[KPrioriteStacks] = 1;
                    int initShield = System.Math.Max(MinShield, BaseShield - PrioriteShieldDecPer);
                    ctx.accumulatedValues[KShield] = initShield;
                    UnityEngine.Debug.Log($"[サン=ジョリオラ] 戦闘開始: コントラタック構え シールド{initShield} (プリオリテ初期+1)");
                    break;

                case PassiveSkillTrigger.OnTurnStart:
                    ApplyPendingTransitions(ctx);
                    int phaseStart = (int)ctx.GetAccumulated(KPhase);
                    if (phaseStart == 1)
                    {
                        // 形態1: ボスはダイスを振らない (0d0)
                        ctx.accumulatedValues["extraDice"] = -BaseDiceCount;
                    }
                    else
                    {
                        // 形態2: 報酬予約→有効化、ボスダイス合計+4
                        if (ctx.GetAccumulated(KP2RewardPending) > 0)
                        {
                            ctx.accumulatedValues[KP2RewardActive] = 1;
                            ctx.accumulatedValues[KP2RewardPending] = 0;
                            ctx.enemyDiceTotalBonus += 4;
                            UnityEngine.Debug.Log("[サン=ジョリオラ・オポジション] 報酬ターン突入: ボスダイス合計+4");
                        }
                    }
                    break;

                case PassiveSkillTrigger.OnPreReceiveDamage:
                {
                    // 形態1のみシールド経由
                    if ((int)ctx.GetAccumulated(KPhase) != 1) break;
                    if (ctx.finalDamage <= 0) break;

                    float shield = ctx.GetAccumulated(KShield);
                    if (shield <= 0)
                    {
                        // シールド既に0 → このターンは無敵
                        ctx.finalDamage = 0;
                        UnityEngine.Debug.Log("[サン=ジョリオラ] シールド0(無敵): ダメージ無効");
                        break;
                    }

                    int absorbed = System.Math.Min((int)shield, ctx.finalDamage);
                    shield -= absorbed;
                    ctx.finalDamage -= absorbed;
                    ctx.accumulatedValues[KShield] = shield;

                    if (shield <= 0)
                    {
                        // シールド破壊ターン: 残ダメを全て無効化、次T形態2へ
                        ctx.finalDamage = 0;
                        ctx.accumulatedValues[KPendingP1ToP2] = 1;
                        UnityEngine.Debug.Log($"[サン=ジョリオラ] シールド破壊! 吸収{absorbed} + 超過分は無敵で無効化 / 次T 形態2");
                    }
                    else
                    {
                        UnityEngine.Debug.Log($"[サン=ジョリオラ] シールド吸収{absorbed} → 残{shield}");
                    }
                    break;
                }

                case PassiveSkillTrigger.OnRollLose:
                {
                    // 敵視点: ボスがロール敗北 = プレイヤーがロール勝利
                    int phaseLose = (int)ctx.GetAccumulated(KPhase);
                    if (phaseLose == 1)
                    {
                        // 反撃: プレイヤー自身の出目の合計 + プリオリテ×PrioriteCounterPer(7) を不可避ダメで返す
                        // 敵視点では ctx.enemyDiceTotal = プレイヤー実ダイス合計、ctx.enemyCurrentHP = プレイヤー実HP
                        int playerTotal = ctx.enemyDiceTotal;
                        int stacks = (int)ctx.GetAccumulated(KPrioriteStacks);
                        int counter = playerTotal + stacks * PrioriteCounterPer;
                        if (counter > 0)
                        {
                            ctx.EnemyDealUnmitigable(counter, null);   // 軽減は無視・シールドは肩代わりする
                            UnityEngine.Debug.Log($"[サン=ジョリオラ・コントラタック] 反撃{counter} (素{playerTotal}+プリオリテ{stacks}×{PrioriteCounterPer}, 不可避) → プレイヤーHP={ctx.enemyCurrentHP}");
                        }
                        // シールドへ +25 確定ダメ
                        float s = ctx.GetAccumulated(KShield);
                        if (s > 0)
                        {
                            float newS = System.Math.Max(0, s - 25);
                            ctx.accumulatedValues[KShield] = newS;
                            UnityEngine.Debug.Log($"[サン=ジョリオラ・コントラタック] +25確定ダメ シールド{s}→{newS}");
                            if (newS <= 0)
                                ctx.accumulatedValues[KPendingP1ToP2] = 1;
                        }
                    }
                    else // 形態2
                    {
                        // 当ターン報酬有効中なら +15 ダメ
                        if (ctx.GetAccumulated(KP2RewardActive) > 0)
                        {
                            // 敵視点: ctx.playerCurrentHP = ボス自身のHP
                            ctx.playerCurrentHP = System.Math.Max(0, ctx.playerCurrentHP - 15);
                            UnityEngine.Debug.Log($"[サン=ジョリオラ・連勝報酬] +15ダメ → ボスHP={ctx.playerCurrentHP}");
                        }
                        // 次ターン分の報酬予約
                        ctx.accumulatedValues[KP2RewardPending] = 1;
                        // 形態1帰還を1T遅延する予約
                        ctx.accumulatedValues[KP2ExtendPending] = 1;
                    }
                    break;
                }

                case PassiveSkillTrigger.OnRollWin:
                {
                    // 敵視点: ボスがロール勝利
                    if ((int)ctx.GetAccumulated(KPhase) != 2) break;
                    int wins = (int)ctx.GetAccumulated(KBossWins) + 1;
                    ctx.accumulatedValues[KBossWins] = wins;
                    UnityEngine.Debug.Log($"[サン=ジョリオラ・オポジション] ボス勝利 累計{wins}/3");
                    if (wins >= 3)
                    {
                        if (ctx.GetAccumulated(KP2ExtendPending) > 0)
                        {
                            // 延長: 形態2を1T継続させ、勝利カウントを2に戻す
                            ctx.accumulatedValues[KBossWins] = 2;
                            ctx.accumulatedValues[KP2ExtendPending] = 0;
                            UnityEngine.Debug.Log("[サン=ジョリオラ] 連勝報酬完了まで形態2延長 (勝利数2へ戻す)");
                        }
                        else
                        {
                            ctx.accumulatedValues[KPendingP2ToP1] = 1;
                            UnityEngine.Debug.Log("[サン=ジョリオラ] 次T 形態1帰還を予約 → シールド再展開");
                        }
                    }
                    break;
                }

                case PassiveSkillTrigger.OnTurnEnd:
                    // 報酬ターン終了処理: +4 ボーナス除去、active クリア
                    if (ctx.GetAccumulated(KP2RewardActive) > 0)
                    {
                        ctx.accumulatedValues[KP2RewardActive] = 0;
                        ctx.enemyDiceTotalBonus = System.Math.Max(0, ctx.enemyDiceTotalBonus - 4);
                    }
                    break;
            }
        }

        private void ApplyPendingTransitions(CombatContext ctx)
        {
            if (ctx.GetAccumulated(KPendingP1ToP2) > 0)
            {
                ctx.accumulatedValues[KPhase] = 2;
                ctx.accumulatedValues[KShield] = 0;
                ctx.accumulatedValues[KBossWins] = 0;
                ctx.accumulatedValues[KPendingP1ToP2] = 0;
                UnityEngine.Debug.Log("[サン=ジョリオラ] 形態1→2 移行: オポジション(4d6) 開始");
            }
            if (ctx.GetAccumulated(KPendingP2ToP1) > 0)
            {
                // プリオリテ累積: 帰還の代償としてシールド −PrioriteShieldDecPer(28) (最低 MinShield)、反撃 +PrioriteCounterPer(7)
                int newStacks = (int)ctx.GetAccumulated(KPrioriteStacks) + 1;
                ctx.accumulatedValues[KPrioriteStacks] = newStacks;
                int newShield = System.Math.Max(MinShield, BaseShield - newStacks * PrioriteShieldDecPer);

                ctx.accumulatedValues[KPhase] = 1;
                ctx.accumulatedValues[KShield] = newShield;
                ctx.accumulatedValues[KBossWins] = 0;
                ctx.accumulatedValues[KPendingP2ToP1] = 0;
                // 報酬関連はクリア（形態跨ぎでは持ち越さない）
                ctx.accumulatedValues[KP2RewardPending] = 0;
                ctx.accumulatedValues[KP2RewardActive] = 0;
                ctx.accumulatedValues[KP2ExtendPending] = 0;
                UnityEngine.Debug.Log($"[サン=ジョリオラ] プリオリテ{newStacks} 主張! 形態2→1 移行: シールド{newShield} (反撃+{newStacks * PrioriteCounterPer})");
            }
        }
    }

}
