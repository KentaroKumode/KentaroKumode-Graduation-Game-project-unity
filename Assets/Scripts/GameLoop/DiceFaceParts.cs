using System.Collections.Generic;

namespace GameLoop
{
    /// <summary>
    /// ダイスの**面**（出目パーツ）。 ADR-0011 (2026-10-18) で作り直した。
    ///
    /// <para><b>役はパーツが持つ。</b> 旧設計は役を全員が無料で持ち、パーツは端子の置き方を変えていた。
    /// いまは役の効果そのものをパーツ（＝カスタム面）に紐づける。 T1 は素の面で何も起きない。</para>
    ///
    /// <list type="table">
    ///   <item><term>T1</term><description>無能力（素の面）。 出目の数字だけを持つ</description></item>
    ///   <item><term>T2</term><description>その目の<b>2 個そろい</b>の効果が発動する</description></item>
    ///   <item><term>T3</term><description>T2 に加えて、その目の<b>ゾロ目</b>の効果も発動する</description></item>
    ///   <item><term>T4</term><description>T3 に加えて、その目<b>固有の追加効果</b>が付く</description></item>
    /// </list>
    ///
    /// <para><b>同じ目の面は 1 枚まで。</b> 面は {1…9} の部分集合になる。 すでに持つ目のパーツを買うと
    /// <b>差し替え</b>（Tier が上がる時だけ。同じか下がる品は並ばない）。
    /// 面の状態は <see cref="RunState.diceFaceParts"/> が全部持つ（素の面 {2,3,4,5} もここに入る）。</para>
    ///
    /// <para><b>面は値ではなく個体。</b> ロールは値ではなく<b>どの面に止まったか (添字)</b> を持ち回る。
    /// 効果は止まった面の Tier で引く。</para>
    ///
    /// <para><b>効果は出目が確定した後にだけ働く。</b>「この面はワイルド」「別の目として扱う」は禁止。
    /// 確率層に触った瞬間、 廃止したダイス強化と同じ事故が別の形で再発する。</para>
    /// </summary>
    public static class DiceFaceParts
    {
        /// <summary>パーツになれる出目の範囲。</summary>
        public const int MinFace = 1;
        public const int MaxFace = 9;

        /// <summary>面の最少枚数。 これ以下には外せない。</summary>
        public const int MinFaceCount = 2;

        /// <summary>Tier。 <see cref="Tier.None"/> は「面を持たない」番兵（配列の既定値）。</summary>
        public enum Tier { None = 0, T1 = 1, T2 = 2, T3 = 3, T4 = 4 }

        /// <summary>ラン中に存在するパーツの総数（出目 9 × Tier 4）。</summary>
        public const int TotalKinds = (MaxFace - MinFace + 1) * 4;

        /// <summary>装着済みパーツ 1 枚 ＝ 面 1 枚。</summary>
        [System.Serializable]
        public struct Part
        {
            /// <summary>この面の出目 (1〜9)。</summary>
            public int face;
            /// <summary>効果の段。</summary>
            public Tier tier;

            public Part(int face, Tier tier) { this.face = face; this.tier = tier; }

            /// <summary>(出目, Tier) を 1 つの int に詰めた識別子。</summary>
            public int Key => face * 10 + (int)tier;
        }

        /// <summary>36 種の全列挙。</summary>
        public static IEnumerable<Part> AllKinds()
        {
            for (int f = MinFace; f <= MaxFace; f++)
                for (int t = 1; t <= 4; t++)
                    yield return new Part(f, (Tier)t);
        }

        /// <summary>**素の面。** 全員が {2,3,4,5} の 4 面（すべて T1）から始まる。
        /// 6 面である必要は無い（ADR-0011）。</summary>
        public static readonly int[] StartingFaces = { 2, 3, 4, 5 };

        /// <summary>ラン開始時の面の一覧（素の面を T1 のパーツとして並べたもの）。</summary>
        public static List<Part> StartingParts()
        {
            var list = new List<Part>(StartingFaces.Length);
            for (int i = 0; i < StartingFaces.Length; i++)
                list.Add(new Part(StartingFaces[i], Tier.T1));
            return list;
        }

        /// <summary>その目の面を持っていれば Tier、 無ければ <see cref="Tier.None"/>。</summary>
        public static Tier TierOfFace(IList<Part> owned, int face)
        {
            if (owned == null) return Tier.None;
            for (int i = 0; i < owned.Count; i++)
                if (owned[i].face == face) return owned[i].tier;
            return Tier.None;
        }

        /// <summary>まだ手に入る種類を列挙する。 ショップの抽選プール。
        ///
        /// <para><b>同じ目は 1 枚まで</b>なので、 すでに持つ目は<b>より高い Tier</b>だけが並ぶ（差し替え）。
        /// 「出現済み」ではなく「所持状態」で弾く ── 見送った品は何度でも並ぶ。</para></summary>
        public static List<Part> RemainingKinds(IList<Part> owned)
        {
            var list = new List<Part>(TotalKinds);
            foreach (var p in AllKinds())
                if ((int)p.tier > (int)TierOfFace(owned, p.face)) list.Add(p);
            return list;
        }

        /// <summary>パーツを装着する。 同じ目の面があれば<b>差し替え</b>、 無ければ追加。
        /// 戻り値: 実際に変わったか（同じか低い Tier なら false）。</summary>
        public static bool AddOrReplace(IList<Part> owned, Part add)
        {
            if (owned == null) return false;
            for (int i = 0; i < owned.Count; i++)
            {
                if (owned[i].face != add.face) continue;
                if ((int)add.tier <= (int)owned[i].tier) return false;
                owned[i] = add;
                return true;
            }
            owned.Add(add);
            return true;
        }

        /// <summary>面配列と、**面添字ごとの Tier** を同時に組む。
        ///
        /// <para>ロールは <c>faces[idx]</c> で値を取り、 同じ <paramref name="tiers"/><c>[idx]</c> で
        /// 効果を引く。 添字が一致していることがこの仕組みの前提なので、
        /// <b>faces と tiers は必ずこの関数から対で受け取ること</b>。</para>
        ///
        /// <para>面が空（テストや Run の無い文脈）なら素の面 {2,3,4,5} を使う。</para></summary>
        public static void Build(IList<Part> parts, out int[] faces, out Tier[] tiers)
        {
            if (parts == null || parts.Count == 0) parts = StartingParts();
            faces = new int[parts.Count];
            tiers = new Tier[parts.Count];
            for (int i = 0; i < parts.Count; i++)
            {
                faces[i] = parts[i].face;
                tiers[i] = parts[i].tier;
            }
        }

        // ============================================================
        //  面を外す (休憩エリアの強化・ADR-0011)
        // ============================================================

        /// <summary>外す価格の基礎（ゴールド換算）。</summary>
        public const int RemovalBasePrice = 30;
        /// <summary>外すたびに足す額。</summary>
        public const int RemovalStep = 30;
        /// <summary>価格のうち強化素材で払う割合。</summary>
        public const float RemovalMaterialShare = 0.40f;
        /// <summary>素材 1 個のゴールド換算（ショップの基準価格）。</summary>
        public const int MaterialGoldValue = 15;

        /// <summary>外す価格。 <paramref name="removalsDone"/> = これまでに外した回数、
        /// <paramref name="faceCountBefore"/> = 外す前の面の枚数。
        /// 外した結果が素の面数 (4) を下回る時は倍率が掛かる: 4→3 は 2 倍、3→2 は 3 倍。
        /// 倍率は素材にも掛かる。 戻り値: 外せるか（最少枚数以下は false）。</summary>
        public static bool RemovalCost(int removalsDone, int faceCountBefore, out int gold, out int materials)
        {
            gold = 0; materials = 0;
            if (faceCountBefore <= MinFaceCount) return false;

            int price = RemovalBasePrice + RemovalStep * removalsDone;
            if (faceCountBefore == 4) price *= 2;
            else if (faceCountBefore == 3) price *= 3;

            materials = (int)System.Math.Round(price * RemovalMaterialShare / MaterialGoldValue,
                                               System.MidpointRounding.AwayFromZero);
            gold = (int)System.Math.Round(price * (1f - RemovalMaterialShare),
                                          System.MidpointRounding.AwayFromZero);
            return true;
        }

        // ============================================================
        //  ID (2026-08-17)
        // ============================================================
        // パーツは items.json に無い。 それでも **他のアイテムと同じ学習経路に乗せる**ため、
        // 36 種それぞれに安定 ID を機械生成する。 ID は
        //   ・ShopSlot.itemId          → 提示/取得の記録 (offeredItemsEver / acquiredItemsEver)
        //   ・item_stats.json のキー   → lift・regβ・準パワー
        // に使われる。 **文字列を他所で組み立てないこと** ── ここが唯一の生成地点で、
        // 表記が 1 文字でもずれると学習統計が別アイテムとして分裂する
        // (CLAUDE.md「ID 文字列を直書きしない」)。
        //
        // ItemDatabase には存在しないので `GetItem(id)` は必ず null を返す。
        // 参照側は null 許容であること (表示名は Label / DescribeId で引く)。

        /// <summary>パーツ ID の接頭辞。</summary>
        public const string IdPrefix = "facepart_";

        /// <summary>パーツ → 安定 ID。 例: 出目 9 の T4 → <c>facepart_9_t4</c>。</summary>
        public static string Id(Part p) => IdPrefix + p.face + "_t" + (int)p.tier;

        /// <summary>その ID が出目パーツのものか。</summary>
        public static bool IsPartId(string id)
            => !string.IsNullOrEmpty(id) && id.StartsWith(IdPrefix, System.StringComparison.Ordinal);

        /// <summary>ID → パーツ。 解釈できなければ false。</summary>
        public static bool TryParseId(string id, out Part p)
        {
            p = default;
            if (!IsPartId(id)) return false;
            string body = id.Substring(IdPrefix.Length);
            int sep = body.IndexOf("_t", System.StringComparison.Ordinal);
            if (sep <= 0) return false;
            if (!int.TryParse(body.Substring(0, sep), out int face)) return false;
            if (!int.TryParse(body.Substring(sep + 2), out int tier)) return false;
            if (face < MinFace || face > MaxFace || tier < 1 || tier > 4) return false;
            p = new Part(face, (Tier)tier);
            return true;
        }

        /// <summary>表示名。 ログと UI で共通に使う。</summary>
        public static string Label(Part p)
            => $"出目{p.face}・{p.tier}";

        /// <summary>ID からの表示名。 パーツ ID でなければ null
        /// (Tier リストなど「id しか持っていない」側から引くため)。</summary>
        public static string LabelOfId(string id)
            => TryParseId(id, out var p) ? Label(p) : null;

        /// <summary>効果の説明文。 ショップ・図鑑で共通に使う。</summary>
        public static string Describe(Tier t)
        {
            switch (t)
            {
                case Tier.T1: return "効果なし（素の面）";
                case Tier.T2: return "この目の2個そろいの効果が発動する";
                case Tier.T3: return "この目の2個そろいとゾロ目の効果が発動する";
                case Tier.T4: return "2個そろい・ゾロ目に加えて、この目固有の追加効果が付く";
                default:      return "";
            }
        }
    }
}
