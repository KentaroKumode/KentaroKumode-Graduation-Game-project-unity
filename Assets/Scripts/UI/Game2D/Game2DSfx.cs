using System.Collections.Generic;
using UnityEngine;

namespace UI.Game2D
{
    /// <summary>
    /// Game2D の効果音 (2026-10-24)。 音は earshot の音のバンクから作った WAV (Assets/Resources/Game2DSfx/) で、 名前で鳴らす。
    /// 音の対応: ui_hover / ui_click / ui_confirm / ui_open / ui_close / ui_tab / nav_press / pot_open / pot_close / gui_in / gui_out /
    /// dice_1〜3 (ダイスの転がり) / wire / decide / swing / hit / crit / enemy_hit / block / enemy_appear / battle_start / victory / defeat。
    /// 鳴らす所は 2D の 1 つの AudioSource。 少し遅らせて鳴らす (<see cref="PlayDelayed"/>) 場合は <see cref="Game2DSfxHost"/> が時刻を見て鳴らす。
    /// </summary>
    public static class Game2DSfx
    {
        /// <summary>全体の音量 (0〜1)。</summary>
        public static float Volume = 0.8f;
        private static readonly Dictionary<string, AudioClip> Clips = new Dictionary<string, AudioClip>();
        private static readonly Dictionary<string, float> Last = new Dictionary<string, float>();
        private static Game2DSfxHost _host;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot() { Ensure(); }

        private static AudioClip Get(string name)
        {
            if (Clips.TryGetValue(name, out var c) && c != null) return c;
            c = Resources.Load<AudioClip>("Game2DSfx/" + name); Clips[name] = c;
            return c;
        }

        private static bool Ensure()
        {
            if (_host != null) return true;
            if (!Application.isPlaying) return false;
            var go = new GameObject("Game2DSfx") { hideFlags = HideFlags.HideAndDontSave };
            Object.DontDestroyOnLoad(go);
            _host = go.AddComponent<Game2DSfxHost>();
            return true;
        }

        /// <summary>すぐ鳴らす。 同じ音は 30ms 以内に重ねない (ホバーの連打など)。 pitch は再生速度 (1 = そのまま)。</summary>
        public static void Play(string name, float vol = 1f, float pitch = 1f)
        {
            if (!Ensure()) return;
            float now = Time.unscaledTime;
            if (Last.TryGetValue(name, out var l) && now - l < 0.03f) return;
            Last[name] = now;
            var c = Get(name); if (c == null) return;
            _host.Shot(c, vol * Volume, pitch);
        }

        /// <summary>sec 秒あとに鳴らす。</summary>
        public static void PlayDelayed(string name, float sec, float vol = 1f, float pitch = 1f)
        {
            if (!Ensure()) return;
            var c = Get(name); if (c == null) return;
            _host.Later(c, sec, vol * Volume, pitch);
        }

        /// <summary>斬撃の当たり (肉を裂く): 音程のない雑音だけで作った 1 つの音 (ザ・シュ・ドスッ・ぐちゅ)。 会心は 2 発 + 重い余韻。 pitch = 低いほど重い武器。</summary>
        public static void Slash(bool crit, float pitch = 1f)
        {
            Play(crit ? "hit_crit" : "hit_slash", 1f, pitch * Random.Range(0.97f, 1.03f));
        }

        /// <summary>
        /// 武器の命中音 (fam = 武器の系統 / kind = 命中の種類)。 剣・短剣 = 斬る音 (通常 = hit_slash / 会心の最後 = hit_crit)、 斧 = 叩きつける音 (hit_axe / crit_axe)、 盾 = 金属板を叩く音 (hit_shield / crit_shield)。
        /// 剣・短剣の会心は、 斬撃痕 (cut) が出るたびに通常の命中音を鳴らし、 最後の会心の演出 (chop / finish) で hit_crit を鳴らす。
        /// </summary>
        public static void Hit(string fam, string kind, bool crit, float pitch = 1f)
        {
            string name;
            switch (fam)
            {
                case "axe": name = crit ? "crit_axe" : "hit_axe"; break;
                case "shield": name = crit ? "crit_shield" : "hit_shield"; break;
                default: name = crit && (kind == "chop" || kind == "finish" || kind == "dragon") ? "hit_crit" : "hit_slash"; break;      // 剣・短剣・竜閃
            }
            Play(name, 1f, pitch * Random.Range(0.97f, 1.03f));
        }

        /// <summary>
        /// GOLD が増えたときのコインの音の段階 (増加量): 1〜5 = 数枚 (coin_few_1・2 のどちらか) / 6〜20 = 降り注ぐ音 1 秒 (coin_pour_1) / 21〜30 = 2 秒 (coin_pour_2) / 31 以上 = 3 秒 (coin_pour_3)。
        /// コイン 1 枚だけの音は使わない (2026-10-25)。
        /// </summary>
        public const int Pour1From = 6, Pour2From = 21, Pour3From = 31;

        /// <summary>GOLD が増えたときのコインの音を、 増加量に応じて 1 回鳴らす。</summary>
        public static void Reward(int delta)
        {
            if (delta <= 0) return;
            if (delta >= Pour3From) Play("coin_pour_3");
            else if (delta >= Pour2From) Play("coin_pour_2");
            else if (delta >= Pour1From) Play("coin_pour_1");
            else Play("coin_few_" + Random.Range(1, 3), 1f, Random.Range(0.97f, 1.03f));
        }

        /// <summary>GOLD の入れ物に GOLD が入る音 (<see cref="Reward"/> と同じ段階)。</summary>
        public static void DiceCoin(int delta) { Reward(delta); }

        /// <summary>ダイスが転がる音 (3 つの音をランダムに、 間をあけて数回)。</summary>
        public static void DiceRoll(int n = 5, float span = 0.55f, float vol = 1f)
        {
            for (int i = 0; i < n; i++)
                PlayDelayed("dice_" + Random.Range(1, 4), span * i / Mathf.Max(1, n) + Random.Range(0f, 0.03f), vol * Random.Range(0.6f, 1f), Random.Range(0.9f, 1.15f));
        }
    }
}
