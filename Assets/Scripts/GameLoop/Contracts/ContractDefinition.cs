namespace GameLoop.Contracts
{
    /// <summary>
    /// 旅団契約の静的定義 (種別ごとに 1 つ)。 名前・台詞・効果説明等のテキスト系。
    /// 数値・効果ロジックは IContractEffect 実装側、 ここはメタデータのみ。
    /// </summary>
    public class ContractDefinition
    {
        public ContractKind kind;
        public string displayName;            // 表示名 (例: "傭兵団")
        public string axisLabel;              // 軸ラベル (例: "DoT" "防御" "経済")
        public string flavorIntro;            // 契約時 / 提示時に出すフレーバー
        public string[] effectByLevel;        // [L1, L2, L3] 効果説明文

        // 敵対解除時の双方セリフ (敵対側のセリフは ContractDefinition (rival) から取得)
        public string rivalryQuote;           // この旅団が敵対契約取得時に発するセリフ

        // 共通: 維持費 (L1=3 / L2=6 / L3=9) は ContractCost.For(level) で取得
    }

    /// <summary>維持費・コスト計算の中央。</summary>
    public static class ContractCost
    {
        /// <summary>L1=3G / L2=6G / L3=9G。 L3 維持後も 9G。</summary>
        public static int For(int level)
        {
            switch (level)
            {
                case 1: return 3;
                case 2: return 6;
                case 3: return 9;
                default: return 0;
            }
        }

        public const int MaxLevel = 3;
    }
}
