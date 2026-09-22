using UnityEngine;

namespace GameLoop
{
    /// <summary>
    /// 確信パッシブと独立した深層ゲートを管理するヘルパー。
    /// 災厄の予兆で stage=1、エリート戦勝利毎に+1されるが、段階値だけではゲートを開かない。
    /// 5層ボス撃破で〈決意〉(6F)、6層「裂け目の記録」の選択で〈真理〉(7F)へ変化する。
    /// </summary>
    public static class ConvictionSystem
    {
        public const string IdConviction = "根拠のない確信";
        public const string IdResolve    = "決意";
        public const string IdTruth      = "真理";

        public const int StageResolve = 2;
        public const int StageTruth   = 4;
        public const string Layer7RevelationEventName = "裂け目の記録";

        /// <summary>〈決意〉以上を所持しているか (6F進入可能か)。</summary>
        public static bool HasResolveOrBetter(RunState run)
            => run != null && (run.layer6Unlocked || Owns(run, IdResolve) || Owns(run, IdTruth));

        /// <summary>〈真理〉を所持しているか (7F進入可能か)。</summary>
        public static bool HasTruth(RunState run)
            => run != null && (run.layer7Unlocked || Owns(run, IdTruth));

        /// <summary>5層ボス撃破を、確信が〈決意〉へ変わる試練として確定する。
        /// 乱数を消費せず、災厄の予兆を受けたランだけに6層資格を与える。</summary>
        public static bool PromoteForLayer6(RunState run)
        {
            if (run == null || (run.convictionStage <= 0 && FindAnyConvictionIndex(run) < 0)) return false;
            run.layer6Unlocked = true;
            run.convictionStage = Mathf.Max(run.convictionStage, StageResolve);
            ReplaceConvictionItem(run, IdResolve);
            return true;
        }

        /// <summary>6層の専用イベントで〈真理〉を得て7層資格を立てる。</summary>
        public static bool RevealTruthInLayer6(RunState run)
        {
            if (run == null || !HasResolveOrBetter(run)) return false;
            run.layer7Unlocked = true;
            run.convictionStage = Mathf.Max(run.convictionStage, StageTruth);
            ReplaceConvictionItem(run, IdTruth);
            return true;
        }

        /// <summary>エリート戦勝利時に呼ぶ。確信パッシブを所持していれば段階を上げ、
        /// アイテム名のマイルストーン変化 (3で決意 / 6で真理) を反映する。</summary>
        public static void OnEliteDefeated(RunState run)
        {
            if (run?.ownedPassiveItems == null) return;
            // 何らかの確信系を所持しているかチェック
            int idx = FindAnyConvictionIndex(run);
            if (idx < 0) return;

            run.convictionStage++;
            // エリート撃破は確信の強さだけを積む。層ゲートの名称変化は
            // 5層ボス／6層専用イベントという物語上の試練でのみ起こす。
            string newId = run.layer7Unlocked ? IdTruth
                         : run.layer6Unlocked ? IdResolve
                         : IdConviction;
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

        private static bool Owns(RunState run, string id)
            => run?.ownedPassiveItems != null && run.ownedPassiveItems.Contains(id);

        private static void ReplaceConvictionItem(RunState run, string newId)
        {
            int idx = FindAnyConvictionIndex(run);
            if (idx >= 0) run.ownedPassiveItems[idx] = newId;
        }

        /// <summary>イベント由来で〈根拠のない確信〉を初取得した瞬間に呼ぶ。stage を 1 に正規化。</summary>
        public static void OnFirstAcquired(RunState run)
        {
            if (run == null) return;
            if (run.convictionStage < 1) run.convictionStage = 1;
        }
    }
}
