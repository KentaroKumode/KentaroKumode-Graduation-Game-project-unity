using CombatSystem;
using GameLoop;
using UI.Game2D.Battle;
using UnityEngine;

namespace UI.Game2D
{
    /// <summary>
    /// 敵の詳細 (#15・<b>仮置き</b>・2026-11-11)。 戦闘中に、 いま戦っている敵の HP・防御・攻撃の幅・パッシブの名前と説明・今の状態 (毒・出血・構え・大技・激化) を
    /// 共通の板のモーダルで見る。 戦闘の HUD (<see cref="Game2DBattleHud"/>) の配置には手を付けず、 上に重ねるだけ。
    /// <para>開き方: Tab か、 液晶の中央の敵 (x 190〜290・y 84〜204 の範囲) をクリック。 閉じる: Tab・Esc・「閉じる」。 開いている間は戦闘のキー操作を止める
    /// (<see cref="Game2DBattleSim"/> が <see cref="Game2DModal.Active"/> を見る)。 値は開いている間も追って描き直す。 戦闘が終わったら閉じる。</para>
    /// 本番の絵は別 issue (#101)。
    /// </summary>
    public class Game2DEnemyPanel : MonoBehaviour
    {
        public Game2DModal modal;
        /// <summary>敵の絵の範囲 (GUI の dot)。 戦闘の演出の敵は中心 x 240・足元 y 204・100×120 (BattleMotion)。</summary>
        private static readonly RectInt EnemyArea = new RectInt(190, 84, 100, 120);

        private ScreenDef _def;
        private Game2DBattleSim _sim; private Game2DScreen _screen;
        private int _hash; private float _nextPoll;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Attach()
        {
            var m = FindObjectOfType<Game2DModal>();
            if (m != null && m.GetComponent<Game2DEnemyPanel>() == null) m.gameObject.AddComponent<Game2DEnemyPanel>().modal = m;
        }

        private bool IsShown => modal != null && _def != null && modal.IsOpen && modal.Current == _def;

        private static bool InCombat()
        {
            var gm = GameManager.Instance;
            return gm != null && gm.CurrentPhase == GameManager.GamePhase.Combat && Game2DBattleSim.Active && CombatManager.Instance != null && CombatManager.Instance.CurrentEnemy != null;
        }

        private void Update()
        {
            if (modal == null) return;
            if (_def != null && !IsShown) _def = null;
            if (Application.isBatchMode || GameHooks.BotDriving) { if (IsShown) Close(); return; }
            if (IsShown)
            {
                if (!InCombat() || Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
                if (Time.unscaledTime >= _nextPoll) { _nextPoll = Time.unscaledTime + 0.2f; int h = StateHash(); if (h != _hash) { _hash = h; _def = Build(); modal.Replace(_def); } }
                return;
            }
            if (modal.IsOpen || Game2DModal.Active || !InCombat()) return;
            // 手札・出目をつかんでいる間と、 落とした瞬間は開かない (2026-11-11)。 敵の範囲は手札 (x 318〜)・差し込み口 (x 〜186) と重ならないので
            //   押した所からつかみは始まらないが、 Tab と、 フレームの順で押下と離しが前後する場合のために、 戦闘の画面の印を見る
            if (Game2DBattleHud.PointerBusy) return;
            if (Input.GetKeyDown(KeyCode.Tab) || (Input.GetMouseButtonDown(0) && ClickedEnemy())) Open();
        }

        private bool ClickedEnemy()
        {
            if (_screen == null) _screen = FindObjectOfType<Game2DScreen>();
            if (_screen == null || !Game2DPointer.TryDot(_screen, out var d)) return false;
            return d.x >= EnemyArea.x && d.x < EnemyArea.xMax && d.y >= EnemyArea.y && d.y < EnemyArea.yMax;
        }

        private void Open() { _hash = StateHash(); _def = Build(); modal.Open(_def); }
        private void Close() { if (IsShown) modal.Close(); _def = null; }

        private Game2DBattleSim Sim { get { if (_sim == null) _sim = FindObjectOfType<Game2DBattleSim>(); return _sim; } }

        private static ItemSystem.PassiveSkills.CombatContext Ctx => ItemSystem.PassiveSkills.PassiveSkillManager.Instance?.Context;

        private int StateHash()
        {
            var cm = CombatManager.Instance; var s = Sim; var ctx = Ctx;
            unchecked
            {
                int h = cm != null && cm.CurrentEnemy != null ? cm.CurrentEnemy.id.GetHashCode() : 0;
                h = h * 31 + (cm != null ? cm.EnemyHP : 0);
                h = h * 31 + (cm != null ? cm.SecondaryEnemyHP : 0);
                if (ctx != null) { h = h * 31 + ctx.enemyDefense; h = h * 31 + ctx.EnemyPoison; h = h * 31 + ctx.enemyBleedStacks; }
                if (s != null) { h = h * 31 + s.turn; h = h * 31 + s.eatk; h = h * 31 + s.Stage; h = h * 31 + s.guardIn; h = h * 31 + s.heavyIn; h = h * 31 + (int)s.phase; h = h * 31 + s.downPending + (s.down ? 7 : 0) + s.downBroken * 13; }
                return h;
            }
        }

        private static string AttackRange(EnemyData e)
        {
            if (e.UsesAttackRoll) return e.attackRollMin + "〜" + e.attackRollMax;
            return e.EffectiveRollCount + " 個 × 1〜" + e.EffectiveRollMax;
        }

        private ScreenDef Build()
        {
            var cm = CombatManager.Instance; var e = cm.CurrentEnemy; var s = Sim; var ctx = Ctx;
            var d = new ScreenDef { id = "enemy_panel", jp = true, w = 460, cols = 1, rowH = 15, bodyLines = 8 };
            d.title = "敵の詳細";
            d.sub = (string.IsNullOrEmpty(e.displayName) ? e.id : e.displayName) + (e.elite ? " (エリート)" : "") + (BossIds.IsBoss(e.id) ? " (ボス)" : "");
            var sb = new System.Text.StringBuilder();
            int def = ctx != null ? ctx.enemyDefense : e.defense, pen = ctx != null ? ctx.enemyArmorPen : e.armorPen;
            sb.Append("HP ").Append(cm.EnemyHP).Append('/').Append(cm.EnemyMaxHP)
              .Append("   防御 ").Append(def).Append(def != e.defense ? " (元 " + e.defense + ")" : "")
              .Append("   貫通 ").Append(pen).Append('\n');
            sb.Append("攻撃の幅 ").Append(AttackRange(e));
            bool known = s != null && s.phase != Game2DBattleSim.P.ERoll && s.phase != Game2DBattleSim.P.Won && s.phase != Game2DBattleSim.P.Lost;
            if (known) sb.Append("   このターンの攻撃 ").Append(s.eatk);
            sb.Append('\n');
            if (s != null)
            {
                sb.Append(s.turn).Append(" ターン目   激化の段階 ").Append(s.Stage).Append(s.NextStageIn >= 0 ? " (次の段階まで " + s.NextStageIn + " ターン)" : " (最大)").Append('\n');
                var plan = new System.Collections.Generic.List<string>();
                if (s.guardIn == 0) plan.Add("このターンは構え (防御が上がる)"); else if (s.guardIn > 0) plan.Add("構えまで " + s.guardIn + " ターン");
                if (s.heavyIn == 0) plan.Add("このターンは大技"); else if (s.heavyIn > 0) plan.Add("大技まで " + s.heavyIn + " ターン");
                // ボスのダウン (2026-11-13): ダウン中・後に続くダウン・次の目盛りまでの残り
                if (s.down) plan.Add("このターンはダウン (攻撃しない・受けるダメージ ×" + CombatSystem.BossDown.TakenMul.ToString("0.0")
                                     + (e.heavyPeriod > 0 ? "・溜めていた大技は取り消し" : "") + ")");
                if (s.downPending > 0) plan.Add("ダウンはあと " + s.downPending + " ターン続く");
                if (s.downNotchHp >= 0) plan.Add("次の目盛りまで " + System.Math.Max(0, cm.EnemyHP - s.downNotchHp));
                if (plan.Count > 0) sb.Append(string.Join("・", plan)).Append('\n');
                if (s.downNotches != null)
                {
                    var ns = new System.Collections.Generic.List<string>();
                    for (int i = 0; i < s.downNotches.Length; i++) ns.Add(Mathf.RoundToInt(s.downNotches[i] * 100f) + "%" + (i < s.downBroken ? " (割った)" : ""));
                    sb.Append("ダウンの目盛り: ").Append(string.Join("・", ns)).Append('\n');
                }
            }
            int poison = ctx != null ? ctx.EnemyPoison : 0, bleed = ctx != null ? ctx.enemyBleedStacks : 0;
            sb.Append("状態: ").Append(poison > 0 || bleed > 0 ? (poison > 0 ? "毒 " + poison : "") + (poison > 0 && bleed > 0 ? "・" : "") + (bleed > 0 ? "出血 " + bleed : "") : "なし");
            var sec = cm.SecondaryEnemy;
            if (sec != null && cm.SecondaryEnemyHP > 0) sb.Append("\n2 体目: ").Append(string.IsNullOrEmpty(sec.displayName) ? sec.id : sec.displayName).Append("  HP ").Append(cm.SecondaryEnemyHP).Append('/').Append(sec.maxHP);
            d.body = sb.ToString();
            if (e.passiveSkills != null)
                foreach (var ps in e.passiveSkills)
                    if (ps != null && !string.IsNullOrEmpty(ps.skillName))
                        d.Items.Add(new ModalItem { name = "〈" + ps.skillName + "〉", desc = ps.description });
            if (d.Items.Count == 0) d.Items.Add(new ModalItem { name = "パッシブなし", desc = "この敵は特別な力を持たない。", disabled = true });
            if (d.Items.Count > 3) d.cols = 2;      // 多い敵は 2 列 (窓の高さに収める)
            d.buttons.Add(new ModalBtn { label = "閉じる", action = "close", primary = true });
            d.footer = "パッシブを選ぶと説明が出る / Tab か Esc で閉じる";
            d.onAction = (a, _) => { if (a == "close") Close(); };
            return d;
        }
    }
}
