using UnityEngine;

namespace GameLoop
{
    /// <summary>
    /// ラストスタンド: ランを通して1度だけ発動する救済システム。
    ///
    /// 仕様:
    /// - ちいさな灯火 → ラストスタンド の順で消費される（灯火優先）
    /// - HP が 0 になった瞬間に発動
    /// - 発動後、最大HPが 1 に固定され、いかなる手段でも増加しない
    /// - 発動後はロール敗北のメインダメージ以外の全てのダメージを無効化
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

            // 2. ラストスタンド（一度きり HP1 化 + ダメージ無効化開始）
            if (!run.lastStandActive)
            {
                run.lastStandActive = true;
                run.playerMaxHP = 1;
                run.playerHP = 1;
                Debug.Log($"[ラストスタンド] 発動: 最大HP=1 に固定、以降の非ロール敗北ダメージを無効化");
                return true;
            }

            return false;
        }

        /// <summary>
        /// 「非ロール敗北ダメージ」を試みる前にこれを呼ぶ。ラストスタンド中なら無効化（0を返す）。
        /// それ以外は dmg をそのまま返す。
        /// </summary>
        public static int FilterIncidentalDamage(RunState run, int dmg)
        {
            if (dmg <= 0) return dmg;
            if (run != null && run.lastStandActive)
            {
                Debug.Log($"[ラストスタンド] 非ロール敗北ダメージ {dmg} を無効化");
                return 0;
            }
            return dmg;
        }

        /// <summary>
        /// 最大HP増加を試みる前にこれを呼ぶ。ラストスタンド中なら 0 を返して増加を拒否。
        /// </summary>
        public static int FilterMaxHPGain(RunState run, int gain)
        {
            if (gain <= 0) return gain;
            if (run != null && run.lastStandActive) return 0;
            return gain;
        }

        /// <summary>
        /// ゴールド入手量を試みる前にこれを呼ぶ。ラストスタンド中なら 0 を返して入手を拒否。
        /// 損失（負値）はそのまま通す。
        /// </summary>
        public static int FilterGoldGain(RunState run, int amount)
        {
            if (amount <= 0) return amount;
            if (run != null && run.lastStandActive)
            {
                Debug.Log($"[ラストスタンド] ゴールド入手 {amount}G を無効化（死神は富を許さない）");
                return 0;
            }
            return amount;
        }
    }
}
