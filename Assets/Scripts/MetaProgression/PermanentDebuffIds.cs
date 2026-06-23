using GameLoop;
using System.Collections.Generic;

namespace MetaProgression
{
    /// <summary>
    /// メタ層から付与される固有恒久デバフの ID 定数。
    /// run.permanentDebuffs に格納される文字列キーとして使う。
    /// 効果説明・大罪フレーバー本文も本ファイルに正本として持つ
    /// (旧 docs/specs/lore-endings.md §1-F の本文をここへ移植済み)。
    /// </summary>
    public static class PermanentDebuffIds
    {
        public const string Pride    = "カイロスの傲慢";       // 通常戦闘の勝利報酬が0、エリート/ボス戦では2倍
        public const string Envy     = "ヤルノクの嫉妬";       // 1ショップにつき1個しか購入できない
        public const string Greed    = "ムシュファの強欲";     // 5層突入時にゴールドが0になる
        public const string Wrath    = "コルヴェンの憤怒";     // ボス戦の1T目: 自ダイス全て最大値 + 現在HP半減
        public const string Sloth    = "クェシナの怠惰";       // 戦闘開始から3T間、消費アイテム使用不可
        public const string Gluttony = "トゥルハドの暴食";     // 飢餓ダメージ ×2
        public const string Lust     = "クァディルの色欲";     // ショップ入店時、買える中で最も高価な品を強制購入

        // ===== UI/Codex 表示用の効果説明 =====

        private static readonly Dictionary<string, string> shortDescriptions = new Dictionary<string, string>
        {
            { Pride,    "通常戦闘の勝利報酬が0、エリート・ボスは2倍" },
            { Envy,     "1ショップにつき通常品1個まで" },
            { Greed,    "5層突入時にゴールドが0" },
            { Wrath,    "ボス1T目 自ダイス全最大＋現HP半減" },
            { Sloth,    "開幕3T 消費アイテム使用不可" },
            { Gluttony, "飢餓ダメージ×2" },
            { Lust,     "入店時、買える中で最高額の品を強制購入" },
        };

        /// <summary>UI に出す簡易説明 (一行)。</summary>
        public static string GetShortDescription(string id)
            => id != null && shortDescriptions.TryGetValue(id, out var s) ? s : "";

        // ===== 大罪フレーバー本文 (旧 lore-endings.md §1-F 移植) =====
        //
        // 大罪はかなり昔の時代、当時の最深部まで到達し、全員その罪に呑まれて発狂し、
        // 流罪となった著名探検家の名と呪い。 効果は各々の罪の在り様に対応する。

        private static readonly Dictionary<string, string> flavorTexts = new Dictionary<string, string>
        {
            { Pride,
@"カイロスは、最深から、英雄として還った。
ギルドは彼に、若い隊長の下につくよう命じた。実績では、その男のほうが上だった。
カイロスには、耐えられなかった。自分より格下が、自分に指図する——それが。
ある朝、その隊長は、物言わぬ骸で見つかった。背中から、ただ一突き。
「あれは、俺の上に立てる器ではなかった」。カイロスは、悪びれもしなかった。
裁きは流刑——補給も供もなく、再び大穴へ落とされた。" },

            { Envy,
@"ヤルノクの帰還は、街の祭りになった。
当時の最深、五層の手前。誰も到達したことのない深みから、彼は生きて還った。遺物を抱え、地図を携え、英雄として迎えられた。
宴の席で、誰かが酌をした。隣の男のほうが、ほんの少し大きな杯で。
——ただ、それだけのことだった。
ヤルノクはその夜、男を刺した。翌朝には妹の嫁ぎ先を、その翌週には恩師を。理由は本人にもわからない。ただ、誰かが自分より多く持っていることが、耐えられなかった。
裁きは流刑だった。補給も供もなく、彼は再び大穴へ落とされた。深みで何を見たのか、誰も知らない。
今では、彼の名は呪いの名として残っている。" },

            { Greed,
@"ムシュファは、誰よりも多くの財を抱えて、最深から還った。
帰路、連れが力尽きかけていた。その懐に、上等な遺物があるのを、ムシュファは見ていた。
連れには、まだ息があった。——ムシュファが、手を下すまでは。
一人だけ肥え太って還った彼を、やがて、生き残りの証言が追い詰めた。
裁きは流刑——奪った財をすべて没収され、補給も供もなく、再び大穴へ落とされた。" },

            { Wrath,
@"コルヴェンの怒りに、理由はいらなかった。
ヴェイランドの酒場で、男が一人、彼の肩に、軽くぶつかった。それだけだった。
気づけば、店は血の海だった。その男も、止めに入った者も、たまたま居合わせた客も。コルヴェンは、返り血の中で、ようやく我に返った。
戦いでも同じだ。敵を見れば、最初の一撃にすべてを込め、相手を——そして自分の身をも——砕いた。手加減という言葉を、彼は持たなかった。
数えきれぬ骸を残し、裁きは流刑——幾重にも縄を打たれ、補給も供もなく、再び大穴へ落とされた。" },

            { Sloth,
@"クェシナはストレリツ公国辺境警備隊の隊長だった。
任地はヴィルヘル国境の、霧深い湿地帯。部下を十四人、預かっていた。

その夜、霧はいつもより深かった。
彼女は見回りの巡路を、いつものように指示した。物見台、林の縁、東の岩場。手順は書類どおり。
第一巡は自分で回った。何も見つからなかった。霧は、深いだけだった。

第二巡を副長に任せて、彼女は床に入った。これも規則どおり。隊長は第二巡を回らない。

副長は戻ってこなかった。

朝、霧の引いた野営の中で、隊は一人残らず死んでいた。
死因は誰にもわからなかった。獣の傷でもなく、刃の傷でもない。ただ皆、見開いた目をしていた。
クェシナだけが生きていた。床の中で起こされて、それを見た。

「お前が警鐘を鳴らさなかったからだ」と軍法は言った。
「規則どおりの当番だった」と彼女は答えた。
「規則どおりだったから、お前が罰せられる。隊長は結果に責任を負う」

副長の死亡時刻は、第二巡の途中だった。クェシナが床にあった時刻だ。
それでも判決は変わらなかった。上の都合で、誰かが背負わねばならぬ夜だった。

裁きは流刑——補給も供もなく、彼女は大穴へ落とされた。
霧の出る湿地で死んだ十四人の代わりに。

——後年、大罪の名簿に「怠惰」の罪人として、彼女の名が刻まれた。
名簿を作った者は、彼女が床にあった時刻のことを、知らなかった。" },

            { Gluttony,
@"トゥルハドは、隊の兵糧を任された男だった。
全員の、十日分。彼はそれを、夜ごとこっそり、三日で喰い尽くした。
残ったのは、空の樽と、飢えた仲間と、引き返せない深さ。
「——足りない。まだ、足りないんだ」。倒れていく仲間の前で、彼は呻いた。
飢えた彼が、その後なにを口にして生き延びたのかは——還った者の口が、堅く閉ざしている。
預かった命を喰った罪で、裁きは流刑——補給も供もなく、再び大穴へ落とされた。" },

            { Lust,
@"クァディルは、欲しいと思ったものから、目を離せなかった。
隊の、最後の水と食糧と、帰り道の地図。それは皆の命綱で、彼一人のものではなかった。
だが、行商の美しい遺物を見た瞬間、彼はそのすべてを、勝手に、交換してしまった。
「これが要る。これだけが、要る」
道を失い渇いた隊は、半ばで全滅しかけた。クァディルだけが、遺物を抱きしめ、生きて這い戻った。
仲間の命綱を売り払った罪で、裁きは流刑——遺物は没収され、補給も供もなく、再び大穴へ落とされた。" },
        };

        /// <summary>codex / 詳細表示用の大罪フレーバー本文 (改行入り)。</summary>
        public static string GetFlavorText(string id)
            => id != null && flavorTexts.TryGetValue(id, out var t) ? t : "";

        /// <summary>定義済みの全大罪 ID。</summary>
        public static readonly string[] AllIds =
        {
            Pride, Envy, Greed, Wrath, Sloth, Gluttony, Lust,
        };
    }

    /// <summary>恒久デバフ判定の薄いヘルパー。</summary>
    public static class PermanentDebuffEffects
    {
        public static bool Has(RunState run, string id)
            => run != null && run.permanentDebuffs != null && run.permanentDebuffs.Contains(id);

        public static bool HasPride(RunState run)    => Has(run, PermanentDebuffIds.Pride);
        public static bool HasEnvy(RunState run)     => Has(run, PermanentDebuffIds.Envy);
        public static bool HasGreed(RunState run)    => Has(run, PermanentDebuffIds.Greed);
        public static bool HasWrath(RunState run)    => Has(run, PermanentDebuffIds.Wrath);
        public static bool HasSloth(RunState run)    => Has(run, PermanentDebuffIds.Sloth);
        public static bool HasGluttony(RunState run) => Has(run, PermanentDebuffIds.Gluttony);
        public static bool HasLust(RunState run)     => Has(run, PermanentDebuffIds.Lust);
    }
}
