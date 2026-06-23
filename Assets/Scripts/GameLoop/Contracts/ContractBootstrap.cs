using UnityEngine;

namespace GameLoop.Contracts
{
    /// <summary>
    /// 契約システムの起動時初期化。
    /// RuntimeInitializeOnLoadMethod (BeforeSceneLoad) で全 12 旅団の効果を ContractManager に登録する。
    /// </summary>
    public static class ContractBootstrap
    {
        private static bool _initialized;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Initialize()
        {
            if (_initialized) return;
            ContractManager.Instance.RegisterAllEffects();
            _initialized = true;
            Debug.Log("[ContractBootstrap] 12 旅団の効果を登録完了");
        }
    }
}
