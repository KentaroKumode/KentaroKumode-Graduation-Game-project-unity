namespace CombatSystem
{
    /// <summary>シールドの軸の品のうち、 端子の解決 (<see cref="TerminalCards.Resolve"/> の 5e) の中で効く物と、 【ジャスト防御】の許容幅に効く物 (2026-11-21・full-item-rework-ledger §③)。
    /// 戦闘の始めに所持品から <see cref="TerminalBattleState.shieldItems"/> へ入れる (CombatManager.BeginTerminalDeck・<see cref="ShieldCore.ItemsOf"/>)。</summary>
    [System.Flags]
    public enum ShieldItems
    {
        None = 0,
        Pauldron = 1 << 0,   // 肩当て: 防御の端子に 3 個とも挿したターン シールド +12
        Padded = 1 << 1,     // 当て布の籠手: 防御の端子に 2 個以上挿したターン シールド +6
        Rivet = 1 << 2,      // 鋲の胸当て: 敵の予告攻撃 12 以上のターン シールド +3
        Worn = 1 << 3,       // 擦り減った小盾: 防御の端子に挿したダイス 1 個につきシールド +2・ジャスト防御の許容幅 +2
    }

    /// <summary>
    /// シールドの軸 (2026-11-21・full-item-rework-spec §3 軸3) の式の<b>正本</b>。
    ///
    /// <para><b>【ジャスト防御】</b>: 敵の直接攻撃の予告 E &gt; 0 のターン、 敵の攻撃で HP を 1 も失わず、
    /// 防御 D (ブロック + 敵の攻撃を受ける時のシールド) が E ≤ D ≤ E + 許容幅 (既定 2・〈擦り減った小盾〉で 4) の時に成立。
    /// E は 1 体目の攻撃 (端子の〈大結界〉を掛けた後)。 ダウン・チャージ・行動不能のターン (攻撃しない) と〈狂暴化〉の攻撃は対象外。
    /// 判定は <see cref="IsJustGuard"/> の 1 か所。 本体 (CombatManager の敵の攻撃の解決)・配線の見込みの画面・BOT の見積もりは
    /// どれも CombatManager.JustGuardForecast / この関数を通す。</para>
    /// <para>このファイルは UnityEngine に依存しない。</para>
    /// </summary>
    public static class ShieldCore
    {
        /// <summary>ジャスト防御の許容幅 (既定) / 〈擦り減った小盾〉の上乗せ。</summary>
        public const int JustWidth = 2, WornWidth = 2;
        /// <summary>5e の量: 〈肩当て〉/〈当て布の籠手〉(2 個以上)/〈擦り減った小盾〉(1 個につき)/〈鋲の胸当て〉(予告 12 以上 +3・未満 0)。</summary>
        public const int PauldronShield = 12, PaddedShield = 6, PaddedNeed = 2, WornPerDie = 2, RivetNeed = 12, RivetHigh = 3, RivetLow = 0;

        /// <summary>ジャスト防御の許容幅。</summary>
        public static int Width(ShieldItems f) => JustWidth + ((f & ShieldItems.Worn) != 0 ? WornWidth : 0);

        /// <summary>【ジャスト防御】の判定 (予告 <paramref name="enemyAttack"/>・防御 <paramref name="guard"/> = ブロック + シールド)。 HP を失わなかったかは呼び手が見る。</summary>
        public static bool IsJustGuard(int enemyAttack, int guard, int width)
            => enemyAttack > 0 && guard >= enemyAttack && guard <= enemyAttack + width;

        /// <summary>端子の〈大結界〉を掛けた後の敵の攻撃 (本体 CombatManager.ApplyTerminalOutcome と同じ丸め)。</summary>
        public static int EnemyAttackAfter(TerminalOutcome o, int enemyAttack)
            => o != null && o.enemyAttackMul < 1f ? System.Math.Max(0, (int)System.Math.Round(enemyAttack * o.enemyAttackMul)) : enemyAttack;

        /// <summary>配線の後、 敵の攻撃を受ける時のシールドの見込み (今のシールド − 端子が払う分 + 端子が足す分 + 〈応急箱＋〉の溢れ)。 本体の ApplyTerminalOutcome の順。</summary>
        public static int ShieldAfterWiring(TerminalOutcome o, in TerminalEnv env)
        {
            if (o == null) return env.shield;
            int over = o.overhealToShield > 0 ? System.Math.Max(0, o.overhealToShield - System.Math.Max(0, env.maxHp - env.hp)) : 0;
            return System.Math.Max(0, env.shield + over - o.shieldSpend) + o.shield;
        }

        /// <summary>所持品からシールドの品の旗を作る (戦闘の始め)。</summary>
        public static ShieldItems ItemsOf(GameLoop.RunState run)
        {
            var f = ShieldItems.None;
            void Own(string id, ShieldItems b) { if (ItemSystem.PassiveSkills.AxisRules.Owns(run, id)) f |= b; }
            Own(GameLoop.ItemIds.ShieldPauldron, ShieldItems.Pauldron); Own(GameLoop.ItemIds.ShieldPadded, ShieldItems.Padded);
            Own(GameLoop.ItemIds.ShieldRivet, ShieldItems.Rivet); Own(GameLoop.ItemIds.ShieldWorn, ShieldItems.Worn);
            return f;
        }

        /// <summary>Resolve の 5e (毒の 5d の後)。 シールドを端子の結果へ足す (本体・画面の見込み・BOT が同じ)。 本体は ApplyTerminalOutcome で ctx.consShield へ入れる。</summary>
        internal static void ApplyToTerminals(int[] dice, in TerminalEnv env, TerminalBattleState st, TerminalOutcome o)
        {
            var f = st.shieldItems;
            if (f == ShieldItems.None) return;
            int b = o.blockDice;
            if ((f & ShieldItems.Pauldron) != 0 && dice != null && b >= dice.Length) o.shield += PauldronShield;
            if ((f & ShieldItems.Padded) != 0 && b >= PaddedNeed) o.shield += PaddedShield;
            if ((f & ShieldItems.Worn) != 0 && b > 0) o.shield += WornPerDie * b;
            if ((f & ShieldItems.Rivet) != 0) o.shield += env.enemyAttack >= RivetNeed ? RivetHigh : RivetLow;
        }
    }
}
