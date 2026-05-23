using UnityEngine;
using GameLoop;

namespace InventorySystem.PassiveItems
{
    /// <summary>
    /// 名前付きパッシブ「ちいさな灯火」の救済ヘルパー。
    /// プレイヤーHPが0になる直前にこのヘルパーを呼ぶと、
    /// 灯火を所持していれば全回復+消費し、true を返す。
    /// </summary>
    public static class TorchRevival
    {
        public const string TorchId = "ちいさな灯火";

        /// <summary>所持しているなら消費して全回復する。発動した場合 true。</summary>
        public static bool TryConsume(RunState run)
        {
            if (run == null || run.ownedPassiveItems == null) return false;
            if (!run.ownedPassiveItems.Contains(TorchId)) return false;

            int idx = run.ownedPassiveItems.IndexOf(TorchId);
            InventorySystem.Helpers.PassiveAddHelper.RemoveAt(run, idx);
            run.playerHP = run.playerMaxHP;
            Debug.Log($"[ちいさな灯火] 致命傷を受ける直前に発動: HP全回復 ({run.playerMaxHP}) + アイテム消滅");
            return true;
        }
    }
}
