namespace AutoTest
{
    /// <summary>
    /// SuperV3 の実験スイッチ (2026-11-05)。 <b>SuperV3 = Optimal + このスイッチ</b>。
    /// 全部 OFF (既定) のとき、 Optimal と<b>1 ランも違わず</b>同じ結果になる (A/B の基準アーム。 検算は同じシードの完全一致で行う)。
    ///
    /// <para>改善案は 1 案 1 フィールドで足し、 Optimal の同じ関数の中で <c>if (v3.xxx)</c> として分岐させる。
    /// コピーを別に持つと、 Optimal を直すたびに片方が古くなる (Super がそうなった)。 A/B は
    /// <c>Tools/v3_arm.txt 風の設定</c> (<c>v3.xxx=True</c>) で 1 本ずつ入れて、 同じシード・同じ学習状態で対にして比べる。</para>
    ///
    /// <para>効いた案は既定を ON にして Optimal 本体へ畳み、 効かなかった案はここから消す (記録は design-rejects へ)。</para>
    /// </summary>
    [System.Serializable]
    public class V3Flags
    {
        // 実験ごとにフィールドを足す (全部 既定 OFF)。

        /// <summary>E1: リロールを「いまの手の最良効用」と「振り直した手の期待最良効用 − 充電の費用」の比較で決める。
        /// 既定の規則 (面平均を下回る合計なら振る) は手の中身を見ないので、 配線の良し悪しとずれる。
        /// 役が成立した手を捨てない規則と、 充電の予備 (RerollChargeReserve)・1 ターン 1 回は残す。</summary>
        public bool rerollEv;

        // ---- 効用の係数のスイッチ (1 = 既定の値のまま・0 = 無効)。 掃引して A/B にかける ----

        /// <summary>E4: 1 ターン延ばす罰 (<c>TimeCostPerTurn</c> = 4.0) の倍率。</summary>
        public float timeCostMul = 1f;
        /// <summary>E3: 充電の効用 (<c>wChg × 2</c>) の倍率。</summary>
        public float chargeMul = 1f;
        /// <summary>E5: このターンの被ダメージ 1 あたりの罰 (0 = なし)。 効用は HP を失うこと自体のコストを持たない
        /// (防ぎ切りの規則だけが穴埋めしている)。 ラン全体で HP は戦闘をまたいで持ち越すので、 失った分は後で響く。</summary>
        public float takenCost = 0f;
    }
}
