using System.Collections.Generic;
using InventorySystem.PassiveSkills.Effects;

namespace InventorySystem.PassiveSkills
{
    /// <summary>
    /// 全パッシブスキル実装の静的レジストリ
    /// JSONの internalName → IPassiveSkillEffect のマッピング
    /// 
    /// 新スキル追加手順:
    /// 1. AllPassiveSkillEffects.cs に IPassiveSkillEffect 実装クラスを追加
    /// 2. RegisterAll() 内に Register(new XxxSkill()) を1行追加
    /// </summary>
    public static class PassiveSkillRegistry
    {
        private static readonly Dictionary<string, IPassiveSkillEffect> registry 
            = new Dictionary<string, IPassiveSkillEffect>();

        private static bool initialized = false;

        /// <summary>
        /// レジストリを初期化（自動呼び出し）
        /// </summary>
        public static void EnsureInitialized()
        {
            if (initialized) return;
            RegisterAll();
            initialized = true;
        }

        /// <summary>
        /// internalName からスキル実装を取得
        /// </summary>
        public static IPassiveSkillEffect Get(string internalName)
        {
            EnsureInitialized();
            registry.TryGetValue(internalName, out var effect);
            return effect;
        }

        /// <summary>
        /// 全登録済みスキルを取得
        /// </summary>
        public static IEnumerable<IPassiveSkillEffect> GetAll()
        {
            EnsureInitialized();
            return registry.Values;
        }

        /// <summary>
        /// スキルが登録されているか確認
        /// </summary>
        public static bool Contains(string internalName)
        {
            EnsureInitialized();
            return registry.ContainsKey(internalName);
        }

        private static void Register(IPassiveSkillEffect effect)
        {
            registry[effect.SkillId] = effect;
        }

        /// <summary>
        /// 全スキルを登録
        /// ★ 新スキル追加時はここに1行追加するだけ ★
        /// </summary>
        private static void RegisterAll()
        {
            // ============================
            //  プレイヤー武器スキル
            // ============================

            // 盾系
            Register(new Breakfall());
            Register(new SpikeArmor());
            Register(new Endurance());
            Register(new DivineShield());
            Register(new DawnBlessing());

            // 剣系
            Register(new BasicSword());
            Register(new Recovery());
            Register(new WandererWit());
            Register(new DragonSlayer());
            Register(new VoidStance());

            // 斧系
            Register(new PainRevert());
            Register(new Warcry());
            Register(new BloodPact());
            Register(new ApexPredator());
            Register(new BloodDecree());

            // 短剣系
            Register(new QuickHands());
            Register(new FatalStab());
            Register(new Sting());
            Register(new Execution());
            Register(new BlindJustice());
            Register(new Nightfall());

            // ============================
            //  敵専用スキル
            // ============================

            // 1～3層: シンプル型
            Register(new Trapper());
            Register(new Undying());
            Register(new Sprint());
            Register(new BruteForce());
            Register(new Flight());

            // 4～5層: 複合型
            Register(new HardScales());
            Register(new TailStrike());
            Register(new Rampage());
            Register(new Ethereal());
            Register(new Curse());
            Register(new Immovable());
            Register(new CounterStance());

            // 6～7層: ユニーク型
            Register(new MultiHead());
            Register(new Regeneration());
            Register(new DemonAura());
            Register(new Hellfire());
            Register(new Lifesteal());
            Register(new NightLord());
            Register(new DeathSentence());
        }
    }
}
