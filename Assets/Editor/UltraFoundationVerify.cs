#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using AutoTest.Ultra;
using UnityEditor;
using UnityEngine;

public static class UltraFoundationVerify
{
    [MenuItem("Tools/AutoRun/診断: Ultra 基盤契約", priority = 9)]
    public static void Run()
    {
        try
        {
            string rngBefore = CaptureLiveRngState();
            VerifyScenarioBank();
            VerifyDecisionEngine();
            string rngAfter = CaptureLiveRngState();
            Require(rngBefore == rngAfter, "Ultra foundation touched live GameRng state");
            Debug.Log("[UltraVerify] PASS: shared scenarios / order invariance / shadow fallback / live GameRng non-interference");
        }
        catch (Exception ex)
        {
            Debug.LogError("[UltraVerify] FAIL: " + ex);
            throw;
        }
    }

    private static void VerifyScenarioBank()
    {
        var bank = new UltraScenarioBank(0x0123456789ABCDEFUL);
        for (int i = 0; i < 1024; i++)
        {
            ulong a = bank.GetScenarioSeed(0xAABBCCDDUL, 7, 11, UltraScenarioPhase.Search, i);
            ulong b = bank.GetScenarioSeed(0xAABBCCDDUL, 7, 11, UltraScenarioPhase.Search, i);
            ulong confirm = bank.GetScenarioSeed(0xAABBCCDDUL, 7, 11, UltraScenarioPhase.Confirmation, i);
            Require(a == b, "scenario bank is not deterministic");
            Require(a != confirm, "search and confirmation domains overlap");
        }
    }

    private static void VerifyDecisionEngine()
    {
        const string fingerprint = "ultra-verify-build";
        var oracleA = new RecordingOracle(fingerprint);
        var shadow = new UltraDecisionEngine(
            new UltraDecisionConfig
            {
                mode = UltraExecutionMode.Shadow,
                plannerSalt = 0x1111222233334444UL,
                searchScenariosPerCandidate = 8,
                confirmationScenarios = 64,
            },
            oracleA);

        UltraDecisionRequest forward = MakeRequest(fingerprint, false);
        UltraDecisionResult shadowResult = shadow.Decide(forward);
        Require(shadowResult.selectedActionId == "baseline", "shadow mode did not execute baseline");
        Require(shadowResult.proposedActionId == "better", "shadow mode did not measure proposal");
        Require(oracleA.CommonScenarioSeedsMatch(), "candidates did not receive common scenario seeds");

        var oracleB = new RecordingOracle(fingerprint);
        var enabled = new UltraDecisionEngine(
            new UltraDecisionConfig
            {
                mode = UltraExecutionMode.Enabled,
                plannerSalt = 0x1111222233334444UL,
                searchScenariosPerCandidate = 8,
                confirmationScenarios = 64,
                confirmationAlpha = 0.01,
            },
            oracleB);
        UltraDecisionResult enabledResult = enabled.Decide(MakeRequest(fingerprint, true));
        Require(enabledResult.selectedActionId == "better", "enabled mode rejected deterministic dominant proposal");
        Require(enabledResult.fallbackReason == UltraFallbackReason.None, "enabled mode reported fallback");

        Require(oracleA.SearchTrace == oracleB.SearchTrace,
            "candidate enumeration order changed the deterministic search trace");
    }

    private static UltraDecisionRequest MakeRequest(string fingerprint, bool reverse)
    {
        var request = new UltraDecisionRequest
        {
            runOrdinal = 42,
            decisionOrdinal = 3,
            publicStateHash = 0xCAFEBABEUL,
            buildFingerprint = fingerprint,
            checkpointSchema = "verify-v1",
            publicCheckpointBase64 = "AA==",
            baselineActionId = "baseline",
        };
        if (reverse)
        {
            request.candidates.Add(new UltraCandidate("better"));
            request.candidates.Add(new UltraCandidate("baseline"));
        }
        else
        {
            request.candidates.Add(new UltraCandidate("baseline"));
            request.candidates.Add(new UltraCandidate("better"));
        }
        return request;
    }

    private static string CaptureLiveRngState()
    {
        Type type = typeof(GameLoop.GameRng);
        BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var saltField = type.GetField("_runSalt", flags);
        var countersField = type.GetField("_counters", flags);
        Require(saltField != null && countersField != null, "GameRng diagnostic fields not found");

        ulong runSalt = (ulong)saltField.GetValue(null);
        var counters = countersField.GetValue(null) as IDictionary<string, int>;
        Require(counters != null, "GameRng counters unavailable");

        var keys = new List<string>(counters.Keys);
        keys.Sort(StringComparer.Ordinal);
        var sb = new StringBuilder();
        sb.Append(GameLoop.GameRng.IsSeeded ? '1' : '0').Append('|')
          .Append(GameLoop.GameRng.MasterSeed).Append('|')
          .Append(GameLoop.GameRng.RunIndex).Append('|')
          .Append(runSalt);
        for (int i = 0; i < keys.Count; i++)
            sb.Append('|').Append(keys[i]).Append('=').Append(counters[keys[i]]);
        return sb.ToString();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class RecordingOracle : IUltraProductionOracle
    {
        private readonly Dictionary<string, List<string>> _searchSeeds
            = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        private readonly StringBuilder _searchTrace = new StringBuilder();

        public RecordingOracle(string fingerprint) { BuildFingerprint = fingerprint; }
        public bool IsHealthy => true;
        public string BuildFingerprint { get; }
        public string SearchTrace => _searchTrace.ToString();

        public bool TryRunEpisode(UltraEpisodeRequest request, out UltraEpisodeResult result)
        {
            bool isSearch = request.jobId.Contains(":search:");
            if (isSearch)
            {
                if (!_searchSeeds.TryGetValue(request.actionId, out List<string> seeds))
                {
                    seeds = new List<string>();
                    _searchSeeds.Add(request.actionId, seeds);
                }
                seeds.Add(request.scenarioStartSeedHex);
                _searchTrace.Append(request.actionId).Append('@').Append(request.scenarioStartSeedHex).Append(';');
            }

            result = new UltraEpisodeResult
            {
                success = true,
                reachedTerminal = true,
                primaryReward = request.actionId == "better" ? 1f : 0f,
                finalPublicStateHash = "verify",
                productionSteps = 1,
            };
            return true;
        }

        public bool CommonScenarioSeedsMatch()
        {
            if (!_searchSeeds.TryGetValue("baseline", out List<string> baseline)) return false;
            if (!_searchSeeds.TryGetValue("better", out List<string> better)) return false;
            if (baseline.Count != better.Count) return false;
            for (int i = 0; i < baseline.Count; i++)
                if (!string.Equals(baseline[i], better[i], StringComparison.Ordinal)) return false;
            return true;
        }
    }
}
#endif
