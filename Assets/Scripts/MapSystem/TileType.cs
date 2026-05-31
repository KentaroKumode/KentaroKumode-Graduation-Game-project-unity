namespace MapSystem
{
    /// <summary>マップ上のマスの種類</summary>
    public enum TileType
    {
        Outpost,        // 前哨基地 — 層の開始、休憩+秘宝+層バフ/デバフ
        Battle,         // 戦闘
        EliteBattle,    // 激戦 — 連続2戦+ボーナス
        Rest,           // 休憩 — HP回復 or 強化
        Treasure,       // 秘宝
        Shop,           // ショップ
        Event,          // イベント — レアリティ抽選
        Mystery,        // ?マス — 廃止(生成されない。enum は互換のため残置)
        Exchange,       // 交換マス — パッシブ1つを渡して上位Tierのパッシブをランダム入手
        Trap,           // 罠
        Boss,           // ボス
        SinAltar,       // 6層ボス前の儀式祭壇 — 3段階の支払いで永続デバフを回避
        LambdaRing,     // Λ層(時間の狭間)の環状線マス — 踏む度にエリート/固有イベントを抽選。再訪で再発火。
        LambdaExit,     // Λ層の中央マス(離脱) — 踏むと6F前哨基地へ帰還。スポーク(環状線の1マス)からのみ到達可。
    }

    /// <summary>イベントのレアリティ</summary>
    public enum EventRarity
    {
        Bronze,
        Silver,
        Gold,
        Legendary,
    }
}
