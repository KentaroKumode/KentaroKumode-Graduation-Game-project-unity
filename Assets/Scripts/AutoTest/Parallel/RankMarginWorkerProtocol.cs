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
        /// <summary>アイテム学習の 1 区間として走らせる時の出力先 (2026-11-04)。 空なら通常の測定。
        /// 指定すると探索 (ホールドアウト・サンプル加点) を学習バッチと同じに入れ、
        /// ラン毎の学習用記録 (<see cref="LearnRunFile"/>) をここへ書く。 <b>学習ファイルへは書かない</b>
        /// ── 合流は親が 1 か所でやる (プロセスごとに別の学習が育たないように)。</summary>
        public string learnRunsPath = "";
    }

    /// <summary>アイテム学習のラン 1 本分 (2026-11-04)。 <c>ItemLearningStats.IngestBatch</c> と
    /// <c>RunDataLogger.AppendBatch</c> が読む項目だけを持つ。 RunRec は HashSet / Dictionary を持ち
    /// JsonUtility で運べないので、 ここで配列へ詰め替える。</summary>
    [Serializable]
    public sealed class LearnRunRow
    {
        public int index;
        public int bandScore;
        public int reachedFloor;
        public bool isExploreRun;
        public long totalDamageDealt, totalDamageTaken, totalHealed, totalShieldGained;
        public string[] acquired = Array.Empty<string>();
        public string[] offered = Array.Empty<string>();
        public string[] exploreOffered = Array.Empty<string>();
        public string[] exploreAcquired = Array.Empty<string>();
        public string[] awakenedFormsKilled = Array.Empty<string>();
        // ── 診断用 (学習の合流には使わない・2026-11-04) ──
        public int deathFloor;
        public bool deathInBossFight;
        public string fatalEnemy = "";
        public int[] lowHpRiskSteps = Array.Empty<int>();
        // お金の流れ (診断用・2026-11-04)
        public int goldGained, coinsSpent, shopPurchases, shopBuyPassive, shopRerolls, shopRerollCoins, finalCoins, peakCoins, totalCombats;
        public LearnFightRow[] fights = Array.Empty<LearnFightRow>();
        /// <summary>金の内訳 (キー = 「深さ|経路」・2026-11-06)。</summary>
        public string[] goldKeys = Array.Empty<string>();
        public int[] goldVals = Array.Empty<int>();
        /// <summary>状態の記録 (10 列ずつ・AutoRunner.SnapState・2026-11-06)。</summary>
        public int[] stateSnaps = Array.Empty<int>();
        /// <summary>航行マップの層ごとの踏破 (4 列ずつ: 深さ, TileType, 数, 踏んだ数・2026-11-09)。</summary>
        public int[] mapVisit = Array.Empty<int>();
        /// <summary>[計装 2026-11-10] ラン開始時に実際に入っていた整備パネルの段。</summary>
        public string metaRanks = "";
        /// <summary>[計装 2026-11-12] ラン開始時の職業 (lockWeaponFamily の測定では武器の家系)。</summary>
        public string startClass = "";

        public static LearnRunRow From(AutoRunner.RunRec r)
        {
            string[] A(System.Collections.Generic.HashSet<string> s)
            {
                if (s == null || s.Count == 0) return Array.Empty<string>();
                var a = new string[s.Count]; s.CopyTo(a); return a;
            }
            return new LearnRunRow
            {
                index = r.index, bandScore = r.bandScore, reachedFloor = r.reachedFloor,
                isExploreRun = r.isExploreRun,
                totalDamageDealt = r.totalDamageDealt, totalDamageTaken = r.totalDamageTaken,
                totalHealed = r.totalHealed, totalShieldGained = r.totalShieldGained,
                acquired = A(r.acquiredItemsEver), offered = A(r.offeredItemsEver),
                exploreOffered = A(r.exploreOfferedEver), exploreAcquired = A(r.exploreAcquiredEver),
                awakenedFormsKilled = A(r.awakenedFormsKilled),
                deathFloor = r.deathFloor, deathInBossFight = r.deathInBossFight, fatalEnemy = r.fatalEnemy ?? "",
                lowHpRiskSteps = r.lowHpRiskSteps != null ? (int[])r.lowHpRiskSteps.Clone() : Array.Empty<int>(),
                goldGained = r.totalGoldGained, coinsSpent = r.totalCoinsSpent, shopPurchases = r.shopPurchases,
                shopBuyPassive = r.shopBuyPassive, shopRerolls = r.shopRerolls, shopRerollCoins = r.shopRerollCoins,
                finalCoins = r.finalCoins, peakCoins = r.peakCoins, totalCombats = r.totalCombats,
                fights = Fights(r.combats),
                goldKeys = r.goldKeys ?? Array.Empty<string>(), goldVals = r.goldVals ?? Array.Empty<int>(),
                stateSnaps = r.stateSnaps != null ? r.stateSnaps.ToArray() : Array.Empty<int>(),
                mapVisit = r.mapVisit != null ? r.mapVisit.ToArray() : Array.Empty<int>(),
                metaRanks = r.metaRanks ?? "",
                startClass = r.startClass ?? "",
            };
        }

        private static LearnFightRow[] Fights(System.Collections.Generic.List<AutoRunner.CombatRec> cs)
        {
            if (cs == null || cs.Count == 0) return Array.Empty<LearnFightRow>();
            var a = new LearnFightRow[cs.Count];
            for (int i = 0; i < cs.Count; i++)
            {
                var c = cs[i];
                a[i] = new LearnFightRow
                {
                    enemyId = c.enemyId ?? "", floor = c.floor, isBoss = c.isBoss, won = c.won, end = c.isFightEnd,
                    turns = c.turns, hpBefore = c.hpBefore, hpAfter = c.hpAfter, maxHp = c.playerMaxHpEnd,
                    dealt = c.damageDealt, taken = c.damageTaken, enemyMaxHp = c.enemyMaxHP,
                    key = c.encounterKey ?? "",
                    dmgParts = c.dmgParts ?? Array.Empty<int>(),
                    fbDecided = c.fbDecided, fbTurns = c.fbTurns, fbKillTurns = c.fbKillTurns,
                    hpLedger = c.hpLedger ?? Array.Empty<float>(), shownFloor = c.shownFloor,
                    axisAtStart = c.axisAtStart ?? Array.Empty<int>(),
                    mainPred = c.mainPred, mainPredCorr = c.mainPredCorr, mainActual = c.mainActual,
                    atkHist = c.atkHist ?? Array.Empty<int>(), dawnShots = c.dawnShots ?? Array.Empty<int>(),
                };
            }
            return a;
        }

        public AutoRunner.RunRec ToRunRec()
        {
            var r = new AutoRunner.RunRec
            {
                index = index, bandScore = bandScore, reachedFloor = reachedFloor, isExploreRun = isExploreRun,
                totalDamageDealt = totalDamageDealt, totalDamageTaken = totalDamageTaken,
                totalHealed = totalHealed, totalShieldGained = totalShieldGained,
            };
            r.acquiredItemsEver.UnionWith(acquired ?? Array.Empty<string>());
            r.offeredItemsEver.UnionWith(offered ?? Array.Empty<string>());
            r.exploreOfferedEver.UnionWith(exploreOffered ?? Array.Empty<string>());
            r.exploreAcquiredEver.UnionWith(exploreAcquired ?? Array.Empty<string>());
            r.awakenedFormsKilled.UnionWith(awakenedFormsKilled ?? Array.Empty<string>());
            return r;
        }
    }

    /// <summary>戦闘 1 回分の診断用の記録 (CombatRec の写し)。</summary>
    [Serializable]
    public sealed class LearnFightRow
    {
        public string enemyId = "";
        public int floor, turns, hpBefore, hpAfter, maxHp, dealt, taken, enemyMaxHp;
        public bool isBoss, won, end;
        public string key = "";   // 戦闘名の鍵 (EncounterDangerStats・2026-11-05)
        /// <summary>与ダメの内訳 [武器, 攻撃の出目, 攻撃+N, 主攻撃の与ダメ, 固定ダメ, 追撃, 出目由来の攻撃, 毒, 出血, 臨界の爆発] (2026-11-06)。
        /// 毒と爆発は固定ダメの内数、 出血は固定ダメの外 (敵 HP を直接引く)。</summary>
        public int[] dmgParts;
        /// <summary>[計装 2026-11-10] 配線を決めたターン数 / うち防ぎ切りの規則の対象 / うち今ターン撃破。</summary>
        public int fbDecided, fbTurns, fbKillTurns;
        /// <summary>[計装 2026-11-10] 敵の HP を実際に減らした量の経路別 (CombatSystem.EnemyHpLedger の添字)。 dmgParts とは数えた時点が違う
        /// (こちらは回避・シールド・踏みとどまり・はみ出しの後)。</summary>
        public float[] hpLedger;
        /// <summary>[計装 2026-11-10] 見せる層 (currentFloor)。 floor は深さ。</summary>
        public int shownFloor;
        /// <summary>[計装 2026-11-10] 段の開始時の偏重 6 軸の品の数 (会心・出血・毒・臨界・充電・シールド)。</summary>
        public int[] axisAtStart;
        /// <summary>[計装 2026-11-10 V3 mainCorr] 攻撃したターンの主攻撃の 見積もり / 比を掛けた見積もり / 実際 (DamageBreakdown[7] の増分) の合計。</summary>
        public float mainPred, mainPredCorr, mainActual;
        /// <summary>[計装 2026-11-10] 配線のターン数を攻撃の本数 0/1/2/3+ で数えたもの。</summary>
        public int[] atkHist;
        /// <summary>[計装 2026-11-10] 黎明の溜める形で、 攻撃したターンに乗った溜め (%)。</summary>
        public int[] dawnShots;
    }

    [Serializable]
    public sealed class LearnRunFile
    {
        public int seedStart;
        public LearnRunRow[] runs = Array.Empty<LearnRunRow>();
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
        /// <summary>[2026-11-12 #118] アームの終わりに BOT が読んでいた学習 (帯のフォルダ名・序列の出どころ・危険度)。</summary>
        public string learningRead = "";
        /// <summary>[2026-11-12 #118] 手書きの序列 (学習データなし) で走ったか。 親はサマリーにはっきり出す。</summary>
        public bool learningFallback;
        // 救済の発動数。 **ログでは数えられない** ── バッチ中は logEnabled=false なので、
        //   「ログに出ていない＝発動していない」は成り立たない。 計数で返す。
        public long torchRevivals;
        public long lastStandRevivals;
        public long fleuretRevivals;
        /// <summary>救済で戻した HP の合計 (灯火・ラストスタンドのみ)。
        /// <b>「値を変えたのに効いていない」を数字で切り分けるために要る。</b>
        /// 発動数が同じでも配った HP が変わっていなければ、 変更が届いていない。</summary>
        public long revivalHpRestored;
        /// <summary>兵站 r10: 物資で払った回数と物資の総消費。</summary>
        public long provisionPayments;
        public long provisionSpent;
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
        public long[] bossEntryProvision = new long[9];
        public long[] bossEntryProvisionTier = new long[9];
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
        public long gateProvisionBefore, gateProvisionAfter;
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
        public double[] damageBreakdown = new double[20];
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
        /// <summary>[計装] 最終戦だけの atkBase 内訳 (CombatStats.DamageBreakdown7F)。</summary>
        public double[] damageBreakdown7F = new double[20];

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
        /// <summary>敵 HP の減りの経路・品別 (<see cref="CombatSystem.HpLossDiag"/>)。 名前 × (種類 3 × 層 5) を平らに並べる。</summary>
        public string[] hpLossNames = Array.Empty<string>();
        public double[] hpLoss = Array.Empty<double>();
        public string[] dmgSrcNames = Array.Empty<string>();
        public double[] dmgSrcAll = Array.Empty<double>();
        public double[] dmgSrcBoss = Array.Empty<double>();
        /// <summary>[計装 2026-11-11] ヴェスカ連戦だけの与ダメ増加の出どころ (dmgSrcNames と同じ並び) ・倍率の段 (CombatStats.DamageStages7F)・被ダメのカット (GuardDiag.Cut7F)。</summary>
        public double[] dmgSrcVesca = Array.Empty<double>();
        public double[] damageStages7F = Array.Empty<double>();
        public double[] cut7F = Array.Empty<double>();
        /// <summary>[計装 2026-11-12 #103] ヴェスカ〈停滞する時間〉で止まった端子 (GuardDiag.Stag7F・段 × 8 の並びはそちらの注記)。</summary>
        public double[] stag7F = Array.Empty<double>();
        /// <summary>[計装 2026-11-12] 端子ごとの使われ方 (<see cref="CombatSystem.TerminalDiag"/>)。 名前 × TerminalDiag.Width を平らに並べる。</summary>
        public string[] termDiagNames = Array.Empty<string>();
        public double[] termDiag = Array.Empty<double>();
        /// <summary>[計装 2026-11-12・#35] 追跡者に捕まった後の結果 (<see cref="GameLoop.NavDiag"/>)。 型の名前 × NavDiag.Width を平らに並べる。</summary>
        public string[] navDiagNames = Array.Empty<string>();
        public double[] navDiag = Array.Empty<double>();
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
                // 「superAI.winHpWeight」のようにドットで入れ子のフィールドも指せる (2026-11-05)。
                object target = runner;
                string leaf = key;
                int dot = key.IndexOf('.');
                while (dot > 0 && target != null)
                {
                    var holder = target.GetType().GetField(leaf.Substring(0, dot), BindingFlags.Public | BindingFlags.Instance);
                    target = holder?.GetValue(target);
                    leaf = leaf.Substring(dot + 1);
                    dot = leaf.IndexOf('.');
                }
                FieldInfo f = target?.GetType().GetField(leaf,
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
                f.SetValue(target, parsed);
            }
        }

        /// <summary>戦闘の条件を決める要のフィールドを、 <b>適用後の実値</b>で読み出す。
        /// 「送ったつもり」ではなく「入っていた値」を返すのが要点。</summary>
        public static string DescribeEffective(AutoRunner runner)
        {
            if (runner == null) return "";
            string[] watched =
            {
                "wiringSkill", "metaBuffMode", "itemPickMode",
                "enableAllDebuffs", "forceNoRelic", "forceTheoreticalRelic",
                "shieldAbsorbsUnmitigable", "suppressFacePartOffers", "robberyBlocksShops",
                "challengeScoreTarget", "masterSeed", "rawTierRatio",
                "learnBotAi", "learnTier",
                // **2026-09-14: ここへ足し忘れて 60,000 ラン を捨てた。**
                //   自作ドライバが useIttBeta を送っておらず、 既定 false のまま走った
                //   (Editor の並列ランナーは True を運んでいた)。 アイテム評価の推定量が
                //   変わるので BOT の買い物が全層で変わり、 Balanced が 28.6% → 17.0% へ落ちた。
                //   監視リストに無い項目は<b>食い違っても画面に出ない</b> ──
                //   運ぶ可能性のあるフィールドは全部ここに並べること。
                "useIttBeta", "lockWeaponFamily", "metaBuildAxis", "sweepAllMetaAxes",
                "challengeSpec", "metaRankSpec",
                "robberySurcharge", "robberyFinalShopOnly", "robberyProvisionCost", "bossTraceFloor",
                // 〈門〉の 3 工程 (2026-09-14)。 アームごとに切り替えるので**必ず印字する**
                //   ── 送ったつもりの値ではなく、 入っていた値を読む。
                "gateBotPaysBlood", "gateBotPaysRelics", "gateBotPaysTransfer", "gateBotSkipsAll",
                "gateRepairDrainPct", "gateIgnitionAttackCutPct", "gatePierceRate",
                "gateProvisionReserveFromFloor", "saberWaltzShopBias", "enemyAttackSpec", "enemyAttackMul", "enemyHpSpec", "eliteUpgradePctOverride", "eliteRewardMul", "flatAttackMul",
                // 航行配点のエリート評価 (2026-09-15)。 BOT の手そのものを変えるので必ず印字する。
                "eliteNavBase", "eliteNavHpBonus", "navEliteCost",
                // 戦闘報酬の配分 (2026-09-15)。 **3 つはセットで意味を持つ** ので 3 つとも印字する
                //   ── ドロップだけ運んで金を運び忘れると「供給が減っただけ」を測ることになる。
                "eliteDropRate", "normalDropRate", "combatGoldScale",

                // 低HP 忌避ゲート (2026-09-15)。 BOT が戦闘を踏むかどうかを直接決める。
                "navBlockCredit", "navSafetyFights", "navGoldComfort", "navTileDanger", "navDangerFloor", "valorSlayerOff", "saleBonusHiPct", "saleBonusLoPct", "rerollKeepsSale", "itemPriceAdd",
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
                // 計測でイベントを切っているか (2026-11-05)。 クリア率の意味が変わるので必ず印字する。
                "suppressEvents", "suppressConvictionChain", "freeNavScout", "navLearnedDanger", "measureNoExplore",
                // 2026-11-10: 配線が与ダメの倍率を読むか (−1 = 技量に従う) と実効値・軸に寄せる方策・配布の有無 (配布は使わない前提を確かめる)
                "v3.readOutgoingMult", "ReadOutgoingMultEffective", "buyAxisFocus", "buyAxisFocusBonus",
                "axisPackageAxis", "axisPackageCount", "v3.hpLedger", "v3.axisCommitBuy", "predTrace",
                // 2026-11-10: 主攻撃の見積もりを実績の比で掛け直すか・黎明の溜める形 (送った値と、 ラン開始時に本体へ入れた値)
                "v3.mainCorr", "daybreakNextAttackPct", "DaybreakNextAttackPctApplied", "weaponTierCap", "vescaDiceForce",
                "pursuerNoReward", "pursuerAtkMul", "pursuerHpAdd", "pursuerSupply", "navFear", "navFearMacro", "navFearRange",
                // 2026-11-12: 縄張りの作り (追跡者が縄張りの外へ出るか・縄張りの宝の中身・BOT が宝を取りに行くしきい値)
                "pursuerLeavesTerritory", "territoryTreasure", "territoryTreasureHpMargin", "navTerritoryDetour",
                // 2026-11-10: 測定用の物差し (主攻撃 / 毒・出血・爆発の倍率)。 送った値と、 ラン開始時に本体へ入れた値
                "probeMainMult", "ProbeMainMultApplied", "probePassiveMult", "ProbePassiveMultApplied",
                // 2026-11-12 (#107): 配線が収支で決まる敵のパッシブと〈吸血〉を読むか (−1 = 技量に従う) と実効値・内訳
                "v3.readEnemyPassives", "ReadEnemyPassivesEffective", "v3.enemyPassiveMask",
                // 2026-11-12 (#105): 山札の 2 ターン先読み (既定 OFF) と、 その範囲・候補数・標本数
                "deckLookahead", "deckLookaheadBossOnly", "deckLookaheadTopK", "deckLookaheadSamples", "deckLookaheadProbe",
                // 2026-11-12 (#6): 挑戦カードを 1 枚ずつ受ける測定の口と、〈金で殴る〉の BOT の金の値段・取り置き
                "challengeForce", "goldPunchHpPerGold", "goldPunchReserve",
                // 2026-11-12 (#118): 挑戦の難易度 (v3_queue の腕が振る値) と、 BOT が読む学習の帯の固定・欠けたら止めるか
                "challengeCardDifficulty", "botLearningBandOverride", "failOnMissingLearning",
                // 2026-11-12 (#95 / #20 / #41 / #102 / #108): 腕ごとに切り替える測定の口。 送った値と、 ラン開始時に本体へ入れる値
                "relicLoadoutSpec", "relicStatOff", "RelicStatOffMaskApplied", "relicSetBonusOff", "RelicSetBonusOffMaskApplied",
                "enemyArmorPenSpec", "EnemyArmorPenOverrideApplied",
                "challengeCardPickByAxis",
                "terminalsLeaveDeck", "randomGrantTerminals", "armorDefPerRank", "armorFlatEvery",
                // 2026-11-12 (#102 第 2 弾): 端子の調整を元の値へ戻す腕 (0 / False = 今の値)
                "terminalHealMul", "terminalArresterNeed", "terminalFirstAidBattleOnly",
                // 2026-11-12 (#134): Optimal の配線のミスの率 (0 = ミスなし・Super と素朴版は経路を通らない)
                "optimalMistakeRate",
            };
            var sb = new System.Text.StringBuilder();
            foreach (string name in watched)
            {
                // ドットで入れ子 (v3.xxx) も読む。 フィールドが無ければプロパティ (実効値) を読む。
                object target = runner; string leaf = name; bool missing = false;
                int dot = leaf.IndexOf('.');
                while (dot > 0 && target != null)
                {
                    FieldInfo pf = target.GetType().GetField(leaf.Substring(0, dot), BindingFlags.Public | BindingFlags.Instance);
                    if (pf == null) { missing = true; break; }
                    target = pf.GetValue(target);
                    leaf = leaf.Substring(dot + 1);
                    dot = leaf.IndexOf('.');
                }
                object val = null;
                if (!missing && target != null)
                {
                    FieldInfo f = target.GetType().GetField(leaf, BindingFlags.Public | BindingFlags.Instance);
                    if (f != null) val = f.GetValue(target) ?? "";
                    else
                    {
                        PropertyInfo pi = target.GetType().GetProperty(leaf, BindingFlags.Public | BindingFlags.Instance);
                        if (pi != null) val = pi.GetValue(target) ?? "";
                    }
                }
                sb.Append(name).Append('=')
                  .Append(val == null ? "<無>" : val.ToString())
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
