using GameLoop;
using InventorySystem.Helpers;

namespace AutoTest
{
    /// <summary>
    /// BOT専用: インベントリ満杯時の取捨選択 (案A・2026-06-04)。
    ///
    /// ファーム源 (戦闘報酬/宝箱/イベント/Λドロップ) は PassiveAddHelper.AddPassiveItem で
    /// 容量無視に積まれるため、 ヘッドレスBOTでは所持パッシブが容量を超えて無限増殖し、
    /// RunPassiveSync が全部を発動させてしまう (人間=グリッド容量縛りとの乖離)。
    ///
    /// 本クラスは各アクション前に容量を点検し、 超過していれば
    /// 「L1スコア最下位の廃棄可能パッシブ」から容量内に収まるまで捨てる。
    ///   ・廃棄可能 = チェーン/初期アイテム(ItemLearningStats.ExcludedFromLift)でなく、
    ///               装備中の武器/ダイスでもなく、 容量を消費する(セル>0)パッシブ。
    ///   ・チェーンアイテム(確信チェーン・進化武器階梯 等)は常に保護 (ユーザー指定)。
    /// </summary>
    public static class InventoryTriage
    {
        /// <summary>容量超過分を売却 (ショップ由来) または廃棄 (非ショップ由来)。 戻り値 = 処分した個数。
        /// 2026-06-22: ショップ購入記録 (run.shopPurchasedCounts) がある品は TrySell で G 換金、 それ以外は無償廃棄。
        /// 2026-06-22b: Λ 取得品 (lambdaProtectedItemIds) は discard 対象外。 Λ 滞在中の discard は計測。</summary>
        public static int EnforceCapacity(RunState run)
        {
            if (run?.ownedPassiveItems == null) return 0;
            int processed = 0;
            int guard = 64; // 無限ループ安全弁 (廃棄可能が尽きたら break するが二重の保険)
            while (InventoryCapacity.UsedCells(run) > InventoryCapacity.Capacity(run) && guard-- > 0)
            {
                int worst = FindWorstDiscardable(run);
                if (worst < 0) break; // 廃棄可能な品が無い (全て保護対象 or 装備中 or Λ保護)
                string id = run.ownedPassiveItems[worst];
                int beforeCoins = run.coins;
                if (TrySellFromBot(run, worst))
                {
                    int gain = run.coins - beforeCoins;
                    UnityEngine.Debug.Log($"[InventoryTriage] 容量超過 → 売却: {id} (+{gain}G)");
                }
                else
                {
                    PassiveAddHelper.RemoveAt(run, worst);
                    UnityEngine.Debug.Log($"[InventoryTriage] 容量超過 → 廃棄: {id} (非ショップ由来、 score={LearnedPriorityProvider.Score(id)})");
                }
                processed++;
                // Λ 滞在中の処分は別カウント (容量圧迫ロスとして可視化)
                if (run.inLambda) run.lambdaItemsDiscardedDuringLambda++;
            }
            return processed;
        }

        /// <summary>BOT 用: 指定 index のパッシブをショップ売却。 成功時 true。
        /// 2026-06-23a: ショップ滞在中のみ売却可 (移動中は false → 呼出側で廃棄フォールバック)。
        /// 2026-06-23b: ショップ由来在庫 (shopPurchasedCounts) 制限を撤廃 ── 非ショップ由来 (イベント/戦闘ドロップ等) も売却可。
        ///              商人の符牒のみが売却の障害。</summary>
        public static bool TrySellFromBot(RunState run, int passiveIndex)
        {
            if (run == null || run.ownedPassiveItems == null) return false;
            if (passiveIndex < 0 || passiveIndex >= run.ownedPassiveItems.Count) return false;
            // ショップ滞在中でなければ売却不可 (= ショップに行かないと売れない、 自然な仕様)
            var sm = InventorySystem.Shop.ShopManager.Instance;
            if (sm == null || sm.Current == null) return false;
            // 商人の符牒は売却を全面阻止
            if (run.OwnsPassive("商人の符牒")) return false;
            // ShopManager.TrySell を呼ぶ (在庫減算・コイン加算を委譲、 由来問わず)
            return sm.TrySell(InventorySystem.Shop.ShopManager.SellSource.Passive, passiveIndex, run);
        }

        /// <summary>廃棄可能パッシブのうち L1スコア最下位の index。 無ければ -1。
        /// 同スコアは先頭(=古い取得)から捨てる。
        /// 2026-06-22d: Λ 保護バイアスを撤廃。 Λ 取得品も raw score で公平判定。
        ///   低 Tier Λ 品が高 Tier 既存品を押し出すバグへの根本対策 ── 「Λ 入替判断」 は
        ///   triage が低 Score を捨てる自然な挙動で実現される。 Λ 品が高 Score なら残り、
        ///   低 Score なら自身が捨てられる。</summary>
        private static int FindWorstDiscardable(RunState run)
        {
            var list = run.ownedPassiveItems;
            int worstIdx = -1, worstScore = int.MaxValue;
            for (int i = 0; i < list.Count; i++)
            {
                string id = list[i];
                if (!IsDiscardable(run, id)) continue;
                // 2026-06-22 高速化: penalty 計算は Triage には不要 (raw Tier で worst を判定)
                int sc = LearnedPriorityProvider.RawScore(id);
                if (sc < worstScore) { worstScore = sc; worstIdx = i; }
            }
            return worstIdx;
        }

        private static bool IsDiscardable(RunState run, string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            // チェーン/初期アイテムは保護 (確信チェーン・進化武器階梯・木ダイス・ちいさな灯火 等)
            if (ItemLearningStats.ExcludedFromLift.Contains(id)) return false;
            // 装備中の武器/ダイスは保護
            if (id == run.equippedWeaponId || id == run.equippedDiceId) return false;
            // 容量を消費しないもの(ダイス/素材)は捨てても容量が減らない → 対象外
            if (InventoryCapacity.CellsOf(id) <= 0) return false;
            // 2026-06-22c: Λ 取得品も基本 discardable (FindWorstDiscardable で +1 バイアス適用)
            return true;
        }
    }
}
