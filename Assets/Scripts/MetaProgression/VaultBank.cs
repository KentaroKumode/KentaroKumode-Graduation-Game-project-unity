using UnityEngine;

namespace MetaProgression
{
    /// <summary><b>金庫 r10 (極点): 層を跨ぐたびに所持金が 1.5 倍になる。</b> 2026-09-13。
    ///
    /// <para><b>倍率 2.0 → 1.5 (2026-09-13)。</b> 2.0 では r10−r9 +6.75pt /
    /// Balanced 比 +5.12 で目標帯を大きく超えていた。</para>
    ///
    /// <para><b>何が選択になるのか。</b> 手持ちを使えば今の棚が買える。 残せば次の層で増える。
    /// 「いま買うか、 寝かせるか」がフロアごとに問われる ── 金庫トラックの段効果
    /// (<see cref="MetaPanel.VaultGold"/> 開幕ゴールド +5/段) がそのまま種銭として効く。</para>
    ///
    /// <para><b>ラン内で完結する。</b> 先に検討した「ランを跨ぐ預金」は
    /// ラン N の所持金がラン N−1 に依存するため、 <b>「ラン i は runIdx だけの関数」</b>という
    /// 並列測定の前提を壊していた。 こちらは壊さない ── digest 照合もチャンク分割も
    /// 従来どおり有効。</para>
    ///
    /// <para><b>指数であることに注意。</b> 7 層なら最大 1.5⁶ ≒ 11 倍。 使わずに寝かせる方策を
    /// BOT が覚えると経済が破綻しうるので、 <see cref="Cap"/> で頭を止める。
    /// 実測で上限に当たらないなら上限は効いていない ── 計装で確認すること
    /// (2.0 倍時代の実測では上限到達 0.8% で、 頭は止まっていなかった)。</para></summary>
    public static class VaultBank
    {
        /// <summary>増加の対象になる所持金の上限。 これを超える分は増えない。
        /// アイテムは 4〜20G (中央値 9G) なので、 200G ≒ 棚を丸ごと買える額。</summary>
        public const int Cap = 200;

        /// <summary>層を跨いだときの倍率。</summary>
        public const float Multiplier = 1.5f;

        // [計装] 使われていないのか効いていないのかを切り分けるため。
        public static long Doublings, GoldGainedTotal, CapHits;
        public static void ResetStats() { Doublings = GoldGainedTotal = CapHits = 0; }

        public static bool IsUnlocked() => MetaBuffApplicator.IsVaultBankUnlocked();

        /// <summary>層を突破した瞬間に呼ぶ。 所持金を <see cref="Multiplier"/> 倍にする。</summary>
        public static void OnFloorCleared(GameLoop.RunState run)
        {
            if (run == null || !IsUnlocked()) return;
            if (run.coins <= 0) return;

            int before = run.coins;
            // 上限までの分だけ増える。 超過分はそのまま持ち越す (没収はしない)。
            int grown = Mathf.FloorToInt(Mathf.Min(before, Cap) * Multiplier)
                      + Mathf.Max(0, before - Cap);
            if (before > Cap) CapHits++;
            if (grown <= before) return;
            run.coins = grown;
            Doublings++;
            GoldGainedTotal += grown - before;
            Debug.Log($"[金庫r10] 層を跨いで所持金が {Multiplier:0.##} 倍に: {before}G → {grown}G"
                    + (before > Cap ? $" (上限 {Cap}G 超過分は据え置き)" : ""));
        }
    }
}
