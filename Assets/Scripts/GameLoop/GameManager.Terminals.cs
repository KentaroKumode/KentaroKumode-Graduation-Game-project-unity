using System.Collections.Generic;
using CombatSystem;
using UnityEngine;

namespace GameLoop
{
    /// <summary>GameManager: 端子の山札 (ADR-0014) の報酬。 戦闘後は 2 択・エリートは 3 択・層ボスはレア 3 択。</summary>
    public partial class GameManager
    {
        public const int TerminalChoicesNormal = 2, TerminalChoicesElite = 3, TerminalChoicesBoss = 3;
        /// <summary>層ボスの報酬が出る最後の層 (見せる層の番号)。 5 層はヴェスカの連戦で、 後に層が無いので出さない。</summary>
        public const int TerminalBossRewardLastFloor = 4;

        /// <summary>戦闘に勝った後、 端子の報酬を提示する。 人は画面で選ぶ (<see cref="RunState.pendingTerminalOffer"/>)。
        /// BOT は <see cref="GameHooks.ChooseTerminalReward"/> でその場で選ぶ (無ければ先頭)。</summary>
        private void OfferTerminalReward(bool elite, bool boss)
        {
            if (Run == null) return;
            if (boss && Run.currentFloor > TerminalBossRewardLastFloor) return;
            int n = (boss ? TerminalChoicesBoss : elite ? TerminalChoicesElite : TerminalChoicesNormal) + Run.terminalExtraChoices;
            Run.terminalExtraChoices = 0;
            var offer = RollTerminalReward(Run, n, boss);
            if (offer.Count == 0) return;
            Run.pendingTerminalOffer = offer;
            if (GameHooks.ChooseTerminalReward == null && Loadout.HumanPlaying) return;      // 人: 画面が出す
            ResolveTerminalReward(GameHooks.ChooseTerminalReward?.Invoke(offer) ?? 0);
        }

        /// <summary>提示された端子から index 番目を山札に加える。 index &lt; 0 = 受け取らない。</summary>
        public void ResolveTerminalReward(int index)
        {
            if (Run == null || Run.pendingTerminalOffer.Count == 0) return;
            if (index >= 0 && index < Run.pendingTerminalOffer.Count)
            {
                string id = Run.pendingTerminalOffer[index];
                Run.terminalDeck.Add(id);
                Log($"端子を山札に加えた: {id} (山札 {Run.terminalDeck.Count} 枚)");
            }
            Run.pendingTerminalOffer.Clear();
        }

        /// <summary>報酬の候補を重ならないように n 個引く。 boss = レアから / それ以外 = 基本でもレアでもない端子から。
        /// 軸の端子は、 その軸の品を持っているほど出やすい (持っていなければ出にくい)。</summary>
        public static List<string> RollTerminalReward(RunState run, int n, bool boss)
        {
            var pool = new List<TerminalDef>(); var w = new List<float>();
            foreach (var d in TerminalCards.All)
            {
                if (d.basic || d.rare != boss) continue;
                float wt = 1f;
                if (d.axis != null)
                {
                    int depth = ItemSystem.PassiveSkills.AxisRules.Count(run, d.axis);
                    wt = depth <= 0 ? 0.3f : 1f + 0.5f * depth;
                }
                pool.Add(d); w.Add(wt);
            }
            var res = new List<string>();
            while (res.Count < n && pool.Count > 0)
            {
                float total = 0f; for (int i = 0; i < w.Count; i++) total += w[i];
                float r = GameRng.Value("terminal.reward") * total; int k = 0;
                for (; k < w.Count - 1; k++) { r -= w[k]; if (r < 0f) break; }
                res.Add(pool[k].id); pool.RemoveAt(k); w.RemoveAt(k);
            }
            return res;
        }
    }
}
