// Development only. Excluded from the PowerShell launcher and package allow-list.
namespace Tobtech.FirmwareLab
{
    using System;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text.RegularExpressions;

    internal sealed class FirmwareFailure : Exception
    {
        internal readonly string Code;
        internal FirmwareFailure(string code) : base(code) { Code = code; }
    }
    internal sealed class FirmwareIdentity
    {
        internal readonly byte Board;
        internal readonly string Version, Id;
        internal FirmwareIdentity(byte board, string version, string id) { Board = board; Version = version; Id = id; }
        internal static bool Hex(string text, int count)
        { return text != null && Regex.IsMatch(text, "\\A[0-9A-F]{" + count + "}\\z"); }
        internal bool Valid { get { return (Board == 6 || Board == 10) && Hex(Version, 8) && Hex(Id, 32); } }
        internal bool Same(FirmwareIdentity other)
        { return other != null && Valid && other.Valid && Board == other.Board && Version == other.Version && Id == other.Id; }
    }
    internal sealed class FirmwareApproval
    {
        internal readonly byte Board;
        internal readonly string To, Sha256, Source, RecoveryEvidence;
        private readonly string[] from;
        internal readonly int Size;
        internal FirmwareApproval(byte board, string[] fromVersions, string to, string hash, int size, string source, string recoveryEvidence)
        {
            Board = board; from = (string[])fromVersions.Clone(); To = to; Sha256 = hash; Size = size;
            Source = source; RecoveryEvidence = recoveryEvidence;
        }
        internal bool Same(FirmwareApproval other)
        {
            return other != null && Board == other.Board && from.SequenceEqual(other.from) && To == other.To &&
                Sha256 == other.Sha256 && Size == other.Size && Source == other.Source && RecoveryEvidence == other.RecoveryEvidence;
        }
        internal void Validate(FirmwareIdentity identity, byte[] data)
        {
            Uri url;
            if (identity == null || !identity.Valid || Board != identity.Board || !from.Contains(identity.Version) ||
                from.Length == 0 || from.Distinct().Count() != from.Length || from.Any(delegate(string v) { return !FirmwareIdentity.Hex(v, 8); }) ||
                !FirmwareIdentity.Hex(To, 8) || String.CompareOrdinal(identity.Version, To) >= 0 ||
                !FirmwareIdentity.Hex(Sha256, 64) || Size < 1 || Size > 16777216 || data == null || data.Length != Size ||
                String.IsNullOrEmpty(RecoveryEvidence) || System.Text.Encoding.UTF8.GetByteCount(RecoveryEvidence) > 200 ||
                !Uri.TryCreate(Source, UriKind.Absolute, out url) || url.Scheme != "https" ||
                (url.Host != "www.hidglobal.com" && url.Host != "sdk.hidglobal.com") || !url.IsDefaultPort ||
                url.UserInfo.Length != 0 || url.Query.Length != 0 || url.Fragment.Length != 0 ||
                !url.AbsolutePath.EndsWith(".fw", StringComparison.Ordinal) || Source.Contains("%") || Source.Contains("\\") ||
                Source.Contains("/./") || Source.Contains("/../") || url.AbsoluteUri != Source)
                throw new FirmwareFailure("package");
            using (SHA256 hash = SHA256.Create())
                if (BitConverter.ToString(hash.ComputeHash(data)).Replace("-", "") != Sha256) throw new FirmwareFailure("package");
        }
    }
    internal enum FirmwareStage { Prepared, Transferring, CommitRequested, Verifying, Completed }
    internal interface IFirmwareLabSession
    {
        // Must own one exclusive, pinned physical session; no silent rebinding.
        FirmwareIdentity Identity();
        bool HasPendingJournal();
        // Export and durably verify a full encrypted configuration, then create the journal.
        // This is a required adapter contract. No production adapter exists yet.
        void BackupAndCreateJournal(FirmwareIdentity identity, FirmwareApproval approval);
        void Persist(FirmwareStage stage);
        byte[] Exchange(byte[] command);
        FirmwareIdentity ReconnectToSameReader();
        void RestoreAndVerifyProtectedOutput();
    }
    internal static class FirmwareApdu
    {
        internal static byte[] Start() { return new byte[] { 255, 104, 10, 0, 4, 0, 0, 0, 0 }; }
        internal static byte[] Finish() { return new byte[] { 255, 104, 10, 2, 0 }; }
        internal static byte[] Result() { return new byte[] { 255, 104, 10, 12, 0 }; }
        internal static byte[] Chunk(byte[] bytes, int offset, int count)
        {
            if (bytes == null || count < 1 || count > 255 || offset < 0 || offset > bytes.Length - count) throw new FirmwareFailure("package");
            byte[] result = new byte[count + 5];
            Array.Copy(new byte[] { 255, 104, 10, 1, (byte)count }, result, 5);
            Array.Copy(bytes, offset, result, 5, count); return result;
        }
        internal static void Success(byte[] reply, bool result)
        {
            byte[] wanted = result ? new byte[] { 0, 144, 0 } : new byte[] { 144, 0 };
            if (reply == null || !reply.SequenceEqual(wanted)) throw new FirmwareFailure("protocolReply");
        }
    }
    internal sealed class FirmwareLabUpdater
    {
        // Approvals must eventually come from the app-authenticated catalog, never a local plan.
        private readonly FirmwareApproval[] approved;
        private int used;
        internal FirmwareLabUpdater(FirmwareApproval[] approvals) { approved = (FirmwareApproval[])approvals.Clone(); }
        internal void Run(FirmwareApproval offer, byte[] input, IFirmwareLabSession session, Func<bool> cancelled, Action<int> progress)
        {
            if (System.Threading.Interlocked.Exchange(ref used, 1) != 0) throw new FirmwareFailure("reused");
            if (offer == null || approved.Count(delegate(FirmwareApproval p) { return p != null && p.Same(offer); }) != 1) throw new FirmwareFailure("approval");
            // Snapshot caller-owned bytes before validation; later mutations cannot alter a chunk.
            byte[] data = input == null ? null : (byte[])input.Clone();
            FirmwareIdentity before = session.Identity(); offer.Validate(before, data);
            if (session.HasPendingJournal()) throw new FirmwareFailure("journal");
            if (cancelled()) throw new FirmwareFailure("cancelled");
            Notify(progress, 0); session.BackupAndCreateJournal(before, offer);
            if (!before.Same(session.Identity())) throw new FirmwareFailure("identity");
            if (cancelled()) throw new FirmwareFailure("cancelled");
            session.Persist(FirmwareStage.Transferring);
            // No cancellation, automatic retries or independent MIB calls after Start.
            FirmwareApdu.Success(session.Exchange(FirmwareApdu.Start()), false);
            for (int offset = 0; offset < data.Length; offset += 255)
            {
                int count = Math.Min(255, data.Length - offset);
                FirmwareApdu.Success(session.Exchange(FirmwareApdu.Chunk(data, offset, count)), false);
                Notify(progress, Math.Min(89, (offset + count) * 90 / data.Length));
            }
            session.Persist(FirmwareStage.CommitRequested);
            try { FirmwareApdu.Success(session.Exchange(FirmwareApdu.Finish()), false); }
            catch (FirmwareFailure ex) { if (ex.Code != "uncertainCommit") throw; }
            session.Persist(FirmwareStage.Verifying);
            FirmwareIdentity after = session.ReconnectToSameReader();
            if (after == null || !after.Valid || after.Id != before.Id || after.Board != before.Board || after.Version != offer.To) throw new FirmwareFailure("identity");
            Notify(progress, 95); FirmwareApdu.Success(session.Exchange(FirmwareApdu.Result()), true);
            session.RestoreAndVerifyProtectedOutput(); session.Persist(FirmwareStage.Completed); Notify(progress, 100);
            // No firmware rollback, journal deletion, or automatic calibration re-labelling.
        }
        private static void Notify(Action<int> progress, int percent)
        {
            // An exception in a UI observer must not interrupt a firmware transfer.
            try { progress(percent); } catch { }
        }
    }
}
