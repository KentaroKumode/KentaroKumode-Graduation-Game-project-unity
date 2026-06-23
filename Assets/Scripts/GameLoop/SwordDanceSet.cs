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
    public static class SwordDanceSet
    {
        public const string SaberWaltz      = "サーベル・ワルツ";       // BRONZE: ダイス合計+1 / 単独時 戦闘開始HP半減 / 店出現率UP
        public const string FalconTango     = "ファコン・タンゴ";       // LEGENDARY: 孤剣 (他の[剣の舞]無) 戦闘開始時 最大HP-1
        public const string EspadaPasodoble = "エスパーダ・パソドブレ"; // SILVER: 自他ダイス合計+5 / 与ダメ+20% / 被ダメ+20%
        public const string FleuretBallet   = "フルーレ・バレエ";       // BRONZE: ダイス合計+3 / 敗北時 自壊+最大HP1で生還

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
        public static bool TryTransform(RunState run)
        {
            if (run?.ownedPassiveItems == null) return false;
            if (CountInInventory(run) < All.Length) return false;

            // 後ろから消して index ズレを防ぐ（刻印リストも PassiveAddHelper.RemoveAt が同期）
            for (int i = run.ownedPassiveItems.Count - 1; i >= 0; i--)
                if (IsDance(run.ownedPassiveItems[i]))
                    InventorySystem.Helpers.PassiveAddHelper.RemoveAt(run, i);

            InventorySystem.Helpers.PassiveAddHelper.AddPassiveItem(run, Finale);

            // 取得時シード: 4枚集約完成への報酬として現在層 × 3 スタックを初期付与 (後半取得偏重の救済)
            var bd = InventorySystem.PassiveSkills.PassiveSkillRegistry.Get("BladeDance")
                     as InventorySystem.PassiveSkills.Effects.BladeDance;
            bd?.SeedOnAcquire(run.currentFloor);

            UnityEngine.Debug.Log("[剣の舞] インベントリに4枚集約 → 全消滅し〈ブレイドダンス〉に変化");
            return true;
        }
    }
}
