using System.Collections.Generic;
using ItemSystem.PassiveSkills.Effects;

namespace ItemSystem.PassiveSkills
{
    /// <summary>
    /// 全パッシブスキル実装の静的レジストリ (スキル id → IPassiveSkillEffect)。
    ///
    /// 新スキル追加手順: 品の軸の Effects/ のファイル (ChargeEffects.cs など・武器は WeaponFamilyEffects.cs) にクラスを足し、
    /// その軸の Register&lt;軸&gt;() に 1 行足す。 抜けは Tools/rework_audit.py が数える。
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

        /// <summary>充電の軸 (2026-11-21・ChargeEffects.cs・full-item-rework-ledger §①)。 〈二股の導線〉〈逆さ避雷針〉〈灼線鞭〉は端子の解決の中だけで効く (CombatSystem.ChargeCore の旗・クラスなし)。</summary>
        private static void RegisterCharge()
        {
            Register(new InfiniteMotor()); Register(new ThunderPowder()); Register(new ChargedCuirass()); Register(new OldGauge());
            Register(new ToppedJar()); Register(new SpareBattery()); Register(new StaticBlanket()); Register(new FlintNeedle());
            Register(new LightningHelm()); Register(new CatfishSpine()); Register(new LeakyCopperWire()); Register(new StorageGauntlet());
            Register(new RerollHammer()); Register(new InsulatedCloak()); Register(new LightningStake());
            Register(new OverloadTuner()); Register(new ScorchedWire()); Register(new ThunderJar()); Register(new ThunderDrum());
            Register(new ThunderSpear()); Register(new LentTime()); Register(new FourthStrikeCore()); Register(new ThunderReliquary());
        }

        /// <summary>毒の軸 (2026-11-21・PoisonEffects.cs・full-item-rework-ledger §②)。 〈緑染みの小刀〉〈蛇血瓶〉〈毒見役の小匙〉は端子の解決の中だけで効く (CombatSystem.PoisonCore の旗・クラスなし)。</summary>
        private static void RegisterPoison()
        {
            Register(new VenomBangle()); Register(new PoisonWedge()); Register(new AcidVial()); Register(new NeedleRing()); Register(new LeafMoldBag());
            Register(new LongCenser()); Register(new RuinedGardenSprayer()); Register(new HerbStaff()); Register(new StitchThread()); Register(new MiasmaJar());
            Register(new ThornBuckler()); Register(new HollowPoisonRing()); Register(new Paralysis()); Register(new RotBlade()); Register(new RotMossBall());
            Register(new Blowpipe()); Register(new PoisonMagazine()); Register(new SlowPoisonBlade()); Register(new MiasmaMantle()); Register(new PoolKeeper());
            Register(new CorrosionCrown()); Register(new BoneMeltOil());
        }

        /// <summary>シールドの軸 22 品のうち効果クラスを持つ 18 品 (2026-11-21・ShieldEffects.cs)。 残りの 4 品 (肩当て・当て布の籠手・鋲の胸当て・擦り減った小盾) は ShieldCore の旗。</summary>
        private static void RegisterShield()
        {
            Register(new MirrorBuckler()); Register(new CatchBracer()); Register(new LayeredTagShield()); Register(new HolyRoadShoes());
            Register(new HeroWill()); Register(new OiledShieldLeather()); Register(new BrokenShieldGrudge()); Register(new StrainGauntlet());
            Register(new VeteranPatch()); Register(new KnightsTrump()); Register(new CompanionSoul()); Register(new ShieldPushArt());
            Register(new SilentStance()); Register(new SiegeAxe()); Register(new SecondBeatHeart()); Register(new PulselessHeart());
            Register(new FallenCastleCornerstone()); Register(new BorderGuardStandard());
        }

        /// <summary>出血の軸 26 品のうち効果クラスを持つ 16 品 (2026-11-21・BleedEffects.cs)。 残りの 10 品 (鉛入りの握斧・血日に澄む佩玉・医家の反り刃・返し刃の留め金・
        /// 欠け刃のナイフ・裂け目の楔・鋸刃の山刀・血止め知らずの針箱・激情の刃・百人切りの鉈) は BleedCore の旗。 〈鎧縫いの針〉は stats の内側。</summary>
        private static void RegisterBleed()
        {
            Register(new ThornGlove()); Register(new DrippingEdge()); Register(new SoulBloodstone()); Register(new BloodFate());
            Register(new ArmorStitchNeedle()); Register(new NoClotSalve()); Register(new Wringing()); Register(new AntiClotting());
            Register(new BloodWheel()); Register(new ClotVial()); Register(new BloodStrike()); Register(new BloodlineRing());
            Register(new CrimsonPress()); Register(new EndlessSpringCrystal()); Register(new ReturningBloodSickle()); Register(new BloodWedge());
        }

        /// <summary>臨界の軸 26 品のうち効果クラスを持つ 15 品 (2026-11-21・HeatEffects.cs)。 残りの 11 品 (過熱石・燻りの革胸当て・火守りの火搔き棒・恒温の竈・二度詰めの雷管・
        /// 焚き足しの炭・帳簿外の爆薬・火花読みの眼鏡・燧石の指輪・暴れ窯の火床・柱折りの発破筒) は HeatCore の旗 (荒砥・点火スパナ・溢れ取りの鋳型・早沸かし釜・八日目の灰燼は旗とクラスの両方)。</summary>
        private static void RegisterHeat()
        {
            Register(new ArsenalCrate()); Register(new DryTinderbox()); Register(new HearthCloak()); Register(new PatchedBoots()); Register(new RoughWhetstone());
            Register(new IgnitionSpanner()); Register(new HeatWall()); Register(new SecondStrike()); Register(new OverflowMold()); Register(new QuickKettle());
            Register(new TenkouKaibutsu()); Register(new HopeShard()); Register(new UnyieldingHeat()); Register(new FierceBlade()); Register(new HarmlessLamp());
        }

        /// <summary>会心の軸 27 品のうち効果クラスを持つ 26 品 (2026-11-21・2026-11-23 に 6 品足した・CritEffects.cs)。 〈必殺の照準器〉は本体の撃の輪の旗 (CombatManager.Turn)。 超過会心率の変換・【練気】・【集中】は CritCore。</summary>
        private static void RegisterCrit()
        {
            Register(new FalconerGlove()); Register(new AimNotch()); Register(new FirstArrow()); Register(new LingeringBell()); Register(new ParryFan());
            Register(new UnwhettedClub()); Register(new SurveyorMonocle()); Register(new WhetAppraisal()); Register(new NickAppraisal()); Register(new HeftyPommel());
            Register(new LapisWhetstone()); Register(new LayeredScabbard()); Register(new LayeredCutForm()); Register(new ChainedFlash()); Register(new QuickDrawArt());
            Register(new Suntetsu()); Register(new PinpointEye()); Register(new ClairvoyantCrystal()); Register(new HeavenEyeGuard()); Register(new DeathMaskVisor());
            Register(new PulseNeedle()); Register(new ReadMark()); Register(new WhiteBladeMirror()); Register(new FirstStrokePin()); Register(new NarrowSpyglass()); Register(new OneFlashMastery());
        }

        /// <summary>山札の軸 19 品のうち効果クラスを持つ 2 品 (2026-11-21・DeckEffects.cs)。 残りの 17 品と〈焼き入れの挟み〉の撃の分・〈仕込みの配電盤〉の手札の分は
        /// CombatSystem.TerminalCards の DeckItems の旗 (〈予備の差込口〉〈整理された工具箱〉は CombatManager.BeginTerminalDeck)。</summary>
        private static void RegisterDeck()
        {
            Register(new TemperTongs()); Register(new PrimedSwitchboard());
        }

        /// <summary>役の軸 13 品のうち効果クラスを持つ 4 品 (2026-11-21・RoleEffects.cs)。 端子の解決の中の分 (見切りの木札・符丁の刻み板・欠け目の見立ての防御の無視・
        /// 血染めの歯車・揃いの拍子木の与ダメージ・雷鳴のゾロ目・血濡れの符丁) は CombatSystem.TerminalCards の RoleItems の旗、
        /// 擦り切れた賽袋・鏡合わせの盤・宿命の刻み目・万象のゾロ目は CombatManager の役の適用 (FreeRerollsLeft / ResolveRoles / DecideOmniPair)。</summary>
        private static void RegisterRole()
        {
            Register(new ChippedEyeReading()); Register(new MatchedClappers()); Register(new LosingStreakDie()); Register(new LowFaceConduit());
        }

        /// <summary>金の 12 品のうち効果クラスを持つ 5 品 (2026-11-21・GoldEffects.cs)。 残りの 7 品 (封蝋・紹介状・合札・銭皿・天秤・溶金・手形) は戦闘の外で、
        /// 持っているかを読む所が効かせる (溶金は items.json の stats・銭皿 = CoinDish・天秤 = SpoilsScale)。</summary>
        private static void RegisterGold()
        {
            Register(new LuckyCoin()); Register(new BlastPrize()); Register(new MacroReceipt()); Register(new MacroAbacus()); Register(new SampleBox());
        }

        /// <summary>軸なし汎用の 33 品のうち効果クラスを持つ 13 品 (2026-11-21・GeneralEffects.cs)。 残りは戦闘の外で働く (探索・嵐・報酬・特殊戦術)。</summary>
        private static void RegisterGeneral()
        {
            Register(new WeightingGauntlet()); Register(new BeltPouch()); Register(new BottomlessVial()); Register(new GapSpear());
            Register(new BreakingMove()); Register(new HeavyBreakerWedge()); Register(new AdversityHourglass()); Register(new MacroBarometer());
            Register(new SaberWaltz()); Register(new EspadaPasodoble()); Register(new FleuretBallet()); Register(new FalconTango()); Register(new BladeDance());
        }

        /// <summary>武器 13 品のスキル (2026-11-21・WeaponFamilyEffects.cs・full-item-rework-ledger §⑫)。 段の割り当ては WeaponProgression.Families、 竜閃は items.json の skills。</summary>
        private static void RegisterWeapon()
        {
            for (int i = 0; i < 3; i++) { Register(new Fervor(i)); Register(new SwordReach(i)); Register(new SwiftHand(i)); Register(new Bulwark(i)); }
            for (int i = 0; i < 2; i++) { Register(new ArmorBreak(i)); Register(new Duelist(i)); Register(new ChainBlade(i)); Register(new ShieldBash(i)); }
            Register(new Recklessness()); Register(new BloodEdict()); Register(new GuardCut()); Register(new UndyingBlade());
            Register(new VitalStrike()); Register(new Execution()); Register(new SteadfastStance()); Register(new Daybreak());
            Register(new GaryoTensei()); Register(new MugaMushin());
        }

        /// <summary>全スキルを登録する。 秘宝は軸ごと (Effects/ の軸別 10 ファイル)・武器・敵の順。 ステータスだけのスキルは <see cref="RegisterStatSkills"/>。</summary>
        private static void RegisterAll()
        {
            RegisterCharge();
            RegisterPoison();
            RegisterShield();
            RegisterBleed();
            RegisterHeat();
            RegisterCrit();
            RegisterDeck();
            RegisterRole();
            RegisterGold();
            RegisterGeneral();
            RegisterWeapon();
            RegisterEnemy();
        }

        /// <summary>敵とボスのパッシブ (EnemyPassiveSkillEffects.cs・7 層のヴェスカは VescaEffects.cs)。</summary>
        private static void RegisterEnemy()
        {
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
        }
    }
}
