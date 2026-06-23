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

        /// <summary>IRunResettable を実装する全スキルの ResetRunState を呼ぶ。
        /// GameManager.StartNewRun から呼び、 ラン跨ぎ永続状態 (Nightfall.persistentOverdamage 等) を初期化。</summary>
        public static void ResetAllRunState()
        {
            EnsureInitialized();
            foreach (var effect in registry.Values)
                if (effect is IRunResettable resettable)
                    resettable.ResetRunState();
        }

        private static void Register(IPassiveSkillEffect effect)
        {
            registry[effect.SkillId] = effect;
        }

        // ============================================================
        //  Lv 家系テーブル (2026-06-22 追加)
        //  仕様: 同名パッシブは 1 回のみ発動。 同家系で複数 Lv 所持時は最高 Lv のみ発動。
        //  例: MightI + MightII 同時所持 → MightII のみ発動 (MightI は無効化)
        // ============================================================
        private static readonly string[] _leveledFamilies = {
            "Pursuit", "Counter", "Might", "Fortitude", "Insight", "Vitality",
            "BladeEdge", "BountyHunter", "Conqueror", "Lifesteal", "Indomitable",
            "ShieldBash", "LentTime", "Grievous",
        };

        /// <summary>skill ID から家系名 + Lv (1-4) を解析。 Lv 制でなければ (id, 0) を返す。</summary>
        public static (string family, int level) GetFamilyLevel(string skillId)
        {
            if (string.IsNullOrEmpty(skillId)) return (skillId, 0);
            foreach (var fam in _leveledFamilies)
            {
                if (!skillId.StartsWith(fam)) continue;
                string suffix = skillId.Substring(fam.Length);
                int lv = suffix switch { "I" => 1, "II" => 2, "III" => 3, "IV" => 4, _ => 0 };
                if (lv > 0) return (fam, lv);
            }
            return (skillId, 0);
        }

        /// <summary>同家系の上位 Lv が指定 IDs 集合に存在するか。 上位ありなら true (=自分は抑制対象)。</summary>
        public static bool IsHigherTierPresent(string skillId, System.Collections.Generic.IEnumerable<string> allSkillIds)
        {
            var (family, level) = GetFamilyLevel(skillId);
            if (level <= 0) return false;
            foreach (var id in allSkillIds)
            {
                if (id == skillId) continue;
                var (fam2, lv2) = GetFamilyLevel(id);
                if (fam2 == family && lv2 > level) return true;
            }
            return false;
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
            Register(new PursuitIV());

            // 反撃
            Register(new CounterI());
            Register(new CounterII());
            Register(new CounterIII());
            Register(new CounterIV());

            // 剛力
            Register(new MightI());
            Register(new MightII());
            Register(new MightIII());
            Register(new MightIV());

            // 堅忍
            Register(new FortitudeI());
            Register(new FortitudeII());
            Register(new FortitudeIII());
            Register(new FortitudeIV());

            // 慧眼
            Register(new InsightI());
            Register(new InsightII());
            Register(new InsightIII());
            Register(new InsightIV());

            // 活力
            Register(new VitalityI());
            Register(new VitalityII());
            Register(new VitalityIII());
            Register(new VitalityIV());

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
            Register(new IronWall());
            Register(new CopperSteady());
            Register(new Moroha());
            Register(new Greed());
            Register(new Perfection());
            Register(new Eternal());
            Register(new Lightweight());
            Register(new Mastery());
            Register(new Skill());
            Register(new BladeEdgeI());
            Register(new BladeEdgeII());
            Register(new BladeEdgeIII());
            Register(new BladeEdgeIV());

            // 処刑・対タンク・役・触媒（2026-05-29 追加）
            Register(new BountyHunterI());
            Register(new BountyHunterII());
            Register(new BountyHunterIII());
            Register(new BountyHunterIV());
            Register(new GrievousI());      // 治癒阻害（Silver）
            Register(new GrievousII());     // 治癒遮断（Gold）
            Register(new Skyladder());      // 天梯（階段→×2）
            Register(new ApexCrit());       // 天極（ゾロ目→会心確定+倍率）
            Register(new ConquerorI());
            Register(new ConquerorII());
            Register(new ConquerorIII());
            Register(new ConquerorIV());
            Register(new LifestealI());     // 吸血（与ダメ%回復）
            Register(new LifestealII());
            Register(new LifestealIII());
            Register(new LifestealIV());
            Register(new IndomitableI());   // 不屈（敵threat軽減）
            Register(new IndomitableII());
            Register(new IndomitableIII());
            Register(new IndomitableIV());
            Register(new ShieldBashI());    // シールドバッシュ（勝利時 与ダメ%シールド化）
            Register(new ShieldBashII());
            Register(new ShieldBashIII());
            Register(new ShieldBashIV());
            Register(new LentTimeI());       // 貸与された時間（被ダメ遅延・上限で一括）
            Register(new LentTimeII());
            Register(new LentTimeIII());
            Register(new LentTimeIV());
            Register(new Lifeline());       // 命脈（ユニーク）
            Register(new PalePikeKnight()); // 蒼白の槍騎士（軽減無視ダメ増幅）
            Register(new Resonance());      // 共鳴（所持数スケール）
            Register(new Truce());
            Register(new TenkouKaibutsu());
            Register(new Bloodlust());
            Register(new Hermes());
            Register(new HungerPill());
            Register(new GoldKingBlade());

            // 2026-06-03 新規追加アイテム
            Register(new EvenEyes());         // 賽振りの目隠し（全偶数→与ダメ+15%）
            Register(new TwinDice());         // 双子の賽（ペア→会心ダイス+1）
            Register(new BloodPathBanner());  // 血路の旗（敵出血stack×与ダメ+3%）
            Register(new MasterworkNotes());  // 匠の手控え（weaponPlus≥3→与ダメ+12%）
            Register(new KaleidoDice());      // 万華の賽（全同/全異/階段→与ダメ×2）
            Register(new JudgmentScale());    // 断罪の天秤（勝利時 合計差×4%、上限+100%）

            // 2026-06-05 会心バリエーション（OnCriticalDamage / OnCriticalCheck）
            Register(new LacerationCore());   // 裂傷の刃心（会心→出血+2、会心倍率連動）
            Register(new GuardFlash());       // 防殻の一閃（会心ダメの5%シールド）
            Register(new VitalPierce());      // 急所穿ち（会心→軽減無視+5）
            Register(new LifeFang());         // 吸命の牙（会心→与ダメ15%回復）
            Register(new SinglePoint());      // 一点集中（会心倍率+0.5／分子-2）
            Register(new ChainApex());        // 連環の極み（会心毎に倍率+0.2累積）

            // 2026-06-04 [剣の舞] セット（4枚集約→ブレイドダンスに変化）
            Register(new SaberWaltz());       // サーベル・ワルツ（ダイス+1／孤剣時HP半減）
            Register(new EspadaPasodoble());  // エスパーダ・パソドブレ（自他ダイス+5／与被ダメ+20%）
            Register(new FleuretBallet());    // フルーレ・バレエ（ダイス+3／敗北時自壊は救済チェーン）
            Register(new FalconTango());      // ファコン・タンゴ（戦闘終了時 廃棄+全カテゴリ獲得）
            Register(new BladeDance());       // ブレイドダンス（剣先スタック：ダイス/回復/反射）

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
            Register(new Berserk());
            Register(new IntimidatePlus());
            Register(new IntimidatePlusPlus());
            Register(new GreedyMerchant());

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
            Register(new EmberAura());
            Register(new StarfireProliferation());
            Register(new ScorchedEarth()); // 焦土（敗北毎に最大HP-2・シールド破壊）
            // ボス威風（ダイス合計バフ）※現在 enemies.json から撤廃済み・未参照。
            // 再有効化が容易なよう登録は残置（参照されなければ発火しない）。
            Register(new StrongOne());  // 強者 +4 (5層)
            Register(new Throne());     // 玉座 +8 (6層)
            Register(new Setsuna());    // 刹那 +12 (7層)
            // 5層裏ボス
            Register(new SaintGeorgesPhases());
            // 7層裏ボス: 覚者×7形態大連戦
            Register(new AwakenedP1Inverse());
            Register(new AwakenedP2BurstFire());
            Register(new AwakenedP3Mirror());
            Register(new AwakenedP4Riposte());
            Register(new AwakenedP5Silent());
            Register(new AwakenedP6EmberWill());
            Register(new AwakenedP7Myokaku());
            Register(new FlawlessRobe()); // 天衣無縫（覚者4形態の回復/シールド減衰）
            Register(new TrueSelf());     // 真我（7層全形態・ダイス上限超の素ロール加算。 オートチューナーが調整）
            // [互換用残置] 旧 AwakenedTrial は連戦化により未使用
        }
    }
}
