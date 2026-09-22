namespace GameLoop
{
    /// <summary>7層終端〈門〉を不完全に起動したことによる欠陥の、<b>数値の正本</b> (2026-09-14)。
    ///
    /// <para>効果は 3 つとも<b>プレイヤー側の規則</b>で、 敵は一切強化しない。
    /// 旧 SinDebuff は「ボスに HP と攻撃を足す」形だったが、 それだと
    /// <b>払わなかった罰と、 払わずに残した戦力が同じ軸で相殺され</b>、
    /// 結局どちらも「数字の増減」にしかならなかった。</para>
    ///
    /// <para><b>刺さる軸が 3 つとも違う。</b>
    /// 修復 = 立て直し (回復) / 起動 = 出力 (賽の本数と役) / 転移 = 受け (ブロック)。</para>
    ///
    /// <para><b>効くのは門をくぐった後だけ。</b> 判定は「フラグを持っているか」で、
    /// 実際には門は 7 層の最終マス＝以降の戦闘は 8 層ヴェスカ戦しか無いので、
    /// 敵 ID での絞り込みはしない (絞ると「どの ID までが最終戦か」という
    /// 4 段連戦の分岐条件を各所に複製することになる)。</para>
    ///
    /// <para><b>較正の履歴 (2026-09-14 / Balanced 10,000 ラン × 6 アーム)。</b>
    /// 初版は 修復 = 充電が毎ターン半減 / 起動 = 同一端子 2 本上限 / 転移 = ブロック 50% 貫通。
    /// 8 層ボス勝率で測ると<b>代償の合計 −11.2pt に対し罰の合計が −3.7pt</b> しかなく、
    /// 血と遺物は<b>踏み倒すのが正解</b>だった (払う方が 4.9 / 5.0pt 損)。
    /// 目標は<b>どの工程も「払う方が 2〜4pt 得」</b>(8 層ボス勝率)。
    /// ・充電半減は Optimal 方策が充電をリロールにしか使わないため効かなかった → 回復へ差し替え。
    /// ・端子 2 本上限は 5 ダイスなら 2+2+1 で置けてしまい、 全振りとの差が小さかった。
    ///   1 本上限は<b>3 端子に 5 本置けず配線不能</b>になるので採れない → 賽の本数へ差し替え。
    /// ・転移だけは唯一釣り合っていた (払う方が 5.0pt 得) ので 50% → 30% に下げて帯へ入れる。</para></summary>
    public static class GateFlaws
    {
        // ── 〈不完全な修復〉傷が塞がらない ───────────────────────────
        //   血＝生命を回さなかったので、 門は身体の縁を繋ぎ直せなかった。

        /// <summary>回復量の倍率。 <b>シールドには掛けない</b> ──
        /// <c>ctx.consShield</c> への加算は 13 箇所に散っていて絞り込み点が無く、
        /// 全部に足すと必ずどれかが漏れる。 回復は <c>CombatManager.HealPlayer</c> の
        /// 一点を通るので、 <b>そこだけで完結する形</b>に閉じてある。</summary>
        public const float RepairHealMult = 0.5f;

        /// <summary>回復量へ〈不完全な修復〉を適用する。</summary>
        public static int ApplyRepairPenalty(RunState run, int amount)
        {
            if (run == null || amount <= 0 || !run.HasFlaw(GateFlaw.IncompleteRepair)) return amount;
            return UnityEngine.Mathf.Max(0, UnityEngine.Mathf.FloorToInt(amount * RepairHealMult));
        }

        /// <summary>毎ターン終了時に失う HP (最大HP に対する割合)。 <b>軽減不可。</b>
        ///
        /// <para>回復半減だけでは 1.3pt しか効かなかった (実測)。 最終戦は回復薬で支える
        /// 構造になっていないので、 回復は罰の軸として機能しない。 <b>時間を軸にする</b> ──
        /// 縁の直っていない身体は、 向こう側に長く居るほど保たない。
        /// 〈不完全な転移〉(ブロック貫通＝守りを罰する) とも軸が被らない。</para>
        ///
        /// <para><b>static フィールドで const ではない。</b> 較正スイープが 1 バッチで
        /// 複数の倍率を掃けるようにするため。 製品の値はここの既定値で、
        /// 書き換えるのは <c>AutoRunner.gateRepairDrainPct</c> だけ
        /// (実効値は <c>[実効状態]</c> に印字される)。</para></summary>
        public static float RepairHpDrainPerTurnPct = 0.02f;

        /// <summary>ターン終了時に失う HP。 0 なら何もしない。</summary>
        public static int RepairDrainAmount(RunState run, int playerMaxHp)
        {
            if (run == null || playerMaxHp <= 0 || !run.HasFlaw(GateFlaw.IncompleteRepair)) return 0;
            if (RepairHpDrainPerTurnPct <= 0f) return 0;
            return UnityEngine.Mathf.Max(1,
                UnityEngine.Mathf.FloorToInt(playerMaxHp * RepairHpDrainPerTurnPct));
        }

        // ── 〈不完全な起動〉出力が足りない ───────────────────────────
        //   遺物を焚かなかったので回路が繋がりきらず、 攻撃端子へ電力が回らない。

        /// <summary>攻撃の基礎値 (atkBase) から差し引く割合。
        ///
        /// <para><b>2026-09-14: 攻撃端子の合計 → atkBase へ対象を広げた。</b>
        /// 端子だけに掛けると梃子が弱すぎる ── 実測で atkBase 60 のうち
        /// 攻撃端子出目は 17 しかなく (残り 39 はパッシブ加算)、 端子 25% カットでも
        /// atkBase は 7% しか減らない。 実際 cut 0.25 で<b>払う方が 2.8pt 損</b>のままだった。
        /// 「出力が足りない」は素直に読めば出力全体の話なので、 atkBase に掛ける。</para>
        ///
        /// <para><b>賽を 1 本減らす案から差し替えた (2026-09-14)。</b> 5 → 4 は実測で
        /// 10.5pt と重すぎたうえ、 <b>整数なので刻めない</b>。 端子ごとの本数上限
        /// (2 本) は 5 ダイスなら 2+2+1 で置けてしまい軽く、 1 本上限は
        /// <b>3 端子へ 5 本置けず配線不能</b>になるので採れない。
        /// 割合なら目標帯へ合わせられる。</para>
        ///
        /// <para>攻撃側<b>だけ</b>に掛ける ── ブロックにも掛けると
        /// 〈不完全な転移〉と軸が重なる。</para></summary>
        public static float IgnitionAttackCutPct = 0.12f;

        /// <summary>atkBase へ〈不完全な起動〉を適用した後の値。</summary>
        public static int ApplyIgnitionCut(RunState run, int atkBase)
        {
            if (run == null || atkBase <= 0 || !run.HasFlaw(GateFlaw.IncompleteIgnition)) return atkBase;
            if (IgnitionAttackCutPct <= 0f) return atkBase;
            return UnityEngine.Mathf.Max(0,
                atkBase - UnityEngine.Mathf.FloorToInt(atkBase * IgnitionAttackCutPct));
        }

        // ── 〈不完全な転移〉身体がずれて届く ─────────────────────────

        /// <summary>貫通率。 ブロック合計のこの割合が無効になる。
        ///
        /// <para><b>2026-09-14: 0.50 → 0.30。</b> 3 工程のうち<b>ここだけが釣り合っていた</b>
        /// (払う方が 5.0pt 得) ので、 目標帯 2〜4pt へ入れるために下げた。
        /// 〈貫きの錐〉(GOLD 遺物) が「ブロックを丸ごと 0 にする」形で致死率を基準の
        /// 2.80 倍まで押し上げた前例があるので、 <b>全部消す形は採らない</b>。
        /// 割合なら厚く配線した分は必ず残る ＝「守り切れるか」の計算問題として成立する。</para></summary>
        public static float TransferPierceRate = 0.30f;

        /// <summary>ブロック合計へ貫通を適用した後の値。</summary>
        public static int ApplyTransferPierce(RunState run, int blockSum)
        {
            if (run == null || blockSum <= 0 || !run.HasFlaw(GateFlaw.IncompleteTransfer)) return blockSum;
            int pierced = UnityEngine.Mathf.FloorToInt(blockSum * TransferPierceRate);
            return UnityEngine.Mathf.Max(0, blockSum - pierced);
        }
    }
}
