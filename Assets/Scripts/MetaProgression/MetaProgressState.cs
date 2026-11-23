using System.Collections.Generic;

namespace MetaProgression
{
    /// <summary>
    /// プレイヤーのメタ進行状態。PlayerPrefs に JSON で保存される。
    ///
    /// 整備パネル (docs/GAME.md §15-1・2026-11-20 刷新): 基礎整備の段・開いた枠・解禁したモジュール・挿しているモジュールを持つ。
    /// 効果の数値は持たない (MetaBuffApplicator が MetaPanel の値から都度出す)。
    /// 旧いセーブ: 58 段トラック (currentLevel) と 24 点の配点式 (panelPointsPurchased) は、 払ったトークンを全額返す。
    /// </summary>
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

        /// <summary>所持している遺物 (§15-5・2026-11-10 再編)。 旧仕様の `relics` / `equippedRelicIndex` は捨てた (JsonUtility は未知の欄を黙って無視する)。
        /// 所持の上限は無い。 JsonUtility は Dictionary 非対応だが List&lt;[Serializable]&gt; は通る。</summary>
        public List<Relics.Relic> ownedRelics = new List<Relics.Relic>();

        /// <summary>部位ごとに持ち込む遺物の index (冠 / 環 / 符)。 -1 = 空き。 持ち込みはコストなし。</summary>
        public int[] equippedRelics = { -1, -1, -1 };

        /// <summary>強化素材 (遺物 1 個の分解 = 1)。 強化 1 段に 1 使う。</summary>
        public int relicMaterial;

        /// <summary>指名したセット (-1 = 指名なし)。 ランの終わりに落ちる遺物の半分がこのセットになる。</summary>
        public int relicTargetSet = -1;

        private void EnsureRelicSlots()
        {
            if (ownedRelics == null) ownedRelics = new List<Relics.Relic>();
            if (equippedRelics == null || equippedRelics.Length != Relics.RelicCatalog.SlotCount) equippedRelics = new[] { -1, -1, -1 };
        }

        /// <summary>部位に持ち込んでいる遺物。 空き・破損・部位違いなら null。</summary>
        public Relics.Relic EquippedRelicAt(Relics.RelicSlot slot)
        {
            EnsureRelicSlots();
            int i = equippedRelics[(int)slot];
            if (i < 0 || i >= ownedRelics.Count) return null;
            var r = ownedRelics[i];
            return r != null && r.IsValid() && r.Slot == slot ? r : null;
        }

        /// <summary>持ち込んでいる遺物 (0〜3 個)。</summary>
        public List<Relics.Relic> EquippedRelics()
        {
            var list = new List<Relics.Relic>(3);
            for (int k = 0; k < Relics.RelicCatalog.SlotCount; k++) { var r = EquippedRelicAt((Relics.RelicSlot)k); if (r != null) list.Add(r); }
            return list;
        }

        /// <summary>遺物を 1 個追加する。 その部位が空いていれば持ち込みにも入れる。</summary>
        public void AddRelic(Relics.Relic relic)
        {
            if (relic == null || !relic.IsValid()) return;
            EnsureRelicSlots();
            ownedRelics.Add(relic);
            if (EquippedRelicAt(relic.Slot) == null) equippedRelics[relic.slot] = ownedRelics.Count - 1;
        }

        /// <summary>持ち込みを切り替える (同じ遺物を押すと外す)。</summary>
        public void ToggleEquip(int index)
        {
            EnsureRelicSlots();
            if (index < 0 || index >= ownedRelics.Count || ownedRelics[index] == null) return;
            int s = ownedRelics[index].slot;
            equippedRelics[s] = equippedRelics[s] == index ? -1 : index;
        }

        /// <summary>遺物を分解する (素材 +1)。 持ち込みの index をずらす。</summary>
        public bool DismantleRelic(int index)
        {
            EnsureRelicSlots();
            if (index < 0 || index >= ownedRelics.Count) return false;
            ownedRelics.RemoveAt(index);
            relicMaterial++;
            for (int k = 0; k < equippedRelics.Length; k++)
            {
                if (equippedRelics[k] == index) equippedRelics[k] = -1;
                else if (equippedRelics[k] > index) equippedRelics[k]--;
            }
            return true;
        }

        /// <summary>持ち込んでいない★maxStars 以下の遺物を全部分解する。 分解した数を返す。</summary>
        public int DismantleUpToStars(int maxStars)
        {
            EnsureRelicSlots();
            int n = 0;
            for (int i = ownedRelics.Count - 1; i >= 0; i--)
            {
                var r = ownedRelics[i];
                if (r == null || r.stars > maxStars) continue;
                if (System.Array.IndexOf(equippedRelics, i) >= 0) continue;
                if (DismantleRelic(i)) n++;
            }
            return n;
        }

        /// <summary>持ち込みを丸ごと差し替える (BOT・測定用)。 所持品も渡した遺物だけにする。</summary>
        public void SetRelicLoadout(List<Relics.Relic> relics)
        {
            ownedRelics = new List<Relics.Relic>();
            equippedRelics = new[] { -1, -1, -1 };
            if (relics == null) return;
            foreach (var r in relics) AddRelic(r);
        }

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

        // === 整備パネル (2026-11-20 刷新・docs/GAME.md §15-1): 基礎整備 + 作戦の枠 ===
        /// <summary>買った基礎整備の段 (0..<see cref="MetaPanel.BaseTierCount"/>)。 順に買う。</summary>
        public int baseTier;
        /// <summary>「基礎整備を切る」(縛り)。 立っている間は基礎整備の段の効果が 0 (買った段は残る)。</summary>
        public bool baseDisabled;
        /// <summary>開いた作戦の枠の数 (0..3)。 開く順は <see cref="MetaSlot"/> の並び。</summary>
        public int slotsOpen;
        /// <summary>解禁したモジュール (<see cref="MetaModule"/> の int)。</summary>
        public List<int> unlockedModules = new List<int>();
        /// <summary>枠ごとに挿しているモジュール (index = <see cref="MetaSlot"/>・-1 = 空き)。</summary>
        public int[] equippedModules = { -1, -1, -1 };

        // === 旧い整備パネル (24 点の配点式・2026-11-20 撤去)。 旧いセーブを読んで返金するためだけに残す ===
        /// <summary>[旧] 買った点の数。 > 0 なら <see cref="MigrateFromPointPanelIfNeeded"/> が全額返して 0 にする。</summary>
        public int panelPointsPurchased;
        /// <summary>[旧] トラックごとの段。 返金は点の数だけで決まるので読まない (移行で消す)。</summary>
        public int[] panelRanks;

        public MetaProgressState()
        {
            EnsurePanelInitialized();
        }

        /// <summary>整備パネルの欄を必要に応じて初期化・丸める (JsonUtility 復元後の欠損・範囲外の値の対応も兼ねる)。 冪等。</summary>
        public void EnsurePanelInitialized()
        {
            if (unlockedModules == null) unlockedModules = new List<int>();
            int slots = System.Enum.GetValues(typeof(MetaSlot)).Length;
            if (equippedModules == null || equippedModules.Length != slots)
            {
                var old = equippedModules;
                equippedModules = new int[slots];
                for (int i = 0; i < slots; i++) equippedModules[i] = old != null && i < old.Length ? old[i] : -1;
            }
            baseTier = UnityEngine.Mathf.Clamp(baseTier, 0, MetaPanel.BaseTierCount);
            slotsOpen = UnityEngine.Mathf.Clamp(slotsOpen, 0, slots);
            // 挿してよいのは: 解禁済み・その枠のモジュール。 それ以外 (壊れた値・旧い番号) は外す
            for (int i = 0; i < slots; i++)
            {
                int m = equippedModules[i];
                if (m < 0) continue;
                if (!System.Enum.IsDefined(typeof(MetaModule), m) || !unlockedModules.Contains(m) || (int)MetaPanel.SlotOf((MetaModule)m) != i)
                    equippedModules[i] = -1;
            }
        }

        // ============================================================
        //  パネル API
        // ============================================================

        public bool IsUnlocked(MetaModule m) => unlockedModules != null && unlockedModules.Contains((int)m);

        public bool IsSlotOpen(MetaSlot slot) => (int)slot < slotsOpen;

        /// <summary>枠に挿しているモジュール (枠が閉じていれば null)。</summary>
        public MetaModule? Equipped(MetaSlot slot)
        {
            // 戦闘中に何度も呼ばれる getter の下なので、 丸め (EnsurePanelInitialized) は通さない (読み込み・書き込みの側で済ませている)
            if (equippedModules == null || (int)slot >= equippedModules.Length) return null;
            int m = equippedModules[(int)slot];
            return IsSlotOpen(slot) && m >= 0 ? (MetaModule)m : (MetaModule?)null;
        }

        /// <summary>そのモジュールが効いているか (開いた枠に挿している)。</summary>
        public bool IsActive(MetaModule m) => Equipped(MetaPanel.SlotOf(m)) == m;

        /// <summary>効いている基礎整備の段 (「基礎整備を切る」の間は 0)。</summary>
        public int EffectiveBaseTier => baseDisabled ? 0 : UnityEngine.Mathf.Clamp(baseTier, 0, MetaPanel.BaseTierCount);

        /// <summary>モジュールを解禁済みにして挿す (測定・移行用・トークンは払わない)。 枠は開いていなくても挿しておく (開いた時に効く)。</summary>
        public void ForceEquip(MetaModule m)
        {
            EnsurePanelInitialized();
            if (!IsUnlocked(m)) unlockedModules.Add((int)m);
            equippedModules[(int)MetaPanel.SlotOf(m)] = (int)m;
        }

        /// <summary>枠の中身を外す。</summary>
        public void Unequip(MetaSlot slot) { EnsurePanelInitialized(); equippedModules[(int)slot] = -1; }

        /// <summary>今の作戦の構成の短い文 (ログ・実効状態・セーブの指紋)。 例 "基礎3/6 枠2 主方針=LastStand 補助=AttackTune 種火=-"。</summary>
        public string DescribePanel()
        {
            EnsurePanelInitialized();
            var sb = new System.Text.StringBuilder();
            sb.Append("基礎").Append(baseTier).Append('/').Append(MetaPanel.BaseTierCount).Append(baseDisabled ? "(切)" : "").Append(" 枠").Append(slotsOpen);
            foreach (MetaSlot sl in System.Enum.GetValues(typeof(MetaSlot)))
            {
                int m = equippedModules[(int)sl];
                sb.Append(' ').Append(sl).Append('=').Append(m >= 0 ? ((MetaModule)m).ToString() : "-");
                if (m >= 0 && !IsSlotOpen(sl)) sb.Append("(閉)");
            }
            return sb.ToString();
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
    }
}
