namespace GameLoop
{
    /// <summary>
    /// [剣の舞] セット（サーベル・ワルツ／ファコン・タンゴ／エスパーダ・パソドブレ／フルーレ・バレエ）の
    /// 定義と所持数ヘルパ。フレーバー上は"武器"だが実体は Passive カテゴリのシナジーセット（佯狂者と同型）。
    /// 4枚がインベントリに揃うと全て消滅し〈ブレイドダンス〉(Finale) に変化する。
    ///
    /// 効果本体は IPassiveSkillEffect (AllPassiveSkillEffects.cs / SaberWaltz 等) 側に実装。
    /// ここは「セット所持判定」と「4枚→変化」の純ロジックだけを持つ。
    /// </summary>
    /// <summary>[剣の舞] 4 枚セット。 <b>2026-09-14: 4 枚とも LEGENDARY へ統一。</b>
    ///
    /// <para>旧構成は BRONZE 7G ×2 / SILVER 8G ×1 / LEGENDARY 10G ×1 で、 さらに
    /// 〈サーベル・ワルツ〉がショップの<b>パッシブ抽選プール全体</b>を 60% で剣の舞へ絞っていた。
    /// 帯をまたぐ差し替えなので、 安い BRONZE が入口になって残り 3 枚を呼び込み、
    /// 実測で <b>4 枚集約が 27.9%</b> (3 層で既に 86/2000 成立)、
    /// 〈ブレイドダンス〉(1 発 +30.3) が atkBase のパッシブ加算の <b>24.6%</b> を占めていた。
    /// 「滅茶苦茶低出現率の切り札」という設計意図と 9 倍ずれていた。</para>
    ///
    /// <para>差し替えは <b>LEGENDARY 帯の中だけ</b>へ移設 (ShopManager.PickItemByKind)。
    /// 出現率の天井は LEGENDARY 重み 0.05 のままで、 BRONZE/SILVER/GOLD の枠は動かない。
    /// 発動条件は<b>セット共通</b>「剣の舞を 1 枚以上所持」。 重複は抽選前の
    /// <c>seenPassiveItemIds</c> 除外で既に出ない。</para>
    ///
    /// <para><b>孤剣ペナルティは 2026-09-14 に全廃した。</b> 4 枚とも LEGENDARY (10G) にした結果、
    /// 1 枚目が「高いデメリット札」になって BOT の購入率が 16.6% ──
    /// 棚に 0.61 枚/ラン 出ているのに取得は 0.10 枚/ラン で、 揃う前に見送られていた。
    /// 「揃えば強い、 単体でも損しない」でないと集めに行く動機が立たない。</para></summary>
    public static class SwordDanceSet
    {
        public const string SaberWaltz      = "剣舞譜「円舞」";       // LEGENDARY: 攻撃+1 (孤剣ペナルティは 2026-09-14 に撤去)
        public const string FalconTango     = "ファコン・タンゴ";       // LEGENDARY: 攻撃+2 (孤剣ペナルティは 2026-09-14 に撤去)
        public const string EspadaPasodoble = "エスパーダ・パソドブレ"; // LEGENDARY: 自他 攻撃+5 / 与ダメ+20% / 被ダメ+20%
        public const string FleuretBallet   = "フルーレ・バレエ";       // LEGENDARY: 攻撃+3 / 敗北時 自壊+最大HP1で生還

        public static readonly string[] All = { SaberWaltz, FalconTango, EspadaPasodoble, FleuretBallet };

        /// <summary>4枚集約の変化先（特殊アイテム）。セットメンバーではない。</summary>
        public const string Finale = "ブレイドダンス";

        /// <summary>id が [剣の舞] セットメンバーか（Finale は含まない）。</summary>
        public static bool IsDance(string id) => id != null && System.Array.IndexOf(All, id) >= 0;

        /// <summary>所持している剣の舞の種類数（昇華済みも含む＝OwnsPassive）。</summary>
        public static int OwnedCount(RunState run)
        {
            if (run == null) return 0;
            int n = 0;
            foreach (var id in All) if (run.OwnsPassive(id)) n++;
            return n;
        }

        /// <summary>self を除いた「他の剣の舞」所持数（昇華含む）。
        /// サーベル・ワルツの「他の剣の舞がインベントリか昇華に存在しないとき」判定に使う。</summary>
        public static int OtherCount(RunState run, string self)
        {
            int n = OwnedCount(run);
            if (run != null && run.OwnsPassive(self)) n--;
            return n < 0 ? 0 : n;
        }

        /// <summary>インベントリ(ownedPassiveItems のみ・昇華は除外)にある剣の舞の枚数。
        /// 仕様「[剣の舞]が4つインベントリにある」= ここで4を満たす（昇華は数えない）。</summary>
        public static int CountInInventory(RunState run)
        {
            if (run?.ownedPassiveItems == null) return 0;
            int n = 0;
            foreach (var id in All) if (run.ownedPassiveItems.Contains(id)) n++;
            return n;
        }

        /// <summary>インベントリに剣の舞が4枚揃っていれば、4枚を削除して〈ブレイドダンス〉を付与する。
        /// 変化した場合 true。seenPassiveItemIds は維持され、剣の舞の再取得・再陳列は引き続き禁止される。</summary>
        /// <summary>[計装 2026-09-14] 4 枚集約が成立したラン数と、 成立した層。
        /// <b>発火回数ではなく「何ランで揃ったか」を数える。</b>
        /// 〈ブレイドダンス〉は 1 発 +30.3 で atkBase のパッシブ加算の 24.6% を占めるが、
        /// それが「稀にしか揃わない報酬」なのか「常道」なのかは発火回数では分からない
        /// (揃ったランは以後毎ターン鳴り続けるので、 回数はラン長の代理になる)。</summary>
        public static long Transforms;
        /// <summary>[計装] 剣の舞を 1 枚取得した回数。 <b>提示 (ShopManager.DanceOffered) と分けて数える</b>
        /// ── 「出ていないのか、 出ているのに買われていないのか」は合算では切り分かない。</summary>
        public static long Acquired;
        public static readonly long[] TransformsByFloor = new long[9];
        public static void ResetStats()
        { Transforms = 0; Acquired = 0; System.Array.Clear(TransformsByFloor, 0, TransformsByFloor.Length); }

        public static bool TryTransform(RunState run)
        {
            if (run?.ownedPassiveItems == null) return false;
            if (CountInInventory(run) < All.Length) return false;
            Transforms++;
            TransformsByFloor[UnityEngine.Mathf.Clamp(run.currentFloor, 0, 8)]++;

            // 後ろから消して index ズレを防ぐ（刻印リストも PassiveAddHelper.RemoveAt が同期）
            for (int i = run.ownedPassiveItems.Count - 1; i >= 0; i--)
                if (IsDance(run.ownedPassiveItems[i]))
                    InventorySystem.Helpers.PassiveAddHelper.RemoveAt(run, i);

            InventorySystem.Helpers.PassiveAddHelper.AddPassiveItem(run, Finale);

            // 取得時シード: 4枚集約完成への報酬として現在層 × 3 スタックを初期付与 (後半取得偏重の救済)
            var bd = InventorySystem.PassiveSkills.PassiveSkillRegistry.Get("ブレイドダンス")
                     as InventorySystem.PassiveSkills.Effects.BladeDance;
            bd?.SeedOnAcquire(run.currentFloor);

            UnityEngine.Debug.Log("[剣の舞] インベントリに4枚集約 → 全消滅し〈ブレイドダンス〉に変化");
            return true;
        }
    }
}
