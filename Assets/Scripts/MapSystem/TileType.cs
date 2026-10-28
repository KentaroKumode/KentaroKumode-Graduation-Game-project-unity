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
        Gate,           // 7層終端の〈門〉 — 3つの代償を払うと完全起動、払わないぶんだけ〈不完全な〜〉を背負って8層へ降りる (2026-09-14)
        LambdaRing,     // 未使用 (旧 Λ層・2026-10-28 廃止)。 後ろの値の序数を保つため残す
        LambdaExit,     // 未使用 (同上)
        SupplyDepot,    // 補給拠点 — フレア・罠・物資を補給できる (2026-10-20 新設。 生成・効果は未実装)
    }
}
