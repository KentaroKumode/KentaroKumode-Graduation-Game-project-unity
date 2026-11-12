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

        /// <summary>この段で博士の「止まらない時計」が効いている (目盛りを 1 つ足した・<see cref="GameLoop.VescaClock"/>)。 画面のヴェスカの状態の欄が読む。</summary>
        public bool VescaClockActive { get; private set; }

        /// <summary>戦闘の始め・形態の交代: 敵の目盛りを入れ直す (目盛りの無い敵・測定の口で止めた時は無し)。
        /// **目盛りの並びを作るのはここ 1 か所** (時計の目盛りもここで足す)。 画面・BOT は <see cref="Down"/> の並びを読む。</summary>
        private void ResetBossDown(EnemyData enemy, bool newCombat)
        {
            bool clock = GameLoop.VescaClock.Held(GameLoop.GameManager.Instance?.Run);
            _down.Reset(BossParams.DownNotches(enemy?.id, clock));
            VescaClockActive = clock && _down.Enabled && GameLoop.BossIds.VescaStageOf(enemy?.id) > 0;   // ダウンを止めた測定の口では効かない
            // 戦闘の始めの地の文 (最初の段だけ 1 回)。 戦闘の画面に文の欄が無いので戦闘ログの行として出す (2026-11-13)。 ヴェスカの台詞は置かない (作者の決定)
            if (VescaClockActive && newCombat && enemy?.id == GameLoop.BossIds.VescaP1)
                Debug.Log("[戦闘] " + GameLoop.VescaClock.BattleLine);
            if (VescaClockActive) Debug.Log($"[ダウン] 〈{GameLoop.VescaClock.Name}〉: {enemy?.displayName} の目盛りに {BossParams.VescaClockNotch * 100f:0}% を足した");
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
