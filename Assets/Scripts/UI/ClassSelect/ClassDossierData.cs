using GameLoop;

namespace UI.ClassSelect
{
    /// <summary>
    /// 職業選択 UI の表示データ。 正本は items.json (スターター消耗品の説明/フレーバー) と
    /// docs/GAME.md §11。 ItemDatabase が未初期化のシーン (タイトル等) でも
    /// 動くよう、 ここにフォールバック文字列を持つ。
    /// </summary>
    public static class ClassDossierData
    {
        public struct Entry
        {
            public ClassType type;
            public string className;      // 職能名
            public string classNameKana;  // 登録票の振り仮名風サブ表記
            public string origin;         // 出自 (空白の数年・肖像下に表示。 正本: class-system.md 職業の物語的位置づけ)
            public string starterId;      // 配給品 ID
            public string starterNameFallback;
            public string starterDescFallback;
            public string flavorFallback;
            public string weaponNameFallback; // メイン武器名 (ItemDatabase 未初期化時のフォールバック)
            public string traits;             // この kit の性質 (機能面の短い説明)
        }

        public static readonly Entry[] Entries =
        {
            new Entry
            {
                type = ClassType.Swordsman,
                className = "剣士",
                classNameKana = "SWORDSMAN",
                origin = "あなたは祖父母の急逝後、人を頭数としか数えない傭兵団に身を投げた。" +
                         "古株たちは、あなたが五度の出撃と保たずに死ぬ方へ賭けた。" +
                         "あなたはその賭け金を総取りした後、傭兵団を去った。",
                starterId = ClassStarter.PolishId,
                starterNameFallback = "瞬間研磨剤",
                starterDescFallback = "次の1撃だけ、与ダメージ+150%。",
                flavorFallback = "認めるかどうかと、斬れるかどうかは、別の話だ。",
                weaponNameFallback = "訓練用の剣",
                traits = "攻守に偏りのない標準構成。配給品はどの装備方針にも乗る汎用強化で、山場を選んで切る判断だけが問われる。",
            },
            new Entry
            {
                type = ClassType.Knight,
                className = "騎士",
                classNameKana = "KNIGHT",
                origin = "あなたは祖父母の急逝後、辺境警備に志願した。" +
                         "型だけを教えたのは騎士団を追われた男たちで、彼らは新入りの名を葬式まで覚えない。" +
                         "彼らがあなたの名を覚えるより先に、あなたは辺境を去った。",
                starterId = ClassStarter.OathId,
                starterNameFallback = "不抜の聖紋",
                starterDescFallback = "戦闘中、HP80%以上を保っている間、被ダメージ-40%。一度でも80%を割った時点で以後無効。",
                flavorFallback = "誓いとは、そういうものらしい。",
                weaponNameFallback = "端材の盾",
                traits = "防御寄りの堅実構成。HP を高く保つほど配給品が活き、一度崩れると牙を失う。被弾管理が生命線。",
            },
            new Entry
            {
                type = ClassType.Berserker,
                className = "狂戦士",
                classNameKana = "BERSERKER",
                origin = "あなたは祖父母の急逝後、北へ向かう隊商に紛れた。そのまま消えるつもりだった。" +
                         "氷原の民の薬は痛みを消し、冬は他の何もかもを消した。" +
                         "冬があなたを消すより先に、あなたは南へ戻る隊商に紛れ、氷原を去った。",
                starterId = ClassStarter.PainkillerId,
                starterNameFallback = "痛覚遮断剤",
                starterDescFallback = "使用ターン、致命ダメージを受けてもHP1で踏みとどまる。次ターン開始時HP=1ならダイス合計+10/基礎攻撃+10、ただし攻撃終了時に死亡する。",
                flavorFallback = "「効いている間は死なない。切れた時に、まとめて来る」",
                weaponNameFallback = "薪割りの斧",
                traits = "攻撃特化の博打構成。配給品は致命傷を一手の猶予に変える決死の薬 ── 猶予の一手で削り切れなければ死ぬ。",
            },
            new Entry
            {
                type = ClassType.Assassin,
                className = "暗殺者",
                classNameKana = "ASSASSIN",
                origin = "あなたは祖父母の急逝後、名を聞かれない仕事を選ぶようになった。" +
                         "教団はそういう人間を刃と呼び、研いで、折れるまで使う。" +
                         "あなたは折れなかった。処分される前に、自分の足で教団を去った。",
                starterId = ClassStarter.DaggerId,
                starterNameFallback = "仕込み刃",
                starterDescFallback = "次のダイスロールに敗北したとき、受けるダメージを無効化し、同値を相手に軽減不能ダメージとして与える。",
                flavorFallback = "負けた側が生き残る型が、ひとつだけあるという。",
                weaponNameFallback = "懐刀",
                traits = "一点賭けの奇襲構成。配給品は敗北を反撃に変える仕込みで、短期決戦ほど強く、長期戦では価値が薄れる。",
            },
        };

        /// <summary>ItemDatabase から現物の説明/フレーバーを取得 (無ければフォールバック)。</summary>
        public static void Resolve(in Entry e, out string itemName, out string itemDesc, out string flavor)
        {
            itemName = e.starterNameFallback;
            itemDesc = e.starterDescFallback;
            flavor   = e.flavorFallback;

            var db = InventorySystem.ItemDatabase.Instance;
            var data = db != null ? db.GetItem(e.starterId) : null;
            if (data == null) return;
            if (!string.IsNullOrEmpty(data.displayName)) itemName = data.displayName;
            if (!string.IsNullOrEmpty(data.description)) itemDesc = data.description;
            if (!string.IsNullOrEmpty(data.flavorText))  flavor = data.flavorText;
        }

        /// <summary>メイン武器の表示名 (正本: ClassStarter.GetWeaponId + ItemDatabase)。</summary>
        public static string ResolveWeaponName(in Entry e)
        {
            var db = InventorySystem.ItemDatabase.Instance;
            var data = db != null ? db.GetItem(GameLoop.ClassStarter.GetWeaponId(e.type)) : null;
            return (data != null && !string.IsNullOrEmpty(data.displayName))
                   ? data.displayName : e.weaponNameFallback;
        }
    }
}
