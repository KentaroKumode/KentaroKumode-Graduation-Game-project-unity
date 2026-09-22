using System.Collections.Generic;
// ItemRarity は namespace InventorySystem 直下 (フォルダは Items/ だが名前空間は分かれていない)。
// 本ファイルは InventorySystem.PassiveSkills.Effects なので、 親名前空間として解決される。

namespace InventorySystem.PassiveSkills.Effects
{
    /// <summary>
    /// 7 層 ヴェスカ（遺物学者）の固有パッシブと遺物プール。 正本: docs/GAME.md §13-4 / §19-4。
    ///
    /// 設計の核:
    ///   - 彼は特別なボス数値を持たない。 プレイヤーと同じ機構を **より上の等級の遺物で** 使う。
    ///   - 毎ターン開始時にプールから抽選し、 予告 (§6-3 段2) で開示する。
    ///     開示があるからこそ「殴らないことが正解のターン」が成立する (§6-2 完全情報)。
    ///   - MYTHIC は items.json に存在しない。 プレイヤーには永久に入手不能。
    ///     (DIVINE は 2026-08-16 に廃止 ── VescaRelicPool.CapForStage の doc を参照)
    /// </summary>
    /// <remarks>
    /// **視点反転に注意。** これらは敵パッシブなので `PassiveSkillManager.FireEnemyTrigger` が
    /// `SwapPerspective()` を掛けた状態で走る。 実行中の対応は:
    ///
    ///   ヴェスカ自身の HP  = `ctx.playerCurrentHP` / `ctx.playerMaxHP`
    ///   プレイヤーの HP    = `ctx.enemyCurrentHP`  / `ctx.enemyMaxHP`
    ///   ヴェスカが受ける固定ダメ = `ctx.fixedDamageToPlayer`
    ///   プレイヤーへの固定ダメ   = `ctx.fixedDamageToEnemy`
    ///   敵視点 OnRollWin = プレイヤーの収支マイナス / OnRollLose = プレイヤーの収支プラス
    ///
    /// 入れ替わるのは HP・ダイス・ダイス合計・固定ダメ・勝敗フラグの 5 組のみ。
    /// `enemyBleedStacks` / `enemyStatusStacks` / `rinkaiMeter` / 充電 / `accumulatedValues` /
    /// 本ファイルで足した vesca* フィールドは **反転しない**（名前どおりの意味のまま）。
    /// </remarks>
    public sealed class VescaRelic
    {
        public string name;
        public ItemRarity rarity;
        public string role;                              // 攻撃 / 防御 / 妨害
        public System.Action<CombatContext> apply;
    }

    /// <summary>15 品の遺物プール。 段が上がるほど上位等級が解禁される (下位等級も最後まで出る)。
    /// 等級は BRONZE/SILVER/GOLD/LEGENDARY/MYTHIC の 5 段 × 各 3 品。</summary>
    public static class VescaRelicPool
    {
        /// <summary>大出血 1 スタックあたりの、 ターン開始時 軽減不可ダメージ。</summary>
        public const int MassiveBleedDamagePerStack = 1;

        private static VescaRelic R(string name, ItemRarity rarity, string role,
                                   System.Action<CombatContext> apply)
            => new VescaRelic { name = name, rarity = rarity, role = role, apply = apply };

        public static readonly List<VescaRelic> All = new List<VescaRelic>
        {
            // ---------- BRONZE ----------
            // 2026-08-16: 出目加算を 5→4 / 5→4 / 15→8 / 20→10 / 20→10 へ。
            //   p4 の攻撃値の内訳を実測したところ遺物由来が 12.5/T あり、
            //   DIVINE 廃止でプールが 15 枚に縮んだぶん攻撃札の抽選率が上がって相殺していた。
            R("鉛芯の握斧", ItemRarity.BRONZE, "攻撃",
                ctx => AddDice(ctx, 4)),
            R("革の手甲", ItemRarity.BRONZE, "防御",
                ctx => ctx.vescaShield += 5),
            R("医家の反り刃", ItemRarity.BRONZE, "妨害",
                ctx => ctx.vescaBleedOnHit += 1),

            // ---------- SILVER ----------
            // 溜め打ち: 次ターン開始時に +1、 永続かつ最大 5。 4 連戦を通じて残る唯一の恒久強化。
            R("溜め打ちの拳套", ItemRarity.SILVER, "攻撃",
                ctx => ctx.accumulatedValues[KeyChargedFistPending] = 1f),
            R("返しの盾", ItemRarity.SILVER, "防御",
                ctx => ctx.vescaShieldReflectRate = System.Math.Max(ctx.vescaShieldReflectRate, 1.0f)),
            R("腐敗の塗り薬", ItemRarity.SILVER, "妨害",
                ctx => ctx.playerHealHalvedTurns = System.Math.Max(ctx.playerHealHalvedTurns, 2)),   // 2026-08-16: 3T→2T

            // ---------- GOLD ----------
            R("落雷の避雷針", ItemRarity.GOLD, "攻撃",
                ctx => ctx.lightningRodArmed = true),
            // 2026-07-28: 〈不死の心臓〉(減少HPの20%回復) を撤去し〈貫きの錐〉へ差し替えた。
            //   理由 1: プレイヤー装備 Vitality_3「不死の心臓」と **同名で別効果** の衝突。
            //   理由 2: 回復量が最大HPに比例するため、 p4 (HP 99999) では〈裂け目〉の
            //           「現在HPの20%を失う」と正面から綱引きし、 最大HPの半分で平衡した
            //           (0.2×現在HP = 0.2×(最大−現在) → 現在 = 最大/2)。
            //           HP を上げるほど回復も比例して増えるため、 HP を動かしても戦闘長が
            //           変わらないという観測はこれが原因だった。
            R("貫きの錐", ItemRarity.GOLD, "攻撃",
                ctx => ctx.playerBlockIgnored = true),   // 2026-08-16: 出目加算(+5→+4)を撤去。 ブロック貫通のみ
            R("麻痺毒の小瓶", ItemRarity.GOLD, "妨害",
                ctx => ctx.playerAttackPowerPenalty += 3),   // 2026-07-29: -10→-5 / 2026-08-16: →-3

            // ---------- LEGENDARY ----------
            R("抱え式の破城槌", ItemRarity.LEGENDARY, "攻撃",
                ctx => AddDice(ctx, 8)),           // 2026-07-29: +20→+15 / 2026-08-16: →+8
            R("不落の城盾", ItemRarity.LEGENDARY, "防御",
                ctx =>
                {
                    ctx.vescaShield += 10;
                    ctx.vescaShieldReflectRate = System.Math.Max(ctx.vescaShieldReflectRate, 2.0f);
                }),
            R("処刑人の烙印", ItemRarity.LEGENDARY, "妨害",
                ctx => ctx.vescaExecuteArmed = true),

            // ---------- MYTHIC (入手不能) ----------
            R("殺戮", ItemRarity.MYTHIC, "攻撃",
                ctx => { ctx.vescaBleedOnHit += 2; AddDice(ctx, 10); }),  // 2026-07-29: 大出血 3→2 / 2026-08-16: 出目 20→10
            // 2026-07-28: 〈真守〉(シールド+99) を撤去し〈灼〉へ差し替えた。
            //   99 吸収が抽選で挟まるたびに削りが止まり、 p4 の「勝T 35.9 / 敗T 20.0」という
            //   **勝つ側だけが長い**歪みを作っていた (理論値は裂け目の減衰で 24T)。
            //   最大HP削りは〈殺戮〉の状態異常とも方向が被らず、 長期戦ほど効いて耐久レースと噛む。
            // 2026-07-29 ナーフ: 致死率 基準比 2.45x。 攻撃 30 → 20、 最大HP削り 5 → 3。
            R("灼", ItemRarity.MYTHIC, "攻撃",
                ctx => { AddDice(ctx, 5); ctx.vescaMaxHpBiteOnHit += 3; }),   // 2026-08-16: 出目 20→10→5
            // 2026-07-29 リワーク。 旧実装は「敵の出血/毒 + プレイヤーの臨界/充電を 0 に」だったが、
            //   ・p4 は〈回帰性真理〉でデバフ免疫 → 敵側の出血/毒はそもそも存在せず空振り
            //   ・プレイヤーがゲージを溜めていなければ完全な不発
            // 「奪った時間がそのまま彼の手番になる」形へ。 奪う対象が無くても停滞は必ず積む。
            //
            // **2026-08-16 ナーフ。** 「臨界+充電を全没収し、 その半分を出目へ」→
            //   **「充電の半分を没収し、 その全量を出目へ」**。 変えた点は 2 つ。
            //   ・**臨界に触らなくなった** ── 臨界の削りは〈回帰性真理〉の毎ターン −5 へ移した。
            //     1 枚の抽選で全損する形だと、 臨界ビルドは「引かれたら終わり」の運ゲーになる。
            //     毎ターン一定量なら、 溜める速度で勝負する読み合いとして成立する。
            //   ・**没収が半分になった** ── 全没収は充電ビルドを 1 枚で無力化していた。
            //   出目への転換率は 1/2 → 1/1 に上げたが、 元が半分なので**転換後の量は据え置き**
            //   (充電 8 なら 旧 4 / 新 4)。 変わったのは**プレイヤーの手元に半分残ること**。
            R("刻", ItemRarity.MYTHIC, "妨害",
                ctx =>
                {
                    int stolen = ctx.ConsumeHalfCharge();
                    if (stolen > 0) AddDice(ctx, stolen);
                    ctx.stagnationStacks += StolenTimeStacks;
                    UnityEngine.Debug.Log($"[刻] 充電の半分 {stolen} を没収 → 出目+{stolen} / "
                                        + $"停滞+{StolenTimeStacks} (計 {ctx.stagnationStacks})");
                }),

            // ---------- DIVINE ----------
            // **2026-08-16 廃止。** 〈天与の剣〉〈天与の盾〉〈天与の指輪〉の 3 枚を削除した。
            //   3 枚とも「プレイヤーのそのターンを無効化する」系 (受けダメ半減 / 出目 3 以上を
            //   1〜2 へ再抽選 / 最大HP削り) で、 p4 でのみ解禁されるため
            //   **p4 だけプレイヤー実火力が 1/4.6 (262→56)** という落差を作っていた。
            //   等級の階段を SILVER/GOLD/LEGENDARY/MYTHIC の 1 段ずつへ組み直し、
            //   最終段は MYTHIC 止まりとする (MYTHIC 自体は据え置き)。
            //   経緯は CapForStage の doc を参照。
        };

        /// <summary>〈刻〉が奪う「時間」= 停滞スタックの加算量。 奪うゲージが無くても必ず積む。</summary>
        public const int StolenTimeStacks = 1;

        public const string KeyChargedFistPending = "vesca_chargedfist_pending";
        public const string KeyChargedFistStacks  = "vesca_chargedfist_stacks";
        /// <summary>このターン、 遺物が積んだ敵出目ボーナスの合計 (RelicScholar が集計してから一括で書き出す)。</summary>
        public const string KeyDiceBonusThisTurn  = "vesca_dice_bonus";

        /// <summary>遺物の攻撃強化はすべて **敵出目ボーナス** 経由で行う。
        ///
        /// `ctx.enemyDiceTotalBonus` は「累積・BeginNewTurn でリセットしない」仕様のフィールドで、
        /// 毎ターン `+=` すると雪だるま式に増える。 そのため遺物側は一旦このキーに溜め、
        /// **RelicScholar が毎ターン `=` で一括代入する**（書き込み口を 1 本に絞る）。
        ///
        /// 2026-07-28 に β (ダイス寄与率 0.3) を撤去したため、 **「出目+20」はそのまま攻撃値 +20** になる
        /// (エスカレーション段階倍率は別途乗る)。 遺物の宣言値と予告値が一致する。</summary>
        public static void AddDice(CombatContext ctx, int n)
        {
            ctx.accumulatedValues[KeyDiceBonusThisTurn] =
                ctx.GetAccumulated(KeyDiceBonusThisTurn) + n;
        }

        /// <summary>段 (1..4) で解禁される最上位等級。
        ///
        /// <para><b>2026-08-16: 1段ずつの素直な階段へ変更し、 DIVINE を廃止した。</b>
        /// 旧 <c>SILVER / LEGENDARY / MYTHIC / DIVINE</c> は p2 で 2 段飛ばしており、
        /// p4 だけ質の違う札 (プレイヤーの手番を無効化する系) を握っていた。
        /// 実測 (HP を 4 段とも 1200 に揃えた計測ラン) でプレイヤーの実火力は
        /// <b>p1 262 / p2 231 / p3 255 に対し p4 は 56</b> ── 4.6 倍の落差があり、
        /// 4 段を同じ長さに設計できなかった。 落差の主因は p4 でのみ解禁される DIVINE 3 枚:
        /// 〈天与の盾〉受けるダメージ半減 /〈天与の指輪〉出目 3 以上を 1〜2 へ再抽選 /
        /// 〈天与の剣〉。 いずれも「そのターンを無かったことにする」種類で、
        /// p1〜p3 の遺物 (出目加算・シールド・出血) とは性質が違った。</para>
        ///
        /// <para><b>MYTHIC は据え置く。</b> 最終段の格を落とさないため、
        /// 〈殺戮〉〈灼〉〈刻〉には手を入れない。</para></summary>
        public static ItemRarity CapForStage(int stage)
        {
            switch (stage)
            {
                case 1:  return ItemRarity.SILVER;
                case 2:  return ItemRarity.GOLD;
                case 3:  return ItemRarity.LEGENDARY;
                default: return ItemRarity.MYTHIC;
            }
        }

        /// <summary>その段で引ける全遺物 (累積プール)。</summary>
        public static List<VescaRelic> ForStage(int stage)
        {
            var cap = CapForStage(stage);
            var list = new List<VescaRelic>();
            foreach (var r in All) if (r.rarity <= cap) list.Add(r);
            return list;
        }

        /// <summary>その段で「新たに追加された」等級のみ (形態変化直後の確定抽選用)。</summary>
        public static List<VescaRelic> NewTierForStage(int stage)
        {
            var cap = CapForStage(stage);
            var list = new List<VescaRelic>();
            foreach (var r in All) if (r.rarity == cap) list.Add(r);
            return list;
        }

        /// <summary>enemies.json の id (boss_layer7_p1..p4) から段番号を取る。</summary>
        public static int ResolveStage()
        {
            var e = CombatSystem.CombatManager.Instance?.CurrentEnemy;
            string id = e?.id ?? "";
            if (id.EndsWith("_p2")) return 2;
            if (id.EndsWith("_p3")) return 3;
            if (id.EndsWith("_p4")) return 4;
            return 1;
        }

        /// <summary>現在の敵が〈天与の才〉を持つか (= 遺物を 2 枚引く)。</summary>
        public static bool HasDivineTalent()
        {
            var e = CombatSystem.CombatManager.Instance?.CurrentEnemy;
            if (e?.passiveSkills == null) return false;
            foreach (var p in e.passiveSkills)
                if (p != null && p.internalName == "DivineTalent") return true;
            return false;
        }
    }

    /// <summary>遺物学者 ── 毎ターン開始時にプールから抽選して遺物を使用する。
    /// 段 4 は〈天与の才〉により 2 枚 (1 枚 MYTHIC 確定 + 1 枚ランダム)。
    /// **形態変化直後の 1 ターン目は、その段で追加された等級から確定で選出する。**</summary>
    public class RelicScholar : IPassiveSkillEffect
    {
        public string SkillId => "RelicScholar";
        // 2026-08-01: 〈大出血〉の tick を **OnTurnEnd** へ移した (旧: OnTurnStart)。
        //   ターン頭だとパリィ入力より前に解決してしまい、 そのターンの判定結果を適用できない。
        //   ターン末なら PERFECT の軽減と生存保証が効く ── 「読み切ったターンは血も止まる」。
        //   遺物の抽選そのものは従来どおりターン頭のまま (予告が成立しなくなるため)。
        public PassiveSkillTrigger[] Triggers => new[]
            { PassiveSkillTrigger.OnTurnStart, PassiveSkillTrigger.OnTurnEnd };

        private const string KeyLastStage = "vesca_last_stage";

        /// <summary>〈大出血〉の継続ダメージ。 解除不可・減衰なし・4 連戦を跨ぐ。
        /// 視点反転中なので **プレイヤーの HP は enemy* 側**。
        /// **軽減 (%/定数/基礎防御) は一切効かない** ── パリィ廃止 (ADR-0010) で緩和経路が消えた。
        /// 役でこれを弱める札も置いていない。 スタックを増やさないことが基本の対処。
        ///
        /// <para>ただし 2026-08-15 以降、 <b>シールドは肩代わりする</b>。
        /// 「軽減無視」は軽減を無視するという意味であって、 被ダメを肩代わりする盾まで
        /// 貫くものではない、 という仕様判断による (<see cref="CombatContext.EnemyDealUnmitigable"/>)。
        /// 盾を積むビルドにだけは持続的な解答があるが、 4 連戦を跨いで累積するので
        /// 供給が追いつかなければ結局押し切られる。</para></summary>
        private static void TickMassiveBleed(CombatContext ctx)
        {
            if (ctx.massiveBleedStacks <= 0) return;
            int dmg = ctx.massiveBleedStacks * VescaRelicPool.MassiveBleedDamagePerStack;
            // 2026-08-16: 内訳へ計上するようになった。 従来 null (=非計上) だったため、
            //   このダメージが残差として **Normal に化けて** いた ── p4 の「主死因 Normal 98%」は
            //   通常攻撃を指していない可能性がある。 ダメージ量は変わらず、 帰属先が増えるだけ。
            ctx.EnemyDealUnmitigable(dmg, DeathCause.MassiveBleed);   // 軽減は無視・シールドは肩代わりする
            UnityEngine.Debug.Log($"[大出血] スタック{ctx.massiveBleedStacks} → プレイヤーへ {dmg} ダメージ (軽減不可・盾は貫かない)");
        }

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx == null) return;
            if (trigger == PassiveSkillTrigger.OnTurnEnd) { TickMassiveBleed(ctx); return; }

            // --- §6-3 段0: シールドは「半減 (切り捨て) → 抽選付与」の順 ---
            // 切り捨てなので 1 → 0 で確実に消える。 定常上限は「毎ターン収入 × 2」で自動的に決まる。
            if (ctx.vescaShield > 0) ctx.vescaShield /= 2;
            ctx.vescaShieldAbsorbedThisTurn = 0;
            ctx.vescaShieldReflectRate = 0f;
            ctx.vescaDrawnRelics = new List<string>();
            if (ctx.enemyDodgeCooldown > 0) ctx.enemyDodgeCooldown--;   // 〈解析演算〉回避の再使用待ち
            ctx.accumulatedValues[VescaRelicPool.KeyDiceBonusThisTurn] = 0f;
            // vescaBleedOnHit / vescaMaxHpBiteOnHit / vescaExecuteArmed は BeginNewTurn がリセット済み。

            // --- 腐敗の塗り薬の持続 ---
            if (ctx.playerHealHalvedTurns > 0)
            {
                ctx.healHalved = true;
                ctx.playerHealHalvedTurns--;
            }

            // --- 溜め打ちの拳套: 前ターンに引いていれば、 このターン開始時に +1 (永続・最大5) ---
            if (ctx.GetAccumulated(VescaRelicPool.KeyChargedFistPending) > 0f)
            {
                ctx.accumulatedValues[VescaRelicPool.KeyChargedFistPending] = 0f;
                int s = System.Math.Min(5, (int)ctx.GetAccumulated(VescaRelicPool.KeyChargedFistStacks) + 1);
                ctx.accumulatedValues[VescaRelicPool.KeyChargedFistStacks] = s;
                UnityEngine.Debug.Log($"[溜め打ちの拳套] 永続 出目+1 (計 +{s}/5)");
            }

            // --- 抽選 ---
            int stage = VescaRelicPool.ResolveStage();
            bool stageJustChanged = (int)ctx.GetAccumulated(KeyLastStage) != stage;
            ctx.accumulatedValues[KeyLastStage] = stage;

            // 2026-07-29: 常時 2 枚 → **HP 閾値を割った次のターンだけ 2 枚**。
            // フラグは〈天与の才〉が立て、 使ったらここで倒す (1 ターン限り)。
            int draws = 1;
            if (ctx.vescaDoubleDrawNextTurn)
            {
                draws = 2;
                ctx.vescaDoubleDrawNextTurn = false;
                UnityEngine.Debug.Log("[天与の才] 追い詰められた ── このターンは遺物を 2 枚使う");
            }
            var pool = VescaRelicPool.ForStage(stage);
            var picked = new List<VescaRelic>();

            // 形態変化直後の 1 枚目は必ず新等級から (段の identity を台詞と同時に立てる)
            if (stageJustChanged)
            {
                var fresh = VescaRelicPool.NewTierForStage(stage);
                if (fresh.Count > 0)
                    picked.Add(fresh[GameLoop.GameRng.Range(0, fresh.Count, "vesca.relicNew", ctx.currentTurn)]);
            }
            // **再抽選ループの連番には必ず試行回数を混ぜること。** GameRng は純関数なので、
            // 同じ (キー, 連番) を引き直しても永久に同じ値が返る。 連番が picked.Count だけだと
            // 重複を引いた瞬間に無限ループする (2026-07-29 に 1 万ランのバッチを固めた実際の事故)。
            const int MaxAttempts = 32;
            for (int attempt = 0; picked.Count < draws && pool.Count > 0 && attempt < MaxAttempts; attempt++)
            {
                int slot = (ctx.currentTurn * 10 + picked.Count) * MaxAttempts + attempt;
                var cand = pool[GameLoop.GameRng.Range(0, pool.Count, "vesca.relic", slot)];
                if (picked.Contains(cand)) continue;
                picked.Add(cand);
            }

            foreach (var r in picked)
            {
                r.apply(ctx);
                ctx.vescaDrawnRelics.Add(r.name);
                UnityEngine.Debug.Log($"[遺物学者] T{ctx.currentTurn} 段{stage} 使用: {r.name} ({r.rarity}/{r.role})");
            }

            // --- 敵出目ボーナスの一括代入 ---
            // enemyDiceTotalBonus は累積フィールドなので `+=` は使わない。 このターンの寄与
            // (遺物 + 溜め打ちの永続分 + 停滞する時間) をここで計算し、 単一の書き込み口として `=` する。
            // 停滞する時間の値をこちら側で読むことで、 パッシブ同士の発火順への依存を無くしている。
            int diceBonus = (int)ctx.GetAccumulated(VescaRelicPool.KeyDiceBonusThisTurn)
                          + (int)ctx.GetAccumulated(VescaRelicPool.KeyChargedFistStacks)
                          + ctx.stagnationStacks * StagnantTime.DicePerStack(ctx);
            ctx.enemyDiceTotalBonus = diceBonus;
            if (diceBonus > 0)
                UnityEngine.Debug.Log($"[遺物学者] 敵攻撃への加算 合計 +{diceBonus}"
                    + $" (遺物 {(int)ctx.GetAccumulated(VescaRelicPool.KeyDiceBonusThisTurn)}"
                    + $" / 溜め打ち {(int)ctx.GetAccumulated(VescaRelicPool.KeyChargedFistStacks)}"
                    + $" / 停滞 {ctx.stagnationStacks * StagnantTime.DicePerStack(ctx)})");
        }
    }

    /// <summary>停滞する時間 ── プレイヤーが攻撃端子にダイスを 1 本も置かなかったターン、
    /// 次ターン以降の敵攻撃 +N (累積)。 シールド半減で「待つ」が無料の正解になる穴を塞ぐ。
    /// **N は L3 チューナーの第一レバー**（廃止した 真我 の後任。 実数値なので §13-3 隠し倍率禁止に適合）。</summary>
    public class StagnantTime : IPassiveSkillEffect
    {
        /// <summary>1 スタックあたりの敵出目ボーナスの基準値。 実効値は L3 チューナーが持つ
        /// <c>BossParam.Stagnation</c> の override（無ければこの基準値）。
        /// **7 層ヴェスカの第一レバー**（廃止した 真我 の後任。 実数値なので §13-3 の隠し倍率禁止に適合）。</summary>
        public const int DicePerStackBase = 1;
        /// <summary>1 スタック積むのに必要な「守りに寄せたターン」数。
        /// 2026-07-29: 1 → 2 → **1 に戻した**。 スケール速度は 2T刻みではなく
        /// 1 スタックあたりの上昇量 (DicePerStackBase: 3 → 1) 側で落とす。
        /// 判定は毎ターン走るので、 守った分だけ素直に 1 ずつ積む形に戻る
        /// (上限 99 = 敵攻撃 +99。 旧 +297 の 1/3)。</summary>
        public const int TurnsPerStack = 1;
        /// <summary>スタック上限。 **2026-08-16 に 99 → 10 へ戻した。**
        ///
        /// <para>2026-07-29 に 10 → 99 (実質無制限) へ上げたのは、
        /// 上限 10 = 敵攻撃 +30 で頭打ちになり、 プレイヤーがブロックを積むだけで
        /// **永久に受け切れた**ためだった（実測: 86.8 ターン戦って被ダメ合計 14.8）。</para>
        ///
        /// <para><b>その前提は 2 つとも消えている。</b>
        /// ① 当時 <c>DicePerStackBase</c> は 3 で「上限 10 = +30」だったが、 現在は 1 なので「+10」。
        /// ② 当時は〈裂け目〉がヴェスカの現在HP を毎ターン 40% 削る「時計」で、
        ///    <b>待てば勝てた</b>から青天井の罰が要った。 2026-08-16 に裂け目を
        ///    「双方の最大HP 2%/T」へ作り替えたので、 <b>削らない限り勝てない</b>。
        ///    待ち続けても勝利に近づかないため、 天井を戻しても「待つほど有利」は復活しない。</para>
        ///
        /// <para>実測 (HP 1385/1565/1715/2055): 停滞は 15T+ で 14.7 に達し、
        /// 敵ダイス合計 31.3 の 47% を占めていた。 上限 10 でそこが 10 に収まる。</para></summary>
        public const int StackCap = 10;

        /// <summary>チューナー調整後の 1 スタックあたり敵出目ボーナス。</summary>
        public static int DicePerStack(CombatContext ctx)
        {
            int v = AutoTest.BossTuning.ParamInt(ctx?.bossId, AutoTest.BossParam.Stagnation);
            return v > 0 ? v : DicePerStackBase;
        }

        private const string KeyPressure = "vesca_stagnation_pressure";

        public string SkillId => "StagnantTime";
        // 加算そのものは RelicScholar が enemyDiceTotalBonus へ一括代入する際に stagnationStacks を
        // 読んで行う。 このパッシブはスタックの計上だけを担当する（書き込み口を 1 本に保つため）。
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx == null) return;
            // 2026-07-29: 判定を「攻撃端子が空」→「**攻撃 ≦ ブロック**」へ広げた。
            //   旧条件は 0 本のときしか積まないので、 攻撃に 1 本だけ挿して残りを全部ブロックへ
            //   回す「ガン守り」が素通りしていた。 実測でプレイヤーの p4 実火力は 25/T まで落ち、
            //   37 ターン耐えて裂け目の自壊を待つだけの戦いになっていた。
            // 2026-07-29: 判定を「攻撃 ≦ ブロック」→「攻撃 **＜** ブロック」へ狭めた。
            //   同数 (2-2 など) は守りに寄せた配線ではないのに毎ターン発火しており、
            //   実質「攻撃に半分挿しても罰される」状態だった。
            int atk = (int)ctx.GetAccumulated("mutualAttackDiceCount");
            int blk = (int)ctx.GetAccumulated("mutualBlockDiceCount");
            if (atk >= blk) return;
            // 守りに寄せたターンを数え、 TurnsPerStack ごとに 1 スタック積む。
            // 上限は据え置きなので **最終的な総量は変わらず、 到達までの時間だけが伸びる**。
            float pressure = ctx.GetAccumulated(KeyPressure) + 1f;
            ctx.accumulatedValues[KeyPressure] = pressure;
            int want = System.Math.Min(StackCap, (int)pressure / TurnsPerStack);
            if (want > ctx.stagnationStacks)
            {
                ctx.stagnationStacks = want;
                UnityEngine.Debug.Log($"[停滞する時間] 守りに寄せた (攻撃{atk}<ブロック{blk}) → "
                                    + $"スタック={ctx.stagnationStacks}/{StackCap} "
                                    + $"(次T以降 敵攻撃+{ctx.stagnationStacks * DicePerStack(ctx)})");
            }
        }
    }

    /// <summary>解析演算 ── プレイヤーが収支プラスで終えるたび回避率 +3% (最大 60%)。
    /// **通常・固定・軽減不可を問わず全ダメージを回避する** (既存のメタ俊敏回避と同挙動)。
    /// 敵視点 OnRollLose = プレイヤーの収支プラス。</summary>
    public class AnalyticComputation : IPassiveSkillEffect
    {
        /// <summary>2026-07-29: 1 回あたり +3% → **+2%**、 上限 60% → **40%**。
        /// あわせて「回避に成功した次のターンは回避できない」を追加した (enemyDodgeCooldown)。
        /// 連続回避で削りが完全に止まる局面を無くし、 回避を「たまに刺さる保険」に留める。</summary>
        public const float PerWin = 0.02f;
        public const float Cap = 0.40f;

        public string SkillId => "AnalyticComputation";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRollLose };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx == null) return;
            if (ctx.enemyDodgeChance >= Cap) return;
            ctx.enemyDodgeChance = System.Math.Min(Cap, ctx.enemyDodgeChance + PerWin);
            UnityEngine.Debug.Log($"[解析演算] プレイヤー収支プラス → 回避率 {ctx.enemyDodgeChance:P0}");
        }
    }

    /// <summary>裂け目 ── 毎ターン、 **ヴェスカとプレイヤーの双方**が最大 HP の 2% を失う。
    /// 裂け目を強引に開き続けている代償 (§19-4)。 開けている本人だけでなく、
    /// 中に立っている者も等しく削られる。
    ///
    /// <para><b>2026-08-16 に「現在 HP の 40% を自分だけ」から作り替えた。</b>
    /// 旧実装は指数減衰 (0.6^n) だったため <b>HP の絶対値によらず戦闘長が一定</b>になり、
    /// 「HP は壁ではなく時計」という状態を作っていた。 その結果 p4 の HP を 65% 削っても
    /// 戦闘長が 6% しか動かず、 <b>HP が調整レバーとして機能しない</b>ボスになっていた。
    /// 最大 HP 基準の定額にすると寄与が線形かつ小さくなるので、 戦闘長は素直に
    /// 「HP ÷ プレイヤー火力」で決まる ── 4 段の所要ターンを設計できるようになる。</para>
    ///
    /// <para><b>配線による停止条件は撤去した。</b> 旧実装は「攻撃端子 &lt; ブロック端子のターンは
    /// 自壊しない」で守りを罰していたが、 これは自壊がプレイヤーの唯一の勝ち筋だった頃の設計。
    /// 双方を同率で削るようになった今は、 止めても割合では損得が生じない
    /// (ヴェスカ 2% / プレイヤー 2%) ため、 条件そのものが意味を失う。
    /// 守りへの罰は〈停滞する時間〉が単独で担う。</para></summary>
    public class RiftHeldOpen : IPassiveSkillEffect
    {
        /// <summary>毎ターン双方が失う「**最大** HP」の割合。</summary>
        public const float LossRate = 0.02f;

        public string SkillId => "RiftHeldOpen";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx == null) return;

            // 視点反転中: player* = ヴェスカ / enemy* = プレイヤー。
            // --- ヴェスカ側 (自壊) ---
            if (ctx.playerCurrentHP > 0 && ctx.playerMaxHP > 0)
            {
                int loss = UnityEngine.Mathf.CeilToInt(ctx.playerMaxHP * LossRate);
                ctx.playerCurrentHP = System.Math.Max(0, ctx.playerCurrentHP - loss);
                UnityEngine.Debug.Log($"[裂け目] ヴェスカ -{loss} (最大HPの{LossRate:P0}) → {ctx.playerCurrentHP}/{ctx.playerMaxHP}");
            }

            // --- プレイヤー側 ---
            // **軽減は無視・シールドは肩代わりする** (2026-08-15 の規約: ③-C)。
            if (ctx.enemyCurrentHP > 0 && ctx.enemyMaxHP > 0)
            {
                int loss = UnityEngine.Mathf.CeilToInt(ctx.enemyMaxHP * LossRate);
                int dealt = ctx.EnemyDealUnmitigable(loss, DeathCause.Rift);
                if (dealt > 0)
                    UnityEngine.Debug.Log($"[裂け目] プレイヤー -{dealt} (最大HPの{LossRate:P0}) → {ctx.enemyCurrentHP}/{ctx.enemyMaxHP}");
            }
        }
    }

    /// <summary>回帰性真理 ── 7 層 p4 ヴェスカ・天与 の中核パッシブ。
    ///
    /// HP 99999 を「削り切る対象」ではなく「時計」に変えるための封じ。
    /// 割合ダメージ・処刑・最大 HP 操作はいずれも巨大 HP に対する近道になるため塞ぐ。
    /// デバフ免疫も同じ理由 (毒/出血の累積は割合ダメージと同じ働きをする)。
    ///
    /// 実際に HP を減らすのは〈裂け目〉の自壊 (現在 HP の 20%/T) と、 プレイヤーの通常ダメージのみ。
    /// 免疫の判定そのものは <see cref="CombatContext"/> 側のゲート
    /// (RatioDamageToEnemy / CanExecuteEnemy / SetEnemyMaxHP / AddStatus / AddEnemyBleed) が持ち、
    /// ここはフラグを立てるだけ。</summary>
    public class RecurrentTruth : IPassiveSkillEffect
    {
        public string SkillId => "RecurrentTruth";
        public PassiveSkillTrigger[] Triggers => new[] {
            PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnTurnStart,
        };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx == null) return;
            if (!ctx.enemyRecurrentTruth)
            {
                ctx.enemyRecurrentTruth = true;
                ctx.enemyCritMultReduction = CritMultReduction;
                UnityEngine.Debug.Log($"[回帰性真理] 毒/出血を毎ターン半減・受ける会心倍率 −{CritMultReduction:F2}"
                                    + $" ・臨界メーター毎ターン −{RinkaiDrainPerTurn} ・処刑/最大HP変更を拒否");
            }
            if (trigger != PassiveSkillTrigger.OnTurnStart) return;

            // **毎ターン**の半減。 OnTurnStart は BeginNewTurn の DOT 判定より後に走るので、
            //   そのターンのダメージは通したうえで蓄積だけを削る。
            ctx.HalveEnemyDots();

            // 2026-08-16 追加: 臨界メーターの毎ターン減衰。 遺物〈刻〉が持っていた
            //   「臨界を全没収」を、 **抽選 1 枚の全損からパッシブの定常減衰へ**移したもの。
            //   `rinkaiMeter` は視点反転の対象外なので、 敵パッシブから読んでも
            //   常にプレイヤーのメーターを指す (本ファイル冒頭の remarks 参照)。
            //   閾値に届かせるには毎ターン 5 を上回るペースで積む必要がある ＝
            //   「溜めるか、 諦めて別の勝ち筋へ回すか」の判断がターンごとに立つ。
            if (ctx.rinkaiMeter > 0)
            {
                int before = ctx.rinkaiMeter;
                ctx.rinkaiMeter = System.Math.Max(0, ctx.rinkaiMeter - RinkaiDrainPerTurn);
                UnityEngine.Debug.Log($"[回帰性真理] 臨界メーター {before} → {ctx.rinkaiMeter}");
            }
        }

        /// <summary>毎ターン削る臨界メーター量。 遺物〈刻〉から移設 (2026-08-16)。</summary>
        public const int RinkaiDrainPerTurn = 5;

        /// <summary>受ける会心の**倍率からの減算量**。 会心倍率 2.0 なら 1.5 になる (下限 1.0)。
        /// **乗算にしないこと。** ×0.5 だと 2.0 が 1.0 になって会心が無意味になり、
        /// さらに遺物で倍率を積むほど削られる量が増える＝投資するほど損、 という反転が起きる。
        /// 減算なら積んだぶんは残る (4.7 → 4.2)。</summary>
        public const float CritMultReduction = 0.5f;
    }

    /// <summary>連続実験 ── HP が 0 になったとき死亡せず、 次のフェーズへ移行する。
    ///
    /// **4 段連戦の存在をプレイヤーに説明するパッシブ。** パッシブ欄に出ているので、
    /// 「削り切ってもまだ続く」ことが戦闘前に読める（§6-2 完全情報）。
    /// 逆に **最終段 (boss_layer7_p4) はこれを持たない** ── 欄に無いこと自体が
    /// 「これで最後」の告知になる。
    ///
    /// 予約方式: 敵パッシブ実行中は視点が反転しているため
    /// SwapEnemy を直接呼ばず `ctx.pendingEnemySwapId` に積み、 CombatManager が
    /// 敵トリガー完了後に処理する。</summary>
    public class ContinuousExperiment : IPassiveSkillEffect
    {
        /// <summary>自段の HP が 0 になっていたら次段への差し替えを予約する。
        /// 敵視点 (swapped) なので <c>ctx.playerCurrentHP</c> がボス自身の HP。
        /// 2026-08-24: 旧 AwakenedChainHelper から移設 (覚者連戦の削除に伴い唯一の利用者になったため)。</summary>
        private static void ReserveSwap(CombatContext ctx, string nextId, string logLabel)
        {
            if (ctx.playerCurrentHP > 0) return;
            if (!string.IsNullOrEmpty(ctx.pendingEnemySwapId)) return; // 既に予約済み
            ctx.pendingEnemySwapId = nextId;
            ctx.pendingEnemySwapLabel = logLabel;
            UnityEngine.Debug.Log($"[ヴェスカ連戦] HP=0検知 → 次段 {logLabel} を予約");
        }

        public string SkillId => "ContinuousExperiment";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };

        /// <summary>遷移先。 index = 現在の段 - 1。 最終段 (4) には次が無い。</summary>
        private static readonly string[] NextStageId =
            { "boss_layer7_p2", "boss_layer7_p3", "boss_layer7_p4", null };
        private static readonly string[] NextStageLabel =
            { "第二段：遺物学者", "第三段：神話", "第四段：天与", null };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx == null) return;
            int stage = VescaRelicPool.ResolveStage();
            int idx = System.Math.Min(stage, NextStageId.Length) - 1;
            string nextId = NextStageId[idx];
            if (string.IsNullOrEmpty(nextId)) return;   // 最終段は素直に死ぬ
            ReserveSwap(ctx, nextId, NextStageLabel[idx]);
        }
    }

    /// <summary>天与の才 ── 段 4 以降限定。 遺物を 2 つ同時に使用する
    /// (形態変化直後は 1 枚 その段の新等級確定 + 1 枚ランダム)。
    ///
    /// **実効判定は RelicScholar が `VescaRelicPool.HasDivineTalent()` で
    /// 敵の passiveSkills を直接見て行う** ── パッシブ同士の発火順に依存させないため。
    /// このクラスは登録の実体とログ用。</summary>
    public class DivineTalent : IPassiveSkillEffect
    {
        /// <summary>この割合を **初めて割った** 次のターンに 2 枚引く。 降順で持つこと。
        ///
        /// <para>2026-08-16: <c>{0.50, 0.25, 0.10}</c> → <b><c>{0.66, 0.33}</c></b>。
        /// 3 段 → 2 段にして、 3 等分の区切りで等間隔に置いた。 旧構成は 10% が終盤に寄りすぎており、
        /// p4 の実長が 4〜6 ターンだと **最後の閾値に届く前に決着する**ことが多かった
        /// (HP 1000・毎ターン数百のダメージでは 10% 帯を通過せず飛び越える)。</para>
        ///
        /// <para><b>ただし 66/33 にしても飛び越えは消えなかった。</b> 実測 (batch_20260816_141154) では
        /// p4 の実長 4.6T・勝ちランの平均削り量は 1 ターンあたり約 200 で、 HP 1000 に対して
        /// 33% (=330) を割るのが T4 前後。 発動は「割った**次**ターン」なので、
        /// 2 段目はしばしば決着に間に合っていない。 閾値をいくら動かしても、
        /// **1 ターンの削り量が閾値間隔を超えている限り飛び越えは起きる**。
        /// そこで閾値そのものを動かすのをやめ、 <see cref="ClampToThreshold"/> の
        /// 「踏みとどまり」で**跨ぐこと自体を禁じた** (2026-08-16)。</para></summary>
        public static readonly float[] Thresholds = { 0.66f, 0.33f };

        /// <summary>閾値 <paramref name="index"/> の HP 実値 (切り上げ)。
        /// 踏みとどまり側と <see cref="処刑"/> 側で**同じ整数**を使うためのもの。
        /// 割合 (float) で突き合わせると、 clamp した HP の比が 0.66 をわずかに超えて
        /// 通過判定が落ちる、 という取りこぼしが起きる。</summary>
        public static int ThresholdHP(int maxHP, int index)
            => UnityEngine.Mathf.CeilToInt(maxHP * Thresholds[index]);

        /// <summary>踏みとどまり ── まだ通過していない閾値を跨ぐダメージを、
        /// **その閾値ちょうどで止める**。 通過済みなら素通し。
        ///
        /// <para>これがないと、 1 ターンの削り量が閾値間隔 (最大HP の 33%) を超えた瞬間に
        /// 天与の才の発動が丸ごと消える。 火力を積むほどボスの切り札が減る＝
        /// **投資するほど戦闘が簡単になる**という反転が起きていた。
        /// 踏みとどまりを入れると、 p4 は最低でも「66% で 1 回・33% で 1 回」を必ず挟むので、
        /// 4 段構成のうち最終段だけが**火力で飛ばせない**段になる。</para>
        ///
        /// <para>呼び出しは CombatManager の相互攻撃パイプラインから 1 箇所のみ。
        /// 役〈極〉の削りより**前**に置くこと ── あちらは
        /// 「軽減・シールドを無視して削る」を謳う札なので、 踏みとどまりに吸われると
        /// 役の文面が嘘になる (メタデバフ〈鋼の皮膚〉と同じ扱い)。
        /// (〈過不足なし〉は 2026-08-22 に〈拮抗〉へリワークされ、 確定キルではなくなった。)</para></summary>
        /// <param name="hp">クランプ後のヴェスカ HP。</param>
        public static int ClampToThreshold(CombatContext ctx, int hp, int maxHP)
        {
            if (ctx == null || maxHP <= 0) return hp;
            if (!VescaRelicPool.HasDivineTalent()) return hp;
            int i = ctx.vescaThresholdsPassed;
            if (i >= Thresholds.Length) return hp;   // 全閾値を通過済み ＝ もう止めない
            int floorHp = ThresholdHP(maxHP, i);
            if (hp >= floorHp) return hp;
            UnityEngine.Debug.Log($"[天与の才] 踏みとどまり: HP {hp} → {floorHp} "
                                + $"(閾値 {Thresholds[i]:P0} / 通過 {i}/{Thresholds.Length})");
            return floorHp;
        }

        public string SkillId => "DivineTalent";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx == null || ctx.playerMaxHP <= 0) return;
            // 視点反転中: ヴェスカ自身の HP は player* 側。
            int hp = ctx.playerCurrentHP, maxHP = ctx.playerMaxHP;
            // 一度に複数の閾値をまたいだ場合も、 発火は 1 回・記録はまとめて進める。
            // (踏みとどまりが効いていれば 1 段ずつしか進まないが、 役の確定キルなど
            //  clamp を通らない経路があるので多段通過は残しておく。)
            int passed = ctx.vescaThresholdsPassed;
            while (passed < Thresholds.Length && hp <= ThresholdHP(maxHP, passed)) passed++;
            if (passed <= ctx.vescaThresholdsPassed) return;
            ctx.vescaThresholdsPassed = passed;
            ctx.vescaDoubleDrawNextTurn = true;
            UnityEngine.Debug.Log($"[天与の才] HP {hp}/{maxHP} が閾値 {Thresholds[passed - 1]:P0} を割った "
                                + $"→ 次ターンは遺物 2 枚 (通過 {passed}/{Thresholds.Length})");
        }
    }
}
