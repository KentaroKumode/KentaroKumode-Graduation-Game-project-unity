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
            BossDown.ActiveNow = true;
            BossDownDiag.NoteDown(hadHeavy, canceled);
            Debug.Log($"[ダウン] T{ctx.currentTurn} {currentEnemy?.displayName}: 行動不能・受けるダメージ ×{BossDown.TakenMul:0.0}"
                    + (canceled ? "・溜めていた大技は取り消し" : hadHeavy ? "・(大技は溜まっていない)" : "")
                    + (_down.pending > 0 ? $" (あと {_down.pending} ターン続く)" : ""));
        }

        /// <summary>予告にダウンの欄を入れる (振り直しの判断の仮組み・配線の判断の予告の 2 か所が呼ぶ・写しを持たない)。
        /// ダウンのターンは敵の攻撃 (1 体目・2 体目 / 〈連撃〉の 2 回目) を 0 にする。 ダウンの無い敵では何も変えない
        /// (欄は既定のまま: enemyDown=false・downNotches=null・downNextNotchHp=−1)。</summary>
        private void FillDownTelegraph(ref MutualTurnTelegraph t)
        {
            t.enemyDown = _down.downNow;
            t.downPending = _down.pending;
            t.downNotches = _down.notches;
            t.downBroken = _down.broken;
            t.downNextNotchHp = currentEnemy != null ? _down.NextNotchHp(currentEnemy.maxHP) : -1;
            if (_down.downNow) { t.enemyAttackValue = 0; t.secondaryAttackValue = 0; }
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
