using UnityEngine;

namespace CoinSystem
{
    /// <summary>
    /// CoinSystemの簡単セットアップ用コンポーネント
    /// GameObjectにアタッチしてPlay modeに入るだけでCoinSystemが使用可能になります
    /// </summary>
    public class CoinSystemManager : MonoBehaviour
    {
        [Header("基本設定")]
        [SerializeField] private GameObject coinPrefab; // Assets/Prefabs/CoinSystem/coin.prefab をアサイン
        [SerializeField] private bool autoSetup = true;
        [SerializeField] private bool createInterface = true;
        
        [Header("ディスペンサー設定")]
        [SerializeField] private Vector3 dispenserPosition = new Vector3(0, 2, 0);
        [SerializeField] private Vector3 dispenserRotation = new Vector3(0, 0, 0); // Euler角
        [SerializeField] private Vector3 dispenserForceDirection = Vector3.right;
        [SerializeField] private float dispenseForce = 8f; // より滑らせるため増加
        [SerializeField] private float dispenseInterval = 0.1f;
        [SerializeField] private float randomForceRange = 1f;
        
        [Header("ポットターゲット設定")]
        [SerializeField] private Vector3 potTargetPosition = new Vector3(0, 0, 2);
        
        [Header("スタック設定")]
        [SerializeField] private Vector3 stackStartPosition = new Vector3(0, 0, 3);
        [SerializeField] private Vector3 stackDirection = Vector3.up; // 縦方向の積み上げ
        [SerializeField] private Vector3 stackGroupDirection = Vector3.right; // 横方向のグループ間隔
        [SerializeField] private float stackSpacing = 0.02f; // コイン間の縦間隔
        [SerializeField] private float stackGroupSpacing = 0.3f; // 束間の横間隔
        [SerializeField] private int coinsPerStack = 10; // 1束あたりのコイン数
        
        [Header("物理設定")]
        [SerializeField] private float coinMass = 0.05f; // 軽くして滑りやすく
        [SerializeField] private float coinDrag = 0.1f; // 大幅に減らして滑りやすく
        [SerializeField] private float coinAngularDrag = 0.02f; // 回転抵抗を減らす
        
        [Header("アニメーション設定")]
        [SerializeField] private float sortAnimationDuration = 0.1f;
        [SerializeField] private float stackingDelay = 0.05f; // 100枚時のパフォーマンス向上のため短縮
        [SerializeField] private float stackGroupDelay = 0.5f; // 山と山の間の遅延時間
        [SerializeField] private bool fastStackingMode = false; // 高速積み上げモード
        
        [Header("デバッグ・診断設定")]
        [SerializeField] private bool enableMemoryLeakDetection = true;
        [SerializeField] private bool enableTextureLeakDetection = true;
        [SerializeField] private bool enableDebugLogger = true;
        
        [Header("可変速度設定")]
        [SerializeField] private bool enableVariableSpeed = false; // 可変速度モードの有効/無効
        [SerializeField] private int speedScaleThreshold = 100; // この枚数で最大速度になる
        [SerializeField] [Range(0.01f, 1.0f)] private float minDispenseInterval = 0.02f; // 最高速度時の排出間隔
        [SerializeField] [Range(0.01f, 1.0f)] private float maxDispenseInterval = 0.2f; // 最低速度時の排出間隔
        [SerializeField] [Range(0.01f, 0.5f)] private float minStackingDelay = 0.01f; // 最高速度時の積み上げ間隔
        [SerializeField] [Range(0.01f, 0.5f)] private float maxStackingDelay = 0.2f; // 最低速度時の積み上げ間隔
        
        [Header("チケット変換システム")]
        [SerializeField] private bool enableTicketSystem = true; // チケットシステムの有効/無効
        [SerializeField] private GameObject ticketPrefab; // チケットのプレハブ
        [SerializeField] private int coinsPerTicket = 10; // 1チケットあたりのコイン数
        [SerializeField] private int coinThresholdHigh = 60; // コイン→チケット変換闾値
        [SerializeField] private int coinThresholdLow = 50; // チケット→コイン変換闾値
        [SerializeField] private Vector3 ticketMachinePosition = new Vector3(2, 1, 0); // 発券機の位置
        [SerializeField] private Vector3 ticketDirection = Vector3.down; // チケットの伸びる方向
        [SerializeField] private float ticketSpacing = 0.1f; // チケット間の間隔
        [SerializeField] private float ticketDispenseDistance = 0.05f; // チケット払い出し距離
        
        [Header("音声設定")]
        [SerializeField] private AudioClip[] coinSounds; // 2-4個のコイン音を設定
        [SerializeField] private bool enableCoinSounds = true;
        [SerializeField] [Range(0f, 1f)] private float coinSoundVolume = 0.7f;
        [SerializeField] [Range(2, 8)] private int maxCoinAudioSources = 4; // 同時再生可能なコイン音数
        [SerializeField] [Range(0.5f, 2.0f)] private float coinSoundPitchMin = 0.8f; // コイン音の最低ピッチ
        [SerializeField] [Range(0.5f, 2.0f)] private float coinSoundPitchMax = 1.2f; // コイン音の最高ピッチ
        
        [Header("払い出し固定音設定")]
        [SerializeField] private AudioClip dispensingSound; // 払い出し時の固定音
        [SerializeField] private bool enableDispensingSound = true;
        [SerializeField] [Range(0f, 1f)] private float dispensingSoundVolume = 0.8f;
        [SerializeField] [Range(0.5f, 2.0f)] private float dispensingSoundPitch = 1.0f; // 払い出し音のピッチ
        
        [Header("積み上げ音声設定")]
        [SerializeField] private AudioClip stackSound; // 積み上げ音
        [SerializeField] private bool enableStackSound = true;
        [SerializeField] [Range(0f, 1f)] private float stackSoundVolume = 0.6f;
        [SerializeField] [Range(0.5f, 2.0f)] private float stackPitchMin = 0.8f; // 1枚目のピッチ
        [SerializeField] [Range(0.5f, 2.0f)] private float stackPitchMax = 1.4f; // 10枚目のピッチ
        
        [Header("チケット音声設定")]
        [SerializeField] private AudioClip ticketDispenseSound; // チケット排出音
        [SerializeField] private bool enableTicketSound = true;
        [SerializeField] [Range(0f, 1f)] private float ticketSoundVolume = 0.7f;
        [SerializeField] [Range(0.5f, 2.0f)] private float ticketSoundPitch = 1.0f;
        [SerializeField] [Range(0f, 1.0f)] private float ticketSoundDelay = 0.0f; // 音声再生後のディレイ
        [SerializeField] [Range(0.1f, 10.0f)] private float ticketEmergenceDuration = 3.0f; // 排出アニメーション時間
        [SerializeField] [Range(0f, 0.5f)] private float ticketRandomVelocity = 0.1f; // 着地後のランダム初速度
        [SerializeField] [Range(0f, 10.0f)] private float ticketCoinSoundDelay = 0.0f; // 発券からコイン音までの遅延
        [SerializeField] [Range(0.5f, 2.0f)] private float ticketCoinSoundPitch = 1.0f; // コイン音のピッチ
        
        [Header("ディスプレイシステム設定")]
        [SerializeField] private bool enableDisplaySystem = false; // ディスプレイシステムの有効/無効
        [SerializeField] private GameObject displayMeshObject; // ディスプレイ用メッシュ(オプション、未設定時は自動生成)
        [SerializeField] private Vector3 displayPosition = new Vector3(0, 2, 0); // ディスプレイ位置(自動生成時)
        [SerializeField] private Vector3 displayRotation = new Vector3(0, 180, 0); // ディスプレイ回転(自動生成時)
        [SerializeField] private Vector3 displayScale = new Vector3(0.344f, 1.11f, 1f); // ディスプレイスケール(自動生成時)
        [SerializeField] private Texture2D displayTileSheet; // 32x32の数字タイル画像
        [SerializeField] private int displayMaxDigits = 3; // 最大桁数
        [SerializeField] private int displayWidth = 344; // ディスプレイ幅
        [SerializeField] private int displayHeight = 1110; // ディスプレイ高さ
        [SerializeField] [Range(1, 64)] private int displayDigitScaleX = 8; // 数字の横スケール(整数倍)
        [SerializeField] [Range(1, 64)] private int displayDigitScaleY = 8; // 数字の縦スケール(整数倍)
        [SerializeField] [Range(-20f, 50f)] private float displayDigitSpacing = 4f; // 数字間隔（負数で詰める、正数で広げる）
        [SerializeField] private TextAnchor displayAlignment = TextAnchor.UpperRight; // 数字の表示位置
        
        [Header("アイコン設定")]
        [SerializeField] private Texture2D displayIconTexture; // アイコン画像（例：コイン）
        [SerializeField] [Range(1, 32)] private int displayIconScaleX = 4; // アイコンの横スケール
        [SerializeField] [Range(1, 32)] private int displayIconScaleY = 4; // アイコンの縦スケール
        [SerializeField] [Range(-2500, 2500)] private int displayIconMarginX = 4; // アイコンの横マージン（画面端から、負の数可）
        [SerializeField] [Range(-2500, 2500)] private int displayIconMarginY = 4; // アイコンの縦マージン（画面端から、負の数可）
        [SerializeField] [Range(-2500, 2500)] private int displayIconSpacing = 10; // アイコンと数字の間隔（負の数可）
        
        [Header("マージン設定")]
        [SerializeField] [Range(-2500, 2500)] private int displayMarginX = 4; // 横マージン（負の数可）
        [SerializeField] [Range(-2500, 2500)] private int displayMarginY = 4; // 縦マージン（負の数可）
        [SerializeField] private bool displayUseEmissive = true; // 発光効果
        [SerializeField] private float displayEmissiveIntensity = 1.0f; // 発光強度
        [SerializeField] private Color displayColor = Color.green; // 背景色
        [SerializeField] private Color displayTextColor = Color.white; // 文字色
        
        [Header("液晶エフェクト設定")]
        [SerializeField] private bool displayEnableLCDEffect = true; // 液晶エフェクトを有効化
        [SerializeField] [Range(0f, 1.0f)] private float displayPixelGap = 0.15f; // ピクセル間のギャップ（0-1.0）
        [SerializeField] private bool displayEnableEdgeGradient = false; // 数字のフチにグラデーションをかける
        [SerializeField] [Range(0f, 1.0f)] private float displayEdgeGradientStrength = 0.3f; // フチグラデーションの強さ
        [SerializeField] [Range(0f, 1f)] private float displayGlowIntensity = 0.0f; // グロー強度（パフォーマンス重視でデフォルトOFF）
        [SerializeField] private bool displayEnableScanlines = true; // スキャンライン効果
        [SerializeField] [Range(0f, 1.0f)] private float displayScanlineIntensity = 0.2f; // スキャンライン暗さ（0-1.0）
        [SerializeField] [Range(1, 100)] private int displayScanlineWidth = 2; // スキャンラインの間隔（ピクセル数）
        [SerializeField] [Range(1, 50)] private int displayScanlineThickness = 1; // スキャンラインの太さ（ピクセル数）
        [SerializeField] private bool displayScanlineGradient = true; // スキャンラインにグラデーションをかける
        [SerializeField] [Range(0f, 0.3f)] private float displayColorTint = 0.1f; // 色温度（青緑がかり）
        
        [Header("アウトライングロー設定")]
        [SerializeField] private bool displayEnableOutlineGlow = false; // アウトライングローを有効化（パフォーマンス重視でデフォルトOFF）
        [SerializeField] [Range(1, 5)] private int displayOutlineGlowRadius = 2; // グロー半径
        [SerializeField] [Range(0f, 1f)] private float displayOutlineGlowIntensity = 0.5f; // グロー強度
        [SerializeField] private Color displayOutlineGlowColor = Color.white; // グロー色
        
        [Header("アンチエイリアス・モアレ対策")]
        [SerializeField] private bool displayEnableMipmaps = true; // ミップマップでモアレ軽減
        [SerializeField] private FilterMode displayTextureFilterMode = FilterMode.Trilinear; // Point=シャープ、Bilinear=滑らか、Trilinear=最高品質
        [SerializeField] [Range(0, 16)] private int displayAnisotropicLevel = 4; // 異方性フィルタリング（斜めから見たときの品質向上）
        
        void Awake()
        {
            if (autoSetup)
            {
                SetupCoinSystem();
            }
        }
        
        [ContextMenu("Setup CoinSystem")]
        public void SetupCoinSystem()
        {
            Debug.Log("=== CoinSystem Setup Started ===");
            
            // ロジック60Hz固定、描画VSync依存のフレーム制御
            FixedLogicFrameController frameController = FindObjectOfType<FixedLogicFrameController>();
            if (frameController == null)
            {
                GameObject frameControllerObj = new GameObject("FixedLogicFrameController");
                frameController = frameControllerObj.AddComponent<FixedLogicFrameController>();
                Debug.Log("FixedLogicFrameController created: Logic 60Hz, Render at monitor refresh rate");
            }
            
            // 1. CoinSystemController を作成または取得
            CoinSystemController controller = FindObjectOfType<CoinSystemController>();
            if (controller == null)
            {
                GameObject controllerObj = new GameObject("CoinSystemController");
                controller = controllerObj.AddComponent<CoinSystemController>();
                
                // 新しいコンポーネントを追加
                CoinPoolManager poolManager = controllerObj.AddComponent<CoinPoolManager>();
                CoinAudioManager audioManager = controllerObj.AddComponent<CoinAudioManager>();
                CoinStackManager stackManager = controllerObj.AddComponent<CoinStackManager>();
                CoinPhysicsManager physicsManager = controllerObj.AddComponent<CoinPhysicsManager>();
                CoinDispenser dispenserManager = controllerObj.AddComponent<CoinDispenser>();
                TicketSystemManager ticketManager = controllerObj.AddComponent<TicketSystemManager>();
                PaymentManager paymentManager = controllerObj.AddComponent<PaymentManager>();
                CoinTicketConversionManager conversionManager = controllerObj.AddComponent<CoinTicketConversionManager>();
                
                // 位置参照用GameObjectを作成（ディスプレイもここで作成される）
                CreateReferenceObjects(controller);
                
                // コンポーネントに設定を適用
                ApplyPoolManagerSettings(poolManager);
                ApplyAudioManagerSettings(audioManager);
                ApplyStackManagerSettings(stackManager);
                ApplyPhysicsManagerSettings(physicsManager);
                ApplyTicketManagerSettings(ticketManager, controller);
                
                // コインプレハブを先に設定（Awakeの初期化より前）
                if (coinPrefab != null)
                {
                    poolManager.SetCoinPrefab(coinPrefab);
                    Debug.Log("CoinPrefab assigned to PoolManager");
                }
                else
                {
                    Debug.LogWarning("CoinPrefab not assigned! Please assign coin.prefab to CoinSystemManager.");
                }
                
                // CoinSystemControllerの設定を適用（残りの設定）
                ApplyControllerSettings(controller);
                
                // チケットプレハブ設定
                if (enableTicketSystem && ticketPrefab != null)
                {
                    ticketManager.SetTicketPrefab(ticketPrefab);
                    Debug.Log("TicketPrefab assigned to TicketManager");
                }
                
                // ディスプレイ設定は既にCreateReferenceObjects内で適用済み
                
                Debug.Log("CoinDispenser created with modular components");
            }
            else
            {
                // 既存のコントローラーに新しいコンポーネントを追加または取得
                CoinPoolManager poolManager = controller.GetComponent<CoinPoolManager>();
                if (poolManager == null) poolManager = controller.gameObject.AddComponent<CoinPoolManager>();
                
                CoinAudioManager audioManager = controller.GetComponent<CoinAudioManager>();
                if (audioManager == null) audioManager = controller.gameObject.AddComponent<CoinAudioManager>();
                
                CoinStackManager stackManager = controller.GetComponent<CoinStackManager>();
                if (stackManager == null) stackManager = controller.gameObject.AddComponent<CoinStackManager>();
                
                CoinPhysicsManager physicsManager = controller.GetComponent<CoinPhysicsManager>();
                if (physicsManager == null) physicsManager = controller.gameObject.AddComponent<CoinPhysicsManager>();
                
                CoinDispenser dispenserManager = controller.GetComponent<CoinDispenser>();
                if (dispenserManager == null) dispenserManager = controller.gameObject.AddComponent<CoinDispenser>();
                
                TicketSystemManager ticketManager = controller.GetComponent<TicketSystemManager>();
                if (ticketManager == null) ticketManager = controller.gameObject.AddComponent<TicketSystemManager>();
                
                PaymentManager paymentManager = controller.GetComponent<PaymentManager>();
                if (paymentManager == null) paymentManager = controller.gameObject.AddComponent<PaymentManager>();
                
                CoinTicketConversionManager conversionManager = controller.GetComponent<CoinTicketConversionManager>();
                if (conversionManager == null) conversionManager = controller.gameObject.AddComponent<CoinTicketConversionManager>();
                
                // ディスプレイシステム
                TiledPixelDisplay pixelDisplay = controller.GetComponent<TiledPixelDisplay>();
                if (enableDisplaySystem && pixelDisplay == null)
                {
                    pixelDisplay = controller.gameObject.AddComponent<TiledPixelDisplay>();
                }
                
                // 設定を適用
                ApplyPoolManagerSettings(poolManager);
                ApplyAudioManagerSettings(audioManager);
                ApplyStackManagerSettings(stackManager);
                ApplyPhysicsManagerSettings(physicsManager);
                ApplyTicketManagerSettings(ticketManager, controller);
                ApplyControllerSettings(controller);
                
                // ディスプレイ設定を適用
                if (enableDisplaySystem)
                {
                    TiledPixelDisplay display = controller.GetComponent<TiledPixelDisplay>();
                    if (display != null)
                    {
                        ApplyDisplaySettings(display, controller);
                    }
                }
                
                Debug.Log("Existing CoinSystemController updated with modular components");
            }
            
            // 2. ユーザーインターフェースを作成
            if (createInterface && FindObjectOfType<CoinDispenserTest>() == null)
            {
                GameObject interfaceObj = new GameObject("CoinSystemInterface");
                CoinDispenserTest coinInterface = interfaceObj.AddComponent<CoinDispenserTest>();
                
                // CoinDispenserへの参照を設定
                CoinDispenser dispenserManager = controller.GetComponent<CoinDispenser>();
                SetDispenserReference(coinInterface, dispenserManager);
                
                Debug.Log("CoinSystem interface created");
            }
            
            Debug.Log("CoinSystem setup complete! Controls: Q=dispense, R=reset");
        }
        
        /// <summary>
        /// CoinPoolManagerに設定を適用
        /// </summary>
        private void ApplyPoolManagerSettings(CoinPoolManager poolManager)
        {
            SetPrivateField(poolManager, "coinPrefab", coinPrefab);
            SetPrivateField(poolManager, "maxConcurrentCoins", 300);
            SetPrivateField(poolManager, "coinMass", coinMass);
            SetPrivateField(poolManager, "coinDrag", coinDrag);
            SetPrivateField(poolManager, "coinAngularDrag", coinAngularDrag);
            
            Debug.Log("CoinPoolManager settings applied");
        }
        
        /// <summary>
        /// CoinAudioManagerに設定を適用
        /// </summary>
        private void ApplyAudioManagerSettings(CoinAudioManager audioManager)
        {
            SetPrivateField(audioManager, "coinSounds", coinSounds);
            SetPrivateField(audioManager, "enableCoinSounds", enableCoinSounds);
            SetPrivateField(audioManager, "coinSoundVolume", coinSoundVolume);
            SetPrivateField(audioManager, "coinSoundPitchMin", coinSoundPitchMin);
            SetPrivateField(audioManager, "coinSoundPitchMax", coinSoundPitchMax);
            SetPrivateField(audioManager, "maxCoinAudioSources", maxCoinAudioSources);
            
            SetPrivateField(audioManager, "dispensingSound", dispensingSound);
            SetPrivateField(audioManager, "enableDispensingSound", enableDispensingSound);
            SetPrivateField(audioManager, "dispensingSoundVolume", dispensingSoundVolume);
            SetPrivateField(audioManager, "dispensingSoundPitch", dispensingSoundPitch);
            
            SetPrivateField(audioManager, "stackSound", stackSound);
            SetPrivateField(audioManager, "enableStackSound", enableStackSound);
            SetPrivateField(audioManager, "stackSoundVolume", stackSoundVolume);
            SetPrivateField(audioManager, "stackPitchMin", stackPitchMin);
            SetPrivateField(audioManager, "stackPitchMax", stackPitchMax);
            
            SetPrivateField(audioManager, "ticketDispenseSound", ticketDispenseSound);
            SetPrivateField(audioManager, "enableTicketSound", enableTicketSound);
            SetPrivateField(audioManager, "ticketSoundVolume", ticketSoundVolume);
            SetPrivateField(audioManager, "ticketSoundPitch", ticketSoundPitch);
            
            Debug.Log("CoinAudioManager settings applied");
        }
        
        /// <summary>
        /// CoinStackManagerに設定を適用
        /// </summary>
        private void ApplyStackManagerSettings(CoinStackManager stackManager)
        {
            SetPrivateField(stackManager, "stackDirection", stackDirection);
            SetPrivateField(stackManager, "stackGroupDirection", stackGroupDirection);
            SetPrivateField(stackManager, "stackSpacing", stackSpacing);
            SetPrivateField(stackManager, "stackGroupSpacing", stackGroupSpacing);
            SetPrivateField(stackManager, "coinsPerStack", coinsPerStack);
            SetPrivateField(stackManager, "sortAnimationDuration", sortAnimationDuration);
            SetPrivateField(stackManager, "stackGroupDelay", stackGroupDelay);
            SetPrivateField(stackManager, "fastStackingMode", fastStackingMode);
            
            SetPrivateField(stackManager, "enableVariableSpeed", enableVariableSpeed);
            SetPrivateField(stackManager, "speedScaleThreshold", speedScaleThreshold);
            SetPrivateField(stackManager, "minStackingDelay", minStackingDelay);
            SetPrivateField(stackManager, "maxStackingDelay", maxStackingDelay);
            
            Debug.Log("CoinStackManager settings applied");
        }
        
        /// <summary>
        /// TicketSystemManagerに設定を適用
        /// </summary>
        private void ApplyTicketManagerSettings(TicketSystemManager ticketManager, CoinSystemController controller)
        {
            SetPrivateField(ticketManager, "enableTicketSystem", enableTicketSystem);
            SetPrivateField(ticketManager, "ticketPrefab", ticketPrefab);
            SetPrivateField(ticketManager, "coinsPerTicket", coinsPerTicket);
            SetPrivateField(ticketManager, "coinThresholdHigh", coinThresholdHigh);
            SetPrivateField(ticketManager, "coinThresholdLow", coinThresholdLow);
            SetPrivateField(ticketManager, "ticketDirection", ticketDirection);
            SetPrivateField(ticketManager, "ticketSpacing", ticketSpacing);
            SetPrivateField(ticketManager, "ticketDispenseDistance", ticketDispenseDistance);
            SetPrivateField(ticketManager, "ticketEmergenceDuration", ticketEmergenceDuration);
            SetPrivateField(ticketManager, "ticketRandomVelocity", ticketRandomVelocity);
            SetPrivateField(ticketManager, "ticketCoinSoundDelay", ticketCoinSoundDelay);
            SetPrivateField(ticketManager, "ticketCoinSoundPitch", ticketCoinSoundPitch);
            SetPrivateField(ticketManager, "ticketSoundDelay", ticketSoundDelay);
            
            var controllerType = typeof(CoinSystemController);
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var ticketMachinePoint = controllerType.GetField("ticketMachinePoint", flags)?.GetValue(controller) as Transform;
            
            if (ticketMachinePoint != null)
            {
                SetPrivateField(ticketManager, "ticketMachinePoint", ticketMachinePoint);
                Debug.Log($"TicketMachinePoint set to TicketSystemManager: {ticketMachinePoint.position}");
            }
            else
            {
                Debug.LogWarning("ticketMachinePoint not found in CoinSystemController");
            }
            
            Debug.Log("TicketSystemManager settings applied");
        }
        
        /// <summary>
        /// CoinPhysicsManagerに設定を適用
        /// </summary>
        private void ApplyPhysicsManagerSettings(CoinPhysicsManager physicsManager)
        {
            physicsManager.UpdatePhysicsSettings(dispenseForce, randomForceRange);
            Debug.Log("CoinPhysicsManager settings applied");
        }
        
        /// <summary>
        /// TiledPixelDisplayに設定を適用
        /// </summary>
        private void ApplyDisplaySettings(TiledPixelDisplay pixelDisplay, CoinSystemController controller)
        {
            if (pixelDisplay == null)
            {
                Debug.LogWarning("TiledPixelDisplay is null");
                return;
            }
            
            if (displayTileSheet == null)
            {
                Debug.LogError("Display Tile Sheet is not assigned in CoinSystemManager! Please assign a 32x32 number texture.");
                return;
            }
            
            SetPrivateField(pixelDisplay, "tileSheet", displayTileSheet);
            SetPrivateField(pixelDisplay, "tilesPerRow", 4);
            SetPrivateField(pixelDisplay, "tilesPerColumn", 4);
            SetPrivateField(pixelDisplay, "tileWidth", 8);
            SetPrivateField(pixelDisplay, "tileHeight", 8);
            
            SetPrivateField(pixelDisplay, "maxDigits", displayMaxDigits);
            SetPrivateField(pixelDisplay, "displayWidth", displayWidth);
            SetPrivateField(pixelDisplay, "displayHeight", displayHeight);
            SetPrivateField(pixelDisplay, "digitSpacing", displayDigitSpacing);
            SetPrivateField(pixelDisplay, "digitScaleX", displayDigitScaleX);
            SetPrivateField(pixelDisplay, "digitScaleY", displayDigitScaleY);
            
            SetPrivateField(pixelDisplay, "alignment", displayAlignment);
            SetPrivateField(pixelDisplay, "marginX", displayMarginX);
            SetPrivateField(pixelDisplay, "marginY", displayMarginY);
            
            SetPrivateField(pixelDisplay, "iconTexture", displayIconTexture);
            SetPrivateField(pixelDisplay, "iconScaleX", displayIconScaleX);
            SetPrivateField(pixelDisplay, "iconScaleY", displayIconScaleY);
            SetPrivateField(pixelDisplay, "iconMarginX", displayIconMarginX);
            SetPrivateField(pixelDisplay, "iconMarginY", displayIconMarginY);
            SetPrivateField(pixelDisplay, "iconSpacing", displayIconSpacing);
            
            SetPrivateField(pixelDisplay, "useEmissive", displayUseEmissive);
            SetPrivateField(pixelDisplay, "emissiveIntensity", displayEmissiveIntensity);
            SetPrivateField(pixelDisplay, "displayColor", displayColor);
            SetPrivateField(pixelDisplay, "textColor", displayTextColor);
            
            SetPrivateField(pixelDisplay, "enableLCDEffect", displayEnableLCDEffect);
            SetPrivateField(pixelDisplay, "pixelGap", displayPixelGap);
            SetPrivateField(pixelDisplay, "enableEdgeGradient", displayEnableEdgeGradient);
            SetPrivateField(pixelDisplay, "edgeGradientStrength", displayEdgeGradientStrength);
            SetPrivateField(pixelDisplay, "glowIntensity", displayGlowIntensity);
            SetPrivateField(pixelDisplay, "enableScanlines", displayEnableScanlines);
            SetPrivateField(pixelDisplay, "scanlineIntensity", displayScanlineIntensity);
            SetPrivateField(pixelDisplay, "scanlineWidth", displayScanlineWidth);
            SetPrivateField(pixelDisplay, "scanlineThickness", displayScanlineThickness);
            SetPrivateField(pixelDisplay, "scanlineGradient", displayScanlineGradient);
            SetPrivateField(pixelDisplay, "colorTint", displayColorTint);
            
            SetPrivateField(pixelDisplay, "enableOutlineGlow", displayEnableOutlineGlow);
            SetPrivateField(pixelDisplay, "outlineGlowRadius", displayOutlineGlowRadius);
            SetPrivateField(pixelDisplay, "outlineGlowIntensity", displayOutlineGlowIntensity);
            SetPrivateField(pixelDisplay, "outlineGlowColor", displayOutlineGlowColor);
            
            SetPrivateField(pixelDisplay, "enableMipmaps", displayEnableMipmaps);
            SetPrivateField(pixelDisplay, "textureFilterMode", displayTextureFilterMode);
            SetPrivateField(pixelDisplay, "anisotropicLevel", displayAnisotropicLevel);
            
            SetPrivateField(controller, "enableDisplay", enableDisplaySystem);
            
            if (!pixelDisplay.IsInitialized)
            {
                pixelDisplay.Initialize();
            }
            
            Debug.Log($"TiledPixelDisplay settings applied and initialized: {displayWidth}x{displayHeight}, Scale={displayDigitScaleX}x{displayDigitScaleY}, Color={displayColor}");
        }
        
        /// <summary>
        /// リフレクションでprivateフィールドに値を設定
        /// </summary>
        private void SetPrivateField(object obj, string fieldName, object value)
        {
            var field = obj.GetType().GetField(fieldName, 
                System.Reflection.BindingFlags.NonPublic | 
                System.Reflection.BindingFlags.Instance);
            
            if (field != null)
            {
                field.SetValue(obj, value);
            }
        }
        
        private void CreateReferenceObjects(CoinSystemController controller)
        {
            // DispenserPoint
            GameObject dispenserPointObj = new GameObject("DispenserPoint");
            dispenserPointObj.transform.position = transform.position + dispenserPosition;
            dispenserPointObj.transform.rotation = Quaternion.Euler(dispenserRotation);
            CoinDispenser dispenserManager = controller.GetComponent<CoinDispenser>();
            dispenserPointObj.transform.SetParent(dispenserManager.transform);
            
            // PotTarget
            GameObject potTargetObj = new GameObject("PotTarget");
            potTargetObj.transform.position = transform.position + potTargetPosition;
            potTargetObj.transform.SetParent(dispenserManager.transform);
            
            // StackStartPoint
            GameObject stackStartObj = new GameObject("StackStartPoint");
            stackStartObj.transform.position = transform.position + stackStartPosition;
            stackStartObj.transform.SetParent(dispenserManager.transform);
            
            // TicketMachinePoint
            GameObject ticketMachinePointObj = new GameObject("TicketMachinePoint");
            ticketMachinePointObj.transform.position = transform.position + ticketMachinePosition;
            ticketMachinePointObj.transform.SetParent(dispenserManager.transform);
            
            // DisplayScreen (ディスプレイシステムが有効な場合)
            GameObject displayScreenObj = null;
            TiledPixelDisplay displayComponent = null;
            
            if (enableDisplaySystem)
            {
                if (displayMeshObject != null)
                {
                    // 指定されたメッシュオブジェクトを使用
                    displayScreenObj = displayMeshObject;
                    
                    // UV座標はメッシュ側で設定されたものをそのまま使用
                    Debug.Log($"Using specified display mesh: {displayMeshObject.name} (UV coordinates from mesh)");
                }
                else
                {
                    // 自動生成
                    displayScreenObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    displayScreenObj.name = "DisplayScreen";
                    displayScreenObj.transform.position = transform.position + displayPosition;
                    displayScreenObj.transform.rotation = Quaternion.Euler(displayRotation);
                    displayScreenObj.transform.localScale = displayScale;
                    displayScreenObj.transform.SetParent(controller.transform);
                    
                    // UV座標をテクスチャの使用領域に合わせて設定
                    MeshFilter meshFilter = displayScreenObj.GetComponent<MeshFilter>();
                    if (meshFilter != null && meshFilter.mesh != null)
                    {
                        Mesh mesh = meshFilter.mesh;
                        
                        // テクスチャは正方形、その中の一部を使用
                        int textureSize = Mathf.Max(displayWidth, displayHeight);
                        float uvWidth = (float)displayWidth / textureSize;
                        float uvHeight = (float)displayHeight / textureSize;
                        
                        Vector2[] uvs = new Vector2[4];
                        uvs[0] = new Vector2(0, 0);        // 左下
                        uvs[1] = new Vector2(uvWidth, 0);  // 右下
                        uvs[2] = new Vector2(0, uvHeight); // 左上
                        uvs[3] = new Vector2(uvWidth, uvHeight); // 右上
                        mesh.uv = uvs;
                        
                        Debug.Log($"Display UV coordinates set to (0,0)-({uvWidth:F3},{uvHeight:F3}) for {displayWidth}x{displayHeight} area in {textureSize}x{textureSize} texture");
                    }
                    
                    Debug.Log("Auto-generated DisplayScreen");
                }
                
                // TiledPixelDisplayコンポーネントをメッシュオブジェクトに追加
                displayComponent = displayScreenObj.GetComponent<TiledPixelDisplay>();
                if (displayComponent == null)
                {
                    displayComponent = displayScreenObj.AddComponent<TiledPixelDisplay>();
                    Debug.Log($"Added TiledPixelDisplay to {displayScreenObj.name}");
                }
                
                // ControllerのpixelDisplayフィールドに参照を設定
                var controllerType = typeof(CoinSystemController);
                var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                var pixelDisplayField = controllerType.GetField("pixelDisplay", flags);
                
                if (pixelDisplayField != null)
                {
                    pixelDisplayField.SetValue(controller, displayComponent);
                    Debug.Log($"[Frame {Time.frameCount}] TiledPixelDisplay reference set to CoinSystemController: {displayComponent != null}");
                    Debug.Log($"[Frame {Time.frameCount}] Controller instance ID: {controller.GetInstanceID()}");
                    
                    // 設定後すぐに確認
                    var verifyValue = pixelDisplayField.GetValue(controller);
                    Debug.Log($"[Frame {Time.frameCount}] Verification: pixelDisplay in controller is now: {(verifyValue != null ? "NOT NULL" : "NULL")}");
                }
                else
                {
                    Debug.LogError("Failed to find 'pixelDisplay' field in CoinSystemController!");
                }
            }
            
            // プライベートフィールドへの参照設定
            var controllerType2 = typeof(CoinSystemController);
            var dispenserManagerType = typeof(CoinDispenser);
            var flags2 = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            
            // 各参照オブジェクトを設定
            controllerType2.GetField("stackStartPoint", flags2)?.SetValue(controller, stackStartObj.transform);
            controllerType2.GetField("ticketMachinePoint", flags2)?.SetValue(controller, ticketMachinePointObj.transform);
            
            // DispenserManagerの設定
            if (dispenserManager != null)
            {
                dispenserManagerType.GetField("dispenserPoint", flags2)?.SetValue(dispenserManager, dispenserPointObj.transform);
                dispenserManagerType.GetField("potTarget", flags2)?.SetValue(dispenserManager, potTargetObj.transform);
            }
            
            // ディスプレイ設定を適用（コンポーネント作成直後）
            if (enableDisplaySystem && displayComponent != null)
            {
                ApplyDisplaySettings(displayComponent, controller);
            }
        }
        
        private void ApplyControllerSettings(CoinSystemController controller)
        {
            var controllerType = typeof(CoinSystemController);
            var dispenserManagerType = typeof(CoinDispenser);
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            
            Debug.Log("Applying CoinSystemController settings...");
            
            // DispenserManagerの参照を取得
            var dispenserManager = controller.GetComponent<CoinDispenser>();
            if (dispenserManager != null)
            {
                // 排出設定
                dispenserManagerType.GetField("forceDirection", flags)?.SetValue(dispenserManager, dispenserForceDirection);
                dispenserManagerType.GetField("dispenseForce", flags)?.SetValue(dispenserManager, dispenseForce);
                dispenserManagerType.GetField("randomForceRange", flags)?.SetValue(dispenserManager, randomForceRange);
                dispenserManagerType.GetField("minDispenseInterval", flags)?.SetValue(dispenserManager, minDispenseInterval);
                dispenserManagerType.GetField("maxDispenseInterval", flags)?.SetValue(dispenserManager, maxDispenseInterval);
                dispenserManagerType.GetField("stackDirection", flags)?.SetValue(dispenserManager, stackDirection);
                dispenserManagerType.GetField("stackGroupDirection", flags)?.SetValue(dispenserManager, stackGroupDirection);
                dispenserManagerType.GetField("stackSpacing", flags)?.SetValue(dispenserManager, stackSpacing);
                dispenserManagerType.GetField("stackGroupSpacing", flags)?.SetValue(dispenserManager, stackGroupSpacing);
                dispenserManagerType.GetField("coinsPerStack", flags)?.SetValue(dispenserManager, coinsPerStack);
                dispenserManagerType.GetField("sortAnimationDuration", flags)?.SetValue(dispenserManager, sortAnimationDuration);
                dispenserManagerType.GetField("stackingDelay", flags)?.SetValue(dispenserManager, stackingDelay);
                dispenserManagerType.GetField("stackGroupDelay", flags)?.SetValue(dispenserManager, stackGroupDelay);
                dispenserManagerType.GetField("fastStackingMode", flags)?.SetValue(dispenserManager, fastStackingMode);
                // 可変速度設定
                dispenserManagerType.GetField("enableVariableSpeed", flags)?.SetValue(dispenserManager, enableVariableSpeed);
                dispenserManagerType.GetField("speedScaleThreshold", flags)?.SetValue(dispenserManager, speedScaleThreshold);
                dispenserManagerType.GetField("minDispenseInterval", flags)?.SetValue(dispenserManager, minDispenseInterval);
                dispenserManagerType.GetField("maxDispenseInterval", flags)?.SetValue(dispenserManager, maxDispenseInterval);
                dispenserManagerType.GetField("minStackingDelay", flags)?.SetValue(dispenserManager, minStackingDelay);
                dispenserManagerType.GetField("maxStackingDelay", flags)?.SetValue(dispenserManager, maxStackingDelay);
                dispenserManagerType.GetField("coinMass", flags)?.SetValue(dispenserManager, coinMass);
                dispenserManagerType.GetField("coinDrag", flags)?.SetValue(dispenserManager, coinDrag);
                dispenserManagerType.GetField("coinAngularDrag", flags)?.SetValue(dispenserManager, coinAngularDrag);
                Debug.Log($"DispenserManager settings: Force={dispenseForce}, Direction={dispenserForceDirection}");
                Debug.Log($"Stack settings: Direction={stackDirection}, GroupDirection={stackGroupDirection}, CoinsPerStack={coinsPerStack}, StackingDelay={stackingDelay}s, StackGroupDelay={stackGroupDelay}s, FastMode={fastStackingMode}, VariableSpeed={enableVariableSpeed}");
                Debug.Log($"Physics settings: Mass={coinMass}, Drag={coinDrag}, AngularDrag={coinAngularDrag}");
                Debug.Log("CoinDispenser settings applied successfully");
            }
        }
        
        private void SetCoinPrefab(CoinDispenser dispenser)
        {
            var dispenserType = typeof(CoinDispenser);
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            
            // coinPrefab フィールドを設定
            var coinPrefabField = dispenserType.GetField("coinPrefab", flags);
            coinPrefabField?.SetValue(dispenser, coinPrefab);
            
            Debug.Log($"CoinPrefab set to: {coinPrefab.name}");
            
            // プールを再初期化（プレハブが設定された後）
            try
            {
                var initMethod = dispenserType.GetMethod("InitializeCoinPool", flags);
                initMethod?.Invoke(dispenser, null);
                Debug.Log("Coin pool initialized successfully after prefab assignment");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to initialize coin pool: {e.Message}");
            }
        }
        
        private void ApplyAudioSettings(CoinDispenser dispenser)
        {
            var dispenserType = typeof(CoinDispenser);
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            
            Debug.Log("Applying CoinDispenser audio settings...");
            
            // 音声設定
            dispenserType.GetField("coinSounds", flags)?.SetValue(dispenser, coinSounds);
            dispenserType.GetField("enableCoinSounds", flags)?.SetValue(dispenser, enableCoinSounds);
            dispenserType.GetField("coinSoundVolume", flags)?.SetValue(dispenser, coinSoundVolume);
            dispenserType.GetField("maxCoinAudioSources", flags)?.SetValue(dispenser, maxCoinAudioSources);
            dispenserType.GetField("coinSoundPitchMin", flags)?.SetValue(dispenser, coinSoundPitchMin);
            dispenserType.GetField("coinSoundPitchMax", flags)?.SetValue(dispenser, coinSoundPitchMax);
            
            // 払い出し固定音設定
            dispenserType.GetField("dispensingSound", flags)?.SetValue(dispenser, dispensingSound);
            dispenserType.GetField("enableDispensingSound", flags)?.SetValue(dispenser, enableDispensingSound);
            dispenserType.GetField("dispensingSoundVolume", flags)?.SetValue(dispenser, dispensingSoundVolume);
            dispenserType.GetField("dispensingSoundPitch", flags)?.SetValue(dispenser, dispensingSoundPitch);
            
            // AudioSourceを追加（ない場合）
            AudioSource audioSource = dispenser.GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = dispenser.gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.volume = coinSoundVolume;
            }
            
            Debug.Log($"Audio settings: Sounds={coinSounds?.Length ?? 0}, Enabled={enableCoinSounds}, Volume={coinSoundVolume}, MaxAudioSources={maxCoinAudioSources}, Pitch={coinSoundPitchMin}-{coinSoundPitchMax}");
            Debug.Log($"Dispensing sound settings: Enabled={enableDispensingSound}, Volume={dispensingSoundVolume}, Pitch={dispensingSoundPitch}");
        }
        
        private void ApplyStackAudioSettings(CoinDispenser dispenser)
        {
            var dispenserType = typeof(CoinDispenser);
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            
            Debug.Log("Applying CoinDispenser stack audio settings...");
            
            // 積み上げ音声設定
            dispenserType.GetField("stackSound", flags)?.SetValue(dispenser, stackSound);
            dispenserType.GetField("enableStackSound", flags)?.SetValue(dispenser, enableStackSound);
            dispenserType.GetField("stackSoundVolume", flags)?.SetValue(dispenser, stackSoundVolume);
            dispenserType.GetField("stackPitchMin", flags)?.SetValue(dispenser, stackPitchMin);
            dispenserType.GetField("stackPitchMax", flags)?.SetValue(dispenser, stackPitchMax);
            
            Debug.Log($"Stack audio settings: Enabled={enableStackSound}, Volume={stackSoundVolume}, Pitch={stackPitchMin}-{stackPitchMax}");
        }
        
        private void ApplyTicketAudioSettings(CoinDispenser dispenser)
        {
            var dispenserType = typeof(CoinDispenser);
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            
            Debug.Log("Applying CoinDispenser ticket audio settings...");
            
            // チケット音声設定
            dispenserType.GetField("ticketDispenseSound", flags)?.SetValue(dispenser, ticketDispenseSound);
            dispenserType.GetField("enableTicketSound", flags)?.SetValue(dispenser, enableTicketSound);
            dispenserType.GetField("ticketSoundVolume", flags)?.SetValue(dispenser, ticketSoundVolume);
            dispenserType.GetField("ticketSoundPitch", flags)?.SetValue(dispenser, ticketSoundPitch);
            dispenserType.GetField("ticketSoundDelay", flags)?.SetValue(dispenser, ticketSoundDelay);
            dispenserType.GetField("ticketEmergenceDuration", flags)?.SetValue(dispenser, ticketEmergenceDuration);
            dispenserType.GetField("ticketRandomVelocity", flags)?.SetValue(dispenser, ticketRandomVelocity);
            dispenserType.GetField("ticketCoinSoundDelay", flags)?.SetValue(dispenser, ticketCoinSoundDelay);
            dispenserType.GetField("ticketCoinSoundPitch", flags)?.SetValue(dispenser, ticketCoinSoundPitch);
            
            Debug.Log($"Ticket audio settings: Enabled={enableTicketSound}, Volume={ticketSoundVolume}, Pitch={ticketSoundPitch}, Delay={ticketSoundDelay}, Duration={ticketEmergenceDuration}, RandomVel={ticketRandomVelocity}, CoinSoundDelay={ticketCoinSoundDelay}, CoinSoundPitch={ticketCoinSoundPitch}");
        }
        
        private void SetupTicketSystem(CoinDispenser coinDispenser)
        {
            var dispenserType = typeof(CoinDispenser);
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            
            Debug.Log("Setting up ticket system...");
            
            // チケットシステム設定
            dispenserType.GetField("enableTicketSystem", flags)?.SetValue(coinDispenser, enableTicketSystem);
            dispenserType.GetField("ticketPrefab", flags)?.SetValue(coinDispenser, ticketPrefab);
            dispenserType.GetField("coinsPerTicket", flags)?.SetValue(coinDispenser, coinsPerTicket);
            dispenserType.GetField("coinThresholdHigh", flags)?.SetValue(coinDispenser, coinThresholdHigh);
            dispenserType.GetField("coinThresholdLow", flags)?.SetValue(coinDispenser, coinThresholdLow);
            
            // 発券機の位置を設定
            GameObject ticketMachine = new GameObject("TicketMachine");
            ticketMachine.transform.position = transform.position + ticketMachinePosition;
            ticketMachine.transform.SetParent(coinDispenser.transform);
            dispenserType.GetField("ticketMachinePoint", flags)?.SetValue(coinDispenser, ticketMachine.transform);
            
            dispenserType.GetField("ticketDirection", flags)?.SetValue(coinDispenser, ticketDirection);
            dispenserType.GetField("ticketSpacing", flags)?.SetValue(coinDispenser, ticketSpacing);
            dispenserType.GetField("ticketDispenseDistance", flags)?.SetValue(coinDispenser, ticketDispenseDistance);
            
            Debug.Log($"Ticket system setup: Enabled={enableTicketSystem}, CoinsPerTicket={coinsPerTicket}, Thresholds={coinThresholdLow}-{coinThresholdHigh}, DispenseDistance={ticketDispenseDistance}");
        }
        
        private void SetDispenserReference(CoinDispenserTest coinInterface, CoinDispenser dispenser)
        {
            var interfaceType = typeof(CoinDispenserTest);
            var dispenserField = interfaceType.GetField("coinDispenser", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            dispenserField?.SetValue(coinInterface, dispenser);
        }
        
        void Update()
        {
            return; // キー入力を無効化
            #pragma warning disable CS0162
            // F1でセットアップ実行
            if (Input.GetKeyDown(KeyCode.F1))
            {
                SetupCoinSystem();
            }
            
            // F2で現在の設定を表示（デバッグ用）
            if (Input.GetKeyDown(KeyCode.F2))
            {
                Debug.Log("=== CoinSystemManager Settings ===");
                Debug.Log($"Enable Variable Speed: {enableVariableSpeed}");
                Debug.Log($"Speed Scale Threshold: {speedScaleThreshold}");
                Debug.Log($"Dispense Interval Range: {minDispenseInterval} - {maxDispenseInterval}");
                Debug.Log($"Stacking Delay Range: {minStackingDelay} - {maxStackingDelay}");
            }
        }
        
        // ==================== エディタ用可視化機能 ====================
        
#if UNITY_EDITOR
        
        /// <summary>
        /// チケット位置のギズモ表示
        /// </summary>
        private void OnDrawGizmos()
        {
            if (!enableTicketSystem) return;
            
            DrawTicketPositionGizmos();
        }
        
        /// <summary>
        /// チケット発券機とチケット配置位置の可視化
        /// </summary>
        private void DrawTicketPositionGizmos()
        {
            Vector3 systemCenter = transform.position;
            Vector3 ticketPos = systemCenter + ticketMachinePosition;
            
            // チケット発券機の表示
            Gizmos.color = Color.blue;
            Gizmos.DrawWireCube(ticketPos, new Vector3(0.2f, 0.1f, 0.15f));
            Gizmos.DrawCube(ticketPos, new Vector3(0.15f, 0.08f, 0.12f));
            
            // チケット排出方向の矢印
            Gizmos.color = Color.cyan;
            Vector3 directionEnd = ticketPos + ticketDirection.normalized * 0.8f;
            Gizmos.DrawRay(ticketPos, ticketDirection.normalized * 0.8f);
            
            // 矢印の先端
            Vector3 arrowSide1 = directionEnd + Vector3.Cross(ticketDirection, Vector3.up).normalized * 0.1f;
            Vector3 arrowSide2 = directionEnd - Vector3.Cross(ticketDirection, Vector3.up).normalized * 0.1f;
            Gizmos.DrawLine(directionEnd, arrowSide1);
            Gizmos.DrawLine(directionEnd, arrowSide2);
            
            // チケット配置プレビュー（5枚分）
            for (int i = 0; i < 5; i++)
            {
                Vector3 ticketPreviewPos = ticketPos + (ticketDirection.normalized * ticketSpacing * i);
                
                // チケットの形状
                Gizmos.color = new Color(0f, 0.8f, 1f, 0.6f);
                Gizmos.DrawWireCube(ticketPreviewPos, new Vector3(0.12f, 0.02f, 0.08f));
                
                // 最初のチケットを強調
                if (i == 0)
                {
                    Gizmos.color = new Color(0f, 1f, 1f, 0.8f);
                    Gizmos.DrawCube(ticketPreviewPos, new Vector3(0.1f, 0.015f, 0.06f));
                }
            }
            
            // ラベル表示（発券機）
            UnityEditor.Handles.Label(ticketPos + Vector3.up * 0.15f, 
                $"チケット発券機\n間隔: {ticketSpacing:F2}\n方向: {ticketDirection}");
        }
        
        /// <summary>
        /// 実際のCoinDispenserの位置設定と一致しているかを確認するテスト用メソッド
        /// </summary>
        [ContextMenu("Debug Position Match")]
        public void DebugPositionMatch()
        {
            var dispenser = FindObjectOfType<CoinDispenser>();
            if (dispenser == null)
            {
                Debug.Log("CoinDispenser not found. Running SetupCoinSystem first...");
                SetupCoinSystem();
                dispenser = FindObjectOfType<CoinDispenser>();
                
                if (dispenser == null)
                {
                    Debug.LogError("Failed to create CoinDispenser. Please check coinPrefab assignment.");
                    return;
                }
            }
            
            // Transform参照を取得（リフレクション使用）
            var dispenserType = typeof(CoinDispenser);
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            
            var stackStartPoint = dispenserType.GetField("stackStartPoint", flags)?.GetValue(dispenser) as Transform;
            
            if (stackStartPoint != null)
            {
                Debug.Log($"=== Position Match Debug ===");
                Debug.Log($"Manager stackStartPosition: {transform.position + stackStartPosition}");
                Debug.Log($"Dispenser stackStartPoint.position: {stackStartPoint.position}");
                Debug.Log($"Match: {Vector3.Distance(transform.position + stackStartPosition, stackStartPoint.position) < 0.001f}");
                
                // スタック位置の計算比較
                Vector3 managerStack0 = (transform.position + stackStartPosition) + stackGroupDirection.normalized * stackGroupSpacing * 0;
                Vector3 dispenserStack0 = stackStartPoint.position + stackGroupDirection.normalized * stackGroupSpacing * 0;
                Debug.Log($"Stack 0 - Manager: {managerStack0}, Dispenser: {dispenserStack0}");
                Debug.Log($"Stack 0 Match: {Vector3.Distance(managerStack0, dispenserStack0) < 0.001f}");
            }
            else
            {
                Debug.LogWarning("stackStartPoint not found in CoinDispenser");
            }
        }
        
#endif
    }
}