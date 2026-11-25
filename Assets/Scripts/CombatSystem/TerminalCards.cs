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

    /// <summary>山札の軸の品のうち、 端子の解決・山札の出し入れを変える品 (2026-11-21・full-item-rework-ledger §⑦)。
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
        Switchboard = 1 << 4,  // 仕込みの切替器
        Mold = 1 << 5,         // 打ち抜きの金型
        Crucible = 1 << 6,     // 再利用のるつぼ
        Blueprint = 1 << 7,    // 無銘の設計図
        Seamless = 1 << 8,     // 継ぎ目のない導線
        Folded = 1 << 9,       // 取っておきの銀弾
        Bag = 1 << 10,         // 解体屋の袋
        Workbench = 1 << 11,   // 常設の作業台
        Ash = 1 << 12,         // 廃材組みの蒸気機関
        Engine = 1 << 13,      // 永久機関の主軸
        Tongs = 1 << 14,       // 焼き入れの挟み (撃ごとの攻撃の分・敵へのダメージは DeckEffects.cs)
        Pride = 1 << 15,       // 切り捨ての半券
        Broom = 1 << 16,       // 灰払いの箒
    }

    /// <summary>役の軸の品のうち、 端子の解決 (<see cref="TerminalCards.Resolve"/> の 6d) の中で効く物 (2026-11-21・full-item-rework-ledger §⑧)。
    /// 戦闘の始めに所持品から <see cref="TerminalBattleState.roleItems"/> へ入れる (CombatManager.BeginTerminalDeck・<see cref="TerminalCards.RoleItemsOf"/>)。
    /// 役は振った 3 つの目で決まるので、 Resolve が同じ判定 (<see cref="TerminalCards.RoleOf"/>) で立つ役を出して効かせる (本体・画面の見込み・BOT の配線と先読みが一致する)。</summary>
    [Flags]
    public enum RoleItems
    {
        None = 0,
        WoodTag = 1 << 0,      // 潮目の木札
        NotchBoard = 1 << 1,   // 符丁の刻み板
        Chipped = 1 << 2,      // 欠け目の見立て (防御の無視の分・目盛りの削りは RoleEffects.cs)
        Gear = 1 << 3,         // 追い目の賽筒
        Clapper = 1 << 4,      // 揃いの拍子木 (与ダメージの分・手札と無料の振り直しは RoleEffects.cs)
        Thunder = 1 << 5,      // 轟きの大賽
        Bloodmark = 1 << 6,    // 連勝の符丁
        Pouch = 1 << 7,        // 擦り切れた賽袋 (振り直したターンのブロックの分・無料の振り直しは CombatManager.FreeRerollsLeft)
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
        /// <summary>軸 (AxisRules の名前)。 null = 軸なし。 報酬に出る確率を偏重 (軸の品の数) で寄せる (強さは戦闘の状態で決まる・2026-11-22〜 偏重は読まない)。</summary>
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
        /// <summary>このターンに〈打ち直し〉で出目が増えた回数 (<see cref="TerminalCards.FoundryMaxPerTurn"/> まで・<see cref="BeginTurn"/> で 0)。</summary>
        public int foundryFiredTurn;
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
        /// (中は書き換えない ── 写しと共有してよい)。 〈留め金の栞〉〈取っておきの銀弾〉と出来事 OnKeptTerminalUse が読む。</summary>
        public List<string> kept;
        /// <summary>采配の品の旗 (戦闘の始めに所持品から・CombatManager.BeginTerminalDeck)。</summary>
        public DeckItems deckItems;
        /// <summary>この戦闘で使い捨ての端子を使った回数 (<see cref="TerminalCards.Resolve"/> が数える・減らない)。 〈再利用のるつぼ〉〈解体屋の袋〉が読む。</summary>
        public int disposedTotal;
        /// <summary>毎ターンの手札 +N (〈整理された工具箱〉・戦闘の始めに決める)。</summary>
        public int handBonus;
        /// <summary>残す印で残した端子を発動したターンが続いた数 (〈取っておきの銀弾〉・<see cref="TerminalCards.Resolve"/> が数える)。</summary>
        public int keptStreak;
        /// <summary>〈解体屋の袋〉がこの戦闘で使い捨てを戻した回数 (<see cref="TerminalCards.BagMax"/> まで・<see cref="TerminalDeckState.DisposeFired"/>)。</summary>
        public int bagReturns;
        /// <summary>〈永久機関の主軸〉がこのターンに多く引かせた枚数 (<see cref="TerminalCards.EngineDrawMax"/> まで・<see cref="BeginTurn"/> で 0)。</summary>
        public int engineDrawTurn;
        /// <summary>この戦闘で〈巡りの鐘〉を使った回数 (切り直すたび、 次のターンに多く引く枚数・<see cref="TerminalDeckState.BeforeShuffle"/>)。</summary>
        public int bells;
        /// <summary>賽目の品の旗 (戦闘の始めに所持品から・CombatManager.BeginTerminalDeck・2026-11-21)。</summary>
        public RoleItems roleItems;
        /// <summary>充電の品の旗 (戦闘の始めに所持品から・CombatManager.BeginTerminalDeck・<see cref="ChargeCore.ItemsOf"/>・2026-11-21)。</summary>
        public ChargeItems chargeItems;
        /// <summary>毒の品の旗 (戦闘の始めに所持品から・CombatManager.BeginTerminalDeck・<see cref="PoisonCore.ItemsOf"/>・2026-11-21)。</summary>
        public PoisonItems poisonItems;
        /// <summary>シールドの品の旗 (戦闘の始めに所持品から・CombatManager.BeginTerminalDeck・<see cref="ShieldCore.ItemsOf"/>・2026-11-21)。</summary>
        public ShieldItems shieldItems;
        /// <summary>出血の品の旗 (戦闘の始めに所持品から・CombatManager.BeginTerminalDeck・<see cref="BleedCore.ItemsOf"/>・2026-11-21)。</summary>
        public BleedItems bleedItems;
        /// <summary>臨界の品の旗 (戦闘の始めに所持品から・CombatManager.BeginTerminalDeck・<see cref="HeatCore.ItemsOf"/>・2026-11-21)。</summary>
        public HeatItems heatItems;
        /// <summary>この戦闘で役が成立した回数 / 直前のターンまで同じ目の役が続いて成立したターンの数 / 直前のターンに立った役の目 (<see cref="TerminalCards.FaceOfRole"/>・0 = なしか連なり)
        /// (CombatManager.ResolveRoles がターンの役の後に書く・2026-11-21・同じ目にしたのは 2026-11-22)。
        /// 〈符丁の刻み板〉〈連勝の符丁〉〈揃いの拍子木〉〈万象の賽盆〉が読む。 Resolve の中では、 このターンの役はまだ入っていない。</summary>
        public int roleCount, roleStreak, roleLastFace;
        /// <summary>この戦闘の何ターン目か (<see cref="BeginTurn"/> が 1 つ進める・戦闘の始めは 0)。 挑戦カード〈初太刀〉の「1 ターン目」が読む (2026-11-22)。</summary>
        public int turn;

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
            return c;
        }

        /// <summary>ターンの始めに呼ぶ (持ち越しを今のターンへ移す)。 戻り値 = このターンに多く引く枚数。</summary>
        public int BeginTurn()
        {
            turn++;
            ignoreCond = ignoreCondNext; ignoreCondNext = false;
            doubleAll = false;
            shuffledNow = 0; engineDrawTurn = 0;
            foundryFiredTurn = 0;
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
        /// <summary>軸の端子が読む戦闘の状態 (2026-11-22・旧: 偏重 = 軸の品の数 axCrit ほか 7 つ・§24): この戦闘の会心の回数 (〈一閃〉〈弱点看破〉・Crit.TotalKey) /
        /// 前のターンに【ジャスト防御】が成立した (〈装甲板〉) / 前のターンかこのターンに爆発した (〈火種〉) / この戦闘で HP を 1 以上失った (〈応急箱〉の発動の条件)。
        /// 過充電 (〈招雷〉) は <see cref="charge"/> と <see cref="ChargeCore.OverchargeAt"/> で見る。</summary>
        public int critsBattle;
        public bool justGuardLast, heatHot, hpLostBattle;
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
        /// <summary>このターンが敵の大技か (兆し読みの賽)。</summary>
        public bool heavyTurn;
        /// <summary>充電を払う量から引く量 (〈過負荷チューナー〉の過充電の間 <see cref="ChargeCore.TunerCut"/>・<see cref="ChargeCore.CostNow"/>)。 端子と再起動・オーバークロック・〈逆さ避雷針〉に掛ける。 「全部払う」端子には掛けない。</summary>
        public int chargeCostCut;
        /// <summary>このターン、 配線より前に充電を使った回数 (振り直し・ctx の ChargeSpendsThisTurnKey)。 〈灼線鞭〉の「2 回以上」が読む。</summary>
        public int chargeSpendsTurn;
        /// <summary>攻撃の種類の端子に挿せない (〈シュヴァリエのレイピア〉のコントラタック中・2026-11-20)。 挿した出目は「0.」で外す (止まった端子と同じ)。</summary>
        public bool noAttack;
        /// <summary>〈凶運〉で封じた役 (ctx.sealedRoleMask・立っても効かない) / このターン、 2 個そろいをゾロ目として扱う (〈万象の賽盆〉・振り直しの後に決める・2026-11-21)。
        /// 立つ役は <see cref="TerminalCards.RoleOf"/> (CombatManager.ResolveRoles と同じ判定)。</summary>
        public int sealedRoleMask;
        public bool pairAsTriple;
        /// <summary>クェシナの怠惰で各ダイスから引いた量 (null = 引いていない・2026-11-23)。 立つ役は引く前の目 (<see cref="TerminalCards.RoleDiceOf"/>) で決める。 CombatManager.TerminalEnvNow が入れる。</summary>
        public int[] roleDiceCut;
        /// <summary><see cref="roleDiceCut"/> を引いた後の目 (この目のダイスにだけ足し戻す・BOT が仮の目で解決を試す時は足さない)。</summary>
        public int[] roleDiceAt;
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
        /// <summary>攻撃の種類の端子に挿した出目の合計 (〈号令の剣〉〈歯車の軸〉の出目 +N は入る・〈剛腕〉の倍・端子の倍率・基礎攻撃は入らない・【再起動】〈残響〉で増えない・2026-11-22 neon 収束)。
        /// 配線のとき (攻撃の前) の臨界はこの半分 (<see cref="HeatCore.EndGain"/>)。</summary>
        public int attackPips;
        /// <summary>このターンに発動した防御の種類の端子の数 (【再起動】の分も 1 回・2026-11-22 〈受け流しの扇〉)。</summary>
        public int blockTerminals;
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
        /// <summary>充電の支払い 1 回ずつ (合計 = <see cref="chargeSpend"/>)。 本体は 1 回ごとに ctx.ConsumeCharge を呼ぶ (「充電を使うたび」の品が回数を数える)。</summary>
        public readonly List<int> chargePays = new List<int>();
        /// <summary>【再起動】した端子 (場の添字・〈二股の導線〉〈逆さ避雷針〉〈灼線鞭〉の順・同じ端子が 2 回入ることもある)。 / 〈二股の導線〉で再起動した端子 (−1 = なし)。</summary>
        public readonly List<int> rebooted = new List<int>();
        public int rebootSlot = -1;
        /// <summary>【オーバークロック】(〈雷鳴の太鼓〉): このターンの最初の 1 撃の最終与ダメージ +<see cref="ChargeCore.OverclockFinalPct"/>%・敵の防御を全部無視 (本体の撃の輪が掛ける)。</summary>
        public bool overclock;
        /// <summary>このターンの被ダメージから引く量 (〈逆さ避雷針〉・本体が ctx.playerFlatDamageReduction へ足す)。</summary>
        public int takenFlatCut;
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
        /// <summary>このターンの攻撃が無視する敵の防御 (〈継ぎ目のない導線〉・本体が ctx.playerArmorPen へ足す)。</summary>
        public int armorPen;
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
        public List<string> attackLog = TerminalCards.NoLog;
        /// <summary><see cref="attackLog"/> を書くか (本体の実ターンの解決だけ・BOT の写しと画面の見込みは書かない・2026-11-21 速度: 1 撃ごとの文字列が BOT の評価で毎回組まれていた)。</summary>
        public bool logAttack;
        /// <summary>攻撃の端子 1 枚ごとの攻撃の量と挿した出目の数 (場の順・ADR-0016: 攻撃は端子 1 枚につき 1 回当たる)。
        /// attackAdd のうちここに入らない分 (連係・変圧・溜めた攻撃・賽の加算) は、 本体が最初の 1 撃に入れる。</summary>
        public readonly List<int> attackHits = new List<int>(), attackHitDice = new List<int>();
        /// <summary>攻撃の端子 1 枚ごとの名前 (<see cref="attackHits"/> と同じ並び・2026-11-20)。 本体の撃の輪が「この撃が〈崩し斬り〉か」を読む。</summary>
        public readonly List<string> attackHitIds = new List<string>();
        /// <summary>攻撃の端子 1 枚ごとの場の添字 (<see cref="attackHits"/> と同じ並び・【再起動】の撃は元の端子・2026-11-21)。 5f が挿した目を読む。</summary>
        public readonly List<int> attackHitSlot = new List<int>();
        /// <summary>撃ごとに、 その撃が当たった直後に敵へ与える出血 (<see cref="attackHits"/> と同じ並び・5f = <see cref="BleedCore.ApplyToTerminals"/>・2026-11-21)。</summary>
        public readonly List<int> attackHitBleed = new List<int>();
        /// <summary>配線で決まる臨界 (5g・<see cref="HeatCore.ApplyToTerminals"/>)。 1 つが 1 回の「臨界を得る」(本体は 1 つずつ Kw.AddHeat へ渡す・〈焚き足しの炭〉はその回数を数える)。
        /// 端子が出す臨界 (<see cref="heat"/>) は別。 / 〈燧石の指輪〉: このターンの臨界の獲得が 2 倍 (本体が ctx へ立ててから臨界を足す)。</summary>
        public readonly List<int> heatGains = new List<int>();
        public bool heatDouble;
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

        /// <summary>最後の攻撃の端子 (2026-11-21・作者の決定): <paramref name="id"/> が攻撃の種類 (<see cref="TerminalKind.Attack"/>) で、
        /// 残りの山 (<paramref name="a"/>〜<paramref name="e"/> ── <paramref name="id"/> 自身の 1 枚は含めない) に攻撃の種類の端子が 1 枚も無いなら true。
        /// true なら、 その端子は山札から消さない: 戦闘から外す効果 (〈無銘の設計図〉・使い捨て・〈火葬の剣〉) は外す代わりに捨て札へ戻し、
        /// ランの山札から抜く所 (店・休憩・イベント・<c>GameManager.CanRemoveTerminalAt</c>) は抜けない。 消える経路はすべてこの判定を通す。
        /// 旧: 〈無銘の設計図〉が攻撃をすべて外し、 攻撃の端子の無い山札で戦闘が終わらなかった (ラン #1012542・〈防御〉〈防御陣〉〈打ち直し〉の 3 枚で 250 ターン)。</summary>
        public static bool KeepsLastAttack(string id, List<string> a, List<string> b = null, List<string> c = null, List<string> d = null, List<string> e = null)
            => Get(id)?.kind == TerminalKind.Attack && !HasAttack(a) && !HasAttack(b) && !HasAttack(c) && !HasAttack(d) && !HasAttack(e);

        private static bool HasAttack(List<string> l)
        {
            if (l != null) foreach (var x in l) if (Get(x)?.kind == TerminalKind.Attack) return true;
            return false;
        }

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
        /// <summary>〈打ち直し〉で出目が増えるのは 1 ターンにこの回数まで (2026-11-21・作者の決定)。 溜まりの余りは次のターンへ持ち越す。
        /// 旧: 届いた回数だけ増えたので、 増えた出目がさらに溜まりを速めて複利で膨らんだ (ラン #1012542 で出目 2,600 万)。 今は 1 ターンに +1 の直線。</summary>
        public const int FoundryMaxPerTurn = 1;
        public const float AmplifierPerPip = 0.16f, ResonatorMul = 1.5f, VacuumCritMult = 2.0f;
        public const int CounterEvery = 10; public const float CounterPct = 0.10f;
        public const int TwinShotMul = 3, DetonatorMul = 3, FirstAidMul = 1, HealMul = 1, BatteryBonus = 2, InsulatorBonus = 4;
        /// <summary>〈火葬の剣〉: 挿した出目の合計に掛けて攻撃に足す倍率 (2026-11-14・作者の決定)。</summary>
        public const int BurnOutMul = 2;
        public const int HarvesterMaxHp = 2, WhetGain = 1, LootGold = 5;
        /// <summary>〈剥ぎ取り〉: 撃破したターンに、 戦闘後の端子の選択肢が増える数 (RunState.terminalExtraChoices に足す)。</summary>
        public const int StripperChoices = 1;
        /// <summary>軸の端子 (2026-11-22・偏重から戦闘の状態へ・§24): 〈一閃〉〈弱点看破〉この戦闘の会心 1 回あたりの会心倍率 / 〈裁きの雷〉このターンの充電の消費 1 回あたりの倍率 /
        /// 〈重装突撃〉攻撃に回す今のシールドの割合。 *Up = 強化した端子。</summary>
        public const float SightPerCrit = 0.08f, SightPerCritUp = 0.10f, VitalPerCrit = 0.08f, VitalPerCritUp = 0.10f, BigDischargePerSpend = 0.25f, ShieldPushPct = 0.50f, ShieldPushPctUp = 0.75f;
        public const float BloodTapPct = 0.05f;
        /// <summary>〈鉤爪〉出血 = 敵の出血 ÷ N (下限 Min・強化は MinUp) / 〈毒霧〉毒 +出目 +Base (敵に毒があれば Poisoned) / 〈火種〉臨界 +出目 +Base (前のターンかこのターンに爆発していれば Hot) /
        /// 〈招雷〉充電 +出目 +Base (過充電なら Over) / 〈装甲板〉シールド +Base (前のターンに【ジャスト防御】なら Just)。 強化の上乗せは *BonusUp。</summary>
        public const int BarbDiv = 4, BarbMin = 2, BarbMinUp = 3, MisterBase = 3, MisterPoisoned = 5, StokerBase = 5, StokerHot = 10, LeakBase = 3, LeakOver = 6, PatchBase = 4, PatchJust = 8;
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

        // ===== 山札の軸の品の数値 (2026-11-21・full-item-rework-ledger §⑦・未測定) =====
        /// <summary>〈焼き入れの挟み〉使い捨て 1 枚につき各撃 +N / 〈留め金の栞〉各撃 +N / 〈補助歯車〉ブロック・1 ターンの回数 / 〈空回りの錘〉多く引く上限 /
        /// 〈切り捨ての半券〉各撃 +N・ブロック・1 ターンの回数 / 〈灰払いの箒〉ブロック・多く引く枚数・引くのに要る 1 ターンの使い捨ての回数 / 〈早回しのゼンマイ〉各撃の上限・ブロック /
        /// 〈仕込みの切替器〉要る枚数・多く引く枚数 / 〈打ち抜きの金型〉出力・補助のブロック / 〈整理された工具箱〉山札の枚数・手札 / 〈組み置きの配線束〉1 ターン目に多く引く枚数。</summary>
        // 2026-11-22 neon 収束: 〈切り捨ての半券〉+2・+3 → +4・+5 / 〈灰払いの箒〉ブロック +1 → +3 (使うたび・引く条件は 1 ターン 2 回のまま)。
        // 2026-11-22 final4: 〈灰払いの箒〉ブロック +3 → +4・引く条件 1 ターン 2 回 → 1 回 (引くのは 1 ターン 1 回まで) / 〈仕込みの切替器〉4 枚 → 3 枚。
        // 2026-11-22 final5: 〈灰払いの箒〉ブロック +4 → +3・引く条件 1 ターン 1 回 → 2 回。
        public const int TongsAttack = 1, ClaspAttack = 6, ClaspBlock = 4, AuxGearBlock = 6, AuxGearMax = 3, IdleWeightDrawMax = 2, PrideAttack = 4, PrideBlock = 5, PrideMax = 1,
            BroomBlock = 3, BroomDrawMax = 1, BroomDrawNeed = 2, RewindMax = 10, RewindBlock = 6, SwitchboardNeed = 3, SwitchboardItemDraw = 1, MoldBonus = 6, MoldSupportBlock = 8,
            ToolboxMaxDeck = 12, ToolboxHand = 1, SparePlugDraw = 2;
        /// <summary>〈再利用のるつぼ〉使い捨ての累計 1 回あたり・上限 / 〈継ぎ目のない導線〉補助 1 枚あたりの各撃 +N・防御の無視 / 〈解体屋の袋〉間隔・1 戦闘の上限 /
        /// 〈常設の作業台〉常設 1 枚あたりの各撃 +N・ブロック / 〈廃材組みの蒸気機関〉要る回数 / 〈永久機関の主軸〉倍化の間隔・1 ターンに多く引く上限。</summary>
        // 2026-11-22 neon 収束 (neon の「旧」は Phase 4 較正の前の値だったので、 今の値に同じ比で掛けた): 〈再利用のるつぼ〉×2 → ×3 / 〈常設の作業台〉各撃 +3 → +5・ブロックは +3 のまま・最終 +10% → +13% / 〈廃材組みの蒸気機関〉6% → 9%。
        // 2026-11-22 final4: 〈再利用のるつぼ〉×3・上限 16 → ×4・上限なし / 〈常設の作業台〉+5・+3・+13% → +7・+5・+18% / 〈廃材組みの蒸気機関〉9%・上限 60% → 15%・上限なし / 〈永久機関の主軸〉3 回目ごと → 2 回目ごと。
        // 2026-11-22 final5: 〈再利用のるつぼ〉×4 → ×2 / 〈常設の作業台〉+7・+5・+18% → +5・+3・+13% / 〈廃材組みの蒸気機関〉2 回・15% → 3 回・9% (上限なしのまま)。
        public const int CruciblePer = 2, SeamlessAttack = 7, SeamlessPen = 8, SeamlessBlock = 3, BagEvery = 4, BagMax = 5,
            WorkbenchPer = 5, WorkbenchBlock = 3, AshNeed = 3, EngineEvery = 2, EngineDrawMax = 2;
        /// <summary>〈取っておきの銀弾〉与ダメージ % の池 / 〈常設の作業台〉常設 1 枚あたりの最終与ダメージ / 〈廃材組みの蒸気機関〉使い捨て 1 回あたりの最終与ダメージ。</summary>
        public const float FoldedPerStreak = 0.25f, WorkbenchFinalPct = 0.25f, AshPer = 0.09f, SeamlessFinalPct = 0.25f;   // answer14: 作業台 13% → 25%・継ぎ目 補助 1 枚につき最終 +25%
        // ===== 賽目 (役の軸) の品の数値 (2026-11-21・段 1-f・item-overhaul-spec §5・未測定) =====
        /// <summary>〈潮目の木札〉4・5 の役のターン 各撃の攻撃 +N・ブロック +N / 〈符丁の刻み板〉3・4 の役のターン 各撃の攻撃 + min(上限, 役の回数 × N) (ゾロ目なら 2 倍) /
        /// 〈欠け目の見立て〉1・2 の役のターン 防御の無視 / 〈追い目の賽筒〉振り直し＋3 の役のターン 各撃 +N / 〈轟きの大賽〉ブロック (full-item-rework-ledger §⑧・未測定)。</summary>
        public const int WoodTagAttack = 1, WoodTagBlock = 2, NotchBoardPer = 1, NotchBoardMax = 6, ChippedPen = 12, GearAttack = 8, GearAttackHigh = 10, ThunderBlock = 10, ThunderRerollBlock = 10, PouchBlock = 5;   // answer12: 歯車 +5 → +7・雷鳴の振り直しの役 ブロック +8・賽袋 振り直しブロック +5
          // answer9: 歯車 3 の役 +6 → どの役も +5 (3 の役かゾロ目は +10)・雷鳴 ブロック +4 → +10
        /// <summary>役の軸の品が働く役の目 (2026-11-22・どの役でも → 目を指定)。 *Faces = 目の集合 (ビット 1 &lt;&lt; 目・<see cref="FaceIn"/>)。
        /// 2026-11-22 neon 収束で広げた: 木札 5 → 4・5 / 刻み板 4 → 3・4 / 見立て 1 → 1・2 / 導管 2 → 1・2・3。 〈小目の骨牌〉は RoleEffects.cs。</summary>
        public const int WoodTagFaces = 1 << 4 | 1 << 5, NotchBoardFaces = 1 << 3 | 1 << 4, ChippedFaces = 1 << 1 | 1 << 2, GearFace = 3, ConduitFaces = 1 << 1 | 1 << 2 | 1 << 3;
        /// <summary>目 <paramref name="face"/> (1〜6・連なりは 0 でどの集合にも入らない) が集合 <paramref name="faces"/> に入るか。</summary>
        public static bool FaceIn(int faces, int face) => face > 0 && ((faces >> face) & 1) != 0;
        /// <summary>〈揃いの拍子木〉与ダメージ % の池 / 〈轟きの大賽〉最終与ダメージ / 〈連勝の符丁〉2 個そろいの役のターン 役の回数 1 回あたりの最終与ダメージ・上限
        /// (2026-11-22 neon 収束: 3・4 の役 × 5% → すべての役 × 8% → final4: 2 個そろいだけ (ゾロ目・連なりでは働かない) × 10%・上限 50% → final5: × 7%・上限なし)。</summary>
        public const float ClapperPct = 0.25f, ThunderFinalPct = 0.45f, ThunderRerollPct = 0.35f, BloodmarkPer = 0.08f;   // answer14: 拍子木 12% → 25%・雷鳴 振り直しの他の役 30% → 35%   // answer12: 雷鳴 振り直したターンの他の役 +30%・符丁 7% → 8%
          // answer9: 雷鳴 +20% → +45%
        /// <summary>〈連勝の符丁〉が働く役 = 2 個そろい (ゾロ目・連なりでは働かない・2026-11-22 final4)。 本体と BOT が同じ判定。</summary>
        public static bool BloodmarkFires(RoleKind k) => true;   // answer9: どの役でも (旧 2 個そろいだけ)
        /// <summary>〈賽振り台〉次のターンの最初の振り直しの費用から引く上限 (min(上限, 役の回数)) / 〈役落とし〉役が成立したターン 攻撃 + 役の回数 × N。
        /// 役の回数 = この戦闘で役が成立した回数 (このターンの役を含む・役の軸の品と同じ数え方・台帳 §⑧)。 *Up = 強化した端子。</summary>
        public const int DiceTableCap = 6, DiceTableCapUp = 8, RoleDropPer = 3, RoleDropPerUp = 4;
        // ===== 采配と共通の端子 (2026-11-20・段 1-e・未測定) =====
        /// <summary>〈焼却斬り〉出目の倍率 / 使い捨て 1 回あたり (2026-11-22・旧 max(2, 采配)) / 〈一刀両断〉出目の倍率 / 〈歯車の軸〉挿した目 1 つあたり / 〈崩し斬り〉最大 HP の %。 *Up = 強化した端子。</summary>
        public const int BurnSlashMul = 2, BurnSlashMulUp = 3, BurnSlashPer = 2, BurnSlashPerUp = 3, OneCutMul = 3, OneCutMulUp = 4, GearPip = 2, GearPipUp = 3, CrushPct = 5, CrushPctUp = 6;

        // ===== 強化した端子 (＋) の数値 (2026-11-20・作者承認の表 Captures/overhaul/work/terminal_upgrades_draft.md・未測定) =====
        //   強化した端子は基の端子の定義を写して、 表の分だけ変えた別の端子 (TerminalIds.Upgraded)。 効果の式は Effect の up の枝。
        /// <summary>基本 (攻撃・防御・充電)・〈後の先〉〈攻防一体〉のブロック: 出目 +1。 / 〈無形の構え〉〈有効活用〉のブロック: 出目 +2。</summary>
        public const int UpPip = 1, UpPipBlock2 = 2;
        public const int DetectorBonusUp = 2, DischargeCostUp = 2, RelayPerFiredUp = 10, OscPerStreakUp = 7, TwinPairBonusUp = 3;
        public const int FuseBlockUp = 10, InsulatorBonusUp = 5, BreakerCostUp = 5, RectifierBonusUp = 2, BatteryBonusUp = 3, HealShieldUp = 2;
        public const int CapacitorNeedUp = 10, ArresterNeedUp = 7, SwitchboardDrawUp = 3;
        public const float VacuumCritMultUp = 2.5f;
        public const int CoolantNeedUp = 10, CounterEveryUp = 8, TwinShotMulUp = 4, HarvesterMaxHpUp = 3, WhetLowHpAttackUp = 4, LootGoldUp = 7, StripperChoicesUp = 2;
        public const int MisterBonusUp = 2, PoisonScaleAttackUp = 4, StokerBonusUp = 3, AfterHeatPerBurstUp = 3, LeakBonusUp = 2, PatchBonusUp = 2;
        public const int DetonatorMulUp = 4, FirstAidBonusUp = 2, PiercerMulUp = 2, ReliefShieldUp = 4, FullChargeCapUp = 5, BurnOutMulUp = 3;

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
            [TerminalIds.Relay] = "出目を攻撃に。このターンに発動した基本以外のカード 1 つにつき攻撃 +" + RelayPerFiredUp + "。",
            [TerminalIds.Grinder] = "出目を攻撃に。全部の出目 +1 (このカードに挿した出目も)。",
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
            [TerminalIds.Transformer] = "攻撃のカードを挿した出目を 2 倍。このカードに挿した出目も攻撃に足す。",
            [TerminalIds.Switchboard] = "次のターン、カードを " + SwitchboardDrawUp + " 枚多く引く。",
            [TerminalIds.VacuumTube] = "このターンの会心倍率 +" + VacuumCritMultUp.ToString("0.0") + "。",
            [TerminalIds.Coolant] = "出目を溜める。" + CoolantNeedUp + " ごとにエスカレーションの段階を 1 つ戻す。",
            [TerminalIds.Counter] = "出目を攻撃に。基本以外の発動が累計 " + CounterEveryUp + " 回になるごとに、この戦闘中の与ダメージ +" + (int)(CounterPct * 100) + "%。",
            [TerminalIds.Preheater] = "出目 +" + UpPipBlock2 + " をブロックに。次のターン、カードの条件を無視する。",
            [TerminalIds.TwinShot] = "同じ目 2 個で、出目の合計 ×" + TwinShotMulUp + " を攻撃に。",
            [TerminalIds.Harvester] = "出目を攻撃に。このターンに敵を倒すと最大 HP +" + HarvesterMaxHpUp + " (恒久)。",
            [TerminalIds.Whet] = "出目を攻撃に。敵の HP が半分以下なら攻撃 +" + WhetLowHpAttackUp + "。このターンに敵を倒すと、このカードの攻撃が恒久に +" + WhetGain + "。",
            [TerminalIds.LootBox] = "出目を攻撃に。このターンに敵を倒すとゴールド +" + LootGoldUp + "。",
            [TerminalIds.Stripper] = "出目を攻撃に。このターンに敵を倒すと、戦闘後のカードの選択肢が " + StripperChoicesUp + " つ増える。",
            [TerminalIds.Retainer] = "出目 +" + UpPipBlock2 + " をブロックに。捨て札から 1 枚、次のターンの手札に戻す。",
            [TerminalIds.StopSignal] = "敵は次のターン行動しない。出目をブロックに。",
            [TerminalIds.Fork] = "出目を攻撃に、出目 +" + UpPip + " をブロックに入れる。",
            [TerminalIds.Sight] = "出目を攻撃に。このターンの会心倍率 +この戦闘の会心の回数 ×" + SightPerCritUp.ToString("0.0#") + "。",
            [TerminalIds.VitalRead] = "このターンの攻撃は必ず会心。会心倍率 +この戦闘の会心の回数 ×" + VitalPerCritUp.ToString("0.0#") + "。",
            [TerminalIds.Barb] = "出目を攻撃に。攻撃が通れば出血 +敵の出血 ÷" + BarbDiv + " (最低 +" + BarbMinUp + ")。",
            [TerminalIds.BloodTap] = "敵の出血 " + BloodTapPer + " につき、このターンの与ダメージ +" + (int)(BloodTapPct * 100) + "%。",
            [TerminalIds.Mister] = "敵に毒 +出目 +" + (MisterBase + MisterBonusUp) + " (敵に毒があれば +" + (MisterPoisoned + MisterBonusUp) + ")。",
            [TerminalIds.PoisonScale] = "出目を攻撃に。敵の毒 " + PoisonScalePer + " につき攻撃 +" + PoisonScaleAttackUp + "。",
            [TerminalIds.Stoker] = "臨界 +出目 +" + (StokerBase + StokerBonusUp) + " (前のターンかこのターンに爆発していれば +" + (StokerHot + StokerBonusUp) + ")。",
            [TerminalIds.AfterHeat] = "出目を攻撃に。この戦闘の爆発 1 回につき攻撃 +" + AfterHeatPerBurstUp + "。",
            [TerminalIds.Leak] = "充電 +出目 +" + (LeakBase + LeakBonusUp) + " (過充電なら +" + (LeakOver + LeakBonusUp) + ")。",
            [TerminalIds.BigDischarge] = "充電をすべて払い、払った充電を攻撃に足す (このターンに先に充電を使った 1 回につき +" + (int)(BigDischargePerSpend * 100) + "%)。挿した出目も攻撃に足す。",
            [TerminalIds.Patch] = "出目をブロックに。シールド +" + (PatchBase + PatchBonusUp) + " (前のターンに【ジャスト防御】なら +" + (PatchJust + PatchBonusUp) + ")。",
            [TerminalIds.ShieldPush] = "出目を攻撃に。今のシールドの " + (int)Math.Round(ShieldPushPctUp * 100) + "% を攻撃に足し、足した分のシールドを払う。",
            [TerminalIds.Detonator] = "出目 ×" + DetonatorMulUp + " を攻撃に。",
            [TerminalIds.Piercer] = "出目 ×" + PiercerMulUp + " を攻撃に。このターン敵の防御を無視。",
            [TerminalIds.ReliefValve] = "敵が攻撃を予告したターンだけ発動。このターンの被ダメージ −" + ReliefCutPct + "%。シールド +" + ReliefShieldUp + "。",
            [TerminalIds.FirstAid] = "この戦闘で HP を失った後だけ発動。HP +出目" + (FirstAidMul == 1 ? "" : " ×" + FirstAidMul) + " +" + FirstAidBonusUp + "。最大 HP を超えた分はシールドになる。",
            [TerminalIds.FullCharge] = "この戦闘中、充電の上限 +" + FullChargeCapUp + "。充電を上限まで満たす。",
            [TerminalIds.CoolShot] = "エスカレーションの段階を 1 つ戻す。出目をブロックに。",
            [TerminalIds.Copier] = "挿さずに空けた手札のカードを 1 枚、この戦闘中だけ複製して、次のターンの手札に加える。出目を充電に。空けたカードが無ければ発動しない。",
            [TerminalIds.BurnOut] = "出目 ×" + BurnOutMulUp + " を攻撃に。挿さずに空けた手札のカードを 1 枚、この戦闘から外す。空けたカードが無ければ発動しない。",
            // 采配と共通の端子 (2026-11-20)。 使い捨ては回数・枚数を増やさない (段 1-b の方針・terminal_upgrades_draft.md)
            [TerminalIds.BurnSlash] = "この戦闘で使い捨てを使った後だけ発動 (前のターンまで)。出目 ×" + BurnSlashMulUp + " を攻撃に。これまでにこの戦闘で使い捨てを使った回数 ×" + BurnSlashPerUp + " を攻撃に足す。",
            [TerminalIds.Reload] = "このターンに使った他の使い捨てを 1 枚 (場の右から)、次のターンの手札に戻す。出目を充電に。",
            [TerminalIds.OneCut] = "出目 ×" + OneCutMulUp + " を攻撃に。この撃は敵の防御を無視。挿した攻撃のカードがこれ 1 枚だけのターンにだけ発動する。",
            [TerminalIds.Gear] = "出目は挿せない。場にあれば、毎ターン最初に発動した基本以外のカード (場の左から) の挿した目 1 つにつき +" + GearPipUp + "。一度引くとこの戦闘中ずっと場に残る。",
            [TerminalIds.Bell] = "この戦闘の残りの間、山札を切り直すたび、次のターンにカードを 1 枚多く引く。出目を充電に。",
            [TerminalIds.Mikiri] = "出目をブロックに。敵が大技を予告したターンなら 2 倍。",
            // 役の軸の端子 (2026-11-21)
            [TerminalIds.DiceTable] = "充電 +出目。次のターンの最初の振り直しの充電の費用 −min(" + DiceTableCapUp + ", この戦闘で役が成立した回数)。",
            [TerminalIds.RoleDrop] = "出目を攻撃に。このターン役が成立していれば、攻撃 +この戦闘で役が成立した回数 ×" + RoleDropPerUp + " (ゾロ目・連なりなら 2 倍)。",
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
            if (u.cond != b.cond) u.condLabel = CondLabel(u.cond);      // 発動の条件の付いた表記 (Sit) は写したまま
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
        /// <summary>発動の条件 (<see cref="SituationOk"/>) の付いた条件の表記。</summary>
        private static TerminalDef Sit(TerminalDef d, string label) { d.condLabel = d.condLabel != null ? d.condLabel + "・" + label : label; return d; }
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
            var perm = D(TerminalIds.Mains, A, "出目を攻撃に。この戦闘で使うたび、以後のこのカードの攻撃 +" + MainsPerUse + "。一度引くとこの戦闘中ずっと場に残る。"); perm.permanent = true;
            return new List<TerminalDef>
            {
                // ── 基本 ──
                Basic(D(TerminalIds.Attack, A, "出目を攻撃に。")),
                Basic(D(TerminalIds.Block,  B, "出目をブロックに。")),
                Basic(D(TerminalIds.Charge, C, "出目を充電に。")),
                // ── 賽の端子 (ADR-0015)。 基本扱い (報酬・店・付与に出ない)・常設 ──
                ClockDef(D(TerminalIds.Clock, S, "時計の賽のカード。出目の数だけ、エスカレーションの次の段階を先へ送る。一度に 1 つ。")),
                // ── コモン ──
                D(TerminalIds.Igniter, A, "攻撃 +" + IgniterAttack + "。", TerminalCond.AtMost3),
                D(TerminalIds.Detector, A, "出目 ×" + DetectorMul + " を攻撃に。", TerminalCond.Odd),
                Cost(D(TerminalIds.DischargeTube, A, "充電 " + DischargeCost + " を払い、出目 ×" + DischargeMul + " を攻撃に。"), DischargeCost),
                D(TerminalIds.Deathwish, A, "出目の半分 (切り上げ) の HP を払い、出目 ×" + DeathwishMul + " を攻撃に。HP が足りなければ発動しない。"),
                D(TerminalIds.Relay, A, "出目を攻撃に。このターンに発動した基本以外のカード 1 つにつき攻撃 +" + RelayPerFired + "。"),
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
                D(TerminalIds.Transformer, S, "攻撃のカードを挿した出目を 2 倍。"),
                D(TerminalIds.Switchboard, S, "次のターン、カードを " + SwitchboardDraw + " 枚多く引く。"),
                // ── レア ──
                Rare(D(TerminalIds.Amplifier, S, "出目 1 につき与ダメージ +" + (int)(AmplifierPerPip * 100) + "% (別枠)。", TerminalCond.AtLeast5)),
                Rare(D(TerminalIds.VacuumTube, S, "このターンの会心倍率 +" + VacuumCritMult.ToString("0.0") + "。", TerminalCond.AtLeast6)),
                Rare(D(TerminalIds.Resonator, S, "このターンの与ダメージ ×" + ResonatorMul.ToString("0.0") + " (別枠)。", TerminalCond.SameElsewhere)),
                Rare(D(TerminalIds.Foundry, S, "出目を溜める。" + FoundryNeed + " ごとにこの戦闘中の全部の出目 +1 (1 ターンに " + FoundryMaxPerTurn + " 回まで・余りは持ち越す。このターンは、まだ効果の出ていないカードの出目から)。")),
                Rare(D(TerminalIds.Coolant, S, "出目を溜める。" + CoolantNeed + " ごとにエスカレーションの段階を 1 つ戻す。")),
                Rare(D(TerminalIds.Counter, A, "出目を攻撃に。基本以外の発動が累計 " + CounterEvery + " 回になるごとに、この戦闘中の与ダメージ +" + (int)(CounterPct * 100) + "%。")),
                Rare(D(TerminalIds.Preheater, B, "出目をブロックに。次のターン、カードの条件を無視する。")),
                Rare(perm),
                Rare(Cap(D(TerminalIds.TwinShot, A, "同じ目 2 個で、出目の合計 ×" + TwinShotMul + " を攻撃に。", TerminalCond.PairSame), 2)),
                Rare(D(TerminalIds.Harvester, A, "出目を攻撃に。このターンに敵を倒すと最大 HP +" + HarvesterMaxHp + " (恒久)。")),
                Rare(D(TerminalIds.Whet, A, "出目を攻撃に。このターンに敵を倒すと、このカードの攻撃が恒久に +" + WhetGain + "。")),
                Rare(D(TerminalIds.LootBox, A, "出目を攻撃に。このターンに敵を倒すとゴールド +" + LootGold + "。")),
                // 〈剥ぎ取り〉: 2026-11-13 に補助から攻撃へ (ユーザーの決定)。 補助のままでは挿しても攻撃にならず、 レアの 3 択でほかに負け続けた (GAME.md §24)
                Rare(D(TerminalIds.Stripper, A, "出目を攻撃に。このターンに敵を倒すと、戦闘後のカードの選択肢が " + StripperChoices + " つ増える。")),
                Rare(D(TerminalIds.Doubler, S, "このターン、他のカードの効果を 2 回発動させる。", TerminalCond.AtLeast6)),
                // 〈有効活用〉: 手札の持ち越し (2026-11-13・HandCarry) で「空けた端子を 1 枚残す」は役目を失ったので、
                //   「捨て札から選んだ 1 枚を、 次のターンの手札に戻す」に作り替えた (ユーザーの決定)。 出目をブロックに入れるのは残す
                //   (ADR-0014 の値付けの規則 2: 準備役は挿した出目を素の端子と同じに入れたうえで効果を足す)。
                //   測定の口で旧い規則に戻した腕では、 元の「空けた端子を 1 枚残す」が働く (説明文は今の規則のまま)。
                Rare(D(TerminalIds.Retainer, B, "出目をブロックに。捨て札から 1 枚、次のターンの手札に戻す。")),
                Rare(Once(D(TerminalIds.StopSignal, S, "敵は次のターン行動しない。"))),
                Rare(D(TerminalIds.Fork, A, "出目を攻撃とブロックの両方に入れる。")),
                // ── 軸 ──
                D(TerminalIds.Sight, A, "出目を攻撃に。このターンの会心倍率 +この戦闘の会心の回数 ×" + SightPerCrit.ToString("0.0#") + "。", TerminalCond.None, ax),
                D(TerminalIds.VitalRead, S, "このターンの攻撃は必ず会心。会心倍率 +この戦闘の会心の回数 ×" + VitalPerCrit.ToString("0.0#") + "。", TerminalCond.Odd, ax),
                D(TerminalIds.Barb, A, "出目を攻撃に。攻撃が通れば出血 +敵の出血 ÷" + BarbDiv + " (最低 +" + BarbMin + ")。", TerminalCond.None, ab),
                D(TerminalIds.BloodTap, S, "敵の出血 " + BloodTapPer + " につき、このターンの与ダメージ +" + (int)(BloodTapPct * 100) + "%。", TerminalCond.AtMost3, ab),
                D(TerminalIds.Mister, S, "敵に毒 +出目 +" + MisterBase + " (敵に毒があれば +" + MisterPoisoned + ")。", TerminalCond.None, ap),
                D(TerminalIds.PoisonScale, A, "出目を攻撃に。敵の毒 " + PoisonScalePer + " につき攻撃 +" + PoisonScaleAttack + "。", TerminalCond.None, ap),
                D(TerminalIds.Stoker, S, "臨界 +出目 +" + StokerBase + " (前のターンかこのターンに爆発していれば +" + StokerHot + ")。", TerminalCond.None, ah),
                D(TerminalIds.AfterHeat, A, "出目を攻撃に。この戦闘の爆発 1 回につき攻撃 +" + AfterHeatPerBurst + "。", TerminalCond.None, ah),
                D(TerminalIds.Leak, C, "充電 +出目 +" + LeakBase + " (過充電なら +" + LeakOver + ")。", TerminalCond.None, ac),
                // 2026-11-19: 使い捨てに (作者の決定)。 〈繋ぎの賽〉で毎ターン撃ち続けられ、 溜めて撃つ判断が消えていた
                Once(D(TerminalIds.BigDischarge, A, "充電をすべて払い、払った充電を攻撃に足す (このターンに先に充電を使った 1 回につき +" + (int)(BigDischargePerSpend * 100) + "%)。", TerminalCond.None, ac)),
                D(TerminalIds.Patch, B, "出目をブロックに。シールド +" + PatchBase + " (前のターンに【ジャスト防御】なら +" + PatchJust + ")。", TerminalCond.None, asx),
                D(TerminalIds.ShieldPush, A, "出目を攻撃に。今のシールドの " + (int)Math.Round(ShieldPushPct * 100) + "% を攻撃に足し、足した分のシールドを払う。", TerminalCond.None, asx),
                // ── 使い捨て ──
                //   2026-11-22 (neon 最終仕上げ §2-1): 使い捨ての機会を出目の条件と発動の条件で絞る (上限でなく機会)
                Once(D(TerminalIds.Detonator, A, "出目 ×" + DetonatorMul + " を攻撃に。", TerminalCond.AtLeast5)),
                Once(D(TerminalIds.Piercer, A, "出目を攻撃に。このターン敵の防御を無視。", TerminalCond.Odd)),
                Sit(Once(D(TerminalIds.ReliefValve, B, "敵が攻撃を予告したターンだけ発動。このターンの被ダメージ −" + ReliefCutPct + "%。", TerminalCond.AtMost3)), "予告"),
                Sit(RunOnce(D(TerminalIds.FirstAid, S, "この戦闘で HP を失った後だけ発動。HP +出目" + (FirstAidMul == 1 ? "" : " ×" + FirstAidMul) + "。", TerminalCond.AtMost3)), "被弾後"),
                Once(D(TerminalIds.FullCharge, C, "充電を上限まで満たす。")),
                Once(D(TerminalIds.CoolShot, S, "エスカレーションの段階を 1 つ戻す。")),
                Once(D(TerminalIds.Copier, S, "挿さずに空けた手札のカードを 1 枚、この戦闘中だけ複製して、次のターンの手札に加える。空けたカードが無ければ発動しない。")),
                // 〈火葬の剣〉: 2026-11-14 に作り直し (作者の決定)。 旧: 補助で「空けた端子を 1 枚この戦闘から外す」だけ (測定で発動 1.7%・GAME.md §24)。
                //   外す端子が無いターン (候補 0) は挿しても不発 (Resolve の 4.)・使い捨ても消費しない
                Once(D(TerminalIds.BurnOut, A, "出目 ×" + BurnOutMul + " を攻撃に。挿さずに空けた手札のカードを 1 枚、この戦闘から外す。空けたカードが無ければ発動しない。")),
                // ── 采配と共通の端子 (2026-11-20・段 1-e・item-overhaul-spec §8 / §12-4) ──
                Sit(Once(D(TerminalIds.BurnSlash, A, "この戦闘で使い捨てを使った後だけ発動 (前のターンまで)。出目 ×" + BurnSlashMul + " を攻撃に。これまでにこの戦闘で使い捨てを使った回数 ×" + BurnSlashPer + " を攻撃に足す。", TerminalCond.None, ItemSystem.PassiveSkills.AxisRules.Deck)), "使い捨て後"),
                //   〈緊急再装填〉: 自分は戻せない (他の〈緊急再装填〉は戻せる・作者 2026-11-20「戻し合っても意味がない」)。 戻す 1 枚は場の右から (Resolve の 5b)
                Once(D(TerminalIds.Reload, S, "このターンに使った他の使い捨てを 1 枚 (場の右から)、次のターンの手札に戻す。", TerminalCond.AtLeast5)),
                Once(D(TerminalIds.OneCut, A, "出目 ×" + OneCutMul + " を攻撃に。この撃は敵の防御を無視。挿した攻撃のカードがこれ 1 枚だけのターンにだけ発動する。")),
                //   〈歯車の軸〉: 挿せない常設 (capacity 0)。 場にあれば効く (Resolve の 5.)。 2 枚あれば 2 枚分
                Cap(Perm(D(TerminalIds.Gear, S, "出目は挿せない。場にあれば、毎ターン最初に発動した基本以外のカード (場の左から) の挿した目 1 つにつき +" + GearPip + "。一度引くとこの戦闘中ずっと場に残る。")), 0),
                Rare(Once(D(TerminalIds.Bell, S, "この戦闘の残りの間、山札を切り直すたび、次のターンにカードを 1 枚多く引く。"))),
                D(TerminalIds.Mikiri, B, "出目をブロックに。敵が大技を予告したターンなら 2 倍。", TerminalCond.AtMost3),
                D(TerminalIds.Crush, A, "出目を攻撃に。この撃でボスの目盛りを割ったら、敵に最大 HP の " + CrushPct + "% の軽減不可ダメージ。", TerminalCond.AtLeast5),
                // ── 役の軸の端子 (2026-11-21・段 1-f・item-overhaul-spec §8-1 / §12-4) ──
                //   〈賽振り台〉: 配線は振り直しの後なので、 安くするのは次のターンの最初の振り直し (§12-4)
                D(TerminalIds.DiceTable, C, "充電 +出目。次のターンの最初の振り直しの充電の費用 −min(" + DiceTableCap + ", この戦闘で役が成立した回数)。", TerminalCond.None, ItemSystem.PassiveSkills.AxisRules.Role),
                D(TerminalIds.RoleDrop, A, "出目を攻撃に。このターン役が成立していれば、攻撃 +この戦闘で役が成立した回数 ×" + RoleDropPer + " (ゾロ目・連なりなら 2 倍)。", TerminalCond.None, ItemSystem.PassiveSkills.AxisRules.Role),
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

        /// <summary>出目の条件とは別の、 発動の条件 (2026-11-22・使い捨ての機会を絞る): 〈応急箱〉この戦闘で HP を 1 以上失った後 /
        /// 〈身代わり札〉敵が攻撃を予告したターン / 〈焼却斬り〉前のターンまでにこの戦闘で使い捨てを 1 回以上使った後。 本体・画面・BOT が Resolve で同じ判定を通る。</summary>
        public static bool SituationOk(TerminalDef d, in TerminalEnv env, TerminalBattleState st)
        {
            switch (d?.baseId)
            {
                case TerminalIds.FirstAid: return env.hpLostBattle;
                case TerminalIds.ReliefValve: return env.enemyAttack > 0;
                case TerminalIds.BurnSlash: return st != null && st.disposedTotal >= 1;
                default: return true;
            }
        }

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
        /// <param name="reboot">〈二股の導線〉【再起動】の対象 (場の添字・<see cref="ChargeCore.RebootNone"/> = しない・<see cref="ChargeCore.RebootAuto"/> = おまかせ)。
        /// このターンに発動した攻撃か防御の端子でなければ再起動しない (充電も払わない)。</param>
        /// <param name="overclock">〈雷鳴の太鼓〉【オーバークロック】を使う (条件を満たさなければ使わない・充電も払わない)。</param>
        /// <summary>attackLog を取らない解決の共有の空の一覧 (書くのは logAttack の時だけ)。</summary>
        public static readonly List<string> NoLog = new List<string>();

        // 速度 (Phase 3i・結果は変えない): Resolve の中だけで使う作業用の配列を使い回す (外へ出さない・Resolve は入れ子に呼ばれない)。 スレッドごと。
        [ThreadStatic] private static int[] _sV, _sCnt, _sSum, _sRaw, _sNum, _sAtk, _sBlk;
        [ThreadStatic] private static TerminalDef[] _sDefs;
        private static int[] Scratch(ref int[] buf, int len)
        {
            if (buf == null || buf.Length != len) buf = new int[len]; else Array.Clear(buf, 0, len);
            return buf;
        }

        public static TerminalOutcome Resolve(List<string> board, int[] wantSlot, int[] dice, TerminalEnv env, TerminalBattleState state, int[] wantTarget = null, bool logAttack = false,
                                              int reboot = ChargeCore.RebootNone, bool overclock = false, int[] plugRank = null)
        {
            int n = dice.Length, m = board.Count;
            var o = new TerminalOutcome { slotOfDie = new int[n], kindOfDie = new DiceTerminal[n], logAttack = logAttack };
            if (logAttack) o.attackLog = new List<string>();
            o.stopped = state.stopped != null ? (bool[])state.stopped.Clone() : null;
            var v = Scratch(ref _sV, n);
            for (int i = 0; i < n; i++) { v[i] = dice[i]; o.slotOfDie[i] = wantSlot != null && i < wantSlot.Length && wantSlot[i] >= 0 && wantSlot[i] < m ? wantSlot[i] : -1; }
            var defs = _sDefs != null && _sDefs.Length == m ? _sDefs : (_sDefs = new TerminalDef[m]);
            for (int s = 0; s < m; s++) defs[s] = Get(board[s]);

            // ── 0. 止まった端子 (ヴェスカ〈停滞する時間〉) と、 挿せる数を超えた分を外す ──
            //   止まった端子に挿した出目は挿さなかった扱い。 本体も BOT も画面もここを通るので、 同じ計算になる。
            var cnt = Scratch(ref _sCnt, m);
            int wiredLeft = CardRules.MaxWiredDice > 0 ? CardRules.MaxWiredDice : n;      // クァディルの色欲 (挑戦カード IV): 挿せる出目の数 (前の出目から・超えた分は挿さなかった扱い)
            for (int i = 0; i < n; i++)
            {
                int s = o.slotOfDie[i]; if (s < 0) continue;
                if (defs[s] == null || state.IsStopped(s) || cnt[s] >= defs[s].capacity || (env.noAttack && defs[s].kind == TerminalKind.Attack) || wiredLeft <= 0) { o.slotOfDie[i] = -1; continue; }
                cnt[s]++; wiredLeft--;
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
            for (int s = 0; s < m; s++)
            {
                if (defs[s] == null || !Plugged(o.slotOfDie, s)) continue;
                int cost = ChargeCore.CostNow(defs[s].chargeCost, env.chargeCostCut);   // 〈過負荷チューナー〉
                if (defs[s].baseId == TerminalIds.BigDischarge) { if (charge <= 0) Unplug(o.slotOfDie, s); continue; }      // 全部払う (量は効果の所で)
                if (cost <= 0) continue;
                if (charge < cost) { Unplug(o.slotOfDie, s); continue; }
                Pay(o, ref charge, cost);
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
            //   発動の条件 (2026-11-22・出目の条件とは別・〈無形の構え〉でも無視しない): 合わなければ不発 (使い捨ても消費しない)
            for (int s = 0; s < m; s++)
                if (defs[s] != null && Plugged(o.slotOfDie, s) && !SituationOk(defs[s], in env, state)) Unplug(o.slotOfDie, s);
            //   〈一刀両断〉(2026-11-20): 挿した攻撃の種類の端子がこれ 1 枚だけのターンにだけ発動する (2 枚以上なら不発・使い捨ても消費しない)
            {
                int atkPlugged = 0;
                for (int s = 0; s < m; s++) if (defs[s] != null && defs[s].kind == TerminalKind.Attack && Plugged(o.slotOfDie, s)) atkPlugged++;
                if (atkPlugged > 1)
                    for (int s = 0; s < m; s++) if (defs[s]?.baseId == TerminalIds.OneCut && Plugged(o.slotOfDie, s)) Unplug(o.slotOfDie, s);
            }

            // ── 5. 効果 ──
            //   このターンに立つ役 (振った目で決まる・CombatManager.ResolveRoles と同じ判定)。 〈役落とし〉と賽目の品 (6d) が読む
            var roleDice = RoleDiceOf(dice, env.roleDiceCut, env.roleDiceAt);      // 怠惰で引く前の目 (引いていなければ dice そのもの)
            var roleKind = RoleOf(roleDice, env);
            o.roleNow = roleKind == null ? 0 : YachtRoles.IsBig(roleKind.Value) ? 2 : 1;
            bool doubler = false;
            for (int s = 0; s < m; s++) if (defs[s]?.baseId == TerminalIds.Doubler && Plugged(o.slotOfDie, s)) doubler = true;
            // 連なりの賽 (2026-11-20): 3 つの目が連番のターン (役〈連なり〉)、 挿した端子がすべて倍化する (〈残響〉と同じ)
            if (env.dieId == GameLoop.DieIds.Straight && YachtRoles.IsRun(env.roleDiceCut != null ? RoleDiceOf(v, env.roleDiceCut, env.roleDiceAt) : v)) doubler = true;
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
            //   【再起動】(5c) が同じ量でもう 1 回走らせるための控え (場の添字ごと)
            Func<int, int> emptyF = Empty;      // 速度: 端子ごとに委譲を作り直さない
            var fSum = Scratch(ref _sSum, m); var fRaw = Scratch(ref _sRaw, m); var fNum = Scratch(ref _sNum, m); var atkD = Scratch(ref _sAtk, m); var blkD = Scratch(ref _sBlk, m);
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
                if (id == TerminalIds.BigDischarge) { paidAll = charge; Pay(o, ref charge, charge); }
                o.fired.Add(s); o.firedSums.Add(sum);
                if (gearHere) gearUsed = true;      // 不発 (〈諸刃の剣〉の HP 不足) なら次の端子へ回る
                if (!d.basic) o.firedNonBasic++;
                switch (d.kind)
                {
                    case TerminalKind.Attack: o.attackDice += num; o.attackPips += raw; break;
                    case TerminalKind.Block: o.blockDice += num; o.blockTerminals++; break;
                    case TerminalKind.Charge: o.chargeDice += num; break;
                    default: o.supportDice += num; break;
                }
                int times = doubler && id != TerminalIds.Doubler ? 2 : 1;
                int atkBefore = o.attackAdd, chgBefore = charge, blkBefore = o.blockAdd;
                int fBefore = state.foundryBonus;
                for (int t = 0; t < times; t++) Effect(id, d.upgraded, board[s], s, sum, raw, num, paidAll, env, state, o, ref charge, emptyF);
                //   采配の品 (2026-11-20): 端子 1 枚の発動につき 1 回 (〈残響〉で効果が 2 回走っても 1 回)
                if (d.disposable) o.disposedNow++;
                if ((state.deckItems & DeckItems.Mold) != 0 && d.cond != TerminalCond.None)      // 〈打ち抜きの金型〉: 条件のある端子の攻撃・ブロック +4・補助はブロック +6 (条件を無視したターンも条件のある端子に数える)
                    switch (d.kind)
                    {
                        case TerminalKind.Attack: o.attackAdd += MoldBonus; break;      // この端子の撃に入る (下の attackHits)
                        case TerminalKind.Block: o.blockAdd += MoldBonus; break;
                        case TerminalKind.Support: o.blockAdd += MoldSupportBlock; break;
                    }
                foundryNow += state.foundryBonus - fBefore;
                if (d.kind == TerminalKind.Attack) { o.attackHits.Add(o.attackAdd - atkBefore); o.attackHitDice.Add(num); o.attackHitIds.Add(board[s]); o.attackHitSlot.Add(s); }
                if (o.logAttack && o.attackAdd != atkBefore) o.attackLog.Add($"{board[s]}(目{sum}{(times > 1 ? "・倍化" : "")}{(chgBefore != charge ? "・充電" + chgBefore + "→" + charge : "")}):+{o.attackAdd - atkBefore}");
                fSum[s] = sum; fRaw[s] = raw; fNum[s] = num; atkD[s] = o.attackAdd - atkBefore; blkD[s] = o.blockAdd - blkBefore;
            }

            // ── 5a. 撃の順 = 攻撃の端子に出目を挿した順 (2026-11-25・作者の決定)。 端子の効果は場の順で解いたまま、 撃の並びだけ挿した順へ並べ替える。
            //   これより後 (【再起動】の撃・〈雷鳴の太鼓〉の最初の 1 撃・5d〜5g・本体の撃の輪) は並べ替えた後の順を読む。 plugRank が無ければ場の順 (今までどおり)
            if (plugRank != null && o.attackHits.Count > 1) SortHitsByPlug(o, plugRank, n);

            // ── 5c. 充電のコアアクション (2026-11-21・ChargeCore・full-item-rework-ledger §①) ──
            //   再起動 = 発動した端子の効果を、 控えた量 (挿した出目の合計) でもう 1 回走らせる。 ダイスは足さない (個数・端子調律の 1 本あたりは増えない)。
            //   攻撃の端子なら撃が 1 つ増える (attackHits に足す)。 〈残響〉の倍化は掛けない (1 回だけ走る)。 〈裁きの雷〉は払う物が無いので対象にしない。
            void Refire(int s, string tag)
            {
                var d = defs[s]; int a0 = o.attackAdd, b0 = o.blockAdd;
                Effect(d.baseId, d.upgraded, board[s], s, fSum[s], fRaw[s], fNum[s], 0, env, state, o, ref charge, emptyF);
                if (d.kind == TerminalKind.Attack) { o.attackHits.Add(o.attackAdd - a0); o.attackHitDice.Add(0); o.attackHitIds.Add(board[s]); o.attackHitSlot.Add(s); }
                else if (d.kind == TerminalKind.Block) o.blockTerminals++;
                atkD[s] += o.attackAdd - a0; blkD[s] += o.blockAdd - b0;
                o.rebooted.Add(s);
                if (o.logAttack && o.attackAdd != a0) o.attackLog.Add($"{board[s]}({tag}):+{o.attackAdd - a0}");
            }
            bool Rebootable(int s, bool attackOnly, bool blockOnly)
            {
                var d = s >= 0 && s < m ? defs[s] : null;
                if (d == null || !o.fired.Contains(s) || d.baseId == TerminalIds.BigDischarge || d.baseId == TerminalIds.Doubler) return false;
                return attackOnly ? d.kind == TerminalKind.Attack : blockOnly ? d.kind == TerminalKind.Block : d.kind == TerminalKind.Attack || d.kind == TerminalKind.Block;
            }
            int Best(bool attackOnly, bool blockOnly)
            {
                int best = -1, bv = 0;
                for (int s = 0; s < m; s++)
                {
                    if (!Rebootable(s, attackOnly, blockOnly)) continue;
                    int val = attackOnly ? atkD[s] : blockOnly ? blkD[s] : ChargeCore.AutoValue(atkD[s], blkD[s], env.enemyAttack, o.blockAdd);
                    if (val > bv) { bv = val; best = s; }
                }
                return best;
            }
            var ci = state.chargeItems;
            //   〈二股の導線〉【再起動】: 1 ターン 1 回・充電 6 (〈過負荷チューナー〉で減る)。 対象 = 人 / BOT の指定 (おまかせは AutoValue の最大)
            if ((ci & ChargeItems.Fork) != 0 && reboot != ChargeCore.RebootNone)
            {
                int tgt = reboot == ChargeCore.RebootAuto ? Best(false, false) : Rebootable(reboot, false, false) ? reboot : -1;
                int cost = CardRules.RebootCost(ChargeCore.CostNow(ChargeCore.RebootCost, env.chargeCostCut));   // 挑戦カード〈予備電源〉III: −N (最低 1)
                if (tgt >= 0 && charge >= cost) { Pay(o, ref charge, cost); o.rebootSlot = tgt; Refire(tgt, "再起動"); }
            }
            //   〈逆さ避雷針〉: 敵の攻撃が来るターン、 充電 5 で被ダメ −6 と、 発動した防御の端子 (ブロックの最も多い 1 枚) の自動再起動 (1 ターン 1 回)
            if ((ci & ChargeItems.Rod) != 0 && env.enemyAttack > 0)
            {
                int cost = ChargeCore.CostNow(ChargeCore.RodCost, env.chargeCostCut);
                if (charge >= cost)
                {
                    Pay(o, ref charge, cost); o.takenFlatCut += ChargeCore.RodCut;
                    int tgt = Best(false, true);
                    if (tgt >= 0) Refire(tgt, "逆さ避雷針");
                }
            }
            //   〈雷鳴の太鼓〉【オーバークロック】: 攻撃の端子を挿したターン、 充電 15 以上なら 12 を払う (1 ターン 1 回)。 掛けるのは本体の撃の輪 (最初の 1 撃)
            if ((ci & ChargeItems.Drum) != 0 && overclock && o.attackHits.Count > 0 && charge >= ChargeCore.OverclockNeed)
            {
                Pay(o, ref charge, CardRules.RebootCost(ChargeCore.CostNow(ChargeCore.OverclockCost, env.chargeCostCut)));
                o.overclock = true;
            }
            //   〈灼線鞭〉: このターン充電を 2 回以上使った (振り直しも数える) なら、 発動した中で最も攻撃の多い攻撃の端子を無料で再起動 (1 ターン 1 回)
            if ((ci & ChargeItems.Whip) != 0 && env.chargeSpendsTurn + o.chargePays.Count >= 2)
            {
                int tgt = Best(true, false);
                if (tgt >= 0) Refire(tgt, "灼線鞭");
            }

            // ── 5d. 毒の品 (2026-11-21・PoisonCore・full-item-rework-ledger §②): 発動・再起動が決まった後に、 毒・回復・ブロック・被ダメ・防御無視を足す ──
            PoisonCore.ApplyToTerminals(defs, dice, in env, state, o);
            // ── 5e. シールドの品 (2026-11-21・ShieldCore・full-item-rework-ledger §③): 防御の端子の個数・敵の予告で決まるシールドを足す ──
            ShieldCore.ApplyToTerminals(dice, in env, state, o);
            // ── 5f. 出血の品 (2026-11-21・BleedCore・full-item-rework-ledger §④): 撃ごとの出血と撃ごとの攻撃 +N (本体は撃が当たった直後に出血を入れる) ──
            BleedCore.ApplyToTerminals(defs, dice, in env, state, o);
            // ── 5g. 臨界の品 (2026-11-21・HeatCore・full-item-rework-ledger §⑤): 配線で決まる臨界・〈燻りの革胸当て〉のブロック・〈燧石の指輪〉の倍 ──
            HeatCore.ApplyToTerminals(defs, dice, state, o);

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
            // 使い捨ての数 (この戦闘)。 山札の品 (6c) はこのターンの分を足した後の回数を読む
            state.disposedTotal += o.disposedNow;
            // 次に攻撃するターンの攻撃に足す分
            int atk6 = o.attackAdd;
            if (o.attackDice > 0 && state.storedAttack > 0) { o.attackAdd += state.storedAttack; state.storedAttack = 0; }
            if (o.logAttack && o.attackAdd != atk6) o.attackLog.Add($"連係・溜めた攻撃:+{o.attackAdd - atk6}");

            // ── 6b. 装備中の賽のパッシブ (ADR-0015): 挿した目ごとに効く・残り火の持ち越し ──
            int atkDie = o.attackAdd;
            DiePassives.ApplyToTerminals(env, state, o, defs, v);
            if (o.logAttack && o.attackAdd != atkDie) o.attackLog.Add($"賽:+{o.attackAdd - atkDie}");

            // ── 6c. 山札の品 (full-item-rework-ledger §⑦)。 攻撃 +N は攻撃の端子の撃 1 つずつに足す (ADR-0016・品の攻撃 +N と同じく全部の撃へ・攻撃しないターンは入らない) ──
            ApplyDeckItems(board, defs, env, state, o);

            // ── 6d. 賽目の品 (2026-11-21・段 1-f)。 役が立ったターン、 攻撃 +N は撃 1 つずつに (6c と同じ形) ──
            ApplyRoleItems(env, state, o, roleKind, dice);

            // ── 6e. 挑戦カードの裏 (2026-11-22・CardRules)。 札が無ければ何もしない ──
            ApplyCardBuffs(state, o);

            for (int i = 0; i < n; i++)
                o.kindOfDie[i] = o.slotOfDie[i] >= 0 ? LegacyKind(defs[o.slotOfDie[i]].kind) : DiceTerminal.None;
            return o;
        }

        /// <summary>Resolve の 5a: 撃 (attackHits と同じ並びの表すべて) を、 その端子に今挿さっている出目のうち最も早く挿した出目の順 (plugRank の小さい順) に並べる。
        /// plugRank = 出目ごとの挿した順 (小さいほど先・負 = 分からない = 後ろ)。 同じ順位は場の順のまま (安定)。</summary>
        private static void SortHitsByPlug(TerminalOutcome o, int[] plugRank, int n)
        {
            int c = o.attackHits.Count;
            var key = new int[c];
            for (int h = 0; h < c; h++)
            {
                int k = int.MaxValue, s = o.attackHitSlot[h];
                for (int i = 0; i < n && i < plugRank.Length; i++) if (o.slotOfDie[i] == s && plugRank[i] >= 0 && plugRank[i] < k) k = plugRank[i];
                key[h] = k;
            }
            for (int a = 1; a < c; a++)
                for (int j = a; j > 0 && key[j - 1] > key[j]; j--)
                {
                    (key[j - 1], key[j]) = (key[j], key[j - 1]);
                    (o.attackHits[j - 1], o.attackHits[j]) = (o.attackHits[j], o.attackHits[j - 1]);
                    (o.attackHitDice[j - 1], o.attackHitDice[j]) = (o.attackHitDice[j], o.attackHitDice[j - 1]);
                    (o.attackHitIds[j - 1], o.attackHitIds[j]) = (o.attackHitIds[j], o.attackHitIds[j - 1]);
                    (o.attackHitSlot[j - 1], o.attackHitSlot[j]) = (o.attackHitSlot[j], o.attackHitSlot[j - 1]);
                }
        }

        /// <summary>山札の品のうち、 全部の発動が決まった後に効く物 (Resolve の 6c)。 旗は <see cref="TerminalBattleState.deckItems"/>。</summary>
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
            int supports = 0;      // 発動した補助の端子 (基本の〈時計〉は数えない)
            foreach (int s in o.fired) if (defs[s] != null && defs[s].kind == TerminalKind.Support && !defs[s].basic) supports++;
            int perms = 0;      // 〈常設の作業台〉: 場の常設の端子 (止まっていない・基本の〈時計〉を除く)
            if ((f & DeckItems.Workbench) != 0)
                for (int s = 0; s < defs.Length; s++) if (defs[s] != null && defs[s].permanent && !defs[s].basic && !st.IsStopped(s)) perms++;
            int disp = o.disposedNow, each = 0;
            if ((f & DeckItems.Tongs) != 0) each += TongsAttack * disp;                                              // 〈焼き入れの挟み〉
            if ((f & DeckItems.Clasp) != 0 && keptUsed > 0) { each += ClaspAttack; o.blockAdd += ClaspBlock; }                                     // 〈留め金の栞〉
            if ((f & DeckItems.AuxGear) != 0) o.blockAdd += AuxGearBlock * Math.Min(AuxGearMax, supports);          // 〈補助歯車〉
            if ((f & DeckItems.Pride) != 0 && disp > 0)                                                              // 〈切り捨ての半券〉
            {
                int n = Math.Min(PrideMax, disp);
                each += PrideAttack * n; o.blockAdd += PrideBlock * n;
            }
            if ((f & DeckItems.Broom) != 0 && disp > 0)                                                              // 〈灰払いの箒〉
            {
                o.blockAdd += BroomBlock * disp; if (disp >= BroomDrawNeed) st.extraDrawNext += BroomDrawMax;   // 1 ターンに 1 回だけ足す (解決は 1 ターン 1 回・2026-11-22 final4: 要る回数 2 → 1 → final5: 2)
            }
            if ((f & DeckItems.Rewind) != 0 && st.shuffledNow > 0)                                                   // 〈早回しのゼンマイ〉: このターンに切り直した枚数
            {
                each += Math.Min(RewindMax, st.shuffledNow); o.blockAdd += RewindBlock;
            }
            if ((f & DeckItems.Crucible) != 0 && disp > 0) each += CruciblePer * st.disposedTotal;   // 〈再利用のるつぼ〉
            if ((f & DeckItems.Seamless) != 0 && supports > 0)                                                       // 〈継ぎ目のない導線〉
            {
                each += SeamlessAttack * supports; o.armorPen += SeamlessPen * supports; o.blockAdd += SeamlessBlock * supports;
                o.damageMul *= 1f + SeamlessFinalPct * supports;   // answer14: 別枠の最終与ダメージ
            }
            if (perms > 0) { each += WorkbenchPer * perms; o.blockAdd += WorkbenchBlock * perms; o.damageMul *= 1f + WorkbenchFinalPct * perms; }   // 〈常設の作業台〉
            if ((f & DeckItems.Ash) != 0 && st.disposedTotal >= AshNeed) o.damageMul *= 1f + AshPer * st.disposedTotal;       // 〈廃材組みの蒸気機関〉
            if (each > 0 && o.attackHits.Count > 0)
            {
                for (int h = 0; h < o.attackHits.Count; h++) o.attackHits[h] += each;
                o.attackAdd += each * o.attackHits.Count;
                if (o.logAttack) o.attackLog.Add($"山札の品:+{each}×{o.attackHits.Count}撃");
            }
            if ((f & DeckItems.Folded) != 0 && keptUsed > 0) o.damageMul *= 1f + FoldedPerStreak * st.keptStreak;   // 〈取っておきの銀弾〉別枠の最終与ダメージ・続いたターン数 × 25%・上限なし (answer13)
            if ((f & DeckItems.Switchboard) != 0 && o.firedNonBasic >= SwitchboardNeed) st.extraDrawNext += SwitchboardItemDraw;   // 〈仕込みの切替器〉 (目盛りの削りは DeckEffects.cs)
            if ((f & DeckItems.IdleWeight) != 0)                                                // 〈空回りの錘〉: どの端子にも挿さなかった出目 (条件に合わず外れた目も)
            {
                int idle = 0;
                foreach (int x in o.slotOfDie) if (x < 0) idle++;
                st.extraDrawNext += Math.Min(IdleWeightDrawMax, idle);
            }
        }

        /// <summary>挑戦カードの裏のうち、 端子の解決の中で効く物 (Resolve の 6e・値は <see cref="CardRules"/>)。
        /// 〈刃研ぎ〉撃ごとの攻撃 +N・〈追い打ち〉ボスのダウン中のターンの撃ごとの攻撃 +N・〈手捌き〉使い捨てを使うたびブロック +N・〈手馴染み〉役が立ったターン ブロック +N・
        /// 〈初太刀〉戦闘の 1 ターン目の撃ごとの出血 +N (5f の出血に足す)。</summary>
        private static void ApplyCardBuffs(TerminalBattleState st, TerminalOutcome o)
        {
            int each = CardRules.BladeAtk + (BossDown.ActiveNow ? CardRules.PursuitAtk : 0);
            if (each > 0 && o.attackHits.Count > 0)
            {
                for (int h = 0; h < o.attackHits.Count; h++) o.attackHits[h] += each;
                o.attackAdd += each * o.attackHits.Count;
                if (o.logAttack) o.attackLog.Add($"挑戦カード:+{each}×{o.attackHits.Count}撃");
            }
            if (CardRules.DisposeBlock > 0 && o.disposedNow > 0) o.blockAdd += CardRules.DisposeBlock * o.disposedNow;
            if (CardRules.RoleBlock > 0 && o.roleNow > 0) o.blockAdd += CardRules.RoleBlock;
            if (CardRules.FirstStrikeBleed > 0 && st.turn == 1)
                for (int h = 0; h < o.attackHitBleed.Count; h++) o.attackHitBleed[h] += CardRules.FirstStrikeBleed;
        }

        /// <summary>役の軸の品のうち、 端子の解決の中で効く物 (Resolve の 6d)。 旗は <see cref="TerminalBattleState.roleItems"/>。 役の回数はこのターンの役を足して数える。</summary>
        private static void ApplyRoleItems(TerminalEnv env, TerminalBattleState st, TerminalOutcome o, RoleKind? rk, int[] dice)
        {
            var f = st.roleItems;
            if (f == RoleItems.None) return;
            if ((f & RoleItems.Pouch) != 0 && env.diceRerolled) o.blockAdd += PouchBlock;   // 〈擦り切れた賽袋〉振り直したターン 役の有無を問わず (answer12)
            if (rk == null) return;
            var k = rk.Value;
            bool big = YachtRoles.IsBig(k);
            int count = st.roleCount + 1, each = 0, face = FaceOfRole(k);   // 品の条件は目の役 (2026-11-22・連なりはどの目でもない)
            if ((f & RoleItems.WoodTag) != 0 && FaceIn(WoodTagFaces, face)) { each += WoodTagAttack; o.blockAdd += WoodTagBlock; }    // 〈潮目の木札〉4〈攻め〉・5〈守り〉
            if ((f & RoleItems.Gear) != 0 && env.diceRerolled) each += face == GearFace || (big && k != RoleKind.Straight) ? GearAttackHigh : GearAttack;   // 〈追い目の賽筒〉振り直したターンのどの役も (3 の役かゾロ目は倍・answer9)
            if ((f & RoleItems.NotchBoard) != 0 && FaceIn(NotchBoardFaces, face))                                            // 〈符丁の刻み板〉3〈追撃〉・4〈攻め〉 (回数はどの役も)
                each += Math.Min(NotchBoardMax, NotchBoardPer * count) * (big ? 2 : 1);
            if ((f & RoleItems.Chipped) != 0 && FaceIn(ChippedFaces, face)) o.armorPen += ChippedPen;                       // 〈欠け目の見立て〉1〈特別〉・2〈封じ〉
            if ((f & RoleItems.Clapper) != 0 && face != 0 && face == st.roleLastFace) o.outgoingPctAdd += ClapperPct;         // 〈揃いの拍子木〉同じ目が続いた
            if ((f & RoleItems.Thunder) != 0 && big) { o.damageMul *= 1f + ThunderFinalPct; o.blockAdd += ThunderBlock; }    // 〈轟きの大賽〉
            else if ((f & RoleItems.Thunder) != 0 && env.diceRerolled) { o.damageMul *= 1f + ThunderRerollPct; o.blockAdd += ThunderRerollBlock; }   // 〈轟きの大賽〉振り直したターンの他の役 (answer12)
            if ((f & RoleItems.Bloodmark) != 0 && BloodmarkFires(k))                                                         // 〈連勝の符丁〉どの役でも (回数はどの役も・answer9)
                o.damageMul *= 1f + BloodmarkPer * count;
            if (each > 0 && o.attackHits.Count > 0)
            {
                for (int h = 0; h < o.attackHits.Count; h++) o.attackHits[h] += each;
                o.attackAdd += each * o.attackHits.Count;
                if (o.logAttack) o.attackLog.Add($"役の品:+{each}×{o.attackHits.Count}撃");
            }
        }

        /// <summary>役 <paramref name="k"/> の目 (2 個そろい・ゾロ目の目・連なりは 0)。 役の軸の品の条件 (2026-11-22)。</summary>
        public static int FaceOfRole(RoleKind k) => k == RoleKind.Straight ? 0 : YachtRoles.FaceOf(k);

        /// <summary>このターンに立つ役 (立たなければ null)。 CombatManager.ResolveRoles と同じ判定: 振った 3 つの目 (<see cref="YachtRoles.Evaluate"/>)・
        /// 〈凶運〉で封じた役は立たない・〈万象の賽盆〉で格上げした 2 個そろいはゾロ目 (<see cref="UpgradePair"/>)。 3 つの目では役は高々 1 つ。</summary>
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

        /// <summary>役の判定に使う目 = 目 + 怠惰で引いた量 (2026-11-23)。 足すのは目が <paramref name="at"/> (引いた後の目) と同じダイスだけ
        /// (BOT が仮の目で試す時は足さない)。 足すものが無ければ <paramref name="dice"/> をそのまま返す。</summary>
        public static int[] RoleDiceOf(int[] dice, int[] cut, int[] at)
        {
            if (dice == null || cut == null || at == null || cut.Length != dice.Length || at.Length != dice.Length) return dice;
            int[] r = null;
            for (int i = 0; i < dice.Length; i++)
            {
                if (cut[i] == 0 || dice[i] != at[i]) continue;
                if (r == null) r = (int[])dice.Clone();
                r[i] = dice[i] + cut[i];
            }
            return r ?? dice;
        }

        /// <summary>〈万象の賽盆〉: <paramref name="asTriple"/> なら 2 個そろいを同じ目のゾロ目にする (そのゾロ目が〈凶運〉で封じられていれば 2 個そろいのまま)。</summary>
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
            if (ItemSystem.PassiveSkills.AxisRules.Owns(run, GameLoop.ItemIds.RoleChipped)) f |= RoleItems.Chipped;
            if (ItemSystem.PassiveSkills.AxisRules.Owns(run, GameLoop.ItemIds.RoleGear)) f |= RoleItems.Gear;
            if (ItemSystem.PassiveSkills.AxisRules.Owns(run, GameLoop.ItemIds.RoleClapper)) f |= RoleItems.Clapper;
            if (ItemSystem.PassiveSkills.AxisRules.Owns(run, GameLoop.ItemIds.RoleThunder)) f |= RoleItems.Thunder;
            if (ItemSystem.PassiveSkills.AxisRules.Owns(run, GameLoop.ItemIds.RoleBloodmark)) f |= RoleItems.Bloodmark;
            if (ItemSystem.PassiveSkills.AxisRules.Owns(run, GameLoop.ItemIds.RoleBag)) f |= RoleItems.Pouch;
            return f;
        }

        /// <summary>所持品から山札の品の旗を作る (戦闘の始め・CombatManager.BeginTerminalDeck)。</summary>
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
            (GameLoop.ItemIds.DeckTongs, DeckItems.Tongs), (GameLoop.ItemIds.DeckPride, DeckItems.Pride), (GameLoop.ItemIds.DeckBroom, DeckItems.Broom),
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

        /// <summary>充電を n 払う (解決の中の手元の充電から引き、 支払い 1 回として控える)。</summary>
        private static void Pay(TerminalOutcome o, ref int charge, int n)
        {
            if (n <= 0) return;
            charge -= n; o.chargeSpend += n; o.chargePays.Add(n);
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
        private static int Gauge(TerminalBattleState st, string id, int add, int need, int max = int.MaxValue)
        {
            st.gauge.TryGetValue(id, out int g); g += add;
            int fired = 0;
            // 速度 (2026-11-21・結果は同じ): 1 つずつ引くと溜まりの量に比例して回る。 〈打ち直し〉で出目が数千万まで膨らんだ戦闘で 1 回の解決が 10 ms を超え、 ワーカーが 16 分止まった
            if (need > 0 && g >= need && max > 0) { fired = Math.Min(max, g / need); g -= fired * need; }
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
                case TerminalIds.Foundry:      // 1 ターンに FoundryMaxPerTurn 回まで (何枚・〈残響〉で何回走っても合わせて・余りは溜まりに残る)
                {
                    int got = Gauge(st, name, sum, GaugeNeed(name), FoundryMaxPerTurn - st.foundryFiredTurn);
                    st.foundryFiredTurn += got; st.foundryBonus += got; break;
                }
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
                // 軸の端子 (2026-11-22): 偏重 (軸の品の数) でなく戦闘の状態を読む (§24)
                case TerminalIds.Sight: o.attackAdd += sum; o.critMultAdd += (up ? SightPerCritUp : SightPerCrit) * env.critsBattle; break;
                case TerminalIds.VitalRead: o.forceCrit = true; o.critMultAdd += (up ? VitalPerCritUp : VitalPerCrit) * env.critsBattle; break;
                case TerminalIds.Barb: o.attackAdd += sum; o.bleedOnHit += Math.Max(up ? BarbMinUp : BarbMin, env.enemyBleed / BarbDiv); break;
                case TerminalIds.BloodTap: o.outgoingPctAdd += BloodTapPct * (env.enemyBleed / BloodTapPer); break;      // 強化は条件 (4 以下) だけ
                case TerminalIds.Mister: o.poison += sum + (env.enemyPoison > 0 ? MisterPoisoned : MisterBase) + (up ? MisterBonusUp : 0); break;
                case TerminalIds.PoisonScale: o.attackAdd += sum + (up ? PoisonScaleAttackUp : PoisonScaleAttack) * (env.enemyPoison / PoisonScalePer); break;
                case TerminalIds.Stoker: o.heat += sum + (env.heatHot ? StokerHot : StokerBase) + (up ? StokerBonusUp : 0); break;
                case TerminalIds.AfterHeat: o.attackAdd += sum + (up ? AfterHeatPerBurstUp : AfterHeatPerBurst) * env.bursts; break;
                case TerminalIds.Leak: o.chargeAdd += sum + (env.charge >= ChargeCore.OverchargeAt ? LeakOver : LeakBase) + (up ? LeakBonusUp : 0); break;
                case TerminalIds.BigDischarge:      // 払うのは呼び手 (5.) が 1 回だけ (paidAll)。 回数 = このターンの充電の消費のうち、 これより前の分 (振り直し・先に払った端子)
                    o.attackAdd += (int)Math.Floor(paidAll * (1f + BigDischargePerSpend * Math.Max(0, env.chargeSpendsTurn + o.chargePays.Count - 1))) + (up ? sum : 0); break;      // 〈裁きの雷＋〉は挿した出目も攻撃に
                case TerminalIds.Patch: o.blockAdd += sum; o.shield += (env.justGuardLast ? PatchJust : PatchBase) + (up ? PatchBonusUp : 0); break;
                case TerminalIds.ShieldPush:
                {
                    // 足した分と同じ量のシールドを払う (払える分まで・〈残響〉の 2 回目は残りのシールドから)
                    int left = Math.Max(0, env.shield - o.shieldSpend);
                    int push = Math.Min(left, (int)Math.Floor(left * (up ? ShieldPushPctUp : ShieldPushPct)));
                    o.attackAdd += sum + push; o.shieldSpend += push; break;
                }
                // 使い捨て
                case TerminalIds.Detonator: o.attackAdd += sum * (up ? DetonatorMulUp : DetonatorMul); break;
                case TerminalIds.Piercer: o.attackAdd += sum * (up ? PiercerMulUp : 1); o.ignoreDefense = true; break;
                case TerminalIds.ReliefValve: o.takenMul *= 1f - ReliefCutPct / 100f; if (up) o.shield += ReliefShieldUp; break;
                case TerminalIds.FirstAid: { int h = sum * FirstAidMul + (up ? FirstAidBonusUp : 0); o.heal += h; if (up) o.overhealToShield += h; break; }
                case TerminalIds.FullCharge: o.chargeFill = true; if (up) o.chargeCapAdd += FullChargeCapUp; break;
                case TerminalIds.CoolShot: o.retreatStage++; if (up) o.blockAdd += sum; break;
                case TerminalIds.Copier: o.copyTarget = empty(slot); if (up) o.chargeAdd += sum; break;
                case TerminalIds.BurnOut: o.attackAdd += sum * (up ? BurnOutMulUp : BurnOutMul); o.burnTarget = empty(slot); break;   // 候補は 4. で確かめ済み (無ければ不発)
                // 采配と共通の端子 (2026-11-20)
                case TerminalIds.BurnSlash: o.attackAdd += sum * (up ? BurnSlashMulUp : BurnSlashMul) + st.disposedTotal * (up ? BurnSlashPerUp : BurnSlashPer); break;   // 回数はこのターンより前の分 (1 以上でないと発動しない・4.)
                case TerminalIds.Reload: if (up) o.chargeAdd += sum; break;      // 戻す 1 枚は 5b
                case TerminalIds.OneCut: o.attackAdd += sum * (up ? OneCutMulUp : OneCutMul); o.ignoreDefense = true; break;      // 攻撃の端子は 1 枚だけ (4.) なので撃は 1 つ
                case TerminalIds.Bell: st.bells++; if (up) o.chargeAdd += sum; break;
                case TerminalIds.Mikiri: o.blockAdd += sum * (env.heavyTurn ? 2 : 1); break;      // 強化は条件 (4 以下) だけ
                case TerminalIds.Crush: o.attackAdd += sum + p1; break;      // 目盛りを割った時の追加は本体の撃の輪 (attackHitIds)
                // 役の軸の端子 (2026-11-21)。 立つ役は Resolve の 5. の頭で決めた o.roleNow (ResolveRoles と同じ判定)
                case TerminalIds.DiceTable: o.chargeAdd += sum; o.rerollDiscountNext += Math.Min(up ? DiceTableCapUp : DiceTableCap, st.roleCount + (o.roleNow > 0 ? 1 : 0)); break;
                case TerminalIds.RoleDrop:
                    o.attackAdd += sum + (o.roleNow > 0 ? (st.roleCount + 1) * (up ? RoleDropPerUp : RoleDropPer) * (o.roleNow >= 2 ? 2 : 1) : 0);
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
                more = BeforeShuffle(discard, exiled, Remaining, state, hand, permanent, returnNext);
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
        /// <remarks><paramref name="hand"/> / <paramref name="perm"/> / <paramref name="ret"/> = 手札・場に残る端子・次のターンの手札へ戻る使い捨て (最後の攻撃の端子の判定 <see cref="TerminalCards.KeepsLastAttack"/> に読む)。</remarks>
        public static int BeforeShuffle(List<string> discard, List<string> exiled, int deckTotal, TerminalBattleState st, List<string> hand, List<string> perm, List<string> ret)
        {
            st.shuffles++; st.shuffledNow += discard.Count;
            st.extraDrawNext += st.bells;      // 〈巡りの鐘〉: 次のターンに多く引く (このターンの分は BeginTurn で取り出し済み)
            // 〈無銘の設計図〉: 捨て札の基本の端子 (攻撃・防御・充電) を 1 枚この戦闘から外す (山札が 3 枚以下なら外さない)。 先に捨てた方から。
            //   最後の攻撃の端子は外さない (捨て札に残し、 次の基本の端子を見る・2026-11-21)
            if ((st.deckItems & DeckItems.Blueprint) != 0 && deckTotal > TerminalCards.MinDeckSize)
                for (int i = 0; i < discard.Count; i++)
                {
                    string id = discard[i];
                    if (TerminalCards.Get(id)?.basic != true) continue;
                    discard.RemoveAt(i);
                    if (TerminalCards.KeepsLastAttack(id, discard, hand, perm, ret)) { discard.Insert(i, id); continue; }
                    exiled?.Add(id); break;
                }
            // 〈永久機関の主軸〉: 切り直すたび 1 枚多く引く (1 ターンに 2 枚まで)・この戦闘の切り直しが 2 の倍数になるたび、 このターン挿した端子がすべて 2 回働く (〈残響〉と同じ倍化)
            if ((st.deckItems & DeckItems.Engine) != 0)
            {
                if (st.shuffles % TerminalCards.EngineEvery == 0) st.doubleAll = true;
                if (st.engineDrawTurn < TerminalCards.EngineDrawMax) { st.engineDrawTurn++; return 1; }
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
        public void DrawTurn(int size, string retained = null, float stopChance = 0f, int forceStop = 0)
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
            var drawnHand = forceStop > 0 ? new List<int>() : null;   // このターンに引いた手札の添字 (挑戦カード〈目詰まり〉・クェシナの怠惰)
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
                else drawnHand?.Add(hand.Count);
                hand.Add(id);
            }
            //   挑戦カード〈目詰まり〉・クェシナの怠惰 (2026-11-22): このターンに引いた手札の端子を、 引いた順に forceStop 枚止める (乱数は引かない)
            if (drawnHand != null) for (int k = 0; k < drawnHand.Count && k < forceStop; k++) stoppedHand.Add(drawnHand[k]);
            // 〈影写し〉の写し (2026-11-14): 引く枚数とは別に、 手札の最後へ 1 枚足す (引く枚数を減らさない・止まる対象でもない)。
            //   使わなければほかの手札と同じに次のターンの頭で捨て札へ行き、 この戦闘の山札を回る (戦闘が終われば消える = BeginBattle で作り直す)
            if (copyNext != null) { hand.Add(copyNext); copyNext = null; }
            // 戻る使い捨て (〈緊急再装填〉〈解体屋の袋〉・2026-11-20): 写しと同じく、 引く枚数とは別に手札の最後へ
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

        /// <summary>次のターンの手札に戻す使い捨て (〈緊急再装填〉〈解体屋の袋〉・2026-11-20)。 <see cref="FinishTurn"/> が <see cref="DisposeFired"/> で入れ、
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
                    //   使い捨ては発動したターンに戦闘から外れるので残せない (印を数えず、 繋ぎの 1 枚も使わない・2026-11-25 作者)
                    if (link && !linkUsed && TerminalCards.Get(board[s])?.disposable != true) { linkUsed = true; l.Add(board[s]); }
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
            DisposeFired(board, o, hand, exiled, returnNext, state, draw, discard, permanent);
            Burn(burn, hand, exiled, draw, discard, permanent, returnNext);
            return retain;
        }

        /// <summary>使った使い捨てを手札から外す (この戦闘から外す・2026-11-20 に共通化)。 本体の <see cref="FinishTurn"/> と BOT の先読み (SampleNextBoard) が同じ関数を呼ぶ。
        /// 戻る使い捨ては <paramref name="returnNext"/> (次のターンの手札) へ:
        /// 〈緊急再装填〉の対象 (<see cref="TerminalOutcome.reloadTargets"/>)・〈解体屋の袋〉(この戦闘の使い捨ての回数が BagEvery (4) の倍数をまたぐたび、 最後に外れた使い捨てを 1 枚・1 戦闘 5 回まで)。</summary>
        /// <remarks>最後の攻撃の端子 (<see cref="TerminalCards.KeepsLastAttack"/>・<paramref name="draw"/> / <paramref name="discard"/> / <paramref name="perm"/> と手札・戻る使い捨てで数える) は外さず手札に残す
        /// ── ターンの終わりに使った端子として捨て札へ行く。</remarks>
        public static void DisposeFired(List<string> board, TerminalOutcome o, List<string> hand, List<string> exiled, List<string> returnNext, TerminalBattleState st,
                                        List<string> draw, List<string> discard, List<string> perm)
        {
            foreach (int s in o.fired)
            {
                if (s < 0 || s >= board.Count) continue;
                string id = board[s];
                var d = TerminalCards.Get(id);
                if (d == null || !d.disposable) continue;
                int at = hand.IndexOf(id);
                if (at < 0) continue;
                hand.RemoveAt(at);
                if (o.reloadTargets.Contains(s)) { returnNext.Add(id); continue; }
                if (TerminalCards.KeepsLastAttack(id, draw, discard, hand, perm, returnNext)) { hand.Insert(at, id); continue; }      // 最後の攻撃の端子は外さない
                exiled.Add(id);
            }
            if ((st.deckItems & DeckItems.Bag) != 0 && o.disposedNow > 0)
            {
                int n = st.disposedTotal / TerminalCards.BagEvery - (st.disposedTotal - o.disposedNow) / TerminalCards.BagEvery;
                for (int k = 0; k < n && st.bagReturns < TerminalCards.BagMax; k++)
                {
                    int j = exiled.FindLastIndex(x => TerminalCards.Get(x)?.disposable == true);
                    if (j < 0) break;
                    returnNext.Add(exiled[j]); exiled.RemoveAt(j); st.bagReturns++;
                }
            }
        }

        /// <summary>〈火葬の剣〉: 挿さずに空けた手札の端子 <paramref name="burn"/> をこの戦闘から外す (null = 何もしない)。 本体の <see cref="FinishTurn"/> と BOT の先読み (SampleNextBoard) が同じ関数を呼ぶ。
        /// 最後の攻撃の端子 (<see cref="TerminalCards.KeepsLastAttack"/>) は外さず手札に残す (ターンの終わりに捨て札へ)。</summary>
        public static void Burn(string burn, List<string> hand, List<string> exiled, List<string> draw, List<string> discard, List<string> perm, List<string> returnNext)
        {
            if (burn == null) return;
            int at = hand.IndexOf(burn);
            if (at < 0) return;
            hand.RemoveAt(at);
            if (TerminalCards.KeepsLastAttack(burn, draw, discard, hand, perm, returnNext)) { hand.Insert(at, burn); return; }
            exiled.Add(burn);
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
