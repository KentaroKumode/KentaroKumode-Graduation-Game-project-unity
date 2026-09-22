using System;
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace AutoTest.ParallelSweep
{
    /// <summary>親プロセスが 1 ワーカーへ渡す指示。
    ///
    /// <para>Ultra の <c>UltraPortfolioCandidateJob</c> とは別系統。 あちらは合成シードと
    /// 候補ポリシーを配るためのもので、 こちらは<b>通常バッチと同じ <c>runIdx</c> の
    /// 区間を切り出すだけ</b>。 シードの作り方を変えないことが、 逐次版と digest が
    /// 一致する (＝並列版が正しいと言える) 唯一の根拠になっている。</para></summary>
    [Serializable]
    public sealed class RankMarginWorkerJob
    {
        public string label = "";
        /// <summary>焦点トラック名 (MetaPanelKind)。 <b>空文字なら Balanced アーム。</b></summary>
        public string focusTrack = "";
        /// <summary>配分を直接指定する (<c>"Shell:2,Output:3,…"</c>)。 <b>focusTrack より優先。</b>
        /// 「Balanced から 1 軸だけ −1」のような、 焦点 1 本では書けない配分を測るために要る。</summary>
        public string rankSpec = "";
        public int focusRank;
        public int runs;
        public int seedStart;
        /// <summary>AutoRunner の公開フィールド名と値。 <see cref="RankMarginWorkerConfig"/> 参照。</summary>
        public string[] configKeys = Array.Empty<string>();
        public string[] configValues = Array.Empty<string>();
        public string responsePath = "";
        public string progressPath = "";
        public string outputRoot = "";
        /// <summary>親が使ったビルドの指紋。 ワーカー側では検証しない (親が起動前に見る)。
        /// 応答へ写して残すことで、 **どのビルドで測った数字か**が結果ファイルだけで分かる。</summary>
        public string buildFingerprint = "";
    }

    /// <summary>ワーカー 1 プロセス分の返答。</summary>
    [Serializable]
    public sealed class RankMarginWorkerResponse
    {
        public string label = "";
        public int seedStart;
        public int runs;
        public int valid;
        public int clears;
        public double stageSum;
        /// <summary>ラン毎の到達段。 <c>runIdx = seedStart + i</c>。 無効ランは −1。</summary>
        public int[] bandScores = Array.Empty<int>();
        /// <summary>ラン毎の <c>deterministicDigest</c>。 逐次版との突き合わせに使う。</summary>
        public string[] digests = Array.Empty<string>();
        public bool completedNormally;
        public string failureCode = "";
        public string buildFingerprint = "";
        /// <summary><b>ワーカーが実際に走った設定</b> (name=value を改行区切り)。
        /// 親が「送ったつもり」の値ではなく、 <b>適用後にフィールドへ入っていた値</b>を読む。
        /// 2026-09-13 に <c>useMutualAttackPipeline</c> の運び漏れで 170,000 ラン を
        /// 旧戦闘経路で測って捨てた ── 運搬は見えないと必ず漏れる。</summary>
        public string effectiveState = "";
        // 救済の発動数。 **ログでは数えられない** ── バッチ中は logEnabled=false なので、
        //   「ログに出ていない＝発動していない」は成り立たない。 計数で返す。
        public long torchRevivals;
        public long lastStandRevivals;
        public long fleuretRevivals;
        /// <summary>救済で戻した HP の合計 (灯火・ラストスタンドのみ)。
        /// <b>「値を変えたのに効いていない」を数字で切り分けるために要る。</b>
        /// 発動数が同じでも配った HP が変わっていなければ、 変更が届いていない。</summary>
        public long revivalHpRestored;
        /// <summary>燈火 r10: 希望で払った回数と希望の総消費。</summary>
        public long hopePayments;
        public long hopeSpent;
        /// <summary>金庫 r10: 層跨ぎの倍化回数 / 増えたゴールド / 上限に当たった回数。</summary>
        public long vaultDoublings;
        public long vaultGoldGained;
        public long vaultCapHits;
        /// <summary>被ダメの内訳 (CombatSystem.GuardDiag)。</summary>
        public long guardAtkBefore;
        public long guardAtkAfter;
        public long guardBlockSum;
        public long guardLossBase;
        public long guardAttacks;
        public long guardFullyBlocked;
        /// <summary>〈剛胆〉: エリート戦に入った回数 / 追加ドロップ数。</summary>
        public long valorEliteFights;
        public long valorEliteDrops;
        // 逐次版のレポート末尾と同じ数字を出すための計数。
        public long plunderDrops;
        public long robberyAttempts;
        public long robberyWins;
        public long robberyLosses;
        public long robberyLootTotal;
        /// <summary>強盗を撃った層の分布 (index 0 = 1層)。 **どこで誤爆しているかを数字で見る。**</summary>
        public long[] robberyByFloor = new long[7];
        /// <summary>ボス突入時の状態 (index = 層)。 休憩で戻らない資源の消耗を見る。</summary>
        public long[] bossEntryCount = new long[9];
        public long[] bossEntryHope = new long[9];
        public long[] bossEntryHopeTier = new long[9];
        public long[] bossEntryConsumables = new long[9];
        public long[] bossEntryHpPct = new long[9];
        public long[] bossEntryPassives = new long[9];
        public long[] bossEntryPower = new long[9];
        /// <summary>強盗の戦利品のうち実際に所持へ入った数 (重複スキップを除く)。</summary>
        public long robberyLootApplied;

        /// <summary>〈門〉の 3 工程 (2026-09-14)。 **「払った」と「払えなかった」を分ける。**
        /// accept=true を渡しても資源が足りなければ欠陥が付くので、
        /// 分けないとアームの結果が「払えた率 × 代償の重さ」の混合になる。</summary>
        public long gateReached;
        public long gateBloodPaid, gateRelicsPaid, gateTransferPaid;
        public long gateHopeBefore, gateHopeAfter;
        public long gateMaxHpPaid, gateRelicsBurned;

        /// <summary>[計装 2026-09-14] atkBase の <c>パッシブ加算</c> の内訳。
        /// 3 つの配列は添字が対応する (SkillId / 合計加算量 / 発火回数)。
        /// <b>発火回数を一緒に返すのが要点</b> ── <c>RegisterSkill</c> は
        /// 別アイテム由来の同名スキルを重複登録して<b>その回数だけ発火させる</b>ので、
        /// 「1 回 +6 の筋力」が実は毎ターン何回鳴っているのかは回数でしか分からない。</summary>
        /// <summary>[計装] 剣の舞 4 枚集約が成立したラン数と、 成立した層の分布。</summary>
        /// <summary>[計装] atkBase の内訳 (全戦闘の累計)。
        /// [0]件数 [1]武器素火力 [2]攻撃端子出目 [3]パッシブ加算 [6]atkBase [7]与ダメ。
        /// <b>配線 : フラットの比をここから直接読む</b> (素火力は比から除く)。</summary>
        public double[] damageBreakdown = new double[19];
        /// <summary>[計装] エリートが何を余分に配っているかの切り分け用 (2026-09-14)。
        /// 報酬倍率 (ゴールド) を ×1.3〜×1.8 で振っても傾きが 2〜3pt しか動かなかったので、
        /// <b>主因はゴールドではない</b>。 取得物を経路別に数えて一意に決める。</summary>
        public long goldGained;
        public long consumablesAcquired;
        public long combatTurnsTotal;
        public string[] passiveSourceNames = Array.Empty<string>();
        public long[] passiveSourceCounts = Array.Empty<long>();
        public long swordDanceTransforms;
        /// <summary>[計装] パッシブ枠の総数 / LEGENDARY 帯を引いた数 / 剣の舞へ差し替えた数。</summary>
        public long passiveSlotsRolled, passiveSlotsLegendary, danceSwaps;
        public long danceOffered, danceAcquired;
        /// <summary>[計装] パッシブの提示/取得 (レアリティ別・index = ItemRarity)。</summary>
        public long[] offeredByRarity = new long[8];
        public long[] acquiredByRarity = new long[8];

        /// <summary>[計装] ヴェスカ連戦の段別 (0=p1..3=p4)。
        /// 突入数 / 突入時 残HP% の合計 / 消耗品使用数。
        /// <b>段の勝率だけでは「消耗させているか」が分からない。</b></summary>
        public long[] vescaPhaseEntries = new long[4];
        public double[] vescaPhaseHpSum = new double[4];
        public long[] vescaPhaseConsumables = new long[4];
        public long[] swordDanceByFloor = new long[9];

        public string[] attackBonusSkills = Array.Empty<string>();
        public long[] attackBonusAmounts = Array.Empty<long>();
        public long[] attackBonusCalls = Array.Empty<long>();
        /// <summary>[計装] 最終戦 (8 層) だけの内訳。 全戦闘の平均と混ぜないこと。</summary>
        public string[] attackBonus7FSkills = Array.Empty<string>();
        public long[] attackBonus7FAmounts = Array.Empty<long>();
        public long[] attackBonus7FCalls = Array.Empty<long>();
        /// <summary>[計装] 最終戦だけの atkBase 内訳 (CombatManager.DamageBreakdown7F)。</summary>
        public double[] damageBreakdown7F = new double[19];

        /// <summary>[計装 2026-09-19] 戦闘の種類別 (0 雑魚 / 1 エリート / 2 ボス) の結果 (CombatSystem.SkillDiag)。
        /// 配線方策を替えたアームどうしで、 どの戦闘で技量が効いているかを見る。</summary>
        public long[] skillFights = new long[3];
        public long[] skillDeaths = new long[3];
        public long[] skillTurns = new long[3];
        public double[] skillHpLostPct = new double[3];
        /// <summary>[計装 2026-09-19] シールドの出どころ別 (CombatSystem.ShieldDiag)。
        /// 名前ごとに [雑魚, エリート, ボス] の 3 要素を平らに並べる。</summary>
        public string[] shieldSrcNames = Array.Empty<string>();
        public long[] shieldSrcAmounts = Array.Empty<long>();
        public long[] shieldSrcCounts = Array.Empty<long>();
        /// <summary>[計装] ボス別 (SkillDiag.ByBoss)。 名前ごとに [戦闘数, 総ターン, 10T 未満, 死亡] の 4 要素。</summary>
        public string[] bossNames = Array.Empty<string>();
        public long[] bossStats = Array.Empty<long>();
        /// <summary>[計装] ボス別の敵HP の減り方 (SkillDiag.BossDmg)。 名前ごとに 9 要素。</summary>
        public string[] bossDmgNames = Array.Empty<string>();
        public double[] bossDmg = Array.Empty<double>();
        /// <summary>[計装] 与ダメ出どころ別 (DmgSourceDiag)。 名前ごとに Width 個、 全戦闘とボス戦で別配列。</summary>
        /// <summary>[計装] 層 × 種類別の戦闘 (SkillDiag.ByFloorKind / ByFloorKindHpLost をそのまま)。</summary>
        public long[] floorKind = Array.Empty<long>();
        public double[] floorKindHpLost = Array.Empty<double>();
        /// <summary>[計装] エリートを選んだ理由別 (SkillDiag.EliteReason をそのまま)。</summary>
        public double[] eliteReason = Array.Empty<double>();
        /// <summary>[計装] FinishCombat の再入回数 (SkillDiag.FinishCombatReentry)。
        /// <b>0 でなければ層別の戦闘数・死亡数が二重に積まれている</b>。</summary>
        public long finishCombatReentry;
        /// <summary>[計装] Λ 層でランが終わった件数。 bandScore の帯 6 から引いて 5層道中 を出す。
        /// <b>戦闘側の計装 (SkillDiag) で引いてはいけない</b> ── あちらは救済で生き返ったランも
        /// 数えるので、 ラン単位の帯から引くと符号が合わない (実測で 5層道中 が 0 になっていた)。</summary>
        public int lambdaRunDeaths;
        public string[] dmgSrcNames = Array.Empty<string>();
        public double[] dmgSrcAll = Array.Empty<double>();
        public double[] dmgSrcBoss = Array.Empty<double>();
    }

    /// <summary>AutoRunner の設定をプロセス間で運ぶ。
    ///
    /// <para><b>なぜ反射か。</b> 逐次版の設定は EditorPrefs 由来の 20 個ほどのフィールドで、
    /// これを job 型に手で並べると<b>フィールドを増やした日に片方だけ古くなる</b>。
    /// 名前と値で運んでフィールドへ流し込めば、 運搬側は増減を知らなくて済む。</para>
    ///
    /// <para>対応する型は bool / int / float / string / enum のみ。 それ以外は捨てて警告を出す
    /// ── 黙って落とすと「設定したつもりが効いていない」という一番読めない事故になる。</para></summary>
    public static class RankMarginWorkerConfig
    {
        public static void Apply(AutoRunner runner, string[] keys, string[] values)
        {
            if (runner == null || keys == null || values == null) return;
            int n = Mathf.Min(keys.Length, values.Length);
            for (int i = 0; i < n; i++)
            {
                string key = keys[i];
                if (string.IsNullOrEmpty(key)) continue;
                FieldInfo f = typeof(AutoRunner).GetField(key,
                    BindingFlags.Public | BindingFlags.Instance);
                if (f == null)
                {
                    Debug.LogWarning("[RankMarginWorker] 未知の設定フィールド: " + key);
                    continue;
                }
                if (!TryConvert(f.FieldType, values[i], out object parsed))
                {
                    Debug.LogWarning("[RankMarginWorker] 変換できない設定: "
                        + key + "=" + values[i] + " (" + f.FieldType.Name + ")");
                    continue;
                }
                f.SetValue(runner, parsed);
            }
        }

        /// <summary>戦闘の条件を決める要のフィールドを、 <b>適用後の実値</b>で読み出す。
        /// 「送ったつもり」ではなく「入っていた値」を返すのが要点。</summary>
        public static string DescribeEffective(AutoRunner runner)
        {
            if (runner == null) return "";
            string[] watched =
            {
                "useMutualAttackPipeline", "wiringSkill", "metaBuffMode", "itemPickMode",
                "enableAllDebuffs", "forceNoRelic", "forceTheoreticalRelic",
                "shieldAbsorbsUnmitigable", "suppressFacePartOffers", "robberyBlocksShops",
                "challengeScoreTarget", "masterSeed", "rawTierRatio",
                "learnBotAi", "learnTier", "tuneBosses",
                // **2026-09-14: ここへ足し忘れて 60,000 ラン を捨てた。**
                //   自作ドライバが useIttBeta を送っておらず、 既定 false のまま走った
                //   (Editor の並列ランナーは True を運んでいた)。 アイテム評価の推定量が
                //   変わるので BOT の買い物が全層で変わり、 Balanced が 28.6% → 17.0% へ落ちた。
                //   監視リストに無い項目は<b>食い違っても画面に出ない</b> ──
                //   運ぶ可能性のあるフィールドは全部ここに並べること。
                "useIttBeta", "lockWeaponFamily", "metaBuildAxis", "sweepAllMetaAxes",
                "challengeSpec", "metaRankSpec",
                "robberySurcharge", "robberyFinalShopOnly", "robberyHopeCost", "bossTraceFloor",
                // 〈門〉の 3 工程 (2026-09-14)。 アームごとに切り替えるので**必ず印字する**
                //   ── 送ったつもりの値ではなく、 入っていた値を読む。
                "gateBotPaysBlood", "gateBotPaysRelics", "gateBotPaysTransfer", "gateBotSkipsAll",
                "gateRepairDrainPct", "gateIgnitionAttackCutPct", "gatePierceRate",
                "gateHopeReserveFromFloor", "saberWaltzShopBias", "enemyAttackSpec", "enemyAttackMul", "enemyHpSpec", "superRerollMode", "superLightRerollSamples", "superLightExactMaxDice", "logWiringDiff", "eliteUpgradePctOverride", "eliteRewardMul", "flatAttackMul",
                // 航行配点のエリート評価 (2026-09-15)。 BOT の手そのものを変えるので必ず印字する。
                "eliteNavBase", "eliteNavHpBonus", "navEliteCost",
                // 戦闘報酬の配分 (2026-09-15)。 **3 つはセットで意味を持つ** ので 3 つとも印字する
                //   ── ドロップだけ運んで金を運び忘れると「供給が減っただけ」を測ることになる。
                "eliteDropRate", "normalDropRate", "combatGoldScale",

                // 低HP 忌避ゲート (2026-09-15)。 BOT が戦闘を踏むかどうかを直接決める。
                "navBlockCredit", "navSafetyFights", "navGoldComfort", "navTileDanger", "lambdaFarmTiles", "lambdaFarmSweep", "lambdaLv3Risk", "navDangerFloor", "valorSlayerOff", "saleBonusHiPct", "saleBonusLoPct", "rerollKeepsSale", "itemPriceAdd",
                // リロールの量と代金 (2026-09-17)。 **上限と価格は別々に運ぶ** ── 棚が 12 枠固定で
                //   リロールが補給を兼ねているので、 回数を削ると「金が浮く」と「供給が減る」が混ざる。
                "rerollHardCap", "rerollPriceScale",
                // 序列の単位 (2026-09-17)。 **RawPt は準パワーの単位ごと変える** ので
                //   PowerCuts も連動する ── 運び漏らすと別の序列で測ったことになる。
                "ittRawPt",
                // リロール停止則 (2026-09-17)。 **3 本はセット** ── 規則だけ運んで事前値を
                //   運び忘れると、 別の停止位置で測ったことになる。
                "rerollStopRule", "rerollShelfValue", "rerollPriorWeight",
                "phase3BeforeReroll", "rerollCurveFirst", "rerollCurveStep",
            };
            var sb = new System.Text.StringBuilder();
            foreach (string name in watched)
            {
                FieldInfo f = typeof(AutoRunner).GetField(name,
                    BindingFlags.Public | BindingFlags.Instance);
                sb.Append(name).Append('=')
                  .Append(f == null ? "<無>" : (f.GetValue(runner) ?? "").ToString())
                  .Append('\n');
            }
            return sb.ToString();
        }

        private static bool TryConvert(Type type, string raw, out object parsed)
        {
            parsed = null;
            raw = raw ?? "";
            if (type == typeof(string)) { parsed = raw; return true; }
            if (type == typeof(bool))
            {
                if (!bool.TryParse(raw, out bool b)) return false;
                parsed = b; return true;
            }
            if (type == typeof(int))
            {
                if (!int.TryParse(raw, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int v)) return false;
                parsed = v; return true;
            }
            if (type == typeof(float))
            {
                if (!float.TryParse(raw, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out float v)) return false;
                parsed = v; return true;
            }
            if (type.IsEnum)
            {
                // 名前でも番号でも受ける。 親は番号 (EditorPrefs の生値) を書く。
                if (int.TryParse(raw, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int num))
                { parsed = Enum.ToObject(type, num); return true; }
                try { parsed = Enum.Parse(type, raw, true); return true; }
                catch { return false; }
            }
            return false;
        }
    }
}
