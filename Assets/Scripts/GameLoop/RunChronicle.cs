using System.Collections.Generic;
using System.Text;

namespace GameLoop
{
    /// <summary>
    /// 行動台帳 ── 1 ラン の出来事を **短いコード + 引数** で積む記録層。
    ///
    /// **記録と表現を分離する**のが目的である。ここに積まれるのは文章ではなくコードで、
    /// エンディング後のダイジェスト小説は別レイヤがこのコード列を読んで文を選ぶ。
    /// 文面を書き換えてもセーブ形式は動かず、逆にコードを足しても既存の文が壊れない。
    ///
    /// **副産物として計装になる。** BOT のランからも同じコード列が出るので、
    /// <see cref="CodeCounts"/> を集計すれば「一度も出ないコード＝死んでいる行動」と
    /// 「出過ぎているコード＝仕様と乖離した頻度」がそのまま出る
    /// （ラストスタンド 0/601 ラン・〈極〉が想定の 72 倍、はどちらもこの形で検出できた種類の問題）。
    ///
    /// <para><b>コードを enum の暗黙 int でセーブしないこと。</b>
    /// このプロジェクトには既に <c>RelicAxis</c> / <c>RelicCurse</c> / <c>RoleKind</c> /
    /// <c>ChallengeAxis</c> で「宣言順を変えると過去のセーブが壊れる」制約がある。
    /// 同じ罠をもう 1 つ作らないため、コードは **文字列** で持つ。
    /// バリアントは後から必ず増えるので、順序の概念自体を持たせない。</para>
    /// </summary>
    public static class RunChronicle
    {
        // ── A: 戦闘 ────────────────────────────────────────────────
        // バリアントは **戦闘の「形」だけ**を表す。 敵・層・ターン数・残 HP は引数へ逃がす。
        // 形に敵や層を畳み込むと組み合わせが爆発し、 文プールが維持できなくなる。
        public const string CombatCrush    = "A1"; // 圧倒 — 短期決着かつ無傷に近い
        public const string CombatClean    = "A2"; // 順当 — 特筆点なし。 選抜で落とすための受け皿
        public const string CombatGrind    = "A3"; // 削り合い — 長期戦、 または半分以上削られた
        public const string CombatNarrow   = "A4"; // 辛勝 — 残 HP 20% 以下で勝った
        public const string CombatFall     = "A5"; // 惜敗 — 削り合った末に敗れた
        public const string CombatRout     = "A6"; // 一蹴 — 高 HP から 1 戦で沈んだ (blowout)

        // ── B: 商い ────────────────────────────────────────────────
        public const string BuyWeapon      = "B1"; // 主武装
        public const string BuyDice        = "B2"; // ダイス
        public const string BuyConsumable  = "B3"; // 消耗品
        public const string BuyPassive     = "B4"; // パッシブ
        public const string BuyForgo       = "B5"; // 見送り — 欲しかったが金が届かない
        public const string BuyReroll      = "B6"; // 漁り — 棚を引き直した

        // ── C: 事件 ────────────────────────────────────────────────
        public const string EventTake      = "C1"; // 応じた
        public const string EventRefuse    = "C2"; // 退けた
        public const string EventPrice     = "C3"; // 代償を払った

        // ── D: 鍛え ────────────────────────────────────────────────
        public const string ForgeUpgrade   = "D1"; // 強化
        public const string ForgeApex      = "D2"; // 到達 (最終 Tier)

        // ── E: 憩い ────────────────────────────────────────────────
        public const string RestHeal       = "E1"; // 癒えた
        public const string RestHollow     = "E2"; // 空振り — 回復量がほぼ入らなかった

        // ── F: Λ層 ────────────────────────────────────────────────
        public const string LambdaEnter    = "F1"; // 踏み込み
        public const string LambdaLeave    = "F2"; // 離脱
        public const string LambdaSink     = "F3"; // 沈む — Λ 内で死亡

        // ── G: 決着 ────────────────────────────────────────────────
        public const string EndKill        = "G1"; // 撃破 (ボス)
        public const string EndDeath       = "G2"; // 敗北 (ラン終了)
        public const string EndClear       = "G3"; // 踏破 (ラン完走)

        /// <summary>1 ラン の上限。 これを超えたら以後は積まない。
        /// 1 ラン は戦闘 30・イベント 8・購入 22 程度なので 400 で十分に余る。
        /// 暴走したループがセーブを膨らませるのを防ぐための蓋であって、 較正値ではない。</summary>
        public const int MaxEntries = 400;

        // ─────────────────────────────────────────────────────────
        //  分類 — **ここが唯一の判定箇所**
        // ─────────────────────────────────────────────────────────

        /// <summary>戦闘の「形」を決める。 呼び出し側は生の数値だけ渡す。
        ///
        /// <para><b>激しさは被ダメで測り、 際どさだけ残 HP で測る。</b>
        /// 残 HP は「この戦闘」ではなく**ラン全体の経緯**を含んでしまう ──
        /// HP 40% で入って 5 ダメージしか受けずに勝った戦闘が「残 35% ＝ 削り合い」に化ける。
        /// 圧倒 / 削り合いは <paramref name="takenPct"/>（この戦闘で受けた量 ÷ 最大HP）で判定する。</para>
        ///
        /// <para>ただし<b>辛勝だけは残 HP</b>である。60 ダメージ受けても残 35% なら削り合いであって
        /// 辛勝ではない ── 「どれだけ殴られたか」と「どれだけ死に近づいたか」は別の軸で、
        /// 辛勝が言いたいのは後者だけ。</para>
        ///
        /// <para><b>サマリの緊張感曲線とは定義を分けてある。</b> あちらは knife-edge = 残 HP ≤ 20% /
        /// blowout = HP &gt; 80% から死亡 で、**バランス指標なので動かさない**。台帳側の辛勝は
        /// 30% まで広げている ── 20% では 12 ラン に 1 回しか出ず、
        /// **大半のランで 1 件も発生しない**ので物語の背骨に使えなかった（2026-08-15 実測 0.08/ラン）。
        /// 両者を突き合わせるときは、**同じ名前で違う量を数えている**ことに注意する。</para></summary>
        /// <param name="turns">戦闘ターン数。</param>
        /// <param name="startHpPct">戦闘開始時の HP 率 (0-100)。 blowout 判定に使う。</param>
        /// <param name="endHpPct">戦闘終了時の HP 率 (0-100)。 敗北なら 0。</param>
        /// <param name="takenPct">この戦闘で受けた被ダメ ÷ 最大HP (0-100)。
        /// <c>CombatResult.damageTaken</c> は最大HP との差なので**使えない**
        /// ── 削れた HP を引き継いで戦闘が始まる以上、 前の戦闘の分が混ざる。</param>
        public static string ClassifyCombat(bool won, int turns, int startHpPct, int endHpPct, int takenPct)
        {
            if (!won)
                return startHpPct > 80 ? CombatRout : CombatFall;

            if (endHpPct <= 30) return CombatNarrow;                  // 際どさ = 残 HP
            // **被ダメを必須にする。** 初版は `被ダメ35% || ターン8以上` の OR で、
            //   ボス戦は元から長いため**無傷でも削り合いに落ちた**
            //   （実出力で「8ターン立ち続けた／残り体力100%」という矛盾した記録が出た）。
            //   ターン数は激しさではない ── 長さは被ダメを伴って初めて削り合いになる。
            if (takenPct >= 20 || (turns >= 8 && takenPct >= 10)) return CombatGrind;
            if (takenPct <= 5 && turns <= 4) return CombatCrush;      // 無傷に近い速攻のみ
            return CombatClean;
        }

        // ─────────────────────────────────────────────────────────
        //  記録
        // ─────────────────────────────────────────────────────────

        public static void Combat(RunState run, string enemyId, bool boss,
                                  bool won, int turns, int startHpPct, int endHpPct, int takenPct)
        {
            CombatsSeen++;
            DamageTakenPct += takenPct;

            string code = ClassifyCombat(won, turns, startHpPct, endHpPct, takenPct);
            var sb = Begin(run, code);
            if (sb == null) return;
            Arg(sb, "e", enemyId);
            if (boss) Arg(sb, "boss", "1");
            Arg(sb, "t", turns);
            Arg(sb, "hp", endHpPct);
            Arg(sb, "d", takenPct);   // 被ダメ率。 文面で「かすり傷」と「死線」を分ける主軸
            Commit(run, sb);
        }

        public static void Buy(RunState run, string code, string itemId, int gold)
        {
            var sb = Begin(run, code);
            if (sb == null) return;
            Arg(sb, "i", itemId);
            Arg(sb, "g", gold);
            Commit(run, sb);
        }

        public static void Reroll(RunState run, int gold)
        {
            var sb = Begin(run, BuyReroll);
            if (sb == null) return;
            Arg(sb, "g", gold);
            Commit(run, sb);
        }

        public static void Event(RunState run, string code, string eventId, int choiceIndex)
        {
            var sb = Begin(run, code);
            if (sb == null) return;
            Arg(sb, "ev", eventId);
            Arg(sb, "c", choiceIndex);
            Commit(run, sb);
        }

        public static void Forge(RunState run, string code, string itemId, int tier)
        {
            var sb = Begin(run, code);
            if (sb == null) return;
            Arg(sb, "i", itemId);
            Arg(sb, "tier", tier);
            Commit(run, sb);
        }

        public static void Rest(RunState run, int healed, int beforeHp, int maxHp)
        {
            // [計装] 回復スポットが空振りする理由を分けるための到着時 HP 分布。
            //   1 回あたり 8.23 HP しか戻っていない (要求は最大HP の 30% ≒ 28) が、
            //   **上限で捨てられているのか、そもそも削れていないのか**が平均では分からない。
            int arrivePct = maxHp > 0 ? (int)(100L * beforeHp / maxHp) : 100;
            if (arrivePct < 0) arrivePct = 0; else if (arrivePct > 100) arrivePct = 100;
            RestArriveBuckets[arrivePct / 10]++;
            RestVisits++;
            RestHealed += healed;

            var sb = Begin(run, healed <= 2 ? RestHollow : RestHeal);
            if (sb == null) return;
            Arg(sb, "hp", healed);
            Arg(sb, "at", arrivePct);
            Commit(run, sb);
        }

        /// <summary>[計装] 回復スポット到着時の HP 率分布 (10% 刻み・添字 10 = 100%)。</summary>
        public static readonly long[] RestArriveBuckets = new long[11];
        public static long RestVisits, RestHealed;

        /// <summary>[計装] 「削られていない」と「削られたぶんを埋め戻している」を分けるための量。
        /// <para>回復スポットに満タンで着く現象は、 <b>殴られていない</b>場合と
        /// <b>殴られたが消耗品で戻してから着いた</b>場合の両方で同じ分布になる。
        /// 到着時 HP だけでは区別できないので、 被ダメ総量と戦闘中回復を併せて採る。</para>
        /// <para>判定: 被ダメが多いのに満タン到着が減らない → 埋め戻している。
        /// 被ダメ自体が増えていない → そもそも殴られていない。</para></summary>
        /// <para><b>被ダメはシールドを抜けて HP に届いた量である。</b> 吸われたぶんは
        /// 最初から入っていないので、 これだけ見ると軽減の主役を見落とす
        /// （実測でボス戦のシールドは回復の約 3 倍だった）。<see cref="ShieldGained"/> と併読すること。</para>
        public static long CombatsSeen, DamageTakenPct, HealInCombat, RunsSeen, ShieldGained;

        /// <summary>[計装 2026-08-15] 軽減無視をシールドで肩代わりできるようにした変更の追跡。
        /// 添字 0 = 全戦闘 / 1 = 5層ボス(業火の審判官)戦のみ。 **異常はこの敵にだけ出ている**ので
        /// 全体平均と分けて採らないと、 他の 40 種以上の戦闘に薄められて見えなくなる。</summary>
        public static readonly long[] ShieldToChip = new long[2];
        public static readonly long[] ShieldToNormal = new long[2];
        /// <summary>戦闘終了時に残っていたシールド。 **使い切れていない盾は死んだ資源**なので、
        /// 「チップに吸われて足りない」のか「そもそも余っている」のかはこれで割れる。</summary>
        public static readonly long[] ShieldLeftOver = new long[2];
        public static readonly long[] ChipRaw = new long[2];
        public static readonly long[] ChipToHp = new long[2];
        public static readonly long[] FightsSeen = new long[2];
        /// <summary>敗北した戦闘の**最終ターンに受けた量**と、 その直前のシールド残。
        /// 「盾が大きい一撃の時に残っていない」仮説の直接の検証点。</summary>
        public static readonly long[] LethalTurnDamage = new long[2];
        public static readonly long[] LethalShieldLeft = new long[2];
        public static readonly long[] Losses = new long[2];

        /// <summary>1 戦分の計装を積む。 <paramref name="isLayer5Boss"/> は業火の審判官戦か。</summary>
        public static void NoteShieldSplit(bool isLayer5Boss, int toChip, int toNormal,
                                           int leftOver, int chipRaw, int chipToHp,
                                           bool lost, int lethalTurnDamage, int shieldAtEnd)
        {
            for (int i = 0; i < 2; i++)
            {
                if (i == 1 && !isLayer5Boss) continue;
                FightsSeen[i]++;
                ShieldToChip[i] += toChip;
                ShieldToNormal[i] += toNormal;
                ShieldLeftOver[i] += leftOver;
                ChipRaw[i] += chipRaw;
                ChipToHp[i] += chipToHp;
                if (lost)
                {
                    Losses[i]++;
                    LethalTurnDamage[i] += lethalTurnDamage;
                    LethalShieldLeft[i] += shieldAtEnd;
                }
            }
        }

        /// <summary>[計装] 〈シールドバッシュ〉の攻撃ボーナス実測。 添字は <see cref="ShieldToChip"/> と同じ
        /// (0=全戦闘 / 1=5層ボス戦)。 <c>Procs</c> は攻撃機会の総数、 <c>Dry</c> は
        /// **盾が空で 1 点も乗らなかった**回数。 Dry が多い = 盾が火力として機能していない。</summary>
        public static readonly long[] BashBonus = new long[2];
        public static readonly long[] BashProcs = new long[2];
        public static readonly long[] BashDry = new long[2];

        public static void NoteShieldBash(InventorySystem.PassiveSkills.CombatContext ctx, int bonus)
        {
            bool b5 = ctx != null && ctx.bossId == BossIds.Layer5Judgment;
            for (int i = 0; i < 2; i++)
            {
                if (i == 1 && !b5) continue;
                BashProcs[i]++;
                BashBonus[i] += bonus;
                if (bonus <= 0) BashDry[i]++;
            }
        }

        /// <summary>[計装 2026-08-16] 7層 p4 の被ダメを**ターン帯 × 発生源**で割る。
        ///
        /// <para>p1+p2+p3 が 9 ターンで合計 7 しか削らないのに、 p4 は 14 ターンで 90 削る (13 倍)。
        /// 素火力の差 (5〜7 対 13) では説明できず、 実際 p4 の攻撃値を 14→13 にしても
        /// 平均被ダメは 57.3→57.2 と動かなかった。 敵攻撃値は
        /// <c>エスカレーション倍率 × (素火力 + 敵ダイス合計)</c> なので、
        /// **ダイス合計へ流れ込むもの (停滞スタック・遺物の出目加算) が主犯**という疑い。
        /// 推測を重ねず、 各項をターン帯ごとに実測する。</para>
        ///
        /// <para>添字 = ターン帯 (0: 1-4T / 1: 5-9T / 2: 10-14T / 3: 15T以降)。</para></summary>
        private const int P4Bands = 4;
        public static readonly long[] P4Turns = new long[P4Bands];
        public static readonly long[] P4Base = new long[P4Bands];       // 素火力
        public static readonly long[] P4DiceTotal = new long[P4Bands];  // 敵ダイス合計 (全ボーナス込み)
        public static readonly long[] P4Stagnation = new long[P4Bands]; // うち停滞スタック
        public static readonly long[] P4RelicDice = new long[P4Bands];  // うち遺物の出目加算 (当ターン)
        public static readonly long[] P4AtkValue = new long[P4Bands];   // 倍率適用後の敵攻撃値
        public static readonly long[] P4Block = new long[P4Bands];      // プレイヤーのブロック合計
        public static readonly long[] P4LossBase = new long[P4Bands];   // 貫通した分 (攻撃値 − ブロック)
        public static readonly long[] P4Bleed = new long[P4Bands];      // 大出血スタック

        public static int P4Band(int turn)
            => turn <= 4 ? 0 : turn <= 9 ? 1 : turn <= 14 ? 2 : 3;

        public static void NoteP4Turn(int turn, int baseAtk, int diceTotal, int stagnation,
                                      int relicDice, int atkValue, int block, int lossBase, int bleed)
        {
            int b = P4Band(turn);
            P4Turns[b]++; P4Base[b] += baseAtk; P4DiceTotal[b] += diceTotal;
            P4Stagnation[b] += stagnation; P4RelicDice[b] += relicDice;
            P4AtkValue[b] += atkValue; P4Block[b] += block; P4LossBase[b] += lossBase;
            P4Bleed[b] += bleed;
        }

        /// <summary>[計装 2026-08-16] 7層 4 段それぞれで、 プレイヤーがダイスを
        /// **どの端子に何本置いたか**。 添字 0..3 = p1..p4。
        ///
        /// <para>p1〜p3 の勝Tが 4.2/4.5/4.3 に対し p4 だけ 15.0 ── 実火力にして 4.5 倍の差がある。
        /// HP・自壊・シールド・消耗・遺物プール・会心の 6 つを順に潰したが、 どれも主因ではなかった。
        /// 残る仮説は「敵の攻撃値が大きいのでブロックに本数を割かれ、 攻撃端子に回る本数が減る」。
        /// **推測を続けず本数を直接数える。**</para></summary>
        public static readonly long[] PhaseTurns = new long[4];
        public static readonly long[] PhaseAtkDice = new long[4];
        public static readonly long[] PhaseBlkDice = new long[4];
        public static readonly long[] PhaseAtkSum = new long[4];
        public static readonly long[] PhaseBlkSum = new long[4];
        public static readonly long[] PhaseDealt = new long[4];

        public static void NotePhaseWiring(int phase, int atkDice, int blkDice,
                                           int atkSum, int blkSum, int dealt)
        {
            if (phase < 1 || phase > 4) return;
            int i = phase - 1;
            PhaseTurns[i]++; PhaseAtkDice[i] += atkDice; PhaseBlkDice[i] += blkDice;
            PhaseAtkSum[i] += atkSum; PhaseBlkSum[i] += blkSum; PhaseDealt[i] += dealt;
        }

        /// <summary>[計装 2026-08-16] 7層 各段の**実際の長さ**。 添字 0..3 = p1..p4。
        ///
        /// <para>サマリの per-phase 平均T と、 端子配分の計装が数えたターン数が食い違った
        /// (p4: 16.6 対 3.5)。 4 連戦は <c>currentTurn</c> をリセットしないので、
        /// どちらかが「連戦通算のターン番号」を段の長さとして出している疑いがある。
        /// **開始ターンと終了ターンを直接控えて確定させる。**</para></summary>
        public static readonly long[] PhaseDurSum = new long[4];
        public static readonly long[] PhaseDurCount = new long[4];
        public static readonly long[] PhaseStartSum = new long[4];
        public static readonly long[] PhaseEndSum = new long[4];

        public static void NotePhaseDuration(int phase, int startTurn, int endTurn)
        {
            if (phase < 1 || phase > 4) return;
            int i = phase - 1;
            PhaseDurCount[i]++;
            PhaseDurSum[i] += System.Math.Max(0, endTurn - startTurn + 1);
            PhaseStartSum[i] += startTurn;
            PhaseEndSum[i] += endTurn;
        }

        public static string DescribePhaseDuration()
        {
            var sb = new StringBuilder();
            sb.AppendLine("【7層 各段の実長 (計装)】");
            sb.AppendLine("  連戦は currentTurn をリセットしないため、 開始/終了ターンを直接控えたもの。");
            sb.AppendLine("  段    件数   開始T(平均)   終了T(平均)   実長(平均)");
            for (int i = 0; i < 4; i++)
            {
                double n = System.Math.Max(1, PhaseDurCount[i]);
                sb.AppendLine($"  p{i + 1} {PhaseDurCount[i],7} {PhaseStartSum[i] / n,13:F1} {PhaseEndSum[i] / n,13:F1}"
                            + $" {PhaseDurSum[i] / n,12:F1}");
            }
            return sb.ToString();
        }

        public static string DescribePhaseWiring()
        {
            var sb = new StringBuilder();
            sb.AppendLine("【7層 4段別 ダイス配分 (計装)】");
            sb.AppendLine("  段   ターン数  攻撃本数  ブロック本数  攻撃出目計  ブロック出目計  与ダメ/T");
            for (int i = 0; i < 4; i++)
            {
                double n = System.Math.Max(1, PhaseTurns[i]);
                sb.AppendLine($"  p{i + 1} {PhaseTurns[i],9} {PhaseAtkDice[i] / n,9:F2} {PhaseBlkDice[i] / n,13:F2}"
                            + $" {PhaseAtkSum[i] / n,11:F1} {PhaseBlkSum[i] / n,15:F1} {PhaseDealt[i] / n,10:F1}");
            }
            return sb.ToString();
        }

        public static string DescribeP4()
        {
            var sb = new StringBuilder();
            sb.AppendLine("【7層p4 敵攻撃値の分解 (計装)】");
            sb.AppendLine("  敵攻撃値 = エスカレーション倍率 × (素火力 + 敵ダイス合計)。 貫通 = 攻撃値 − ブロック。");
            sb.AppendLine("  帯      ターン数   素火力  ダイス計  うち停滞  うち遺物   攻撃値  ブロック   貫通  大出血");
            string[] label = { "  1-4T ", "  5-9T ", " 10-14T", " 15T+  " };
            for (int i = 0; i < P4Bands; i++)
            {
                double n = System.Math.Max(1, P4Turns[i]);
                sb.AppendLine($"{label[i]} {P4Turns[i],8} {P4Base[i] / n,8:F1} {P4DiceTotal[i] / n,9:F1}"
                            + $" {P4Stagnation[i] / n,9:F1} {P4RelicDice[i] / n,9:F1}"
                            + $" {P4AtkValue[i] / n,8:F1} {P4Block[i] / n,9:F1} {P4LossBase[i] / n,6:F1}"
                            + $" {P4Bleed[i] / n,7:F1}");
            }
            return sb.ToString();
        }

        public static string DescribeShieldBash()
        {
            var sb = new StringBuilder();
            sb.AppendLine("【シールドバッシュ = 残存シールドが火力 (計装)】");
            for (int i = 0; i < 2; i++)
            {
                double n = System.Math.Max(1, BashProcs[i]);
                sb.AppendLine((i == 0 ? "  ── 全戦闘   " : "  ── 5層ボス戦")
                            + $" 攻撃機会 {BashProcs[i]} / 空振り(盾0) {BashDry[i]}"
                            + $" ({100.0 * BashDry[i] / n:F1}%)"
                            + $" / 与えた攻撃ボーナス 総 {BashBonus[i]} (1機会 {BashBonus[i] / n:F2})");
            }
            return sb.ToString();
        }

        public static string DescribeShieldSplit()
        {
            var sb = new StringBuilder();
            sb.AppendLine("【シールドの配分と軽減無視ダメージ (計装)】");
            sb.AppendLine("  盾は有限の池。 毎ターン確実に飛ぶチップが先に食うと、 大きい一撃の時に残らない ── の検証。");
            for (int i = 0; i < 2; i++)
            {
                double n = System.Math.Max(1, FightsSeen[i]);
                double l = System.Math.Max(1, Losses[i]);
                sb.AppendLine(i == 0 ? "  ── 全戦闘" : "  ── 5層ボス(業火の審判官)戦のみ");
                sb.AppendLine($"     戦闘数 {FightsSeen[i]} / 敗北 {Losses[i]}");
                sb.AppendLine($"     盾→チップ {ShieldToChip[i]} (1戦 {ShieldToChip[i] / n:F2})"
                            + $" / 盾→通常攻撃 {ShieldToNormal[i]} (1戦 {ShieldToNormal[i] / n:F2})"
                            + $" / 使い残し {ShieldLeftOver[i]} (1戦 {ShieldLeftOver[i] / n:F2})");
                long used = ShieldToChip[i] + ShieldToNormal[i];
                sb.AppendLine($"     盾の用途比 チップ {(used > 0 ? 100.0 * ShieldToChip[i] / used : 0):F1}%"
                            + $" / 通常 {(used > 0 ? 100.0 * ShieldToNormal[i] / used : 0):F1}%");
                sb.AppendLine($"     軽減無視 総量 {ChipRaw[i]} (1戦 {ChipRaw[i] / n:F2})"
                            + $" → HP到達 {ChipToHp[i]} (1戦 {ChipToHp[i] / n:F2}"
                            + $" ・肩代わり率 {(ChipRaw[i] > 0 ? 100.0 * ShieldToChip[i] / ChipRaw[i] : 0):F1}%)");
                sb.AppendLine($"     敗北戦の最終ターン被ダメ 平均 {LethalTurnDamage[i] / l:F2}"
                            + $" / その時の残シールド 平均 {LethalShieldLeft[i] / l:F2}");
            }
            return sb.ToString();
        }

        public static string DescribeRest()
        {
            var sb = new StringBuilder();
            sb.AppendLine("【回復スポット 到着時 HP 分布】");
            long tot = 0; foreach (var v in RestArriveBuckets) tot += v;
            for (int i = 0; i <= 10; i++)
            {
                long v = RestArriveBuckets[i];
                string label = i == 10 ? "100%" : $"{i * 10}-{i * 10 + 9}%";
                sb.AppendLine($"  到着 {label,-8} {v,7}  ({(tot > 0 ? 100.0 * v / tot : 0),5:F1}%)");
            }
            sb.AppendLine($"  訪問 {RestVisits} / 実回復 {RestHealed} ＝ 1回 {(RestVisits > 0 ? RestHealed / (double)RestVisits : 0):F2} HP");
            double r = System.Math.Max(1, RunsSeen);
            sb.AppendLine($"  ラン {RunsSeen} / 戦闘 {CombatsSeen} ({CombatsSeen / r:F1}/ラン)");
            sb.AppendLine($"  被ダメ総量 {DamageTakenPct} (最大HP比%) ＝ 1ラン {DamageTakenPct / r:F0}% / 1戦 {(CombatsSeen > 0 ? DamageTakenPct / (double)CombatsSeen : 0):F1}%");
            sb.AppendLine($"  戦闘中の回復 {HealInCombat} HP ＝ 1ラン {HealInCombat / r:F1} HP"
                        + $" / 1戦 {(CombatsSeen > 0 ? HealInCombat / (double)CombatsSeen : 0):F2} HP");
            sb.AppendLine($"  シールド生成 {ShieldGained} ＝ 1ラン {ShieldGained / r:F1}"
                        + $" / 1戦 {(CombatsSeen > 0 ? ShieldGained / (double)CombatsSeen : 0):F2}");
            return sb.ToString();
        }

        public static void Lambda(RunState run, string code, int tiles, int gold)
        {
            var sb = Begin(run, code);
            if (sb == null) return;
            Arg(sb, "n", tiles);
            Arg(sb, "g", gold);
            Commit(run, sb);
        }

        public static void End(RunState run, string code, string enemyId)
        {
            var sb = Begin(run, code);
            if (sb == null) return;
            if (!string.IsNullOrEmpty(enemyId)) Arg(sb, "e", enemyId);
            Commit(run, sb);

            // ラン終了コードなら台帳の写しを残す。 RunState は次ランで Reset されるので、
            //   バッチのあとから 1 本を読み返す手段がこれしか無い。
            if (code == EndDeath || code == EndClear)
            {
                if (Enabled) LastRun = new List<string>(run.chronicle);
                RunsSeen++;
            }
        }

        /// <summary>最後に終了したランの台帳の写し。 バッチ後の読み返し・ダイジェスト生成用。</summary>
        public static List<string> LastRun;

        // ─────────────────────────────────────────────────────────
        //  組み立て
        // ─────────────────────────────────────────────────────────

        /// <summary>台帳の記録そのものを止めるスイッチ。 **性能の切り分け用**。
        /// false にすると 1 行も積まれず、 文字列生成もアロケーションも起きない
        /// （集計カウンタは軽いので別扱い）。 ダイジェスト機能は台帳が空なら
        /// 「(記録なし)」を返すだけで壊れない。</summary>
        /// <remarks>Play モード開始時にドメインリロードで既定値へ戻るので、
        /// 切り分けのときは**この既定値を直接書き換える**（実行時に外から設定しても間に合わない）。</remarks>
        public static bool Enabled = false;   // ← 2026-08-15 性能切り分け中。 確認後 true へ戻すこと

        /// <summary>行の頭 (コード + 層) を作る。 上限到達なら null を返す。</summary>
        private static StringBuilder Begin(RunState run, string code)
        {
            if (!Enabled) return null;
            if (run == null || string.IsNullOrEmpty(code)) return null;
            if (run.chronicle == null) run.chronicle = new List<string>();
            if (run.chronicle.Count >= MaxEntries) return null;

            var sb = new StringBuilder(48);
            sb.Append(code);
            Arg(sb, "f", run.currentFloor);
            return sb;
        }

        private static void Arg(StringBuilder sb, string key, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            sb.Append('|').Append(key).Append('=').Append(value);
        }

        private static void Arg(StringBuilder sb, string key, int value)
        {
            sb.Append('|').Append(key).Append('=').Append(value);
        }

        private static void Commit(RunState run, StringBuilder sb)
        {
            string line = sb.ToString();
            run.chronicle.Add(line);

            int cut = line.IndexOf('|');
            string code = cut > 0 ? line.Substring(0, cut) : line;
            CodeCounts.TryGetValue(code, out int n);
            CodeCounts[code] = n + 1;
        }

        // ─────────────────────────────────────────────────────────
        //  集計 (BOT 用・プロセス横断)
        // ─────────────────────────────────────────────────────────

        /// <summary>コード別の累計出現数。 **ラン跨ぎで積む**ので、
        /// バッチの頭で <see cref="ResetCounts"/> を呼ぶこと
        /// （呼ばないと前バッチの値が混ざる ── 2026-08-10 に <c>_combatSeq</c> で踏んだのと同じ罠）。</summary>
        public static readonly Dictionary<string, int> CodeCounts = new Dictionary<string, int>();

        public static void ResetCounts()
        {
            CodeCounts.Clear();
            System.Array.Clear(RestArriveBuckets, 0, RestArriveBuckets.Length);
            RestVisits = RestHealed = 0;
            CombatsSeen = DamageTakenPct = HealInCombat = RunsSeen = ShieldGained = 0;
        }

        /// <summary>コード表の全コード（出現 0 のものを「死んでいる行動」として出すために要る）。</summary>
        public static readonly string[] AllCodes =
        {
            CombatCrush, CombatClean, CombatGrind, CombatNarrow, CombatFall, CombatRout,
            BuyWeapon, BuyDice, BuyConsumable, BuyPassive, BuyForgo, BuyReroll,
            EventTake, EventRefuse, EventPrice,
            ForgeUpgrade, ForgeApex,
            RestHeal, RestHollow,
            LambdaEnter, LambdaLeave, LambdaSink,
            EndKill, EndDeath, EndClear,
        };

        /// <summary>コードから短い和名を引く（レポート表示用。 小説の文面ではない）。</summary>
        public static string NameOf(string code)
        {
            switch (code)
            {
                case CombatCrush:   return "戦闘/圧倒";
                case CombatClean:   return "戦闘/順当";
                case CombatGrind:   return "戦闘/削り合い";
                case CombatNarrow:  return "戦闘/辛勝";
                case CombatFall:    return "戦闘/惜敗";
                case CombatRout:    return "戦闘/一蹴";
                case BuyWeapon:     return "商い/主武装";
                case BuyDice:       return "商い/ダイス";
                case BuyConsumable: return "商い/消耗品";
                case BuyPassive:    return "商い/パッシブ";
                case BuyForgo:      return "商い/見送り";
                case BuyReroll:     return "商い/漁り";
                case EventTake:     return "事件/応じた";
                case EventRefuse:   return "事件/退けた";
                case EventPrice:    return "事件/代償";
                case ForgeUpgrade:  return "鍛え/強化";
                case ForgeApex:     return "鍛え/到達";
                case RestHeal:      return "憩い/癒えた";
                case RestHollow:    return "憩い/空振り";
                case LambdaEnter:   return "Λ/踏み込み";
                case LambdaLeave:   return "Λ/離脱";
                case LambdaSink:    return "Λ/沈む";
                case EndKill:       return "決着/撃破";
                case EndDeath:      return "決着/敗北";
                case EndClear:      return "決着/踏破";
                default:            return code;
            }
        }

        /// <summary>出現頻度レポート。 **0 件のコードを省略しない** ── 死んでいる行動を
        /// 見つけるのがこの出力の主目的なので、 出ないものこそ印字する。</summary>
        public static string DescribeCounts(int runs)
        {
            var sb = new StringBuilder();
            sb.AppendLine("【行動台帳 コード頻度】");
            sb.AppendLine("  コード 名称                 出現数   1ラン");
            int total = 0;
            foreach (var c in AllCodes)
            {
                CodeCounts.TryGetValue(c, out int n);
                total += n;
                double per = runs > 0 ? n / (double)runs : 0;
                string flag = n == 0 ? "  ← 一度も出ていない" : "";
                sb.AppendLine($"  {c,-6} {NameOf(c),-20} {n,8} {per,7:F2}{flag}");
            }
            sb.AppendLine($"  合計 {total} 行 / {runs} ラン ＝ 1ラン {(runs > 0 ? total / (double)runs : 0):F1} 行");
            return sb.ToString();
        }
    }
}
