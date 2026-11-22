using UnityEngine;
using GameLoop;
using ItemSystem.PassiveSkills.Keywords;

namespace ItemSystem.PassiveSkills.Effects
{
    // ============================================================
    //  軸なし汎用の 36 品 (2026-11-21・full-item-rework-ledger §⑩・docs/GAME.md §7-9)。 枠外 (動的 4 枠ロックの対象外)。
    //  効果クラスを持つのは戦闘の中で働く 13 品 (拳套・薬嚢・小瓶・短槍・崩し・楔・砂時計・気圧計・[剣の舞] 5 品)。
    //  戦闘の外だけで効く品 (探索・追跡者の 13 品・風見鶏・天幕・鑑定書・交換帳・止まらない時計・特殊戦術の 5 品) は持っているかを読む所が効かせる
    //  (〈古物商の交換帳〉= GameManager.ExchangeLedgerAxis)。 ここの先頭の Rw は全部の軸のファイルが使う道具。
    // ============================================================

    /// <summary>秘宝の効果で使う小さな道具 (全部の軸のファイルが使う)。</summary>
    internal static class Rw
    {
        public static GameLoop.RunState Run => GameLoop.GameManager.Instance?.Run;
        public static int Steps => Run?.stepsSinceBattle ?? 0;
        public static int Count(CombatContext ctx, string key) => (int)ctx.GetAccumulated(key);

        public static bool Won(CombatContext ctx) => ctx.enemyCurrentHP <= 0;

        /// <summary>プレイヤーの攻撃が通った (ダメージ後トリガーの中で使う)。</summary>
        public static bool PlayerHit(CombatContext ctx) => ctx.playerWonRoll && ctx.finalDamage > 0;

        public static void Gold(int n, string label)
        {
            var run = Run;
            if (run != null && n > 0) GameLoop.GoldIncome.Gain(run, n, label);
        }

        /// <summary>このターン 1 回だけ (key に今のターン番号を書く)。</summary>
        public static bool OncePerTurn(CombatContext ctx, string key)
        {
            if (Count(ctx, key) == ctx.currentTurn) return false;
            ctx.accumulatedValues[key] = ctx.currentTurn;
            return true;
        }

        /// <summary>この戦闘で 1 回だけ。</summary>
        public static bool OncePerBattle(CombatContext ctx, string key)
        {
            if (Count(ctx, key) > 0) return false;
            ctx.accumulatedValues[key] = 1;
            return true;
        }

        public static void Shield(CombatContext ctx, int n, string label)
        {
            if (n <= 0) return;
            ctx.consShield += n;
            CombatSystem.ShieldDiag.Note(label, n);
            ctx.shieldGainedTotal += n;
        }

        public static void Poison(CombatContext ctx, int n)
        {
            if (n > 0) ctx.AddStatus(StatusTarget.Enemy, "poison", n);
        }

        public static void Log(string s) => Debug.Log(s);

        /// <summary>プレイヤーの HP を n 回復する (戦闘中は CombatManager.HealPlayer ── 手元の HP と ctx の両方・回復を削るデバフも通す)。</summary>
        public static void Heal(CombatContext ctx, int n)
        {
            if (n <= 0) return;
            var cm = CombatSystem.CombatManager.Instance;
            if (cm != null && cm.IsCombatActive) cm.HealPlayer(n);
            else if (ctx != null) ctx.playerCurrentHP = System.Math.Min(ctx.playerMaxHP, ctx.playerCurrentHP + n);
        }

        /// <summary>次のターンに端子を n 枚多く引く (端子〈先読み〉と同じ口・TerminalBattleState.extraDrawNext)。</summary>
        public static void DrawNext(int n)
        {
            var deck = CombatSystem.CombatManager.Instance?.TerminalDeck;
            if (deck != null && n > 0) deck.state.extraDrawNext += n;
        }

        /// <summary>いまの敵がボスか (BossIds.IsBoss)。</summary>
        public static bool IsBoss()
        {
            var enemy = CombatSystem.CombatManager.Instance?.CurrentEnemy;
            return enemy != null && GameLoop.BossIds.IsBoss(enemy.id);
        }

        /// <summary>いまの戦闘がエリート戦かボス戦か (StatModifierEffect の strongFoe と同じ判定)。</summary>
        public static bool StrongFoe()
        {
            var type = MapSystem.MapManager.Instance?.CurrentNode?.type;
            return type == MapSystem.TileType.EliteBattle || type == MapSystem.TileType.Boss;
        }
    }

    // ============================================================
    //  多段・消耗品・ボスと大技・嵐
    // ============================================================

    /// <summary>加重の拳套: 同じターンに 2 撃目以降の攻撃が通るたび、 その撃の与ダメージ +20% (与ダメ % の池・その撃だけ・ctx.hitOutgoingPct)。</summary>
    public class WeightingGauntlet : IPassiveSkillEffect, IRelicDisplay
    {
        public const string Id = "加重の拳套";
        public string SkillId => Id;
        public const int Pct = 20;
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(ctx.hitCount >= 2);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (ctx.IsRealHit && ctx.hitIndex >= 1) ctx.hitOutgoingPct += Pct;
        }
    }

    /// <summary>帯革の薬嚢: 戦闘中に消耗品を使うたび、 端子を 1 枚引いて手札に加え、 ブロック +6 (このターン・ctx.itemBlockBonus)。</summary>
    public class BeltPouch : IPassiveSkillEffect
    {
        public string SkillId => ItemIds.LinkPouch;
        public const int Draw = 1, Block = 6;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnConsumableUse };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            var dk = CombatSystem.CombatManager.Instance?.TerminalDeck;
            int got = dk != null ? dk.DrawIntoHand(Draw) : 0;
            ctx.itemBlockBonus += Block;
            Rw.Log($"[帯革の薬嚢] 消耗品を使った → 端子 {got} 枚引く・ブロック +{Block}");
        }
    }

    /// <summary>底抜けの小瓶: 戦闘で最初に使った消耗品は消費されず、 その戦闘中にもう 1 回使える (1 戦闘 1 回)。
    /// もう 1 回使わなければ、 戦闘の終わりに消える (<see cref="Consumables.Consume"/>)。 使う時の判定は <see cref="Consumables.Use"/> が <see cref="TryKeep"/> で聞く。</summary>
    public class BottomlessVial : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => ItemIds.LinkBottomless;
        /// <summary>[persistent] 0 = まだ / 1 = 最初の品を残している / 2 = 使い終わった。</summary>
        private const string StateKey = "link.bottomless.state";
        /// <summary>残している品 (戦闘の間だけ・<see cref="_ctx"/> の戦闘のもの)。</summary>
        private static string _keptId;
        private static CombatContext _ctx;

        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Once(Rw.Count(ctx, StateKey) >= 2, true);

        /// <summary>消耗品を使った直後 (効果が出た後・所持から減らす前): 消費しないなら true。 <paramref name="ctx"/> が null (戦闘の外) なら false。</summary>
        public static bool TryKeep(CombatContext ctx, RunState run, string id)
        {
            if (ctx == null || !AxisRules.Owns(run, ItemIds.LinkBottomless)) return false;
            int st = Rw.Count(ctx, StateKey);
            if (st == 0)
            {
                ctx.accumulatedValues[StateKey] = 1; _keptId = id; _ctx = ctx;
                Rw.Log($"[底抜けの小瓶] 〈{id}〉は消費しない (この戦闘中にもう 1 回使える)");
                return true;
            }
            if (st == 1 && ReferenceEquals(_ctx, ctx) && id == _keptId) { ctx.accumulatedValues[StateKey] = 2; _keptId = null; _ctx = null; }
            return false;
        }

        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (Rw.Count(ctx, StateKey) == 1 && ReferenceEquals(_ctx, ctx) && _keptId != null)
            {
                Consumables.Consume(Rw.Run, _keptId);
                Rw.Log($"[底抜けの小瓶] もう 1 回使わなかった〈{_keptId}〉は戦闘の終わりに消える");
            }
            _keptId = null; _ctx = null;
        }
    }

    /// <summary>隙衝きの短槍: 敵がチャージ中 (大技の前で攻撃しない) かダウン中のターン、 攻撃の与ダメージ +40% (与ダメ % の池・撃ごとに乗る)。</summary>
    public class GapSpear : IPassiveSkillEffect
    {
        public string SkillId => ItemIds.TacGapSpear;
        public const float Pct = 0.40f;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            var cm = CombatSystem.CombatManager.Instance;
            if (cm == null || !cm.EnemyChargingOrDownNow(ctx.currentTurn)) return;
            ctx.AddOutgoingPct(Pct);
            Rw.Log("[隙衝きの短槍] 敵がチャージ中かダウン中 → 与ダメージ +40%");
        }
    }

    /// <summary>崩しの一手: ボスの目盛りを割ってダウンさせたターン (通常攻撃でもひびでも・1 ターンに 1 回)、 次のターンの手札 +2 枚・次のターンの全撃の基礎攻撃 +6 (攻撃 +N・撃ごとに乗る)。</summary>
    public class BreakingMove : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => ItemIds.TacBreak;
        public const int Draw = 2, Atk = 6;
        /// <summary>[persistent] ダウンさせたターンの番号 (次のターンに読む)。</summary>
        private const string Key = "tac.break.turn";
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.On(Shd.LastTurn(ctx, Key));
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBossDown, PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnPostRoll) { if (Shd.LastTurn(ctx, Key)) ctx.AddPlayerAttackOrDiceBonus(Atk); return; }
            if (!Rw.OncePerTurn(ctx, Key)) return;
            Rw.DrawNext(Draw);
            Rw.Log($"[崩しの一手] 目盛りを割った → 次のターン 手札 +{Draw}・全撃 +{Atk}");
        }
    }

    /// <summary>大技殺しの楔: ダウンか〈封じ〉で敵の大技を取り消したとき (OnHeavyCanceled)、 敵に最大 HP の 10% の軽減不可ダメージ・次のターンのブロック +15
    /// (取り消したターンは敵が攻撃しないので次のターン・<see cref="CombatSystem.YachtRoleEffects.BlockNextKey"/>)。</summary>
    public class HeavyBreakerWedge : IPassiveSkillEffect
    {
        public string SkillId => ItemIds.TacWedge;
        public const int MaxHpPct = 10, Block = 15;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnHeavyCanceled };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            int dmg = (int)((long)ctx.enemyMaxHP * MaxHpPct / 100);
            Kw.DamageEnemy(ctx, dmg);
            CombatSystem.YachtRoleEffects.AddNextTurn(ctx, CombatSystem.YachtRoleEffects.BlockNextKey, Block);
            Rw.Log($"[大技殺しの楔] 大技を取り消した → 軽減不可 {dmg}・次のターン ブロック +{Block}");
        }
    }

    /// <summary>逆境の砂時計: エスカレーションの段階が 2 以上の間、 ターン開始時にブロック +4 (このターン)・攻撃の与ダメージ +25% (与ダメ % の池)。
    /// 段階は〈鎮静香〉の遅延と段階の戻しを差し引いた今の段階 (CombatManager.EscalationStageNow・予告に出る段階)。</summary>
    public class AdversityHourglass : IPassiveSkillEffect
    {
        public string SkillId => ItemIds.TacHourglass;
        public const int NeedStage = 2, Block = 4;
        public const float Pct = 0.25f;
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart, PassiveSkillTrigger.OnWired };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            var cm = CombatSystem.CombatManager.Instance;
            if (cm == null || cm.EscalationStageNow(ctx) < NeedStage) return;
            if (t == PassiveSkillTrigger.OnTurnStart) ctx.itemBlockBonus += Block;
            else ctx.AddOutgoingPct(Pct);
        }
    }

    /// <summary>気圧計のペンダント: 戦闘開始時の嵐の段 1 につき、 シールド +5・この戦闘の攻撃 +2 (段 3 ならシールド +15・攻撃 +6)。 段は戦闘開始時に読んで、 戦闘中は変えない。
    /// 開幕のシールドは台帳 §⑩ が「開幕バリアとして維持可」とした形のまま。</summary>
    public class MacroBarometer : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => ItemIds.MacroBarometer;
        public const int ShieldPerPhase = 5, AttackPerPhase = 2;
        /// <summary>[persistent] 戦闘開始時の嵐の段。</summary>
        private const string PhaseKey = "macro.barometer.phase";
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Count(ctx != null ? Rw.Count(ctx, PhaseKey) : GameManager.Instance?.FreeMap?.StormPhase ?? 0);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart)
            {
                int ph = GameManager.Instance?.FreeMap?.StormPhase ?? 0;
                ctx.accumulatedValues[PhaseKey] = ph;
                if (ph <= 0) return;
                Rw.Shield(ctx, ShieldPerPhase * ph, SkillId);
                Rw.Log($"[気圧計のペンダント] 嵐の段 {ph}: シールド +{ShieldPerPhase * ph}・攻撃 +{AttackPerPhase * ph}");
                return;
            }
            int n = Rw.Count(ctx, PhaseKey);
            if (n > 0) ctx.AddPlayerAttackOrDiceBonus(AttackPerPhase * n);
        }
    }

    // ============================================================
    //  [剣の舞] (現行維持・台帳 §⑩)
    // ============================================================

    /// <summary>サーベル・ワルツ (BRONZE): ダイス合計+1。
    /// 他の[剣の舞]がインベントリにも昇華にも存在しないとき、戦闘開始時にHPを半減（孤剣のリスク）。
    /// ショップでの[剣の舞]出現率上昇は ShopManager 側のフックで処理。</summary>
    public class SaberWaltz : IPassiveSkillEffect
    {
        public string SkillId => "サーベル・ワルツ";
        public PassiveSkillTrigger[] Triggers => new[]
        {
            PassiveSkillTrigger.OnBattleStart,
            PassiveSkillTrigger.OnPostRoll,
        };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            // [撤去 2026-09-14] 孤剣ペナルティ (戦闘開始HP -10%)。
            //   4 枚とも LEGENDARY へ揃えた結果、 1 枚目が「10G のデメリット付き」になり
            //   BOT の購入率が 16.6% ── 棚に 0.61 枚/ラン 出ているのに取得は 0.10 枚/ラン で、
            //   <b>揃える前に見送られて集約が 0%</b> になっていた。
            //   「揃えば強い、 単体でも損しない」へ寄せる。 集めに行く動機はここで作る。
            //   (2026-09-05 に HP半減 → -10% へ緩和した経緯があり、 今回で全廃。)
            if (trigger == PassiveSkillTrigger.OnBattleStart) return;
            // OnPostRoll: 与ダメージ +10% (2026-11-10・旧 攻撃に挿した出目 +10%・その前は 攻撃+1)。 [剣の舞] 2 枚で 1.5 倍・3 枚で 2 倍 (2026-11-20)
            ctx.AddOutgoingPct(0.1f * GameLoop.SwordDanceSet.SoloScale(GameLoop.GameManager.Instance?.Run));
        }
    }

    /// <summary>エスパーダ・パソドブレ (SILVER): 自分と敵のダイス合計に+5。
    /// 与えるダメージ+20%（outgoing）／受けるダメージ+20%（OnPreReceiveで×1.2）。
    /// 自他+5でダイス差は不変だが、合計値を読む効果（血令/断罪の天秤/万華 等）と高め合う両刃。</summary>
    public class EspadaPasodoble : IPassiveSkillEffect
    {
        public string SkillId => "エスパーダ・パソドブレ";
        public PassiveSkillTrigger[] Triggers => new[]
        {
            PassiveSkillTrigger.OnPostRoll,
            PassiveSkillTrigger.OnPreReceiveDamage,
        };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (trigger == PassiveSkillTrigger.OnPostRoll)
            {
                // 旧: 自他ダイス合計+5 (差分不変で「合計」参照系と相乗) / 新: 自攻撃+5 のみ
                // (敵ダイス合計+5 は新モデルではロール勝負がないため機能喪失 = 廃止)
                // 与ダメージ +40% (2026-11-10: 旧 出目 +50% と与ダメージ +20% を 1 本にまとめた・毎ターンリセット→再適用)。
                //   [剣の舞] 2 枚で 1.5 倍・3 枚で 2 倍 (2026-11-20・受けるダメージ +20% は伸ばさない)
                ctx.AddOutgoingPct(0.4f * GameLoop.SwordDanceSet.SoloScale(GameLoop.GameManager.Instance?.Run));
                return;
            }
            // OnPreReceiveDamage: 被ダメ+20%（毎被弾イベントで都度乗る＝累積しない）
            if (ctx.finalDamage > 0)
                ctx.finalDamage = UnityEngine.Mathf.CeilToInt(ctx.finalDamage * 1.2f);
        }
    }

    /// <summary>フルーレ・バレエ (BRONZE): ダイス合計+3。
    /// 「戦闘に敗北したとき、このアイテムを廃棄し最大HPを1にして生還」は救済チェーン
    /// (GameLoop.LastStand.TryConsumeRevival) 側で処理する。ここでは火力部分のみ。</summary>
    public class FleuretBallet : IPassiveSkillEffect
    {
        public string SkillId => "フルーレ・バレエ";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            // 与ダメージ +25% (2026-11-10・旧 攻撃に挿した出目 +30%・その前は 攻撃+3)。 [剣の舞] 2 枚で 1.5 倍・3 枚で 2 倍 (2026-11-20)
            ctx.AddOutgoingPct(0.25f * GameLoop.SwordDanceSet.SoloScale(GameLoop.GameManager.Instance?.Run));
        }
    }

    /// <summary>ファコン・タンゴ (LEGENDARY): 2026-06-20 効果変更。
    /// 攻撃+2。 <b>2026-09-14: 孤剣ペナルティ (戦闘開始時 最大HP-1) を撤去。</b></summary>
    public class FalconTango : IPassiveSkillEffect
    {
        public string SkillId => "ファコン・タンゴ";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };

        /// <summary>攻撃加算量。 <b>[撤去 2026-09-14] 孤剣ペナルティ (戦闘開始時 最大HP-1)。</b>
        ///
        /// <para>この札は<b>効果がペナルティだけ</b>だったので、 単に外すと効果ゼロの札になる。
        /// 10G の LEGENDARY が無効果では買われず、 ペナルティを外した意味が消えるため、
        /// セット共通の書式 (ワルツ +1 / バレエ +3 / パソドブレ +5) に合わせて最小限の上振れを持たせる。
        /// <b>この値だけが今回の追加分</b>なので、 不要ならここを 0 にすれば元の「無効果」に戻る。</para></summary>
        public const float OutgoingPct = 0.2f;   // 与ダメージ +20% (2026-11-10・旧 攻撃に挿した出目 +20%・その前は 攻撃+2)

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (trigger != PassiveSkillTrigger.OnPostRoll) return;
            ctx.AddOutgoingPct(OutgoingPct * GameLoop.SwordDanceSet.SoloScale(GameLoop.GameManager.Instance?.Run));   // [剣の舞] 2 枚で 1.5 倍・3 枚で 2 倍 (2026-11-20)
        }
    }

    /// <summary>無銘の剣舞譜 (特殊・4枚集約の変化先): 戦闘突入ごとに[剣先]スタック+1（最大99・ラン中持続）。
    /// ダイス合計に剣先スタック分を加算。与ダメージ時に剣先スタック分HP回復、被ダメージ時に剣先スタック分の
    /// 軽減不可ダメージを相手へ。スタックはランを跨がず IRunResettable でリセット。</summary>
    public class BladeDance : IPassiveSkillEffect, IRelicDisplay, IRunResettable, IRunStatePersistent
    {
        public string SkillId => "無銘の剣舞譜";
        /// <summary>秘宝の列 (#320) の見え方。</summary>
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Count(kensaki);
        private int kensaki = 0; // 剣先スタック（ラン中持続）

        public void ResetRunState() { kensaki = 0; }

        // 中断セーブ (issue #84): 剣先スタックはラン中ずっと持つので、 中断して再開しても続きから数える
        public string CaptureRunState() => kensaki.ToString(System.Globalization.CultureInfo.InvariantCulture);
        public bool RestoreRunState(string state)
        {
            if (!int.TryParse(state, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int v)) return false;
            kensaki = System.Math.Max(0, System.Math.Min(99, v));
            return true;
        }

        /// <summary>4枚集約による BD 取得時の初期スタック付与 (現在層 × 5・2026-11-15 にコードに合わせてコメントを直した)。
        /// 取得が後半層に偏る構造のため、 集約成功への報酬として「現時点の層数 × 5」 をプリロードする。
        /// 既に kensaki > seed の場合は上書きしない (後から再取得→上書きで減少を防ぐ)。</summary>
        public void SeedOnAcquire(int floor)
        {
            int seed = System.Math.Min(99, System.Math.Max(0, floor) * 5);
            if (seed > kensaki) kensaki = seed;
            if (UnityEngine.Debug.unityLogger.logEnabled) UnityEngine.Debug.Log($"[無銘の剣舞譜] 取得時シード: floor={floor} → kensaki={kensaki}");
        }

        public PassiveSkillTrigger[] Triggers => new[]
        {
            PassiveSkillTrigger.OnBattleStart,
            PassiveSkillTrigger.OnPostRoll,
            PassiveSkillTrigger.OnPostDealDamage,
            PassiveSkillTrigger.OnPostReceiveDamage,
        };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            switch (trigger)
            {
                case PassiveSkillTrigger.OnBattleStart:
                    kensaki = System.Math.Min(99, kensaki + 1);
                    if (UnityEngine.Debug.unityLogger.logEnabled) UnityEngine.Debug.Log($"[無銘の剣舞譜] 戦闘突入: 剣先スタック → {kensaki}");
                    break;
                case PassiveSkillTrigger.OnPostRoll:
                    // 旧: ダイス合計+剣先 / 新: 攻撃+剣先
                    if (kensaki > 0) ctx.AddPlayerAttackOrDiceBonus(kensaki);
                    break;
                case PassiveSkillTrigger.OnPostDealDamage:
                    // 与ダメージ時: 剣先スタック分HP回復
                    if (kensaki > 0 && ctx.finalDamage > 0)
                        ctx.playerCurrentHP = System.Math.Min(ctx.playerMaxHP, ctx.playerCurrentHP + kensaki);
                    break;
                case PassiveSkillTrigger.OnPostReceiveDamage:
                    // 被ダメージ時: 剣先スタック分の軽減不可ダメージを相手へ
                    if (kensaki > 0 && ctx.finalDamage > 0)
                        ctx.fixedDamageToEnemy += kensaki;
                    break;
            }
        }
    }
}
