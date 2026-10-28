using GameLoop;
using UnityEngine;

namespace UI.Game2D
{
    /// <summary>
    /// 2D 画面の GUI をゲームの状態へつなぐ (2026-10-19)。 配置の正本は docs/design/map-prototype.html (描画の順は 資源バー → レーダー → 操作パネル → GOLD の入れ物)。
    /// 資源バー (bar_layer1〜4) は HP と、 航行 = 物資 / 戦闘 = シールドの板を持つ。
    /// GOLD の入れ物・レーダー・操作パネルは、 それぞれの部品 (Game2DGoldPot / Game2DRadar / Game2DNavPanel) が HTML の動きで描く。
    /// </summary>
    public class Game2DHud : MonoBehaviour
    {
        [Header("常にある")]
        public Game2DGoldPot gold;
        public Game2DRadar radar;
        public Game2DBar hpBar;
        public Game2DResBar resBar;

        [Header("航行だけ")]
        public GameObject navOnly;
        public Game2DNavPanel panel;

        [Header("戦闘だけ")]
        public GameObject battleOnly;
        [Tooltip("航行の計器の足りない分 (天気・層・警告・待つ・職業・状態異常・アイテム枠・脱出)。 戦闘中は引っ込む")] public Game2DNavExtras extras;
        [Tooltip("戦闘の GUI (仮置き)。 航行用の GUI と入れ替わりに入ってくる")] public Game2DBattleHud battleHud;
        [Tooltip("戦闘の流れ (試作)。 動いている間は HP とシールドの表示をこちらの値にする")] public Battle.Game2DBattleSim battleSim;

        [Header("全体")]
        public GameObject root;

        private Game2DMapView _view;
        private bool _battle;
        private Game2DSpring _spNav, _spGold;      // 航行用の GUI の引っ込み具合 (0 = 出ている / 1 = 画面の外)
        private Vector2 _goldBase; private bool _radarInit;
        /// <summary>会心の衝撃で GUI 全体を揺らす量 (dot)。 戦闘演出 (Game2DBattleFx) が毎フレーム入れる。</summary>
        public static Vector2 ShakeDots;
        private int _lastShownPhase = -1;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // ── 試験用: ゴールドを任意で振り込む (HTML の「GOLD +N（試験）」) ──
        //   = / テンキー+ : +10   Shift + = : +100   F9 : 数字を打って Enter で、 その額を振り込む (Esc でやめる)   - : 10 引く (振り込みの演出は出ない)
        [Header("試験用 (エディタ / 開発ビルドだけ)")]
        public int quickGain = 10, quickGainBig = 100;
        private bool _entering; private string _entry = "";

        private void DebugGold(GameManager gm)
        {
            var run = gm.Run; if (run == null) return;
            if (_entering)
            {
                foreach (char c in Input.inputString)
                {
                    if (c >= '0' && c <= '9' && _entry.Length < 6) { _entry += c; Debug.Log("[GOLD 試験] 入力: " + _entry); }
                    else if (c == '' && _entry.Length > 0) { _entry = _entry.Substring(0, _entry.Length - 1); Debug.Log("[GOLD 試験] 入力: " + _entry); }
                }
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                {
                    _entering = false;
                    if (int.TryParse(_entry, out int n) && n > 0) { run.coins += n; Debug.Log("[GOLD 試験] +" + n + " G"); }
                    _entry = "";
                }
                else if (Input.GetKeyDown(KeyCode.Escape)) { _entering = false; _entry = ""; Debug.Log("[GOLD 試験] やめた"); }
                return;
            }
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus)) { int n = shift ? quickGainBig : quickGain; run.coins += n; Debug.Log("[GOLD 試験] +" + n + " G"); }
            if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus)) { run.coins = Mathf.Max(0, run.coins - quickGain); }
            if (Input.GetKeyDown(KeyCode.F9)) { _entering = true; _entry = ""; Debug.Log("[GOLD 試験] 額を打って Enter (Esc でやめる)"); }
        }
#endif

        private void Start()
        {
            if (panel == null) return;
            panel.actions[0].AddListener(() => { var gm = GameManager.Instance; if (gm != null && gm.Run != null) { Consumables.Use(gm.Run, ItemIds.Lure); Game2DSfx.Play("lure_set"); } });
            panel.actions[1].AddListener(() => GameManager.Instance?.UseFlare());
            panel.actions[2].AddListener(() => GameManager.Instance?.ReconTurn());
        }

        private void LateUpdate()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;
            var phase = gm.CurrentPhase;
            bool show = phase != GameManager.GamePhase.Title && phase != GameManager.GamePhase.GameOver;
            if (root != null && root.activeSelf != show) root.SetActive(show);
            if (!show) return;

            bool battle = phase == GameManager.GamePhase.Combat || Battle.Game2DBattleFx.Showing;
            if (battle != _battle || (int)phase != _lastShownPhase) { if (battle != _battle) Game2DSfx.Play(battle ? "gui_out" : "gui_in"); _battle = battle; _lastShownPhase = (int)phase; ApplyMode(); }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            DebugGold(gm);
#endif
            Animate();
            var run = gm.Run;
            if (run == null) return;
            if (gold != null) gold.SetGold(run.coins);
            if (resBar != null)
            {
                if (_view == null) _view = FindObjectOfType<Game2DMapView>();
                bool simOn = Battle.Game2DBattleSim.Active && battleSim != null;
                float hp = simOn ? battleSim.playerHp / (float)battleSim.playerMax : (run.playerMaxHP > 0 ? run.playerHP / (float)run.playerMaxHP : 0f);
                bool nav = _view != null && !_battle;
                float frac;
                if (_battle && simOn) frac = Mathf.Clamp01(battleSim.GetWired().blk / 18f);      // 試作の戦闘: 今ターンの防御の配線 (最大 18)
                else if (_battle)
                {
                    // 戦闘 = シールド (吸収量 ÷ 最大 HP)
                    var ctx = ItemSystem.PassiveSkills.PassiveSkillManager.Instance?.Context;
                    frac = ctx != null && run.playerMaxHP > 0 ? Mathf.Clamp01(ctx.consShield / (float)run.playerMaxHP) : 0f;
                }
                else frac = run.provisionCap > 0 ? run.provision / (float)run.provisionCap : 0f;
                int mainNum = _battle && simOn ? battleSim.GetWired().blk : _battle ? (ItemSystem.PassiveSkills.PassiveSkillManager.Instance?.Context?.consShield ?? 0) : run.provision;
                resBar.Set(hp, frac, _battle, nav ? _view.SupplyPreview : 0f, nav ? _view.SupplyPreviewText : null, simOn ? battleSim.playerHp : run.playerHP, mainNum);
            }

            var sim = gm.FreeMap;
            if (radar != null)
            {
                int level = 0, bearing4 = -1;
                if (sim != null)
                {
                    var st = sim.Stone(); level = st.level;
                    if (level > 0)
                    {
                        // レーダーは斜めの 4 方向 (0 = 南東 / 1 = 南西 / 2 = 北西 / 3 = 北東・扇は東西南北から 45° 傾けた 90° ずつ)。 追跡者のいる方角が入る扇を選ぶ
                        var me = sim.Layout.pos[sim.Me]; var foe = sim.Layout.pos[sim.FoeNode];
                        float a = Mathf.Atan2((float)(foe.y - me.y), (float)(foe.x - me.x));
                        bearing4 = ((Mathf.FloorToInt(a / (Mathf.PI / 2f)) % 4) + 4) % 4;
                    }
                }
                radar.SetStone(level, bearing4);
            }

            if (panel != null)
            {
                panel.buttonsActive = !_battle;
                bool canAct = !_battle && phase == GameManager.GamePhase.MapNavigation && sim != null && !sim.InFight && !sim.Dead;
                int lures = CountLures(run);
                // 照明は物資で払う (1 回 FlareSupply)。 残数 = 今の物資であと何回撃てるか
                int flares = run.provision / Mathf.Max(1, MapSystem.FreeMove.FreeMapParams.FlareSupply);
                panel.SetDisabled(0, !canAct || lures <= 0);
                panel.SetDisabled(1, !canAct || sim.FlareOn || flares <= 0);
                panel.SetDisabled(2, !canAct);
                panel.SetCounts(Mathf.Min(3, lures), Mathf.Min(3, flares));
            }
        }

        private static int CountLures(RunState run)
        {
            int n = 0;
            if (run?.ownedConsumables != null) foreach (var c in run.ownedConsumables) if (c == ItemIds.Lure) n++;
            return n;
        }

        /// <summary>
        /// 航行用の GUI (操作パネル) は、 戦闘中は画面の外へ引っ込む (左へ)。 魔石レーダーは資源バーの液晶なので、 引っ込めない。 会心の衝撃では GUI 全体を揺らす。
        /// 引っ込んでいる間はボタンを受けない。
        /// </summary>
        private void Animate()
        {
            float target = _battle ? 1f : 0f, dt = Mathf.Min(0.05f, Time.unscaledDeltaTime);
            // 出し入れはばね: 引っ込む時は少し遅れて勢いよく出ていき、 戻る時は行き過ぎて弾んで収まる。 部品ごとに少しずつずらす (パネル → GOLD)
            _spNav.Go(target, 0f); _spGold.Go(target, 0.14f);
            _spNav.Step(dt); _spGold.Step(dt);
            if (navOnly != null)
            {
                navOnly.SetActive(!(_spNav.tgt > 0.5f && _spNav.x > 0.995f && _spNav.Settled));
                var nr = (RectTransform)navOnly.transform; float ox = -Mathf.Round(_spNav.Value(100f) * 100f);
                nr.offsetMin = new Vector2(ox, 0); nr.offsetMax = new Vector2(ox, 0);
            }
            if (!_radarInit)
            {
                if (gold != null) _goldBase = ((RectTransform)gold.transform).anchoredPosition;
                _radarInit = true;
            }
            if (gold != null) ((RectTransform)gold.transform).anchoredPosition = _goldBase + new Vector2(0, Mathf.Round(_spGold.Value(110f) * 110f));      // GOLD の入れ物は戦闘中は出さない (上へ引っ込む)
            if (extras != null) extras.Drive(_spNav.Value(100f));
            if (battleHud != null) { battleHud.Drive(_battle, dt); if (battleOnly != null) battleOnly.SetActive(battleHud.Visible); }      // 戦闘の GUI は、 入ってくる間・出ていく間も生かす
            if (root != null)
            {
                var hr = (RectTransform)root.transform; var sk = new Vector2(Mathf.Round(ShakeDots.x), -Mathf.Round(ShakeDots.y));
                hr.offsetMin = sk; hr.offsetMax = sk;
            }
        }

        /// <summary>航行 / 戦闘の配置 (HTML の NAV / BAT)。</summary>
        private void ApplyMode()
        {
        }
    }
}
