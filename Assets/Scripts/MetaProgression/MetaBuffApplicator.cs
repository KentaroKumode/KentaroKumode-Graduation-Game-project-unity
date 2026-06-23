using UnityEngine;
using GameLoop;

namespace MetaProgression
{
    /// <summary>
    /// バフ効果を各システムに適用するための中央ヘルパー。
    /// 各システムからは Get* / Apply* メソッドを呼ぶだけで良い。
    /// </summary>
    public static class MetaBuffApplicator
    {
        private static MetaProgressState S => MetaProgressManager.Instance?.State;

        // ============================================================
        //  RunState 初期化時の補正
        // ============================================================

        /// <summary>RunState.Initialize 直後に呼んで開幕値を底上げする。</summary>
        public static void ApplyToRunStart(RunState run, int baseHP)
        {
            if (run == null) return;
            var s = S;
            if (s == null) return;

            int hp = baseHP + s.hpBonus;
            run.playerMaxHP = hp;
            run.playerHP = hp;
            // 開幕ゴールド: Gold() 段を 5 段に絞ったため、 1段=+1G の素通しに変更。
            // (旧 21段→/5 デノミ式は段数削減と引き換えに撤廃)
            int startGold = s.goldBonus;
            run.coins += startGold;
            run.weaponMaterials += s.startMaterial;

            Debug.Log($"[MetaBuff] 開幕補正: HP {baseHP}→{hp}, Gold +{startGold}, Material +{s.startMaterial}");
        }

        // ============================================================
        //  戦闘関連
        // ============================================================

        /// <summary>被ダメージ補正（負数を返す。0 未満にはならないよう呼び出し側で clamp）。</summary>
        public static int GetDamageReduction()
            => S != null ? S.damageReduce : 0;

        /// <summary>戦闘勝利時の追加ゴールド。最大3段(MetaBuffTrack)で raw 0-3 だが、
        /// 1/5 デノミ済み新経済下では出力上限を 2 にクランプする。
        /// 加えて呼び出し側(GameManager)で「ボス撃破時のみ」適用するよう制限している。</summary>
        public static int GetCombatGoldBonus()
            => S != null ? UnityEngine.Mathf.Min(2, S.combatGoldBonus) : 0;

        /// <summary>会心ダイスへの追加補正値（0/1/2/3）。</summary>
        public static int GetCritBonus()
            => S != null ? S.critLevel : 0;

        /// <summary>ダイス合計値補正（出目の合計に追加で加算）。</summary>
        public static int GetDiceTotalBonus()
            => S != null ? S.diceTotalBonus : 0;

        // ============================================================
        //  希望ゲージ減少の軽減 (ADR-0002 で飢餓から希望に統合)
        // ============================================================

        /// <summary>戦闘後の希望ゲージ減少の軽減量（最大3）。0 未満にはならないよう呼び出し側で clamp。</summary>
        public static int GetHopeLossReduction()
            => S != null ? S.hopeLossReduce : 0;

        // ============================================================
        //  ショップ
        // ============================================================

        /// <summary>2026-06-22: 旧「購入返金確率」 を廃止し、 「特売品」 機構に変更。
        /// メタバフ refundLevel に応じてショップに 1/2/3 個の特売品を出現させる。
        /// 特売品は 20-60% の範囲でランダム割引が適用される。</summary>
        public static int GetSaleItemCount()
        {
            var s = S;
            if (s == null) return 0;
            return Mathf.Clamp(s.refundLevel, 0, 3);
        }

        /// <summary>特売割引率の最小・最大値 (%)。 適用時はこの範囲で一様乱数。</summary>
        public const int SaleDiscountMinPct = 20;
        public const int SaleDiscountMaxPct = 60;

        /// <summary>互換用: 旧 RollRefund は no-op に。 特売機構へ移行済のため。</summary>
        public static int RollRefund(int paidAmount, RunState run) => 0;
        /// <summary>互換用: 旧 GetRefundChance は 0 を返す (特売機構へ移行済)。</summary>
        public static float GetRefundChance() => 0f;

        // ============================================================
        //  ボス追加報酬
        // ============================================================

        public enum BossExtraDrop { None, Normal, Rare }

        /// <summary>ボス撃破時の追加パッシブ獲得状態を返す。</summary>
        public static BossExtraDrop GetBossExtraDrop()
        {
            var s = S;
            if (s == null) return BossExtraDrop.None;
            if (s.bossExtraRareUnlocked) return BossExtraDrop.Rare;
            if (s.bossExtraNormalUnlocked) return BossExtraDrop.Normal;
            return BossExtraDrop.None;
        }

        // ============================================================
        //  新規バフ
        // ============================================================

        /// <summary>〈開幕パッシブ〉解放済みかどうか。RunStart で1個獲得。</summary>
        public static bool IsStartingPassiveItemUnlocked()
            => S != null && S.startingPassiveItemUnlocked;

        /// <summary>フロアクリア時の追加回復量（0/1/2）。</summary>
        public static int GetFloorClearHeal()
            => S != null ? S.floorClearHeal : 0;

        /// <summary>〈宝箱の財宝〉解放済みかどうか。宝箱マスでゴールドも獲得する。</summary>
        public static bool IsTreasureChestGoldUnlocked()
            => S != null && S.treasureChestGoldUnlocked;

        /// <summary>〈値下げ交渉〉解放済みかどうか。ショップで強盗行動が可能。</summary>
        public static bool IsShopRobberyUnlocked()
            => S != null && S.shopRobberyUnlocked;

        /// <summary>会心倍率（恒久バフでの会心倍率変更は撤廃され、常に 2.0）。
        /// 装備パッシブ等が個別に上書きしうるが、メタ恒久値としては固定。</summary>
        public static float GetCriticalMultiplier() => 2.0f;

        // ============================================================
        //  与ダメ%ボーナス（コモン10段・最大+50%、他%倍率と加算合成）
        // ============================================================

        /// <summary>与ダメ%ボーナス(0..50)。CombatManager 側で outgoingDamageMultiplier に加算する。</summary>
        public static int GetOutgoingDamagePct()
            => S != null ? UnityEngine.Mathf.Clamp(S.outgoingDamagePct, 0, 50) : 0;

        // ============================================================
        //  追加 unlock 系フラグ
        // ============================================================

        /// <summary>ラストスタンド発動時の最大HP半減を無効化するか。</summary>
        public static bool IsLastStandHpLossDisabled()
            => S != null && S.lastStandHpLossDisabled;

        /// <summary>フロアボス前の休憩エリアで回復+強化が同時にできるか。</summary>
        public static bool IsBossRestHealAndUpgradeUnlocked()
            => S != null && S.bossRestHealAndUpgradeUnlocked;

        /// <summary>会心ダメージ加算量（Lv58 final、 既定 0 / フル取得時 1.0 = +100%）。
        /// CombatContext.criticalMultiplier に加算される。 他の会心ダメージバフと加算合成。</summary>
        public static float GetCritDamageBonus()
            => S != null ? S.critDamageBonus : 0f;
    }
}
