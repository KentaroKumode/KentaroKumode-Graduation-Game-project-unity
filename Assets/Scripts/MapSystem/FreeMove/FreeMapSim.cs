using System;
using System.Collections.Generic;

namespace MapSystem.FreeMove
{
    /// <summary>追跡者の状態。 「巡回 → 警戒 → 追跡 → 見失う」。</summary>
    public enum FoeMode
    {
        /// <summary>縄張りの巡回の輪を毎回同じ順で回る。</summary>
        Patrol = 0,
        /// <summary>プレイヤーの新しい痕跡、 または置かれた囮罠に気づいて、 そちらへ向かう。</summary>
        Alert = 1,
        /// <summary>補足したプレイヤーを追う。</summary>
        Chase = 2,
    }

    /// <summary><see cref="FreeMapSim"/> の操作 1 回の間に起きたことを返すフラグ。</summary>
    [Flags]
    public enum FreeMapEvent
    {
        None = 0,
        /// <summary>点に着いた (<see cref="FreeMapSim.LastArrived"/>)。</summary>
        Arrived = 1 << 0,
        /// <summary>補足された (追跡者が追跡に入った瞬間)。</summary>
        Detected = 1 << 1,
        /// <summary>追跡者が見失った。</summary>
        FoeLost = 1 << 2,
        /// <summary>追跡者がプレイヤーの痕跡に気づいた / 囮罠に気を引かれた。</summary>
        FoeAlerted = 1 << 3,
        /// <summary>追跡者に捕まった。 戦闘の決着まで手番は進まない。</summary>
        Caught = 1 << 4,
        /// <summary>使い魔に接触した。 戦闘の決着まで手番は進まない。</summary>
        FamiliarContact = 1 << 5,
        /// <summary>使い魔が放たれた。</summary>
        FamiliarLaunched = 1 << 6,
        /// <summary>見えている使い魔が消えた (目標地点でプレイヤーを見つけられなかった)。</summary>
        FamiliarVanished = 1 << 7,
        /// <summary>追跡者が囮罠を踏んで足止めされた。</summary>
        LureTriggered = 1 << 8,
        /// <summary>手番が 1 つ以上進んだ。</summary>
        TurnPassed = 1 << 9,
        /// <summary>前線が進んだ。</summary>
        FrontAdvanced = 1 << 10,
        /// <summary>前線の天候で道が崩れた / 点が沈んだ (地図の形が変わった)。</summary>
        MapChanged = 1 << 11,
        /// <summary>物資が尽き、 HP も尽きた。</summary>
        Died = 1 << 12,
    }

    /// <summary>使い魔。 本体の手先で、 プレイヤーの最後に分かった位置へ向かう。 猟犬は倒しても消えず本体の元へ戻る。</summary>
    public struct Familiar
    {
        public int pos;
        /// <summary>向かう点。 -1 = まだ目標が無い。</summary>
        public int target;
        public bool hound;
    }

    /// <summary>移動に払う物資と HP の窓口。 素の C# 側は RunState を知らないのでここを通す。</summary>
    public interface IFreeMapWallet
    {
        int Provision { get; }
        void SpendProvision(int amount);
        /// <summary>HP を払う。 HP が尽きたら true。 <b>死ぬ移動も許す</b> (§23-21) ── 拒否はしない。</summary>
        bool PayHp(int amount);
    }

    /// <summary>魔石の読み。 追跡者への距離の段と方角 (使い魔には反応しない)。</summary>
    public struct StoneReading
    {
        /// <summary>0 = 反応なし / 1 = 遠い (≤4 歩) / 2 = かなり近い (≤2 歩) / 3 = 察知されている (追跡中)。</summary>
        public int level;
        /// <summary>方角 0..7 = 東・北東・北・北西・西・南西・南・南東 (画面で上 = 北 = y が小さい側)。 level 0 では -1。</summary>
        public int bearing;
        public static readonly string[] BearingNames = { "東", "北東", "北", "北西", "西", "南西", "南", "南東" };
        public string Text
        {
            get
            {
                if (level <= 0) return "反応なし";
                string dir = bearing >= 0 ? BearingNames[bearing] : "";
                return level == 3 ? $"察知されている（{dir}）" : level == 2 ? $"かなり近い（{dir}）" : $"遠い（{dir}）";
            }
        }
    }

    /// <summary>補足されている時の「最短で何手で捕まるか」(こちらがその場に留まった場合)。</summary>
    public struct CatchEta
    {
        public int turns;
        /// <summary>0 = 本体 / 1 = 猟犬 / 2 = 使い魔。 turns が int.MaxValue なら意味を持たない。</summary>
        public int who;
        public bool Valid => turns != int.MaxValue;
        public static readonly CatchEta None = new CatchEta { turns = int.MaxValue, who = -1 };
        public string WhoName => who == 0 ? "本体" : who == 1 ? "猟犬" : "使い魔";
    }

    /// <summary>
    /// 航行マップの動く中身 (手番制・2026-10-18)。 参照実装は docs/design/map-grid-prototype.html。
    ///
    /// <para><b>移動は道でつながった隣の点へ 1 歩</b>。 自分が 1 歩動く (または待つ・偵察する) ごとに 1 手番が進み、
    /// 追跡者が 1 度動く。 戦闘・イベント・照明・囮罠は手番を使わない。 物資は距離によらず 1 歩ごとに一定。</para>
    ///
    /// <para>乱数は前線の天候 (道が崩れる・点が沈む) の選択だけで、
    /// <c>GameRng.Range(.., "map.free.weather", 層 × 100000 + 手番 × 10 + 試行)</c> ── 同じ操作なら同じ結果になる。</para>
    ///
    /// <para>UnityEngine に依存しない (BOT が同期的に回す・単体で検算できる)。</para>
    /// </summary>
    public sealed class FreeMapSim
    {
        public readonly FreeMapLayout Layout;
        public readonly int Floor;
        private readonly IFreeMapWallet _wallet;

        // ── 地図 (前線の天候で崩れる) ──
        private readonly List<int>[] _adj;
        private readonly bool[] _sunk;

        // ── 自分 ──
        public int Me { get; private set; }
        /// <summary>直前に居た点 (追跡者の戦闘から逃げた時に戻る先)。</summary>
        public int PrevNode { get; private set; }
        public int LastArrived { get; private set; } = -1;
        public bool Dead { get; private set; }
        public int Turn { get; private set; }
        /// <summary>状態が変わるたびに増える番号。 表示が「描き直すか」を安く判定するために読む。</summary>
        public int Version { get; private set; }
        /// <summary>照明が点いている残り手番。</summary>
        public int FlareTurnsLeft { get; private set; }
        public bool FlareOn => FlareTurnsLeft > 0;

        // ── 見えているもの ──
        /// <summary>マスの種別が判明している点。 点と道の配置は層全体が見える。</summary>
        public readonly bool[] Known;
        /// <summary>使い切った (起動済みの) 点。 沈める点の候補から外す。 MapManager が写す。</summary>
        public readonly bool[] Done;

        // ── 痕跡 ──
        private readonly int[] _trace;
        private const int NoTrace = int.MinValue;

        // ── 追跡者 ──
        public FoeProfile Profile { get; private set; }
        public bool HasFoe { get; private set; }
        public bool FoeAlive { get; private set; }
        public int FoeNode { get; private set; }
        public FoeMode FoeMode { get; private set; }
        /// <summary>追跡者が最後にプレイヤーを補足した点。</summary>
        public int LastSeen { get; private set; } = -1;
        private int _lose;
        private int _patrol;
        private int _sinceSummon = 99;
        /// <summary>足止めの残り手番 (囮罠・逃げた直後)。</summary>
        public int FoeFreezeTurns { get; private set; }
        private int _alertLure = -1;
        public readonly List<Familiar> Familiars = new List<Familiar>();

        // ── 囮罠 ──
        public struct Lure { public int node; public int turnsLeft; }
        public readonly List<Lure> Lures = new List<Lure>();

        // ── 接触 (戦闘の決着待ち) ──
        public enum ContactKind { None = 0, Body = 1, Familiar = 2 }
        public ContactKind Contact { get; private set; }
        public bool InFight => Contact != ContactKind.None;
        /// <summary>着いた点に接触して戦闘に入った時、 戦闘の後に発動させる点。</summary>
        public int PendingArrival { get; private set; } = -1;

        // ── 天候 ──
        public WeatherKind Weather { get; private set; }
        /// <summary>前線の x (左端からの距離)。 まだ届いていなければ負の無限大。</summary>
        public double Front { get; private set; } = double.NegativeInfinity;

        public FreeMapSim(FreeMapLayout layout, IFreeMapWallet wallet, bool withFoe,
                          FoeProfile profile = null, WeatherKind? weather = null)
        {
            Layout = layout ?? throw new ArgumentNullException(nameof(layout));
            Floor = layout.floor;
            _wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
            int n = layout.Count;
            _adj = new List<int>[n];
            for (int i = 0; i < n; i++) _adj[i] = new List<int>(layout.adj[i]);
            _sunk = new bool[n];
            Known = new bool[n];
            Done = new bool[n];
            _trace = new int[n];
            for (int i = 0; i < n; i++) _trace[i] = NoTrace;

            Me = PrevNode = layout.start;
            Known[layout.start] = true;
            Known[layout.goal] = true;
            Done[layout.start] = true;
            _trace[Me] = 0;
            Weather = weather ?? WeatherInfo.ForFloor(Floor);

            HasFoe = withFoe && layout.waypoints.Count > 0;
            FoeAlive = HasFoe;
            Profile = profile ?? (HasFoe ? FoeProfile.ForFloor(Floor) : FoeProfile.Brute);
            FoeNode = layout.home;
            FoeMode = FoeMode.Patrol;
            if (HasFoe && Profile.hound)
                Familiars.Add(new Familiar { pos = FoeNode, target = -1, hound = true });
            Reveal(Me, FreeMapParams.Vision);
        }

        // =====================================================================
        //  読み取り
        // =====================================================================

        /// <summary>隣の点 (崩れた道・沈んだ点は含まない)。</summary>
        public IReadOnlyList<int> Neighbors(int i) => _adj[i];

        public bool HasRoad(int a, int b) => a >= 0 && b >= 0 && _adj[a].Contains(b);

        public bool IsSunk(int i) => _sunk[i];

        /// <summary>崩れずに残っている道 (a &lt; b)。</summary>
        public IEnumerable<(int a, int b)> Roads()
        {
            for (int a = 0; a < _adj.Length; a++)
                foreach (int b in _adj[a]) if (b > a) yield return (a, b);
        }

        /// <summary>道だけを通る歩数 (全点)。 つながっていなければ int.MaxValue。</summary>
        public int[] HopsFrom(int from) => FreeMapLayout.Hops(_adj, from);

        public int HopsBetween(int from, int to) => HopsFrom(from)[to];

        /// <summary>from から to へ向かう次の 1 歩 (最短の道・崩れた道は通らない)。 着いている / 行けないなら from。</summary>
        public int StepToward(int from, int to) => FreeMapLayout.StepToward(_adj, from, to);

        /// <summary>いま見える範囲 (道で何歩先まで)。 照明中は <see cref="FreeMapParams.FlareRange"/>。</summary>
        public int VisionRange => FlareOn ? FreeMapParams.FlareRange : FreeMapParams.Vision;

        /// <summary>点 i が前線の内側か。</summary>
        public bool InFront(int i) => Layout.pos[i].x <= Front;

        /// <summary>点 i で天候 k の効果が働いているか (前線の内側だけ)。</summary>
        public bool WeatherAt(int i, WeatherKind k) => Weather == k && InFront(i);

        /// <summary>霧で「?」に戻っている点か (道で 2 歩以上離れた点。 始点と終点は隠れない)。</summary>
        public bool FogHidden(int i, int[] hopsFromMe = null)
        {
            if (!WeatherAt(i, WeatherKind.Fog) || i == Layout.goal || i == Layout.start) return false;
            return (hopsFromMe ?? HopsFrom(Me))[i] >= 2;
        }

        /// <summary>点 i に痕跡があるか (痕跡の寿命は天候で伸び縮みする)。</summary>
        public bool HasTrace(int i)
        {
            if (_trace[i] == NoTrace) return false;
            return Turn - _trace[i] <= TraceLife(i);
        }

        /// <summary>痕跡の鮮度 0..1 (1 = いま付いた)。 痕跡が無ければ 0。</summary>
        public double TraceFreshness(int i)
        {
            if (!HasTrace(i)) return 0;
            double a = 1.0 - (double)(Turn - _trace[i]) / (TraceLife(i) + 1);
            return a < 0 ? 0 : a;
        }

        private double TraceLife(int i)
        {
            double life = FreeMapParams.TraceLife;
            if (WeatherAt(i, WeatherKind.Still)) life *= 2;
            if (WeatherAt(i, WeatherKind.Sand)) life /= 2;
            return life;
        }

        /// <summary>点 j へ 1 歩動く物資 (砂の前線の内側は 1.5 倍)。</summary>
        public int SupplyFor(int j)
            => (int)Math.Round(FreeMapParams.MoveSupply * (WeatherAt(j, WeatherKind.Sand) || WeatherAt(Me, WeatherKind.Sand) ? 1.5 : 1.0),
                               MidpointRounding.AwayFromZero);

        /// <summary>点 j へ 1 歩動くのに進む手番 (雪の前線の内側は 2)。</summary>
        public int TurnsFor(int j) => WeatherAt(j, WeatherKind.Snow) || WeatherAt(Me, WeatherKind.Snow) ? 2 : 1;

        /// <summary>点 j へ 1 歩動いた時の HP の損 (雹の前線の内側)。</summary>
        public int HpLossFor(int j) => WeatherAt(j, WeatherKind.Hail) ? 1 : 0;

        /// <summary>追跡者がいま見えているか (視界の内側)。</summary>
        public bool FoeVisible => FoeAlive && HopsFrom(Me)[FoeNode] <= VisionRange;

        /// <summary>いま見えている使い魔。</summary>
        public IEnumerable<Familiar> VisibleFamiliars()
        {
            var d = HopsFrom(Me);
            foreach (var f in Familiars) if (d[f.pos] <= VisionRange) yield return f;
        }

        /// <summary>追跡者の次の 1 歩 (予告用・状態は変えない)。 動かない性質・居ない時は <see cref="FoeNode"/> のまま。</summary>
        public int PeekFoeStep()
        {
            if (!FoeAlive || Profile.foeSteps <= 0 || FoeFreezeTurns > 0) return FoeNode;
            int patrol = _patrol; var mode = FoeMode;
            return ComputeFoeStep(ref mode, ref patrol);
        }

        /// <summary>魔石の読み。 追跡者への歩数と方角 (使い魔には反応しない)。</summary>
        public StoneReading Stone()
        {
            if (!FoeAlive) return new StoneReading { level = 0, bearing = -1 };
            int d = HopsFrom(Me)[FoeNode];
            int level = FoeMode == FoeMode.Chase ? 3
                      : d <= FreeMapParams.StoneNear ? 2
                      : d <= FreeMapParams.StoneFar ? 1 : 0;
            return new StoneReading { level = level, bearing = level > 0 ? Bearing(Me, FoeNode) : -1 };
        }

        /// <summary>from から見た to の方角 0..7 (東から反時計回り・上 = 北 = y が小さい側)。
        /// 三角関数を使わず、 傾きの比較だけで 45° ずつの 8 方位に分ける。</summary>
        public int Bearing(int from, int to)
        {
            double dx = Layout.pos[to].x - Layout.pos[from].x;
            double dy = Layout.pos[from].y - Layout.pos[to].y;   // 上 (y が小さい側) を正にする
            if (dx == 0 && dy == 0) return 0;
            double ax = Math.Abs(dx), ay = Math.Abs(dy);
            const double T = 0.41421356237;                       // tan(22.5°)
            int sector;                                           // 第 1 象限基準で 0..2 (東 / 北東 / 北)
            if (ay <= T * ax) sector = 0;
            else if (ax <= T * ay) sector = 2;
            else sector = 1;
            if (dx >= 0 && dy >= 0) return sector == 0 ? 0 : sector == 1 ? 1 : 2;
            if (dx < 0 && dy >= 0) return sector == 0 ? 4 : sector == 1 ? 3 : 2;
            if (dx < 0 && dy < 0) return sector == 0 ? 4 : sector == 1 ? 5 : 6;
            return sector == 0 ? 0 : sector == 1 ? 7 : 6;
        }

        /// <summary>補足されている時の「最短で何手で捕まるか」(こちらが留まった場合)。</summary>
        public CatchEta Eta()
        {
            var best = CatchEta.None;
            if (!FoeAlive || Contact != ContactKind.None) return best;
            var d = HopsFrom(Me);
            if (FoeMode == FoeMode.Chase && Profile.foeSteps > 0 && d[FoeNode] != int.MaxValue)
            {
                int t = Ceil(d[FoeNode], Profile.foeSteps);
                if (FoeFreezeTurns > 0) t += FoeFreezeTurns;
                if (t < best.turns) best = new CatchEta { turns = t, who = 0 };
            }
            foreach (var f in Familiars)
            {
                if (Profile.familiarSteps <= 0 || d[f.pos] == int.MaxValue) continue;
                bool chasing = f.hound ? FoeMode == FoeMode.Chase : f.target >= 0;
                if (!chasing) continue;
                int t = Ceil(d[f.pos], Profile.familiarSteps);
                if (t < best.turns) best = new CatchEta { turns = t, who = f.hound ? 1 : 2 };
            }
            return best;
        }

        private static int Ceil(int a, int b) => (a + b - 1) / b;

        // =====================================================================
        //  操作
        // =====================================================================

        public bool CanMoveTo(int j)
            => !Dead && !InFight && j >= 0 && j < _adj.Length && !_sunk[j] && _adj[Me].Contains(j);

        /// <summary>隣の点 j へ 1 歩。 物資を払い、 痕跡を残し、 追跡者が動く。</summary>
        public FreeMapEvent MoveTo(int j)
        {
            if (!CanMoveTo(j)) return FreeMapEvent.None;
            int cost = SupplyFor(j), turns = TurnsFor(j), hail = HpLossFor(j);
            var ev = FreeMapEvent.None;
            if (Pay(cost)) ev |= FreeMapEvent.Died;
            if (hail > 0 && _wallet.PayHp(hail)) ev |= FreeMapEvent.Died;
            PrevNode = Me;
            Me = j;
            Version++;
            _trace[j] = Turn + turns;
            Known[j] = true;
            LastArrived = j;
            ev |= FreeMapEvent.Arrived;
            if ((ev & FreeMapEvent.Died) != 0) { Dead = true; return ev; }
            ev |= PassTurns(turns);
            if ((ev & (FreeMapEvent.Caught | FreeMapEvent.FamiliarContact)) != 0) PendingArrival = j;
            return ev;
        }

        /// <summary>待つ (1 手番)。</summary>
        public FreeMapEvent Wait() => Dead || InFight ? FreeMapEvent.None : PassTurns(1);

        /// <summary>偵察 (1 手番): 道で <see cref="FreeMapParams.ReconRange"/> 歩先まで見える。</summary>
        public FreeMapEvent Recon()
        {
            if (Dead || InFight) return FreeMapEvent.None;
            Reveal(Me, FreeMapParams.ReconRange);
            return PassTurns(1);
        }

        /// <summary>照明 (手番は使わない): 物資を払い、 <see cref="FreeMapParams.FlareTurns"/> 手番のあいだ
        /// 道で <see cref="FreeMapParams.FlareRange"/> 歩先まで見える。 追跡者の補足の歩数も同じだけ伸びる。</summary>
        public FreeMapEvent UseFlare()
        {
            if (Dead || InFight) return FreeMapEvent.None;
            var ev = Pay(FreeMapParams.FlareSupply) ? FreeMapEvent.Died : FreeMapEvent.None;
            if ((ev & FreeMapEvent.Died) != 0) { Dead = true; return ev; }
            FlareTurnsLeft = FreeMapParams.FlareTurns;
            Reveal(Me, FreeMapParams.FlareRange);
            Version++;
            return ev;
        }

        /// <summary>通常の戦闘から逃げた: 直前の点へ戻る (手番は使わない)。</summary>
        public void FleeToPrevious()
        {
            Version++;
            if (Me != PrevNode && _adj[Me].Contains(PrevNode)) Me = PrevNode;
            PendingArrival = -1;
            Reveal(Me, VisionRange);
        }

        /// <summary>多めに回復する休憩など、 手番だけを進める。</summary>
        public FreeMapEvent PassTurnsExternal(int n) => Dead || InFight ? FreeMapEvent.None : PassTurns(n);

        /// <summary>囮罠を今いる点に置く (手番は使わない)。 近くの追跡者は気を引かれて向かい、 踏むと足止めされる。</summary>
        public FreeMapEvent PlaceLure()
        {
            if (Dead || InFight) return FreeMapEvent.None;
            Lures.RemoveAll(l => l.node == Me);
            Lures.Add(new Lure { node = Me, turnsLeft = FreeMapParams.LureLife });
            Version++;
            if (FoeAlive && FoeMode != FoeMode.Chase && FoeNotices(Me))
            {
                FoeMode = FoeMode.Alert; _alertLure = Me;
                return FreeMapEvent.FoeAlerted;
            }
            return FreeMapEvent.None;
        }

        /// <summary>点 i に有効な囮罠が置かれているか。</summary>
        public bool HasLureAt(int i)
        {
            foreach (var l in Lures) if (l.node == i) return true;
            return false;
        }

        private bool FoeNotices(int lureNode) => HopsFrom(FoeNode)[lureNode] <= FreeMapParams.LureNotice;

        /// <summary>マスを起動した (使い切り)。 沈める点の候補から外す。</summary>
        public void MarkDone(int i) { if (i >= 0 && i < Done.Length) { Done[i] = true; Version++; } }

        /// <summary>着いた点が戦闘の後の発動を待っているなら、 その点を返して待ちを解く。 無ければ -1。</summary>
        public int TakePendingArrival()
        {
            int p = PendingArrival;
            if (p < 0 || p != Me) { PendingArrival = -1; return -1; }
            PendingArrival = -1;
            return p;
        }

        // ── 戦闘の決着 ──

        /// <summary>追跡者に勝った: 追跡者は倒れ、 使い魔も消える。</summary>
        public void ResolveBodyWon()
        {
            Version++;
            Contact = ContactKind.None;
            FoeAlive = false;
            Familiars.Clear();
        }

        /// <summary>追跡者の戦闘から逃げた: 直前の点へ戻り、 追跡者は <see cref="FreeMapParams.FleeFreezeTurns"/> 手番足止めされる。</summary>
        public void ResolveBodyFled()
        {
            Version++;
            Contact = ContactKind.None;
            PendingArrival = -1;
            if (Me != PrevNode && _adj[Me].Contains(PrevNode)) Me = PrevNode;
            FoeFreezeTurns = Math.Max(FoeFreezeTurns, FreeMapParams.FleeFreezeTurns);
            Reveal(Me, VisionRange);
        }

        /// <summary>使い魔との戦闘が終わった (勝っても逃げても使い魔は消える。 猟犬は本体の元へ戻る)。</summary>
        public void ResolveFamiliarFight(bool fled)
        {
            Version++;
            Contact = ContactKind.None;
            for (int i = Familiars.Count - 1; i >= 0; i--)
            {
                var f = Familiars[i];
                if (f.pos != Me) continue;
                if (f.hound) { f.pos = FoeNode; f.target = -1; Familiars[i] = f; }
                else Familiars.RemoveAt(i);
            }
            if (fled)
            {
                PendingArrival = -1;
                if (Me != PrevNode && _adj[Me].Contains(PrevNode)) Me = PrevNode;
                Reveal(Me, VisionRange);
            }
        }

        // =====================================================================
        //  手番
        // =====================================================================

        private bool Pay(int cost)
        {
            int pay = Math.Min(cost, Math.Max(0, _wallet.Provision));
            if (pay > 0) _wallet.SpendProvision(pay);
            int short_ = cost - pay;
            if (short_ <= 0) return false;
            return _wallet.PayHp((short_ + FreeMapParams.SupplyPerHp - 1) / FreeMapParams.SupplyPerHp);
        }

        private void Reveal(int center, int range)
        {
            var d = HopsFrom(center);
            for (int i = 0; i < d.Length; i++) if (d[i] <= range) Known[i] = true;
        }

        private FreeMapEvent PassTurns(int n)
        {
            var ev = FreeMapEvent.None;
            Version++;
            for (int k = 0; k < n; k++)
            {
                Turn++;
                ev |= FreeMapEvent.TurnPassed;
                ev |= FoeTurn();
                TickLures();
                if (Turn % FreeMapParams.FrontEvery == 0) ev |= AdvanceFront();
                if (Contact != ContactKind.None) break;
            }
            Reveal(Me, VisionRange);
            return ev;
        }

        private void TickLures()
        {
            for (int i = Lures.Count - 1; i >= 0; i--)
            {
                var l = Lures[i]; l.turnsLeft--;
                if (l.turnsLeft <= 0) Lures.RemoveAt(i); else Lures[i] = l;
            }
        }

        private FreeMapEvent FoeTurn()
        {
            var ev = FreeMapEvent.None;
            if (FoeAlive)
            {
                var dMe = HopsFrom(Me);
                int detect = FlareOn ? FreeMapParams.FlareRange : FreeMapParams.DetectRange;
                if (dMe[FoeNode] <= detect)
                {
                    if (FoeMode != FoeMode.Chase) ev |= FreeMapEvent.Detected;
                    FoeMode = FoeMode.Chase; LastSeen = Me; _lose = 0;
                }
                else if (FoeMode == FoeMode.Chase && ++_lose > FreeMapParams.QuietLose)
                {
                    FoeMode = FoeMode.Patrol; ev |= FreeMapEvent.FoeLost;
                }
                else if (FoeMode == FoeMode.Patrol && HasTrace(FoeNode))
                {
                    FoeMode = FoeMode.Alert; ev |= FreeMapEvent.FoeAlerted;
                }
                // 囮罠: 追跡していない間、 近くに置かれた囮罠へ気を引かれる (一番近いもの)
                if (FoeMode != FoeMode.Chase)
                {
                    int lure = NearestLureFromFoe();
                    if (lure >= 0) { if (FoeMode != FoeMode.Alert || _alertLure != lure) ev |= FreeMapEvent.FoeAlerted; FoeMode = FoeMode.Alert; _alertLure = lure; }
                }

                if (FoeFreezeTurns > 0) FoeFreezeTurns--;
                else
                {
                    for (int k = 0; k < Profile.foeSteps; k++)
                    {
                        var mode = FoeMode; int patrol = _patrol;
                        FoeNode = ComputeFoeStep(ref mode, ref patrol);
                        FoeMode = mode; _patrol = patrol;
                        if (StepOnLure()) { ev |= FreeMapEvent.LureTriggered; break; }
                        if (FoeNode == Me) break;
                    }
                }
                if (FoeAlive && FoeNode == Me) { Contact = ContactKind.Body; ev |= FreeMapEvent.Caught; }

                if (Profile.summons && FoeMode == FoeMode.Chase && ++_sinceSummon >= FreeMapParams.SummonEvery)
                {
                    _sinceSummon = 0;
                    Familiars.Add(new Familiar { pos = FoeNode, target = LastSeen, hound = false });
                    ev |= FreeMapEvent.FamiliarLaunched;
                }
            }

            ev |= FamiliarsTurn();
            if (FlareTurnsLeft > 0) FlareTurnsLeft--;
            return ev;
        }

        private FreeMapEvent FamiliarsTurn()
        {
            var ev = FreeMapEvent.None;
            if (Familiars.Count == 0) return ev;
            bool vanished = false;
            for (int i = 0; i < Familiars.Count; i++)
            {
                var f = Familiars[i];
                if (f.hound) f.target = FoeMode == FoeMode.Chase ? LastSeen : FoeNode;
                for (int k = 0; k < Profile.familiarSteps; k++)
                {
                    if (HopsFrom(f.pos)[Me] <= FreeMapParams.FamiliarSee) f.target = Me;
                    if (f.target < 0) break;
                    f.pos = FreeMapLayout.StepToward(_adj, f.pos, f.target);
                    if (f.pos == Me) break;
                }
                if (!f.hound && f.pos == f.target && f.pos != Me && HopsFrom(f.pos)[Me] > FreeMapParams.FamiliarSee)
                {
                    f.target = -2;   // 消える印
                    vanished = true;
                }
                Familiars[i] = f;
            }
            if (vanished)
            {
                Familiars.RemoveAll(f => f.target == -2);
                ev |= FreeMapEvent.FamiliarVanished;
            }
            foreach (var f in Familiars)
                if (f.pos == Me && Contact == ContactKind.None) { Contact = ContactKind.Familiar; ev |= FreeMapEvent.FamiliarContact; break; }
            return ev;
        }

        private int NearestLureFromFoe()
        {
            if (Lures.Count == 0) return -1;
            var d = HopsFrom(FoeNode);
            int best = -1, bd = int.MaxValue;
            foreach (var l in Lures)
                if (d[l.node] <= FreeMapParams.LureNotice && d[l.node] < bd) { bd = d[l.node]; best = l.node; }
            return best;
        }

        /// <summary>追跡者が囮罠のある点に居るなら足止めされ、 囮罠は使い切る。</summary>
        private bool StepOnLure()
        {
            for (int i = 0; i < Lures.Count; i++)
            {
                if (Lures[i].node != FoeNode) continue;
                Lures.RemoveAt(i);
                FoeFreezeTurns = FreeMapParams.LureHoldTurns;
                if (FoeMode == FoeMode.Alert) FoeMode = FoeMode.Patrol;
                _alertLure = -1;
                return true;
            }
            return false;
        }

        /// <summary>追跡者の次の 1 歩。 予告 (<see cref="PeekFoeStep"/>) と実際の移動が同じ関数を通る。
        /// 状態は書き換えない (mode / patrol は参照渡しの控えだけが変わる)。 実際の移動は呼び出し側が書き戻す。</summary>
        private int ComputeFoeStep(ref FoeMode mode, ref int patrol)
        {
            if (mode == FoeMode.Chase)
                return LastSeen >= 0 ? FreeMapLayout.StepToward(_adj, FoeNode, LastSeen) : FoeNode;
            if (mode == FoeMode.Alert)
            {
                if (_alertLure >= 0 && FoeNode != _alertLure)
                    return FreeMapLayout.StepToward(_adj, FoeNode, _alertLure);
                // 痕跡をたどる: いまの点より新しい痕跡のある隣へ
                int best = -1, bt = _trace[FoeNode];
                foreach (int v in _adj[FoeNode])
                    if (HasTrace(v) && _trace[v] > bt) { bt = _trace[v]; best = v; }
                if (best >= 0) return best;
                mode = FoeMode.Patrol;
            }
            var way = Layout.waypoints;
            int t = way.Count > 0 ? way[patrol % way.Count] : Layout.home;
            if (FoeNode == t)
            {
                patrol++;
                return FreeMapLayout.StepToward(_adj, FoeNode, way.Count > 0 ? way[patrol % way.Count] : Layout.home);
            }
            return FreeMapLayout.StepToward(_adj, FoeNode, t);
        }

        // =====================================================================
        //  前線と天候
        // =====================================================================

        private FreeMapEvent AdvanceFront()
        {
            if (Weather == WeatherKind.None || Front >= FreeMapParams.Width) return FreeMapEvent.None;
            Front = double.IsNegativeInfinity(Front) ? FreeMapParams.FrontStep : Front + FreeMapParams.FrontStep;
            var ev = FreeMapEvent.FrontAdvanced;
            int baseIdx = Floor * 100000 + Turn * 10;
            if (Weather == WeatherKind.Crumble && CrumbleOne(baseIdx)) ev |= FreeMapEvent.MapChanged;
            if (Weather == WeatherKind.Sink && SinkOne(baseIdx)) ev |= FreeMapEvent.MapChanged;
            return ev;
        }

        private bool KeepsGoal() => HopsFrom(Me)[Layout.goal] != int.MaxValue;

        private void Unlink(int a, int b) { _adj[a].Remove(b); _adj[b].Remove(a); }

        private bool CrumbleOne(int baseIdx)
        {
            var cand = new List<(int a, int b)>();
            foreach (var (a, b) in Roads()) if (InFront(a) && InFront(b) && a != Me && b != Me) cand.Add((a, b));
            for (int tries = 0; tries < 20 && cand.Count > 0; tries++)
            {
                int pick = GameLoop.GameRng.Range(0, cand.Count, WeatherInfo.RngKey, baseIdx + tries);
                var (a, b) = cand[pick]; cand.RemoveAt(pick);
                Unlink(a, b);
                if (KeepsGoal() && _adj[a].Count > 0 && _adj[b].Count > 0) return true;
                _adj[a].Add(b); _adj[b].Add(a);
            }
            return false;
        }

        private bool SinkOne(int baseIdx)
        {
            var cand = new List<int>();
            for (int i = 0; i < _adj.Length; i++)
                if (InFront(i) && !Done[i] && !_sunk[i] && i != Me && i != FoeNode && i != Layout.goal && i != Layout.start)
                    cand.Add(i);
            for (int tries = 0; tries < 20 && cand.Count > 0; tries++)
            {
                int pick = GameLoop.GameRng.Range(0, cand.Count, WeatherInfo.RngKey, baseIdx + tries);
                int i = cand[pick]; cand.RemoveAt(pick);
                var saved = new List<int>(_adj[i]);
                foreach (int v in saved) Unlink(i, v);
                if (KeepsGoal()) { _sunk[i] = true; return true; }
                foreach (int v in saved) { _adj[i].Add(v); _adj[v].Add(i); }
            }
            return false;
        }
    }
}
