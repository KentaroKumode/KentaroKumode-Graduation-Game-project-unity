using UnityEngine;

namespace UI
{
    /// <summary>
    /// ホーム画面 (タイトルのメニュー) の配置と 4 つ目の「SETTINGS」(2026-11-16・作者が見本の案 r を選んだ)。
    /// <b>シーン (Game2D.unity) は触らない</b>: 起動した時に、 ロゴと 3 つのボタンの位置をコードで置き直し、 GALLERY を複製して SETTINGS を作る
    /// (<see cref="TitleMenuBootstrap"/> が呼ぶ)。 位置は<b>ここの定数 1 か所</b>。
    ///
    /// <para><b>座標</b>: 値は液晶の 480×270 の格子の、 絵の中心の dot の y (上から)。 タイトルの液晶のリグ (TitleLcdRig/Content) は 1 unit = 16 dot で、
    /// 液晶の真ん中 (y 135) が Content の y 0。 部品は Content/Menu の子なので、 Menu の中の位置 = (135 − dot) ÷ 16 − Menu の y (シーンでは −1.5)。
    /// x は今のまま (中央)。 今の値 (シーン): ロゴ 79・ENTER THE HOLLOW 175・SKILL TREE 207・GALLERY 239 (間隔 32)。</para>
    ///
    /// <para><b>SETTINGS の絵</b>: 3 つのボタンの絵の字 (S・E・T・I・N・G) を切り出して並べた物 (作り方 Captures/title_options/work/make_settings.py・
    /// 金の字と黒い縁だけ・にじみ無し)。 3 つの絵はシーンが参照する取り込み済みのテクスチャだが、 これはシーンに無いので、
    /// Resources/TitleMenu/settings_day / night.png.bytes をテキストの資産として読み、 <see cref="Texture2D.LoadImage(byte[])"/> で戻して Sprite を作る
    /// (PPU 32・点の補間・圧縮なし ── 3 つの絵と同じ扱い。 .meta が git 管理外でも取り込みの設定に左右されない・戦闘の背景と同じ読み方)。</para>
    ///
    /// <para>押すと設定の窓 (<see cref="Game2D.Game2DSettingsPanel"/>・Esc と同じ窓) を開く。 両端の矢印 (<see cref="MenuHoverCursor"/>) と
    /// 昼夜の差し替え (<see cref="TitleDayNight"/>) に足す。 タイトルのメニューに上下のキーの操作は無い (マウスだけ)。</para>
    /// </summary>
    public static class TitleMenuLayout
    {
        /// <summary>絵の中心の dot の y (案 r・作者 2026-11-16)。 間隔 27 dot。</summary>
        public const int LogoY = 71, PlayY = 158, SkillY = 185, GalleryY = 212, SettingsY = 239;
        /// <summary>タイトルの液晶のリグの 1 unit の dot 数と、 液晶の真ん中の dot の y。</summary>
        public const float DotsPerUnit = 16f, CenterY = 135f;
        /// <summary>SETTINGS の絵の置き場 (Resources の下・*.png.bytes)・PPU。</summary>
        public const string ArtDay = "TitleMenu/settings_day.png", ArtNight = "TitleMenu/settings_night.png";
        public const float Ppu = 32f;
        public const string SettingsName = "SettingsButton";

        private static bool _done;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { _done = false; }

        /// <summary>置き直す (1 度だけ)。 PlayButton が見つからない (タイトルの無いシーン) なら何もしない。</summary>
        public static void Apply()
        {
            if (_done) return;
            var play = Find("PlayButton"); if (play == null) return;
            var menu = play.transform.parent; if (menu == null) return;
            _done = true;
            var skill = Find("SkillTreeButton"); var gallery = Find("GalleryButton");
            var logo = menu.Find("Title");
            float menuY = menu.localPosition.y;
            Place(logo, LogoY, menuY); Place(play.transform, PlayY, menuY);
            if (skill != null) Place(skill.transform, SkillY, menuY);
            if (gallery == null) { Debug.LogWarning("[TitleMenuLayout] GalleryButton が無い (SETTINGS を作れない)"); return; }
            Place(gallery.transform, GalleryY, menuY);
            MakeSettings(gallery, menu, menuY);
        }

        /// <summary>dot の y を Menu の中の位置にして置く (x と z は今のまま)。</summary>
        private static void Place(Transform t, int dotY, float menuY)
        {
            if (t == null) return;
            var p = t.localPosition; p.y = (CenterY - dotY) / DotsPerUnit - menuY; t.localPosition = p;
        }

        private static void MakeSettings(SpriteButton gallery, Transform menu, float menuY)
        {
            if (menu.Find(SettingsName) != null) return;
            var day = LoadSprite(ArtDay); var night = LoadSprite(ArtNight);
            if (day == null) { Debug.LogWarning("[TitleMenuLayout] Resources/" + ArtDay + ".bytes を読めない (SETTINGS を作らない)"); return; }
            var go = Object.Instantiate(gallery.gameObject, menu, false);       // 部品 (絵・当たり・SpriteButton・層) は GALLERY と同じ
            go.name = SettingsName;
            Place(go.transform, SettingsY, menuY);
            var btn = go.GetComponent<SpriteButton>();
            var sr = btn != null && btn.targetRenderer != null ? btn.targetRenderer : go.GetComponent<SpriteRenderer>();
            var dn = Object.FindObjectOfType<TitleDayNight>();
            bool isNight = dn != null && dn.mode == TitleDayNight.Mode.Night;
            if (sr != null) sr.sprite = isNight && night != null ? night : day;
            // 当たり: GALLERY の当たりと絵の幅の差 (左右の余白) をそのままに、 SETTINGS の絵の幅へ合わせる
            var gcol = gallery.GetComponent<BoxCollider2D>(); var col = go.GetComponent<BoxCollider2D>(); var gsr = gallery.GetComponent<SpriteRenderer>();
            if (col != null && gcol != null && gsr != null && gsr.sprite != null)
            {
                float margin = gsr.sprite.bounds.size.x - gcol.size.x;
                col.size = new Vector2(day.bounds.size.x - margin, gcol.size.y);
            }
            if (btn != null) btn.Clicked += OnSettingsClicked;
            // 昼夜の差し替えと両端の矢印に足す
            if (dn != null && sr != null) dn.AddSlot(sr, day, night != null ? night : day);
            var cursor = menu.GetComponent<MenuHoverCursor>() ?? Object.FindObjectOfType<MenuHoverCursor>();
            if (cursor != null && btn != null) cursor.AddItem(btn);
        }

        private static void OnSettingsClicked()
        {
            var gm = GameLoop.GameManager.Instance;
            if (gm != null && gm.CurrentPhase != GameLoop.GameManager.GamePhase.Title) return;
            if (!Game2D.Game2DSettingsPanel.OpenFromMenu()) Debug.LogWarning("[TitleMenuLayout] 設定の窓を開けない (ほかの窓が出ている・窓が無い)");
        }

        /// <summary>Resources の PNG (.png.bytes) から Sprite を作る (PPU 32・点の補間・中心が基準)。 無ければ null。</summary>
        private static Sprite LoadSprite(string path)
        {
            var a = Resources.Load<TextAsset>(path); if (a == null) return null;
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = path };
            bool ok = t.LoadImage(a.bytes); Resources.UnloadAsset(a);
            if (!ok) { Object.Destroy(t); return null; }
            t.filterMode = FilterMode.Point;
            return Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), Ppu, 0, SpriteMeshType.FullRect);
        }

        private static SpriteButton Find(string name)
        {
            foreach (var b in Object.FindObjectsOfType<SpriteButton>(true))
                if (b != null && b.name == name) return b;
            return null;
        }
    }
}
