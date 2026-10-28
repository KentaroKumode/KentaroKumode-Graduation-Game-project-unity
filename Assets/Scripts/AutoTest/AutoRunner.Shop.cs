using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using GameLoop;
using CombatSystem;
using MapSystem;
using EventSystem;
using ItemSystem;
using ItemSystem.Shop;
using AutoTest.Ultra;

namespace AutoTest
{
    /// <summary>AutoRunner: 金の圧力の計装・マス評価 (Rank)・ショップ。</summary>
    public partial class AutoRunner
    {
        public static void ResetGoldPressureStats() { GpSamples = GpZero = GpOne = 0; GpSum = 0; }

        [NonSerialized] private int _lastShopExitCoins;
        [NonSerialized] private bool _lastShopExitSeen;
        [NonSerialized] private int _lastShopExitFloor;
        [NonSerialized] private int _floor7ShopEntryCoins = -1;
        /// <summary>[計装 2026-09-18] 最終店の出口と 7 層の店の入口を <b>クリア(0) / 非クリア(1)</b> で分けた和。</summary>
        public static readonly long[] ExitByOutcomeN = new long[2], Floor7EntryN = new long[2];
        public static readonly double[] ExitByOutcomeCoins = new double[2], ExitByOutcomeGain = new double[2],
                                        ExitByOutcomeFloor = new double[2], Floor7EntryCoins = new double[2];
        /// <summary>[計装] 最後に店を出たときの残金と、 そこからラン終了までの増分 (ラン単位)。</summary>
        public static long ShopExitRuns;
        public static double ShopExitCoinsSum, PostShopGainSum;
        public static void ResetShopExitStats()
        {
            ShopExitRuns = 0; ShopExitCoinsSum = PostShopGainSum = 0;
            for (int i = 0; i < 2; i++)
            {
                ExitByOutcomeN[i] = Floor7EntryN[i] = 0;
                ExitByOutcomeCoins[i] = ExitByOutcomeGain[i] = ExitByOutcomeFloor[i] = Floor7EntryCoins[i] = 0;
            }
        }

        /// <summary>[計装] 余剰リロールループの出口。 0=リロール不可 / 1=水位 / 2=リロール失敗 / 3=回数上限
        /// / 4=価値 (停止則: 見込み ≤ 費用)。</summary>
        public static readonly long[] SurplusExit = new long[5];
        public static readonly double[] SurplusExitCoins = new double[5];
        public static void ResetSurplusExitStats()
        { System.Array.Clear(SurplusExit, 0, 5); System.Array.Clear(SurplusExitCoins, 0, 5); }
        private static void NoteSurplusExit(int why, int coins)
        { if (why < 0 || why > 4) return; SurplusExit[why]++; SurplusExitCoins[why] += coins; }

        /// <summary>[計装 2026-09-17] <b>リロールの経路別。</b> 店の処理には独立した
        /// リロールループが 5 本あり、 <b>どれが 223G を動かしているのか分かっていなかった</b>。
        ///
        /// <para>フェーズ2 (品質) の条件は「S 級を 1 個も持っていない」なので、
        /// <b>最初の店で S を買った時点で恒久停止する</b>はず ── そうであれば回数の大半は
        /// フェーズ2b の水位ルール (<c>coins &gt;= price + SurplusBuyReserve</c>) 由来で、
        /// <b>品物の評価が一切入っていない</b>ことになる。 推論のままにせず数える。</para>
        ///
        /// <para>添字は <see cref="RerollPathName"/> と対応。</para></summary>
        public static readonly long[] RerollPathCount = new long[6];
        public static readonly double[] RerollPathCoins = new double[6];
        public static readonly string[] RerollPathName =
        { "品質(S/A狙い)", "余剰再投資", "最終店の消耗品", "Super全賭け", "強盗直前", "防御ストック" };
        public static void ResetRerollPathStats()
        {
            System.Array.Clear(RerollPathCount, 0, 6); System.Array.Clear(RerollPathCoins, 0, 6);
            DiscardRolls = DiscardPassive = DiscardPart = DiscardWeapon = DiscardJunk = 0; DiscardPower = 0;
            System.Array.Clear(DiscardRollsByNth, 0, 4); System.Array.Clear(DiscardItemsByNth, 0, 4);
        }
        /// <summary>何回目のリロールか別 (0=1回目 / 1=2回目 / 2=3回目 / 3=4回目以降)。</summary>
        public static readonly long[] DiscardRollsByNth = new long[4], DiscardItemsByNth = new long[4];

        /// <summary>[計装 2026-09-18] リロールが張り替えで捨てた「フェーズ3 なら買った」枠。
        /// 種別ごとの件数、 準パワーの和、 そのうち PowerCuts[3] 未満 (スコアでは足切りされる品) の件数。</summary>
        public static long DiscardRolls, DiscardPassive, DiscardPart, DiscardWeapon, DiscardJunk;
        public static double DiscardPower;
        /// <summary>1 回ぶんを経路へ計上する。 <paramref name="paid"/> は実際に減ったゴールド
        /// (〈試し振りの符牒〉の無料リロールは 0 になる)。</summary>
        private static void NoteReroll(int path, int paid)
        { if (path < 0 || path > 5) return; RerollPathCount[path]++; RerollPathCoins[path] += paid; }

        /// <summary>[計装] ラン終了時の残金。 クリア / 非クリア 別。</summary>
        public static long EndGoldClearN, EndGoldDeadN;
        public static double EndGoldClearSum, EndGoldDeadSum;
        public static long EndGoldClear30, EndGoldDead30;
        public static void ResetEndGoldStats()
        { EndGoldClearN = EndGoldDeadN = EndGoldClear30 = EndGoldDead30 = 0;
          EndGoldClearSum = EndGoldDeadSum = 0; }
        private static void NoteEndGold(int coins, bool cleared)
        {
            if (cleared) { EndGoldClearN++; EndGoldClearSum += coins; if (coins >= 30) EndGoldClear30++; }
            else         { EndGoldDeadN++;  EndGoldDeadSum  += coins; if (coins >= 30) EndGoldDead30++; }
        }

        private static float GoldPressure(GameLoop.RunState run)
        {
            if (run == null) return 0.5f;
            float gp = Mathf.Clamp01(1f - run.coins / GoldComfort);
            GpSamples++; GpSum += gp;
            if (gp <= 0.0001f) GpZero++; else if (gp >= 0.9999f) GpOne++;
            return gp;
        }

        // ── エリートの航行評価 (2026-09-15) ──
        //   `Rank` の戦闘枝で `elite ? (EliteNavBase - EliteNavHpBonus * hpRatio) : 1f` として使う
        //   (**小さいほど優先**)。 通常戦の基準 1.0 に対する位置を HP 比で決める:
        //
        //       HP 100%  →  0.40   エリートを取りに行く
        //       HP  75%  →  0.70   まだエリート優先
        //       HP  50%  →  1.00   通常戦と釣り合う (交差点)
        //       HP  50% 未満       low ゲート (95/98) が先に弾く
        //
        //   交差点を 50% に置いたのは、 確信チェーンの強制優先枝が使う `hpRatio >= 0.5f` と
        //   同じ境界だから ── 二つの枝が違う HP 観を持っていると、 片方が他方を打ち消す。
        //   **較正専用**: AutoRunner の `eliteNavBase` / `eliteNavHpBonus` から上書きできる。

        // ── エリートの戦闘コスト見積り (2026-09-20) ──
        //   **旧判断は HP 比だけで、 自分の火力と相手の硬さを見ていなかった。** 満タンなら必ず取りに行くので、
        //   初期装備の 1 層でもほぼ毎ラン精鋭を踏み、 1 層エリート戦の死亡率が 46% (被ダメ平均 53%) だった。
        //   危険度ゲート (FightBudgetDanger) の基準「平均 1 戦 × 2」も中身はほぼ雑魚戦で、
        //   1 層では精鋭 1 戦 ≒ 雑魚 2.4 戦ぶん削られることが見えていなかった。
        //
        //   見積り: 精鋭の HP ÷ (このランの 1 ターン与ダメ) = 倒すまでのターン数、
        //           × (このランの 1 ターン被ダメ) × 精鋭の攻撃倍率 = 失う HP。
        //   戦った後に HP が EliteReserve (最大 HP 比) 以上残る見込みがなければ、 精鋭を避ける。
        //   **実績が無いうち (まだ雑魚と戦っていない) は精鋭を踏まない** ── 自分の強さが分からないまま
        //   格上に挑むのは判断ではなく賭け。

        /// <summary>精鋭の戦闘コスト見積りを使うか。 A/B 用 (AutoRunner.navEliteCost)。</summary>
        public static bool UseEliteCostNav = true;
        /// <summary>見積りの上振れ余裕。 精鋭は攻撃ロール上限 +3 で波が荒いので 1.5 倍で見る。</summary>
        public static float EliteLossSafety = 1.5f;
        /// <summary>精鋭戦の後に残したい HP (最大 HP 比)。</summary>
        public static float EliteReserve = 0.30f;
        /// <summary>[計装] 見積りで精鋭を避けた回数 / 見積りを行った回数 / 実績なしで避けた回数。</summary>
        public static long EliteCostChecks, EliteCostAvoided, EliteCostNoData;
        public static void ResetEliteCostStats() { EliteCostChecks = EliteCostAvoided = EliteCostNoData = 0; }

        /// <summary>この精鋭マスで失う HP の見積り。 実績が無ければ負を返す。</summary>
        private static float EliteFightLoss(MapNode node, GameLoop.RunState run)
        {
            if (run == null || run.navTurns <= 0 || run.navDealt <= 0) return -1f;
            float hp;
            var ea = node != null && node.encounterRevealed ? CombatSystem.EnemyDatabase.Get(node.encounterEnemyA) : null;
            float atkRatio;
            if (ea != null) { hp = ea.maxHP; atkRatio = EliteAttackRatio(ea, run.Depth); }
            else
            {
                // 未開示: この層に出るエリート敵の平均 (直近 3 層の窓は FloorManager と同じ)。
                float sum = 0, ratio = 0; int n = 0;
                foreach (var e in CombatSystem.EnemyDatabase.GetByFloorRange(Mathf.Max(1, run.Depth - 2), run.Depth))
                {
                    if (e == null || !e.elite) continue;
                    sum += e.maxHP; ratio += EliteAttackRatio(e, run.Depth); n++;
                }
                if (n == 0) return -1f;
                hp = sum / n; atkRatio = ratio / n;
            }
            float dealtPerTurn = run.navDealt / (float)run.navTurns;
            float takenPerTurn = run.navTaken / (float)run.navTurns;
            float turns = Mathf.Ceil(hp / Mathf.Max(1f, dealtPerTurn));
            return turns * takenPerTurn * atkRatio;
        }

        /// <summary>このエリートの攻撃が、 この層の雑魚の平均攻撃の何倍か。 ラン内の被ダメ実績 (ほぼ雑魚戦) をエリートへ換算する係数。</summary>
        private static float EliteAttackRatio(CombatSystem.EnemyData elite, int floor)
        {
            float sum = 0; int n = 0;
            foreach (var e in CombatSystem.EnemyDatabase.GetByFloorRange(Mathf.Max(1, floor - 2), floor))
            {
                if (e == null || e.elite || GameLoop.BossIds.IsBoss(e.id) || e.floor >= GameLoop.FloorManager.SpecialEncounterFloor) continue;
                sum += e.EffectiveBaseAttack + 0.5f * (e.attackRollMin + e.attackRollMax); n++;
            }
            if (n == 0 || sum <= 0f) return 1f;
            float normal = sum / n;
            return (elite.EffectiveBaseAttack + 0.5f * (elite.attackRollMin + elite.attackRollMax)) / normal;
        }

        /// <summary>精鋭を踏んでよいか。 見積りを使わない設定なら常に true。</summary>
        private static bool EliteAffordable(MapNode node, float hpRatio, GameLoop.RunState run)
        {
            if (!UseEliteCostNav || run == null || run.playerMaxHP <= 0) return true;
            EliteCostChecks++;
            float loss = EliteFightLoss(node, run);
            if (loss < 0f) { EliteCostNoData++; EliteCostAvoided++; return false; }
            float after = hpRatio - loss * EliteLossSafety / run.playerMaxHP;
            if (after >= EliteReserve) return true;
            EliteCostAvoided++;
            return false;
        }

        /// <summary>エリートの基準値 (HP 0 のときの値)。 大きいほどエリートを避ける。</summary>
        public static float EliteNavBase = 1.6f;
        /// <summary>HP 比 1 あたりの引き。 大きいほど「健康ならエリート」が強くなる。</summary>
        public static float EliteNavHpBonus = 1.2f;

        /// <summary>層化ホールドアウトの抽選。 候補を**提示重みの逆数**で重み付けして 1 つ選ぶ。
        ///
        /// <para>重みの出所は <see cref="ItemSystem.Shop.ShopManager.TierOfferWeight"/> 一箇所。
        /// ここで直書きすると、 ショップ側の重みを変えたときに静かに食い違う。</para>
        ///
        /// <para>rarity を引けない枠 (出目パーツ・強化素材) は中立の 0.25 相当として扱う。
        /// 在庫に居ない帯は選べないので、 均等化は<b>並んでいる範囲でのみ</b>成立する。</para></summary>
        private int PickStratified(ItemSystem.Shop.ShopInventory inv)
        {
            if (_exploreCandidates.Count == 1) return _exploreCandidates[0];
            var db = ItemSystem.ItemDatabase.Instance;
            double total = 0;
            _exploreWeights.Clear();
            for (int i = 0; i < _exploreCandidates.Count; i++)
            {
                var s = inv.slots[_exploreCandidates[i]];
                var data = (s != null && !string.IsNullOrEmpty(s.itemId)) ? db?.GetItem(s.itemId) : null;
                float w = data != null
                    ? 1f / Mathf.Max(0.001f, ItemSystem.Shop.ShopManager.TierOfferWeight(data.rarity))
                    : 4f;   // rarity 不明 (パーツ/素材) は 1/0.25
                _exploreWeights.Add(w);
                total += w;
            }
            double r = _exploreRng.NextDouble() * total;
            for (int i = 0; i < _exploreCandidates.Count; i++)
            {
                r -= _exploreWeights[i];
                if (r <= 0) return _exploreCandidates[i];
            }
            return _exploreCandidates[_exploreCandidates.Count - 1];
        }

        private readonly List<double> _exploreWeights = new List<double>();

        /// <summary>[計装] ラン終了時の家系別最高段を <see cref="FamilyTierStats"/> へ渡す。
        ///
        /// <para><b>14 家系すべてを渡す。</b> 取得された家系だけを渡すと
        /// 「0 段で終わった家系」が分母から落ち、 到達率が過大に出る。</para></summary>
        private void NoteFamilyTierRunEnd()
        {
            var run = GameManager.Instance?.Run;
            var db = ItemSystem.ItemDatabase.Instance;
            var max = new Dictionary<string, int>();
            foreach (var fam in ItemSystem.PassiveSkills.PassiveSkillRegistry.LeveledFamilies)
                max[fam] = 0;

            void Scan(string id)
            {
                var d = db?.GetItem(id);
                if (d?.passiveSkills == null) return;
                foreach (var ps in d.passiveSkills)
                {
                    if (string.IsNullOrEmpty(ps.internalName)) continue;
                    var (f, lv) = ItemSystem.PassiveSkills.PassiveSkillRegistry.GetFamilyLevel(ps.internalName);
                    if (lv > 0 && max.TryGetValue(f, out int prev) && lv > prev) max[f] = lv;
                }
            }
            if (run != null)
            {
                if (run.ownedPassiveItems != null) foreach (var id in run.ownedPassiveItems) Scan(id);
                Scan(run.equippedWeaponId);
                // 昇華済みも「到達した段」として数える (1G 割引の根拠と揃える)。
                if (run.ascendedPassiveIds != null) foreach (var id in run.ascendedPassiveIds) Scan(id);
            }
            AutoTest.FamilyTierStats.EndRun(max);
        }

        private float Rank(MapNode node, float hpRatio, bool averse, bool preferRest, GameLoop.RunState run,
                           float lowBar)
        {
            if (node == null) return 4f;
            TileType t = node.EffectiveType;
            var polN = AutoTest.PolicyParameters.Current;
            bool crit = hpRatio < polN.hpCritThreshold;   // 危機
            bool low  = hpRatio < lowBar;                 // 低HP (危険度駆動・床は hpLowThreshold)

            // 空腹尽きかけ／貪欲のボス前整え: 休憩を最優先（食事＝空腹全回復も兼ねる）
            if (preferRest && t == TileType.Rest) return -1;

            // 確信チェーン進路の強制優先 (両プロファイル共通)
            //  - 災厄の予兆 未完了 (= convictionStage 0) → イベント/ミステリ を常に最優先
            //    (Event/Mystery は戦闘でないので HP に関わらず追跡可能)
            //  - 災厄の予兆 完了 + 真理未到達 (stage 1〜4) → HP≥50% のみエリート最優先
            //    (エリートは戦闘発生 = HP リスクなので安全時のみ)
            int convStage = run?.convictionStage ?? 0;
            // 7層資格は6層の一度限りイベントでのみ得る。イベントマスを見つけたら
            // HPに関係なく最優先し、人間が進行条件を理解している場合を再現する。
            if (run != null && run.Depth == 6
                && GameLoop.ConvictionSystem.HasResolveOrBetter(run)
                && !GameLoop.ConvictionSystem.HasTruth(run)
                && t == TileType.Event
                && (!node.freeMap || node.isFixedEvent))   // 自由移動の層では固定の点「裂け目の記録」だけ
                return -20;
            if (convStage == 0)
            {
                // Mystery は 20% でしか Event 化しないため Bot の確信進路としては当てにせず、
                // Event タイルのみ強優先する。
                if (t == TileType.Event) return -10;
            }
            else if (convStage > 0 && convStage <= GameLoop.ConvictionSystem.StageTruth)
            {
                if (t == TileType.EliteBattle && hpRatio >= 0.5f && EliteAffordable(node, hpRatio, run)) return -5;
            }

            // --- 戦闘タイル: ここだけが比較軸（1変数） ---
            // HP低下時 (50%未満) は profile に関係なく Battle/Elite を強く忌避する。
            //  (回復を最優先にしたいというユーザー要望)
            // 金の逼迫度。 **2026-08-28 追加**。 それ以前の Rank は `run` を
            //   convictionStage と currentFloor にしか使っておらず、 **所持金を一度も見ていなかった**。
            //   「金が無いから戦って稼ぐ」「余っているから店を優先する」がどちらも表現できず、
            //   ショップは常に 1、 戦闘は常に 1/2 の固定階級だった。
            float gp = GoldPressure(run);

            if (t == TileType.Battle || t == TileType.EliteBattle)
            {
                bool elite = t == TileType.EliteBattle;

                // **開示済みなら、そのマスの敵で忌避を判断し直す** (2026-09-16)。
                //   `lowBar` の元になる EstimateFloorMaxHit は「その層までに出る敵の最悪値」で、
                //   4 層以降はずっと death_knight (攻撃 41) 基準。 ところが `node.encounterRevealed`
                //   なら**次に戦う敵は確定している** ── 下のスコア微調整 (0.03 × 攻撃値) では
                //   既にその値を読んでいるのに、 <b>戦うかどうかの足切りだけ層の最悪ケースのまま</b>
                //   だった。 弱い敵が見えているマスも death_knight 基準で門前払いしていた。
                //   規則は変えない (見えていないマスは従来どおり層の最悪値)。
                if (UseTileDanger && node.encounterRevealed)
                {
                    float tileBar = TileDanger(node, elite, run);
                    if (tileBar > 0f) low = hpRatio < tileBar;
                }

                // 精鋭は戦闘コストの見積りでも弾く (2026-09-20)。 弾いたら低 HP と同じ扱い (強く忌避)。
                if (elite && !low && !EliteAffordable(node, hpRatio, run)) low = true;
                if (low) return elite ? (crit ? 98f : 95f) : (crit ? 96f : 93f); // 低HP/危機: 強く忌避
                if (averse) return elite ? 93f : 90f;

                // ── エリート vs 通常戦の基準値 (2026-09-15 修正) ──
                //   **旧: `elite ? 2f : 1f` ＝ 通常戦がエリートより常に上位という固定順位。**
                //   下の gp 補正 (最大 1.1 対 0.8) を足しても差は 0.7〜1.0 残るので、
                //   **所持金がいくらでもエリートは一度も選ばれなかった**。
                //   実測ではエリートは踏むほど得 (格上げ率 0%→50% でクリア率 21.69%→28.13%・
                //   z=+13.0) で、 BOT は得な選択肢を構造的に避けていた。
                //
                //   危険は既に 3 箇所で見ている ── low/averse ゲート、 下の被攻撃値、
                //   そしてエリート敵の数値。 ここが持つべきは<b>報酬の差</b>と
                //   <b>それを受け止められる余力</b>で、 それは HP 比で表す:
                //   満タンなら取りに行き、 半分で通常戦と釣り合い、 それ以下は上のゲートが弾く。
                float score = elite ? (EliteNavBase - EliteNavHpBonus * hpRatio) : 1f;

                // 稼ぎの必要性。 無一文なら戦闘の優先度を上げる。
                //
                //   **エリートの取り分は CombatRewards.EliteRewardMul から引く** (2026-09-15・2026-09-20 に移設)。
                //   旧実装は `elite ? 1.1f : 0.8f` の決め打ちで、 <b>報酬倍率を上げても
                //   BOT の評価が一切動かなかった</b>。 危険側は 2026-09-15 に AttackMul を
                //   反映させたので、 報酬だけ固定のままだと<b>強化するほど一方的に嫌われる</b> ──
                //   実測で E (攻撃 ×1.35→×1.50) を入れるとエリート踏破が 6.32 → 5.39 に落ち、
                //   報酬を ×1.8 → ×2.6 に上げても戻らなかったのはこれが原因。
                //   危険と報酬の両方を同じ出典から読ませる。
                float goldPull = elite ? 0.8f * GameLoop.CombatRewards.EliteRewardMul : 0.8f;
                score -= goldPull * gp;

                // ── エンカウント (2026-08-28) ──
                //   **開示済みのノードしか見ない。** 未開示のプリセットを読むと BOT だけが
                //   1 マス前開示という規則の外側に立つことになり、 機構の意味が消える。
                if (node.encounterRevealed)
                {
                    // 2 体戦は**通常マス限定** (2026-09-14 以降エリートは常に単体・FloorManager)。
                    //   報酬 2 倍の引き ×(HPが高いほど強い) と、 攻撃 2 パケットの押し返し
                    //   ×(HPが低いほど強い)。 健康なら取りに行き、 削れていれば迂回する
                    //   ＝「普段は地雷、 仕上がっていれば最高の稼ぎ場」という設計そのもの。
                    if (!string.IsNullOrEmpty(node.encounterEnemyB))
                    {
                        score -= 1.4f * hpRatio;
                        score += 1.8f * (1f - hpRatio);
                    }

                    // 相手の攻撃値で薄く差をつける。 **戦闘マス同士の同点を割るのはここだけ。**
                    //   従来は同じ TileType なら常に同値だったので、 戦闘 vs 戦闘の比較は
                    //   100% 同点 → リスト順のタイブレークに落ちていた。
                    //
                    //   エリートマスの敵はエリート敵 (enemies.json の elite: true) そのものなので、 素の値で読む。
                    var ea = CombatSystem.EnemyDatabase.Get(node.encounterEnemyA);
                    if (ea != null) score += 0.03f * ea.EffectiveBaseAttack;
                    var eb = CombatSystem.EnemyDatabase.Get(node.encounterEnemyB);
                    if (eb != null) score += 0.03f * eb.EffectiveBaseAttack;
                }
                return score;
            }

            // --- 非戦闘タイル: 両プロファイル共通固定 ---
            switch (t)
            {
                case TileType.Rest:        return crit ? 0f : low ? 0f : 6f;
                // 店は所持金で価値が変わる。 無一文なら踏んでも何も買えず、
                //   潤沢なら Battle(1) を抜いて最優先級になる。
                case TileType.Shop:        return 1f + 2.0f * gp - 0.8f * (1f - gp);
                case TileType.Treasure:    return crit ? 2f : low ? 1f : -2f; // 装備強化源: T4到達率改善のため健康時優先度UP (0 → -2)
                case TileType.Event:       return crit ? 5f : 3f;
                case TileType.Mystery:     return crit ? 5f : 3f;
                case TileType.Exchange:    return crit ? 5f : 2f;  // ビルド強化源（厳密アップグレード）
                case TileType.Trap:        return crit ? 7f : 6f;
                case TileType.Gate:        return 9f;  // 7層の終端。踏まないと 8 層へ降りられない
                case TileType.Boss:        return 9f;  // 最後に残れば踏む(フロアクリア必須)
                case TileType.Outpost:     return 0f;
                default:                   return 4f;
            }
        }

        /// <summary><b>この店が「ランで最後のショップ」か (2026-09-12)。</b>
        ///
        /// <para><b>なぜ要るか。</b> 値下げ交渉(=強盗) の代償は
        /// 「以降のショップ価格 ×<see cref="ItemSystem.Shop.ShopManager.RobberySurcharge"/>」 で、
        /// <b>残りの店の数にそのまま比例する</b>。 旧規則の `currentFloor &gt;= 6` は条件を満たした
        /// 最初の店で撃つ貪欲規則だったので、 各フロアの確定ショップ (MapGenerator.ForceInjectShops) と
        /// 7 層のボス前確定ショップ (Shop→Rest→Boss) をまとめて割増価格にしていた。</para>
        ///
        /// <para><b>判定は純粋な到達可能性で行う。</b> 「7 層のあの店」 のような座標決め打ちは
        /// マップ生成を触るたびに黙って壊れる。 現在地から前進して届くノードに Shop が
        /// 1 つも無いことを確かめる ── 横移動リンクは同じ行へ戻るだけなので探索は前へしか進まない。
        /// <b>判定できないときは false</b> (＝撃たない) ── 撃つのは取り返しがつかない。</para></summary>
        /// <summary>この店で手をつけずに残す額。 <b>強盗の直前リロール用の取り置き。</b>
        /// 購入の足切り (<c>Buy</c> / <c>BuyPriorityPass</c>) がここを差し引いて判断する。
        /// 店を出るたびに 0 へ戻す。</summary>
        private int _shopReserve;

        private static bool IsFinalShopOfRun(GameLoop.RunState run)
        {
            if (run == null) return false;
            // 以降のフロアには確定ショップが 1 個ずつ生える。 **店が生える最後の層**より
            //   手前なら、 この店が最後ということはありえない。
            //   <b>maxFloor ではない</b> ── 8 層 (Null Point) に店は無いので、
            //   maxFloor で比べると永遠に成立せず強盗が撃たれなくなる (2026-09-14)。
            if (run.Depth < MapSystem.MapGenerator.LastFloorWithShop) return false;

            var mm = MapSystem.MapManager.Instance;
            var map = mm != null ? mm.CurrentMap : null;
            var cur = mm != null ? mm.CurrentNode : null;
            if (map == null || cur == null) return false;

            var seen = new HashSet<string> { cur.id };
            var stack = new Stack<string>();
            stack.Push(cur.id);
            while (stack.Count > 0)
            {
                var node = map.GetNode(stack.Pop());
                if (node?.connections == null) continue;
                foreach (var nextId in node.connections)
                {
                    if (!seen.Add(nextId)) continue;
                    var next = map.GetNode(nextId);
                    if (next == null) continue;
                    if (next.EffectiveType == MapSystem.TileType.Shop) return false;
                    stack.Push(nextId);
                }
            }
            return true;
        }

        private void DoShop()
        {
            var gm = GameManager.Instance;
            var sm = ShopManager.Instance;
            var inv = sm != null ? sm.Current : null;

            // 物資の補給 (2026-09-28): 物資が物資回復の閾値を割っていたら、 棚より先に包 (100 = 10G) を買う。
            //   自由移動の層では物資が移動の燃料で、 払底すると HP で歩くことになる。
            if (sm != null && gm.Run != null)
            {
                int guardPack = 0;
                while (gm.Run.provision <= ProvisionRefillFloorEffective() && guardPack++ < ShopManager.ProvisionPackStock
                       && sm.TryBuyProvisionPack(gm.Run)) { }
            }

            if (inv != null && inv.slots != null)
            {
                var run = gm.Run;

                // L1出現lift用: このショップでの提示アイテムを記録 (購入有無を問わず)
                if (_cur != null)
                {
                    for (int oi = 0; oi < inv.slots.Count; oi++)
                    {
                        var os = inv.slots[oi];
                        if (os != null && !string.IsNullOrEmpty(os.itemId))
                            _cur.offeredItemsEver.Add(os.itemId);
                    }
                }

                bool Buy(int i)
                {
                    var s = inv.slots[i];
                    // **支払い可能額で見る** ── 燈火 r10 は不足分を物資で払える。
                    //   ここを run.coins のままにすると、 足切りを通した候補が
                    //   この入口で弾かれ、 **機構が 1 回も発動しない** (実測 170,000 ラン で 0 件)。
                    //   同じ判断を 2 箇所に置いた典型で、 2026-08-10 の WouldUpgrade と同じ形。
                    if (s == null || s.sold
                        || s.price > GameLoop.ProvisionPayment.PolicyAffordable(run) - _shopReserve)
                        return false;
                    if (s.kind != ItemSystem.Shop.ShopSlotKind.WeaponMaterial
                        && !string.IsNullOrEmpty(s.itemId))
                    {
                        // 2026-06-22 Phase C: 購入候補が現所持品の上位 Lv なら、 下位 Lv を先に売却 (装備武器は除く)
                        TrySellLowerTierBefore(run, s.itemId);
                    }
                    int before = run.coins;
                    string id = s.itemId;
                    gm.ShopBuy(i);
                    // **成否は「金が減ったか」では判定しない。** 全額を物資で払った場合
                    //   (所持金 0 で購入) は金が動かないので、 買えたのに失敗として数えてしまう。
                    //   在庫が売り切れたかを見るのが素直 ── 素材枠だけは sold を立てないので
                    //   従来どおり金の減少で見る。
                    bool bought = s.kind == ItemSystem.Shop.ShopSlotKind.WeaponMaterial
                        ? run.coins < before
                        : s.sold;
                    if (bought)
                    {
                        _cur.shopPurchases++;
                        // 種別ごとに分解する。 価格倍率の弾性が枠ごとに違うため (上記コメント)。
                        switch (s.kind)
                        {
                            case ItemSystem.Shop.ShopSlotKind.Passive:        _cur.shopBuyPassive++; break;
                            case ItemSystem.Shop.ShopSlotKind.Consumable:     _cur.shopBuyConsumable++; break;
                            case ItemSystem.Shop.ShopSlotKind.Weapon:         _cur.shopBuyWeapon++; break;
                            case ItemSystem.Shop.ShopSlotKind.FacePart:       _cur.shopBuyDice++; break;
                            case ItemSystem.Shop.ShopSlotKind.WeaponMaterial: _cur.shopBuyMaterial++; break;
                        }
                        if (AutoTest.LearnedPriorityProvider.IsPriority(id)) _cur.priorityItemsAcquired++;
                        // L1学習: 購入時点で取得集合に記録（後で使い切って消えても残る）
                        if (!string.IsNullOrEmpty(id)) _cur.acquiredItemsEver.Add(id);
                        return true;
                    }
                    return false;
                }

                // Phase C: 購入候補のパッシブが、 現所持品 (装備武器以外) の上位 Lv に該当するなら下位を売却
                void TrySellLowerTierBefore(RunState rs, string candidateItemId)
                {
                    var db = ItemSystem.ItemDatabase.Instance;
                    if (db == null || rs == null) return;
                    var cdata = db.GetItem(candidateItemId);
                    if (cdata?.passiveSkills == null) return;
                    // 候補の各パッシブの (家系, Lv) を抽出
                    var candidateFamilyLv = new Dictionary<string, int>();
                    foreach (var ps in cdata.passiveSkills)
                    {
                        if (string.IsNullOrEmpty(ps.internalName)) continue;
                        var (fam, lv) = ItemSystem.PassiveSkills.PassiveSkillRegistry.GetFamilyLevel(ps.internalName);
                        if (lv > 0 && (!candidateFamilyLv.TryGetValue(fam, out int prev) || lv > prev))
                            candidateFamilyLv[fam] = lv;
                    }
                    if (candidateFamilyLv.Count == 0) return;
                    // 所持品 (装備武器・装備ダイス以外) を見て、 下位 Lv を持つアイテムを売却対象に
                    int safety = 0;
                    while (safety++ < 8)
                    {
                        int targetIdx = -1;
                        for (int j = 0; j < rs.ownedPassiveItems.Count; j++)
                        {
                            string ownedId = rs.ownedPassiveItems[j];
                            if (string.IsNullOrEmpty(ownedId)) continue;
                            if (ownedId == rs.equippedWeaponId) continue; // 装備武器は除外
                            if (ownedId == rs.equippedDiceId) continue;
                            var odata = db.GetItem(ownedId);
                            if (odata?.passiveSkills == null) continue;
                            bool isLowerTier = false;
                            foreach (var ops in odata.passiveSkills)
                            {
                                if (string.IsNullOrEmpty(ops.internalName)) continue;
                                var (ofam, olv) = ItemSystem.PassiveSkills.PassiveSkillRegistry.GetFamilyLevel(ops.internalName);
                                if (olv > 0 && candidateFamilyLv.TryGetValue(ofam, out int candLv) && candLv > olv)
                                {
                                    isLowerTier = true; break;
                                }
                            }
                            if (!isLowerTier) continue;
                            // 2026-06-23c: 由来制限撤廃 ── TrySellFromBot が「ショップ滞在中なら由来問わず売却可」 を統括
                            targetIdx = j;
                            break;
                        }
                        if (targetIdx < 0) break;
                        string sellId = rs.ownedPassiveItems[targetIdx];
                        int coinsBefore = rs.coins;
                        if (AutoTest.BotSelling.TrySellFromBot(rs, targetIdx))
                            UnityEngine.Debug.Log($"[Phase C] 上位購入前に下位 {sellId} を売却 (+{rs.coins - coinsBefore}G)");
                        else break;
                    }
                }

                // --- [アブレーション] ショップの購入判断を潰す ---
                //   **買う/買わないの選別だけを捨てる。** 買えるものから一様に選んで金が尽きるまで買う。
                //   売却・優先度・戦力ギャップの判断が全部飛ぶので、 落ち幅が
                //   「ショップで何を買うか」の技量そのものになる。
                if (ablateShop)
                {
                    var cand = new List<int>();
                    for (int guard = 0; guard < 40; guard++)
                    {
                        cand.Clear();
                        for (int i = 0; i < inv.slots.Count; i++)
                        {
                            var s = inv.slots[i];
                            if (s != null && !s.sold && s.price <= run.coins) cand.Add(i);
                        }
                        if (cand.Count == 0) break;
                        int pick = cand[AblationRng.Next(0, cand.Count)];
                        // 買えない枠が 1 つだけ残ったら打ち切る (無限ループ防止)。
                        if (!Buy(pick) && cand.Count == 1) break;
                    }
                    gm.ExitShop();
                    return;
                }

                bool BuyKind(ShopSlotKind k)
                {
                    // 武器/ダイスは **装備に至るものだけ**。 劣る個体を買っても
                    //   Loadout.TryAutoEquip が装備しないので、 ゴールドが消えるだけ (2026-08-10)。
                    // 出目パーツは「増設」なので更新判定 (WouldUpgrade) の対象外。
                    //   旧ダイスは面が同じで常に false → 1 個も買われなかった。
                    bool gearOnly = (k == ShopSlotKind.Weapon);
                    for (int i = 0; i < inv.slots.Count; i++)
                        if (inv.slots[i] != null && !inv.slots[i].sold
                            && inv.slots[i].kind == k
                            && (!gearOnly || GameLoop.Loadout.WouldUpgrade(run, inv.slots[i].itemId))
                            && Buy(i)) return true;
                    return false;
                }

                /// <summary>手持ちの**回復薬**の本数。 シールド薬・攻撃薬・物資薬は数えない。</summary>
                int HealCount()
                {
                    if (run.ownedConsumables == null) return 0;
                    int n = 0;
                    foreach (var id in run.ownedConsumables)
                        if (!string.IsNullOrEmpty(id)
                            && GameLoop.ItemIds.ConsFamilyOf(id) == GameLoop.ItemIds.ConsHealFamily) n++;
                    return n;
                }

                /// <summary>回復薬を 1 本買う。 買えるうち **最上位 Tier** を選ぶ。
                /// 回復は毎回 1 枠固定で並ぶ (ItemIds.ConsumableFixedFamily) ので、
                /// 金さえあれば必ず取れる。</summary>
                bool BuyHeal()
                {
                    int bestIdx = -1, bestTier = 0;
                    for (int i = 0; i < inv.slots.Count; i++)
                    {
                        var s = inv.slots[i];
                        if (s == null || s.sold || s.kind != ShopSlotKind.Consumable) continue;
                        if (string.IsNullOrEmpty(s.itemId)
                            || GameLoop.ItemIds.ConsFamilyOf(s.itemId) != GameLoop.ItemIds.ConsHealFamily) continue;
                        if (s.price > run.coins) continue;
                        int t = GameLoop.ItemIds.ConsTierOf(s.itemId);
                        if (t > bestTier) { bestTier = t; bestIdx = i; }
                    }
                    return bestIdx >= 0 && Buy(bestIdx);
                }

                /// <summary>**回復薬を 1 本も持っていないなら、 何より先に確保する (2026-08-16)。**
                ///
                /// <para>旧実装は「消耗品を 1 つも持っていないなら」だったが、 消耗品は 4 系統
                /// (回復/シールド/攻撃強化/物資) あるので、 **シールド薬 1 本を持っているだけで
                /// この保険が外れていた**。 実測 (batch_20260816_145127): p4 で死亡した 199 ランのうち
                /// 回復薬 0 本が 90.5% ある一方、 消耗品の所持数は平均 0.86 ＝
                /// 「回復以外は持っているのに回復は無い」状態が常態化していた。
                /// 判定を回復系統に限定する。</para></summary>
                bool EnsureHealStock()
                {
                    if (HealCount() > 0) return false;
                    if (BuyHeal()) return true;
                    // 回復が買えない (金/売切) なら、 保険として他系統でも確保しておく。
                    return BuyKind(ShopSlotKind.Consumable);
                }

                /// <summary>手持ちの**防御系** (回復 + シールド) の本数。</summary>
                int DefensiveCount()
                {
                    if (run.ownedConsumables == null) return 0;
                    int n = 0;
                    foreach (var id in run.ownedConsumables)
                        if (!string.IsNullOrEmpty(id)
                            && (GameLoop.ItemIds.ConsFamilyOf(id) == GameLoop.ItemIds.ConsHealFamily
                             || GameLoop.ItemIds.ConsFamilyOf(id) == GameLoop.ItemIds.ConsShieldFamily)) n++;
                    return n;
                }

                /// <summary>陳列中の防御系のうち**最上位 Tier** を 1 つ買う。</summary>
                bool BuyBestDefensive()
                {
                    int bestIdx = -1, bestTier = 0;
                    for (int i = 0; i < inv.slots.Count; i++)
                    {
                        var s = inv.slots[i];
                        if (s == null || s.sold || s.kind != ShopSlotKind.Consumable) continue;
                        if (string.IsNullOrEmpty(s.itemId)) continue;
                        if (GameLoop.ItemIds.ConsFamilyOf(s.itemId) != GameLoop.ItemIds.ConsHealFamily
                         && GameLoop.ItemIds.ConsFamilyOf(s.itemId) != GameLoop.ItemIds.ConsShieldFamily) continue;
                        if (s.price > run.coins) continue;
                        int t = GameLoop.ItemIds.ConsTierOf(s.itemId);
                        if (t > bestTier) { bestTier = t; bestIdx = i; }
                    }
                    return bestIdx >= 0 && Buy(bestIdx);
                }

                /// <summary>**回復・シールドを上限まで積む。 在庫に無ければ再入荷させてでも探す。**
                ///
                /// <para>実測 (batch_20260817_042337): 消耗品は 1 ラン 14.7 回買われており、
                /// 金も在庫も制約になっていない。
                /// それでも p4 死亡時の回復所持 0 本が 69% なのは、
                /// **道中で使い切ってボス前に手元が薄くなる**ため。 買う量ではなく
                /// 「ボスに入る時点で満タンか」を保証する。</para>
                ///
                /// <para>回復は毎回 1 枠固定で並ぶ (ItemIds.ConsumableFixedFamily) が、
                /// シールドは残り 2 枠を攻撃強化・物資と争う抽選なので **並ばない店がある**。
                /// リロールが要るのは主にシールド側。</para></summary>
                void StockDefensive(int cap)
                {
                    int guard = 24;
                    while (DefensiveCount() < cap && guard-- > 0)
                    {
                        if (BuyBestDefensive()) continue;
                        // 陳列に無い → 再入荷させて探す。 回した後に 1 つ買える額は残す
                        //   (最安の消費は basePrice 4 なので、 倍率込みでも 8 あれば足りる)。
                        int rp = inv.CurrentRerollPrice;
                        if (rp <= 0 || run.coins < rp + 8) break;
                        if (!RuleSaysReroll(rp)) break;   // 停止則 on のときだけ効く
                        if (Phase3BeforeReroll) BuyPhase3Items();   // A/B: 捨てる前に買い切る
                        int before = run.coins;
                        NoteShelfDiscard();   // [計装] 捨てる棚を数える
                        gm.ShopReroll();
                        if (run.coins >= before) break;
                        _cur.shopRerolls++;
                        _cur.shopRerollCoins += before - run.coins;
                        NoteReroll(5, before - run.coins);
                    }
                }

                // ============================================================
                // フェーズ0: 値下げ交渉(=強盗) 判定  ── **購入より先に置く**
                //
                //   旧版はフェーズ2.5 (購入の後) に在って、 3 つの意味で損をしていた:
                //     ① 棚の未売却分は強盗で**タダで手に入る**のに、 先に金を払って買っていた。
                //     ② `StockDepletedByPurchase()` が「1 枠でも sold なら true」 なので、
                //        ①で必ず何か買っている以上ほぼ常に真 → 毎回リロールを 3 回転ぶん金を捨てていた。
                //     ③ 強盗は `return` で抜けるため、 フェーズ3 の StockDefensive /
                //        EnsureHealStock が走らず、 **回復薬もシールド薬も積まずに**先へ進んでいた。
                //   撃つと決めたなら買わない・回さない。 撃たないと決めたなら以降は普通の店。
                //
                //   タイミングは「ランで最後のショップ」に限定する。 旧版の `currentFloor >= 6` は
                //   条件を満たした**最初の**店で撃つ貪欲規則で、 各フロアの確定ショップと
                //   7 層のボス前確定ショップ (MapGenerator: Shop→Rest→Boss) を捨てていた。
                // ============================================================
                //   **2026-09-13: 6 層以降で決め打ち。** 規則は 1 ラン 1 回のまま。
                //
                //     旧「ラン最後の店」は最終層へ到達したランでしか撃てず、 発動 11%。
                //     しかも最後の店で撃つと以降に店が無く、 **手持ちのゴールドが
                //     使い道を失ったまま消える**。 6 層で撃てば 7 層の店が残る。
                //
                //     <b>条件を全部外したら壊れた。</b> 階層ゲートが 1 つも無いと
                //     `BaseStartingHP = 75` で開幕から HP 条件を満たし、
                //     **最初に出会った店で撃つ** ── 実測の分布は
                //     F1=44.8% / F2=30.2% / F3=23.8% で 98.8% が 1〜3 層、 6 層以降は 0%。
                //     HP1840・被ダメ50%軽減のエリートに 1 層の装備で挑んで勝率 23.4%、
                //     平均到達段 2.97 (Balanced 7.70) まで落ちた。
                //
                //     層は「強さ」の代理でしかないが、 代理として機能する。
                //     **実行済みフラグは必ず見ること** ── 外すと 2 軒目以降、
                //     規則に弾かれるのに return でショップ処理ごと飛ばしてしまう。
                bool plannedRobbery =
                    MetaProgression.MetaBuffApplicator.IsShopRobberyUnlocked()
                    && !run.shopRobberyDone
                    && (robberyFinalShopOnly ? IsFinalShopOfRun(run) : run.Depth >= 6)
                    && run.playerMaxHP >= 50     // 最大HP下限 固定 (序盤の貧弱を除外)
                    && run.playerHP >= run.playerMaxHP * AutoTest.PolicyParameters.Current.robberyMinHpRatio;

                // **撃つと決めても、 先に買い物を済ませる (2026-09-13)。**
                //   旧実装はここで即 ShopRobbery して return していたため、
                //   **この店で 1 つも買わずにゴールドを抱えたまま次層へ行き**、
                //   価格 ×2 の罰でその金の価値が半減していた。 6 層で撃つと
                //   7 層がラン最後の買い物機会なので、 そこが一番痛い。
                //
                //   正しい順序は 「買い占める → リロール → 奪う」。
                //   **リロールは割増の対象外** (ShopInventory.CurrentRerollPrice は
                //   priceMultiplier を参照しない) なので、 割増前のレートで
                //   ゴールドを「棚の質」へ変換できる。 その 1 回ぶんだけ取り置く。
                _shopReserve = plannedRobbery ? inv.CurrentRerollPrice : 0;
                // [計装 2026-09-18] 7 層の店に入った時点の所持金 (ランで最初の 1 回だけ)。
                if (run.Depth >= 7 && _floor7ShopEntryCoins < 0) _floor7ShopEntryCoins = run.coins;
                // [廃止 2026-09-14] 〈貪欲の儀〉のためのゴールド取り置き。
                //   門リワークでゴールドの要求そのものが無くなった (代償は 最大HP / 遺物 / 物資)。

                // ============================================================
                // フェーズ1: 在庫の S/A 級を先取り（買えるだけ買う）
                //   2026-06-22: 買えない S/A があり、 廃棄候補 (Score=0 かつショップ由来在庫あり) を所持しているなら
                //               TrySell で換金してから購入を試みる。
                //   2026-06-23: Power 帯認識 (Weak/Early は C+ 購入、 Late/Apex は A+ のみ)。
                // ============================================================
                // 2026-06-23c: Power 帯は購入で変動するため、 毎回再計算 (旧版は 1 ショップ訪問で 1 度のみ算出し帯遷移を取り逃していた)。
                int CurrentPowerBand()
                    => AutoTest.InventoryPower.GetPowerBandRank(AutoTest.InventoryPower.Compute(run));
                int CurrentDynamicMinScore()
                {
                    int b = CurrentPowerBand();
                    return b <= 1 ? 1 : (b <= 2 ? 2 : 3);
                }
                // 連続値版の足切り (2026-08-17b: Tier 枠廃止に伴い準パワーの絶対しきい値へ)。
                //   Weak/Early は中立まで、 Mid は余裕時帯以上、 Late/Apex は強推以上。
                //   整数版 CurrentDynamicMinScore と同じ段に対応させてある。
                float CurrentDynamicMinPower()
                {
                    var cuts = AutoTest.LearnedPriorityProvider.PowerCuts;
                    int b = CurrentPowerBand();
                    return b <= 1 ? cuts[3] : (b <= 2 ? cuts[2] : cuts[1]);
                }

                bool HasUnaffordablePriority()
                {
                    int dyn = CurrentDynamicMinScore();
                    for (int i = 0; i < inv.slots.Count; i++)
                    {
                        var s = inv.slots[i];
                        if (s == null || s.sold) continue;
                        // 物資で届くなら「買えない」ではないので、 売却して工面する必要が無い。
                        if (s.price <= GameLoop.ProvisionPayment.PolicyAffordable(run)) continue;
                        if (string.IsNullOrEmpty(s.itemId)) continue;
                        // Score 細分化 (2026-06-22): B+ (>=2) を S/A/B 帯として扱う
                        // 特売補正込み: 40% 割引以上は Score +1、 60% 以上は +2
                        // 2026-06-23: Power 帯認識: Weak/Early は C+ で十分価値あり、 Mid 以上は B+
                        int sc = AutoTest.LearnedPriorityProvider.Score(s.itemId) + PersonaBonus(s.itemId);
                        int saleBonus = s.discountPct >= 60 ? 2 : (s.discountPct >= 40 ? 1 : 0);
                        if (sc + saleBonus >= dyn) return true;
                    }
                    return false;
                }
                bool SellOneScore0()
                {
                    // (商人の符牒 売却阻止フックは 2026-07-18 アイテム削除に伴い除去)
                    for (int i = 0; i < run.ownedPassiveItems.Count; i++)
                    {
                        string id = run.ownedPassiveItems[i];
                        if (string.IsNullOrEmpty(id)) continue;
                        if (id == run.equippedWeaponId || id == run.equippedDiceId) continue;
                        if (AutoTest.LearnedPriorityProvider.Score(id) + PersonaBonus(id) > 0) continue;
                        // 2026-06-23c: 由来制限撤廃 (TrySellFromBot 内で符牒/ショップ滞在を統括判定)
                        if (AutoTest.BotSelling.TrySellFromBot(run, i)) return true;
                    }
                    return false;
                }
                int sellGuard = 0;
                while (HasUnaffordablePriority() && SellOneScore0() && sellGuard++ < 16) { }

                // 2026-06-23: Power 帯認識ショップ判定 (dynamicMinScore は上で算出済み)
                //   Weak(<10)/Early(<25): C 級 (Score 1) も購入対象、 minScore 緩和
                //   Mid(25-49): 標準 (B+)
                //   Late/Apex(>=50): A+ のみ (ゴミ買い禁止、 G 温存)
                // 2026-08-17b: **Tier 枠を廃し、準パワー (連続値) の降順で上から買う**。
                //   旧版は整数 Score (S=4..C=1) を主軸に、 同値のときだけ連続値で割っていた。
                //   段が粗いぶん「同点」が頻発し、 実質 ΔPower/G が決めていた場面が多い。
                //   枠が無くなった以上、 主軸そのものを連続値にするのが素直。
                //   <paramref name="minPower"/> は caller が指定する下限 (Power 帯の下限と max を取る)。
                // 停止則が「1 回のリロールで実際にどれだけ買えたか」を読むための積算。
                float passPowerBought = 0f;

                /// 1 枠の購入意欲 (準パワー + 補正)。 買えない・対象外なら false。
                /// **BuyPriorityPass と停止則の両方がここを通る** ── 同じ判断を 2 箇所に書くと
                /// 片方だけ直してずれる (2026-08-10 WouldUpgrade の教訓)。
                bool TrySlotPower(int i, out float pw)
                {
                    pw = 0f;
                    var s = inv.slots[i];
                    if (s == null || s.sold) return false;
                    // 燈火 r10 (物資払い) を織り込んだ支払い可能額。 規則より保守的な PolicyFloor。
                    if (s.price > GameLoop.ProvisionPayment.PolicyAffordable(run) - _shopReserve) return false;
                    // **武器は装備判定を通す** (2026-08-10)。 主経路にゲートが無く素通りしていた。
                    if (s.kind == ShopSlotKind.Weapon && !GameLoop.Loadout.WouldUpgrade(run, s.itemId)) return false;
                    float step = AutoTest.LearnedPriorityProvider.StepPerTier;
                    if (s.kind == ShopSlotKind.FacePart
                        && !AutoTest.LearnedPriorityProvider.HasLearned(s.itemId))
                    {
                        int raw = AutoTest.InventoryPower.FacePartPower(run, s.facePart);
                        if (raw <= 0) return false;
                        pw = AutoTest.InventoryPower.BootstrapPowerScore(raw);
                    }
                    else
                    {
                        pw = AutoTest.LearnedPriorityProvider.BuyScore(s.itemId)
                           + PersonaBonus(s.itemId) * step
                           + AutoTest.BuildSynergy.ScoreAdjust(s.itemId, run);
                    }
                    // 特売補正。 **60/40 は割引段 {15,30,50} に対する意図的な線引き**。
                    //   50/30 は全条件で悪化した (2026-09-17・安物買いになる)。 下げないこと。
                    pw += (s.discountPct >= SaleBonusHiPct ? 2
                         : (s.discountPct >= SaleBonusLoPct ? 1 : 0)) * step;
                    return true;
                }

                /// 今の棚に残っている最良品の準パワー (0 下限)。 リロールすると未売却枠は
                /// 張り替わるので、 **回すことはこれを捨てること**でもある。
                /// 停止則の判定本体。 <b>停止則が off なら常に true</b> (旧規則の経路は触らない)。
                /// フェーズ2 の置き換えだけでなく、 最終店の消耗品・防御ストック・Super 全賭け
                /// も同じ判定を通す ── 1 経路だけ賢くしても、 浮いた金を別の経路が
                /// 1 回 47G で燃やす (2026-09-18 実測)。
                bool RuleSaysReroll(int price)
                {
                    if (!RerollStopRule) return true;
                    float expected = (RerollShelfValue * RerollPriorWeight + _rerollYieldSum)
                                   / (RerollPriorWeight + _rerollYieldN);
                    float gain = expected - BestRemainingPower();
                    float cost = price * GoldBandRate;
                    NoteRerollDecision(gain, cost);
                    return gain > cost;
                }

                /// [計装 2026-09-18] <b>リロールが捨てる棚</b>を数える。 各 ShopReroll の直前に呼ぶ。
                ///
                /// <para>フェーズ1/2 は準パワー ≥ PowerCuts[3] の品しか買わないが、 その後の
                /// フェーズ3 (通常購入) は所持金がゲートを超えていれば<b>スコアに関係なく</b>
                /// パッシブを全部買う。 リロールは未売却枠を張り替えるので、
                /// <b>フェーズ3 が買うはずだった品を先に捨てている</b>疑いがある。 金が余っていても
                /// 3 回目のリロールが損になる (上限 2 < 3 < 無制限) 理由の候補。</para>
                ///
                /// <para>TryReroll が断る条件 (上限・金不足) は先に見て、 捨てなかった回を数えない。</para>
                /// [A/B 2026-09-18] <b>リロールの前に、 フェーズ3 が買う品を買い切る</b>。
                /// 計装で「リロール 1 回が フェーズ3 の購入対象を約 5 品 (準パワー平均 0.065 band ≒ 売値相当)
                /// 捨てている」と出た。 フェーズ1/2 の下限 (進行度で 0.10〜0.20 band) と
                /// フェーズ3 (所持金がゲートを超えればパッシブを全部買う) の基準が 2 段になっているため。
                /// 先に買い切れば、 リロールが張り替えるのは本当に要らない枠だけになる。
                /// 基準はフェーズ3 と同じ ── 最終店はゲート無し、 それ以外はゲート付き。
                void BuyPhase3Items()
                {
                    float gate = RunPowerBudget.GateScale(PowerGap());
                    bool gk = run.OwnsPassive(GameLoop.ItemIds.GoldenKingSword);
                    bool lastShopForP3 = run.Depth >= run.normalClearFloor;   // フェーズ3 の lastShop と同じ式
                    int passiveGate = lastShopForP3 ? 0 : Mathf.RoundToInt((gk ? 15 : 25) * gate);
                    int weaponGate  = lastShopForP3 ? 0 : Mathf.RoundToInt((gk ? 25 : 40) * gate);
                    for (int i = 0; i < inv.slots.Count; i++)
                    {
                        var s = inv.slots[i];
                        if (s == null || s.sold) continue;
                        if (s.kind == ShopSlotKind.Weapon && run.coins > weaponGate
                            && GameLoop.Loadout.WouldUpgrade(run, s.itemId)) Buy(i);
                        else if (s.kind == ShopSlotKind.FacePart && run.coins > s.price) Buy(i);
                        else if (s.kind == ShopSlotKind.Passive && run.coins > passiveGate) Buy(i);
                    }
                }

                void NoteShelfDiscard()
                {
                    int price = inv.CurrentRerollPrice;
                    int cap = ItemSystem.Shop.ShopManager.RerollHardCap;
                    if (cap >= 0 && inv.rerollCount >= cap) return;
                    if (run.coins < price) return;
                    int after = run.coins - price;
                    float gate = RunPowerBudget.GateScale(PowerGap());
                    int passiveGate = Mathf.RoundToInt((run.OwnsPassive(GameLoop.ItemIds.GoldenKingSword) ? 15 : 25) * gate);
                    int weaponGate  = Mathf.RoundToInt((run.OwnsPassive(GameLoop.ItemIds.GoldenKingSword) ? 25 : 40) * gate);
                    DiscardRolls++;
                    int nth = Mathf.Min(3, inv.rerollCount);   // 0=1回目 … 3=4回目以降
                    DiscardRollsByNth[nth]++;
                    for (int i = 0; i < inv.slots.Count; i++)
                    {
                        var s = inv.slots[i];
                        if (s == null || s.sold) continue;
                        bool p3 =
                            (s.kind == ShopSlotKind.Passive  && after > passiveGate) ||
                            (s.kind == ShopSlotKind.FacePart && after > s.price) ||
                            (s.kind == ShopSlotKind.Weapon   && after > weaponGate
                                && GameLoop.Loadout.WouldUpgrade(run, s.itemId));
                        if (!p3) continue;
                        DiscardItemsByNth[nth]++;
                        if (s.kind == ShopSlotKind.Passive) DiscardPassive++;
                        else if (s.kind == ShopSlotKind.FacePart) DiscardPart++;
                        else DiscardWeapon++;
                        if (TrySlotPower(i, out float pw))
                        {
                            DiscardPower += pw;
                            if (pw < AutoTest.LearnedPriorityProvider.PowerCuts[3]) DiscardJunk++;
                        }
                    }
                }

                float BestRemainingPower()
                {
                    float best = 0f;   // 純益で比べる (棚替え実績と同じ物差し)
                    for (int i = 0; i < inv.slots.Count; i++)
                        if (TrySlotPower(i, out float p))
                        {
                            float net = p - inv.slots[i].price * GoldBandRate;
                            if (net > best) best = net;
                        }
                    return best;
                }

                bool BuyPriorityPass(float minPower)
                {
                    // 動的閾値を反映。 2026-06-23c: 毎回再計算 ── 同ショップ訪問内で帯を跨いだら即時反映
                    float effMinPower = Mathf.Max(minPower, CurrentDynamicMinPower());
                    // [アブレーション] **足切りは残し、順位付けだけ潰す。**
                    //   「買ってよいものを知っている」と「その中でどれが最良かを知っている」を分ける。
                    var ablCand = ablateShopScore ? new List<int>() : null;
                    int bestIdx = -1; float bestPower = float.MinValue; float bestEff = float.MinValue;
                    for (int i = 0; i < inv.slots.Count; i++)
                    {
                        var s = inv.slots[i];
                        // 準パワー・支払可否・武器の装備判定・特売補正は TrySlotPower に一本化。
                        //   経緯 (燈火 r10 の物資払い / 武器ゲートの取りこぼし / 60/40 の線引き) は
                        //   git log を参照。
                        if (!TrySlotPower(i, out float pw)) continue;
                        if (pw < effMinPower) continue;
                        ablCand?.Add(i);   // 足切りを通った候補 (アブレーション時のみ収集)
                        // ΔPower/G コスパ (slot.price は割引後の価格なので特売/upgrade が自然に有利化)
                        int delta = AutoTest.InventoryPower.SimulateAddItemDelta(run, s.itemId);
                        float eff = AutoTest.InventoryPower.CostEfficiency(delta, s.price);
                        // 足切りを通った候補は探索の母集団になる (下のホールドアウトで使う)
                        if (exploreHoldoutRate > 0f) _exploreCandidates.Add(i);
                        // 準パワー主軸 → ΔPower/G tiebreak
                        bool better = pw > bestPower || (pw == bestPower && eff > bestEff);
                        if (better) { bestPower = pw; bestEff = eff; bestIdx = i; }
                    }
                    // [アブレーション] 足切りを通った候補から一様抽選へ差し替える。
                    if (ablCand != null && ablCand.Count > 0)
                        bestIdx = ablCand[AblationRng.Next(0, ablCand.Count)];
                    if (bestIdx < 0) { _exploreCandidates.Clear(); return false; }

                    // **ランダム化ホールドアウト。** 一定割合の決定で、 足切りを通った候補から
                    //   **一様に**選ぶ。 その候補集合の中では取得/未取得が期待値で等質になるので、
                    //   ここで採ったデータだけを使えば lift が不偏になる。
                    //   乱数は GameRng ではなく専用ストリーム ── 消費列が変わると
                    //   シード固定のペア比較が壊れる (SuperCombatAI のロールアウトと同じ方針)。
                    if (_isExploreRun && exploreHoldoutRate > 0f && _exploreCandidates.Count > 1
                        && _exploreRng.NextDouble() < exploreHoldoutRate)
                    {
                        // **層化抽選 (2026-09-04)。** 旧: 候補から一様抽選。
                        //   一様だと選ばれる品の rarity 構成が**提示重みをそのまま保存**するので
                        //   (BRONZE 45 / SILVER 32 / GOLD 18 / LEGENDARY 5)、 稀少帯のサンプルが
                        //   永久に薄いままだった ── 実測 10,000 ラン で LEGENDARY は 1 品 2.8 件、
                        //   両群 30 以上に届いたのは 104 品中 21 品 (全部 BRONZE/SILVER) だけ。
                        //   **ラン数では解けない**: 帯間の比は試行回数に依存しないため。
                        //
                        //   提示重みの**逆数**で重み付けすると各帯の期待選択率が均等化し、
                        //   LEGENDARY のサンプルが約 4 倍になる (理論 5 倍・在庫天井で 4 倍
                        //   ── パッシブ 5 枠中に LEGENDARY が 1 つ以上並ぶ確率が 22.6% なので)。
                        //
                        //   **ゲーム側は一切変えない。** 提示も価格も購入の主経路も製品条件のまま、
                        //   変わるのは「探索が発火したとき候補のどれを選ぶか」だけ。 選択確率が
                        //   アイテムの効果と独立なので、 群内比較としての不偏性は保たれる。
                        int pick = PickStratified(inv);
                        if (_cur != null)
                        {
                            _cur.exploreDecisions++;
                            foreach (int ci in _exploreCandidates)
                            {
                                var cs = inv.slots[ci];
                                if (cs != null && !string.IsNullOrEmpty(cs.itemId))
                                    _cur.exploreOfferedEver.Add(cs.itemId);
                            }
                            var ps = inv.slots[pick];
                            if (ps != null && !string.IsNullOrEmpty(ps.itemId))
                                _cur.exploreAcquiredEver.Add(ps.itemId);
                        }
                        _exploreCandidates.Clear();
                        return Buy(pick);
                    }
                    _exploreCandidates.Clear();
                    int paidPrice = inv.slots[bestIdx].price;
                    bool boughtBest = Buy(bestIdx);
                    // 停止則の学習用: 買えた品の**純益** (準パワー − 代金の band 価値) を積む。
                    //   代金を引かないと「高い品を買えた棚替え」ほど得に見えて回し過ぎる。
                    if (boughtBest) passPowerBought += bestPower - paidPrice * GoldBandRate;
                    return boughtBest;
                }
                // 余金が許す限り 余裕時帯以上を全て取得
                int safety = 0;
                while (BuyPriorityPass(AutoTest.LearnedPriorityProvider.PowerCuts[2]) && safety++ < 30) { }

                // ============================================================
                // フェーズ2: リロール判定
                //   トリガ = (S級未所持 かつ 在庫にS級なし) OR
                //            (5層以降の最終ショップで A級ゼロ かつ 在庫にA級なし)
                //   コスト ≤ run.coins × 0.30 かつ「購入予算 6G 以上は残す」
                // ============================================================
                bool HasSInInventory()
                {
                    for (int i = 0; i < inv.slots.Count; i++)
                    {
                        var s = inv.slots[i];
                        if (s == null || s.sold) continue;
                        if (AutoTest.LearnedPriorityProvider.IsSRank(s.itemId)) return true;
                    }
                    return false;
                }
                bool HasAInInventory()
                {
                    for (int i = 0; i < inv.slots.Count; i++)
                    {
                        var s = inv.slots[i];
                        if (s == null || s.sold) continue;
                        if (AutoTest.LearnedPriorityProvider.IsARank(s.itemId)) return true;
                    }
                    return false;
                }
                bool OwnsAnyS()
                {
                    if (run.ownedPassiveItems != null)
                        foreach (var id in run.ownedPassiveItems)
                            if (AutoTest.LearnedPriorityProvider.IsSRank(id)) return true;
                    if (run.ownedConsumables != null)
                        foreach (var id in run.ownedConsumables)
                            if (AutoTest.LearnedPriorityProvider.IsSRank(id)) return true;
                    return false;
                }
                bool OwnsAnyA()
                {
                    if (run.ownedPassiveItems != null)
                        foreach (var id in run.ownedPassiveItems)
                            if (AutoTest.LearnedPriorityProvider.IsARank(id)) return true;
                    return false;
                }

                bool lastShop = run.Depth >= run.normalClearFloor;
                var pol = AutoTest.PolicyParameters.Current;

                // ============================================================
                // リロール停止則 (2026-09-17) ── フェーズ2 と 2b を 1 本に畳む
                //
                //   旧: 「S 級を 1 個も持っていない」(品質) と「所持金 ≥ 価格 + 30G」(余剰) の
                //   2 本立て。 実測で S は最初の店でほぼ埋まり品質側は 1.4 回/ラン で停止、
                //   **223G の 82% を評価の入っていない水位ルールが使っていた**。
                //   しかも安い 1〜2 回目を判断のある側が、 高い 3 回目以降を無判断の側が使う
                //   ── 判断の質が価格の逆順に並んでいた。
                //
                //   新: 1 回ごとに **回す価値 > 払う額の価値** かを比べるだけ。
                //     回す価値 = E[1 回の棚替えで買える準パワー] − 今の棚に残る最良品
                //     払う額   = price × GoldBandRate  (ITT 実測の 1G の band 価値)
                //   価格が 5→10→20→30 と上がるので、 同じ期待値でもいずれ必ず止まる。
                //
                //   **E[1 回の棚替え] はラン内の実績から更新する。** 事前値 RerollShelfValue を
                //   擬似観測 RerollPriorWeight 件ぶんとして持ち、 回すたびに実際に買えた
                //   準パワーの和で置き換えていく。 所持が埋まって新顔が減る終盤は自然に下がる。
                //   ラン内の実績だけを使うので runIdx の純関数のまま (決定性を壊さない)。
                // ============================================================
                if (RerollStopRule)
                {
                    int exitWhy = 3;
                    int ruleGuard = 0;
                    while (ruleGuard++ < 16)
                    {
                        // 買えるものは先に買う (旧 2b と同じ下限)。
                        int sb3 = 0;
                        while (BuyPriorityPass(AutoTest.LearnedPriorityProvider.PowerCuts[3]) && sb3++ < 30) { }

                        int price = inv.CurrentRerollPrice;
                        if (price <= 0) { exitWhy = 0; break; }
                        // 回した後に 1 品も買えないなら回す意味が無い。
                        if (run.coins < price + RerollBuyFloor) { exitWhy = 1; break; }

                        if (!RuleSaysReroll(price)) { exitWhy = 4; break; }

                        if (Phase3BeforeReroll) BuyPhase3Items();   // A/B: 捨てる前に買い切る
                        int beforeR = run.coins;
                        NoteShelfDiscard();   // [計装] 捨てる棚を数える
                        bool rolled = gm.ShopReroll();
                        if (_shopReserve > 0) _shopReserve = inv.CurrentRerollPrice;
                        if (!rolled) { exitWhy = 2; break; }
                        _cur.shopRerolls++;
                        _cur.shopRerollCoins += (beforeR - run.coins);
                        NoteReroll(0, beforeR - run.coins);

                        // この棚替えで実際に買えた準パワーを実績に積む。
                        passPowerBought = 0f;
                        int sb4 = 0;
                        while (BuyPriorityPass(AutoTest.LearnedPriorityProvider.PowerCuts[3]) && sb4++ < 30) { }
                        _rerollYieldSum += passPowerBought;
                        _rerollYieldN++;
                    }
                    NoteSurplusExit(exitWhy, run.coins);
                }
                else
                {
                int rerollGuard = 0;
                // 2026-06-23 Power 帯認識リロール:
                //   Weak/Early (band 0-1): リロールしない (G 温存、 まず手元の品で固める)
                //   Mid (band 2): 通常 (S 未所持なら reroll)
                //   Late/Apex (band 3-4): 積極 (リロール上限 +2、 残り G ライン緩和)
                // 2026-06-23c: 各ループで band を再評価 ── 購入で帯が上がっても新しい閾値で続行
                while (rerollGuard++ < 12)
                {
                    int band = CurrentPowerBand();
                    int rerollMaxLoops = band >= 3 ? 10 : (band <= 1 ? 3 : 8);
                    int residualG = band >= 3 ? 4 : (band <= 1 ? 10 : 6);
                    if (rerollGuard > rerollMaxLoops) break;

                    int price = inv.CurrentRerollPrice;
                    if (price > run.coins * pol.rerollCostRatio) break;
                    // 通常ショップでは購入余力を最低 residualG 残す
                    if (!lastShop && run.coins - price < residualG) break;

                    bool needS = !OwnsAnyS() && !HasSInInventory();
                    bool needA = lastShop && !OwnsAnyA() && !HasAInInventory();
                    // Weak/Early はリロールしない (まず手持ち固め優先)
                    if (band <= 1) { needS = false; needA = false; }
                    if (!needS && !needA) break;

                    if (Phase3BeforeReroll) BuyPhase3Items();   // A/B: 捨てる前に買い切る
                    int beforeR = run.coins;
                    // **戻り値で判定する。** コインの増減で見ると〈合札〉の無料リロールを
                    //   失敗と誤認する (2026-09-17)。
                    NoteShelfDiscard();   // [計装] 捨てる棚を数える
                    bool rolled = gm.ShopReroll();
                    // 取り置きを現在価格へ更新。 リロール価格は 5→10→20→30 と逓増するので、
                    //   店に入った時点の額のままだと強盗直前に足りなくなる。
                    if (_shopReserve > 0) _shopReserve = inv.CurrentRerollPrice;
                    if (!rolled) break;
                    _cur.shopRerolls++;
                    _cur.shopRerollCoins += (beforeR - run.coins);
                    NoteReroll(0, beforeR - run.coins);

                    // 直後に新在庫の S/A をかき集める
                    int s2 = 0;
                    while (BuyPriorityPass(AutoTest.LearnedPriorityProvider.PowerCuts[2]) && s2++ < 30) { }
                }

                // ============================================================
                // フェーズ2b: **余剰資金の再投資** (2026-08-11)
                //   従来のリロールは「S 級を持っていない時」だけが条件だった。 S を 1 つ確保した
                //   時点で二度と回さなくなり、 **平均 57.9G を使い残したままランを終えていた**。
                //   金は持ち越せるが、 持ち越しても勝率にはならない ── 在庫が尽きたなら
                //   **リロールで商品を補給する**のが、 余剰の唯一の出口である。
                //
                //   条件は「買うものが無い」かつ「回してもなお買える額が残る」。
                //   買えるものが並んでいる限り回さないので、 購入より優先されることはない。
                // ============================================================
                {
                    int surplusGuard = 0;
                    int exitWhy = 3;   // 既定 = ガード上限に当たった
                    while (surplusGuard++ < 8)
                    {
                        int price = inv.CurrentRerollPrice;
                        if (price <= 0) { exitWhy = 0; break; }
                        // **回した後に 1 個買える額を残す。** 回すだけ回して買えないのは最悪の使い方。
                        if (run.coins < price + SurplusBuyReserve) { exitWhy = 1; break; }
                        // まだ買える品が並んでいるなら、 リロールせずそちらを買う
                        if (BuyPriorityPass(AutoTest.LearnedPriorityProvider.PowerCuts[3])) continue;

                        if (Phase3BeforeReroll) BuyPhase3Items();   // A/B: 捨てる前に買い切る
                        int beforeR = run.coins;
                        NoteShelfDiscard();   // [計装] 捨てる棚を数える
                        bool rolled = gm.ShopReroll();
                        // 取り置きを現在価格へ更新。 リロール価格は 5→10→20→30 と逓増するので、
                        //   店に入った時点の額のままだと強盗直前に足りなくなる。
                        if (_shopReserve > 0) _shopReserve = inv.CurrentRerollPrice;
                        if (!rolled) { exitWhy = 2; break; }
                        _cur.shopRerolls++;
                        _cur.shopRerollCoins += (beforeR - run.coins);
                        NoteReroll(1, beforeR - run.coins);
                        int s3 = 0;
                        while (BuyPriorityPass(AutoTest.LearnedPriorityProvider.PowerCuts[3]) && s3++ < 30) { }
                    }
                    // [計装 2026-09-17] **余剰ループの出口**。 残金 89.5G の内訳を切り分ける。
                    //   構造的な下限は「リロール価格(最大30) + SurplusBuyReserve(30) = 60G」だが
                    //   実測はそれより 30G 高い ＝ 水位以外の理由で止まっている分がある。
                    NoteSurplusExit(exitWhy, run.coins);
                }
                }   // !RerollStopRule

                // (旧フェーズ2.5 の強盗判定はフェーズ0 へ移動 ── 買う前に撃つ)

                // ============================================================
                // フェーズ3: 通常購入（旧ロジック）
                //   黄金卿の剣 所持時 (2026-05-31 v3 消費Gold基準に変更後):
                //   旧 = 余剰金保持 / 新 = **積極消費**で与ダメ倍率を伸ばす
                //   → 通常購入閾値を緩める (=より積極的に買い回し)
                // ============================================================
                bool hasGoldKing = run != null && run.OwnsPassive(GameLoop.ItemIds.GoldenKingSword);
                if (lastShop)
                {
                    // ボス直前。 **まず防御系を上限まで積む** ── 装備より先に生存資源を確保する。
                    StockDefensive(pol.consumableStockMax);
                    int guard = 0;
                    bool bought = true;
                    while (bought && guard++ < 40)
                    {
                        bought = false;
                        // 回復 0 本なら何よりも先に確保する (2026-08-16)。
                        if (EnsureHealStock()) { bought = true; continue; }
                        if (BuyKind(ShopSlotKind.Weapon)) { bought = true; continue; }
                        // **2026-08-17 実験 B: 出目パーツをパッシブより前に置く。**
                        //   A (パッシブ優先) では 1 ラン 0.90 個しか買われず、 提示 20 回前後に対して
                        //   ほぼ全部をパッシブが吸っていた。 パーツは 8〜17G の恒久強化で
                        //   ダイス 5 個すべてに毎ターン効くので、 費用対効果で上回る可能性がある。
                        //   **ただし旧ダイス用の高いゲート (75G) を外しただけでは足りない**ことが
                        //   実測で分かったので、 順序そのものを入れ替えて比較する。
                        if (BuyKind(ShopSlotKind.FacePart)) { bought = true; continue; }
                        // **パッシブを先に買う** (2026-08-10)。 同じ金額ならパッシブの方が強い
                        //   ことが実測で出ている (ダイスが陳列から消えるだけで +9.4pt)。
                        if (BuyKind(ShopSlotKind.Passive)) { bought = true; continue; }
                        // 業物廃止 (2026-08-10) 後は T4+ で素材の行き先が消える。
                        //   最後のショップで金を使い切るループなので、 ここを塞がないと
                        //   使い道の無い素材に金を捨て、 測定側のビルドが不当に弱くなる。
                        if (GameManager.CanSpendMaterials(run)
                            && BuyKind(ShopSlotKind.WeaponMaterial)) { bought = true; continue; }
                        if (BuyKind(ShopSlotKind.Consumable)) { bought = true; continue; }
                    }
                }
                else
                {
                    // **回復を1つも持っていないなら、 何よりも先に1つ確保する (2026-08-05)。**
                    //   高難易度の敗因の 56% は削り負けで、 その多くは「低HPで戦闘に入る」形。
                    //   回復薬は購入順の最後・余り金 (coins>4) 依存だったため、 搾取経済で価格が
                    //   上がる高難易度ほど買えなくなり、 手ぶらで進む悪循環になっていた。
                    //   1 個目だけは武器・パッシブ・素材より優先する。
                    //   2026-08-16: 判定を「消耗品 0 個」から **「回復薬 0 本」** へ (EnsureHealStock)。
                    //   2026-08-17: **回復・シールドを上限まで積む** (リロールしてでも) へ強化。
                    StockDefensive(pol.consumableStockMax);

                    // 物資が枯れかけているなら物資回復も最優先で確保する (2026-08-05)。
                    //   物資は横移動・戦闘で一方的に減り、 発狂すると秒読みでランが終わる。
                    //   回復源が cons_hope_* だけになったので、 切らすと進路の自由度を失う。
                    // 取り置き中 (門で物資を払うアーム) はここが底上げされ、
                    //   6/7 層の店で物資回復を先に確保する。
                    if (run.provision <= ProvisionRefillFloorEffective())
                        for (int i = 0; i < inv.slots.Count; i++)
                        {
                            var s = inv.slots[i];
                            if (s != null && !s.sold && s.kind == ShopSlotKind.Consumable
                                && GameLoop.ItemIds.ConsFamilyOf(s.itemId) == GameLoop.ItemIds.ConsProvisionFamily
                                && run.coins >= s.price) { Buy(i); break; }
                        }

                    // 2026-05-31 v3: 消費Gold基準なので、 黄金卿所持時は **閾値を緩めて積極消費** (旧と逆向き)
                    // 2026-08-10 経済リスケール: **絶対値の残金閾値なので価格と同じ ×5**。
                    //   据え置くとゲートが実質無効化され、 BOT が残金を使い切ってしまう。
                    // 次のボスに対する戦力ギャップでゲートを動かす (天井専用・§RunPowerBudget)。
                    //   足りないほど金を使う。 充足なら倍率 1.0 ＝ 従来どおり。
                    float gate = RunPowerBudget.GateScale(PowerGap());
                    int weaponGate   = Mathf.RoundToInt((hasGoldKing ? 25 : 40) * gate);
                    int passiveGate  = Mathf.RoundToInt((hasGoldKing ? 15 : 25) * gate);
                    int materialGate = Mathf.RoundToInt((hasGoldKing ? 10 : 20) * gate);
                    // **装備に至らない武器/ダイスは買わない (2026-08-10)。**
                    //   旧: 所持金がゲートを超えていれば枠を無条件に全部買っていた。
                    //   Loadout.TryAutoEquip は「今より良い時だけ」装備するので、
                    //   劣るダイスを買ってもゴールドが消えるだけだった
                    //   (実測 1 ラン 8.14 個購入。 大半が死に金)。
                    for (int i = 0; i < inv.slots.Count; i++)
                        if (inv.slots[i] != null && !inv.slots[i].sold && run.coins > weaponGate
                            && inv.slots[i].kind == ShopSlotKind.Weapon
                            && GameLoop.Loadout.WouldUpgrade(run, inv.slots[i].itemId)) Buy(i);
                    // 出目パーツは「増設」なので武器のような更新判定を通さない (面が 1 枚増えるだけ)。
                    //   恒久強化なので、 素材や消耗品より先に取る。
                    for (int i = 0; i < inv.slots.Count; i++)
                        if (inv.slots[i] != null && !inv.slots[i].sold
                            && inv.slots[i].kind == ShopSlotKind.FacePart
                            && run.coins > inv.slots[i].price) Buy(i);
                    for (int i = 0; i < inv.slots.Count; i++)
                        if (inv.slots[i] != null && !inv.slots[i].sold && run.coins > passiveGate
                            && inv.slots[i].kind == ShopSlotKind.Passive) Buy(i);
                    // T4 到達率改善: 素材買い溜め上限 2→4 (連続強化で T3+ → T4 まで一気に届く余地)
                    int needMat = GameManager.WeaponUpgradeCost(run);
                    int matBuys = 0;
                    int matBuyCap = run.Depth >= 4 ? 4 : 2;
                    // **強化先が無いなら買わない。** 業物廃止 (2026-08-10) で T4+ 到達後は
                    //   武器が素材を受け付けない。 needMat が int.MaxValue のとき needMat*2 は
                    //   桁溢れで負になり「偶然買わない」形になっていたが、 偶然に頼らず明示する。
                    if (GameManager.CanSpendMaterials(run))
                        while (run.weaponMaterials < needMat * 2 && run.coins > materialGate && matBuys++ < matBuyCap
                               && BuyKind(ShopSlotKind.WeaponMaterial)) { }
                    int stock = run.ownedConsumables != null ? run.ownedConsumables.Count : 0;
                    int stockCap = pol.consumableStockMax;
                    for (int i = 0; i < inv.slots.Count && stock < stockCap; i++)
                        if (inv.slots[i] != null && !inv.slots[i].sold && run.coins > 20   // ×5 リスケール
                            && inv.slots[i].kind == ShopSlotKind.Consumable && Buy(i)) stock++;
                }

                // ============================================================
                // **7層のショップでは所持金を残さない (2026-08-16)。**
                //   実測 (batch_20260816_145127): p4 で死亡した 199 ランの残金が平均 29.3G、
                //   回復薬の所持 0 個が 90.5%。 **買えるものがあるのに金を握ったまま
                //   最終ボスへ入っている。** 人間なら絶対に取らない判断で、 計測器としての欠陥。
                //
                //   既存の all-in ルーチン (下の IsSuperTailMode) は挑戦スコア 50 以上が条件
                //   (superHighDifficultyMinScore = 50) なので、 **0pt の基準測定では
                //   一度も起動していなかった**。 難易度に依らず 7層では常に使い切る。
                //
                //   **7層では買える枠がほぼ「パッシブ×3」と「消費×3」しかない。**
                //   ダイスと武器は Loadout.WouldUpgrade がまず通らず、 素材は強化 4 到達で
                //   CanSpendMaterials が false、 拡張は上限到達なら陳列自体が生成されない
                //   (ShopManager.Generate)。 なので「金が余る」＝「消費枠しか残っていないのに、
                //   その Tier が低い」状態を指す。
                //
                //   よって **低 Tier の消費を掴んで終わるのではなく、 回して上位 Tier を探す**。
                //   T3/T4 なら即買い、 T1/T2 は回せる限り見送る。 回せなくなった最後の周だけ
                //   Tier を問わず買う ── 残り物でも空手で入るよりは良い。
                // ============================================================
                if (run.Depth >= 7)
                {
                    // 現在の陳列の最安値を、 再入荷後の最安値の代理として使う。
                    int cheapest = int.MaxValue;
                    for (int i = 0; i < inv.slots.Count; i++)
                        if (inv.slots[i] != null && !inv.slots[i].sold && inv.slots[i].price > 0)
                            cheapest = Math.Min(cheapest, inv.slots[i].price);
                    if (cheapest == int.MaxValue) cheapest = 20;   // 空店: ×5 リスケール後の下限相当

                    // Tier 下限つきの消費購入。 minTier=0 で「Tier を問わない」。
                    bool BuyConsumableAtLeast(int minTier)
                    {
                        for (int i = 0; i < inv.slots.Count; i++)
                        {
                            var s = inv.slots[i];
                            if (s == null || s.sold || s.kind != ShopSlotKind.Consumable) continue;
                            if (GameLoop.ItemIds.ConsTierOf(s.itemId) < minTier) continue;
                            if (Buy(i)) return true;
                        }
                        return false;
                    }

                    // 7層のボス直前。 使い切る前に防御系を満たしておく。
                    StockDefensive(pol.consumableStockMax);

                    int spendGuard = 0;
                    while (spendGuard++ < 64)
                    {
                        int price = inv.CurrentRerollPrice;
                        // 回した後に最安品を 1 つ買えるか。 買えないなら、 これが最後の周。
                        bool canReroll = price > 0 && run.coins >= price + cheapest;
                        int minTier = canReroll ? HighConsumableTier : 0;

                        bool bought;
                        int buyGuard = 0;
                        do
                        {
                            bought = false;
                            // 回復 0 本なら何よりも先に確保する。 Tier 待ちより優先 ──
                            //   T1 でも「持っている」ことに意味がある (次の 1 撃で死ぬ判定で使える)。
                            if (EnsureHealStock()) { bought = true; continue; }
                            if (BuyKind(ShopSlotKind.Weapon)) { bought = true; continue; }
                            if (BuyKind(ShopSlotKind.FacePart)) { bought = true; continue; }
                            if (BuyKind(ShopSlotKind.Passive)) { bought = true; continue; }
                            if (BuyConsumableAtLeast(minTier)) { bought = true; continue; }
                            if (GameManager.CanSpendMaterials(run)
                                && BuyKind(ShopSlotKind.WeaponMaterial)) { bought = true; continue; }
                        }
                        while (bought && buyGuard++ < 64);

                        if (!canReroll) break;
                        if (!RuleSaysReroll(price)) break;   // 停止則 on のときだけ効く
                        if (Phase3BeforeReroll) BuyPhase3Items();   // A/B: 捨てる前に買い切る
                        int before = run.coins;
                        NoteShelfDiscard();   // [計装] 捨てる棚を数える
                        gm.ShopReroll();
                        // 取り置きを現在価格へ更新。 リロール価格は 5→10→20→30 と逓増するので、
                        //   店に入った時点の額のままだと強盗直前に足りなくなる。
                        if (_shopReserve > 0) _shopReserve = inv.CurrentRerollPrice;
                        // [欠陥 2026-09-17] **コインの増減で失敗を判定している。**
                        //   〈試し振りの符牒〉の無料リロールは coins が減らないので、
                        //   この店で 1 度も回していないと **初回で必ず break する**。
                        //   フェーズ2/2b は戻り値判定へ直したが (8206/8242)、 ここは残っている。
                        //   **今は直さない** ── 判断の組み直しと同じバッチで動かすと切り分けが壊れる。
                        if (run.coins >= before) break;   // 減らないなら回しても無駄
                        _cur.shopRerolls++;
                        _cur.shopRerollCoins += before - run.coins;
                        NoteReroll(2, before - run.coins);
                    }
                }

                // 最大難度Super・6層以降: 金を残して死ぬ平均点戦略を捨てる。
                // 固定ショップはボス直前なので、最大6回の再入荷へ賭け、各回で装備更新・
                // パッシブ・戦闘消耗品を買えるだけ買う。外れれば金だけ失う高分散ルーチン。
                if (IsSuperTailMode && run.Depth >= 6)
                {
                    int allInGuard = 0;
                    while (true)
                    {
                        bool bought;
                        int buyGuard = 0;
                        do
                        {
                            bought = false;
                            if (BuyKind(ShopSlotKind.Weapon)) { bought = true; continue; }
                            if (BuyKind(ShopSlotKind.Passive)) { bought = true; continue; }
                            if (BuyKind(ShopSlotKind.Consumable)) { bought = true; continue; }
                            if (BuyKind(ShopSlotKind.FacePart)) { bought = true; continue; }
                        }
                        while (bought && buyGuard++ < 32);

                        if (allInGuard++ >= 6) break;
                        int price = inv.CurrentRerollPrice;
                        // 再入荷後に最安品を1つ買える最低限だけ残す。価格10未満の品が多い。
                        if (price <= 0 || run.coins < price + 4) break;
                        if (!RuleSaysReroll(price)) break;   // 停止則 on のときだけ効く
                        if (Phase3BeforeReroll) BuyPhase3Items();   // A/B: 捨てる前に買い切る
                        int before = run.coins;
                        NoteShelfDiscard();   // [計装] 捨てる棚を数える
                        gm.ShopReroll();   // [経路3] Super 全賭け
                        // 取り置きを現在価格へ更新。 リロール価格は 5→10→20→30 と逓増するので、
                        //   店に入った時点の額のままだと強盗直前に足りなくなる。
                        if (_shopReserve > 0) _shopReserve = inv.CurrentRerollPrice;
                        if (run.coins >= before) break;
                        _cur.shopRerolls++;
                        _cur.shopRerollCoins += before - run.coins;
                        NoteReroll(3, before - run.coins);
                    }
                }

                // 買い切った後に 「リロール → 強盗」。 順序の理由は上の取り置きのコメント。
                if (plannedRobbery)
                {
                    _shopReserve = 0;
                    int rerollPrice = inv.CurrentRerollPrice;
                    if (rerollPrice > 0 && run.coins >= rerollPrice)
                    {
                        if (Phase3BeforeReroll) BuyPhase3Items();   // A/B: 捨てる前に買い切る
                        int beforeRoll = run.coins;
                        NoteShelfDiscard();   // [計装] 捨てる棚を数える
                        gm.ShopReroll();
                        // 取り置きを現在価格へ更新。 リロール価格は 5→10→20→30 と逓増するので、
                        //   店に入った時点の額のままだと強盗直前に足りなくなる。
                        if (_shopReserve > 0) _shopReserve = inv.CurrentRerollPrice;
                        if (run.coins < beforeRoll)
                        {
                            _cur.shopRerolls++;
                            _cur.shopRerollCoins += beforeRoll - run.coins;
                            NoteReroll(4, beforeRoll - run.coins);
                        }
                    }
                    Debug.Log($"[AutoRunner] 値下げ交渉 (6層以降・買い物後): "
                            + $"F{run.Depth} HP{run.playerHP}/{run.playerMaxHP} "
                            + $"棚{inv.slots.Count}枠 残金{run.coins}G");
                    gm.ShopRobbery();
                    return;
                }
            }
            // [計装 2026-09-17] **ラン単位**で「最後に店を出たときの残金」を上書き記録する。
            //   店ごとの平均と終了時残金を引き算すると単位が違う (1 ラン 6 店) ので、
            //   「店を出たあとに増えた分」はラン単位でしか出せない。
            _lastShopExitCoins = gm.Run != null ? gm.Run.coins : 0;
            _lastShopExitFloor = gm.Run != null ? gm.Run.Depth : 0;
            _lastShopExitSeen = true;
            _shopReserve = 0;
            NoteUpgradeDeclines(gm.Run);
            gm.ExitShop();
        }

        /// <summary>[計装] 店を出る時点で売れ残っている 1G アップグレード枠を、
        /// **理由別に 1 スロット 1 回だけ**数える (§ FamilyTierStats ②)。
        ///
        /// <para><b>成約率だけでは「段の設計」を判定できない。</b> 購入の足切り
        /// (<c>if (pw &lt; effMinPower) continue;</c>) は<b>価格を一切見ず</b>、
        /// コスパ (<c>ΔPower/G</c>) は準パワー同点時のタイブレークにしか使われない。
        /// つまり見送りは「より良い品を選んだ」とは限らず、
        /// <b>学習スコアが閾値に届かなかっただけ</b>の可能性が高い。 そこを分ける。</para>
        ///
        /// <para>判定に使うのは<b>最も緩い帯</b> <c>PowerCuts[3]</c> ── 実際の購入ループが
        /// 最後にそこまで緩めて回るので、 これを下回った品は<b>どの周回でも一度も候補に入っていない</b>。</para></summary>
        private void NoteUpgradeDeclines(GameLoop.RunState run)
        {
            var inv = ItemSystem.Shop.ShopManager.Instance?.Current;
            if (inv?.slots == null || run == null) return;
            float loosest = AutoTest.LearnedPriorityProvider.PowerCuts[3];
            for (int i = 0; i < inv.slots.Count; i++)
            {
                var s = inv.slots[i];
                if (s == null || s.sold || s.upgradeStep <= 0) continue;
                float pw = AutoTest.LearnedPriorityProvider.BuyScore(s.itemId)
                         + (s.discountPct >= 60 ? 2 : (s.discountPct >= 40 ? 1 : 0));
                if (pw < loosest) AutoTest.FamilyTierStats.NoteUpgradeDeclined(FamilyTierStats.DeclineCut);
                else if (run.coins < s.price) AutoTest.FamilyTierStats.NoteUpgradeDeclined(FamilyTierStats.DeclineNoGold);
                else AutoTest.FamilyTierStats.NoteUpgradeDeclined(FamilyTierStats.DeclineOutbid);
            }
        }
    }
}
