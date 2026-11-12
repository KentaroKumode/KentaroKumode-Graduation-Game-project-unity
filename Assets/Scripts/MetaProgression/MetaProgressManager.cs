using UnityEngine;

namespace MetaProgression
{
    /// <summary>
    /// メタ進行のシングルトン。State の保持・購入処理・保存/ロードを担当。
    ///
    /// 2026-07-25 v6 移行:
    ///   ・購入モデル = 整備パネル (MetaPanel)。
    ///   ・トークン → ポイント購入 → トラックへランク配点。 ポイント上限 36。
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
            PlayerPrefs.DeleteKey(PrefsKey);
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
            if (src == BackedPrefs.LoadSource.Backup || src == BackedPrefs.LoadSource.CorruptReset) PlayerPrefs.Save();   // 退避した値をディスクへ
            State.EnsurePanelInitialized();
            State.MigrateFromLegacyTrackIfNeeded();
            State.ClampRanksToMax();   // #113: 枠から外したトラックの段が古いセーブから残って効くのを防ぐ
#endif
        }

        public void ResetAll()
        {
            State = new MetaProgressState();
            // 控えも消す (残すと、 やり直した後に古い値が戻る道ができる)。 退避した壊れた値 (.corrupt) は残す。
            BackedPrefs.DeleteAll(PlayerPrefsStore.I, PrefsKey);
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
        //  ポイント購入 (整備パネル v6)
        // ============================================================

        public int NextPointCost => State?.NextPointCost() ?? int.MaxValue;
        public bool IsPointCapReached => (State?.panelPointsPurchased ?? 0) >= MetaPanel.MaxPoints;

        public bool CanPurchasePoint()
        {
            if (State == null || IsPointCapReached) return false;
            return State.tokens >= NextPointCost;
        }

        /// <summary>ポイントを 1 個購入 (未配分残に加算)。 割り振りは AssignPoint。</summary>
        public bool TryPurchasePoint()
        {
            if (!CanPurchasePoint()) return false;
            int cost = NextPointCost;
            State.tokens -= cost;
            State.panelPointsPurchased++;
            OnStateChanged?.Invoke();
            Save();
            Debug.Log($"[MetaProgress] ポイント購入: {State.panelPointsPurchased}/{MetaPanel.MaxPoints} (-{cost})");
            return true;
        }

        // ============================================================
        //  配点 (拠点でいつでも無料・無制限)
        // ============================================================

        /// <summary>指定トラックに 1 ランク配点 (未配分ポイントが必要)。 max 到達で false。</summary>
        public bool TryAssignRank(MetaPanelKind kind)
        {
            if (State == null) return false;
            // 2026-09-10: 宣言系は 3pt/段 なので、 残 pt が単価に足りるかで判定する。
            if (State.UnassignedPoints() < MetaPanel.RankCost(kind)) return false;
            int cur = State.GetRank(kind);
            if (cur >= kind.MaxRank()) return false;
            State.SetRank(kind, cur + 1);
            OnStateChanged?.Invoke();
            Save();
            return true;
        }

        /// <summary>指定トラックから 1 ランク払い戻し (未配分残に戻る・拠点無料)。</summary>
        public bool TryRefundRank(MetaPanelKind kind)
        {
            if (State == null) return false;
            int cur = State.GetRank(kind);
            if (cur <= 0) return false;
            State.SetRank(kind, cur - 1);
            OnStateChanged?.Invoke();
            Save();
            return true;
        }

        /// <summary>全トラックのランクを 0 に戻し、ポイントを未配分に返す (拠点リスペック)。</summary>
        public void RespecAllRanks()
        {
            if (State == null) return;
            foreach (MetaPanelKind k in System.Enum.GetValues(typeof(MetaPanelKind)))
                State.SetRank(k, 0);
            OnStateChanged?.Invoke();
            Save();
        }

        // ============================================================
        //  挑戦 (docs/GAME.md §23-25)
        // ============================================================

        // [削除 2026-11-12] SetChallengeMax ── 挑戦の難易度を最大 / 0 にする口。 呼ぶ所が 0 件だった
        //   (AutoRunner は challengeDifficulty を直接書く)。
    }
}
