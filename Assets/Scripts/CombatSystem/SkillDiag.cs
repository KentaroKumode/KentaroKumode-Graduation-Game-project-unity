namespace CombatSystem
{
    /// <summary>
    /// [計装 2026-09-19] 戦闘の種類別 (雑魚 / エリート / ボス) の結果。
    ///
    /// <para><b>技量がどの戦闘で効いているかを測るためのもの。</b> 配線方策 (Naive / Optimal / Super) を
    /// 替えたアームどうしで、 種類ごとの 1 戦あたり被ダメ・死亡率を比べる。 クリア率はビルドの分岐で
    /// ばらけるので、 戦闘単位の数字で見る。 バッチ累計 ── worker が Reset して返す。</para>
    /// </summary>
    public static class SkillDiag
    {
        public const int Normal = 0, Elite = 1, Boss = 2, Kinds = 3;

        public static readonly long[] Fights = new long[Kinds];
        public static readonly long[] Deaths = new long[Kinds];
        public static readonly long[] Turns = new long[Kinds];
        /// <summary>被ダメ (開始 HP − 終了 HP、 回復で増えた分は負になる) を開始時の最大 HP で割った値の和。</summary>
        public static readonly double[] HpLostPct = new double[Kinds];

        /// <summary>ボス別の [戦闘数, 総ターン, 10 ターン未満で終わった数, 死亡数] (2026-09-19)。
        /// 目標「1 層以外のボス戦は最低 10 ターン」を、 平均でなく分布の下側で確かめるための計数。</summary>
        public static readonly System.Collections.Generic.Dictionary<string, long[]> ByBoss
            = new System.Collections.Generic.Dictionary<string, long[]>();
        public const int ShortFightTurns = 10;

        public static void Reset()
        {
            for (int i = 0; i < Kinds; i++) { Fights[i] = Deaths[i] = Turns[i] = 0; HpLostPct[i] = 0; }
            ByBoss.Clear();
            BossDmg.Clear();
            System.Array.Clear(ByFloorKind, 0, ByFloorKind.Length);
            System.Array.Clear(ByFloorKindHpLost, 0, ByFloorKindHpLost.Length);
            System.Array.Clear(EliteReason, 0, EliteReason.Length);
            PendingEliteReason = -1; PendingEliteEst = -1f;
            FinishCombatReentry = 0;
        }

        /// <summary>ボス別の「敵 HP がどの経路で減ったか」 (2026-09-20)。
        /// [戦闘数, 最大HP合計, 通常攻撃, 固定ダメ, 役〈極〉, 出血, 終了時HP合計, 1ターン通常攻撃の最大, 撃破数,
        ///  攻撃した回数, その atkBase 合計, うち会心回数,
        ///  atkBase の内訳: 武器素火力, 攻撃端子の出目合計(ゴースト・役込み), 端子調律メタ分, パッシブ攻撃+, 消費攻撃+, 遺物攻撃+,
        ///  攻撃端子のダイス本数, 振ったダイス本数,
        ///  出目合計の内訳: 実体ダイスの出目, ゴースト接続, それ以外 (役・特殊端子など), 最大出目]。
        /// 「それ以外」 = 最大HP − 終了HP − (通常攻撃+固定+極+出血)。 即処刑・パッシブの直接削り・開始時削りがここに入る。</summary>
        public static readonly System.Collections.Generic.Dictionary<string, double[]> BossDmg
            = new System.Collections.Generic.Dictionary<string, double[]>();
        public const int BossDmgFields = 24;

        public static double[] BossDmgOf(string id)
        {
            if (!BossDmg.TryGetValue(id, out var a)) { a = new double[BossDmgFields]; BossDmg[id] = a; }
            return a;
        }

        public static void NoteBoss(string id, int turns, bool died)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (!ByBoss.TryGetValue(id, out var a)) { a = new long[4]; ByBoss[id] = a; }
            a[0]++; a[1] += turns;
            if (turns < ShortFightTurns) a[2]++;
            if (died) a[3]++;
        }

        /// <summary>層 × 種類別 (2026-09-20)。 [層0..8][種類] ごとに
        /// [戦闘数, 死亡数, 総ターン, ターン分布 1/2/3/4/5/6〜9/10+ の 7 区分] = 10 個。
        /// バランス方針 (GAME.md §13-5「層ごとの役割」) の「雑魚 2〜5T・ボス 5 層以降 10T 以上」を分布で確かめる。</summary>
        public const int Floors = 9, FloorFields = 10;
        public static readonly long[] ByFloorKind = new long[Floors * Kinds * FloorFields];
        /// <summary>同じ添字の被ダメ合計 (最大 HP 比)。</summary>
        public static readonly double[] ByFloorKindHpLost = new double[Floors * Kinds];

        /// <summary>エリートマスを選んだ理由 (2026-09-20)。 航行 (AutoRunner) が選んだ時点で <see cref="PendingEliteReason"/> に置き、
        /// その戦闘の終わりに実際の被ダメ・死亡と突き合わせる。
        /// 0=見積りで挑めると判断 / 1=見積りで避けたが他に手が無かった / 2=確信の進行で強制 /
        /// 3=候補がエリートだけ / 4=見積りを使っていない (旧判断)。</summary>
        public const int EliteReasons = 5, EliteReasonFields = 5;
        public static readonly string[] EliteReasonNames =
            { "見積りで挑めると判断", "避けたが他に手が無い", "確信の進行で強制", "候補がエリートだけ", "旧判断(見積りなし)" };
        /// <summary>[層][理由] ごとに [戦闘数, 死亡数, 見積り被ダメ(最大HP比)の和, 見積りがあった数, 実被ダメ(最大HP比)の和]。</summary>
        public static readonly double[] EliteReason = new double[Floors * EliteReasons * EliteReasonFields];
        public static int PendingEliteReason = -1;
        public static float PendingEliteEst = -1f;

        /// <summary>[計装] <c>CombatManager.FinishCombat</c> が既に閉じた戦闘に対して再度呼ばれた回数。
        /// <b>0 でなければ計装が二重に積まれていた</b> ── 2026-09-20 に冪等化するまで、
        /// 戦闘側の死亡数がラン側より 10.3% 多いという形で現れていた。</summary>
        public static long FinishCombatReentry;

        public static void Note(int kind, bool died, int turns, int startHp, int endHp, int maxHp, int floor)
        {
            Note(kind, died, turns, startHp, endHp, maxHp);
            if (kind < 0 || kind >= Kinds) return;
            if (kind == Elite && PendingEliteReason >= 0)
            {
                int fr = System.Math.Max(0, System.Math.Min(Floors - 1, floor));
                int e = (fr * EliteReasons + PendingEliteReason) * EliteReasonFields;
                EliteReason[e]++;
                if (died) EliteReason[e + 1]++;
                if (PendingEliteEst >= 0f) { EliteReason[e + 2] += PendingEliteEst; EliteReason[e + 3]++; }
                if (maxHp > 0) EliteReason[e + 4] += (startHp - System.Math.Max(0, endHp)) / (double)maxHp;
            }
            if (kind == Elite) { PendingEliteReason = -1; PendingEliteEst = -1f; }
            int f = System.Math.Max(0, System.Math.Min(Floors - 1, floor));
            int b = (f * Kinds + kind) * FloorFields;
            ByFloorKind[b]++;
            if (died) ByFloorKind[b + 1]++;
            ByFloorKind[b + 2] += turns;
            int bucket = turns <= 1 ? 0 : turns <= 5 ? turns - 1 : turns <= 9 ? 5 : 6;
            ByFloorKind[b + 3 + bucket]++;
            if (maxHp > 0) ByFloorKindHpLost[f * Kinds + kind] += (startHp - System.Math.Max(0, endHp)) / (double)maxHp;
        }

        public static void Note(int kind, bool died, int turns, int startHp, int endHp, int maxHp)
        {
            if (kind < 0 || kind >= Kinds) return;
            Fights[kind]++;
            if (died) Deaths[kind]++;
            Turns[kind] += turns;
            if (maxHp > 0) HpLostPct[kind] += (startHp - System.Math.Max(0, endHp)) / (double)maxHp;
        }
    }
}
