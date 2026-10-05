using System;
using GameLoop;
using MapSystem.FreeMove;
using UnityEngine;

namespace MapSystem
{
    /// <summary>自由移動の層の時間の速さ。</summary>
    public enum FreeMapTimeMode
    {
        Paused = 0,
        Normal = 1,
        Fast = 2,
    }

    /// <summary>
    /// 人のための時計 (2026-09-28・docs/GAME.md §5)。 <b>中身は持たない薄い MonoBehaviour</b> ──
    /// 実時間に合わせて <see cref="GameManager.AdvanceFreeMap"/> を 1 刻みずつ呼び、 起きたことに応じて
    /// 自動で止める・早送りを解く (規則は素の C# の <see cref="FreeMapClockPolicy"/>)。
    ///
    /// <para>操作 (§23-21 の Highfleet 式): <b>スペースを短く押すと停止⇄順速、 0.5 秒以上押し続けると
    /// 押している間だけ早送り</b> (停止中から長押しすれば離した時に停止へ戻る)。 最初は停止。
    /// 順速 1 手番 ≒ 6 秒・早送り ≒ 0.6 秒。 早送りは「点に着いた」「察知された」「魔石の反応が一段上がった」で
    /// 自動解除して順速へ戻る (押したままでは再開しない)。 <b>察知・目視で時間が止まる</b> (自動の一時停止は
    /// 長押しより優先) ── このゲームの肝なので、 契機は FreeMapSim のイベントで正確に受ける。</para>
    ///
    /// <para>BOT はこの時計を使わない (<see cref="GameManager.TravelSync"/> で同期的に進める)。
    /// 場面にこの時計が居ない時、 旧来の <see cref="GameManager.MoveToNode"/> は着くまで同期的に進む。</para>
    /// </summary>
    public class FreeMapClock : MonoBehaviour
    {
        private static FreeMapClock _active;
        private static bool _shuttingDown;

        /// <summary>有効な時計。 無ければ null (＝ MoveToNode は同期的に進む)。</summary>
        public static FreeMapClock Active => _shuttingDown ? null : _active;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _active = null;
            _shuttingDown = false;
        }

        [Tooltip("長押しで早送りになるまでの秒数")]
        [SerializeField] private float holdToFastSeconds = 0.5f;
        [SerializeField] private KeyCode key = KeyCode.Space;

        public FreeMapTimeMode Mode { get; private set; } = FreeMapTimeMode.Paused;

        /// <summary>速さが変わった (自動の停止・解除を含む)。</summary>
        public event Action<FreeMapTimeMode> OnModeChanged;
        /// <summary>自動で時間が止まった。 引数は止めた契機 (察知・目視など)。 演出 (音程が下がりきる音) はここを購読する。</summary>
        public event Action<FreeMapEvent> OnAutoPause;
        /// <summary>早送りが自動で解けた。</summary>
        public event Action<FreeMapEvent> OnFastCancelled;

        private float _acc;
        private float _pressTime = -1f;
        private bool _holdActive;          // 長押しで早送り中
        private bool _holdFromPause;       // 停止中から長押しした (離したら停止へ戻す)
        private bool _holdCancelled;       // 自動解除された長押し (離すまで早送りに戻さない)

        void OnEnable()
        {
            _shuttingDown = false;
            if (_active == null) _active = this;
        }

        void OnDisable()
        {
            if (_active == this) _active = null;
        }

        void OnApplicationQuit() => _shuttingDown = true;

        // ================================================================
        //  操作 (UI ボタンからも呼べる)
        // ================================================================

        public void SetMode(FreeMapTimeMode mode)
        {
            if (Mode == mode) return;
            Mode = mode;
            _acc = 0f;
            OnModeChanged?.Invoke(mode);
        }

        /// <summary>停止⇄順速。</summary>
        public void TogglePause() => SetMode(Mode == FreeMapTimeMode.Paused ? FreeMapTimeMode.Normal : FreeMapTimeMode.Paused);

        void Update()
        {
            ReadKey();

            var gm = GameManager.Instance;
            if (gm == null || gm.FreeMap == null || gm.CurrentPhase != GameManager.GamePhase.MapNavigation || Mode == FreeMapTimeMode.Paused)
            {
                _acc = 0f;
                return;
            }

            float spt = Mode == FreeMapTimeMode.Fast ? FreeMapParams.SecondsPerTickFast : FreeMapParams.SecondsPerTickNormal;
            _acc += Time.unscaledDeltaTime;
            // 1 フレームで進めすぎない (処理落ちの後に何十刻みも一気に進めると、 止まるべき所を飛び越して見える)
            int budget = 20;
            while (_acc >= spt && budget-- > 0)
            {
                _acc -= spt;
                var ev = gm.AdvanceFreeMap(1);
                if (FreeMapClockPolicy.ShouldPause(ev))
                {
                    _holdActive = false; _holdFromPause = false; _holdCancelled = _pressTime >= 0f;
                    SetMode(FreeMapTimeMode.Paused);
                    OnAutoPause?.Invoke(ev);
                    break;
                }
                if (Mode == FreeMapTimeMode.Fast && FreeMapClockPolicy.ShouldCancelFast(ev))
                {
                    _holdActive = false; _holdFromPause = false; _holdCancelled = _pressTime >= 0f;
                    SetMode(FreeMapTimeMode.Normal);
                    OnFastCancelled?.Invoke(ev);
                    break;
                }
                if (gm.CurrentPhase != GameManager.GamePhase.MapNavigation) break;
            }
            if (budget <= 0) _acc = 0f;
        }

        private void ReadKey()
        {
            if (Input.GetKeyDown(key))
            {
                _pressTime = Time.unscaledTime;
                _holdCancelled = false;
            }
            if (_pressTime >= 0f && Input.GetKey(key) && !_holdActive && !_holdCancelled
                && Time.unscaledTime - _pressTime >= holdToFastSeconds)
            {
                _holdActive = true;
                _holdFromPause = Mode == FreeMapTimeMode.Paused;
                SetMode(FreeMapTimeMode.Fast);
            }
            if (_pressTime >= 0f && Input.GetKeyUp(key))
            {
                if (_holdActive)
                    SetMode(_holdFromPause ? FreeMapTimeMode.Paused : FreeMapTimeMode.Normal);
                else if (!_holdCancelled)
                    TogglePause();   // 短く押した
                _pressTime = -1f;
                _holdActive = false;
                _holdFromPause = false;
                _holdCancelled = false;
            }
        }
    }
}
