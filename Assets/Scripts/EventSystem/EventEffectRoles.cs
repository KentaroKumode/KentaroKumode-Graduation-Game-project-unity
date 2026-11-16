using System.Collections.Generic;

namespace EventSystem
{
    /// <summary>効果の向き: プレイヤーの得になるか、 代償か、 どちらでもないか (2026-11-13)。</summary>
    public enum EventEffectRole
    {
        /// <summary>どちらでもない (なし・ランダムなイベント・廃止の効果)。</summary>
        Neutral,
        /// <summary>得になる効果 (受け取る・明かす・撒く・遠ざける・近道 …)。</summary>
        Benefit,
        /// <summary>代償 (払う・失う・戦闘・ターン・追跡者を呼ぶ・前線を近づける …)。</summary>
        Cost,
    }

    /// <summary>選ぶ時の状況のうち、 効果が起きるかを決めるもの (2026-11-13)。 本体の今の状態から <c>EventChoice</c> が作る。
    /// 純粋なデータなので、 検算 (Tools/event_branch_check) も同じ規則を Unity なしで回せる。</summary>
    public struct EventEffectSituation
    {
        /// <summary>航行の状態を読めたか (本体の今のランを判定している時だけ true)。 false なら航行の効果は「起きる」として扱う。</summary>
        public bool navKnown;
        /// <summary>航行マップがある層か。</summary>
        public bool map;
        /// <summary>追跡者が生きているか。</summary>
        public bool foe;
        /// <summary>天候 (前線) がある層か。</summary>
        public bool weather;
        /// <summary>前線がもう層に届いているか。</summary>
        public bool frontArrived;
        /// <summary>天候が「嵐」の作りか (2026-11-13・測定の口 GameHooks.StormEnabled)。 true なら「前線」の効果は嵐の時計を動かし、 文も「嵐」で言う。</summary>
        public bool stormRules;
        /// <summary>この層に嵐があるか (嵐の作りの時だけ読む・深さ 1/3/5/6)。</summary>
        public bool storm;
        /// <summary>差し出せる秘宝があるか (取り替えが成り立つか)。</summary>
        public bool canTrade;
        /// <summary>武器をまだ鍛えられるか (終点でない)。 素材が足りるかは支払いの側で見る。</summary>
        public bool canForge;
    }

    /// <summary>効果ごとの「得か代償か」と「今の状況で起きるか」を 1 か所で決める (2026-11-13・作者の決定)。
    ///
    /// <para><b>選べない選択肢の規則</b>: 暗くするのは (a) 払えない時 (ゴールド・物資・素材) と、 (b) その選択肢の
    /// <b>得になる効果が、 何ひとつ起きない時</b>だけ。 起きない効果が代償 (追跡者を呼ぶ・前線を近づける) だけなら選べる。
    /// 得になる効果の一部だけが起きない時も選べる (起きなかった効果は結果の行に出す)。 HP が満ちている時の HP+N は「起きる」とみなす。
    /// (a) は <c>EventChoice.UnavailableReason</c>、 (b) は <see cref="NothingGainedReason"/>。</para>
    ///
    /// <para>向きは BOT の値付け (<c>AutoTest.EventChoiceScorer</c>) の符号と食い違わないように置く: 得 = 正の値・代償 = 負の値。
    /// 綴りで判定しない ── 効果の種類 (<see cref="EventEffectType"/>) と量の符号だけで決める。</para></summary>
    public static class EventEffectRoles
    {
        /// <summary>効果の向き。 確率分岐は、 どれかの分岐に得があれば得・無ければ代償があれば代償。</summary>
        public static EventEffectRole RoleOf(EventEffect e)
        {
            if (e == null) return EventEffectRole.Neutral;
            switch (e.type)
            {
                case EventEffectType.HpDelta:
                case EventEffectType.MaxHpDelta:
                case EventEffectType.GoldDelta:
                case EventEffectType.HungerDelta:
                case EventEffectType.ProvisionDelta:
                case EventEffectType.MaterialDelta:
                    return e.amount > 0 ? EventEffectRole.Benefit : e.amount < 0 ? EventEffectRole.Cost : EventEffectRole.Neutral;

                case EventEffectType.ShiftFront:
                    // 遠ざける (負) が得・近づける (正) が代償
                    return e.amount < 0 ? EventEffectRole.Benefit : e.amount > 0 ? EventEffectRole.Cost : EventEffectRole.Neutral;

                case EventEffectType.HpFullHeal:
                case EventEffectType.TimedBuff:
                case EventEffectType.GainPassiveItem:
                case EventEffectType.GainConsumableItem:
                case EventEffectType.GainSpecificItem:
                case EventEffectType.GainFlag:
                case EventEffectType.ObservatoryTakeCopy:
                case EventEffectType.VescaClockTake:   // ヴェスカのダウンの機会が増える ── 遊ぶ人の得
                case EventEffectType.GainTruth:        // 深さ 7 への資格
                case EventEffectType.RevealMap:
                case EventEffectType.RevealEncounters:
                case EventEffectType.RevealSupplies:
                case EventEffectType.ShakeOffPursuer:
                case EventEffectType.PushPursuer:
                case EventEffectType.Shortcut:
                case EventEffectType.OfferPassives:
                case EventEffectType.TradePassive:
                case EventEffectType.ForgeWeapon:
                case EventEffectType.NextShopDiscount:
                case EventEffectType.NextRestBonus:
                    return EventEffectRole.Benefit;

                case EventEffectType.HpSetTo:          // 「HPがNになる」は下げる代償として書かれている (BOT も致死域として値付けする)
                case EventEffectType.HpHalve:
                case EventEffectType.ArmorDurabilityLoss:
                case EventEffectType.TimedDebuff:
                case EventEffectType.PermanentDebuff:
                case EventEffectType.DiscardFlag:
                case EventEffectType.DiscardPassiveItem:
                case EventEffectType.EnterCombat:
                case EventEffectType.EnterEliteCombat:
                case EventEffectType.TurnsPass:
                case EventEffectType.PullPursuer:
                    return EventEffectRole.Cost;

                case EventEffectType.Probability:
                {
                    bool cost = false;
                    if (e.branches != null)
                        foreach (var br in e.branches)
                            if (br != null)
                                foreach (var c in br)
                                {
                                    var r = RoleOf(c);
                                    if (r == EventEffectRole.Benefit) return EventEffectRole.Benefit;
                                    if (r == EventEffectRole.Cost) cost = true;
                                }
                    return cost ? EventEffectRole.Cost : EventEffectRole.Neutral;
                }

                default:   // None / RandomEvent / CircusHandover
                    return EventEffectRole.Neutral;
            }
        }

        // ── 代償を見せるか (2026-11-16・作者の決定) ──────────────────────────────
        //   線引き: 「自分から差し出す物は見せる・ハプニング (行動の結果として失う物) は見せない」。 得る物は見せない (見出しの文が語る)。
        //   既定は効果の種類で決める (ShownByDefault)。 例外はイベントの定義ファイルの印で、 効果ごとに書く:
        //     〔見せる〕物資-3  /  〔伏せる〕武器強化素材-2     (EventEffect.costMark・EventParser が外して読む)
        //   BOT は印を読まない (値付けは全部の効果を読む・完全情報のまま)。 選べない理由は印に関係なく必ず出す。

        /// <summary>効果の頭に付けて「この代償を選択肢の下に見せる」印 (既定が見せない効果に付ける)。</summary>
        public const string ShowMark = "〔見せる〕";
        /// <summary>効果の頭に付けて「この代償を見せない」印 (既定が見せる効果に付ける)。</summary>
        public const string HideMark = "〔伏せる〕";

        /// <summary><b>伏せた支払いは、 足りなくても選べて「ある分だけ失う」か</b> (2026-11-16・司令塔の前提・作者の明示の承認は未)。
        /// true = 見せない支払い (伏せた物資-N・素材-N) は選べるかの判定に入れない (確率分岐の中の物資と同じ扱い) /
        /// false = 旧い規則 (確率分岐の外の支払いは、 見せても見せなくても足りなければ選べない)。 <b>切り替えはこの 1 か所</b>
        /// (EventChoice.UnavailableReason と、 それを通す EventEncounter.IsChoiceSelectable・BOT の EventChoiceScorer が読む)。</summary>
        public const bool HiddenPaymentMayFallShort = true;

        /// <summary>種類で決まる既定: 選択肢の下に見せる代償か (確率分岐の外・「勝利後」でない効果について)。
        /// 見せる = ターン+N・ゴールド-N・武器強化素材-N・秘宝の廃棄・秘宝を差し出して替える。
        /// 見せない = 物資-N・HP-N・最大HP-N・HP の半分・HP が N になる・戦闘・時限 / 永続デバフ・追跡者を呼ぶ・前線を近づける・フラグの廃棄 (進行用)・得る物すべて。</summary>
        public static bool ShownByDefault(EventEffect e)
        {
            if (e == null) return false;
            switch (e.type)
            {
                case EventEffectType.TurnsPass:          return e.amount > 0;
                case EventEffectType.GoldDelta:          return e.amount < 0;
                case EventEffectType.MaterialDelta:      return e.amount < 0;
                case EventEffectType.DiscardPassiveItem: return true;
                case EventEffectType.TradePassive:       return true;
                default:                                 return false;
            }
        }

        /// <summary>この効果 (選択肢の効果の並びの 1 つ) を選択肢の下に見せるか。 確率分岐 (と中の物すべて)・「勝利後」は見せない (印があっても)。
        /// それ以外は印 (<see cref="EventEffect.costMark"/>) があれば印のとおり、 無ければ <see cref="ShownByDefault"/>。</summary>
        public static bool IsShownCost(EventEffect e)
        {
            if (e == null || e.postCombat || e.type == EventEffectType.Probability) return false;
            return e.costMark != 0 ? e.costMark > 0 : ShownByDefault(e);
        }

        /// <summary>支払い (ゴールド・物資・素材の -N) が足りない時に、 選択肢を選べなくするか。 見せる支払いは選べなくする (理由を出す)。
        /// 見せない支払いは <see cref="HiddenPaymentMayFallShort"/> が true なら選べて、 ある分だけ失う。</summary>
        public static bool BlocksWhenShort(EventEffect e) => !HiddenPaymentMayFallShort || IsShownCost(e);

        /// <summary>見せる代償の短い文 (選択肢の下の説明に並べる)。 ターンは補足中の見込みを呼び手が後ろに付ける。 文を持たない効果は null。</summary>
        public static string ShownCostText(EventEffect e)
        {
            if (e == null) return null;
            int n = System.Math.Abs(e.amount);
            switch (e.type)
            {
                case EventEffectType.TurnsPass:          return n + " ターンを使う";
                case EventEffectType.GoldDelta:          return e.amount < 0 ? "ゴールド " + n + " を払う" : null;
                case EventEffectType.MaterialDelta:      return e.amount < 0 ? "素材 " + n + " を渡す" : null;
                case EventEffectType.ProvisionDelta:
                case EventEffectType.HungerDelta:        return e.amount < 0 ? "物資 " + n + " を渡す" : null;
                case EventEffectType.DiscardPassiveItem: return string.IsNullOrEmpty(e.param) ? "秘宝を 1 つ手放す" : "〈" + e.param + "〉を手放す";
                case EventEffectType.TradePassive:       return "秘宝を 1 つ差し出す";
                case EventEffectType.HpDelta:            return e.amount < 0 ? "HP " + n + " を失う" : null;
                case EventEffectType.MaxHpDelta:         return e.amount < 0 ? "最大 HP " + n + " を失う" : null;
                case EventEffectType.HpHalve:            return "HP の半分を払う";
                case EventEffectType.HpSetTo:            return "HP が " + e.amount + " になる";
                case EventEffectType.EnterCombat:        return "戦闘になる";
                case EventEffectType.EnterEliteCombat:   return "強敵との戦闘になる";
                case EventEffectType.TimedDebuff:
                case EventEffectType.PermanentDebuff:    return "〈" + e.param + "〉を受ける";
                case EventEffectType.PullPursuer:        return "追跡者を呼ぶ";
                case EventEffectType.ShiftFront:         return e.amount > 0 ? "前線が近づく" : null;
                default:                                 return null;
            }
        }

        /// <summary>この効果が今の状況で起きない理由 (画面に出す短い文)。 起きるなら null。
        /// 得になる効果にも代償にも使う ── 選べるかの判定 (<see cref="NothingGainedReason"/>) と、 選んだ後の結果の行 (<c>EventEffectExecutor</c>) が同じ文を出す。</summary>
        public static string NotHappeningReason(EventEffect e, in EventEffectSituation s)
        {
            if (e == null) return null;
            switch (e.type)
            {
                case EventEffectType.TradePassive: return s.canTrade ? null : "差し出せる秘宝が無い";
                case EventEffectType.ForgeWeapon:  return s.canForge ? null : "武器はもう鍛えられない";
            }
            if (!s.navKnown) return null;
            switch (e.type)
            {
                case EventEffectType.RevealMap:
                case EventEffectType.RevealEncounters:
                case EventEffectType.RevealSupplies:
                case EventEffectType.Shortcut:
                    return s.map ? null : "この層に地図は無い";
                case EventEffectType.ShakeOffPursuer:
                case EventEffectType.PushPursuer:
                case EventEffectType.PullPursuer:
                    return s.map && s.foe ? null : "この層に追跡者は居ない";
                case EventEffectType.ShiftFront:
                    // 嵐の作り (2026-11-13): 嵐のある層なら、 静かな時間でも嵐の時計を動かせる (遠ざけると嵐が来るのが遅れる) ── 「まだ来ていない」で選べなくしない
                    if (s.stormRules) return s.map && s.storm ? null : "この層に嵐は無い";
                    if (!s.map || !s.weather) return "この層に前線は無い";
                    // 遠ざける (amount < 0) は、 前線がまだ来ていなければ何も起きない (FreeMapSim.ShiftFront)
                    if (e.amount < 0 && !s.frontArrived) return "前線はまだ来ていない";
                    return null;
            }
            return null;
        }

        /// <summary>規則 (b): 選択肢の<b>得になる効果が、 何ひとつ起きない</b>なら、 その理由 (最初に書かれた得の効果の理由)。
        /// 得になる効果が 1 つでも起きる・得になる効果を持たない (立ち去る・代償だけ) なら null (選べる)。
        /// 「勝利後」の効果も得として数える (勝てば起きる)。 確率分岐は起きるものとして数える (中に航行の効果は置かない)。</summary>
        public static string NothingGainedReason(IEnumerable<EventEffect> effects, in EventEffectSituation s)
        {
            if (effects == null) return null;
            string first = null;
            foreach (var e in effects)
            {
                if (RoleOf(e) != EventEffectRole.Benefit) continue;
                string r = NotHappeningReason(e, s);
                if (r == null) return null;
                if (first == null) first = r;
            }
            return first;
        }
    }
}
