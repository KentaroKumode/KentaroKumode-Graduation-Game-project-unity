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

        // === Achievements (docs/achievements.md) ===
        // Parallel lists keep JsonUtility compatibility. IDs are stable strings.
        public List<string> unlockedAchievementIds = new List<string>();
        public List<long> achievementUnlockUtcTicks = new List<long>();
        public int achievementCombatWins;
        public int achievementRoleMask;

        /// <summary>挑戦デバフの構成 (docs/GAME.md §15-2)。 2026-07-29 に旧 activeDebuffs
        /// (MetaDebuffLevel Lv1-10 の独立トグル) を**破棄して**置換した。 旧セーブの
        /// activeDebuffs は JsonUtility が黙って捨てるので、 マイグレーションは不要。</summary>
        public ChallengeLoadout challenge = new ChallengeLoadout();

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

        /// <summary>所持している遺物 (§15-5)。 ラン終了ごとに 1 個追加される。
        /// JsonUtility は Dictionary 非対応だが List&lt;[Serializable]&gt; は通る。</summary>
        public List<Relics.RolledRelic> relics = new List<Relics.RolledRelic>();

        /// <summary>持ち込む遺物の index。 -1 = 装備なし。 **持ち込みは 1 個・コストなし**。</summary>
        public int equippedRelicIndex = -1;

        /// <summary>装備中の遺物。 未装備・破損セーブなら null。</summary>
        public Relics.RolledRelic EquippedRelic
        {
            get
            {
                if (relics == null) return null;
                if (equippedRelicIndex < 0 || equippedRelicIndex >= relics.Count) return null;
                var r = relics[equippedRelicIndex];
                // enum の並べ替え事故やセーブ破損をここで止める (効果を一切適用しない)
                return (r != null && r.IsValid()) ? r : null;
            }
        }

        /// <summary>遺物を 1 個追加する。 初めての 1 個は自動装備する。</summary>
        public void AddRelic(Relics.RolledRelic relic)
        {
            if (relic == null || !relic.IsValid()) return;
            if (relics == null) relics = new List<Relics.RolledRelic>();
            relics.Add(relic);
            if (equippedRelicIndex < 0) equippedRelicIndex = relics.Count - 1;
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
        //  挑戦デバフ
        // ============================================================

        /// <summary>解決済み構成のキャッシュ。 挑戦設定はラン中に変わらないので、
        /// 変更時だけ捨てて作り直す (<see cref="InvalidateChallenge"/>)。</summary>
        [System.NonSerialized] private ResolvedChallenge _resolved;

        /// <summary>解決済みの挑戦構成。 各システムはこれだけを見る。</summary>
        public ResolvedChallenge Challenge
        {
            get
            {
                if (_resolved == null)
                {
                    if (challenge == null) challenge = new ChallengeLoadout();
                    challenge.Sanitize();
                    _resolved = ChallengeResolver.Build(challenge);
                }
                return _resolved;
            }
        }

        /// <summary>挑戦構成を書き換えたあとに呼ぶ。 次回参照時に再解決される。</summary>
        public void InvalidateChallenge() => _resolved = null;

        /// <summary>現在の挑戦スコア (0..100)。</summary>
        public int ChallengeScore => Challenge.Score;

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
