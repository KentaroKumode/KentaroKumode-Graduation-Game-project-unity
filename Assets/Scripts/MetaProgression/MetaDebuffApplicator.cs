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
        //  Lv2: 凶暴化する魔物
        // ============================================================

        /// <summary>敵の与ダメージへの加算量。50%確率で +1、それ以外 0。</summary>
        public static int RollEnemyDamageBonus()
        {
            if (!IsActive(MetaDebuffLevel.Lv2_凶暴化する魔物)) return 0;
            return Random.value < 0.5f ? 1 : 0;
        }

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
        //  Lv5: 狂った時計
        // ============================================================

        /// <summary>戦闘ターン数に応じた敵の追加ダメージ（7ターン目以降 +1/ターン、最大+5）。</summary>
        public static int GetMadClockBonus(int currentTurn)
        {
            if (!IsActive(MetaDebuffLevel.Lv5_狂った時計)) return 0;
            if (currentTurn < 7) return 0;
            return Mathf.Min(5, currentTurn - 6);
        }

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
        //  Lv8: 飢餓の極地
        // ============================================================

        /// <summary>フロア毎のハンガー初期値減算量（Lv8 ON なら -2）。</summary>
        public static int GetHungerInitialPenalty()
            => IsActive(MetaDebuffLevel.Lv8_飢餓の極地) ? 2 : 0;

        // ============================================================
        //  Lv9: 崩壊の前触れ
        // ============================================================

        /// <summary>前哨基地マスの効果が無効化されているか。マス自体は残す。</summary>
        public static bool IsForwardBaseDisabled()
            => IsActive(MetaDebuffLevel.Lv9_崩壊の前触れ);

        // ============================================================
        //  Lv10: 天変地異
        // ============================================================

        /// <summary>敵ダメージの倍率（Lv10 ON で 2.0、それ以外 1.0）。</summary>
        public static float GetEnemyDamageMultiplier()
            => IsActive(MetaDebuffLevel.Lv10_天変地異) ? 2f : 1f;

        /// <summary>敵が初回致命傷で 1HP 耐えるかどうか（Lv10）。</summary>
        public static bool EnemySurvivesFirstLethal()
            => IsActive(MetaDebuffLevel.Lv10_天変地異);
    }
}
