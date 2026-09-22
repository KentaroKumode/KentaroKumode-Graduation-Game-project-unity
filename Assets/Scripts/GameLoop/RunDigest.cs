using System;
using System.Collections.Generic;
using System.Text;

namespace GameLoop
{
    /// <summary>
    /// 行動台帳（<see cref="RunChronicle"/>）のコード列を、エンディング後に読ませる
    /// **短い事後記録**へ組み立てる。
    ///
    /// <para><b>要約ではなく選抜である。</b> 1 ラン は戦闘 35.6・イベント 8.3・購入 21.5 件あり、
    /// 全部書くと議事録になる。実測（601 ラン）では戦闘の <b>61.0% が A1 圧倒 / 26.2% が A2 順当</b>で、
    /// この 87% は「山」ではない ── **捨てる**。残る A3〜A6 が 1 ラン 4.6 件、
    /// これに層の踏破・最終 Tier 到達・代償を払った事件を足して 14 件前後になる。
    /// そこから <see cref="MaxBeats"/> 件へ絞る。</para>
    ///
    /// <para><b>文体の規約（厳守）。</b> 第三者視点の記録であり、本人の主観的独白を書かない。
    /// 第三者が知り得ないこと（行動の理由・内心）を断言しない。知り得なかったことは
    /// 「確かめられなかった」と書く。決め台詞を置かない。
    /// イベントの文面は <c>EventChoice.digest</c> が正本で、**postFlavor で代用しない**
    /// ── あちらは選択直後に読む独白調で、回想の文脈に置くと語りの位置がずれる。</para>
    /// </summary>
    public static class RunDigest
    {
        /// <summary>本文に残すビート数の上限。
        /// 8 件では**層が丸ごと落ちて**記録として読めなかった（二層と六層が消えた）。
        /// 1 ラン の候補が 14 件前後なので、20 ならほぼ全部を拾いつつ
        /// 圧倒・順当だけを落とす形になる。</summary>
        public const int MaxBeats = 20;

        // ─────────────────────────────────────────────────────────
        //  組み立て
        // ─────────────────────────────────────────────────────────

        public static string Render(IReadOnlyList<string> chronicle)
        {
            if (chronicle == null || chronicle.Count == 0) return "(記録なし)";

            var beats = new List<Beat>();
            foreach (var line in chronicle)
            {
                var b = Parse(line);
                if (b.code == null) continue;
                b.weight = WeightOf(b);
                if (b.weight > 0) beats.Add(b);
            }

            // ── 選抜 ────────────────────────────────────────────
            //  骨格を先に確定させ、残った枠を事件その他で埋める。
            //    ① ボス戦は無条件で入れる（層の節目そのもの）
            //    ② 敗北も無条件（ラン の決着）
            //    ③ ボス以外の戦闘は **層ごとに最も重い 1 件だけ**
            //       ── 1 ラン 35.6 戦のうち大半は同じ相手の反復で、二度目を書いても記録は増えない
            //    ④ 空いた枠を重み順で埋める（事件が戦闘より高い重みを持つ）
            var chosen = new HashSet<int>();   // beats の**位置**で持つ（index ではない）
            var isCombat = new bool[beats.Count];
            var bestZakoPerFloor = new Dictionary<int, int>();

            for (int i = 0; i < beats.Count; i++)
            {
                var b = beats[i];
                isCombat[i] = b.code[0] == 'A';
                if (!isCombat[i]) continue;

                bool loss = b.code == RunChronicle.CombatFall || b.code == RunChronicle.CombatRout;
                if (b.boss || loss) { chosen.Add(i); continue; }              // ①②

                if (!bestZakoPerFloor.TryGetValue(b.floor, out int j)         // ③
                    || b.weight > beats[j].weight)
                    bestZakoPerFloor[b.floor] = i;
            }
            foreach (var kv in bestZakoPerFloor) chosen.Add(kv.Value);

            // 決着 (G2/G3) は必ず載せる。
            for (int i = 0; i < beats.Count; i++)
                if (beats[i].code == RunChronicle.EndDeath || beats[i].code == RunChronicle.EndClear)
                    chosen.Add(i);

            // ④ 残枠を埋める。 **戦闘はここでは足さない** ── ③ で層ごと 1 件に絞った以上、
            //    落ちた戦闘を重み順で拾い直すと絞った意味が消える。
            var fill = new List<int>();
            for (int i = 0; i < beats.Count; i++)
                if (!isCombat[i] && !chosen.Contains(i)) fill.Add(i);
            fill.Sort((x, y) => beats[y].weight.CompareTo(beats[x].weight));
            foreach (int i in fill)
            {
                if (chosen.Count >= MaxBeats) break;
                chosen.Add(i);
            }

            // 層ごとに最低 1 件を確保する（戦闘の無い層が本文から消えるのを防ぐ）。
            var floorHasPick = new HashSet<int>();
            var bestPerFloor = new Dictionary<int, int>();
            for (int i = 0; i < beats.Count; i++)
            {
                var b = beats[i];
                if (b.code[0] == 'G') continue;             // 決着は層に属さない
                if (chosen.Contains(i)) floorHasPick.Add(b.floor);
                if (!bestPerFloor.TryGetValue(b.floor, out int j) || b.weight > beats[j].weight)
                    bestPerFloor[b.floor] = i;
            }
            foreach (var kv in bestPerFloor)
                if (!floorHasPick.Contains(kv.Key)) chosen.Add(kv.Value);

            var sb = new StringBuilder();
            int lastFloor = -1;
            // 直前と同一の文は落とす。 文の選択を行のハッシュで決めている以上、
            //   **同じ敵・同じターン・同じ被ダメの戦闘からは必ず同じ文が出る**
            //   （乱数を使わない＝同じランからは同じ物語が出る、という設計の代償）。
            //   連続した重複だけを潰す ── 離れた位置での再出現は記録として許容する。
            string lastText = null;
            for (int i = 0; i < beats.Count; i++)
            {
                var b = beats[i];
                if (!chosen.Contains(i)) continue;

                // **文を先に決めてから見出しを出す。** 逆にすると、落とされたビートのために
                //   本文の無い層見出しだけが残る。
                string text = TextOf(b);
                if (string.IsNullOrEmpty(text)) continue;
                if (text == lastText) continue;
                lastText = text;

                if (b.floor != lastFloor && b.code[0] != 'G')
                {
                    if (lastFloor >= 0) sb.AppendLine();
                    sb.AppendLine($"── {Kanji(b.floor)}層 ──");
                    lastFloor = b.floor;
                }
                if (b.code[0] == 'G') sb.AppendLine();      // 決着の前は一行空ける
                sb.AppendLine("　" + text);
            }
            return sb.ToString().TrimEnd();
        }

        // ─────────────────────────────────────────────────────────
        //  選抜 — 重み 0 は本文に出さない
        // ─────────────────────────────────────────────────────────

        private static int WeightOf(Beat b)
        {
            switch (b.code)
            {
                // 圧倒・順当は「山」ではない。 87% がここに入るので**雑魚なら捨てる**。
                //   ただし**ボス戦は分類に関わらず残す** ── 層の節目そのものなので、
                //   危なげなく勝った層だけボスが記録から消えるのは記録として歪む
                //   （実測では一〜四層のボスが全部この理由で落ちていた）。
                case RunChronicle.CombatCrush:
                case RunChronicle.CombatClean:  return b.boss ? 60 : 0;

                case RunChronicle.CombatNarrow: return 100 + (30 - b.hp);   // 残 HP が薄いほど重い
                case RunChronicle.CombatRout:   return 95;
                case RunChronicle.CombatFall:   return 90;
                case RunChronicle.CombatGrind:  return 40 + b.dmg + (b.boss ? 25 : 0);

                case RunChronicle.EndDeath:     return 1000;
                case RunChronicle.EndClear:     return 1000;
                case RunChronicle.EndKill:      return 0;   // 層の区切りは見出しが担う

                case RunChronicle.ForgeApex:    return 55;
                case RunChronicle.BuyWeapon:    return 45;
                case RunChronicle.BuyDice:      return 35;

                // **事件は戦闘より優先する。** 事件は (イベントID × 選択肢) ごとに固有の文を
                //   持つので、1 件が 1 件分の情報を運ぶ。一方、戦闘は同じ形の文が使い回されるうえ、
                //   1 ラン 35.6 件と数が多く、放っておくと本文を埋め尽くす。
                case RunChronicle.EventPrice:   return 70;
                case RunChronicle.EventTake:    return 60;
                case RunChronicle.EventRefuse:  return 60;

                case RunChronicle.LambdaEnter:  return 45;
                case RunChronicle.LambdaLeave:  return 45;
                case RunChronicle.LambdaSink:   return 95;
                default:                        return 0;
            }
        }

        // ─────────────────────────────────────────────────────────
        //  文
        // ─────────────────────────────────────────────────────────

        private static string TextOf(Beat b)
        {
            switch (b.code)
            {
                // 圧倒・順当に文があるのは**ボスのときだけ**。 雑魚は重み 0 でここへ来ない。
                case RunChronicle.CombatCrush:
                    return b.boss ? Pick(b, CrushBoss) : null;
                case RunChronicle.CombatClean:
                    return b.boss ? Pick(b, CleanBoss) : null;
                case RunChronicle.CombatGrind:
                    return Pick(b, b.boss ? GrindBoss : Grind);
                case RunChronicle.CombatNarrow:
                    return Pick(b, Narrow);
                case RunChronicle.CombatFall:
                    return Pick(b, Fall);
                case RunChronicle.CombatRout:
                    return Pick(b, Rout);

                case RunChronicle.ForgeApex:
                    return $"{ItemName(b.item)}は、それ以上鍛えられない形に達した。";
                case RunChronicle.BuyWeapon:
                    return $"{ItemName(b.item)}を購めた。";
                case RunChronicle.BuyDice:
                    return $"{ItemName(b.item)}を手に入れた。以後、振る目が変わった。";

                case RunChronicle.EventTake:
                case RunChronicle.EventRefuse:
                case RunChronicle.EventPrice:
                    return EventDigest(b.item, b.choice);

                case RunChronicle.LambdaEnter:
                    return "時間の狭間へ踏み込んだ。層の数え方が、そこでは通じなかった。";
                case RunChronicle.LambdaLeave:
                    return "時間の狭間から引き返した。持ち出せたものは、背負えるだけだった。";
                case RunChronicle.LambdaSink:
                    return "時間の狭間から出る道は、見つからなかった。";

                case RunChronicle.EndDeath:
                    return $"{Kanji(b.floor)}層で、記録はそこで途切れている。";
                case RunChronicle.EndClear:
                    return "七層を抜けた。戻ってきた者の数は、記録に残っていない。";
                default:
                    return null;
            }
        }

        // **文中に実数値を出さない。** ターン数・被ダメ率・残 HP・金額を一切書かない。
        //   置ける差し込みは {e}（相手の名）だけで、数値のプレースホルダを足してはならない。
        //   数値は**どの文を選ぶかの判断にだけ**使う。
        //   これは体裁の問題ではない ── 「8ターン立ち続けた／残り体力100%」という
        //   矛盾した記録が出たのは、回復で埋め戻された戦闘の生値をそのまま書いたからで、
        //   統計値は文脈を持たないまま並べると事実と食い違う。
        private static readonly string[] Grind =
        {
            "{e}とは長く噛み合った。決着したときには、こちらも相応に削られていた。",
            "{e}は一撃では倒れなかった。打ち合いは続き、無傷では抜けられなかった。",
            "{e}との戦いは長引いた。倒しはしたが、負った傷は浅くない。",
        };

        private static readonly string[] CrushBoss =
        {
            "{e}は、抵抗らしい抵抗を見せないまま倒れた。",
            "{e}との決着は早々についた。傷らしい傷は残らなかった。",
        };

        private static readonly string[] CleanBoss =
        {
            "{e}を退けた。危ういところはなかった。",
            "{e}は倒れた。手順どおりに運んだ戦いだった。",
        };

        private static readonly string[] GrindBoss =
        {
            "{e}との戦いは長く続いた。倒れたのは相手だったが、こちらも深く削られていた。",
            "{e}は容易に膝をつかなかった。押し合いの末に決着したとき、傷は残っていた。",
        };

        private static readonly string[] Narrow =
        {
            "{e}を倒したとき、立っているのがやっとだった。",
            "{e}との決着はついた。もう一撃を許していれば、倒れていたのはこちらだった。",
        };

        private static readonly string[] Fall =
        {
            "{e}との戦いは長く続き、そこで途切れた。",
            "{e}を削り切れなかった。先に倒れたのは、こちらだった。",
        };

        // 〈一蹴〉は **「万全の状態から入って敗れた」** の意味で、
        //   短さは定義に含まれない ── 初版は速攻を前提に書いてしまい、
        //   16 ターン粘った敗北に「傷らしい傷を負う前に」と付いてデータと矛盾した。
        //   ターン数には触れない。
        private static readonly string[] Rout =
        {
            "{e}の前に立ったとき、傷らしい傷は負っていなかった。それでも越えられなかった。",
            "万全の状態で{e}に挑み、そのまま敗れた。削り合いにはならなかった。",
        };

        /// <summary>行の内容から決まる添字で選ぶ。 乱数を使わない ──
        /// **同じランからは同じ文が出る**方が、記録としての性質に合う。</summary>
        private static string Pick(Beat b, string[] pool)
        {
            int h = 17;
            foreach (char c in b.raw) h = h * 31 + c;
            // 差し込みは相手の名だけ。 **数値の差し込みを足さないこと**（上の規約参照）。
            return pool[(h & 0x7fffffff) % pool.Length].Replace("{e}", EnemyName(b.item));
        }

        // ─────────────────────────────────────────────────────────
        //  名前解決 — 引けなければ ID をそのまま出す（黙って落とさない）
        // ─────────────────────────────────────────────────────────

        private static string EnemyName(string id)
        {
            if (string.IsNullOrEmpty(id)) return "それ";
            try
            {
                var e = CombatSystem.EnemyDatabase.Get(id);
                if (e != null && !string.IsNullOrEmpty(e.displayName)) return e.displayName;
            }
            catch { }
            return id;
        }

        private static string ItemName(string id)
        {
            if (string.IsNullOrEmpty(id)) return "それ";
            try
            {
                var it = InventorySystem.ItemDatabase.Instance?.GetItem(id);
                if (it != null && !string.IsNullOrEmpty(it.displayName)) return it.displayName;
            }
            catch { }
            return id;
        }

        private static string EventDigest(string evId, int choice)
        {
            try
            {
                var ev = EventSystem.EventDatabase.GetById(evId);
                if (ev != null && choice >= 0 && choice < ev.choices.Count)
                {
                    string d = ev.choices[choice].digest;
                    // **未記入なら沈黙する。** postFlavor で埋めない（独白調が混じる）。
                    if (!string.IsNullOrEmpty(d)) return d;
                }
            }
            catch { }
            return null;
        }

        private static string Kanji(int n)
        {
            switch (n)
            {
                case 1: return "一"; case 2: return "二"; case 3: return "三";
                case 4: return "四"; case 5: return "五"; case 6: return "六";
                case 7: return "七"; default: return n.ToString();
            }
        }

        // ─────────────────────────────────────────────────────────
        //  行の解釈
        // ─────────────────────────────────────────────────────────

        private struct Beat
        {
            public string code, item, raw;
            public int floor, turns, hp, dmg, gold, choice, weight, index;
            public bool boss;
        }

        private static int _seq;

        private static Beat Parse(string line)
        {
            var b = new Beat { index = _seq++, raw = line, choice = -1 };
            if (string.IsNullOrEmpty(line)) return b;

            var parts = line.Split('|');
            b.code = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                int eq = parts[i].IndexOf('=');
                if (eq <= 0) continue;
                string k = parts[i].Substring(0, eq);
                string v = parts[i].Substring(eq + 1);
                int n;
                switch (k)
                {
                    case "f":    if (int.TryParse(v, out n)) b.floor = n; break;
                    case "t":    if (int.TryParse(v, out n)) b.turns = n; break;
                    case "hp":   if (int.TryParse(v, out n)) b.hp = n; break;
                    case "d":    if (int.TryParse(v, out n)) b.dmg = n; break;
                    case "g":    if (int.TryParse(v, out n)) b.gold = n; break;
                    case "c":    if (int.TryParse(v, out n)) b.choice = n; break;
                    case "boss": b.boss = true; break;
                    case "e":    b.item = v; break;
                    case "i":    b.item = v; break;
                    case "ev":   b.item = v; break;
                }
            }
            return b;
        }

        /// <summary>最後に終了したランを 1 本、物語として出す。</summary>
        public static string RenderLastRun()
        {
            _seq = 0;
            return Render(RunChronicle.LastRun);
        }
    }
}
