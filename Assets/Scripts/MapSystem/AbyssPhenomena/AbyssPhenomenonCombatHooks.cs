using System;
using GameLoop;
using UnityEngine;

namespace MapSystem.AbyssPhenomena
{
    /// <summary>
    /// 戦闘ループから呼ばれる異常現象の効果フック。
    /// CombatManager から呼び出し、 RunState.activePhenomena を参照して各効果を適用する。
    /// </summary>
    public static class AbyssPhenomenonCombatHooks
    {
        /// <summary>戦闘開始時。 蝕夜の判定 (30%で双方T1スキップ) と、 鉄を溶かす太陽の発火Tを決める。</summary>
        public static void OnCombatStart(RunState run)
        {
            if (run == null) return;
            run.eclipsedNightTriggered = false;
            run.ironSunNextTurn = 0;

            if (run.HasPhenomenon(AbyssPhenomenon.EclipsedNight) && UnityEngine.Random.value < 0.30f)
            {
                run.eclipsedNightTriggered = true;
                Debug.Log("[AbyssPhenomenon] 蝕夜: 双方とも開幕 1T 行動不能");
            }

            if (run.HasPhenomenon(AbyssPhenomenon.IronMeltingSun))
            {
                run.ironSunNextTurn = UnityEngine.Random.Range(1, 6); // 1〜5T 目に最初の直射
            }
        }

        /// <summary>T1 で蝕夜が発動しているか (双方の行動を無効化するか)。</summary>
        public static bool IsEclipsedTurn(RunState run, int currentTurn)
            => run != null && currentTurn == 1 && run.eclipsedNightTriggered;

        /// <summary>朱の雪: 自分の与える主ダメージへの加算補正 (常に -1、 最低 0)。</summary>
        public static int CrimsonSnowDamageDelta(RunState run)
            => (run != null && run.HasPhenomenon(AbyssPhenomenon.CrimsonSnow)) ? -1 : 0;

        /// <summary>影が落ちない正午: T1 で敵の攻撃が空振りするか (50%)。</summary>
        public static bool ShouldNoonMiss(RunState run, int currentTurn)
            => run != null
               && currentTurn == 1
               && run.HasPhenomenon(AbyssPhenomenon.NoonWithoutShadow)
               && UnityEngine.Random.value < 0.50f;

        /// <summary>鉄を溶かす太陽: このターンに直射が発火するか判定。 発火時はプレイヤーダイスを 0 にして HP-10。
        /// 戻り値は HP 損失量 (0=不発)。</summary>
        public static int ApplyIronSunIfTriggered(RunState run, int currentTurn, int[] playerDice)
        {
            if (run == null || !run.HasPhenomenon(AbyssPhenomenon.IronMeltingSun)) return 0;
            if (run.ironSunNextTurn <= 0 || currentTurn < run.ironSunNextTurn) return 0;

            if (playerDice != null)
            {
                for (int i = 0; i < playerDice.Length; i++) playerDice[i] = 0;
            }
            run.ironSunNextTurn = currentTurn + 5;
            Debug.Log($"[AbyssPhenomenon] 鉄を溶かす太陽: 直射 HP-10 + 行動無効 (次T={run.ironSunNextTurn})");
            return 10;
        }

        /// <summary>鳴りやまない鐘: 各ダイスの出目を 20%で -1 (最低 1)。 戻り値は補正後配列を直接書き換え。</summary>
        public static void ApplyBellPenalty(RunState run, int[] dice)
        {
            if (run == null || dice == null || dice.Length == 0) return;
            if (!run.HasPhenomenon(AbyssPhenomenon.UnceasingBell)) return;
            if (UnityEngine.Random.value >= 0.20f) return;
            int idx = UnityEngine.Random.Range(0, dice.Length);
            dice[idx] = Mathf.Max(1, dice[idx] - 1);
            Debug.Log($"[AbyssPhenomenon] 鳴りやまない鐘: ダイス[{idx}] -1 → {dice[idx]}");
        }

        /// <summary>
        /// 各ターン終了時の HP 影響を計算する。
        /// 適用先 (player/enemy) は呼び出し側で反映してもらう。 戻り値は (playerDelta, enemyDelta) のタプル。
        /// </summary>
        public static (int playerDelta, int enemyDelta) ApplyTurnEnd(RunState run, int currentTurn, int enemyMaxHP)
        {
            int pDelta = 0, eDelta = 0;
            if (run == null) return (0, 0);

            bool silenced = run.HasPhenomenon(AbyssPhenomenon.SinkingSilence);

            if (!silenced)
            {
                // 削る砂: 毎T -1
                if (run.HasPhenomenon(AbyssPhenomenon.AbrasiveSand))
                {
                    pDelta -= 1;
                }
                // 崩れる地平: 5T 以降毎T -2
                if (run.HasPhenomenon(AbyssPhenomenon.CollapsingHorizon) && currentTurn >= 5)
                {
                    pDelta -= 2;
                }
                // 間歇の崩落: 5T 経過毎に双方 -3
                if (run.HasPhenomenon(AbyssPhenomenon.IntermittentFall) && currentTurn > 0 && currentTurn % 5 == 0)
                {
                    pDelta -= 3;
                    eDelta -= 3;
                }
                // 燃える河: 敵に最大HP 2% (最低1)
                if (run.HasPhenomenon(AbyssPhenomenon.BurningRiver))
                {
                    eDelta -= Mathf.Max(1, Mathf.RoundToInt(enemyMaxHP * 0.02f));
                }
                // 逆さ雷: 15% で敵 +3
                if (run.HasPhenomenon(AbyssPhenomenon.InvertedLightning) && UnityEngine.Random.value < 0.15f)
                {
                    eDelta -= 3;
                }
            }

            return (pDelta, eDelta);
        }

        /// <summary>沈む静寂が発動中か (CombatManager の他スリップ系の抑止判定用)。</summary>
        public static bool IsSilenced(RunState run)
            => run != null && run.HasPhenomenon(AbyssPhenomenon.SinkingSilence);

        /// <summary>マップノード移動時の HP 損 (落石のような雹: -1)。</summary>
        public static int OnNodeMove(RunState run)
            => (run != null && run.HasPhenomenon(AbyssPhenomenon.BoulderHail)) ? 1 : 0;

        /// <summary>希望が減少するときに 追加で発生する損失量 (薄れる人: +1)。</summary>
        public static int OnHopeReduceExtraLoss(RunState run)
            => (run != null && run.HasPhenomenon(AbyssPhenomenon.FadingPerson)) ? 1 : 0;
    }
}
