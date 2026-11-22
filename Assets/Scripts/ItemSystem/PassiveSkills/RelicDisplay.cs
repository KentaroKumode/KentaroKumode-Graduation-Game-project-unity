namespace ItemSystem.PassiveSkills
{
    /// <summary>
    /// 秘宝の列 (#320) に出す、 品 1 つの今の見え方 (2026-11-15 作り直し・作者の方針「条件を満たしている状態なら光り続ける、 満たしていなければ光らない」)。
    /// 値は効果のクラスが自分の状態から作る (<see cref="IRelicDisplay"/>)。 画面 (UI.Game2D.Game2DBattleLcd の秘宝の列) はこれを読むだけで、 品と数のキーの対応を持たない。
    /// </summary>
    public struct RelicView
    {
        /// <summary>この値が効果のクラスから来たか (false = その品は見え方を持たない → 画面は「出来事で一瞬光る」か「光らない (【規則】)」で扱う)。</summary>
        public bool known;
        /// <summary>光っている (条件を満たしている・待機中・数が 1 以上)。</summary>
        public bool lit;
        /// <summary>アイコンの右下に出す数 (−1 = 出さない)。 <see cref="percent"/> なら % の値。</summary>
        public int count;
        public bool percent;
        /// <summary>1 戦闘 1 回の品を使い終わった (暗くする)。</summary>
        public bool spent;

        /// <summary>数を貯める型: 1 以上の間は光り、 右下に数。</summary>
        public static RelicView Count(int n) => new RelicView { known = true, lit = n > 0, count = n > 0 ? n : -1 };
        /// <summary>% で貯める型 (0.05 = 5%)。</summary>
        public static RelicView Pct(float v) { int p = (int)System.Math.Round(v * 100f); return new RelicView { known = true, lit = p > 0, count = p > 0 ? p : -1, percent = true }; }
        /// <summary>状態の間だけ効く型: 条件を満たしている間は光る (数は出さない)。</summary>
        public static RelicView On(bool on) => new RelicView { known = true, lit = on, count = -1 };
        /// <summary>1 戦闘 1 回の型: 使い終わったら暗く、 まだなら <paramref name="ready"/> の間は光る。</summary>
        public static RelicView Once(bool used, bool ready) => new RelicView { known = true, lit = !used && ready, count = -1, spent = used };
    }

    /// <summary>秘宝の列の判断に使う、 画面の側の今の挿し方 (配線を決める前の見込み)。 −1 = 分からない (戦闘の記録 <see cref="CombatContext"/> の値を使う)。</summary>
    public struct RelicEnv
    {
        public int atkDice, blockDice;
        public static readonly RelicEnv Unknown = new RelicEnv { atkDice = -1, blockDice = -1 };
        public int Atk(CombatContext ctx) => atkDice >= 0 ? atkDice : (int)ctx.GetAccumulated(Effects.MutualKeys.AttackDiceCount);
        public int Block(CombatContext ctx) => blockDice >= 0 ? blockDice : (int)ctx.GetAccumulated(Effects.MutualKeys.BlockDiceCount);
    }

    /// <summary>秘宝の列での見え方を持つ効果 (#320)。 持たない効果は、 動いて値が変わった瞬間に短く光る (<see cref="RelicFireProbe"/>・【規則】の品は光らない)。</summary>
    public interface IRelicDisplay
    {
        RelicView View(CombatContext ctx, RelicEnv env);
    }

    /// <summary>秘宝の列 (#320) が品 1 つをどう出すか。</summary>
    public enum RelicMode
    {
        /// <summary>効果のクラスが見え方を持つ (<see cref="IRelicDisplay"/>): 光る / 数 / 使用済みをそのまま出す。</summary>
        State,
        /// <summary>【規則】の品: 光らせない。</summary>
        Quiet,
        /// <summary>出来事に反応するだけの品: 効果で値が変わった瞬間だけ短く光る (<see cref="RelicFireProbe"/>・起きるたびに光り直す)。</summary>
        Pulse,
    }

    /// <summary>品 1 つの見え方を引く (#320)。 品の効果 (items.json の skills) の中に <see cref="IRelicDisplay"/> を持つ物があればその見え方、
    /// 無ければ説明に【規則】がある品は <see cref="RelicMode.Quiet"/>、 それ以外は <see cref="RelicMode.Pulse"/>。 戦闘の値は読むだけ。</summary>
    public static class RelicLookup
    {
        public static RelicMode ModeOf(string itemId, RelicEnv env, out RelicView view)
        {
            view = default;
            var data = ItemDatabase.Instance != null ? ItemDatabase.Instance.GetItem(itemId) : null;
            if (data == null) return RelicMode.Pulse;
            var ctx = PassiveSkillManager.Instance != null ? PassiveSkillManager.Instance.Context : null;
            if (data.passiveSkills != null)
                foreach (var ps in data.passiveSkills)
                {
                    var d = DisplayOf(PassiveSkillRegistry.Get(ps.internalName));
                    if (d == null) continue;
                    if (ctx == null) { view = new RelicView { known = true, count = -1 }; return RelicMode.State; }
                    view = d.View(ctx, env);
                    if (view.known) return RelicMode.State;
                }
            return !string.IsNullOrEmpty(data.description) && data.description.Contains("【規則】") ? RelicMode.Quiet : RelicMode.Pulse;
        }

        /// <summary>効果 (包み Inner をほどく) が見え方を持っていればそれ。</summary>
        public static IRelicDisplay DisplayOf(IPassiveSkillEffect e)
        {
            for (int k = 0; k < 4 && e != null; k++)
            {
                if (e is IRelicDisplay d) return d;
                if (e is Effects.StatModifierEffect sm) e = sm.Inner;
                else break;
            }
            return null;
        }
    }
}
