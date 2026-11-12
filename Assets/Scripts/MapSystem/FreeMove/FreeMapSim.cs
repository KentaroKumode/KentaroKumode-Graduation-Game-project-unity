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
        /// <summary>追跡者に捕まった。 戦闘の決着までターンは進まない。</summary>
        Caught = 1 << 4,
        /// <summary>使い魔に接触した。 戦闘の決着までターンは進まない。</summary>
        FamiliarContact = 1 << 5,
        /// <summary>使い魔が放たれた。</summary>
        FamiliarLaunched = 1 << 6,
        /// <summary>見えている使い魔が消えた (目標地点でプレイヤーを見つけられなかった)。</summary>
        FamiliarVanished = 1 << 7,
        /// <summary>追跡者が囮罠を踏んで足止めされた。</summary>
        LureTriggered = 1 << 8,
        /// <summary>ターンが 1 つ以上進んだ。</summary>
        TurnPassed = 1 << 9,
        /// <summary>前線が進んだ。</summary>
        FrontAdvanced = 1 << 10,
        /// <summary>前線の天候で道が崩れた / 点が沈んだ (地図の形が変わった)。</summary>
        MapChanged = 1 << 11,
        /// <summary>物資が尽き、 HP も尽きた。</summary>
        Died = 1 << 12,
        /// <summary>〈煙玉〉が発動した (補足される代わりに、 追跡者は警戒へ落ちた)。 Detected は立たない。</summary>
        SmokeUsed = 1 << 13,
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
    /// 航行マップの動く中身 (ターン制・2026-10-18)。 参照実装は docs/design/map-grid-prototype.html。
    ///
    /// <para><b>移動は道でつながった隣の点へ 1 歩</b>。 自分が 1 歩動く (または待つ・偵察する) ごとに 1 ターンが進み、
    /// 追跡者が 1 度動く。 戦闘・イベント・照明・囮罠はターンを使わない。 物資は距離によらず 1 歩ごとに一定。
    /// イベントの「近道+N」の残り (<see cref="FreeMovesLeft"/>) がある間の 1 歩だけは、 物資もターンも使わない。</para>
    ///
    /// <para>乱数は前線の天候 (道が崩れる・点が沈む) の選択だけで、
    /// <c>GameRng.Range(.., "map.free.weather", 層 × 100000 + ターン × 10 + 試行)</c> ── 同じ操作なら同じ結果になる。</para>
    ///
    /// <para><b>追跡者は縄張りの番人 (2026-11-12)</b>: 既定では本体も使い魔も縄張り (<see cref="FreeMapLayout.territory"/>) の外へ出ず、
    /// 縄張りの外に居るプレイヤーには気づかない。 縄張りの中には見えている宝 (<see cref="TreasureNode"/>) がある。
    /// 旧い作り (層のどこまでも追う) は <see cref="ConfigureTerritory"/> の leashed = false (測定の口)。</para>
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
        /// <summary>照明が点いている残りターン。</summary>
        public int FlareTurnsLeft { get; private set; }
        public bool FlareOn => FlareTurnsLeft > 0;

        // ── 見えているもの ──
        /// <summary>マスの種別が判明している点。 点と道の配置は層全体が見える。</summary>
        public readonly bool[] Known;
        /// <summary>使い切った (起動済みの) 点。 沈める点の候補から外す。 MapManager が写す。</summary>
        public readonly bool[] Done;
        /// <summary>**完全公開** (偵察・照明で見つけた) 点 (2026-11-05)。 戦闘名まで分かる。
        /// 何もしない時の視界 (1 歩) で見えただけの点は種別だけで、 ここには入らない。 一度立ったら戻さない。</summary>
        public readonly bool[] Scouted;
        /// <summary>[計装] いま財布へ HP を払わせている理由 (物資切れの歩行 / 雹)。 財布の側が記録に使う (2026-11-05)。
        /// このクラスは本体 (GameLoop) を参照しない ── 単体の検算 (Tools/freemap_check) で動かすため。</summary>
        public string HpPayReason { get; private set; } = "";

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
        /// <summary>今の追跡が続いているターン数 (<see cref="FreeMapParams.ChaseGiveUp"/> で諦める)。</summary>
        private int _chaseTurns;
        /// <summary>諦めた後に補足しない残りターン。</summary>
        private int _chaseRest;
        private int _patrol;
        private int _sinceSummon = 99;
        /// <summary>足止めの残りターン (囮罠・逃げた直後)。</summary>
        public int FoeFreezeTurns { get; private set; }
        private int _alertLure = -1;
        public readonly List<Familiar> Familiars = new List<Familiar>();

        // ── 縄張りと宝 (2026-11-12) ──
        /// <summary>true = 追跡者 (本体と使い魔) は<b>縄張りの外へ出ない</b> (既定)。 縄張りの外に居るプレイヤーには気づかず、
        /// 追跡の途中でもその先へは進まない (縁で止まり、 補足できないまま見失う)。
        /// false = 旧い作り (層のどこまでも追う)。 本体 (MapManager) が層を作った時に <see cref="ConfigureTerritory"/> で決める。</summary>
        public bool Leashed { get; private set; } = true;
        /// <summary>縄張りの宝の点 (-1 = 無い)。 層に入った時から地図に印が見える (点の種類は見えないまま)。
        /// 置き場所は <see cref="TreasurePointOf"/> (乱数を使わない)。</summary>
        public int TreasureNode { get; private set; } = -1;
        /// <summary>縄張りの宝を取ったか (1 層 1 回)。</summary>
        public bool TreasureTaken { get; private set; }
        /// <summary>縄張りの宝がまだ取れるか。</summary>
        public bool TreasureAvailable => TreasureNode >= 0 && !TreasureTaken;

        // ── 補給線 (〈兵站〉r10・2026-11-09) ──
        private readonly HashSet<int> _relays = new HashSet<int>();
        /// <summary>この層で一度着いた前哨基地・店 (〈補給線〉で跳べる先)。 本体 (MapManager) が着いた時に足す。</summary>
        public IReadOnlyCollection<int> RelayPoints => _relays;

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
        /// <summary>前線がもう層に届いているか (届いていなければ <see cref="ShiftFront"/> の遠ざけるは何も起きない)。</summary>
        public bool FrontArrived => !double.IsNegativeInfinity(Front);

        // ── 航行アイテムの係数 (2026-11-11・nav-items.md) ──
        /// <summary>いま掛かっている係数。 本体が操作のたびに <see cref="SetPerks"/> で差し替える (層の途中で品を得た時も効くように)。</summary>
        public FreeMapPerks Perks { get; private set; } = FreeMapPerks.None;
        /// <summary>この層の〈煙玉〉の残り回数。</summary>
        public int SmokeLeft { get; private set; }
        /// <summary>この層の〈前哨の旗〉の残り回数。</summary>
        public int BeaconLeft { get; private set; }
        /// <summary>〈前哨の旗〉の拠点 (-1 = 置いていない)。 層を出ると Sim ごと消える。</summary>
        public int Beacon { get; private set; } = -1;
        private bool _smokeGranted, _beaconGranted;
        /// <summary>〈渡り鳥の羅盤〉: 終点へ近づいた歩の数 (偶数番目の歩が無料)。</summary>
        private int _goalwardSteps;

        // ── 近道 (イベント「近道+N」・2026-11-12 のユーザーの決定) ──
        /// <summary>ただで動ける残りの回数。 1 以上の間、 隣への 1 歩 (<see cref="MoveTo"/>) は物資を払わず、 ターンも進めない
        /// (追跡者・使い魔・前線・照明と囮罠の残りが動かない)。 1 歩ごとに 1 減る。 待つ・偵察・〈補給線〉では減らない。
        /// 層を出ると Sim ごと消える (持ち越さない)。</summary>
        public int FreeMovesLeft { get; private set; }

        /// <summary>ただで動ける回数を足す (重ねたら足し合わせ)。</summary>
        public void GrantFreeMoves(int n)
        {
            if (n <= 0) return;
            FreeMovesLeft += n;
            Version++;
        }

        /// <summary>係数を差し替える。 回数のある品 (煙玉・前哨の旗) は、 層の中で初めて持った時に 1 度だけ回数を入れる。</summary>
        public void SetPerks(FreeMapPerks p)
        {
            p = p ?? FreeMapPerks.None;
            if (p.smokeCharges > 0 && !_smokeGranted) { SmokeLeft = p.smokeCharges; _smokeGranted = true; }
            if (p.beaconCharges > 0 && !_beaconGranted) { BeaconLeft = p.beaconCharges; _beaconGranted = true; }
            if (!ReferenceEquals(p, Perks)) Version++;
            Perks = p;
        }

        public FreeMapSim(FreeMapLayout layout, IFreeMapWallet wallet, bool withFoe,
                          FoeProfile profile = null, WeatherKind? weather = null, FreeMapPerks perks = null)
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
            Scouted = new bool[n];
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
            SetPerks(perks);
            // 〈地図売りの走り書き〉: 層に入った瞬間だけ、 始点から直線距離で半径の内の点の種類を公開する (戦闘名・敵は出さない)
            if (Perks.sketchRadius > 0)
            {
                var s = layout.pos[layout.start]; double r2 = Perks.sketchRadius * Perks.sketchRadius;
                for (int i = 0; i < n; i++)
                {
                    double dx = layout.pos[i].x - s.x, dy = layout.pos[i].y - s.y;
                    if (dx * dx + dy * dy <= r2) Known[i] = true;
                }
            }
            Reveal(Me, Perks.vision, false);
        }

        /// <summary>縄張りの作りを決める (層を作った直後・最初の操作の前に 1 回)。 <paramref name="leashed"/> = 追跡者が縄張りの外へ出ないか、
        /// <paramref name="treasure"/> = 縄張りの宝を置くか (追跡者が居る層だけ)。 乱数は引かない。</summary>
        public void ConfigureTerritory(bool leashed, bool treasure)
        {
            Leashed = leashed;
            TreasureNode = treasure && HasFoe ? TreasurePointOf(Layout) : -1;
            Version++;
        }

        /// <summary>縄張りの宝の点: 縄張りの中心 (追跡者の最初の居場所) を除いた縄張りの点のうち、 中心に直線で一番近い点 (同点は添字の小さい方)。
        /// 中心そのものは追跡者の居場所で、 動かない追跡者 (召喚者) だと取りに行く手が戦うしか無くなるので 1 つずらす。
        /// 性質で置き場所を変えないのは、 置き場所から性質が読めないように。 乱数は使わない (地図の生成の消費を変えない)。</summary>
        public static int TreasurePointOf(FreeMapLayout L)
        {
            int best = -1; double bd = double.MaxValue;
            foreach (int i in L.territory)
            {
                if (i == L.home || i == L.start || i == L.goal) continue;
                double d = L.Dist(i, L.home);
                if (d < bd || (d == bd && i < best)) { bd = d; best = i; }
            }
            return best;
        }

        /// <summary>点 i が追跡者の縄張りか。</summary>
        public bool InTerritory(int i) => i >= 0 && Layout.territory.Contains(i);

        /// <summary>今いる点の縄張りの宝を取る (1 層 1 回)。 取れたら true。 中身を渡すのは本体 (GameManager)。</summary>
        public bool TakeTreasure()
        {
            if (!TreasureAvailable || Me != TreasureNode || Dead || InFight) return false;
            TreasureTaken = true;
            Version++;
            return true;
        }

        // =====================================================================
        //  中断セーブ (issue #84)
        // =====================================================================

        /// <summary>動く中身の写し (JsonUtility で書ける形)。 形 (<see cref="Layout"/>)・追跡者の性質 (<see cref="Profile"/>)・
        /// 航行アイテムの係数 (<see cref="Perks"/>) は持たない ── 形は別に写し、 性質と係数は作り直す時に同じものを渡す。
        /// 実数 (前線の x) は丸めずに戻すため、 ビット列 (long) で持つ。</summary>
        [Serializable]
        public sealed class SavedState
        {
            public int count;
            public int[] adjCounts = new int[0];
            public int[] adjItems = new int[0];
            public bool[] sunk = new bool[0], known = new bool[0], done = new bool[0], scouted = new bool[0];
            public int[] trace = new int[0];
            public int me, prevNode, lastArrived, turn, version, flareTurnsLeft;
            public bool dead;
            public string hpPayReason = "";
            public bool hasFoe, foeAlive;
            public int foeNode, foeMode, lastSeen, lose, chaseTurns, chaseRest, patrol, sinceSummon, foeFreezeTurns, alertLure;
            public int[] famPos = new int[0], famTarget = new int[0];
            public bool[] famHound = new bool[0];
            public int[] relays = new int[0];
            public int[] lureNode = new int[0], lureTurns = new int[0];
            public int contact, pendingArrival;
            public int weather;
            public bool frontSet;
            public long frontBits;
            public int smokeLeft, beaconLeft, beacon, goalwardSteps;
            public bool smokeGranted, beaconGranted;
            /// <summary>縄張りの宝を取ったか (2026-11-12)。 宝の有無と追跡者が縄張りを出るかは写さない ── 作り直す時に <see cref="ConfigureTerritory"/> で同じものを決める。</summary>
            public bool treasureTaken;
            /// <summary>近道 (時限バフ「近道」) の残りの回数 (2026-11-13)。 古いセーブには無い欄なので 0 で戻る。</summary>
            public int freeMovesLeft;
        }

        /// <summary>いまの中身を写す。</summary>
        public SavedState CaptureState()
        {
            int n = _adj.Length;
            var s = new SavedState { count = n, adjCounts = new int[n] };
            var items = new List<int>();
            for (int i = 0; i < n; i++) { s.adjCounts[i] = _adj[i].Count; items.AddRange(_adj[i]); }   // 並びも写す (経路の同点は並びで決まる)
            s.adjItems = items.ToArray();
            s.sunk = (bool[])_sunk.Clone(); s.known = (bool[])Known.Clone(); s.done = (bool[])Done.Clone(); s.scouted = (bool[])Scouted.Clone();
            s.trace = (int[])_trace.Clone();
            s.me = Me; s.prevNode = PrevNode; s.lastArrived = LastArrived; s.turn = Turn; s.version = Version; s.flareTurnsLeft = FlareTurnsLeft;
            s.dead = Dead; s.hpPayReason = HpPayReason ?? "";
            s.hasFoe = HasFoe; s.foeAlive = FoeAlive; s.foeNode = FoeNode; s.foeMode = (int)FoeMode; s.lastSeen = LastSeen;
            s.lose = _lose; s.chaseTurns = _chaseTurns; s.chaseRest = _chaseRest; s.patrol = _patrol; s.sinceSummon = _sinceSummon;
            s.foeFreezeTurns = FoeFreezeTurns; s.alertLure = _alertLure;
            int fc = Familiars.Count;
            s.famPos = new int[fc]; s.famTarget = new int[fc]; s.famHound = new bool[fc];
            for (int i = 0; i < fc; i++) { s.famPos[i] = Familiars[i].pos; s.famTarget[i] = Familiars[i].target; s.famHound[i] = Familiars[i].hound; }
            var relays = new List<int>(_relays); relays.Sort(); s.relays = relays.ToArray();
            int lc = Lures.Count;
            s.lureNode = new int[lc]; s.lureTurns = new int[lc];
            for (int i = 0; i < lc; i++) { s.lureNode[i] = Lures[i].node; s.lureTurns[i] = Lures[i].turnsLeft; }   // 並びも写す (一番近い囮罠の同点は並びで決まる)
            s.contact = (int)Contact; s.pendingArrival = PendingArrival;
            s.weather = (int)Weather;
            s.frontSet = !double.IsNegativeInfinity(Front);
            s.frontBits = s.frontSet ? BitConverter.DoubleToInt64Bits(Front) : 0L;
            s.smokeLeft = SmokeLeft; s.beaconLeft = BeaconLeft; s.beacon = Beacon; s.goalwardSteps = _goalwardSteps;
            s.smokeGranted = _smokeGranted; s.beaconGranted = _beaconGranted;
            s.treasureTaken = TreasureTaken;
            s.freeMovesLeft = FreeMovesLeft;
            return s;
        }

        /// <summary>写しを戻す。 同じ形 (<see cref="Layout"/>) と同じ性質で作った中身に対して呼ぶ。 点の数が合わなければ例外。
        /// <paramref name="perks"/> は回数を入れ直さずにそのまま掛ける (回数は写しの残りを使う)。</summary>
        public void LoadState(SavedState s, FreeMapPerks perks)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            int n = _adj.Length;
            if (s.count != n || s.adjCounts == null || s.adjCounts.Length != n
                || s.sunk?.Length != n || s.known?.Length != n || s.done?.Length != n || s.scouted?.Length != n || s.trace?.Length != n)
                throw new InvalidOperationException($"航行の写しの点の数が形と合わない (写し {s.count} / 形 {n})");
            int k = 0;
            for (int i = 0; i < n; i++)
            {
                _adj[i].Clear();
                for (int c = 0; c < s.adjCounts[i]; c++) _adj[i].Add(s.adjItems[k++]);
            }
            Array.Copy(s.sunk, _sunk, n); Array.Copy(s.known, Known, n); Array.Copy(s.done, Done, n); Array.Copy(s.scouted, Scouted, n);
            Array.Copy(s.trace, _trace, n);
            Me = s.me; PrevNode = s.prevNode; LastArrived = s.lastArrived; Turn = s.turn; Version = s.version + 1; FlareTurnsLeft = s.flareTurnsLeft;
            Dead = s.dead; HpPayReason = s.hpPayReason ?? "";
            HasFoe = s.hasFoe; FoeAlive = s.foeAlive; FoeNode = s.foeNode; FoeMode = (FoeMode)s.foeMode; LastSeen = s.lastSeen;
            _lose = s.lose; _chaseTurns = s.chaseTurns; _chaseRest = s.chaseRest; _patrol = s.patrol; _sinceSummon = s.sinceSummon;
            FoeFreezeTurns = s.foeFreezeTurns; _alertLure = s.alertLure;
            Familiars.Clear();
            for (int i = 0; i < (s.famPos?.Length ?? 0); i++)
                Familiars.Add(new Familiar { pos = s.famPos[i], target = s.famTarget[i], hound = s.famHound[i] });
            _relays.Clear();
            if (s.relays != null) foreach (int r in s.relays) _relays.Add(r);
            Lures.Clear();
            for (int i = 0; i < (s.lureNode?.Length ?? 0); i++) Lures.Add(new Lure { node = s.lureNode[i], turnsLeft = s.lureTurns[i] });
            Contact = (ContactKind)s.contact; PendingArrival = s.pendingArrival;
            Weather = (WeatherKind)s.weather;
            Front = s.frontSet ? BitConverter.Int64BitsToDouble(s.frontBits) : double.NegativeInfinity;
            SmokeLeft = s.smokeLeft; BeaconLeft = s.beaconLeft; Beacon = s.beacon; _goalwardSteps = s.goalwardSteps;
            _smokeGranted = s.smokeGranted; _beaconGranted = s.beaconGranted;
            TreasureTaken = s.treasureTaken;
            FreeMovesLeft = System.Math.Max(0, s.freeMovesLeft);
            Perks = perks ?? FreeMapPerks.None;
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

        /// <summary>from から to へ向かう次の 1 歩 (最短の道・崩れた道は通らない)。 着いている / 行けないなら from。</summary>
        public int StepToward(int from, int to) => FreeMapLayout.StepToward(_adj, from, to);

        /// <summary>from から to へ、 <b>踏む縄張りの点の数が一番少なく</b>、 その中で一番短い道の最初の 1 歩 (2026-11-12・問い合わせだけ)。
        /// 踏む数には to を含み from を含まない (縄張りの中から出る時は、 出るまでに踏む点が一番少ない道)。
        /// <paramref name="hops"/> = その道の歩数・<paramref name="territorySteps"/> = その道で踏む縄張りの点の数。 行けなければ from を返し hops = int.MaxValue。
        /// 同点は隣接表の並びが先の点 (<see cref="StepToward"/> と同じ決め方)。 状態は変えず、 乱数も使わない。
        /// BOT の航行 (縄張りを横切らない道) と検算 (freemap_check) が使う。 遊ぶ人の規則には関わらない。</summary>
        public int StepAvoidingTerritory(int from, int to, out int hops, out int territorySteps)
        {
            hops = int.MaxValue; territorySteps = int.MaxValue;
            if (from == to) { hops = 0; territorySteps = 0; return from; }
            int n = _adj.Length;
            // 費用 = 踏む縄張りの点の数 × 1000 + 歩数 (点は 50 程度なので歩数は 1000 に届かない)。 to から逆に広げる
            var cost = new int[n];
            var done = new bool[n];
            for (int i = 0; i < n; i++) cost[i] = int.MaxValue;
            if (_sunk[to]) return from;
            cost[to] = (InTerritory(to) ? 1000 : 0);
            while (true)
            {
                int u = -1, cu = int.MaxValue;
                for (int i = 0; i < n; i++) if (!done[i] && cost[i] < cu) { cu = cost[i]; u = i; }
                if (u < 0) break;
                done[u] = true;
                if (u == from) continue;   // from から先へは広げない (from は踏む数に入れない)
                foreach (int v in _adj[u])
                {
                    if (done[v] || _sunk[v]) continue;
                    int c = cu + 1 + (v != from && InTerritory(v) ? 1000 : 0);
                    if (c < cost[v]) cost[v] = c;
                }
            }
            int best = from, bc = int.MaxValue;
            foreach (int v in _adj[from]) if (!_sunk[v] && cost[v] < bc) { bc = cost[v]; best = v; }
            if (best == from) return from;
            hops = bc % 1000 + 1; territorySteps = bc / 1000;
            return best;
        }

        /// <summary>いま見える範囲 (道で何歩先まで)。 照明中は <see cref="FreeMapParams.FlareRange"/>。</summary>
        public int VisionRange => FlareOn ? Math.Max(FreeMapParams.FlareRange, Perks.vision) : Perks.vision;

        /// <summary>点 i が〈前哨の旗〉の拠点から見えている範囲か (拠点が無ければ false)。</summary>
        public bool InBeacon(int i, int[] hopsFromBeacon = null)
            => Beacon >= 0 && i >= 0 && (hopsFromBeacon ?? HopsFrom(Beacon))[i] <= Perks.beaconRange;

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

        private double TraceLife(int i)
        {
            double life = FreeMapParams.TraceLife * Perks.traceLifeMul;
            if (WeatherAt(i, WeatherKind.Sand)) life /= 2;
            return life;
        }

        /// <summary>点 j へ 1 歩動く物資。 削る砂の前線の内側は 50% で +1 (2026-11-09: 旧「1.5 倍」をデノミ後の 1 で表す)。
        /// 抽選は (ターン, 行き先) で決まる専用の鍵なので、 同じターンに何度聞いても同じ答え (予告・BOT の先読みと実際の支払いが一致する)。</summary>
        public int SupplyFor(int j)
        {
            if (FreeMovesLeft > 0) return 0;   // 近道の残りがある間はただ
            int c = FreeMapParams.MoveSupply;
            if (!Perks.ignoreSand && (WeatherAt(j, WeatherKind.Sand) || WeatherAt(Me, WeatherKind.Sand))
                && GameLoop.GameRng.Range(0, 1000, "map.free.sand", Turn * 1000 + j) < (int)(FreeMapParams.SandExtraChance * 1000))
                c += 1;
            // 〈渡り鳥の羅盤〉: 終点へ近づく歩は 2 歩に 1 回 1 安い (×0.5 を 1 歩 = 1 の物資で表す・偶数番目の歩が安い)
            if (Perks.goalwardHalf && c > 0 && (_goalwardSteps & 1) == 1 && IsGoalward(j)) c -= 1;
            return c;
        }

        /// <summary>点 j へ動くと終点までの道の歩数が減るか。</summary>
        public bool IsGoalward(int j)
        {
            var g = HopsFrom(Layout.goal);
            return g[j] != int.MaxValue && g[j] < g[Me];
        }

        /// <summary>点 j へ 1 歩動くのに進むターン (雪の前線の内側は 2・近道の残りがある間は 0)。</summary>
        public int TurnsFor(int j) => FreeMovesLeft > 0 ? 0 : WeatherAt(j, WeatherKind.Snow) || WeatherAt(Me, WeatherKind.Snow) ? 2 : 1;

        /// <summary>点 j へ 1 歩動いた時の HP の損 (雹の前線の内側)。 近道の残りがある間の 1 歩は 0 (2026-11-13・作者の決定「ただで動いた時は払わない」)。</summary>
        public int HpLossFor(int j) => FreeMovesLeft > 0 ? 0 : WeatherAt(j, WeatherKind.Hail) ? 1 : 0;

        /// <summary>追跡者がいま補足に使う歩数 (性質と照明で変わる)。 表示 (気配の輪) もこれを読む。</summary>
        public int DetectHops
        {
            get
            {
                int d = Profile != null && Profile.detectRange > 0 ? Profile.detectRange : FreeMapParams.DetectRange;
                return FlareOn ? Math.Max(d, FreeMapParams.FlareRange) : d;
            }
        }

        /// <summary>追跡者がいま見えているか (視界の内側)。</summary>
        public bool FoeVisible => FoeAlive && (HopsFrom(Me)[FoeNode] <= VisionRange || InBeacon(FoeNode));

        /// <summary>いま見えている使い魔 (〈前哨の旗〉の拠点の範囲も含む)。</summary>
        public IEnumerable<Familiar> VisibleFamiliars()
        {
            var d = HopsFrom(Me); var b = Beacon >= 0 ? HopsFrom(Beacon) : null;
            foreach (var f in Familiars) if (d[f.pos] <= VisionRange || (b != null && b[f.pos] <= Perks.beaconRange)) yield return f;
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
                      : d <= FreeMapParams.StoneFar + Perks.stoneFarBonus ? 1 : 0;
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
            // 縄張りを出ない追跡者: 縄張りの外に居れば誰も来ない。 中に居れば、 縄張りの中の道だけで数える
            if (Leashed && !InTerritory(Me)) return best;
            var d = Leashed ? FreeMapLayout.Hops(TerritoryGraph(), Me) : HopsFrom(Me);
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

        /// <summary>隣の点 j へ 1 歩。 物資を払い、 痕跡を残し、 追跡者が動く。
        /// <para>近道の残り (<see cref="FreeMovesLeft"/>) がある間は<b>ただの 1 歩</b>: 物資を払わず、 ターンを進めず (追跡者・使い魔・前線・
        /// 照明と囮罠の残りが動かない)、 雹の HP も払わず (2026-11-13)、 残りを 1 減らす。 痕跡・着いた点の起動は通常の 1 歩と同じ。 補足の判定は
        /// 追跡者の手番 (= 次にターンが進んだ時) にしか無いので、 ここでは鳴らない。 追跡者・使い魔の居る点へ自分から踏み込んだ時だけ、 その場で接触する。</para></summary>
        public FreeMapEvent MoveTo(int j)
        {
            if (!CanMoveTo(j)) return FreeMapEvent.None;
            bool free = FreeMovesLeft > 0;
            int cost = SupplyFor(j), turns = TurnsFor(j), hail = HpLossFor(j);
            // 〈渡り鳥の羅盤〉の「2 歩に 1 回安い」は払う歩だけを数える (ただの歩で順番をずらさない)
            if (!free && Perks.goalwardHalf && IsGoalward(j)) _goalwardSteps++;
            var ev = FreeMapEvent.None;
            if (Pay(cost)) ev |= FreeMapEvent.Died;
            HpPayReason = "雹";
            if (hail > 0 && _wallet.PayHp(hail)) ev |= FreeMapEvent.Died;
            PrevNode = Me;
            Me = j;
            Version++;
            _trace[j] = Turn + turns;
            Known[j] = true;
            LastArrived = j;
            ev |= FreeMapEvent.Arrived;
            if ((ev & FreeMapEvent.Died) != 0) { Dead = true; return ev; }
            if (free)
            {
                FreeMovesLeft--;
                ev |= ContactWithoutTurn(j);
                RevealView();
            }
            else ev |= PassTurns(turns);
            if ((ev & (FreeMapEvent.Caught | FreeMapEvent.FamiliarContact)) != 0) PendingArrival = j;
            return ev;
        }

        /// <summary>ターンを進めずに点 j へ着いた時の接触: 追跡者の本体の居る点なら捕まる (補足もその場で入る)、
        /// 使い魔・猟犬の居る点なら接触する。 補足の範囲に入っただけでは何も起きない (判定は次の追跡者の手番)。
        /// 〈煙玉〉は補足を外す品で、 踏み込んだ接触には効かない。</summary>
        private FreeMapEvent ContactWithoutTurn(int j)
        {
            var ev = FreeMapEvent.None;
            if (FoeAlive && FoeNode == j)
            {
                if (FoeMode != FoeMode.Chase) { ev |= FreeMapEvent.Detected; _chaseTurns = 0; }
                FoeMode = FoeMode.Chase; LastSeen = j; _lose = 0; _alertLure = -1;
                Contact = ContactKind.Body;
                return ev | FreeMapEvent.Caught;
            }
            foreach (var f in Familiars)
                if (f.pos == j) { Contact = ContactKind.Familiar; ev |= FreeMapEvent.FamiliarContact; break; }
            return ev;
        }

        /// <summary>〈補給線〉の中継点を足す (この層で一度着いた前哨基地・店)。</summary>
        public void AddRelayPoint(int i) { if (i >= 0 && i < _adj.Length && _relays.Add(i)) Version++; }

        /// <summary>〈補給線〉で点 j へ跳べるか (解禁の判定は呼ぶ側 = 整備パネル〈兵站〉r10)。</summary>
        public bool CanRelayTo(int j)
            => !Dead && !InFight && j >= 0 && j < _adj.Length && j != Me && !_sunk[j] && _relays.Contains(j);

        /// <summary>〈補給線〉: 一度着いた前哨基地・店へ跳ぶ。 物資 <see cref="FreeMapParams.RelaySupply"/>・<b>ターンは使わない</b> (追跡者は動かない)。
        /// <para><b>補足は解けない</b> ── 追いかけている追跡者は跳んだ先を知っていて、 そこへ向かう。 距離は稼げるが撒けない (2026-11-09)。</para></summary>
        public FreeMapEvent RelayTo(int j)
        {
            if (!CanRelayTo(j)) return FreeMapEvent.None;
            var ev = FreeMapEvent.None;
            if (Pay(FreeMapParams.RelaySupply)) ev |= FreeMapEvent.Died;
            PrevNode = Me;
            Me = j;
            Version++;
            _trace[j] = Turn;
            Known[j] = true;
            LastArrived = j;
            // 縄張りの外へ跳んだ時は、 縄張りを出ない追跡者には追う先が無い (最後に見た点のまま・補足できずに見失う)
            if (FoeAlive && FoeMode == FoeMode.Chase && (!Leashed || InTerritory(j))) LastSeen = j;
            ev |= FreeMapEvent.Arrived;
            if ((ev & FreeMapEvent.Died) != 0) { Dead = true; return ev; }
            RevealView();
            return ev;
        }

        /// <summary>待つ (1 ターン)。</summary>
        public FreeMapEvent Wait() => Dead || InFight ? FreeMapEvent.None : PassTurns(1);

        /// <summary>偵察 (1 ターン): 道で <see cref="FreeMapParams.ReconRange"/> 歩先まで見える。</summary>
        public FreeMapEvent Recon()
        {
            if (Dead || InFight) return FreeMapEvent.None;
            Reveal(Me, FreeMapParams.ReconRange, true);
            return PassTurns(1);
        }

        /// <summary>照明 (ターンは使わない): 物資を払い、 <see cref="FreeMapParams.FlareTurns"/> ターンのあいだ
        /// 道で <see cref="FreeMapParams.FlareRange"/> 歩先まで見える。 追跡者の補足の歩数も同じだけ伸びる。</summary>
        public FreeMapEvent UseFlare()
        {
            if (Dead || InFight) return FreeMapEvent.None;
            var ev = Pay(FreeMapParams.FlareSupply) ? FreeMapEvent.Died : FreeMapEvent.None;
            if ((ev & FreeMapEvent.Died) != 0) { Dead = true; return ev; }
            FlareTurnsLeft = FreeMapParams.FlareTurns;
            Reveal(Me, FreeMapParams.FlareRange, true);
            Version++;
            return ev;
        }

        /// <summary>通常の戦闘から逃げた: 直前の点へ戻る (ターンは使わない)。</summary>
        public void FleeToPrevious()
        {
            Version++;
            if (Me != PrevNode && _adj[Me].Contains(PrevNode)) Me = PrevNode;
            PendingArrival = -1;
            RevealView();
        }

        /// <summary>多めに回復する休憩など、 ターンだけを進める。</summary>
        public FreeMapEvent PassTurnsExternal(int n) => Dead || InFight ? FreeMapEvent.None : PassTurns(n);

        // =====================================================================
        //  イベントの航行の効果 (2026-11-12・docs/GAME.md §17)
        //  どれもターンを使わない。 本体はイベントを閉じて地図へ戻った時に 1 つずつ呼ぶ (GameManager.ApplyEventNav)。
        //  イベント専用の分岐を本体の他の処理に持たせないため、 動詞は「地図の上で何が起きるか」だけで書く。
        // =====================================================================

        /// <summary>この層で種類が分かっている点の割合 (0..1)。 イベントの出やすさ (地図の系) が読む。</summary>
        public double KnownFraction
        {
            get
            {
                int k = 0;
                for (int i = 0; i < Known.Length; i++) if (Known[i]) k++;
                return Known.Length > 0 ? (double)k / Known.Length : 1.0;
            }
        }

        /// <summary>今いる点から道で range 歩以内の点を明かす。 scouting = 戦闘名まで (偵察と同じ完全公開)。 新しく種類が分かった点の数を返す。</summary>
        public int RevealAround(int range, bool scouting)
        {
            if (range <= 0) return 0;
            var d = HopsFrom(Me);
            int fresh = 0;
            for (int i = 0; i < d.Length; i++)
            {
                if (d[i] > range) continue;
                if (!Known[i]) fresh++;
                Known[i] = true;
                if (scouting) Scouted[i] = true;
            }
            Version++;
            return fresh;
        }

        /// <summary>指定した点を完全公開する (種類と戦闘名)。 沈んだ点は飛ばす。</summary>
        public int RevealPoints(IEnumerable<int> points)
        {
            int fresh = 0;
            if (points == null) return 0;
            foreach (int i in points)
            {
                if (i < 0 || i >= Known.Length || _sunk[i]) continue;
                if (!Known[i]) fresh++;
                Known[i] = true; Scouted[i] = true;
            }
            Version++;
            return fresh;
        }

        /// <summary>補足を外す: 追跡者は見失って巡回へ戻り、 <see cref="FreeMapParams.ChaseRest"/> ターンは補足しない
        /// (追跡の上限で諦めた時と同じ休み)。 放たれた使い魔は消え、 猟犬は本体の元へ戻る。 追跡者が居なければ何もしない。</summary>
        public FreeMapEvent ShakeOff()
        {
            if (!FoeAlive) return FreeMapEvent.None;
            bool was = FoeMode != FoeMode.Patrol || Familiars.Exists(f => !f.hound);
            FoeMode = FoeMode.Patrol; _lose = 0; _chaseTurns = 0; _alertLure = -1;
            _chaseRest = Math.Max(_chaseRest, FreeMapParams.ChaseRest);
            Familiars.RemoveAll(f => !f.hound);
            for (int i = 0; i < Familiars.Count; i++) { var f = Familiars[i]; f.pos = FoeNode; f.target = -1; Familiars[i] = f; }
            Version++;
            return was ? FreeMapEvent.FoeLost : FreeMapEvent.None;
        }

        /// <summary>追跡者の本体を道で steps 歩、 自分から遠ざける (毎歩、 自分からの歩数がいちばん増える隣へ・同点は隣接表の並び)。
        /// これ以上離れられなければそこで止まる。 動かない性質 (召喚者) も動かす ── 本体の性質ではなく、 イベントの出来事として動く。
        /// 猟犬は本体の元へ戻る。 追跡中なら追跡は続く (最後に補足した点へ向かう)。</summary>
        public FreeMapEvent PushFoe(int steps)
        {
            if (!FoeAlive || steps <= 0) return FreeMapEvent.None;
            var d = HopsFrom(Me);
            for (int k = 0; k < steps; k++)
            {
                int best = FoeNode;
                foreach (int v in _adj[FoeNode])
                    if (d[v] != int.MaxValue && d[v] > d[best]) best = v;
                if (best == FoeNode) break;
                FoeNode = best;
            }
            for (int i = 0; i < Familiars.Count; i++)
                if (Familiars[i].hound) { var f = Familiars[i]; f.pos = FoeNode; Familiars[i] = f; }
            Version++;
            return FreeMapEvent.None;
        }

        /// <summary>追跡者の本体を道で steps 歩、 自分へ近づける (代償の効果)。 自分の隣で止まる (この効果では捕まらない)。
        /// 補足するかどうかは次のターンの追跡者の手番が決める。</summary>
        public FreeMapEvent PullFoe(int steps)
        {
            if (!FoeAlive || steps <= 0) return FreeMapEvent.None;
            for (int k = 0; k < steps; k++)
            {
                int nx = StepToward(FoeNode, Me);
                if (nx == FoeNode || nx == Me) break;
                FoeNode = nx;
            }
            Version++;
            return FreeMapEvent.None;
        }

        /// <summary>天候の前線を動かす。 steps &gt; 0 = 近づける (前線が steps 回進む・崩れる地平と沈む湖面はその分だけ働く)、
        /// steps &lt; 0 = 遠ざける (前線を |steps| × <see cref="FreeMapParams.FrontStep"/> だけ左へ戻す・まだ届いていない所まで戻ったら未到達に戻す)。
        /// 天候の無い層・まだ前線が来ていない時に遠ざけるのは何も起きない。</summary>
        public FreeMapEvent ShiftFront(int steps)
        {
            if (Weather == WeatherKind.None || steps == 0) return FreeMapEvent.None;
            var ev = FreeMapEvent.None;
            if (steps > 0)
            {
                for (int k = 0; k < steps; k++) ev |= AdvanceFront();
            }
            else if (!double.IsNegativeInfinity(Front))
            {
                double f = Math.Min(Front, FreeMapParams.Width) + steps * (double)FreeMapParams.FrontStep;
                Front = f < FreeMapParams.FrontStep ? double.NegativeInfinity : f;
                // 戻した時は FrontAdvanced を立てない (「前線が進んだ」の知らせを鳴らさない)
            }
            Version++;
            RevealView();
            return ev;
        }

        /// <summary>囮罠を今いる点に置く (ターンは使わない)。 近くの追跡者は気を引かれて向かい、 踏むと足止めされる。</summary>
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

        /// <summary>追跡者の戦闘から逃げた: 直前の点へ戻り、 追跡者は <see cref="FreeMapParams.FleeFreezeTurns"/> ターン足止めされる。</summary>
        public void ResolveBodyFled()
        {
            Version++;
            Contact = ContactKind.None;
            PendingArrival = -1;
            if (Me != PrevNode && _adj[Me].Contains(PrevNode)) Me = PrevNode;
            FoeFreezeTurns = Math.Max(FoeFreezeTurns, FreeMapParams.FleeFreezeTurns);
            RevealView();
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
                RevealView();
            }
        }

        // =====================================================================
        //  ターン
        // =====================================================================

        private bool Pay(int cost)
        {
            int pay = Math.Min(cost, Math.Max(0, _wallet.Provision));
            if (pay > 0) _wallet.SpendProvision(pay);
            int short_ = cost - pay;
            if (short_ <= 0) return false;
            HpPayReason = "物資切れの歩行";
            return _wallet.PayHp(short_ * FreeMapParams.HpPerSupply);
        }

        /// <summary>道で range 歩以内の点の種類を公開する。 scouting = 完全公開 (戦闘名まで) ── 偵察・照明 (照明中の視界も含む)・〈前哨の旗〉。
        /// 何もしない時の視界 (〈夜目の指輪〉で広げた分も含む) は種類だけ。</summary>
        private void Reveal(int center, int range, bool scouting)
        {
            var d = HopsFrom(center);
            for (int i = 0; i < d.Length; i++)
                if (d[i] <= range) { Known[i] = true; if (scouting) Scouted[i] = true; }
        }

        /// <summary>今の視界 (照明中は完全公開) と、 〈前哨の旗〉の拠点の範囲 (常に完全公開) を公開する。</summary>
        private void RevealView()
        {
            Reveal(Me, VisionRange, FlareOn);
            if (Beacon >= 0) Reveal(Beacon, Perks.beaconRange, true);
        }

        /// <summary>〈前哨の旗〉: 今いる点に偵察拠点を置く (ターンは使わない・層に 1 回)。 その層にいる間、 拠点から
        /// <see cref="FreeMapPerks.beaconRange"/> 歩の点の種類と戦闘名・追跡者・使い魔を常に見せる。 置けなければ false。</summary>
        public bool PlaceBeacon()
        {
            if (Dead || InFight || BeaconLeft <= 0) return false;
            BeaconLeft--;
            Beacon = Me;
            Version++;
            RevealView();
            return true;
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
            RevealView();
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
                int detect = DetectHops;
                if (_chaseRest > 0) _chaseRest--;
                // 縄張りを出ない追跡者は、 縄張りの外に居るプレイヤーには気づかない (追跡中なら、 補足できないまま見失う側へ数える)
                bool sees = _chaseRest == 0 && dMe[FoeNode] <= detect && (!Leashed || InTerritory(Me));
                if (sees && FoeMode != FoeMode.Chase && SmokeLeft > 0)
                {
                    // 〈煙玉〉(層に 1 回・自動): 補足される代わりに、 追跡者は警戒へ落ちて痕跡をたどる (最後の位置 = 発動した地点)。
                    //   すぐ補足し直さないよう、 数ターンは補足しない。 その間にどこへ逃げるかは問われる (痕跡を踏まれれば追いつかれる)
                    SmokeLeft--;
                    FoeMode = FoeMode.Alert; _alertLure = -1; _chaseRest = FreeMapPerks.SmokeRestTurns;
                    ev |= FreeMapEvent.SmokeUsed;
                }
                else if (sees)
                {
                    if (FoeMode != FoeMode.Chase) { ev |= FreeMapEvent.Detected; _chaseTurns = 0; }
                    FoeMode = FoeMode.Chase; LastSeen = Me; _lose = 0;
                }
                else if (FoeMode == FoeMode.Chase && ++_lose > FreeMapParams.QuietLose)
                {
                    FoeMode = FoeMode.Patrol; ev |= FreeMapEvent.FoeLost;
                }
                // 追跡の上限: 追い続けて捕まえられなければ諦め、 しばらくは補足しない (2026-11-04)
                if (FoeMode == FoeMode.Chase && ++_chaseTurns > FreeMapParams.ChaseGiveUp)
                {
                    FoeMode = FoeMode.Patrol; _chaseTurns = 0; _chaseRest = FreeMapParams.ChaseRest;
                    ev |= FreeMapEvent.FoeLost;
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
                    if (FamiliarSeesMe(f.pos)) f.target = Me;
                    if (f.target < 0) break;
                    int np = FoeStepToward(f.pos, f.target);
                    // 縄張りを出ない時、 縄張りの中では目標へ届かない使い魔 (猟犬でない方) は、 その場で見失う (下で消える)
                    if (Leashed && np == f.pos && f.pos != f.target && !f.hound) { f.target = f.pos; break; }
                    f.pos = np;
                    if (f.pos == Me) break;
                }
                if (!f.hound && f.pos == f.target && f.pos != Me && !FamiliarSeesMe(f.pos))
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

        /// <summary>使い魔が点 <paramref name="pos"/> からプレイヤーを見つけるか (縄張りを出ない時は、 縄張りの中のプレイヤーだけ)。</summary>
        private bool FamiliarSeesMe(int pos) => HopsFrom(pos)[Me] <= Perks.familiarSee && (!Leashed || InTerritory(Me));

        /// <summary>縄張りの点だけを残した道 (崩れた道・沈んだ点を反映)。</summary>
        private List<int>[] TerritoryGraph()
        {
            var g = new List<int>[_adj.Length];
            for (int i = 0; i < g.Length; i++)
            {
                g[i] = new List<int>();
                if (!InTerritory(i)) continue;
                foreach (int v in _adj[i]) if (InTerritory(v)) g[i].Add(v);
            }
            return g;
        }

        /// <summary>追跡者 (本体・使い魔) が from から to へ向かう次の 1 歩。 予告と実際の移動の両方がここを通る。
        /// <para>縄張りを出ない時 (<see cref="Leashed"/>): 行き先が縄張りの中なら、 縄張りの中の道だけで最短を取る。
        /// 行き先が縄張りの外 (囮罠) か、 縄張りの中の道では届かない時は、 縄張りの中の隣のうち行き先へ道で近づく点へ。
        /// 近づける点が無ければ動かない ── 縄張りの縁で止まる。 縄張りの外に居る時 (旧い写しなど) は、 普通に歩いて戻る。</para></summary>
        private int FoeStepToward(int from, int to)
        {
            if (!Leashed || !InTerritory(from)) return FreeMapLayout.StepToward(_adj, from, to);
            if (from == to) return from;
            if (InTerritory(to))
            {
                var g = TerritoryGraph();
                var d = FreeMapLayout.Hops(g, to);
                if (d[from] != int.MaxValue)
                {
                    int b = from;
                    foreach (int v in g[from]) if (d[v] < d[b]) b = v;
                    return b;
                }
            }
            var dt = FreeMapLayout.Hops(_adj, to);
            int best = from;
            foreach (int v in _adj[from]) if (InTerritory(v) && dt[v] < dt[best]) best = v;
            return best;
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
                return LastSeen >= 0 ? FoeStepToward(FoeNode, LastSeen) : FoeNode;
            if (mode == FoeMode.Alert)
            {
                // 囮罠へ向かう。 縄張りを出ない時は、 効果の切れた囮罠は追わない (縄張りの外の囮罠の前で縁に立ち続けないように)
                if (_alertLure >= 0 && FoeNode != _alertLure && (!Leashed || HasLureAt(_alertLure)))
                    return FoeStepToward(FoeNode, _alertLure);
                // 痕跡をたどる: いまの点より新しい痕跡のある隣へ (縄張りを出ない時は縄張りの中の隣だけ)
                int best = -1, bt = _trace[FoeNode];
                foreach (int v in _adj[FoeNode])
                    if (HasTrace(v) && _trace[v] > bt && (!Leashed || InTerritory(v))) { bt = _trace[v]; best = v; }
                if (best >= 0) return best;
                mode = FoeMode.Patrol;
            }
            var way = Layout.waypoints;
            int t = way.Count > 0 ? way[patrol % way.Count] : Layout.home;
            if (FoeNode == t)
            {
                patrol++;
                t = way.Count > 0 ? way[patrol % way.Count] : Layout.home;
            }
            int step = FoeStepToward(FoeNode, t);
            // 縄張りを出ない時: 道が崩れて縄張りの中では届かない巡回の点は飛ばす (届く点が 1 つも無ければその場に留まる)
            if (Leashed && step == FoeNode && FoeNode != t)
                for (int k = 1; k < way.Count; k++)
                {
                    patrol++;
                    t = way[patrol % way.Count];
                    if (t == FoeNode) continue;
                    step = FoeStepToward(FoeNode, t);
                    if (step != FoeNode) break;
                }
            return step;
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
                if (InFront(i) && !Done[i] && !_sunk[i] && i != Me && i != FoeNode && i != Layout.goal && i != Layout.start && i != TreasureNode)
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
