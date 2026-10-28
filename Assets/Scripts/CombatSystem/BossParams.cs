using System.Collections.Generic;
using UnityEngine;

namespace CombatSystem
{
    /// <summary>
    /// ボスの機構パラメータ。 **乗算係数(×1.xx)ではなく、 各パッシブの「具体的な数値」そのもの**
    /// (断罪ダイス上乗せ・断罪係数の基礎・審判の炎の上限・灰塵の鎧の軽減量・停滞する時間の係数 ほか)。
    /// 値の正本は <see cref="BossParams.Spec"/> の基準値。
    /// </summary>
    public enum BossParam
    {
        JudgmentDice,      // 灰燼/業火・断罪ターンの敵ダイス上乗せ (見切りロール難度)。 基準10
        JudgmentCoefBase,  // 業火の断罪の係数基礎。 係数 = base + 2n。 基準2 (1回目=4)
        RobeStacks,        // [死にパラメータ・2026-10-17] 天衣無縫 (FlawlessRobe) を削除したので読む側が無い。 並びを保つため残置
                           //   現在どの敵も未装備だが Registry には流用可として登録済み
        ChipCap,           // 業火の審判官・審判の炎の毎ターン継続ダメ上限。 基準13
        RegenReduction,    // 灰燼・灰塵の鎧の被ダメ軽減量 (タンク性/ジリ貧)。 基準9
        Stagnation,        // 停滞する時間: 攻撃端子が空のTごとに積むスタック1つあたりの敵出目加算。 基準3
                           //   → **7層ヴェスカの第一レバー** (docs/GAME.md §13-4)
        IntimidateThreat,  // 威圧+ の脅威加算 (勝利時の削り/敗北時の被弾の基準)。 基準3
        PoisonStackCap,    // 毒沼の主・毒の侵蝕の毒スタック上限 (DOTの最大強度)。 基準5
        MirrorReflectCap,  // 鏡の双子・鏡映の応答の反射ダメ上限。 基準9
        GoblinCall,        // ゴブリン王・号令の毎ターン自ダイス合計加算。 基準3
        StanceAtkMult,     // 敵スタンス(ADR-0005)・高火力スタンスの被ダメ倍率 (=ボスの攻撃力)。 基準1.6
        WeakRollRatio,     // 敵スタンス・弱ロールの面縮小比 (高いほどロールが弱まらない=強い)。 基準0.65
    }

    /// <summary>
    /// ボスの数値の正本 (機構パラメータの基準値・固有面ダイス) と、 敵データへの適用。
    ///
    /// <para><b>本体は基準値だけで完結する。</b> 外から値を差し替える口は <see cref="Overrides"/> だけで、
    /// 既定は null (= 基準値)。 BOT のボス調整器 (AutoTest.BossTuning) は AutoRunner の起動時にだけここへ差し込む。
    /// 本体から AutoTest の型を参照しないための境界 (2026-10-29)。</para>
    /// </summary>
    public static class BossParams
    {
        /// <summary>基準値と、 差し替え値を受け付ける範囲。</summary>
        public readonly struct ParamSpec
        {
            public readonly float baseValue, min, max;
            public ParamSpec(float baseValue, float min, float max) { this.baseValue = baseValue; this.min = min; this.max = max; }
        }

        /// <summary>各 BossParam の基準値と範囲。</summary>
        public static ParamSpec Spec(BossParam p)
        {
            switch (p)
            {
                //                                             base  min   max
                case BossParam.JudgmentDice:      return new ParamSpec(15f, 0f, 30f);
                case BossParam.JudgmentCoefBase:  return new ParamSpec( 2f, 0f,  8f);
                // 2026-08-15: 一度 10 → 7 にしたが **10 へ戻した**。 「5層の主死因が Chip 95〜98%」
                //   という根拠自体が、 〈怪しい商人〉が 5層の通常抽選に混ざっていた汚染下の測定だった
                //   (FloorManager.SpecialEncounterFloor 参照)。 7 で測っても 7層クリアは
                //   5.9% → 5.8% と動かず、 効いたのは 5層ボス単体の勝率 (70.9→74.1%) だけ。
                //   出現漏れを直した土俵で測り直してから是非を判断する。
                case BossParam.RobeStacks:        return new ParamSpec(10f, 1f, 20f);
                case BossParam.ChipCap:           return new ParamSpec(10f, 1f, 20f);
                case BossParam.RegenReduction:    return new ParamSpec( 9f, 0f, 20f);
                // 2026-08-16: 基準 3 → 1。 〈停滞する時間〉の 1 スタックあたり敵出目加算。
                //   実測 (7層p4 の攻撃値分解) で 15T+ の停滞寄与が 15.3 ＝ 5.1 スタック × 3 に達し、
                //   敵ダイス合計 32.0 の 48% を占めていた。 そこへエスカレーション倍率が掛かるので
                //   攻撃値 70.9 に対しブロック 19.2 ＝ 貫通 51.7 (プレイヤー最大HP 96 の半分超/T)。
                //   なお StackCap(10) は 5.1 スタックまでしか積まないため**一度も効いていない** ──
                //   99→10 に戻しても結果が 1 件も変わらなかった。 効くのはこちらの係数。
                //   StagnantTime.DicePerStackBase(=1) はこの値に負けるので、
                //   **コード側の定数を読んでも実効値は分からない**点に注意。
                case BossParam.Stagnation:        return new ParamSpec( 1f, 0f, 12f);
                case BossParam.IntimidateThreat:  return new ParamSpec( 3f, 0f, 10f);
                case BossParam.PoisonStackCap:    return new ParamSpec( 5f, 1f, 12f);
                case BossParam.MirrorReflectCap:  return new ParamSpec( 9f, 0f, 15f);
                case BossParam.GoblinCall:        return new ParamSpec( 3f, 0f, 12f);
                case BossParam.StanceAtkMult:     return new ParamSpec(1.6f, 1.0f, 3.0f);
                case BossParam.WeakRollRatio:     return new ParamSpec(0.65f, 0.40f, 0.90f);
                default:                          return new ParamSpec( 1f, 0f,  1f);
            }
        }

        // ===== 外からの差し替え (BOT のボス調整器だけが使う) =====

        /// <summary>ボスの数値を差し替える口。 値を持たない項目は基準値に落ちる。</summary>
        public interface IOverrides
        {
            /// <summary>param の差し替え値。 無ければ false。 範囲へのクランプは呼び出し側で行う。</summary>
            bool TryGetParam(string id, BossParam p, out float value);
            /// <summary>最大 HP の絶対値。 0 = 差し替え無し。</summary>
            int MaxHp(string id);
            /// <summary>素ダイスの目標期待値。 0 = 差し替え無し。</summary>
            float DiceExpected(string id);
            /// <summary>固有面ダイスの面。 null = 既定面。</summary>
            int[] SignatureFaces(string id);
        }

        /// <summary>null = 基準値 (本番)。</summary>
        public static IOverrides Overrides;

        public static bool IsBoss(string id) => GameLoop.BossIds.IsBoss(id);

        /// <summary>id のボスの param 現在値 (差し替え or 基準)。 パッシブが直接引く。</summary>
        public static float Param(string id, BossParam p)
        {
            var s = Spec(p);
            if (Overrides != null && !string.IsNullOrEmpty(id) && Overrides.TryGetParam(id, p, out float v))
                return Mathf.Clamp(v, s.min, s.max);
            return s.baseValue;
        }

        /// <summary>id のボスの param を丸めた整数値。</summary>
        public static int ParamInt(string id, BossParam p) => Mathf.RoundToInt(Param(id, p));

        // ===== 素ダイス (期待値ベース) =====
        // ダイス期待値の範囲 (1d1=1 〜 5d9=25)。 ダイス個数≤5・最高出目≤9 の制約。
        public const int MaxDiceCount = 5;
        public const int MaxDiceFaces = 9;
        public const float DiceEMin = 1f;
        public const float DiceEMax = 25f; // 5 × (9+1)/2

        public static float DiceE(int count, int faces) => count * (faces + 1) / 2f;

        /// <summary>目標期待値に最も近い (個数≤5・最高出目≤9) のダイス構成。 同値なら個数多い方(低分散)を優先。</summary>
        public static (int count, int faces) BestDiceConfig(float targetE)
        {
            int bestC = 1, bestF = 1; float bestDiff = float.MaxValue;
            for (int c = 1; c <= MaxDiceCount; c++)
                for (int f = 1; f <= MaxDiceFaces; f++)
                {
                    float diff = Mathf.Abs(DiceE(c, f) - targetE);
                    if (diff < bestDiff - 0.001f || (Mathf.Abs(diff - bestDiff) <= 0.001f && c > bestC))
                    { bestDiff = diff; bestC = c; bestF = f; }
                }
            return (bestC, bestF);
        }

        // ===== 固有面ダイス(署名ダイス) =====
        //  対象4ボスは均一ダイス(diceExpected/BestDiceConfig)を使わず、 ボス固有の面構成で振る。
        //  グラフィック制約により 1面の値は [SignatureFaceMin, SignatureFaceMax] にクランプ。

        public const int SignatureDiceCount = 5;   // 署名ダイスは5個振り
        public const int SignatureFaceCount = 6;   // 面は6つ
        public const int SignatureFaceMin = 1;
        public const int SignatureFaceMax = 9;     // 11等はダイスのグラフィック表現不可 → 上限9

        /// <summary>固有面ダイスの面 (正本)。</summary>
        private static readonly Dictionary<string, int[]> _signatureDefaults = new Dictionary<string, int[]>
        {
            // 5層 業火の審判官「裁火のダイス」: 二極(赦免/断罪) 平均4.83 ×5 = E24.2
            { "boss_layer5",        new[] { 2, 3, 3, 6, 7, 8 } },
            // 5層裏 シュヴァリエ「決闘のダイス」: 低分散・規律 平均5.5 ×5 = E27.5
            { "boss_layer5_hidden", new[] { 4, 5, 5, 6, 6, 7 } },
            // 6層 灰燼の王「薄火のダイス」: 上寄り 平均6.67 ×5 = E33.3 (+灰塵の威圧+3で実効E36)
            { "boss_layer6",        new[] { 4, 6, 7, 7, 8, 8 } },
            // 7層 ヴェスカ「往生呪のダイス」: 高い床 平均8.5 ×5 = E42.5 (プレイヤー1T目天井に匹敵)
            { "boss_layer7",        new[] { 7, 8, 8, 9, 9, 9 } },
        };

        public static bool IsSignatureDiceBoss(string id)
            => !string.IsNullOrEmpty(id) && _signatureDefaults.ContainsKey(id);

        /// <summary>このボスの固有面のコピー (差し替えがあればそれ、 無ければ既定)。 非対象ボスは null。</summary>
        public static int[] SignatureFaces(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var ov = Overrides?.SignatureFaces(id);
            if (IsValidSignature(ov)) return (int[])ov.Clone();
            if (_signatureDefaults.TryGetValue(id, out var def))
                return (int[])def.Clone();
            return null;
        }

        public static bool IsValidSignature(int[] faces)
            => faces != null && faces.Length == SignatureFaceCount;

        // ===== 適用 =====

        /// <summary>ボスなら固有面ダイスと差し替え (HP・ダイス期待値) を反映した敵データを返す。
        ///
        /// **戻り値は複製とは限らない。** 非ボス、 およびボスでも変更が一つも無い場合は
        /// 引数をそのまま (= EnemyDatabase の共有インスタンスを) 返す。 複製が保証されるのは
        /// 署名ダイスボスだけ。 **戻り値を書き換える側は自分で <see cref="EnemyData.Clone"/> すること。**
        /// (2026-08-04: この戻り値を複製と誤認して直接書き換えた結果、 敵 HP が遭遇ごとに
        ///  複利で膨らみ int を溢れさせた事故がある)</summary>
        public static EnemyData Apply(EnemyData e)
        {
            if (e == null || !IsBoss(e.id)) return e;
            int hpOv = Overrides?.MaxHp(e.id) ?? 0;

            // 固有面ダイス対象ボス: 均一ダイス(diceExpected/BestDiceConfig)を使わず署名面で振る。
            if (IsSignatureDiceBoss(e.id))
            {
                var c = e.Clone();
                if (hpOv > 0) c.maxHP = Mathf.Max(1, hpOv);
                var sig = SignatureFaces(e.id);
                if (IsValidSignature(sig))
                {
                    c.diceFaces = sig;
                    c.diceCount = SignatureDiceCount;
                    int mx = SignatureFaceMin;
                    foreach (var v in sig) if (v > mx) mx = v;
                    c.diceMaxValue = mx; // 弱ロール面縮小/勝率推定など均一前提コードの整合用
                }
                return c;
            }

            float diceOv = Overrides?.DiceExpected(e.id) ?? 0f;
            bool changeHp = hpOv > 0 && hpOv != e.maxHP;
            bool changeDice = diceOv > 0f;
            if (!changeHp && !changeDice) return e;
            var cc = e.Clone();
            if (changeHp) cc.maxHP = Mathf.Max(1, hpOv);
            if (changeDice)
            {
                var (cnt, faces) = BestDiceConfig(Mathf.Clamp(diceOv, DiceEMin, DiceEMax));
                cc.diceCount = cnt;
                cc.diceMaxValue = faces;
            }
            return cc;
        }
    }
}
