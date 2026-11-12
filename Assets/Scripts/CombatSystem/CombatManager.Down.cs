using ItemSystem.PassiveSkills;
using UnityEngine;

namespace CombatSystem
{
    /// <summary>CombatManager: ボスのダウン (HP の目盛り・2026-11-13・GAME.md §6-15)。 規則の式は <see cref="BossDown"/> にだけある (ここは状態を進めるだけ)。</summary>
    public partial class CombatManager
    {
        /// <summary>この戦闘 (ヴェスカは段) のダウンの状態。 戦闘の始めと形態の交代で作り直す。</summary>
        private readonly BossDownState _down = new BossDownState();

        /// <summary>ダウンの状態 (画面・BOT が読む)。 書き換えないこと。</summary>
        public BossDownState Down => _down;

        /// <summary>戦闘の始め・形態の交代: 敵の目盛りを入れ直す (目盛りの無い敵・測定の口で止めた時は無し)。</summary>
        private void ResetBossDown(EnemyData enemy, bool newCombat)
        {
            _down.Reset(BossParams.DownNotches(enemy?.id));
            BossDown.ActiveNow = false;
            if (newCombat) Escalation.HeavyCycleOffset = 0;
            BossDownDiag.BeginFight(enemy?.id, _down.Enabled);
        }

        /// <summary>ターンの頭 (ターン番号を進めた後・敵が振る前): 待っているダウンがあれば、 このターンをダウンにする。
        /// 溜めていた大技は取り消し、 大技の周期は次のターンから頭に戻す (<see cref="Escalation.HeavyCycleOffset"/>)。</summary>
        private void BeginBossDownTurn(CombatContext ctx)
        {
            BossDown.ActiveNow = false;
            if (!_down.Enabled || ctx == null) { _down.downNow = false; return; }
            if (!_down.BeginTurn()) return;
            bool hadHeavy = currentEnemy != null && currentEnemy.heavyPeriod > 0;
            bool canceled = hadHeavy && Escalation.IsHeavyTurn(currentEnemy, ctx.currentTurn);   // 起点を戻す前に見る
            Escalation.HeavyCycleOffset = ctx.currentTurn;
            // エスカレーション (ターンで進む敵の段階) もダウンのターンは進めない: 冷却材と同じ遅延の口に 1 ターン足す (予告・BOT の見込みも同じ口を読む)
            YachtRoleEffects.AddEscalationDelay(ctx, 1);
            // 〈遺物学者〉の抽選も止まる (OnTurnStart を鳴らさない) ので、 前のターンの抽選の名前を予告に残さない
            ctx.vescaDrawnRelics = new System.Collections.Generic.List<string>();
            BossDown.ActiveNow = true;
            BossDownDiag.NoteDown(hadHeavy, canceled);
            Debug.Log($"[ダウン] T{ctx.currentTurn} {currentEnemy?.displayName}: 行動不能・受けるダメージ ×{BossDown.TakenMul:0.0}"
                    + (canceled ? "・溜めていた大技は取り消し" : hadHeavy ? "・(大技は溜まっていない)" : "")
                    + (_down.pending > 0 ? $" (あと {_down.pending} ターン続く)" : ""));
        }

        /// <summary>ターンの終わり (ターン終了時の削りを拾った後): 新しく割った目盛りを数え、 その数だけ次のターンからダウンを待たせる。</summary>
        private void NoteBossDownTurnEnd()
        {
            if (!_down.Enabled || currentEnemy == null || enemyHP <= 0) return;
            int d = _down.NoteTurnEnd(enemyHP, currentEnemy.maxHP);
            if (d > 0) Debug.Log($"[ダウン] 目盛りを {d} つ割った (HP {enemyHP}/{currentEnemy.maxHP}) → 次のターンからダウン {_down.pending} ターン");
        }

        /// <summary>戦闘の終わり: ダウンの倍率を戻す。</summary>
        private void EndBossDown()
        {
            BossDown.ActiveNow = false;
            _down.downNow = false;
            Escalation.HeavyCycleOffset = 0;
        }
    }
}
