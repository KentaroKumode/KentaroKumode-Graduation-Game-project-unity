using UnityEngine;

namespace GameLoop
{
    /// <summary>
    /// 確信パッシブの段階進化を管理するヘルパー。
    /// 災厄の予兆 イベントで〈根拠のない確信〉を取得すると stage=1 で始まり、
    /// エリート戦勝利毎に +1。 stage に応じて所持アイテム名が以下に変化する:
    ///   stage 1  : 〈根拠のない確信〉
    ///   stage 2-3: 〈決意〉           (6F進入フラグ)
    ///   stage 4+ : 〈真理〉           (6F + 7F 進入フラグ)
    /// </summary>
    public static class ConvictionSystem
    {
        public const string IdConviction = "根拠のない確信";
        public const string IdResolve    = "決意";
        public const string IdTruth      = "真理";

        public const int StageResolve = 2;
        public const int StageTruth   = 4;

        /// <summary>〈決意〉以上を所持しているか (6F進入可能か)。</summary>
        public static bool HasResolveOrBetter(RunState run)
            => run != null && run.convictionStage >= StageResolve;

        /// <summary>〈真理〉を所持しているか (7F進入可能か)。</summary>
        public static bool HasTruth(RunState run)
            => run != null && run.convictionStage >= StageTruth;

        /// <summary>エリート戦勝利時に呼ぶ。確信パッシブを所持していれば段階を上げ、
        /// アイテム名のマイルストーン変化 (3で決意 / 6で真理) を反映する。</summary>
        public static void OnEliteDefeated(RunState run)
        {
            if (run?.ownedPassiveItems == null) return;
            // 何らかの確信系を所持しているかチェック
            int idx = FindAnyConvictionIndex(run);
            if (idx < 0) return;

            run.convictionStage++;
            string newId = StageToItemId(run.convictionStage);
            string oldId = run.ownedPassiveItems[idx];
            if (oldId != newId)
            {
                run.ownedPassiveItems[idx] = newId;
                Debug.Log($"[Conviction] エリート撃破 → stage{run.convictionStage}: 「{oldId}」が「{newId}」に変化");
            }
            else
            {
                Debug.Log($"[Conviction] エリート撃破 → stage{run.convictionStage} ({newId})");
            }
        }

        /// <summary>段階番号から表示すべきアイテムIDを返す。</summary>
        public static string StageToItemId(int stage)
        {
            if (stage >= StageTruth)   return IdTruth;
            if (stage >= StageResolve) return IdResolve;
            return IdConviction;
        }

        /// <summary>所持リストから 根拠のない確信/決意/真理 のいずれかの index を返す。なければ -1。</summary>
        private static int FindAnyConvictionIndex(RunState run)
        {
            for (int i = 0; i < run.ownedPassiveItems.Count; i++)
            {
                var id = run.ownedPassiveItems[i];
                if (id == IdConviction || id == IdResolve || id == IdTruth) return i;
            }
            return -1;
        }

        /// <summary>イベント由来で〈根拠のない確信〉を初取得した瞬間に呼ぶ。stage を 1 に正規化。</summary>
        public static void OnFirstAcquired(RunState run)
        {
            if (run == null) return;
            if (run.convictionStage < 1) run.convictionStage = 1;
        }
    }
}
