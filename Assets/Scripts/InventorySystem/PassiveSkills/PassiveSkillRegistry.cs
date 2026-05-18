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
            //  汎用パッシブ（6種×3段階）
            // ============================

            // 追撃
            Register(new PursuitI());
            Register(new PursuitII());
            Register(new PursuitIII());

            // 反撃
            Register(new CounterI());
            Register(new CounterII());
            Register(new CounterIII());

            // 剛力
            Register(new MightI());
            Register(new MightII());
            Register(new MightIII());

            // 堅忍
            Register(new FortitudeI());
            Register(new FortitudeII());
            Register(new FortitudeIII());

            // 慧眼
            Register(new InsightI());
            Register(new InsightII());
            Register(new InsightIII());

            // 活力
            Register(new VitalityI());
            Register(new VitalityII());
            Register(new VitalityIII());

            // ============================
            //  ユニークパッシブ
            // ============================

            // 盾系
            Register(new Parry());
            Register(new HolyShield());

            // 剣系
            Register(new Riposte());
            Register(new VoidStance());

            // 斧系
            Register(new Frenzy());
            Register(new BloodDecree());
            Register(new Sting());

            // 短剣系
            Register(new Execute());
            Register(new Nightfall());

            // デッドエンド
            Register(new Ignite());

            // 投資武器（聖剣ライン）
            Register(new HolyMemory());
            Register(new HolyAura());
            Register(new Terminus());

            // 呪い武器
            Register(new CurseBind());
            Register(new Abyss());

            // ============================
            //  ダイス固有パッシブ
            // ============================
            Register(new Shimmer());
            Register(new ReversalFlame());
            Register(new Steadfast());
            Register(new StarFate());
            Register(new Destiny());
            Register(new Starguide());
            Register(new Judgement());

            // 竜閃（ユニーク武器）
            Register(new MugaMushin());
            Register(new GaryoTensei());

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
            Register(new HoningDuel());
            Register(new EliteVigor());
            // エリート固有パッシブ（基敵ごと・逆スケール）
            Register(new EliteSlime());
            Register(new EliteGoblin());
            Register(new EliteKobold());
            Register(new EliteSkeleton());
            Register(new EliteWolf());
            Register(new EliteHarpy());
            Register(new EliteDecree13());
            Register(new EliteOrc());
            Register(new EliteLizard());
            Register(new EliteWraith());
            Register(new EliteGolem());
            Register(new EliteMinotaur());
            Register(new EliteDarkKnight());

            // 6～7層: ユニーク型
            Register(new MultiHead());
            Register(new Regeneration());
            Register(new DemonAura());
            Register(new Hellfire());
            Register(new Lifesteal());
            Register(new NightLord());
            Register(new DeathSentence());
            Register(new ScratchAura());

            // 6層 SinAltar 由来 (CombatManager から動的に注入される)
            Register(new Boss6Golgotha());
            Register(new Boss6SeveredTime());
            Register(new Boss6Ashen());

            // 13番目の死
            Register(new Decree13th());

            // 各層ボス専用パッシブ
            Register(new GoblinKingsCall());
            Register(new FrozenBardSong());
            Register(new MiasmaCorrosion());
            Register(new MirrorTwinsResponse());
            Register(new JudgmentFlames());
            Register(new RoyalEmber());
            Register(new SinChain());
            Register(new EternalBurning());
            Register(new ReturnToAshes());
            // 灰燼の王 リワーク（見切り＆カウンター型）
            Register(new JudgmentBlaze());
            Register(new AshArmor());
            Register(new ImmortalEmber());
            Register(new StarfireProliferation());
        }
    }
}
