using System.Collections.Generic;
using CombatSystem;
using GameLoop;
using MapSystem.FreeMove;
using UnityEngine;

namespace UI.Game2D
{
    /// <summary>効果音の再生係 (<see cref="Game2DSfx"/> が実行時に作る)。 複数の音を重ねて鳴らし、 遅らせた音は時刻になったら鳴らす。
    /// ループの音 (<see cref="LoopOn"/>・名前ごとに 1 本・フェードで出入り) も持つ。 本体の出来事 (航行・戦闘の合図) を拾って鳴らすのもここ。
    /// 何がどこで鳴るかの表は docs/GAME.md の「音」の節。</summary>
    public class Game2DSfxHost : MonoBehaviour
    {
        private struct Job { public AudioClip c; public float at, vol, pitch; }
        private readonly List<Job> _jobs = new List<Job>();
        private AudioSource[] _src;
        private int _next;

        private void Awake()
        {
            _src = new AudioSource[12];
            for (int i = 0; i < _src.Length; i++) { _src[i] = gameObject.AddComponent<AudioSource>(); _src[i].playOnAwake = false; _src[i].spatialBlend = 0f; }
        }

        public void Shot(AudioClip c, float vol, float pitch)
        {
            var s = _src[_next]; _next = (_next + 1) % _src.Length;
            s.pitch = pitch; s.volume = Mathf.Clamp01(vol); s.clip = c; s.Play();
        }

        /// <summary>鳴り始めたばかり (0.1 秒以内) の同じ音を止める。 失敗の音で、 先に鳴った選ぶ音を打ち消すため。</summary>
        public void Cut(AudioClip c)
        {
            if (c == null) return;
            foreach (var s in _src) if (s.isPlaying && s.clip == c && s.time < 0.1f) s.Stop();
        }

        // エディタで Play を抜けても HideAndDontSave の係が残り、 Play のたびに増えていたので、 終わる時に消す (2026-11-15)
        private void OnApplicationQuit() { Destroy(gameObject); }

        public void Later(AudioClip c, float sec, float vol, float pitch) { _jobs.Add(new Job { c = c, at = Time.unscaledTime + sec, vol = vol, pitch = pitch }); }

        // ── ループの音 (#316・2026-11-15) ──
        //   名前ごとに AudioSource を 1 本。 音量は「頼まれた音量 × Game2DSfx.Volume × フェード」で毎フレーム掛け直す (設定の SE の音量を変えるとすぐ効く)。
        private sealed class LoopVoice { public AudioSource src; public float vol, k, target, rate; }
        private readonly Dictionary<string, LoopVoice> _loops = new Dictionary<string, LoopVoice>();

        public void LoopOn(string name, AudioClip c, float vol, float fade)
        {
            if (!_loops.TryGetValue(name, out var v))
            {
                var s = gameObject.AddComponent<AudioSource>(); s.playOnAwake = false; s.spatialBlend = 0f; s.loop = true;
                v = new LoopVoice { src = s }; _loops[name] = v;
            }
            v.vol = vol; v.target = 1f; v.rate = fade > 0.001f ? 1f / fade : 1000f;
            if (v.src.clip != c) v.src.clip = c;
            if (!v.src.isPlaying) { v.k = fade > 0.001f ? 0f : 1f; v.src.Play(); }
        }

        public void LoopOff(string name, float fade)
        {
            if (!_loops.TryGetValue(name, out var v)) return;
            v.target = 0f; v.rate = fade > 0.001f ? 1f / fade : 1000f;
        }

        public void LoopOffAll(float fade) { foreach (var k in _loops.Keys) LoopOff(k, fade); }

        private void StepLoops(float dt)
        {
            foreach (var v in _loops.Values)
            {
                if (v.src == null) continue;
                v.k = Mathf.MoveTowards(v.k, v.target, v.rate * dt);
                v.src.volume = Mathf.Clamp01(v.vol * Game2DSfx.Volume * v.k);
                if (v.k <= 0f && v.target <= 0f && v.src.isPlaying) v.src.Stop();
            }
        }

        // ── 本体の出来事 → 音 ──
        private GameManager _gm; private readonly Dictionary<string, float> _wxAt = new Dictionary<string, float>();
        private bool _cueHooked;

        private void Hook()
        {
            if (!_cueHooked) { CombatManager.OnCue += OnCombatCue; _cueHooked = true; }
            var gm = GameManager.Instance; if (gm == null || gm == _gm) return;
            Unhook(); _gm = gm;
            _gm.OnFreeMapEvent += OnMapEvent; _gm.OnNavFx += OnNavFx; _gm.OnNavHpPaid += OnNavHp;
            _gm.OnFloorAdvanced += OnFloor; _gm.OnTileActivated += OnTile;
        }

        private void Unhook()
        {
            if (_gm == null) return;
            _gm.OnFreeMapEvent -= OnMapEvent; _gm.OnNavFx -= OnNavFx; _gm.OnNavHpPaid -= OnNavHp;
            _gm.OnFloorAdvanced -= OnFloor; _gm.OnTileActivated -= OnTile; _gm = null;
        }

        private void OnDestroy() { Unhook(); if (_cueHooked) { CombatManager.OnCue -= OnCombatCue; _cueHooked = false; } }

        /// <summary>航行の操作 (ボタンでもキーでも同じ関数を通る・#321): 照明・偵察・囮罠 (置けた時だけ・#323)・待つ。</summary>
        private void OnNavFx(GameManager.NavFx fx)
        {
            switch (fx)
            {
                case GameManager.NavFx.Flare: Game2DSfx.Play("flare_launch"); Game2DSfx.PlayDelayed("flare_burst", 0.45f, 0.8f); break;
                case GameManager.NavFx.Recon: Game2DSfx.Play("recon"); break;
                case GameManager.NavFx.Lure: Game2DSfx.Play("lure_set"); break;
                case GameManager.NavFx.Wait: Game2DSfx.Play("ui_click"); break;
            }
        }
        /// <summary>航行で HP を払った (物資切れの歩行・嵐・雹・#318)。</summary>
        private void OnNavHp(int dmg, string reason) { if (dmg > 0 && !GameHooks.BotDriving) Game2DSfx.Play("hurt"); }
        // (戦闘の報酬の GOLD の音は GOLD の入れ物 (Game2DGoldPot.Payout) だけが鳴らす。 ここでも鳴らすと二重になった・#322)
        private void OnFloor(int floor) { Game2DSfx.Play("floor_up"); }
        private void OnTile(MapSystem.TileType t) { Game2DSfx.Play("ui_open", 0.8f, 0.9f); }

        /// <summary>天候の音 (wx_*)。 嵐の層では嵐の段が進んだ時に、 その嵐の天候の音を 1 回 (#317・#329)。 旧い前線の層 (測定の口) は前線が進んだ時。 同じ音は 4 秒に 1 回まで。</summary>
        private void Weather(FreeMapSim sim)
        {
            string n = null;
            switch (sim.Weather) { case WeatherKind.Fog: n = "wx_fog"; break; case WeatherKind.Sand: n = "wx_sand"; break; case WeatherKind.Snow: n = "wx_snow"; break; case WeatherKind.Hail: n = "wx_hail"; break; }
            if (n == null) return;
            if (_wxAt.TryGetValue(n, out var at) && Time.unscaledTime - at < 4f) return;
            _wxAt[n] = Time.unscaledTime;
            Game2DSfx.Play(n);
        }

        private void OnMapEvent(FreeMapEvent ev)
        {
            var sim = _gm != null ? _gm.FreeMap : null;
            // 雪の足音 (#331): 層の天候ではなく、 着いた点が雪の中か (嵐の層は嵐の中の点だけ・前線の層は前線の内側)
            if ((ev & FreeMapEvent.Arrived) != 0) Game2DSfx.Play("step", 1f, sim != null && sim.WeatherAt(sim.Me, WeatherKind.Snow) ? 0.75f : Random.Range(0.95f, 1.05f));
            if ((ev & FreeMapEvent.Detected) != 0) { Game2DSfx.Play("chase_riser"); Game2DSfx.PlayDelayed("chase_hit", 0.55f); }
            if ((ev & FreeMapEvent.FoeAlerted) != 0) { Game2DSfx.Play("alert"); Game2DSfx.PlayDelayed("alert", 0.14f, 0.8f, 1.2f); }
            if ((ev & FreeMapEvent.FoeLost) != 0) Game2DSfx.Play("lost");
            if ((ev & (FreeMapEvent.Caught | FreeMapEvent.FamiliarContact)) != 0) Game2DSfx.Play("caught");
            if ((ev & FreeMapEvent.FamiliarLaunched) != 0) Game2DSfx.Play("familiar");
            if ((ev & FreeMapEvent.FamiliarVanished) != 0) Game2DSfx.Play("ui_close", 0.7f, 0.8f);
            if ((ev & FreeMapEvent.LureTriggered) != 0) Game2DSfx.Play("lure_hit");
            if ((ev & (FreeMapEvent.FrontAdvanced | FreeMapEvent.StormAdvanced)) != 0 && sim != null) Weather(sim);      // 嵐の段が進んだ (#317)
            if ((ev & FreeMapEvent.SmokeUsed) != 0) Game2DSfx.Play("flare_launch", 0.8f, 0.7f);      // 煙 (#317・新しい音は作らず、 打ち上げの「シュッ」を低く)
            // (〈崩れる地平〉〈沈み続ける湖面〉の crumble / sink は、 現象の廃止 (§5-12) に合わせて鳴らさない・#330。 wav は残す)
            if ((ev & FreeMapEvent.Died) != 0) Game2DSfx.Play("defeat");
        }

        /// <summary>戦闘の出来事の合図 (#319)。 音を付けるのは今ある音で合う物だけ (激化の段が上がった = 警戒の音を低く)。 ほかは合図だけ (音の素材ができたらここに足す)。</summary>
        private void OnCombatCue(CombatManager.CombatCue cue, string arg)
        {
            if (GameHooks.BotDriving) return;
            switch (cue)
            {
                case CombatManager.CombatCue.EscalationUp: Game2DSfx.Play("alert", 0.6f, 0.8f); break;
            }
        }

        private void Update()
        {
            Hook();
            for (int i = _jobs.Count - 1; i >= 0; i--)
                if (Time.unscaledTime >= _jobs[i].at) { Shot(_jobs[i].c, _jobs[i].vol, _jobs[i].pitch); _jobs.RemoveAt(i); }
            StepLoops(Time.unscaledDeltaTime);
        }
    }
}
