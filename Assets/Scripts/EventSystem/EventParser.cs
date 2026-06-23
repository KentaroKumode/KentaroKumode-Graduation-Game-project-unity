using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace EventSystem
{
    /// <summary>
    /// イベントテキストフォーマットをパースする。
    /// フォーマット: イベント名:出現条件:フレーバー:選択肢-結果-選択後フレ/選択肢-結果-選択後フレ
    /// コメント行は # で始まる行、または 「###」「━━」を含む見出し行は無視。
    /// </summary>
    public static class EventParser
    {
        /// <summary>テキスト全体をパースして EventDefinition のリストを返す。</summary>
        public static List<EventDefinition> ParseAll(string text)
        {
            var result = new List<EventDefinition>();
            if (string.IsNullOrEmpty(text)) return result;

            var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            int autoId = 1;

            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (string.IsNullOrEmpty(line)) continue;
                if (IsCommentLine(line)) continue;
                // 区切り記号(:)が3個未満の行は本文とみなさない
                if (CountChar(line, ':') < 3) continue;

                try
                {
                    var ev = ParseLine(line);
                    if (ev != null)
                    {
                        if (string.IsNullOrEmpty(ev.id))
                            ev.id = $"event_{autoId:D4}";
                        autoId++;
                        result.Add(ev);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[EventParser] 解析失敗: {line.Substring(0, Math.Min(30, line.Length))}... ({e.Message})");
                }
            }

            return result;
        }

        private static bool IsCommentLine(string line)
        {
            if (line.StartsWith("#")) return true;
            if (line.StartsWith("//")) return true;
            // 見出しっぽい行（イベントフォーマットでない）
            if (line.Contains("━━") || line.Contains("===")) return true;
            return false;
        }

        private static int CountChar(string s, char c)
        {
            int n = 0;
            foreach (var ch in s) if (ch == c) n++;
            return n;
        }

        // 条件部が "フラグ:[...]" / "パッシブ:[...]" で始まるか（内部コロンを持つ）
        private static readonly Regex condColonPrefix = new Regex(@"^(フラグ|パッシブ):\[");

        /// <summary>1行をパース</summary>
        private static EventDefinition ParseLine(string line)
        {
            // name:condition:flavor:choices を ':' で分割する。
            // ただし condition が "フラグ:[X]" / "パッシブ:[X]" で始まる場合、その内部コロンを
            // フィールド区切りと誤認しないよう1つ読み飛ばす（誤読すると優先/要求フラグ/一度のみ等が全消失する）。
            int p1 = line.IndexOf(':');
            int condStart = p1 + 1;
            int searchFrom = condStart;
            if (condStart < line.Length && condColonPrefix.IsMatch(line.Substring(condStart)))
                searchFrom = line.IndexOf(':', condStart) + 1;
            int p2 = line.IndexOf(':', searchFrom);
            int p3 = line.IndexOf(':', p2 + 1);

            string name = line.Substring(0, p1).Trim();
            string condStr = line.Substring(p1 + 1, p2 - p1 - 1).Trim();
            string flavor = line.Substring(p2 + 1, p3 - p2 - 1).Trim();
            string choicesStr = line.Substring(p3 + 1).Trim();

            var ev = new EventDefinition
            {
                id = MakeId(name),
                name = name,
                flavor = flavor,
                condition = ParseCondition(condStr),
                choices = ParseChoices(choicesStr),
            };
            return ev;
        }

        private static string MakeId(string name)
        {
            // 日本語名を小文字英数字IDに変換できないので、ハッシュ化
            if (string.IsNullOrEmpty(name)) return "";
            return $"ev_{Math.Abs(name.GetHashCode()):X8}";
        }

        // ============================================================
        //  出現条件パース
        // ============================================================

        private static readonly Regex floorRangeRegex = new Regex(@"^(\d+)\-(\d+)層$");
        private static readonly Regex floorSingleRegex = new Regex(@"^(\d+)層$");
        private static readonly Regex flagRegex = new Regex(@"^フラグ:\[(.+?)\]$");
        private static readonly Regex passiveCondRegex = new Regex(@"^パッシブ:\[(.+?)\]$");

        private static EventCondition ParseCondition(string s)
        {
            var c = new EventCondition();
            if (string.IsNullOrEmpty(s) || s == "全") return c;

            var tokens = s.Split(',');
            foreach (var rawToken in tokens)
            {
                var t = rawToken.Trim();
                if (string.IsNullOrEmpty(t)) continue;

                if (t == "全") continue;
                if (t == "一度のみ") { c.onceOnly = true; continue; }
                if (t == "優先") { c.priority = true; continue; }
                if (t == "稀") { c.rare = true; continue; }

                var m = floorRangeRegex.Match(t);
                if (m.Success)
                {
                    c.floorMin = int.Parse(m.Groups[1].Value);
                    c.floorMax = int.Parse(m.Groups[2].Value);
                    continue;
                }

                m = floorSingleRegex.Match(t);
                if (m.Success)
                {
                    int f = int.Parse(m.Groups[1].Value);
                    c.floorMin = f;
                    c.floorMax = f;
                    continue;
                }

                m = flagRegex.Match(t);
                if (m.Success)
                {
                    c.requiredFlags.Add(m.Groups[1].Value);
                    continue;
                }

                m = passiveCondRegex.Match(t);
                if (m.Success)
                {
                    c.requiredPassiveItems.Add(m.Groups[1].Value);
                    continue;
                }
            }
            return c;
        }

        // ============================================================
        //  選択肢パース
        // ============================================================

        private static List<EventChoice> ParseChoices(string block)
        {
            var list = new List<EventChoice>();
            if (string.IsNullOrEmpty(block)) return list;

            // '/' で選択肢を分割
            var parts = block.Split('/');
            foreach (var raw in parts)
            {
                var t = raw.Trim();
                if (string.IsNullOrEmpty(t)) continue;

                var choice = ParseSingleChoice(t);
                if (choice != null) list.Add(choice);
            }
            return list;
        }

        /// <summary>
        /// 「選択肢-結果-選択後フレ」を分解する。
        /// '-' は結果セクション内の数値（HP-5 等）にも現れるので、
        /// 「最初の -」と「最後の -」で挟むヒューリスティックを使う。
        /// </summary>
        private static EventChoice ParseSingleChoice(string s)
        {
            int firstDash = s.IndexOf('-');
            int lastDash = s.LastIndexOf('-');
            if (firstDash < 0 || lastDash <= firstDash)
            {
                Debug.LogWarning($"[EventParser] 選択肢形式不正: {s}");
                return null;
            }

            string choiceText = s.Substring(0, firstDash).Trim();
            string resultStr  = s.Substring(firstDash + 1, lastDash - firstDash - 1).Trim();
            string postFlavor = s.Substring(lastDash + 1).Trim();

            var choice = new EventChoice
            {
                text = choiceText,
                postFlavor = postFlavor,
                effects = ParseEffects(resultStr),
            };
            return choice;
        }

        // ============================================================
        //  効果パース
        // ============================================================

        // 「N%でA、N%でB、...」確率分岐検出
        private static readonly Regex probSplitRegex = new Regex(@"(\d+)%で");

        private static List<EventEffect> ParseEffects(string s)
        {
            var list = new List<EventEffect>();
            if (string.IsNullOrEmpty(s) || s == "なし") return list;

            // 確率分岐かどうかチェック（「N%で」を2回以上含む）
            var probMatches = probSplitRegex.Matches(s);
            if (probMatches.Count >= 2)
            {
                var prob = ParseProbabilityBranches(s, probMatches);
                if (prob != null) { list.Add(prob); return list; }
            }

            // 通常: ',' で分割
            var parts = s.Split(',');
            foreach (var raw in parts)
            {
                var t = raw.Trim();
                if (string.IsNullOrEmpty(t)) continue;
                var eff = ParseSingleEffect(t);
                if (eff != null) list.Add(eff);
            }
            return list;
        }

        private static EventEffect ParseProbabilityBranches(string s, MatchCollection matches)
        {
            var branches = new List<List<EventEffect>>();
            var weights = new List<float>();

            for (int i = 0; i < matches.Count; i++)
            {
                int pct = int.Parse(matches[i].Groups[1].Value);
                int contentStart = matches[i].Index + matches[i].Length;
                int contentEnd = (i + 1 < matches.Count) ? matches[i + 1].Index : s.Length;

                string segment = s.Substring(contentStart, contentEnd - contentStart);
                // 直前の区切り「、」「,」を除去
                segment = segment.TrimEnd('、', ',', ' ', '　');

                weights.Add(pct / 100f);
                branches.Add(ParseEffects(segment));
            }

            return new EventEffect
            {
                type = EventEffectType.Probability,
                branchWeights = weights,
                branches = branches,
            };
        }

        // 単一効果パース用の正規表現群
        private static readonly Regex hpDelta      = new Regex(@"^HP([+\-])(\d+)$");
        private static readonly Regex hpSet        = new Regex(@"^HPが(\d+)になる$");
        private static readonly Regex maxHp        = new Regex(@"^最大HP([+\-])(\d+)$");
        private static readonly Regex gold         = new Regex(@"^ゴールド([+\-])(\d+)$");
        private static readonly Regex hunger       = new Regex(@"^空腹度([+\-])(\d+)$");
        private static readonly Regex hope         = new Regex(@"^希望([+\-])(\d+)$");
        private static readonly Regex material     = new Regex(@"^武器強化素材([+\-])(\d+)$");
        private static readonly Regex tBuff        = new Regex(@"^時限バフ\[(.+?)\](?:を獲得)?$");
        private static readonly Regex tDebuff      = new Regex(@"^時限デバフ\[(.+?)\](?:を獲得)?$");
        private static readonly Regex pDebuff      = new Regex(@"^永続デバフ\[(.+?)\](?:を獲得)?$");
        private static readonly Regex passiveNamed = new Regex(@"^パッシブアイテム\[(.+?)\](?:を獲得)?$");
        private static readonly Regex consumNamed  = new Regex(@"^消費アイテム\[(.+?)\](?:を獲得)?$");
        private static readonly Regex itemNamed    = new Regex(@"^アイテム\[(.+?)\](?:を獲得)?$");
        private static readonly Regex flagGain     = new Regex(@"^フラグアイテム\[(.+?)\]を獲得$");
        private static readonly Regex flagDiscard  = new Regex(@"^フラグアイテム\[(.+?)\]を廃棄$");
        private static readonly Regex passiveDiscard = new Regex(@"^パッシブアイテム\[(.+?)\]を廃棄$");
        private static readonly Regex rareLead     = new Regex(@"^レアな");

        private static EventEffect ParseSingleEffect(string t)
        {
            bool postCombat = false;
            if (t.StartsWith("勝利後"))
            {
                postCombat = true;
                t = t.Substring(3).Trim();
            }

            // レア表記は無視（パッシブアイテム獲得として扱う）
            t = rareLead.Replace(t, "");

            Match m;

            if (t == "なし") return null;
            if (t == "HP全回復") return Eff(EventEffectType.HpFullHeal, postCombat: postCombat);
            if (t == "防具の耐久値減少") return Eff(EventEffectType.ArmorDurabilityLoss, postCombat: postCombat);
            if (t == "戦闘に突入") return Eff(EventEffectType.EnterCombat, postCombat: postCombat);
            if (t == "エリートとの戦闘に突入") return Eff(EventEffectType.EnterEliteCombat, postCombat: postCombat);
            if (t == "ランダムなイベント発生") return Eff(EventEffectType.RandomEvent, postCombat: postCombat);
            if (t == "サーカス引渡し") return Eff(EventEffectType.CircusHandover, postCombat: postCombat);
            if (t == "パッシブアイテム獲得" || t == "パッシブアイテムを獲得")
                return Eff(EventEffectType.GainPassiveItem, postCombat: postCombat);
            if (t == "消費アイテム獲得" || t == "消費アイテムを獲得")
                return Eff(EventEffectType.GainConsumableItem, postCombat: postCombat);
            if (t == "アイテム獲得" || t == "アイテムを獲得")
                return Eff(EventEffectType.GainPassiveItem, postCombat: postCombat);

            m = hpDelta.Match(t);    if (m.Success) return Eff(EventEffectType.HpDelta, amount: SignedInt(m, 1, 2), postCombat: postCombat);
            m = hpSet.Match(t);      if (m.Success) return Eff(EventEffectType.HpSetTo, amount: int.Parse(m.Groups[1].Value), postCombat: postCombat);
            m = maxHp.Match(t);      if (m.Success) return Eff(EventEffectType.MaxHpDelta, amount: SignedInt(m, 1, 2), postCombat: postCombat);
            m = gold.Match(t);       if (m.Success) return Eff(EventEffectType.GoldDelta, amount: SignedInt(m, 1, 2), postCombat: postCombat);
            m = hunger.Match(t);     if (m.Success) return Eff(EventEffectType.HungerDelta, amount: SignedInt(m, 1, 2), postCombat: postCombat);
            m = hope.Match(t);       if (m.Success) return Eff(EventEffectType.HopeDelta, amount: SignedInt(m, 1, 2), postCombat: postCombat);
            m = material.Match(t);   if (m.Success) return Eff(EventEffectType.MaterialDelta, amount: SignedInt(m, 1, 2), postCombat: postCombat);
            m = tBuff.Match(t);      if (m.Success) return Eff(EventEffectType.TimedBuff, param: m.Groups[1].Value, postCombat: postCombat);
            m = tDebuff.Match(t);    if (m.Success) return Eff(EventEffectType.TimedDebuff, param: m.Groups[1].Value, postCombat: postCombat);
            m = pDebuff.Match(t);    if (m.Success) return Eff(EventEffectType.PermanentDebuff, param: m.Groups[1].Value, postCombat: postCombat);
            m = passiveNamed.Match(t); if (m.Success) return Eff(EventEffectType.GainPassiveItem, param: m.Groups[1].Value, postCombat: postCombat);
            m = consumNamed.Match(t);  if (m.Success) return Eff(EventEffectType.GainConsumableItem, param: m.Groups[1].Value, postCombat: postCombat);
            m = itemNamed.Match(t);    if (m.Success) return Eff(EventEffectType.GainSpecificItem, param: m.Groups[1].Value, postCombat: postCombat);
            m = flagGain.Match(t);     if (m.Success) return Eff(EventEffectType.GainFlag, param: m.Groups[1].Value, postCombat: postCombat);
            m = flagDiscard.Match(t);  if (m.Success) return Eff(EventEffectType.DiscardFlag, param: m.Groups[1].Value, postCombat: postCombat);
            m = passiveDiscard.Match(t); if (m.Success) return Eff(EventEffectType.DiscardPassiveItem, param: m.Groups[1].Value, postCombat: postCombat);

            // 未知の表現はログのみ。フレーバー的な記述はスキップ。
            Debug.LogWarning($"[EventParser] 未認識の効果: '{t}'");
            return null;
        }

        private static int SignedInt(Match m, int signGroup, int numGroup)
        {
            int n = int.Parse(m.Groups[numGroup].Value);
            return m.Groups[signGroup].Value == "-" ? -n : n;
        }

        private static EventEffect Eff(EventEffectType type, string param = null, int amount = 0, bool postCombat = false)
        {
            return new EventEffect { type = type, param = param, amount = amount, postCombat = postCombat };
        }
    }
}
