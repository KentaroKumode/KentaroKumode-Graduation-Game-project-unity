using System.Collections.Generic;
using GameLoop;
using MetaProgression;
using UnityEngine;

namespace Codex
{
    /// <summary>記録を数える係 (実行時に 1 つ作る・シーンを跨いで残る)。 毎フレーム「数が変わったか」だけを見て、 変わった時に中身を見る。</summary>
    public class CodexRecorder : MonoBehaviour
    {
        private static CodexRecorder _instance;
        private static bool _shuttingDown;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { _shuttingDown = false; _instance = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (_shuttingDown || _instance != null || Application.isBatchMode) return;
            var go = new GameObject("CodexRecorder") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<CodexRecorder>();
        }

        private int _sig = int.MinValue;

        /// <summary>一覧の指紋 (数と末尾の品。 使って買い直した時も拾う)。</summary>
        private static int Sig(List<string> l) { unchecked { return l == null || l.Count == 0 ? 0 : l.Count * 7919 + (l[l.Count - 1] ?? "").GetHashCode(); } }

        private void OnApplicationQuit() { _shuttingDown = true; }
        private void OnDestroy() { if (_instance == this) _instance = null; }

        private void Update()
        {
            if (GameHooks.BotDriving) return;
            var gm = GameManager.Instance; var run = gm != null ? gm.Run : null;
            if (run == null || gm.CurrentPhase == GameManager.GamePhase.Title) return;
            var cm = CombatSystem.CombatManager.Instance;
            int sig;
            unchecked
            {
                sig = run.Depth;
                sig = sig * 31 + Sig(run.ownedPassiveItems);
                sig = sig * 31 + Sig(run.ownedConsumables);
                sig = sig * 31 + (run.equippedWeaponId != null ? run.equippedWeaponId.GetHashCode() : 0);
                if (cm != null && cm.IsCombatActive)
                {
                    sig = sig * 31 + (cm.CurrentEnemy != null ? cm.CurrentEnemy.id.GetHashCode() : 1);
                    sig = sig * 31 + (cm.SecondaryEnemy != null ? cm.SecondaryEnemy.id.GetHashCode() : 1);
                }
            }
            if (sig == _sig) return;
            _sig = sig;
            if (CodexRecord.Note(run, cm)) MetaProgressManager.Instance?.Save();
        }
    }
}
