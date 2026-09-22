using TMPro;
using UnityEngine;

namespace UI.ClassSelect
{
    /// <summary>
    /// 職業選択の「撮影リグ」 をタイトル画面と同一方式で構築する (2026-07-07)。
    ///
    /// 方式: シーン内の専用ゾーン (タイトルコンテンツカメラから X+2000) に UI を置き、
    /// 専用の正射影カメラで撮影 → LcdAuxCamera で LcdScreen の RenderTexture へ転写。
    /// カメラ depth をタイトルより +1 にし、 SolidColor クリアで全面を描くため、
    /// カメラの有効/無効だけで「タイトル ⇔ 職業選択」 の画面切替になる。
    ///
    /// 実行時 (TitleMenuBootstrap) とエディタ (ClassSelectAutoSetup) の双方から呼ぶ共通ビルダー。
    /// </summary>
    public static class ClassSelectRigBuilder
    {
        /// <summary>タイトルゾーンからの X オフセット (既存ゾーン 1000/1500/2000/2500 と衝突しない距離)。</summary>
        public const float ZoneOffsetX = 2000f;

        public static ClassSelectView Build(Lcd.LcdScreen lcd)
        {
            var titleCam = lcd != null ? lcd.contentCamera : null;
            if (titleCam == null)
            {
                Debug.LogWarning("[ClassSelectRigBuilder] LcdScreen.contentCamera が無いため構築できない");
                return null;
            }

            var rig = new GameObject("[ClassSelectRig]");
            rig.transform.position = titleCam.transform.position + new Vector3(ZoneOffsetX, 0f, 0f);
            rig.transform.rotation = titleCam.transform.rotation;

            // ---- 撮影カメラ (タイトルのコンテンツカメラ設定を複製) ----
            var camGo = new GameObject("ClassSelectCamera");
            camGo.transform.SetParent(rig.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = titleCam.orthographic;
            cam.orthographicSize = titleCam.orthographicSize;
            cam.fieldOfView = titleCam.fieldOfView;
            cam.nearClipPlane = titleCam.nearClipPlane;
            cam.farClipPlane = titleCam.farClipPlane;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.08f, 0.07f, 0.06f); // 暗い机の上 (書類が浮く)
            cam.cullingMask = ~0;                 // 専用ゾーンなので全レイヤー可 (他ゾーンは写野外)
            cam.depth = titleCam.depth + 1;       // タイトルの後に描いて RT を上書き
            var aux = camGo.AddComponent<Lcd.LcdAuxCamera>();
            aux.lcdScreen = lcd;

            // ---- UI 本体 (カメラ正面 5 ユニット) ----
            var uiGo = new GameObject("ClassSelectUI");
            uiGo.transform.SetParent(rig.transform, false);
            uiGo.transform.position = camGo.transform.position + camGo.transform.forward * 5f;
            uiGo.transform.rotation = camGo.transform.rotation;

            uiGo.SetActive(false); // Awake(Build) 前に設定値を流し込む
            var view = uiGo.AddComponent<ClassSelectView>();
            view.pointerCamera = cam;
            view.rigCamera = cam;
            view.showOnStart = false;
            view.buttonsSelfRaycast = Object.FindObjectOfType<Lcd.LcdPointer>() == null;
            if (cam.orthographic)
                view.dotWorldSize = cam.orthographicSize * 2f / 139f; // 556px÷4px/dot=139dot が写野の縦
            view.font = FindJapaneseFont();
            uiGo.SetActive(true);

            // LcdPointer が contentMask で限定レイヤの Collider しか拾わないので、
            // Pointer が居る場合は Rig 内全 GameObject をその対応レイヤに揃える (未合わせだとクリック不発)。
            var pointer = Object.FindObjectOfType<Lcd.LcdPointer>();
            if (pointer != null)
            {
                int layer = FirstLayerFromMask(pointer.contentMask);
                if (layer >= 0)
                {
                    SetLayerRecursively(rig, layer);
                    Debug.Log($"[ClassSelectRigBuilder] Rig 全体を LcdPointer.contentMask 対応レイヤ ({LayerMask.LayerToName(layer)}) に設定");
                }
            }

            // 表示制御は view.Show()/Hide() が握る ── 初期はカメラ無効 = タイトルが映ったまま
            camGo.SetActive(false);

            Debug.Log($"[ClassSelectRigBuilder] 構築完了 zoneX={rig.transform.position.x:F0} " +
                      $"dot={view.dotWorldSize:F4} selfRaycast={view.buttonsSelfRaycast} " +
                      $"font={(view.font != null ? view.font.name : "<TMP default>")}");
            return view;
        }

        /// <summary>LayerMask から立っている最初のビット位置 (レイヤ番号) を返す。 該当なしなら -1。</summary>
        private static int FirstLayerFromMask(LayerMask mask)
        {
            int m = mask.value;
            for (int i = 0; i < 32; i++)
                if ((m & (1 << i)) != 0) return i;
            return -1;
        }

        /// <summary>Rig 内の全 GameObject を指定レイヤに設定 (Camera 含む)。</summary>
        private static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform c in go.transform) SetLayerRecursively(c.gameObject, layer);
        }

        /// <summary>ロード済み TMP フォントから日本語対応らしきものを探す。</summary>
        public static TMP_FontAsset FindJapaneseFont()
        {
            TMP_FontAsset fallback = null;
            foreach (var f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            {
                if (f == null) continue;
                string n = f.name.ToLowerInvariant();
                if (n.Contains("jp") || n.Contains("noto") || n.Contains("japan")
                    || n.Contains("gothic") || n.Contains("mincho") || n.Contains("meiryo"))
                    return f;
                if (fallback == null && !n.Contains("liberation")) fallback = f;
            }
            return fallback; // null なら TMP 既定に任せる
        }
    }
}
