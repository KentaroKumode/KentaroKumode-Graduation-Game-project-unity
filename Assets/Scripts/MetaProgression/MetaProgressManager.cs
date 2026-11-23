using UnityEngine;

namespace MetaProgression
{
    /// <summary>
    /// メタ進行のシングルトン。State の保持・購入処理・保存/ロードを担当。
    ///
    /// 整備パネル (2026-11-20 刷新・MetaPanel): トークンで基礎整備の段・作戦の枠・モジュールを買い切り、 モジュールは無料で挿し替える。
    ///   ・旧 58 段トラックの API (TryPurchase / NextLevel ほか) は 2026-11-13 に削除 (#132・読んでいたのは旧 SkillTreeView だけ)。
    /// </summary>
    public class MetaProgressManager : MonoBehaviour
    {
        private const string PrefsKey = "MetaProgressState_v1";

        private static MetaProgressManager _instance;
        private static bool _shuttingDown;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _shuttingDown = false; _instance = null; }

        public static MetaProgressManager Instance
        {
            get
            {
                if (_shuttingDown) return null;
                if (_instance == null)
                {
                    var go = new GameObject("MetaProgressManager");
                    _instance = go.AddComponent<MetaProgressManager>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        public MetaProgressState State { get; private set; }

        /// <summary>今ある管理 (作らない・EditMode のテストで GameObject を作らないため)。</summary>
        public static MetaProgressManager Existing => _shuttingDown ? null : _instance;

        public System.Action OnStateChanged;

        void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            Load();
        }

        void OnApplicationQuit() { _shuttingDown = true; Save(); }
        void OnDestroy() { if (_instance == this) _instance = null; }

        // ============================================================
        //  保存・ロード
        // ============================================================

        /// <summary>PlayerPrefs を <see cref="BackedPrefs"/> の置き場として渡す口。</summary>
        private sealed class PlayerPrefsStore : IPrefsStore
        {
            public static readonly PlayerPrefsStore I = new PlayerPrefsStore();
            public bool HasKey(string key) => PlayerPrefs.HasKey(key);
            public string GetString(string key) => PlayerPrefs.GetString(key, "");
            public void SetString(string key, string value) => PlayerPrefs.SetString(key, value);
            public void DeleteKey(string key) => PlayerPrefs.DeleteKey(key);
        }

        /// <summary>記録として読める JSON か (JsonUtility が例外を出さず、 null を返さない)。</summary>
        private static bool IsValidStateJson(string json)
        {
            try { return JsonUtility.FromJson<MetaProgressState>(json) != null; }
            catch { return false; }
        }

        /// <summary>直近の読み込みがどの経路を通ったか (G-25)。 画面への知らせは未実装 ── 出すならここを読む (Backup / CorruptReset の時)。</summary>
        public BackedPrefs.LoadSource LastLoadSource { get; private set; } = BackedPrefs.LoadSource.Empty;

        public void Save()
        {
            if (State == null) return;
            // 並列ワーカーは保存しない (PlayerPrefs は全プロセスで共有され、 次の測定へ状態が持ち込まれる・GameHooks の説明)
            if (GameLoop.GameHooks.MetaPersistenceDisabled) return;
            if (GameLoop.SaveGuard.Suppressed) return;      // 実機の確認中は書かない (手元の State はそのまま)
            var json = JsonUtility.ToJson(State);
#if UNITY_EDITOR
            // エディタは起動のたびに空で始めて読み込まない (Load) ので、 控えは作らない (エディタの測定の動きを旧いまま保つ)。
            PlayerPrefs.SetString(PrefsKey, json);
#else
            // 1 つ前の値を控え (鍵.bak) に残してから書く (G-25・BackedPrefs)。
            BackedPrefs.Save(PlayerPrefsStore.I, PrefsKey, json, IsValidStateJson);
#endif
            PlayerPrefs.Save();
        }

        public void Load()
        {
#if UNITY_EDITOR
            // エディタ起動時はメタ進行を毎回初期状態へリセット (UI 開発中の便宜)。
            GameLoop.SaveGuard.DeleteKey(PrefsKey);
            State = new MetaProgressState();
            State.tokens = 3000;
            State.EnsurePanelInitialized();
            return;
#else
            // 並列ワーカーは PlayerPrefs を読まず、 まっさらな状態から始める (エディタ実行と同じ・GameHooks.MetaPersistenceDisabled)
            if (GameLoop.GameHooks.MetaPersistenceDisabled)
            {
                State = new MetaProgressState();
                State.EnsurePanelInitialized();
                return;
            }
            // 鍵が無い = 初回 (空で始める・控えは見ない)。 鍵があるのに読めない = 壊れた値を退避して控えから・控えも駄目なら空で (G-25)。
            var src = BackedPrefs.Load(PlayerPrefsStore.I, PrefsKey, IsValidStateJson, out var json, out var note);
            LastLoadSource = src;
            State = null;
            if (json != null)
            {
                try { State = JsonUtility.FromJson<MetaProgressState>(json); }
                catch (System.Exception e) { Debug.LogError($"[MetaProgress] 確かめた値が読めない (起きないはず): {e.Message}"); }
            }
            if (State == null) State = new MetaProgressState();
            if (src == BackedPrefs.LoadSource.Backup) Debug.LogWarning($"[MetaProgress] {note}");
            else if (src == BackedPrefs.LoadSource.CorruptReset) Debug.LogError($"[MetaProgress] {note}");
            if (src == BackedPrefs.LoadSource.Backup || src == BackedPrefs.LoadSource.CorruptReset) GameLoop.SaveGuard.Save();   // 退避した値をディスクへ (SaveGuard の間は書かない)
            State.MigrateFromLegacyTrackIfNeeded();
            State.MigrateFromPointPanelIfNeeded();   // 旧い 24 点の整備パネルの分を全額返す (2026-11-20)
            State.EnsurePanelInitialized();
#endif
        }

        public void ResetAll()
        {
            State = new MetaProgressState();
            // 控えも消す (残すと、 やり直した後に古い値が戻る道ができる)。 退避した壊れた値 (.corrupt) は残す。
            if (!GameLoop.SaveGuard.Suppressed) BackedPrefs.DeleteAll(PlayerPrefsStore.I, PrefsKey);
            OnStateChanged?.Invoke();
        }

        // ============================================================
        //  トークン
        // ============================================================

        public void AddTokens(int amount)
        {
            if (State == null || amount <= 0) return;
            State.tokens += amount;
            OnStateChanged?.Invoke();
            Save();
        }

        // ============================================================
        //  整備パネル (2026-11-20 刷新・docs/GAME.md §15-1)
        // ============================================================

        /// <summary>次に買う基礎整備の段のトークン。 全部買っていれば int.MaxValue。</summary>
        public int NextBaseTierCost
            => State == null || State.baseTier >= MetaPanel.BaseTierCount ? int.MaxValue : MetaPanel.BaseTierCost[State.baseTier];

        /// <summary>次の作戦の枠を開くトークン。 全部開いていれば int.MaxValue。</summary>
        public int NextSlotCost
            => State == null || State.slotsOpen >= MetaPanel.SlotOpenCost.Length ? int.MaxValue : MetaPanel.SlotOpenCost[State.slotsOpen];

        /// <summary>基礎整備の次の段を買う (段は順に買う)。</summary>
        public bool TryBuyBaseTier() => TrySpend(NextBaseTierCost, () => State.baseTier++, "基礎整備の段");

        /// <summary>作戦の次の枠を開く (開く順は MetaSlot の並び)。</summary>
        public bool TryOpenSlot() => TrySpend(NextSlotCost, () => State.slotsOpen++, "作戦の枠");

        /// <summary>モジュールを解禁する (買い切り)。</summary>
        public bool TryUnlockModule(MetaModule m)
        {
            if (State == null || State.IsUnlocked(m)) return false;
            return TrySpend(MetaPanel.ModuleCost(m), () => State.unlockedModules.Add((int)m), "モジュール " + m);
        }

        /// <summary>解禁済みのモジュールを、 その枠へ挿す (同じ物を押すと外す・無料)。 枠が閉じている・未解禁なら false。</summary>
        public bool TryToggleEquip(MetaModule m)
        {
            if (State == null || !State.IsUnlocked(m)) return false;
            var slot = MetaPanel.SlotOf(m);
            if (!State.IsSlotOpen(slot)) return false;
            if (State.Equipped(slot) == m) State.Unequip(slot); else State.ForceEquip(m);
            Changed();
            return true;
        }

        /// <summary>「基礎整備を切る」(縛り) を切り替える。 買った段は残る。</summary>
        public void ToggleBaseDisabled()
        {
            if (State == null) return;
            State.baseDisabled = !State.baseDisabled;
            Changed();
        }

        // ============================================================
        //  整備盤 (2026-11-23・docs/GAME.md §15-1・MetaBoard)
        // ============================================================

        /// <summary>マスを開くトークン (開けられなければ −1・<see cref="MetaBoard.OpenCost"/>)。</summary>
        public int CellOpenCost(int cell) => State == null ? -1 : MetaBoard.OpenCost(State.boardOpenMask, cell);

        /// <summary>マスを開く (中心 → 辺 → 角の順・開けた個数で値段が上がる)。</summary>
        public bool TryOpenCell(int cell)
        {
            int cost = CellOpenCost(cell);
            if (cost < 0) return false;
            return TrySpendBoard(cost, () => State.boardOpenMask |= 1 << cell, $"盤のマス {cell}");
        }

        /// <summary>パネルを買う (常設の物だけ・実績のパネルは実績で手に入る)。</summary>
        public bool TryBuyPanel(string id)
        {
            var d = MetaBoard.Get(id);
            if (State == null || d == null || d.FromAchievement || State.OwnsPanel(id)) return false;
            return TrySpendBoard(d.cost, () => State.GrantPanel(id), $"パネル {d.name}");
        }

        /// <summary>持っているパネルを置く・置き直す (無料)。</summary>
        public bool TryPlacePanel(BoardPlacement p)
        {
            if (State == null || !State.PlacePanel(p)) return false;
            Changed();
            return true;
        }

        /// <summary>パネルを盤から外す (無料)。</summary>
        public void RemovePanel(string id)
        {
            if (State == null) return;
            State.RemovePanel(id);
            Changed();
        }

        /// <summary>実績を解いた時: その実績のパネルを持たせる (AchievementService.Unlock が呼ぶ)。</summary>
        public void GrantAchievementPanels(string achievementId)
        {
            if (State == null) return;
            bool any = false;
            foreach (var d in MetaBoard.ForAchievement(achievementId))
                if (!State.OwnsPanel(d.id)) { State.GrantPanel(d.id); any = true; Debug.Log($"[MetaProgress] 実績 {achievementId} でパネル〈{d.name}〉を手に入れた"); }
            if (any) Changed();
        }

        private bool TrySpendBoard(int cost, System.Action apply, string what)
        {
            if (State == null || cost < 0 || State.tokens < cost) return false;
            State.tokens -= cost;
            apply();
            State.BoardChanged();
            Changed();
            Achievements.AchievementService.NoteBoardChanged(State);   // 盤の実績
            Debug.Log($"[MetaProgress] {what} を買った (-{cost}): {State.DescribeBoard()}");
            return true;
        }

        private bool TrySpend(int cost, System.Action apply, string what)
        {
            if (State == null || cost == int.MaxValue || State.tokens < cost) return false;
            State.tokens -= cost;
            apply();
            State.EnsurePanelInitialized();
            Changed();
            Achievements.AchievementService.NotePanelAssigned(State);   // 実績〈整備完了〉
            Debug.Log($"[MetaProgress] {what} を買った (-{cost}): {State.DescribePanel()}");
            return true;
        }

        private void Changed() { OnStateChanged?.Invoke(); Save(); }

        // ============================================================
        //  挑戦 (docs/GAME.md §23-25)
        // ============================================================

        // [削除 2026-11-12] SetChallengeMax ── 挑戦の難易度を最大 / 0 にする口。 呼ぶ所が 0 件だった
        //   (AutoRunner は challengeDifficulty を直接書く)。
    }
}
