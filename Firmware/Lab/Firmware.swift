// Development only. Not part of Mac/Sources or a distributed application.
import Foundation
import CryptoKit

enum FirmwareFailure: Error, Equatable {
  case approval, package, identity, backup, journal, protocolReply, uncertainCommit, protection, cancelled, reused
}

struct FirmwareIdentity: Equatable {
  let board: UInt8
  let version: String
  let id: String
  var valid: Bool {
    [6, 10].contains(board) && Self.hex(version, count: 8) && Self.hex(id, count: 32)
  }
  static func hex(_ text: String, count: Int) -> Bool {
    text.utf8.count == count && text.utf8.allSatisfy { (48...57).contains($0) || (65...70).contains($0) }
  }
}

// Only an app-reviewed, authenticated catalog may eventually supply approvals.
// The lab has no catalog loader, signing authority, or production approvals.
struct FirmwareApproval: Equatable {
  let board: UInt8
  let from: [String]
  let to: String
  let sha256: String
  let size: Int
  let source: String
  let recoveryEvidence: String

  func validate(identity: FirmwareIdentity, bytes: Data) throws {
    guard identity.valid, board == identity.board, from.contains(identity.version),
      !from.isEmpty, Set(from).count == from.count,
      from.allSatisfy({ FirmwareIdentity.hex($0, count: 8) }),
      FirmwareIdentity.hex(to, count: 8), identity.version < to,
      FirmwareIdentity.hex(sha256, count: 64), (1...16_777_216).contains(size),
      bytes.count == size, !recoveryEvidence.isEmpty, recoveryEvidence.utf8.count <= 200,
      let url = URLComponents(string: source), url.scheme == "https",
      ["www.hidglobal.com", "sdk.hidglobal.com"].contains(url.host ?? ""),
      url.user == nil, url.password == nil, url.port == nil, url.query == nil, url.fragment == nil,
      url.percentEncodedPath == url.path, url.path.hasSuffix(".fw"),
      !url.path.split(separator: "/").contains(where: { $0 == "." || $0 == ".." }),
      SHA256.hash(data: bytes).map({ String(format: "%02X", $0) }).joined() == sha256
    else { throw FirmwareFailure.package }
  }
}

enum FirmwareStage: String { case prepared, transferring, commitRequested, verifying, completed }

protocol FirmwareLabSession: AnyObject {
  // Implementations must own one exclusive, pinned physical session. No silent rebinding.
  func identity() throws -> FirmwareIdentity
  func hasPendingJournal() throws -> Bool
  // Must export and durably verify a full encrypted configuration, then create the journal.
  // Returning successfully is a prerequisite for every write. No live adapter exists yet.
  func backupAndCreateJournal(_ identity: FirmwareIdentity, approval: FirmwareApproval) throws
  func persist(_ stage: FirmwareStage) throws
  func exchange(_ command: [UInt8]) throws -> [UInt8]
  func reconnectToSameReader() throws -> FirmwareIdentity
  func restoreAndVerifyProtectedOutput() throws
}

enum FirmwareAPDU {
  static let start: [UInt8] = [0xFF, 0x68, 0x0A, 0x00, 0x04, 0, 0, 0, 0]
  static let finish: [UInt8] = [0xFF, 0x68, 0x0A, 0x02, 0]
  static let result: [UInt8] = [0xFF, 0x68, 0x0A, 0x0C, 0]
  static func chunk(_ bytes: ArraySlice<UInt8>) throws -> [UInt8] {
    guard (1...255).contains(bytes.count) else { throw FirmwareFailure.package }
    return [0xFF, 0x68, 0x0A, 0x01, UInt8(bytes.count)] + bytes
  }
  static func success(_ reply: [UInt8], payload: [UInt8] = []) throws {
    guard reply == payload + [0x90, 0] else { throw FirmwareFailure.protocolReply }
  }
}

final class FirmwareLabUpdater {
  // This list must be app-authenticated, never accepted from an installation plan or local JSON.
  private let approved: [FirmwareApproval]
  private var used = false
  private let lock = NSLock()
  init(approved: [FirmwareApproval]) { self.approved = approved }

  func run(_ offer: FirmwareApproval, bytes: Data, session: FirmwareLabSession,
           cancelled: () -> Bool = { false }, progress: (Int) -> Void = { _ in }) throws {
    lock.lock()
    guard !used else { lock.unlock(); throw FirmwareFailure.reused }
    used = true
    lock.unlock()
    guard approved.filter({ $0 == offer }).count == 1 else { throw FirmwareFailure.approval }
    let before = try session.identity()
    try offer.validate(identity: before, bytes: bytes)
    guard try !session.hasPendingJournal() else { throw FirmwareFailure.journal }
    guard !cancelled() else { throw FirmwareFailure.cancelled }
    progress(0)
    try session.backupAndCreateJournal(before, approval: offer)
    // Recheck after backup: a swapped reader must not receive even the Start command.
    guard try session.identity() == before else { throw FirmwareFailure.identity }
    guard !cancelled() else { throw FirmwareFailure.cancelled }
    try session.persist(.transferring)
    // No cancellation, retries, mode changes, or independent MIB calls once upload starts.
    try FirmwareAPDU.success(session.exchange(FirmwareAPDU.start))
    let data = Array(bytes)
    for offset in stride(from: 0, to: data.count, by: 255) {
      let end = min(offset + 255, data.count)
      try FirmwareAPDU.success(session.exchange(FirmwareAPDU.chunk(data[offset..<end])))
      progress(min(89, end * 90 / data.count))
    }
    // Persist uncertainty BEFORE the command that may replace firmware and reboot.
    try session.persist(.commitRequested)
    do { try FirmwareAPDU.success(session.exchange(FirmwareAPDU.finish)) }
    catch FirmwareFailure.uncertainCommit { /* Only a verified transport disconnect may map here. */ }
    try session.persist(.verifying)
    let after = try session.reconnectToSameReader()
    guard after.valid, after.id == before.id, after.board == before.board, after.version == offer.to
    else { throw FirmwareFailure.identity }
    progress(95)
    try FirmwareAPDU.success(session.exchange(FirmwareAPDU.result), payload: [0])
    try session.restoreAndVerifyProtectedOutput()
    try session.persist(.completed)
    progress(100)
    // No automatic firmware rollback, journal deletion, or calibration re-labelling.
  }
}
