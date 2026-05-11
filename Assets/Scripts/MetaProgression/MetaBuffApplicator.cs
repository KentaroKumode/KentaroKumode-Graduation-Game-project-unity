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
            run.coins += s.goldBonus;
            run.weaponMaterials += s.startMaterial;

            Debug.Log($"[MetaBuff] 開幕補正: HP {baseHP}→{hp}, Gold +{s.goldBonus}, Material +{s.startMaterial}");
        }

        // ============================================================
        //  戦闘関連
        // ============================================================

        /// <summary>被ダメージ補正（負数を返す。0 未満にはならないよう呼び出し側で clamp）。</summary>
        public static int GetDamageReduction()
            => S != null ? S.damageReduce : 0;

        /// <summary>戦闘勝利時の追加ゴールド。</summary>
        public static int GetCombatGoldBonus()
            => S != null ? S.combatGoldBonus : 0;

        /// <summary>会心ダイスへの追加補正値（0/1/2/3）。</summary>
        public static int GetCritBonus()
            => S != null ? S.critLevel : 0;

        /// <summary>ダイス合計値補正（出目の合計に追加で加算）。</summary>
        public static int GetDiceTotalBonus()
            => S != null ? S.diceTotalBonus : 0;

        // ============================================================
        //  飢餓
        // ============================================================

        /// <summary>飢餓ダメージ削減量（最大3）。0 未満にはならないよう呼び出し側で clamp。</summary>
        public static int GetHungerDamageReduction()
            => S != null ? S.hungerReduce : 0;

        // ============================================================
        //  ショップ
        // ============================================================

        /// <summary>購入返金確率（0.0〜0.15）。</summary>
        public static float GetRefundChance()
        {
            var s = S;
            if (s == null) return 0f;
            switch (s.refundLevel)
            {
                case 1: return 0.05f;
                case 2: return 0.10f;
                case 3: return 0.15f;
                default: return 0f;
            }
        }

        /// <summary>支払金額に対し、返金抽選を行う。返金された分だけ run.coins に戻す。</summary>
        public static int RollRefund(int paidAmount, RunState run)
        {
            float chance = GetRefundChance();
            if (chance <= 0f || paidAmount <= 0 || run == null) return 0;
            if (Random.value >= chance) return 0;
            run.coins += paidAmount;
            Debug.Log($"[MetaBuff] 返金発動: +{paidAmount}G");
            return paidAmount;
        }

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
    }
}
