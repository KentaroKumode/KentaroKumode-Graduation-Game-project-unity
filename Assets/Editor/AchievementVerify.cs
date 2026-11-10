using System;
using System.Collections.Generic;
using MetaProgression;
using MetaProgression.Achievements;
using UnityEditor;
using UnityEngine;

public static class AchievementVerify
{
    [MenuItem("Tools/Verify/Achievements")]
    public static void Run()
    {
        Require(AchievementCatalog.All.Count == 46, $"catalog count {AchievementCatalog.All.Count} != 46");
        var ids = new HashSet<string>();
        for (int i = 0; i < AchievementCatalog.All.Count; i++)
        {
            var d = AchievementCatalog.All[i];
            Require(d != null && !string.IsNullOrEmpty(d.id), $"empty definition at {i}");
            Require(ids.Add(d.id), $"duplicate id: {d.id}");
            Require(AchievementCatalog.Get(d.id) == d, $"index mismatch: {d.id}");
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

        Debug.Log("[AchievementVerify] PASS: 46 definitions, unique IDs, save defaults, AutoRunner guard");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[AchievementVerify] " + message);
    }
}
