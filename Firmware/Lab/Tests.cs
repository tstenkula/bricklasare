namespace Tobtech.FirmwareLab
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Security.Cryptography;

    internal sealed class Fake : IFirmwareLabSession
    {
        internal string FailAt = "";
        internal readonly List<string> Events = new List<string>();
        internal readonly List<byte[]> Commands = new List<byte[]>();
        internal bool Pending, Swapped, BadResult, BadAck, DisconnectOnCommit;
        internal FirmwareStage? Stage;
        internal FirmwareIdentity NextIdentity = FirmwareLabTests.After;
        internal Action OnBackup;
        private void Step(string name)
        {
            Events.Add(name); if (FailAt == name) throw new FirmwareFailure("injectedFailure");
        }
        public FirmwareIdentity Identity()
        {
            Step("identity"); return Swapped && Pending ? new FirmwareIdentity(6, FirmwareLabTests.Before.Version, new string('B', 32)) : FirmwareLabTests.Before;
        }
        public bool HasPendingJournal() { Step("pending"); return Pending; }
        public void BackupAndCreateJournal(FirmwareIdentity identity, FirmwareApproval approval)
        {
            Step("backup"); Pending = true; Stage = FirmwareStage.Prepared; if (OnBackup != null) OnBackup();
        }
        public void Persist(FirmwareStage stage)
        {
            Step("persist-" + stage); if (!Pending) throw new FirmwareFailure("journal"); Stage = stage;
        }
        public byte[] Exchange(byte[] command)
        {
            Commands.Add((byte[])command.Clone()); if (!Pending) throw new FirmwareFailure("journal");
            if (command.SequenceEqual(FirmwareApdu.Start()))
            {
                if (Stage != FirmwareStage.Transferring) throw new FirmwareFailure("journal"); Step("start");
            }
            else if (command.SequenceEqual(FirmwareApdu.Finish()))
            {
                if (Stage != FirmwareStage.CommitRequested) throw new FirmwareFailure("journal"); Step("finish");
                if (DisconnectOnCommit) throw new FirmwareFailure("uncertainCommit");
            }
            else if (command.SequenceEqual(FirmwareApdu.Result()))
            {
                Step("result"); return new byte[] { BadResult ? (byte)1 : (byte)0, 144, 0 };
            }
            else
            {
                if (command.Length < 6 || !command.Take(4).SequenceEqual(new byte[] { 255, 104, 10, 1 }) ||
                    command[4] != command.Length - 5 || Stage != FirmwareStage.Transferring) throw new FirmwareFailure("protocolReply");
                Step("chunk-" + (Commands.Count - 1));
            }
            return BadAck ? new byte[] { 144, 0, 0 } : new byte[] { 144, 0 };
        }
        public FirmwareIdentity ReconnectToSameReader() { Step("reconnect"); return NextIdentity; }
        public void RestoreAndVerifyProtectedOutput() { Step("protect"); }
    }
    public static class FirmwareLabTests
    {
        private static readonly byte[] Data = Enumerable.Range(0, 512).Select(delegate(int n) { return (byte)(n % 256); }).ToArray();
        internal static readonly FirmwareIdentity Before = new FirmwareIdentity(6, "01000000", new string('A', 32));
        internal static readonly FirmwareIdentity After = new FirmwareIdentity(6, "01000001", Before.Id);
        private static FirmwareApproval Offer(byte board = 6, string[] from = null, string to = "01000001",
            string source = "https://www.hidglobal.com/synthetic-never-download.fw", string evidence = "synthetic-test-evidence")
        {
            string hash; using (SHA256 sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(Data)).Replace("-", "");
            return new FirmwareApproval(board, from ?? new string[] { Before.Version }, to, hash, Data.Length, source, evidence);
        }
        private static void Check(bool value, string reason) { if (!value) throw new Exception("FAIL: " + reason); }
        private static void Rejected(Action action)
        {
            try { action(); } catch (FirmwareFailure) { return; } throw new Exception("Expected rejection");
        }
        private static void Run(FirmwareApproval approval, Fake fake, byte[] bytes = null, Action<int> progress = null)
        {
            new FirmwareLabUpdater(new FirmwareApproval[] { approval }).Run(approval, bytes ?? Data, fake, delegate { return false; }, progress ?? delegate { });
        }
        public static int Run()
        {
            FirmwareApproval approved = Offer(); int groups = 0;
            {
                Fake fake = new Fake(); List<int> progress = new List<int>(); Run(approved, fake, progress: progress.Add);
                Check(fake.Stage == FirmwareStage.Completed && progress.Last() == 100 && progress.SequenceEqual(progress.OrderBy(delegate(int n) { return n; })), "Verified completion and monotonic progress");
                Check(fake.Events.Skip(fake.Events.Count - 3).SequenceEqual(new string[] { "result", "protect", "persist-Completed" }), "Final checks");
                byte[][] chunks = fake.Commands.Where(delegate(byte[] c) { return c.Length > 4 && c[3] == 1; }).ToArray();
                Check(chunks.Select(delegate(byte[] c) { return (int)c[4]; }).SequenceEqual(new int[] { 255, 255, 2 }), "Chunk framing");
                Check(chunks.SelectMany(delegate(byte[] c) { return c.Skip(5); }).SequenceEqual(Data), "Every byte exactly once"); groups++;
            }
            {
                Fake fake = new Fake();
                Rejected(delegate { new FirmwareLabUpdater(new FirmwareApproval[0]).Run(approved, Data, fake, delegate { return false; }, delegate { }); });
                Rejected(delegate { new FirmwareLabUpdater(new FirmwareApproval[] { approved, approved }).Run(approved, Data, fake, delegate { return false; }, delegate { }); });
                Check(fake.Events.Count == 0, "No unique approval must not contact reader"); groups++;
            }
            foreach (FirmwareApproval bad in new FirmwareApproval[] { Offer(board: 10), Offer(from: new string[] { "02000000" }),
                Offer(from: new string[] { Before.Version, Before.Version }), Offer(to: Before.Version), Offer(to: "00000000"), Offer(to: "not-hex!"), Offer(evidence: ""),
                Offer(source: "http://www.hidglobal.com/test.fw"), Offer(source: "https://www.hidglobal.com.evil.invalid/test.fw"),
                Offer(source: "https://www.hidglobal.com/test.wbo"), Offer(source: "https://www.hidglobal.com/test.cfg"),
                Offer(source: "https://www.hidglobal.com/test.fw?key=bad"), Offer(source: "https://www.hidglobal.com/%2e%2e/test.fw"),
                Offer(source: "https://user@www.hidglobal.com/test.fw") })
            {
                Fake fake = new Fake(); Rejected(delegate { Run(bad, fake); }); Check(fake.Commands.Count == 0 && !fake.Pending, "Invalid metadata before backup/write");
            }
            groups++;
            foreach (byte[] bad in new byte[][] { Data.Take(Data.Length - 1).ToArray(), new byte[Data.Length], Data.Concat(new byte[] { 0 }).ToArray() })
            {
                Fake fake = new Fake(); Rejected(delegate { Run(approved, fake, bad); }); Check(fake.Commands.Count == 0 && !fake.Pending, "Corrupt package writes nothing");
            }
            groups++;
            foreach (string point in new string[] { "backup", "persist-Transferring", "start", "chunk-1", "chunk-2", "chunk-3", "persist-CommitRequested", "finish", "persist-Verifying", "reconnect", "result", "protect", "persist-Completed" })
            {
                Fake fake = new Fake { FailAt = point }; List<int> progress = new List<int>(); Rejected(delegate { Run(approved, fake, progress: progress.Add); });
                Check(fake.Stage != FirmwareStage.Completed && !progress.Contains(100), "No false success at " + point);
                Check(fake.Events.Count(delegate(string name) { return name == point; }) == 1, "Never retry at " + point);
                if (point != "backup") Check(fake.Pending, "Retain journal at " + point);
                if (point == "backup" || point == "persist-Transferring") Check(fake.Commands.Count == 0, "Journal precedes Start");
                if (point.StartsWith("chunk-")) Check(!fake.Commands.Any(delegate(byte[] c) { return c.SequenceEqual(FirmwareApdu.Finish()); }), "Never commit partial transfer");
            }
            groups++;
            foreach (FirmwareIdentity wrong in new FirmwareIdentity[] { new FirmwareIdentity(10, After.Version, After.Id),
                new FirmwareIdentity(6, Before.Version, After.Id), new FirmwareIdentity(6, After.Version, new string('B', 32)) })
            {
                Fake fake = new Fake { NextIdentity = wrong }; Rejected(delegate { Run(approved, fake); });
                Check(fake.Pending && fake.Stage != FirmwareStage.Completed && !fake.Events.Contains("protect"), "Wrong reader after reboot");
            }
            groups++;
            {
                Fake swap = new Fake { Swapped = true }; Rejected(delegate { Run(approved, swap); }); Check(swap.Commands.Count == 0, "Swap during backup");
                Fake pending = new Fake { Pending = true }; Rejected(delegate { Run(approved, pending); });
                Check(pending.Commands.Count == 0 && !pending.Events.Contains("backup"), "Preserve pending journal");
                Fake cancelled = new Fake(); Rejected(delegate { new FirmwareLabUpdater(new FirmwareApproval[] { approved }).Run(approved, Data, cancelled, delegate { return true; }, delegate { }); });
                Check(cancelled.Commands.Count == 0, "Cancel before transfer"); groups++;
            }
            {
                Fake fake = new Fake { DisconnectOnCommit = true }; Run(approved, fake);
                Check(fake.Stage == FirmwareStage.Completed && fake.Events.Count(delegate(string name) { return name == "finish"; }) == 1, "Readback after uncertain commit, no retry");
                Fake bad = new Fake { BadResult = true }; Rejected(delegate { Run(approved, bad); }); Check(bad.Stage != FirmwareStage.Completed, "Device update failure");
                Fake ack = new Fake { BadAck = true }; Rejected(delegate { Run(approved, ack); });
                Check(!ack.Commands.Any(delegate(byte[] c) { return c.SequenceEqual(FirmwareApdu.Finish()); }), "Malformed acknowledgement"); groups++;
            }
            {
                Rejected(delegate { FirmwareApdu.Chunk(Data, 0, 0); }); Rejected(delegate { FirmwareApdu.Chunk(Data, 0, 256); });
                Rejected(delegate { FirmwareApdu.Chunk(Data, -1, 1); }); Rejected(delegate { FirmwareApdu.Chunk(Data, Data.Length, 1); });
                FirmwareLabUpdater updater = new FirmwareLabUpdater(new FirmwareApproval[] { approved }); Fake fake = new Fake();
                updater.Run(approved, Data, fake, delegate { return false; }, delegate { }); int count = fake.Events.Count;
                Rejected(delegate { updater.Run(approved, Data, fake, delegate { return false; }, delegate { }); }); Check(fake.Events.Count == count, "One-shot updater"); groups++;
            }
            {
                byte[] callerBytes = (byte[])Data.Clone(); Fake fake = new Fake { OnBackup = delegate { callerBytes[0] ^= 255; } };
                Run(approved, fake, callerBytes, delegate { throw new Exception("Synthetic UI callback failure"); });
                Check(fake.Stage == FirmwareStage.Completed && fake.Commands[1][5] == Data[0], "Caller mutation and UI observer cannot corrupt upload"); groups++;
            }
            Console.WriteLine("Firmware lab: " + groups + " simulated safety test groups passed. No hardware access; no recovery qualification."); return groups;
        }
    }
}
