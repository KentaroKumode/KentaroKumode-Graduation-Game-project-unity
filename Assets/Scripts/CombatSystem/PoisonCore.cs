namespace CombatSystem
{
    /// <summary>毒の軸の品のうち、 端子の解決 (<see cref="TerminalCards.Resolve"/> の 5d) の中で効く物と、 起爆の式に効く物 (2026-11-21・full-item-rework-ledger §②)。
    /// 戦闘の始めに所持品から <see cref="TerminalBattleState.poisonItems"/> へ入れる (CombatManager.BeginTerminalDeck・<see cref="PoisonCore.ItemsOf"/>)。</summary>
    [System.Flags]
    public enum PoisonItems
    {
        None = 0,
        GreenKnife = 1 << 0,   // 緑染みの小刀: 攻撃の端子の発動 1 回につき毒 +2 (最大 3 回)
        Serpent = 1 << 1,      // 蛇血瓶: 4 以上の目を挿した攻撃の端子 1 枚につき毒 +3 (最大 3 回)
        Taster = 1 << 2,       // 毒見役の小匙: 攻撃と防御の両方に挿したターン 毒 +4・HP +2
        Thorn = 1 << 3,        // 毒棘の小盾: 防御の端子に挿したターン ブロック +6
        Mantle = 1 << 4,       // 瘴気の外套: 毒 15 以上で受けるダメージ −35%
        Blowpipe = 1 << 5,     // 麻痺の吹き矢: 毒 18 以上で敵の防御を全部無視
        Ring = 1 << 6,         // 蓋付きの毒指輪: 毒 24 以上で攻撃が当たると【起爆】 (2026-11-22 neon 収束: 16 → 24)
        Magazine = 1 << 7,     // 劇毒の火薬庫: 起爆の倍率 ×5・敵の最大 HP の 0.32% / 毒
        SlowBlade = 1 << 8,    // 遅効の毒刃: 起爆のダメージ +30% × この戦闘の起爆の回数
        Oil = 1 << 9,          // 骨溶かしの黒油: ダウン中・よろめき中の毒のダメージと起爆 ×2・よろめきにも ×1.5・ダウンさせたとき毒 +8
    }

    /// <summary>
    /// 毒の軸 (2026-11-21・full-item-rework-spec §3 軸2) の式の<b>正本</b>。
    ///
    /// <para><b>【起爆】</b>: 敵の毒を 1/3 (切り上げ・2026-11-22 neon 収束: 旧 半分) 消費し、 max(消費量 × 倍率 (既定 ×4), floor(消費量 × 敵の最大 HP × 0.25%)) の軽減不可ダメージ (防御も無視)。
    /// 最大 HP の項は 2026-11-22 neon 収束 (HP の大きいボスで起爆が細る分を補う)。
    /// 起爆したとき、 ボスの次の目盛りをその起爆のダメージの 起爆のダメージの割合ぶん (2026-11-26 に混乱 +<see cref="DetonateGauge"/> へ)上げる (今の HP の 1 下まで・ひびは割らない・2026-11-25 作者の決定 / neon answer_gauge)。
    /// 起爆は品が起こす (〈蓋付きの毒指輪〉〈劇毒の火薬庫〉)。 ダメージの式はここだけにあり、 本体 (PoisonEffects の Psn.Detonate) と BOT の見積もり (AutoRunner の ItemSight) が同じ関数を呼ぶ。</para>
    /// <para>このファイルは UnityEngine に依存しない。</para>
    /// </summary>
    public static class PoisonCore
    {
        /// <summary>起爆の倍率 (消費した毒 1 につき) / 〈劇毒の火薬庫〉を持つ時の倍率。</summary>
        public const int DetonateMul = 4, MagazineMul = 5;
        /// <summary>起爆の最大 HP の項: 消費した毒 1 につき 敵の最大 HP × これ / 10000 (既定 25 = 0.25% / 〈劇毒の火薬庫〉32 = 0.32%・2026-11-22 neon 収束)。</summary>
        public const int DetonateMaxHpBp = 25, MagazineMaxHpBp = 32;
        /// <summary>起爆で消費する毒の割合の分母 (毒 ÷ これ・切り上げ・2026-11-22 neon 収束: 2 → 3)。</summary>
        public const int SpendDiv = 3;
        /// <summary>〈蓋付きの毒指輪〉: 起爆に要る敵の毒 (2026-11-27: 26) / 発動に要る毒秘宝数 (6・他軸侵入封じ) / 開幕暗殺ターン数 / その間の付与毒 +N・劇症ダメージ +% (25)。</summary>
        public const int RingNeed = 26, RingMinPoisonRelics = 6, RingAssassinTurns = 5, RingBonusPoison = 2, RingEarlyPct = 25;
        /// <summary>起爆の最大 HP 割合項を 100% 発揮するのに要る毒秘宝数 (他軸つまみ食い封じ・2026-11-27)。</summary>
        public const int DetonateMaxHpFullRelics = 5;
        /// <summary>〈劇毒の火薬庫〉: 1 ターンに与えた毒の合計がこれ以上、かつ敵の毒が <see cref="MagazineMinPoison"/> 以上なら、ターンの終わりに起爆 (2026-11-26 下限追加)。</summary>
        public const int MagazineGiven = 8, MagazineMinPoison = 18;
        /// <summary>〈遅効の毒刃〉: この戦闘で起爆した回数 1 回につき、 起爆のダメージ +% (最大 +150%・2026-11-26 上限追加) / 劇症後の即死ライン倍率 (毒 × 2、ボスは半分 = × 1)。</summary>
        public const int SlowBladePctPerDetonate = 30, SlowBladeMaxPct = 150, SlowBladeKillMul = 2;
        /// <summary>〈骨溶かしの黒油〉: ダウン中の毒のダメージ・起爆の倍率。</summary>
        public const int OilDownMul = 2;
        /// <summary>【起爆】でボスの次の目盛りを上げる割合 (起爆のダメージの %) / 〈骨溶かしの黒油〉: ボスをダウンさせたとき敵に与える毒 (2026-11-25 neon answer_gauge)。</summary>
        public const int DetonateGauge = 3, OilDownPoison = 8;   // 2026-11-26: 起爆のダメージの 35% の目盛り → 混乱 +3
        /// <summary>〈瘴気の外套〉: 受けるダメージ −35% の毒 / ターンの終わりの消費しない起爆 (威力 50%) の毒。</summary>
        public const int MantleNeed = 15, MantleCutPct = 35, MantleEchoNeed = 25, MantleEchoPct = 50;
        /// <summary>〈毒溜まりの主〉: ターン開始の毒のダメージが 2 回になる毒。</summary>
        public const int PoolNeed = 20;
        /// <summary>出来事 OnPoisonTick を配っている間 1: この毒のダメージの後に毒が減る (ターン開始の 1 回目だけ・〈瘴気溜まりの壺〉が読む)。
        /// 〈廃庭の霧吹き〉の分・〈毒溜まりの主〉の 2 回目は 0。</summary>
        public const string DecayTickKey = "psn.decayTick";
        /// <summary>〈麻痺の吹き矢〉: 敵の防御を全部無視する毒。</summary>
        public const int BlowpipeIgnoreAt = 12;
        /// <summary>5d の量: 〈緑染みの小刀〉毒 / 回数 ・〈蛇血瓶〉毒 / 目 / 回数・〈毒見役の小匙〉毒 / HP・〈毒棘の小盾〉ブロック。</summary>
        public const int KnifePoison = 3, KnifeMax = 3, SerpentPoison = 4, SerpentFace = 4, SerpentMax = 3, TasterPoison = 3, TasterHeal = 1, ThornBlock = 6;

        /// <summary>起爆で消費する毒 (1/3・切り上げ)。</summary>
        public static int Spend(int poison) => poison > 0 ? (poison + SpendDiv - 1) / SpendDiv : 0;

        /// <summary>起爆 1 回のダメージ (消費量 <paramref name="spent"/>・この戦闘でここまでに起爆した回数 <paramref name="before"/>・ダウン中 <paramref name="down"/>・
        /// 敵の最大 HP <paramref name="enemyMaxHp"/> (ctx.enemyMaxHP・ヴェスカは段ごとの最大 HP)・威力 <paramref name="powerPct"/>%・ターン <paramref name="turn"/>)。 切り捨て。</summary>
        public static int Damage(int spent, PoisonItems f, int before, bool down, int enemyMaxHp, int powerPct = 100, int turn = 99, int poisonRelics = DetonateMaxHpFullRelics)
        {
            if (spent <= 0) return 0;
            bool mag = (f & PoisonItems.Magazine) != 0;
            long flat = (long)spent * (mag ? MagazineMul : DetonateMul);
            int relScale = System.Math.Min(DetonateMaxHpFullRelics, System.Math.Max(0, poisonRelics));
            long maxHpTerm = (long)spent * System.Math.Max(0, enemyMaxHp) * (mag ? MagazineMaxHpBp : DetonateMaxHpBp) * relScale / (10000L * DetonateMaxHpFullRelics);
            long d = System.Math.Max(flat, maxHpTerm);
            if (CardRules.DetonateMulTenths > 0) d += (long)spent * CardRules.DetonateMulTenths / 10;   // 挑戦カード〈毒仕込み〉: 倍率 +0.5 / +1.0 / +1.5 (切り捨て・最大 HP の項との大きい方に足す)
            if ((f & PoisonItems.Ring) != 0 && turn >= 1 && turn <= RingAssassinTurns) d = d * (100 + RingEarlyPct) / 100;   // 〈蓋付きの毒指輪〉: 1〜5 ターン目 +50%
            if ((f & PoisonItems.SlowBlade) != 0 && before > 0) d = d * (100 + System.Math.Min(SlowBladeMaxPct, SlowBladePctPerDetonate * before)) / 100;
            if ((f & PoisonItems.Oil) != 0 && down) d *= OilDownMul;
            return (int)System.Math.Min(int.MaxValue, d * powerPct / 100);
        }

        /// <summary>〈遅効の毒刃〉: 劇症ダメージ後の即死 HP ライン (現在の残り毒 × 2、ボスはその半分 = × 1)。 0 以下なら不発。</summary>
        public static int SlowBladeKillLine(int currentPoison, bool boss)
            => currentPoison <= 0 ? -1 : boss ? currentPoison : currentPoison * SlowBladeKillMul;

        /// <summary>所持品から毒の品の旗を作る (戦闘の始め)。</summary>
        public static PoisonItems ItemsOf(GameLoop.RunState run)
        {
            var f = PoisonItems.None;
            void Own(string id, PoisonItems b) { if (ItemSystem.PassiveSkills.AxisRules.Owns(run, id)) f |= b; }
            Own(GameLoop.ItemIds.PoisonGreenKnife, PoisonItems.GreenKnife); Own(GameLoop.ItemIds.PoisonSerpent, PoisonItems.Serpent);
            Own(GameLoop.ItemIds.PoisonTaster, PoisonItems.Taster); Own(GameLoop.ItemIds.PoisonThorn, PoisonItems.Thorn);
            Own(GameLoop.ItemIds.PoisonMantle, PoisonItems.Mantle); Own(GameLoop.ItemIds.PoisonBlowpipe, PoisonItems.Blowpipe);
            Own(GameLoop.ItemIds.PoisonRing, PoisonItems.Ring); Own(GameLoop.ItemIds.PoisonMagazine, PoisonItems.Magazine);
            Own(GameLoop.ItemIds.PoisonSlowBlade, PoisonItems.SlowBlade); Own(GameLoop.ItemIds.PoisonOil, PoisonItems.Oil);
            return f;
        }

        /// <summary>Resolve の 5d (全部の発動・再起動が決まった後)。 毒・回復・ブロック・被ダメ・防御無視を端子の結果へ足す (本体・画面の見込み・BOT が同じ)。
        /// <paramref name="dice"/> = 振った目 (〈蛇血瓶〉は挿した目そのもので見る)。</summary>
        internal static void ApplyToTerminals(TerminalDef[] defs, int[] dice, in TerminalEnv env, TerminalBattleState st, TerminalOutcome o)
        {
            var f = st.poisonItems;
            if (f == PoisonItems.None) return;
            int hits = o.attackHits.Count;      // 攻撃の端子の発動 (再起動の分も 1 回)
            if ((f & PoisonItems.GreenKnife) != 0 && hits > 0) o.poison += KnifePoison * System.Math.Min(KnifeMax, hits);
            if ((f & PoisonItems.Serpent) != 0 && hits > 0)
            {
                int n = 0;
                foreach (int s in o.fired)
                {
                    if (n >= SerpentMax || defs[s] == null || defs[s].kind != TerminalKind.Attack) continue;
                    for (int i = 0; i < dice.Length && i < o.slotOfDie.Length; i++)
                        if (o.slotOfDie[i] == s && dice[i] >= SerpentFace) { n++; break; }
                }
                o.poison += SerpentPoison * n;
            }
            if ((f & PoisonItems.Taster) != 0 && o.attackDice > 0 && o.blockDice > 0) { o.poison += TasterPoison; o.heal += TasterHeal; }
            if ((f & PoisonItems.Thorn) != 0 && o.blockDice > 0) o.blockAdd += ThornBlock;
            if ((f & PoisonItems.Mantle) != 0 && env.enemyPoison >= MantleNeed) o.takenMul *= (100 - MantleCutPct) / 100f;
            if ((f & PoisonItems.Blowpipe) != 0 && env.enemyPoison >= BlowpipeIgnoreAt) o.ignoreDefense = true;
        }
    }
}
