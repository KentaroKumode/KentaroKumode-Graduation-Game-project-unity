using System;
using System.Collections.Generic;

namespace CombatSystem
{
    /// <summary>手札の規則 (2026-11-13・GAME.md §6-4)。 値の並びを変えない (測定の口が番号でも受ける)。</summary>
    public enum HandCarryMode
    {
        /// <summary>旧い規則: ターンの頭に前の手札を全部捨てる (止まっていた端子と、 旧い〈保持器〉の 1 枚だけ残る)。</summary>
        Off = 0,
        /// <summary>挿さなかった端子は全部残る (2026-11-13 の最初の形・居座りで選べる端子が減ったので取り下げ・測定用に残す)。</summary>
        All = 1,
        /// <summary>既定: 挿さなかった端子のうち、 残す印を付けた端子だけ残る (何も印を付けなければ旧い規則と同じ)。</summary>
        Pick = 2,
    }

    /// <summary>端子の種類 (ADR-0014)。 「攻撃に挿した個数」などを読む既存の品・武器・敵の規則は、 この種類で数える。</summary>
    public enum TerminalKind { Attack, Block, Charge, Support }

    /// <summary>出目の条件。 合わない出目は挿せない (不発)。</summary>
    public enum TerminalCond
    {
        None,
        AtMost3,        // 3 以下
        AtMost4,        // 4 以下 (強化した〈牽制斬り〉〈血の渇き〉・2026-11-20)
        AtLeast5,       // 5 以上
        AtLeast6,       // 6 以上
        Even,           // 偶数
        Odd,            // 奇数
        SameElsewhere,  // 同じ目が他の端子に挿さっている
        PairSame,       // 同じ目 2 個を挿す (capacity 2・2 個そろって初めて発動)
    }

    /// <summary>采配 (山札の軸) の品のうち、 端子の解決・山札の出し入れを変える品 (2026-11-20・段 1-e・item-overhaul-spec §4 / §12-4)。
    /// 戦闘の始めに所持品から <see cref="TerminalBattleState.deckItems"/> へ入れる (CombatManager.BeginTerminalDeck・<see cref="TerminalCards.DeckItemsOf"/>)。
    /// 効くのは <see cref="TerminalCards.Resolve"/> と山札の出し入れ (<see cref="TerminalDeckState.BeforeShuffle"/> / <see cref="TerminalDeckState.DisposeFired"/>) の中だけ ──
    /// 本体も BOT の写し (Clone) も同じ旗を読むので、 見積もりと動きが自動で一致する。</summary>
    [Flags]
    public enum DeckItems
    {
        None = 0,
        Clasp = 1 << 0,        // 留め金の栞
        AuxGear = 1 << 1,      // 補助歯車
        IdleWeight = 1 << 2,   // 空回りの錘
        Rewind = 1 << 3,       // 早回しのゼンマイ
        Switchboard = 1 << 4,  // 仕込みの配電盤
        Mold = 1 << 5,         // 型抜きの金型
        Crucible = 1 << 6,     // 再利用のるつぼ
        Blueprint = 1 << 7,    // 無銘の設計図
        Seamless = 1 << 8,     // 継ぎ目のない導線
        Folded = 1 << 9,       // 折り目のついた札
        Bag = 1 << 10,         // 解体屋の袋
        Workbench = 1 << 11,   // 常設の作業台
        Ash = 1 << 12,         // 灰から組む機関
        Engine = 1 << 13,      // 永久機関の主軸
    }

    /// <summary>賽目 (役の軸) の品のうち、 端子の解決 (<see cref="TerminalCards.Resolve"/>) の中で効く物 (2026-11-21・段 1-f・item-overhaul-spec §5 / §12-2)。
    /// 戦闘の始めに所持品から <see cref="TerminalBattleState.roleItems"/> へ入れる (CombatManager.BeginTerminalDeck・<see cref="TerminalCards.RoleItemsOf"/>)。
    /// 役は振った 3 つの目で決まるので、 Resolve が同じ判定 (<see cref="TerminalCards.RoleOf"/>) で立つ役を出して効かせる (本体・画面の見込み・BOT の配線と先読みが一致する)。</summary>
    [Flags]
    public enum RoleItems
    {
        None = 0,
        WoodTag = 1 << 0,      // 見切りの木札
        NotchBoard = 1 << 1,   // 符丁の刻み板
    }

    /// <summary>端子の名前 (id = 表示名・品と同じ規則)。 **文字列を直書きしない** ── ここを経由する。</summary>
    public static class TerminalIds
    {
        // 基本
        public const string Attack = "攻撃", Block = "防御", Charge = "充電";
        /// <summary>時計の賽 (ADR-0015) の常設の端子。 山札には入らない (時計の賽を持つ戦闘の始めに場へ置く・報酬にも店にも出ない)。</summary>
        public const string Clock = "時計";
        // コモン
        public const string Igniter = "牽制斬り", Detector = "片刃斬り", DischargeTube = "雷撃", Deathwish = "諸刃の剣", Relay = "連係斬り",
            Grinder = "号令の剣", Oscillator = "反復突き", Twin = "二刀流", Fuse = "小盾", Ground = "受け流し", Insulator = "二重障壁",
            Parry = "後の先", Breaker = "大結界", Rectifier = "倍加の雷", Battery = "雷溜め", Heal = "包帯", Capacitor = "力溜め",
            Arrester = "防御陣", Transformer = "剛腕", Switchboard = "先読み";
        // レア
        public const string Amplifier = "激昂", VacuumTube = "致命の一撃", Resonator = "共鳴", Foundry = "打ち直し", Coolant = "鎮静香",
            Counter = "柔軟な戦略", Preheater = "無形の構え", Mains = "千刃", TwinShot = "十字斬り", Harvester = "魂狩り", Whet = "処刑の刃",
            LootBox = "賞金狩り", Stripper = "剥ぎ取り", Doubler = "残響", Retainer = "有効活用" /* 2026-11-13 に〈保持器〉から改名 */, StopSignal = "石化の瞳", Fork = "攻防一体";
        // 軸
        public const string Sight = "一閃", VitalRead = "弱点看破", Barb = "鉤爪", BloodTap = "血の渇き", Mister = "毒霧",
            PoisonScale = "腐食の刃", Stoker = "火種", AfterHeat = "爆炎斬り", Leak = "招雷", BigDischarge = "裁きの雷",
            Patch = "装甲板", ShieldPush = "重装突撃";
        // 使い捨て
        public const string Detonator = "火薬樽", Piercer = "貫通弾", ReliefValve = "身代わり札", FirstAid = "応急箱", FullCharge = "大導電",
            CoolShot = "氷結瓶", Copier = "影写し", BurnOut = "火葬の剣";
        // 采配と共通の端子 (2026-11-20・段 1-e・item-overhaul-spec §8 / §12-4・名前は仮)。 〈焼却斬り〉だけ山札の軸の端子
        public const string BurnSlash = "焼却斬り", Reload = "緊急再装填", OneCut = "一刀両断", Gear = "歯車の軸", Bell = "巡りの鐘" /* 設計書の〈残響の鐘〉(〈残響〉と紛れるので仮に改名) */,
            Mikiri = "見切りの構え", Crush = "崩し斬り";
        // 役の軸の端子 (2026-11-21・段 1-f・item-overhaul-spec §8-1 / §12-4・名前は仮)
        public const string DiceTable = "賽振り台", RoleDrop = "役落とし";

        /// <summary>強化した端子の名前の印 (全角の「＋」・2026-11-20)。</summary>
        public const string UpgradeMark = "＋";
        /// <summary>強化した端子の名前 (例: 「攻撃」→「攻撃＋」)。 強化した端子の名前を作るのはここだけ。
        /// 強化した端子から基の端子を引くのは <see cref="TerminalCards.BaseOf"/> (定義が持つ <see cref="TerminalDef.baseId"/>・名前の綴りは切らない)。</summary>
        public static string Upgraded(string id) => id + UpgradeMark;
    }

    /// <summary>端子 1 種の定義。</summary>
    public sealed class TerminalDef
    {
        public string id;
        /// <summary>基の端子の名前 (素の端子は id と同じ・強化した端子は強化する前の名前)。 効果の分かれ道はこの名前で見る。</summary>
        public string baseId;
        /// <summary>強化した端子 (休憩で得る・名前の後ろに「＋」・<see cref="TerminalIds.Upgraded"/>・2026-11-20)。 店・報酬・付与の抽選には出ない。 強化した端子はもう強化できない。</summary>
        public bool upgraded;
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
        /// <summary>使い捨て: 使うと、 その戦闘中は山札に戻らない (次の戦闘ではまた山札に入っている)。 使い捨ての決まりはこれだけ (2026-11-14・作者の決定)。</summary>
        public bool disposable;
        /// <summary>[測定用] 測定の口 <see cref="GameLoop.GameHooks.TerminalFirstAidLeavesRunDeck"/> が立っている時だけ、 使うとそのランの山札からも抜ける
        /// (〈応急箱〉の 2026-11-12〜11-14 の決まり・2026-11-14 に取り下げ = 既定では使い捨てと同じ)。 <see cref="disposable"/> と組む。
        /// 抜くのは本体の <c>CombatManager.FinishTerminalTurn</c>。 山札が <see cref="TerminalCards.MinDeckSize"/> 枚以下なら抜かない (店・休憩の抜き方と同じ下限)。
        /// 判定は <see cref="TerminalCards.LeavesRunDeck"/> を通す。</summary>
        public bool leavesRunDeck;
        /// <summary>常設: 一度引くとその戦闘中ずっと場に残り、 手札の枠を使わない。</summary>
        public bool permanent;
        /// <summary>軸 (AxisRules の名前)。 null = 軸なし。 強さがその軸の偏重で決まり、 報酬に出る確率も偏重で寄せる。</summary>
        public string axis;
        public string description;
        /// <summary>条件の短い表記 (画面用)。 条件なしは null。</summary>
        public string condLabel;

        public TerminalDef Copy() => (TerminalDef)MemberwiseClone();
    }

    /// <summary>戦闘の間に端子が覚えている値 (溜め・連続・持ち越し)。 BOT の見積もりは <see cref="Clone"/> した写しに掛ける。</summary>
    public sealed class TerminalBattleState
    {
        /// <summary>溜める端子の溜まり (端子の名前 → 量)。</summary>
        public Dictionary<string, int> gauge = new Dictionary<string, int>();
        /// <summary>〈反復突き〉: 前に挿した目 / 連続回数。 / 強化した〈反復突き＋〉の分 (名前ごとに数える・2026-11-20)。</summary>
        public int oscLast, oscStreak, oscLastUp, oscStreakUp;
        /// <summary>基本以外の端子の発動の累計 (〈柔軟な戦略〉)。</summary>
        public int firedTotal;
        /// <summary>このターン / 次のターン、 出目の条件を無視する (〈無形の構え〉・このターンの分は戦術消耗品〈無銘の潤滑油〉も立てる)。</summary>
        public bool ignoreCond, ignoreCondNext;
        /// <summary>このターン、 挿した端子がすべて 2 回働く (戦術消耗品〈倍化の共鳴玉〉・2026-11-20・〈残響〉と同じ倍化)。 <see cref="BeginTurn"/> で戻す。
        /// 盤面 (TerminalEnv) でなく状態に持つのは、 判断の間 (配線の画面・BOT の写し) に使っても、 本体・画面・BOT が同じ値を読むため。</summary>
        public bool doubleAll;
        /// <summary>次に攻撃するターンの攻撃に足す量 (〈力溜め〉)。 / 次のターンから使える分 (〈後の先〉)。</summary>
        public int storedAttack, storedAttackNext;
        /// <summary>次のターンに多く引く枚数 (〈先読み〉)。</summary>
        public int extraDrawNext;
        /// <summary>〈打ち直し〉で増えた出目 (この戦闘中・次のターンから)。</summary>
        public int foundryBonus;
        /// <summary>〈千刃〉をこの戦闘で使った回数。</summary>
        public int mainsUses;
        /// <summary>残り火の賽 (ADR-0015): このターンの攻撃に足す分 (前のターンに捨てた目 × 2) / 次のターンへ渡す分。 持ち越すのは 1 ターン分だけ。</summary>
        public int emberCarry, emberNext;
        /// <summary>ヴェスカ〈停滞する時間〉: このターン止まっている場の端子 (添字 = 場の位置・null = 止まっている端子なし)。
        /// <see cref="TerminalDeckState.DrawTurn"/> が毎ターン作り直す。 止まった端子には挿せない (<see cref="TerminalCards.Resolve"/> が外す)。</summary>
        public bool[] stopped;

        // ── 山札の数 (2026-11-20・段 1-e・采配の軸)。 BOT の写し (Clone) にも乗る ──
        /// <summary>この戦闘で捨て札を切り直した回数 / このターンに切り直した枚数 (<see cref="BeginTurn"/> で 0)。
        /// 数えるのは <see cref="TerminalDeckState.BeforeShuffle"/> (本体の引く所と BOT の先読みが同じ関数を呼ぶ)。</summary>
        public int shuffles, shuffledNow;
        /// <summary>このターン、 残す印で前のターンから残った端子の名前 (null = 無し)。 <see cref="TerminalDeckState.DrawTurn"/> が作り直す
        /// (中は書き換えない ── 写しと共有してよい)。 〈留め金の栞〉〈折り目のついた札〉と出来事 OnKeptTerminalUse が読む。</summary>
        public List<string> kept;
        /// <summary>采配の品の旗 (戦闘の始めに所持品から・CombatManager.BeginTerminalDeck)。</summary>
        public DeckItems deckItems;
        /// <summary>この戦闘で使い捨ての端子を使った回数 (<see cref="TerminalCards.Resolve"/> が数える・減らない)。 〈再利用のるつぼ〉〈解体屋の袋〉が読む。</summary>
        public int disposedTotal;
        /// <summary>毎ターンの手札 +N (〈整理された工具箱〉・戦闘の始めに決める)。</summary>
        public int handBonus;
        /// <summary>残す印で残した端子を発動したターンが続いた数 (〈折り目のついた札〉・<see cref="TerminalCards.Resolve"/> が数える)。</summary>
        public int keptStreak;
        /// <summary>〈灰から組む機関〉で一度戻った使い捨ての名前 (多重集合・もう一度使えば外れる・<see cref="TerminalDeckState.DisposeFired"/>)。</summary>
        public List<string> ashReturned = new List<string>();
        /// <summary>この戦闘で〈巡りの鐘〉を使った回数 (切り直すたび、 次のターンに多く引く枚数・<see cref="TerminalDeckState.BeforeShuffle"/>)。</summary>
        public int bells;
        /// <summary>賽目の品の旗 (戦闘の始めに所持品から・CombatManager.BeginTerminalDeck・2026-11-21)。</summary>
        public RoleItems roleItems;
        /// <summary>〈宿命の刻み目〉: このターンに立った役〈連なり〉の効果 (挿した端子がすべて 2 回働く) を次のターンにももう 1 回 (<see cref="BeginTurn"/> が <see cref="doubleAll"/> へ移す・CombatManager.ResolveRoles が立てる)。</summary>
        public bool fateDoubleNext;

        /// <summary>場の s がこのターン止まっているか。</summary>
        public bool IsStopped(int s) => stopped != null && s >= 0 && s < stopped.Length && stopped[s];

        /// <summary>このターン止まっている端子の枚数。</summary>
        public int StoppedCount
        {
            get { int n = 0; if (stopped != null) foreach (bool b in stopped) if (b) n++; return n; }
        }

        public TerminalBattleState Clone()
        {
            var c = (TerminalBattleState)MemberwiseClone();
            c.gauge = new Dictionary<string, int>(gauge);
            c.stopped = stopped != null ? (bool[])stopped.Clone() : null;
            c.ashReturned = new List<string>(ashReturned);
            return c;
        }

        /// <summary>ターンの始めに呼ぶ (持ち越しを今のターンへ移す)。 戻り値 = このターンに多く引く枚数。</summary>
        public int BeginTurn()
        {
            ignoreCond = ignoreCondNext; ignoreCondNext = false;
            doubleAll = fateDoubleNext; fateDoubleNext = false;      // 〈宿命の刻み目〉の連なりの分 (2026-11-21)
            shuffledNow = 0;
            storedAttack += storedAttackNext; storedAttackNext = 0;
            emberCarry = emberNext; emberNext = 0;
            int extra = extraDrawNext; extraDrawNext = 0;
            return extra;
        }
    }

    /// <summary>端子の見積もりに要る盤面 (戦闘の今の値)。 本体も BOT もこれを埋めて <see cref="TerminalCards.Resolve"/> へ渡す。</summary>
    public struct TerminalEnv
    {
        public int charge, chargeCap, hp, maxHp, shield;
        public int enemyAttack, enemyPoison, enemyBleed, bursts;
        /// <summary>偏重 (軸の品の数): 心眼・血晶・瘴気・炉温・電流・堅牢・采配 (山札)・賽目 (役)。</summary>
        public int axCrit, axBleed, axPoison, axHeat, axCharge, axShield, axDeck, axRole;
        /// <summary>〈処刑の刃〉の恒久の攻撃 (ラン単位)。</summary>
        public int whetBonus;
        /// <summary>敵の今の HP / 最大 HP (〈処刑の刃＋〉の「敵の HP が半分以下なら」・2026-11-20)。 最大 HP 0 = 読まない。</summary>
        public int enemyHp, enemyMaxHp;
        /// <summary>防御の種類の端子に挿した出目 1 つから引く量 (下限 0)。 時限デバフ〈罪悪感〉(2026-11-16・EventSystem.TimedEffects.TimedDebuffRule) が 2。 0 = なし。
        /// <see cref="TerminalCards.Resolve"/> の「5. 効果」で、 端子へ入る出目の合計を作る所だけで引く (端子の条件は挿した目そのもので見る)。
        /// 本体・画面の見込み・BOT の配線の評価はどれも CombatManager.TerminalEnvNow の盤面を Resolve に渡すので、 同じ値を読む。</summary>
        public int blockDieCut;
        /// <summary>装備中の賽の id (ADR-0015)。 空なら賽のパッシブを足さない (<see cref="DiePassives.ApplyToTerminals"/>)。</summary>
        public string dieId;
        /// <summary>このターンに振り直したか (渡し守の賽)。 振り直しは全部の目を振る。</summary>
        public bool diceRerolled;
        /// <summary>このターンの敵の出目の最大 (夜番の賽)。</summary>
        public int enemyMaxDie;
        /// <summary>このターンが敵の大技か (大技読みの賽)。</summary>
        public bool heavyTurn;
        /// <summary>充電を払う端子の払う量から引く量 (最低 1・〈過負荷チューナー〉の過充電の間 1・2026-11-20)。 「全部払う」端子には掛けない。</summary>
        public int chargeCostCut;
        /// <summary>攻撃の種類の端子に挿せない (〈シュヴァリエのレイピア〉のコントラタック中・2026-11-20)。 挿した出目は「0.」で外す (止まった端子と同じ)。</summary>
        public bool noAttack;
        /// <summary>〈凶運〉で封じた役 (ctx.sealedRoleMask・立っても効かない) / このターン、 2 個そろいをゾロ目として扱う (〈万象のゾロ目〉・振り直しの後に決める・2026-11-21)。
        /// 立つ役は <see cref="TerminalCards.RoleOf"/> (CombatManager.ResolveRoles と同じ判定)。</summary>
        public int sealedRoleMask;
        public bool pairAsTriple;
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
        /// <summary>撃破のボーナス (このターンに敵を倒した時だけ配る・本体 CombatManager.FinishTerminalTurn)。 〈残響〉で 2 回発動すれば 2 回分 (2026-11-20〜・旧: 発動したかの真偽で 1 回分)。
        /// 最大 HP (〈魂狩り〉) / 〈処刑の刃〉の恒久の攻撃 / ゴールド (〈賞金狩り〉) / 次の報酬の選択肢 (〈剥ぎ取り〉)。</summary>
        public int killMaxHp, killWhet, killGold, killChoices;
        /// <summary>撃破した時に戻る HP (〈諸刃の剣＋〉が払った HP)。 回復ではない (回復を減らす効果は掛からない)。</summary>
        public int killRefundHp;
        /// <summary>〈応急箱＋〉の回復の量 (このうち最大 HP を超えた分がシールドになる)。 <see cref="heal"/> にも入っている。</summary>
        public int overhealToShield;
        /// <summary>この戦闘の充電の上限に足す量 (〈大導電＋〉・満たす前に足す)。</summary>
        public int chargeCapAdd;
        /// <summary>このターンに払うシールド (〈重装突撃〉)。 足す分 (shield) より先に引く。</summary>
        public int shieldSpend;
        /// <summary>〈後の先〉がブロックに入れた量 (〈残響〉の 2 回目も) / そのうち Resolve が見込みとして <see cref="TerminalBattleState.storedAttackNext"/> へ入れた量 (min(ブロック, 敵の攻撃))。
        /// 本体は敵の攻撃の後、 ブロックで実際に防いだ量へ直す (2026-11-20〜・CombatManager.SettleParry)。 BOT の見積もりは見込みのまま読む。</summary>
        public int parryBlock, parryStore;
        public bool chargeFill;
        public int poison, heat, bleedOnHit;
        public bool enemyStunNext, ignoreDefense;
        /// <summary>このターンに立つ役 (<see cref="TerminalCards.RoleOf"/>・2026-11-21): 0 = 立たない / 1 = 2 個そろい / 2 = ゾロ目か連なり。 端子〈役落とし〉と賽目の品が読む。</summary>
        public int roleNow;
        /// <summary>次のターンの最初の振り直しの充電の費用から引く量 (〈賽振り台〉・2026-11-21)。 本体が nextTurnBuffs (CombatManager.RerollDiscountKey) へ足す。</summary>
        public int rerollDiscountNext;
        /// <summary>エスカレーションの段階を戻す数 (〈氷結瓶〉・溜まりが届いた〈鎮静香〉。 〈残響〉の 2 回目・1 ターンに 2 回届いた〈鎮静香〉も数える・2026-11-20〜 旧: 真偽で 1 段だけ)。</summary>
        public int retreatStage;
        /// <summary>時計の賽の端子: エスカレーションの次の段階を何ターン先へ送るか (挿した出目の合計)。</summary>
        public int escalationDelay;
        /// <summary>このターンに発動した端子 (場の添字)。</summary>
        public readonly List<int> fired = new List<int>();
        /// <summary>発動した端子ごとの挿した出目の合計 (<see cref="fired"/> と同じ並び・2026-11-20)。 出来事 OnKeptTerminalUse / OnSupportTerminalUse の量。</summary>
        public readonly List<int> firedSums = new List<int>();
        /// <summary>このターンに使った使い捨ての枚数 (2026-11-20)。</summary>
        public int disposedNow;
        /// <summary>〈緊急再装填〉が次のターンの手札に戻す使い捨て (場の添字・<see cref="TerminalDeckState.DisposeFired"/> が読む)。</summary>
        public readonly List<int> reloadTargets = new List<int>();
        /// <summary>[計装 2026-11-19] 攻撃の合計 (attackAdd) を増やした所ごとの量 (「端子名:+N」)。 判断には使わない。</summary>
        public readonly List<string> attackLog = new List<string>();
        /// <summary>攻撃の端子 1 枚ごとの攻撃の量と挿した出目の数 (場の順・ADR-0016: 攻撃は端子 1 枚につき 1 回当たる)。
        /// attackAdd のうちここに入らない分 (連係・変圧・溜めた攻撃・賽の加算) は、 本体が最初の 1 撃に入れる。</summary>
        public readonly List<int> attackHits = new List<int>(), attackHitDice = new List<int>();
        /// <summary>攻撃の端子 1 枚ごとの名前 (<see cref="attackHits"/> と同じ並び・2026-11-20)。 本体の撃の輪が「この撃が〈崩し斬り〉か」を読む。</summary>
        public readonly List<string> attackHitIds = new List<string>();
        /// <summary>このターンに発動した基本以外の端子の数。</summary>
        public int firedNonBasic;
        /// <summary>〈有効活用〉〈影写し〉〈火葬の剣〉の対象 (場の添字・−1 = なし)。 選んだ対象 (Resolve の wantTarget) が候補なら それ、
        /// でなければ左端の候補 (<see cref="TerminalCards.TargetCandidates"/> の先頭)。</summary>
        public int retainTarget = -1, copyTarget = -1, burnTarget = -1;
        /// <summary>〈有効活用〉(手札の持ち越しの規則): 捨て札から 1 枚、 次のターンの手札に戻す (戻す 1 枚は <see cref="TerminalDeckState.RecallPick"/>)。</summary>
        public bool recall;
        /// <summary>〈反復突き〉(素 / 強化) の連続をこのターンもう数えたか。</summary>
        public bool oscCounted, oscCountedUp;
        /// <summary>ヴェスカ〈停滞する時間〉で止まっている場の端子 (Resolve に渡した状態の写し・null = なし)。
        /// 〈影写し〉〈火葬の剣〉〈有効活用〉の候補から外す (<see cref="TerminalCards.IsCandidate"/>)。</summary>
        public bool[] stopped;

        /// <summary>このターンに id の端子 (強化した端子も基の名前で数える) が発動したか。</summary>
        public bool Fired(List<string> board, string id)
        {
            for (int i = 0; i < fired.Count; i++) if (TerminalCards.BaseOf(board[fired[i]]) == id) return true;
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
        public const int HandSize = 5;
        /// <summary>山札の下限 (これを下回る抜き方はできない)。 出目 3 個を挿す先が足りなくならない枚数。</summary>
        public const int MinDeckSize = 3;

        // ===== 数値 (仮) =====
        // 2026-11-11 (1): 基本以外の端子の得を約 2 倍にした。 旧の値では、 端子を取る BOT が取らない BOT より弱かった
        //   (同じ BOT・各 100 ラン・6 層到達: 取らない 80% / 旧の値 68% / 2 倍 82%。 main は 81%)。
        // 2026-11-11 (2): 1 枚ずつの合否 (Tools/terminal_values_review.py のページ) で、 2 倍が強すぎると判定された端子を元の値へ戻した。
        //   2 倍のまま残したのは: 受け流し・身代わり札・二重障壁・包帯・打ち直し・鎮静香・柔軟な戦略・激昂・致命の一撃・一閃・弱点看破・重装突撃。
        //   作り替え: 大結界 (充電 6)・鎮静香 (段階を戻す)・千刃 (使うたび +1)・重装突撃 (足した分のシールドを払う)・
        //             剛腕 (条件なし)・先読み (2 枚)・有効活用 (出目をブロックに)。
        public const int DetectorMul = 2, RectifierMul = 2;
        public const int IgniterAttack = 7, FuseBlock = 8, GroundCutPct = 60, BreakerCutPct = 75, ReliefCutPct = 90;
        public const int DischargeCost = 3, DischargeMul = 3, BreakerCost = 6, DeathwishMul = 3, RelayPerFired = 8, OscPerStreak = 5;
        public const int CapacitorNeed = 12, CapacitorAttack = 40, ArresterNeed = 8, FoundryNeed = 10, CoolantNeed = 12;
        public const float AmplifierPerPip = 0.16f, ResonatorMul = 1.5f, VacuumCritMult = 2.0f;
        public const int CounterEvery = 10; public const float CounterPct = 0.10f;
        public const int TwinShotMul = 3, DetonatorMul = 4, FirstAidMul = 2, HealMul = 1, BatteryBonus = 2, InsulatorBonus = 4;
        /// <summary>〈火葬の剣〉: 挿した出目の合計に掛けて攻撃に足す倍率 (2026-11-14・作者の決定)。</summary>
        public const int BurnOutMul = 2;
        public const int HarvesterMaxHp = 2, WhetGain = 1, LootGold = 5;
        /// <summary>〈剥ぎ取り〉: 撃破したターンに、 戦闘後の端子の選択肢が増える数 (RunState.terminalExtraChoices に足す)。</summary>
        public const int StripperChoices = 1;
        public const float SightPerRank = 0.2f, VitalPerRank = 0.1f, BloodTapPct = 0.05f, BigDischargePerRank = 0.10f, ShieldPushPerRank = 0.06f;
        public const int BloodTapPer = 5, PoisonScalePer = 10, PoisonScaleAttack = 3, AfterHeatPerBurst = 2;
        // 2026-11-12 (3): 端子の ITT (Optimal / Super・各 5 万ラン・AutoRunLogs/terminal_itt/) で、 両方そろって強すぎ・弱すぎの物だけを動かした。
        //   強すぎ (Δクリア率 Optimal / Super): 応急箱 +13.0 / +16.0 → ×3→×2・包帯 +7.8 / +12.8 → ×4→×2・柔軟な戦略 +2.9 / +5.6 → 20%→10%・
        //     賞金狩り +1.9 / +3.6 → 10G→5G・二重障壁 +1.7 / +2.7 → +6→+4。
        //   弱すぎ: 諸刃の剣 −1.8 / −3.3 → 払う HP を出目の半分 (切り上げ) に・力溜め −1.2 / −1.4 → 溜める量 15→12。
        //   千刃 (+4.9 / +6.8)・攻防一体 (+3.2 / +4.0) はユーザーの指定した作りなので据え置き。 防御の割合の端子 (受け流し・防御陣) は弱いが原因が読めないので据え置き。
        // 2026-11-12 (4): 同じ ITT の第 2 弾 (根拠は前回の調整の後の ITT・Optimal / Super の差と対数オッズ比)。
        //   包帯 +2.35 / +6.15 → ×2→×1 (前回 ×4→×2 で倍率に素直に効いた)。
        //   応急箱 +7.09 / +12.08 → 使うと、 そのランの山札から抜ける (第 1 弾の ×3→×2 は倍率をほとんど感じなかったので、 倍率でなく機会の少なさで止める)。
        //   防御陣 −0.35 / −1.12 → 溜める量 12→8 (下げるほど発動が増える = 強くなる向き。 机上: 敵の攻撃 20 で出目 1 あたりの軽減が 2.9 → 4.4 になり、
        //     基本の防御 (出目 1 個 = 平均 3.5) を少し上回る)。
        //   千刃・諸刃の剣・号令の剣・力溜め・受け流し・剥ぎ取り・火葬の剣は据え置き。 腕から元の値へ戻す口は GameHooks.Terminal* (AutoRunner.terminal*)。
        // 2026-11-14: 〈応急箱〉の「使うとランの山札から抜ける」を取り下げた (作者の決定:「使い捨ては全部『その戦闘で山札に戻らない』でいい」)。
        //   使い捨ての決まりは disposable だけ。 旧い決まりへ戻す測定の口は GameHooks.TerminalFirstAidLeavesRunDeck (AutoRunner.terminalFirstAidLeavesRunDeck)。
        /// <summary>〈千刃〉: この戦闘で使うたび、 以後のこの端子の攻撃 +N。 / 〈先読み〉: 次のターンに多く引く枚数。</summary>
        public const int MainsPerUse = 1, SwitchboardDraw = 2;

        // ===== 采配 (山札の軸) の品の数値 (2026-11-20・段 1-e・item-overhaul-spec §4 / §12-4・未測定) =====
        /// <summary>〈留め金の栞〉各撃 +N / 〈補助歯車〉シールド / 〈空回りの錘〉多く引く上限 / 〈仕込みの配電盤〉要る枚数・多く引く枚数 / 〈型抜きの金型〉出力・補助のシールド /
        /// 〈常設の作業台〉常設 1 枚あたり / 〈永久機関の主軸〉倍化の間隔 / 〈解体屋の袋〉間隔 / 〈整理された工具箱〉山札の枚数・手札 / 〈予備の差込口〉1 ターン目に多く引く枚数。</summary>
        public const int ClaspAttack = 5, AuxGearShield = 4, IdleWeightDrawMax = 2, SwitchboardNeed = 3, SwitchboardItemDraw = 2, MoldBonus = 3, MoldSupportShield = 4,
            WorkbenchPer = 3, EngineEvery = 3, BagEvery = 3, ToolboxMaxDeck = 12, ToolboxHand = 1, SparePlugDraw = 2;
        /// <summary>〈折り目のついた札〉: 与ダメージ % の池に足す。</summary>
        public const float FoldedPct = 0.40f;
        // ===== 賽目 (役の軸) の品の数値 (2026-11-21・段 1-f・item-overhaul-spec §5・未測定) =====
        /// <summary>〈見切りの木札〉役が立ったターン 各撃の攻撃 +N・ブロック +N / 〈符丁の刻み板〉各撃の攻撃 + 賽目 × N (ゾロ目・連なりなら 2 倍)。</summary>
        public const int WoodTagBonus = 4, NotchBoardPer = 2;
        /// <summary>〈賽振り台〉次のターンの最初の振り直しの費用から引く上限 (min(上限, 賽目)) / 〈役落とし〉役が成立したターン 攻撃 + 賽目 × N。 *Up = 強化した端子。</summary>
        public const int DiceTableCap = 6, DiceTableCapUp = 8, RoleDropPer = 3, RoleDropPerUp = 4;
        // ===== 采配と共通の端子 (2026-11-20・段 1-e・未測定) =====
        /// <summary>〈焼却斬り〉出目の倍率 / 使い捨て 1 回あたりの下限 (max(2, 采配)) / 〈一刀両断〉出目の倍率 / 〈歯車の軸〉挿した目 1 つあたり / 〈崩し斬り〉最大 HP の %。 *Up = 強化した端子。</summary>
        public const int BurnSlashMul = 2, BurnSlashMulUp = 3, BurnSlashMinPer = 2, OneCutMul = 3, OneCutMulUp = 4, GearPip = 2, GearPipUp = 3, CrushPct = 5, CrushPctUp = 6;

        // ===== 強化した端子 (＋) の数値 (2026-11-20・作者承認の表 Captures/overhaul/work/terminal_upgrades_draft.md・未測定) =====
        //   強化した端子は基の端子の定義を写して、 表の分だけ変えた別の端子 (TerminalIds.Upgraded)。 効果の式は Effect の up の枝。
        /// <summary>基本 (攻撃・防御・充電)・〈後の先〉〈攻防一体〉のブロック: 出目 +1。 / 〈無形の構え〉〈有効活用〉のブロック: 出目 +2。</summary>
        public const int UpPip = 1, UpPipBlock2 = 2;
        public const int DetectorBonusUp = 2, DischargeCostUp = 2, RelayPerFiredUp = 10, OscPerStreakUp = 7, TwinPairBonusUp = 3;
        public const int FuseBlockUp = 10, InsulatorBonusUp = 5, BreakerCostUp = 5, RectifierBonusUp = 2, BatteryBonusUp = 3, HealShieldUp = 2;
        public const int CapacitorNeedUp = 10, ArresterNeedUp = 7, SwitchboardDrawUp = 3;
        public const float VacuumCritMultUp = 2.5f;
        public const int CoolantNeedUp = 10, CounterEveryUp = 8, TwinShotMulUp = 4, HarvesterMaxHpUp = 3, WhetLowHpAttackUp = 4, LootGoldUp = 7, StripperChoicesUp = 2;
        public const float SightPerRankUp = 0.25f, VitalPerRankUp = 0.15f, ShieldPushPerRankUp = 0.08f;
        public const int BarbBonusUp = 2, MisterBonusUp = 2, PoisonScaleAttackUp = 4, StokerBonusUp = 2, AfterHeatPerBurstUp = 3, LeakBonusUp = 2, PatchBonusUp = 2;
        public const int DetonatorMulUp = 5, PiercerMulUp = 2, ReliefShieldUp = 4, FullChargeCapUp = 5, BurnOutMulUp = 3;

        /// <summary>強化できない端子 (作者の決定 2026-11-20)。 〈時計〉は山札に入らないので対象にならない。</summary>
        private static readonly HashSet<string> NotUpgradable = new HashSet<string>
        {
            TerminalIds.Doubler, TerminalIds.Amplifier, TerminalIds.Resonator, TerminalIds.Mains, TerminalIds.Foundry, TerminalIds.Clock,
        };

        /// <summary>強化した端子の説明文 (基の端子の名前 → 強化後の文)。 ここに無い端子は強化できない。</summary>
        private static readonly Dictionary<string, string> UpgradedText = new Dictionary<string, string>
        {
            [TerminalIds.Attack] = "出目 +" + UpPip + " を攻撃に。",
            [TerminalIds.Block] = "出目 +" + UpPip + " をブロックに。",
            [TerminalIds.Charge] = "出目 +" + UpPip + " を充電に。",
            [TerminalIds.Igniter] = "攻撃 +" + IgniterAttack + "。",
            [TerminalIds.Detector] = "出目 ×" + DetectorMul + " +" + DetectorBonusUp + " を攻撃に。",
            [TerminalIds.DischargeTube] = "充電 " + DischargeCostUp + " を払い、出目 ×" + DischargeMul + " を攻撃に。",
            [TerminalIds.Deathwish] = "出目の半分 (切り上げ) の HP を払い、出目 ×" + DeathwishMul + " を攻撃に。HP が足りなければ発動しない。このターンに敵を倒すと、払った HP が戻る。",
            [TerminalIds.Relay] = "出目を攻撃に。このターンに発動した基本以外の端子 1 つにつき攻撃 +" + RelayPerFiredUp + "。",
            [TerminalIds.Grinder] = "出目を攻撃に。全部の出目 +1 (この端子に挿した出目も)。",
            [TerminalIds.Oscillator] = "出目を攻撃に。前にこれを挿した目と同じなら攻撃 +" + OscPerStreakUp + " × 連続回数。",
            [TerminalIds.Twin] = "出目を攻撃に。出目 2 個まで挿せる。2 個挿したら攻撃 +" + TwinPairBonusUp + "。",
            [TerminalIds.Fuse] = "ブロック +" + FuseBlockUp + "。",
            [TerminalIds.Ground] = "このターンの被ダメージ −" + GroundCutPct + "%。出目をブロックに。",
            [TerminalIds.Insulator] = "シールド +出目 +" + InsulatorBonusUp + "。",
            [TerminalIds.Parry] = "出目 +" + UpPip + " をブロックに。防いだ量を、次に攻撃するターンの攻撃に足す。",
            [TerminalIds.Breaker] = "充電 " + BreakerCostUp + " を払い、このターンの敵の攻撃 −" + BreakerCutPct + "%。",
            [TerminalIds.Rectifier] = "充電 +出目 ×" + RectifierMul + " +" + RectifierBonusUp + "。",
            [TerminalIds.Battery] = "充電 +出目 +" + BatteryBonusUp + "。",
            [TerminalIds.Heal] = (HealMul == 1 ? "HP +出目。" : "HP +出目 ×" + HealMul + "。") + "シールド +" + HealShieldUp + "。",
            [TerminalIds.Capacitor] = "出目を溜める。" + CapacitorNeedUp + " で次の攻撃 +" + CapacitorAttack + "。",
            [TerminalIds.Arrester] = "出目を溜める。" + ArresterNeedUp + " でそのターンの被ダメージ半分。",
            [TerminalIds.Transformer] = "攻撃の端子を挿した出目を 2 倍。この端子に挿した出目も攻撃に足す。",
            [TerminalIds.Switchboard] = "次のターン、端子を " + SwitchboardDrawUp + " 枚多く引く。",
            [TerminalIds.VacuumTube] = "このターンの会心倍率 +" + VacuumCritMultUp.ToString("0.0") + "。",
            [TerminalIds.Coolant] = "出目を溜める。" + CoolantNeedUp + " ごとにエスカレーションの段階を 1 つ戻す。",
            [TerminalIds.Counter] = "出目を攻撃に。基本以外の発動が累計 " + CounterEveryUp + " 回になるごとに、この戦闘中の与ダメージ +" + (int)(CounterPct * 100) + "%。",
            [TerminalIds.Preheater] = "出目 +" + UpPipBlock2 + " をブロックに。次のターン、端子の条件を無視する。",
            [TerminalIds.TwinShot] = "同じ目 2 個で、出目の合計 ×" + TwinShotMulUp + " を攻撃に。",
            [TerminalIds.Harvester] = "出目を攻撃に。このターンに敵を倒すと最大 HP +" + HarvesterMaxHpUp + " (恒久)。",
            [TerminalIds.Whet] = "出目を攻撃に。敵の HP が半分以下なら攻撃 +" + WhetLowHpAttackUp + "。このターンに敵を倒すと、この端子の攻撃が恒久に +" + WhetGain + "。",
            [TerminalIds.LootBox] = "出目を攻撃に。このターンに敵を倒すとゴールド +" + LootGoldUp + "。",
            [TerminalIds.Stripper] = "出目を攻撃に。このターンに敵を倒すと、戦闘後の端子の選択肢が " + StripperChoicesUp + " つ増える。",
            [TerminalIds.Retainer] = "出目 +" + UpPipBlock2 + " をブロックに。捨て札から 1 枚、次のターンの手札に戻す。",
            [TerminalIds.StopSignal] = "敵は次のターン行動しない。出目をブロックに。",
            [TerminalIds.Fork] = "出目を攻撃に、出目 +" + UpPip + " をブロックに入れる。",
            [TerminalIds.Sight] = "出目を攻撃に。このターンの会心倍率 +心眼 ×" + SightPerRankUp.ToString("0.0#") + "。",
            [TerminalIds.VitalRead] = "このターンの攻撃は必ず会心。会心倍率 +心眼 ×" + VitalPerRankUp.ToString("0.0#") + "。",
            [TerminalIds.Barb] = "出目を攻撃に。攻撃が通れば出血 +血晶 +" + BarbBonusUp + "。",
            [TerminalIds.BloodTap] = "敵の出血 " + BloodTapPer + " につき、このターンの与ダメージ +" + (int)(BloodTapPct * 100) + "%。",
            [TerminalIds.Mister] = "敵に毒 +出目 +瘴気 +" + MisterBonusUp + "。",
            [TerminalIds.PoisonScale] = "出目を攻撃に。敵の毒 " + PoisonScalePer + " につき攻撃 +" + PoisonScaleAttackUp + "。",
            [TerminalIds.Stoker] = "臨界 +出目 +炉温 +" + StokerBonusUp + "。",
            [TerminalIds.AfterHeat] = "出目を攻撃に。この戦闘の爆発 1 回につき攻撃 +" + AfterHeatPerBurstUp + "。",
            [TerminalIds.Leak] = "充電 +出目 +電流 +" + LeakBonusUp + "。",
            [TerminalIds.BigDischarge] = "充電をすべて払い、払った充電を攻撃に足す (電流 1 につき +" + (int)(BigDischargePerRank * 100) + "%)。挿した出目も攻撃に足す。",
            [TerminalIds.Patch] = "出目をブロックに。シールド +堅牢 +" + PatchBonusUp + "。",
            [TerminalIds.ShieldPush] = "出目を攻撃に。今のシールドの 堅牢 ×" + (int)Math.Round(ShieldPushPerRankUp * 100) + "% を攻撃に足し、足した分のシールドを払う。",
            [TerminalIds.Detonator] = "出目 ×" + DetonatorMulUp + " を攻撃に。",
            [TerminalIds.Piercer] = "出目 ×" + PiercerMulUp + " を攻撃に。このターン敵の防御を無視。",
            [TerminalIds.ReliefValve] = "このターンの被ダメージ −" + ReliefCutPct + "%。シールド +" + ReliefShieldUp + "。",
            [TerminalIds.FirstAid] = "HP +出目 ×" + FirstAidMul + "。最大 HP を超えた分はシールドになる。",
            [TerminalIds.FullCharge] = "この戦闘中、充電の上限 +" + FullChargeCapUp + "。充電を上限まで満たす。",
            [TerminalIds.CoolShot] = "エスカレーションの段階を 1 つ戻す。出目をブロックに。",
            [TerminalIds.Copier] = "挿さずに空けた手札の端子を 1 枚、この戦闘中だけ複製して、次のターンの手札に加える。出目を充電に。空けた端子が無ければ発動しない。",
            [TerminalIds.BurnOut] = "出目 ×" + BurnOutMulUp + " を攻撃に。挿さずに空けた手札の端子を 1 枚、この戦闘から外す。空けた端子が無ければ発動しない。",
            // 采配と共通の端子 (2026-11-20)。 使い捨ては回数・枚数を増やさない (段 1-b の方針・terminal_upgrades_draft.md)
            [TerminalIds.BurnSlash] = "出目 ×" + BurnSlashMulUp + " を攻撃に。これまでにこの戦闘で使い捨てを使った回数 × max(" + BurnSlashMinPer + ", 采配) を攻撃に足す。",
            [TerminalIds.Reload] = "このターンに使った他の使い捨てを 1 枚 (場の右から)、次のターンの手札に戻す。出目を充電に。",
            [TerminalIds.OneCut] = "出目 ×" + OneCutMulUp + " を攻撃に。この撃は敵の防御を無視。挿した攻撃の端子がこれ 1 枚だけのターンにだけ発動する。",
            [TerminalIds.Gear] = "出目は挿せない。場にあれば、毎ターン最初に発動した基本以外の端子 (場の左から) の挿した目 1 つにつき +" + GearPipUp + "。一度引くとこの戦闘中ずっと場に残る。",
            [TerminalIds.Bell] = "この戦闘の残りの間、山札を切り直すたび、次のターンに端子を 1 枚多く引く。出目を充電に。",
            [TerminalIds.Mikiri] = "出目をブロックに。敵が大技を予告したターンなら 2 倍。",
            // 役の軸の端子 (2026-11-21)
            [TerminalIds.DiceTable] = "充電 +出目。次のターンの最初の振り直しの充電の費用 −min(" + DiceTableCapUp + ", 賽目)。",
            [TerminalIds.RoleDrop] = "出目を攻撃に。このターン役が成立していれば、攻撃 +賽目 ×" + RoleDropPerUp + " (ゾロ目・連なりなら 2 倍)。",
            [TerminalIds.Crush] = "出目 +" + UpPip + " を攻撃に。この撃でボスの目盛りを割ったら、敵に最大 HP の " + CrushPctUp + "% の軽減不可ダメージ。",
        };

        /// <summary>基の端子の定義を写して、 強化した端子を作る (名前・説明文と、 表で変える条件・払う充電だけ変える)。</summary>
        private static TerminalDef MakeUpgraded(TerminalDef b)
        {
            var u = b.Copy();
            u.id = TerminalIds.Upgraded(b.id); u.baseId = b.id; u.upgraded = true;
            u.description = UpgradedText[b.id];
            switch (b.id)
            {
                case TerminalIds.Igniter: case TerminalIds.BloodTap: case TerminalIds.Mikiri: u.cond = TerminalCond.AtMost4; break;
                case TerminalIds.DischargeTube: u.chargeCost = DischargeCostUp; break;
                case TerminalIds.Breaker: u.chargeCost = BreakerCostUp; break;
            }
            u.condLabel = CondLabel(u.cond);
            return u;
        }

        /// <summary>〈包帯〉の倍率 (測定の口 <see cref="GameLoop.GameHooks.TerminalHealMulOverride"/> があればその値)。 効果はこれを読む。</summary>
        public static int HealMulNow => GameLoop.GameHooks.TerminalHealMulOverride > 0 ? GameLoop.GameHooks.TerminalHealMulOverride : HealMul;
        /// <summary>〈防御陣〉の溜める量 (測定の口 <see cref="GameLoop.GameHooks.TerminalArresterNeedOverride"/> があればその値)。 効果と BOT はこれを読む。</summary>
        public static int ArresterNeedNow => GameLoop.GameHooks.TerminalArresterNeedOverride > 0 ? GameLoop.GameHooks.TerminalArresterNeedOverride : ArresterNeed;

        /// <summary>使った時に、 そのランの山札から抜けるか。 既定では抜けない (2026-11-14〜・使い捨ては全部「その戦闘で山札に戻らない」だけ)。
        /// 測定の口 <see cref="GameLoop.GameHooks.TerminalFirstAidLeavesRunDeck"/> が立っていれば、 <see cref="TerminalDef.leavesRunDeck"/> の端子 (〈応急箱〉) は
        /// 旧い決まり (2026-11-12) どおり抜ける。 本体 (CombatManager.FinishTerminalTurn) と BOT が読む。</summary>
        public static bool LeavesRunDeck(TerminalDef d)
            => d != null && d.leavesRunDeck && GameLoop.GameHooks.TerminalFirstAidLeavesRunDeck;

        /// <summary>手札の持ち越し (2026-11-13): 挿さなかった端子は捨て札へ行かず、 次のターンも手札に残る。 次のターンは手札が規定の枚数になるまで引く。
        /// 測定の口 <see cref="GameLoop.GameHooks.HandCarryDisabled"/> が立っていれば旧い規則 (前の手札を全部捨てて引き直す)。
        /// 本体 (<see cref="TerminalDeckState.DrawTurn"/>) と BOT の先読みが <see cref="TerminalDeckState.SplitHand"/> を通して同じ規則を読む。</summary>
        public static bool HandCarry => GameLoop.GameHooks.HandCarry != HandCarryMode.Off;

        /// <summary>手札の規則 (測定の口 <see cref="GameLoop.GameHooks.HandCarry"/>)。 <see cref="HandCarry"/> は Off 以外 (〈有効活用〉が捨て札から戻す形になる)。</summary>
        public static HandCarryMode CarryMode => GameLoop.GameHooks.HandCarry;

        private static TerminalDef D(string id, TerminalKind k, string desc, TerminalCond c = TerminalCond.None, string axis = null)
            => new TerminalDef { id = id, kind = k, cond = c, description = desc, axis = axis, condLabel = CondLabel(c) };

        private static string CondLabel(TerminalCond c)
        {
            switch (c)
            {
                case TerminalCond.AtMost3: return "3以下";
                case TerminalCond.AtMost4: return "4以下";
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
        /// <summary>使い捨て + 測定の口で旧い決まり (使うとランの山札から抜ける) へ戻せる印。 既定の挙動は <see cref="Once"/> と同じ。</summary>
        private static TerminalDef RunOnce(TerminalDef d) { d.disposable = true; d.leavesRunDeck = true; return d; }
        private static TerminalDef Cost(TerminalDef d, int n) { d.chargeCost = n; return d; }
        private static TerminalDef Cap(TerminalDef d, int n) { d.capacity = n; return d; }
        private static TerminalDef ClockDef(TerminalDef d) { d.basic = true; d.permanent = true; return d; }
        private static TerminalDef Perm(TerminalDef d) { d.permanent = true; return d; }

        /// <summary>全部の端子 (素の端子の後ろに強化した端子)。 抽選 (店・報酬・付与) は <see cref="TerminalDef.upgraded"/> を外して使う。</summary>
        public static readonly List<TerminalDef> All = Build();

        private static List<TerminalDef> Build()
        {
            var l = BuildBase();
            foreach (var b in l) b.baseId = b.id;
            int n = l.Count;
            for (int i = 0; i < n; i++)
                if (!NotUpgradable.Contains(l[i].id) && UpgradedText.ContainsKey(l[i].id)) l.Add(MakeUpgraded(l[i]));
            return l;
        }

        private static List<TerminalDef> BuildBase()
        {
            const TerminalKind A = TerminalKind.Attack, B = TerminalKind.Block, C = TerminalKind.Charge, S = TerminalKind.Support;
            string ax = ItemSystem.PassiveSkills.AxisRules.Crit, ab = ItemSystem.PassiveSkills.AxisRules.Bleed, ap = ItemSystem.PassiveSkills.AxisRules.Poison,
                   ah = ItemSystem.PassiveSkills.AxisRules.Heat, ac = ItemSystem.PassiveSkills.AxisRules.Charge, asx = ItemSystem.PassiveSkills.AxisRules.Shield;
            var perm = D(TerminalIds.Mains, A, "出目を攻撃に。この戦闘で使うたび、以後のこの端子の攻撃 +" + MainsPerUse + "。一度引くとこの戦闘中ずっと場に残る。"); perm.permanent = true;
            return new List<TerminalDef>
            {
                // ── 基本 ──
                Basic(D(TerminalIds.Attack, A, "出目を攻撃に。")),
                Basic(D(TerminalIds.Block,  B, "出目をブロックに。")),
                Basic(D(TerminalIds.Charge, C, "出目を充電に。")),
                // ── 賽の端子 (ADR-0015)。 基本扱い (報酬・店・付与に出ない)・常設 ──
                ClockDef(D(TerminalIds.Clock, S, "時計の賽の端子。出目の数だけ、エスカレーションの次の段階を先へ送る。一度に 1 つ。")),
                // ── コモン ──
                D(TerminalIds.Igniter, A, "攻撃 +" + IgniterAttack + "。", TerminalCond.AtMost3),
                D(TerminalIds.Detector, A, "出目 ×" + DetectorMul + " を攻撃に。", TerminalCond.Odd),
                Cost(D(TerminalIds.DischargeTube, A, "充電 " + DischargeCost + " を払い、出目 ×" + DischargeMul + " を攻撃に。"), DischargeCost),
                D(TerminalIds.Deathwish, A, "出目の半分 (切り上げ) の HP を払い、出目 ×" + DeathwishMul + " を攻撃に。HP が足りなければ発動しない。"),
                D(TerminalIds.Relay, A, "出目を攻撃に。このターンに発動した基本以外の端子 1 つにつき攻撃 +" + RelayPerFired + "。"),
                D(TerminalIds.Grinder, A, "出目を攻撃に。他の出目すべて +1。"),
                D(TerminalIds.Oscillator, A, "出目を攻撃に。前にこれを挿した目と同じなら攻撃 +" + OscPerStreak + " × 連続回数。"),
                Cap(D(TerminalIds.Twin, A, "出目を攻撃に。出目 2 個まで挿せる。"), 2),
                D(TerminalIds.Fuse, B, "ブロック +" + FuseBlock + "。", TerminalCond.AtMost3),
                D(TerminalIds.Ground, B, "このターンの被ダメージ −" + GroundCutPct + "%。", TerminalCond.AtMost3),
                D(TerminalIds.Insulator, B, "シールド +出目 +" + InsulatorBonus + "。", TerminalCond.Even),
                D(TerminalIds.Parry, B, "出目をブロックに。防いだ量を、次に攻撃するターンの攻撃に足す。"),
                Cost(D(TerminalIds.Breaker, B, "充電 " + BreakerCost + " を払い、このターンの敵の攻撃 −" + BreakerCutPct + "%。"), BreakerCost),
                D(TerminalIds.Rectifier, C, "充電 +出目 ×" + RectifierMul + "。", TerminalCond.Even),
                D(TerminalIds.Battery, C, "充電 +出目 +" + BatteryBonus + "。"),
                D(TerminalIds.Heal, S, (HealMul == 1 ? "HP +出目。" : "HP +出目 ×" + HealMul + "。"), TerminalCond.AtMost3),
                D(TerminalIds.Capacitor, S, "出目を溜める。" + CapacitorNeed + " で次の攻撃 +" + CapacitorAttack + "。"),
                D(TerminalIds.Arrester, S, "出目を溜める。" + ArresterNeed + " でそのターンの被ダメージ半分。"),
                D(TerminalIds.Transformer, S, "攻撃の端子を挿した出目を 2 倍。"),
                D(TerminalIds.Switchboard, S, "次のターン、端子を " + SwitchboardDraw + " 枚多く引く。"),
                // ── レア ──
                Rare(D(TerminalIds.Amplifier, S, "出目 1 につき与ダメージ +" + (int)(AmplifierPerPip * 100) + "% (別枠)。", TerminalCond.AtLeast5)),
                Rare(D(TerminalIds.VacuumTube, S, "このターンの会心倍率 +" + VacuumCritMult.ToString("0.0") + "。", TerminalCond.AtLeast6)),
                Rare(D(TerminalIds.Resonator, S, "このターンの与ダメージ ×" + ResonatorMul.ToString("0.0") + " (別枠)。", TerminalCond.SameElsewhere)),
                Rare(D(TerminalIds.Foundry, S, "出目を溜める。" + FoundryNeed + " ごとにこの戦闘中の全部の出目 +1 (このターンは、まだ効果の出ていない端子の出目から)。")),
                Rare(D(TerminalIds.Coolant, S, "出目を溜める。" + CoolantNeed + " ごとにエスカレーションの段階を 1 つ戻す。")),
                Rare(D(TerminalIds.Counter, A, "出目を攻撃に。基本以外の発動が累計 " + CounterEvery + " 回になるごとに、この戦闘中の与ダメージ +" + (int)(CounterPct * 100) + "%。")),
                Rare(D(TerminalIds.Preheater, B, "出目をブロックに。次のターン、端子の条件を無視する。")),
                Rare(perm),
                Rare(Cap(D(TerminalIds.TwinShot, A, "同じ目 2 個で、出目の合計 ×" + TwinShotMul + " を攻撃に。", TerminalCond.PairSame), 2)),
                Rare(D(TerminalIds.Harvester, A, "出目を攻撃に。このターンに敵を倒すと最大 HP +" + HarvesterMaxHp + " (恒久)。")),
                Rare(D(TerminalIds.Whet, A, "出目を攻撃に。このターンに敵を倒すと、この端子の攻撃が恒久に +" + WhetGain + "。")),
                Rare(D(TerminalIds.LootBox, A, "出目を攻撃に。このターンに敵を倒すとゴールド +" + LootGold + "。")),
                // 〈剥ぎ取り〉: 2026-11-13 に補助から攻撃へ (ユーザーの決定)。 補助のままでは挿しても攻撃にならず、 レアの 3 択でほかに負け続けた (GAME.md §24)
                Rare(D(TerminalIds.Stripper, A, "出目を攻撃に。このターンに敵を倒すと、戦闘後の端子の選択肢が " + StripperChoices + " つ増える。")),
                Rare(D(TerminalIds.Doubler, S, "このターン、他の端子の効果を 2 回発動させる。", TerminalCond.AtLeast6)),
                // 〈有効活用〉: 手札の持ち越し (2026-11-13・HandCarry) で「空けた端子を 1 枚残す」は役目を失ったので、
                //   「捨て札から選んだ 1 枚を、 次のターンの手札に戻す」に作り替えた (ユーザーの決定)。 出目をブロックに入れるのは残す
                //   (ADR-0014 の値付けの規則 2: 準備役は挿した出目を素の端子と同じに入れたうえで効果を足す)。
                //   測定の口で旧い規則に戻した腕では、 元の「空けた端子を 1 枚残す」が働く (説明文は今の規則のまま)。
                Rare(D(TerminalIds.Retainer, B, "出目をブロックに。捨て札から 1 枚、次のターンの手札に戻す。")),
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
                // 2026-11-19: 使い捨てに (作者の決定)。 〈繋ぎの賽〉で毎ターン撃ち続けられ、 溜めて撃つ判断が消えていた
                Once(D(TerminalIds.BigDischarge, A, "充電をすべて払い、払った充電を攻撃に足す (電流 1 につき +" + (int)(BigDischargePerRank * 100) + "%)。", TerminalCond.None, ac)),
                D(TerminalIds.Patch, B, "出目をブロックに。シールド +堅牢。", TerminalCond.None, asx),
                D(TerminalIds.ShieldPush, A, "出目を攻撃に。今のシールドの 堅牢 ×" + (int)(ShieldPushPerRank * 100) + "% を攻撃に足し、足した分のシールドを払う。", TerminalCond.None, asx),
                // ── 使い捨て ──
                Once(D(TerminalIds.Detonator, A, "出目 ×" + DetonatorMul + " を攻撃に。")),
                Once(D(TerminalIds.Piercer, A, "出目を攻撃に。このターン敵の防御を無視。")),
                Once(D(TerminalIds.ReliefValve, B, "このターンの被ダメージ −" + ReliefCutPct + "%。")),
                RunOnce(D(TerminalIds.FirstAid, S, "HP +出目 ×" + FirstAidMul + "。")),
                Once(D(TerminalIds.FullCharge, C, "充電を上限まで満たす。")),
                Once(D(TerminalIds.CoolShot, S, "エスカレーションの段階を 1 つ戻す。")),
                Once(D(TerminalIds.Copier, S, "挿さずに空けた手札の端子を 1 枚、この戦闘中だけ複製して、次のターンの手札に加える。空けた端子が無ければ発動しない。")),
                // 〈火葬の剣〉: 2026-11-14 に作り直し (作者の決定)。 旧: 補助で「空けた端子を 1 枚この戦闘から外す」だけ (測定で発動 1.7%・GAME.md §24)。
                //   外す端子が無いターン (候補 0) は挿しても不発 (Resolve の 4.)・使い捨ても消費しない
                Once(D(TerminalIds.BurnOut, A, "出目 ×" + BurnOutMul + " を攻撃に。挿さずに空けた手札の端子を 1 枚、この戦闘から外す。空けた端子が無ければ発動しない。")),
                // ── 采配と共通の端子 (2026-11-20・段 1-e・item-overhaul-spec §8 / §12-4) ──
                Once(D(TerminalIds.BurnSlash, A, "出目 ×" + BurnSlashMul + " を攻撃に。これまでにこの戦闘で使い捨てを使った回数 × max(" + BurnSlashMinPer + ", 采配) を攻撃に足す。", TerminalCond.None, ItemSystem.PassiveSkills.AxisRules.Deck)),
                //   〈緊急再装填〉: 自分は戻せない (他の〈緊急再装填〉は戻せる・作者 2026-11-20「戻し合っても意味がない」)。 戻す 1 枚は場の右から (Resolve の 5b)
                Once(D(TerminalIds.Reload, S, "このターンに使った他の使い捨てを 1 枚 (場の右から)、次のターンの手札に戻す。")),
                Once(D(TerminalIds.OneCut, A, "出目 ×" + OneCutMul + " を攻撃に。この撃は敵の防御を無視。挿した攻撃の端子がこれ 1 枚だけのターンにだけ発動する。")),
                //   〈歯車の軸〉: 挿せない常設 (capacity 0)。 場にあれば効く (Resolve の 5.)。 2 枚あれば 2 枚分
                Cap(Perm(D(TerminalIds.Gear, S, "出目は挿せない。場にあれば、毎ターン最初に発動した基本以外の端子 (場の左から) の挿した目 1 つにつき +" + GearPip + "。一度引くとこの戦闘中ずっと場に残る。")), 0),
                Rare(Once(D(TerminalIds.Bell, S, "この戦闘の残りの間、山札を切り直すたび、次のターンに端子を 1 枚多く引く。"))),
                D(TerminalIds.Mikiri, B, "出目をブロックに。敵が大技を予告したターンなら 2 倍。", TerminalCond.AtMost3),
                D(TerminalIds.Crush, A, "出目を攻撃に。この撃でボスの目盛りを割ったら、敵に最大 HP の " + CrushPct + "% の軽減不可ダメージ。", TerminalCond.AtLeast5),
                // ── 役の軸の端子 (2026-11-21・段 1-f・item-overhaul-spec §8-1 / §12-4) ──
                //   〈賽振り台〉: 配線は振り直しの後なので、 安くするのは次のターンの最初の振り直し (§12-4)
                D(TerminalIds.DiceTable, C, "充電 +出目。次のターンの最初の振り直しの充電の費用 −min(" + DiceTableCap + ", 賽目)。", TerminalCond.None, ItemSystem.PassiveSkills.AxisRules.Role),
                D(TerminalIds.RoleDrop, A, "出目を攻撃に。このターン役が成立していれば、攻撃 +賽目 ×" + RoleDropPer + " (ゾロ目・連なりなら 2 倍)。", TerminalCond.None, ItemSystem.PassiveSkills.AxisRules.Role),
            };
        }

        private static readonly Dictionary<string, TerminalDef> _byId = Index();
        private static Dictionary<string, TerminalDef> Index()
        {
            var d = new Dictionary<string, TerminalDef>();
            foreach (var t in All) d[t.id] = t;
            return d;
        }

        /// <summary>基の端子の名前 (強化した端子なら強化する前の名前・素の端子と知らない名前はそのまま)。 効果の分かれ道・品の判定はこれで見る。</summary>
        public static string BaseOf(string id) => Get(id)?.baseId ?? id;

        /// <summary>id の端子を強化できるか (強化できる素の端子)。 強化した端子は <see cref="TerminalIds.Upgraded"/> の名前で引ける。</summary>
        public static bool CanUpgrade(string id)
        {
            var d = Get(id);
            return d != null && !d.upgraded && Get(TerminalIds.Upgraded(id)) != null;
        }

        /// <summary>溜める端子の溜まりの必要量 (名前ごと・強化した端子は表の値)。 本体の効果と BOT の見積もりが読む。 溜める端子でなければ 0。</summary>
        public static int GaugeNeed(string id)
        {
            var d = Get(id); if (d == null) return 0;
            switch (d.baseId)
            {
                case TerminalIds.Capacitor: return d.upgraded ? CapacitorNeedUp : CapacitorNeed;
                case TerminalIds.Arrester:  return d.upgraded ? ArresterNeedUp : ArresterNeedNow;
                case TerminalIds.Foundry:   return FoundryNeed;
                case TerminalIds.Coolant:   return d.upgraded ? CoolantNeedUp : CoolantNeed;
                default: return 0;
            }
        }

        /// <summary>今の id の端子。 無い id は null (旧 id の対応表は 2026-11-14 に削除・docs/GAME.md §24)。</summary>
        public static TerminalDef Get(string id)
            => id != null && _byId.TryGetValue(id, out var d) ? d : null;

        /// <summary>端子の名前の一覧 (ランの山札など) から、 今の端子に無い名前を外す。 外した数を返す。 null は何もしない。
        /// <para>中断セーブの読み込み口 (<see cref="GameLoop.RunSave"/>) が使う ── 改名前の名前 (例: 2026-11-13 までの〈保持器〉) が
        /// 山札に残っていても、 引いた先で「無い端子」として場に出さない。 今の id だけの一覧は何も変わらない。</para></summary>
        public static int DropUnknownIds(List<string> ids)
            => ids == null ? 0 : ids.RemoveAll(id => id == null || !_byId.ContainsKey(id));

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
                case TerminalCond.AtMost4: return v <= 4;
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
        /// <param name="wantTarget">〈影写し〉〈火葬の剣〉〈有効活用〉の対象 (添字 = その端子の場の位置・値 = 対象の場の位置・−1 / null = 選ばない)。
        /// 対象が候補でなければ (挿した・常設・自分) 左端の候補になる。</param>
        public static TerminalOutcome Resolve(List<string> board, int[] wantSlot, int[] dice, TerminalEnv env, TerminalBattleState state, int[] wantTarget = null)
        {
            int n = dice.Length, m = board.Count;
            var o = new TerminalOutcome { slotOfDie = new int[n], kindOfDie = new DiceTerminal[n] };
            o.stopped = state.stopped != null ? (bool[])state.stopped.Clone() : null;
            var v = new int[n];
            for (int i = 0; i < n; i++) { v[i] = dice[i]; o.slotOfDie[i] = wantSlot != null && i < wantSlot.Length && wantSlot[i] >= 0 && wantSlot[i] < m ? wantSlot[i] : -1; }
            var defs = new TerminalDef[m];
            for (int s = 0; s < m; s++) defs[s] = Get(board[s]);

            // ── 0. 止まった端子 (ヴェスカ〈停滞する時間〉) と、 挿せる数を超えた分を外す ──
            //   止まった端子に挿した出目は挿さなかった扱い。 本体も BOT も画面もここを通るので、 同じ計算になる。
            var cnt = new int[m];
            for (int i = 0; i < n; i++)
            {
                int s = o.slotOfDie[i]; if (s < 0) continue;
                if (defs[s] == null || state.IsStopped(s) || cnt[s] >= defs[s].capacity || (env.noAttack && defs[s].kind == TerminalKind.Attack)) { o.slotOfDie[i] = -1; continue; }
                cnt[s]++;
            }

            // ── 1. 〈号令の剣〉: 他の出目すべて +1 (他の端子より先に効く)。 強化した〈号令の剣＋〉は挿した出目も +1 ──
            for (int i = 0; i < n; i++)
                if (o.slotOfDie[i] >= 0 && defs[o.slotOfDie[i]]?.baseId == TerminalIds.Grinder)
                {
                    bool self = defs[o.slotOfDie[i]].upgraded;
                    for (int j = 0; j < n; j++) if (j != i || self) v[j]++;
                }

            // ── 2. 条件 ──
            bool ign = state.ignoreCond;
            for (int pass = 0; pass < 2; pass++)      // 2 回: 〈共鳴〉は「他の端子に同じ目」なので、 外れた出目を見てもう一度
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
                int cost = defs[s].chargeCost > 1 ? System.Math.Max(1, defs[s].chargeCost - env.chargeCostCut) : defs[s].chargeCost;   // 〈過負荷チューナー〉
                if (defs[s].baseId == TerminalIds.BigDischarge) { if (charge <= 0) { Unplug(o.slotOfDie, s); continue; } paid[s] = true; continue; }      // 全部払う (量は効果の所で)
                if (cost <= 0) { paid[s] = true; continue; }
                if (charge < cost) { Unplug(o.slotOfDie, s); continue; }
                charge -= cost; o.chargeSpend += cost; paid[s] = true;
            }

            // ── 4. 空けた手札の端子 (〈有効活用〉〈影写し〉〈火葬の剣〉の対象)。 選んだ対象が候補ならそれ、 でなければ左端の候補 ──
            int Empty(int self)
            {
                int want = wantTarget != null && self >= 0 && self < wantTarget.Length ? wantTarget[self] : -1;
                if (IsCandidate(board, o, self, want)) return want;
                for (int s = 0; s < m; s++)
                    if (IsCandidate(board, o, self, s)) return s;
                return -1;
            }
            //   〈火葬の剣〉(2026-11-14)・〈影写し〉(2026-11-20〜): 対象 (候補) が無ければ不発 ── 挿した出目を外す (条件に合わない・充電が足りない時と同じ扱い・使い捨ても消費しない)。
            //   旧い規則の〈有効活用〉は今までどおり、 対象が無くても発動する (効果が無いだけ)。 旧: 〈影写し〉も対象なしで発動して消費した
            for (int s = 0; s < m; s++)
                if ((defs[s]?.baseId == TerminalIds.BurnOut || defs[s]?.baseId == TerminalIds.Copier) && Plugged(o.slotOfDie, s) && Empty(s) < 0) Unplug(o.slotOfDie, s);
            //   〈一刀両断〉(2026-11-20): 挿した攻撃の種類の端子がこれ 1 枚だけのターンにだけ発動する (2 枚以上なら不発・使い捨ても消費しない)
            {
                int atkPlugged = 0;
                for (int s = 0; s < m; s++) if (defs[s] != null && defs[s].kind == TerminalKind.Attack && Plugged(o.slotOfDie, s)) atkPlugged++;
                if (atkPlugged > 1)
                    for (int s = 0; s < m; s++) if (defs[s]?.baseId == TerminalIds.OneCut && Plugged(o.slotOfDie, s)) Unplug(o.slotOfDie, s);
            }

            // ── 5. 効果 ──
            //   このターンに立つ役 (振った目で決まる・CombatManager.ResolveRoles と同じ判定)。 〈役落とし〉と賽目の品 (6d) が読む
            { var rk = RoleOf(dice, env); o.roleNow = rk == null ? 0 : YachtRoles.IsBig(rk.Value) ? 2 : 1; }
            bool doubler = false;
            for (int s = 0; s < m; s++) if (defs[s]?.baseId == TerminalIds.Doubler && Plugged(o.slotOfDie, s)) doubler = true;
            // 連なりの賽 (2026-11-20): 3 つの目が連番のターン (役〈連なり〉)、 挿した端子がすべて倍化する (〈残響〉と同じ)
            if (env.dieId == GameLoop.DieIds.Straight && YachtRoles.IsRun(v)) doubler = true;
            if (state.doubleAll) doubler = true;      // 〈倍化の共鳴玉〉(2026-11-20): このターン、 挿した端子がすべて 2 回働く (〈残響〉と同じ)
            //   〈剛腕〉(2026-11-20・説明文どおり): 攻撃の種類の端子に挿した出目を 2 倍にしてから、 その端子の効果に入れる (倍率の端子は 2 倍の目に掛かる)。
            //   条件 (2.) は挿した目そのもので見る。 〈剛腕〉1 枚 (効果 1 回) につき ×2 ── 〈残響〉で 2 回発動すれば ×4、 2 枚挿せば ×4。
            //   旧: 攻撃の種類の端子に挿した出目の合計を、 攻撃にもう 1 回足していた (倍率の端子では説明より小さく、 出目を攻撃に使わない端子の分も足した)
            int pipMul = 1;
            for (int s = 0; s < m; s++)
                if (defs[s]?.baseId == TerminalIds.Transformer && Plugged(o.slotOfDie, s)) pipMul *= doubler ? 4 : 2;
            //   〈打ち直し〉(2026-11-20〜 説明文どおり): 溜まりが届いたターンは、 まだ効果を出していない (場の順で後ろの) 端子の出目も 1 個ずつ +1。
            //   条件 (2.) は挿した目そのもので見る。 次のターンからは振った出目に足す (本体 DrawTerminalHand → FoundryBonusKey)
            int foundryNow = 0;
            //   〈歯車の軸〉(2026-11-20): 場にあれば (止まっていない)、 このターン最初に発動した基本以外の端子 (場の順) の挿した目 1 つにつき +2 (強化 +3・何枚でも足す)
            int gearPip = 0; bool gearUsed = false;
            for (int s = 0; s < m; s++) if (defs[s]?.baseId == TerminalIds.Gear && !state.IsStopped(s)) gearPip += defs[s].upgraded ? GearPipUp : GearPip;
            for (int s = 0; s < m; s++)
            {
                if (defs[s] == null || !Plugged(o.slotOfDie, s)) continue;
                var d = defs[s]; string id = d.baseId;      // 効果の分かれ道は基の名前 (強化した端子は d.upgraded)
                int sum = 0, num = 0;
                //   〈罪悪感〉(2026-11-16): 防御の種類の端子に挿した出目は 1 つずつ −blockDieCut (下限 0)。 〈号令の剣〉の +1 の後に引く。
                //   条件 (「2. 条件」) は引く前の目で見る ── 引いた目で見ると〈導火〉(3 以下) などに挿せる目が増え、 罰が得になる。
                int cutHere = d.kind == TerminalKind.Block ? env.blockDieCut : 0;
                for (int i = 0; i < n; i++) if (o.slotOfDie[i] == s) { sum += EventSystem.TimedEffects.TimedDebuffRule.CutBlockDie(v[i] + foundryNow, cutHere); num++; }
                bool gearHere = gearPip > 0 && !gearUsed && !d.basic;
                if (gearHere) sum += gearPip * num;
                int raw = sum;      // 〈剛腕〉で 2 倍にする前 (〈反復突き〉の「同じ目」はこれで見る)
                if (d.kind == TerminalKind.Attack) sum *= pipMul;
                //   払う物 (充電・HP) は 1 回だけ、 効果は〈残響〉で 2 回 (2026-11-20〜・充電は 3. で 1 回払っている)。
                //   〈諸刃の剣〉: 出目の半分 (切り上げ) の HP を払う。 払うと HP が 0 になる (足りない) なら不発 (旧: 払える分だけ払って攻撃は全部入った・〈残響〉で 2 回払った)
                if (id == TerminalIds.Deathwish)
                {
                    int cost = (sum + 1) / 2;
                    if (cost > env.hp - 1 - o.selfDamage) { Unplug(o.slotOfDie, s); continue; }
                    o.selfDamage += cost;
                    if (d.upgraded) o.killRefundHp += cost;      // 〈諸刃の剣＋〉: このターンに敵を倒すと、 払った HP が戻る
                }
                //   〈裁きの雷〉: 充電をすべて払う (1 回)。 〈残響〉の 2 回目も払った量で攻撃に足す (旧: 2 回目は充電 0 で出目の分だけ)
                int paidAll = 0;
                if (id == TerminalIds.BigDischarge) { paidAll = charge; o.chargeSpend += charge; charge = 0; }
                o.fired.Add(s); o.firedSums.Add(sum);
                if (gearHere) gearUsed = true;      // 不発 (〈諸刃の剣〉の HP 不足) なら次の端子へ回る
                if (!d.basic) o.firedNonBasic++;
                switch (d.kind)
                {
                    case TerminalKind.Attack: o.attackDice += num; break;
                    case TerminalKind.Block: o.blockDice += num; break;
                    case TerminalKind.Charge: o.chargeDice += num; break;
                    default: o.supportDice += num; break;
                }
                int times = doubler && id != TerminalIds.Doubler ? 2 : 1;
                int atkBefore = o.attackAdd, chgBefore = charge;
                int fBefore = state.foundryBonus;
                for (int t = 0; t < times; t++) Effect(id, d.upgraded, board[s], s, sum, raw, num, paidAll, env, state, o, ref charge, Empty);
                //   采配の品 (2026-11-20): 端子 1 枚の発動につき 1 回 (〈残響〉で効果が 2 回走っても 1 回)
                if (d.disposable) o.disposedNow++;
                if ((state.deckItems & DeckItems.Mold) != 0 && d.cond != TerminalCond.None)      // 〈型抜きの金型〉: 条件のある端子の出力 +3・補助はシールド +4 (条件を無視したターンも条件のある端子に数える)
                    switch (d.kind)
                    {
                        case TerminalKind.Attack: o.attackAdd += MoldBonus; break;      // この端子の撃に入る (下の attackHits)
                        case TerminalKind.Block: o.blockAdd += MoldBonus; break;
                        case TerminalKind.Charge: o.chargeAdd += MoldBonus; break;
                        default: o.shield += MoldSupportShield; break;
                    }
                if ((state.deckItems & DeckItems.AuxGear) != 0 && d.kind == TerminalKind.Support && !d.basic) o.shield += AuxGearShield;   // 〈補助歯車〉 (基本の〈時計〉は数えない)
                foundryNow += state.foundryBonus - fBefore;
                if (d.kind == TerminalKind.Attack) { o.attackHits.Add(o.attackAdd - atkBefore); o.attackHitDice.Add(num); o.attackHitIds.Add(board[s]); }
                if (o.attackAdd != atkBefore) o.attackLog.Add($"{board[s]}(目{sum}{(times > 1 ? "・倍化" : "")}{(chgBefore != charge ? "・充電" + chgBefore + "→" + charge : "")}):+{o.attackAdd - atkBefore}");
            }

            // ── 5b. 〈緊急再装填〉(2026-11-20): このターンに使った他の使い捨てを 1 枚ずつ (場の右から・自分は除く・他の〈緊急再装填〉は戻せる)。 〈残響〉で 2 回働いても 1 枚 ──
            for (int s = 0; s < m; s++)
            {
                if (defs[s]?.baseId != TerminalIds.Reload || !o.fired.Contains(s)) continue;
                for (int t = m - 1; t >= 0; t--)
                    if (t != s && defs[t] != null && defs[t].disposable && o.fired.Contains(t) && !o.reloadTargets.Contains(t)) { o.reloadTargets.Add(t); break; }
            }

            // ── 6. 他の端子を見る効果 (全部の発動が決まった後) ──
            for (int s = 0; s < m; s++)
            {
                if (defs[s] == null || !Plugged(o.slotOfDie, s)) continue;
                int times = doubler ? 2 : 1;
                // 〈連係斬り〉: このターンに発動した基本以外の端子 (自分も数える・2026-11-20〜 説明文どおり。 旧: 自分を数えなかった)
                if (defs[s].baseId == TerminalIds.Relay) o.attackAdd += (defs[s].upgraded ? RelayPerFiredUp : RelayPerFired) * o.firedNonBasic * times;
            }
            // 〈柔軟な戦略〉: 基本以外の発動の累計 (付けていなくても数える・効くのは引いて場にあるターンだけ = 本体 DrawTerminalHand)
            state.firedTotal += o.firedNonBasic;
            // 使い捨ての数 (この戦闘・2026-11-20)。 〈再利用のるつぼ〉: 使い捨てを使ったターン、 この戦闘で使った回数 × 采配 を次の攻撃に
            //   (〈力溜め〉と同じ口 ── 攻撃するならこのターンの攻撃に入る・しなければ次に攻撃するターン)
            state.disposedTotal += o.disposedNow;
            if ((state.deckItems & DeckItems.Crucible) != 0 && o.disposedNow > 0) state.storedAttack += state.disposedTotal * env.axDeck;
            // 次に攻撃するターンの攻撃に足す分
            int atk6 = o.attackAdd;
            if (o.attackDice > 0 && state.storedAttack > 0) { o.attackAdd += state.storedAttack; state.storedAttack = 0; }
            if (o.attackAdd != atk6) o.attackLog.Add($"連係・溜めた攻撃:+{o.attackAdd - atk6}");

            // ── 6b. 装備中の賽のパッシブ (ADR-0015): 挿した目ごとに効く・残り火の持ち越し ──
            int atkDie = o.attackAdd;
            DiePassives.ApplyToTerminals(env, state, o, defs, v);
            if (o.attackAdd != atkDie) o.attackLog.Add($"賽:+{o.attackAdd - atkDie}");

            // ── 6c. 采配の品 (2026-11-20・段 1-e)。 攻撃 +N は攻撃の端子の撃 1 つずつに足す (ADR-0016・品の攻撃 +N と同じく全部の撃へ・攻撃しないターンは入らない) ──
            ApplyDeckItems(board, defs, env, state, o);

            // ── 6d. 賽目の品 (2026-11-21・段 1-f)。 役が立ったターン、 攻撃 +N は撃 1 つずつに (6c と同じ形) ──
            ApplyRoleItems(env, state, o);

            for (int i = 0; i < n; i++)
                o.kindOfDie[i] = o.slotOfDie[i] >= 0 ? LegacyKind(defs[o.slotOfDie[i]].kind) : DiceTerminal.None;
            return o;
        }

        /// <summary>采配の品のうち、 全部の発動が決まった後に効く物 (Resolve の 6c)。 旗は <see cref="TerminalBattleState.deckItems"/>。</summary>
        private static void ApplyDeckItems(List<string> board, TerminalDef[] defs, TerminalEnv env, TerminalBattleState st, TerminalOutcome o)
        {
            var f = st.deckItems;
            // 残す印で残した端子を、 このターンに発動したか (名前で数える ── 同じ名前の 2 枚目は区別しない・常設は残せない)。 続いた数は品が無くても数える
            int keptUsed = 0;
            if (st.kept != null)
            {
                var left = new List<string>(st.kept);
                foreach (int s in o.fired) if (defs[s] != null && !defs[s].permanent && left.Remove(board[s])) keptUsed++;
            }
            st.keptStreak = keptUsed > 0 ? st.keptStreak + 1 : 0;
            if (f == DeckItems.None) return;
            bool support = false;
            foreach (int s in o.fired) if (defs[s] != null && defs[s].kind == TerminalKind.Support && !defs[s].basic) support = true;
            int perms = 0;      // 〈常設の作業台〉: 場の常設の端子 (止まっていない・基本の〈時計〉を除く)
            if ((f & DeckItems.Workbench) != 0)
                for (int s = 0; s < defs.Length; s++) if (defs[s] != null && defs[s].permanent && !defs[s].basic && !st.IsStopped(s)) perms++;
            int each = 0;
            if ((f & DeckItems.Clasp) != 0 && keptUsed > 0) each += ClaspAttack;            // 〈留め金の栞〉
            if ((f & DeckItems.Rewind) != 0) each += st.shuffledNow;                          // 〈早回しのゼンマイ〉: このターンに切り直した枚数
            if ((f & DeckItems.Seamless) != 0 && support) each += env.axDeck;                 // 〈継ぎ目のない導線〉
            if (perms > 0) { each += WorkbenchPer * perms; o.blockAdd += WorkbenchPer * perms; }
            if (each > 0 && o.attackHits.Count > 0)
            {
                for (int h = 0; h < o.attackHits.Count; h++) o.attackHits[h] += each;
                o.attackAdd += each * o.attackHits.Count;
                o.attackLog.Add($"采配の品:+{each}×{o.attackHits.Count}撃");
            }
            if ((f & DeckItems.Folded) != 0 && st.keptStreak >= 2) o.outgoingPctAdd += FoldedPct;                     // 〈折り目のついた札〉
            if ((f & DeckItems.Switchboard) != 0 && o.firedNonBasic >= SwitchboardNeed) st.extraDrawNext += SwitchboardItemDraw;   // 〈仕込みの配電盤〉
            if ((f & DeckItems.IdleWeight) != 0)                                                // 〈空回りの錘〉: どの端子にも挿さなかった出目 (条件に合わず外れた目も)
            {
                int idle = 0;
                foreach (int x in o.slotOfDie) if (x < 0) idle++;
                st.extraDrawNext += Math.Min(IdleWeightDrawMax, idle);
            }
        }

        /// <summary>賽目の品のうち、 端子の解決の中で効く物 (Resolve の 6d)。 旗は <see cref="TerminalBattleState.roleItems"/>。</summary>
        private static void ApplyRoleItems(TerminalEnv env, TerminalBattleState st, TerminalOutcome o)
        {
            var f = st.roleItems;
            if (f == RoleItems.None || o.roleNow <= 0) return;
            int each = 0;
            if ((f & RoleItems.WoodTag) != 0) { each += WoodTagBonus; o.blockAdd += WoodTagBonus; }      // 〈見切りの木札〉
            if ((f & RoleItems.NotchBoard) != 0) each += env.axRole * NotchBoardPer * (o.roleNow >= 2 ? 2 : 1);   // 〈符丁の刻み板〉
            if (each > 0 && o.attackHits.Count > 0)
            {
                for (int h = 0; h < o.attackHits.Count; h++) o.attackHits[h] += each;
                o.attackAdd += each * o.attackHits.Count;
                o.attackLog.Add($"賽目の品:+{each}×{o.attackHits.Count}撃");
            }
        }

        /// <summary>このターンに立つ役 (立たなければ null)。 CombatManager.ResolveRoles と同じ判定: 振った 3 つの目 (<see cref="YachtRoles.Evaluate"/>)・
        /// 〈凶運〉で封じた役は立たない・〈万象のゾロ目〉で格上げした 2 個そろいはゾロ目 (<see cref="UpgradePair"/>)。 3 つの目では役は高々 1 つ。</summary>
        public static RoleKind? RoleOf(int[] dice, in TerminalEnv env)
        {
            var l = _roleBuf ?? (_roleBuf = new List<RoleKind>(2));      // BOT が何度も呼ぶので使い回す
            l.Clear();
            YachtRoles.Evaluate(dice, null, null, l);
            foreach (var k in l)
            {
                if ((env.sealedRoleMask & (1 << (int)k)) != 0) continue;
                return UpgradePair(k, env.pairAsTriple, env.sealedRoleMask);
            }
            return null;
        }

        [ThreadStatic] private static List<RoleKind> _roleBuf;

        /// <summary>〈万象のゾロ目〉: <paramref name="asTriple"/> なら 2 個そろいを同じ目のゾロ目にする (そのゾロ目が〈凶運〉で封じられていれば 2 個そろいのまま)。</summary>
        public static RoleKind UpgradePair(RoleKind k, bool asTriple, int sealedMask)
        {
            if (!asTriple || k == RoleKind.Straight || YachtRoles.IsTriple(k)) return k;
            var t = YachtRoles.Of(YachtRoles.FaceOf(k), true);
            return (sealedMask & (1 << (int)t)) != 0 ? k : t;
        }

        /// <summary>所持品から賽目の品の旗を作る (戦闘の始め・CombatManager.BeginTerminalDeck)。</summary>
        public static RoleItems RoleItemsOf(GameLoop.RunState run)
        {
            var f = RoleItems.None;
            if (ItemSystem.PassiveSkills.AxisRules.Owns(run, GameLoop.ItemIds.RoleWoodTag)) f |= RoleItems.WoodTag;
            if (ItemSystem.PassiveSkills.AxisRules.Owns(run, GameLoop.ItemIds.RoleNotchBoard)) f |= RoleItems.NotchBoard;
            return f;
        }

        /// <summary>所持品から采配の品の旗を作る (戦闘の始め・CombatManager.BeginTerminalDeck)。</summary>
        public static DeckItems DeckItemsOf(GameLoop.RunState run)
        {
            var f = DeckItems.None;
            foreach (var (id, flag) in DeckItemTable) if (ItemSystem.PassiveSkills.AxisRules.Owns(run, id)) f |= flag;
            return f;
        }
        private static readonly (string id, DeckItems flag)[] DeckItemTable =
        {
            (GameLoop.ItemIds.DeckClasp, DeckItems.Clasp), (GameLoop.ItemIds.DeckAuxGear, DeckItems.AuxGear), (GameLoop.ItemIds.DeckIdleWeight, DeckItems.IdleWeight),
            (GameLoop.ItemIds.DeckRewind, DeckItems.Rewind), (GameLoop.ItemIds.DeckSwitchboard, DeckItems.Switchboard), (GameLoop.ItemIds.DeckMold, DeckItems.Mold),
            (GameLoop.ItemIds.DeckCrucible, DeckItems.Crucible), (GameLoop.ItemIds.DeckBlueprint, DeckItems.Blueprint), (GameLoop.ItemIds.DeckSeamless, DeckItems.Seamless),
            (GameLoop.ItemIds.DeckFolded, DeckItems.Folded), (GameLoop.ItemIds.DeckBag, DeckItems.Bag), (GameLoop.ItemIds.DeckWorkbench, DeckItems.Workbench),
            (GameLoop.ItemIds.DeckAsh, DeckItems.Ash), (GameLoop.ItemIds.DeckEngine, DeckItems.Engine),
        };

        /// <summary>捨て札の 1 枚を対象に取る端子 (手札の持ち越しの規則の〈有効活用〉・強化した端子も)。 対象 (挿し方の targets の値) は捨て札の添字。</summary>
        public static bool PicksDiscard(string id) => BaseOf(id) == TerminalIds.Retainer && HandCarry;

        /// <summary>挿さずに空けた手札の端子を対象に取る端子 (〈影写し〉〈火葬の剣〉・旧い規則の〈有効活用〉・強化した端子も)。</summary>
        public static bool PicksTarget(string id)
        {
            string b = BaseOf(id);
            return b == TerminalIds.Copier || b == TerminalIds.BurnOut || (b == TerminalIds.Retainer && !HandCarry);
        }

        /// <summary>場の s が、 self (対象を取る端子) の対象の候補か: 自分でない・常設でない・止まっていない (ヴェスカ〈停滞する時間〉)・
        /// 出目が 1 個も挿さっていない手札の端子。 o は解決の結果 ── 挿し方を直した後の <see cref="TerminalOutcome.slotOfDie"/>
        /// (条件に合わず外れた端子も「空けた」に入る) と、 止まった端子 (<see cref="TerminalOutcome.stopped"/>) を読む。</summary>
        public static bool IsCandidate(List<string> board, TerminalOutcome o, int self, int s)
        {
            if (board == null || s < 0 || s >= board.Count || s == self) return false;
            if (o?.stopped != null && s < o.stopped.Length && o.stopped[s]) return false;
            var d = Get(board[s]);
            return d != null && !d.permanent && (o?.slotOfDie == null || !Plugged(o.slotOfDie, s));
        }

        /// <summary>self の対象の候補 (場の添字・左から)。</summary>
        public static List<int> TargetCandidates(List<string> board, TerminalOutcome o, int self)
        {
            var l = new List<int>();
            if (board != null) for (int s = 0; s < board.Count; s++) if (IsCandidate(board, o, self, s)) l.Add(s);
            return l;
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

        /// <summary>端子 1 つの効果 (id = 基の名前・up = 強化した端子・name = 場の名前 (溜まりは名前ごと)・sum = 挿した出目の合計・raw = 〈剛腕〉で 2 倍にする前・num = 挿した出目の数・
        /// paidAll = 〈裁きの雷〉が払った充電)。 払う物 (充電・HP) は呼び手が 1 回だけ払う (ここは〈残響〉で 2 回走る)。</summary>
        private static void Effect(string id, bool up, string name, int slot, int sum, int raw, int num, int paidAll, TerminalEnv env, TerminalBattleState st, TerminalOutcome o, ref int charge, Func<int, int> empty)
        {
            int p1 = up ? UpPip : 0, p2 = up ? UpPipBlock2 : 0;      // 強化した端子の「出目 +1」「出目 +2」
            switch (id)
            {
                // 基本
                case TerminalIds.Attack: o.attackAdd += sum + p1; break;
                // 出目をそのまま攻撃に入れるだけの端子。 上乗せは別の所で付く:
                //   号令の剣 = 解決の前に他の出目 +1 / 連係斬り = 下の「6. 他の端子を見る効果」/ 柔軟な戦略 = 本体 (DrawTerminalHand) /
                //   魂狩り・賞金狩り・剥ぎ取り = 撃破した時 (CombatManager.FinishTerminalTurn)
                case TerminalIds.Relay:
                case TerminalIds.Grinder: case TerminalIds.Counter:
                    o.attackAdd += sum; break;
                case TerminalIds.Twin: o.attackAdd += sum + (up && num >= 2 ? TwinPairBonusUp : 0); break;
                case TerminalIds.Harvester: o.attackAdd += sum; o.killMaxHp += up ? HarvesterMaxHpUp : HarvesterMaxHp; break;
                case TerminalIds.LootBox: o.attackAdd += sum; o.killGold += up ? LootGoldUp : LootGold; break;
                case TerminalIds.Stripper: o.attackAdd += sum; o.killChoices += up ? StripperChoicesUp : StripperChoices; break;
                case TerminalIds.Block: o.blockAdd += sum + p1; break;
                case TerminalIds.Charge: o.chargeAdd += sum + p1; break;
                // コモン
                case TerminalIds.Igniter: o.attackAdd += IgniterAttack; break;      // 強化は条件 (4 以下) だけ
                case TerminalIds.Detector: o.attackAdd += sum * DetectorMul + (up ? DetectorBonusUp : 0); break;
                case TerminalIds.DischargeTube: o.attackAdd += sum * DischargeMul; break;      // 強化は払う充電 (定義の chargeCost) だけ
                case TerminalIds.Deathwish: o.attackAdd += sum * DeathwishMul; break;      // HP は呼び手 (5.) が 1 回だけ払う (出目の半分 (切り上げ)・足りなければ不発)
                case TerminalIds.Oscillator:
                {
                    // 連続は 1 ターンに 1 つだけ進める (〈残響〉で効果が 2 回走っても、 2 回目は同じ連続回数で足すだけ)。 素と強化した端子は別に数える
                    ref int last = ref (up ? ref st.oscLastUp : ref st.oscLast);
                    ref int streak = ref (up ? ref st.oscStreakUp : ref st.oscStreak);
                    if (!(up ? o.oscCountedUp : o.oscCounted))
                    {
                        if (last == raw) streak++; else streak = 0;
                        last = raw;
                        if (up) o.oscCountedUp = true; else o.oscCounted = true;
                    }
                    o.attackAdd += sum + (up ? OscPerStreakUp : OscPerStreak) * streak; break;
                }
                case TerminalIds.Fuse: o.blockAdd += up ? FuseBlockUp : FuseBlock; break;
                case TerminalIds.Ground: o.takenMul *= 1f - GroundCutPct / 100f; if (up) o.blockAdd += sum; break;
                case TerminalIds.Insulator: o.shield += sum + (up ? InsulatorBonusUp : InsulatorBonus); break;
                case TerminalIds.Parry:
                {
                    // 「防いだ量」= ブロックで防いだ量 (2026-11-20〜)。 ここでは見込み (min(後の先のブロック, 敵の攻撃)) を入れ、 本体が敵の攻撃の後に実際の量へ直す
                    //   (CombatManager.SettleParry・旧: 見込みのまま ── 敵が攻撃しないターン・〈大結界〉で減った攻撃でも同じ量を溜めた)
                    o.blockAdd += sum + p1; o.parryBlock += sum + p1;
                    int est = Math.Min(o.parryBlock, Math.Max(0, env.enemyAttack)) - o.parryStore;
                    st.storedAttackNext += est; o.parryStore += est; break;
                }
                case TerminalIds.Breaker: o.enemyAttackMul *= 1f - BreakerCutPct / 100f; break;      // 強化は払う充電だけ
                case TerminalIds.Rectifier: o.chargeAdd += sum * RectifierMul + (up ? RectifierBonusUp : 0); break;
                case TerminalIds.Battery: o.chargeAdd += sum + (up ? BatteryBonusUp : BatteryBonus); break;
                case TerminalIds.Heal: o.heal += sum * HealMulNow; if (up) o.shield += HealShieldUp; break;
                case TerminalIds.Capacitor: st.storedAttack += CapacitorAttack * Gauge(st, name, sum, GaugeNeed(name)); break;
                case TerminalIds.Arrester: if (Gauge(st, name, sum, GaugeNeed(name)) > 0) o.takenMul *= 0.5f; break;
                case TerminalIds.Transformer: if (up) o.attackAdd += sum; break;      // 2 倍は 5. の頭 (pipMul)。 〈剛腕＋〉は挿した出目も攻撃に足す
                case TerminalIds.Switchboard: st.extraDrawNext += up ? SwitchboardDrawUp : SwitchboardDraw; break;
                // レア
                case TerminalIds.Amplifier: o.damageMul *= 1f + AmplifierPerPip * sum; break;
                case TerminalIds.VacuumTube: o.critMultAdd += up ? VacuumCritMultUp : VacuumCritMult; break;
                case TerminalIds.Resonator: o.damageMul *= ResonatorMul; break;
                case TerminalIds.Foundry: st.foundryBonus += Gauge(st, name, sum, GaugeNeed(name)); break;
                case TerminalIds.Coolant: o.retreatStage += Gauge(st, name, sum, GaugeNeed(name)); break;      // 1 ターンに 2 回届けば 2 段 (2026-11-20〜)
                case TerminalIds.Clock: o.escalationDelay += sum; break;      // 時計の賽 (ADR-0015)
                case TerminalIds.Preheater: o.blockAdd += sum + p2; st.ignoreCondNext = true; break;
                case TerminalIds.Mains: o.attackAdd += sum + MainsPerUse * st.mainsUses; st.mainsUses++; break;
                case TerminalIds.TwinShot: o.attackAdd += sum * (up ? TwinShotMulUp : TwinShotMul); break;
                case TerminalIds.Whet:
                    o.attackAdd += sum + env.whetBonus; o.killWhet += WhetGain;
                    // 〈処刑の刃＋〉: 敵の HP が半分以下 (端子を解く時の HP) なら攻撃 +4
                    if (up && env.enemyMaxHp > 0 && env.enemyHp * 2 <= env.enemyMaxHp) o.attackAdd += WhetLowHpAttackUp;
                    break;
                // 残響 (5. の頭で見る) は、 ここでは何もしない
                case TerminalIds.Retainer: o.blockAdd += sum + p2; if (!HandCarry) o.retainTarget = empty(slot); else o.recall = true; break;   // 持ち越しの規則では捨て札から 1 枚戻す (対象は RecallPick)
                case TerminalIds.StopSignal: o.enemyStunNext = true; if (up) o.blockAdd += sum; break;
                case TerminalIds.Fork: o.attackAdd += sum; o.blockAdd += sum + p1; break;
                // 軸
                case TerminalIds.Sight: o.attackAdd += sum; o.critMultAdd += (up ? SightPerRankUp : SightPerRank) * env.axCrit; break;
                case TerminalIds.VitalRead: o.forceCrit = true; o.critMultAdd += (up ? VitalPerRankUp : VitalPerRank) * env.axCrit; break;
                case TerminalIds.Barb: o.attackAdd += sum; o.bleedOnHit += env.axBleed + (up ? BarbBonusUp : 0); break;
                case TerminalIds.BloodTap: o.outgoingPctAdd += BloodTapPct * (env.enemyBleed / BloodTapPer); break;      // 強化は条件 (4 以下) だけ
                case TerminalIds.Mister: o.poison += sum + env.axPoison + (up ? MisterBonusUp : 0); break;
                case TerminalIds.PoisonScale: o.attackAdd += sum + (up ? PoisonScaleAttackUp : PoisonScaleAttack) * (env.enemyPoison / PoisonScalePer); break;
                case TerminalIds.Stoker: o.heat += sum + env.axHeat + (up ? StokerBonusUp : 0); break;
                case TerminalIds.AfterHeat: o.attackAdd += sum + (up ? AfterHeatPerBurstUp : AfterHeatPerBurst) * env.bursts; break;
                case TerminalIds.Leak: o.chargeAdd += sum + env.axCharge + (up ? LeakBonusUp : 0); break;
                case TerminalIds.BigDischarge:      // 払うのは呼び手 (5.) が 1 回だけ (paidAll)
                    o.attackAdd += (int)Math.Floor(paidAll * (1f + BigDischargePerRank * env.axCharge)) + (up ? sum : 0); break;      // 〈裁きの雷＋〉は挿した出目も攻撃に
                case TerminalIds.Patch: o.blockAdd += sum; o.shield += env.axShield + (up ? PatchBonusUp : 0); break;
                case TerminalIds.ShieldPush:
                {
                    // 足した分と同じ量のシールドを払う (払える分まで・〈残響〉の 2 回目は残りのシールドから)
                    int left = Math.Max(0, env.shield - o.shieldSpend);
                    int push = Math.Min(left, (int)Math.Floor(left * (up ? ShieldPushPerRankUp : ShieldPushPerRank) * env.axShield));
                    o.attackAdd += sum + push; o.shieldSpend += push; break;
                }
                // 使い捨て
                case TerminalIds.Detonator: o.attackAdd += sum * (up ? DetonatorMulUp : DetonatorMul); break;
                case TerminalIds.Piercer: o.attackAdd += sum * (up ? PiercerMulUp : 1); o.ignoreDefense = true; break;
                case TerminalIds.ReliefValve: o.takenMul *= 1f - ReliefCutPct / 100f; if (up) o.shield += ReliefShieldUp; break;
                case TerminalIds.FirstAid: o.heal += sum * FirstAidMul; if (up) o.overhealToShield += sum * FirstAidMul; break;
                case TerminalIds.FullCharge: o.chargeFill = true; if (up) o.chargeCapAdd += FullChargeCapUp; break;
                case TerminalIds.CoolShot: o.retreatStage++; if (up) o.blockAdd += sum; break;
                case TerminalIds.Copier: o.copyTarget = empty(slot); if (up) o.chargeAdd += sum; break;
                case TerminalIds.BurnOut: o.attackAdd += sum * (up ? BurnOutMulUp : BurnOutMul); o.burnTarget = empty(slot); break;   // 候補は 4. で確かめ済み (無ければ不発)
                // 采配と共通の端子 (2026-11-20)
                case TerminalIds.BurnSlash: o.attackAdd += sum * (up ? BurnSlashMulUp : BurnSlashMul) + st.disposedTotal * Math.Max(BurnSlashMinPer, env.axDeck); break;   // 回数はこのターンより前の分
                case TerminalIds.Reload: if (up) o.chargeAdd += sum; break;      // 戻す 1 枚は 5b
                case TerminalIds.OneCut: o.attackAdd += sum * (up ? OneCutMulUp : OneCutMul); o.ignoreDefense = true; break;      // 攻撃の端子は 1 枚だけ (4.) なので撃は 1 つ
                case TerminalIds.Bell: st.bells++; if (up) o.chargeAdd += sum; break;
                case TerminalIds.Mikiri: o.blockAdd += sum * (env.heavyTurn ? 2 : 1); break;      // 強化は条件 (4 以下) だけ
                case TerminalIds.Crush: o.attackAdd += sum + p1; break;      // 目盛りを割った時の追加は本体の撃の輪 (attackHitIds)
                // 役の軸の端子 (2026-11-21)。 立つ役は Resolve の 5. の頭で決めた o.roleNow (ResolveRoles と同じ判定)
                case TerminalIds.DiceTable: o.chargeAdd += sum; o.rerollDiscountNext += Math.Min(up ? DiceTableCapUp : DiceTableCap, env.axRole); break;
                case TerminalIds.RoleDrop:
                    o.attackAdd += sum + (o.roleNow > 0 ? env.axRole * (up ? RoleDropPerUp : RoleDropPer) * (o.roleNow >= 2 ? 2 : 1) : 0);
                    break;
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
            draw.Clear(); discard.Clear(); hand.Clear(); permanent.Clear(); exiled.Clear(); board.Clear(); _usedLastTurn.Clear(); recallNext = null; copyNext = null; keepNext.Clear(); LastKept.Clear(); _shuffled.Clear(); returnNext.Clear();
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

        /// <summary>〈常設の作業台〉(2026-11-20): 山の常設の端子をすべて場へ出す (戦闘の始め・最初に引くより前)。</summary>
        public void MovePermanentsToBoard()
        {
            for (int i = draw.Count - 1; i >= 0; i--)
                if (TerminalCards.Get(draw[i])?.permanent == true) { permanent.Add(draw[i]); draw.RemoveAt(i); }
        }

        /// <summary>1 枚引く。 山が尽きていれば捨て札を切り直す。 <paramref name="more"/> = 切り直しで追加に引く枚数 (〈永久機関の主軸〉)。</summary>
        private string DrawOne(out int more)
        {
            more = 0;
            if (draw.Count == 0)
            {
                if (discard.Count == 0) return null;
                int n = discard.Count;
                more = BeforeShuffle(discard, exiled, Remaining, state);
                _shuffled.Add(n);
                draw.AddRange(discard); discard.Clear(); Shuffle(draw);
                if (draw.Count == 0) return null;      // 〈無銘の設計図〉が最後の 1 枚を外した
            }
            var id = draw[draw.Count - 1]; draw.RemoveAt(draw.Count - 1);
            return id;
        }

        /// <summary>捨て札を山へ切り直す直前の規則 (2026-11-20・段 1-e・采配の軸)。 本体の引く所 (<see cref="DrawOne"/>) と BOT の先読み
        /// (AutoRunner.Terminals の SampleNextBoard) が同じ関数を呼ぶ。 切り直した回数と枚数を <paramref name="st"/> に数える。
        /// <paramref name="deckTotal"/> = この戦闘の山札の枚数 (山 + 捨て札 + 手札 + 場に残る端子)。
        /// 戻り値 = この切り直しで追加に引く枚数 (〈永久機関の主軸〉)。</summary>
        public static int BeforeShuffle(List<string> discard, List<string> exiled, int deckTotal, TerminalBattleState st)
        {
            st.shuffles++; st.shuffledNow += discard.Count;
            st.extraDrawNext += st.bells;      // 〈巡りの鐘〉: 次のターンに多く引く (このターンの分は BeginTurn で取り出し済み)
            // 〈無銘の設計図〉: 捨て札の基本の端子 (攻撃・防御・充電) を 1 枚この戦闘から外す (山札が 3 枚以下なら外さない)。 先に捨てた方から
            if ((st.deckItems & DeckItems.Blueprint) != 0 && deckTotal > TerminalCards.MinDeckSize)
                for (int i = 0; i < discard.Count; i++)
                    if (TerminalCards.Get(discard[i])?.basic == true) { exiled?.Add(discard[i]); discard.RemoveAt(i); break; }
            // 〈永久機関の主軸〉: 切り直すたび 1 枚多く引く・この戦闘の切り直しが 3 の倍数になるたび、 このターン挿した端子がすべて 2 回働く (〈残響〉と同じ倍化)
            if ((st.deckItems & DeckItems.Engine) != 0)
            {
                if (st.shuffles % TerminalCards.EngineEvery == 0) st.doubleAll = true;
                return 1;
            }
            return 0;
        }

        /// <summary>切り直した枚数のうち、 まだ出来事 (OnDeckShuffle) を配っていない分 (CombatManager が <see cref="TakeShuffled"/> で取り出して配る)。</summary>
        private readonly List<int> _shuffled = new List<int>();
        /// <summary>まだ配っていない切り直し (1 回ごとの枚数) を取り出して空にする。</summary>
        public List<int> TakeShuffled() { var l = new List<int>(_shuffled); _shuffled.Clear(); return l; }

        /// <summary>ヴェスカ〈停滞する時間〉の乱数のキー (専用 ── 他の抽選の連番を動かさない)。</summary>
        public const string StopRngKey = "vesca.stagnant";

        /// <summary>ターンの始め: 前の手札のうち、 挿した端子を捨て札へ送り (手札の持ち越し・<see cref="TerminalCards.HandCarry"/>。
        /// 旧い規則では前の手札を全部捨て、 残す 1 枚と前のターンに止まっていた端子だけを残す ── <see cref="SplitHand"/>)、 手札が size 枚になるまで引く。
        /// 持ち越しで size 枚以上残っていれば引かない (上限で捨てない)。 常設は場へ出して引き直す。
        ///
        /// <para><paramref name="stopChance"/> (0〜1) = ヴェスカ〈停滞する時間〉: <b>このターンに引いた</b>端子 1 枚ごとに、 この確率で止める
        /// (常設を引いた時も・〈先読み〉で多く引く分も)。 止まった端子はこのターン挿せず、 捨て札へ行かず次のターンも手札に残って普通に使える
        /// (引いた物ではないのでもう一度は止まらない・その分 新しく引く枚数が減る)。 止まった常設は場に残り、 次のターンには解ける。
        /// 止まった場所は <see cref="TerminalBattleState.stopped"/> (場の添字) に持つ ── BOT の写しにも乗る。</para></summary>
        public void DrawTurn(int size, string retained = null, float stopChance = 0f)
        {
            // 前のターンの手札を、 残す端子と捨て札へ送る端子に分ける (規則は SplitHand の 1 か所・BOT の先読みも同じ関数を呼ぶ)
            var mode = TerminalCards.CarryMode;
            bool carry = mode != HandCarryMode.Off;      // 〈有効活用〉が捨て札から戻す (All・Pick)
            var keep = new List<string>();
            var toDiscard = new List<string>();
            int heldStopped = SplitHand(hand, board, permanent.Count, state.stopped, retained, _usedLastTurn, mode, keep, toDiscard, keepNext);
            LastKept.Clear();
            if (mode == HandCarryMode.Pick) foreach (var id in keepNext) if (keep.Contains(id)) LastKept.Add(id);   // 印で残した端子 (計装・〈留め金の栞〉ほか)
            state.kept = LastKept.Count > 0 ? new List<string>(LastKept) : null;
            keepNext.Clear();
            state.stopped = null;
            _usedLastTurn.Clear();
            discard.AddRange(toDiscard);
            int carried = keep.Count;
            if (carry) ApplyRecall(recallNext, keep, discard, size);   // 〈有効活用〉: 捨て札から 1 枚戻す (手札の枚数に数える)
            recallNext = null;
            hand.Clear();
            hand.AddRange(keep);
            LastCarriedCount = mode == HandCarryMode.All ? carried : mode == HandCarryMode.Pick ? LastKept.Count : 0;

            // 引く (このターンに引いた物だけが止まる対象)
            var stoppedPerm = new List<int>();   // permanent の添字
            var stoppedHand = new List<int>();   // hand の添字
            int guard = 0, drawn = 0;
            LastDrawnIds.Clear();
            while (hand.Count < size && guard++ < 64)
            {
                var id = DrawOne(out int more);
                if (id == null) break;
                size += more;
                drawn++;
                LastDrawnIds.Add(id);   // [計装 #106]
                bool stop = stopChance > 0f && GameLoop.GameRng.Chance(stopChance, StopRngKey);
                var d = TerminalCards.Get(id);
                if (d != null && d.permanent) { if (stop) stoppedPerm.Add(permanent.Count); permanent.Add(id); continue; }
                if (stop) stoppedHand.Add(hand.Count);
                hand.Add(id);
            }
            // 〈影写し〉の写し (2026-11-14): 引く枚数とは別に、 手札の最後へ 1 枚足す (引く枚数を減らさない・止まる対象でもない)。
            //   使わなければほかの手札と同じに次のターンの頭で捨て札へ行き、 この戦闘の山札を回る (戦闘が終われば消える = BeginBattle で作り直す)
            if (copyNext != null) { hand.Add(copyNext); copyNext = null; }
            // 戻る使い捨て (〈灰から組む機関〉〈緊急再装填〉〈解体屋の袋〉・2026-11-20): 写しと同じく、 引く枚数とは別に手札の最後へ
            if (returnNext.Count > 0) { hand.AddRange(returnNext); returnNext.Clear(); }
            board.Clear(); board.AddRange(permanent); board.AddRange(hand);
            if (stoppedPerm.Count + stoppedHand.Count > 0)
            {
                state.stopped = new bool[board.Count];
                foreach (int p in stoppedPerm) state.stopped[p] = true;
                foreach (int h in stoppedHand) state.stopped[permanent.Count + h] = true;
            }
            LastDrawnCount = drawn; LastHeldCount = heldStopped;
            LastStoppedCount = stoppedPerm.Count + stoppedHand.Count; LastStoppedMains = stoppedPerm.Count;
        }

        /// <summary>直近の <see cref="DrawTurn"/> で 新しく引いた枚数 / 前のターンに止まっていて手札に残った枚数 / 止まった枚数 / うち常設 (計装・画面用)。</summary>
        public int LastDrawnCount { get; private set; }
        /// <summary>[計装 #106] 直近の <see cref="DrawTurn"/> で新しく引いた端子の名前 (常設も含む)。</summary>
        public readonly List<string> LastDrawnIds = new List<string>();
        public int LastHeldCount { get; private set; }
        public int LastStoppedCount { get; private set; }
        public int LastStoppedMains { get; private set; }
        /// <summary>直近の <see cref="DrawTurn"/> で、 手札の持ち越し (<see cref="TerminalCards.HandCarry"/>) で前のターンから残った枚数 (旧い規則では 0・計装・画面用)。</summary>
        public int LastCarriedCount { get; private set; }

        /// <summary>〈有効活用〉(手札の持ち越しの規則) が次のターンの手札に戻す端子 (null = 戻さない)。 配線の後に CombatManager が
        /// <see cref="RecallPick"/> で決め、 次の <see cref="DrawTurn"/> が捨て札から手札へ移して空にする。</summary>
        public string recallNext;

        /// <summary>〈影写し〉が次のターンの手札に足す写し (null = 無し)。 <see cref="FinishTurn"/> が入れ、 次の <see cref="DrawTurn"/> が
        /// 引き終えた手札の最後へ足して空にする (引く枚数とは別 ── 次のターンは手札が 1 枚多い)。 2026-11-14〜 (旧: 捨て札へ入れた)。</summary>
        public string copyNext;

        /// <summary>次のターンの手札に戻す使い捨て (〈灰から組む機関〉〈緊急再装填〉〈解体屋の袋〉・2026-11-20)。 <see cref="FinishTurn"/> が <see cref="DisposeFired"/> で入れ、
        /// 次の <see cref="DrawTurn"/> が〈影写し〉の写しと同じく引き終えた手札の最後へ足して空にする (引く枚数とは別)。</summary>
        public readonly List<string> returnNext = new List<string>();

        /// <summary>残す印 (手札の規則 Pick): 次のターンも手札に残す端子の名前 (このターンに挿さなかった手札の端子だけ)。 配線の後に CombatManager が
        /// <see cref="KeepFromMarks"/> で入れ、 次の <see cref="DrawTurn"/> が読んで空にする。</summary>
        public readonly List<string> keepNext = new List<string>();
        /// <summary>[計装] 直近の <see cref="DrawTurn"/> で残す印で残った端子の名前。</summary>
        public readonly List<string> LastKept = new List<string>();

        /// <summary>残す印 (場の添字ごとの真偽) から、 残す端子の名前を作る。 残せるのは手札の端子 (常設でない) で、 このターンに挿さなかった
        /// (発動していない) 物だけ。 止まっている端子は印が無くても残るので数えない。 本体と BOT が同じ関数を呼ぶ。</summary>
        public static List<string> KeepFromMarks(List<string> board, int permCount, TerminalOutcome o, bool[] marks)
        {
            var l = new List<string>();
            if (board == null || marks == null) return l;
            // 繋ぎの賽 (ADR-0015): 挿した (発動した) 手札の端子も 1 枚だけ残せる。 左から最初の 1 枚
            bool link = GameLoop.EquippedDie.IdOf(GameLoop.GameManager.Instance?.Run) == GameLoop.DieIds.Link;
            bool linkUsed = false;
            for (int s = permCount; s < board.Count && s < marks.Length; s++)
            {
                if (!marks[s]) continue;
                if (o != null && o.fired.Contains(s))
                {
                    if (link && !linkUsed) { linkUsed = true; l.Add(board[s]); }
                    continue;
                }
                if (o?.stopped != null && s < o.stopped.Length && o.stopped[s]) continue;
                l.Add(board[s]);
            }
            return l;
        }

        /// <summary>〈有効活用〉が戻す 1 枚: <paramref name="want"/> が捨て札の添字として正しければそれ、 でなければ最後に捨てた 1 枚 (捨て札の末尾)。 捨て札が空なら null (効果なし)。
        /// 使い捨てで外れた端子・焼き切った端子は捨て札に無いので選べない。 本体と BOT が同じ関数を呼ぶ。</summary>
        public static string RecallPick(List<string> discard, int want)
        {
            if (discard == null || discard.Count == 0) return null;
            return want >= 0 && want < discard.Count ? discard[want] : discard[discard.Count - 1];
        }

        /// <summary>〈有効活用〉の戻す 1 枚を、 残した手札へ足す。 捨て札から 1 枚抜く。 本体の DrawTurn と BOT の先読みが同じ関数を呼ぶ。
        /// 残した手札がすでに <paramref name="size"/> 枚以上でも戻す (2026-11-20〜・説明文に無い制限だったので外した。 旧: 規定の枚数に満たない時だけ)。 戻り値 = 戻したか。</summary>
        public static bool ApplyRecall(string recall, List<string> keep, List<string> discard, int size)
        {
            if (recall == null || !discard.Remove(recall)) return false;
            keep.Add(recall);
            return true;
        }

        /// <summary>前のターンに手札から挿した (発動した) 端子の名前 (<see cref="FinishTurn"/> が控え、 次の <see cref="DrawTurn"/> が捨て札へ送って空にする)。
        /// 使い捨て・火葬の剣は FinishTurn が手札から外すので入れない。 旧い規則では読まない (控えるだけ)。</summary>
        private readonly List<string> _usedLastTurn = new List<string>();

        /// <summary>前のターンに手札から挿した端子の名前 (<paramref name="board"/> の添字 ≥ <paramref name="permCount"/>・使い捨てを除く)。
        /// 本体の <see cref="FinishTurn"/> と BOT の先読みが同じ規則で数える。</summary>
        public static List<string> UsedFromHand(List<string> board, int permCount, TerminalOutcome o)
        {
            var used = new List<string>();
            if (board == null || o == null) return used;
            foreach (int s in o.fired)
            {
                if (s < permCount || s >= board.Count) continue;
                var d = TerminalCards.Get(board[s]);
                if (d == null || d.disposable) continue;
                used.Add(board[s]);
            }
            return used;
        }

        /// <summary>
        /// 前のターンの手札 <paramref name="hand"/> (FinishTurn の後 = 使い捨て・火葬の剣を外した後) を、 次のターンに残す端子 <paramref name="keep"/> と
        /// 捨て札へ送る端子 <paramref name="toDiscard"/> に分ける。 <b>手札の規則の正本</b> ── 本体の <see cref="DrawTurn"/> と BOT の先読み
        /// (AutoRunner.Terminals の SampleNextBoard) が同じ関数を呼ぶ。 <paramref name="hand"/> は書き換えない。
        /// <list type="bullet">
        /// <item><b>全部残す</b> (<paramref name="mode"/> = All): 前のターンに挿した端子 (<paramref name="used"/>) だけを捨て札へ、 残りは手札の順のまま残す。</item>
        /// <item><b>印を付けて残す</b> (Pick・既定): 旧い規則に加えて、 残す印 (<paramref name="marks"/>・挿さなかった端子の名前) の付いた端子を残す (止まっていた端子の後ろ)。
        /// 印が無ければ旧い規則と同じ分け方 (捨てる順も同じ)。</item>
        /// <item><b>旧い規則</b>: 前のターンに止まっていた端子 (ヴェスカ〈停滞する時間〉・<paramref name="prevStopped"/> は前のターンの場の添字) と、
        /// 〈有効活用〉が残す 1 枚 (<paramref name="retained"/>) だけを残し、 残りは手札の順に捨て札へ。</item>
        /// </list>
        /// 戻り値 = 前のターンに止まっていて残った枚数 (計装)。
        /// </summary>
        public static int SplitHand(List<string> hand, List<string> prevBoard, int prevPermCount, bool[] prevStopped, string retained,
                                    List<string> used, HandCarryMode mode, List<string> keep, List<string> toDiscard, List<string> marks = null)
        {
            bool carry = mode == HandCarryMode.All;
            // 前のターンに止まっていた手札の端子 (前のターンの場と、 その時の常設の枚数から読む ── 常設は DrawTurn でしか増えない)。
            // FinishTurn が使い捨て・火葬の剣を手札から抜いて添字がずれるので、 名前で数えて同じ枚数を残す。
            var rest = new List<string>(hand);
            var held = new List<string>();
            if (prevStopped != null && prevBoard != null)
                for (int s = prevPermCount; s < prevBoard.Count && s < prevStopped.Length; s++)
                    if (prevStopped[s]) held.Add(prevBoard[s]);
            int heldCount = 0;
            if (carry)
            {
                foreach (var id in held) if (rest.Contains(id)) heldCount++;
                var usedLeft = used != null ? new List<string>(used) : new List<string>();
                foreach (var id in rest)
                {
                    if (usedLeft.Remove(id)) toDiscard.Add(id);
                    else keep.Add(id);
                }
                return heldCount;
            }
            foreach (var id in held) if (rest.Remove(id)) { keep.Add(id); heldCount++; }
            // 残す印 (Pick): 印の付いた端子を 1 枚ずつ残す (同じ名前が 2 枚あれば印の数だけ)。 Off では印を読まない。 印が無ければ Off と同じ分け方
            var marksLeft = mode == HandCarryMode.Pick && marks != null && marks.Count > 0 ? new List<string>(marks) : null;
            bool kept = false;
            var marked = new List<string>();
            foreach (var id in rest)
            {
                if (!kept && retained != null && id == retained) { kept = true; continue; }
                if (marksLeft != null && marksLeft.Remove(id)) { marked.Add(id); continue; }
                toDiscard.Add(id);
            }
            if (kept) keep.Add(retained);
            keep.AddRange(marked);
            return heldCount;
        }

        /// <summary>ターンの終わり: 結果を山札へ反映する (使い捨て・影写し (次のターンの手札へ)・火葬の剣)。 戻り値 = 次のターンも手札に残す端子 (無ければ null)。</summary>
        public string FinishTurn(TerminalOutcome o)
        {
            _usedLastTurn.Clear();
            _usedLastTurn.AddRange(UsedFromHand(board, permanent.Count, o));
            string retain = o.retainTarget >= 0 && o.retainTarget < board.Count ? board[o.retainTarget] : null;
            // 〈影写し〉: 写しは次のターンの手札に入る (DrawTurn が引いた後に足す・2026-11-14。 旧: 捨て札へ入れたので、 山札を一巡しないと引けなかった)
            copyNext = o.copyTarget >= 0 && o.copyTarget < board.Count ? board[o.copyTarget] : null;
            string burn = o.burnTarget >= 0 && o.burnTarget < board.Count ? board[o.burnTarget] : null;
            // 使った使い捨てを手札から外す (この戦闘には戻らない・戻る物は次のターンの手札へ)
            DisposeFired(board, o, hand, exiled, returnNext, state);
            if (burn != null && hand.Remove(burn)) exiled.Add(burn);
            return retain;
        }

        /// <summary>使った使い捨てを手札から外す (この戦闘から外す・2026-11-20 に共通化)。 本体の <see cref="FinishTurn"/> と BOT の先読み (SampleNextBoard) が同じ関数を呼ぶ。
        /// 戻る使い捨ては <paramref name="returnNext"/> (次のターンの手札) へ:
        /// 〈緊急再装填〉の対象 (<see cref="TerminalOutcome.reloadTargets"/>)・〈灰から組む機関〉(1 ターンに 1 枚・場の左から・使い捨て 1 枚につきこの戦闘で 1 回 ──
        /// 一度戻った名前は <see cref="TerminalBattleState.ashReturned"/> に控え、 次に使えば外れる)・〈解体屋の袋〉(この戦闘の使い捨ての回数が 3 の倍数をまたぐたび、 最後に外れた使い捨てを 1 枚)。</summary>
        public static void DisposeFired(List<string> board, TerminalOutcome o, List<string> hand, List<string> exiled, List<string> returnNext, TerminalBattleState st)
        {
            bool ash = (st.deckItems & DeckItems.Ash) != 0, ashDone = false;
            foreach (int s in o.fired)
            {
                if (s < 0 || s >= board.Count) continue;
                string id = board[s];
                var d = TerminalCards.Get(id);
                if (d == null || !d.disposable || !hand.Remove(id)) continue;
                if (o.reloadTargets.Contains(s)) { returnNext.Add(id); continue; }
                // 〈灰から組む機関〉: 一度戻った名前なら外す (控えを 1 つ消す)。 まだならこのターンの 1 枚目を戻す
                if (ash && !st.ashReturned.Remove(id) && !ashDone) { ashDone = true; st.ashReturned.Add(id); returnNext.Add(id); continue; }
                exiled.Add(id);
            }
            if ((st.deckItems & DeckItems.Bag) != 0 && o.disposedNow > 0)
            {
                int n = st.disposedTotal / TerminalCards.BagEvery - (st.disposedTotal - o.disposedNow) / TerminalCards.BagEvery;
                for (int k = 0; k < n; k++)
                {
                    int j = exiled.FindLastIndex(x => TerminalCards.Get(x)?.disposable == true);
                    if (j < 0) break;
                    returnNext.Add(exiled[j]); exiled.RemoveAt(j);
                }
            }
        }

        // ── 判断の間に手札を動かす (戦術消耗品・2026-11-20) ──
        //   手札が変わったら場 (常設 + 手札) を作り直す。 止まった端子の印 (ヴェスカ〈停滞する時間〉) は、 残る端子の分だけ写す (新しく入った端子は止まらない)。
        //   BOT の先読み (AutoRunner.Terminals の SampleNextBoard) はこの後の山札・捨て札・手札をそのまま読むので、 同じ状態から次のターンを見積もる。
        //   「次のターンの手札に足す」口 (copyNext 1 枚・recallNext 1 枚・keepNext・extraDrawNext) は使わない ── どれもこのターンの手札へ直に入れる。

        /// <summary>〈入れ替えの符〉: 今の手札をすべて捨て札へ送り、 同じ枚数を山札から引き直す (山札が尽きたら捨て札を切り直す ── いま捨てた端子も混ざる)。
        /// 常設を引いたら場に残し、 手札の枚数には数えない (<see cref="DrawTurn"/> と同じ)。 戻り値 = 引き直した枚数。</summary>
        public int SwapHand()
        {
            int n = hand.Count, perm0 = permanent.Count;
            if (n == 0) return 0;
            discard.AddRange(hand); hand.Clear();
            int got = DrawInto(n);
            RebuildBoard(perm0, 0);
            return got;
        }

        /// <summary>手札へ n 枚引き足す (〈帯革の薬嚢〉)。 戻り値 = 引いた枚数。</summary>
        public int DrawIntoHand(int n)
        {
            int perm0 = permanent.Count, keep = hand.Count;
            int got = DrawInto(n);
            RebuildBoard(perm0, keep);
            return got;
        }

        /// <summary>手札へ id の写しを copies 枚足す (〈複製の水銀〉・この戦闘の間だけ ── 次の戦闘は <see cref="BeginBattle"/> がランの山札から作り直す)。</summary>
        public void AddToHand(string id, int copies)
        {
            if (string.IsNullOrEmpty(id) || copies <= 0) return;
            int perm0 = permanent.Count, keep = hand.Count;
            for (int i = 0; i < copies; i++) hand.Add(id);
            RebuildBoard(perm0, keep);
        }

        private int DrawInto(int n)
        {
            int got = 0, guard = 0, want = hand.Count + n;
            while (hand.Count < want && guard++ < 64)
            {
                var id = DrawOne(out int more);
                if (id == null) break;
                want += more;
                got++;
                var d = TerminalCards.Get(id);
                if (d != null && d.permanent) { permanent.Add(id); continue; }
                hand.Add(id);
            }
            return got;
        }

        /// <summary>場を作り直す。 <paramref name="perm0"/> = 前の常設の枚数 / <paramref name="keepHand"/> = 前の手札のうち、 同じ位置に残っている先頭の枚数 (止まった印を写す)。</summary>
        private void RebuildBoard(int perm0, int keepHand)
        {
            var old = state.stopped;
            board.Clear(); board.AddRange(permanent); board.AddRange(hand);
            if (old == null) return;
            var st = new bool[board.Count]; bool any = false;
            for (int p = 0; p < perm0 && p < old.Length; p++) { st[p] = old[p]; any |= old[p]; }
            for (int h = 0; h < keepHand && perm0 + h < old.Length; h++) { bool f = old[perm0 + h]; st[permanent.Count + h] = f; any |= f; }
            state.stopped = any ? st : null;
        }

        /// <summary>山札の残り (山 + 捨て札 + 手札 + 場)。 画面用。</summary>
        public int Remaining => draw.Count + discard.Count + hand.Count + permanent.Count;
    }
}
