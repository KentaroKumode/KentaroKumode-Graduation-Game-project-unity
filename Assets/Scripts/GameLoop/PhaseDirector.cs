using System;
using UnityEngine;

namespace GameLoop
{
    /// <summary>
    /// GameManager.CurrentPhase に応じて、 シーン内の GameObject を表示/非表示に切り替える。
    /// 各エントリは「指定フェーズ群のいずれかなら表示、 それ以外は非表示」のシンプル方針。
    ///
    /// 利用例:
    ///   Menu (タイトルメニュー) → [Title]
    ///   [MapManager] → [MapNavigation, FloorClear, RestStop, Reward, ...]
    /// </summary>
    [DisallowMultipleComponent]
    public class PhaseDirector : MonoBehaviour
    {
        [Serializable]
        public class Entry
        {
            public string label;
            public GameObject target;
            public GameManager.GamePhase[] visibleInPhases;
        }

        [SerializeField] private Entry[] entries;

        private bool _subscribed;

        private void OnEnable()
        {
            Subscribe();
            Apply();
        }

        private void Start()
        {
            // GameManager の Awake/Start タイミング差を吸収
            Subscribe();
            Apply();
        }

        private void OnDisable()
        {
            var gm = GameManager.Instance;
            if (gm != null) gm.OnPhaseChanged -= OnPhaseChanged;
            _subscribed = false;
        }

        private void Subscribe()
        {
            if (_subscribed) return;
            var gm = GameManager.Instance;
            if (gm == null) return;
            gm.OnPhaseChanged += OnPhaseChanged;
            _subscribed = true;
        }

        private void OnPhaseChanged(GameManager.GamePhase _) => Apply();

        private void Apply()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;
            var phase = gm.CurrentPhase;
            if (entries == null) return;
            foreach (var e in entries)
            {
                if (e == null || e.target == null) continue;
                bool show = false;
                if (e.visibleInPhases != null)
                {
                    foreach (var p in e.visibleInPhases)
                        if (p == phase) { show = true; break; }
                }
                if (e.target.activeSelf != show) e.target.SetActive(show);
            }
        }
    }
}
