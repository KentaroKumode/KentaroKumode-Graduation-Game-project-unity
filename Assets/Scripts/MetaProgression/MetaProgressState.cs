using System.Collections.Generic;

namespace MetaProgression
{
    /// <summary>
    /// プレイヤーのメタ進行状態。PlayerPrefs に JSON で保存される。
    ///
    /// 2026-07-25 v6 移行:
    ///   ・整備パネル (docs/GAME.md §15-1) へ全面移行
    ///   ・購入モデル: ポイント (Max=36) → トラック (MetaPanelKind) にランク配点
    ///   ・旧 currentLevel (0-58 一直線トラック) は Migrate() で全額返却
    ///
    /// 集計値 (hpBonus/goldBonus 等) は Applicator が直接 MetaPanel.*(rank) で再計算するため、
    /// State には rank 配列 (panelRanks) のみを持つ (Recalculate は撤廃)。
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

        // === Achievements (docs/achievements.md) ===
        // Parallel lists keep JsonUtility compatibility. IDs are stable strings.
        public List<string> unlockedAchievementIds = new List<string>();
        public List<long> achievementUnlockUtcTicks = new List<long>();
        public int achievementCombatWins;
        public int achievementRoleMask;

        // (旧: 挑戦デバフの配点 `challenge` (§15-2) は 2026-11-10 に撤去 (#9)。 JsonUtility は未知の欄を黙って捨てる)

        /// <summary>挑戦カード (docs/GAME.md §23-25) の難易度 (0..10 = ラン中にカードを引く回数)。 整備の「挑戦」の目盛りで選ぶ。</summary>
        public int challengeDifficulty;
        /// <summary>5 層を踏破した最高の難易度。 選べるのはこの +1 まで。</summary>
        public int challengeBest;

        // 双蛇のダイス〈永劫〉: 勝利した戦闘数をランを跨いで永続蓄積。パネルとは独立。
        public int eternalStacks;

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

        // === レガシー (旧 58 段トラック時代の遺物・マイグレーション判定にのみ使用) ===
        /// <summary>旧 currentLevel。> 0 なら未マイグレーション状態 → Migrate() でトークン返却して 0 化。</summary>
        public int currentLevel;

        // === 整備パネル v6: ポイント + 配点 ===
        /// <summary>購入済みポイント数 (0..MetaPanel.MaxPoints=36)。</summary>
        public int panelPointsPurchased;

        /// <summary>各トラックの現在ランク。 index = (int)MetaPanelKind。配列長 = enum 数。
        /// JsonUtility は enum-key Dictionary 非対応のため int[] で保持。</summary>
        public int[] panelRanks;

        public MetaProgressState()
        {
            EnsurePanelInitialized();
        }

        /// <summary>panelRanks を必要に応じて初期化 (JsonUtility 復元後の欠損対応も兼ねる)。</summary>
        public void EnsurePanelInitialized()
        {
            int n = System.Enum.GetValues(typeof(MetaPanelKind)).Length;
            if (panelRanks == null || panelRanks.Length != n)
            {
                var old = panelRanks;
                panelRanks = new int[n];
                if (old != null)
                    for (int i = 0; i < System.Math.Min(old.Length, n); i++) panelRanks[i] = old[i];
            }
        }

        // ============================================================
        //  (挑戦の配点の解決は 2026-11-10 に撤去・難易度は challengeDifficulty)
        // ============================================================

        // ============================================================
        //  パネル API
        // ============================================================

        public int GetRank(MetaPanelKind kind)
        {
            EnsurePanelInitialized();
            int i = (int)kind;
            return (i >= 0 && i < panelRanks.Length) ? panelRanks[i] : 0;
        }

        public void SetRank(MetaPanelKind kind, int rank)
        {
            EnsurePanelInitialized();
            int i = (int)kind;
            if (i < 0 || i >= panelRanks.Length) return;
            panelRanks[i] = UnityEngine.Mathf.Clamp(rank, 0, kind.MaxRank());
        }

        /// <summary>現在の総配点 <b>pt</b>。 2026-09-10: 段数合計ではなく
        /// <see cref="MetaPanel.RankCost"/> による<b>重み付き</b>合計
        /// (宣言系 = 3pt/段)。 段数が欲しい場合は panelRanks を直接見ること。</summary>
        public int TotalAssignedPoints()
        {
            EnsurePanelInitialized();
            var kinds = (MetaPanelKind[])System.Enum.GetValues(typeof(MetaPanelKind));
            int sum = 0;
            for (int i = 0; i < panelRanks.Length && i < kinds.Length; i++)
                sum += panelRanks[i] * MetaPanel.RankCost(kinds[i]);
            return sum;
        }

        /// <summary>未配分ポイント数 (purchased - assigned)。</summary>
        public int UnassignedPoints() => System.Math.Max(0, panelPointsPurchased - TotalAssignedPoints());

        /// <summary>次に買う (n+1 個目) のコスト。上限到達なら int.MaxValue。</summary>
        public int NextPointCost()
            => panelPointsPurchased >= MetaPanel.MaxPoints ? int.MaxValue : MetaPanel.PointCost(panelPointsPurchased + 1);

        // ============================================================
        //  マイグレーション (旧 58 段 → 配点方式)
        // ============================================================

        /// <summary>旧 currentLevel > 0 のセーブを検出したら、消費トークンを全額返却して panel を初期状態に。
        /// 呼び出しは Manager.Load 直後 (一度だけ)。 冪等。</summary>
        public void MigrateFromLegacyTrackIfNeeded()
        {
            if (currentLevel <= 0) return;
            int refund = 0;
            for (int lv = 1; lv <= currentLevel; lv++)
                refund += MetaBuffTrack.CalcCost(lv);
            tokens += refund;
            currentLevel = 0;
            panelPointsPurchased = 0;
            EnsurePanelInitialized();
            for (int i = 0; i < panelRanks.Length; i++) panelRanks[i] = 0;
            UnityEngine.Debug.Log($"[MetaMigrate] 旧 58 段トラックから配点方式へ移行: {refund} トークン返却");
        }
    }
}
