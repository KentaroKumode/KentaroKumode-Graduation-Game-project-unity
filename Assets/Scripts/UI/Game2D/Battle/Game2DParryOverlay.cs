using CombatSystem;
using GameLoop;
using UnityEngine;

namespace UI.Game2D.Battle
{
    /// <summary>
    /// [2026-11-26] 致死パリィのタイミング判定オーバーレイ。
    /// CombatManager が TurnPauseKind.LethalParry で停止している間だけ表示し、
    /// 約 0.60 秒の予兆タメで外円が目標円へ縮む演出を出す。
    /// 入力は最初の 1 回のみ有効（早押し即失敗＝連打無効）。左クリック / Space / Enter 対応。
    /// </summary>
    public class Game2DParryOverlay : MonoBehaviour
    {
        private Game2DBattleSim _sim;
        private bool _active;
        private float _elapsed;
        private float _windowSec;
        private int _windowFrames;
        private int _succSoFar;
        private bool _resolved;
        private bool _resultOk;
        private float _feedbackLeft;
        private Texture2D _whiteTex;

        private const float FeedbackSeconds = 0.35f;

        private void Awake()
        {
            _sim = GetComponent<Game2DBattleSim>();
        }

        private void OnDestroy()
        {
            if (_whiteTex != null) { Destroy(_whiteTex); _whiteTex = null; }
        }

        private void Update()
        {
            if (Application.isBatchMode || GameHooks.BotDriving) return;
            var cm = CombatManager.Instance;
            var p = cm != null ? cm.PendingDecision : null;

            if (_feedbackLeft > 0f)
            {
                _feedbackLeft -= Time.unscaledDeltaTime;
                if (_feedbackLeft <= 0f)
                {
                    _active = false;
                    if (cm != null && cm.PendingDecision?.kind == TurnPauseKind.LethalParry)
                    {
                        cm.AnswerLethalParry(_resultOk);
                        _sim?.OnLethalParryResolved(cm);
                    }
                }
                return;
            }

            if (p != null && p.kind == TurnPauseKind.LethalParry)
            {
                if (!_active)
                {
                    _active = true;
                    _resolved = false;
                    _resultOk = false;
                    _elapsed = 0f;
                    _windowFrames = Mathf.Max(1, p.parryWindowFrames);
                    _windowSec = Mathf.Max(1f / 60f, p.parryWindowSeconds);
                    _succSoFar = p.parrySuccessCountSoFar;
                    // 配線確定のクリック / Space と同一フレームで誤爆して即 MISS にならないよう、開始フレームは入力を読まない
                    return;
                }

                _elapsed += Time.unscaledDeltaTime;
                float target = LethalParry.CueDurationSeconds;
                float halfWin = _windowSec * 0.5f;

                bool pressed = _elapsed >= 0.08f && (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter));
                if (pressed && !_resolved)
                {
                    // 最初の 1 回のみ有効（早押し即失敗＝連打無効）
                    float diff = Mathf.Abs(_elapsed - target);
                    Finish(diff <= halfWin);
                }
                else if (!_resolved && _elapsed > target + halfWin)
                {
                    Finish(false);
                }
            }
            else if (_active && !_resolved)
            {
                _active = false;
            }
        }

        private void Finish(bool ok)
        {
            _resolved = true;
            _resultOk = ok;
            _feedbackLeft = FeedbackSeconds;
            Game2DSfx.Play(ok ? "decide" : "ui_cancel");
        }

        private void EnsureTex()
        {
            if (_whiteTex != null) return;
            _whiteTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _whiteTex.SetPixel(0, 0, Color.white);
            _whiteTex.Apply();
        }

        private void OnGUI()
        {
            if (!_active) return;
            EnsureTex();

            float sw = Screen.width, sh = Screen.height;
            float cx = sw * 0.5f, cy = sh * 0.48f;
            float targetR = Mathf.Min(sw, sh) * 0.11f;
            float startR = targetR * 2.8f;

            // 暗幕
            GUI.color = new Color(0f, 0f, 0f, 0.52f);
            GUI.DrawTexture(new Rect(0, 0, sw, sh), _whiteTex);

            if (!_resolved)
            {
                float tNorm = Mathf.Clamp01(_elapsed / LethalParry.CueDurationSeconds);
                float curR = Mathf.Lerp(startR, targetR, tNorm);
                float halfWin = _windowSec * 0.5f;
                bool inZone = Mathf.Abs(_elapsed - LethalParry.CueDurationSeconds) <= halfWin;

                // 目標円（ターゲット）
                DrawRing(cx, cy, targetR, 4f, inZone ? new Color(1f, 0.92f, 0.35f, 1f) : new Color(0.95f, 0.35f, 0.30f, 0.9f));
                // 縮むリング
                DrawRing(cx, cy, curR, 5f, inZone ? new Color(1f, 1f, 0.65f, 1f) : new Color(1f, 0.85f, 0.55f, 0.95f));

                var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.Max(18, (int)(sh * 0.032f)), fontStyle = FontStyle.Bold };
                GUI.color = Color.white;
                GUI.Label(new Rect(cx - 220, cy - targetR - 56, 440, 40), $"PARRY! ({_windowFrames}f)", style);
                style.fontSize = Mathf.Max(13, (int)(sh * 0.020f));
                GUI.color = new Color(0.9f, 0.9f, 0.9f, 0.85f);
                GUI.Label(new Rect(cx - 220, cy + targetR + 18, 440, 32), "クリック / Space / Enter（1回のみ）", style);
            }
            else
            {
                var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.Max(26, (int)(sh * 0.052f)), fontStyle = FontStyle.Bold };
                GUI.color = _resultOk ? new Color(1f, 0.92f, 0.35f, 1f) : new Color(0.95f, 0.30f, 0.30f, 1f);
                string msg = _resultOk ? $"PARRY!!  〈決死〉+{LethalParry.DesperateBonusPct(_succSoFar + 1)}%" : "MISS...";
                GUI.Label(new Rect(cx - 280, cy - 30, 560, 60), msg, style);
            }
            GUI.color = Color.white;
        }

        private void DrawRing(float cx, float cy, float radius, float thickness, Color col)
        {
            GUI.color = col;
            const int segs = 48;
            for (int i = 0; i < segs; i++)
            {
                float a = (i / (float)segs) * Mathf.PI * 2f;
                float x = cx + Mathf.Cos(a) * radius;
                float y = cy + Mathf.Sin(a) * radius;
                GUI.DrawTexture(new Rect(x - thickness * 0.5f, y - thickness * 0.5f, thickness, thickness), _whiteTex);
            }
        }
    }
}
