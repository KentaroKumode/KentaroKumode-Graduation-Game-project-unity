using System;
using UnityEngine;

namespace AutoTest
{
    /// <summary>
    /// **次のボスに必要な戦力**と現有戦力を突き合わせ、 ラン全体の振る舞いを決める物差し。
    ///
    /// 動機 (2026-08-10 の実測)。 先読み方策 <see cref="SuperCombatAI"/> は戦闘を明確に
    /// 勝てるようになった (30pt で 5層クリア 30.2% → 61.2%) のに、 7層クリアは
    /// 1.6% → 3.4% しか動かなかった。 **中盤の勢いが終盤の戦力へ変換されていない** ──
    /// 変換を担うのはショップの買い方・休憩・ルート取りで、 そこは技量に関係なく
    /// 同じ貪欲コードを使っていた。 戦闘をいくら上手く打っても、 ここが同じなら差は届かない。
    ///
    /// **これは天井 (WiringSkill.Super) 専用の能力**にしてある。 Optimal と共有すると
    /// 技量帯の参照点そのものが動き、 「要求戦力を読むと何 pt 増えるか」が測れなくなる。
    ///
    /// 敵の素データを読むことは不正にしない ── 上位プレイヤーはボスの HP と攻撃を
    /// 覚えている。 読まないと約束したのは**未来の出目と敵の内部状態**であって、
    /// 攻略情報として公開されている静的な数値ではない。
    /// </summary>
    public static class RunPowerBudget
    {
        /// <summary>ボスを倒し切る目標ターン数。 エスカレーション段階3 (T15) に入る前に
        /// 決着させたいので 12 に置く。 <see cref="CombatSystem.Escalation.Thresholds"/> 参照。</summary>
        public const int TargetTurns = 12;

        /// <summary>目標ターン内での敵攻撃値の段階倍率の平均 (1.00/1.30/1.60 の加重)。</summary>
        public const float EscalationAvg = 1.4f;

        /// <summary>戦力ギャップ。 1.0 で「ちょうど足りる」。 **詰まっている方が支配する**ので
        /// 与ダメ比とHP比の小さい方を返す。 ボスが引けない (層が最終) なら 1.0 を返す。</summary>
        public static float Gap(GameLoop.RunState run, int floor, float dmgMul, int diceCount, float meanFace)
        {
            if (run == null) return 1f;
            var boss = NextBoss(floor);
            if (boss == null) return 1f;

            float reqDpt = boss.maxHP / (float)TargetTurns;
            float bossAtk = boss.EffectiveBaseAttack
                          + boss.EffectiveRollCount * (boss.EffectiveRollMax + 1) * 0.5f;
            float reqEhp = Mathf.Max(1f, bossAtk * EscalationAvg * TargetTurns);

            // 現有: 与ダメは 6 割のダイスを攻撃へ、 ブロックは 3 割という配分の期待値で見る
            // (SuperCombatAI.StaticValue と同じ仮定に揃えてある)。
            // 素火力は装備武器から引く。 取れなければ既定 2 (武器なし相当)。
            int atkPower = 2;
            {
                var w = InventorySystem.ItemDatabase.Instance?.GetItem(run.equippedWeaponId);
                if (w != null && w.hasWeaponStats) atkPower = Math.Max(1, w.attackPower);
            }
            float curDpt = Mathf.Max(0.5f, (atkPower + 0.6f * diceCount * meanFace) * Mathf.Max(0.1f, dmgMul));
            float curEhp = run.playerMaxHP + 0.3f * diceCount * meanFace * TargetTurns;

            return Mathf.Min(curDpt / reqDpt, curEhp / reqEhp);
        }

        /// <summary>その層の**次に控えるボス**。 層ボスの id は層番号で引ける。</summary>
        private static CombatSystem.EnemyData NextBoss(int floor)
        {
            int target = Mathf.Clamp(floor, 1, 7);
            return CombatSystem.EnemyDatabase.Get($"boss_layer{target}");
        }

        /// <summary>購入ゲートに掛ける倍率。 **足りないほど金を使う**。
        ///
        /// 値は初期値 (2026-08-10)。 現在のゲート (武器40/パッシブ25/素材20) に対する相対倍率で、
        /// 大幅不足なら武器 14 で買いに行く形になる。 実測での較正はこれから。</summary>
        public static float GateScale(float gap)
        {
            if (gap < 0.7f) return 0.35f;   // 大幅不足: 買えるものは買う
            if (gap < 1.0f) return 0.6f;    // 不足
            if (gap <= 1.3f) return 1.0f;   // 充足: 現状維持
            return 1.5f;                     // 余裕: 貯めてより良い買い物へ
        }

        /// <summary>エリートを踏むか。 足りないなら危険を冒して稼ぐ、 足りているなら避ける。</summary>
        public static bool ShouldTakeElite(float gap, int hp, int maxHp)
        {
            if (gap < 0.7f) return true;                       // 大幅不足: 常に踏む
            if (gap < 1.0f) return maxHp > 0 && hp * 10 >= maxHp * 6;  // 不足: HP 60% 以上なら
            return gap <= 1.3f;                                 // 余裕なら避ける
        }

        /// <summary>休憩を優先するか。 余裕があるうちに HP を貯金しておく。</summary>
        public static bool ShouldPreferRest(float gap) => gap > 1.3f;
    }
}
