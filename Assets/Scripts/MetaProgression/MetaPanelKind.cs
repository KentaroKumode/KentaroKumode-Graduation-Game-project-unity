namespace MetaProgression
{
    /// <summary>
    /// 整備パネルのトラック識別子 (18 個)。 うち Lantern / Precision / SparkCrit は廃止 (MaxRank 0 = 枠に出ない)。
    /// 数値系 max 10 (Shell / Output / Guard / Vault / Plunder / Trade / Supply / Armor) +
    /// 宣言系 max 3 (種火 5 + 端子 2)。 <b>enum は並べ替えも削除もしない</b> (panelRanks は enum のインデックスで引く)。
    /// 旧 #16〈予見〉は ADR-0010 でパリィごと廃止した (enum 末尾だったのでセーブ移行は不要)。
    /// <b>段ごとの数値の正本は MetaPanel.cs</b> (下の行のコメントは何のトラックかの目印だけ。 数値は写さない)。
    /// 画面の説明文は Game2DMetaPanel。 仕様: docs/GAME.md §15-1
    /// </summary>
    public enum MetaPanelKind
    {
        // === 数値系 (max 10) ===
        Shell,        // #1 外殻:  最大HP (MetaPanel.ShellHp)、 r10: ラストスタンド
        Output,       // #2 出力:  与ダメ % (OutputPct)、 r10:〈戦意〉
        Guard,        // #3 剛胆 (旧〈防御〉・開幕シールドは 2026-08-15 廃止):  エリートの格上げ・報酬・ドロップ (Valor*)、 r10: エリートへの与ダメ
        Vault,        // #4 金庫:  開幕ゴールド (VaultGold)、 r10: 宝箱ゴールド復活・預け入れ
        Plunder,      // #5 強奪:  ボス撃破の追加ゴールド・パッシブドロップ (Plunder*)、 r10: ショップ強盗 解禁
        Trade,        // #6 商才:  特売枠 (TradeSale*)、 r10: 99%引きの枠が出る
        Supply,       // #7 宝物庫:  開幕パッシブの本数とレア度 (Supply*)、 r10: 開幕パッシブが選択式
        Lantern,      // #8 兵站 (旧〈燈火〉):  2026-11-10 に枠から外した (MaxRank 0)。 物資の上限は素の値で固定。 後継は末尾の〈装甲〉
        Precision,    // #9 精密:  2026-09-10 廃止 (MaxRank 0)。 旧: 会心ダイス・会心率 +%/段

        // === 種火 (キーワードビルド宣言・max 3) ===
        //   r1: 対応キーワードアイテムの出現重み (SparkAppearanceWeightMul)
        //   r2: 固有効果 (下記)
        //   r3: 対応キーワードアイテムを開幕所持 (SparkStartingItemCount 個)
        SparkCharge,  // #10 充電:   r2 拡張バッテリー (充電上限 + と開幕の充電・ChargeCapBonus / ChargeOpening)
        SparkRinkai,  // #11 臨界:   r2 保温炉 (戦闘終了時メーターの50%を次戦闘へ持ち越し)
        SparkPoison,  // #12 毒:     r2 濃縮 (毒付与量 +・PoisonApplyBonus)
        SparkBleed,   // #13 出血:   r2 失血衰弱 (出血が溜まった敵の攻撃値 −・BleedWeakenAttack)

        // === 端子調律 (max 3) ===
        AttackTerm,   // #14 攻端子: 攻撃端子に挿したダイス 1 個ごとに配線合計 +1/段
        BlockTerm,    // #15 防端子: 防御端子に挿したダイス 1 個ごとに配線合計 +1/段

        // === 種火の追加 (2026-11-07・軸の再設計で会心とシールドも軸になったため)。 enum の末尾に足す ──
        //   panelRanks は int[] を enum のインデックスで引くので、 途中に挟むと保存済みのランクがずれる。
        SparkCrit,    // #16 会心:   2026-11-07 廃止 (MaxRank 0)。 旧 r2 研ぎ澄まし (会心倍率 +)
        SparkShield,  // #17 シールド: r2 厚手の芯 (ターン開始の減衰で残るシールド +%・ShieldKeepBonusPct)

        // === 2026-11-10 追加 (数値系・max 10)。 末尾に足す (保存済みランクの番号を保つ) ===
        Armor,        // #18 装甲:  防御 (ArmorDefense)・被ダメージ −N (ArmorFlatReduction)、 r10〈不動〉: 一撃の上限 (ArmorHitCap)
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
            // **会心の種火も廃止 (2026-11-07)。** 会心はどのビルドも最初から使う共通の資源で、 種火で品を増やしても
            //   Balanced に対して +1.6 / −0.7 ポイント (有意でない)。 MaxRank 0 = 枠から消える。 enum は保存済みランクがずれるので残す。
            MetaPanelKind.SparkCrit => 0,
            // **兵站も廃止 (2026-11-10)。** 物資の上限は「頭打ち (約 10〜11) の手前か後か」で価値が反転し、 段の効き目を設計で制御できなかった
            //   (素の上限 8 で r3→r10 の 7 点は −11〜−20、 素の上限 5 なら +2〜+4)。 物資の上限は素の 8 で固定し、 後継は〈装甲〉。
            MetaPanelKind.Lantern => 0,
            MetaPanelKind.Shell or MetaPanelKind.Output or MetaPanelKind.Guard or
            MetaPanelKind.Vault or MetaPanelKind.Plunder or MetaPanelKind.Trade or
            MetaPanelKind.Supply or MetaPanelKind.Armor => 10,
            _ => 3,
        };

    }
}
