using UnityEngine;

namespace GameLoop
{
    /// <summary>
    /// ラストスタンド: ランを通して1度だけ発動する救済システム。
    ///
    /// 仕様（リワーク版）:
    /// - ちいさな灯火 → ラストスタンド の順で消費される（灯火優先）
    /// - HP が 0 になった瞬間に発動
    /// - 発動ラウンドに限り「無敵」: 致命の一撃を打ち消して生き延びる
    /// - 代償として最大HPが半減（恒久・残りラン全体）。生還後は半減バーが満タン
    /// - 発動後のガード処理は一切なし（DOT無効化/巻き戻し/GOLD入手不可 等は廃止）。
    ///   以降は通常どおりダメージを受け、再度HP0ならゲームオーバー
    /// </summary>
    public static class LastStand
    {
        /// <summary>ラストスタンドが既に発動済みか確認する（保存対象）。</summary>
        public static bool IsActive(RunState run)
            => run != null && run.lastStandActive;

        /// <summary>ラストスタンドが未発動か確認する。</summary>
        public static bool IsAvailable(RunState run)
            => run != null && !run.lastStandActive;

        /// <summary>
        /// HP が 0 になった直前に呼ぶ。ちいさな灯火 → ラストスタンド の順に試す。
        /// 救済に成功した場合 true。失敗ならゲームオーバー処理に進む。
        /// </summary>
        public static bool TryConsumeRevival(RunState run)
        {
            if (run == null) return false;

            // 1. ちいさな灯火（一度きり全回復）
            if (InventorySystem.PassiveItems.TorchRevival.TryConsume(run))
                return true;

            // 2. ラストスタンド（一度きり・発動ラウンド無敵 + 最大HP半減）
            //    メタデバフ Lv10 天変地異 が有効な場合は発動しない。
            if (!run.lastStandActive
                && !MetaProgression.MetaDebuffApplicator.IsLastStandDisabled())
            {
                run.lastStandActive = true;
                int before = run.playerMaxHP;
                run.playerMaxHP = Mathf.Max(1, run.playerMaxHP / 2);
                run.playerHP = run.playerMaxHP; // 致命を打ち消し、半減バー満タンで生還
                Debug.Log($"[ラストスタンド] 発動: 致命を無敵で耐え、最大HP {before}→{run.playerMaxHP} に半減（以降ガード無し）");
                return true;
            }

            return false;
        }

        /// <summary>
        /// 旧ガード処理の互換用パススルー（ラストスタンドは経済を制限しない）。
        /// 既存呼び出し箇所を壊さないため残置。常に amount をそのまま返す。
        /// </summary>
        public static int FilterGoldGain(RunState run, int amount) => amount;

        /// <summary>
        /// 旧ガード処理の互換用パススルー（ラストスタンドは最大HP増加を妨げない）。
        /// 既存呼び出し箇所を壊さないため残置。常に gain をそのまま返す。
        /// </summary>
        public static int FilterMaxHPGain(RunState run, int gain) => gain;
    }
}
