using GameLoop;

namespace CombatSystem
{
    /// <summary>
    /// ヴェスカの大技 = 天与の剣 / 盾 / 指輪 (2026-11-15・作者の決定・docs/GAME.md §13 のヴェスカの節・DAMAGE_CALC_REFERENCE §9)。
    ///
    /// <para><b>撃つのは 3 段目と 4 段目だけ</b> (大技の周期 heavyPeriod を持つヴェスカの段 = enemies.json で p3 / p4 だけが持つ)。 1・2 段目は大技もチャージも無い。
    /// 周期・チャージのターン (大技の 1 ターン前・攻撃しない)・ダウンでの取り消しは、 ほかのボスの大技と同じ (Escalation)。</para>
    ///
    /// <para><b>どれを撃つか</b>: チャージのターンの頭 (〈遺物学者〉の抽選の所) で、 3 つから抽選する。 直前に撃った物は続けない (残り 2 つから)。
    /// 乱数は GameRng (専用の鍵 "vesca.tenyo"・連番 = そのターンの番号) ── 同じシードなら同じ物。 予告 (<see cref="MutualTurnTelegraph.tenyoGift"/>) に出す。</para>
    ///
    /// <para><b>効果</b> (仮の値・BossParams に実数):
    /// 剣 = 今の大技の攻撃 (攻撃 × heavyMul) + 当たったら最大 HP を削る (<see cref="BossParam.TenyoSwordBite"/>・〈灼〉の 2 倍)・
    /// 盾 = 攻撃しない・ヴェスカが最大 HP の <see cref="BossParam.TenyoShieldPct"/>% のシールドを得る (大技のターンの頭・その後の半減は普段どおり)・
    /// 指輪 = 攻撃しない・次のターン、 こちらが引いた端子を全部止める (〈停滞する時間〉の止め方を確率 1 で)。</para>
    ///
    /// <para><b>遺物</b>: チャージのターンと大技のターンは、 ほかの遺物を使わない (〈遺物学者〉の抽選を止める)。 〈天与の才〉の 2 枚引きは使わずに、 次のふつうのターンへ送る。</para>
    ///
    /// <para>状態は静的に持つ (大技の周期の起点 Escalation.HeavyCycleOffset と同じく、 戦闘の始めと段の入れ替えで戻す)。 BOT の先読みも同じ値を読む。</para>
    /// </summary>
    public static class TenyoHeavy
    {
        public enum Gift { None = 0, Sword = 1, Shield = 2, Ring = 3 }

        /// <summary>予告・画面に出す名前。</summary>
        public static string NameOf(Gift g) => g == Gift.Sword ? "天与の剣" : g == Gift.Shield ? "天与の盾" : g == Gift.Ring ? "天与の指輪" : "";

        /// <summary>次の (いまチャージしている) 大技の天与。 None = 決まっていない。</summary>
        public static Gift Pending { get; private set; }
        /// <summary>直前に撃った天与 (同じ物を続けない)。</summary>
        public static Gift Last { get; private set; }
        /// <summary>指輪で端子が全部止まるターン (−1 = 無し)。</summary>
        public static int RingStopTurn { get; private set; } = -1;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>[実機の確認用] 次の抽選で、 この天与にする (1 回限り・QaBattle.ForceTenyo)。 None = 指定なし (既定)。</summary>
        public static Gift QaForce;
#endif

        /// <summary>戦闘の始め・段の入れ替え: 全部戻す。</summary>
        public static void Reset() { Pending = Gift.None; Last = Gift.None; RingStopTurn = -1; }

        /// <summary>この敵の大技が天与か (ヴェスカの段で、 大技の周期を持つ = 3・4 段目)。</summary>
        public static bool Is(EnemyData e) => e != null && e.heavyPeriod > 0 && BossIds.VescaStageOf(e.id) > 0;

        /// <summary>段が入れ替わった (〈連続実験〉): 天与の状態を戻し、 大技を持つ段なら周期を頭から数える (入れ替わったターンを起点に、 1 周期ぶん溜めてから)。
        /// 前の段に大技が無い (1・2 段目) ので、 そのまま数えると、 チャージを経ずに大技のターンが来ることがある。</summary>
        public static void OnStageSwap(EnemyData newEnemy, int currentTurn)
        {
            Pending = Gift.None; RingStopTurn = -1;
            if (Is(newEnemy)) Escalation.HeavyCycleOffset = currentTurn;
        }

        /// <summary>このターンの大技の扱い (〈遺物学者〉のターンの頭から呼ぶ・ダウンの判定の後)。 戻り値 = このターンは遺物を使わないか (チャージか大技のターン)。
        /// チャージのターン: 天与を抽選して <see cref="Pending"/> に。 大技のターン: 効果の頭の分 (盾のシールド・剣の最大 HP 削り・指輪の次のターン) を入れる。
        /// ctx は敵の視点 (ヴェスカ = player*)。</summary>
        public static bool OnTurnStart(EnemyData e, ItemSystem.PassiveSkills.CombatContext ctx)
        {
            if (!Is(e) || ctx == null || BossDown.ActiveNow) return false;
            int turn = ctx.currentTurn, toHeavy = Escalation.TurnsToHeavy(e, turn);
            if (toHeavy == 1 && Escalation.IsChargeTurn(e, turn))
            {
                if (Pending == Gift.None) Pending = Draw(turn);
                if (UnityEngine.Debug.unityLogger.logEnabled) UnityEngine.Debug.Log($"[天与] T{turn} チャージ: 次のターンは {NameOf(Pending)}");
                return true;
            }
            if (toHeavy == 0)
            {
                if (Pending == Gift.None) Pending = Draw(turn);      // チャージを経ずに来た (起点が動いた時など): ここで決める
                switch (Pending)
                {
                    case Gift.Shield:
                    {
                        int add = (int)System.Math.Round(ctx.enemyMaxHP * BossParams.Param(e.id, BossParam.TenyoShieldPct) / 100f);
                        ctx.vescaShield += add;
                        if (UnityEngine.Debug.unityLogger.logEnabled) UnityEngine.Debug.Log($"[天与の盾] T{turn} シールド +{add} (最大 HP {ctx.enemyMaxHP} の {BossParams.Param(e.id, BossParam.TenyoShieldPct)}%) → {ctx.vescaShield}");
                        break;
                    }
                    case Gift.Sword:
                        ctx.vescaMaxHpBiteOnHit += BossParams.ParamInt(e.id, BossParam.TenyoSwordBite);
                        if (UnityEngine.Debug.unityLogger.logEnabled) UnityEngine.Debug.Log($"[天与の剣] T{turn} 大技の攻撃 + 当たったら最大 HP −{BossParams.ParamInt(e.id, BossParam.TenyoSwordBite)}");
                        break;
                    case Gift.Ring:
                        RingStopTurn = turn + 1;
                        if (UnityEngine.Debug.unityLogger.logEnabled) UnityEngine.Debug.Log($"[天与の指輪] T{turn} 次のターン (T{turn + 1}) は引いた端子が全部止まる");
                        break;
                }
                Last = Pending;
                return true;
            }
            if (toHeavy != 0 && Pending != Gift.None && toHeavy > 1) Pending = Gift.None;      // 周期が頭に戻った (ダウンで取り消し): 次のチャージで引き直す
            return false;
        }

        private static Gift Draw(int turn)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (QaForce != Gift.None) { var f = QaForce; QaForce = Gift.None; return f; }
#endif
            if (Last == Gift.None) return (Gift)(1 + GameRng.Range(0, 3, "vesca.tenyo", turn));
            int r = GameRng.Range(0, 2, "vesca.tenyo", turn);      // 直前の物を除いた 2 つ
            int g = 1; for (int seen = -1; ; g++) { if ((Gift)g == Last) continue; if (++seen == r) break; }
            return (Gift)g;
        }

        /// <summary>このターンの大技は攻撃しない天与 (盾・指輪) か。 攻撃値の倍率 (Escalation.HeavyFactor) を 0 にし、 戦闘は「攻撃しない」の枝を通す。</summary>
        public static bool NoAttackAt(EnemyData e, int turn)
            => Is(e) && (Pending == Gift.Shield || Pending == Gift.Ring) && Escalation.TurnsToHeavy(e, turn) == 0;

        /// <summary>予告に出す天与 (チャージのターンと大技のターンだけ・ダウンのターンは無し)。</summary>
        public static Gift TelegraphAt(EnemyData e, int turn, bool down)
        {
            if (!Is(e) || down) return Gift.None;
            int k = Escalation.TurnsToHeavy(e, turn);
            return k == 0 || k == 1 ? Pending : Gift.None;
        }

        /// <summary>このターン、 引いた端子を全部止めるか (指輪の次のターン)。</summary>
        public static bool RingStopsAt(int turn) => RingStopTurn >= 0 && turn == RingStopTurn;
    }
}
