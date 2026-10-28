using System.Collections.Generic;
using GameLoop;
using MapSystem.FreeMove;
using UnityEngine;

namespace UI.Game2D
{
    /// <summary>効果音の再生係 (<see cref="Game2DSfx"/> が実行時に作る)。 複数の音を重ねて鳴らし、 遅らせた音は時刻になったら鳴らす。</summary>
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

        public void Later(AudioClip c, float sec, float vol, float pitch) { _jobs.Add(new Job { c = c, at = Time.unscaledTime + sec, vol = vol, pitch = pitch }); }

        // ── 航行のイベント → 音 ──
        private GameManager _gm; private readonly Dictionary<string, float> _wxAt = new Dictionary<string, float>();

        private void Hook()
        {
            var gm = GameManager.Instance; if (gm == null || gm == _gm) return;
            Unhook(); _gm = gm;
            _gm.OnFreeMapEvent += OnMapEvent; _gm.OnNavFx += OnNavFx; _gm.OnStarvationDamage += OnStarve;
            _gm.OnRewardGranted += OnReward; _gm.OnFloorAdvanced += OnFloor; _gm.OnTileActivated += OnTile;
        }

        private void Unhook()
        {
            if (_gm == null) return;
            _gm.OnFreeMapEvent -= OnMapEvent; _gm.OnNavFx -= OnNavFx; _gm.OnStarvationDamage -= OnStarve;
            _gm.OnRewardGranted -= OnReward; _gm.OnFloorAdvanced -= OnFloor; _gm.OnTileActivated -= OnTile; _gm = null;
        }

        private void OnDestroy() { Unhook(); }

        private void OnNavFx(GameManager.NavFx fx)
        {
            if (fx == GameManager.NavFx.Flare) { Game2DSfx.Play("flare_launch"); Game2DSfx.PlayDelayed("flare_burst", 0.45f, 0.8f); }
            else Game2DSfx.Play("recon");
        }
        private void OnStarve(int dmg) { Game2DSfx.Play("hurt"); }
        private void OnReward(int gold) { Game2DSfx.Reward(gold); }
        private void OnFloor(int floor) { Game2DSfx.Play("floor_up"); }
        private void OnTile(MapSystem.TileType t) { Game2DSfx.Play("ui_open", 0.8f, 0.9f); }

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
            if ((ev & FreeMapEvent.Arrived) != 0) Game2DSfx.Play("step", 1f, sim != null && sim.Weather == WeatherKind.Snow ? 0.75f : Random.Range(0.95f, 1.05f));
            if ((ev & FreeMapEvent.Detected) != 0) { Game2DSfx.Play("chase_riser"); Game2DSfx.PlayDelayed("chase_hit", 0.55f); }
            if ((ev & FreeMapEvent.FoeAlerted) != 0) { Game2DSfx.Play("alert"); Game2DSfx.PlayDelayed("alert", 0.14f, 0.8f, 1.2f); }
            if ((ev & FreeMapEvent.FoeLost) != 0) Game2DSfx.Play("lost");
            if ((ev & (FreeMapEvent.Caught | FreeMapEvent.FamiliarContact)) != 0) Game2DSfx.Play("caught");
            if ((ev & FreeMapEvent.FamiliarLaunched) != 0) Game2DSfx.Play("familiar");
            if ((ev & FreeMapEvent.FamiliarVanished) != 0) Game2DSfx.Play("ui_close", 0.7f, 0.8f);
            if ((ev & FreeMapEvent.LureTriggered) != 0) Game2DSfx.Play("lure_hit");
            if ((ev & FreeMapEvent.FrontAdvanced) != 0 && sim != null) Weather(sim);
            if ((ev & FreeMapEvent.MapChanged) != 0 && sim != null) Game2DSfx.Play(sim.Weather == WeatherKind.Crumble ? "crumble" : "sink");
            if ((ev & FreeMapEvent.Died) != 0) Game2DSfx.Play("defeat");
        }

        private void Update()
        {
            Hook();
            for (int i = _jobs.Count - 1; i >= 0; i--)
                if (Time.unscaledTime >= _jobs[i].at) { Shot(_jobs[i].c, _jobs[i].vol, _jobs[i].pitch); _jobs.RemoveAt(i); }
        }
    }
}
