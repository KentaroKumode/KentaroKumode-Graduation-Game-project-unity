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
            WrapEntries();
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

        /// <summary>毒・臨界の軸の既存品に入り口 (決まった機会に資源を少し足す) を重ねる (2026-11-07・<see cref="Effects.EntryEffect"/>)。</summary>
        private static void WrapEntries()
        {
            foreach (var e in Effects.EntryEffect.Table)
            {
                if (!registry.TryGetValue(e.id, out var inner))
                {
                    UnityEngine.Debug.LogError($"[PassiveSkillRegistry] 入り口の対象 {e.id} が登録されていない");
                    continue;
                }
                registry[e.id] = new Effects.EntryEffect(inner, e.when, e.heat, e.amount, e.gate, e.gateN);
            }
        }

        private static string Signature(StatJson[] stats)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var s in stats)
                sb.Append(s.stat).Append('=').Append(s.value.ToString(System.Globalization.CultureInfo.InvariantCulture))
                  .Append(s.when == null || s.when.IsEmpty ? "" : UnityEngine.JsonUtility.ToJson(s.when))
                  .Append(string.IsNullOrEmpty(s.per) ? "" : $"/per={s.per},{s.perEvery},{s.perMax}")
                  .Append(string.IsNullOrEmpty(s.on) ? "" : "@" + s.on).Append(';');
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

        /// <summary>中断セーブ (issue #84): ラン中の状態を持つスキルの状態を (スキル id, 状態) の並びで写す。 id の順に並べる。
        /// <see cref="IRunResettable"/> なのに <see cref="IRunStatePersistent"/> を実装していないスキルは、 再開で状態が 0 に戻るので警告する。</summary>
        public static void CaptureAllRunState(List<string> ids, List<string> states)
        {
            EnsureInitialized();
            var keys = new List<string>(registry.Keys);
            keys.Sort(System.StringComparer.Ordinal);
            foreach (var k in keys)
            {
                var effect = registry[k];
                if (effect is IRunStatePersistent p) { ids.Add(k); states.Add(p.CaptureRunState() ?? ""); }
                else if (effect is IRunResettable)
                    UnityEngine.Debug.LogWarning($"[PassiveSkillRegistry] {k} はラン中の状態を持つが中断セーブに写せない (IRunStatePersistent が無い)。 再開するとこの状態は 0 に戻る");
            }
        }

        /// <summary><see cref="CaptureAllRunState"/> の写しを戻す。 先に <see cref="ResetAllRunState"/> を呼んでおくこと (写しに無いスキルは 0 のまま)。
        /// 登録に無い id・読めない状態があれば false (戻せた分は戻す)。</summary>
        public static bool RestoreAllRunState(List<string> ids, List<string> states)
        {
            EnsureInitialized();
            int n = ids?.Count ?? 0;
            if (n != (states?.Count ?? 0)) return false;
            bool ok = true;
            for (int i = 0; i < n; i++)
            {
                if (registry.TryGetValue(ids[i], out var effect) && effect is IRunStatePersistent p && p.RestoreRunState(states[i])) continue;
                UnityEngine.Debug.LogWarning($"[PassiveSkillRegistry] 中断セーブのスキル状態を戻せない: {ids[i]} = {states[i]}");
                ok = false;
            }
            return ok;
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
            // 鎧縫いの針 はステータス化 (段 3・敵の出血 1 につき貫通 +1)
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
            // 血日に澄む佩玉 はステータス化 (段 3・敵の出血 1 につき会心率 +2%)
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
            //  軸の品 (2026-11-07・AxisItemEffects.cs)。 各軸に 燃料 / ① 数を参照 / ②【規則】 / 誓約
            // ============================
            // 研ぎ師の見立て書 / 千里眼の水晶 / 麻痺の吹き矢 はステータス化 (段 3・2026-11-11)
            Register(new FalconerGlove());                                       Register(new LayeredCutForm());     Register(new LayeredScabbard()); Register(new PinpointEye());
            Register(new ReturnEdgeClasp());  Register(new UnstanchedNeedles()); Register(new ClotVial());            Register(new RustOath());
            Register(new TasterSpoon());      Register(new MiasmaJar());         Register(new RotMossBall());         Register(new DregsOath());
            Register(new FirekeeperPoker());  Register(new CharcoalFeeder());       Register(new SparkReadingGlasses()); Register(new RefiredPrimer());   Register(new CoreOath());
            Register(new ToppedJar());        Register(new LeakyCopperWire());   Register(new ForkedTerminal());      Register(new SpriteOath());
            Register(new PaddedBracer());     Register(new OiledShieldLeather());Register(new ShieldPushArt());       Register(new WardenOath());
            // 軸の品・追加 34 品 (2026-11-07・AxisItemEffects2.cs)。 各軸を 23 品にそろえた
            Register(new AimNotch());   Register(new FirstArrow());   Register(new LingeringBell()); Register(new ParryFan());
            Register(new NickAppraisal()); Register(new LosingStreakDie()); Register(new ChainedFlash()); Register(new QuickDrawArt());
            Register(new SpareBattery()); Register(new StaticBlanket()); Register(new FlintNeedle()); Register(new StorageGauntlet());
            Register(new RerollHammer()); Register(new InsulatedCloak()); Register(new LightningStake()); Register(new ThunderDrum());
            Register(new ChippedKnife()); Register(new ThornGlove()); Register(new RiftWedge()); Register(new DrippingEdge());
            Register(new NoClotSalve()); Register(new CrimsonPress()); Register(new EndlessSpringCrystal());
            Register(new AcidVial()); Register(new NeedleRing()); Register(new LeafMoldBag());
            Register(new MiasmaMantle()); Register(new PoolKeeper());
            Register(new RivetPlate()); Register(new CatchBracer()); Register(new LayeredTagShield()); Register(new BrokenShieldGrudge());

            // ============================
            //  汎用パッシブ
            // ============================
            // 追撃・剛力・堅忍・慧眼・不屈・治癒阻害の I〜IV の効果クラス (英字の id) は 2026-11-12 削除。
            //   スキル id が品の名前になった (2026-09-22) あと、 どの品もその id を持っていなかった。

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
            // 処刑はステータスだけ (残HP 35% 以下で与ダメ +80%・2026-11-11 に 25% から)
            Register(new SwordReachI());  // II / III はステータス化
            Register(new DuelistIII());   // I / II はステータス化
            Register(new GuardCut());     // 切り返し
            Register(new UndyingBlade()); // 不滅の刃
            Register(new BulwarkI());     Register(new BulwarkII());     Register(new BulwarkIII());
            Register(new ShieldBashLadderI()); Register(new ShieldBashLadderII()); Register(new ShieldBashLadderIII());
            Register(new SteadfastStance());   // 堅守の構え
            Register(new Daybreak());          // 黎明
            Register(new FervorI());      // II / III はステータス化 (段 3・経過ターン 1 につき 攻撃に挿した出目 +10%・上限 9 / 14)
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

            // Ignite / CurseBind / Abyss は 2026-07-18 に class ごと削除 (dead_staff / curse 系列全廃)

            // ============================
            //  ダイス固有パッシブ
            // ============================
            // Destiny はステータス化 (単体戦)
            // 武器Tier段階補正 (Lightweight/Mastery/Skill) は 2026-07-15 廃止
            // (筋力とロールが重複)。クラス実装は AllPassiveSkillEffects に残置 = items.json 参照は将来的に別スキルへ差し替え要

            // 処刑・対タンク・役・触媒（2026-05-29 追加）
            // LifestealII はステータス化
            // IndomitableIII はステータス化
            // ShieldBashI〜IV (旧〈内から落ちた城盾〉) は 2026-10-28 削除。 盾家系の〈シールドバッシュ〉へ一本化した。
            // Bloodlust / Hermes はステータス化
            // 溶金の領主剣 はステータス化 (段 3・使ったゴールド 1 につき与ダメ +0.2%)

            // 2026-06-03 新規追加アイテム
            // EvenEyes / MasterworkNotes / KaleidoDice はステータス化

            // 2026-06-05 会心バリエーション（OnCriticalDamage / OnCriticalCheck）
            Register(new LacerationCore());   // 裂傷の刃心（会心→出血+2、会心倍率連動）

            // 2026-06-04 [剣の舞] セット（4枚集約→無銘の剣舞譜に変化）
            Register(new SaberWaltz());       // サーベル・ワルツ（ダイス+1／孤剣時HP半減）
            Register(new EspadaPasodoble());  // エスパーダ・パソドブレ（自他ダイス+5／与被ダメ+20%）
            Register(new FleuretBallet());    // フルーレ・バレエ（ダイス+3／敗北時自壊は救済チェーン）
            Register(new FalconTango());      // ファコン・タンゴ（戦闘終了時 廃棄+全カテゴリ獲得）
            Register(new BladeDance());       // 無銘の剣舞譜（剣先スタック：ダイス/回復/反射）

            // 竜閃（ユニーク武器）
            Register(new MugaMushin());
            Register(new GaryoTensei());

            // 2026-07-15 鈍器系 (新規 9 種・会心を犠牲に非会心火力を伸ばすトレードオフ型)
            // 重い一撃 / 破城槌 / 鈍器 はステータス化 (items.json の stats)
            // SingleMinded はステータス化 (非会心の連続)
            // Stubbornness はステータス化

            // 2026-07-15 毒系 (新規 9 種・拘束/妨害中心のキーワードビルド)
            Register(new SerpentBlood());     // 蛇の血 (会心で毒+2)
            Register(new Paralysis());        // 麻痺毒 (毒スタック分 敵攻撃-N・拘束中核)
            // VenomBlade はステータス化

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
            // Overload はステータス化
            // 雷神槍 (旧 ShortCircuit) はステータス化 (段 3・このターンに使った充電の半分を攻撃へ)

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
            Register(new CrackingArmor());
            Register(new StackingArmor());
            Register(new TailStrike());
            Register(new Rampage());
            Register(new Ethereal());
            Register(new Curse());
            Register(new Immovable());
            Register(new CounterStance());
            Register(new HoningDuel());
            Register(new EliteVigor());
            // エリート固有パッシブ（基敵ごと・逆スケール）
            Register(new EliteDecree13());
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
            Register(new Berserk());
            Register(new IntimidatePlus());
            Register(new StormEnemy());   // 嵐の中で始まった戦闘 (2026-11-13)
            Register(new GreedyMerchant());

            // 13番目の死
            Register(new Decree13th());

            // 各層ボス専用パッシブ
            Register(new GoblinKingsCall());
            Register(new MiasmaCorrosion());
            Register(new MirrorTwinsResponse());
            Register(new JudgmentFlames());
            Register(new BlazeBrand());
            // 灰燼の王 リワーク（見切り＆カウンター型）※ADR-0009 相互攻撃モデル下で発火経路が壊れており deprecated
            Register(new JudgmentBlaze());
            Register(new AshArmor());
            // 灰燼の王 v2 リワーク (2026-07-16 ADR-0009 相互攻撃対応): 外殻半減/周期予兆/階段回復/収支負けスタック
            // 2026-07-16 二相型化: HP50% で Phase1(再生)→Phase2(火力連打) に切替 (AshRegrowth 停止 + EmberFury 発動 + Omen 周期3T)
            Register(new AshCarapace());
            Register(new EmberOmen());
            Register(new AshRegrowth());
            Register(new EmberFury());
            Register(new ScorchedPact());
            // 5層裏ボス
            Register(new SaintGeorgesPhases());
            // 7層ボス: ヴェスカ（遺物学者）×4段連戦。 正本: docs/GAME.md §13-4
            Register(new RelicScholar());        // 遺物学者（毎T 遺物を抽選使用・予告で開示）
            Register(new StagnantTime());        // 停滞する時間（引いた端子を確率で止める・処理は TerminalDeckState.DrawTurn / TerminalCards.Resolve。L3 第一レバー）
            Register(new AnalyticComputation()); // 解析演算（収支プラスごとに回避率+2%・上限40%・回避の次Tは回避なし）
            Register(new RiftHeldOpen());        // 裂け目（毎T ヴェスカ最大HPの2%・プレイヤー最大HPの1%を失う）
            Register(new RecurrentTruth());      // 回帰性真理（毒/出血の毎T半減・受ける会心倍率-0.5・臨界-5/T・処刑/最大HP変更を拒否）
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
