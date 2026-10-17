using System;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeModeSaveDiskReceipt
    {
        [Serializable] public sealed class NumberBits
        {
            public string recordId;
            public long best;
            public long[] components;
        }
        [Serializable]
        public sealed class Receipt
        {
            public string filePath, bytesHash;
            public long generation, memoryGeneration;
            public NativeSaveData.TimeTrialRecord[] timeTrials;
            public NativeSaveData.CupRecord[] cups;
            public NumberBits[] timeTrialBits, cupBits;
            public bool diskMatchesMemory, reloadSucceeded, reloadPreservedReceipt;
        }

        public static bool IsolatedExactDirectory(string expectedOwnedDirectory, string[] args, out string error)
        {
            error = "";
            try
            {
                if (string.IsNullOrWhiteSpace(expectedOwnedDirectory)) throw new ArgumentException("Owned audit directory missing");
                string explicitDirectory = null; int count = 0;
                for (int i = 0; args != null && i < args.Length; i++)
                {
                    string value = args[i];
                    if (value == "--import-save-exchange" || value.StartsWith("--import-save-exchange=", StringComparison.Ordinal))
                        throw new InvalidOperationException("Save import is outside the isolated mode audit");
                    if (value == "--save-directory")
                    {
                        if (++i >= args.Length) throw new ArgumentException("Explicit save directory value missing");
                        explicitDirectory = args[i]; count++;
                    }
                    else if (value.StartsWith("--save-directory=", StringComparison.Ordinal))
                    { explicitDirectory = value.Substring(17); count++; }
                }
                if (count != 1 || string.IsNullOrWhiteSpace(explicitDirectory))
                    throw new InvalidOperationException("Exactly one explicit save directory is required");
                string expected = Path.GetFullPath(expectedOwnedDirectory).TrimEnd(Path.DirectorySeparatorChar);
                string actual = Path.GetFullPath(explicitDirectory).TrimEnd(Path.DirectorySeparatorChar);
                if (!string.Equals(expected, actual, StringComparison.Ordinal))
                    throw new InvalidOperationException("CLI directory differs from owned audit directory");
                string personal = Path.GetFullPath(Application.persistentDataPath).TrimEnd(Path.DirectorySeparatorChar);
                if (string.Equals(expected, personal, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Personal save directory is outside the audit");
                if (!Directory.Exists(expected)) throw new InvalidOperationException("Owned audit directory must be prepared before Player startup");
                for (var node = new DirectoryInfo(expected); node != null; node = node.Parent)
                    if ((node.Attributes & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidOperationException("Audit directory ancestor is a symbolic link");
                string expectedFile = Path.Combine(expected, "native-progress.json");
                if (!string.Equals(NativeSaveStore.FilePath, expectedFile, StringComparison.Ordinal))
                    throw new InvalidOperationException("Active save store differs from owned audit directory");
                if (File.Exists(expectedFile) && (File.GetAttributes(expectedFile) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("Audit primary is a symbolic link");
                return true;
            }
            catch (Exception exception) { error = exception.Message; return false; }
        }

        public static Receipt Capture(string expectedOwnedDirectory, string[] args)
        {
            if (!IsolatedExactDirectory(expectedOwnedDirectory, args, out string error)) throw new InvalidOperationException(error);
            string path = NativeSaveStore.FilePath;
            byte[] bytes = File.ReadAllBytes(path);
            var disk = NativeSaveJson.FromJson<NativeSaveData>(System.Text.Encoding.UTF8.GetString(bytes));
            if (!NativeSaveStore.Validate(disk, out error)) throw new InvalidOperationException("Invalid audit primary: " + error);
            string hash;
            using (var algorithm = SHA256.Create()) hash = BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
            var memory = NativeSaveStore.Data;
            return new Receipt { filePath = path, bytesHash = hash, generation = disk.generation,
                memoryGeneration = memory.generation, timeTrials = disk.timeTrialRecords, cups = disk.cupRecords,
                timeTrialBits = TrialBits(disk.timeTrialRecords), cupBits = CupBits(disk.cupRecords),
                diskMatchesMemory = disk.generation == memory.generation && RecordsEqual(disk.timeTrialRecords, memory.timeTrialRecords,
                    disk.cupRecords, memory.cupRecords) };
        }

        public static bool VerifyUnchanged(Receipt before, Receipt after)
        {
            return before != null && after != null && before.diskMatchesMemory && after.diskMatchesMemory
                && before.filePath == after.filePath && before.bytesHash == after.bytesHash
                && before.generation == after.generation && before.memoryGeneration == after.memoryGeneration
                && RecordsEqual(before.timeTrials, after.timeTrials, before.cups, after.cups);
        }

        public static Receipt ReloadAndVerify(Receipt before, string expectedOwnedDirectory, string[] args)
        {
            if (!IsolatedExactDirectory(expectedOwnedDirectory, args, out string error)) throw new InvalidOperationException(error);
            if (before == null || before.filePath != NativeSaveStore.FilePath) throw new InvalidOperationException("Reload receipt path differs");
            NativeSaveStore.ResetForVerification();
            bool loaded = NativeSaveStore.Initialize(new[] { "--save-directory", expectedOwnedDirectory });
            if (!loaded) throw new InvalidOperationException("Final audit reload failed: " + NativeSaveStore.LastError);
            var after = Capture(expectedOwnedDirectory, args);
            after.reloadSucceeded = loaded;
            after.reloadPreservedReceipt = VerifyUnchanged(before, after);
            return after;
        }

        static NumberBits[] TrialBits(NativeSaveData.TimeTrialRecord[] records)
        {
            var result = new NumberBits[records.Length];
            for (int i = 0; i < records.Length; i++) result[i] = new NumberBits {
                recordId = records[i].trackId, best = BitConverter.DoubleToInt64Bits(records[i].bestSeconds),
                components = Array.ConvertAll(records[i].lapSeconds, BitConverter.DoubleToInt64Bits) };
            return result;
        }
        static NumberBits[] CupBits(NativeSaveData.CupRecord[] records)
        {
            var result = new NumberBits[records.Length];
            for (int i = 0; i < records.Length; i++) result[i] = new NumberBits {
                recordId = records[i].cupId, best = BitConverter.DoubleToInt64Bits(records[i].bestSeconds),
                components = Array.ConvertAll(records[i].stationSeconds, BitConverter.DoubleToInt64Bits) };
            return result;
        }

        static bool RecordsEqual(NativeSaveData.TimeTrialRecord[] leftTT, NativeSaveData.TimeTrialRecord[] rightTT,
            NativeSaveData.CupRecord[] leftGP, NativeSaveData.CupRecord[] rightGP)
        {
            if (leftTT == null || rightTT == null || leftGP == null || rightGP == null
                || leftTT.Length != rightTT.Length || leftGP.Length != rightGP.Length) return false;
            for (int i = 0; i < leftTT.Length; i++)
            {
                if (leftTT[i] == null || rightTT[i] == null || JsonUtility.ToJson(leftTT[i]) != JsonUtility.ToJson(rightTT[i])
                    || !Bits(leftTT[i].bestSeconds, rightTT[i].bestSeconds) || !Bits(leftTT[i].lapSeconds, rightTT[i].lapSeconds)) return false;
            }
            for (int i = 0; i < leftGP.Length; i++)
            {
                if (leftGP[i] == null || rightGP[i] == null || JsonUtility.ToJson(leftGP[i]) != JsonUtility.ToJson(rightGP[i])
                    || !Bits(leftGP[i].bestSeconds, rightGP[i].bestSeconds) || !Bits(leftGP[i].stationSeconds, rightGP[i].stationSeconds)) return false;
            }
            return true;
        }
        static bool Bits(double left, double right) => BitConverter.DoubleToInt64Bits(left) == BitConverter.DoubleToInt64Bits(right);
        static bool Bits(double[] left, double[] right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; i++) if (!Bits(left[i], right[i])) return false;
            return true;
        }
    }
}
