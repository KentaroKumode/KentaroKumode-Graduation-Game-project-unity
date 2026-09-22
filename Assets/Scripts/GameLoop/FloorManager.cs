using System.Collections.Generic;
using UnityEngine;
using CombatSystem;

namespace GameLoop
{
    /// <summary>
    /// フロアごとの敵エンカウント管理
    /// enemies.jsonのfloorデータを使い、現在フロアに出現する敵を選出する
    /// </summary>
    public static class FloorManager
    {
        /// <summary>**特殊エンカウント専用**を表す floor 値。 この値以上の敵は通常戦闘の抽選に一切乗らず、
        /// <c>EnemyDatabase.Get(id)</c> で id 直指定されたときにだけ現れる。
        ///
        /// <para>該当するのは 〈怪しい商人〉(ショップ強盗 = 値下げ交渉) と
        /// 〈偽の商人〉(メタデバフ Lv5) の 2 体。 どちらも「特定の行動を選んだ罰」として
        /// 設計されているので、 道中でいきなり出会ってよい強さになっていない。</para>
        ///
        /// <para><b>2026-08-15 に定数化した。</b> 〈偽の商人〉は floor 99 で正しく除外されていたが、
        /// 〈怪しい商人〉が floor 5 のまま放置され、 5層の通常抽選に混ざっていた。
        /// しかも <see cref="PickEnemy"/> の「初登場フロアを 50% で優先」分岐に乗るため
        /// 5層の戦闘マスの約 10% を占め、 1000 ラン中 1184 回出現して致命 271 件
        /// (全死因の 28.8%) を出していた。 99 が偶然どのフロア窓にも入らないから効いていただけで、
        /// 規約として書かれていなかったのが原因。 <b>新しい特殊敵は必ずこの値を使うこと。</b></para></summary>
        public const int SpecialEncounterFloor = 99;

        /// <summary>
        /// 指定フロアの敵候補からランダムに1体選出
        /// </summary>
        public static EnemyData PickEnemy(int floor)
        {
            // 候補を直近フロア [floor-2, floor] に限定（低層雑魚が後半まで居座って
            // 道中を無風化する問題への対処。ボス専用敵 boss_layer* は通常戦闘から除外）
            int minFloor = Mathf.Max(1, floor - 2);
            var candidates = EnemyDatabase.GetByFloorRange(minFloor, floor);
            candidates?.RemoveAll(e => IsBossOnly(e) || e.elite);
            if (candidates == null || candidates.Count == 0)
            {
                // 窓内に候補が無い低層フォールバック: 従来通り floor 以下全敵から
                candidates = EnemyDatabase.GetByFloor(floor);
                candidates?.RemoveAll(e => IsBossOnly(e) || e.elite);
            }
            if (candidates == null || candidates.Count == 0)
            {
                Debug.LogWarning($"[FloorManager] フロア{floor}の敵が見つかりません。全敵からランダム選出");
                return EnemyDatabase.GetRandom();
            }

            // 現在フロアに初登場する敵を優先（50%で選出。ボス専用敵は除外）
            var newEnemies = EnemyDatabase.GetNewOnFloor(floor);
            newEnemies?.RemoveAll(e => IsBossOnly(e) || e.elite);
            if (newEnemies != null && newEnemies.Count > 0 && GameRng.Chance(0.5f, "enemy.preferNew"))
            {
                return newEnemies[GameRng.Range(0, newEnemies.Count, "enemy.pickNew")];
            }

            return candidates[GameRng.Range(0, candidates.Count, "enemy.pick")];
        }

        /// <summary>通常戦闘の抽選から外す敵か。 ボス専用敵 (id が boss_layer*) と
        /// 特殊エンカウント専用敵 (<see cref="SpecialEncounterFloor"/> 以上) の 2 種。
        ///
        /// <para>floor 窓 [floor-2, floor] は floor ≤ 7 なので 99 は自然に外れるが、
        /// フォールバック経路 (<c>GetByFloor</c> / <c>GetRandom</c>) は窓を使わない。
        /// **除外は窓の副作用に頼らず明示する。**</para></summary>
        private static bool IsBossOnly(EnemyData e)
            => e != null && (BossIds.IsBoss(e.id) || e.floor >= SpecialEncounterFloor);

        // ============================================================
        //  エンカウント解決 (2026-08-28)
        // ============================================================

        /// <summary>ノードごとに一意で、 <b>訪問順に依存しない</b> 乱数添字。
        ///
        /// <para>GameRng はキー別かつ index 指定で位置非依存 (<c>Draw(key, index)</c>) なので、
        /// ここで作った添字で引く限り<b>未訪問ノードの解決が乱数列を進めない</b>。
        /// 添字を渡さずキー内カウンタで引くと、 開示のために先に解決したノードのぶんだけ
        /// 列がずれ、 同一シードのペアが割れる。</para></summary>
        public static int NodeRngIndex(int floor, MapSystem.MapNode node)
        {
            if (node == null) return 0;
            // lane は収束ノードで -1 を取るので +1 して 0 以上に寄せる。
            return floor * 4096 + node.row * 8 + Mathf.Clamp(node.lane + 1, 0, 7);
        }

        /// <summary>ノード添字を明示して敵を 1 体引く。 <see cref="PickEnemy"/> の決定論版。
        /// <paramref name="elite"/> なら <b>エリート敵 (<c>EnemyData.elite</c>) だけ</b>から、 そうでなければ雑魚だけから引く。</summary>
        public static EnemyData PickEnemyForNode(int floor, int nodeIdx, bool elite = false)
        {
            var candidates = BuildCandidates(floor, elite);
            if (candidates == null || candidates.Count == 0) return EnemyDatabase.GetRandom();

            var newEnemies = EnemyDatabase.GetNewOnFloor(floor);
            newEnemies?.RemoveAll(e => IsBossOnly(e) || e.elite != elite);
            if (newEnemies != null && newEnemies.Count > 0
                && GameRng.Value("enemy.preferNew", nodeIdx) < 0.5f)
                return newEnemies[GameRng.Range(0, newEnemies.Count, "enemy.pickNew", nodeIdx)];

            return candidates[GameRng.Range(0, candidates.Count, "enemy.pick", nodeIdx)];
        }

        /// <summary>このノードのエンカウントを (まだなら) 決めて焼き付ける。
        /// **冪等**。 開示時にも踏んだ時にも呼んでよい。</summary>
        public static void EnsureEncounter(int floor, MapSystem.MapNode node)
        {
            if (node == null) return;
            var t = node.EffectiveType;

            // ── ボスマス ──
            //   **3 層だけボスが 3 体プールからの抽選**なので、 ここで確定させて焼き付ける。
            //   焼き付けないと「表示のために引いた値」と「戦闘で引いた値」が別物になり、
            //   しかもキー内カウンタで二度引くぶん乱数列がずれる。
            if (t == MapSystem.TileType.Boss)
            {
                if (!string.IsNullOrEmpty(node.encounterId)) return;
                node.encounterId = "enc_boss";
                node.encounterEnemyA = BossIdForFloor(floor, NodeRngIndex(floor, node));
                node.encounterEnemyB = null;
                return;
            }

            if (t != MapSystem.TileType.Battle && t != MapSystem.TileType.EliteBattle) return;
            if (!string.IsNullOrEmpty(node.encounterId)) return;

            int idx = NodeRngIndex(floor, node);
            bool elite = t == MapSystem.TileType.EliteBattle;

            // **Λ 環状線は 2 体戦から除外する。** Λ は 6 層への強制通過点で、
            //   「このノードを避ける」という選択肢が存在しない (§14-1 の環状線構造)。
            //   選べない場所に賭けを置くと、 報酬 2 倍は賭けではなく固定の税になる。
            //   判定は **素の node.type** で行う ── EffectiveType はエリート枝で
            //   EliteBattle を返すので Λ を取りこぼす (§24 の oneShot 判定と同じ罠)。
            if (node.type == MapSystem.TileType.LambdaRing)
            {
                node.encounterId = EncounterPresets.Generic.id;
                node.encounterEnemyA = PickEnemyForNode(floor, idx, elite)?.id;
                node.encounterEnemyB = null;
                return;
            }

            // **エリートは常に単体** (2026-09-14 リワーク)。 2 体戦と精鋭は別種の危険で、
            //   両方乗せると何が効いたか読めない。 2 体戦は通常マスの引き (報酬 ×2 の賭け) として残し、
            //   エリートは「1 体が強い」で統一する。
            //   エリートは enemies.json のエリート敵 (elite: true) から引く。 強さはその数値そのもの。
            if (elite)
            {
                node.encounterId = EncounterPresets.Generic.id;
                node.encounterEnemyA = PickEnemyForNode(floor, idx, true)?.id;
                node.encounterEnemyB = null;
                return;
            }

            var preset = EncounterPresets.PickFor(floor, elite, idx);

            // ペアの 2 体目が DB に無ければ単体戦へ縮退する (プリセットの typo で戦闘が壊れないように)。
            bool pairOk = preset.IsPair && EnemyDatabase.Get(preset.enemyB) != null
                                        && EnemyDatabase.Get(preset.enemyA) != null;

            node.encounterId   = pairOk ? preset.id : EncounterPresets.Generic.id;
            node.encounterEnemyA = pairOk ? preset.enemyA : PickEnemyForNode(floor, idx)?.id;
            node.encounterEnemyB = pairOk ? preset.enemyB : null;
        }

        /// <summary>その層のボス ID。 <b>ボスが置かれるのは 1/3/5/6/8 層だけ</b>で、
        /// 2 層 / 4 層 / 7 層にボスマスは無い (<c>boss_layer2</c>＝ゴブリン王 / <c>boss_layer4</c>＝鏡の双子 は
        /// <b>3 層プール用に流用されている ID</b> であって、 その層のボスという意味ではない ──
        /// <c>$"boss_layer{floor}"</c> をそのまま層のボスとして読むと 3 層で毒沼の主に固定され、
        /// 2/4 層では実在しない配置を名指しすることになる)。
        ///
        /// <para>裏ボス (シュヴァリエ) はここで返さない。 レイピア所持で<b>到着時に</b>差し替わる相手なので、
        /// 事前に返すと分岐が解決する前に正体が漏れる。</para></summary>
        public static string BossIdForFloor(int floor, int nodeIdx)
        {
            if (floor == 3)
            {
                string[] pool = { BossIds.Layer3Marsh, BossIds.Layer3Goblin, BossIds.Layer3Mirror };
                return pool[GameRng.Range(0, pool.Length, "boss.pool", nodeIdx)];
            }
            // **8 層 (Null Point) のボスは boss_layer7 = ヴェスカ。** 2026-09-14 に
            //   7 層からボスを外して門の転移先へ移したが、 <b>ID は据え置く</b> ──
            //   学習ファイル・Tier 表・boss_tuning.json のキーが ID 基準で、
            //   改名すると過去データと切れる (3 層プールの boss_layer2/4 と同じ扱い)。
            if (floor == 8) return BossIds.Layer7Prefix;
            if (floor == 1 || floor == 5 || floor == 6)
                return BossIds.LayerPrefix + floor;
            return null;   // 2 層 / 4 層 / 7 層 にボスマスは無い
        }

        /// <summary>マップに出す戦闘名。 未開示なら null (呼び出し側で「？」を出す)。
        ///
        /// <para><b>単体もペアもボスも必ず名前を返す。</b> 名前の有無で構成が漏れると
        /// 「この名前は危険だった」と覚える必要が消えるため。 対応表に無い敵だけ
        /// 敵の表示名へ落ちる (新しい敵を足したら <c>EncounterPresets.SoloTitles</c> にも足す)。</para></summary>
        public static string EncounterTitle(MapSystem.MapNode node, int floor = 0)
        {
            if (node == null || !node.encounterRevealed) return null;

            // ボスは EnsureEncounter が焼き付けた ID をそのまま読む (3 層は 3 体プールの抽選結果)。
            //   **裏ボスは載らない** ── 到着時にレイピア所持で差し替わるので、
            //   事前に出すと分岐が解決する前に正体が漏れる。
            var p = EncounterPresets.Get(node.encounterId);
            if (p != null && !string.IsNullOrEmpty(p.title)) return p.title;

            string solo = EncounterPresets.SoloTitle(node.encounterEnemyA);
            if (!string.IsNullOrEmpty(solo)) return solo;
            return EnemyDatabase.Get(node.encounterEnemyA)?.displayName;
        }

        /// <summary>抽選候補の構築 (<see cref="PickEnemy"/> と共通)。 エリートと雑魚は混ぜない。</summary>
        private static List<EnemyData> BuildCandidates(int floor, bool elite = false)
        {
            int minFloor = Mathf.Max(1, floor - 2);
            var candidates = EnemyDatabase.GetByFloorRange(minFloor, floor);
            candidates?.RemoveAll(e => IsBossOnly(e) || e.elite != elite);
            if (candidates == null || candidates.Count == 0)
            {
                candidates = EnemyDatabase.GetByFloor(floor);
                candidates?.RemoveAll(e => IsBossOnly(e) || e.elite != elite);
            }
            return candidates;
        }

        /// <summary>
        /// フロアに応じた報酬コインを計算
        /// </summary>
        public static int CalculateRewardCoins(int floor, bool playerWon, int turnsUsed)
            => CalculateRewardCoins(floor, playerWon, turnsUsed, 1f);

        /// <summary>報酬コイン。 <paramref name="encounterMultiplier"/> は 2 体戦の 2 倍など、
        /// エンカウントプリセット由来の倍率。</summary>
        public static int CalculateRewardCoins(int floor, bool playerWon, int turnsUsed,
                                               float encounterMultiplier)
        {
            if (!playerWon) return 0;

            // **2026-08-10 経済リスケール: 価格 ×5 / 収入 ×3。**
            //   価格側の二重デノミを解いた (店頭 1〜5G → 4〜20G) のに合わせ、 収入は ×3 に留める。
            //   ×5 だと購入数が現状 (1ラン 29 個 ＝ 提示 60 枠の約半分) のままで、
            //   店が「何を諦めるか」の選択にならないため。 目標は 1ラン 19 個前後。
            //   グラデーション (F1..F7 で 7〜10) は 1/5 デノミ時に潰れたままなので、
            //   ここでは復活させない ── 一度に 1 つのことだけ変える。
            // **2026-09-09: ×3 → ×4 (1 勝あたり 6G → 8G)。** 通常戦のアイテムドロップを
            //   50% → 15% へ落とした代償 (GameManager の戦闘勝利報酬)。 現物ではなく金で払う形に寄せ、
            //   「戦闘を経済の主軸へ」という筋は残す。 **意図的に過少補償**している ──
            //   1 品の価値は band +0.10 前後 ＝ 約 21G 相当なので、 失った 0.35 品/戦 を
            //   完全に補うなら +7G 必要だが、 供給を絞るのが目的なので +2G に留める。
            // **2026-09-15: ×4 → ×6** (CombatRewards.GoldScale)。 エリートの確定ドロップを
            //   外し通常戦も 15% → 10% へ落としたぶんを金で返す ── 供給を店での購入へ寄せる。
            //   係数の正本は GameLoop.CombatRewards (ドロップ率と同じ場所で持つ)。
            int baseReward = Mathf.Max(1, Mathf.CeilToInt((7 + floor / 2) / 5f))
                             * CombatRewards.GoldScale;

            // フロアデバフの報酬倍率（Fortune層・shopPriceMultiplier等）
            var mod = MapSystem.FloorModifierDatabase.Get(floor);
            if (mod != null && Mathf.Abs(mod.coinRewardMultiplier - 1f) > 0.001f)
                baseReward = Mathf.Max(1, Mathf.CeilToInt(baseReward * mod.coinRewardMultiplier));

            if (Mathf.Abs(encounterMultiplier - 1f) > 0.001f)
                baseReward = Mathf.Max(1, Mathf.CeilToInt(baseReward * encounterMultiplier));

            return baseReward;
        }

        /// <summary>
        /// フロアに応じたHP回復量を計算（戦闘間）
        /// </summary>
        public static int CalculateHealAmount(int floor)
        {
            // 基本: 5HP回復、後半フロアは少なめ
            if (floor <= 3) return 5;
            if (floor <= 5) return 3;
            return 2;
        }
    }
}
