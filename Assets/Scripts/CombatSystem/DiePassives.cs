using System.Collections.Generic;
using GameLoop;
using ItemSystem;

namespace CombatSystem
{
    /// <summary>
    /// 装備中の賽のパッシブ (ADR-0015・2026-11-18)。 <b>賽は 1 個で毎ターン 3 回振る</b>ので、 目 1 つごとに効くパッシブは 1 ターンに最大 3 回効く。
    ///
    /// <para><b>挿した目に効く物</b> (錆釘・鈍色・逆目・秤・渡し守・夜番・大技読み・残り火) は <see cref="ApplyToTerminals"/>。
    /// <see cref="TerminalCards.Resolve"/> の最後の段で呼ぶので、 本体・BOT の見積もり・画面の見込みが同じ値になる (盤面は <see cref="TerminalEnv"/>)。</para>
    ///
    /// <para><b>戦闘の他の所で効く物</b>: 拾い物・古銭 = <see cref="OnTurnEnd"/> (ターンの終わり)・古銭の〈癒し〉+3 = YachtRoleEffects (<see cref="RoleHealBonus"/>)・賭場 = 振り直しの費用 (CombatManager.RerollCostNow) と HP の支払い・
    /// 墓標 = HP 0 からの踏みとどまり (CombatManager.TryLastStandRevive)。</para>
    ///
    /// <para><b>ほかの所にある賽</b>: 目利き = 振り直し (CombatManager.RerollPhase・残す目)・逆巻き = エスカレーションの段階 (ApplyRewind・Escalation.StageOffset)・
    /// 繋ぎ = 残す印 (TerminalDeckState.KeepFromMarks)・時計 = 常設の〈時計〉の端子 (TerminalIds.Clock)・
    /// 連なり = 役〈連なり〉(YachtRoles / YachtRoleEffects)。 呪物 = 店以外の秘宝の提示を呪いの LEGENDARY に置き換える (<see cref="MaybeCurse"/>) と
    /// 呪いの秘宝を手に入れた時の最大 HP (<see cref="OnPassiveAcquired"/>)。 効果の数値はすべて仮の値 (items.json の説明文と同じ)。</para>
    ///
    /// <para><b>出目は書き換えない</b> (ADR-0015 の 2)。 ここは出目が決まって端子に挿された後の上乗せだけ。</para>
    /// </summary>
    public static class DiePassives
    {
        /// <summary>錆釘: 攻撃に挿した目がこれ以上なら攻撃 +1。</summary>
        public const int RustyNailMin = 5;
        /// <summary>渡し守: 振り直した後の目を攻撃に挿すと攻撃 +N。</summary>
        public const int FerryAttack = 2;
        /// <summary>夜番: 防御に挿した目が敵の出目の最大より大きければシールド +目 × N (2026-11-20: 旧 +3 固定)。</summary>
        public const int NightWatchShieldPerPip = 2;
        /// <summary>秤: 目がほかの 2 つの目の間の値なら、 挿した先に +N (2026-11-20: 旧 +1)。</summary>
        public const int ScaleBonus = 3;
        /// <summary>残り火: 挿さずに捨てた目の何倍を次のターンの攻撃に足すか。</summary>
        public const int EmberMul = 2;
        /// <summary>拾い物: ①が出たターンの終わりにゴールド +N (2026-11-21 台帳 §⑪: 旧 1)。</summary>
        public const int ScavengerGold = 2;
        /// <summary>鈍色: 偶数を防御に挿すと防御 +N・奇数を攻撃に挿すと攻撃 +N (2026-11-21 台帳 §⑪: 旧 1)。</summary>
        public const int DullBonus = 2;
        /// <summary>古銭: ⑧が出るたびゴールド +N (2026-11-21 Phase 4 較正: 台帳 §⑪ の 4 から 2 へ戻す)。</summary>
        public const int OldCoinGold = 2;
        /// <summary>古銭: そのターンの役〈癒し〉(⑧の役) の回復量 +N (YachtRoleEffects の〈癒し〉が読む・1 回働くごと)。</summary>
        public const int OldCoinHeal = 1;
        public const int OldCoinFace = 8;
        /// <summary>呪物の賽 (2026-11-25 作者の決定・旧 2026-11-20 の LEGENDARY の重み ×3 と「LEGENDARY を手に入れるたび −5」は撤去):
        /// 店以外で決まった秘宝が GOLD 以下なら、 確率「その場所の LEGENDARY の出やすさ × これ」で呪いの LEGENDARY に変わる。</summary>
        public const float CursedChanceMul = 2f;
        /// <summary>呪物の賽: 呪いの秘宝を手に入れた時に減る最大 HP (このランの間・1 より下にはならない)。 ふつうに出た LEGENDARY は減らない。</summary>
        public const int CursedMaxHp = 5;
        /// <summary>提示の行に出す呪いの印と、 その説明 (報酬・品を選ぶ・開幕の秘宝の窓)。</summary>
        public const string CurseMark = "呪い";
        public static readonly string CurseNote = "呪い: 手に入れると最大 HP −" + CursedMaxHp + "（このランの間）。";

        /// <summary>役〈癒し〉の回復量に足す分 (古銭の賽なら <see cref="OldCoinHeal"/>・それ以外 0)。</summary>
        public static int RoleHealBonus(RunState run) => EquippedDie.IdOf(run) == DieIds.OldCoin ? OldCoinHeal : 0;

        /// <summary>その品が今、 呪いの付いた提示か (提示の窓・BOT の値踏みが読む)。</summary>
        public static bool IsCursedOffer(RunState run, string itemId)
            => run?.cursedOfferIds != null && !string.IsNullOrEmpty(itemId) && run.cursedOfferIds.Contains(itemId);

        /// <summary>呪物の賽の置き換え (2026-11-25)。 店以外で秘宝の提示が 1 つ決まった後に呼ぶ。 <paramref name="itemId"/> = ふつうに決まった品。
        /// 呪物の賽を装備していて、 決まった秘宝が GOLD 以下なら、 確率 <see cref="CursedChanceMul"/> × <paramref name="legendaryChance"/> で
        /// <paramref name="pool"/> の LEGENDARY の秘宝 (<paramref name="alsoExclude"/> と同じ提示の品を除く) に替え、 呪いの印を付けて返す。
        /// 乱数は専用のキー (<paramref name="key"/>.curse / .curse.pick) だけ ── 呪物の賽でない時は引かない (ほかの抽選の列は動かない)。</summary>
        public static string MaybeCurse(RunState run, string itemId, List<CompleteItemData> pool, float legendaryChance, string key, ICollection<string> alsoExclude = null)
        {
            if (run == null || string.IsNullOrEmpty(itemId)) return itemId;
            if (run.cursedOfferIds == null) run.cursedOfferIds = new HashSet<string>();
            run.cursedOfferIds.Remove(itemId);   // 前の提示で呪いが付いて見送られた品が、 今度はふつうに出た
            if (EquippedDie.IdOf(run) != DieIds.Cursed || pool == null) return itemId;
            var d = ItemDatabase.Instance?.GetItem(itemId);
            if (d == null || !d.IsPassive || d.rarity > ItemRarity.GOLD) return itemId;
            if (GameRng.Value(key + ".curse") >= CursedChanceMul * legendaryChance) return itemId;
            var legs = pool.FindAll(it => it != null && it.IsPassive && it.rarity == ItemRarity.LEGENDARY && it.internalName != itemId
                                          && (alsoExclude == null || !alsoExclude.Contains(it.internalName)));
            if (legs.Count == 0) return itemId;
            string got = legs[GameRng.RangeAuto(key + ".curse.pick", 0, legs.Count)].internalName;
            run.cursedOfferIds.Add(got);
            if (UnityEngine.Debug.unityLogger.logEnabled) UnityEngine.Debug.Log($"[賽] 呪物の賽: {itemId} → 呪いの {got}");
            return got;
        }

        /// <summary>秘宝 (category Passive) を手に入れた (PassiveAddHelper.AddPassiveItem)。 呪いの付いた提示 (<see cref="IsCursedOffer"/>) なら最大 HP −<see cref="CursedMaxHp"/>。</summary>
        public static void OnPassiveAcquired(RunState run, string itemId)
        {
            if (run?.cursedOfferIds == null || !run.cursedOfferIds.Remove(itemId)) return;
            run.playerMaxHP = System.Math.Max(1, run.playerMaxHP - CursedMaxHp);
            if (run.playerHP > run.playerMaxHP) run.playerHP = run.playerMaxHP;
            if (UnityEngine.Debug.unityLogger.logEnabled) UnityEngine.Debug.Log($"[賽] 呪物の賽: 呪いの秘宝 {itemId} を手に入れた → 最大 HP {run.playerMaxHP}");
        }

        /// <summary>端子の結果へ賽のパッシブを足す。 <paramref name="v"/> は端子に入る目 (〈号令の剣〉の +1 の後)。</summary>
        public static void ApplyToTerminals(TerminalEnv env, TerminalBattleState state, TerminalOutcome o, TerminalDef[] defs, int[] v)
        {
            string die = env.dieId;
            if (string.IsNullOrEmpty(die) || o == null || v == null) return;
            int n = v.Length;

            // 残り火: 前のターンに捨てた分を、 このターン攻撃に挿した目があれば足す。 持ち越すのは 1 ターン分だけ
            if (die == DieIds.Ember && state != null)
            {
                if (o.attackDice > 0) o.attackAdd += state.emberCarry;
                state.emberCarry = 0;
                int next = 0;
                for (int i = 0; i < n; i++) if (o.slotOfDie[i] < 0) next += EmberMul * System.Math.Max(0, v[i]);
                state.emberNext = next;
                return;
            }

            for (int i = 0; i < n; i++)
            {
                int s = o.slotOfDie[i];
                if (s < 0 || s >= defs.Length || defs[s] == null) continue;
                var k = defs[s].kind;
                int val = v[i];
                switch (die)
                {
                    case DieIds.RustyNail:
                        if (k == TerminalKind.Attack && val >= RustyNailMin) o.attackAdd += 1;
                        break;
                    case DieIds.Dull:
                        if (val % 2 == 0 && k == TerminalKind.Block) o.blockAdd += DullBonus;
                        else if (val % 2 != 0 && k == TerminalKind.Attack) o.attackAdd += DullBonus;
                        break;
                    case DieIds.Reverse:
                        if (k == TerminalKind.Attack) o.attackAdd += System.Math.Max(0, (7 - val) / 2);
                        break;
                    case DieIds.Scale:
                        if (n == 3 && Between(v, i)) AddByKind(o, k, ScaleBonus);
                        break;
                    case DieIds.Ferry:
                        if (env.diceRerolled && k == TerminalKind.Attack) o.attackAdd += FerryAttack;
                        break;
                    case DieIds.NightWatch:
                        if (k == TerminalKind.Block && val > env.enemyMaxDie) o.shield += val * NightWatchShieldPerPip;
                        break;
                    case DieIds.HeavyRead:
                        if (env.heavyTurn) AddByKind(o, k, val / 2);
                        break;
                }
            }
        }

        /// <summary>目 i が、 ほかの 2 つの目の間 (両端を含まない) にあるか。</summary>
        private static bool Between(int[] v, int i)
        {
            int lo = int.MaxValue, hi = int.MinValue;
            for (int j = 0; j < v.Length; j++)
            {
                if (j == i) continue;
                if (v[j] < lo) lo = v[j];
                if (v[j] > hi) hi = v[j];
            }
            return lo < v[i] && v[i] < hi;
        }

        /// <summary>挿した端子の種類の量へ足す (攻撃・防御・充電。 補助の端子には足さない)。</summary>
        private static void AddByKind(TerminalOutcome o, TerminalKind k, int add)
        {
            if (add <= 0) return;
            switch (k)
            {
                case TerminalKind.Attack: o.attackAdd += add; break;
                case TerminalKind.Block: o.blockAdd += add; break;
                case TerminalKind.Charge: o.chargeAdd += add; break;
            }
        }

        /// <summary>ターンの終わり: 拾い物 (①が出た) と古銭 (⑧が出た数) のゴールド。 <paramref name="dice"/> = このターンの出目。</summary>
        public static void OnTurnEnd(RunState run, int[] dice, bool enemyKilled)
        {
            if (run == null || dice == null) return;
            string die = EquippedDie.IdOf(run);
            if (die == DieIds.Scavenger && Has(dice, 1))
            {
                GoldIncome.Gain(run, ScavengerGold, "拾い物の賽");
                if (UnityEngine.Debug.unityLogger.logEnabled) UnityEngine.Debug.Log($"[賽] 拾い物の賽: ①が出た → +{ScavengerGold}G");
            }
            else if (die == DieIds.OldCoin)
            {
                int eights = 0;
                foreach (int d in dice) if (d == OldCoinFace) eights++;
                if (eights > 0)
                {
                    GoldIncome.Gain(run, OldCoinGold * eights, "古銭の賽");
                    if (UnityEngine.Debug.unityLogger.logEnabled) UnityEngine.Debug.Log($"[賽] 古銭の賽: ⑧ × {eights} → +{OldCoinGold * eights}G");
                }
            }
        }

        private static bool Has(int[] dice, int face)
        {
            for (int i = 0; i < dice.Length; i++) if (dice[i] == face) return true;
            return false;
        }

        /// <summary>逆巻きの賽: 攻撃の種類の端子に入った目がこれ以上のターンに発動する。</summary>
        public const int RewindNeed = 3;

        /// <summary>逆巻きの賽 (作者 2026-11-18): 攻撃に目を 3 つ挿したターン、 エスカレーションの段階を 1 つ下げる (2 → 1・0 → −1)。
        /// エスカレーションはふつうに進む (ターンの閾値で 1 つ上がる)。 段階がマイナスの間は、 自分の与ダメに同じ段の倍率を足す (−1 で +30%)。
        /// 敵の攻撃に効くのは次のターンから (このターンの予告はもう決まっている)。 配線の結果が決まった直後・主攻撃より前に呼ぶ。</summary>
        public static void ApplyRewind(ItemSystem.PassiveSkills.CombatContext ctx, TerminalOutcome term, int escTurn)
        {
            if (ctx == null || term == null || EquippedDie.IdOf(GameManager.Instance?.Run) != DieIds.Rewind) return;
            if (term.attackDice >= RewindNeed && Escalation.EffectiveStage(escTurn) > Escalation.MinStage)
            {
                Escalation.StageOffset++;
                if (UnityEngine.Debug.unityLogger.logEnabled) UnityEngine.Debug.Log($"[賽] 逆巻きの賽: 段階を 1 つ下げた → {Escalation.EffectiveStage(escTurn)}");
            }
            int s = Escalation.EffectiveStage(escTurn);
            if (s < 0) ctx.outgoingDamageMultiplier += Escalation.NegativeStageBuff(-s);
        }

        /// <summary>賭場の賽: 振り直しが無料 (費用 0)・代わりに振り直すたびに HP −N。</summary>
        public const int GambleHpCost = 1;
        public static bool FreeRerollByDie(RunState run) => EquippedDie.IdOf(run) == DieIds.Gamble;
    }
}
