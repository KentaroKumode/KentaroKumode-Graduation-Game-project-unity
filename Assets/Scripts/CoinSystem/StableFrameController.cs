using UnityEngine;

namespace CoinSystem
{
    /// <summary>
    /// VSync主導の安定60FPS制御
    /// accumulator方式で固定Δtロジック + 可変フレーム描画補間
    /// </summary>
    public class StableFrameController : MonoBehaviour
    {
        // 固定Δt（60Hz）
        private const float FIXED_DELTA_TIME = 1.0f / 60.0f;
        
        // スパイラル・オブ・デス防止（1フレームでの最大ロジック更新回数）
        private const int MAX_UPDATES_PER_FRAME = 5;
        
        // accumulator
        private double lastFrameTime;
        private double accumulator;
        
        // 補間係数（外部から参照可能）
        public float Alpha { get; private set; }
        
        // ロジック更新カウント（デバッグ用）
        private int logicUpdateCount;
        
        // デバッグ表示フラグ
        [SerializeField] private bool showDebugInfo = true;
        
        // FPS計測用
        private float fpsTimer;
        private int fpsFrameCount;
        private float currentFPS;
        
        void Awake()
        {
            // VSync主導でFPS制御（エディタでも強制適用）
            QualitySettings.vSyncCount = 1;
            Application.targetFrameRate = -1;
            
            // エディタ用：確実にVSyncを有効化
            Debug.Log($"[StableFrameController] VSync set to: {QualitySettings.vSyncCount}, targetFrameRate: {Application.targetFrameRate}");
            
            // 初期化
            lastFrameTime = Time.realtimeSinceStartupAsDouble;
            accumulator = 0.0;
            Alpha = 0f;
        }
        
        void Update()
        {
            // 実時間を取得（GC Allocなし）
            double currentTime = Time.realtimeSinceStartupAsDouble;
            double frameTime = currentTime - lastFrameTime;
            lastFrameTime = currentTime;
            
            // accumulatorに実時間を加算
            accumulator += frameTime;
            
            // 固定Δtで複数回ロジック更新（スパイラル・オブ・デス防止）
            int updates = 0;
            while (accumulator >= FIXED_DELTA_TIME && updates < MAX_UPDATES_PER_FRAME)
            {
                // 固定Δtでロジック更新
                FixedTick(FIXED_DELTA_TIME);
                
                accumulator -= FIXED_DELTA_TIME;
                updates++;
                logicUpdateCount++;
            }
            
            // 補間係数を計算（0.0～1.0）
            // 次のロジック更新までの進行度
            Alpha = (float)(accumulator / FIXED_DELTA_TIME);
            Alpha = Mathf.Clamp01(Alpha);
            
            // FPS計測
            if (showDebugInfo)
            {
                UpdateFPSCounter();
            }
        }
        
        void LateUpdate()
        {
            // 補間描画
            RenderTick(Alpha);
        }
        
        /// <summary>
        /// 固定Δtでのロジック更新（60Hz）
        /// </summary>
        private void FixedTick(float fixedDeltaTime)
        {
            // ここでゲームロジック更新
            // 物理演算、移動、状態遷移など
            // Time.deltaTimeは使わず、fixedDeltaTimeを使用
            
            // 他のコンポーネントに通知する場合は、イベントやコールバックを使用
            OnFixedTick?.Invoke(fixedDeltaTime);
        }
        
        /// <summary>
        /// 補間描画（可変フレーム）
        /// </summary>
        private void RenderTick(float alpha)
        {
            // 補間値を使った描画更新
            // Transform補間、カメラ追従など
            
            OnRenderTick?.Invoke(alpha);
        }
        
        /// <summary>
        /// FPS計測（デバッグ用）
        /// </summary>
        private void UpdateFPSCounter()
        {
            fpsTimer += Time.unscaledDeltaTime;
            fpsFrameCount++;
            
            if (fpsTimer >= 1.0f)
            {
                currentFPS = fpsFrameCount / fpsTimer;
                fpsTimer = 0f;
                fpsFrameCount = 0;
            }
        }
        
        void OnGUI()
        {
            // 完全に無効化（TLS Allocatorエラー防止）
            return;
        }
        
        // イベント（外部システムとの連携用）
        public System.Action<float> OnFixedTick;
        public System.Action<float> OnRenderTick;
    }
}
