using System.Collections.Generic;
using System.Linq;
using GameLoop;
using MetaProgression;

namespace Codex
{
    /// <summary>
    /// 読み物（WorldVignettes）の解禁判定 + メタ永続状態への読み書き。
    /// MonoBehaviour 非依存。UI は別途実装。
    ///
    /// 永続化は MetaProgressState に追加した並列リストフィールドを介して
    /// MetaProgressManager.Save() / Load() に乗せる。
    /// （JsonUtility は Dictionary 非対応のため並列リストを採用）
    /// </summary>
    public static class VignetteUnlockState
    {
        private static MetaProgressState Meta => MetaProgressManager.Instance?.State;

        // ----------------------------------------------------------------
        //  RunClear フック
        // ----------------------------------------------------------------

        /// <summary>
        /// ラン完了時に呼ぶ。エンド別クリア回数・最高難度フラグを更新し、
        /// MetaProgressManager.Save() を呼んで永続化する。
        /// </summary>
        /// <param name="run">クリアしたランの RunState。</param>
        /// <param name="endingId">"end1"〜"end5"。</param>
        /// <param name="isMaxDifficulty">最高難度クリアか否か。</param>
        public static void OnRunClear(RunState run, string endingId, bool isMaxDifficulty)
        {
            var meta = Meta;
            if (meta == null || string.IsNullOrEmpty(endingId)) return;

            // クリア回数インクリメント
            int idx = meta.endingClearKeys.IndexOf(endingId);
            if (idx < 0)
            {
                meta.endingClearKeys.Add(endingId);
                meta.endingClearValues.Add(1);
            }
            else
            {
                meta.endingClearValues[idx]++;
            }

            // 最高難度フラグ
            if (isMaxDifficulty && !meta.endingMaxDiffCleared.Contains(endingId))
                meta.endingMaxDiffCleared.Add(endingId);

            MetaProgressManager.Instance.Save();
        }

        // ----------------------------------------------------------------
        //  解禁判定
        // ----------------------------------------------------------------

        /// <summary>指定ビネットが現在の状態で解禁されているか。</summary>
        public static bool IsUnlocked(WorldVignettes.Vignette v)
        {
            if (v?.condition == null) return false;
            var meta = Meta;
            if (meta == null) return false;

            var cond = v.condition;

            switch (cond.tier)
            {
                case WorldVignettes.UnlockTier.AlwaysUnlocked:
                    return true;

                case WorldVignettes.UnlockTier.FirstClear:
                    return GetClearCount(meta, cond.endingId) >= 1;

                case WorldVignettes.UnlockTier.MaxDifficultyClear:
                    return IsMaxDiffCleared(meta, cond.endingId);

                case WorldVignettes.UnlockTier.ThreeClears:
                    return GetClearCount(meta, cond.endingId) >= 3;

                case WorldVignettes.UnlockTier.AllEndingsMaxDifficulty:
                    return AllEndingsMaxDifficultyCleared(meta);

                default:
                    return false;
            }
        }

        /// <summary>現在解禁済みのビネット一覧。</summary>
        public static IEnumerable<WorldVignettes.Vignette> UnlockedVignettes()
            => WorldVignettes.All.Where(IsUnlocked);

        // ----------------------------------------------------------------
        //  最高難度判定ヘルパー
        // ----------------------------------------------------------------

        /// <summary>
        /// ランが最高難度かどうかを判定する。「メタデバフフル」 = MetaDebuffLevel Lv1〜Lv10 が全てアクティブ。
        /// MetaProgressManager.State.activeDebuffs を参照する（プレイ開始時に確定するため Run でも参照可能）。
        /// </summary>
        public static bool IsMaxDifficultyRun(RunState run)
        {
            var meta = MetaProgression.MetaProgressManager.Instance?.State;
            if (meta?.activeDebuffs == null) return false;
            for (int lv = 1; lv <= 10; lv++)
            {
                if (!meta.activeDebuffs.Contains(lv)) return false;
            }
            return true;
        }

        // ----------------------------------------------------------------
        //  内部ユーティリティ
        // ----------------------------------------------------------------

        private static int GetClearCount(MetaProgressState meta, string endingId)
        {
            if (meta.endingClearKeys == null || endingId == null) return 0;
            int idx = meta.endingClearKeys.IndexOf(endingId);
            if (idx < 0) return 0;
            return (meta.endingClearValues != null && idx < meta.endingClearValues.Count)
                ? meta.endingClearValues[idx] : 0;
        }

        private static bool IsMaxDiffCleared(MetaProgressState meta, string endingId)
            => meta.endingMaxDiffCleared != null && endingId != null
               && meta.endingMaxDiffCleared.Contains(endingId);

        private static bool AllEndingsMaxDifficultyCleared(MetaProgressState meta)
        {
            // 新フレーム: capstone「最後の一筆」廃止に伴い、 End5 (無上正等覚撃破) を事実上の最高難度
            // オールクリアとして扱う。 End5 自体が Lv10 + 円環選択を要求するため、 これ単独で
            // 全制覇相当のゲートとなる。
            // ※ 2026-06 時点で AllEndingsMaxDifficulty 階層を使う vignette は無い (E5 codex は別途検討中)。
            //   将来 E5 専用 codex を追加した際にこのゲートを利用する予定。
            if (meta.endingMaxDiffCleared == null) return false;
            return meta.endingMaxDiffCleared.Contains("end5");
        }
    }
}
