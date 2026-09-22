using UnityEngine;
using GameLoop;

namespace InventorySystem.Helpers
{
    /// <summary>
    /// パッシブアイテム取得時に <see cref="RunState.ownedPassiveItems"/> を更新するための薄いヘルパー。
    ///
    /// 既存コードに散らばっている <c>run.ownedPassiveItems.Add(id)</c> 呼び出しを
    /// なるべくこちらへ寄せて、重複判定の漏れを防ぐ。
    ///
    /// 2026-08-24: 〈刻印〉(PassiveSigil) の削除に伴い、並列配列 <c>passiveSigils</c> の
    /// 同期責務が無くなった。後継として置かれた〈副次ステータス〉も 2026-09-15 に全廃したので、
    /// ここが持つのは重複判定と取得元の計装だけ。経緯は docs/GAME.md §23-4 / §24。
    /// </summary>
    /// <summary>パッシブが**どこから入ってきたか**の計装 (2026-09-09)。
    ///
    /// <para><b>なぜ要るか。</b> 1 ラン の所持は約 68 品だが、 <c>ショップ購入数 16.5</c> しかない。
    /// つまり供給の 2/3 は無料経路で、 <b>価格を上げても届かない</b>
    /// (実測: パッシブ価格 4/6/8/10 → 7/8/9/10 で床が 19.8% → 15.3% にしか動かなかった)。
    /// どの無料経路が何品配っているかを知らないと、 削る先を選べない。</para>
    ///
    /// <para><b>呼び出し側を触らない。</b> 取得経路は <see cref="GameManager.CurrentPhase"/> と
    /// <c>Run.inLambda</c> から引ける ── ショップ / 宝箱 / イベント / 戦闘報酬 / 交換マス は
    /// 別々のフェーズなので、 helper の中で 1 回読むだけで足りる。 20 箇所ある呼び出し側に
    /// 引数を足して回ると、 足し忘れが「その他」に化けて静かに嘘をつく。</para></summary>
    public static class PassiveSourceAudit
    {
        /// <summary>経路 → 取得数 (バッチ累計)。</summary>
        public static readonly System.Collections.Generic.Dictionary<string, int> Counts
            = new System.Collections.Generic.Dictionary<string, int>();

        public static void Reset() { Counts.Clear(); }

        /// <summary>いまの局面から取得経路名を引く。</summary>
        public static string CurrentSource(RunState run)
        {
            if (run != null && run.inLambda) return "Λ層";
            var gm = GameManager.Instance;
            if (gm == null) return "その他";
            switch (gm.CurrentPhase)
            {
                case GameManager.GamePhase.ShopVisit:      return "ショップ";
                case GameManager.GamePhase.TreasureOpen:   return "宝箱";
                case GameManager.GamePhase.EventEncounter: return "イベント";
                case GameManager.GamePhase.ExchangeTile:   return "交換マス";
                case GameManager.GamePhase.RestStop:       return "休憩";
                case GameManager.GamePhase.GateRitual:     return "門";
                case GameManager.GamePhase.Reward:
                case GameManager.GamePhase.BattleResult:   return "戦闘報酬";
                case GameManager.GamePhase.FloorClear:     return "ボス撃破";
                case GameManager.GamePhase.Title:
                case GameManager.GamePhase.RunStart:
                case GameManager.GamePhase.FloorIntro:     return "開幕";
                default:                                   return NodeSource(gm);
            }
        }

        /// <summary>フェーズが変わらないマスの経路をノード種別から引く。
        ///
        /// <para><b>宝箱はフェーズを変えない。</b> <c>ActivateTile</c> は Shop / Event / Exchange /
        /// Gate では <c>SetPhase</c> するが、 <c>Treasure</c> は <c>OpenTreasure()</c> を
        /// 直接呼ぶだけなので <c>MapNavigation</c> のまま。 フェーズだけで分類すると
        /// 宝箱が 0 品に見え、 その分が「MapNavigation」という読めない名前に化ける
        /// (2026-09-09 に実際にそう出た)。</para></summary>
        private static string NodeSource(GameManager gm)
        {
            var node = MapSystem.MapManager.Instance?.CurrentNode;
            if (node == null) return gm.CurrentPhase.ToString();
            switch (node.EffectiveType)
            {
                case MapSystem.TileType.Treasure:     return "宝箱";
                case MapSystem.TileType.Event:        return "イベント";
                case MapSystem.TileType.Shop:         return "ショップ";
                case MapSystem.TileType.Exchange:     return "交換マス";
                case MapSystem.TileType.Battle:
                case MapSystem.TileType.EliteBattle:
                case MapSystem.TileType.Boss:         return "戦闘報酬";
                default: return gm.CurrentPhase + ":" + node.EffectiveType;
            }
        }

        public static void Note(RunState run)
        {
            string s = CurrentSource(run);
            Counts.TryGetValue(s, out int c);
            Counts[s] = c + 1;
        }
    }

    public static class PassiveAddHelper
    {
        /// <summary>[計装 2026-09-14] 取得したパッシブのレアリティ別件数 (index = ItemRarity)。
        /// 提示側 (<see cref="Shop.ShopManager.OfferedByRarity"/>) と対で読む。</summary>
        public static readonly long[] AcquiredByRarity = new long[8];
        public static void ResetAcquiredStats()
        { System.Array.Clear(AcquiredByRarity, 0, AcquiredByRarity.Length); }

        /// <summary>パッシブアイテムを所持リストへ追加。</summary>
        /// <returns>追加できたら true (重複で弾かれた場合 false)。</returns>
        public static bool AddPassiveItem(RunState run, string itemId)
        {
            if (run == null || string.IsNullOrEmpty(itemId)) return false;

            // パッシブアイテムの重複禁止: 現所持 or このランで取得済(売却/廃棄した物も含む)なら付与しない。
            // 武器/ダイス/消費は対象外（カテゴリで判定）。匿名取得("")やDB未登録は従来どおり許可。
            if (IsPassiveCategory(itemId)
                && (run.ownedPassiveItems.Contains(itemId)
                    || (run.seenPassiveItemIds != null && run.seenPassiveItemIds.Contains(itemId))))
            {
                Debug.Log($"[PassiveAdd] 重複のため取得スキップ: {itemId}");
                return false;
            }

            {
                var rdef = ItemDatabase.Instance?.GetItem(itemId);
                if (rdef != null)
                {
                    int ri = (int)rdef.rarity;
                    if (ri >= 0 && ri < AcquiredByRarity.Length) AcquiredByRarity[ri]++;
                }
            }
            run.ownedPassiveItems.Add(itemId);
            run.seenPassiveItemIds?.Add(itemId); // 以後の重複取得を禁止（廃棄後も残す）
            PassiveSourceAudit.Note(run);        // どの経路から入ったかを数える (計装)

            // [剣の舞] を取得し、インベントリに4枚揃ったら〈ブレイドダンス〉へ変化させる。
            // （Finale=ブレイドダンスは IsDance=false のため再帰しない）
            if (SwordDanceSet.IsDance(itemId))
            {
                SwordDanceSet.Acquired++;   // [計装] 提示と取得を分けて数える
                SwordDanceSet.TryTransform(run);
            }

            return true;
        }

        /// <summary>itemId が「パッシブ」カテゴリか（重複禁止の対象）。DB未登録/匿名は false（=従来どおり許可）。</summary>
        private static bool IsPassiveCategory(string itemId)
        {
            var def = ItemDatabase.Instance?.GetItem(itemId);
            if (def == null) return false;
            return def.category == ItemCategory.Passive || def.category == ItemCategory.PassiveItem;
        }

        /// <summary>所持リストから index 指定で除去。</summary>
        public static void RemoveAt(RunState run, int index)
        {
            if (run == null || index < 0) return;
            if (index < run.ownedPassiveItems.Count)
                run.ownedPassiveItems.RemoveAt(index);
        }
    }
}
