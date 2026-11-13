namespace GameLoop
{
    /// <summary>
    /// 深さ 6 (画面の第 4 層・灰燼の王の層) の〈灰に埋もれた基地〉(2026-11-13・docs/GAME.md §4-2 / §17)。
    /// いつ出すかの規則は <see cref="MapSystem.FreeMove.AshBase"/> (検算と同じ式)。 ここはどちらの版を出すかと、 このランで済んだかを持つ。
    ///
    /// <para><b>版は 2 本</b> (どちらも召喚専用・一度のみ・event_list.txt):
    /// <list type="bullet">
    /// <item>〈灰に埋もれた基地〉(<see cref="FirstName"/>) ── 灰燼の王をまだ一度も倒したことがない記録。 選択肢は 1 つ (時限デバフ〈徒労〉を受ける)。
    /// 手記を開く選択肢は無いので〈真理〉は得られず、 灰燼の王を倒すと第 4 層のクリアの結末で終わる (GameManager.HandleFloorClear)。</item>
    /// <item>〈灰に埋もれた基地・再訪〉(<see cref="RevisitName"/>) ── 倒したことがある記録。 「手記の最後の頁を開く」(効果「真理を得る」+ 時限バフ〈覚悟〉) か、
    /// 〈徒労〉を受けて頁を開かない (第 4 層のクリアで終わる) の 2 つ。</item>
    /// </list>
    /// 〈真理〉はランをまたいで残らない (毎回、 基地で得る)。</para>
    /// <para>「倒したことがある」はランの外の記録 <see cref="MetaProgression.MetaProgressState.ashKingBeaten"/> (灰燼の王を倒した瞬間に立てる・結末の種類によらない)。
    /// 測定の口 <see cref="GameHooks.AshKingBeatenEver"/> が値を持っていればそちらを使う (BOT は毎回空の記録で走るため・AutoRunner.ashKingBeatenEver)。</para>
    /// </summary>
    public static class AshBaseEvent
    {
        /// <summary>はじめての版のイベント名 (event_list.txt の定義名と一致させる)。</summary>
        public const string FirstName = "灰に埋もれた基地";
        /// <summary>灰燼の王を倒したことがある記録の版のイベント名。</summary>
        public const string RevisitName = "灰に埋もれた基地・再訪";

        /// <summary>灰燼の王 (深さ 6 のボス) を倒したことがあるか。 測定の口が値を持っていればそれ、 無ければランの外の記録。</summary>
        public static bool AshKingBeatenEver()
        {
            if (GameHooks.AshKingBeatenEver.HasValue) return GameHooks.AshKingBeatenEver.Value;
            var st = MetaProgression.MetaProgressManager.Instance?.State;
            return st != null && st.ashKingBeaten;
        }

        /// <summary>灰燼の王を倒した瞬間に呼ぶ (結末の種類によらない・GameManager.ConfirmBattleResult のボス撃破)。 ランの外の記録へ立てて保存する
        /// (並列ワーカーは保存しない ── MetaProgressManager.Save・エディタは起動のたびに空で始まる)。</summary>
        public static void NoteAshKingBeaten()
        {
            var mm = MetaProgression.MetaProgressManager.Instance;
            var st = mm?.State;
            if (st == null || st.ashKingBeaten) return;
            st.ashKingBeaten = true;
            mm.Save();
            UnityEngine.Debug.Log("[灰に埋もれた基地] 灰燼の王を倒した記録を立てた (次のランから基地は再訪の版)");
        }

        /// <summary>出す版の名前 (記録で選ぶ)。</summary>
        public static string NameFor() => AshKingBeatenEver() ? RevisitName : FirstName;

        /// <summary>このランで基地の選択を済ませたか (どちらの版でも)。 一度のみの記録 (<c>seenOnceEvents</c>・中断セーブに乗る) を読む。</summary>
        public static bool Seen(RunState run)
        {
            if (run?.seenOnceEvents == null) return false;
            foreach (var name in new[] { FirstName, RevisitName })
            {
                var def = EventSystem.EventDatabase.GetByName(name);
                if (def != null && run.seenOnceEvents.Contains(def.id)) return true;
            }
            return false;
        }

        /// <summary>このイベントが基地 (どちらかの版) か。</summary>
        public static bool IsBase(EventSystem.EventDefinition def)
            => def != null && (def.name == FirstName || def.name == RevisitName);
    }
}
