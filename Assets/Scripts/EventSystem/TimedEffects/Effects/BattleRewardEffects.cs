using GameLoop;
using ItemSystem.PassiveSkills;

namespace EventSystem.TimedEffects.Effects
{
    /// <summary>
    /// 〈徒労〉(時限デバフ・2026-11-13・docs/GAME.md §17): 深さ 6 の〈灰に埋もれた基地〉で受ける。 <see cref="Charges"/> つ持った状態になり、
    /// <b>通常の戦闘</b> (エリート・ボス・追跡者・使い魔・イベントの戦闘でない) <b>に勝つたびに 1 つ減り、 その戦闘では何も得られない</b>
    /// (ゴールド・ドロップ・強化素材・物資・そのほか戦闘の勝利で得る物が全部 0)。 エリートやボスの戦闘は減らさず、 報酬も普通に出る。 逃げた戦闘は減らさない。
    /// 層を出ても残る。
    /// <para>効果そのもの (報酬を 0 にする) は報酬を配る側の 1 か所 (<c>GameManager.TakeNormalBattleBoon</c>) が、 減らす前の残りを見て決める。
    /// ここの <see cref="Apply"/> は何もしない (減らすのは <see cref="TimedEffectManager.OnNormalBattleWon"/>)。</para>
    /// </summary>
    public class ToilDebuffEffect : ITimedEffect
    {
        /// <summary>名前 (event_list.txt の「時限デバフ[徒労]」と一致させる)。</summary>
        public const string Name = "徒労";
        /// <summary>受けた時に持つ数 (= 報われない通常の戦闘の数)。</summary>
        public const int Charges = 3;
        /// <summary>画面に出す説明 (仮の文)。</summary>
        public const string Description = "通常の戦闘に勝っても何も得られない";

        public string Id => Name;
        public TimedEffectTrigger Trigger => TimedEffectTrigger.NormalBattleWon;
        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat) { }
    }

    /// <summary>
    /// 〈覚悟〉(時限バフ・名前は仮・2026-11-13・docs/GAME.md §17): 〈徒労〉の対。 〈灰に埋もれた基地・再訪〉で手記の最後の頁を開くと得る。
    /// <see cref="Charges"/> つ持った状態になり、 <b>通常の戦闘に勝つたびに 1 つ減り、 その戦闘で得るゴールドとドロップと強化素材が <see cref="Multiplier"/> 倍</b>。
    /// 物資は増やさない (倍にするのはゴールド・ドロップ・強化素材だけ)。 ドロップの倍は抽選を <see cref="Multiplier"/> 回引く (乱数を余分に引く)。
    /// 隠し倍率ではない ── 名前と説明を画面に出し、 報酬の結果の行にも「〈覚悟〉で 2 倍」と出す。
    /// </summary>
    public class ReadinessBuffEffect : ITimedEffect
    {
        /// <summary>名前 (event_list.txt の「時限バフ[覚悟]」と一致させる)。</summary>
        public const string Name = "覚悟";
        /// <summary>得た時に持つ数。</summary>
        public const int Charges = 3;
        /// <summary>通常の戦闘の報酬 (ゴールド・ドロップ・強化素材) の倍率。</summary>
        public const int Multiplier = 2;
        /// <summary>画面に出す説明 (仮の文)。</summary>
        public const string Description = "通常の戦闘で得るゴールドとドロップと強化素材が2倍";

        public string Id => Name;
        public TimedEffectTrigger Trigger => TimedEffectTrigger.NormalBattleWon;
        public void Apply(CombatContext ctx, RunState run, CombatSystem.CombatManager combat) { }
    }
}
