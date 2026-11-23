namespace CombatSystem
{
    /// <summary>充電の軸の品のうち、 端子の解決 (<see cref="TerminalCards.Resolve"/>) の中で効く物 (2026-11-21・full-item-rework-ledger §①)。
    /// 戦闘の始めに所持品から <see cref="TerminalBattleState.chargeItems"/> へ入れる (CombatManager.BeginTerminalDeck・<see cref="ChargeCore.ItemsOf"/>)。</summary>
    [System.Flags]
    public enum ChargeItems
    {
        None = 0,
        Fork = 1 << 0,     // 二股の導線: 【再起動】
        Rod = 1 << 1,      // 逆さ避雷針: 被ダメ −6 と防御の端子の自動再起動
        Whip = 1 << 2,     // 灼線鞭: 充電を 2 回以上使ったターンの無料の再起動
        Drum = 1 << 3,     // 雷鳴の太鼓: 【オーバークロック】
    }

    /// <summary>
    /// 充電 = 戦闘のマナ (2026-11-21・full-item-rework-spec §3 軸1) の決まりの<b>正本</b>。 本体 (CombatManager)・画面の見込み・BOT の見積もりは
    /// どれも <see cref="TerminalCards.Resolve"/> を通るので、 再起動とオーバークロックの式はそこにだけある (ここは数と旗)。
    ///
    /// <para><b>【再起動】</b>: そのターンに発動した端子 1 枚の効果を、 ダイス不要でもう 1 回走らせる (攻撃の端子なら撃が 1 つ増える・挿した出目の数は増えない)。
    /// <b>【オーバークロック】</b>: 攻撃の端子を挿したターン、 充電を一括で払い、 最初の 1 撃の最終与ダメージ +N%・敵の防御を全部無視 (本体の撃の輪が掛ける)。</para>
    /// </summary>
    public static class ChargeCore
    {
        /// <summary>過充電 = 充電がこれ以上 (2026-11-21〜・旧 7)。 <see cref="ItemSystem.PassiveSkills.CombatContext.IsOvercharged"/> が読む。</summary>
        public const int OverchargeAt = 10;
        /// <summary>〈過負荷チューナー〉: 過充電の間、 充電を払う量 (2 以上の物) から引く量 (最低 1)。</summary>
        public const int TunerCut = 2;
        /// <summary>〈二股の導線〉【再起動】の充電 (1 ターン 1 回)。</summary>
        public const int RebootCost = 6;   // answer13: 5 → 7・answer14: 7 → 6
        /// <summary>〈逆さ避雷針〉: 払う充電 / 被ダメージから引く量 (1 ターン 1 回)。</summary>
        public const int RodCost = 5, RodCut = 6;
        /// <summary>〈雷鳴の太鼓〉: 充電の上限の上乗せ / オーバークロックに要る充電 / 払う充電 / 最初の 1 撃の最終与ダメージ +% (1 ターン 1 回)。</summary>
        public const int DrumCapAdd = 15, OverclockNeed = 15, OverclockCost = 12, OverclockFinalPct = 100;   // answer15: +80% → +100%

        /// <summary>再起動の対象の指定: 再起動しない / おまかせ (<see cref="AutoValue"/> の最大・BOT はこれ)。 0 以上 = 場の添字。</summary>
        public const int RebootNone = -1, RebootAuto = -2;

        /// <summary>払う量 n を〈過負荷チューナー〉の引き量 cut で減らす (2 以上の物だけ・最低 1)。 端子・秘宝・振り直しの全部がこれを通す。</summary>
        public static int CostNow(int n, int cut) => n > 1 && cut > 0 ? System.Math.Max(1, n - cut) : n;

        /// <summary>おまかせの再起動の値: 攻撃の量 + 敵の攻撃に足りない分を埋めるブロック。 最大の端子を選ぶ (0 なら選ばない)。</summary>
        public static int AutoValue(int atkDelta, int blkDelta, int enemyAttack, int blockSoFar)
            => atkDelta + System.Math.Min(blkDelta, System.Math.Max(0, enemyAttack - blockSoFar));

        /// <summary>所持品から充電の品の旗を作る (戦闘の始め)。</summary>
        public static ChargeItems ItemsOf(GameLoop.RunState run)
        {
            var f = ChargeItems.None;
            if (ItemSystem.PassiveSkills.AxisRules.Owns(run, GameLoop.ItemIds.ChargeFork)) f |= ChargeItems.Fork;
            if (ItemSystem.PassiveSkills.AxisRules.Owns(run, GameLoop.ItemIds.ChargeRod)) f |= ChargeItems.Rod;
            if (ItemSystem.PassiveSkills.AxisRules.Owns(run, GameLoop.ItemIds.ChargeWhip)) f |= ChargeItems.Whip;
            if (ItemSystem.PassiveSkills.AxisRules.Owns(run, GameLoop.ItemIds.ChargeDrum)) f |= ChargeItems.Drum;
            return f;
        }
    }
}
