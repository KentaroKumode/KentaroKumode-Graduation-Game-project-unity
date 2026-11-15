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

        /// <summary>実行時に、 <paramref name="t"/> を写す表示の欄 (欄の target が t 自身か t の祖先) に、 表示するフェーズを足す (シーンの設定は書き換えない・2026-11-15 #328)。
        /// エンディングの画面は液晶の中身のカメラ (シーンでは踏破 RunClear・裂け目の一択 RiftChoice のフェーズで止めていた) に写るので、 その間もカメラを生かす。 足したら今のフェーズで当て直す。</summary>
        public static void AlsoShowFor(Transform t, params GameManager.GamePhase[] phases)
        {
            if (t == null || phases == null) return;
            foreach (var d in FindObjectsOfType<PhaseDirector>(true))
            {
                if (d.entries == null) continue;
                bool changed = false;
                foreach (var e in d.entries)
                {
                    if (e == null || e.target == null || !(t == e.target.transform || t.IsChildOf(e.target.transform))) continue;
                    var list = new System.Collections.Generic.List<GameManager.GamePhase>(e.visibleInPhases ?? new GameManager.GamePhase[0]);
                    foreach (var p in phases) if (!list.Contains(p)) { list.Add(p); changed = true; }
                    e.visibleInPhases = list.ToArray();
                }
                if (changed) d.Apply();
            }
        }

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
