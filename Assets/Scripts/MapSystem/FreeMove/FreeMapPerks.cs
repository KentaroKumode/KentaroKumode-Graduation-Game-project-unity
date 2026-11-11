namespace MapSystem.FreeMove
{
    /// <summary>
    /// 航行アイテムが航行の規則へ掛ける係数 (2026-11-11・docs/design/nav-items.md)。
    /// <para><b><see cref="FreeMapSim"/> はアイテムの id を見ない</b> ── どの品を持っているかは本体 (GameLoop.NavItems) が読み、
    /// ここへ係数として渡す。 Sim は素の C# のまま (BOT が同期的に回す・単体で検算できる)。</para>
    /// <para>既定値 (<see cref="None"/>) は品なしの規則そのもの。 数値は全部仮置き (§23-21 と同じ扱い ── 動かす時は採否を取ってから測定する)。</para>
    /// </summary>
    public sealed class FreeMapPerks
    {
        /// <summary>何もしていない時に見える範囲 (道で何歩先まで)。 〈夜目の指輪〉で 2。</summary>
        public int vision = FreeMapParams.Vision;
        /// <summary>魔石の「遠い」の歩数に足す分。 〈魔石の欠片〉で +1 (4 → 5)。</summary>
        public int stoneFarBonus;
        /// <summary>痕跡が残るターンの倍率。 〈忍びの外套〉で 0.5 (4 → 2)。</summary>
        public double traceLifeMul = 1.0;
        /// <summary>使い魔がプレイヤーを見つける歩数。 〈鏡の護符〉で 0 (重なった時だけ)。</summary>
        public int familiarSee = FreeMapParams.FamiliarSee;
        /// <summary>層ごとの〈煙玉〉の回数 (補足された瞬間に自動で発動し、 追跡者を警戒へ落とす)。</summary>
        public int smokeCharges;
        /// <summary>層ごとの〈前哨の旗〉の回数 (今いる点に偵察拠点を置く)。</summary>
        public int beaconCharges;
        /// <summary>層に入った瞬間に種類を公開する、 始点からの直線距離 (層の座標の単位)。 0 = しない。 〈地図売りの走り書き〉で 5。</summary>
        public double sketchRadius;
        /// <summary>終点へ近づく 1 歩の物資が、 2 歩に 1 回 0 になる (〈渡り鳥の羅盤〉・×0.5 を 1 歩 = 1 の物資で表す)。</summary>
        public bool goalwardHalf;
        /// <summary>削る砂の前線の追加の物資 (+1) を受けない (〈油布の靴〉)。</summary>
        public bool ignoreSand;
        /// <summary>前哨の旗の拠点から常に公開する歩数。</summary>
        public int beaconRange = BeaconRangeDefault;

        public const int BeaconRangeDefault = 3;
        /// <summary>〈煙玉〉が効いた後、 追跡者が補足し直さないターン (減らしてから判定するので、 実際の猶予は 1 少ない)。</summary>
        public const int SmokeRestTurns = 3;

        public static readonly FreeMapPerks None = new FreeMapPerks();
    }
}
