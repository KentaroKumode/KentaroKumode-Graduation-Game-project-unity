using System.Collections.Generic;

namespace GameLoop
{
    /// <summary>
    /// 7層終端の〈門〉で代償を払わなかったぶん、門が不完全なまま起動したことによる欠陥。
    /// <b>2026-09-14: 旧 SinDebuff (罪の儀式) から全面リワーク。</b>
    ///
    /// <para><b>名前が機能を説明するようにしてある。</b> 旧称 (ゴルゴダの心 / 断絶した時間 /
    /// 灰燼の烙印) は「何が起きるのか」が読み取れず、 代償と罰の因果も見えなかった。
    /// 門は 3 工程 ── <b>外縁の修復 → 起動 → 転移</b> ── で動き、
    /// 手を抜いた工程がそのまま向こう側での欠陥になる。</para>
    ///
    /// <para>効果は<b>門をくぐった後の戦闘</b>にだけ乗る (実際には 8 層ヴェスカ戦のみ)。
    /// 複数同時所持可（フラグ）。</para>
    /// </summary>
    [System.Flags]
    public enum GateFlaw
    {
        None               = 0,
        /// <summary>〈不完全な修復〉外縁に血を回さなかった。 充電が毎ターン終了時に半減する。</summary>
        IncompleteRepair   = 1 << 0,
        /// <summary>〈不完全な起動〉遺物を焚かなかった。 同じ端子へ 3 本以上配線できない。</summary>
        IncompleteIgnition = 1 << 1,
        /// <summary>〈不完全な転移〉光に抗った。 ボスの攻撃がブロック出目を貫通する。</summary>
        IncompleteTransfer = 1 << 2,
    }

    /// <summary>
    /// 2026-06-28 追加: ラン開始時に選択する職業。 効果は専用スターター消耗品 1 個の配布のみ。
    /// 常時バフを持たせず build を硬直化させない。 デフォルト = 剣士。
    /// </summary>
    public enum ClassType
    {
        Swordsman = 0, // 剣士 → 瞬間研磨剤
        Knight    = 1, // 騎士 → 不抜の聖紋
        Berserker = 2, // 狂戦士 → 痛覚遮断剤
        Assassin  = 3, // 暗殺者 → 仕込み刃
    }

    /// <summary>職業 ↔ スターター消耗品 ID マッピング (正本)。</summary>
    public static class ClassStarter
    {
        public const string PolishId     = "瞬間研磨剤";     // 剣士
        public const string OathId       = "不抜の聖紋";       // 騎士
        public const string PainkillerId = "痛覚遮断剤"; // 狂戦士
        public const string DaggerId     = "仕込み刃";     // 暗殺者

        public static string GetConsumableId(ClassType cls)
        {
            switch (cls)
            {
                case ClassType.Swordsman: return PolishId;
                case ClassType.Knight:    return OathId;
                case ClassType.Berserker: return PainkillerId;
                case ClassType.Assassin:  return DaggerId;
                default:                  return PolishId;
            }
        }

        /// <summary>職業別のメイン武器 (2026-07-14: ランダム配布から職業固定へ変更)。
        /// タブアイコン (剣/槍/斧/短剣) と対応。槍武器は未実装のため騎士は盾。
        ///
        /// <para><b>2026-09-05: 配布が T1 → T2 になった。</b> T1 は全職業が必ず通過するだけの段で
        /// (`ExcludedFromLift` に入っていて評価対象ですらなかった)、 段を 1 つ減らして
        /// 中間武器の総量を抑えるため廃止した。</para></summary>
        public static string GetWeaponId(ClassType cls)
        {
            switch (cls)
            {
                case ClassType.Swordsman: return "鍛鉄の剣";
                case ClassType.Knight:    return "鉄盾";
                case ClassType.Berserker: return "猛斧";
                case ClassType.Assassin:  return "盗賊の短刀";
                default:                  return "鍛鉄の剣";
            }
        }
    }

    /// <summary>
    /// 1ランの進行状態を保持するデータクラス（MonoBehaviour非依存）。
    /// マップベース進行: ボス撃破でフロアクリア。5層=通常クリア、6層=裏ボス。
    /// </summary>
    public class RunState
    {
        // === 進行 ===
        public int currentFloor = 1;
        /// <summary>最深層。 <b>2026-09-14: 7 → 8。</b> 7 層の終端が〈門〉になり、
        /// ヴェスカは門の転移先 = 8 層 (Null Point) へ移った。</summary>
        public int maxFloor = 8;
        public int normalClearFloor = 5;
        public bool bossDefeatedThisFloor;

        // === 職業 (2026-06-28) ===
        /// <summary>ラン開始時に選択した職業。 効果はスターター消耗品 1 個配布のみ。 デフォルト = 剣士。</summary>
        public ClassType playerClass = ClassType.Swordsman;

        // === 値下げ交渉(=強盗) ===
        /// <summary>強盗を既に実行したか。 <b>1 ラン 1 回まで</b>の門番と、
        /// 以降のショップ価格割増 (<see cref="InventorySystem.Shop.ShopManager.RobberySurcharge"/>) の条件を兼ねる。
        ///
        /// <para><b>旧 shopsBlocked からの改称 (2026-09-12)。</b> 以前は「以降のショップマスを全て素通り」
        /// という出禁だったが、 <b>強盗報酬の 90〜180G と罰が同じ資源を指して打ち消し合っていた</b>
        /// ── 現金を渡してから使い道を全部閉じていた。 罰を価格割増へ置き換えたことで、
        /// 報酬と罰が同じ通貨で釣り合うようになった。</para></summary>
        public bool shopRobberyDone;
        /// <summary>現在「怪しい商人戦」が進行中（HandleCombatEnd で勝利報酬／敗北脱出 を分岐）。</summary>
        public bool shopRobberyInProgress;
        /// <summary>強盗勝利時に付与するアイテムID一覧（成功した時点のショップ品出し内容）。</summary>
        public List<string> robberyPendingItems = new List<string>();

        // === プレイヤーステータス ===
        public int playerHP;
        public int playerMaxHP;
        /// <summary>この層で踏破したマス数。 挑戦デバフ 軸14〈焦燥〉が閾値超過を数えるのに使う。
        /// 層に入るたび 0 に戻す (GameManager.EnterFloor)。</summary>
        public int tilesThisFloor;

        // === 航行の危険度見積り用 (2026-09-15) ===
        /// <summary>このランで観測したブロック出目の合計と、その回数。
        ///
        /// <para><b>ラン内に閉じること。</b> <c>CombatSystem.GuardDiag</c> は同じ数字を
        /// 持っているが<b>バッチ累計</b>なので、 これを判断に使うと ラン i が ラン 0..i-1 に
        /// 依存する ＝ 「ラン i は runIdx だけの関数」が壊れ、 チャンク分割した並列測定と
        /// 逐次測定の digest が一致しなくなる。</para></summary>
        public int blockSeenSum;
        public int blockSeenCount;

        /// <summary>このランで観測したブロックの平均。 まだ 1 回も戦っていなければ 0。</summary>
        public float AverageBlockSeen
            => blockSeenCount > 0 ? (float)blockSeenSum / blockSeenCount : 0f;

        /// <summary>このランで実際に受けたダメージの合計と、終えた戦闘数。
        /// <b>回復を差し引かない「受けた量」</b>を足す ── 必要 HP の見積りに使うので、
        /// 回復で相殺された分も「その戦闘で失う体力」として数える必要がある。</summary>
        public int fightDamageSum;
        public int fightCount;

        /// <summary>1 戦あたりの実被弾。 まだ 1 戦も終えていなければ 0。
        ///
        /// <para><b>これが危険度の正本。</b> 敵の数値から組み立てた見積りと違い、
        /// ブロック・軽減パッシブ・層の深さ・精鋭化・デバフが<b>全部すでに入っている</b>
        /// ── 式を足し込む必要がない。 2026-09-15 に旧 <c>floorHit × safetyHits</c> が
        /// 「敵の最大上振れ × 防御ゼロ」で 4〜7 層は常にクランプ天井 (0.95) に張り付き、
        /// ゲートが実質「序盤 50% / 中盤以降 95%」の二値になっていたのを置き換えるもの。</para></summary>
        public float AverageFightDamage
            => fightCount > 0 ? (float)fightDamageSum / fightCount : 0f;

        /// <summary>ボス以外の戦闘での、 ターン数 / 与えたダメージ / 受けたダメージの合計 (2026-09-20)。
        /// 航行が「このエリートと戦えば何 % 削られるか」を見積もるのに使う (AutoRunner.EliteFightLoss)。
        /// <b>ラン内に閉じる</b> ── 上の blockSeen と同じ理由 (ラン i は runIdx だけの関数)。</summary>
        public int navTurns;
        public int navDealt;
        public int navTaken;

        // === 旅団契約 (docs/GAME.md §12) ===
        // [廃止] 旅団契約 (2026-08-11 にシステムごと削除)。 activeContracts / contractsExpiredThisLayer を撤去。
        // [廃止] circusHandedOver: 旅団契約システムを 2026-08-11 に削除

        // === 戦績 ===
        public int totalBattles;
        public int totalWins;
        public int totalTurns;
        public int coins;
        /// <summary>このラン中に支出した累積ゴールド (ショップ購入/リロール等)。
        /// 黄金卿の剣の与ダメ計算で参照: 消費1Gあたり +1% 与ダメ倍率。
        /// 加算は GameManager.SpendCoins() で一元集計。</summary>
        public int coinsSpent;

        // === 状態 ===
        public bool isRunActive;

        // === 7層〈門〉の不完全起動による欠陥（門をくぐった後の戦闘でのみ参照される） ===
        public GateFlaw gateFlaws;
        public bool HasFlaw(GateFlaw f) => (gateFlaws & f) != 0;
        public void AddFlaw(GateFlaw f) => gateFlaws |= f;

        // === イベント由来の状態（軽量。完全な実装は将来のインベントリ統合で置換） ===

        /// <summary>武器強化素材（マグナイト等）の所持数。</summary>
        public int weaponMaterials;

        /// <summary>フラグアイテム名のセット（例: "苦難の予言", "迷い犬の首輪"）。</summary>
        public HashSet<string> ownedFlags = new HashSet<string>();

        /// <summary><b>パッシブアイテム名のリスト（重複可）。匿名取得は "" 名で1個分計上。</b>
        ///
        /// <para><b>【不変条件】所持数に上限は無い。</b> 何個でも持てる。 取得によって
        /// 既存の品が押し出されたり捨てられたりすることは<b>無い</b>。
        /// 減るのは以下の明示的な経路だけ:
        /// <list type="bullet">
        /// <item>ショップ売却 (<c>ShopManager.TrySell</c>)</item>
        /// <item>偽の商人に敗北 (<c>GameManager.LoseRandomPassiveItem</c>)</item>
        /// <item>イベント効果 <c>DiscardPassiveItem</c></item>
        /// <item>交換マス / 武器の Tier 差し替え</item>
        /// </list>
        /// <b>「枠」「容量」「圧迫」「押し出し」を前提にした推論をしないこと。</b>
        /// 測定結果の説明にその種の機構を持ち出す前に、 この行を読み直す。</para></summary>
        public List<string> ownedPassiveItems = new List<string>();

        /// <summary>消費アイテム名のリスト。 <b>所持数に上限は無い</b> (ownedPassiveItems と同じ)。</summary>
        public List<string> ownedConsumables = new List<string>();



        /// <summary>消耗品を 1 つ取得する。 挑戦デバフ〈穴の空いた鞄〉(§15-2 v4.0) の
        /// 同時所持上限に達していたら **取得できない** (false)。
        ///
        /// **持っている物を捨てるのではなく、 新しく持てなくする**形にした ──
        /// 買った直後に消えるのは選択の否定であり、 ショップ側で買わせない方が読める。
        /// 全ての取得経路 (ショップ / 報酬 / イベント / スターター) はここを通すこと。</summary>
        /// <summary>[計装] 消耗品を取得した回数 (バッチ累計)。</summary>
        public static long ConsumablesAcquired;
        public static void ResetConsumableStats() { ConsumablesAcquired = 0; }

        public bool TryAddConsumable(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            int cap = MetaProgression.MetaDebuffApplicator.GetConsumableCarryCap();
            if (cap > 0 && ownedConsumables.Count >= cap) return false;
            ownedConsumables.Add(id);
            ConsumablesAcquired++;   // [計装 2026-09-14] エリートの取得物切り分け
            return true;
        }

        /// <summary>ショップで購入したアイテムの「売却可能在庫」 カウント (id → 残売却可能数)。
        /// 2026-06-22 追加。 ショップ購入時 +1、 売却時 -1。 イベント/戦闘報酬などの非ショップ取得は計上しない。
        /// BOT (AutoRunner) の売却判定で参照: counts[id] > 0 = TrySell 可、 そうでなければ無償廃棄。
        /// 商人の符牒は別途 ShopManager.TrySell 内で売却を全面阻止する。</summary>
        public Dictionary<string, int> shopPurchasedCounts = new Dictionary<string, int>();

        // 2026-06-22d: Λ 保護バイアス撤廃に伴い lambdaProtectedItemIds は write-only と化したため
        // 2026-06-23c に削除。 取得カウンタのみ計測用途で残存。
        // 2026-09-11: 対になっていた lambdaItemsDiscardedDuringLambda (容量圧迫ロス) も削除 ──
        //   容量撤廃後は discard が発生しないので恒久的に 0 だった。

        /// <summary>Λ 層滞在中に追加されたアイテムの総数 (取得数)。</summary>
        public int lambdaItemsAcquiredGross;

        /// <summary>**計装 (2026-08-08)**: Λ で最後に戦利品を引いた時点の、 抽選候補の残数。
        /// 「潜るほど取得が減るのは候補が枯れるからか」を入力側で確かめるために記録する。
        /// <see cref="lambdaPoolRemainingAll"/> = 重複排除・除外フィルタ後の全候補数、
        /// <see cref="lambdaPoolRemainingFloored"/> = そこへ深度連動のレア度下限を掛けた数。
        /// 枯渇が原因なら踏破が伸びるほど 0 に近づくはず。 減らないなら原因は別 (取得機会側)。</summary>
        public int lambdaPoolRemainingAll = -1;
        public int lambdaPoolRemainingFloored = -1;

        /// <summary>**計装 (2026-08-08)**: Λ 環状線マスの解決内訳。
        /// 踏破を 2 倍にしても取得アイテムが 7.9 個で動かず、 枠・プール枯渇は共に否定された
        /// (候補は 126 種残存)。 残る仮説は「抽選の機会自体が増えていない」なので、
        /// **マスを踏んでから報酬に至るまでの各段を数える**。
        ///   Rings   : 環状線マスを踏んだ回数 (= dimensionalDisturbance と一致するはず)
        ///   Elite   : エリート戦へ振れた回数
        ///   Event   : 固有イベントへ振れた回数
        ///   EventHeal : うち HP 低下で回復へ化けた回数 (アイテム抽選に到達しない)
        ///   EventItem : うち実際にアイテム抽選へ到達した回数
        /// Rings ≫ Elite+Event なら踏破がマス起動に繋がっていない。
        /// Event ≫ EventItem なら回復への化けが原因。</summary>
        /// <summary>遺物軸〈Λ共鳴〉のカウンタ: Λ層で**終えた戦闘**の回数。
        /// 会心倍率の加算量 = この回数 × 段の値%。 ラン中ずっと持続する。
        /// Λ を出た後も効き続けるので、 6〜7 層で回収する設計。</summary>
        public int lambdaCombatsFinished;

        public int lambdaRingsEntered;
        public int lambdaRingElite;
        public int lambdaRingEvent;
        public int lambdaRingEventHeal;
        public int lambdaRingEventItem;

        /// <summary>時限バフ: ID → 残り適用回数（次戦闘で1減算する想定）。</summary>
        public Dictionary<string, int> timedBuffs = new Dictionary<string, int>();

        /// <summary>時限デバフ: ID → 残り適用回数。</summary>
        public Dictionary<string, int> timedDebuffs = new Dictionary<string, int>();

        /// <summary>永続デバフID（5層清算時に効果発動）。</summary>
        public HashSet<string> permanentDebuffs = new HashSet<string>();

        /// <summary>「一度のみ」イベントの既出 ID 集合。</summary>
        public HashSet<string> seenOnceEvents = new HashSet<string>();

        /// <summary>このランで一度でも取得したパッシブアイテムID（売却/廃棄で外したものも残す）。
        /// パッシブの重複取得を禁止する判定に使う（= 一度捨てたものも再取得しない）。</summary>
        public HashSet<string> seenPassiveItemIds = new HashSet<string>();

        /// <summary>〈昇華〉済みパッシブID（グリッド外・刻印なしの永久パッシブ）。
        /// 戦闘では owned と同様に発動するが、容量/トリアージ/グリッドには載らない（枠を消費しない）。</summary>
        public List<string> ascendedPassiveIds = new List<string>();

        /// <summary>〈昇華〉実行回数。逓増コスト（n個目 = n pt）の算出に使う。</summary>
        public int sublimationCount;

        /// <summary>パッシブ効果の「所持」判定。 グリッド所持(ownedPassiveItems)に加え、
        /// 〈昇華〉済み(ascendedPassiveIds・グリッド外だが効果は発動)も所持とみなす。
        /// セット判定/ショップ符牒/天工開物 等の効果フックはこれで判定する（容量/トリアージ/刻印は除く）。</summary>
        public bool OwnsPassive(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            if (ownedPassiveItems != null && ownedPassiveItems.Contains(id)) return true;
            if (ascendedPassiveIds != null && ascendedPassiveIds.Contains(id)) return true;
            return false;
        }

        /// <summary>ラストスタンド発動済みフラグ。ラン中1回のみ true。</summary>
        public bool lastStandActive;

        /// <summary>5層裏ボス（シュヴァリエ・サン=ジョリオラ）撃破済みフラグ。
        /// 立っている場合、シュヴァリエのレイピア解除時に会心+9 補正が付与される。</summary>
        public bool defeatedSaintGeorges;

        /// <summary>ヴェスカ (8 層 Null Point・id は boss_layer7 のまま) の戦闘に敗北したか。
        /// <b>エンディングには繋がらない</b> ── 2026-08-17 に敗北エンドの自動表示を廃止した
        /// (死ぬたびに同じ文章を読ませるのは物語ではなく作業になる)。 計測と codex 条件のためのフラグ。
        /// 正本: docs/GAME.md §20。</summary>
        public bool lostAtLayer7;

        /// <summary>最後に撃破したボスの敵ID（エンディング分岐用・<see cref="Endings"/>）。
        /// boss_layer5=審判官 / boss_layer5_hidden=剣聖 / boss_layer6=灰燼の王 / boss_layer7=ヴェスカ。</summary>
        public string lastBossId = "";

        // === Λ層（時間の狭間） ===

        /// <summary>Λ層（時間の狭間）に滞在中か。5層ボス撃破後、〈決意〉以上所持で強制突入し、
        /// 中央マス踏破で 6F 前哨基地へ着地する。滞在中は currentFloor は 5 のまま。</summary>
        public bool inLambda;

        /// <summary>Λ層で踏んだマスの累積数（=次元の乱れスタック）。
        /// 3 毎に <see cref="lambdaDebuffs"/> へランダムデバフを付与/段階上昇させる。</summary>
        public int dimensionalDisturbance;

        /// <summary>Λ層由来の恒久デバフ: ID → 段階(1〜3)。ラン中ずっと戦闘へ影響する。
        /// 同IDを再付与すると段階+1（最大3）。</summary>
        public Dictionary<string, int> lambdaDebuffs = new Dictionary<string, int>();

        // === 大穴の異常現象 (層モディファイア) ===

        /// <summary>現在層で発動中の異常現象 (層突入時に抽選、 AdvanceFloor でクリア)。
        /// 正本: docs/GAME.md §5-7</summary>
        public List<MapSystem.AbyssPhenomena.AbyssPhenomenon> activePhenomena
            = new List<MapSystem.AbyssPhenomena.AbyssPhenomenon>();

        /// <summary>逆行する滝: このフロアで「戻り」 を使用済みか。</summary>
        public bool reverseFallsUsed;

        /// <summary>蝕夜: 現戦闘で発動したか (T1 双方行動不能フラグ)。</summary>
        public bool eclipsedNightTriggered;

        /// <summary>鉄を溶かす太陽: 次に直射が発生するターン番号 (0 = 非発動／戦闘外)。</summary>
        public int ironSunNextTurn;

        public bool HasPhenomenon(MapSystem.AbyssPhenomena.AbyssPhenomenon p)
            => activePhenomena != null && activePhenomena.Contains(p);

        /// <summary>指定 Λ デバフの段階(0=未所持,1〜3)。</summary>
        public int GetLambdaDebuffLevel(string id)
            => lambdaDebuffs != null && lambdaDebuffs.TryGetValue(id, out int lv) ? lv : 0;

        /// <summary>確信の強さ。災厄の予兆で1になり、以降エリート戦勝利毎に+1。
        /// 層ゲートは layer6Unlocked / layer7Unlocked へ分離されている。</summary>
        public int convictionStage;

        /// <summary>5層ボス撃破時に〈根拠のない確信〉が〈決意〉へ変わった証。
        /// 6層への進入資格。 convictionStage とは独立した進行フラグ。</summary>
        public bool layer6Unlocked;

        /// <summary>6層の一度限りイベントで〈真理〉を得た証。
        /// 7層への進入資格。6層ボス撃破だけでは立たない。</summary>
        public bool layer7Unlocked;

        /// <summary>装備中の武器アイテムID（空=未装備=デフォルト2d6）。取得時に Loadout.TryAutoEquip で更新。</summary>
        public string equippedWeaponId = "";

        /// <summary>装備中のダイスアイテムID（空=武器ダイス使用）。取得時に Loadout.TryAutoEquip で更新。</summary>
        public string equippedDiceId = "";

        /// <summary>ラン開始時の最大HP。 挑戦デバフ〈長引く負傷〉の閾値判定の**固定基準**。
        /// 現在値を基準にすると削れるほど発動しやすくなり自己加速する (§15-2 v4.0)。</summary>
        public int baseMaxHPAtRunStart;

        /// <summary>〈長引く負傷〉でこのランに失った最大HPの累計。 上限で打ち止める。</summary>
        public int lingeringWoundLost;
        /// <summary>〈長引く負傷〉ラン中に受けた累計ダメージ。 **最大HP の減少はこの値から導く**
        /// (割合を掛けた整数部が増えるたびに 1 ずつ減らす)。 減少量そのものを積むのではなく
        /// 蓄積量を持つのは、 割合が Tier で変わっても一貫した換算にするため。</summary>
        public int lingeringWoundDamageAccum;
        /// <summary>T4-C〈凶運〉ラン開始時に封印された役のビットマスク (index = RoleKind)。
        /// **ラン中ずっと固定**。 どの役が死ぬかでそのランの組み立てが変わるので、
        /// 戦闘ごとに引き直さない (引き直すと「運が悪い戦闘」の集合になり、 判断が生まれない)。</summary>
        public int sealedRoleMask;
        /// <summary>装備ダイスに差し込んだ面パーツ（<see cref="DiceFaceParts"/>）。
        /// 廃止したダイス強化と違い**面の重複を作らない**ので、〈極〉の確率は 1/1296 のまま動かない。
        /// セーブ対象。 適用は GameManager の戦闘開始時（面配列の解決地点）。</summary>
        public List<DiceFaceParts.Part> diceFaceParts = new List<DiceFaceParts.Part>();

        /// <summary>ヴェスカ撃破後の選択 (2026-08-17)。 **true = 裂け目を閉じなかった**。
        ///
        /// <para>エンディングはこの 1 択だけで決まる。 false なら TRUE END〈裂け目を閉じる〉、
        /// true なら GOOD END〈閉じない〉。 撃破時に一度だけ set し、 以後は動かさない。</para>
        ///
        /// <para><b>既定は false (閉じる)</b> ── 額縁 (『大穴』の卓上遊戯翻案) が成立するのは
        /// こちらの分岐だけなので、 未選択のまま終わった場合も物語の筋が通る側へ倒す。</para></summary>
        public bool riftLeftOpen;

        /// <summary>行動台帳（<see cref="RunChronicle"/>）。 1 行 = 1 つの出来事を
        /// <c>"A4|f7|e=vesca|t=14|hp=8"</c> の形で積む。 **文章ではなくコード**を持つので、
        /// エンディング後のダイジェスト小説の文面を書き換えてもセーブ形式は動かない。
        /// 積むのは <see cref="RunChronicle"/> 経由のみ ── 直接 Add しないこと
        /// （分類の閾値が 1 箇所に無くなるとサマリの緊張感曲線と定義が食い違う）。</summary>
        public List<string> chronicle = new List<string>();

        /// <summary>T4-E〈最後の審判〉ラン中の累計戦闘ターン数。</summary>
        public int totalCombatTurns;
        /// <summary>刻限後の被ダメージ。死亡/生存ラン別の収支計装用。</summary>
        public int judgmentPostDamage;
        /// <summary>刻限後に要求された回復量と実際に HP へ入った量。差は上限等による廃棄。</summary>
        public int judgmentHealRequested, judgmentHealActual;
        /// <summary>刻限の回復減衰により失われた回復量。</summary>
        public int judgmentHealPrevented;
        /// <summary>T4-A〈破綻〉が発動した回数 (最大HP を半減した回数)。 計装用。</summary>
        public int breakdownCount;

        /// <summary>〈長引く負傷〉がこのランで発動した回数。 **計装専用**。
        /// 「効いていない」のか「そもそも発動していない」のかは、 到達層や勝率からは
        /// 絶対に区別できない ── 2026-08-10 に実際にそこで判断を誤った。</summary>
        public int lingeringWoundTriggers;

        /// <summary>装着中の特殊端子ID（空=未装着＝**第4端子が存在しない**）。ショップで購入する。
        /// 正本は <see cref="CombatSystem.SpecialTerminals"/>、仕様は docs/GAME.md §6-5。</summary>
        public string equippedSpecialTerminalId = "";

        /// <summary>ダイス強化 Lv (2026-07-18): id → Lv (0=素、Max はレア別)。 素材消費で最も低い2面に+1/Lv。
        /// 未登録なら Lv0 扱い。 セーブ時はそのままシリアライズ (Dictionary&lt;string,int&gt; は Unity JSON 未対応のため
        /// キー配列+値配列の並行リストとして保持する run.diceEnhanceKeys/Values で永続化 → EnsureEnhanceDictSynced()）。</summary>
        [System.NonSerialized]
        public Dictionary<string, int> diceEnhanceLevels = new Dictionary<string, int>();
        /// <summary>diceEnhanceLevels のシリアライズ用キー配列 (JsonUtility は Dictionary 未対応)。</summary>
        public List<string> diceEnhanceKeys = new List<string>();
        /// <summary>diceEnhanceLevels のシリアライズ用値配列。</summary>
        public List<int> diceEnhanceValues = new List<int>();

        /// <summary>武器の"+"段階(0 or 1)。T_n と T_n+ を表す。休憩強化で 0→1、1の状態で次Tier武器へ置換し0に戻る。</summary>
        public int weaponPlus;

        /// <summary>[廃止 2026-08-10] 業物(限界突破)の段階。 **常に 0**。
        /// 旧: T4+ 到達後に休憩で上昇し、 1lv ごとに与ダメ +20% (加算合成)。
        /// セーブ互換のためフィールドだけ残置。 加算箇所は削除済みなので参照しないこと。
        /// 廃止理由と代償は <see cref="GameManager.WeaponUpgradeCost"/> のコメント参照。</summary>
        public int limitBreakStage;

        // === 希望（カルマ＋飢餓を統合・ADR-0002 / 正本: docs/GAME.md §5・adr/0002-hope-system.md） ===

        /// <summary>希望ゲージ(0-100)。開始100。被弾HP収支マイナス・横移動・悪選択・絶望的な進軍で減少し、
        /// HP収支非マイナス勝利と食料で回復。床(75/45/20)ごとにデバフが累積し、0で発狂(秒読み)。
        /// 後ろ重心: 上は暇・終盤は絶望。ロジックは <see cref="HopeSystem"/>。</summary>
        public int hope;

        /// <summary>希望の上限。**一方向のラチェット** ── 希望が 45 以下になったら上限 45、
        /// 20 以下になったら上限 20 に固定され、回復してもその値を超えられない
        /// (<see cref="HopeSystem"/> の UpdateCapLock)。
        /// 75帯までは上限100のまま完全回復可。</summary>
        public int hopeCap;

        /// <summary>防御 r10 (極点): 前戦闘の残りシールドの <b>50%</b> (2026-09-12)。
        /// 次の戦闘の開幕シールドへ加算して 0 に戻す。
        /// 極点が未解禁なら常に 0 (持ち越しの保存自体を行わない)。</summary>
        public int carriedShield;

        /// <summary><b>出力 r10 (極点) 〈戦意〉: 戦闘に勝った回数。</b> 2026-09-13。
        /// 与ダメが <c>MetaPanel.BattleSpiritPctPerWin</c>% × これ だけ伸びる (ラン中のみ)。
        /// 極点が未解禁でも数えるが、 参照されないので無害。</summary>
        public int battleSpiritWins;

        /// <summary>戦闘開始時HPのスナップショット。戦闘終了時HPと比較してHP収支(マイナス=希望減少)を判定する。</summary>
        public int combatStartHP;

        /// <summary>発狂(hope==0)時の残り移動回数。-1=非発狂。0到達でラン終了。hope>=10 回復で-1へリセット。</summary>
        public int madnessMoveCounter = -1;

        /// <summary>佯狂者の冠: 発狂したら true。希望を0固定し、回復を拒否する（ラン中不変）。</summary>
        public bool crownHopeLocked;

        /// <summary>佯狂者の冠フルセット時の「狂気スタック」。移動毎+1。最大HP-スタック・与ダメ+スタック×4%。
        /// 最大HPが尽きると燃え尽きてラン終了。</summary>
        public int madnessStack;

        // === 消費アイテム: 次戦闘へ持ち越すバフ（マップ上で使用した場合） ===
        // 戦闘中に使用した場合は CombatContext へ直接書き込まれるため、これらは使われない。
        // 戦闘開始時に CombatContext へコピーされ、ここはクリアされる（1戦のみ）。
        public int  pendingConsAtkBurst;       // 攻撃力: 次戦闘の最初の勝利ターンに与ダメ+X
        public int  pendingConsDiceRoll;       // ダイス補正: 勝敗判定のみ+X（ダメージ非加算）
        public int  pendingConsDiceRollBattles;// ダイス補正の残戦闘数（>0 で次戦闘へ持ち越し）
        /// <summary>2026-07-21: cons_atk_* の残ターン数 (-1=戦闘中永続)。 出戦時に ctx.consDiceRollTurnsLeft へコピー。</summary>
        public int  pendingConsDiceRollTurns;
        public int  pendingConsShield;         // シールド吸収量
        public int  pendingConsShieldTurns;    // シールド持続(>0)/無制限(-1)/無(0)
        public int  pendingConsRegen;          // 継続回復: 初期値X（毎T後X回復しX-1）
        public float pendingConsCritPct;       // 会心率 (小数) の持ち越し
        public int  pendingConsCritBattles;    // 会心率の残戦闘数
        public int  pendingConsFlatReduce;     // 被ダメ毎ターン定数-X
        public int  pendingConsFlatReduceBattles; // 軽減の残戦闘数
        public int  pendingConsDmgMultPct;     // 与ダメ+X%（cons_dmg_*)
        /// <summary>2026-07-21: cons_dmg_* の残ターン数 (-1=戦闘中永続)。 出戦時に ctx.consDmgMultTurnsLeft へコピー。</summary>
        public int  pendingConsDmgMultTurns;

        // === メタ v6: 種火の戦闘跨ぎ値 ===
        /// <summary>臨界 r2 保温炉: 前戦闘終了時に memored し次戦闘開始時に ctx.rinkaiMeter へ +する量。</summary>
        public int  pendingRinkaiCarryover;
        public bool pendingConsReflect;        // 鏡写し: 被メインダメを敵に反射
        public int  pendingConsEnemyDiceDebuff;// 敵弱体: 敵ダイス合計-X
        public int  pendingEnemyStartHpCutPct; // 奇襲: 敵開始HP-X%
        public bool pendingGamblerDice;        // 賭博師: 50%全最大/50%全1
        public int  pendingFirstRollTotal;     // 加速の粉: 次戦闘の初回ロールでダイス合計+X

        // === 2026-06-28: 職業スターター消耗品 (戦闘外で使用された場合の次戦闘持ち越し) ===
        public bool pendingPolishArmed;        // 瞬間研磨剤: 次戦闘の最初の1撃で 与ダメージ+150%
        public bool pendingOathArmed;          // 不抜の聖紋: 次戦闘の HP 80% 維持中 被ダメ -40%
        public bool pendingDaggerArmed;        // 仕込み刃: 次戦闘の最初のロール敗北で無効化+反射

        // === 消費アイテム: 戦闘外ユーティリティ用フラグ/カウンタ ===
        public int  nextLootMinRarity = -1;    // 鑑定の眼鏡: 次の宝箱/ショップ最低レア(ItemRarity int)。-1=無
        public bool nextShopHalfPrice;         // 商人の鈴: 次ショップ全価格半額
        public int  philStoneUsed;             // 賢者の石: このランでの使用回数(最大5)

        /// <summary>次戦闘持ち越しバフをすべて消去（戦闘開始時にコピー後 or リセット時）。</summary>
        public void ClearPendingCombatConsumables()
        {
            // 単発系は即クリア。 マルチバトル系（dice/crit/reduce）は ConsumePendingAfterBattle で
            // 残戦闘数 > 0 の間は値を保持する。
            pendingConsAtkBurst = 0;
            pendingConsShield = 0; pendingConsShieldTurns = 0;
            pendingConsRegen = 0;
            pendingConsDmgMultPct = 0; pendingConsDmgMultTurns = 0;
            pendingConsDiceRollTurns = 0;
            pendingConsReflect = false; pendingConsEnemyDiceDebuff = 0;
            pendingEnemyStartHpCutPct = 0; pendingGamblerDice = false;
            pendingFirstRollTotal = 0;
            // 2026-06-28: 職業スターター消耗品の pending フラグ ── 戦闘開始時に ctx へコピー後クリア
            pendingPolishArmed = false; pendingOathArmed = false; pendingDaggerArmed = false;
            // dice/crit/reduce は ConsumePendingAfterBattle 側で管理（残戦闘数 0 で初めてクリア）
            ConsumeMultiBattleConsumablesAfterCopy();
        }

        /// <summary>マルチバトル持続バフ（dice/crit/reduce）の戦闘消費。 残数を 1 減らし、 0 到達時のみ値クリア。</summary>
        private void ConsumeMultiBattleConsumablesAfterCopy()
        {
            if (pendingConsDiceRollBattles > 1) pendingConsDiceRollBattles--;
            else { pendingConsDiceRoll = 0; pendingConsDiceRollBattles = 0; }
            if (pendingConsCritBattles > 1) pendingConsCritBattles--;
            else { pendingConsCritPct = 0f; pendingConsCritBattles = 0; }
            if (pendingConsFlatReduceBattles > 1) pendingConsFlatReduceBattles--;
            else { pendingConsFlatReduce = 0; pendingConsFlatReduceBattles = 0; }
        }

        // === 時限バフ・デバフ用ヘルパー ===

        public bool HasTimedBuff(string id)
            => timedBuffs != null && timedBuffs.TryGetValue(id, out int n) && n > 0;

        public bool HasTimedDebuff(string id)
            => timedDebuffs != null && timedDebuffs.TryGetValue(id, out int n) && n > 0;

        public int GetTimedBuffCharges(string id)
            => timedBuffs != null && timedBuffs.TryGetValue(id, out int n) ? n : 0;

        public int GetTimedDebuffCharges(string id)
            => timedDebuffs != null && timedDebuffs.TryGetValue(id, out int n) ? n : 0;

        /// <summary>新規ランの初期化</summary>
        /// <param name="startHP">プレイヤーの基礎最大HP。
        /// **正本は [GameManager.BaseStartingHP]。** ここの既定値は直接 Initialize を呼ぶ
        /// テスト等のためだけに残している。 2026-07-28 に「30 → 50」としたつもりの変更は
        /// ここだけを直したため**一度も効いていなかった** (2026-08-03 に発覚。
        /// それまでの全バランス調整は実効 30 の上で行われていた)。値を変えるときは必ず GameManager 側で。</param>
        public void Initialize(int startHP = GameManager.BaseStartingHP)
        {
            currentFloor = 1;
            bossDefeatedThisFloor = false;
            shopRobberyDone = false;
            shopRobberyInProgress = false;
            robberyPendingItems = new List<string>();
            // 挑戦デバフ〈脆弱な肉体〉: 最大HP ×0.90 / ×0.85 / ×0.80。 **1 を下回らせない。**
            //   v3.0 で定数減算から倍率へ変更 ── 定数だと整備パネルで最大HPを伸ばすほど薄まった。
            float fragile = MetaProgression.MetaDebuffApplicator.GetMaxHpMultiplier();
            playerMaxHP = UnityEngine.Mathf.Max(1, UnityEngine.Mathf.RoundToInt(startHP * fragile));
            playerHP = playerMaxHP;
            totalBattles = 0;
            totalWins = 0;
            totalTurns = 0;
            coins = 0;
            isRunActive = true;
            gateFlaws = GateFlaw.None;

            weaponMaterials = 0;
            ownedFlags = new HashSet<string>();
            ownedPassiveItems = new List<string>();
            ownedConsumables = new List<string>();
            shopPurchasedCounts = new Dictionary<string, int>();
            lambdaItemsAcquiredGross = 0;
            lambdaPoolRemainingAll = -1;
            lambdaPoolRemainingFloored = -1;
            lambdaCombatsFinished = 0;
            lambdaRingsEntered = 0;
            lambdaRingElite = 0;
            lambdaRingEvent = 0;
            lambdaRingEventHeal = 0;
            lambdaRingEventItem = 0;
            timedBuffs = new Dictionary<string, int>();
            timedDebuffs = new Dictionary<string, int>();
            permanentDebuffs = new HashSet<string>();
            seenOnceEvents = new HashSet<string>();
            seenPassiveItemIds = new HashSet<string>();
            ascendedPassiveIds = new List<string>();
            sublimationCount = 0;
            lastStandActive = false;
            battleSpiritWins = 0;
            defeatedSaintGeorges = false;
            lostAtLayer7 = false;
            lastBossId = "";
            inLambda = false;
            dimensionalDisturbance = 0;
            lambdaDebuffs = new Dictionary<string, int>();
            activePhenomena = new List<MapSystem.AbyssPhenomena.AbyssPhenomenon>();
            reverseFallsUsed = false;
            eclipsedNightTriggered = false;
            ironSunNextTurn = 0;
            convictionStage = 0;
            layer6Unlocked = false;
            layer7Unlocked = false;
            equippedWeaponId = "";
            equippedDiceId = "";
            equippedSpecialTerminalId = "";
            baseMaxHPAtRunStart = 0;
            lingeringWoundLost = 0;
            lingeringWoundDamageAccum = 0;
            sealedRoleMask = 0;
            if (chronicle == null) chronicle = new List<string>(); else chronicle.Clear();
            if (diceFaceParts == null) diceFaceParts = new List<DiceFaceParts.Part>(); else diceFaceParts.Clear();
            riftLeftOpen = false;
            totalCombatTurns = 0;
            judgmentPostDamage = judgmentHealRequested = judgmentHealActual = judgmentHealPrevented = 0;
            breakdownCount = 0;
            lingeringWoundTriggers = 0;
            diceEnhanceLevels = new Dictionary<string, int>();
            diceEnhanceKeys.Clear();
            diceEnhanceValues.Clear();
            weaponPlus = 0;
            limitBreakStage = 0;
            // 2026-07-25 v6: 燈火 r10 で希望上限 +N (通常 0、r10 で +10)
            int hopeCapBonus = MetaProgression.MetaBuffApplicator.GetHopeCapBonus();
            // 挑戦デバフ 軸5〈絶望的な戦闘〉T2: 希望上限 −5。
            // **整備パネル適用後に足す** (plan §6-4)。 先に引くとパネル側の計算で薄まる。
            int hopeCapPenalty = MetaProgression.MetaDebuffApplicator.GetHopeCapDelta();
            hopeCap = UnityEngine.Mathf.Max(1, HopeSystem.HopeMax + hopeCapBonus + hopeCapPenalty);
            hope    = hopeCap;

            // 遺物軸〈渇き〉: **開幕希望を下げる**（既定 60）。 上限 (hopeCap) は動かさない。
            //   この軸は「希望 ≤ hopeCap×40% の間だけ与ダメ+N%」なので、 開幕 100 のままだと
            //   序盤に一度も発動せず、 実測で 1〜3層の与ダメが遺物なしと同じ (+2.5%) だった。
            //   軸が**自分で発動条件を作る**形にして、 最初から圧倒的な火力を出す代わりに
            //   希望という資源を丸ごと前借りする ── 賭けとして自己完結させる。
            int hopeStartCap = MetaProgression.Relics.RelicApplicator.GetHopeBurnStartHope();
            if (hopeStartCap > 0) hope = UnityEngine.Mathf.Min(hope, hopeStartCap);
            combatStartHP = 0;
            madnessMoveCounter = -1;
            crownHopeLocked = false;
            madnessStack = 0;
            ClearPendingCombatConsumables();
            nextLootMinRarity = -1;
            nextShopHalfPrice = false;
            philStoneUsed = 0;
        }

        /// <summary>戦闘結果を反映</summary>
        public void ApplyBattleResult(bool playerWon, int remainingHP, int turnsUsed)
        {
            totalBattles++;
            totalTurns += turnsUsed;
            playerHP = remainingHP;

            if (playerWon)
                totalWins++;
        }

        /// <summary>フロアを進める</summary>
        public bool AdvanceFloor()
        {
            if (currentFloor >= maxFloor) return false;
            currentFloor++;
            bossDefeatedThisFloor = false;
            outpostUpgradeUsedThisFloor = false; // 層が変わるたびに前哨基地強化を再解禁
            return true;
        }

        /// <summary>この層の前哨基地で武器強化を1回使ったか。
        /// EnterFloor / AdvanceFloor でリセットされる。</summary>
        public bool outpostUpgradeUsedThisFloor;

        /// <summary>通常クリア（5層ボス撃破）</summary>
        public bool IsNormalClear => currentFloor >= normalClearFloor && bossDefeatedThisFloor && playerHP > 0;

        /// <summary>完全クリア（8層 Null Point でヴェスカ撃破）</summary>
        public bool IsFullClear => currentFloor >= maxFloor && bossDefeatedThisFloor && playerHP > 0;

        /// <summary>ランが終了したか</summary>
        public bool IsRunOver => !isRunActive || playerHP <= 0;

        /// <summary>ラン終了</summary>
        /// <summary>ランを終了する。 クリア・死亡いずれの経路もここを通る
        /// （GameManager の GameOver 発火点はすべて EndRun の直後）。
        ///
        /// **遺物 (§15-5) の獲得はここで 1 回だけ行う。** 呼び出し点が複数あるので、
        /// 各所に足すのではなく choke point へ寄せる。 二重呼び出しは isRunActive で弾く。</summary>
        public void EndRun()
        {
            if (!isRunActive) return;   // 二重終了で遺物が 2 個出るのを防ぐ
            isRunActive = false;
            MetaProgression.MetaBuffApplicator.GrantRelicOnRunEnd(currentFloor, bossDefeatedThisFloor);
        }
    }
}
