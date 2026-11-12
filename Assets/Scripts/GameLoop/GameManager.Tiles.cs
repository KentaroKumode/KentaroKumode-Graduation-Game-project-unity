using System;
using System.Collections.Generic;
using UnityEngine;
using Codex;
using CombatSystem;
using EventSystem;
using ItemSystem;
using ItemSystem.PassiveSkills;
using ItemSystem.Shop;
using MapSystem;

namespace GameLoop
{
    /// <summary>GameManager: マスの起動と交換マス。 GameManager.Floor.cs から切り出した (2026-11-12・中身は同じ)。</summary>
    public partial class GameManager
    {
        /// <summary>タイルの種類に応じてフェーズを遷移</summary>
        private void ActivateTile(MapNode node)
        {
            var effectiveType = node.EffectiveType;

            // 収束ノード(Boss/Outpost)以外の一度きりタイルは、再訪時に再発火させない。
            // （イベントの無限ファーム＝同行マス往復によるデッドロックを構造的に防ぐ）
            //
            // **除外判定は EffectiveType ではなく素の type で行う (2026-08-08 修正)。**
            //   resolvedType を立てたまま戻さない経路があると、 EffectiveType 基準ではノードの種類が
            //   すり替わって oneShot の判定を誤る (旧 Λ 環状線で全ノードが沈黙した原因・GAME.md §24)。
            var baseType = node.type;
            bool oneShot = baseType != TileType.Boss && baseType != TileType.Outpost;
            if (oneShot && node.activated)
            {
                SetPhase(GamePhase.MapNavigation);
                return;
            }
            if (oneShot) node.activated = true;

            OnTileActivated?.Invoke(effectiveType);
            Log($"タイル起動: {TileToJapanese(effectiveType)} ({node.id})");

            // T4-A〈破綻〉: 有利マスが空白になっていることがある。
            //   **oneShot で activated を立てた後に判定する** ── 空白化したマスは
            //   「踏んだ」扱いで確定させ、 往復して引き直せないようにする。

            switch (effectiveType)
            {
                case TileType.Battle:
                    StartBattleTile();
                    break;
                case TileType.EliteBattle:
                    IsEliteSecondFight = false;
                    firstEliteEnemy = null;
                    StartBattleTile();
                    break;
                case TileType.Boss:
                    StartBossTile();
                    break;
                case TileType.Rest:
                    SetPhase(GamePhase.RestStop);
                    break;
                case TileType.Shop:
                    // [変更 2026-09-12] 強盗後の「出禁」(ショップマス素通り) は既定で撤去。
                    //   罰は ShopManager 側の価格割増へ移した ── 出禁だと強盗報酬の 90〜180G が
                    //   使い道ごと消えて、 報酬と罰が打ち消し合っていた。
                    //   下の分岐は比較スイープ用の退避経路 (ShopManager.RobberyBlocksShops)。
                    if (Run != null && Run.shopRobberyDone
                        && ItemSystem.Shop.ShopManager.RobberyBlocksShops)
                    {
                        Log("ショップ閉鎖中: 値下げ交渉により商人ギルドから出禁 (旧仕様・比較用)");
                        SetPhase(GamePhase.MapNavigation);
                        break;
                    }
                    if (node.isFalseMerchant) StartFalseMerchantCombat();
                    else EnterShop();
                    break;
                case TileType.Event:
                    if (SuppressEvents)   // 計測専用 (上の SuppressEvents)
                    {
                        SetPhase(GamePhase.MapNavigation);
                        break;
                    }
                    BeginEventEncounter();
                    break;
                case TileType.Treasure:
                    OpenTreasure();
                    break;
                case TileType.Exchange:
                    SetPhase(GamePhase.ExchangeTile);
                    break;
                case TileType.Gate:
                    BeginGateRitual();
                    break;
                default:
                    SetPhase(GamePhase.MapNavigation);
                    break;
            }
        }

        // ---- 交換マス（パッシブ1つ → 上位Tierパッシブをランダム入手・任意） ----

        /// <summary>交換可能か（渡せるパッシブを所持しているか）。</summary>
        public bool CanExchangeTile
            => CurrentPhase == GamePhase.ExchangeTile && FindLowestTierOwnedPassiveIndex() >= 0;

        /// <summary>所持パッシブのうち最も低Tierのものの index。無ければ -1。</summary>
        private int FindLowestTierOwnedPassiveIndex()
        {
            var owned = Run?.ownedPassiveItems;
            if (owned == null) return -1;
            var db = ItemDatabase.Instance;
            int bestIdx = -1;
            ItemRarity bestR = ItemRarity.MYTHIC;
            for (int i = 0; i < owned.Count; i++)
            {
                var it = db?.GetItem(owned[i]);
                if (it == null || it.category != ItemCategory.Passive) continue;
                if (bestIdx < 0 || it.rarity < bestR) { bestR = it.rarity; bestIdx = i; }
            }
            return bestIdx;
        }

        /// <summary>交換実行: 最低Tierの所持パッシブを渡し、上位Tierのパッシブをランダム入手。</summary>
        public void DoExchangeTile()
        {
            if (CurrentPhase != GamePhase.ExchangeTile) return;
            int idx = FindLowestTierOwnedPassiveIndex();
            if (idx < 0) { SetPhase(GamePhase.MapNavigation); return; }

            var db = ItemDatabase.Instance;
            string givenId = Run.ownedPassiveItems[idx];
            var given = db?.GetItem(givenId);
            if (given == null) { SetPhase(GamePhase.MapNavigation); return; }

            string resultId = PickHigherTierPassive(given.rarity);
            if (string.IsNullOrEmpty(resultId))
            {
                Log("交換: 上位Tierの秘宝が無く成立せず");
                SetPhase(GamePhase.MapNavigation);
                return;
            }

            ItemSystem.Helpers.PassiveAddHelper.RemoveAt(Run, idx);
            Loadout.Gain(Run, resultId);
            var rd = db?.GetItem(resultId);
            Log($"交換マス: 「{given.displayName}」を渡し「{(rd != null ? rd.displayName : resultId)}」を入手");
            SetPhase(GamePhase.MapNavigation);
        }

        /// <summary>交換せずに通過。</summary>
        public void SkipExchangeTile()
        {
            if (CurrentPhase != GamePhase.ExchangeTile) return;
            SetPhase(GamePhase.MapNavigation);
        }

        /// <summary>given より高Tier（上限LEGENDARY、given が LEGENDARY なら据え置き）のパッシブを
        /// 重複排除の上でレア度重み抽選。未所持が枯渇したら重複許可で再抽選（上位からを許可）。</summary>
        private string PickHigherTierPassive(ItemRarity givenRarity)
        {
            var db = ItemDatabase.Instance;
            var all = db?.GetAllItems();
            if (all == null || all.Count == 0) return null;

            bool atCap = givenRarity >= ItemRarity.LEGENDARY;
            System.Func<ItemRarity, bool> tierOk = atCap
                ? (System.Func<ItemRarity, bool>)(r => r == ItemRarity.LEGENDARY)
                : (r => r > givenRarity && r <= ItemRarity.LEGENDARY);

            var excl = BuildDedupExclude();
            var pool = new System.Collections.Generic.List<CompleteItemData>();
            foreach (var it in all)
            {
                if (it == null || it.category != ItemCategory.Passive) continue;
                if (!ItemSystem.Shop.EventOnlyItemFilter.IsAllowed(it)) continue;
                if (!tierOk(it.rarity)) continue;
                if (excl.Contains(it.internalName)) continue;
                pool.Add(it);
            }
            if (pool.Count == 0) // 枯渇 → 重複許可で再構築
            {
                foreach (var it in all)
                {
                    if (it == null || it.category != ItemCategory.Passive) continue;
                    if (!ItemSystem.Shop.EventOnlyItemFilter.IsAllowed(it)) continue;
                    if (!tierOk(it.rarity)) continue;
                    pool.Add(it);
                }
            }
            if (pool.Count == 0) return null;
            return ItemSystem.RarityWeightedPicker.Pick(pool)?.internalName;
        }

        /// <summary>所持パッシブからランダムに1つ除去（刻印も同期）。除去した index を返す。無ければ -1。</summary>
        private int LoseRandomPassiveItem()
        {
            var owned = Run?.ownedPassiveItems;
            if (owned == null || owned.Count == 0) return -1;
            int idx = GameLoop.GameRng.RangeAuto("GameManager.5", 0, owned.Count);
            string lostId = owned[idx];
            ItemSystem.Helpers.PassiveAddHelper.RemoveAt(Run, idx);
            var dd = ItemDatabase.Instance?.GetItem(lostId);
            Log($"恒久アイテム喪失: {(dd != null ? dd.displayName : lostId)}");
            return idx;
        }
    }
}
