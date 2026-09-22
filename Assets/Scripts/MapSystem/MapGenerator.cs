using System.Collections.Generic;
using UnityEngine;

namespace MapSystem
{
    /// <summary>
    /// フロアマップの手続き生成。
    /// 3レーン × 10行 + ボス収束ノード。
    /// 6層/7層/8層は固定構成。 2026-09-14: 7層の終端を〈門〉へ差し替え、 ヴェスカを 8層へ移設。
    /// </summary>
    public static class MapGenerator
    {
        // === タイル抽選ウェイト（ランダム行用）===
        // 旧 Mystery(15) を廃止し Exchange(7) を新設。余り8を Battle+4 / EliteBattle+4 に再配分。
        private static readonly (TileType type, float weight)[] TileWeights =
        {
            (TileType.Battle,      29f),   // 旧25 (+4)
            (TileType.Event,       25f),
            (TileType.EliteBattle, 14f),   // 旧10 (+4)
            // Trap は撤去 (ADR-0001: 効果ゼロの死にノード)。抽選プールから除外。
            (TileType.Exchange,     7f),   // 新設(交換マス)
            (TileType.Shop,         7f),
            (TileType.Treasure,     3f),
            (TileType.Rest,         2f),
        };

        /// <summary>4 層の抽選ウェイト。 <b>精鋭だけを 14 → 7 に落としてある</b> (2026-09-21)。
        ///
        /// <para><b>4 層はボスが無いのでランダム行が 1 行多い</b> (Row 1〜8、 ボス層は 1〜7)。
        /// 全タイルが等しく 1 行ぶん増えるが、 精鋭だけは 1 戦の死亡率が桁違いなので
        /// 遭遇数の増加がそのまま死亡数になる ── 実測 (10,000 ラン) で 4 層の精鋭は
        /// <b>1 ラン 1.2 回</b>踏まれ (5 層は 0.76 回)、 被ダメが 4% しかないのに
        /// 死亡 3.5%、 4 層の死亡 669 件のうち 426 件 (64%) を占めていた。
        /// 雑魚のほうは被ダメ −1% (回復が上回る) で無害。</para>
        ///
        /// <para><b>敵の数値ではなく出現率で直す。</b> 4 層は「休憩」の層 (§13-5) で、
        /// 死因は削り合いの負けではなく<b>踏んだ回数</b>だった。 敵をさらに弱めると
        /// 雑魚が完全に無風になるだけで、 精鋭の回数は減らない。</para></summary>
        private static readonly (TileType type, float weight)[] Floor4TileWeights =
        {
            (TileType.Battle,      29f),
            (TileType.Event,       25f),
            (TileType.EliteBattle,  7f),   // 通常層の半分
            (TileType.Exchange,     7f),
            (TileType.Shop,         7f),
            (TileType.Treasure,     3f),
            (TileType.Rest,         2f),
        };

        /// <summary>その層のランダム行の抽選ウェイト（読み取り専用）。
        ///
        /// <para>**Ultra の veil が「同じ分布から」引き直すために公開している**
        /// ([UltraSnapshotVeil](../../Scripts/AutoTest/Ultra/UltraSnapshotVeil.cs))。
        /// 向こうで重みを書き写すと、ここを調整したときに静かに食い違う。
        /// <b>層を渡すこと</b> ── 4 層だけ表が違うので、 層を無視すると
        /// veil が盤面と別の分布から引く。</para></summary>
        public static IReadOnlyList<(TileType type, float weight)> RandomRowTileWeights(int floor)
            => floor == 4 ? Floor4TileWeights : TileWeights;

        /// <summary>ランダム抽選で決まる最後の行。 これより下 (ショップ保証行・休憩行・ボス) は
        /// 層構造として固定なので、 プレイヤーは見えていなくても種別を知っている。</summary>
        public static int LastRandomRow(int floor)
        {
            int restRow = RowCount - 1;
            int shopRow = HasBoss(floor) ? RowCount - 2 : -1;
            return (shopRow > 0 ? shopRow : restRow) - 1;
        }

        public const int LaneCount = 3;
        public const int RowCount = 10;   // Row 0(前哨) 〜 Row 9(休憩)
        public const float LateralChance = 0.3f;
        public const float DiagonalChance = 0.6f;

        // ================================================================
        //  公開 API
        // ================================================================

        /// <summary>ボスノードを置くフロア。 2026-07-29: 全層 → **1/3/5/6/7** に変更。
        ///
        /// 2 層 (勝率 98.8%) と 4 層 (98.2%) はボス戦として機能しておらず、
        /// 全ランのうちそこで死ぬのは 1.2% / 1.5% しかなかった。 とくに 4 層は
        /// 勝 6.1T に対し **敗 15.6T** と、 負けるときだけ 2.5 倍かかる粘着戦で、
        /// 緊張ではなく待ち時間を足しているだけだった。
        /// 間隔を空けて 1 戦あたりの重みを上げる。 1 層は関門ではなく
        /// 「ボスとはこういう画面だ」を見せる報酬枠なので残す。</summary>
        ///
        /// <para><b>2026-09-14: 7 層からボスを外した。</b> 7 層の終端は〈門〉で、
        /// ヴェスカは門の転移先 (8 層) に座る。 7 層はボスなし層 (2/4 層と同じ) として
        /// <c>IsBosslessFloorCleared</c> の終端判定で層クリアする。</para></summary>
        public static bool HasBoss(int floor) => floor != 2 && floor != 4 && floor != 7;

        /// <summary>確定ショップが生える<b>最後の</b>層。 <b>最終層ではない。</b>
        ///
        /// <para>8 層 (Null Point) は前哨基地とボスだけで店が無いので、
        /// 「ランで最後のショップか」を <c>currentFloor >= maxFloor</c> で判定すると
        /// <b>永遠に成立しない</b> ── 値下げ交渉 (強盗) の発火条件がここに乗っている
        /// (<c>AutoRunner.IsFinalShopOfRun</c>)。 層構成を動かしたらここも動かすこと。</para></summary>
        public const int LastFloorWithShop = 7;

        /// <summary>指定フロアのマップを生成</summary>
        public static FloorMap Generate(int floor)
        {
            if (floor == 6) return GenerateLayer6();
            if (floor == 7) return GenerateLayer7();
            if (floor == 8) return GenerateLayer8();
            return GenerateStandard(floor);
        }

        /// <summary>?マスの実タイプを抽選（Mystery は廃止済み。互換のため残置、呼ばれても Battle を返す）。</summary>
        public static TileType ResolveMystery()
        {
            return TileType.Battle;
        }

        // ================================================================
        //  通常フロア (1-5)
        // ================================================================

        private static FloorMap GenerateStandard(int floor)
        {
            var map = new FloorMap
            {
                floor = floor,
                laneCount = LaneCount,
                rowCount = RowCount
            };

            // --- ノード ---

            // Row 0: 前哨基地（収束ノード）
            map.AddNode(new MapNode("outpost", 0, -1, TileType.Outpost));
            map.startNodeId = "outpost";

            // Row 8: ショップ（保証・**ボスのある層のみ**）
            //   2026-08-16 追加。 休憩は元から Row 9 で全レーン保証されていたが、
            //   ショップは ForceInjectShops が中央行に 1 マス置くだけで、
            //   **そのレーンを通らなければ踏めない**＝ボス前の補給が運任せだった。
            //   ボスの無い層 (2/4) は「ボス前」が存在しないので従来どおりランダム行。
            int restRow  = RowCount - 1;                        // 9
            int shopRow  = HasBoss(floor) ? RowCount - 2 : -1;  // 8
            int lastRandomRow = (shopRow > 0 ? shopRow : restRow) - 1;

            // Row 1 〜: ランダムタイル（旧 Row8 の宝箱確定を撤廃し通常ランダム行に）
            for (int row = 1; row <= lastRandomRow; row++)
            {
                for (int lane = 0; lane < LaneCount; lane++)
                {
                    var type = WeightedRandom(floor == 4 ? Floor4TileWeights : TileWeights,
                                              "map.tile", (floor * 100 + row) * 10 + lane);
                    // 整備パネル〈剛胆〉: 通常戦をエリートへ格上げする。
                    //   **抽選そのものは動かさず、 引いた後で差し替える** ── 重み表を
                    //   書き換えると他タイルの出現率まで連動して動き、 何が効いたか読めなくなる。
                    //   乱数は専用キー。 引く回数を条件で変えないため**常に 1 回引く**
                    //   (段 0 でも引く) ── 消費列が条件で変わると同一シードのペア比較が壊れる。
                    int upgradeRoll = GameLoop.GameRng.Range(0, 100, "map.eliteUpgrade",
                        (floor * 100 + row) * 10 + lane);
                    if (type == TileType.Battle
                        && upgradeRoll < MetaProgression.MetaBuffApplicator.GetEliteUpgradePct())
                        type = TileType.EliteBattle;
                    map.AddNode(new MapNode(NodeId(row, lane), row, lane, type));
                }
            }

            if (shopRow > 0)
                for (int lane = 0; lane < LaneCount; lane++)
                    map.AddNode(new MapNode(NodeId(shopRow, lane), shopRow, lane, TileType.Shop));

            // Row 9: 休憩（保証）
            for (int lane = 0; lane < LaneCount; lane++)
                map.AddNode(new MapNode(NodeId(restRow, lane), restRow, lane, TileType.Rest));

            // ボス（Row 10 相当、収束ノード）
            // 旧 karma_trap ノードは 2026-07-27 に削除 (カルマ廃止 ADR-0002 + トラップ撤去 ADR-0001)。
            // 2026-07-29: ボスを置かない層 (2/4) では休憩行が終端になる。
            if (HasBoss(floor))
            {
                map.AddNode(new MapNode("boss", RowCount, -1, TileType.Boss));
                map.bossNodeId = "boss";
            }

            // --- エッジ ---

            // 前哨基地 → Row 1 全レーン
            for (int lane = 0; lane < LaneCount; lane++)
                map.AddConnection("outpost", NodeId(1, lane));

            // Row 1〜9 → 次行: 前方 + 斜め前方
            // Rest(行9)・Shop(行8) を含む行/接続では斜めを 0% にする
            //   ── 全レーン同種の行では斜めに意味が無く、 経路の見え方が濁るだけ。
            for (int row = 1; row < RowCount; row++)
            {
                int targetRow = row + 1;
                bool sourceGuaranteed = (row == restRow || row == shopRow);
                bool targetGuaranteed = (targetRow == restRow || targetRow == shopRow);
                bool allowDiagonal = !sourceGuaranteed && !targetGuaranteed;

                for (int lane = 0; lane < LaneCount; lane++)
                {
                    string from = NodeId(row, lane);

                    // 前方（同レーン、次行）— 常時
                    string fwd = (row + 1 <= restRow) ? NodeId(row + 1, lane) : null;
                    if (fwd != null)
                        map.AddConnection(from, fwd);

                    if (!allowDiagonal) continue;

                    // 斜め左（60%）
                    if (lane > 0 && GameLoop.GameRng.Chance(DiagonalChance, "map.diagL", (floor * 100 + row) * 10 + lane))
                    {
                        string dl = (row + 1 <= restRow) ? NodeId(row + 1, lane - 1) : null;
                        if (dl != null)
                            map.AddConnection(from, dl);
                    }

                    // 斜め右（60%）
                    if (lane < LaneCount - 1 && GameLoop.GameRng.Chance(DiagonalChance, "map.diagR", (floor * 100 + row) * 10 + lane))
                    {
                        string dr = (row + 1 <= restRow) ? NodeId(row + 1, lane + 1) : null;
                        if (dr != null)
                            map.AddConnection(from, dr);
                    }
                }
            }

            // 休憩行 → ボス (ボスのある層のみ。 無い層は休憩行が終端 = 踏破で層クリア)
            if (HasBoss(floor))
                for (int lane = 0; lane < LaneCount; lane++)
                    map.AddConnection(NodeId(restRow, lane), "boss");

            // 横移動（確率、双方向）— Rest 行・Shop 行は除外（全レーン同種なので動く意味が無い）
            for (int row = 1; row <= restRow; row++)
            {
                if (row == restRow || row == shopRow) continue;

                for (int lane = 0; lane < LaneCount - 1; lane++)
                {
                    if (GameLoop.GameRng.Chance(LateralChance, "map.lateral", (floor * 100 + row) * 10 + lane))
                        map.AddConnection(NodeId(row, lane), NodeId(row, lane + 1), bidirectional: true);
                }
            }

            // ショップ確定出現: 上下3行を除いた中央行(4〜6)のランダムマスを
            // 1〜3個 Shop へ強制置換（接続トポロジは不変＝type だけ差し替え）
            ForceInjectShops(map);

            // メタデバフ Lv5 偽の商人: Shopマスを確率で偽商人化（見た目はShopのまま）
            ApplyFalseMerchant(map);

            return map;
        }

        /// <summary>メタデバフ Lv5 が有効なとき、各 Shop マスを 30% で偽商人に変える。
        /// type は Shop のまま（プレイヤーには判別不可）、isFalseMerchant フラグだけ立てる。</summary>
        private static void ApplyFalseMerchant(FloorMap map)
        {
            // 偽商人は3層以降のみ出現（序盤の事故を避ける）
            if (map == null || map.floor < 3) return;
            float chance = MetaProgression.MetaDebuffApplicator.GetFalseMerchantChance();
            if (chance <= 0f) return;

            for (int row = 0; row <= RowCount; row++)
                for (int lane = 0; lane < LaneCount; lane++)
                {
                    var n = map.GetNode(NodeId(row, lane));
                    if (n == null || n.EffectiveType != TileType.Shop) continue;
                    if (GameLoop.GameRng.Chance(chance, "map.falseMerchant", (map.floor * 100 + row) * 10 + lane))
                    {
                        n.isFalseMerchant = true;
                        Debug.Log($"[MapGenerator] 偽の商人を配置: {n.id}");
                    }
                }
        }

        /// <summary>中央行(4〜6)のランダムマスから1マスだけ Shop に強制置換（各フロア確定1個）。</summary>
        private static void ForceInjectShops(FloorMap map)
        {
            int firstCentral = 4;
            int lastCentral = (RowCount - 3) - 1; // RowCount=10 → 6
            if (lastCentral < firstCentral) lastCentral = firstCentral;

            var cells = new List<MapNode>();
            for (int row = firstCentral; row <= lastCentral; row++)
                for (int lane = 0; lane < LaneCount; lane++)
                {
                    var n = map.GetNode(NodeId(row, lane));
                    if (n != null) cells.Add(n);
                }
            if (cells.Count == 0) return;

            // 確定1個のみ Shop 化
            var pick = cells[GameLoop.GameRng.Range(0, cells.Count, "map.forceShop", map.floor)];
            pick.type = TileType.Shop;
            pick.resolvedType = null; // EffectiveType が Shop を返すように
        }

        // ================================================================
        //  6層（裏ボス）— 固定リニア構成
        // ================================================================

        private static FloorMap GenerateLayer6()
        {
            // 2026-08-16: ボス直前に Rest を追加 (Shop は元からある)。
            //   「ボス前にはショップと休憩を必ず置く」を全層で揃えるため。
            var map = new FloorMap { floor = 6, laneCount = 1, rowCount = 5 };

            // **2026-09-14: 儀式マス (旧 SinAltar) は 6 層から消えた。** 6 層に置くと、
            //   直後の店で使うゴールド・以後の全戦に効くアイテム・最大HP を、
            //   <b>6 層ボス 1 戦だけに効く罰</b>と引き換えにすることになり、 支払いが一方的に損だった
            //   (同一シード 10,000 本のペア比較で払う側が −4.33pt / McNemar z = −16.6)。
            //   現在は 7 層終端の〈門〉(GenerateLayer7) が後継で、 罰が当たるのは
            //   **ランを決める最終ボス** (8 層 Null Point)。
            var map6 = map;
            map6.AddNode(new MapNode("outpost", 0, -1, TileType.Outpost));
            map6.AddNode(new MapNode(NodeId(1, 0), 1, 0, TileType.Event));
            map6.AddNode(new MapNode(NodeId(2, 0), 2, 0, TileType.Shop));
            map6.AddNode(new MapNode(NodeId(3, 0), 3, 0, TileType.Rest));
            map6.AddNode(new MapNode("boss", 4, -1, TileType.Boss));

            map6.startNodeId = "outpost";
            map6.bossNodeId = "boss";

            map6.AddConnection("outpost", NodeId(1, 0));
            map6.AddConnection(NodeId(1, 0), NodeId(2, 0));
            map6.AddConnection(NodeId(2, 0), NodeId(3, 0));
            map6.AddConnection(NodeId(3, 0), "boss");

            return map;
        }

        // ================================================================
        //  7層（掘り止め）— 固定リニア小フロア。 終端は〈門〉でボスは居ない
        // ================================================================
        /// <summary>7層: 灰燼撃破後の独立小フロア。 ファーム不可の一本道。
        /// 6F到達時点のビルドのみで最終戦に挑む試金石。 精鋭ファームの余地は無い。
        ///
        /// <para><b>2026-08-16: outpost→Boss 直結に Shop と Rest を挿入した。</b>
        /// 旧構成は補給機会が 6層のショップで打ち切られており、 実測で
        /// <b>p4 で死亡した 199 ランの残金が平均 29.3G・回復薬の所持 0 個が 90.5%</b>。
        /// 「使い道の無いゴールドを握って最終ボスに入る」状態は、 試金石ではなく
        /// **持ち込み資源を捨てさせているだけ**で、 プレイヤーの判断が介在しない。
        /// ファーム不可 (戦闘マスが無い＝ HP もゴールドも増えない) は維持したまま、
        /// **既に稼いだぶんを使い切る場所**だけを与える。</para>
        ///
        /// <para><b>2026-09-14: 終端を Boss から Gate へ差し替えた。</b> ヴェスカは
        /// 門の転移先 (8 層) に移設。 この層に戦闘マスは 1 つも無く、
        /// 終端 (前へ進めない) に到達した時点で層クリアになる
        /// (<c>GameManager.IsBosslessFloorCleared</c>)。 店 → 休憩 → 門 の順は
        /// 旧祭壇のときと同じで、 <b>門の 3 つの代償が全て最終戦の戦力から引かれる</b>
        /// ようにしてある ── 払って完全起動するか、 戦力を残して不完全なまま降りるか。</para></summary>
        private static FloorMap GenerateLayer7()
        {
            var map = new FloorMap { floor = 7, laneCount = 1, rowCount = 4 };

            map.AddNode(new MapNode("outpost", 0, -1, TileType.Outpost));
            map.AddNode(new MapNode(NodeId(1, 0), 1, 0, TileType.Shop));
            map.AddNode(new MapNode(NodeId(2, 0), 2, 0, TileType.Rest));
            map.AddNode(new MapNode("gate", 3, -1, TileType.Gate));

            map.startNodeId = "outpost";
            map.bossNodeId = null;   // ボスは居ない

            map.AddConnection("outpost", NodeId(1, 0));
            map.AddConnection(NodeId(1, 0), NodeId(2, 0));
            map.AddConnection(NodeId(2, 0), "gate");

            return map;
        }

        // ================================================================
        //  8層 Null Point（Signal lost）— ヴェスカ 1 戦のみ
        // ================================================================
        /// <summary>8層: 門が転移させた先。 <b>前哨基地とボスだけ</b>。
        ///
        /// <para>補給は 7 層で終わっている ── 門をくぐった先に店も休憩も無い。
        /// 前哨基地の回復も**効かない** (<c>GameManager.EnterFloor</c> が 8 層を除外する)。
        /// 転移直後の一点であって、 野営できる場所ではない。</para>
        ///
        /// <para><b>ボスの ID は <c>boss_layer7</c> のまま据え置く。</b> 学習ファイル・
        /// Tier 表・boss_tuning.json のキーが ID 基準なので、 層番号に合わせて
        /// 改名すると過去データと切れる (Ids.cs の 3 層プールと同じ扱い)。</para></summary>
        private static FloorMap GenerateLayer8()
        {
            var map = new FloorMap { floor = 8, laneCount = 1, rowCount = 2 };

            map.AddNode(new MapNode("outpost", 0, -1, TileType.Outpost));
            map.AddNode(new MapNode("boss", 1, -1, TileType.Boss));

            map.startNodeId = "outpost";
            map.bossNodeId = "boss";

            map.AddConnection("outpost", "boss");

            return map;
        }

        // ================================================================
        //  Λ層（時間の狭間）— 環状線 + 中央(離脱)スポーク
        // ================================================================
        /// <summary>Λ層: 5層ボス撃破後〈決意〉以上で強制突入する周回エリア。
        /// 環状線(S→A→B→S)の3マス + 中央(離脱)。中央は S からのみ到達可（最低1周を強制）。
        /// 環状線マスは踏む度にエリート/固有イベントを抽選し、移動毎に「次元の乱れ」を蓄積する。
        /// 3マス周回ごとに次元の乱れ+3＝デバフ1個の閾値に一致する設計。</summary>
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

        // ================================================================
        //  ユーティリティ
        // ================================================================

        private static string NodeId(int row, int lane) => $"r{row}_l{lane}";

        private static TileType WeightedRandom((TileType type, float weight)[] weights,
                                               string key = "map.tile", int index = -1)
        {
            float total = 0f;
            foreach (var (_, w) in weights) total += w;

            float roll = GameLoop.GameRng.Value(key, index) * total;
            float cumulative = 0f;

            foreach (var (type, weight) in weights)
            {
                cumulative += weight;
                if (roll <= cumulative) return type;
            }

            return weights[0].type;
        }
    }
}
