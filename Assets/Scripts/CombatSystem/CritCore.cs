namespace CombatSystem
{
    /// <summary>
    /// 会心の軸 (2026-11-21・full-item-rework-spec §3 軸6・full-item-rework-ledger §⑥・2026-11-23 会心の仕組みの転換 = crit_redesign/neon/answer8_final_impl.md) の式の<b>正本</b>。
    /// 本体 (PassiveSkillManager.ProcessDamage・CombatManager の撃の輪)・BOT の配線の見積もり・画面の表示は同じ関数を通る。
    ///
    /// <para><b>超過会心率</b>: 会心率が 100% を超えた分は、 1% ごとに会心倍率 +0.01 (〈寸鉄〉で +0.02) になる (会心した撃だけ・2026-11-23: final5〜6 の +0.02 から戻した)。</para>
    /// <para><b>【練気】</b>: プレイヤーの撃が会心するたび +1 (戦闘の中だけ・減らない・上限なし・<see cref="ItemSystem.PassiveSkills.Effects.Crit.Renki"/>)。</para>
    /// <para><b>【集中】</b>: 集中の品を <see cref="FocusNeedCount"/> つ以上持ち、 攻撃の端子 1 枚だけのターンの撃で、 集中の会心率
    /// (会心率 − <see cref="FocusCost"/> ＋〈抜き打ちの作法〉の上乗せ) が <see cref="FocusMinRate"/> 以上なら、 その撃は集中の撃になる。
    /// 集中の撃が会心したら【練気】を +1 してから <see cref="RenkiSpend"/> (半分・切り上げ) を使う。 集中会心の品はその使った量で効く。</para>
    /// <para>旧〈天眼の鍔〉の確定会心 ×1.25 (HeavenSureMul) と、 会心の品が配る確定会心は 2026-11-23 に撤去した。</para>
    /// <para>このファイルは UnityEngine に依存しない。</para>
    /// </summary>
    public static class CritCore
    {
        /// <summary>超過会心率 1 (= 100%) あたりの会心倍率 (1% につき +0.01) / 〈寸鉄〉の倍。</summary>
        public const float ExcessPerRate = 1f, SuntetsuExcessMul = 2f;

        /// <summary>超過会心率で足す会心倍率 (<paramref name="critAdd"/> = 合算した会心率・1 = 100%)。</summary>
        public static float ExcessMul(float critAdd, bool suntetsu)
        {
            if (critAdd <= 1f) return 0f;
            float m = (critAdd - 1f) * ExcessPerRate * (suntetsu ? SuntetsuExcessMul : 1f);
            return CardRules.CritExcessPct > 0 ? m * (100 + CardRules.CritExcessPct) / 100f : m;   // 挑戦カード〈急所読み〉III: 変換 +25%
        }

        /// <summary>会心した撃に実際に掛かる会心倍率。 順: 敵の会心耐性 (下限 1.0) → 超過会心率の変換。</summary>
        public static float EffectiveMul(float critMul, float enemyReduction, float critAdd, bool suntetsu)
        {
            float m = critMul;
            if (enemyReduction > 0f) m = System.Math.Max(1f, m - enemyReduction);
            m += ExcessMul(critAdd, suntetsu);
            return m;
        }

        /// <summary>撃 <paramref name="h"/> (攻撃の端子 1 枚) に挿した目の一番大きい値 (挿していない撃・再起動の撃は元の端子の目・0 = 目なし)。 〈狙い目の刻み〉が読む。</summary>
        public static int HitFaceMax(TerminalOutcome o, int[] dice, int h)
        {
            if (o == null || dice == null || h < 0 || h >= o.attackHitSlot.Count) return 0;
            int slot = o.attackHitSlot[h], mx = 0;
            for (int i = 0; i < dice.Length && i < o.slotOfDie.Length; i++)
                if (o.slotOfDie[i] == slot && dice[i] > mx) mx = dice[i];
            return mx;
        }

        /// <summary>〈狙い目の刻み〉: この撃に挿した目が 5 なら会心率 +12%、 6 なら +24% (2026-11-23・確定会心は撤去・final6: 5 以上 +25%・6 で確定会心)。</summary>
        public const int AimNeed = 5, AimHigh = 6;
        public const float AimPct = 0.12f, AimHighPct = 0.24f;
        public static float AimRate(int faceMax) => faceMax >= AimHigh ? AimHighPct : faceMax >= AimNeed ? AimPct : 0f;

        // ---- 【集中】(2026-11-23) ----

        /// <summary>集中の品の数の入口 / 集中の会心率の下限 / 基礎の賭け金 / 会心の品 1 つあたりの賭け金の軽減 (会心の品が <see cref="FocusNeedCount"/> を超えた分)。</summary>
        public const int FocusNeedCount = 3;
        public const float FocusMinRate = 0.01f, FocusBaseCost = 0.25f, FocusMitigationPerRelic = 0.20f;
        /// <summary>集中の品の賭け金 (レア度ごと: BRONZE 0・SILVER 8%・GOLD 20%・LEGENDARY 28%)。</summary>
        public const float FocusCostB = 0f, FocusCostS = 0.08f, FocusCostG = 0.20f, FocusCostL = 0.28f;

        /// <summary>集中の品 9 品 (分岐 B〈集中〉)。 ビットの並びは <see cref="FocusIds"/> と同じ。</summary>
        [System.Flags]
        public enum FocusItems
        {
            None = 0, FirstArrow = 1 << 0, FirstStrokePin = 1 << 1, NarrowSpyglass = 1 << 2, NickAppraisal = 1 << 3, HeftyPommel = 1 << 4,
            QuickDrawArt = 1 << 5, OneFlashMastery = 1 << 6, HeavenEyeGuard = 1 << 7, SniperScope = 1 << 8,
        }
        /// <summary>集中の品の id (ビットの順) と、 その賭け金。</summary>
        public static readonly string[] FocusIds =
        {
            GameLoop.ItemIds.CritFirstArrow, GameLoop.ItemIds.CritFirstStroke, GameLoop.ItemIds.CritNarrow, GameLoop.ItemIds.CritNick, GameLoop.ItemIds.CritPommel,
            GameLoop.ItemIds.CritQuickDraw, GameLoop.ItemIds.CritOneFlash, GameLoop.ItemIds.CritHeaven, GameLoop.ItemIds.CritSniper,
        };
        private static readonly float[] FocusIdCost = { FocusCostB, FocusCostB, FocusCostB, FocusCostS, FocusCostS, FocusCostG, FocusCostG, FocusCostL, FocusCostL };

        /// <summary>持っている集中の品 (〈昇華〉を含む所持の判定は RunState.OwnsPassive)。</summary>
        public static FocusItems FocusOf(GameLoop.RunState run)
        {
            var f = FocusItems.None;
            if (run == null) return f;
            for (int i = 0; i < FocusIds.Length; i++) if (run.OwnsPassive(FocusIds[i])) f |= (FocusItems)(1 << i);
            return f;
        }
        public static int FocusCount(FocusItems f)
        {
            int n = 0;
            for (int i = 0; i < FocusIds.Length; i++) if (((int)f & (1 << i)) != 0) n++;
            return n;
        }
        /// <summary>持っている集中の品の賭け金の合計 (基礎の 25% は含まない)。</summary>
        public static float RawFocusItemCost(FocusItems f)
        {
            float c = 0f;
            for (int i = 0; i < FocusIds.Length; i++) if (((int)f & (1 << i)) != 0) c += FocusIdCost[i];
            return c;
        }
        /// <summary>持っている会心の軸の秘宝の数 (items.json の axis・〈昇華〉の品も所持の一覧に入る)。</summary>
        public static int CritRelicCount(GameLoop.RunState run) => ItemSystem.PassiveSkills.AxisRules.Count(run, ItemSystem.PassiveSkills.AxisRules.Crit);

        /// <summary>集中の賭け金 (会心率から引く・小数): (基礎 25% ＋ 品の賭け金の合計) ÷ (1 ＋ 0.2 × max(0, 会心の品の数 − 3))。 上限なし・常に正。</summary>
        public static float FocusCost(float rawCostSum, int critRelicCount)
            => (FocusBaseCost + rawCostSum) / (1f + FocusMitigationPerRelic * System.Math.Max(0, critRelicCount - FocusNeedCount));

        /// <summary>集中の撃の会心率。 集中にならない時は −1 (入口: 集中の品 3 つ以上・攻撃の端子 1 枚の撃・照準器のもう 1 回でない・集中の会心率 1% 以上)。
        /// <paramref name="preFocus"/> = 乗算まで済んだ合算の会心率 / <paramref name="bonus"/> = 〈抜き打ちの作法〉の上乗せ。</summary>
        public static float FocusedRate(float preFocus, bool soloHit, bool refire, FocusItems f, int critRelicCount, float bonus)
        {
            if (!soloHit || refire || FocusCount(f) < FocusNeedCount) return -1f;
            float r = preFocus - FocusCost(RawFocusItemCost(f), critRelicCount) + bonus;
            return r >= FocusMinRate ? r : -1f;
        }

        /// <summary>集中会心で使う【練気】(+1 した後の値の半分・切り上げ)。</summary>
        public static int RenkiSpend(int renkiAfterGain) => renkiAfterGain <= 0 ? 0 : (renkiAfterGain + 1) / 2;

        /// <summary>〈必殺の照準器〉: もう 1 回発火した撃の会心倍率の上乗せ = 0.50 ＋ 元の集中会心で使った【練気】× 0.25。</summary>
        public const float SniperMulBase = 0.50f, SniperMulPerRenki = 0.25f;
        public static float SniperMulAdd(int spent) => SniperMulBase + SniperMulPerRenki * spent;
    }
}
