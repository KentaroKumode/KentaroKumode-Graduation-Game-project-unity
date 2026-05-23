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
            int gain = GameLoop.LastStand.FilterGoldGain(run, paidAmount);
            run.coins += gain;
            if (gain > 0) Debug.Log($"[MetaBuff] 返金発動: +{gain}G");
            return gain;
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

        // ============================================================
        //  新規バフ
        // ============================================================

        /// <summary>〈神の加護〉解放済みかどうか。1戦闘1回ロール敗北を引分に変える。</summary>
        public static bool IsDivineProtectUnlocked()
            => S != null && S.divineProtectUnlocked;

        /// <summary>〈開幕パッシブ〉解放済みかどうか。RunStart で1個獲得。</summary>
        public static bool IsStartingPassiveItemUnlocked()
            => S != null && S.startingPassiveItemUnlocked;

        /// <summary>フロアクリア時の追加回復量（0/1/2）。</summary>
        public static int GetFloorClearHeal()
            => S != null ? S.floorClearHeal : 0;

        /// <summary>〈宝箱の財宝〉解放済みかどうか。宝箱マスでゴールドも獲得する。</summary>
        public static bool IsTreasureChestGoldUnlocked()
            => S != null && S.treasureChestGoldUnlocked;

        /// <summary>会心倍率（恒久バフでの会心倍率変更は撤廃され、常に 2.0）。
        /// 装備パッシブ等が個別に上書きしうるが、メタ恒久値としては固定。</summary>
        public static float GetCriticalMultiplier() => 2.0f;
    }
}
