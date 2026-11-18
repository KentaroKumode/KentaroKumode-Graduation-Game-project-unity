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
    /// <para><b>SETTINGS の絵</b>: 2026-11-17〜 4 行とも DenkiChip で作り直した字の絵「v3-2」(字の高さ 18 dot・字の間 1 dot・単語の間 7 dot・作り方 Captures/title_menu_font/v3s/work/make_v3s.py・
    /// 1 dot = 2 px・全画素が不透明か透明・旧い絵の控えは Captures/title_menu_font/work/orig_menu/)。 旧: 3 つのボタンの絵の字を切り出して並べた物 (Captures/title_options/work/make_settings.py)。 3 つの絵はシーンが参照する取り込み済みのテクスチャだが、 これはシーンに無いので、
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
            if (gallery == null) { Debug.LogWarning("[TitleMenuLayout] GalleryButton が無い (SETTINGS を作れない)"); }
            float galleryX = gallery != null ? gallery.transform.localPosition.x : 0f;      // SETTINGS は GALLERY の複製なので、 ずらす前の GALLERY の x を基にする
            Place(logo, LogoY, menuY); Place(play.transform, PlayY, menuY);
            if (skill != null) Place(skill.transform, SkillY, menuY);
            if (gallery == null) return;
            Place(gallery.transform, GalleryY, menuY);
            // 当たりを今の絵の大きさに合わせる (2026-11-17: 字の絵を DenkiChip の作り直し v2-c → v3-2 に差し替えて小さくなった・シーンの BoxCollider2D は旧い絵の固定の大きさ)
            FitCollider(play); FitCollider(skill); FitCollider(gallery);
            MakeSettings(gallery, menu, menuY, galleryX);
        }

        /// <summary>ボタンの当たり (BoxCollider2D) を、 今の絵 (SpriteRenderer の sprite) の範囲にそろえる。 両端の矢印 (<see cref="MenuHoverCursor"/>) も当たりの範囲に置くので一緒にそろう。
        /// 字の絵は透明の余白を持たない (v3-2・全画素が字か縁か透明で、 絵の外接の箱 = 絵の大きさ) ので、 絵の範囲そのまま。 昼と夜の絵は同じ大きさ。</summary>
        private static void FitCollider(SpriteButton b)
        {
            if (b == null) return;
            var sr = b.targetRenderer != null ? b.targetRenderer : b.GetComponent<SpriteRenderer>();
            var col = b.GetComponent<BoxCollider2D>();
            if (sr == null || sr.sprite == null || col == null) return;
            var bd = sr.sprite.bounds;      // 絵の中の座標 (基準点から・拡大縮小は 1)
            col.size = new Vector2(bd.size.x, bd.size.y); col.offset = new Vector2(bd.center.x, bd.center.y);
        }

        /// <summary>dot の y を Menu の中の位置にして置く (z は今のまま)。 baseX = ずらす前の x (Menu の中・null なら今の x)。
        /// <para><b>液晶の dot の格子にそろえる</b> (2026-11-17): 絵の中心を置くので、 絵の幅・高さ (dot) が奇数だと縁が半端な 0.5 dot に来る
        /// (字の絵 v3-2 は高さ 23 dot・ENTER 189 dot・SETTINGS 93 dot)。 その時だけ中心を +0.5 dot ずらし、 縁を整数の dot に置く。
        /// 縦は下へ (dot の y を増やす)、 横は右へ。 大きさは今の絵から求めるので、 字の絵を替えても効く。 当たり (<see cref="FitCollider"/>) と両端の矢印は
        /// 絵の範囲から作るので、 ずらした位置に付いてくる。</para></summary>
        private static void Place(Transform t, int dotY, float menuY, float? baseX = null)
        {
            if (t == null) return;
            float dy = 0f, dx = 0f;
            var sr = t.GetComponent<SpriteRenderer>();
            if (sr != null && sr.sprite != null)
            {
                var size = sr.sprite.bounds.size * DotsPerUnit;      // 絵の大きさ (dot・拡大縮小は 1)
                if (Mathf.RoundToInt(size.y) % 2 == 1) dy = 0.5f;
                if (Mathf.RoundToInt(size.x) % 2 == 1) dx = 0.5f;
            }
            var p = t.localPosition;
            float x0 = baseX ?? p.x;
            p.y = (CenterY - (dotY + dy)) / DotsPerUnit - menuY;
            p.x = Mathf.Round(x0 * DotsPerUnit) / DotsPerUnit + dx / DotsPerUnit;      // ずらす前の x を dot の格子に丸めてから右へ
            t.localPosition = p;
        }

        private static void MakeSettings(SpriteButton gallery, Transform menu, float menuY, float galleryX)
        {
            if (menu.Find(SettingsName) != null) return;
            var day = LoadSprite(ArtDay); var night = LoadSprite(ArtNight);
            if (day == null) { Debug.LogWarning("[TitleMenuLayout] Resources/" + ArtDay + ".bytes を読めない (SETTINGS を作らない)"); return; }
            var go = Object.Instantiate(gallery.gameObject, menu, false);       // 部品 (絵・当たり・SpriteButton・層) は GALLERY と同じ
            go.name = SettingsName;
            var btn = go.GetComponent<SpriteButton>();
            var sr = btn != null && btn.targetRenderer != null ? btn.targetRenderer : go.GetComponent<SpriteRenderer>();
            var dn = Object.FindObjectOfType<TitleDayNight>();
            bool isNight = dn != null && dn.mode == TitleDayNight.Mode.Night;
            if (sr != null) sr.sprite = isNight && night != null ? night : day;
            Place(go.transform, SettingsY, menuY, galleryX);      // 絵を替えてから置く (大きさで半端な 0.5 dot をそろえるため)
            FitCollider(btn);      // 当たりを SETTINGS の絵の範囲に
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
