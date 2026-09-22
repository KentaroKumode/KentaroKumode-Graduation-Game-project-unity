namespace MetaProgression
{
    /// <summary>
    /// 整備パネルの15トラック識別子。
    /// 数値系9 (max 10) + 宣言系6 (種火4 + 端子2, max 3)。
    /// #16〈予見〉は ADR-0010 でパリィごと廃止した。 **enum 末尾だったので
    /// セーブ移行は不要** (EnsurePanelRanks が配列長の差を吸収する)。
    /// 正本仕様: docs/GAME.md §15-1
    /// </summary>
    public enum MetaPanelKind
    {
        // === 数値系 (max 10) ===
        Shell,        // #1 外殻:  最大HP +3/段
        Output,       // #2 出力:  与ダメ +5%/段
        Guard,        // #3 防御:  開幕シールド +3/段、r5:被ダメ-1、r10:被ダメ-2 (累計)
        Vault,        // #4 金庫:  開幕ゴールド +5/段、r10:宝箱ゴールド復活
        Plunder,      // #5 強奪:  戦闘勝利ゴールド+1/段、r10:ショップ強盗 解禁
        Trade,        // #6 商才:  特売枠+1/段 (割引 15/30/50%)、r10:99%引きの枠が出る
        Supply,       // #7 兵站:  r2/5/8 素材+1、r10:開幕パッシブ
        Lantern,      // #8 燈火:  希望上限+10/段、r10:横移動の希望消費 0
        Precision,    // #9 精密:  r3/6/9 会心ダイス+1、全段 会心率+0.55%/段 (r10 で +5.5%)

        // === 種火 (キーワードビルド宣言・max 3) ===
        //   r1: 対応キーワードアイテムの出現重み +60%
        //   r2: 固有効果 (下記)
        //   r3: 対応キーワードアイテムを1つ (BRONZE) 開幕所持
        SparkCharge,  // #10 充電:   r2 拡張バッテリー (充電上限 10→15)
        SparkRinkai,  // #11 臨界:   r2 保温炉 (戦闘終了時メーターの50%を次戦闘へ持ち越し)
        SparkPoison,  // #12 毒:     r2 濃縮 (毒付与量 +1)
        SparkBleed,   // #13 出血:   r2 失血衰弱 (出血3以上の敵の攻撃値 -2)

        // === 端子調律 (max 3) ===
        AttackTerm,   // #14 攻端子: 攻撃端子の配線合計 +1/段
        BlockTerm,    // #15 防端子: 防御端子の配線合計 +1/段
    }

    public static class MetaPanelKindExt
    {
        /// <summary>そのトラックの最大段数 (数値系=10, 宣言系=3)。</summary>
        public static int MaxRank(this MetaPanelKind k) => k switch
        {
            // **精密は廃止 (2026-09-10)。** MaxRank 0 = 枠から消える。
            //   enum メンバーは**残す** ── panelRanks は int[] を enum のインデックスで
            //   引いているので、 中間の #9 を削除すると種火・端子の保存済みランクが
            //   1 つずれて別トラックに化ける。
            MetaPanelKind.Precision => 0,
            MetaPanelKind.Shell or MetaPanelKind.Output or MetaPanelKind.Guard or
            MetaPanelKind.Vault or MetaPanelKind.Plunder or MetaPanelKind.Trade or
            MetaPanelKind.Supply or MetaPanelKind.Lantern => 10,
            _ => 3,
        };

        /// <summary>数値系 (max 10) か否か。false = 宣言系 (max 3)。</summary>
        public static bool IsNumeric(this MetaPanelKind k) => k.MaxRank() == 10;
    }
}
