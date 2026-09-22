using System.Collections.Generic;

namespace GameLoop
{
    /// <summary>
    /// ダイスの**出目パーツ**（2026-08-17 リワーク）。 面を 1 枚**追加**する。
    ///
    /// <para><b>面は値ではなく個体。</b> 素の <c>1</c> とパーツの <c>1</c> は別物で、
    /// 効果はパーツの面に止まったときだけ働く。 そのためロールは
    /// 「値」ではなく**どの面に止まったか (添字)** を持ち回る必要がある
    /// （<see cref="CombatSystem.CombatManager"/> のロール地点を参照）。</para>
    ///
    /// <para><b>36 種。</b> 出目 1〜9 × Tier 1〜4。 ラン中に同じ (出目, Tier) は 1 回しか出現しない。</para>
    ///
    /// <list type="table">
    ///   <item><term>T1</term><description>そのダイスは**振り直せない**</description></item>
    ///   <item><term>T2</term><description>なし（面が 1 枚増えるだけ）</description></item>
    ///   <item><term>T3</term><description>**異なる 2 端子**へ同時接続できる</description></item>
    ///   <item><term>T4</term><description>T3 に加え**同一端子へ重ねられ**、重ねたとき その端子に +3</description></item>
    /// </list>
    ///
    /// <para><b>実体ダイスとゴーストダイス。</b> 2 接続のうち片方が実体、もう片方がゴースト。
    /// 合計値には両方が乗るが、<b>端子役の判定に参加するのは実体だけ</b>。
    /// これが無いと 1 個のダイスが 2 個ぶんとして数えられ、 同一端子へ重ねた瞬間に
    /// 〈対〉が確定成立する（端子役は 対 / 束 / 小階 / 飛階）。
    /// 手札役（二対・中階・大階・満・極）は 5 個の出目そのものを見るので配線の影響を受けない。</para>
    ///
    /// <para><b>面の重複は許す。</b> 廃止したダイス強化は「最も低い 2 面に +1」で面を潰して
    /// <c>1 2 3 4 5 6</c> を Lv3 で <c>4 4 4 4 5 6</c> にし、〈極〉を 1 ラン 12.7 回
    /// （想定 0.12 回）まで暴走させた。 <b>あれが壊れたのは素材を払えば確実に潰せたから</b>で、
    /// こちらは 36 種から特定の数枚を引き当てる必要がある。 実測（機会費用込み）:
    /// 特定の 1 出目を 3 枚集めるには陳列を 24 個見て 59%、 その間ほかのパーツを全部見送るので
    /// 面数 30 → 9・効果ゼロという代償を払う。 素直に集めると面が増えて分母が伸びるため、
    /// 12 枚あたりで〈極〉はむしろ**基準を下回る** (0.93 倍)。</para>
    ///
    /// <para><b>効果は出目が確定した後にだけ働く。</b>「この面はワイルド」「別の目として扱う」は禁止。
    /// 確率層に触った瞬間、 廃止したダイス強化と同じ事故が別の形で再発する。</para>
    /// </summary>
    public static class DiceFaceParts
    {
        /// <summary>パーツになれる出目の範囲。</summary>
        public const int MinFace = 1;
        public const int MaxFace = 9;

        /// <summary>Tier。 <see cref="Tier.None"/> は「パーツではない素の面」を表す番兵。</summary>
        public enum Tier { None = 0, T1 = 1, T2 = 2, T3 = 3, T4 = 4 }

        /// <summary>T4 が同一端子へ重ねたときに、その端子の合計値へ足す量。</summary>
        public const int T4StackBonus = 3;

        /// <summary>ラン中に存在するパーツの総数（出目 9 × Tier 4）。</summary>
        public const int TotalKinds = (MaxFace - MinFace + 1) * 4;

        /// <summary>装着済みパーツ 1 枚 ＝ 追加される面 1 枚。</summary>
        [System.Serializable]
        public struct Part
        {
            /// <summary>この面の出目 (1〜9)。</summary>
            public int face;
            /// <summary>効果の段。</summary>
            public Tier tier;

            public Part(int face, Tier tier) { this.face = face; this.tier = tier; }

            /// <summary>(出目, Tier) を 1 つの int に詰めた識別子。 重複出現の判定に使う。</summary>
            public int Key => face * 10 + (int)tier;
        }

        /// <summary>36 種の全列挙。</summary>
        public static IEnumerable<Part> AllKinds()
        {
            for (int f = MinFace; f <= MaxFace; f++)
                for (int t = 1; t <= 4; t++)
                    yield return new Part(f, (Tier)t);
        }

        /// <summary>**素のサイコロ。** 2026-08-17 にダイスというアイテム種別を廃止し、
        /// 全員が同じ 6 面から始まる形にした。 個性はすべて出目パーツで付ける。
        ///
        /// <para>旧構成はダイス 10 種に固有の面表 (木 1-6 / 竜 5-10 / 天 2..14 等) を与える設計だったが、
        /// **実装されないまま 10 種すべてが <c>[1,2,3,4,5,6]</c> で同一**になっていた。
        /// 面が同じでは <see cref="Loadout"/> の更新判定が常に false を返し、
        /// ショップのダイス枠は BOT が 1 個も買わない死に枠だった。
        /// 「面構成で個性を出す」役割はパーツが引き継ぐ。</para></summary>
        public static readonly int[] BaseFaces = { 1, 2, 3, 4, 5, 6 };

        /// <summary>まだ所持していない種類を列挙する。 ショップの抽選プール。
        ///
        /// <para><b>「出現済み」ではなく「所持済み」で弾く。</b> 序盤に見送った品が
        /// 二度と出ないと、 買わない判断がそのままランを縛ってしまう。
        /// 見送ったものは何度でも並ぶ。</para></summary>
        public static List<Part> RemainingKinds(IList<Part> owned)
        {
            var list = new List<Part>(TotalKinds);
            foreach (var p in AllKinds())
            {
                bool has = false;
                if (owned != null)
                    for (int i = 0; i < owned.Count && !has; i++)
                        if (owned[i].Key == p.Key) has = true;
                if (!has) list.Add(p);
            }
            return list;
        }

        /// <summary>面配列にパーツを**追加**する。 純関数。
        /// <b>差し替えではない</b> ── 面数は 6 → 6+パーツ枚数 に増える。</summary>
        public static int[] Apply(int[] baseFaces, IList<Part> parts)
        {
            if (baseFaces == null || baseFaces.Length == 0) return baseFaces;
            if (parts == null || parts.Count == 0) return baseFaces;

            var arr = new int[baseFaces.Length + parts.Count];
            System.Array.Copy(baseFaces, arr, baseFaces.Length);
            for (int i = 0; i < parts.Count; i++)
                arr[baseFaces.Length + i] = parts[i].face;
            return arr;
        }

        /// <summary>面配列と、**面添字ごとの Tier** を同時に組む。
        ///
        /// <para>ロールは <c>faces[idx]</c> で値を取り、 同じ <paramref name="tiers"/><c>[idx]</c> で
        /// 効果を引く。 添字が一致していることがこの仕組みの前提なので、
        /// <b>faces と tiers は必ずこの関数から対で受け取ること</b>
        /// （別々に組むと素の面とパーツ面がずれ、 効果が別の面に付く）。</para>
        ///
        /// <para>素の面は <see cref="Tier.None"/>。 T2 も効果は無いが
        /// <c>Tier.T2</c> として区別する ── 「パーツで増えた面か」は
        /// UI 表示と計装で必要になる。</para></summary>
        public static void Build(int[] baseFaces, IList<Part> parts, out int[] faces, out Tier[] tiers)
        {
            int baseLen = baseFaces?.Length ?? 0;
            int extra = parts?.Count ?? 0;

            faces = new int[baseLen + extra];
            tiers = new Tier[baseLen + extra];
            if (baseLen > 0) System.Array.Copy(baseFaces, faces, baseLen);
            // 素の面は Tier.None のまま (配列の既定値 0)。
            for (int i = 0; i < extra; i++)
            {
                faces[baseLen + i] = parts[i].face;
                tiers[baseLen + i] = parts[i].tier;
            }
        }

        /// <summary>その面添字が「振り直せない」(T1) か。</summary>
        public static bool IsLocked(Tier[] tiers, int faceIndex)
            => tiers != null && faceIndex >= 0 && faceIndex < tiers.Length
               && tiers[faceIndex] == Tier.T1;

        /// <summary>その面添字が 2 接続を許すか (T3 / T4)。</summary>
        public static bool AllowsDualLink(Tier[] tiers, int faceIndex)
        {
            if (tiers == null || faceIndex < 0 || faceIndex >= tiers.Length) return false;
            var t = tiers[faceIndex];
            return t == Tier.T3 || t == Tier.T4;
        }

        /// <summary>その面添字が**同一端子への重ね**を許すか (T4 のみ)。</summary>
        public static bool AllowsSameTerminalStack(Tier[] tiers, int faceIndex)
            => tiers != null && faceIndex >= 0 && faceIndex < tiers.Length
               && tiers[faceIndex] == Tier.T4;

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
                case Tier.T1: return "この面が出たダイスは振り直せない";
                case Tier.T2: return "効果なし";
                case Tier.T3: return "この面が出たダイスは異なる2端子へ同時接続できる（実体1・ゴースト1）";
                case Tier.T4: return $"この面が出たダイスは2端子へ同時接続でき、同じ端子に重ねるとその端子に+{T4StackBonus}";
                default:      return "";
            }
        }
    }
}
