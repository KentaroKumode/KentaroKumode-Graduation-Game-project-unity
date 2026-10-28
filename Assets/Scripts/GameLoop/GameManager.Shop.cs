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
    /// <summary>GameManager: ショップ (購入・売却・リロール・強盗)。</summary>
    public partial class GameManager
    {
        /// <summary>ショップマス到達時。マップ巻取り → 在庫生成 → ShopVisit フェーズへ。</summary>
        private void EnterShop()
        {
            Debug.Log("[GameManager] EnterShop called");
            ShopSellMode = false;
            ShopSellSource = ShopManager.SellSource.Passive;

            var sm = ShopManager.Instance;
            if (sm == null) { Debug.LogError("[GameManager] ShopManager.Instance is null!"); return; }
            sm.Generate(Run.Depth);
            SetPhase(GamePhase.ShopVisit);
        }

        public void ToggleShopSellMode()
        {
            if (CurrentPhase != GamePhase.ShopVisit) return;
            ShopSellMode = !ShopSellMode;
            Log($"ショップモード: {(ShopSellMode ? "売却" : "購入")}");
        }

        public void CycleSellSource()
        {
            if (CurrentPhase != GamePhase.ShopVisit || !ShopSellMode) return;
            ShopSellSource = (ShopManager.SellSource)(((int)ShopSellSource + 1) % 3);
            Log($"売却対象: {ShopSellSource}");
        }

        public void ShopBuy(int slotIndex)
        {
            if (CurrentPhase != GamePhase.ShopVisit) return;
            ShopManager.Instance.TryBuy(slotIndex, Run);
        }

        public void ShopSell(int listIndex)
        {
            if (CurrentPhase != GamePhase.ShopVisit) return;
            ShopManager.Instance.TrySell(ShopSellSource, listIndex, Run);
        }

        /// <summary>ショップのリロール。 <b>成否を返す。</b>
        ///
        /// <para><b>呼び出し側はコインの増減で判定しないこと</b> (2026-09-17)。
        /// 〈合札〉のように価格が 0 になる経路があると、 成功してもコインが減らないので
        /// 「失敗」と誤認する。 実際 BOT の余剰リロールは<b>店の 21.6% で金を持ったまま
        /// 抜けており (そのとき平均 224.5G)</b>、 ラン終了時の残金 92G の主因だった。</para></summary>
        public bool ShopReroll()
        {
            if (CurrentPhase != GamePhase.ShopVisit) { RunStats.RerollOutcome[0]++; return false; }
            var sm = ShopManager.Instance;
            if (sm == null) { RunStats.RerollOutcome[1]++; return false; }
            bool ok = sm.TryReroll(Run);
            RunStats.RerollOutcome[ok ? 3 : 2]++;
            return ok;
        }

        /// <summary>
        /// 値下げ交渉 (=強盗) を実行。
        /// 1. ShopManager.TryRobbery: 在庫スナップ・物資コスト・shopRobberyDone (以降の価格割増), ショップを閉じる
        /// 2. 「怪しい商人」(shady_merchant) との特殊エリート戦に突入
        /// 3. HandleCombatEnd 内で勝利時=報酬付与/敗北時=HP1脱出 を分岐
        /// </summary>
        public void ShopRobbery()
        {
            if (CurrentPhase != GamePhase.ShopVisit) return;
            if (!ShopManager.Instance.TryRobbery(Run)) return;

            // 戦闘準備: 怪しい商人を取得
            var data = CombatSystem.EnemyDatabase.Get("shady_merchant");
            if (data == null)
            {
                Debug.LogError("[GameManager] shady_merchant が enemies.json に見つかりません");
                Run.shopRobberyInProgress = false;
                SetPhase(GamePhase.MapNavigation);
                return;
            }
            CurrentEnemy = data;
            OnEnemyEncountered?.Invoke(CurrentEnemy);
            var (dc, dm, cr, df, ft_, str_) = GatherPlayerCombatStats();
            SetPhase(GamePhase.Combat);
            CombatManager.Instance.StartCombat(CurrentEnemy, Run.playerHP, dc, dm, cr, df, str_, ft_);
            Log("値下げ交渉 → 怪しい商人が刃を抜いた");
        }

        public void ExitShop()
        {
            if (CurrentPhase != GamePhase.ShopVisit) return;
            ShopManager.Instance.Close();

            SetPhase(GamePhase.MapNavigation);
        }

        // ================================================================
        //  宝箱マス
        // ================================================================

        /// <summary>最後に開けた宝箱の中身（HUD表示用）</summary>
        public string LastTreasureSummary { get; private set; }
    }
}
