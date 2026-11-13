using MapSystem.FreeMove;
using UnityEngine;

namespace GameLoop
{
    /// <summary>
    /// 不安定化 (2026-11-13・GAME.md §5-12): <b>大穴から何かを取るたびに嵐が進む</b>。 本体は「何が起きたか」を理由として渡すだけで、
    /// 何ポイントかは <see cref="Instability.PointsOf"/> (1 か所の表) が決める。 嵐の無い層・手番の時計 (測定の口 <see cref="GameHooks.StormClock"/> = Turns)・
    /// 航行マップの無い層では何も起きない (<see cref="FreeMapSim.AddInstability(InstabilitySource, int, out int)"/> が見る)。
    ///
    /// <para>取る行為と理由 (呼ぶ場所):
    /// 宝の点を開ける / 縄張りの宝 → <see cref="InstabilitySource.Treasure"/> (<c>OpenTreasure</c> / <c>TakeTerritoryTreasure</c>)・
    /// 秘宝を 1 つ得る → <see cref="InstabilitySource.Relic"/> (イベント・戦闘の報酬・番人の報酬・〈剛胆〉の精鋭の戦利品)・
    /// エリート / 番人を倒す → <see cref="InstabilitySource.Elite"/> / <see cref="InstabilitySource.Pursuer"/>・
    /// 通常の戦闘 (使い魔・イベントの戦闘) に勝つ → <see cref="InstabilitySource.Battle"/>・
    /// イベントで何かを得る → <see cref="InstabilitySource.EventGain"/> (秘宝を得た時は数えない)・
    /// 戦闘のエスカレーションの段が上がる → <see cref="InstabilitySource.Escalation"/> (ボス戦は除く)・
    /// イベントの「ターン+N」→ <see cref="InstabilitySource.EventTurns"/>。 歩く・待つ・偵察・嵐を動かす は <see cref="FreeMapSim"/> の中で溜まる。</para>
    ///
    /// <para>溜まらない: 店で買う・交換マス・強盗・偽の商人・逃げた戦闘・負けて救済された戦闘・ボス戦・端子の報酬・休憩・照明・〈補給線〉・近道の 1 歩。</para>
    /// </summary>
    public partial class GameManager
    {
        /// <summary>[計装] 理由ごとに溜まったポイント (プロセス通算・<see cref="InstabilitySource"/> の順)。 AutoRunner の実効状態の行が読む。</summary>
        public static readonly long[] InstabilityTotals = new long[System.Enum.GetValues(typeof(InstabilitySource)).Length];
        /// <summary>[計装] 嵐の層を抜けた (次の層へ進んだ) 時の累計の合計と層の数・段ごとの層の数 (プロセス通算)。</summary>
        public static long InstabilityLayerSum, InstabilityLayers;
        public static readonly long[] InstabilityLayerPhase = new long[5];

        /// <summary>この戦闘でエスカレーションから溜まったポイント (戦闘の画面の 1 行が読む・戦闘が始まる時に 0)。</summary>
        public int InstabilityFromEscalationThisCombat { get; private set; }

        /// <summary>直近の戦闘が始まってから溜まった量 (エスカレーション・勝ち・報酬の秘宝)。 報酬の画面の結果の行が読む (戦闘が始まる時に 0)。</summary>
        public int InstabilityThisBattle { get; private set; }

        /// <summary>報酬の画面の結果の行 (「不安定 +N（累計 M）」・溜まっていなければ null)。</summary>
        public string BattleInstabilityLine() => InstabilityLine(InstabilityThisBattle);

        /// <summary>直近に溜まった量と理由 (結果の行・地図の帯の知らせが読む)。</summary>
        public int LastInstabilityGain { get; private set; }

        /// <summary>同じイベントで「何かを得た +1」(か秘宝) を既に数えたか (選んだ効果と勝利後の効果で +1 を重ねないため)。</summary>
        private bool _eventGainCounted;

        /// <summary>不安定化ポイントを足し、 ログに残す。 段が変わったら地図の知らせ (<see cref="OnFreeMapEvent"/>) を流す。 実際に動いた量を返す (嵐の無い層などは 0)。</summary>
        public int AddInstability(InstabilitySource src, int n = 1, string what = null)
        {
            var sim = FreeMap;
            if (sim == null || !sim.InstabilityMode) return 0;
            var ev = sim.AddInstability(src, n, out int delta);
            if (delta == 0) return 0;
            InstabilityTotals[(int)src] += delta;
            LastInstabilityGain = delta;
            InstabilityThisBattle += delta;
            Log($"不安定 {delta:+0;-0}（{what ?? Instability.NameOf(src)}・累計 {sim.Instability}）");
            if (ev != FreeMapEvent.None)
            {
                OnFreeMapEvent?.Invoke(ev);
                LogStormAdvanced(sim);
            }
            return delta;
        }

        /// <summary>航行の操作 1 回の中で (<see cref="FreeMapSim"/> の中で) 溜まった分を、 内訳の差でログと記録へ (歩く・イベントのターン・嵐を動かす)。
        /// 段の変わり目のログは操作の知らせ (HandleFreeMapEvent) が出す。</summary>
        private void NoteSimInstability(FreeMapSim sim, int[] before)
        {
            for (int p = 0; p < before.Length; p++)
            {
                int d = sim.InstabilityParts[p] - before[p];
                if (d == 0) continue;
                var src = p == Instability.PartWalk ? InstabilitySource.Walk : p == Instability.PartEventTurns ? InstabilitySource.EventTurns : InstabilitySource.Shift;
                InstabilityTotals[(int)src] += d;
                LastInstabilityGain = d;
                Log($"不安定 {d:+0;-0}（{Instability.NameOf(src)}・累計 {sim.Instability}）");
            }
        }

        /// <summary>結果の行の文: 「不安定 +N（累計 M）」。 動かなかったら null。</summary>
        public string InstabilityLine(int delta)
        {
            var sim = FreeMap;
            if (delta == 0 || sim == null || !sim.InstabilityMode) return null;
            return $"不安定 {delta:+0;-0}（累計 {sim.Instability}）";
        }

        /// <summary>嵐の段が変わった時のログ (手番の時計・不安定化の時計で共通)。</summary>
        private void LogStormAdvanced(FreeMapSim sim)
        {
            int ph = sim.StormPhase;
            string kind = Storm.KindName(sim.Weather);
            string next = sim.InstabilityMode ? $"次まで 不安定 {sim.StormTurnsToNext}" : $"次まで {sim.StormTurnsToNext}";
            Log(ph == 0 ? (sim.InstabilityMode ? $"嵐が静まった（{kind}・崩れ始めるまで 不安定 {sim.StormTurnsToNext}）" : $"嵐が遠のいた（{kind}・嵐まで {sim.StormTurnsToNext}）")
                : ph == 3 ? $"嵐 フェーズ {ph}（{kind}）── ボスの居場所が見えた"
                : ph == 4 ? $"嵐 フェーズ {ph}（{kind}）── ボスの点のほかは全部が嵐の中"
                : ph == 1 && sim.InstabilityMode ? $"崩れ始めた ── 嵐 フェーズ 1（{kind}・{next}）"
                : $"嵐 フェーズ {ph}（{kind}・{next}）");
        }

        // =====================================================================
        //  戦闘
        // =====================================================================

        /// <summary>戦闘が始まった (CombatManager.StartCombat から)。 この戦闘のエスカレーションの分を 0 に戻す。</summary>
        public void NoteCombatStartedForInstability() { InstabilityFromEscalationThisCombat = 0; InstabilityThisBattle = 0; }

        /// <summary>戦闘の中でエスカレーションの段が <paramref name="rises"/> 段上がった (CombatManager の予告から)。 ボス戦では溜めない。</summary>
        public void NoteEscalationRise(int rises, bool bossFight)
        {
            if (Instability.EscalationPoints(rises, bossFight) <= 0) return;   // ボス戦では溜めない (規則は Instability の 1 か所)
            int d = AddInstability(InstabilitySource.Escalation, rises);
            InstabilityFromEscalationThisCombat += d;
        }

        /// <summary>戦闘に勝った時の不安定化 (ボス以外)。 elite = エリートの点の戦闘、 pursuer = 番人。 動いた量を返す。</summary>
        private int AddBattleWinInstability(bool elite, bool pursuer)
            => AddInstability(pursuer ? InstabilitySource.Pursuer : elite ? InstabilitySource.Elite : InstabilitySource.Battle);

        /// <summary>報酬・宝で得た品が秘宝なら +3 (秘宝 = パッシブの品・items.json の category で判定)。 進行の資格の品 (確信の 3 品・ItemIds.QualificationItems) は数えない。</summary>
        private int AddRelicInstabilityIfPassive(string itemId)
        {
            if (!ItemIds.CountsAsTakenRelic(itemId)) return 0;
            var d = ItemSystem.ItemDatabase.Instance?.GetItem(itemId);
            return d != null && d.category == ItemSystem.ItemCategory.Passive ? AddInstability(InstabilitySource.Relic, 1, "秘宝 " + d.displayName) : 0;
        }

        // =====================================================================
        //  イベント
        // =====================================================================

        /// <summary>イベントの選択肢の効果を実行した直後 (EventEncounter.ResolveChoice・画面へ結果を流す前)。 秘宝 +3 ずつ、 ほかの得だけなら +1 (重ねない)。
        /// 結果の行に「不安定 +N（累計 M）」を足す。 postCombat = 「勝利後」の効果 (同じイベントで +1 を既に数えていれば +1 は足さない)。</summary>
        public void SettleEventInstability(EventSystem.EventEffectExecutor.ExecutionResult r, bool postCombat)
        {
            if (r == null) return;
            if (!postCombat) _eventGainCounted = false;
            int relics = r.relicsGained, total = 0;
            if (relics > 0) total += AddInstability(InstabilitySource.Relic, relics, $"秘宝 {relics} つ");
            else if (r.otherGains > 0 && !_eventGainCounted) total += AddInstability(InstabilitySource.EventGain);
            if (relics > 0 || r.otherGains > 0) _eventGainCounted = true;
            string line = InstabilityLine(total);
            if (line != null) r.log.Add(line);
        }
    }
}
