using UnityEngine;

namespace CombatSystem.DiceLED
{
    /// <summary>
    /// 3×3 LEDグリッドで 1つのサイコロ面 を表現するコンポーネント。
    /// 
    /// <para>
    /// LED配置（Inspector での割り当て順）:
    /// <code>
    /// [0] [1] [2]
    /// [3] [4] [5]
    /// [6] [7] [8]
    /// </code>
    /// </para>
    /// 
    /// <para><b>パフォーマンス設計:</b></para>
    /// <list type="bullet">
    ///   <item>MaterialPropertyBlock で Emission を個別制御 → マテリアルコピー不要</item>
    ///   <item>isDirty フラグで変更時のみ GPU に送信</item>
    ///   <item>PropertyBlock は Awake で 9 個確保し使い回し（GC ゼロ）</item>
    /// </list>
    /// 
    /// <para><b>FBX セットアップ:</b></para>
    /// <list type="number">
    ///   <item>FBX 内で各 LED を個別メッシュとしてエクスポート</item>
    ///   <item>Unity にインポートすると子オブジェクト(MeshRenderer)になる</item>
    ///   <item>このコンポーネントをサイコロの親オブジェクトにアタッチ</item>
    ///   <item>ledRenderers[] に 9 個の LED Renderer をドラッグ、
    ///         または右クリック → "Auto-Assign LED Renderers" で自動検索</item>
    /// </list>
    /// </summary>
    public class SingleDiceLED : MonoBehaviour
    {
        // =================================================================
        //  Inspector
        // =================================================================

        [Header("LED Renderers（3×3 = 9個）")]
        [Tooltip("左上→右下の順で 9 個の MeshRenderer を設定")]
        [SerializeField] private Renderer[] ledRenderers = new Renderer[9];

        // =================================================================
        //  内部状態
        // =================================================================

        /// <summary>LED ごとの MaterialPropertyBlock（使い回し）</summary>
        private MaterialPropertyBlock[] propertyBlocks;

        /// <summary>各 LED の ON/OFF 状態</summary>
        private bool[] ledStates = new bool[9];

        /// <summary>描画更新が必要か</summary>
        private bool isDirty;

        /// <summary>現在の出目（0 = 消灯）</summary>
        private int currentValue;

        /// <summary>初期化済みフラグ</summary>
        private bool isInitialized;

        // --- 色設定（Manager から注入）---
        private Color onColor  = Color.white;
        private Color offColor = Color.black;
        private float emissionIntensity = 3f;

        // --- シェーダープロパティ ID キャッシュ ---
        private static readonly int EmissionColorID =
            Shader.PropertyToID("_EmissionColor");

        // =================================================================
        //  出目パターン定義（1～9）
        // =================================================================
        //
        //  LED 番号:
        //  [0] [1] [2]
        //  [3] [4] [5]
        //  [6] [7] [8]

        private static readonly bool[][] DicePatterns = new bool[][]
        {
            // 0: 全消灯
            new bool[] { false, false, false,
                         false, false, false,
                         false, false, false },

            // 1: 中央
            new bool[] { false, false, false,
                         false, true,  false,
                         false, false, false },

            // 2: 左中 + 右中
            new bool[] { false, false, false,
                         true,  false, true,
                         false, false, false },

            // 3: 左上 + 中央 + 右下（対角線）
            new bool[] { true,  false, false,
                         false, true,  false,
                         false, false, true  },

            // 4: 四隅
            new bool[] { true,  false, true,
                         false, false, false,
                         true,  false, true  },

            // 5: 四隅 + 中央
            new bool[] { true,  false, true,
                         false, true,  false,
                         true,  false, true  },

            // 6: 左列 + 右列
            new bool[] { true,  false, true,
                         true,  false, true,
                         true,  false, true  },

            // 7: 左列 + 右列 + 中央
            new bool[] { true,  false, true,
                         true,  true,  true,
                         true,  false, true  },

            // 8: 全点灯 − 中央
            new bool[] { true,  true,  true,
                         true,  false, true,
                         true,  true,  true  },

            // 9: 全点灯
            new bool[] { true,  true,  true,
                         true,  true,  true,
                         true,  true,  true  },
        };

        // =================================================================
        //  ライフサイクル
        // =================================================================

        void Awake()
        {
            EnsureInitialized();
        }

        private void EnsureInitialized()
        {
            if (isInitialized) return;
            isInitialized = true;

            propertyBlocks = new MaterialPropertyBlock[9];
            for (int i = 0; i < 9; i++)
            {
                propertyBlocks[i] = new MaterialPropertyBlock();
            }
        }

        // =================================================================
        //  公開 API
        // =================================================================

        /// <summary>
        /// LED の発光色と強度を設定（Manager から呼ばれる）
        /// </summary>
        public void SetColors(Color on, Color off, float intensity)
        {
            onColor            = on;
            offColor           = off;
            emissionIntensity  = intensity;
            isDirty            = true;
        }

        /// <summary>
        /// サイコロの出目を設定（1～9）。0 で全消灯。
        /// </summary>
        public void SetValue(int value)
        {
            value = Mathf.Clamp(value, 0, 9);
            if (currentValue == value) return;

            currentValue = value;
            var pattern  = DicePatterns[value];

            for (int i = 0; i < 9; i++)
                ledStates[i] = pattern[i];

            isDirty = true;
        }

        /// <summary>全 LED 消灯</summary>
        public void TurnOff() => SetValue(0);

        /// <summary>
        /// ランダム出目に設定（ローリングアニメーション用）
        /// </summary>
        public void SetRandomValue(int maxValue)
        {
            // SetValue の同値スキップを回避するため直接パターン書き込み
            int v = Random.Range(1, Mathf.Clamp(maxValue, 1, 9) + 1);
            currentValue = v;
            var pattern  = DicePatterns[v];

            for (int i = 0; i < 9; i++)
                ledStates[i] = pattern[i];

            isDirty = true;
        }

        /// <summary>
        /// MaterialPropertyBlock を GPU に送信して描画を更新する。
        /// isDirty == false なら何もしない。
        /// </summary>
        public void ApplyVisuals()
        {
            EnsureInitialized();

            if (!isDirty) return;
            isDirty = false;

            for (int i = 0; i < 9; i++)
            {
                if (ledRenderers[i] == null) continue;

                Color emission = ledStates[i]
                    ? onColor * emissionIntensity
                    : offColor;

                ledRenderers[i].GetPropertyBlock(propertyBlocks[i]);
                propertyBlocks[i].SetColor(EmissionColorID, emission);
                ledRenderers[i].SetPropertyBlock(propertyBlocks[i]);
            }
        }

        /// <summary>現在の出目を取得</summary>
        public int CurrentValue => currentValue;

        // =================================================================
        //  エディタ用ヘルパー
        // =================================================================

        /// <summary>
        /// 子オブジェクトの MeshRenderer を座標から自動判定して
        /// 3×3 グリッドにマッピングする。
        /// 
        /// 判定ルール:
        ///   ローカル座標で Z 昇順（小さい Z = 上段）、
        ///   同じ行内で X 降順（大きい X = 左列）でソート。
        ///   名前に依存しないため、任意のメッシュ名で OK。
        /// 
        /// <code>
        ///   X大,Z小 [0]  [1]  [2] X小,Z小
        ///            [3]  [4]  [5]
        ///   X大,Z大 [6]  [7]  [8] X小,Z大
        /// </code>
        /// 
        /// 右クリック → "Auto-Assign LEDs (座標ソート)" で実行可能。
        /// </summary>
        [ContextMenu("Auto-Assign LEDs (座標ソート)")]
        public void AutoAssignRenderers()
        {
            var renderers = GetChildRenderers();

            if (renderers.Count < 9)
            {
                Debug.LogWarning($"[SingleDiceLED] 子 Renderer が {renderers.Count} 個" +
                                 $"（9 個必要）");
                return;
            }

            var sorted = SortRenderersByPosition(renderers);

            ledRenderers = new Renderer[9];
            for (int i = 0; i < 9; i++)
                ledRenderers[i] = sorted[i];

            Debug.Log($"[SingleDiceLED] 座標ソートで {renderers.Count} 個から 9 個を割り当て:");
            for (int i = 0; i < 9; i++)
            {
                var lp = sorted[i].transform.localPosition;
                Debug.Log($"  [{i}] {sorted[i].gameObject.name}" +
                          $"  (X={lp.x:F3}, Z={lp.z:F3})");
            }
        }

        /// <summary>
        /// 自分自身を除く全子 Renderer を取得
        /// </summary>
        private System.Collections.Generic.List<Renderer> GetChildRenderers()
        {
            var all = GetComponentsInChildren<Renderer>(true);
            var list = new System.Collections.Generic.List<Renderer>();
            foreach (var r in all)
            {
                if (r.gameObject != gameObject)
                    list.Add(r);
            }
            return list;
        }

        /// <summary>
        /// Renderer リストをローカル座標で 3×3 グリッド順にソート。
        /// 
        /// 1) Z 昇順で 3 行に分割（小 Z = Row0、大 Z = Row2）
        /// 2) 各行内で X 降順（大 X = 左 = index 0）
        /// </summary>
        public static System.Collections.Generic.List<Renderer> SortRenderersByPosition(
            System.Collections.Generic.List<Renderer> renderers)
        {
            // まず Z 昇順でソート
            var byZ = new System.Collections.Generic.List<Renderer>(renderers);
            byZ.Sort((a, b) =>
                a.transform.localPosition.z.CompareTo(
                    b.transform.localPosition.z));

            // 3行に分割（先頭3個 = Row0, 次の3個 = Row1, 次の3個 = Row2）
            var result = new System.Collections.Generic.List<Renderer>();
            for (int row = 0; row < 3; row++)
            {
                int start = row * 3;
                var rowRenderers = new System.Collections.Generic.List<Renderer>();
                for (int i = start; i < start + 3 && i < byZ.Count; i++)
                    rowRenderers.Add(byZ[i]);

                // 行内で X 降順（大きい X = 左 = index 0）
                rowRenderers.Sort((a, b) =>
                    b.transform.localPosition.x.CompareTo(
                        a.transform.localPosition.x));

                result.AddRange(rowRenderers);
            }

            return result;
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (ledRenderers != null && ledRenderers.Length == 9
                && propertyBlocks != null && Application.isPlaying)
            {
                isDirty = true;
                ApplyVisuals();
            }
        }
#endif
    }
}
