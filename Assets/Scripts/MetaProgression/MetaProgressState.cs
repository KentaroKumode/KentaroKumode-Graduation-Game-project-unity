using System.Collections.Generic;

namespace MetaProgression
{
    /// <summary>
    /// プレイヤーのメタ進行状態。PlayerPrefs に JSON で保存される。
    ///
    /// 整備盤 (docs/GAME.md §15-1・2026-11-23): 開いたマス・持っているパネル・置き方を持つ。 効果の数値は持たない (パネルの定義 MetaBoard から都度出す)。
    /// 旧いセーブ: 58 段トラック (currentLevel)・24 点の配点式 (panelPointsPurchased)・基礎整備と作戦の枠 (baseTier ほか) は払ったトークンを全額返し、 遺物と素材はトークンに換える。
    /// </summary>
    /// <summary>[旧] 遺物 1 個 (2026-11-23 に撤去)。 旧いセーブの ownedRelics の数を数えるためだけの空の型 (JsonUtility は知らない欄を捨てる)。</summary>
    [System.Serializable]
    public class LegacyRelicStub { }

    [System.Serializable]
    public class MetaProgressState
    {
        public int tokens;

        // === 読み物 (WorldVignettes) 解禁用カウンタ ===
        // JsonUtility は Dictionary<,> を非対応のため、並列リストで持つ。
        // アクセスは VignetteUnlockState の GetEndingClearCount / IsEndingMaxDiffCleared 経由。
        /// <summary>エンド別クリア回数: キーリスト ("end1"〜"end5")。endingClearValues と並列。</summary>
        public List<string> endingClearKeys   = new List<string>();
        /// <summary>エンド別クリア回数: 値リスト。endingClearKeys と並列。</summary>
        public List<int>    endingClearValues = new List<int>();
        /// <summary>最高難度クリア済エンドのIDリスト (要素が存在 = true)。</summary>
        public List<string> endingMaxDiffCleared = new List<string>();

        // === 記録 (codex) の「出会った・手に入れた・着いた」 (#24・2026-11-11) ===
        //   書くのは Codex.CodexRecord だけ (人が遊んでいる時だけ数える)。 解禁の判定は Codex.CodexRecord / VignetteUnlockState。
        /// <summary>戦ったことのある敵の id。</summary>
        public List<string> codexEnemies = new List<string>();
        /// <summary>手に入れたことのある品の id (items.json の id = 表示名)。</summary>
        public List<string> codexItems = new List<string>();
        /// <summary>着いたことのある層 (Run.Depth)。</summary>
        public List<int> codexFloors = new List<int>();

        // === 実績 (docs/GAME.md §15-6・正本は Achievements/AchievementCatalog.cs) ===
        // Parallel lists keep JsonUtility compatibility. IDs are stable strings.
        public List<string> unlockedAchievementIds = new List<string>();
        public List<long> achievementUnlockUtcTicks = new List<long>();
        public int achievementCombatWins;
        public int achievementRoleMask;
        /// <summary>[実績] 倒したことのある 2 層 (深さ 3) のボスの id (三つの玉座・ランを跨いで数える)。 書くのは AchievementService だけ。</summary>
        public List<string> achievementLayer2Bosses = new List<string>();
        /// <summary>[実績] 背負って 3 層のボスを倒した「大罪:○○」の種類 (ChallengeCards の kind・七つの重荷)。</summary>
        public List<string> achievementSinsCarried = new List<string>();
        /// <summary>[実績] エリートを倒した数 (精鋭狩り)。</summary>
        public int achievementEliteKills;
        /// <summary>[実績] 選択肢を選んで終えたイベントの数 (寄り道の常連)。</summary>
        public int achievementEventsDone;
        /// <summary>[実績] 出会ったイベントの名前 (EventDefinition.name・旅の目録)。</summary>
        public List<string> achievementEventsSeen = new List<string>();
        /// <summary>[実績] 受けた挑戦カードのバフの種類 (ChallengeCards の kind・品揃え)。</summary>
        public List<string> achievementBuffsTaken = new List<string>();
        /// <summary>[実績] 最後の段 (T4+) まで鍛えた武器の家系 (items.json の family・四つの得物)。</summary>
        public List<string> achievementWeaponFamiliesMaxed = new List<string>();

        // (旧: 挑戦デバフの配点 `challenge` (§15-2) は 2026-11-10 に撤去 (#9)。 JsonUtility は未知の欄を黙って捨てる)

        /// <summary>挑戦カード (docs/GAME.md §23-25) の難易度 (0..10 = ラン中にカードを引く回数)。 整備の「挑戦」の目盛りで選ぶ。</summary>
        public int challengeDifficulty;
        /// <summary>5 層を踏破した最高の難易度。 選べるのはこの +1 まで。</summary>
        public int challengeBest;

        // 双蛇のダイス〈永劫〉: 勝利した戦闘数をランを跨いで永続蓄積。パネルとは独立。
        public int eternalStacks;

        /// <summary>灰燼の王 (深さ 6 = 画面の第 4 層のボス) を倒したことがあるか (2026-11-13・docs/GAME.md §4-2)。 倒した瞬間に立てる (結末の種類によらない)。
        /// 立っていると、 深さ 6 の〈灰に埋もれた基地〉が再訪の版になり、 手記の最後の頁を開いて〈真理〉(深さ 7 への資格) を得られる。
        /// 読み書きは <see cref="GameLoop.AshBaseEvent"/> だけ (測定の口 GameHooks.AshKingBeatenEver が値を持てばそちらが優先)。
        /// 図鑑の敵 (<see cref="codexEnemies"/>) は「戦ったことがある」なので代わりに使えない。</summary>
        public bool ashKingBeaten;

        /// <summary>チュートリアルのラン「街に着く前」(docs/GAME.md §23-35) を終えたか (2026-11-24)。 立っていない間は、 タイトルの PLAY がチュートリアルのラン指定
        /// (GameLoop.RunSpec.FirstRunTutorial) を始める。 ボスを倒した時に立てる (GameLoop.RunSpec.MarkTutorialDone)。 2 回目以降に見る入口は無い (作者の決定)。</summary>
        public bool tutorialDone;

        /// <summary>〈さびれた観測所〉で博士に会った回数。 **ランを跨いで数える。**
        /// 0 = 初対面 / 1 以上 = 再訪の文面に切り替わる。
        ///
        /// <para>再訪で博士がするのは「また来たね」という再会ではない ──
        /// それはプレイヤーの反復を彼が記憶していることになり、 §24 で廃止された
        /// 永劫回帰 (決定論的宇宙ループ) の再導入になる。 彼の自説に沿わせて
        /// **「同じ人間が複数の写しに居る」ことへの戸惑い**として書く。
        /// 記憶ではなく観測データの異常として扱わせること。</para>
        ///
        /// <para>出現率が 1/38 なので、 再訪を見ること自体が稀。
        /// ただし AutoRunner の長いバッチでは早々に 1 を超えて以後ずっと再訪側になる。
        /// 報酬は両方同じなので測定には影響しないが、 文面の出現比は実プレイと違う。</para></summary>
        /// <summary>[廃止] ここに置いたが **AutoRunner.RunOne が毎ラン State を作り直す**ため
        /// ラン跨ぎで残らず、1000 ラン で再訪が 0 回だった。
        /// 実体は <see cref="GameLoop.ObservatoryState"/> (PlayerPrefs 直) へ移設済み。
        /// JsonUtility はフィールドを消しても旧セーブを黙って読めるので残置不要だが、
        /// 同じ場所へ再実装されるのを防ぐために名前だけ残す。</summary>
        [System.Obsolete("GameLoop.ObservatoryState を使うこと")]
        public int observatoryMeetings;

        // === 旧い遺物 (§15-5・2026-11-23 に撤去)。 旧いセーブを読んで返金するためだけに残す (移行で消す) ===
        /// <summary>[旧] 持っていた遺物。 中身は読まない (数だけ・1 個 <see cref="LegacyRelicRefund"/> トークン)。</summary>
        public List<LegacyRelicStub> ownedRelics = new List<LegacyRelicStub>();
        /// <summary>[旧] 遺物の強化素材 (1 個 <see cref="LegacyMaterialRefund"/> トークン)。</summary>
        public int relicMaterial;

        // === 整備盤 (2026-11-23・docs/GAME.md §15-1・MetaBoard) ===
        /// <summary>開いたマス (bit = マスの index 0〜8・4 = 中心)。</summary>
        public int boardOpenMask;
        /// <summary>持っているパネルの id (買った物と実績で手に入れた物)。</summary>
        public List<string> ownedPanels = new List<string>();
        /// <summary>盤に置いているパネル (置き直しは無料)。 効くのは <see cref="MetaBoard.Valid"/> を通った物だけ。</summary>
        public List<BoardPlacement> boardPlacements = new List<BoardPlacement>();

        // 効果の控え (置き方・開いたマスを変える口が <see cref="BoardChanged"/> で捨てる)。 戦闘中に何度も読まれるので毎回は数えない
        [System.NonSerialized] private BoardStats _boardStats;
        [System.NonSerialized] private List<BoardPlacement> _boardValid;
        [System.NonSerialized] private readonly HashSet<MetaModule> _boardModules = new HashSet<MetaModule>();

        /// <summary>盤を変えたら呼ぶ (効果の控えを捨てる)。</summary>
        public void BoardChanged() { _boardStats = null; _boardValid = null; }

        private void EnsureBoard()
        {
            if (_boardStats != null) return;
            if (ownedPanels == null) ownedPanels = new List<string>();
            if (boardPlacements == null) boardPlacements = new List<BoardPlacement>();
            _boardValid = MetaBoard.Valid(boardOpenMask, ownedPanels, boardPlacements);
            _boardStats = MetaBoard.Sum(_boardValid, _boardModules);
        }

        /// <summary>効いているパネルの数値の合計。</summary>
        public BoardStats BoardEffects { get { EnsureBoard(); return _boardStats; } }
        /// <summary>効いている置き方 (持っている・開いたマスだけ・重ならない)。</summary>
        public List<BoardPlacement> ValidPlacements { get { EnsureBoard(); return _boardValid; } }
        /// <summary>そのモジュール (主方針・補助・種火の効果) を持つパネルが効いているか。</summary>
        public bool BoardHas(MetaModule m) { EnsureBoard(); return _boardModules.Contains(m); }
        /// <summary>効いている種火 (2×2 は中心を必ず使うので 1 枚まで)。 無ければ null。</summary>
        public MetaModule? BoardSpark()
        {
            EnsureBoard();
            foreach (var m in _boardModules) if (MetaPanel.SlotOf(m) == MetaSlot.Spark) return m;
            return null;
        }

        public bool OwnsPanel(string id) => ownedPanels != null && ownedPanels.Contains(id);

        /// <summary>パネルを持たせる (購入・実績・測定)。 持っていれば何もしない。</summary>
        public void GrantPanel(string id)
        {
            if (string.IsNullOrEmpty(id) || MetaBoard.Get(id) == null) return;
            if (ownedPanels == null) ownedPanels = new List<string>();
            if (!ownedPanels.Contains(id)) ownedPanels.Add(id);
            BoardChanged();
        }

        /// <summary>パネルを置く (同じパネルは置き直し)。 持っていない・はみ出す・閉じたマス・ほかのパネルと重なるなら false で何も変えない。</summary>
        public bool PlacePanel(BoardPlacement p)
        {
            if (p == null || !OwnsPanel(p.id)) return false;
            int m = MetaBoard.MaskOf(MetaBoard.CellsOf(p));
            if (m == 0 || (m & ~boardOpenMask) != 0) return false;
            if (boardPlacements == null) boardPlacements = new List<BoardPlacement>();
            foreach (var q in boardPlacements)
                if (q != null && q.id != p.id && (MetaBoard.MaskOf(MetaBoard.CellsOf(q)) & m) != 0) return false;
            boardPlacements.RemoveAll(q => q == null || q.id == p.id);
            boardPlacements.Add(new BoardPlacement(p.id, ((p.rot % 4) + 4) % 4, p.row, p.col));
            BoardChanged();
            return true;
        }

        /// <summary>パネルを盤から外す (持ち物には残る)。</summary>
        public void RemovePanel(string id) { boardPlacements?.RemoveAll(q => q == null || q.id == id); BoardChanged(); }

        /// <summary>盤を測定・移行の指定どおりにする (トークンは払わない): マスを開き、 パネルを持たせ、 置き方を丸ごと差し替える。</summary>
        public void ForceBoard(int openMask, List<BoardPlacement> placements)
        {
            boardOpenMask = openMask & ((1 << MetaBoard.Cells) - 1);
            boardPlacements = new List<BoardPlacement>();
            BoardChanged();
            if (placements == null) return;
            foreach (var p in placements) { GrantPanel(p.id); PlacePanel(p); }
        }

        /// <summary>盤の短い文 (ログ・実効状態)。</summary>
        public string DescribeBoard() => MetaBoard.Describe(boardOpenMask, ValidPlacements);

        // === レガシー (旧 58 段トラック時代の遺物・マイグレーション判定にのみ使用) ===
        /// <summary>旧 currentLevel。> 0 なら未マイグレーション状態 → Migrate() でトークン返却して 0 化。</summary>
        public int currentLevel;

        // === 旧い整備パネル (基礎整備 6 段 + 作戦の枠 3 つ・2026-11-20〜11-23)。 旧いセーブを読んで返金するためだけに残す (移行で消す) ===
        /// <summary>[旧] 買った基礎整備の段。</summary>
        public int baseTier;
        /// <summary>[旧] 開いた作戦の枠の数。</summary>
        public int slotsOpen;
        /// <summary>[旧] 解禁したモジュール (<see cref="MetaModule"/> の int)。</summary>
        public List<int> unlockedModules = new List<int>();

        // === 旧い整備パネル (24 点の配点式・2026-11-20 撤去)。 旧いセーブを読んで返金するためだけに残す ===
        /// <summary>[旧] 買った点の数。 > 0 なら <see cref="MigrateFromPointPanelIfNeeded"/> が全額返して 0 にする。</summary>
        public int panelPointsPurchased;
        /// <summary>[旧] トラックごとの段。 返金は点の数だけで決まるので読まない (移行で消す)。</summary>
        public int[] panelRanks;

        public MetaProgressState()
        {
            EnsurePanelInitialized();
        }

        /// <summary>整備盤の欄を必要に応じて初期化する (JsonUtility 復元後の欠損の対応も兼ねる)。 冪等。</summary>
        public void EnsurePanelInitialized()
        {
            if (ownedPanels == null) ownedPanels = new List<string>();
            if (boardPlacements == null) boardPlacements = new List<BoardPlacement>();
            ownedPanels.RemoveAll(id => MetaBoard.Get(id) == null);      // 名前の変わった・消えたパネル
            boardOpenMask &= (1 << MetaBoard.Cells) - 1;
            BoardChanged();
        }

        // ============================================================
        //  マイグレーション (旧 58 段 → 配点方式)
        // ============================================================

        /// <summary>旧 currentLevel > 0 のセーブを検出したら、消費トークンを全額返却して 0 にする。
        /// 呼び出しは Manager.Load 直後 (一度だけ)。 冪等。</summary>
        public void MigrateFromLegacyTrackIfNeeded()
        {
            if (currentLevel <= 0) return;
            int refund = 0;
            for (int lv = 1; lv <= currentLevel; lv++)
                refund += LegacyTrackStepCost(lv);
            tokens += refund;
            currentLevel = 0;
            UnityEngine.Debug.Log($"[MetaMigrate] 旧 58 段トラックの分を返却: {refund} トークン");
        }

        /// <summary>旧 58 段トラックの lv 段目の購入コスト (トークン)。 返金の計算にだけ使う。
        /// 旧 MetaBuffTrack.CalcCost の式 (2026-11-13 にトラックの定義ごと削除・#132)。</summary>
        private static int LegacyTrackStepCost(int lv) => 30 + lv * 2;

        /// <summary>旧い整備パネル (24 点の配点式) で点を買っていたセーブなら、 点に払ったトークンを全額返して旧い欄を消す (2026-11-20・§1-3)。
        /// <see cref="MigrateFromLegacyTrackIfNeeded"/> と同じ形 (返して 0 にする・冪等)。 呼び出しは Manager.Load 直後。</summary>
        public void MigrateFromPointPanelIfNeeded()
        {
            if (panelPointsPurchased <= 0) { panelRanks = null; return; }
            int refund = 0;
            for (int n = 1; n <= panelPointsPurchased; n++) refund += LegacyPointCost(n);
            tokens += refund;
            UnityEngine.Debug.Log($"[MetaMigrate] 旧い 24 点の整備パネルから移行: {panelPointsPurchased} 点の {refund} トークンを返却");
            panelPointsPurchased = 0;
            panelRanks = null;
        }

        /// <summary>旧い整備パネルの n 個目の点のトークン (旧 MetaPanel.PointCost = 8 × n)。 返金の計算にだけ使う。</summary>
        private static int LegacyPointCost(int n) => 8 * n;

        // ---- 旧い整備パネル (基礎整備 + 作戦の枠) と遺物 → 整備盤 (2026-11-23) ----

        /// <summary>[旧] 基礎整備の n 段目 (1 起点) のトークン。 返金の計算にだけ使う。</summary>
        private static readonly int[] LegacyBaseTierCost = { 40, 60, 120, 160, 240, 320 };
        /// <summary>[旧] 作戦の n 枠目のトークン。</summary>
        private static readonly int[] LegacySlotOpenCost = { 50, 150, 300 };
        /// <summary>[旧] モジュールの解禁 (主方針 120・補助と種火 80)。</summary>
        private static int LegacyModuleCost(int m)
            => System.Enum.IsDefined(typeof(MetaModule), m) && MetaPanel.SlotOf((MetaModule)m) == MetaSlot.Main ? 120 : 80;
        /// <summary>旧い遺物 1 個 / 強化素材 1 個の換算 (作者の決定)。</summary>
        public const int LegacyRelicRefund = 10, LegacyMaterialRefund = 5;

        /// <summary>旧い整備パネル (基礎整備・作戦の枠・モジュール) に払ったトークンを全額返し、 遺物を 1 個 10・素材を 1 個 5 トークンに換えて、 旧い欄を消す。
        /// 冪等 (返したら欄が 0 になる)。 呼び出しは Manager.Load 直後。</summary>
        public void MigrateToBoardIfNeeded()
        {
            int refund = 0;
            for (int n = 0; n < UnityEngine.Mathf.Clamp(baseTier, 0, LegacyBaseTierCost.Length); n++) refund += LegacyBaseTierCost[n];
            for (int n = 0; n < UnityEngine.Mathf.Clamp(slotsOpen, 0, LegacySlotOpenCost.Length); n++) refund += LegacySlotOpenCost[n];
            if (unlockedModules != null) foreach (int m in unlockedModules) refund += LegacyModuleCost(m);
            int relics = ownedRelics?.Count ?? 0;
            refund += relics * LegacyRelicRefund + System.Math.Max(0, relicMaterial) * LegacyMaterialRefund;
            if (refund > 0)
                UnityEngine.Debug.Log($"[MetaMigrate] 整備盤へ移行: 基礎{baseTier}段・枠{slotsOpen}・モジュール{unlockedModules?.Count ?? 0}・遺物{relics}個・素材{relicMaterial} → {refund} トークンを返却");
            tokens += refund;
            baseTier = 0; slotsOpen = 0; unlockedModules = new List<int>();
            ownedRelics = new List<LegacyRelicStub>(); relicMaterial = 0;
        }
    }
}
