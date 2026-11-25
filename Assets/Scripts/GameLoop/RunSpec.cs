using System;
using MapSystem;
using MapSystem.FreeMove;
using UnityEngine;

namespace GameLoop
{
    /// <summary>
    /// ラン指定 (2026-11-24・docs/GAME.md §23-35)。 データで「特別なラン」を決める口: 初期の持ち物・山札・シード・一本道の地図と敵・
    /// 隠す表示・負けた時・ボスの後の絵の列。 置き場は Resources/RunSpecs/*.json (<see cref="Load"/>)。
    ///
    /// <para><b>本体はチュートリアルの名前で分岐しない</b> (§24 の NPC ラインの教訓)。 読むのはこの指定の欄だけで、 同じ口は展示の版にも使える。
    /// 例外は「初回の PLAY でどの指定を遊ぶか」の入口 (<see cref="FirstRunTutorial"/>) だけ。</para>
    ///
    /// <para>指定のあるランの間だけ <see cref="Current"/> が立つ (<see cref="GameManager.StartNewRun(RunSpec)"/> が立て、 タイトルへ戻る時と次のランの始めに外す)。
    /// 指定が無ければ本体の動きは今までと同じ (BOT・測定は指定を使わない)。</para>
    /// </summary>
    [Serializable]
    public sealed class RunSpec
    {
        /// <summary>指定の名前 (ログ用)。</summary>
        public string id;
        /// <summary>シード (10 進の数字・<see cref="GameRng.TryParseSeed"/>)。 空 = ふだんの乱数のまま。 ランの間だけ効かせ、 終わったら前の乱数の状態へ戻す。</summary>
        public string seed;
        /// <summary>初期武器の id (items.json)。 空 = 武器なし。</summary>
        public string weapon;
        /// <summary>開始の山札 (端子の名前)。 空 = 本編の開始の山札 (CombatSystem.TerminalCards.StartingDeck)。</summary>
        public string[] deck;
        /// <summary>職業の初期の消耗品・開幕の秘宝・〈戦術の懐刀〉を配らない。</summary>
        public bool noStarterItems;
        /// <summary>整備盤の効果と挑戦カードを効かせない (MetaBuffApplicator が整備盤を読まない・難易度 0)。</summary>
        public bool noMetaBuffs;
        /// <summary>戦闘の報酬 (金・品・端子・物資)・メタのトークン・実績を出さない。</summary>
        public bool noRewards;
        /// <summary>開始の囮罠の数。</summary>
        public int lures;
        /// <summary>層に入った時の題 (null = 本編の題・空 = 出さない)。</summary>
        public string layerTitle;
        /// <summary>一本道の地図 (始点の前哨基地の後ろから、 道の順)。 <see cref="MapBattle"/>敵id / <see cref="MapBoss"/>敵id / <see cref="MapWalk"/>。 末尾はボス。 空 = 本編の地図。</summary>
        public string[] map;
        /// <summary>隠す表示・止める操作 (<see cref="HideEscalation"/> / <see cref="HideHandbook"/> / <see cref="HideFlee"/>)。</summary>
        public string[] hide;
        /// <summary><see cref="OnDeathRetry"/> = 負けたらその戦闘を HP 満タンでやり直す (ゲームオーバーにしない)。 空 = 本編どおり。</summary>
        public string onDeath;
        /// <summary>ボスを倒したら <see cref="MetaProgression.MetaProgressState.tutorialDone"/> を立てる。</summary>
        public bool setsTutorialDone;
        /// <summary>敵ごとの戦闘の始めの充電。</summary>
        public Battle[] battles;
        /// <summary>敵とターンを決めて、 そのターンの最初の出目を固定する (振り直した後は乱数のまま)。</summary>
        public DiceForce[] dice;
        /// <summary>戦闘の画面で光らせる部品 (<see cref="HintParts"/>)。</summary>
        public Hint[] hints;
        /// <summary>ボスを倒した後に流す絵の列 (UI.Game2D.Game2DEndingScreen)。 空 = すぐタイトルへ。</summary>
        public Slide[] slides;

        [Serializable] public sealed class Battle { public string enemy; public int charge; }
        [Serializable] public sealed class DiceForce { public string enemy; public int turn; public int[] faces; }
        /// <summary>enemy の戦闘で、 turn (1 始まり) のターン、 または when (<see cref="WhenCharge"/> = 敵のチャージのターン / <see cref="WhenHeavy"/> = 大技のターン) に、
        /// parts を順に光らせる (済んだ部品は飛ばす・画面の規則は Game2DBattleLcd)。 turn と when の両方が空の行は使わない。</summary>
        [Serializable] public sealed class Hint { public string enemy; public int turn; public string when; public string[] parts; }
        /// <summary>絵の列の 1 枚。 art = Resources/Game2DEndingArt の名前。 frame があれば窓の場面 (art が窓の外を横に流れる・mid = 場面の真ん中で窓の左端に映る art の x・
        /// yoff = 窓に映す縦の位置・arrive = 最後に減速して止まる)。 adapt = 白飛びから目が慣れて止まる最後の絵。 どちらでもなければ止め絵。</summary>
        [Serializable] public sealed class Slide { public string art; public string frame; public int mid; public int yoff; public bool arrive; public bool adapt; }

        public const string HideEscalation = "escalation", HideHandbook = "handbook", HideFlee = "flee";
        public const string OnDeathRetry = "retryBattle";
        public const string WhenCharge = "charge", WhenHeavy = "heavy";
        public const string MapBattle = "battle:", MapBoss = "boss:", MapWalk = "walk";
        /// <summary>光らせる部品の名前 (Game2DBattleLcd が範囲を出す)。</summary>
        public const string PartDice = "dice", PartAttack = "attack", PartBlock = "block", PartCharge = "charge",
            PartOrb = "orb", PartReroll = "reroll", PartIntent = "intent", PartGo = "go";
        /// <summary>初回の PLAY で遊ぶ指定の名前 (<see cref="FirstRunTutorial"/>)。</summary>
        public const string TutorialName = "tutorial";

        // =====================================================================
        //  今のランの指定
        // =====================================================================

        /// <summary>今のランの指定。 指定の無いラン (本編・BOT) は null。</summary>
        public static RunSpec Current { get; private set; }
        private static GameRng.State _rngBefore;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { Current = null; _rngBefore = null; }

        /// <summary>ランの始めに呼ぶ (null = 指定なし)。 前の指定は外す。 シードがあれば乱数の状態を控えてから、 そのシードでランを始める。</summary>
        public static void Begin(RunSpec spec)
        {
            End();
            Current = spec;
            if (spec != null && GameRng.TryParseSeed(spec.seed, out ulong s))
            {
                _rngBefore = GameRng.CaptureState();
                GameRng.SetMasterSeed(s);
                GameRng.BeginRun(0);
            }
        }

        /// <summary>指定のランを終える (タイトルへ戻る時)。 シードを入れていたら、 控えた乱数の状態へ戻す (次の本編のランの乱数を変えない)。</summary>
        public static void End()
        {
            if (_rngBefore != null) { GameRng.RestoreState(_rngBefore); _rngBefore = null; }
            Current = null;
        }

        public static bool Hides(string what) => Current?.hide != null && Array.IndexOf(Current.hide, what) >= 0;
        public static bool NoMeta => Current != null && Current.noMetaBuffs;
        public static bool NoRewards => Current != null && Current.noRewards;
        public static bool RetryOnDeath => Current != null && Current.onDeath == OnDeathRetry;
        public bool HasMap => map != null && map.Length > 0;

        /// <summary>今の指定で、 敵 enemyId の戦闘の始めに足す充電 (指定なし・該当なしは 0)。</summary>
        public static int OpeningCharge(string enemyId)
        {
            var b = Current?.battles;
            if (b == null || enemyId == null) return 0;
            foreach (var x in b) if (x != null && x.enemy == enemyId) return Mathf.Max(0, x.charge);
            return 0;
        }

        /// <summary>今の指定で、 敵 enemyId の戦闘のターン turn の最初の出目 (指定なし・該当なしは null)。 書き換えないこと。</summary>
        public static int[] ForcedDice(string enemyId, int turn)
        {
            var d = Current?.dice;
            if (d == null || enemyId == null) return null;
            foreach (var x in d) if (x != null && x.enemy == enemyId && x.turn == turn && x.faces != null && x.faces.Length > 0) return x.faces;
            return null;
        }

        /// <summary>今の指定で光らせる部品の並び (指定なし・該当なしは null)。 turn の一致を先に、 次に when (チャージ / 大技のターン) を見る。 上から最初に合った行。</summary>
        public static string[] HintParts(string enemyId, int turn, bool chargeTurn, bool heavyTurn)
        {
            var h = Current?.hints;
            if (h == null || enemyId == null) return null;
            foreach (var x in h)
            {
                if (x == null || x.enemy != enemyId || x.parts == null) continue;
                if (x.turn > 0 ? x.turn == turn
                    : x.when == WhenCharge ? chargeTurn
                    : x.when == WhenHeavy && heavyTurn) return x.parts;
            }
            return null;
        }

        // =====================================================================
        //  地図
        // =====================================================================

        /// <summary>地図の 1 つの点の種別。 walk = 前哨基地の種別 (着いても何も起きない・起動しても地図へ戻るだけ・使い切りにならない)。</summary>
        public static TileType TypeOf(string entry)
            => entry == null ? TileType.Outpost
             : entry.StartsWith(MapBoss, StringComparison.Ordinal) ? TileType.Boss
             : entry.StartsWith(MapBattle, StringComparison.Ordinal) ? TileType.Battle
             : TileType.Outpost;

        /// <summary>地図の 1 つの点の敵の id (戦闘・ボスでなければ null)。 書式は「種別:敵id」(id そのものは読まない)。</summary>
        public static string EnemyOf(string entry)
            => entry == null ? null
             : entry.StartsWith(MapBoss, StringComparison.Ordinal) ? entry.Substring(MapBoss.Length)
             : entry.StartsWith(MapBattle, StringComparison.Ordinal) ? entry.Substring(MapBattle.Length)
             : null;

        /// <summary>一本道の形 (前哨基地 → <see cref="map"/> の順)。 山岳・嵐・追跡者・天候は無い。</summary>
        public FreeMapLayout BuildLayout(int floor)
        {
            var types = new TileType[map.Length + 1];
            types[0] = TileType.Outpost;
            for (int k = 0; k < map.Length; k++) types[k + 1] = TypeOf(map[k]);
            return FreeMapGenerator.Line(floor, types);
        }

        /// <summary>地図の戦闘とボスの点に、 指定の敵を焼き付ける (FloorManager.EnsureEncounter は焼き付けてある点を引き直さない)。</summary>
        public void ApplyEncounters(FloorMap m)
        {
            int n = map.Length + 1;
            for (int k = 0; k < map.Length; k++)
            {
                string e = EnemyOf(map[k]);
                var node = e != null ? m.GetNodeByIndex(FreeMapGenerator.LineIndex(k + 1, n)) : null;
                if (node == null) continue;
                node.encounterId = EncounterPresets.Generic.id;
                node.encounterEnemyA = e;
                node.encounterEnemyB = null;
            }
        }

        // =====================================================================
        //  読み込み
        // =====================================================================

        /// <summary>Resources/RunSpecs/name.json を読む。 無い・読めない・形が合わないなら null (ログに出す)。</summary>
        public static RunSpec Load(string name)
        {
            var a = Resources.Load<TextAsset>("RunSpecs/" + name);
            if (a == null) { Debug.LogWarning("[RunSpec] Resources/RunSpecs/" + name + ".json が無い"); return null; }
            return Parse(a.text, name);
        }

        public static RunSpec Parse(string json, string name = null)
        {
            RunSpec s;
            try { s = JsonUtility.FromJson<RunSpec>(json); }
            catch (Exception e) { Debug.LogError("[RunSpec] " + name + " を読めない: " + e.Message); return null; }
            string err = s?.Validate() ?? "空";
            if (err != null) { Debug.LogError("[RunSpec] " + name + " の形が合わない: " + err); return null; }
            return s;
        }

        /// <summary>形の確かめ (合っていれば null・合わなければ理由)。 地図は戦闘・ボスに敵の id が要り、 末尾がボス (終点) で、 ボスは末尾だけ。</summary>
        public string Validate()
        {
            if (!HasMap) return null;
            for (int k = 0; k < map.Length; k++)
            {
                string e = map[k];
                bool last = k == map.Length - 1;
                if (e != MapWalk && string.IsNullOrEmpty(EnemyOf(e))) return "地図の " + (k + 1) + " 番目「" + e + "」が読めない";
                if ((TypeOf(e) == TileType.Boss) != last) return "ボスは地図の末尾に 1 つだけ置く";
            }
            return null;
        }

        // =====================================================================
        //  初回のチュートリアル
        // =====================================================================

        /// <summary>初回の PLAY で遊ぶ指定。 チュートリアルを終えていない人の遊び (BOT・バッチでない) なら <see cref="TutorialName"/> の指定、 それ以外は null。</summary>
        public static RunSpec FirstRunTutorial()
        {
            if (Application.isBatchMode || GameHooks.BotDriving) return null;
            var st = MetaProgression.MetaProgressManager.Instance?.State;
            if (st == null || st.tutorialDone) return null;
            return Load(TutorialName);
        }

        /// <summary>チュートリアルを終えた印を立てて保存する。</summary>
        public static void MarkTutorialDone()
        {
            var mm = MetaProgression.MetaProgressManager.Instance;
            var st = mm?.State;
            if (st == null || st.tutorialDone) return;
            st.tutorialDone = true;
            mm.Save();
            Debug.Log("[RunSpec] チュートリアルを終えた印を立てた (次の PLAY から本編)");
        }
    }
}
