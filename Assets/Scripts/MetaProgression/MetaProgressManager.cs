using UnityEngine;

namespace MetaProgression
{
    /// <summary>
    /// メタ進行のシングルトン。State の保持・購入処理・保存/ロードを担当。
    ///
    /// 2026-07-25 v6 移行:
    ///   ・購入モデル = 整備パネル (MetaPanel)。
    ///   ・トークン → ポイント購入 → トラックへランク配点。 ポイント上限 36。
    ///   ・旧 TryPurchase (58 段トラック) は Deprecated (noop)。 呼び出し側は AutoRunner の
    ///     MaxAllForTesting と ClassSelect UI 相当のみ、後者は v6 UI 実装時に置換予定。
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

        public void Save()
        {
            if (State == null) return;
            var json = JsonUtility.ToJson(State);
            PlayerPrefs.SetString(PrefsKey, json);
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
            string json = PlayerPrefs.GetString(PrefsKey, "");
            if (string.IsNullOrEmpty(json))
            {
                State = new MetaProgressState();
            }
            else
            {
                try { State = JsonUtility.FromJson<MetaProgressState>(json) ?? new MetaProgressState(); }
                catch { State = new MetaProgressState(); }
            }
            State.EnsurePanelInitialized();
            State.MigrateFromLegacyTrackIfNeeded();
#endif
        }

        public void ResetAll()
        {
            State = new MetaProgressState();
            PlayerPrefs.DeleteKey(PrefsKey);
            OnStateChanged?.Invoke();
        }

        /// <summary>整備パネルの全トラックを max 状態にする (bot / AutoRunner 用)。
        /// PlayerPrefs には保存しない (テスト専用)。</summary>
        public void MaxAllForTesting()
        {
            State = new MetaProgressState();
            State.EnsurePanelInitialized();
            // 上限までポイント購入・全トラックを max に
            State.panelPointsPurchased = MetaPanel.MaxPoints;
            foreach (MetaPanelKind k in System.Enum.GetValues(typeof(MetaPanelKind)))
                State.SetRank(k, k.MaxRank());
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
        //  挑戦デバフ (docs/GAME.md §15-2)
        // ============================================================

        /// <summary>軸の Tier を設定する。 tier = 0 で解除。 軸内排他は Loadout 側が保証する。
        /// 存在しない Tier を渡すと false を返して何もしない。</summary>
        public bool SetChallengeTier(ChallengeAxis axis, int tier)
        {
            if (State?.challenge == null) return false;
            if (State.challenge.GetTier(axis) == tier) return false;
            if (!State.challenge.SetTier(axis, tier)) return false;
            State.InvalidateChallenge();
            OnStateChanged?.Invoke();
            Save();
            return true;
        }

        /// <summary>T4 の ON/OFF。 選択するとカテゴリ全軸が最高 Tier へ強制される (解決時)。</summary>
        public bool SetChallengeT4(ChallengeT4 v, bool enabled)
        {
            if (State?.challenge == null) return false;
            if (State.challenge.HasT4(v) == enabled) return false;
            State.challenge.SetT4(v, enabled);
            State.InvalidateChallenge();
            OnStateChanged?.Invoke();
            Save();
            return true;
        }

        /// <summary>挑戦構成を全解除。</summary>
        public void ClearChallenge()
        {
            if (State?.challenge == null) return;
            State.challenge.Clear();
            State.InvalidateChallenge();
            OnStateChanged?.Invoke();
            Save();
        }

        /// <summary>満点 (100pt) 構成にする / 全解除する。 AutoRunner の最高難度モード用。</summary>
        public void SetChallengeMax(bool on)
        {
            if (State?.challenge == null) return;
            State.challenge.Clear();
            if (on)
                for (int i = 0; i < ChallengeCatalog.T4s.Count; i++)
                    State.challenge.SetT4(ChallengeCatalog.T4s[i].t4, true);   // T4 が全軸を最高 Tier へ引き上げる
            State.InvalidateChallenge();
            OnStateChanged?.Invoke();
            Save();
        }

        // ============================================================
        //  レガシー API (互換のため残置・v6 では noop)
        // ============================================================

        [System.Obsolete("v6: 旧 58 段トラックは廃止。TryPurchasePoint + TryAssignRank を使用。")]
        public int NextLevel => 0;
        [System.Obsolete("v6: 旧 58 段トラックは廃止。NextPointCost を使用。")]
        public int NextCost => int.MaxValue;
        [System.Obsolete("v6: 旧 58 段トラックは廃止。IsPointCapReached を使用。")]
        public bool IsTrackComplete => true;
        [System.Obsolete("v6: 旧 58 段トラックは廃止。CanPurchasePoint を使用。")]
        public bool CanPurchase() => false;
        [System.Obsolete("v6: 旧 58 段トラックは廃止。TryPurchasePoint を使用。")]
        public bool TryPurchase() => false;
    }
}
