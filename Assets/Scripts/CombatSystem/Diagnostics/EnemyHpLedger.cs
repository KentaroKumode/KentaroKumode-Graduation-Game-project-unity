using System;
using System.Collections.Generic;

namespace CombatSystem
{
    /// <summary>
    /// [計装 2026-11-10] 敵の HP を<b>実際に減らした量</b>の経路別の帳簿。 計測専用 ── ゲームの結果は変えない (乱数も引かない)。
    ///
    /// <para><b>なぜ要るか。</b> <see cref="CombatStats.DamageBreakdown"/> の主攻撃 [7]・毒 [20]・爆発 [22] は回避・シールド・踏みとどまり・
    /// 撃破のはみ出しの<b>前</b>の値で、 出血 [21] だけが入った分。 〈廃庭の霧吹き〉の毒は HP から直接引くので固定ダメ [9] に入らない。
    /// 割合を比べると、 経路ごとに「数えた時点」が違うものを比べることになる。 ここでは全部を「敵の HP が実際に減った量」で揃える。</para>
    ///
    /// <para><b>読み方。</b> 累計の配列 <see cref="Cum"/> (戦闘をまたいで増えるだけ) を、 読み手 (AutoRunner) が戦闘の開始と終了の差で切る。
    /// 「入った量」の合計 [0..7] − 回復 [8] − 説明のつかない増加 [28] = 敵の失った HP (最大 HP − 終了 HP)。 合わなければ計装していない経路がある。</para>
    ///
    /// <para>固定ダメの枠 (毒・爆発・その他) は 1 つの数で敵に入るので、 回避・シールド・はみ出しで減った後の「入った量」は
    /// 要求された量の比で按分する (小数で持つ)。 踏みとどまり (〈天与の才〉・〈鋼の皮膚〉) で戻した量は、 そのターンに直前に入れた経路から順に差し戻す。</para>
    /// </summary>
    public static class EnemyHpLedger
    {
        // ── 入った量 (敵の HP が実際に減った量) ──
        public const int Main = 0;          // 主攻撃
        public const int Poison = 1;        // 毒 (ターン頭の毒 = 固定ダメ枠の按分 + 〈廃庭の霧吹き〉)
        public const int Bleed = 2;         // 出血 (出血 + 出血端子)
        public const int Burst = 3;         // 臨界の爆発 (固定ダメ枠の按分)
        public const int OtherFixed = 4;    // その他の固定ダメ (枠の残り・品の直接 (攻撃後)・役〈極〉・奇襲)
        public const int Reflect = 5;       // 反射・反撃 (仕込み刃・反攻・反撃固定・反射 3 種)
        public const int CtxDirect = 6;     // 戦闘の情報 (ctx) を通した直接の削り (品の処刑・絞り出し・黎明・Kw.DamageEnemy の直接 等)
        public const int EnemySelf = 7;     // 敵自身の効果による減少 (〈裂け目〉の自壊・割合損失 等)
        // ── HP が増えた量 ──
        public const int Heal = 8;          // 敵の回復 (フロア・〈再生〉・敵のパッシブ)
        public const int Restored = 9;      // [内訳] 踏みとどまりで戻した量 (入った量からは差し引き済み)
        // ── 主攻撃が入るまでに失った量 ──
        public const int MainDefense = 10;  // 敵の防御値で削られた量
        public const int MainEnemyRed = 11; // 敵の軽減パッシブ (OnPreReceiveDamage) で削られた量
        public const int MainDodge = 12;    // ヴェスカ〈解析演算〉の回避
        public const int MainHalve = 13;    // 〈天与の盾〉の半減
        public const int MainShield = 14;   // 敵のシールドに吸われた量
        public const int MainOver = 15;     // 撃破のはみ出し (と踏みとどまりで戻した分)
        // ── 固定ダメ枠 (毒・爆発・その他の合計) が入るまでに失った量 ──
        public const int FixedDodge = 16, FixedHalve = 17, FixedShield = 18;
        // ── 経路ごとの「要求 − 入った量」(回避・半減・シールド・はみ出し・逃げるターンの捨て分を全部含む) ──
        public const int PoisonLost = 19, BurstLost = 20, OtherFixedLost = 21, BleedLost = 22;
        // ── 配線の集計 (CombatStats.RecordTurn と同じ地点) ──
        public const int AtkDice = 23, BlkDice = 24, Turns = 25, ChgDice = 26;
        // ── 要求された量 ──
        public const int MainRequested = 27;   // 主攻撃の要求 (ヴェスカの回避・半減・シールドの<b>後</b>・はみ出しの前。 DamageBreakdown[7] − [12..14] に等しい)
        public const int CtxUnexplained = 28;  // ctx の同期で HP が増えた量のうち、 敵の効果で説明できない分 (0 であるべき)
        public const int MistPoison = 29;      // [内訳] 毒のうち〈廃庭の霧吹き〉
        public const int PoisonRequested = 30, BurstRequested = 31, OtherFixedRequested = 32, BleedRequested = 33;
        public const int Length = 34;

        /// <summary>累計 (戦闘をまたいで増えるだけ)。 読み手が差分で切る。</summary>
        public static readonly double[] Cum = new double[Length];

        // ── [測定用の物差し 2026-11-10] 既定 1 (今の挙動と完全に同じ)。 製品では使わない (書くのは AutoRunner だけ・ラン終了で 1 に戻す) ──
        /// <summary>測定用の物差し・既定 1・製品では使わない。 プレイヤーの主攻撃の最終ダメージ (与ダメ倍率・攻撃バーストの後、
        /// 敵の軽減パッシブ・防御値・ヴェスカの回避・シールドの前) に掛ける。 <c>CombatManager.ApplyWinDamageModifiers</c> (呼び手はプレイヤーの主攻撃 1 か所だけ) で掛ける。
        /// 敵の攻撃には掛からない。</summary>
        public static float ProbeMainMult = 1f;
        /// <summary>測定用の物差し・既定 1・製品では使わない。 敵の HP に入る 毒の tick (ターン頭・〈廃庭の霧吹き〉)・出血の tick (出血・出血端子)・
        /// 臨界の爆発 1 回の量 (爆発の会心の上乗せを含む)・〈逆綴じの止血帯〉の量に掛ける。 スタックの溜まり方・減衰・出来事 (OnPoisonTick 等) に渡す量は変えない。
        /// 〈黎明〉・反射・その他の固定ダメ・敵の自傷・プレイヤーが受ける毒/出血には掛けない。</summary>
        public static float ProbePassiveMult = 1f;
        /// <summary><see cref="ProbeMainMult"/> を掛けた量 (既定 1 なら素通し)。</summary>
        public static int ProbeMain(int dmg)
            => ProbeMainMult == 1f || dmg <= 0 ? dmg : Math.Max(0, (int)Math.Round(dmg * (double)ProbeMainMult, MidpointRounding.AwayFromZero));
        /// <summary><see cref="ProbePassiveMult"/> を掛けた量 (既定 1 なら素通し)。</summary>
        public static int ProbePassive(int dmg)
            => ProbePassiveMult == 1f || dmg <= 0 ? dmg : Math.Max(0, (int)Math.Round(dmg * (double)ProbePassiveMult, MidpointRounding.AwayFromZero));

        // 固定ダメ枠へこのターンに入った毒・爆発 (ターンの頭の毒の tick と爆発の予約で積み、 枠を取り出した時に使って 0 に戻す)
        private static double _pendPoison, _pendBurst, _fixedPreDefense = -1;
        // 主攻撃の防御・敵の軽減で削った量 (攻撃しないターンは捨てる)
        private static double _pendDef, _pendRed;
        // 敵のトリガー (視点反転中) の間に ctx の敵 HP が減った / 増えた量 (次の同期で使う)
        private static double _pendSelf, _pendHeal;
        // 踏みとどまりで差し戻す先 (直近に入れた経路と量)
        private static readonly List<KeyValuePair<int, double>> _recent = new List<KeyValuePair<int, double>>(16);

        /// <summary>戦闘の開始・連戦の差し替え: 持ち越してはいけない予約を捨てる。</summary>
        public static void BeginForm()
        {
            _pendPoison = _pendBurst = 0; _fixedPreDefense = -1;
            _pendDef = _pendRed = 0; _pendSelf = _pendHeal = 0;
            _recent.Clear();
        }

        /// <summary>ターンの頭 (固定ダメ枠が 0 に戻る時): 前のターンの予約を捨てる。</summary>
        public static void BeginTurn()
        {
            _pendPoison = _pendBurst = 0; _fixedPreDefense = -1;
            _pendDef = _pendRed = 0;
            _recent.Clear();
        }

        public static void NotePoisonIntoFixed(int dmg) { if (dmg > 0) _pendPoison += dmg; }
        public static void NoteBurstIntoFixed(int dmg) { if (dmg > 0) _pendBurst += dmg; }
        /// <summary>主攻撃の防御値・敵の軽減パッシブで削った量 (ApplyWinDamageModifiers)。</summary>
        public static void NoteMainDefense(int before, int after) { if (before > after) _pendDef += before - after; }
        public static void NoteMainEnemyReduction(int before, int after) { if (before > after) _pendRed += before - after; }
        /// <summary>攻撃したターンだけ、 主攻撃の防御・軽減の削りを帳簿に載せる。</summary>
        public static void CommitMainMitigation(bool attacked)
        {
            if (attacked) { Cum[MainDefense] += _pendDef; Cum[MainEnemyRed] += _pendRed; }
            _pendDef = _pendRed = 0;
        }
        /// <summary>ヴェスカの防御 (回避・半減・シールド) の前の固定ダメ枠。</summary>
        public static void NoteFixedPreDefense(int fixedDmg) { _fixedPreDefense = Math.Max(0, fixedDmg); }
        /// <summary>ヴェスカの防御で主攻撃 / 固定ダメ枠から消えた量。 kind: 0 回避 / 1 半減 / 2 シールド。</summary>
        public static void NoteVescaDefense(int kind, int mainLost, int fixedLost)
        {
            if (mainLost > 0) Cum[kind == 0 ? MainDodge : kind == 1 ? MainHalve : MainShield] += mainLost;
            if (fixedLost > 0) Cum[kind == 0 ? FixedDodge : kind == 1 ? FixedHalve : FixedShield] += fixedLost;
        }

        /// <summary><see cref="HpLossDiag.Lose"/> から: 経路 <paramref name="channel"/> に要求 <paramref name="requested"/>、 実際に減った <paramref name="landed"/>。</summary>
        public static void Land(string channel, int requested, int landed)
        {
            requested = Math.Max(0, requested); landed = Math.Max(0, landed);
            switch (channel)
            {
                case "通常攻撃":
                    Cum[MainRequested] += requested;
                    Push(Main, landed);
                    Cum[MainOver] += requested - landed;
                    return;
                case "固定ダメ":
                    LandFixed(requested, landed);
                    return;
                case "出血":
                case "出血端子":
                    Cum[BleedRequested] += requested;
                    Push(Bleed, landed);
                    Cum[BleedLost] += requested - landed;
                    return;
                case "毒(廃庭の霧吹き)":
                    Cum[PoisonRequested] += requested;
                    Push(Poison, landed);
                    Cum[MistPoison] += landed;
                    Cum[PoisonLost] += requested - landed;
                    return;
            }
            if (channel != null && (channel.StartsWith("反射") || channel == "仕込み刃" || channel == "反攻" || channel == "反撃固定"))
            {
                Push(Reflect, landed);
                return;
            }
            // 役〈極〉・品の直接 (攻撃後)・奇襲 と、 名前を知らない経路
            Cum[OtherFixedRequested] += requested;
            Push(OtherFixed, landed);
            Cum[OtherFixedLost] += requested - landed;
        }

        /// <summary>固定ダメ枠: 要求 (ヴェスカの防御の前) を毒・爆発・その他に分け、 入った量を同じ比で按分する。</summary>
        private static void LandFixed(int afterDefense, int landed)
        {
            double f0 = _fixedPreDefense >= 0 ? _fixedPreDefense : afterDefense;
            double p = _pendPoison, b = _pendBurst;
            double o = Math.Max(0, f0 - p - b);
            // 逃げるターン等で枠が捨てられた (f0 < 毒 + 爆発) ときも、 要求は要求として数える
            double req = p + b + o;
            Cum[PoisonRequested] += p; Cum[BurstRequested] += b; Cum[OtherFixedRequested] += o;
            if (req > 0)
            {
                double lp = landed * p / req, lb = landed * b / req, lo = landed * o / req;
                Push(Poison, lp); Push(Burst, lb); Push(OtherFixed, lo);
                Cum[PoisonLost] += p - lp; Cum[BurstLost] += b - lb; Cum[OtherFixedLost] += o - lo;
            }
            else if (landed > 0)
            {
                Push(OtherFixed, landed);   // 起きないはず (要求 0 で入った)。 残差で見えるようにその他へ
            }
            _pendPoison = _pendBurst = 0; _fixedPreDefense = -1;
        }

        private static void Push(int idx, double v)
        {
            if (v <= 0) return;
            Cum[idx] += v;
            _recent.Add(new KeyValuePair<int, double>(idx, v));
            if (_recent.Count > 64) _recent.RemoveAt(0);
        }

        /// <summary>踏みとどまりで敵の HP を <paramref name="amount"/> 戻した: 直近に入れた経路から順に差し戻す。</summary>
        public static void Restore(int amount)
        {
            if (amount <= 0) return;
            Cum[Restored] += amount;
            double left = amount;
            for (int i = _recent.Count - 1; i >= 0 && left > 0; i--)
            {
                var kv = _recent[i];
                double take = Math.Min(left, kv.Value);
                Cum[kv.Key] -= take;
                AddLost(kv.Key, take);
                left -= take;
                double rest = kv.Value - take;
                if (rest <= 0) _recent.RemoveAt(i);
                else _recent[i] = new KeyValuePair<int, double>(kv.Key, rest);
            }
            if (left > 0) Cum[CtxUnexplained] += left;   // 差し戻す先が無い (起きないはず)
        }

        private static void AddLost(int idx, double v)
        {
            switch (idx)
            {
                case Main: Cum[MainOver] += v; break;
                case Poison: Cum[PoisonLost] += v; break;
                case Burst: Cum[BurstLost] += v; break;
                case Bleed: Cum[BleedLost] += v; break;
                case OtherFixed: Cum[OtherFixedLost] += v; break;
                case EnemySelf: break;   // 敵の自傷は要求の量を持たない (差し引いた [7] がそのまま正味)。 損失の欄は無い
            }
        }

        /// <summary>敵の回復 (CombatManager が敵の HP を直接増やした)。</summary>
        public static void NoteHeal(int amount) { if (amount > 0) Cum[Heal] += amount; }

        /// <summary>敵のトリガー (視点反転中) の前後で、 プレイヤー視点の ctx の敵 HP が <paramref name="delta"/> 動いた。</summary>
        public static void NoteEnemyTriggerDelta(int delta)
        {
            if (delta < 0) _pendSelf += -delta;
            else if (delta > 0) _pendHeal += delta;
        }

        /// <summary>CombatManager が ctx の敵 HP を手元の値で上書きした: それまでの ctx の変化は捨てられたので、 敵の効果の予約も捨てる。</summary>
        public static void CtxOverwritten() { _pendSelf = _pendHeal = 0; }

        /// <summary>SyncHPFromContext: 手元の敵 HP <paramref name="before"/> を ctx の値 <paramref name="after"/> で置き換える。</summary>
        public static void CtxSync(int before, int after)
        {
            double d = before - after;            // 正 = HP が減った
            double s = _pendSelf, h = _pendHeal;
            _pendSelf = _pendHeal = 0;
            if (d == 0 && s == 0 && h == 0) return;
            // d = 敵の自傷 s − 敵の回復 h + その他 x
            double x = d - s + h;
            // 2026-11-10: 自傷も差し戻し先に積む (積まないと、 踏みとどまりで戻した分が差し戻す先を失って [28] に落ちる)
            if (s > 0) Push(EnemySelf, s);
            if (h > 0) Cum[Heal] += h;
            if (x > 0) Push(CtxDirect, x);
            else if (x < 0) Cum[CtxUnexplained] += -x;
        }

        // ── [計装 2026-11-10] ctx を通した削り [6] の品別の内訳 (診断用) ──
        //   品の効果が ctx の敵 HP を下げた量 (PassiveSkillManager.FireTrigger / PassiveItemManager の前後差) と、
        //   Kw.DamageEnemy の直接の分。 ctx はその後 CombatManager の手元の値で上書きされることがあるので、 <b>上限の見積もり</b>
        //   (入った量 [6] より大きく出うる)。 キーは「区分|品」(区分 = ヴェスカの段の id か "他")。
        public static readonly Dictionary<string, double> CtxBySource = new Dictionary<string, double>();
        public static string SourceScope = "他";
        public static void NoteCtxSource(string id, int hpBefore, int hpAfter)
        {
            int d = hpBefore - hpAfter;
            if (d <= 0) return;
            string k = SourceScope + "|" + (string.IsNullOrEmpty(id) ? "?" : id);
            CtxBySource.TryGetValue(k, out double v);
            CtxBySource[k] = v + d;
        }

        /// <summary>配線の集計 (1 ターン)。</summary>
        public static void NoteWiring(int atkDice, int blkDice, int chgDice)
        {
            Cum[AtkDice] += atkDice; Cum[BlkDice] += blkDice; Cum[ChgDice] += chgDice; Cum[Turns] += 1;
        }
    }
}
