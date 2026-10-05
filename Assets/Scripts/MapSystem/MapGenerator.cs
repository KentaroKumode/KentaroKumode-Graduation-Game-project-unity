using System.Collections.Generic;
using MapSystem.FreeMove;
using UnityEngine;

namespace MapSystem
{
    /// <summary>
    /// フロアマップの生成。
    ///
    /// <para><b>2026-09-28: 3 列前進のボードを自由移動のマップへ置き換えた</b>（旧マップは残していない。
    /// 廃止の理由は docs/GAME.md §24）。 形の生成は素の C# の <see cref="FreeMapGenerator"/> が行い、
    /// ここはそれを <see cref="FloorMap"/> / <see cref="MapNode"/> へ写して、 ゲーム側の規則
    /// （整備パネル〈剛胆〉の格上げ・挑戦デバフ〈偽の商人〉）を掛けるだけ。</para>
    ///
    /// <para>層の構成は <see cref="FloorPlan"/>: 1 → 3 → 5 →(Λ)→ 6 → 7 → 8。
    /// 1/3/5/6/7 層が自由移動（7 層の終点は〈門〉）、 8 層は内部でボス戦だけの層、 Λ 層は環状線のまま。</para>
    /// </summary>
    public static class MapGenerator
    {
        /// <summary>マスの抽選重み（読み取り専用）。 層によらず同じ表。
        /// Ultra の veil が同じ分布から引き直すために公開している（自由移動の層では veil 自体が無効）。</summary>
        public static IReadOnlyList<(TileType type, float weight)> RandomRowTileWeights(int floor)
            => FreeMapGenerator.TileWeights;

        /// <summary>旧 3 列マップの「ランダム抽選の最後の行」。 自由移動には行が無いので常に 0。
        /// Ultra の veil (自由移動では無効) がコンパイルを通すためだけに残している。</summary>
        public static int LastRandomRow(int floor) => 0;

        /// <summary>ボスノードを置くフロア (1/3/5/6/8)。 正本は <see cref="FloorPlan.HasBoss"/>。
        /// 7 層の終点は〈門〉で、 ヴェスカは門の転移先 (8 層) に座る。</summary>
        public static bool HasBoss(int floor) => FloorPlan.HasBoss(floor);

        /// <summary>確定ショップが生える<b>最後の</b>層。 <b>最終層ではない。</b>
        ///
        /// <para>8 層 (Null Point) はボス戦だけで店が無いので、
        /// 「ランで最後のショップか」を <c>currentFloor >= maxFloor</c> で判定すると
        /// <b>永遠に成立しない</b> ── 値下げ交渉 (強盗) の発火条件がここに乗っている
        /// (<c>AutoRunner.IsFinalShopOfRun</c>)。 層構成を動かしたらここも動かすこと。</para></summary>
        public const int LastFloorWithShop = 7;

        /// <summary>自由移動の層の start / goal の ID。 他は "n{添字}"。</summary>
        public const string StartNodeId = "outpost";
        public const string BossNodeId = "boss";
        public const string GateNodeId = "gate";

        /// <summary>指定フロアのマップを生成</summary>
        public static FloorMap Generate(int floor)
        {
            if (!FloorPlan.IsFreeMap(floor)) return GenerateBossOnly(floor);
            return FromLayout(FreeMapGenerator.Generate(floor));
        }

        /// <summary>?マスの実タイプを抽選（Mystery は廃止済み。互換のため残置、呼ばれても Battle を返す）。</summary>
        public static TileType ResolveMystery() => TileType.Battle;

        /// <summary>自由移動の層の形を FloorMap へ写す。 道は双方向の接続になる。</summary>
        public static FloorMap FromLayout(FreeMapLayout L)
        {
            var map = new FloorMap { floor = L.floor, laneCount = 1, rowCount = 1, layout = L };
            for (int i = 0; i < L.Count; i++)
            {
                string id = i == L.start ? StartNodeId
                          : i == L.goal ? (L.types[i] == TileType.Gate ? GateNodeId : BossNodeId)
                          : "n" + i;
                // row / lane は旧マップの座標。 表示コードが落ちないように x 座標の整数部と添字を入れる
                // (本番の表示は次の段階で座標 x / y を読む)。
                var node = new MapNode(id, Mathf.FloorToInt((float)L.pos[i].x), i, L.types[i])
                {
                    index = i,
                    x = (float)L.pos[i].x,
                    y = (float)L.pos[i].y,
                    isFixedEvent = i == L.fixedEvent,
                    freeMap = true,
                    // 種別が最初から分かっている点: 始点・終点 (ボス / 門)・6 層の「裂け目の記録」。
                    //   他は視界 (距離 1)・照明・偵察で見えるまで伏せる。
                    revealed = i == L.start || i == L.goal || i == L.fixedEvent,
                };
                ApplyEliteUpgrade(L.floor, node);
                map.AddNode(node);
            }
            map.startNodeId = StartNodeId;
            map.bossNodeId = FloorPlan.HasBoss(L.floor) ? BossNodeId : null;
            foreach (var (a, b) in L.roads)
                map.AddConnection(map.NodeIdAt(a), map.NodeIdAt(b), bidirectional: true);

            ApplyFalseMerchant(map);
            return map;
        }

        /// <summary>整備パネル〈剛胆〉: 通常戦をエリートへ格上げする。
        /// **抽選そのものは動かさず、 引いた後で差し替える** ── 重み表を書き換えると他タイルの出現率まで
        /// 連動して動き、 何が効いたか読めなくなる。 乱数は専用キー。 引く回数を条件で変えないため
        /// **常に 1 回引く**（段 0 でも引く）── 消費列が条件で変わると同一シードのペア比較が壊れる。</summary>
        private static void ApplyEliteUpgrade(int floor, MapNode node)
        {
            int roll = GameLoop.GameRng.Range(0, 100, "map.eliteUpgrade", floor * 100 + node.index);
            if (node.type == TileType.Battle && roll < MetaProgression.MetaBuffApplicator.GetEliteUpgradePct())
                node.type = TileType.EliteBattle;
        }

        /// <summary>メタデバフ Lv5 が有効なとき、各 Shop マスを確率で偽商人に変える。
        /// type は Shop のまま（プレイヤーには判別不可）、isFalseMerchant フラグだけ立てる。</summary>
        private static void ApplyFalseMerchant(FloorMap map)
        {
            // 偽商人は3層以降のみ出現（序盤の事故を避ける）
            if (map == null || map.floor < 3) return;
            float chance = MetaProgression.MetaDebuffApplicator.GetFalseMerchantChance();
            if (chance <= 0f) return;

            foreach (var n in map.GetAllNodes())
            {
                if (n == null || n.EffectiveType != TileType.Shop) continue;
                if (GameLoop.GameRng.Chance(chance, "map.falseMerchant", map.floor * 100 + n.index))
                {
                    n.isFalseMerchant = true;
                    Debug.Log($"[MapGenerator] 偽の商人を配置: {n.id}");
                }
            }
        }

        // ================================================================
        //  8層 Null Point（Signal lost）— ヴェスカ 1 戦のみ
        // ================================================================
        /// <summary>8層: 門が転移させた先。 <b>前哨基地とボスだけ</b>で、 GameManager.EnterFloor が
        /// 前哨基地の処理の直後にボスを起動する（プレイヤーにはマップを見せない）。
        ///
        /// <para>補給は 7 層で終わっている ── 門をくぐった先に店も休憩も無い。
        /// 前哨基地の回復も**効かない** (<c>GameManager.EnterFloor</c> が 8 層を除外する)。</para>
        ///
        /// <para><b>ボスの ID は <c>boss_layer7</c> のまま据え置く。</b> 学習ファイル・
        /// Tier 表・boss_tuning.json のキーが ID 基準なので、 層番号に合わせて
        /// 改名すると過去データと切れる (Ids.cs の 3 層プールと同じ扱い)。</para></summary>
        private static FloorMap GenerateBossOnly(int floor)
        {
            var map = new FloorMap { floor = floor, laneCount = 1, rowCount = 2 };

            map.AddNode(new MapNode(StartNodeId, 0, -1, TileType.Outpost));
            map.AddNode(new MapNode(BossNodeId, 1, -1, TileType.Boss));

            map.startNodeId = StartNodeId;
            map.bossNodeId = BossNodeId;

            map.AddConnection(StartNodeId, BossNodeId);

            return map;
        }

        // ================================================================
        //  Λ層（時間の狭間）— 環状線 + 中央(離脱)スポーク
        // ================================================================
        /// <summary>Λ層: 5層ボス撃破後〈決意〉以上で強制突入する周回エリア。
        /// 環状線(S→A→B→S)の3マス + 中央(離脱)。中央は S からのみ到達可（最低1周を強制）。
        /// 環状線マスは踏む度にエリート/固有イベントを抽選し、移動毎に「次元の乱れ」を蓄積する。
        /// 3マス周回ごとに次元の乱れ+3＝デバフ1個の閾値に一致する設計。
        /// **2026-09-28 の自由移動化の対象外**（Λ 層は今のまま）。</summary>
        public static FloorMap GenerateLambda()
        {
            var map = new FloorMap { floor = 5, laneCount = 1, rowCount = 1 };

            // 環状線3マス（S=スポーク, A, B）
            map.AddNode(new MapNode("lambda_s", 0, 0, TileType.LambdaRing));
            map.AddNode(new MapNode("lambda_a", 1, 0, TileType.LambdaRing));
            map.AddNode(new MapNode("lambda_b", 2, 0, TileType.LambdaRing));
            // 中央(離脱)
            map.AddNode(new MapNode("lambda_center", 1, -1, TileType.LambdaExit));

            map.startNodeId = "lambda_s";
            map.bossNodeId = null;

            // 環状: S→A→B→S（前方向のみの単純サイクル）
            map.AddConnection("lambda_s", "lambda_a");
            map.AddConnection("lambda_a", "lambda_b");
            map.AddConnection("lambda_b", "lambda_s");
            // スポーク: 中央へは S からのみ
            map.AddConnection("lambda_s", "lambda_center");

            return map;
        }
    }
}
