using System;
using System.Collections.Generic;

namespace CombatSystem
{
    /// <summary>端子の種類 (ADR-0014)。 「攻撃に挿した個数」などを読む既存の品・武器・敵の規則は、 この種類で数える。</summary>
    public enum TerminalKind { Attack, Block, Charge, Support }

    /// <summary>出目の条件。 合わない出目は挿せない (不発)。</summary>
    public enum TerminalCond
    {
        None,
        AtMost3,        // 3 以下
        AtLeast5,       // 5 以上
        AtLeast6,       // 6 以上
        Even,           // 偶数
        Odd,            // 奇数
        SameElsewhere,  // 同じ目が他の端子に挿さっている
        PairSame,       // 同じ目 2 個を挿す (capacity 2・2 個そろって初めて発動)
    }

    /// <summary>端子の名前 (id = 表示名・品と同じ規則)。 **文字列を直書きしない** ── ここを経由する。</summary>
    public static class TerminalIds
    {
        // 基本
        public const string Attack = "攻撃", Block = "防御", Charge = "充電";
        // コモン
        public const string Igniter = "点火栓", Detector = "検波器", DischargeTube = "放電管", Deathwish = "決死", Relay = "継電器",
            Grinder = "研磨盤", Oscillator = "発振器", Twin = "二連", Fuse = "ヒューズ", Ground = "接地", Insulator = "絶縁体",
            Parry = "受け太刀", Breaker = "遮断器", Rectifier = "整流器", Battery = "蓄電池", Heal = "治癒", Capacitor = "蓄電器",
            Arrester = "避雷器", Transformer = "変圧器", Switchboard = "配電盤";
        // レア
        public const string Amplifier = "増幅器", VacuumTube = "真空管", Resonator = "共振器", Foundry = "鋳造口", Coolant = "冷却材",
            Counter = "計数器", Preheater = "予熱器", Mains = "主幹", TwinShot = "双発", Harvester = "収穫機", Whet = "研ぎ澄まし",
            LootBox = "戦利品箱", Stripper = "剥ぎ取り", Doubler = "倍電圧", Retainer = "保持器", StopSignal = "停止信号", Fork = "二股";
        // 軸
        public const string Sight = "照星", VitalRead = "急所読み", Barb = "返し鉤", BloodTap = "血抜き栓", Mister = "霧吹き口",
            PoisonScale = "毒見の秤", Stoker = "焚き口", AfterHeat = "余熱板", Leak = "漏電口", BigDischarge = "大放電",
            Patch = "当て板", ShieldPush = "盾押し口";
        // 使い捨て
        public const string Detonator = "起爆筒", Piercer = "貫通弾", ReliefValve = "非常弁", FirstAid = "応急箱", FullCharge = "満充電",
            CoolShot = "冷却弾", Copier = "写し取り", BurnOut = "焼き切り";
    }

    /// <summary>端子 1 種の定義。</summary>
    public sealed class TerminalDef
    {
        public string id;
        public TerminalKind kind;
        public TerminalCond cond;
        /// <summary>挿せる出目の数 (既定 1)。</summary>
        public int capacity = 1;
        /// <summary>発動に払う充電 (足りなければ不発)。</summary>
        public int chargeCost;
        /// <summary>基本 (攻撃・防御・充電)。 「基本以外の発動」に数えない。</summary>
        public bool basic;
        /// <summary>層ボスの報酬に出る。</summary>
        public bool rare;
        /// <summary>使い捨て: 使うと、 その戦闘中は山札に戻らない。</summary>
        public bool disposable;
        /// <summary>常設: 一度引くとその戦闘中ずっと場に残り、 手札の枠を使わない。</summary>
        public bool permanent;
        /// <summary>軸 (AxisRules の名前)。 null = 軸なし。 強さがその軸の偏重で決まり、 報酬に出る確率も偏重で寄せる。</summary>
        public string axis;
        public string description;
        /// <summary>条件の短い表記 (画面用)。 条件なしは null。</summary>
        public string condLabel;
    }

    /// <summary>戦闘の間に端子が覚えている値 (溜め・連続・持ち越し)。 BOT の見積もりは <see cref="Clone"/> した写しに掛ける。</summary>
    public sealed class TerminalBattleState
    {
        /// <summary>溜める端子の溜まり (端子の名前 → 量)。</summary>
        public Dictionary<string, int> gauge = new Dictionary<string, int>();
        /// <summary>〈発振器〉: 前に挿した目 / 連続回数。</summary>
        public int oscLast, oscStreak;
        /// <summary>基本以外の端子の発動の累計 (〈計数器〉)。</summary>
        public int firedTotal;
        /// <summary>〈計数器〉で積んだ与ダメージ (+10% × 段)。</summary>
        public int counterStacks;
        /// <summary>このターン / 次のターン、 出目の条件を無視する (〈予熱器〉)。</summary>
        public bool ignoreCond, ignoreCondNext;
        /// <summary>次に攻撃するターンの攻撃に足す量 (〈蓄電器〉)。 / 次のターンから使える分 (〈受け太刀〉)。</summary>
        public int storedAttack, storedAttackNext;
        /// <summary>次のターンに多く引く枚数 (〈配電盤〉)。</summary>
        public int extraDrawNext;
        /// <summary>〈鋳造口〉で増えた出目 (この戦闘中・次のターンから)。</summary>
        public int foundryBonus;

        public TerminalBattleState Clone()
        {
            var c = (TerminalBattleState)MemberwiseClone();
            c.gauge = new Dictionary<string, int>(gauge);
            return c;
        }

        /// <summary>ターンの始めに呼ぶ (持ち越しを今のターンへ移す)。 戻り値 = このターンに多く引く枚数。</summary>
        public int BeginTurn()
        {
            ignoreCond = ignoreCondNext; ignoreCondNext = false;
            storedAttack += storedAttackNext; storedAttackNext = 0;
            int extra = extraDrawNext; extraDrawNext = 0;
            return extra;
        }
    }

    /// <summary>端子の見積もりに要る盤面 (戦闘の今の値)。 本体も BOT もこれを埋めて <see cref="TerminalCards.Resolve"/> へ渡す。</summary>
    public struct TerminalEnv
    {
        public int charge, chargeCap, hp, maxHp, shield;
        public int enemyAttack, enemyPoison, enemyBleed, bursts;
        /// <summary>偏重 (軸の品の数): 心眼・血晶・瘴気・炉温・電流・堅牢。</summary>
        public int axCrit, axBleed, axPoison, axHeat, axCharge, axShield;
        /// <summary>〈研ぎ澄まし〉の恒久の攻撃 (ラン単位)。</summary>
        public int whetBonus;
    }

    /// <summary>1 ターンの端子の結果。 本体がこれを攻撃・防御・充電・戦闘の情報へ反映する。</summary>
    public sealed class TerminalOutcome
    {
        /// <summary>挿し方を直した後の、 出目ごとの行き先 (場の端子の添字・−1 = 挿していない)。</summary>
        public int[] slotOfDie;
        /// <summary>出目ごとの種類 (既存の「配線」の値)。</summary>
        public DiceTerminal[] kindOfDie;
        public int attackAdd, blockAdd, chargeAdd;
        public int attackDice, blockDice, chargeDice, supportDice;
        /// <summary>別枠の与ダメージの掛け算 (1 = なし)。</summary>
        public float damageMul = 1f;
        /// <summary>他の与ダメ% と足す分。</summary>
        public float outgoingPctAdd;
        public float critMultAdd;
        public bool forceCrit;
        /// <summary>このターンの被ダメージに掛ける (1 = なし)。</summary>
        public float takenMul = 1f;
        /// <summary>このターンの敵の攻撃に掛ける (1 = なし)。</summary>
        public float enemyAttackMul = 1f;
        public int heal, selfDamage, shield, chargeSpend;
        public bool chargeFill;
        public int poison, heat, bleedOnHit;
        public int escalationDelay;
        public bool retreatStage, enemyStunNext, ignoreDefense;
        /// <summary>このターンに発動した端子 (場の添字)。</summary>
        public readonly List<int> fired = new List<int>();
        /// <summary>このターンに発動した基本以外の端子の数。</summary>
        public int firedNonBasic;
        /// <summary>〈保持器〉〈写し取り〉〈焼き切り〉の対象 (場の添字・−1 = なし)。</summary>
        public int retainTarget = -1, copyTarget = -1, burnTarget = -1;
        /// <summary>〈発振器〉の連続をこのターンもう数えたか。</summary>
        public bool oscCounted;

        public bool Fired(List<string> board, string id)
        {
            for (int i = 0; i < fired.Count; i++) if (board[fired[i]] == id) return true;
            return false;
        }
    }

    /// <summary>
    /// 端子の山札 (ADR-0014) の定義と、 1 ターンの解決。
    ///
    /// <para>解決は <b>盤面を書き換えない純粋な計算</b> (書き換えるのは渡された <see cref="TerminalBattleState"/> だけ)。
    /// 本体 (CombatManager) は結果を戦闘へ反映し、 BOT は状態の写しを渡して挿し方を比べる ── 同じ式を 2 か所に書かない。</para>
    /// </summary>
    public static class TerminalCards
    {
        /// <summary>[開発中の切り替え] 端子の山札で戦う (既定 true・2026-11-11 に BOT と画面がそろった)。 false = 旧い配線。 旧い経路を消したらこの切り替えも外す。</summary>
        public static bool Enabled = true;

        public const int HandSize = 3;

        // ===== 数値 (すべて仮・未測定) =====
        public const int IgniterAttack = 7, FuseBlock = 8, GroundCutPct = 40, BreakerCutPct = 50, ReliefCutPct = 80;
        public const int DischargeCost = 3, DischargeMul = 3, BreakerCost = 4, DeathwishMul = 3, RelayPerFired = 8, OscPerStreak = 5;
        public const int CapacitorNeed = 15, CapacitorAttack = 40, ArresterNeed = 12, FoundryNeed = 20, CoolantNeed = 24;
        public const float AmplifierPerPip = 0.08f, ResonatorMul = 1.5f, VacuumCritMult = 1.0f;
        public const int CounterEvery = 10; public const float CounterPct = 0.10f;
        public const int TwinShotMul = 3, DetonatorMul = 4, FirstAidMul = 3, HealMul = 2, BatteryBonus = 2, InsulatorBonus = 3;
        public const int HarvesterMaxHp = 2, WhetGain = 1, LootGold = 10;
        public const float SightPerRank = 0.1f, VitalPerRank = 0.05f, BloodTapPct = 0.05f, BigDischargePerRank = 0.10f, ShieldPushPerRank = 0.03f;
        public const int BloodTapPer = 5, PoisonScalePer = 10, PoisonScaleAttack = 3, AfterHeatPerBurst = 2;

        private static TerminalDef D(string id, TerminalKind k, string desc, TerminalCond c = TerminalCond.None, string axis = null)
            => new TerminalDef { id = id, kind = k, cond = c, description = desc, axis = axis, condLabel = CondLabel(c) };

        private static string CondLabel(TerminalCond c)
        {
            switch (c)
            {
                case TerminalCond.AtMost3: return "3以下";
                case TerminalCond.AtLeast5: return "5以上";
                case TerminalCond.AtLeast6: return "6以上";
                case TerminalCond.Even: return "偶数";
                case TerminalCond.Odd: return "奇数";
                case TerminalCond.SameElsewhere: return "同じ目が他に";
                case TerminalCond.PairSame: return "同じ目2個";
                default: return null;
            }
        }

        private static TerminalDef Basic(TerminalDef d) { d.basic = true; return d; }
        private static TerminalDef Rare(TerminalDef d) { d.rare = true; return d; }
        private static TerminalDef Once(TerminalDef d) { d.disposable = true; return d; }
        private static TerminalDef Cost(TerminalDef d, int n) { d.chargeCost = n; return d; }
        private static TerminalDef Cap(TerminalDef d, int n) { d.capacity = n; return d; }

        public static readonly List<TerminalDef> All = Build();

        private static List<TerminalDef> Build()
        {
            const TerminalKind A = TerminalKind.Attack, B = TerminalKind.Block, C = TerminalKind.Charge, S = TerminalKind.Support;
            string ax = ItemSystem.PassiveSkills.AxisRules.Crit, ab = ItemSystem.PassiveSkills.AxisRules.Bleed, ap = ItemSystem.PassiveSkills.AxisRules.Poison,
                   ah = ItemSystem.PassiveSkills.AxisRules.Heat, ac = ItemSystem.PassiveSkills.AxisRules.Charge, asx = ItemSystem.PassiveSkills.AxisRules.Shield;
            var perm = D(TerminalIds.Mains, A, "出目を攻撃に。一度引くとこの戦闘中ずっと場に残る。"); perm.permanent = true;
            return new List<TerminalDef>
            {
                // ── 基本 ──
                Basic(D(TerminalIds.Attack, A, "出目を攻撃に。")),
                Basic(D(TerminalIds.Block,  B, "出目をブロックに。")),
                Basic(D(TerminalIds.Charge, C, "出目を充電に。")),
                // ── コモン ──
                D(TerminalIds.Igniter, A, "攻撃 +" + IgniterAttack + "。", TerminalCond.AtMost3),
                D(TerminalIds.Detector, A, "出目 ×2 を攻撃に。", TerminalCond.Odd),
                Cost(D(TerminalIds.DischargeTube, A, "充電 " + DischargeCost + " を払い、出目 ×" + DischargeMul + " を攻撃に。"), DischargeCost),
                D(TerminalIds.Deathwish, A, "出目ぶん HP を払い、出目 ×" + DeathwishMul + " を攻撃に。"),
                D(TerminalIds.Relay, A, "出目を攻撃に。このターンに発動した基本以外の端子 1 つにつき攻撃 +" + RelayPerFired + "。"),
                D(TerminalIds.Grinder, A, "出目を攻撃に。他の出目すべて +1。"),
                D(TerminalIds.Oscillator, A, "出目を攻撃に。前にここへ挿した目と同じなら攻撃 +" + OscPerStreak + " × 連続回数。"),
                Cap(D(TerminalIds.Twin, A, "出目を攻撃に。2 個まで挿せる。"), 2),
                D(TerminalIds.Fuse, B, "ブロック +" + FuseBlock + "。", TerminalCond.AtMost3),
                D(TerminalIds.Ground, B, "このターンの被ダメージ −" + GroundCutPct + "%。", TerminalCond.AtMost3),
                D(TerminalIds.Insulator, B, "シールド +出目 +" + InsulatorBonus + "。", TerminalCond.Even),
                D(TerminalIds.Parry, B, "出目をブロックに。防いだ量を、次に攻撃するターンの攻撃に足す。"),
                Cost(D(TerminalIds.Breaker, B, "充電 " + BreakerCost + " を払い、このターンの敵の攻撃 −" + BreakerCutPct + "%。"), BreakerCost),
                D(TerminalIds.Rectifier, C, "充電 +出目 ×2。", TerminalCond.Even),
                D(TerminalIds.Battery, C, "充電 +出目 +" + BatteryBonus + "。"),
                D(TerminalIds.Heal, S, "HP +出目 ×" + HealMul + "。", TerminalCond.AtMost3),
                D(TerminalIds.Capacitor, S, "出目を溜める。" + CapacitorNeed + " で次の攻撃 +" + CapacitorAttack + "。"),
                D(TerminalIds.Arrester, S, "出目を溜める。" + ArresterNeed + " でそのターンの被ダメージ半分。"),
                D(TerminalIds.Transformer, S, "攻撃の端子に挿した出目を 2 倍。", TerminalCond.AtLeast5),
                D(TerminalIds.Switchboard, S, "次のターン、端子を 1 枚多く引く。"),
                // ── レア ──
                Rare(D(TerminalIds.Amplifier, S, "出目 1 につき与ダメージ +" + (int)(AmplifierPerPip * 100) + "% (別枠)。", TerminalCond.AtLeast5)),
                Rare(D(TerminalIds.VacuumTube, S, "このターンの会心倍率 +" + VacuumCritMult.ToString("0.0") + "。", TerminalCond.AtLeast6)),
                Rare(D(TerminalIds.Resonator, S, "このターンの与ダメージ ×" + ResonatorMul.ToString("0.0") + " (別枠)。", TerminalCond.SameElsewhere)),
                Rare(D(TerminalIds.Foundry, S, "出目を溜める。" + FoundryNeed + " ごとにこの戦闘中の全部の出目 +1。")),
                Rare(D(TerminalIds.Coolant, S, "出目を溜める。" + CoolantNeed + " ごとにエスカレーションを 1 ターン遅らせる。")),
                Rare(D(TerminalIds.Counter, A, "出目を攻撃に。基本以外の発動が累計 " + CounterEvery + " 回になるごとに、この戦闘中の与ダメージ +" + (int)(CounterPct * 100) + "%。")),
                Rare(D(TerminalIds.Preheater, B, "出目をブロックに。次のターン、端子の条件を無視する。")),
                Rare(perm),
                Rare(Cap(D(TerminalIds.TwinShot, A, "同じ目 2 個で、出目の合計 ×" + TwinShotMul + " を攻撃に。", TerminalCond.PairSame), 2)),
                Rare(D(TerminalIds.Harvester, A, "出目を攻撃に。このターンに敵を倒すと最大 HP +" + HarvesterMaxHp + " (恒久)。")),
                Rare(D(TerminalIds.Whet, A, "出目を攻撃に。このターンに敵を倒すと、この端子の攻撃が恒久に +" + WhetGain + "。")),
                Rare(D(TerminalIds.LootBox, A, "出目を攻撃に。このターンに敵を倒すとゴールド +" + LootGold + "。")),
                Rare(D(TerminalIds.Stripper, S, "このターンに敵を倒すと、戦闘後の端子の選択肢が 1 つ増える。")),
                Rare(D(TerminalIds.Doubler, S, "このターン、他の端子の効果を 2 回発動させる。", TerminalCond.AtLeast6)),
                Rare(D(TerminalIds.Retainer, S, "挿さずに空けた手札の端子を、次のターンも手札に残す。")),
                Rare(Once(D(TerminalIds.StopSignal, S, "敵は次のターン行動しない。"))),
                Rare(D(TerminalIds.Fork, A, "出目を攻撃とブロックの両方に入れる。")),
                // ── 軸 ──
                D(TerminalIds.Sight, A, "出目を攻撃に。このターンの会心倍率 +心眼 ×" + SightPerRank.ToString("0.0#") + "。", TerminalCond.None, ax),
                D(TerminalIds.VitalRead, S, "このターンの攻撃は必ず会心。会心倍率 +心眼 ×" + VitalPerRank.ToString("0.0#") + "。", TerminalCond.Odd, ax),
                D(TerminalIds.Barb, A, "出目を攻撃に。攻撃が通れば出血 +血晶。", TerminalCond.None, ab),
                D(TerminalIds.BloodTap, S, "敵の出血 " + BloodTapPer + " につき、このターンの与ダメージ +" + (int)(BloodTapPct * 100) + "%。", TerminalCond.AtMost3, ab),
                D(TerminalIds.Mister, S, "敵に毒 +出目 +瘴気。", TerminalCond.None, ap),
                D(TerminalIds.PoisonScale, A, "出目を攻撃に。敵の毒 " + PoisonScalePer + " につき攻撃 +" + PoisonScaleAttack + "。", TerminalCond.None, ap),
                D(TerminalIds.Stoker, S, "臨界 +出目 +炉温。", TerminalCond.None, ah),
                D(TerminalIds.AfterHeat, A, "出目を攻撃に。この戦闘の爆発 1 回につき攻撃 +" + AfterHeatPerBurst + "。", TerminalCond.None, ah),
                D(TerminalIds.Leak, C, "充電 +出目 +電流。", TerminalCond.None, ac),
                D(TerminalIds.BigDischarge, A, "充電をすべて払い、払った充電を攻撃に足す (電流 1 につき +" + (int)(BigDischargePerRank * 100) + "%)。", TerminalCond.None, ac),
                D(TerminalIds.Patch, B, "出目をブロックに。シールド +堅牢。", TerminalCond.None, asx),
                D(TerminalIds.ShieldPush, A, "出目を攻撃に。今のシールドの 堅牢 ×" + (int)(ShieldPushPerRank * 100) + "% を攻撃に足す。", TerminalCond.None, asx),
                // ── 使い捨て ──
                Once(D(TerminalIds.Detonator, A, "出目 ×" + DetonatorMul + " を攻撃に。")),
                Once(D(TerminalIds.Piercer, A, "出目を攻撃に。このターン敵の防御を無視。")),
                Once(D(TerminalIds.ReliefValve, B, "このターンの被ダメージ −" + ReliefCutPct + "%。")),
                Once(D(TerminalIds.FirstAid, S, "HP +出目 ×" + FirstAidMul + "。")),
                Once(D(TerminalIds.FullCharge, C, "充電を上限まで満たす。")),
                Once(D(TerminalIds.CoolShot, S, "エスカレーションの段階を 1 つ戻す。")),
                Once(D(TerminalIds.Copier, S, "挿さずに空けた手札の端子を 1 枚、この戦闘中だけ複製して山札に加える。")),
                Once(D(TerminalIds.BurnOut, S, "挿さずに空けた手札の端子を 1 枚、この戦闘中だけ山札から取り除く。")),
            };
        }

        private static readonly Dictionary<string, TerminalDef> _byId = Index();
        private static Dictionary<string, TerminalDef> Index()
        {
            var d = new Dictionary<string, TerminalDef>();
            foreach (var t in All) d[t.id] = t;
            return d;
        }

        public static TerminalDef Get(string id)
            => id != null && _byId.TryGetValue(id, out var d) ? d : null;

        /// <summary>開始の山札: 攻撃 2・防御 2・充電 1。</summary>
        public static List<string> StartingDeck()
            => new List<string> { TerminalIds.Attack, TerminalIds.Attack, TerminalIds.Block, TerminalIds.Block, TerminalIds.Charge };

        public static DiceTerminal LegacyKind(TerminalKind k)
            => k == TerminalKind.Attack ? DiceTerminal.Attack : k == TerminalKind.Block ? DiceTerminal.Block
             : k == TerminalKind.Charge ? DiceTerminal.Charge : DiceTerminal.Special;

        /// <summary>出目 v が条件 c に合うか (他の出目との関係を見る条件はここでは true)。</summary>
        public static bool CondOk(TerminalCond c, int v)
        {
            switch (c)
            {
                case TerminalCond.AtMost3: return v <= 3;
                case TerminalCond.AtLeast5: return v >= 5;
                case TerminalCond.AtLeast6: return v >= 6;
                case TerminalCond.Even: return (v & 1) == 0;
                case TerminalCond.Odd: return (v & 1) == 1;
                default: return true;
            }
        }

        // =====================================================================
        //  1 ターンの解決
        // =====================================================================

        /// <summary>
        /// 場の端子 (常設 + 手札) へ出目を挿した結果を計算する。
        /// </summary>
        /// <param name="board">場の端子の名前 (添字 = 場の位置)。</param>
        /// <param name="wantSlot">出目ごとの挿し先 (場の添字・−1 = 挿さない)。 規則に合わない挿し方は直す (挿さなかった扱い)。</param>
        /// <param name="dice">出目。 書き換えない。</param>
        /// <param name="state">戦闘の間の端子の状態。 <b>書き換える</b> (見積もりなら写しを渡す)。</param>
        public static TerminalOutcome Resolve(List<string> board, int[] wantSlot, int[] dice, TerminalEnv env, TerminalBattleState state)
        {
            int n = dice.Length, m = board.Count;
            var o = new TerminalOutcome { slotOfDie = new int[n], kindOfDie = new DiceTerminal[n] };
            var v = new int[n];
            for (int i = 0; i < n; i++) { v[i] = dice[i]; o.slotOfDie[i] = wantSlot != null && i < wantSlot.Length && wantSlot[i] >= 0 && wantSlot[i] < m ? wantSlot[i] : -1; }
            var defs = new TerminalDef[m];
            for (int s = 0; s < m; s++) defs[s] = Get(board[s]);

            // ── 0. 挿せる数を超えた分を外す ──
            var cnt = new int[m];
            for (int i = 0; i < n; i++)
            {
                int s = o.slotOfDie[i]; if (s < 0) continue;
                if (defs[s] == null || cnt[s] >= defs[s].capacity) { o.slotOfDie[i] = -1; continue; }
                cnt[s]++;
            }

            // ── 1. 〈研磨盤〉: 他の出目すべて +1 (他の端子より先に効く) ──
            for (int i = 0; i < n; i++)
                if (o.slotOfDie[i] >= 0 && board[o.slotOfDie[i]] == TerminalIds.Grinder)
                    for (int j = 0; j < n; j++) if (j != i) v[j]++;

            // ── 2. 条件 ──
            bool ign = state.ignoreCond;
            for (int pass = 0; pass < 2; pass++)      // 2 回: 〈共振器〉は「他の端子に同じ目」なので、 外れた出目を見てもう一度
                for (int i = 0; i < n; i++)
                {
                    int s = o.slotOfDie[i]; if (s < 0) continue;
                    var d = defs[s]; bool ok = true;
                    if (!ign)
                    {
                        if (d.cond == TerminalCond.SameElsewhere)
                        {
                            ok = false;
                            for (int j = 0; j < n; j++) if (j != i && o.slotOfDie[j] >= 0 && o.slotOfDie[j] != s && v[j] == v[i]) ok = true;
                        }
                        else if (d.cond == TerminalCond.PairSame)
                        {
                            int same = 0, here = 0;
                            for (int j = 0; j < n; j++) if (o.slotOfDie[j] == s) { here++; if (v[j] == v[i]) same++; }
                            ok = here == 2 && same == 2;
                        }
                        else ok = CondOk(d.cond, v[i]);
                    }
                    else if (d.cond == TerminalCond.PairSame)      // 条件無視でも 2 個は要る
                    {
                        int here = 0; for (int j = 0; j < n; j++) if (o.slotOfDie[j] == s) here++;
                        ok = here == 2;
                    }
                    if (!ok) o.slotOfDie[i] = -1;
                }

            // ── 3. 充電の支払い (場の順)。 足りなければ不発 ──
            int charge = env.charge;
            var paid = new bool[m];
            for (int s = 0; s < m; s++)
            {
                if (defs[s] == null || !Plugged(o.slotOfDie, s)) continue;
                int cost = defs[s].chargeCost;
                if (board[s] == TerminalIds.BigDischarge) { if (charge <= 0) { Unplug(o.slotOfDie, s); continue; } paid[s] = true; continue; }      // 全部払う (量は効果の所で)
                if (cost <= 0) { paid[s] = true; continue; }
                if (charge < cost) { Unplug(o.slotOfDie, s); continue; }
                charge -= cost; o.chargeSpend += cost; paid[s] = true;
            }

            // ── 4. 空けた手札の端子 (〈保持器〉〈写し取り〉〈焼き切り〉の対象) ──
            int Empty(int self)
            {
                for (int s = 0; s < m; s++)
                    if (s != self && defs[s] != null && !defs[s].permanent && !Plugged(o.slotOfDie, s)) return s;
                return -1;
            }

            // ── 5. 効果 ──
            bool doubler = false;
            for (int s = 0; s < m; s++) if (board[s] == TerminalIds.Doubler && Plugged(o.slotOfDie, s)) doubler = true;
            int attackPips = 0;      // 攻撃の種類の端子に挿した出目 (〈変圧器〉が 2 倍にする分)
            for (int s = 0; s < m; s++)
            {
                if (defs[s] == null || !Plugged(o.slotOfDie, s)) continue;
                var d = defs[s]; string id = board[s];
                int sum = 0, num = 0;
                for (int i = 0; i < n; i++) if (o.slotOfDie[i] == s) { sum += v[i]; num++; }
                o.fired.Add(s);
                if (!d.basic) o.firedNonBasic++;
                switch (d.kind)
                {
                    case TerminalKind.Attack: o.attackDice += num; attackPips += sum; break;
                    case TerminalKind.Block: o.blockDice += num; break;
                    case TerminalKind.Charge: o.chargeDice += num; break;
                    default: o.supportDice += num; break;
                }
                int times = doubler && id != TerminalIds.Doubler ? 2 : 1;
                for (int t = 0; t < times; t++) Effect(id, s, sum, env, state, o, ref charge, Empty);
            }

            // ── 6. 他の端子を見る効果 (全部の発動が決まった後) ──
            for (int s = 0; s < m; s++)
            {
                if (defs[s] == null || !Plugged(o.slotOfDie, s)) continue;
                int times = doubler ? 2 : 1;
                if (board[s] == TerminalIds.Relay) o.attackAdd += RelayPerFired * (o.firedNonBasic - 1) * times;
                else if (board[s] == TerminalIds.Transformer) o.attackAdd += attackPips * times;
            }
            // 〈計数器〉: 基本以外の発動の累計 (付けていなくても数える・積むのは持っている時だけ = 山札にあれば効く)
            state.firedTotal += o.firedNonBasic;
            // 次に攻撃するターンの攻撃に足す分
            if (o.attackDice > 0 && state.storedAttack > 0) { o.attackAdd += state.storedAttack; state.storedAttack = 0; }

            for (int i = 0; i < n; i++)
                o.kindOfDie[i] = o.slotOfDie[i] >= 0 ? LegacyKind(defs[o.slotOfDie[i]].kind) : DiceTerminal.None;
            return o;
        }

        private static bool Plugged(int[] slotOfDie, int s)
        {
            for (int i = 0; i < slotOfDie.Length; i++) if (slotOfDie[i] == s) return true;
            return false;
        }
        private static void Unplug(int[] slotOfDie, int s)
        {
            for (int i = 0; i < slotOfDie.Length; i++) if (slotOfDie[i] == s) slotOfDie[i] = -1;
        }
        private static int Gauge(TerminalBattleState st, string id, int add, int need)
        {
            st.gauge.TryGetValue(id, out int g); g += add;
            int fired = 0;
            while (g >= need) { g -= need; fired++; }
            st.gauge[id] = g;
            return fired;
        }

        /// <summary>端子 1 つの効果 (sum = 挿した出目の合計)。</summary>
        private static void Effect(string id, int slot, int sum, TerminalEnv env, TerminalBattleState st, TerminalOutcome o, ref int charge, Func<int, int> empty)
        {
            switch (id)
            {
                // 基本
                case TerminalIds.Attack: o.attackAdd += sum; break;
                case TerminalIds.Block: o.blockAdd += sum; break;
                case TerminalIds.Charge: o.chargeAdd += sum; break;
                // コモン
                case TerminalIds.Igniter: o.attackAdd += IgniterAttack; break;
                case TerminalIds.Detector: o.attackAdd += sum * 2; break;
                case TerminalIds.DischargeTube: o.attackAdd += sum * DischargeMul; break;
                case TerminalIds.Deathwish:
                {
                    int self = Math.Min(sum, Math.Max(0, env.hp - 1 - o.selfDamage));
                    o.selfDamage += self; o.attackAdd += sum * DeathwishMul; break;
                }
                case TerminalIds.Relay: o.attackAdd += sum; break;                 // +8 × 他の発動は後で
                case TerminalIds.Grinder: o.attackAdd += sum; break;               // 他の出目 +1 は先に済み
                case TerminalIds.Oscillator:
                    // 連続は 1 ターンに 1 つだけ進める (〈倍電圧〉で効果が 2 回走っても、 2 回目は同じ連続回数で足すだけ)
                    if (!o.oscCounted)
                    {
                        if (st.oscLast == sum) st.oscStreak++; else st.oscStreak = 0;
                        st.oscLast = sum; o.oscCounted = true;
                    }
                    o.attackAdd += sum + OscPerStreak * st.oscStreak; break;
                case TerminalIds.Twin: o.attackAdd += sum; break;
                case TerminalIds.Fuse: o.blockAdd += FuseBlock; break;
                case TerminalIds.Ground: o.takenMul *= 1f - GroundCutPct / 100f; break;
                case TerminalIds.Insulator: o.shield += sum + InsulatorBonus; break;
                case TerminalIds.Parry: o.blockAdd += sum; st.storedAttackNext += Math.Min(sum, Math.Max(0, env.enemyAttack)); break;
                case TerminalIds.Breaker: o.enemyAttackMul *= 1f - BreakerCutPct / 100f; break;
                case TerminalIds.Rectifier: o.chargeAdd += sum * 2; break;
                case TerminalIds.Battery: o.chargeAdd += sum + BatteryBonus; break;
                case TerminalIds.Heal: o.heal += sum * HealMul; break;
                case TerminalIds.Capacitor: st.storedAttack += CapacitorAttack * Gauge(st, id, sum, CapacitorNeed); break;
                case TerminalIds.Arrester: if (Gauge(st, id, sum, ArresterNeed) > 0) o.takenMul *= 0.5f; break;
                case TerminalIds.Transformer: break;                               // 後で
                case TerminalIds.Switchboard: st.extraDrawNext++; break;
                // レア
                case TerminalIds.Amplifier: o.damageMul *= 1f + AmplifierPerPip * sum; break;
                case TerminalIds.VacuumTube: o.critMultAdd += VacuumCritMult; break;
                case TerminalIds.Resonator: o.damageMul *= ResonatorMul; break;
                case TerminalIds.Foundry: st.foundryBonus += Gauge(st, id, sum, FoundryNeed); break;
                case TerminalIds.Coolant: o.escalationDelay += Gauge(st, id, sum, CoolantNeed); break;
                case TerminalIds.Counter: o.attackAdd += sum; break;
                case TerminalIds.Preheater: o.blockAdd += sum; st.ignoreCondNext = true; break;
                case TerminalIds.Mains: o.attackAdd += sum; break;
                case TerminalIds.TwinShot: o.attackAdd += sum * TwinShotMul; break;
                case TerminalIds.Harvester: o.attackAdd += sum; break;
                case TerminalIds.Whet: o.attackAdd += sum + env.whetBonus; break;
                case TerminalIds.LootBox: o.attackAdd += sum; break;
                case TerminalIds.Stripper: break;
                case TerminalIds.Doubler: break;
                case TerminalIds.Retainer: o.retainTarget = empty(slot); break;
                case TerminalIds.StopSignal: o.enemyStunNext = true; break;
                case TerminalIds.Fork: o.attackAdd += sum; o.blockAdd += sum; break;
                // 軸
                case TerminalIds.Sight: o.attackAdd += sum; o.critMultAdd += SightPerRank * env.axCrit; break;
                case TerminalIds.VitalRead: o.forceCrit = true; o.critMultAdd += VitalPerRank * env.axCrit; break;
                case TerminalIds.Barb: o.attackAdd += sum; o.bleedOnHit += env.axBleed; break;
                case TerminalIds.BloodTap: o.outgoingPctAdd += BloodTapPct * (env.enemyBleed / BloodTapPer); break;
                case TerminalIds.Mister: o.poison += sum + env.axPoison; break;
                case TerminalIds.PoisonScale: o.attackAdd += sum + PoisonScaleAttack * (env.enemyPoison / PoisonScalePer); break;
                case TerminalIds.Stoker: o.heat += sum + env.axHeat; break;
                case TerminalIds.AfterHeat: o.attackAdd += sum + AfterHeatPerBurst * env.bursts; break;
                case TerminalIds.Leak: o.chargeAdd += sum + env.axCharge; break;
                case TerminalIds.BigDischarge:
                    o.attackAdd += (int)Math.Floor(charge * (1f + BigDischargePerRank * env.axCharge));
                    o.chargeSpend += charge; charge = 0; break;
                case TerminalIds.Patch: o.blockAdd += sum; o.shield += env.axShield; break;
                case TerminalIds.ShieldPush: o.attackAdd += sum + (int)Math.Floor(env.shield * ShieldPushPerRank * env.axShield); break;
                // 使い捨て
                case TerminalIds.Detonator: o.attackAdd += sum * DetonatorMul; break;
                case TerminalIds.Piercer: o.attackAdd += sum; o.ignoreDefense = true; break;
                case TerminalIds.ReliefValve: o.takenMul *= 1f - ReliefCutPct / 100f; break;
                case TerminalIds.FirstAid: o.heal += sum * FirstAidMul; break;
                case TerminalIds.FullCharge: o.chargeFill = true; break;
                case TerminalIds.CoolShot: o.retreatStage = true; break;
                case TerminalIds.Copier: o.copyTarget = empty(slot); break;
                case TerminalIds.BurnOut: o.burnTarget = empty(slot); break;
            }
        }
    }

    /// <summary>
    /// 戦闘の間の山札 (山・捨て札・手札・場に残る端子・この戦闘から外れた端子)。 ADR-0014。
    /// </summary>
    public sealed class TerminalDeckState
    {
        public readonly List<string> draw = new List<string>();
        public readonly List<string> discard = new List<string>();
        /// <summary>このターンの手札。</summary>
        public readonly List<string> hand = new List<string>();
        /// <summary>常設: 場に残っている端子。</summary>
        public readonly List<string> permanent = new List<string>();
        /// <summary>この戦闘から外れた端子 (使い捨てを使った・焼き切った)。</summary>
        public readonly List<string> exiled = new List<string>();
        /// <summary>場 = 常設 + 手札 (この順)。 出目の挿し先はこの添字。</summary>
        public readonly List<string> board = new List<string>();
        public readonly TerminalBattleState state = new TerminalBattleState();

        /// <summary>戦闘の始め: ランの山札を写して切る。</summary>
        public void BeginBattle(List<string> runDeck)
        {
            draw.Clear(); discard.Clear(); hand.Clear(); permanent.Clear(); exiled.Clear(); board.Clear();
            if (runDeck != null) draw.AddRange(runDeck);
            Shuffle(draw);
        }

        private static void Shuffle(List<string> l)
        {
            for (int i = l.Count - 1; i > 0; i--)
            {
                int j = GameLoop.GameRng.RangeAuto("terminal.shuffle", 0, i + 1);
                var t = l[i]; l[i] = l[j]; l[j] = t;
            }
        }

        private string DrawOne()
        {
            if (draw.Count == 0)
            {
                if (discard.Count == 0) return null;
                draw.AddRange(discard); discard.Clear(); Shuffle(draw);
            }
            var id = draw[draw.Count - 1]; draw.RemoveAt(draw.Count - 1);
            return id;
        }

        /// <summary>ターンの始め: 前の手札を捨て (残す 1 枚は残し)、 手札が size 枚になるまで引く。 常設は場へ出して引き直す。</summary>
        public void DrawTurn(int size, string retained = null)
        {
            bool kept = false;
            foreach (var id in hand)
            {
                if (!kept && retained != null && id == retained) { kept = true; continue; }
                discard.Add(id);
            }
            hand.Clear();
            if (kept) hand.Add(retained);
            int guard = 0;
            while (hand.Count < size && guard++ < 64)
            {
                var id = DrawOne();
                if (id == null) break;
                var d = TerminalCards.Get(id);
                if (d != null && d.permanent) { permanent.Add(id); continue; }
                hand.Add(id);
            }
            board.Clear(); board.AddRange(permanent); board.AddRange(hand);
        }

        /// <summary>ターンの終わり: 結果を山札へ反映する (使い捨て・写し取り・焼き切り)。 戻り値 = 次のターンも手札に残す端子 (無ければ null)。</summary>
        public string FinishTurn(TerminalOutcome o)
        {
            string retain = o.retainTarget >= 0 && o.retainTarget < board.Count ? board[o.retainTarget] : null;
            if (o.copyTarget >= 0 && o.copyTarget < board.Count) discard.Add(board[o.copyTarget]);
            string burn = o.burnTarget >= 0 && o.burnTarget < board.Count ? board[o.burnTarget] : null;
            // 使った使い捨てを手札から外す (この戦闘には戻らない)
            foreach (int s in o.fired)
            {
                var d = TerminalCards.Get(board[s]);
                if (d != null && d.disposable && hand.Remove(board[s])) exiled.Add(board[s]);
            }
            if (burn != null && hand.Remove(burn)) exiled.Add(burn);
            return retain;
        }

        /// <summary>山札の残り (山 + 捨て札 + 手札 + 場)。 画面用。</summary>
        public int Remaining => draw.Count + discard.Count + hand.Count + permanent.Count;
    }
}
