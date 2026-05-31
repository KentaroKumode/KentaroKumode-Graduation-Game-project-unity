using UnityEngine;

namespace MetaProgression
{
    /// <summary>
    /// メタ進行のシングルトン。State の保持・購入処理・保存/ロードを担当。
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
            State.RecalculateFromTrack();
        }

        public void ResetAll()
        {
            State = new MetaProgressState();
            PlayerPrefs.DeleteKey(PrefsKey);
            OnStateChanged?.Invoke();
        }

        /// <summary>メタバフ全段(Lv115)を解放した状態に強制セット。AutoRunner の全有効化パターン用。</summary>
        public void MaxAllForTesting()
        {
            State = new MetaProgressState();
            State.currentLevel = MetaBuffTrack.TotalSteps;
            State.RecalculateFromTrack();
            // 永続化はしない（テスト用途専用、PlayerPrefs を汚染しないため Save() しない）
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
        //  購入
        // ============================================================

        public int NextLevel => (State?.currentLevel ?? 0) + 1;
        public int NextCost => MetaBuffTrack.CalcCost(NextLevel);
        public bool IsTrackComplete => (State?.currentLevel ?? 0) >= MetaBuffTrack.TotalSteps;

        public bool CanPurchase()
        {
            if (State == null) return false;
            if (IsTrackComplete) return false;
            return State.tokens >= NextCost;
        }

        public bool TryPurchase()
        {
            if (!CanPurchase()) return false;
            int cost = NextCost;
            State.tokens -= cost;
            State.currentLevel++;
            var step = MetaBuffTrack.Get(State.currentLevel);
            if (step != null)
            {
                // RecalculateFromTrack を全走査せず増分のみ反映
                ApplyStepToState(step);
            }
            OnStateChanged?.Invoke();
            Save();
            Debug.Log($"[MetaProgress] 購入: Lv{State.currentLevel} {step?.DisplayLabel} (-{cost})");
            return true;
        }

        private void ApplyStepToState(MetaBuffStep step)
        {
            switch (step.kind)
            {
                case MetaBuffKind.Hp:               State.hpBonus += step.amount; break;
                case MetaBuffKind.Gold:             State.goldBonus += step.amount; break;
                case MetaBuffKind.DiceTotal:        State.diceTotalBonus += step.amount; break;
                case MetaBuffKind.DamageReduce:     State.damageReduce += step.amount; break;
                case MetaBuffKind.HungerReduce:     State.hungerReduce += step.amount; break;
                case MetaBuffKind.StartMaterial:    State.startMaterial += step.amount; break;
                case MetaBuffKind.CombatGoldBonus:  State.combatGoldBonus += step.amount; break;
                case MetaBuffKind.BossExtraNormal:  State.bossExtraNormalUnlocked = true; break;
                case MetaBuffKind.BossExtraRare:    State.bossExtraRareUnlocked = true; break;
                case MetaBuffKind.RefundLevelUp:    State.refundLevel = Mathf.Min(3, State.refundLevel + step.amount); break;
                case MetaBuffKind.CritLevelUp:      State.critLevel = Mathf.Min(3, State.critLevel + step.amount); break;
                case MetaBuffKind.DivineProtect:    State.divineProtectUnlocked = true; break;
                case MetaBuffKind.StartingPassiveItem: State.startingPassiveItemUnlocked = true; break;
                case MetaBuffKind.FloorClearHeal:   State.floorClearHeal = Mathf.Min(2, State.floorClearHeal + step.amount); break;
                case MetaBuffKind.TreasureChestGold: State.treasureChestGoldUnlocked = true; break;
                case MetaBuffKind.ShopRobberyUnlock: State.shopRobberyUnlocked = true; break;
            }
        }

        // ============================================================
        //  デバフトグル
        // ============================================================

        public bool ToggleDebuff(MetaDebuffLevel lv, bool enabled)
        {
            if (State == null) return false;
            int v = (int)lv;
            bool changed = false;
            if (enabled && !State.activeDebuffs.Contains(v)) { State.activeDebuffs.Add(v); changed = true; }
            else if (!enabled && State.activeDebuffs.Contains(v)) { State.activeDebuffs.Remove(v); changed = true; }
            if (changed) { OnStateChanged?.Invoke(); Save(); }
            return changed;
        }
    }
}
