namespace CombatSystem
{
    /// <summary>
    /// 挑戦カード (docs/GAME.md §23-25・2026-11-22) のうち、 端子の解決・軸の式・撃の輪 (本体・画面の見込み・BOT の見積もりが同じ関数を通る所) で効く値の写し。
    /// 戦闘の始め (<see cref="CombatManager"/> の BeginTerminalDeck) に <see cref="Load"/> で受けている札から写し、 戦闘の間は変えない (札は層の入口でしか増えない)。
    /// 札を受けていなければ全部 0 ── 札を使わない経路の計算は今までと 1 ビットも変わらない (どの式も 0 の時は掛けない・足さない)。
    /// <para>読み手が UnityEngine に依存しない式 (各軸の Core・<see cref="TerminalCards.Resolve"/>) なので、 カードを引く処理 (MetaProgression) はここに置かない。</para>
    /// </summary>
    public static class CardRules
    {
        /// <summary>クァディルの色欲: 1 ターンに挿せる出目の数 (0 = 制限なし)。 <see cref="TerminalCards.Resolve"/> の 0. が超えた分を外す。</summary>
        public static int MaxWiredDice;
        /// <summary>〈見切り〉: ジャスト防御の時の防御無視の反撃 / 許容幅の上乗せ。</summary>
        public static int JustCounter, JustWidthAdd;
        /// <summary>〈毒仕込み〉: 起爆の倍率の上乗せ (10 分の 1 単位・+0.5 = 5)。</summary>
        public static int DetonateMulTenths;
        /// <summary>〈予熱〉: 爆発が無視する敵の防御の上乗せ (%) / 爆発の基礎の上乗せ。</summary>
        public static int BurstPenAddPct, BurstBaseAdd;
        /// <summary>〈急所読み〉III: 超過会心率の変換の上乗せ (%)。</summary>
        public static int CritExcessPct;
        /// <summary>〈初太刀〉: 戦闘の 1 ターン目、 撃ごとに足す出血。</summary>
        public static int FirstStrikeBleed;
        /// <summary>〈手捌き〉: 使い捨ての端子を使うたびのブロック / 〈手馴染み〉: 役が立ったターンのブロック。</summary>
        public static int DisposeBlock, RoleBlock;
        /// <summary>〈刃研ぎ〉: 撃ごとの攻撃 / 〈追い打ち〉: ボスのダウン中のターンの撃ごとの攻撃・手札。</summary>
        public static int BladeAtk, PursuitAtk, PursuitHand;
        /// <summary>〈予備電源〉III: 【再起動】【オーバークロック】の充電から引く量 (最低 1)。</summary>
        public static int RebootCostCut;
        /// <summary>〈手捌き〉: 戦闘の 1 ターン目に多く引く枚数。</summary>
        public static int OpeningDraw;

        /// <summary>受けている札から写す (戦闘の始め)。 札が無ければ全部 0。</summary>
        public static void Load()
        {
            int V(string k) => (int)MetaProgression.ChallengeCards.Value(k);
            int V2(string k) => (int)MetaProgression.ChallengeCards.Value2(k);
            MaxWiredDice = V(MetaProgression.ChallengeCards.SinLust);
            JustCounter = V(MetaProgression.ChallengeCards.Shield); JustWidthAdd = V2(MetaProgression.ChallengeCards.Shield);
            DetonateMulTenths = (int)System.Math.Round(MetaProgression.ChallengeCards.Value(MetaProgression.ChallengeCards.Poison) * 10f);
            BurstPenAddPct = V(MetaProgression.ChallengeCards.Heat); BurstBaseAdd = V2(MetaProgression.ChallengeCards.Heat);
            CritExcessPct = V2(MetaProgression.ChallengeCards.Crit);
            FirstStrikeBleed = V(MetaProgression.ChallengeCards.Bleed);
            OpeningDraw = V(MetaProgression.ChallengeCards.Deck); DisposeBlock = V2(MetaProgression.ChallengeCards.Deck);
            RoleBlock = V2(MetaProgression.ChallengeCards.Role);
            BladeAtk = V(MetaProgression.ChallengeCards.Blade);
            PursuitHand = V(MetaProgression.ChallengeCards.Pursuit); PursuitAtk = V2(MetaProgression.ChallengeCards.Pursuit);
            RebootCostCut = V2(MetaProgression.ChallengeCards.Charge);
            BossDown.TakenMulCard = MetaProgression.MetaDebuffApplicator.GetDownTakenMul();
        }

        /// <summary>【再起動】【オーバークロック】の充電 (〈予備電源〉III の −N・最低 1)。</summary>
        public static int RebootCost(int cost) => RebootCostCut > 0 && cost > 1 ? System.Math.Max(1, cost - RebootCostCut) : cost;
    }
}
