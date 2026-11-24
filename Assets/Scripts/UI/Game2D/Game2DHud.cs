using GameLoop;
using UnityEngine;

namespace UI.Game2D
{
    /// <summary>
    /// 2D 画面の GUI をゲームの状態へつなぐ (2026-10-19)。 航行の計器 (所持金・HP・物資・測量 / 囮罠 / 待つ・天気・ターン・所持品) は
    /// 全部 <see cref="Game2DNavExtras"/> が液晶の中の暗い帯として描く (2026-11-24・旧い筐体の部品 = GOLD の入れ物・ボタン盤・レーダー・資源バー・数字ドラムは削除・GAME.md §24)。
    /// ここは、 表示の出し入れ (ロード画面・戦闘) のばねと、 所持金の増減の知らせだけを持つ。
    /// </summary>
    public class Game2DHud : MonoBehaviour
    {
        [Tooltip("航行の計器 (天気・ターン・所持金・HP・物資・操作・所持品)。 戦闘中は引っ込む")] public Game2DNavExtras extras;
        [Tooltip("戦闘の流れ (試作)")] public Battle.Game2DBattleSim battleSim;

        [Header("全体")]
        public GameObject root;

        private bool _battle;
        private Game2DSpring _spNav;      // 航行用の GUI の引っ込み具合 (0 = 出ている / 1 = 画面の外)
        /// <summary>会心の衝撃で GUI 全体を揺らす量 (dot)。 戦闘演出 (Game2DBattleFx) が毎フレーム入れる。</summary>
        public static Vector2 ShakeDots;
        private int _lastShownPhase = -1;
        private bool _wasLoading;

        /// <summary>旧い筐体の部品の GameObject の名前 (2026-11-24 に削除)。 builder で作り直していないシーンに残っていれば、 起動時に消す
        /// (スクリプトの外れた絵 = 錆びた GOLD の箱・金属のボタン・HP の計器が出たままにならないように)。</summary>
        private static readonly string[] LegacyParts = { "ResBar", "Radar", "NavOnly", "GoldPot", "NavPanelCtl" };

        private void Awake()
        {
            if (root == null) return;
            foreach (var n in LegacyParts) { var t = root.transform.Find(n); if (t != null) Destroy(t.gameObject); }
        }

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

        private void LateUpdate()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;
            var phase = gm.CurrentPhase;
            bool loading = Game2DLoadingScreen.Blocking || Game2DEndingScreen.Blocking || Game2DEventScreen.Showing;      // エンディングの画面 (#328)・イベントの画面 (2026-11-16) も液晶の全面を使う
            bool show = phase != GameManager.GamePhase.Title && phase != GameManager.GamePhase.GameOver && !loading;      // ロード画面は液晶の全面を使う
            if (root != null && root.activeSelf != show) root.SetActive(show);
            // ロード画面が抜けた直後: 計器盤を引っ込めた位置から、 戦闘から戻る時と同じばねで滑り込ませる (ぱっと出さない)
            if (_wasLoading && !loading) { _spNav.Set(1f); Game2DSfx.Play("gui_in"); }
            _wasLoading = loading;
            if (!show) return;

            bool battle = phase == GameManager.GamePhase.Combat || Battle.Game2DBattleFx.Showing;
            if (battle != _battle || (int)phase != _lastShownPhase) { if (battle != _battle) Game2DSfx.Play(battle ? "gui_out" : "gui_in"); _battle = battle; _lastShownPhase = (int)phase; }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            DebugGold(gm);
#endif
            Animate();
            var run = gm.Run;
            if (run != null && extras != null) extras.NoteGold(run);      // 所持金の増減の手応え (点滅・粒・硬貨の音)。 戦闘中も数える (表示は戦闘の後)
        }

        /// <summary>航行の計器は戦闘中は画面の外へ引っ込む (ばね)。 会心の衝撃では GUI 全体を揺らす。 引っ込んでいる間はボタンを受けない。</summary>
        private void Animate()
        {
            float target = _battle ? 1f : 0f, dt = Mathf.Min(0.05f, Time.unscaledDeltaTime);
            // 出し入れはばね: 引っ込む時は少し遅れて勢いよく出ていき、 戻る時は行き過ぎて弾んで収まる
            _spNav.Go(target, 0f); _spNav.Step(dt);
            if (extras != null) extras.Drive(_spNav.Value(100f));
            if (root != null)
            {
                var hr = (RectTransform)root.transform; var sk = new Vector2(Mathf.Round(ShakeDots.x), -Mathf.Round(ShakeDots.y));
                hr.offsetMin = sk; hr.offsetMax = sk;
            }
        }

    }
}
