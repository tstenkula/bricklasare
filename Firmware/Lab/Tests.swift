import Foundation
import CryptoKit

private let bytes = Data((0..<512).map { UInt8($0 % 256) })
private let before = FirmwareIdentity(board: 6, version: "01000000", id: String(repeating: "A", count: 32))
private let after = FirmwareIdentity(board: 6, version: "01000001", id: before.id)
private func offer(board: UInt8 = 6, from: [String] = [before.version], to: String = after.version,
                   source: String = "https://www.hidglobal.com/synthetic-never-download.fw",
                   evidence: String = "synthetic-test-evidence") -> FirmwareApproval {
  FirmwareApproval(board: board, from: from, to: to,
    sha256: SHA256.hash(data: bytes).map { String(format: "%02X", $0) }.joined(),
    size: bytes.count, source: source, recoveryEvidence: evidence)
}
private final class Fake: FirmwareLabSession {
  var failAt = "", events = [String](), pending = false, stage: FirmwareStage?
  var nextIdentity = after, swapped = false, badResult = false, badAck = false, disconnectOnCommit = false
  var commands = [[UInt8]](), onBackup: (() -> Void)?
  func step(_ name: String) throws {
    events.append(name)
    if failAt == name { throw FirmwareFailure.backup }
  }
  func identity() throws -> FirmwareIdentity {
    try step("identity")
    if swapped && pending { return FirmwareIdentity(board: 6, version: before.version, id: String(repeating: "B", count: 32)) }
    return before
  }
  func hasPendingJournal() throws -> Bool { try step("pending"); return pending }
  func backupAndCreateJournal(_ identity: FirmwareIdentity, approval: FirmwareApproval) throws {
    try step("backup"); pending = true; stage = .prepared; onBackup?()
  }
  func persist(_ value: FirmwareStage) throws {
    try step("persist-" + value.rawValue)
    guard pending else { throw FirmwareFailure.journal }
    stage = value
  }
  func exchange(_ command: [UInt8]) throws -> [UInt8] {
    commands.append(command)
    guard pending else { throw FirmwareFailure.journal }
    if command == FirmwareAPDU.start {
      guard stage == .transferring else { throw FirmwareFailure.journal }
      try step("start")
    } else if command == FirmwareAPDU.finish {
      guard stage == .commitRequested else { throw FirmwareFailure.journal }
      try step("finish")
      if disconnectOnCommit { throw FirmwareFailure.uncertainCommit }
    } else if command == FirmwareAPDU.result {
      try step("result"); return badResult ? [1, 0x90, 0] : [0, 0x90, 0]
    } else {
      guard command.count >= 6, command[0..<4] == [0xFF,0x68,0x0A,1][0..<4],
        Int(command[4]) == command.count - 5, stage == .transferring else { throw FirmwareFailure.protocolReply }
      try step("chunk-" + String(commands.count - 1))
    }
    return badAck ? [0x90, 0, 0] : [0x90, 0]
  }
  func reconnectToSameReader() throws -> FirmwareIdentity { try step("reconnect"); return nextIdentity }
  func restoreAndVerifyProtectedOutput() throws { try step("protect") }
}

private func check(_ value: Bool, _ description: String) throws {
  if !value { throw NSError(domain: description, code: 1) }
}
private func rejected(_ action: () throws -> Void) throws {
  do { try action() } catch is FirmwareFailure { return }
  throw NSError(domain: "Expected rejection", code: 2)
}
@main enum FirmwareLabTests {
  static func main() throws {
    let approved = offer()
    var groups = 0
    do {
      let fake = Fake(); var progress = [Int]()
      try FirmwareLabUpdater(approved: [approved]).run(approved, bytes: bytes, session: fake, progress: { progress.append($0) })
      try check(fake.stage == .completed && progress.last == 100 && progress == progress.sorted(), "Complete only after verification")
      try check(fake.events.suffix(3) == ["result", "protect", "persist-completed"], "Final checks")
      let chunks = fake.commands.filter { $0.count > 4 && $0[3] == 1 }
      try check(chunks.map { Int($0[4]) } == [255, 255, 2], "Chunk framing")
      try check(Data(chunks.flatMap { $0.dropFirst(5) }) == bytes, "Every firmware byte exactly once")
      groups += 1
    }
    do {
      let fake = Fake()
      try rejected { try FirmwareLabUpdater(approved: []).run(approved, bytes: bytes, session: fake) }
      try check(fake.events.isEmpty, "No approval must not contact reader")
      try rejected { try FirmwareLabUpdater(approved: [approved, approved]).run(approved, bytes: bytes, session: fake) }
      try check(fake.events.isEmpty, "Ambiguous approval must not contact reader")
      groups += 1
    }
    for bad in [offer(board: 10), offer(from: ["02000000"]), offer(from: [before.version, before.version]),
                offer(to: before.version), offer(to: "00000000"), offer(to: "not-hex!"), offer(evidence: ""),
                offer(source: "http://www.hidglobal.com/test.fw"),
                offer(source: "https://www.hidglobal.com.evil.invalid/test.fw"),
                offer(source: "https://www.hidglobal.com/test.wbo"),
                offer(source: "https://www.hidglobal.com/test.cfg"),
                offer(source: "https://www.hidglobal.com/test.fw?key=bad"),
                offer(source: "https://www.hidglobal.com/%2e%2e/test.fw"),
                offer(source: "https://user@www.hidglobal.com/test.fw")] {
      let fake = Fake()
      try rejected { try FirmwareLabUpdater(approved: [bad]).run(bad, bytes: bytes, session: fake) }
      try check(fake.commands.isEmpty && !fake.pending, "Rejected metadata before backup/write")
    }
    groups += 1
    for badBytes in [bytes.dropLast(), Data(repeating: 0, count: bytes.count), bytes + Data([0])] {
      let fake = Fake()
      try rejected { try FirmwareLabUpdater(approved: [approved]).run(approved, bytes: Data(badBytes), session: fake) }
      try check(fake.commands.isEmpty && !fake.pending, "Corrupt package writes nothing")
    }
    groups += 1
    for point in ["backup", "persist-transferring", "start", "chunk-1", "chunk-2", "chunk-3",
                  "persist-commitRequested", "finish", "persist-verifying", "reconnect", "result", "protect", "persist-completed"] {
      let fake = Fake(); fake.failAt = point; var progress = [Int]()
      try rejected { try FirmwareLabUpdater(approved: [approved]).run(approved, bytes: bytes, session: fake, progress: { progress.append($0) }) }
      try check(fake.stage != .completed && !progress.contains(100), "Failure cannot report success: " + point)
      try check(fake.events.filter { $0 == point }.count == 1, "No automatic retry: " + point)
      if point != "backup" { try check(fake.pending, "Keep recovery journal: " + point) }
      if point == "backup" || point == "persist-transferring" { try check(fake.commands.isEmpty, "Journal precedes Start") }
      if point.hasPrefix("chunk-") { try check(!fake.commands.contains(FirmwareAPDU.finish), "Never commit partial transfer") }
    }
    groups += 1
    for wrong in [FirmwareIdentity(board: 10, version: after.version, id: after.id),
                  FirmwareIdentity(board: 6, version: before.version, id: after.id),
                  FirmwareIdentity(board: 6, version: after.version, id: String(repeating: "B", count: 32))] {
      let fake = Fake(); fake.nextIdentity = wrong
      try rejected { try FirmwareLabUpdater(approved: [approved]).run(approved, bytes: bytes, session: fake) }
      try check(fake.pending && fake.stage != .completed && !fake.events.contains("protect"), "Wrong reader/version after reboot")
    }
    groups += 1
    do {
      let fake = Fake(); fake.swapped = true
      try rejected { try FirmwareLabUpdater(approved: [approved]).run(approved, bytes: bytes, session: fake) }
      try check(fake.commands.isEmpty, "Device swapped during backup writes nothing")
      let pending = Fake(); pending.pending = true
      try rejected { try FirmwareLabUpdater(approved: [approved]).run(approved, bytes: bytes, session: pending) }
      try check(pending.commands.isEmpty && !pending.events.contains("backup"), "Do not overwrite pending journal")
      let cancel = Fake()
      try rejected { try FirmwareLabUpdater(approved: [approved]).run(approved, bytes: bytes, session: cancel, cancelled: { true }) }
      try check(cancel.commands.isEmpty, "Pre-transfer cancellation")
      groups += 1
    }
    do {
      let fake = Fake(); fake.disconnectOnCommit = true
      try FirmwareLabUpdater(approved: [approved]).run(approved, bytes: bytes, session: fake)
      try check(fake.stage == .completed && fake.events.filter { $0 == "finish" }.count == 1, "Uncertain commit requires readback, never retry")
      let bad = Fake(); bad.badResult = true
      try rejected { try FirmwareLabUpdater(approved: [approved]).run(approved, bytes: bytes, session: bad) }
      try check(bad.stage != .completed, "Nonzero device update result")
      let ack = Fake(); ack.badAck = true
      try rejected { try FirmwareLabUpdater(approved: [approved]).run(approved, bytes: bytes, session: ack) }
      try check(!ack.commands.contains(FirmwareAPDU.finish), "Malformed acknowledgement")
      groups += 1
    }
    do {
      try rejected { _ = try FirmwareAPDU.chunk([][...]) }
      try rejected { _ = try FirmwareAPDU.chunk(Array(repeating: UInt8(0), count: 256)[...]) }
      let updater = FirmwareLabUpdater(approved: [approved]); let fake = Fake()
      try updater.run(approved, bytes: bytes, session: fake)
      let count = fake.events.count
      try rejected { try updater.run(approved, bytes: bytes, session: fake) }
      try check(fake.events.count == count, "One-shot updater")
      groups += 1
    }
    print("Firmware lab: \(groups) simulated safety test groups passed. No hardware access; no recovery qualification.")
  }
}
