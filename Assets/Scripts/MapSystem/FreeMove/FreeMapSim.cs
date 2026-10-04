using System;
using System.Collections.Generic;

namespace MapSystem.FreeMove
{
    /// <summary>徘徊エネミーの状態。 §23-21「巡回 → 警戒 → 追跡 → 見失う」。</summary>
    public enum FoeState
    {
        /// <summary>縄張りの巡回点を毎回同じ順で回る。 縄張りの外へは出ない。</summary>
        Patrol = 0,
        /// <summary>物音・新しい痕跡・囮に気づき、 最後に分かった位置へ向かう。</summary>
        Alert = 1,
        /// <summary>察知したプレイヤーを追う。</summary>
        Chase = 2,
    }

    /// <summary><see cref="FreeMapSim.Tick"/> が 1 刻みの間に起きたことを返すフラグ。
    /// 時間の自動停止・早送りの解除はこれを見て決める (<see cref="FreeMapClockPolicy"/>)。</summary>
    [Flags]
    public enum FreeMapEvent
    {
        None = 0,
        /// <summary>点に着いた (<see cref="FreeMapSim.LastArrived"/>)。 着いた点だけが発動する ── 途中の点は通過するだけ。</summary>
        Arrived = 1 << 0,
        /// <summary>察知された (敵が追跡に入った瞬間)。</summary>
        Detected = 1 << 1,
        /// <summary>徘徊エネミーが目の前 (視界 1) に見えた瞬間。</summary>
        FoeSighted = 1 << 2,
        /// <summary>魔石の反応が一段上がった。</summary>
        StoneRaised = 1 << 3,
        /// <summary>魔石が「察知されている」に入った。</summary>
        StoneHot = 1 << 4,
        /// <summary>徘徊エネミーに捕まった。 連戦の決着まで時間は進まない。</summary>
        Caught = 1 << 5,
        /// <summary>罠が作動した (敵の位置が判明・足止め)。</summary>
        TrapTriggered = 1 << 6,
        /// <summary>偵察が終わった。</summary>
        ScoutFinished = 1 << 7,
        /// <summary>払底中の移動で HP が尽きた。</summary>
        Died = 1 << 8,
        /// <summary>囮が壊された。</summary>
        DecoyBroken = 1 << 9,
        /// <summary>手番が 1 つ進んだ (察知の判定・痕跡の減衰が走った)。</summary>
        TurnPassed = 1 << 10,
        /// <summary>使い魔が見えた瞬間 (視界 1・照明中は 5)。</summary>
        FamiliarSighted = 1 << 11,
        /// <summary>使い魔に接触した。 交戦の決着まで時間は進まない。</summary>
        FamiliarContact = 1 << 12,
        /// <summary>見えている使い魔が消えた (目標地点でプレイヤーを見つけられなかった)。</summary>
        FamiliarVanished = 1 << 13,
    }

    /// <summary>使い魔 (巡航ミサイル)。 察知された地点へ直線で飛び、 狭い探知範囲の内にプレイヤーが居れば向きを変える。
    /// 目標地点に着いてもプレイヤーを探知できなければ消える。 接触すると交戦。</summary>
    public struct Familiar
    {
        public Vec2 pos;
        public Vec2 target;
        /// <summary>再誘導を使ったか (2026-10-03)。 使い魔は自分では探知しない ── 目標地点まで直進し、
        /// 着いて外れた時にプレイヤーが<b>母体の察知距離</b>の内に居れば、 1 度だけ「その瞬間の位置」へ向き直す。
        /// 旧来は探知範囲 1.2 の内で毎刻み向き直す速さ 3 の純追尾で、 回避の軌道をとっても必ず捕まっていた。</summary>
        public bool retargeted;
    }

    /// <summary>移動に払う物資と HP の窓口。 素の C# 側は RunState を知らないのでここを通す。</summary>
    public interface IFreeMapWallet
    {
        int Provision { get; }
        void SpendProvision(int amount);
        /// <summary>HP を払う。 HP が尽きたら true。 <b>死ぬ移動も許す</b> (§23-21) ── 拒否はしない。</summary>
        bool PayHp(int amount);
    }

    /// <summary>行き先の見積もり。</summary>
    public struct MovePreview
    {
        public bool valid;
        /// <summary>道の外で山岳を横切る ＝ 行けない。</summary>
        public bool blocked;
        public double distance;
        public bool road;
        /// <summary>距離 1 あたりの物資 (照明・速さ込み)。</summary>
        public double perDistance;
        public int provisionCost;
        /// <summary>物資が足りない分を HP で払う見込み。</summary>
        public int hpCost;
        public double turns;
    }

    /// <summary>
    /// 自由移動の層の動く中身。 参照実装は docs/design/map-prototype.html (tick / turn / advanceFoe ほか)。
    ///
    /// <para><b>時間は 0.1 手番刻みでしか進まない。</b> 人は <c>FreeMapClock</c> (停止 / 順速 / 早送り) が
    /// 実時間に合わせて <see cref="Tick"/> を呼び、 BOT は <see cref="RunUntil"/> で同期的に進める。
    /// どちらも同じ刻みを踏むので、 同じ操作なら同じ結果になる。</para>
    ///
    /// <para>1 刻みは常にちょうど 0.1 手番 (試作は到着の刻みだけ端数だった)。 時間を整数の刻みで数えて、
    /// 浮動小数の累積誤差で手番の境目がずれないようにしてある。</para>
    ///
    /// <para>乱数は察知の 1d6 だけで、 <c>GameRng.Range(1, 7, "map.free.detect", 層 × 100000 + 手番)</c>。</para>
    /// </summary>
    public sealed class FreeMapSim
    {
        public const string DetectRngKey = "map.free.detect";

        public readonly FreeMapLayout Layout;
        public readonly int Floor;
        private readonly IFreeMapWallet _wallet;

        // ── 自分 ──
        public Vec2 Me { get; private set; }
        /// <summary>今いる点。 移動中は -1。</summary>
        public int AtNode { get; private set; }
        /// <summary>道の上を移動中ならその道の両端。 道の外・点の上では (-1,-1)。</summary>
        public (int a, int b) Segment { get; private set; } = (-1, -1);
        /// <summary>直前に出発した点 (逃げた時に戻る先)。</summary>
        public int PrevNode { get; private set; }
        public int MoveTarget { get; private set; } = -1;
        public bool MoveOnRoad { get; private set; }
        private int _moveFrom = -1;
        public bool IsMoving => MoveTarget >= 0;
        public MoveSpeed Speed { get; set; } = MoveSpeed.Normal;
        public bool FlareOn { get; private set; }
        public int ScoutTicksLeft { get; private set; }
        public bool IsScouting => ScoutTicksLeft > 0;
        /// <summary>戦闘の後の気配が残る手番。</summary>
        public int BattleHeatTurns { get; private set; }
        public int LastArrived { get; private set; } = -1;
        /// <summary>着いた刻みに捕まった点。 連戦が終わった次の刻みで Arrived を出す (新しい行き先を決めたら捨てる)。</summary>
        private int _pendingArrival = -1;
        /// <summary>着いた刻みに捕まった点が、 連戦の後の発動を待っているか。</summary>
        public bool HasPendingArrival => _pendingArrival >= 0 && !IsMoving && AtNode == _pendingArrival;
        public bool InGauntlet { get; private set; }
        public bool Dead { get; private set; }

        // ── 時間 ──
        public long Ticks { get; private set; }
        public double TimeTurns => Ticks * (double)FreeMapParams.Tick;
        public int TurnIndex { get; private set; }
        private int _tickInTurn;

        // ── 見えているもの ──
        /// <summary>マスの種別が判明している点。 点と道の配置は層全体が見える。</summary>
        public readonly bool[] Known;
        public int StoneLevel { get; private set; }
        /// <summary>魔石が指す方角 (2026-09-28): 徘徊エネミーの居る向きを 60° ずつ 6 つに分けた番号 (0〜5)。
        /// 反応なし (段 0) の時は -1。 0 は +x (右) を中心に ±30°、 以後 +y へ回る (<see cref="BearingVector"/>)。</summary>
        public int StoneBearing { get; private set; } = -1;
        /// <summary>徘徊エネミーの最後に分かった位置 (目視・照明・偵察・罠)。</summary>
        public Vec2? LastSeenFoe { get; private set; }
        public double LastSeenFoeTime { get; private set; } = -1;
        /// <summary>偵察で分かった移動方向 (始点→終点)。</summary>
        public (Vec2 from, Vec2 to)? FoeArrow { get; private set; }
        private bool _foeInSightPrev;
        private readonly List<Vec2> _scoutObs = new List<Vec2>();
        private int _scoutObsTurn = -1;
        private bool _scoutActive;

        // ── 痕跡・囮・罠 ──
        public struct Track { public Vec2 p; public int strength; }
        public struct Decoy { public Vec2 p; public int turns; public int delay; }
        public readonly List<Track> Tracks = new List<Track>();
        public readonly List<Decoy> Decoys = new List<Decoy>();
        public readonly List<Vec2> Traps = new List<Vec2>();
        private double _trackAcc;

        // ── 支払いの端数 ──
        private double _provFrac, _hpFrac;

        // ── 徘徊エネミー ──
        public bool HasFoe { get; private set; }
        public bool FoeAlive { get; private set; }
        public FoeState FoeState { get; private set; }
        /// <summary>足止めの残り刻み。 手番の境目ではなく刻みで数える ── 境目で数えると
        /// 「1 手番の足止め」が 0.1〜1 手番のどれにもなり得る。</summary>
        public int FoeFreezeTicks { get; private set; }
        public int FoeFreezeTurns => (FoeFreezeTicks + FreeMapParams.TicksPerTurn - 1) / FreeMapParams.TicksPerTurn;
        private FoeState _freezeResume = FoeState.Chase;
        private int _fa, _fb;
        private double _ft;
        private readonly List<int> _foePath = new List<int>();
        private int _baseIdx;
        private Vec2? _lastKnown;
        private int _lost;
        public int GauntletFights { get; private set; }

        /// <summary>この層の徘徊エネミーの性質。 <b>プレイヤーには明かさない</b> (BOT も読まない)。</summary>
        public FoeProfile Profile { get; private set; }
        /// <summary>道を外れて飛び掛かっている最中の位置 (飛び掛かりの性質)。 道の上なら null。</summary>
        private Vec2? _lungePos;

        // ── 使い魔 ──
        public readonly List<Familiar> Familiars = new List<Familiar>();
        private int _familiarCooldownTicks;
        private bool _familiarInSightPrev;
        /// <summary>使い魔と交戦中 (時間は進まない)。</summary>
        public bool InFamiliarFight { get; private set; }
        /// <summary>[計装] 放った使い魔の数・接触した数。</summary>
        public int FamiliarsLaunched { get; private set; }
        public int FamiliarContacts { get; private set; }

        public FreeMapSim(FreeMapLayout layout, IFreeMapWallet wallet, bool withFoe, FoeProfile profile = null)
        {
            Layout = layout ?? throw new ArgumentNullException(nameof(layout));
            Floor = layout.floor;
            _wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
            Known = new bool[layout.Count];
            AtNode = layout.start;
            PrevNode = layout.start;
            Me = layout.pos[layout.start];
            HasFoe = withFoe && layout.waypoints.Count > 0;
            FoeAlive = HasFoe;
            GauntletFights = FloorPlan.GauntletFights(Floor);
            Profile = profile ?? (HasFoe ? FoeProfile.ForFloor(Floor) : FoeProfile.Standard);
            if (HasFoe)
            {
                _fa = _fb = layout.waypoints[0];
                _ft = 0;
                FoeState = FoeState.Patrol;
                PlanFoe();
            }
            Reveal(FreeMapParams.VisionNone);
            _foeInSightPrev = FoeAlive && Vec2.Dist(FoePos, Me) <= FreeMapParams.VisionNone + 1e-6;
            StoneLevel = ComputeStoneLevel();
            StoneBearing = StoneLevel > 0 ? BearingOf(FoePos - Me) : -1;
        }

        // =====================================================================
        //  読み取り
        // =====================================================================

        public Vec2 FoePos => _lungePos ?? Vec2.Lerp(Layout.pos[_fa], Layout.pos[_fb], _ft);

        /// <summary>いま見えている使い魔 (視界 1・照明中は 5)。 見えていないものは返さない。</summary>
        public IEnumerable<Vec2> VisibleFamiliars()
        {
            double r = FlareOn ? FreeMapParams.VisionFlare : FreeMapParams.VisionNone;
            foreach (var f in Familiars) if (Vec2.Dist(f.pos, Me) <= r + 1e-6) yield return f.pos;
        }

        public double FoeDistance => FoeAlive ? Vec2.Dist(FoePos, Me) : double.PositiveInfinity;

        /// <summary>気配。 道を移動中 +2 (速さで置換) / 戦闘の後 +2。 点に止まっている時・道の外は 0。</summary>
        public int Kehai
            => (IsMoving ? Speed.Kehai(MoveOnRoad) : 0) + (BattleHeatTurns > 0 ? FreeMapParams.KehaiBattle : 0);

        /// <summary>察知距離。 照明なし: 2 ＋ 気配 (最大 4)。 照明中: 照明で見える範囲 5 で固定。</summary>
        public double DetectRadius
            => FlareOn ? FreeMapParams.VisionFlare
                       : Math.Min(FreeMapParams.DetectCap, FreeMapParams.DetectBase + Kehai);

        /// <summary>1 手番あたりの察知確率 (距離 d・察知距離 R)。 d ≦ R なら 1d6 ≦ max(1, ⌊R − d + 1⌋)。</summary>
        public static double DetectChance(double d, double R)
        {
            if (d > R) return 0;
            return DetectNeed(d, R) / 6.0;
        }

        /// <summary>察知に要る出目の上限 (1〜6)。</summary>
        public static int DetectNeed(double d, double R)
            => Math.Min(6, Math.Max(1, (int)Math.Floor(R - d + 1)));

        /// <summary>今いる場所から点 j へ道で行けるか (点から隣へ・道の途中から両端へ)。</summary>
        public bool IsRoadTo(int j)
        {
            if (AtNode >= 0) return Layout.HasRoad(AtNode, j);
            return Segment.a == j || Segment.b == j;
        }

        public MovePreview Preview(int j)
        {
            var pv = new MovePreview();
            if (j < 0 || j >= Layout.Count || j == AtNode) return pv;
            pv.valid = true;
            pv.road = IsRoadTo(j);
            pv.distance = Vec2.Dist(Me, Layout.pos[j]);
            pv.perDistance = PerDistance(pv.road);
            pv.provisionCost = (int)Math.Round(pv.distance * pv.perDistance);
            int shortfall = Math.Max(0, pv.provisionCost - _wallet.Provision);
            pv.hpCost = shortfall > 0
                ? (int)Math.Ceiling(shortfall / pv.perDistance * HpRate(pv.road) * Speed.CostMul())
                : 0;
            pv.turns = pv.distance * Speed.TimeMul();
            pv.blocked = !pv.road && Layout.SegmentHitsMountain(Me, Layout.pos[j]);
            return pv;
        }

        private double PerDistance(bool road)
            => ((road ? FreeMapParams.CostRoad : FreeMapParams.CostOffRoad) + (FlareOn ? FreeMapParams.CostFlare : 0))
               * (double)Speed.CostMul();

        private static int HpRate(bool road) => road ? FreeMapParams.HpPayRoad : FreeMapParams.HpPayOffRoad;

        // =====================================================================
        //  操作
        // =====================================================================

        /// <summary>行き先を決める (移動中なら行き先を変える)。 山岳を横切る・偵察中・連戦中は false。</summary>
        public bool SetDestination(int j)
        {
            if (Dead || InGauntlet || InFamiliarFight || IsScouting) return false;
            var pv = Preview(j);
            if (!pv.valid || pv.blocked) return false;
            if (!IsMoving) PrevNode = AtNode >= 0 ? AtNode : Layout.NearestNode(Me);
            _pendingArrival = -1;
            MoveOnRoad = pv.road;
            _moveFrom = AtNode >= 0 ? AtNode : (Segment.a >= 0 ? (Segment.a == j ? Segment.b : Segment.a) : -1);
            MoveTarget = j;
            return true;
        }

        public void SetFlare(bool on)
        {
            FlareOn = on;
            if (on) { Reveal(FreeMapParams.VisionFlare); SnapshotFoe(FreeMapParams.VisionFlare); }
        }

        /// <summary>偵察を始める (止まっている時だけ)。 ScoutTurns 手番動けない。</summary>
        public bool StartScout()
        {
            if (IsMoving || IsScouting || InGauntlet || Dead) return false;
            ScoutTicksLeft = FreeMapParams.ScoutTurns * FreeMapParams.TicksPerTurn;
            _scoutActive = true;
            _scoutObs.Clear();
            _scoutObsTurn = -1;
            ScoutObserve(false);
            return true;
        }

        /// <summary>囮を今いる場所に置く (所持数の管理は呼び出し側)。</summary>
        public void PlaceDecoy()
            => Decoys.Add(new Decoy { p = Me, turns = FreeMapParams.DecoyTurns, delay = FreeMapParams.DecoyDelay });

        /// <summary>罠を今いる場所に置く (所持数の管理は呼び出し側)。 道の外に置いても敵はまず踏まない。</summary>
        public void PlaceTrap() => Traps.Add(Me);

        /// <summary>物音: 半径 r の (追跡中でない) 敵を警戒させ、 今の位置へ呼ぶ。</summary>
        public void MakeNoise(double r)
        {
            if (!FoeAlive || FoeState == FoeState.Chase) return;
            if (Vec2.Dist(FoePos, Me) > r) return;
            FoeState = FoeState.Alert;
            _lastKnown = Me;
            PlanFoe();
        }

        /// <summary>戦闘が終わった: 2 手番の気配 +2 と、 半径 4 の物音。</summary>
        public void NoteBattleEnded()
        {
            BattleHeatTurns = FreeMapParams.BattleKehaiTurns;
            MakeNoise(FreeMapParams.BattleNoise);
        }

        /// <summary>補給庫を漁った物音 (半径 3・気配は上げない)。</summary>
        public void NoteCacheLooted() => MakeNoise(FreeMapParams.CacheNoise);

        /// <summary>通常の戦闘から逃げた: 直前の点へ戻る (そのマスは使い切りにならない ── 呼び出し側の責務)。</summary>
        public void FleeToPrevious()
        {
            int to = PrevNode >= 0 ? PrevNode : Layout.start;
            MoveTarget = -1;
            _pendingArrival = -1;
            Me = Layout.pos[to];
            AtNode = to;
            Segment = (-1, -1);
        }

        /// <summary>連戦に勝ち切った: 徘徊エネミーは倒れる。</summary>
        public void ResolveGauntletWon()
        {
            InGauntlet = false;
            FoeAlive = false;
            BattleHeatTurns = FreeMapParams.BattleKehaiTurns;
            StoneLevel = 0;
            StoneBearing = -1;
        }

        /// <summary>連戦から逃げた: 2 手番後から追跡。 移動中に捕まったなら出発した点へ戻る
        /// (点に居て捕まったならその場に留まる ── そこが「直前の点」)。</summary>
        public void ResolveGauntletFled()
        {
            InGauntlet = false;
            if (AtNode < 0) FleeToPrevious();
            MoveTarget = -1;
            FoeState = FoeState.Chase;
            _freezeResume = FoeState.Chase;
            FoeFreezeTicks = FreeMapParams.FleeFreezeTurns * FreeMapParams.TicksPerTurn;
            _lost = 0;
        }

        // =====================================================================
        //  時間
        // =====================================================================

        /// <summary>stopMask のどれかが起きるか、 maxTicks 刻み進むか、 止まる理由 (死亡・連戦・何もすることが無い) が
        /// 生じるまで同期的に進める。 BOT 用。 起きたことの和を返す。</summary>
        public FreeMapEvent RunUntil(FreeMapEvent stopMask, int maxTicks)
        {
            var acc = FreeMapEvent.None;
            for (int i = 0; i < maxTicks; i++)
            {
                var ev = Tick();
                acc |= ev;
                if ((ev & stopMask) != 0 || Dead || InGauntlet || InFamiliarFight) break;
            }
            return acc;
        }

        /// <summary>0.1 手番進める。</summary>
        public FreeMapEvent Tick()
        {
            if (Dead || InGauntlet || InFamiliarFight) return FreeMapEvent.None;
            var ev = FreeMapEvent.None;
            int arrivedAt = -1;
            if (_pendingArrival >= 0 && !IsMoving && AtNode == _pendingArrival) arrivedAt = _pendingArrival;
            _pendingArrival = -1;

            if (IsMoving)
            {
                arrivedAt = StepMove(ref ev);
                if (Dead) return ev | FreeMapEvent.Died;
            }
            else if (IsScouting)
            {
                ScoutTicksLeft--;
            }
            Ticks++;

            if (FoeAlive && FoeFreezeTicks > 0 && --FoeFreezeTicks == 0)
            {
                FoeState = _freezeResume; _lost = 0; PlanFoe();
                _freezeResume = FoeState.Chase;
            }
            else if (FoeAlive && FoeFreezeTicks <= 0)
            {
                AdvanceFoe(FreeMapParams.Tick * (FoeState == FoeState.Patrol ? Profile.patrolSpeed : Profile.chaseSpeed));
                var fp = FoePos;
                int k = Traps.FindIndex(t => Vec2.Dist(t, fp) <= FreeMapParams.TrapHitDist);
                if (k >= 0)
                {
                    Traps.RemoveAt(k);
                    _freezeResume = FoeState == FoeState.Chase ? FoeState.Chase : FoeState.Patrol;
                    FoeFreezeTicks = FreeMapParams.TrapFreezeTurns * FreeMapParams.TicksPerTurn;
                    LastSeenFoe = fp; LastSeenFoeTime = TimeTurns;   // 罠が作動した場所 ＝ 敵の位置
                    ev |= FreeMapEvent.TrapTriggered;
                }
            }

            if (_scoutActive)
            {
                ScoutObserve(ScoutTicksLeft == 0);
                if (ScoutTicksLeft == 0)
                {
                    _scoutActive = false;
                    FinishScout();
                    ev |= FreeMapEvent.ScoutFinished;
                }
            }

            if (++_tickInTurn >= FreeMapParams.TicksPerTurn)
            {
                _tickInTurn = 0;
                ev |= Turn();
                if (Dead) return ev | FreeMapEvent.Died;
            }

            if (FlareOn) { Reveal(FreeMapParams.VisionFlare); SnapshotFoe(FreeMapParams.VisionFlare); }
            Reveal(FreeMapParams.VisionNone);
            // 目視: 視界 1 に入った瞬間だけ (居続けても毎刻みは出さない)
            bool inSight = FoeAlive && Vec2.Dist(FoePos, Me) <= FreeMapParams.VisionNone + 1e-6;
            if (inSight) { LastSeenFoe = FoePos; LastSeenFoeTime = TimeTurns; }
            if (inSight && !_foeInSightPrev) ev |= FreeMapEvent.FoeSighted;
            _foeInSightPrev = inSight;

            ev |= UpdateStone();

            if (_familiarCooldownTicks > 0) _familiarCooldownTicks--;
            ev |= AdvanceFamiliars();
            if (InFamiliarFight)
            {
                MoveTarget = -1;
                _pendingArrival = arrivedAt;         // 着いた点は交戦の後で発動する
                return ev | FreeMapEvent.FamiliarContact;
            }

            if (FoeAlive && FoeFreezeTicks <= 0 && Vec2.Dist(FoePos, Me) <= FreeMapParams.CatchDist)
            {
                InGauntlet = true;
                MoveTarget = -1;
                _pendingArrival = arrivedAt;         // 着いた点は連戦の後で発動する (捕まった方が先)
                return ev | FreeMapEvent.Caught;
            }

            if (arrivedAt >= 0)
            {
                LastArrived = arrivedAt;
                ev |= FreeMapEvent.Arrived;
            }
            return ev;
        }

        private int StepMove(ref FreeMapEvent ev)
        {
            var to = Layout.pos[MoveTarget];
            double rem = Vec2.Dist(Me, to);
            double step = Math.Min(Speed.StepPerTick(), rem);
            double per = PerDistance(MoveOnRoad);
            Pay(step * per, step * HpRate(MoveOnRoad) * Speed.CostMul());
            if (Dead) return -1;

            double k = rem > 0 ? step / rem : 1;
            Me = new Vec2(Me.x + (to.x - Me.x) * k, Me.y + (to.y - Me.y) * k);
            AtNode = -1;
            Segment = MoveOnRoad && _moveFrom >= 0 ? (_moveFrom, MoveTarget) : (-1, -1);
            _trackAcc += step;
            if (_trackAcc >= 1)
            {
                _trackAcc -= 1;
                Tracks.Add(new Track { p = Me, strength = MoveOnRoad ? FreeMapParams.TrackRoad : FreeMapParams.TrackOffRoad });
            }
            if (rem - step <= 1e-6)
            {
                int j = MoveTarget;
                Me = to; AtNode = j; Segment = (-1, -1); MoveTarget = -1;
                return j;
            }
            return -1;
        }

        /// <summary>物資で払えない分は HP で払う (払底)。 死ぬ移動も許す。</summary>
        private void Pay(double provCost, double hpCost)
        {
            int have = _wallet.Provision;
            if (have >= provCost)
            {
                _provFrac += provCost;
                int whole = (int)Math.Floor(_provFrac);
                if (whole > have) whole = have;
                if (whole > 0) _wallet.SpendProvision(whole);
                _provFrac -= whole;
                return;
            }
            double covered = provCost > 0 ? have / provCost : 1;
            if (have > 0) _wallet.SpendProvision(have);
            _provFrac = 0;
            _hpFrac += hpCost * (1 - covered);
            int hp = (int)Math.Floor(_hpFrac);
            if (hp > 0)
            {
                _hpFrac -= hp;
                if (_wallet.PayHp(hp)) Dead = true;
            }
        }

        /// <summary>手番ごと: 痕跡が薄れる / 戦闘の気配が冷める / 照明の消費 (止まっている時) / 囮 / 敵の察知。</summary>
        private FreeMapEvent Turn()
        {
            var ev = FreeMapEvent.TurnPassed;
            int turn = TurnIndex++;
            for (int i = Tracks.Count - 1; i >= 0; i--)
            {
                var t = Tracks[i]; t.strength--;
                if (t.strength <= 0) Tracks.RemoveAt(i); else Tracks[i] = t;
            }
            if (BattleHeatTurns > 0) BattleHeatTurns--;
            if (FlareOn && !IsMoving) { Pay(FreeMapParams.CostFlare, 0); if (Dead) return ev; }
            if (!FoeAlive) return ev;

            if (FoeFreezeTicks > 0) return ev;   // 足止め中は察知も囮も無い (解除は Tick が刻みで数える)

            // 囮: 置いてから DecoyDelay 手番後に鳴り始め、 追跡中でない近くの敵を呼ぶ。 敵が着くと壊れる
            for (int i = 0; i < Decoys.Count; i++)
            {
                var d = Decoys[i]; d.turns--;
                if (d.delay > 0) { d.delay--; Decoys[i] = d; continue; }
                var fp = FoePos;
                if (FoeState != FoeState.Chase && Vec2.Dist(fp, d.p) <= FreeMapParams.DecoyNoise)
                {
                    if (FoeState != FoeState.Alert || _lastKnown == null || Vec2.Dist(_lastKnown.Value, d.p) > 0.1)
                    { FoeState = FoeState.Alert; _lastKnown = d.p; PlanFoe(); }
                }
                if (Vec2.Dist(FoePos, d.p) <= FreeMapParams.DecoyBreak) { d.turns = 0; ev |= FreeMapEvent.DecoyBroken; }
                Decoys[i] = d;
            }
            Decoys.RemoveAll(x => x.turns <= 0);

            double dist = Vec2.Dist(FoePos, Me), R = DetectRadius;
            bool seen = false;
            if (dist <= R)
            {
                int roll = GameLoop.GameRng.Range(1, 7, DetectRngKey, Floor * 100000 + turn);
                seen = roll <= DetectNeed(dist, R);
            }
            if (seen)
            {
                if (FoeState != FoeState.Chase)
                {
                    // 影の性質は察知した合図を出さない (魔石にも反応しない)
                    if (!Profile.silentWhileChasing) ev |= FreeMapEvent.Detected;
                    LaunchFamiliar(Me);   // 察知した地点へ使い魔を放つ
                }
                FoeState = FoeState.Chase; _lost = 0; _lastKnown = Me; PlanFoe();
            }
            else if (FoeState == FoeState.Chase)
            {
                if (++_lost >= Profile.lostTurns)
                {
                    FoeState = FoeState.Alert;
                    if (_lastKnown == null) _lastKnown = Me;
                }
                PlanFoe();
            }
            else if (FoeState == FoeState.Patrol)
            {
                var fp = FoePos;
                foreach (var t in Tracks)
                    if (Vec2.Dist(t.p, fp) <= FreeMapParams.TrackNoticeDist)
                    { FoeState = FoeState.Alert; _lastKnown = t.p; PlanFoe(); break; }
            }
            return ev;
        }

        private int ComputeStoneLevel()
        {
            if (!FoeAlive) return 0;
            double d = Vec2.Dist(FoePos, Me);
            if (d > FreeMapParams.VisionStone) return 0;
            bool hunting = FoeState == FoeState.Chase || FoeFreezeTicks > 0;
            if (hunting && Profile.silentWhileChasing) return 0;              // 影: 追跡中は一切反応しない
            if (hunting) return 3;                                              // 察知されている
            return d <= FreeMapParams.StoneNearDist ? 2 : 1;                    // かなり近い / 遠い
        }

        private FreeMapEvent UpdateStone()
        {
            int lv = ComputeStoneLevel();
            var ev = FreeMapEvent.None;
            if (lv > StoneLevel) ev |= FreeMapEvent.StoneRaised;
            if (lv == 3 && StoneLevel != 3) ev |= FreeMapEvent.StoneHot;
            StoneLevel = lv;
            StoneBearing = lv > 0 ? BearingOf(FoePos - Me) : -1;
            return ev;
        }

        // 方角の境目 (30° + 60°k の単位ベクトル)。 三角関数を使わず √3 の定数だけで決める ── 実装差で境目が揺れない
        private const double H3 = 0.8660254037844386;   // √3 / 2
        private static readonly Vec2[] BearingEdges =
        {
            new Vec2(H3, 0.5), new Vec2(0, 1), new Vec2(-H3, 0.5), new Vec2(-H3, -0.5), new Vec2(0, -1), new Vec2(H3, -0.5),
        };
        private static readonly Vec2[] BearingCenters =
        {
            new Vec2(1, 0), new Vec2(0.5, H3), new Vec2(-0.5, H3), new Vec2(-1, 0), new Vec2(-0.5, -H3), new Vec2(0.5, -H3),
        };

        /// <summary>向き d が 6 つの方角のどれか (0〜5)。 0 は +x を中心に ±30°。</summary>
        public static int BearingOf(Vec2 d)
        {
            if (d.x == 0 && d.y == 0) return 0;
            for (int k = 0; k < 6; k++)
            {
                var a = BearingEdges[(k + 5) % 6]; var b = BearingEdges[k];   // 方角 k は境目 k-1 から境目 k まで
                if (Cross(a, d) >= 0 && Cross(d, b) > 0) return k;
            }
            return 0;
        }

        /// <summary>方角の中心の単位ベクトル。</summary>
        public static Vec2 BearingVector(int sector) => BearingCenters[((sector % 6) + 6) % 6];

        private static double Cross(Vec2 a, Vec2 b) => a.x * b.y - a.y * b.x;

        private void Reveal(double r)
        {
            for (int i = 0; i < Layout.Count; i++)
                if (!Known[i] && Vec2.Dist(Layout.pos[i], Me) <= r + 1e-6) Known[i] = true;
        }

        private void SnapshotFoe(double r)
        {
            if (!FoeAlive) return;
            var p = FoePos;
            if (Vec2.Dist(p, Me) <= r + 1e-6) { LastSeenFoe = p; LastSeenFoeTime = TimeTurns; }
        }

        // 偵察: 範囲内の敵の位置を手番ごとに記録し、 終わった時にまとめて出す
        private void ScoutObserve(bool final)
        {
            if (!FoeAlive) return;
            var p = FoePos;
            if (Vec2.Dist(p, Me) > FreeMapParams.VisionScout) return;
            if (_scoutObs.Count == 0 || final || TurnIndex != _scoutObsTurn)
            {
                _scoutObs.Add(p);
                _scoutObsTurn = TurnIndex;
            }
        }

        private void FinishScout()
        {
            Reveal(FreeMapParams.VisionScout);
            if (_scoutObs.Count > 0)
            {
                var a = _scoutObs[0]; var b = _scoutObs[_scoutObs.Count - 1];
                LastSeenFoe = b; LastSeenFoeTime = TimeTurns;
                FoeArrow = _scoutObs.Count >= 2 && Vec2.Dist(a, b) > 0.15 ? (a, b) : ((Vec2, Vec2)?)null;
            }
            _scoutObs.Clear();
            _scoutObsTurn = -1;
        }

        // =====================================================================
        //  徘徊エネミー (道の上しか動けない)
        // =====================================================================

        private int FoeTargetNode()
        {
            if (FoeState == FoeState.Chase) return Layout.NearestNode(Me);
            if (FoeState == FoeState.Alert && _lastKnown != null) return Layout.NearestNode(_lastKnown.Value);
            return Layout.waypoints[_baseIdx];
        }

        private void PlanFoe()
        {
            if (!FoeAlive) return;
            var allowed = FoeState == FoeState.Patrol ? Layout.territory : null;
            _foePath.Clear();
            if (_fa != _fb && _ft > 0 && _ft < 1)
            {   // 道の途中: 向かっている端まで行ってから経路に乗る
                _foePath.AddRange(Layout.ShortestRoadPath(_fb, FoeTargetNode(), allowed));
                return;
            }
            int here = _ft < 0.5 ? _fa : _fb;
            _fa = _fb = here; _ft = 0;
            _foePath.AddRange(Layout.ShortestRoadPath(here, FoeTargetNode(), allowed));
        }

        private void AdvanceFoe(double amount)
        {
            // 飛び掛かり: 追跡中で近ければ道を外れて直進。 離れたら最寄りの点から道へ戻る
            if (Profile.lungeRange > 0)
            {
                if (FoeState == FoeState.Chase)
                {
                    var fp = FoePos; double d = Vec2.Dist(fp, Me);
                    if (d <= Profile.lungeRange || (_lungePos.HasValue && d <= Profile.lungeRange + 1.0))
                    {
                        double k = Math.Min(1, amount / Math.Max(1e-9, d));
                        _lungePos = Vec2.Lerp(fp, Me, k);
                        return;
                    }
                }
                if (_lungePos.HasValue) ReturnToRoad();
            }
            int guard = 0;
            while (amount > 1e-9 && guard++ < 64)
            {
                if (_fa == _fb)
                {
                    if (_foePath.Count == 0)
                    {   // 目的地に着いた
                        if (FoeState == FoeState.Patrol)
                        {
                            _baseIdx = (_baseIdx + 1) % Layout.waypoints.Count;
                            _foePath.AddRange(Layout.ShortestRoadPath(_fa, Layout.waypoints[_baseIdx], Layout.territory));
                            if (_foePath.Count == 0) return;
                        }
                        else if (FoeState == FoeState.Alert)
                        {   // 何も無かった: 最寄りの巡回点へ戻り、 縄張りの巡回を再開
                            FoeState = FoeState.Patrol; _lastKnown = null;
                            int bi = 0;
                            for (int i = 1; i < Layout.waypoints.Count; i++)
                                if (Layout.Dist(Layout.waypoints[i], _fa) < Layout.Dist(Layout.waypoints[bi], _fa)) bi = i;
                            _baseIdx = bi;
                            PlanFoe();
                            if (_foePath.Count == 0) return;
                        }
                        else return;   // 追跡: 道の端で待つ (道の外へは出られない)
                    }
                    _fb = _foePath[0]; _foePath.RemoveAt(0); _ft = 0;
                }
                double len = Layout.Dist(_fa, _fb);
                if (len < 1e-6) len = 1e-6;
                double rest = (1 - _ft) * len, step = Math.Min(rest, amount);
                _ft += step / len; amount -= step;
                if (_ft >= 1 - 1e-9) { _fa = _fb; _ft = 0; }
            }
        }

        /// <summary>飛び掛かりの後、 最寄りの点へ戻って道の上の動きに戻る。</summary>
        private void ReturnToRoad()
        {
            var p = _lungePos.Value;
            _lungePos = null;
            _fa = _fb = Layout.NearestNode(p); _ft = 0;
            PlanFoe();
        }

        // =====================================================================
        //  使い魔 (巡航ミサイル)
        // =====================================================================

        private void LaunchFamiliar(Vec2 target)
        {
            if (!FoeAlive || !Profile.LaunchesFamiliars) return;
            if (_familiarCooldownTicks > 0 || Familiars.Count >= Profile.familiarMax) return;
            Familiars.Add(new Familiar { pos = FoePos, target = target });
            _familiarCooldownTicks = Profile.familiarCooldownTurns * FreeMapParams.TicksPerTurn;
            FamiliarsLaunched++;
        }

        /// <summary>使い魔を 1 刻み飛ばす。 目標地点まで直進し、 途中で触れれば交戦。 着いて外れたら、
        /// プレイヤーが母体の察知距離の内に居れば <b>1 度だけ</b> その位置へ向き直し、 居なければ (2 度目なら) 消える。</summary>
        private FreeMapEvent AdvanceFamiliars()
        {
            var ev = FreeMapEvent.None;
            if (Familiars.Count == 0) { _familiarInSightPrev = false; return ev; }
            double step = FreeMapParams.FamiliarSpeed * FreeMapParams.Tick;
            double vis = FlareOn ? FreeMapParams.VisionFlare : FreeMapParams.VisionNone;
            bool anyInSight = false;
            for (int i = Familiars.Count - 1; i >= 0; i--)
            {
                var f = Familiars[i];
                double rem = Vec2.Dist(f.pos, f.target);
                if (rem <= step) f.pos = f.target;
                else f.pos = Vec2.Lerp(f.pos, f.target, step / rem);
                bool visible = Vec2.Dist(f.pos, Me) <= vis + 1e-6;
                if (Vec2.Dist(f.pos, Me) <= FreeMapParams.FamiliarCatchDist)
                {
                    Familiars.RemoveAt(i);
                    InFamiliarFight = true;
                    FamiliarContacts++;
                    return ev;
                }
                if (Vec2.Dist(f.pos, f.target) <= 1e-9)
                {
                    if (!f.retargeted && FoeAlive && Vec2.Dist(FoePos, Me) <= DetectRadius) { f.target = Me; f.retargeted = true; }
                    else
                    {
                        Familiars.RemoveAt(i);
                        if (visible) ev |= FreeMapEvent.FamiliarVanished;
                        continue;
                    }
                }
                Familiars[i] = f;
                anyInSight |= visible;
            }
            if (anyInSight && !_familiarInSightPrev) ev |= FreeMapEvent.FamiliarSighted;
            _familiarInSightPrev = anyInSight;
            return ev;
        }

        /// <summary>使い魔との交戦が終わった (勝っても逃げても使い魔は消える)。 逃げたなら移動中は出発した点へ戻る。</summary>
        public void ResolveFamiliarFight(bool fled)
        {
            InFamiliarFight = false;
            if (fled && AtNode < 0) FleeToPrevious();
            MoveTarget = -1;
        }

        /// <summary>テスト・デバッグ用: 徘徊エネミーが今いる道の両端 (点の上なら同じ値)。</summary>
        public (int a, int b) FoeEdge => (_fa, _fb);
    }

    /// <summary>
    /// 時間の自動停止と早送りの解除の規則。 §23-21「自動の一時停止（察知・目視）は時間の停止として働く」
    /// 「早送りは『点に着いた』『察知された』『魔石の反応が一段上がった』で自動解除して順速へ戻る」。
    /// <b>察知で時間が止まる演出はこのゲームの肝</b>なので、 規則をここ 1 か所に置いて人の時計が必ずこれを読む。
    /// </summary>
    public static class FreeMapClockPolicy
    {
        /// <summary>時間を止める契機: 察知された・魔石が「察知されている」に入った・敵を目視・使い魔を目視・接触・捕まった・死んだ。</summary>
        public const FreeMapEvent PauseMask =
            FreeMapEvent.Detected | FreeMapEvent.StoneHot | FreeMapEvent.FoeSighted
            | FreeMapEvent.FamiliarSighted | FreeMapEvent.FamiliarContact
            | FreeMapEvent.Caught | FreeMapEvent.Died;

        /// <summary>早送りを解除する契機 (止めはしない): 点に着いた・魔石の反応が一段上がった。</summary>
        public const FreeMapEvent CancelFastMask = FreeMapEvent.Arrived | FreeMapEvent.StoneRaised;

        public static bool ShouldPause(FreeMapEvent ev) => (ev & PauseMask) != 0;
        public static bool ShouldCancelFast(FreeMapEvent ev) => (ev & (CancelFastMask | PauseMask)) != 0;
    }
}
