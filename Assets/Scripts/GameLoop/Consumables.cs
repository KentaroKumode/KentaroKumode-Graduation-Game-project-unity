using UnityEngine;
using CombatSystem;
using ItemSystem;
using ItemSystem.PassiveSkills;

namespace GameLoop
{
    /// <summary>
    /// RunState に対する消費アイテム使用処理（マップ進行モデル / 戦闘内 両対応）。
    ///
    /// ・戦闘外で使用 → 即時系はその場適用、戦闘バフ系は RunState.pending* に保持
    ///   （次戦闘開始時に CombatManager が ctx へコピー）。
    /// ・戦闘中で使用 → 戦闘バフ系は現在の CombatContext へ即時書き込み（使用ターンから発動）。
    /// 戦闘終了で ctx は破棄されるため効果は自動消滅。
    ///
    /// id 体系: cons_<family>_<tier1-4> / uniq_<name>
    /// </summary>
    public static class Consumables
    {
        /// <summary>この戦闘で使った消耗品の数 (挑戦カード「1 戦闘で使える消耗品は N 個まで」・戦闘ごとに作り直す CombatContext の一時値)。</summary>
        public const string BattleUseKey = "chal_battleUse";
        /// <summary>古い手紙を使ったターンの印 (ctx.accumulatedValues)。 CombatManager が敵攻撃の解決で読んで消す。</summary>
        public const string OldLetterGuardKey = "oldLetterGuard";

        private static CombatContext ActiveCtx()
        {
            var cm = CombatManager.Instance;
            if (cm == null || !cm.IsCombatActive) return null;
            return PassiveSkillManager.Instance?.Context;
        }

        /// <summary>〈シュヴァリエのレイピア〉のコントラタックが立っている (ctx の印・1/0)。</summary>
        public const string ContreKey = "player_contre";
        /// <summary>コントラタックを解いた次のターンの番号 (そのターンの攻撃は会心確定・剣聖撃破済みなら会心倍率 +1.0)。</summary>
        public const string RapierReleaseTurnKey = "rapier_release_turn";
        /// <summary>解いた次のターンの会心倍率の上乗せ (剣聖〈サン=ジョリオラ〉撃破済みのランだけ)。 / 手札の上乗せ。</summary>
        public const float RapierReleaseCritMul = 1.0f;
        public const int RapierReleaseDraw = 2;

        /// <summary>シュヴァリエのレイピア: 戦闘中に使用するとコントラタックを切替（消費されない）。2026-11-20 作り直し。
        /// 発動中: 攻撃の端子に挿せない (TerminalEnv.noAttack) 代わり、 ブロックとシールドで防いだダメージの 2 倍を敵へ軽減不可で返す (CombatManager.ApplyLossDamageModifiers)。
        /// 解除: 次のターン 手札 +2 枚・そのターンの攻撃は会心確定 (剣聖撃破済みなら会心倍率 +1.0・<see cref="ApplyRapierRelease"/>)。
        /// 旧: 発動中 1d1 強制 (実装は振り直しを止めるだけ)・被ダメ 50% 軽減・軽減 ×2 反射 / 解除 次のターン ダイス +1 (未実装)・剣聖撃破済みなら会心率 +50%。
        /// パッシブアイテム所持が条件。</summary>
        public static bool TryUseRapier(RunState run)
        {
            if (run == null || run.ownedPassiveItems == null
                || !run.ownedPassiveItems.Contains(ItemIds.ChevalierRapier)) return false;
            var ctx = ActiveCtx();
            if (ctx == null) { Debug.Log("[レイピア] 戦闘外では使用不可"); return false; }
            if (ctx.consumablesLocked) { Debug.Log("[レイピア] 死翔により使用不可"); return false; }


            if (ctx.GetAccumulated(ContreKey) <= 0)
            {
                ctx.accumulatedValues[ContreKey] = 1;
                Debug.Log("[レイピア] コントラタック発動: 攻撃の端子に挿せない / ブロックとシールドで防いだダメージ ×2 を返す");
            }
            else
            {
                ctx.accumulatedValues[ContreKey] = 0;
                ctx.accumulatedValues[RapierReleaseTurnKey] = ctx.currentTurn + 1;
                var deck = CombatManager.Instance?.TerminalDeck;
                if (deck != null) deck.state.extraDrawNext += RapierReleaseDraw;
                if (Debug.unityLogger.logEnabled) Debug.Log($"[レイピア] コントラタック解除: 次のターン 手札 +{RapierReleaseDraw}・攻撃は会心確定"
                        + (run.defeatedSaintGeorges ? $"・会心倍率 +{RapierReleaseCritMul:0.0} (真の決闘術)" : ""));
            }
            return true;
        }

        /// <summary>ターンの頭 (BeginNewTurn の後) に呼ぶ: コントラタックを解いた次のターンなら、 そのターンの攻撃を会心確定にし、
        /// 剣聖撃破済みのランなら会心倍率 +1.0 (どちらも BeginNewTurn が毎ターン戻す値なので、 次のターンには残らない)。</summary>
        public static void ApplyRapierRelease(CombatContext ctx)
        {
            if (ctx == null || (int)ctx.GetAccumulated(RapierReleaseTurnKey) != ctx.currentTurn || ctx.currentTurn <= 0) return;
            ctx.forceCritical = true;
            if (GameManager.Instance?.Run?.defeatedSaintGeorges ?? false) ctx.criticalMultiplier += RapierReleaseCritMul;
        }

        /// <summary>[計装 2026-09-14] ヴェスカ連戦の<b>段別</b>消耗品使用数
        /// (index 0=p1 / 1=p2 / 2=p3 / 3=p4)。
        ///
        /// <para><b>「p1〜p3 は致命傷にならないだけで、 消耗はさせているのか」を分けるため。</b>
        /// 段の勝率 (p1 100% / p2 99.9% / p3 98.6%) だけを見ると何もしていないように読めるが、
        /// 通貨は HP と消耗品なので、 そこを数えないと結論が出せない。</para></summary>
        public static readonly long[] VescaPhaseConsumables = new long[4];
        public static void ResetVescaPhaseStats()
        { System.Array.Clear(VescaPhaseConsumables, 0, VescaPhaseConsumables.Length); }

        private static void NoteVescaPhaseUse()
        {
            var c = ActiveCtx();
            string b = c != null ? c.bossId : null;
            if (string.IsNullOrEmpty(b) || !b.StartsWith(BossIds.Layer7Prefix)) return;
            int i = System.Math.Max(1, BossIds.VescaStageOf(b)) - 1;
            VescaPhaseConsumables[i]++;
        }

        /// <summary>所持から id を1つ消費し効果適用。意味があった場合 true。
        /// <paramref name="target"/> = 対象を取る品 (〈複製の水銀〉) の対象 (場の添字・−1 = 選ばない → 左端の手札の端子)。
        /// 戦闘中に使えたら出来事 <see cref="PassiveSkillTrigger.OnConsumableUse"/> を配る (量 = Tier・Tier の無い品は 1)。</summary>
        public static bool Use(RunState run, string id, int target = -1)
        {
            if (run == null || string.IsNullOrEmpty(id)) return false;
            if (run.ownedConsumables == null || !run.ownedConsumables.Contains(id)) return false;
            // 精鋭ハーピィ「死翔」: この戦闘中は消費アイテム使用不可
            var lockCtx = ActiveCtx();
            if (lockCtx != null && lockCtx.consumablesLocked)
            {
                Debug.Log("[Consumables] 死翔により使用不可");
                return false;
            }

            // 挑戦カード: 1 戦闘で使える消耗品は N 個まで (ヴェスカ連戦は 1 戦闘として数える)
            int useCap = MetaProgression.MetaDebuffApplicator.GetBattleConsumableCap();
            if (useCap > 0 && lockCtx != null && lockCtx.GetAccumulated(BattleUseKey) >= useCap)
            {
                if (Debug.unityLogger.logEnabled) Debug.Log($"[Consumables] この戦闘ではもう使えない ({useCap} 個まで)");
                return false;
            }

            bool ok = Apply(run, id, target);
            if (ok)
            {
                if (useCap > 0 && lockCtx != null) lockCtx.accumulatedValues[BattleUseKey] = lockCtx.GetAccumulated(BattleUseKey) + 1f;
                // 〈底抜けの小瓶〉(2026-11-20): 戦闘で最初に使った消耗品は消費しない (その戦闘中にもう 1 回使える・使わなければ戦闘の終わりに消える)
                if (!ItemSystem.PassiveSkills.Effects.BottomlessVial.TryKeep(lockCtx, run, id)) Consume(run, id);
                MetaProgression.Achievements.AchievementService.NoteConsumableUsed();
                NoteVescaPhaseUse();
                if (Debug.unityLogger.logEnabled) Debug.Log($"[Consumables] 使用: {id}");
                // 消耗品の連動品の出来事 (2026-11-20・item-overhaul-spec §2-2)。 戦闘中だけ
                if (lockCtx != null) ItemSystem.PassiveSkills.Keywords.Kw.Raise(lockCtx, PassiveSkillTrigger.OnConsumableUse, System.Math.Max(1, ItemIds.ConsTierOf(id)));
            }
            return ok;
        }

        /// <summary>使った品を所持から 1 つ減らす。 古い手紙は 1 枚で OldLetterUses 回使える (使い切ったときだけ手放す)。
        /// 〈底抜けの小瓶〉が戦闘の終わりに、 もう 1 回使わなかった品を消すのもここ。</summary>
        public static void Consume(RunState run, string id)
        {
            if (run?.ownedConsumables == null || string.IsNullOrEmpty(id)) return;
            bool keep = false;
            if (id == ItemIds.OldLetter)
            {
                run.oldLetterUsesSpent++;
                keep = run.oldLetterUsesSpent < ItemIds.OldLetterUses;
                if (!keep) run.oldLetterUsesSpent = 0;
            }
            if (!keep) run.ownedConsumables.Remove(id);
        }

        /// <summary>今 <see cref="Use"/> を呼んだら使えるか (使えれば null・使えなければ理由の短い文)。 画面が品を暗く出すための<b>読むだけ</b>の判定で、
        /// 状態を変えない。 条件は <see cref="Use"/> と <see cref="Apply"/> が false を返す所の写し ── Apply の分岐を変えたらここも直す。
        /// 使うのは必ず <see cref="Use"/> (人の画面も BOT も同じ関数)。 ここが null でも Use が false を返すことはある (その時は消費しない)。
        /// <para>使えても効き目が無い場面 (〈狂暴化〉で回復が封じられている時の回復薬など) は、 Use が消費するので「使える」と答える。</para></summary>
        public static string BlockReason(RunState run, string id)
        {
            if (run == null || string.IsNullOrEmpty(id)) return "使えない";
            if (run.ownedConsumables == null || !run.ownedConsumables.Contains(id)) return "持っていない";
            var ctx = ActiveCtx();
            if (ctx != null && ctx.consumablesLocked) return "この戦闘では消耗品を使えない";
            int useCap = MetaProgression.MetaDebuffApplicator.GetBattleConsumableCap();
            if (useCap > 0 && ctx != null && ctx.GetAccumulated(BattleUseKey) >= useCap) return "この戦闘ではもう使えない (" + useCap + " 個まで)";

            if (ItemIds.ConsFamilyOf(id) == ItemIds.ConsLureFamily)
            {
                if (ctx != null) return "戦闘中は使えない";
                var gm = GameManager.Instance;
                return gm != null && gm.CanPlaceLure ? null : "航行中だけ置ける";
            }
            if (ItemIds.ConsFamilyOf(id) == ItemIds.ConsTacticFamily) return TacticBlockReason(ctx, id);
            if (HealRatio(id) > 0f)
            {
                var cm = CombatManager.Instance;
                bool full = ctx != null ? (cm == null || cm.PlayerHP >= cm.PlayerMaxHP) : run.playerHP >= run.playerMaxHP;
                return full ? "HP が満ちている" : null;
            }
            // シールド・攻撃強化の系統は、 戦闘の中でも外でも必ず効く (外なら次の戦闘の開幕へ持ち越す)。 系統で読む ── id の綴りは見ない
            string fam = ItemIds.ConsFamilyOf(id);
            if (fam == ItemIds.ConsShieldFamily || fam == ItemIds.ConsPowerFamily) return null;
            switch (id)
            {
                case ClassStarter.PolishId: case ClassStarter.OathId: case ClassStarter.DaggerId:
                    return null;
                case ItemIds.OldLetter:
                case ClassStarter.PainkillerId:
                    return ctx != null ? null : "戦闘中だけ使える";
                case ItemIds.PhilStone:
                    return run.philStoneUsed >= 5 ? "もう使えない (ランで 5 回まで)" : null;
                default:
                    return "使えない";
            }
        }

        /// <summary>今 <see cref="Use"/> で使えるか (<see cref="BlockReason"/> が null)。</summary>
        public static bool CanUse(RunState run, string id) => BlockReason(run, id) == null;

        /// <summary>〈シュヴァリエのレイピア〉を今 <see cref="TryUseRapier"/> で切り替えられるか (持っていて・戦闘中で・死翔で封じられていない)。</summary>
        public static bool CanUseRapier(RunState run)
        {
            if (run?.ownedPassiveItems == null || !run.ownedPassiveItems.Contains(ItemIds.ChevalierRapier)) return false;
            var ctx = ActiveCtx();
            return ctx != null && !ctx.consumablesLocked;
        }

        /// <summary>〈シュヴァリエのレイピア〉のコントラタックが立っているか (戦闘中だけ意味がある)。</summary>
        public static bool RapierContreOn() => RapierContreOn(ActiveCtx());

        /// <summary>この ctx でコントラタックが立っているか。</summary>
        public static bool RapierContreOn(CombatContext ctx) => ctx != null && ctx.GetAccumulated(ContreKey) > 0;

        /// <summary>効果適用本体（リスト削除はしない）。</summary>
        private static bool Apply(RunState run, string id, int target = -1)
        {
            var ctx = ActiveCtx();

            // 航行マップで使う消耗品 (2026-09-28)。 **系統で判定する** ── id を綴りで読まない。
            //   戦闘中・航行マップでない層では使えない (false = 消費しない)。
            string mapFamily = ItemIds.ConsFamilyOf(id);
            if (mapFamily == ItemIds.ConsLureFamily) return GameManager.Instance != null && GameManager.Instance.TryPlaceLure();
            if (mapFamily == ItemIds.ConsTacticFamily) return TacticBlockReason(ctx, id) == null && ApplyTactic(ctx, id, target);

            // 回復: 最大HP割合の即時回復。 割合の表は HealRatio の 1 か所だけ (BOT の選び方と共有)。
            float healRatio = HealRatio(id);
            if (healRatio > 0f) return HealPct(run, ctx, healRatio);

            switch (id)
            {
                // ===== 3 系統 × Tier1〜4 (2026-08-04 再編。 詳細と経緯は ItemIds.ConsHealFamily 近傍) =====
                //   **Tier 軸は効果量のみ。持続は全 Tier「その戦闘中」で固定。**
                //   旧設計は「効果量↓ × 持続↑」だったため実効量が戦闘の長さで逆転し、
                //   通常戦では高 Tier ほど弱いという状態になっていた。

                // シールド: 戦闘中持続 (expireTurn=-1)。 **戦闘中にも使用できる** ──
                //   通常戦が 3〜4T になり「削られてから張る」判断が成立するようになったため。
                //   量の表は ShieldAmount の 1 か所だけ (BOT の見積もりと共有)。
                case ItemIds.Talisman1:
                case ItemIds.Talisman2:
                case ItemIds.Talisman3:
                case ItemIds.Talisman4: return AddShield(run, ctx, ShieldAmount(id));

                // 攻撃強化: 最終与ダメージ +X% (その戦闘中・2026-11-25 に与ダメの池から最終へ移した・CombatManager.Damage)。 旧: outgoingDamageMultiplier は**加算プール**
                //   (実測 ×2.00) なので、 +75% でも最終与ダメは ×1.375 に薄まる。
                case ItemIds.PowerOil1: return AddTimedDmgMult(run, ctx, 15, -1);
                case ItemIds.PowerOil2: return AddTimedDmgMult(run, ctx, 30, -1);
                case ItemIds.PowerOil3: return AddTimedDmgMult(run, ctx, 50, -1);
                case ItemIds.PowerOil4: return AddTimedDmgMult(run, ctx, 75, -1);

                // 古い手紙 (2026-10-31): 次のターンの被ダメを 0 にする。 戦闘中だけ使える。
                //   仕込み刃と同じ位置 (CombatManager の敵攻撃の解決) で消す。 反撃は無い。
                //   旧効果の物資回復は、 物資回復の系統ごと廃止した (物資はショップの包で買う)。
                case ItemIds.OldLetter:
                    if (ctx == null) { Debug.Log("[古い手紙] 戦闘外では使用不可"); return false; }
                    ctx.accumulatedValues[OldLetterGuardKey] = 1;
                    return true;

                // ===== 賢者の石: 10ゴールドを武器強化素材1に変換 (最大5回/ラン) =====
                case ItemIds.PhilStone:
                {
                    if (run.philStoneUsed >= 5) return false;
                    run.weaponMaterials++; run.philStoneUsed++;
                    return true;
                }

                // 2026-06-28: 職業スターター消耗品 4 種 (剣士/騎士/狂戦士/暗殺者)
                case ClassStarter.PolishId: // 瞬間研磨剤 (剣士): 次の 1 撃だけ 与ダメージ+150%
                    if (ctx != null) ctx.polishArmed = true;
                    else run.pendingPolishArmed = true;
                    return true;

                case ClassStarter.OathId: // 不抜の聖紋 (騎士): HP 80% 維持中 被ダメ -40% (戦闘内のみ意味あり)
                    if (ctx != null)
                    {
                        // 戦闘中使用なら直ちに装着。 現 HP が既に 80% 未満なら oathBroken で実質無効
                        ctx.oathArmed = true;
                        if (ctx.playerMaxHP > 0 && ctx.playerCurrentHP * 5 < ctx.playerMaxHP * 4)
                            ctx.oathBroken = true;
                    }
                    else run.pendingOathArmed = true;
                    return true;

                case ClassStarter.PainkillerId: // 痛覚遮断剤 (狂戦士): 戦闘中使用専用
                    if (ctx == null) { Debug.Log("[痛覚遮断剤] 戦闘外では使用不可"); return false; }
                    ctx.painkillerArmedThisTurn = true;
                    return true;

                case ClassStarter.DaggerId: // 仕込み刃 (暗殺者): このターンの被ダメ 0 + 5 倍を軽減不可で反撃
                    if (ctx != null) ctx.daggerArmed = true;
                    else run.pendingDaggerArmed = true;
                    return true;

                default:
                    return false;
            }
        }

        // ===== 戦術消耗品 (2026-11-20・item-overhaul-spec §7-1・数値は仮) =====
        //   どれも戦闘中・判断の間 (振り直し・配線) だけ使える。 効き目が無い時 (同じターンに 2 つ目の潤滑油など) は使えない (消費しない)。
        //   端子に効く物は TerminalBattleState の旗 (本体・画面・BOT の写しが同じ値を読む)・手札を動かす物は TerminalDeckState を直に動かす。

        /// <summary>〈鎧割りの酸瓶〉: 敵の防御から引く量 (この戦闘中・下限 0)。</summary>
        public const int AcidDefenseCut = 12;
        /// <summary>〈目盛り削りの鑿〉: 敵へのダメージ。</summary>
        public const int ChiselDamage = 15;
        /// <summary>〈複製の水銀〉: 手札に足す写しの枚数。</summary>
        public const int MercuryCopies = 2;

        /// <summary>戦術消耗品の一覧 (items.json の consFamily = <see cref="ItemIds.ConsTacticFamily"/>・レア度が <paramref name="maxRarity"/> 以下・items.json の並び順)。
        /// 店の 3 枠目 (ShopManager) と〈行商人の見本箱〉が引く。</summary>
        public static System.Collections.Generic.List<CompleteItemData> TacticPool(ItemRarity maxRarity = ItemRarity.LEGENDARY)
        {
            var all = ItemDatabase.Instance?.GetItemsByCategory(ItemCategory.Consumable);
            var l = new System.Collections.Generic.List<CompleteItemData>();
            if (all != null) foreach (var it in all) if (it.consFamily == ItemIds.ConsTacticFamily && it.rarity <= maxRarity) l.Add(it);
            return l;
        }

        /// <summary>〈複製の水銀〉の対象: 場の添字 <paramref name="want"/> が手札の端子ならそれ、 でなければ左端の手札の端子 (−1 = 手札が無い)。</summary>
        public static int MercuryTarget(TerminalDeckState dk, int want)
        {
            if (dk == null || dk.hand.Count == 0) return -1;
            int p = dk.permanent.Count;
            return want >= p && want < dk.board.Count ? want : p;
        }

        /// <summary>戦術消耗品を今使えるか (使えれば null・使えなければ理由)。 <see cref="ApplyTactic"/> と同じ条件。</summary>
        private static string TacticBlockReason(CombatContext ctx, string id)
        {
            var cm = CombatManager.Instance;
            var dk = cm?.TerminalDeck;
            if (ctx == null || cm == null || !cm.IsCombatActive) return "戦闘中だけ使える";
            switch (id)
            {
                case ItemIds.TacticSwap: return dk == null || dk.hand.Count == 0 ? "手札が無い" : null;
                case ItemIds.TacticOil: return dk == null ? "使えない" : dk.state.ignoreCond ? "このターンはもう効いている" : null;
                case ItemIds.TacticOrb: return dk == null ? "使えない" : dk.state.doubleAll ? "このターンはもう効いている" : null;
                case ItemIds.TacticMercury: return dk == null || dk.hand.Count == 0 ? "写す端子が無い" : null;
                case ItemIds.TacticNeedle: return ctx.GetAccumulated(CombatManager.TacticNeedleKey) > 0f ? "もう仕込んである" : null;
                case ItemIds.TacticChisel: return cm.EnemyHP <= 0 ? "敵が倒れている" : null;
                case ItemIds.TacticAcid: case ItemIds.TacticSand: return null;
                default: return "使えない";
            }
        }

        private static bool ApplyTactic(CombatContext ctx, string id, int target)
        {
            var cm = CombatManager.Instance; var dk = cm.TerminalDeck;
            switch (id)
            {
                case ItemIds.TacticSwap:
                    if (Debug.unityLogger.logEnabled) Debug.Log($"[入れ替えの符] 手札を捨てて {dk.SwapHand()} 枚引き直した");
                    return true;
                case ItemIds.TacticOil:
                    dk.state.ignoreCond = true;      // 〈無形の構え〉の「条件を無視」と同じ旗 (このターンだけ・BeginTurn が戻す)
                    return true;
                case ItemIds.TacticOrb:
                    dk.state.doubleAll = true;
                    return true;
                case ItemIds.TacticAcid:
                {
                    int before = ctx.enemyDefense;
                    ctx.enemyDefense = Mathf.Max(0, ctx.enemyDefense - AcidDefenseCut);
                    ctx.accumulatedValues[CombatManager.TacticAcidKey] = ctx.GetAccumulated(CombatManager.TacticAcidKey) + 1f;
                    if (Debug.unityLogger.logEnabled) Debug.Log($"[鎧割りの酸瓶] 敵の防御 {before} → {ctx.enemyDefense} (この戦闘中)");
                    return true;
                }
                case ItemIds.TacticNeedle:
                    ctx.accumulatedValues[CombatManager.TacticNeedleKey] = 1f;      // 撃のループ (CombatManager.Turn) が攻撃のある最初の撃で使う
                    return true;
                case ItemIds.TacticChisel:
                    return cm.TacticChisel(ctx, ChiselDamage);
                case ItemIds.TacticSand:
                {
                    // 〈氷結瓶〉と同じ口 (エスカレーションの遅延)。 段階 0 で戻せない時は、 次のターンに段階が進まないよう 1 ターン遅らせる
                    int t = YachtRoleEffects.TurnsToRetreatOneStage(ctx);
                    YachtRoleEffects.AddEscalationDelay(ctx, t > 0 ? t : 1);
                    Debug.Log(t > 0 ? "[反転の砂] エスカレーションの段階を 1 つ戻す" : "[反転の砂] 段階 0: 次のターンは段階が進まない");
                    return true;
                }
                case ItemIds.TacticMercury:
                {
                    int s = MercuryTarget(dk, target);
                    if (s < 0) return false;
                    string copied = dk.board[s];
                    dk.AddToHand(copied, MercuryCopies);
                    if (Debug.unityLogger.logEnabled) Debug.Log($"[複製の水銀] 〈{copied}〉の写しを {MercuryCopies} 枚手札に (この戦闘の間だけ)");
                    return true;
                }
                default: return false;
            }
        }

        // ===== 効果適用ヘルパー =====

        private static bool HealPct(RunState run, CombatContext ctx, float pct)
        {
            // T4-A〈破綻〉: 全ての回復量 −25% (§15-2 v3.0)。
            pct *= MetaProgression.MetaDebuffApplicator.GetHealMultiplier();
            if (ctx != null)
            {
                var cm = CombatManager.Instance;
                if (cm == null || cm.PlayerHP >= cm.PlayerMaxHP) return false;
                int amt = Mathf.CeilToInt(cm.PlayerMaxHP * pct);
                cm.HealPlayer(amt);   // playerHP/ctx 双方を更新
                return true;
            }
            if (run.playerHP >= run.playerMaxHP) return false;
            int a = Mathf.CeilToInt(run.playerMaxHP * pct);
            int before = run.playerHP;
            run.playerHP = Mathf.Min(run.playerMaxHP, run.playerHP + a);
            return true;
        }

        /// <summary>与ダメ倍率バフ (cons_dmg_*)。 outgoing +pct% を turns ターン持続 (-1=戦闘中永続)。</summary>
        private static bool AddTimedDmgMult(RunState run, CombatContext ctx, int pct, int turns)
        {
            if (ctx != null)
            {
                ctx.consDmgMultPct += pct;
                if (turns == -1 || ctx.consDmgMultTurnsLeft == -1) ctx.consDmgMultTurnsLeft = -1;
                else ctx.consDmgMultTurnsLeft = Mathf.Max(ctx.consDmgMultTurnsLeft, turns);
            }
            else if (run != null)
            {
                run.pendingConsDmgMultPct += pct;
                if (turns == -1 || run.pendingConsDmgMultTurns == -1) run.pendingConsDmgMultTurns = -1;
                else run.pendingConsDmgMultTurns = Mathf.Max(run.pendingConsDmgMultTurns, turns);
            }
            return true;
        }

        /// <summary>開幕シールド (cons_def_*)。 戦闘中持続 (expireTurn=-1)。</summary>
        private static bool AddShield(RunState run, CombatContext ctx, int amount)
        {
            if (ctx != null)
            {
                int gained = Mathf.Max(0, amount);
                ctx.consShield += gained;
                CombatSystem.ShieldDiag.Note("消耗品", gained);
                ctx.shieldGainedTotal += gained;
                ctx.consShieldExpireTurn = -1;
            }
            else if (run != null)
            {
                run.pendingConsShield += amount;
                run.pendingConsShieldTurns = -1;
            }
            return true;
        }

        // durationBattles: dice/crit/reduce が次戦闘以降も持続する戦闘数。 1=次戦闘1回のみ（既定）、 2=次戦闘から2回。
        // 戦闘中使用時は当戦闘も1戦としてカウント＝持ち越し戦闘数は durationBattles-1。

        // 2026-08-04: RestoreProvision は cons_food_* 専用だったため、 同系統の廃止とあわせて削除。

        // ===== ボット支援: 種別判定 =====

        /// <summary>id の即時回復割合（heal系以外は0）。 <b>回復薬の効果量の正本</b> (Apply もここを引く)。
        /// 値は float の定数のまま返す (実行時に割り算で作らない・Mono の float 精度の罠)。</summary>
        public static float HealRatio(string id)
        {
            switch (id)
            {
                case ItemIds.HealPotion1: return 0.25f;
                case ItemIds.HealPotion2: return 0.40f;
                case ItemIds.HealPotion3: return 0.60f;
                case ItemIds.HealPotion4: return 1.00f;
                default: return 0f;
            }
        }

        /// <summary>護符のシールド量 (護符以外は 0)。 <b>護符の効果量の正本</b> (Apply もここを引く・BOT の見積もりと共有)。</summary>
        public static int ShieldAmount(string id)
        {
            switch (id)
            {
                case ItemIds.Talisman1: return 15;
                case ItemIds.Talisman2: return 30;
                case ItemIds.Talisman3: return 50;
                case ItemIds.Talisman4: return 80;
                default: return 0;
            }
        }

        /// <summary>
        /// HP割合が urgencyRatio 以下なら、不足分を最も無駄なく埋める即時回復を1個使う。
        /// 過剰回復を避け、不足を満たせない場合は最大の回復を使う。使ったら true。
        /// </summary>
        public static bool TryUseBestHeal(RunState run, float urgencyRatio)
        {
            if (run?.ownedConsumables == null || run.ownedConsumables.Count == 0) return false;
            if (run.playerMaxHP <= 0) return false;

            float ratio = (float)run.playerHP / run.playerMaxHP;
            if (ratio > urgencyRatio || ratio >= 1f) return false;

            float missingFrac = 1f - ratio;
            string bestCover = null; float bestCoverR = 99f;
            string bestAny = null;   float bestAnyR = -1f;
            foreach (var id in run.ownedConsumables)
            {
                float r = HealRatio(id);
                if (r <= 0f) continue;
                if (r >= missingFrac && r < bestCoverR) { bestCover = id; bestCoverR = r; }
                if (r > bestAnyR) { bestAny = id; bestAnyR = r; }
            }
            string pick = bestCover ?? bestAny;
            return pick != null && Use(run, pick);
        }

        // 物資を回復する消耗品は 2026-10-31 に廃止 (2 度目)。 物資はショップの「物資の包」で買う。
    }
}
