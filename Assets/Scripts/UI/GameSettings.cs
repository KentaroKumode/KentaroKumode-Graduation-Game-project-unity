using UnityEngine;

namespace UI
{
    /// <summary>
    /// 設定の値 (#85・2026-11-11)。 <b>設定の値の置き場はここ 1 か所</b> ── 範囲・既定・保存の鍵・画面に出す名前をまとめて持つ。
    /// 保存は PlayerPrefs (メタ進行はエディタの起動ごとに初期化されるが、 設定は次の起動でも効かせたいので別にする)。
    /// 値を実際に効かせるのは <see cref="Game2D.Game2DSettingsApply"/> (音量・液晶の効果)、 画面は <see cref="Game2D.Game2DSettingsPanel"/>。
    /// <para>音量は 1〜100 (100 = 素材の音量のまま)。 液晶の効果は 0〜100 で、 <b>50 = 調整済みの今の強さ</b> (100 = その 2 倍・0 = 切)。
    /// 明るさは 1〜100 で 50 = 今の明るさ (1 = 約半分・100 = 約 1.5 倍)。</para>
    /// </summary>
    public static class GameSettings
    {
        public enum Key { Master, Bgm, Se, Bright, Scan, Curve, Chroma, Vignette, Noise }

        public struct Def
        {
            public string pref, name, desc; public int min, max, def;
        }

        /// <summary>項目の定義 (並びは <see cref="Key"/> と同じ)。</summary>
        public static readonly Def[] Defs =
        {
            new Def { pref = "Settings.master",   name = "メイン",     min = 1, max = 100, def = 100, desc = "すべての音の音量。" },
            new Def { pref = "Settings.bgm",      name = "BGM",        min = 1, max = 100, def = 100, desc = "音楽の音量 (メインの音量に掛かる)。" },
            new Def { pref = "Settings.se",       name = "SE",         min = 1, max = 100, def = 80,  desc = "効果音の音量 (メインの音量に掛かる)。" },
            new Def { pref = "Settings.bright",   name = "明るさ",     min = 1, max = 100, def = 50,  desc = "液晶全体の明るさ。 50 が標準。" },
            new Def { pref = "Settings.scan",     name = "走査線",     min = 0, max = 100, def = 50,  desc = "横線の暗い帯の濃さ。 50 が標準・0 で切。" },
            new Def { pref = "Settings.curve",    name = "湾曲",       min = 0, max = 100, def = 50,  desc = "画面のふくらみ。 50 が標準・0 で平ら。" },
            new Def { pref = "Settings.chroma",   name = "色収差",     min = 0, max = 100, def = 50,  desc = "縁ほど赤と青がずれる強さ。 50 が標準・0 で切。" },
            new Def { pref = "Settings.vignette", name = "ビネット",   min = 0, max = 100, def = 50,  desc = "四隅の暗さ。 50 が標準・0 で切。" },
            new Def { pref = "Settings.noise",    name = "ノイズ",     min = 0, max = 100, def = 50,  desc = "横線のノイズとフィルムの粒。 50 が標準・0 で切。" },
        };

        private static int[] _v;
        /// <summary>値が変わるたびに増える (効かせる側が読み直す合図)。</summary>
        public static int Version { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { _v = null; Version = 0; }

        private static int[] V
        {
            get
            {
                if (_v != null) return _v;
                _v = new int[Defs.Length];
                for (int i = 0; i < Defs.Length; i++) _v[i] = Mathf.Clamp(PlayerPrefs.GetInt(Defs[i].pref, Defs[i].def), Defs[i].min, Defs[i].max);
                return _v;
            }
        }

        public static int Get(Key k) => V[(int)k];

        /// <summary>値を変える (範囲に収める)。 PlayerPrefs へは書くが、 ディスクへの書き出し (<see cref="Save"/>) は画面を閉じる時にまとめて行う。</summary>
        public static void Set(Key k, int v)
        {
            var d = Defs[(int)k]; v = Mathf.Clamp(v, d.min, d.max);
            if (V[(int)k] == v) return;
            V[(int)k] = v; PlayerPrefs.SetInt(d.pref, v); Version++;
        }

        public static void Save() => PlayerPrefs.Save();

        // ── 効かせる側が読む値 ──
        public static float Master01 => Get(Key.Master) / 100f;
        public static float Bgm01 => Get(Key.Bgm) / 100f;
        public static float Se01 => Get(Key.Se) / 100f;
        /// <summary>液晶の明るさの倍率 (50 → 1)。</summary>
        public static float Brightness => 0.5f + Get(Key.Bright) / 100f;
        /// <summary>液晶の効果の倍率 (50 → 1・100 → 2・0 → 0)。</summary>
        public static float FxScale(Key k) => Get(k) / 50f;
    }
}
