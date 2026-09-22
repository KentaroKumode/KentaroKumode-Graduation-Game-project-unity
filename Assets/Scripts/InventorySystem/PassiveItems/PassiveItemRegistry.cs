using System.Collections.Generic;
using EventSystem.TimedEffects;

namespace InventorySystem.PassiveItems
{
    /// <summary>
    /// 名前付き固有パッシブアイテムの効果レジストリ。
    /// ITimedEffect を流用するが、こちらは「永続」（チャージ消費なし）。
    /// PassiveItemManager がプレイヤー所持リストから対象を引き、トリガごとに Apply する。
    /// </summary>
    public static class PassiveItemRegistry
    {
        private static readonly Dictionary<string, ITimedEffect> registry
            = new Dictionary<string, ITimedEffect>();

        private static bool initialized;

        public static void EnsureInitialized()
        {
            if (initialized) return;
            initialized = true;
            RegisterAll();
        }

        public static ITimedEffect Get(string id)
        {
            EnsureInitialized();
            return registry.TryGetValue(id, out var v) ? v : null;
        }

        public static IEnumerable<KeyValuePair<string, ITimedEffect>> All
        {
            get { EnsureInitialized(); return registry; }
        }

        private static void Register(ITimedEffect effect) => registry[effect.Id] = effect;

        private static void RegisterAll()
        {
            // 名前付き固有パッシブの効果実装
            // (2026-07-18 死コード掃除: 記憶の砂時計/死神の数珠/嵐の徽章/沈黙の剣帯/狂乱のメダリオン/
            //  静寂のローブ/黒煙の符/蒼穹の眼/守護天使の鈴 の 9 effect はアイテム削除に伴い class ごと削除)
            Register(new Effects.PilgrimStaffEffect());
            Register(new Effects.HopeEmberEffect());

            // HP閾値発動系
            // 末那識 はパッシブスキル側のステータスへ移した (2026-09-19・与ダメ%を他と同じタイミングで加算)

            // 歩行HP回復
            Register(new Effects.CalmShoesEffect());
            Register(new Effects.HealingShoesEffect());
            Register(new Effects.HolyShoesEffect());

            // その他高レア
            Register(new Effects.GoldenScaleEffect());
            Register(new Effects.IronHeartEffect());
            Register(new Effects.CalamityRingEffect());
            Register(new Effects.EternalLanternEffect());

            // 佯狂者シリーズ（発狂連動）。鈴は店フックのため非登録（商人の符牒と同型）。
            Register(new Effects.YokyoStaffEffect());
            Register(new Effects.YokyoGarbEffect());
            Register(new Effects.YokyoCrownEffect());

            // 2026-06-03 新規追加アイテム
            Register(new Effects.PilgrimCharmEffect()); // 巡礼の杖飾り（移動時25%で希望+1）
            Register(new Effects.RoadMoneyBandEffect()); // 道銭の帯封（層突入でゴールド+6）
            // 狂宴の仮面 はパッシブスキル側のステータスへ移した (2026-09-19・与ダメ%を他と同じタイミングで加算)
            // 商人の符牒・食通の懐刀 は他システム連携でフックされる（PassiveItemRegistry には登録しない）

            // イベント入手の名前付きパッシブ (2026-09-18 に items.json へ登録し効果を付与)。
            //   ちいさな灯火は TorchRevival (救済チェーン)、 希望の灯片は上で登録済み、
            //   根拠のない確信/決意/真理は ConvictionSystem が扱う ── ここには来ない。
            Register(new Effects.LuckyCoinEffect());     // 幸運の硬貨（勝利時+3G）
            // 英雄の意志 はパッシブスキル側のステータスへ移した (2026-09-19・与ダメ%を他と同じタイミングで加算)
            // 激情の刃 はパッシブスキル側のステータスへ移した (2026-09-19・与ダメ%を他と同じタイミングで加算)
            Register(new Effects.CompanionSoulEffect()); // 相棒の魂（開幕シールド+3）
        }
    }
}
