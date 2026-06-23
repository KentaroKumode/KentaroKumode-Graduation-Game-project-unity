using UnityEngine;

namespace UI
{
    /// <summary>
    /// タイトル/スキルツリーの BGM 再生。 Day / Night / SkillTree の 3 モードを保持し、
    /// SetMode でクリップを切り替えてループ再生する。 切替は短いクロスフェード（2 AudioSource）。
    ///
    /// 連動:
    ///   ・<see cref="TitleDayNight"/> から SetMode(Day/Night) 呼び出し
    ///   ・<see cref="SkillTree.SkillTreeView"/> の Open/Close で SetMode(SkillTree)/前のモード復帰
    ///
    /// 付け先: タイトル常駐の任意 GameObject。 既存の <c>clip</c> フィールドは Day BGM として継続利用。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public class TitleBgm : MonoBehaviour
    {
        public enum BgmMode { Day, Night, SkillTree }

        [Header("クリップ")]
        [Tooltip("Day BGM（既存の title_bgm.wav）。 既存 Inspector 設定を温存するため名前は clip のまま。")]
        public AudioClip clip;
        [Tooltip("Night BGM（title_night.wav）")]
        public AudioClip nightClip;
        [Tooltip("スキルツリー BGM（skill_tree.wav）")]
        public AudioClip skillTreeClip;

        [Header("ボリューム / フェード")]
        [Range(0f, 1f)] public float volume = 0.2f;
        [Tooltip("最初の再生時のフェードイン秒数。 0 で即時。")]
        [Range(0f, 5f)] public float fadeIn = 0.5f;
        [Tooltip("モード切替時のクロスフェード秒数。 0 で即時切替。")]
        [Range(0f, 5f)] public float crossfade = 0.8f;
        [Tooltip("シーン切替で破棄せず継続再生する")]
        public bool dontDestroyOnLoad = false;

        [Header("立体音響")]
        [Tooltip("ON で 3D 音源（カメラ移動で左右の耳に振れる）／OFF で 2D。")]
        public bool spatial = true;
        [Tooltip("音源位置として使う Transform。 未指定なら このGameObject の位置を使う。 LCD 中央に置いた空 GO を割り当てるのが推奨。")]
        public Transform sourcePoint;
        [Tooltip("3D 音源の減衰開始距離。 これより近いと最大音量。")]
        public float minDistance = 1f;
        [Tooltip("3D 音源の減衰終了距離。 これより遠いと最小音量。 タイトル LCD では大きめ(50)推奨。")]
        public float maxDistance = 50f;

        [Header("起動時のモード")]
        public BgmMode startMode = BgmMode.Day;

        [Header("ミュート (BGM オフ)")]
        [Tooltip("ON で全 BGM を消音。 PlayerPrefs に状態が保存され、 次回起動時に復元される。")]
        public bool isMuted;
        [Tooltip("ホットキーでミュートをトグル。 None で無効。")]
        public KeyCode muteHotkey = KeyCode.None;
        [Tooltip("PlayerPrefs に保存する Key。 同一プロジェクトで複数 BGM 系統があれば変える。")]
        public string mutePrefKey = "TitleBgm.Muted";

        public BgmMode CurrentMode { get; private set; } = BgmMode.Day;
        public BgmMode PreviousMode { get; private set; } = BgmMode.Day;

        private AudioSource _srcA, _srcB; // クロスフェード用 2ch（A=現行 / B=新規）
        private float _fadeT;             // 初回フェードイン用
        private float _xfT;               // クロスフェード進捗（0..crossfade）
        private bool _xfActive;

        private void Awake()
        {
            _srcA = GetComponent<AudioSource>();
            _srcB = gameObject.AddComponent<AudioSource>();
            foreach (var s in new[] { _srcA, _srcB })
            {
                s.playOnAwake = false;
                s.loop = true;
            }
            ApplySpatialSettings();
            if (dontDestroyOnLoad) DontDestroyOnLoad(gameObject);

            // ミュート状態を PlayerPrefs から復元
            if (!string.IsNullOrEmpty(mutePrefKey))
                isMuted = PlayerPrefs.GetInt(mutePrefKey, isMuted ? 1 : 0) != 0;

            // 全クリップを事前にロードしておく（モード切替時の初回 Play で固まらないように）
            foreach (var pc in new[] { clip, nightClip, skillTreeClip })
            {
                if (pc == null) continue;
                if (pc.loadState == AudioDataLoadState.Unloaded) pc.LoadAudioData();
            }

            // タイトルが夜モードならその BGM で始める（TitleDayNight が後で SetMode してもクロスフェードで切替不要になる）
            var dn = FindObjectOfType<TitleDayNight>();
            BgmMode initialMode = startMode;
            if (dn != null && dn.mode == TitleDayNight.Mode.Night) initialMode = BgmMode.Night;
            CurrentMode = initialMode;
            PreviousMode = initialMode;
            var c = ResolveClip(initialMode);
            if (c != null)
            {
                _srcA.clip = c;
                _srcA.volume = (fadeIn > 0f) ? 0f : volume;
                _srcA.Play();
            }
        }

        private void ApplySpatialSettings()
        {
            foreach (var s in new[] { _srcA, _srcB })
            {
                if (s == null) continue;
                s.spatialBlend = spatial ? 1f : 0f;
                s.rolloffMode = AudioRolloffMode.Linear;
                s.minDistance = minDistance;
                s.maxDistance = maxDistance;
                s.dopplerLevel = 0f;
            }
        }

        private AudioClip ResolveClip(BgmMode m)
        {
            switch (m)
            {
                case BgmMode.Day: return clip;
                case BgmMode.Night: return nightClip;
                case BgmMode.SkillTree: return skillTreeClip;
            }
            return null;
        }

        /// <summary>BGM のモードを切替（クロスフェード）。 同モードなら何もしない。</summary>
        public void SetMode(BgmMode m)
        {
            if (m == CurrentMode && _srcA != null && _srcA.isPlaying) return;
            PreviousMode = CurrentMode;
            CurrentMode = m;
            var next = ResolveClip(m);
            if (_srcA == null || _srcB == null) return;

            // クロスフェード無し or 現行が無音なら即切替
            if (crossfade <= 0.001f || !_srcA.isPlaying)
            {
                _srcA.Stop();
                _srcA.clip = next;
                _srcA.volume = volume;
                if (next != null) _srcA.Play();
                _xfActive = false;
                return;
            }

            // クロスフェード: B に新クリップを乗せて Play、 A→0 / B→volume へ補間
            _srcB.clip = next;
            _srcB.volume = 0f;
            if (next != null) _srcB.Play();
            _xfT = 0f;
            _xfActive = true;
        }

        /// <summary>SetMode(PreviousMode) のショートカット。</summary>
        public void RestorePreviousMode() { SetMode(PreviousMode); }

        /// <summary>BGM のミュート状態を設定する (PlayerPrefs にも保存)。</summary>
        public void SetMuted(bool m)
        {
            if (isMuted == m) return;
            isMuted = m;
            if (!string.IsNullOrEmpty(mutePrefKey))
            {
                PlayerPrefs.SetInt(mutePrefKey, m ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        /// <summary>BGM のミュートをトグルする。</summary>
        public void ToggleMute() => SetMuted(!isMuted);

        private void OnDisable()
        {
            if (_srcA != null && _srcA.isPlaying) _srcA.Stop();
            if (_srcB != null && _srcB.isPlaying) _srcB.Stop();
        }

        // Inspector で値を変えた瞬間に Play 中へ反映。
        private void OnValidate()
        {
            if (_srcA == null) _srcA = GetComponent<AudioSource>();
            ApplySpatialSettings();
            if (_srcA != null && !(fadeIn > 0f && _fadeT < fadeIn) && !_xfActive) _srcA.volume = volume;
        }

        private void Update()
        {
            if (_srcA == null) return;

            // ホットキーでミュート切替
            if (muteHotkey != KeyCode.None && Input.GetKeyDown(muteHotkey))
                ToggleMute();

            // 音源位置を sourcePoint に追従
            if (sourcePoint != null && transform.position != sourcePoint.position)
                transform.position = sourcePoint.position;

            // クロスフェード進行
            if (_xfActive)
            {
                _xfT += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(_xfT / Mathf.Max(0.0001f, crossfade));
                _srcA.volume = Mathf.Lerp(volume, 0f, t);
                _srcB.volume = Mathf.Lerp(0f, volume, t);
                if (t >= 1f)
                {
                    _srcA.Stop();
                    // A と B を入れ替え（次回切替で A を現行扱いに）
                    var tmp = _srcA; _srcA = _srcB; _srcB = tmp;
                    _xfActive = false;
                }
                return;
            }

            // 初回フェードイン
            if (_srcA.isPlaying && fadeIn > 0f && _fadeT < fadeIn)
            {
                _fadeT += Time.unscaledDeltaTime;
                _srcA.volume = Mathf.Lerp(0f, volume, Mathf.Clamp01(_fadeT / fadeIn));
            }
            else if (_srcA.isPlaying && Mathf.Abs(_srcA.volume - volume) > 0.001f)
            {
                _srcA.volume = volume;
            }

            // ミュート中は最終的に音量を 0 に上書き (フェード/クロスフェードの値を尊重したまま消音)
            if (isMuted)
            {
                if (_srcA != null) _srcA.volume = 0f;
                if (_srcB != null) _srcB.volume = 0f;
            }
        }
    }
}
