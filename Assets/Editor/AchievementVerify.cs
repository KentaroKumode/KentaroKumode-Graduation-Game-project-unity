using System;
using System.Collections.Generic;
using MetaProgression;
using MetaProgression.Achievements;
using UnityEditor;
using UnityEngine;

/// <summary>実績の一覧の検査 (メニュー Tools/Verify/Achievements)。 数は固定しない (2026-11-17 の再編で 46 → 90)。
/// 見るもの: id の重複・欄の欠け・並び順・結末の番号 (END1〜5 が 1 個ずつ)・隠しでない実績の文の決まり (docs/GAME.md §15-6)・保存の既定値・BOT の間は解除しないこと。</summary>
public static class AchievementVerify
{
    /// <summary>隠しでない実績の名前と文に出さない語 (4 層・5 層の番号、 深い所のボスと結末の名前、 結末に関わる品)。</summary>
    private static readonly string[] SpoilerWords =
    {
        "4層", "5層", "灰燼", "ヴェスカ", "英雄の帰還", "無明の剣", "灰の玉座", "帰路", "終始",
        "根拠のない確信", "決意", "真理", "止まらない時計", "裂け目",
    };

    /// <summary>どの実績の文にも使わない語 (§2-7 の禁止語彙・旧い語)。</summary>
    private static readonly string[] BannedWords = { "ダイス合計", "最終層", "再抽選", "逃亡", "デバフ", "七つの大罪", "パッシブ" };

    [MenuItem("Tools/Verify/Achievements")]
    public static void Run()
    {
        var all = AchievementCatalog.All;
        Require(all.Count > 0, "catalog is empty");
        var ids = new HashSet<string>();
        var endings = new HashSet<int>();
        for (int i = 0; i < all.Count; i++)
        {
            var d = all[i];
            Require(d != null && !string.IsNullOrEmpty(d.id), $"empty definition at {i}");
            Require(ids.Add(d.id), $"duplicate id: {d.id}");
            Require(AchievementCatalog.Get(d.id) == d, $"index mismatch: {d.id}");
            Require(!string.IsNullOrEmpty(d.displayName) && !string.IsNullOrEmpty(d.description), $"name or description missing: {d.id}");
            Require(d.order == i, $"order {d.order} != position {i}: {d.id}");
            Require(Enum.IsDefined(typeof(AchievementCategory), d.category), $"bad category: {d.id}");
            Require(d.stars >= 1 && d.stars <= 5, $"stars out of range: {d.id}");
            if (d.endingNo != 0)
            {
                Require(d.endingNo >= 1 && d.endingNo <= 5, $"ending number out of range: {d.id}");
                Require(endings.Add(d.endingNo), $"duplicate ending number END{d.endingNo}: {d.id}");
            }
            foreach (var w in BannedWords)
                Require(!d.displayName.Contains(w) && !d.description.Contains(w), $"banned word '{w}' in {d.id}");
            if (!d.hidden)
                foreach (var w in SpoilerWords)
                    Require(!d.displayName.Contains(w) && !d.description.Contains(w), $"spoiler '{w}' in non-hidden {d.id}");
        }
        for (int n = 1; n <= 5; n++) Require(endings.Contains(n), $"no achievement for END{n}");
        // 系統の中は並び順どおり (InCategory が一覧の順で返す)
        foreach (var c in AchievementCatalog.Categories)
        {
            var l = AchievementCatalog.InCategory(c);
            Require(l.Count > 0, $"empty category: {c}");
            for (int i = 1; i < l.Count; i++) Require(l[i - 1].order < l[i].order, $"category order broken: {c}");
            Require(!string.IsNullOrEmpty(AchievementCatalog.CategoryName(c)), $"category name missing: {c}");
        }

        var state = new MetaProgressState();
        Require(state.unlockedAchievementIds != null, "unlock ID list not initialized");
        Require(state.achievementUnlockUtcTicks != null, "unlock timestamp list not initialized");

        bool old = MetaBuffApplicator.SuppressRelicGrant;
        try
        {
            MetaBuffApplicator.SuppressRelicGrant = true;
            Require(!AchievementService.Unlock(AchievementCatalog.FirstRun), "AutoRunner suppression failed");
        }
        finally { MetaBuffApplicator.SuppressRelicGrant = old; }

        Debug.Log($"[AchievementVerify] PASS: {all.Count} definitions, {AchievementCatalog.Categories.Count} categories, unique IDs, END1-5, wording rules, save defaults, AutoRunner guard");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[AchievementVerify] " + message);
    }
}
