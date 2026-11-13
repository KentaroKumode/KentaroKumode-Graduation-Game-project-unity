using UnityEngine;

namespace GameLoop
{
    /// <summary>
    /// 確信パッシブと独立した深層ゲートを管理するヘルパー。
    /// 災厄の予兆で stage=1、エリート戦勝利毎に+1されるが、段階値だけではゲートを開かない。
    /// 5層ボス撃破で〈決意〉(6F)、6層〈灰に埋もれた基地・再訪〉の「手記の最後の頁を開く」(効果「真理を得る」) で〈真理〉(7F)へ変化する。
    /// </summary>
    public static class ConvictionSystem
    {
        public const string IdConviction = "根拠のない確信";
        public const string IdResolve    = "決意";
        public const string IdTruth      = "真理";

        public const int StageResolve = 2;
        public const int StageTruth   = 4;

        /// <summary>深さ 6 (画面の第 4 層) に、 層の 3 分の 1 あたりの固定のイベントの点を置くか (<c>FreeMapLayout.fixedEvent</c>・乱数を引かない)。 層を作る時に読む。
        /// <para>置くのは、 層に入った時に〈真理〉を持っている時 (イベントなしの測定 <c>SuppressEvents</c> / <c>SuppressConvictionChain</c> は層に入る時に無償で配る) と
        /// イベントなしの測定の時だけ ── その測定の地図を 2026-11-13 までの作りと 1 点も変えないため。 ふだんの深さ 6 は生成どおりの種別
        /// (〈灰に埋もれた基地〉は最初に踏んだイベントマスで起きる・<see cref="AshBaseEvent"/>)。</para></summary>
        public static bool KeepsFixedEventPoint(RunState run)
            => GameManager.SuppressEvents || HasTruth(run);

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

        /// <summary>6層の専用イベント (〈灰に埋もれた基地・再訪〉の効果「真理を得る」) で〈真理〉を得て7層資格を立てる。
        /// イベントなしの測定では深さ 6 に入る時に呼ぶ (GameManager.EnterFloor)。 〈真理〉はランをまたいで残らない。</summary>
        public static bool RevealTruthInLayer6(RunState run)
        {
            if (run == null || !HasResolveOrBetter(run)) return false;
            run.layer7Unlocked = true;
            run.convictionStage = Mathf.Max(run.convictionStage, StageTruth);
            ReplaceConvictionItem(run, IdTruth);
            return true;
        }

        /// <summary>エリート戦勝利時に呼ぶ。災厄の予兆を受けたラン (段が 1 以上) なら段階を上げ (GAME.md §4-2)、
        /// 確信の品を持っていれば名前を資格に合わせる。
        /// <para><b>品の有無では判定しない</b> (2026-11-13)。 資格の正本はフラグと段で、 品は手放せない
        /// (<see cref="ItemIds.Undroppable"/>) が、 古い中断セーブには品を売ってしまったランが残り得る。</para></summary>
        public static void OnEliteDefeated(RunState run)
        {
            if (run == null || run.convictionStage < 1) return;

            run.convictionStage++;
            int idx = run.ownedPassiveItems != null ? FindAnyConvictionIndex(run) : -1;
            if (idx < 0)
            {
                Debug.Log($"[Conviction] エリート撃破 → stage{run.convictionStage} (確信の品なし)");
                return;
            }
            // エリート撃破は確信の強さだけを積む。層ゲートの名称変化は
            // 5層ボス／6層専用イベント (灰に埋もれた基地) という物語上の試練でのみ起こす。
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

        /// <summary>確信の品を <paramref name="newId"/> へ書き換える。 書き換える元の品が無く、 資格 (段が 1 以上・<c>layer6Unlocked</c>・
        /// <c>layer7Unlocked</c>) がある時は <paramref name="newId"/> を<b>足す</b> (2026-11-13)。
        /// <para>品は手放せない (<see cref="ItemIds.Undroppable"/>) ので、 足すのは古い中断セーブで既に確信を売ってしまったランが戻って来た時だけ。
        /// 足すのは <c>PassiveAddHelper.AddPassiveItem</c> (重複の判定・取得の記録) で、 取得時の副作用 (<see cref="OnFirstAcquired"/>・
        /// 〈決意〉の獲得ボーナス) はイベントの獲得の側 (EventEffectExecutor.ApplyPassiveAcquisitionBonus) にあるのでここでは走らない。
        /// このランで一度持って手放した品 (<c>seenPassiveItemIds</c>) は足せない (ログだけ)。</para></summary>
        private static void ReplaceConvictionItem(RunState run, string newId)
        {
            if (run?.ownedPassiveItems == null) return;
            int idx = FindAnyConvictionIndex(run);
            if (idx >= 0) { run.ownedPassiveItems[idx] = newId; return; }
            if (run.convictionStage < 1 && !run.layer6Unlocked && !run.layer7Unlocked) return;
            bool added = ItemSystem.Helpers.PassiveAddHelper.AddPassiveItem(run, newId);
            Debug.Log(added ? $"[Conviction] 確信の品が無いので「{newId}」を足した (資格は段・フラグが持つ)"
                            : $"[Conviction] 確信の品が無く「{newId}」も足せなかった (このランで手放し済み)");
        }

        /// <summary>イベント由来で〈根拠のない確信〉を初取得した瞬間に呼ぶ。stage を 1 に正規化。</summary>
        public static void OnFirstAcquired(RunState run)
        {
            if (run == null) return;
            if (run.convictionStage < 1) run.convictionStage = 1;
        }
    }
}
