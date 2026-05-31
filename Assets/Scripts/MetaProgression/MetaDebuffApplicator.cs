using UnityEngine;
using GameLoop;

namespace MetaProgression
{
    /// <summary>
    /// デバフ効果を各システムに適用するための中央ヘルパー。
    /// </summary>
    public static class MetaDebuffApplicator
    {
        private static MetaProgressState S => MetaProgressManager.Instance?.State;

        public static bool IsActive(MetaDebuffLevel lv)
            => S != null && S.HasDebuff(lv);

        // ============================================================
        //  Lv1: 困窮した商隊
        // ============================================================

        /// <summary>ショップ価格倍率（1.0 or 1.25）。</summary>
        public static float GetShopPriceMultiplier()
            => IsActive(MetaDebuffLevel.Lv1_困窮した商隊) ? 1.25f : 1f;

        // ============================================================
        //  Lv2: 俊敏
        // ============================================================

        /// <summary>敵が各戦闘の最初の1回の被弾を必ず回避するか（Lv2）。
        /// 「初回回避済み」の管理は CombatManager 側 (metaAgilityDodgeUsed) が行う。</summary>
        public static bool EnemyDodgesFirstHit()
            => IsActive(MetaDebuffLevel.Lv2_俊敏);

        // ============================================================
        //  Lv3: 向かい風
        // ============================================================

        /// <summary>プレイヤーの与ダメージへの減算量（既に 0 にはならないよう外側で clamp 推奨）。</summary>
        public static int GetPlayerDamageReduction()
            => IsActive(MetaDebuffLevel.Lv3_向かい風) ? 1 : 0;

        // ============================================================
        //  Lv4: 前途多難
        // ============================================================

        /// <summary>マップ視界の最大手数（0 = 無効、有効時は 2）。</summary>
        public static int GetMapSightLimit()
            => IsActive(MetaDebuffLevel.Lv4_前途多難) ? 2 : 0;

        // ============================================================
        //  Lv5: 偽の商人
        // ============================================================

        /// <summary>ショップマスが偽商人へ変化する確率（Lv5 ON で 0.2、OFF で 0）。</summary>
        public static float GetFalseMerchantChance()
            => IsActive(MetaDebuffLevel.Lv5_偽の商人) ? 0.2f : 0f;

        // ============================================================
        //  Lv6: 死神の影 / Lv10: 天変地異 — 恒久デバフ抽選
        // ============================================================

        /// <summary>3層突入時に呼ぶ。Lv6 ON なら恒久デバフを1つ抽選して付与。</summary>
        public static void TryGrantOnFloor3(RunState run)
        {
            if (!IsActive(MetaDebuffLevel.Lv6_死神の影)) return;
            GrantPermanent(run, "Lv6_死神の影");
        }

        /// <summary>1層突入時に呼ぶ。Lv10 ON なら恒久デバフを1つ抽選して付与。</summary>
        public static void TryGrantOnFloor1(RunState run)
        {
            if (!IsActive(MetaDebuffLevel.Lv10_天変地異)) return;
            GrantPermanent(run, "Lv10_天変地異");
        }

        private static void GrantPermanent(RunState run, string source)
        {
            if (run == null) return;
            string id = MetaPermanentDebuffPicker.Pick(run);
            if (string.IsNullOrEmpty(id))
            {
                Debug.LogWarning($"[MetaDebuff] {source}: 恒久デバフプールが空 or 全保持済みのため付与なし");
                return;
            }
            run.permanentDebuffs.Add(id);
            Debug.Log($"[MetaDebuff] {source}: 恒久デバフ「{id}」を付与");
        }

        // ============================================================
        //  Lv7: 補給断絶
        // ============================================================

        /// <summary>前哨基地での回復値の上限（最大HPの50%）。Lv7 OFF 時は -1（=制限なし）。</summary>
        public static int GetForwardBaseHealCap(int playerMaxHP)
        {
            if (!IsActive(MetaDebuffLevel.Lv7_補給断絶)) return -1;
            return Mathf.Max(1, playerMaxHP / 2);
        }

        // ============================================================
        //  Lv8: 飢餓の極地 (2026-05-31 リワーク: 前哨基地全回復化で旧効果失効 → 飢餓ダメ×2 に置換)
        // ============================================================

        /// <summary>飢餓ダメージ倍率（Lv8 ON なら 2、 OFF なら 1）。</summary>
        public static int GetStarvationDamageMultiplier()
            => IsActive(MetaDebuffLevel.Lv8_飢餓の極地) ? 2 : 1;

        // ============================================================
        //  Lv9: 鋼の皮膚
        // ============================================================

        /// <summary>敵が初回致命傷で 1HP 耐えるかどうか（Lv9）。</summary>
        public static bool EnemySurvivesFirstLethal()
            => IsActive(MetaDebuffLevel.Lv9_鋼の皮膚);

        /// <summary>旧 Lv9(崩壊の前触れ) は廃止。前哨基地は常に有効（互換のため false 固定）。</summary>
        public static bool IsForwardBaseDisabled() => false;

        // ============================================================
        //  Lv10: 天変地異
        // ============================================================

        /// <summary>敵ダメージの倍率（Lv10 ON で 2.0、それ以外 1.0）。</summary>
        public static float GetEnemyDamageMultiplier()
            => IsActive(MetaDebuffLevel.Lv10_天変地異) ? 2f : 1f;

        /// <summary>ラストスタンドの発動が封じられているか（Lv10）。</summary>
        public static bool IsLastStandDisabled()
            => IsActive(MetaDebuffLevel.Lv10_天変地異);
    }
}
