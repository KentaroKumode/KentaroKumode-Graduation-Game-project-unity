using UnityEngine;
using System;

namespace CoinSystem
{
    /// <summary>
    /// ロジック60Hz固定、描画はVSync依存のフレーム制御
    /// 60Hzモニター: 60FPS表示
    /// 144Hzモニター: 144FPS表示、体感60Hz
    /// </summary>
    public class FixedLogicFrameController : MonoBehaviour
    {
        // 固定Δt（60Hz）
        private const double FIXED_DELTA_TIME = 1.0 / 60.0;
        
        // スパイラル・オブ・デス防止
        private const int MAX_UPDATES_PER_FRAME = 5;
        
        // accumulator
        private double lastFrameTime;
        private double accumulator;
        
        // 補間係数（0-1、外部から参照可能）
        public float Alpha { get; private set; }
        
        // イベント
        public event Action<float> OnFixedLogicUpdate; // 固定60Hzロジック
        public event Action<float> OnRenderUpdate;     // 補間描画
        
        void Start()
        {
            // VSync主導、CPU FPS制限なし
            QualitySettings.vSyncCount = 1;
            Application.targetFrameRate = -1;
            
            // 初期化
            lastFrameTime = Time.realtimeSinceStartupAsDouble;
            accumulator = 0.0;
            Alpha = 0f;
            
            Debug.Log($"[FixedLogicFrameController] VSync={QualitySettings.vSyncCount}, targetFrameRate={Application.targetFrameRate}");
        }
        
        void Update()
        {
            // 実時間計測（GC Allocなし）
            double currentTime = Time.realtimeSinceStartupAsDouble;
            double frameTime = currentTime - lastFrameTime;
            lastFrameTime = currentTime;
            
            // accumulator加算
            accumulator += frameTime;
            
            // 固定ロジック更新ループ（スパイラル・オブ・デス防止）
            int updates = 0;
            while (accumulator >= FIXED_DELTA_TIME && updates < MAX_UPDATES_PER_FRAME)
            {
                // 固定60Hzロジック実行
                OnFixedLogicUpdate?.Invoke((float)FIXED_DELTA_TIME);
                
                accumulator -= FIXED_DELTA_TIME;
                updates++;
            }
            
            // スパイラル・オブ・デス検出
            if (updates >= MAX_UPDATES_PER_FRAME && accumulator >= FIXED_DELTA_TIME)
            {
                // accumulatorリセット
                accumulator = 0.0;
            }
            
            // 補間係数算出（0-1にクランプ）
            Alpha = Mathf.Clamp01((float)(accumulator / FIXED_DELTA_TIME));
        }
        
        void LateUpdate()
        {
            // 補間描画実行
            OnRenderUpdate?.Invoke(Alpha);
        }
    }
}
