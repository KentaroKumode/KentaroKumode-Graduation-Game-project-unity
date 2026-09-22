using UnityEngine;

namespace MetaProgression
{
    /// <summary>
    /// 整備パネル (v6) の予算・コスト・段効果計算の中央定義。
    /// 状態は MetaProgressState.panel*、購入 API は MetaProgressManager、
    /// 効果適用は MetaBuffApplicator 経由で読み出される (Applicator が「開幕HP+N」等に翻訳)。
    ///
    /// 予算式: n 個目のポイント = 8 × n トークン。上限 36 個 = 総額 5,328 ≒ 現行 5,162。
    /// 枠総数 108 (数値 9×10 + 宣言 6×3) / 上限 36 = 33% → 常に取捨選択。
    /// </summary>
    public static class MetaPanel
    {
        /// <summary>予算。 <b>2026-09-12: 33 → 24</b>。
        ///
        /// <para><b>24 は「均等配分」と「1 本振り切り」がぴったり一致する点。</b>
        /// 数値系 8 本に 3pt ずつ = 24pt、 焦点 1 本を 10pt + 残り 7 本を 2pt ずつ = 24pt。
        /// どちらの遊び方も同じ予算で成立し、 <b>極点を取る代償が「全軸を 1 段ずつ削る」</b>
        /// という均一な形になる。</para>
        ///
        /// <para>これは測定の都合でもある ── 33pt 時代は Balanced が強奪/商才を 0 に置いていたため、
        /// その 2 本を焦点にしたアームだけが 10pt 全額を新規に払い、 <b>出力を 4 段まるごと</b>
        /// 手放していた。 犠牲が不均一で、 トラックの弱さと火力喪失が混ざって読めなかった。</para>
        ///
        /// <para>枠総数 98 に対し 24 = <b>24%</b>。 取捨選択の度合いは 33pt 時代 (34%) より強まる。</para>
        ///
        /// <para>(2026-09-10: 36 → 33 は精密トラック撤去に伴う出力中立化)</para></summary>
        public const int MaxPoints = 24;
        public const int PointCostBase = 8;

        /// <summary>n 個目 (1-indexed) のポイントを買うためのトークンコスト。</summary>
        public static int PointCost(int nthPoint)
            => (nthPoint <= 0) ? int.MaxValue : PointCostBase * nthPoint;

        // ============================================================
        //  種火・端子・スロット総枠数
        // ============================================================

        /// <summary><b>1 段あたりのポイント単価 (2026-09-10)。</b> 宣言系 (max3) = 3pt / 数値系 (max10) = 1pt。
        ///
        /// <para><b>なぜ要るか。</b> drop-one 実測で端子トラックが突出して過小価格だった:
        /// 攻撃端子 0.208 対数オッズ/pt に対し出力 0.063 ── <b>3.3 倍</b>。 端子は
        /// 「接続ダイス 1 本あたり +N」なのでビルド規模に比例して伸びるのに、
        /// 数値系と同じ 1pt/段 で買えていた。 効率格差 16.4 倍 の主因。</para>
        ///
        /// <para>宣言系はどれも max 3 段なので、 3pt/段 にすると <b>max まで 9pt</b> ＝
        /// 数値系 (1pt × 最大10段) と近い水準で揃う。 種火 4 トラックも同じ扱いになる
        /// ── こちらは効果量が未測定のまま値上げする点に注意。</para></summary>
        public static int RankCost(MetaPanelKind kind) => kind.MaxRank() <= 3 ? 3 : 1;

        /// <summary>その配分の総コスト (段数ではなく pt)。</summary>
        public static int CostOf(MetaPanelKind kind, int rank)
            => System.Math.Max(0, rank) * RankCost(kind);

        /// <summary>全トラックの max 段数の合計 (= 枠総数 98)。</summary>
        public static int TotalSlots()
        {
            int total = 0;
            foreach (MetaPanelKind k in System.Enum.GetValues(typeof(MetaPanelKind)))
                total += k.MaxRank();
            return total;
        }

        // ============================================================
        //  段効果ゲッタ (rank 0..MaxRank に対する各種効果値)
        //  意味論: 「rank 段まで購入した状態の現在値」
        // ============================================================

        /// <summary>外殻: HP +4/段。**最終段 (r10) のみ増加量 2 倍 (+8)** → 計 +44。
        /// <b>2026-09-12: +3 → +4。</b> r9 が Balanced −4.88pt で、 目標帯 (±2pt) に届いていなかった。</summary>
        public static int ShellHp(int rank)
            => Mathf.Clamp(rank, 0, 10) * 4 + (rank >= 10 ? 4 : 0);

        /// <summary>外殻 r10 の極点: **ラストスタンドを解禁する** (2026-08-04)。
        /// 従来は全ランに既定で付いていた救済で、 代償に最大HPを半減していた。
        ///   ・既定救済だと「一度は死ねる」が前提になり、 道中のリスク判断が緩む
        ///   ・最大HP半減は、 救済を受けた側をそのまま詰ませる方向にしか働かない
        /// 外殻トラックを振り切った者だけの特典に移し、 **ボス戦でも発動する**
        /// (灯火 / フルーレ・バレエ は従来どおりボス戦では不可)。
        ///
        /// <para><b>2026-09-13: その場蘇生へ。</b> 灯火/フルーレと同じ「戦闘を畳んで
        /// マップへ戻る」経路を共有していたため、 出力接続の無いボスノードで発動すると
        /// 移動先なしで詰んでいた (300 ラン中 61 件)。 正本は
        /// <see cref="CombatSystem.CombatManager"/>.TryLastStandRevive。</para>
        ///
        /// <para><b>2026-09-12: 最大HP半減を復活。</b> 移設時に半減も同時に廃止したため、
        /// <b>専用化と無償化が重なって 10 段目 1pt が +11.87pt</b> になっていた
        /// (r9 36.20% → r10 48.07%、 Balanced は 40.30%)。 発動率も 62.6% → 53.6% で、
        /// 「一度は死ねるを前提から外す」という移設の目的自体が達成できていなかった。
        /// 代償は <see cref="GameLoop.LastStand.TryConsumeRevival"/> が持つ。</para></summary>
        public static bool ShellLastStandUnlocked(int rank) => rank >= 10;

        /// <summary>出力: 与ダメ% <b>+7/段</b> (r10 で +70%)。
        /// 2026-09-13: +6 → +7 / 2026-09-12: +5 → +6。 r9 が目標帯を外していたため段階的に増量。
        /// 極点は <see cref="BattleSpiritUnlocked"/>。</summary>
        public static int OutputPct(int rank)
            => Mathf.Clamp(rank, 0, 10) * 7;

        /// <summary><b>出力 r10 (極点): 〈戦意〉。</b> 2026-09-13 新設。
        /// <b>戦闘に勝つたびに与ダメが恒久的に伸びる</b> (ラン中のみ・
        /// <see cref="BattleSpiritPctPerWin"/>%/勝)。
        ///
        /// <para><b>なぜこれか。</b> 既存 7 極点は「定数を配る」か「戦闘内のルールを変える」の
        /// どちらかで、 <b>ラン の時間軸で育つものが 1 つも無かった</b>。 ローグライクの
        /// 「潜るほど強くなる」感覚を整備パネルが持っていない ── そこが最大の空き地。</para>
        ///
        /// <para>死因は 5層 / 6層 / 7層p4 に集中している。 そこへ着く頃には 25〜38 勝して
        /// +50〜76% 乗っているので、 <b>死ぬ場所に効く形</b>になる。</para>
        ///
        /// <para><b>旧〈オーバーロード〉の撤去 (2026-09-13)。</b> 「1戦闘1回・与ダメ2倍・反動」は
        /// 反動を HP (15%→5%) でも 希望 (−5) でも機能しなかった
        /// ── HP は「HP を払って HP を守る」で釣り合わず、 希望は回復源ゼロで
        /// <b>クリア率 7.92% (Balanced −39.66pt)</b> の大惨事。 床を入れて中立 (+0.19pt) に
        /// 戻したが r10−r9 が +0.89pt で極点として死んでいた。
        /// <b>資源にコストを置く設計を諦め、 コスト無しの累積成長へ振り替える。</b></para></summary>
        public static bool BattleSpiritUnlocked(int rank) => rank >= 10;

        /// <summary>〈戦意〉1 勝あたりの与ダメ上昇 (%)。 1 ラン 約 40 戦なので上限は +80% 相当。
        /// 他の与ダメ% と同じ pool に加算合成される (純倍率にするとインフレするため)。</summary>
        /// <summary>戦闘勝利 1 回あたりの与ダメ%。
        /// <b>2026-09-13: 2 → 1.5。</b> r10−r9 が +7.76pt と全極点で突出していた
        /// (次点の商才 +1.81pt の 4 倍)。 勝利数に線形なので、 深く進むほど加速する。
        /// <para>端数は切り捨てで清算する (<c>MetaBuffApplicator.GetBattleSpiritPct</c>) ──
        /// 与ダメ% の合成は整数 pool なので、 勝利ごとに四捨五入すると 2%/勝 に戻ってしまう。</para></summary>
        public const float BattleSpiritPctPerWin = 1.5f;

        // [撤去 2026-09-13] OverloadHopeCost / OverloadHopeFloor ── 旧〈オーバーロード〉の調整つまみ。
        //   経緯は BattleSpiritUnlocked を参照。

        // [削除 2026-09-13] GuardShield / GuardDamageReduce ── どちらも呼び出し元が無い、
        //   または 0 を返すだけの残骸だった。
        //   **残骸を残したせいで「+2/段のシールド」という存在しない仕様を読み取り、
        //   それを信じて 40,000 ラン 回して数字が 1 桁も動かない事故を起こした。**

        // ============================================================
        //  〈剛胆〉(旧〈防御〉) ── リスク軸。 2026-09-13 の完全リワーク。
        // ============================================================
        //
        //  **なぜ防御を捨てたか。** 耐久で強くする限り外殻 (HP) と効果の向きが同じで、
        //  何を作っても「死ににくくなる軸」が 2 本ある形にしかならなかった。
        //  割合軽減はブロック後の残りに掛かって切り上げに飲まれ、 分散削りは
        //  説明が数式になる。 どちらも筋が悪い。
        //
        //  **リスク軸にする。** 強敵を呼び込み、 その見返りを取る。 現状 8 軸が
        //  すべて「盤面の数値」を動かすのに対し、 これは<b>遭遇の中身</b>を変える
        //  唯一の軸になる。 そして「強敵が増える代わりに実入りが増える」の 1 行で済む。
        //
        //  抽選表を変えるだけなので **BOT の方策を書き換えなくても効く** ──
        //  今日 2 回踏んだ「機構を作ったが使われない」を構造的に避けられる。

        /// <summary>戦闘マスがエリートへ格上げされる確率 (1 段 5%、 r10 で 50%)。</summary>
        public static float ValorEliteUpgradePct(int rank)
            => Mathf.Clamp(rank, 0, 10) * 5f;

        /// <summary>エリート戦の報酬ゴールド割増 (1 段 +10%、 r10 で +100%)。</summary>
        public static float ValorEliteGoldPct(int rank)
            => Mathf.Clamp(rank, 0, 10) * 10f;

        /// <summary>エリート撃破時の追加パッシブドロップ率 (1 段 <b>+1%</b>、 r10 で +10%)。
        ///
        /// <para><b>2026-09-13: 5% → 2%。</b> 5% では r9 で 1 ラン 13.91 個ドロップし
        /// (Balanced 3.79 の 3.7 倍)、 アイテムが降り注いでいた。 エリート戦は
        /// 12.6 → 15.4 回/ラン と 1.2 倍にしかなっていないのにドロップだけ跳ねる
        /// ── <b>1 戦あたりの率が高すぎた</b>。 段効果の主役は「強敵を呼ぶ」側に置く。</para></summary>
        /// <para><b>2026-09-14: 2% → 1%。</b> 全軸 10,000 ラン で剛胆が Bal比 <b>+18.85pt</b>
        /// (次点の強奪 +8.09 の 2 倍以上) と単独で壊れており、 計装が原因を指していた ──
        /// <b>エリート戦は 1.33 倍 (11.21→14.94) にしかなっていないのに、
        /// 精鋭ドロップだけ 4.5 倍 (1.33→5.98)</b>。 実測の 1 戦あたり率は 5.98/14.94 = 40% で、
        /// 設定値 20% の倍 ── 激戦マスが連続 2 戦 (§5-2) なので<b>1 マスで 2 回抽選される</b>。
        /// 設計コメントどおり「段効果の主役は強敵を呼ぶ側」なので、 ドロップ側を半分にする。</para></summary>
        public static float ValorElitePassiveDropPct(int rank)
            => Mathf.Clamp(rank, 0, 10) * 1f;

        /// <summary><b>剛胆 r10 (極点): 精鋭への与ダメ +10%。</b>
        ///
        /// <para>段効果が「強敵を呼ぶ」なので、 極点は<b>呼んだ強敵を倒せるようにする</b>。
        /// 段を積むほど極点の適用対象が増える ── 段と極点が同じ方向を向く。</para>
        ///
        /// <para><b>2026-09-14: 20% → 10%、 対象から<u>ボスを外した</u>。</b>
        /// ボスを含めていたため最終ボス勝率が <b>90.2%</b> (他トラックは 67〜78%) まで跳ね、
        /// r10−r9 の +11.17pt の主因になっていた。 ボスは<b>呼んだ強敵ではない</b>ので
        /// トラックの筋 (リスクを取って強敵を呼び、 その見返りを取る) から外れる。</para></summary>
        public static bool ValorEliteSlayerUnlocked(int rank) => SlayerEnabled && rank >= 10;

        /// <summary>[較正専用] 極点〈精鋭スレイヤー〉を無効化する。 <b>既定 true = 有効。</b>
        /// r10−r9 の +6.36pt が極点由来か線形 3 効果由来かを切り分けるための穴。</summary>
        public static bool SlayerEnabled = true;

        /// <summary>精鋭への与ダメ割増 (極点)。 <b>ボスには乗らない。</b></summary>
        public const int ValorEliteSlayerPct = 10;

        /// <summary><b>防御 r10 (極点): 戦闘終了時に残ったシールドを次の戦闘へ持ち越す。</b>
        /// 2026-09-10 新設。 現状シールドは戦闘ごとに破棄されるので、 これは<b>規則の変更</b>。
        ///
        /// <para>既存の知見と噛み合う ── 「盾の使い残しは ShieldBash の弾＝火力」(2026-08-15)。
        /// 持ち越せると「守りに使うか、 溜めて撃つか」という<b>運用の選択</b>が生まれ、
        /// 防御トラックが「耐える」だけの軸でなくなる。</para></summary>
        public static bool GuardShieldCarryUnlocked(int rank) => rank >= 10;

        /// <summary>金庫: 開幕ゴールド +5/段 (0..50)。</summary>
        public static int VaultGold(int rank) => Mathf.Clamp(rank, 0, 10) * 5;

        /// <summary>金庫 r10: 宝箱ゴールド復活。</summary>
        public static bool VaultTreasureUnlocked(int rank) => rank >= 10;

        /// <summary><b>金庫 r10 (極点): 貸金庫。 ゴールドをランを跨いで持ち越せる。</b>
        /// 2026-09-13 新設。 規則の正本は <see cref="VaultBank"/>。
        ///
        /// <para>宝箱ゴールド (<see cref="VaultTreasureUnlocked"/>) と同居させている。
        /// 宝箱側は F1=3〜F7=12 の少額で、 貸金庫 (上限 150G) の横では誤差。
        /// 分離すると Balanced の土台まで動いて基準が変わるので、 r10 にまとめた。</para></summary>
        public static bool VaultBankUnlocked(int rank) => rank >= 10;

        /// <summary><b>強奪: ボス撃破時の追加ゴールド +1/段 (0..10)。</b>
        ///
        /// <para><b>旧名 PlunderCombatGold から改称 (2026-09-13)。</b> 「戦闘勝利ゴールド」と
        /// 名乗っていたが、 実装は <c>GameManager</c> の <c>isBossNodeForMeta</c> で
        /// <b>ボスマス限定</b>だった ── 1 ラン 約 40 戦のうち 7 戦だけ。
        /// r9 で +9G/段 が「+360G のつもりで実際 +63G」になり、
        /// 強奪 r9 が Balanced −5.78pt だった原因のひとつ。 <b>名前を実装に合わせた。</b>
        /// ボス限定のまま据え置き、 道中は <see cref="PlunderPassiveDropPct"/> が担当する。</para></summary>
        public static int PlunderBossGold(int rank) => Mathf.Clamp(rank, 0, 10);

        /// <summary><b>強奪: 通常エネミー撃破時の追加パッシブドロップ確率 +2%/段 (0..20%)。</b>
        /// 2026-09-13 新設。 <b>ボス戦は対象外</b> ── ボスは <see cref="PlunderBossGold"/> が担当。
        ///
        /// <para>金だけでは弱かった。 ITT の用量反応で<b>ランダムなパッシブ 1 本 ≒ 4.3pt</b> なので、
        /// r9 = 18% × 道中 33 戦 ≒ 6 本。 金より遥かに直接的に効く。
        /// 「道中で奪い、 ボスから奪う」で軸の性格も揃う。</para></summary>
        public static float PlunderPassiveDropPct(int rank) => Mathf.Clamp(rank, 0, 10) * 2f;

        /// <summary><b>強奪 r10 (極点): ショップ強盗を解禁する。</b> 2026-09-12 に商才から移設。
        ///
        /// <para><b>なぜ移したか。</b> 商才は「安く買う」、 強奪は「力ずくで奪う」で系統が違う。
        /// 同居していたせいで <b>商才 r10 の +4.50pt が「割引下限」と「強盗」のどちらの取り分か
        /// 分離できなかった</b>。 1 トラック 1 極点に戻して測定可能にする。</para>
        ///
        /// <para><b>旧 r10「ボス追加報酬」は廃止。</b> 単体で r10−r9 = +7.02pt と
        /// 目標帯 (±3pt) の倍以上あり、 強盗と併せると二階建てになるため。</para></summary>
        public static bool PlunderShopRobberyUnlocked(int rank) => rank >= 10;

        /// <summary>商才: 特売枠数 = <b>rank × 2/3</b> (r3=2 / r6=4 / r9=6 / r10=6)。
        ///
        /// <para><b>2026-09-12 の経緯。</b> 旧 r3/6/9 で +1 (r9 で 3 枠) は r9 が Balanced −7.36pt で
        /// 弱すぎた ── 3枠 × 平均8G × 40%引 ≒ 10G は 9pt の対価にならない。
        /// いったん毎段 +1 (r9 で 9 枠) にしたが、 <b>実測 48.67% ＝ 旧 Balanced +7.99pt</b> と
        /// 反対側へ 15pt 突き抜けた (陳列 12 枠のうち素材を除く 11 枠がほぼ全部特売になるため)。
        /// 枠あたり約 2.5pt の勾配から、 目標帯 (±2pt) に入る 6 枠へ引き戻す。</para>
        ///
        /// <para>割引率は一様 20〜60% (期待値 40%、 r10 で下限 40% → 期待値 50%)。
        /// 枠数を動かすと期待値が枠数に比例するので、 効き幅はここで調整する。</para></summary>
        public static int TradeSaleSlots(int rank) => Mathf.Clamp(rank, 0, 10);

        /// <summary><b>特売の割引率テーブル (2026-09-12)。</b> 一様乱数 20〜60% をやめ、
        /// <b>固定値からの重み付き抽選</b>にした。
        ///
        /// <para><b>なぜ固定値か。</b> 一様だと 37%引 のような半端が出て、 プレイヤーが
        /// 「これは安いのか」を毎回計算し直すことになる。 段が決まっていれば値札を見た瞬間に分かり、
        /// <b>割引の深さ自体が判断材料になる</b>。 調整側も、 レンジではなく<b>重みだけ</b>で
        /// 期待値を動かせる。</para>
        ///
        /// <para><b>r10 の極点は 99% 枠の解禁。</b> 「割引下限が 20→40%」という統計的な変化より、
        /// <b>ほぼタダの品が並ぶ</b>ほうが極点として見て分かる。 頻度は 10 枠 × 5% = 0.5 個/店、
        /// 1 ラン 5.5 店で約 2.8 個。</para></summary>
        public static int[] TradeSaleTiers(int rank)
            => rank >= 10 ? new[] { 15, 30, 50, 99 } : new[] { 15, 30, 50 };

        /// <summary><see cref="TradeSaleTiers"/> に対応する重み。
        /// 既定 <b>42:38:20</b> = 期待値 <b>28.30%</b> / r10 42:35:15:8 = 期待値 <b>32.07%</b>。
        ///
        /// <para><b>2026-09-13: 既定を 50:35:15 → 42:38:20。</b> 商才 r9 が Balanced −1.73pt で、
        /// <b>段効果そのものが弱かった</b>。 枠数 (<see cref="TradeSaleSlots"/>) を増やすと
        /// 「安い品が並ぶ数」が増えるだけで棚の質は変わらないので、 深い割引の出る率を上げる。
        /// 期待値 25.50% → 28.30% (+2.8pt)。</para>
        ///
        /// <para><b>2026-09-13: 99% 枠の重みを 5 → 8。</b> r10−r9 が +1.81pt で目標 (+3pt) に
        /// 届いていなかった。 99% 枠の頻度が 10 枠 × 5% = 0.5 個/店 では、
        /// <b>1 店に 1 個も並ばない店が 6 割</b>で極点が見えない。 8% なら 0.8 個/店。</para>
        ///
        /// <para>トラックの効き幅は <see cref="TradeSaleSlots"/> (枠数・毎段+1) が持つ。
        /// 重みを段依存にすると二重に効くので、 <b>動かすのは r10 だけ</b>。</para></summary>
        public static int[] TradeSaleWeights(int rank)
            => rank >= 10 ? new[] { 42, 35, 15, 8 } : new[] { 42, 38, 20 };

        /// <b>【2026-09-10 廃止】常に 0。</b> 兵站が配るものを素材からパッシブへ変えた。
        /// 理由: drop-one 実測で寄与が測定分解能未満 (0.013 対数オッズ/pt / 分解能 ±0.029)。
        /// 原因は<b>素材が制約になっていない</b>こと ── ラン終了時の平均残が 12〜16 もあり、
        /// 余っている資源を +2 増やしても何も変わらない。 精密 (二重減衰で価値が薄い) とは
        /// 故障の種類が違い、 <b>需要のない資源を配っていた</b>。
        public static int SupplyStartMaterial(int rank) => 0;

        /// <summary><b>兵站: 開幕パッシブの本数 = r2/5/8 で +1 (0..3)、 r10 で +1 (計4)。</b>
        /// 2026-09-10 に素材から移行 ── 旧 r10 極点「開幕パッシブ1本」を刻んで前倒ししたもので、
        /// 新しい機構は足していない。
        ///
        /// <para><b>較正の根拠。</b> ランダム付与 ITT の用量反応から
        /// <b>ランダムなパッシブ 1 本 ≒ 0.174 対数オッズ</b> (0品 40.9% → 10品 79.7%)。
        /// Balanced の r5 = 2 本 = 0.347 を 5pt で買う ＝ <b>0.069/pt</b> で、
        /// 出力 (0.063/pt) とほぼ同値。 意図的に横並びへ置いている。</para>
        ///
        /// <para><b>他トラックと種類が違う</b>のが採用理由 ── HP でも火力でも金でもなく
        /// 「開幕の手札」。 精密を貫通軸にする案が「出力の上位互換では」と却下された
        /// のに対し、 こちらは倍率軸と competing しない。</para></summary>
        public static int SupplyStartingPassives(int rank)
        {
            int n = 0;
            if (rank >= 3) n++;
            if (rank >= 6) n++;
            if (rank >= 9) n++;
            return n;   // r10 は本数を増やさない ── 極点は SupplyStartingChoiceUnlocked
        }

        /// <summary><b>兵站: 開幕パッシブのレア度重み付け (2026-09-13)。</b>
        /// 段が上がるほど抽選が LEGENDARY 側へ寄る。 0 = 素の重み / 1.0 = 最大の寄せ。
        ///
        /// <para>本数だけ増やしても <b>r9 で Balanced −2.63pt</b> と弱かった。 配られるのが
        /// <c>RarityWeightedPicker</c> の素の抽選 ＝ <b>カタログ平均の品</b>で、
        /// BOT が自分で買う「学習序列の上位」に質で負けているため。
        /// 本数ではなく<b>質</b>を段の報酬にする。</para>
        ///
        /// <para>しきい値は本数と同じ r3/6/9。 「段を踏むたびに開幕の手札が良くなる」が
        /// 1 本の軸として読めるようにする。</para></summary>
        public static float SupplyRarityBias(int rank)
        {
            if (rank >= 9) return 1.0f;
            if (rank >= 6) return 0.6f;
            if (rank >= 3) return 0.3f;
            return 0f;
        }

        /// <summary><b>兵站 r10 (極点): 開幕パッシブが「2 つ提示 → 1 つ選ぶ」になる。</b>
        /// 2026-09-10 新設。 本数は増やさず<b>選択肢に変える</b>。
        ///
        /// <para><b>強度の実測 (ITT 効果量分布 n=89 / 平均 4.65pt)</b>:
        /// ランダム 1 本 4.65pt / <b>2 択 7.38pt (1.59 倍)</b> / 3 択 9.33pt (2.01 倍)。
        /// 分布が右に大きく裾を引く (max 33.17) ため、 選択肢が増えるほど裾を掴める。
        /// 3 択は r10 の限界価値が +0.52 対数オッズ (パネル平均の 7 倍) と浮くので 2 択を採用。</para></summary>
        public static bool SupplyStartingChoiceUnlocked(int rank) => rank >= 10;

        /// <summary>開幕パッシブ <paramref name="itemIndex"/> 本目 (0 起点) の提示数。
        ///
        /// <para>r10 未満は常に 1 ＝ ランダム。 <b>r10 は 1 本目 1 択 / 2 本目 2 択 / 3 本目 3 択</b>
        /// と段階的に増える (2026-09-13)。 一律 3 択は r10−r9 +3.38pt / Balanced 比 +4.71 と
        /// 強すぎた ── 3 本すべてを最良で揃えると開幕が完成しすぎる。
        /// 後の本ほど選択肢が増えるので、 <b>最初の 1 本は運、 最後の 1 本は選択</b>になる。</para></summary>
        public static int SupplyStartingOffers(int rank, int itemIndex)
            => rank >= 10 ? Mathf.Clamp(itemIndex + 1, 1, 3) : 1;

        /// <summary>互換: 開幕パッシブが 1 本でも付くか。</summary>
        public static bool SupplyStartingPassiveUnlocked(int rank) => SupplyStartingPassives(rank) > 0;

        /// <summary>燈火: 戦闘後の希望減少軽減。 <b>2026-09-12 廃止 (常に 0)</b> ──
        /// 効果を <see cref="LanternHopeCapBonus"/> (希望上限) へ移した。
        /// r9 で −3 軽減まで積んでも Balanced −5.06pt で、 戦闘 1 回あたりの損 (4〜10) を
        /// 削っても効いていなかった。</summary>
        public static int LanternHopeLossReduce(int rank) => 0;

        /// <summary><b>燈火: 希望上限 +15/段。</b> r9 で 235、 r10 で 250。
        ///
        /// <para><b>効いていない。</b> 上限を上げても<b>希望には収入が無い</b>ので、
        /// タンクが空のまま大きくなるだけだった ── 実測で r9 の 6 層突入時の希望が
        /// <b>170 (未使用)</b>、 全軸 10,000 ラン で r9 が Bal比 <b>−11.12pt と全アーム最下位</b>。
        /// 減少軽減 (2026-09-12 廃止・−5.06pt) に続き、 <b>上限も効かない</b>ことが確定した。
        /// <b>未解決</b> ── 使い道へ移す案 (2026-09-14 案C) は BOT の方策が追随せず撤回した。</para></summary>
        public static int LanternHopeCapBonus(int rank) => Mathf.Clamp(rank, 0, 10) * 15;

        /// <summary><b>燈火: 希望で支払える下限 (段効果)。</b> 段が上がるほど深く払える。
        ///
        /// <para><b>2026-09-14 新設 (案C)。</b> 旧構成は「希望払い」を r10 の極点だけの特権にしていたが、
        /// 極点は実測で <b>95,283 回発動</b>しており<b>機構は確実に動く</b>。
        /// これを段効果へ降ろせば、 上限 +N がそのまま購買力になり
        /// 「タンクが空」問題が構造的に消える ── 上限と使い道が同じ方向を向く。</para>
        ///
        /// <para>返すのは<b>「ここまで希望を減らしてよい」という床</b>。 rank 0 は解禁前なので
        /// <see cref="int.MaxValue"/> 相当 (＝1 も払えない) を意味する大きな値を返す。
        /// r1 で 悲観帯 (45)、 段ごとに 3 ずつ下がり、 r9 で 21、
        /// r10 で <see cref="GameLoop.HopeSystem.FloorDespair"/> (20) ＝ 規則上の最深。</para></summary>
        /// <summary>燈火: 希望払いの<b>規則としての</b>下限。 ここを割る支払いはできない。
        ///
        /// <para><b>規則は緩い。</b> 絶望帯 (20) の手前まで払える ── 希望 0 は発狂＝ラン終了なので
        /// そこだけは止めるが、 <b>残り少なくても払えること自体は許す</b>。
        /// 人間が「ここで押し切る」と決めた手を規則で禁じない。</para>
        ///
        /// <para><b>「どこまで払うのが賢いか」は方策の側</b>
        /// (<see cref="GameLoop.HopePayment.PolicyFloorNow"/>) が決める。 規則と方策を同じ数字にすると、
        /// 規則を緩めた瞬間に方策まで無謀になる。 実際 案C 初版でそれを踏んだ
        /// ── 床を段で 45→20 まで下げたら BOT が使い切り、 Balanced が 24.60% → 12.29% へ崩れた。</para>
        ///
        /// <para><b>段効果は上限が担う。</b> 希望上限 +15/段 で r9 なら 235。
        /// 方策側の閾値 100 (= 素の上限) を超えた分がそのまま購買力になるので、
        /// <b>燈火で伸ばした分しか実際には使われない</b> ── 上限と使い道が直結する。</para></summary>
        public static int LanternHopeSpendFloor(int rank)
            => Mathf.Clamp(rank, 0, 10) >= 1 ? GameLoop.HopeSystem.FloorDespair : int.MaxValue;

        /// <summary><b>[方策] BOT が希望を使い始める水位。</b> 規則ではない ──
        /// 規則上は <see cref="LanternHopeSpendFloor"/> (絶望帯の手前) まで払える。
        ///
        /// <para>素の希望上限 (<see cref="GameLoop.HopeSystem.HopeMax"/> = 100) と同じ値。
        /// <b>燈火で上限を伸ばした分しか BOT は使わない</b>。
        /// 80 で測ると Balanced でも 3.39 回/ラン 発動してクリア率が 24.60% → 21.56% へ落ちた
        /// ── 平穏ギリギリまで使い切った後、 通常の戦闘損で焦燥へ落ちるため。</para></summary>
        public const int HopeSpendThreshold = GameLoop.HopeSystem.HopeMax;

        /// <summary><b>燈火 r10 (極点): ゴールド不足分を希望で 1:1 で払える。</b> 2026-09-13。
        ///
        /// <para>旧極点「横移動を無税にする」を差し替えた。 無税は r10−r9 +1.07pt で
        /// 目標 (+3pt) に届かず、 かつ<b>トラックの段効果と噛み合っていなかった</b> ──
        /// 希望上限 +15/段 を積んでも、 回復源が無い希望は余ればランの終わりに捨てられる。
        /// 支払いに使えるなら上限がそのまま購買力になる。</para>
        ///
        /// <para>規則の本体は <see cref="GameLoop.HopePayment"/>。</para></summary>
        public static bool LanternHopePaymentUnlocked(int rank) => rank >= 10;

        /// <summary>【廃止】精密の「会心ダイス」補正。 常に 0。
        ///
        /// 会心率は % に統一済み (2026-09-19)。 2026-07-28 に精密の効果を
        /// <see cref="PrecisionCritRatePct"/> の会心率(%) へ一本化した。</summary>
        public static int PrecisionCritBonus(int rank) => 0;

        /// <summary>精密: 会心率 **+5%/段** (r10 で +50%)。 数値系トラック共通の全段リニア
        /// (外殻 +3HP/段、 出力 +5%/段、 防御 +3シールド/段、 金庫 +5G/段) と同じ形。
        /// 極点は r10 の <see cref="PrecisionOverflowMultiplier"/>。
        ///
        /// 2026-07-26 は `rank × 0.55%` だったが、 逓減カーブ下では
        /// **精密 9pt ≒ 期待与ダメ +23% に対し 出力 9pt = +45%** と 2 倍の開きがあり、
        /// トラックとして選ばれる理由が無かった。 2026-07-28 に釣り合う水準へ増量 (§15-1)。
        /// なお会心ダイス (分子) 経由は廃止済み ── 分子/分母モデル自体が存在しないため。</summary>
        /// <b>【2026-09-10 廃止】常に 0。</b> 精密トラックを撤去した。
        /// drop-one 実測で寄与が測定分解能未満 (−0.006 対数オッズ/pt / 分解能 ±0.049)。
        /// 原因は二重減衰 ── 双曲線逓減で名目 +15% が実効 +9.2pt になり、 会心倍率 2.0 で
        /// 与ダメ +6.9% にしかならない (出力の 46%)。 2026-07-28 の増量は会心倍率 3.0 を
        /// 前提に較正されており、 08-03 の 3.0→2.0 差し戻しで宙に浮いていた。
        /// <b>貫通軸への転換も検討したが棄却</b> ── 実効会心率の分布が P90/P10 = 1.46 倍 と狭く
        /// (逓減が分散ごと潰している) 倍率化してもビルド条件依存が成立しない。 貫通 (armorPenPct)
        /// は敵軽減 0/15/35% に沿って条件依存するが、 「totalDmg に掛かる倍率」という点で
        /// 出力と同種で、 9 本目のトラックを持つ理由にならないと判断した。
        public static float PrecisionCritRatePct(int rank) => 0f;

        /// <summary>精密 r10 の極点: **逓減で捨てられた会心率を、会心倍率へ変換する。**
        ///
        /// 会心率は <see cref="InventorySystem.PassiveSkills.CombatContext.ResolveCritRate"/> で
        /// 双曲線逓減するため、 積むほど「加算したのに実効に乗らない分」が増える。 r10 はその
        /// **捨てられた分 5 ポイントごとに会心倍率 +0.1** を返す (切り捨て)。
        ///
        /// 設計意図: 逓減の壁に当たっているビルドほど見返りが大きい。
        /// 精密を 10 段積み切った時点で壁に届くので、 **最後の 1 点を踏むかどうかが分岐**になる
        /// (他の数値系トラックが r10 に節目を持つのと同じ構造)。
        /// **注意散漫 (Λ) の天井を適用する前の値で評価してはならない** ── デバフが
        /// 精密ビルドへのバフに反転する。 呼び出し側は天井適用後の実効率を渡すこと。</summary>
        /// <param name="critAddTotal">逓減前の会心率加算合計 (1.0 = +100%)。</param>
        /// <param name="effectiveRate">逓減・天井適用後の実効会心率。</param>
        public static float PrecisionOverflowMultiplier(int rank, float critAddTotal, float effectiveRate)
        {
            return 0f;   // 【2026-09-10 廃止】精密トラック撤去に伴い極点も消滅
#pragma warning disable 162
            if (rank < 10) return 0f;
            float discarded = critAddTotal - effectiveRate;
            if (discarded <= 0f) return 0f;
            // 捨て分 5pt ごとに +0.05。 他トラックの r10 節目 (被ダメ-2 / 解禁系) と釣り合う量。
            return UnityEngine.Mathf.Floor(discarded * 100f / 5f) * 0.05f;
#pragma warning restore 162
        }

        // === 種火共通テンプレ ===

        /// <summary>種火 r1: 対応キーワードアイテムの出現重み倍率 (r1 到達で 1.6 = +60%)。</summary>
        public static float SparkAppearanceWeightMul(int rank) => rank >= 1 ? 1.60f : 1.0f;

        /// <summary>種火 r3: 対応キーワードのアイテムを 1 つ開幕所持。
        /// **抽選対象は LEGENDARY を除く全等級**（BRONZE 固定ではない・2026-07-28）。</summary>
        public static bool SparkStartingItemUnlocked(int rank) => rank >= 3;

        /// <summary>充電 r2 拡張バッテリー: 充電上限 +5 (10 → 15)。</summary>
        public static int ChargeCapBonus(int rank) => rank >= 2 ? 5 : 0;

        /// <summary>臨界 r2 保温炉: 戦闘終了時にメーターの何割を持ち越すか (0..1)。</summary>
        public static float RinkaiCarryoverRatio(int rank) => rank >= 2 ? 0.5f : 0f;

        /// <summary>毒 r2 濃縮: 毒付与時の追加スタック +1。</summary>
        public static int PoisonApplyBonus(int rank) => rank >= 2 ? 1 : 0;

        /// <summary>出血 r2 失血衰弱: 出血 N 以上の敵の攻撃値 -M (閾値=3, 減算=2)。閾値未到達なら 0。</summary>
        public static int BleedWeakenAttack(int rank, int enemyBleedStacks)
            => (rank >= 2 && enemyBleedStacks >= 3) ? 2 : 0;

        // === 端子調律 ===

        /// <summary>攻撃端子: **接続したダイス 1 本あたり** +N (0..3)。
        /// 2026-07-28 に「配線合計 +N」から変更 ── 端子へ多く挿すほど伸びる形にして、
        /// 配線判断 (§6-4) と噛ませる。 r3 で 3 本接続なら +9。</summary>
        public static int AttackTerminalPerDice(int rank) => Mathf.Clamp(rank, 0, 3);

        /// <summary>防御端子: **接続したダイス 1 本あたり** +N (0..3)。</summary>
        public static int BlockTerminalPerDice(int rank) => Mathf.Clamp(rank, 0, 3);
    }
}
