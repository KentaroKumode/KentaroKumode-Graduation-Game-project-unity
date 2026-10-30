using System.Collections.Generic;
using ItemSystem.PassiveSkills.Effects;

namespace ItemSystem.PassiveSkills
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
            RegisterStatSkills();
            initialized = true;
        }

        /// <summary><b>ステータスで表せるスキルを items.json から登録する</b> (2026-09-19)。
        /// <c>passiveSkills[].stats</c> を持つスキルは専用クラスを持たず、 汎用の
        /// <see cref="Effects.StatModifierEffect"/> をその internalName で登録する。
        ///
        /// <para>同じ internalName を複数の品が持つ (例 SwordReachIII は T4 武器 4 本) ので、
        /// <b>定義が食い違っていたらエラー</b>にする ── レジストリは ID 単位で 1 つしか持てない。</para></summary>
        private static void RegisterStatSkills()
        {
            var db = ItemDatabase.Instance;
            if (db == null) return;
            var seen = new Dictionary<string, string>();
            foreach (var item in db.GetAllItems())
            {
                if (item?.passiveSkills == null) continue;
                foreach (var ps in item.passiveSkills)
                {
                    if (ps == null || !ps.IsStatSkill) continue;
                    string sig = Signature(ps.stats);
                    if (seen.TryGetValue(ps.internalName, out var prev))
                    {
                        if (prev != sig)
                            UnityEngine.Debug.LogError($"[PassiveSkillRegistry] {ps.internalName} のステータス定義が品によって違う: {prev} / {sig} ({item.internalName})");
                        continue;
                    }
                    foreach (var s in ps.stats)
                        if (!StatKeys.All.Contains(s.stat))
                            UnityEngine.Debug.LogError($"[PassiveSkillRegistry] {ps.internalName}: 未知のステータス '{s.stat}'");
                    seen[ps.internalName] = sig;
                    // 専用クラスもある = 固有部分を持つスキル (〈処刑〉のダイス潰し等)。 クラス側には
                    // **固有部分だけ**を残し、 ステータスで表せる部分は stats へ移すこと (両方に書くと二重に乗る)。
                    registry.TryGetValue(ps.internalName, out var inner);
                    registry[ps.internalName] = new Effects.StatModifierEffect(ps.internalName, ps.stats, inner);
                }
            }
        }

        private static string Signature(StatJson[] stats)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var s in stats)
                sb.Append(s.stat).Append('=').Append(s.value.ToString(System.Globalization.CultureInfo.InvariantCulture))
                  .Append(s.when == null || s.when.IsEmpty ? "" : UnityEngine.JsonUtility.ToJson(s.when)).Append(';');
            return sb.ToString();
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
        /// <summary>Lv 制家系の一覧 (読み取り専用)。 計装が「0 段で終わった家系」も数えるために要る。
        ///
        /// <para><b>2026-09-03: 家系内の Lv 制は廃止した。</b> 14 家系はそれぞれ Lv1〜4 の
        /// <b>どれか 1 段に固定</b>され、Tier は家系<b>間</b>の識別子（＝出現の稀少度と効果量）になった。
        /// ID の接尾辞 (I/II/III/IV) と <see cref="GetFamilyLevel"/> は<b>互換のため残置</b>しているが、
        /// <b>同一家系に複数段が同時に存在することはもう無い</b>。 これに依存していた機構
        /// （上位互換アップグレード割引 / <see cref="IsHigherTierPresent"/> /
        /// <c>TrySellLowerTierBefore</c> / <c>InventoryPower</c> の下位スキップ /
        /// <c>GameManager.ExcludeObsoleteFamilyTiers</c>）は<b>すべて発火しない死んだ経路</b>である。
        /// 測定の解釈にこれらを持ち出さないこと。</para></summary>
        public static System.Collections.Generic.IReadOnlyList<string> LeveledFamilies => _leveledFamilies;

        private static readonly string[] _leveledFamilies = {
            "Pursuit", "Might", "Fortitude", "Insight", "Indomitable", "Grievous",
            // ShieldBash は 2026-10-28、 Counter / Vitality / BladeEdge / BountyHunter / Conqueror / Lifesteal / LentTime は
            //   2026-10-31 (アイテム再編) に家系ごと削除
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
            //  アイテム再編 (2026-10-31・ReworkItemEffects.cs)
            // ============================
            Register(new OverheatStone());
            Register(new HearthCloak());
            Register(new DryTinderbox());
            Register(new MorningOven());
            Register(new OverflowMold());
            Register(new SecondStrike());
            Register(new SecondBeatHeart());
            Register(new OffBookChain());
            Register(new HeatWall());
            Register(new QuickKettle());
            Register(new IgnitionSpanner());
            Register(new RoughWhetstone());
            Register(new HarmlessLamp());
            Register(new ArsenalCrate());
            Register(new PatchedBoots());
            Register(new HopeShard());
            Register(new InfiniteMotor());
            Register(new ThunderPowder());
            Register(new OldGauge());
            Register(new InvertedRod());
            Register(new ThunderJar());
            Register(new ScorchedWire());
            Register(new ChargedCuirass());
            Register(new CatfishSpine());
            Register(new SearingWhip());
            Register(new LentTime());
            Register(new WornBuckler());
            Register(new Pauldron());
            Register(new MirrorBuckler());
            Register(new HolyRoadShoes());
            Register(new CompanionSoul());
            Register(new KnightsTrump());
            Register(new VeteranPatch());
            Register(new PulselessHeart());
            Register(new SmolderingBreastplate());
            Register(new LightningHeadguard());
            Register(new ThornBuckler());
            Register(new ArmorNeedle());
            Register(new StrainGauntlet());
            Register(new GreenKnife());
            Register(new PoisonWedge());
            Register(new HerbStaff());
            Register(new StitchThread());
            Register(new HollowPoisonRing());
            Register(new HundredCleaver());
            Register(new RuinedGardenSprayer());
            Register(new LongCenser());
            Register(new WeightingGauntlet());
            Register(new VenomBangle());
            Register(new RotBlade());
            Register(new BloodDayPendant());
            Register(new SoulBloodstone());
            Register(new BloodlineRing());
            Register(new SiegeAxe());
            Register(new BloodWheel());
            Register(new LeadHandAxe());
            Register(new FuryBlade());
            Register(new CoinDish());
            Register(new TenkouKaibutsu());
            Register(new LuckyCoin());
            Register(new SpoilsScale());
            Register(new BlastPrize());
            Register(new SurveyorMonocle());
            Register(new FierceBlade());
            Register(new SlowPoisonBlade());
            Register(new LapisWhetstone());
            Register(new FlintRing());
            Register(new Suntetsu());
            Register(new UnwhettedClub());

            // ============================
            //  汎用パッシブ（6種×3段階）
            // ============================

            // 追撃
            Register(new PursuitI());
            Register(new PursuitII());
            Register(new PursuitIII());

            // 反撃

            // 剛力
            // MightI はステータス化 (items.json の stats)
            Register(new MightII());
            Register(new MightIII());
            Register(new MightIV());

            // 堅忍
            // FortitudeI はステータス化
            Register(new FortitudeII());
            Register(new FortitudeIII());
            Register(new FortitudeIV());

            // 慧眼
            // InsightI / InsightII はステータス化
            Register(new InsightIII());
            Register(new InsightIV());

            // 素のステータス品 (2026-09-15)。 **ラダーではない** ── 1 品 1 効果の独立品。
            //   BRONZE/SILVER に発動条件なしで乗る品が 3 / 1 しか無かった穴を埋める。
            // 荒砥 / 二度目の打金 / 肩当て / 擦り減った小盾 はステータス化 (items.json の stats)

            // 活力
            // VitalityI はステータス化

            // ============================
            //  武器家系 専用ラダー (2026-09-05)
            //   共通ラダー (筋力/追撃/心眼/頑強/活力) の借用をやめ、家系ごとに 2 本ずつ持たせた。
            //   正本: WeaponFamilyEffects.cs / 割り当ては WeaponProgression.Families
            // ============================
            //   2026-10-28 整理: 1 段に仕組み 1 つ・家系ごとに防御と貫通の使い方を分けた (ADR-0013)。
            Register(new SwiftHandI());   // II / III はステータス化
            Register(new VenomHandI());   Register(new VenomHandII());   Register(new VenomHandIII());
            Register(new VitalStrike());  // 急所: 会心は防御無視 (会心率 +10% は stats)
            // 処刑はステータスだけ (残HP 25% 以下で与ダメ +80%)
            Register(new SwordReachI());  // II / III はステータス化
            Register(new DuelistIII());   // I / II はステータス化
            Register(new GuardCut());     // 切り返し
            Register(new UndyingBlade()); // 不滅の刃
            Register(new BulwarkI());     Register(new BulwarkII());     Register(new BulwarkIII());
            Register(new ShieldBashLadderI()); Register(new ShieldBashLadderII()); Register(new ShieldBashLadderIII());
            Register(new SteadfastStance());   // 堅守の構え
            Register(new Daybreak());          // 黎明
            Register(new FervorI());      Register(new FervorII());      Register(new FervorIII());
            Register(new ArmorBreakI());  Register(new ArmorBreakII());  Register(new ArmorBreakIII());
            // 捨て身はステータスだけ (HP 割合で劣勢)
            Register(new BloodEdict());   // 血令

            // 複合武器 T4 の固有 6 種は 2026-09-21 削除 (複合武器の廃止・GAME.md §24)。
            // 反攻 / 大鉈 / パリィ / 衛士の慣い / 復讐 / 蝕夜 / 処刑のダイス潰し / 果たし合い は 2026-10-28 削除 (GAME.md §24)。

            // ============================
            //  ユニークパッシブ
            // ============================

            // 剣系
            Register(new VoidStance());

            // 斧系
            Register(new Sting());

            // Ignite / CurseBind / Abyss は 2026-07-18 に class ごと削除 (dead_staff / curse 系列全廃)

            // ============================
            //  ダイス固有パッシブ
            // ============================
            // Destiny はステータス化 (単体戦)
            // 武器Tier段階補正 (Lightweight/Mastery/Skill) は 2026-07-15 廃止
            // (筋力とロールが重複)。クラス実装は AllPassiveSkillEffects に残置 = items.json 参照は将来的に別スキルへ差し替え要

            // 処刑・対タンク・役・触媒（2026-05-29 追加）
            Register(new GrievousI());      // 治癒阻害（Silver）
            Register(new GrievousII());     // 治癒遮断（Gold）
            Register(new ApexCrit());       // 天極（ゾロ目→会心確定+倍率）
            // LifestealII はステータス化
            Register(new IndomitableI());   // 不屈（敵threat軽減）
            Register(new IndomitableII());
            // IndomitableIII はステータス化
            Register(new IndomitableIV());
            // ShieldBashI〜IV (旧〈内から落ちた城盾〉) は 2026-10-28 削除。 盾家系の〈シールドバッシュ〉へ一本化した。
            // Bloodlust / Hermes はステータス化
            Register(new GoldKingBlade());

            // 2026-06-03 新規追加アイテム
            // EvenEyes / MasterworkNotes / KaleidoDice はステータス化

            // 2026-06-05 会心バリエーション（OnCriticalDamage / OnCriticalCheck）
            Register(new LacerationCore());   // 裂傷の刃心（会心→出血+2、会心倍率連動）

            // 2026-06-04 [剣の舞] セット（4枚集約→ブレイドダンスに変化）
            Register(new SaberWaltz());       // サーベル・ワルツ（ダイス+1／孤剣時HP半減）
            Register(new EspadaPasodoble());  // エスパーダ・パソドブレ（自他ダイス+5／与被ダメ+20%）
            Register(new FleuretBallet());    // フルーレ・バレエ（ダイス+3／敗北時自壊は救済チェーン）
            Register(new FalconTango());      // ファコン・タンゴ（戦闘終了時 廃棄+全カテゴリ獲得）
            Register(new BladeDance());       // ブレイドダンス（剣先スタック：ダイス/回復/反射）

            // 竜閃（ユニーク武器）
            Register(new MugaMushin());
            Register(new GaryoTensei());

            // 2026-07-15 鈍器系 (新規 9 種・会心を犠牲に非会心火力を伸ばすトレードオフ型)
            // 重い一撃 / 破城槌 / 鈍器 はステータス化 (items.json の stats)
            // SingleMinded はステータス化 (非会心の連続)
            // Stubbornness はステータス化

            // 2026-07-15 毒系 (新規 9 種・拘束/妨害中心のキーワードビルド)
            Register(new CorrosiveStrike());  // 腐蝕の一撃 (攻撃10% 毒+1)
            Register(new SerpentBlood());     // 蛇の血 (会心で毒+2)
            Register(new Paralysis());        // 麻痺毒 (毒スタック分 敵攻撃-N・拘束中核)
            // VenomBlade はステータス化
            Register(new SlowVenomCurse());   // 遅効の呪 (T5以降 毒+1/T)

            // 2026-07-15 炎上系 拡張 (既存 Ignite + 新 8種で計9のキーワードビルド化)
            // Burn (炎上) 系 2026-07-16 削除: Bleed と機構重複のため撤廃、「臨界」新軸に置換
            // Kindling/FlamingWeapon/InfernoOrder/Stoke/Immortalflame/InfernoManifest/FlameEdge/EverBurning は 2026-07-18 class ごと削除

            // 2026-07-15 出血系 拡張 (既存3種+新6種で計9のキーワードビルド化)
            // BloodScent はステータス化
            Register(new CrimsonBlade());    // 紅蓮の刃 (HP割合で出血付与)
            Register(new Wringing());        // 絞り出し (T終了時 出血×2 追加ダメ)
            Register(new BloodStrike());     // 血の一撃 (出血1消費で×3ダメ)
            Register(new AntiClotting());    // 止血阻害 (出血減衰無効化)
            Register(new BloodFate());       // 血の宿命 (開幕 出血+2)

            // 2026-07-15 充電系 (ADR-0009 柱5・新パイプライン専用)
            Register(new Thundercloud());     // 雷雲 (ロール後25%で+1)
            // Overload はステータス化
            Register(new ShortCircuit());   // 2026-07-16 改名: 旧 Criticality (「臨界」キーワードを新軸に譲渡)

            // 臨界系 (Rinkai) 2026-07-16 追加: メーター蓄積型・Burn 撤廃後の新軸
            Register(new UnyieldingHeat());    // 不朽の熱 (meter40+ で致命耐え・meter全消費)      // 臨界 (過充電3T継続で会心確定+全消費)

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

            // 13番目の死
            Register(new Decree13th());

            // 各層ボス専用パッシブ
            Register(new GoblinKingsCall());
            Register(new FrozenBardSong());
            Register(new MiasmaCorrosion());
            Register(new MirrorTwinsResponse());
            Register(new JudgmentFlames());
            Register(new RoyalEmber());
            Register(new BlazeBrand());
            Register(new SinChain());
            Register(new EternalBurning());
            Register(new ReturnToAshes());
            // 灰燼の王 リワーク（見切り＆カウンター型）※ADR-0009 相互攻撃モデル下で発火経路が壊れており deprecated
            Register(new JudgmentBlaze());
            Register(new AshArmor());
            Register(new ImmortalEmber());
            Register(new EmberAura());
            Register(new StarfireProliferation());
            Register(new ScorchedEarth()); // 焦土（敗北毎に最大HP-2・シールド破壊）
            // 灰燼の王 v2 リワーク (2026-07-16 ADR-0009 相互攻撃対応): 外殻半減/周期予兆/階段回復/収支負けスタック
            // 2026-07-16 二相型化: HP50% で Phase1(再生)→Phase2(火力連打) に切替 (AshRegrowth 停止 + EmberFury 発動 + Omen 周期2T)
            Register(new AshCarapace());
            Register(new EmberOmen());
            Register(new AshRegrowth());
            Register(new EmberFury());
            Register(new ScorchedPact());
            // ボス威風（ダイス合計バフ）※現在 enemies.json から撤廃済み・未参照。
            // 再有効化が容易なよう登録は残置（参照されなければ発火しない）。
            Register(new StrongOne());  // 強者 +4 (5層)
            Register(new Throne());     // 玉座 +8 (6層)
            Register(new Setsuna());    // 刹那 +12 (7層)
            // 5層裏ボス
            Register(new SaintGeorgesPhases());
            // 7層ボス: ヴェスカ（遺物学者）×4段連戦。 正本: docs/GAME.md §13-4
            Register(new RelicScholar());        // 遺物学者（毎T 遺物を抽選使用・予告で開示）
            Register(new StagnantTime());        // 停滞する時間（攻撃端子が空のTを罰する。L3 第一レバー）
            Register(new AnalyticComputation()); // 解析演算（収支プラスごとに回避率+3%・上限60%）
            Register(new RiftHeldOpen());        // 裂け目（毎T 現在HPの20%を自壊 → HP依存しない一定長）
            Register(new RecurrentTruth());      // 回帰性真理（割合ダメ/処刑/最大HP変更/デバフを無効化）
            Register(new ContinuousExperiment()); // 連続実験（HP0で死なず次フェーズへ。最終段は非所持）
            Register(new DivineTalent());        // 天与の才（段4のみ 2枚抽選）
            // [削除 2026-10-17] FlawlessRobe (天衣無縫) ── 覚者専用・使う敵なし        // 天衣無縫（回復/シールド獲得の減衰。 ヴェスカでも流用可）

            // [廃止] 覚者×7形態連戦 (AwakenedP1..P7 / TrueSelf)。
            //   旧ロール勝負モデル専用の機構で ADR-0009 既定 true 下では発火経路が成立しない。
            //   **2026-08-24 にクラスごと削除済み** (再登録できるコードはもう無い)。
            //   経緯と代替は docs/GAME.md §24 / §13-4。
        }
    }
}
