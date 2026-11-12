using System;

namespace AutoTest.Ultra
{
    /// <summary>並列ワーカーの凍結ビルド (UltraProductionWorkerBuild) の descriptor に書く指紋の組。
    ///
    /// <para>名前の Ultra は旧い技量 Ultra (2026-11-13 削除・#135) の名残。 descriptor の形
    /// (ultra-production-worker.json) を変えずに読めるよう、 型名とフィールドはそのまま残す。
    /// いまの測定 (RankMargin / ItemLearning の並列ランナー) が照合に使うのは <see cref="buildFingerprint"/> だけで、
    /// 他の 4 つは書式 (小文字 16 進 64 桁) の検査だけを通る。</para></summary>
    [Serializable]
    public sealed class UltraFingerprintSet
    {
        public string buildFingerprint;
        public string dataFingerprint;
        public string checkpointFingerprint;
        /// <summary>旧: Ultra の方策の組の候補集合のハッシュ。 2026-11-13 からは固定の印のハッシュ
        /// (UltraProductionWorkerBuild.ActionFingerprintTag)。 照合する側は無い。</summary>
        public string actionFingerprint;
        public string objectiveFingerprint;

        public UltraFingerprintSet Clone()
        {
            return new UltraFingerprintSet
            {
                buildFingerprint = buildFingerprint,
                dataFingerprint = dataFingerprint,
                checkpointFingerprint = checkpointFingerprint,
                actionFingerprint = actionFingerprint,
                objectiveFingerprint = objectiveFingerprint,
            };
        }
    }

    /// <summary>descriptor を読む時の指紋の書式の検査 (UltraProductionWorkerBuild.TryLoadDescriptor が通す)。
    /// 旧 Ultra のワーカーとの取り決め (build manifest の照合・handshake) は 2026-11-13 に削除した (#135)。</summary>
    public static class UltraBuildManifestValidation
    {
        public const int Sha256HexChars = 64;

        public static bool TryValidateFingerprints(
            UltraFingerprintSet fingerprints,
            out string failureCode,
            out string failureDetail)
        {
            failureCode = null;
            failureDetail = null;
            if (fingerprints == null)
                return Fail("fingerprints_missing", "fingerprint set is null", out failureCode, out failureDetail);
            if (!IsFingerprint(fingerprints.buildFingerprint))
                return Fail("build_fingerprint", "build fingerprint is missing or invalid", out failureCode, out failureDetail);
            if (!IsFingerprint(fingerprints.dataFingerprint))
                return Fail("data_fingerprint", "data fingerprint is missing or invalid", out failureCode, out failureDetail);
            if (!IsFingerprint(fingerprints.checkpointFingerprint))
                return Fail("checkpoint_fingerprint", "checkpoint fingerprint is missing or invalid", out failureCode, out failureDetail);
            if (!IsFingerprint(fingerprints.actionFingerprint))
                return Fail("action_fingerprint", "action fingerprint is missing or invalid", out failureCode, out failureDetail);
            if (!IsFingerprint(fingerprints.objectiveFingerprint))
                return Fail("objective_fingerprint", "objective fingerprint is missing or invalid", out failureCode, out failureDetail);
            return true;
        }

        private static bool IsFingerprint(string value)
        {
            // One canonical representation prevents case-only identities from being treated
            // as different builds by ordinal comparison.
            if (value == null || value.Length != Sha256HexChars) return false;
            for (int i = 0; i < value.Length; i++)
            {
                char ch = value[i];
                if (!((ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f'))) return false;
            }
            return true;
        }

        private static bool Fail(
            string code,
            string detail,
            out string failureCode,
            out string failureDetail)
        {
            failureCode = code;
            failureDetail = detail;
            return false;
        }
    }
}
