using CombatSystem;
using MapSystem;
using MapSystem.FreeMove;
using UnityEngine;

namespace GameLoop
{
    /// <summary>
    /// 航行マップ (ターン制・2026-10-18・docs/GAME.md §5) の進行。 動く中身は素の C# の <see cref="FreeMapSim"/>
    /// (<see cref="MapManager.Sim"/>) で、 ここはゲームの規則 (到着した点の起動・追跡者との戦闘・
    /// 逃げる・囮罠・休憩) へ橋渡しする。
    ///
    /// <para><b>時間を進める口は「1 歩動く」「待つ」「偵察」「休憩で多めに回復」だけ。</b> どれも
    /// <see cref="FreeMapSim"/> の操作 1 回が 1 ターンぶん (雪の前線の内側は 2 ターン) で、
    /// 戦闘・イベント・照明・囮罠はターンを使わない。 人も BOT も同じ口を同期的に叩く。</para>
    /// </summary>
    public partial class GameManager
    {
        /// <summary>true の間は、 点に着いても戦闘・イベント・ショップなどの旧画面へ入らない (航行だけを試すため)。 2D 画面 (Game2DMapView) が立てる。</summary>
        public static bool NavOnly;

        /// <summary>航行マップの中身。 8 層・マップ外では null。</summary>
        public FreeMapSim FreeMap => MapManager.Instance != null && MapManager.Instance.IsFreeMap ? MapManager.Instance.Sim : null;

        /// <summary>操作ごとに起きたこと (None は流さない)。 表示・効果音・計器が購読する。</summary>
        public event System.Action<FreeMapEvent> OnFreeMapEvent;

        /// <summary>航行の画面演出の合図 (偵察・照明)。 ターンを進めない照明や、 イベントを返さない偵察でも鳴る。 Game2DMapFx が購読する。</summary>
        public enum NavFx { Recon, Flare }
        public event System.Action<NavFx> OnNavFx;

        // ── 追跡者との戦闘 ──
        private bool _inPursuerFight;
        private int _pursuerFightCount;
        /// <summary>追跡者 (本体) に捕まって戦闘中か。</summary>
        public bool InPursuerCombat => _inPursuerFight;

        // ── 使い魔との戦闘 ──
        private bool _inFamiliarFight;
        private int _familiarFightCount;
        /// <summary>使い魔との戦闘中か。</summary>
        public bool InFamiliarCombat => _inFamiliarFight;
        /// <summary>マスに紐づかない戦闘 (追跡者・使い魔) の途中か。 マスの報酬・ボス判定を通さない。</summary>
        public bool InMapEncounterCombat => _inPursuerFight || _inFamiliarFight;

        // =====================================================================
        //  操作 (ターンを進める)
        // =====================================================================

        /// <summary>隣の点へ 1 歩動く。 行けない (道が無い・沈んだ・戦闘中) なら false。</summary>
        public bool MoveStep(string nodeId)
        {
            var sim = FreeMapForAction();
            if (sim == null) return false;
            var node = MapManager.Instance.CurrentMap.GetNode(nodeId);
            if (node == null || node.index < 0 || !sim.CanMoveTo(node.index))
            {
                Log("そこへは行けない（道がつながっていない）");
                return false;
            }
            RunFreeMapOp(s => s.MoveTo(node.index));
            return true;
        }

        /// <summary>〈補給線〉が使えるか (整備パネル〈兵站〉r10)。</summary>
        public static bool SupplyLineUnlocked => MetaProgression.MetaBuffApplicator.IsSupplyLineUnlocked();

        /// <summary>〈補給線〉: この層で一度着いた前哨基地・店へ跳ぶ (物資 1・ターンを使わない・補足は解けない)。</summary>
        public bool RelayStep(string nodeId)
        {
            var sim = FreeMapForAction();
            if (sim == null || !SupplyLineUnlocked) return false;
            var node = MapManager.Instance.CurrentMap.GetNode(nodeId);
            if (node == null || node.index < 0 || !sim.CanRelayTo(node.index)) return false;
            Log($"補給線（物資 −{FreeMapParams.RelaySupply}・ターンは使わない）→ {TileToJapanese(node.type)}");
            RunFreeMapOp(s => s.RelayTo(node.index));
            return true;
        }

        /// <summary>待つ (1 ターン)。</summary>
        public bool WaitTurn()
        {
            var sim = FreeMapForAction();
            if (sim == null) return false;
            Log("待つ（1ターン）");
            RunFreeMapOp(s => s.Wait());
            return true;
        }

        /// <summary>偵察 (1 ターン): 道で 2 歩先まで見える。</summary>
        public bool ReconTurn()
        {
            var sim = FreeMapForAction();
            if (sim == null) return false;
            Log("偵察（1ターン）");
            RunFreeMapOp(s => s.Recon());
            OnNavFx?.Invoke(NavFx.Recon);
            return true;
        }

        /// <summary>照明 (ターンは使わない): 物資 −50 で 3 ターンのあいだ道で 3 歩先まで見える。</summary>
        public bool UseFlare()
        {
            var sim = FreeMapForAction();
            if (sim == null || sim.FlareOn) return false;
            Log($"照明（物資 −{FreeMapParams.FlareSupply}・ターンは使わない・{FreeMapParams.FlareTurns}ターンのあいだ見える）");
            RunFreeMapOp(s => s.UseFlare());
            OnNavFx?.Invoke(NavFx.Flare);
            return true;
        }

        /// <summary>囮罠を今いる点に仕掛ける (ターンは使わない・消耗品の消費は <see cref="Consumables.Use"/> が行う)。</summary>
        public bool TryPlaceLure()
        {
            var sim = FreeMapForAction();
            if (sim == null) return false;
            Log($"囮罠を仕掛けた（{FreeMapParams.LureLife}ターンのあいだ有効・踏んだ追跡者は{FreeMapParams.LureHoldTurns}ターン足止めされる）");
            NavDiag.NoteLure(false);   // [計装]
            RunFreeMapOp(s => s.PlaceLure());
            return true;
        }

        /// <summary>〈前哨の旗〉を今いる点に立てられるか (持っていて、 この層でまだ立てていない・航行中)。
        /// 層に 1 回なので「まだ拠点が無い」で見る (層の途中で旗を得た直後は、 回数が次の操作で入る)。</summary>
        public bool CanUseOutpostFlag
        {
            get
            {
                var sim = FreeMapForAction();
                return sim != null && NavItems.Has(Run, ItemIds.NavOutpostFlag) && sim.Beacon < 0;
            }
        }

        /// <summary>〈前哨の旗〉(層に 1 回・ターンは使わない): 今いる点に偵察拠点を置く。 その層にいる間、 拠点から
        /// <see cref="FreeMapPerks.beaconRange"/> 歩の点の種類と戦闘名・追跡者・使い魔を常に見せる。 人も BOT もここを叩く。</summary>
        public bool UseOutpostFlag()
        {
            var sim = FreeMapForAction();
            if (sim == null || !NavItems.Has(Run, ItemIds.NavOutpostFlag)) return false;
            bool ok = false;
            RunFreeMapOp(s => { ok = s.PlaceBeacon(); return FreeMapEvent.None; });
            if (ok) Log($"〈{ItemIds.NavOutpostFlag}〉を立てた（ターンは使わない・この層にいる間、 {sim.Perks.beaconRange}歩先まで見張る）");
            return ok;
        }

        /// <summary>ターンを進められる状態の中身 (マップ移動中・戦闘中でない)。 そうでなければ null。</summary>
        private FreeMapSim FreeMapForAction()
        {
            if (CurrentPhase != GamePhase.MapNavigation) return null;
            var sim = FreeMap;
            return sim == null || sim.Dead || sim.InFight ? null : sim;
        }

        /// <summary>操作を 1 回実行し、 起きたことを処理する。</summary>
        private FreeMapEvent RunFreeMapOp(System.Func<FreeMapSim, FreeMapEvent> op)
        {
            var sim = FreeMap;
            if (sim == null) return FreeMapEvent.None;
            sim.SetPerks(NavItems.PerksFor(Run));      // 層の途中で航行アイテムを得た・封じられた時も、 次の操作から効く
            var ev = op(sim);
            MapManager.Instance?.SyncFreeKnowledge();
            if (ev != FreeMapEvent.None) OnFreeMapEvent?.Invoke(ev);
            HandleFreeMapEvent(sim, ev);
            return ev;
        }

        private void HandleFreeMapEvent(FreeMapSim sim, FreeMapEvent ev)
        {
            if (ev == FreeMapEvent.None) return;
            var pf = sim.Profile;
            if ((ev & FreeMapEvent.SmokeUsed) != 0) Log($"〈{ItemIds.NavSmokeBall}〉が弾けた ── 追跡者は見失い、 最後に見た所を探している（この層ではもう使えない）");
            if ((ev & FreeMapEvent.Detected) != 0) Log($"〈{pf.displayName}〉に補足された");
            if ((ev & FreeMapEvent.FoeLost) != 0) Log($"〈{pf.displayName}〉は見失った");
            if ((ev & FreeMapEvent.FoeAlerted) != 0) Log("（追跡者が何かに気づいた）");
            if ((ev & FreeMapEvent.FamiliarLaunched) != 0) Log("使い魔が放たれた");
            if ((ev & FreeMapEvent.FamiliarVanished) != 0) Log("（使い魔は見失って消えた）");
            if ((ev & FreeMapEvent.LureTriggered) != 0) NavDiag.NoteLure(true);   // [計装]
            if ((ev & FreeMapEvent.LureTriggered) != 0) Log($"（囮罠が作動した）追跡者が{FreeMapParams.LureHoldTurns}ターン足止めされた");
            if ((ev & FreeMapEvent.FrontAdvanced) != 0 && sim.Weather != WeatherKind.None)
                Log($"前線が左から {System.Math.Min(sim.Front, FreeMapParams.Width):0.0} まで届いた（{sim.Weather.DisplayName()}）");
            if ((ev & FreeMapEvent.MapChanged) != 0) Log(sim.Weather == WeatherKind.Crumble ? "（道が1本崩れた）" : "（点が1つ沈んだ）");

            if ((ev & FreeMapEvent.Died) != 0)
            {
                Log("物資が尽き、歩き続けた末に倒れた");
                Run.EndRun();
                SetPhase(GamePhase.GameOver);
                OnGameOver?.Invoke(Run);
                return;
            }
            if (NavOnly)
            {
                // 航行だけを試す (2D 化の途中: 戦闘・イベント・ショップなど旧画面へは入らない)。 接触は無かったことにし、 着いた点は「到着」の記録だけ残す。
                if ((ev & FreeMapEvent.FamiliarContact) != 0)
                {
                    sim.ResolveFamiliarFight(false); MapManager.Instance?.SyncCurrentNodeFromSim(); Log("（航行のみ）使い魔に接触 ── 戦闘は省略した");
                    // 着いたターンに接触していても、 着いた点はそのまま (終点なら次の層へ進めるように、 到着の処理へ続ける)
                    if ((ev & FreeMapEvent.Arrived) == 0) return;
                    if ((ev & FreeMapEvent.Caught) == 0) sim.TakePendingArrival();
                }
                if ((ev & FreeMapEvent.Caught) != 0)
                {
                    if ((ev & FreeMapEvent.Arrived) != 0 && sim.LastArrived == sim.Layout.goal)
                    {
                        // 終点に着いたターンに捕まった: 戦闘は省いて勝ったことにし、 そのまま到着の処理へ (追い返され続けて終点へ入れなくなるのを防ぐ)
                        sim.ResolveBodyWon(); sim.TakePendingArrival(); Log("（航行のみ）終点で追跡者に捕まった ── 戦闘は省略し、 退けたことにした");
                    }
                    else { sim.ResolveBodyFled(); MapManager.Instance?.SyncCurrentNodeFromSim(); Log("（航行のみ）追跡者に捕まった ── 戦闘は省略し、 直前の点へ戻った"); return; }
                }
                if ((ev & FreeMapEvent.Arrived) != 0)
                {
                    var arrived = MapManager.Instance.NoteFreeArrival(sim.LastArrived);
                    if (arrived != null) Log($"（航行のみ）到着: {arrived.type}");
                    // 終点 (ボス / 〈門〉) に着いたら、 戦闘・エンディングを省いて次の層へ進む
                    if (arrived != null && (arrived.type == TileType.Boss || arrived.type == TileType.Gate)) AdvanceFloorNavOnly();
                }
                return;
            }
            if ((ev & FreeMapEvent.FamiliarContact) != 0) { StartFamiliarFight(); return; }
            if ((ev & FreeMapEvent.Caught) != 0) { BeginPursuerFight(); return; }
            if ((ev & FreeMapEvent.Arrived) != 0) ActivateArrival(sim.LastArrived);
        }

        /// <summary>航行だけを試す時 (NavOnly) の層移動: 終点に着いたら次の航行の層へ。 ボス戦・エンディングは省く。
        /// 次が航行マップを持たない層 (8 層 = ボス戦だけ) なら、 そこで止める。</summary>
        private void AdvanceFloorNavOnly()
        {
            int next = MapSystem.FreeMove.FloorPlan.Next(Run.Depth);
            if (next == Run.Depth || !MapSystem.FreeMove.FloorPlan.IsFreeMap(next))
            {
                Log($"（航行のみ）{Run.currentFloor}層の終点に着いた ── 最終層なのでここまで (ボス戦は未接続)");
                return;
            }
            Log($"（航行のみ）{Run.currentFloor}層の終点 → {MapSystem.FreeMove.FloorPlan.OrdinalOf(next)}層へ");
            Run.bossDefeatedThisFloor = true;
            SetPhase(GamePhase.FloorClear);
            ConfirmFloorClear();
        }

        /// <summary>着いた点を起動する。</summary>
        private void ActivateArrival(int index)
        {
            var node = MapManager.Instance.NoteFreeArrival(index);
            if (node == null) return;
            BeforeNodeEntered();
            AfterNodeEntered();
        }

        /// <summary>点に着いたターンに捕まっていたら、 戦闘が終わった今その点を発動させる。
        /// 放っておくと、 着いたのに起動しない点ができる。</summary>
        private void FlushPendingArrival()
        {
            var sim = FreeMap;
            if (CurrentPhase != GamePhase.MapNavigation || sim == null) return;
            int p = sim.TakePendingArrival();
            if (p >= 0) ActivateArrival(p);
        }

        /// <summary>開始時の囮罠 (<see cref="FreeMapParams.StartLures"/> 個)。 StartNewRun から呼ぶ。</summary>
        private void GrantStartingMapTools()
        {
            for (int i = 0; i < FreeMapParams.StartLures; i++) Run.TryAddConsumable(ItemIds.Lure);
        }

        // =====================================================================
        //  休憩 (多めに回復 = +1 ターン)
        // =====================================================================

        /// <summary>休憩で多めに回復する: 最大 HP の <see cref="FreeMapParams.RestMoreRatio"/>、 代わりに
        /// <see cref="FreeMapParams.RestMoreTurns"/> ターンが進む (追跡者が動く)。 通常の回復 (<see cref="RestHeal"/>) は 0 ターン。</summary>
        public void RestHealMore()
        {
            if (CurrentPhase != GamePhase.RestStop) return;
            int heal = Mathf.CeilToInt(Run.playerMaxHP * FreeMapParams.RestMoreRatio
                                       * MetaProgression.MetaDebuffApplicator.GetHealMultiplier());
            int before = Run.playerHP;
            Run.playerHP = Mathf.Min(Run.playerMaxHP, Run.playerHP + heal);
            RunChronicle.Rest(Run, Run.playerHP - before, before, Run.playerMaxHP);
            Log($"休憩（多めに回復）: +{Run.playerHP - before}HP（{FreeMapParams.RestMoreTurns}ターン）");

            // 休憩の画面を閉じてからターンを進める。 捕まれば戦闘へ、 そうでなければ地図 (ボス撃破後は層クリア) へ戻る。
            ReturnToMapOrClearFloor();
            if (CurrentPhase == GamePhase.MapNavigation && FreeMap != null)
                RunFreeMapOp(s => s.PassTurnsExternal(FreeMapParams.RestMoreTurns));
        }

        // =====================================================================
        //  追跡者との戦闘
        // =====================================================================

        /// <summary>追跡者の専用エネミーの中身 (性質ごと・2026-11-04)。
        /// 2026-11-12 (#35): 捕まった後の死亡 2.6% / 生還時の HP 65% (目標 15〜25% / 30〜40%) と楽すぎたので、 HP を各 +0.3・攻撃 ×1.25 (一段目)。 強さの土台は「その層の次の層のエリート」(7 層は 7 層自身) で、
        /// その上に性質ごとの型を足す。 数値は全部仮置き (§23-21・動かす時は採否を取ってから測定する)。</summary>
        private sealed class PursuerFight
        {
            public string name, description;
            /// <summary>本体の最大 HP の倍率。</summary>
            public float hpMul = 1f;
            /// <summary>本体の攻撃 (基礎攻撃と攻撃ロールの上限) の倍率 (2026-11-12・#35)。</summary>
            public float atkMul = 1f;
            /// <summary>2 体目: その層の通常の敵 1 体 (召喚者の使い魔)。</summary>
            public bool floorCompanion;
            /// <summary>2 体目: 猟犬 (本体を縮めた写し)。 HP・攻撃は本体に対する比。</summary>
            public bool hound;
            public float houndHp, houndAtk;
            /// <summary>先手: 最初の N ターンの攻撃倍率。</summary>
            public int openingTurns; public float openingMul;
            /// <summary>防御の下限と、 積み上がる装甲を足すか。</summary>
            public int minDefense; public bool stackingArmor;
        }

        private static PursuerFight PursuerFightOf(string profileId)
        {
            switch (profileId)
            {
                case "summoner": return new PursuerFight { name = "群れ呼び", description = "自分は動かず、手下を呼び寄せて戦わせる。呼ばれた一体と並んで立ちはだかる。", hpMul = 1.5f, atkMul = 1.25f, floorCompanion = true };
                case "hound":    return new PursuerFight { name = "猟犬使い", description = "足の速い猟犬を連れた狩人。獲物を追い詰めたら、猟犬と二人がかりで仕留める。", hpMul = 1.2f, atkMul = 1.25f, hound = true, houndHp = 0.4f, houndAtk = 0.7f };
                case "sprinter": return new PursuerFight { name = "影走り", description = "道を二歩ずつ駆ける追手。追いついた勢いのまま斬りかかるが、長くは保たない。", hpMul = 1.1f, atkMul = 1.25f, openingTurns = 2, openingMul = 1.5f };
                case "brute":    return new PursuerFight { name = "鉄塊の追手", description = "全身を鉄で覆った追手。歩みは遅いが、打ち合うほど鎧が厚くなる。", hpMul = 1.5f, atkMul = 1.25f, minDefense = 10, stackingArmor = true };
            }
            return new PursuerFight { name = null };
        }

        /// <summary>追跡者の専用エネミー。 性質によっては 2 体戦 (<paramref name="secondary"/>)。</summary>
        private EnemyData BuildPursuerEnemy(FreeMapSim sim, out EnemyData secondary)
        {
            secondary = null;
            // 追跡者はその層のエリートから引く (2026-11-05)。 旧: 次の層のエリート ── 2 層の死亡の約 6 割が追跡者戦で、
            //   層をまたいだ難度の階段の中でここだけ 1 段先を踏んでいた。
            int enemyFloor = Run.Depth;
            // 添字: 層ごとの捕まった回数 × 8。 ノードの添字 (NodeRngIndex) と重ならない帯へ置く。
            int idx = Run.Depth * 4096 + 2048 + _pursuerFightCount * 8;
            var template = FloorManager.PickEnemyForNode(enemyFloor, idx, elite: true);
            if (template == null) return null;
            var e = template.Clone();
            var f = PursuerFightOf(sim.Profile.id);
            if (GameHooks.PursuerAtkMulOverride > 0f) f.atkMul = GameHooks.PursuerAtkMulOverride;   // [測定 #35]
            f.hpMul += GameHooks.PursuerHpAdd;                                                     // [測定 #35]
            e.displayName = f.name ?? $"〈{sim.Profile.displayName}〉{template.displayName}";
            if (!string.IsNullOrEmpty(f.description)) e.description = f.description;
            e.maxHP = Mathf.Max(1, Mathf.RoundToInt(e.maxHP * f.hpMul));
            if (f.atkMul != 1f)
            {
                e.baseAttack = Mathf.Max(1, Mathf.RoundToInt(e.EffectiveBaseAttack * f.atkMul));
                if (e.attackRollMax > 0) e.attackRollMax = Mathf.Max(e.attackRollMin, Mathf.RoundToInt(e.attackRollMax * f.atkMul));
            }
            if (f.openingTurns > 0) { e.openingTurns = f.openingTurns; e.openingAttackMul = f.openingMul; }
            if (f.minDefense > 0) e.defense = Mathf.Max(e.defense, f.minDefense);
            if (f.stackingArmor && !HasPassive(e, "StackingArmor"))
            {
                if (e.passiveSkills == null) e.passiveSkills = new System.Collections.Generic.List<EnemyPassiveEntry>();
                e.passiveSkills.Insert(0, new EnemyPassiveEntry { internalName = "StackingArmor", skillName = "積み上がる装甲", description = "毎ターンの終わりに、防御+3（24まで）" });
            }
            if (f.floorCompanion)
            {
                var c = FloorManager.PickEnemyForNode(Run.Depth, idx + 1, elite: false);
                if (c != null) secondary = c.Clone();
            }
            else if (f.hound)
            {
                var h = template.Clone();
                h.displayName = "猟犬"; h.description = "狩人の連れた猟犬。噛みつきは鋭いが、体は軽い。";
                h.maxHP = Mathf.Max(1, Mathf.RoundToInt(e.maxHP * f.houndHp));
                h.baseAttack = Mathf.Max(1, Mathf.RoundToInt(h.EffectiveBaseAttack * f.houndAtk));
                if (h.attackRollMax > 0) h.attackRollMax = Mathf.Max(h.attackRollMin, Mathf.RoundToInt(h.attackRollMax * f.houndAtk));
                h.defense = 0; h.guardPeriod = 0; h.heavyPeriod = 0;
                h.passiveSkills = new System.Collections.Generic.List<EnemyPassiveEntry>();
                secondary = h;
            }
            // 〈追っ手殺しの刃〉: 2 体目 (手下・猟犬) は付かない ── 逃げる一択だった敵を狩る道 (nav-items.md の「連戦が 1 戦減る」の読み替え)
            if (NavItems.PursuerSolo(Run)) secondary = null;
            return e;
        }

        private static bool HasPassive(EnemyData e, string id)
        {
            if (e.passiveSkills == null) return false;
            foreach (var p in e.passiveSkills) if (p != null && p.internalName == id) return true;
            return false;
        }

        private void BeginPursuerFight()
        {
            var sim = FreeMap;
            _inPursuerFight = true;
            _pursuerFightCount++;
            var enemy = BuildPursuerEnemy(sim, out var secondEnemy);
            if (enemy == null)
            {
                Debug.LogError("[GameManager] 追跡者の敵の選出に失敗 ── 戦闘を打ち切る");
                _inPursuerFight = false;
                sim?.ResolveBodyWon();
                SetPhase(GamePhase.MapNavigation);
                return;
            }
            CurrentEnemy = enemy;
            CurrentEnemySecondary = secondEnemy;
            CurrentEncounterRewardMultiplier = 1f;
            NavDiag.Begin(sim?.Profile?.id, Run, sim != null && sim.Lures.Count > 0);   // [計装 2026-11-12・#35]
            OnEnemyEncountered?.Invoke(CurrentEnemy);
            Log($"追跡者に捕まった ── {CurrentEnemy.displayName}" + (CurrentEnemySecondary != null ? $" ＋ {CurrentEnemySecondary.displayName}" : ""));

            var (dc, dm, cr, df, ft_, str_) = GatherPlayerCombatStats();
            SetPhase(GamePhase.Combat);
            CombatManager.Instance.StartCombat(CurrentEnemy, Run.playerHP, dc, dm, cr, df, str_, ft_, CurrentEnemySecondary);
            CombatManager.Instance.FleeAllowed = true;
        }

        /// <summary>追跡者に勝った: 追跡者は倒れ、 物資 +300 とアイテム 1 個。</summary>
        private void HandlePursuerWon()
        {
            _inPursuerFight = false;
            NavDiag.End(NavDiag.Won, Run);   // [計装]
            FreeMap?.ResolveBodyWon();
            int before = Run.provision;
            int supply = GameHooks.PursuerSupplyOverride >= 0 ? GameHooks.PursuerSupplyOverride : NavItems.PursuerSupply(Run);   // [測定 #35]
            if (!GameHooks.PursuerNoReward) ProvisionSystem.Supply(Run, supply);      // 〈追っ手殺しの刃〉で ×1.5
            string pid = GameHooks.PursuerNoReward ? null : PickPassiveItemForBossExtra(false);
            if (!string.IsNullOrEmpty(pid)) GrantRewardItem(pid, eliteWin: true);
            Log($"追跡者を退けた。物資 +{Run.provision - before}" + (string.IsNullOrEmpty(pid) ? "" : $"、{pid}"));
            SetPhase(GamePhase.MapNavigation);
            FlushPendingArrival();
        }

        /// <summary>追跡者の戦闘から抜けた (逃げた・救済で生き延びた): 直前の点へ戻り、 追跡者は 2 ターン足止めされる。</summary>
        private void EndPursuerFightAsFled(bool payCost)
        {
            _inPursuerFight = false;
            NavDiag.End(payCost ? NavDiag.Fled : NavDiag.Rescued, Run);   // [計装] 逃げた (代償あり) / 救済 (代償なし)
            if (payCost) PayFleeCost();
            FreeMap?.ResolveBodyFled();
            MapManager.Instance?.SyncCurrentNodeFromSim();
            Log($"追跡者から逃げた。直前の点へ戻った（{FreeMapParams.FleeFreezeTurns}ターンは追ってこない）");
        }

        // =====================================================================
        //  逃げる
        // =====================================================================

        /// <summary>逃げるの代償: 物資 −100 ＋ ゴールド半減。 報酬は無い。</summary>
        private void PayFleeCost()
        {
            var (p, g) = ProvisionRules.ApplyFlee(Run.provision, Run.coins);
            g = NavItems.GoldAfterFlee(Run, Run.coins, g);      // 〈逃げ足の護符〉: 失うのは 1/4
            Log($"逃げた代償: 物資 {Run.provision}→{p}、ゴールド {Run.coins}→{g}");
            Run.provision = p;
            Run.coins = g;
        }

        // =====================================================================
        //  使い魔との戦闘
        // =====================================================================

        /// <summary>使い魔に接触した: その層の通常の戦闘相手 1 体との戦闘 (仮置き)。 逃げられる。</summary>
        private void StartFamiliarFight()
        {
            _inFamiliarFight = true;
            _familiarFightCount++;
            int idx = Run.Depth * 4096 + 3072 + _familiarFightCount;
            var enemy = FloorManager.PickEnemyForNode(Run.Depth, idx, elite: false)?.Clone();
            if (enemy == null)
            {
                Debug.LogError("[GameManager] 使い魔の敵の選出に失敗 ── 戦闘を打ち切る");
                EndFamiliarFight(fled: false);
                return;
            }
            enemy.displayName = $"使い魔（{enemy.displayName}）";
            CurrentEnemy = enemy;
            CurrentEnemySecondary = null;
            CurrentEncounterRewardMultiplier = 1f;
            OnEnemyEncountered?.Invoke(CurrentEnemy);
            Log($"使い魔に捕捉された: {CurrentEnemy.displayName}");
            var (dc, dm, cr, df, ft_, str_) = GatherPlayerCombatStats();
            SetPhase(GamePhase.Combat);
            CombatManager.Instance.StartCombat(CurrentEnemy, Run.playerHP, dc, dm, cr, df, str_, ft_);
            CombatManager.Instance.FleeAllowed = true;
        }

        /// <summary>使い魔との戦闘が終わった (勝っても逃げても使い魔は消える。 猟犬は本体の元へ戻る)。</summary>
        private void EndFamiliarFight(bool fled)
        {
            _inFamiliarFight = false;
            FreeMap?.ResolveFamiliarFight(fled);
            MapManager.Instance?.SyncCurrentNodeFromSim();
            SetPhase(GamePhase.MapNavigation);
            FlushPendingArrival();
        }

        /// <summary>使い魔に勝った: 通常の戦闘と同じ物資 (+30)。</summary>
        private void HandleFamiliarFightWon()
        {
            ProvisionSystem.Supply(Run, FreeMapParams.RewardBattle);
            Log("使い魔を退けた");
            EndFamiliarFight(fled: false);
        }

        /// <summary>戦闘から逃げた。 通常の戦闘なら直前の点へ戻り、 そのマスは使い切りにならない (敵が残る)。</summary>
        private void HandleFledBattle()
        {
            if (_inFamiliarFight)
            {
                PayFleeCost();
                Log("使い魔から逃げた");
                EndFamiliarFight(fled: true);
                return;
            }
            if (_inPursuerFight)
            {
                EndPursuerFightAsFled(payCost: true);
                SetPhase(GamePhase.MapNavigation);
                return;
            }
            PayFleeCost();
            var mm = MapManager.Instance;
            var node = mm?.CurrentNode;
            if (node != null) node.activated = false;   // 使い切りにならない
            var sim = FreeMap;
            if (sim != null)
            {
                sim.FleeToPrevious();
                mm.SyncCurrentNodeFromSim();
            }
            Log($"{CurrentEnemy?.displayName ?? "敵"}から逃げた。直前の点へ戻った（敵は残っている）");
            SetPhase(GamePhase.MapNavigation);
        }
    }
}
