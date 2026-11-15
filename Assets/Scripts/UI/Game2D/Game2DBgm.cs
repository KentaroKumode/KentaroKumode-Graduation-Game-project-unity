using System.Collections.Generic;
using GameLoop;
using UnityEngine;

namespace UI.Game2D
{
    /// <summary>
    /// 場面の BGM を選んで鳴らす係 (#313・2026-11-15)。 実行時に 1 つ作る・シーンを跨いで残る。 タイトルの曲は <see cref="TitleBgm"/> が鳴らし、 タイトル以外の場面をここが受け持つ。
    ///
    /// <para><b>選び方</b> (<see cref="Want"/>・毎フレーム): ロード画面 = bgm_loading / 戦闘 = 敵の種類 (灰燼の王 = bgm_boss_ash・ヴェスカ = bgm_boss_vesca・ほかの層のボス = bgm_boss・
    /// 追跡者と使い魔 = bgm_pursuer・エリート = bgm_elite・それ以外 = bgm_battle) / 店 = bgm_shop・休憩 = bgm_rest・イベント = bgm_event・〈門〉と裂け目の一択 = bgm_gate /
    /// 層を抜けた = bgm_floor_clear (1 回)・力尽きた = bgm_gameover (1 回) / 報酬の画面 = 前の曲のまま / 航行 = 嵐が来ていれば (嵐の段 1 以上) bgm_nav_storm、 それ以外は bgm_nav。
    /// 名前は issue #293〜#312 の曲の名前。 曲は Resources/Game2DBgm/&lt;名前&gt; に置く。 <b>無ければ何も鳴らさない</b> (今は 1 曲も無い)。</para>
    ///
    /// <para><b>鳴らし方</b>: 2 本の AudioSource のクロスフェード (<see cref="Crossfade"/> 秒)。 音量 = <see cref="BaseVolume"/> × 設定の BGM の音量 (<see cref="GameSettings.Bgm01"/>)。
    /// メインの音量は AudioListener 側で掛かる。 BOT がランを動かしている間・バッチモードでは鳴らさない。</para>
    /// </summary>
    public class Game2DBgm : MonoBehaviour
    {
        // ── 曲の名前 (issue #293〜#312) ──
        public const string Nav = "bgm_nav", NavStorm = "bgm_nav_storm", Battle = "bgm_battle", Elite = "bgm_elite", Boss = "bgm_boss", BossAsh = "bgm_boss_ash",
            BossVesca = "bgm_boss_vesca", Pursuer = "bgm_pursuer", Shop = "bgm_shop", Rest = "bgm_rest", Event = "bgm_event", ClassSelect = "bgm_class_select",
            Gate = "bgm_gate", EndTrue = "bgm_end_true", EndGood = "bgm_end_good", EndOther = "bgm_end_other", Epilogue = "bgm_epilogue",
            GameOver = "bgm_gameover", FloorClear = "bgm_floor_clear", Loading = "bgm_loading";
        /// <summary>曲の置き場所 (Resources の下)。</summary>
        public const string Folder = "Game2DBgm/";
        /// <summary>ループしない曲 (ジングル)。</summary>
        private static readonly HashSet<string> OneShot = new HashSet<string> { FloorClear, GameOver };

        /// <summary>曲の音量 (設定の BGM の音量を掛ける前・TitleBgm の既定と同じ)。</summary>
        public const float BaseVolume = 0.2f;
        /// <summary>曲を替える時のクロスフェードの秒。</summary>
        public const float Crossfade = 0.8f;

        private static Game2DBgm _instance;
        private static bool _shuttingDown;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { _shuttingDown = false; _instance = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (_shuttingDown || _instance != null || Application.isBatchMode) return;
            var go = new GameObject("Game2DBgm") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<Game2DBgm>();
        }

        private void OnApplicationQuit() { _shuttingDown = true; }
        private void OnDestroy() { if (_instance == this) _instance = null; }

        /// <summary>いま鳴らそうとしている曲の名前 (確認用・null = 何も選んでいない)。 曲が Resources に無ければ選んでいても鳴らない。</summary>
        public static string Current => _instance != null ? _instance._want : null;

        private AudioSource _a, _b;      // _a = 今の曲 / _b = 入れ替わりの曲
        private string _want, _playing;
        private float _xf = 1f;          // クロスフェードの進み (1 = 済み)
        private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();

        private void Awake()
        {
            _a = gameObject.AddComponent<AudioSource>(); _b = gameObject.AddComponent<AudioSource>();
            foreach (var s in new[] { _a, _b }) { s.playOnAwake = false; s.spatialBlend = 0f; s.loop = true; s.volume = 0f; }
        }

        private AudioClip Clip(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (!_clips.TryGetValue(name, out var c)) { c = Resources.Load<AudioClip>(Folder + name); _clips[name] = c; }
            return c;
        }

        /// <summary>今の場面で鳴らす曲の名前。 null = 何も鳴らさない (タイトル・BOT)。 <paramref name="keep"/> = 前の曲のまま。</summary>
        private static string Want(string keep)
        {
            if (GameHooks.BotDriving || Application.isBatchMode) return null;
            var gm = GameManager.Instance;
            if (gm == null || gm.CurrentPhase == GameManager.GamePhase.Title) return null;      // タイトルは TitleBgm
            if (Game2DLoadingScreen.Blocking) return Loading;
            switch (gm.CurrentPhase)
            {
                case GameManager.GamePhase.Combat:
                case GameManager.GamePhase.BattleResult: return BattleTrack(gm);
                case GameManager.GamePhase.Reward: return keep;
                case GameManager.GamePhase.ShopVisit: return Shop;
                case GameManager.GamePhase.RestStop: return Rest;
                case GameManager.GamePhase.EventEncounter: return Event;
                case GameManager.GamePhase.GateRitual:
                case GameManager.GamePhase.RiftChoice: return Gate;
                case GameManager.GamePhase.FloorClear: return FloorClear;
                case GameManager.GamePhase.GameOver: return GameOver;
                case GameManager.GamePhase.RunClear: return keep;
                default:
                    var sim = gm.FreeMap;
                    return sim != null && sim.StormMode && sim.StormPhase >= 1 ? NavStorm : Nav;      // 嵐が来てから (段 1〜4)
            }
        }

        private static string BattleTrack(GameManager gm)
        {
            if (gm.InPursuerCombat || gm.InFamiliarCombat) return Pursuer;
            var e = gm.CurrentEnemy;
            if (e == null) return Battle;
            if (e.id == BossIds.Layer6Ash) return BossAsh;
            if (BossIds.VescaStageOf(e.id) > 0) return BossVesca;
            if (BossIds.IsBoss(e.id)) return Boss;
            return e.elite ? Elite : Battle;
        }

        private float Vol => BaseVolume * GameSettings.Bgm01;

        private void Update()
        {
            string w = Want(_want);
            if (w != _want) { _want = w; Switch(w); }
            float dt = Time.unscaledDeltaTime;
            if (_xf < 1f)
            {
                _xf = Mathf.Min(1f, _xf + dt / Crossfade);
                _a.volume = Vol * (1f - _xf); _b.volume = _b.clip != null ? Vol * _xf : 0f;
                if (_xf >= 1f) { _a.Stop(); _a.clip = null; var t = _a; _a = _b; _b = t; }
            }
            else if (_a.isPlaying) _a.volume = Vol;      // 設定の BGM の音量を変えた時にすぐ効く
        }

        /// <summary>曲を替える (クロスフェード)。 同じ曲なら何もしない。 曲が無ければ今の曲を消していく。</summary>
        private void Switch(string name)
        {
            var c = Clip(name);
            string now = c != null ? name : null;
            if (now == _playing) return;
            _playing = now;
            if (_xf < 1f) { _a.Stop(); var t = _a; _a = _b; _b = t; }      // 替えている途中なら、 入ってきていた方を今の曲にする
            _b.Stop(); _b.clip = c; _b.loop = c != null && !OneShot.Contains(name); _b.volume = 0f;
            if (c != null) _b.Play();
            _xf = 0f;
        }
    }
}
