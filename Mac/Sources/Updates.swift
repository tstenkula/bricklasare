import AppKit
import CryptoKit
import Darwin
import Security

struct UpdateAsset: Codable {
  let Version: String, Url: String, Sha256: String, Size: Int
  let Notes: String?
  // Not encoded into plans: the original signed bytes must remain the authority.
  var signedFeed: Data? = nil
  var channel: String? = nil
  enum CodingKeys: String, CodingKey { case Version, Url, Sha256, Size, Notes }
}
struct UpdateFeed: Codable {
  let Schema: Int, Channel: String
  let Windows: UpdateAsset?, Mac: UpdateAsset?
}
struct UpdatePlan: Codable {
  let target: String, directory: String, parent: Int32, channel: String
}
final class UpdateDownload: NSObject, URLSessionDataDelegate {
  var bytes = Data(), failure: Error?
  let maximum: Int, done = DispatchSemaphore(value: 0)
  init(_ maximum: Int) { self.maximum = maximum }
  static func allowed(_ url: URL) -> Bool {
    url.scheme == "https" && (url.port == nil || url.port == 443) && url.user == nil
      && url.password == nil
      && [
        "raw.githubusercontent.com", "github.com", "release-assets.githubusercontent.com",
        "objects.githubusercontent.com",
      ].contains(url.host ?? "")
  }
  func urlSession(
    _ session: URLSession, task: URLSessionTask,
    willPerformHTTPRedirection response: HTTPURLResponse, newRequest request: URLRequest,
    completionHandler: @escaping (URLRequest?) -> Void
  ) {
    completionHandler(request.url.map(Self.allowed) == true ? request : nil)
  }
  func urlSession(
    _ session: URLSession, dataTask: URLSessionDataTask, didReceive response: URLResponse,
    completionHandler: @escaping (URLSession.ResponseDisposition) -> Void
  ) {
    guard (response as? HTTPURLResponse)?.statusCode == 200,
      response.expectedContentLength <= maximum
    else {
      failure = fail("UPDATE_SOURCE", Updates.unavailable)
      completionHandler(.cancel)
      return
    }
    completionHandler(.allow)
  }
  func urlSession(_ session: URLSession, dataTask: URLSessionDataTask, didReceive data: Data) {
    guard bytes.count + data.count <= maximum else {
      failure = fail("UPDATE_SIZE", "Uppdateringsfilen är för stor.")
      dataTask.cancel()
      return
    }
    bytes.append(data)
  }
  func urlSession(_ session: URLSession, task: URLSessionTask, didCompleteWithError error: Error?) {
    if failure == nil { failure = error }
    done.signal()
  }
  static func get(_ url: URL, maximum: Int) throws -> Data {
    try require(allowed(url), "Otillåten uppdateringsadress.")
    let download = UpdateDownload(maximum)
    let config = URLSessionConfiguration.ephemeral
    config.timeoutIntervalForRequest = 30
    config.timeoutIntervalForResource = 180
    config.httpAdditionalHeaders = ["User-Agent": "Tobtech-Bricklasaren/" + Updates.current]
    let session = URLSession(configuration: config, delegate: download, delegateQueue: nil)
    defer { session.invalidateAndCancel() }
    session.dataTask(with: url).resume()
    guard download.done.wait(timeout: .now() + 190) == .success else {
      throw fail("UPDATE_TIMEOUT", Updates.unavailable)
    }
    if let error = download.failure { throw error }
    return download.bytes
  }
}
enum Updates {
  static let current = "3.5.2"
  static let publicKey =
    "MIIBigKCAYEAn8JTA0cbbRrraGNb11fX4Dyanoa8S9TzTBGl8Edfagx743HROUj9lDCwJvuVF6cNpH4mB2dN33O+XfYKZMrUaJ0K/3Q9gmPLqedlvbpuHWlEaeUzekdT4Npo7EZpwo+Cwcxjlmlahgoll1MkjEu8QERzAQEeuTB4vZCsKdXdYcRlKAQuPEKqCWkiH49prMDvVdqUUYLl2TPLcHPD8MDdkbFh/glWJDTRB1No++PCUU8PVG80X94BnMe1E/+LLyTjxaNYVSsIFZlD9XX3eup7t/UTKUk9RC1u8WY23l74ccXwy71lirntfOFW6MdK4kFsIGOk+2PF/QGwoIcpwfhSt7+Id0y85VGEqe002CngsfxUQ7BkAAAB2+D4vPcU3FBLkleYkG5B+WXasbCslTGFXG+hnFpTU2R5XQECRJ2UXjuJN/lDaw8AE7dwUTTfibfYGs9mAUZ6nECBsL3OmpXtCK9ZyTHx2AWs+hvXP+7hzcluf12K2H4MveyyNcOFSNedAgMBAAE="
  static let unavailable =
    "Det gick inte att söka efter uppdateringar. Kontrollera internetanslutningen och försök igen senare. Du kan fortsätta använda Brickläsaren."
  static func version(_ text: String) throws -> [Int] {
    let parts = text.split(separator: ".", omittingEmptySubsequences: false)
    try require(
      parts.count == 3
        && parts.allSatisfy {
          !$0.isEmpty && $0.count <= 5 && $0.utf8.allSatisfy { (48...57).contains($0) }
            && (Int($0) ?? 65536) <= 65535
        }, "Ogiltigt versionsnummer.")
    return parts.map { Int($0)! }
  }
  static func validate(_ asset: UpdateAsset) throws {
    _ = try version(asset.Version)
    guard let url = URL(string: asset.Url) else { throw fail("UPDATE_URL", "Ogiltig adress.") }
    try require(
      url.scheme == "https" && url.host == "github.com" && url.port == nil && url.user == nil
        && url.password == nil && url.query == nil && url.fragment == nil
        && url.path.hasPrefix("/tstenkula/bricklasare/releases/download/")
        && url.path.hasSuffix(".zip") && (1...52_428_800).contains(asset.Size)
        && asset.Sha256.count == 64
        && asset.Sha256.utf8.allSatisfy {
          (48...57).contains($0) || (65...70).contains($0) || (97...102).contains($0)
        } && (asset.Notes ?? "").count <= 4000, "Ogiltigt uppdateringspaket.")
  }
  static func decode(_ bytes: Data, channel: String) throws -> UpdateAsset? {
    try require(
      bytes.count <= 65536 && ["stable", "preview"].contains(channel), "Ogiltig uppdateringsfil.")
    let lines = String(decoding: bytes, as: UTF8.self).trimmingCharacters(in: .newlines).components(
      separatedBy: "\n")
    guard lines.count == 3, lines[0] == "BRICKLASARE-UPDATE-1",
      let payload = Data(base64Encoded: lines[1]), let signature = Data(base64Encoded: lines[2]),
      let keyData = Data(base64Encoded: publicKey),
      let key = SecKeyCreateWithData(
        keyData as CFData,
        [
          kSecAttrKeyType: kSecAttrKeyTypeRSA, kSecAttrKeyClass: kSecAttrKeyClassPublic,
          kSecAttrKeySizeInBits: 3072,
        ] as CFDictionary, nil),
      SecKeyVerifySignature(
        key, .rsaSignatureMessagePKCS1v15SHA256, payload as CFData, signature as CFData, nil)
    else {
      throw fail(
        "UPDATE_SIGNATURE", "Det gick inte att kontrollera uppdateringen. Försök igen senare.")
    }
    let feed = try JSONDecoder().decode(UpdateFeed.self, from: payload)
    try require(
      feed.Schema == 1 && feed.Channel == channel, "Uppdateringen tillhör en annan kanal.")
    guard var asset = feed.Mac else { return nil }
    try validate(asset)
    asset.signedFeed = bytes
    asset.channel = channel
    return asset
  }
  static func check(_ channel: String) throws -> UpdateAsset? {
    try require(["stable", "preview"].contains(channel), "Ogiltig kanal.")
    return try decode(
      UpdateDownload.get(
        URL(
          string:
            "https://raw.githubusercontent.com/tstenkula/bricklasare/main/Updates/\(channel).feed")!,
        maximum: 65536), channel: channel)
  }
  static func authenticated(_ offered: UpdateAsset) throws -> UpdateAsset {
    guard let envelope = offered.signedFeed, let channel = offered.channel,
      let signed = try decode(envelope, channel: channel)
    else { throw fail("UPDATE_SIGNATURE", "Signerad uppdateringsinformation saknas.") }
    try require(
      signed.Version == offered.Version && signed.Url == offered.Url
        && signed.Sha256 == offered.Sha256 && signed.Size == offered.Size,
      "Uppdateringsinformationen har ändrats.")
    try require(
      try version(current).lexicographicallyPrecedes(version(signed.Version)),
      "Uppdateringen är inte nyare än den installerade versionen.")
    return signed
  }
  static func readRegular(_ url: URL, maximum: Int) throws -> Data {
    let fd = open(url.path, O_RDONLY | O_NOFOLLOW | O_CLOEXEC)
    try require(fd >= 0, "Uppdateringsfilen kunde inte öppnas.")
    let handle = FileHandle(fileDescriptor: fd, closeOnDealloc: true)
    defer { try? handle.close() }
    var info = stat()
    try require(
      fstat(fd, &info) == 0 && (info.st_mode & S_IFMT) == S_IFREG
        && info.st_size > 0 && info.st_size <= maximum,
      "Uppdateringsfilen är ogiltig eller för stor.")
    let bytes = try handle.read(upToCount: maximum + 1) ?? Data()
    try require(!bytes.isEmpty && bytes.count <= maximum, "Uppdateringsfilen är för stor.")
    return bytes
  }
  // Both the final installer and its regression tests use this exact trust boundary.
  static func installPackage(_ directory: URL, channel: String) throws -> (UpdateAsset, Data) {
    let envelope = try readRegular(directory.appendingPathComponent("release.feed"), maximum: 65536)
    guard let signed = try decode(envelope, channel: channel) else {
      throw fail("UPDATE_SIGNATURE", "Uppdateringen saknar ett signerat paket.")
    }
    let asset = try authenticated(signed)
    let bytes = try readRegular(directory.appendingPathComponent("package.zip"), maximum: 52_428_800)
    try verify(bytes, asset: asset)
    return (asset, bytes)
  }
  static func pending(_ store: Store) -> Bool {
    store.files().contains {
      ($0.hasPrefix("aterstall-") || $0.hasPrefix("usb-pending-") || $0.hasPrefix("usb-bootstrap-"))
        && $0.hasSuffix(".json")
    }
  }
  static func verify(_ bytes: Data, asset: UpdateAsset) throws {
    try validate(asset)
    let hash = SHA256.hash(data: bytes).map { String(format: "%02x", $0) }.joined()
    try require(
      bytes.count == asset.Size && hash == asset.Sha256.lowercased(),
      "Den hämtade uppdateringen kunde inte kontrolleras. Försök hämta den igen.")
  }
  // Validate central AND local names before ditto sees the archive. Reject links,
  // traversal, duplicate names, encryption, ZIP64 and excessive expanded sizes.
  static func validateZip(_ bytes: Data) throws {
    let b = [UInt8](bytes)
    func u16(_ offset: Int) throws -> Int {
      try require(offset >= 0 && offset + 2 <= b.count, "Trasigt ZIP-paket.")
      return Int(b[offset]) | (Int(b[offset + 1]) << 8)
    }
    func u32(_ offset: Int) throws -> Int {
      try require(offset >= 0 && offset + 4 <= b.count, "Trasigt ZIP-paket.")
      return try u16(offset) | (u16(offset + 2) << 16)
    }
    try require(b.count >= 22, "Tomt ZIP-paket.")
    var end: Int?
    for i in stride(from: b.count - 22, through: max(0, b.count - 65557), by: -1) {
      if try u32(i) == 0x0605_4b50, try i + 22 + u16(i + 20) == b.count {
        end = i
        break
      }
    }
    guard let end else { throw fail("UPDATE_ZIP", "Trasigt ZIP-paket.") }
    let count = try u16(end + 10)
    let centralSize = try u32(end + 12)
    let central = try u32(end + 16)
    try require(
      try u16(end + 4) == 0 && u16(end + 6) == 0 && u16(end + 8) == count && count > 0
        && count <= 1000 && central + centralSize == end, "ZIP-formatet stöds inte.")
    var offset = central
    var total = 0
    var names = Set<String>()
    for _ in 0..<count {
      try require(
        try u32(offset) == 0x0201_4b50 && u16(offset + 8) & 1 == 0, "ZIP-formatet stöds inte.")
      let nameLength = try u16(offset + 28)
      let extra = try u16(offset + 30)
      let comment = try u16(offset + 32)
      let size = try u32(offset + 24)
      let packed = try u32(offset + 20)
      let local = try u32(offset + 42)
      let mode = try u32(offset + 38) >> 16
      try require(
        offset + 46 + nameLength + extra + comment <= end && (mode & 0xf000) != 0xa000
          && size <= 104_857_600, "Osäkert ZIP-innehåll.")
      let nameBytes = Array(b[(offset + 46)..<(offset + 46 + nameLength)])
      guard let name = String(bytes: nameBytes, encoding: .utf8) else {
        throw fail("UPDATE_ZIP", "Ogiltigt filnamn.")
      }
      let parts = name.split(separator: "/", omittingEmptySubsequences: false)
      try require(
        name.hasPrefix("Bricklasaren.app/") && !name.contains("\\") && !name.contains(":")
          && !name.contains("\0") && names.insert(name.lowercased()).inserted
          && parts.dropLast(name.hasSuffix("/") ? 1 : 0).allSatisfy {
            !$0.isEmpty && $0 != ".." && $0 != "."
          }, "Osäker sökväg i paketet.")
      total += size
      try require(total <= 104_857_600, "Paketet är för stort.")
      try require(
        try u32(local) == 0x0403_4b50 && u16(local + 26) == nameLength
          && u16(local + 6) == u16(offset + 8) && u16(local + 8) == u16(offset + 10),
        "ZIP-rubrikerna stämmer inte.")
      let dataStart = try local + 30 + nameLength + u16(local + 28)
      try require(
        dataStart + packed <= central
          && Array(b[(local + 30)..<(local + 30 + nameLength)]) == nameBytes,
        "ZIP-rubrikerna stämmer inte.")
      offset += 46 + nameLength + extra + comment
    }
    try require(offset == end, "Trasigt ZIP-paket.")
  }
  static func run(_ path: String, _ arguments: [String], timeout: TimeInterval = 120, input: Data? = nil) throws {
    let process = Process()
    process.executableURL = URL(fileURLWithPath: path)
    process.arguments = arguments
    process.standardOutput = FileHandle.nullDevice
    process.standardError = FileHandle.nullDevice
    let pipe = input.map { _ in Pipe() }
    if let pipe {
      try require(fcntl(pipe.fileHandleForWriting.fileDescriptor, F_SETNOSIGPIPE, 1) == 0,
        "Kunde inte förbereda paketkontrollen.")
      process.standardInput = pipe
    }
    let done = DispatchSemaphore(value: 0)
    process.terminationHandler = { _ in done.signal() }
    try process.run()
    if let input, let pipe {
      try? pipe.fileHandleForReading.close()
      DispatchQueue.global(qos: .utility).async {
        defer { try? pipe.fileHandleForWriting.close() }
        try? pipe.fileHandleForWriting.write(contentsOf: input)
      }
    }
    if done.wait(timeout: .now() + timeout) != .success {
      if process.isRunning { process.terminate() }
      if done.wait(timeout: .now() + 2) != .success {
        if process.isRunning { _ = kill(process.processIdentifier, SIGKILL) }
        _ = done.wait(timeout: .now() + 2)
      }
      throw fail("UPDATE_TIMEOUT", "Kontrollen tog för lång tid.")
    }
    try require(process.terminationStatus == 0, "Kontrollen av den nya versionen misslyckades.")
  }
  static func stage(_ offered: UpdateAsset, store: Store) throws -> URL {
    let asset = try authenticated(offered)
    try require(!pending(store), "Återställ läsaren innan programmet uppdateras.")
    let target = Bundle.main.bundleURL.standardizedFileURL
    try require(
      target.pathExtension == "app" && !target.path.hasPrefix("/Volumes/")
        && FileManager.default.isWritableFile(atPath: target.deletingLastPathComponent().path),
      "Flytta appen till en skrivbar Program-mapp innan du uppdaterar.")
    let directory = store.url("updates/" + UUID().uuidString)
    try FileManager.default.createDirectory(
      at: directory, withIntermediateDirectories: true, attributes: [.posixPermissions: 0o700])
    do {
      let bytes = try UpdateDownload.get(URL(string: asset.Url)!, maximum: 52_428_800)
      try verify(bytes, asset: asset)
      try validateZip(bytes)
      try bytes.write(to: directory.appendingPathComponent("package.zip"), options: .atomic)
      try asset.signedFeed!.write(to: directory.appendingPathComponent("release.feed"), options: .atomic)
      let plan = UpdatePlan(
        target: target.path, directory: directory.path, parent: getpid(), channel: asset.channel!)
      let planURL = directory.appendingPathComponent("plan.json")
      try JSONEncoder().encode(plan).write(to: planURL, options: .atomic)
      let helper = directory.appendingPathComponent("installer")
      try FileManager.default.copyItem(at: Bundle.main.executableURL!, to: helper)
      return planURL
    } catch {
      try? FileManager.default.removeItem(at: directory)
      throw error
    }
  }
  static func install(_ planURL: URL) throws {
    let plan = try JSONDecoder().decode(UpdatePlan.self, from: readRegular(planURL, maximum: 16384))
    let store = try Store()
    let fm = FileManager.default
    let directory = URL(fileURLWithPath: plan.directory)
    let target = URL(fileURLWithPath: plan.target)
    try require(
      directory.standardizedFileURL.path
        == planURL.deletingLastPathComponent().standardizedFileURL.path
        && directory.deletingLastPathComponent().standardizedFileURL.path == store.url("updates").standardizedFileURL.path
        && directory.resolvingSymlinksInPath() == directory.standardizedFileURL
        && target.resolvingSymlinksInPath() == target.standardizedFileURL
        && ["stable", "preview"].contains(plan.channel) && plan.parent > 1
        && target.pathExtension == "app" && !target.path.hasPrefix("/Volumes/"),
      "Ogiltig uppdateringsplan.")
    let deadline = Date().addingTimeInterval(120)
    while kill(plan.parent, 0) == 0 {
      try require(Date() < deadline, "Programmet är fortfarande igång.")
      Thread.sleep(forTimeInterval: 0.2)
    }
    let lock = open(store.url("app.lock").path, O_CREAT | O_RDWR | O_NOFOLLOW, 0o600)
    try require(lock >= 0, "Kunde inte låsa programmet.")
    defer {
      flock(lock, LOCK_UN)
      close(lock)
    }
    try require(
      flock(lock, LOCK_EX | LOCK_NB) == 0 && !pending(store),
      "Programmet används eller läsaren behöver återställas.")
    let archive = directory.appendingPathComponent("package.zip")
    let (asset, bytes) = try installPackage(directory, channel: plan.channel)
    try validateZip(bytes)
    let extracted = directory.appendingPathComponent("extracted-" + UUID().uuidString)
    try fm.createDirectory(at: extracted, withIntermediateDirectories: false, attributes: [.posixPermissions: 0o700])
    defer { try? fm.removeItem(at: extracted) }
    // Feed the exact verified bytes to ditto. Never reopen a replaceable archive path.
    try run("/usr/bin/ditto", ["-x", "-k", "-", extracted.path], input: bytes)
    let candidate = extracted.appendingPathComponent("Bricklasaren.app")
    guard let bundle = Bundle(url: candidate) else {
      throw fail("UPDATE_APP", "Programmet saknas i paketet.")
    }
    try require(
      bundle.bundleIdentifier == "se.tobtech.bricklasaren"
        && bundle.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String
          == asset.Version,
      "Fel programversion i paketet.")
    try run("/usr/bin/codesign", ["--verify", "--deep", "--strict", candidate.path])
    try run(candidate.appendingPathComponent("Contents/MacOS/Bricklasaren").path, ["--self-test"])
    let sibling = target.deletingLastPathComponent().appendingPathComponent(
      ".Bricklasaren-new-" + UUID().uuidString + ".app")
    try fm.copyItem(at: candidate, to: sibling)
    let backup = target.deletingLastPathComponent().appendingPathComponent(
      "Bricklasaren.previous-" + UUID().uuidString + ".app")
    try replace(target: target, candidate: sibling, backup: backup)
    flock(lock, LOCK_UN)
    try run("/usr/bin/open", ["-n", "-a", target.path])
    try store.writeData(
      "uppdatering-status.txt",
      Data(
        "Uppdatering installerad. Föregående app finns kvar som Bricklasaren.previous-kopia.\n".utf8
      ))
    try? fm.removeItem(at: archive)
    try? fm.removeItem(at: extracted)
  }
  static func replace(target: URL, candidate: URL, backup: URL) throws {
    let fm = FileManager.default
    try fm.moveItem(at: target, to: backup)
    do { try fm.moveItem(at: candidate, to: target) } catch {
      try fm.moveItem(at: backup, to: target)
      throw error
    }
  }
}
extension AppController {
  func checkStartupUpdates() {
    guard !preview, !busy, startupUpdatePending else { return }
    startupUpdatePending = false
    checkUpdates(automatic: true)
  }
  func checkUpdates(automatic: Bool = false) {
    guard !busy, !updateChecking else { return }
    updateChecking = true
    let channel = workflow.channel
    DispatchQueue.global(qos: .utility).async { [weak self] in
      let result = Result { try Updates.check(channel) }
      DispatchQueue.main.async {
        guard let self else { return }
        self.updateChecking = false
        guard !self.busy, self.workflow.channel == channel else { return }
        switch result {
        case .failure(let error):
          if !automatic {
            let a = NSAlert()
            a.messageText = "Uppdateringar"
            a.informativeText = (error as? ReaderError)?.message ?? Updates.unavailable
            a.runModal()
          }
        case .success(let asset):
          guard let asset, let next = try? Updates.version(asset.Version),
            let current = try? Updates.version(Updates.current),
            current.lexicographicallyPrecedes(next)
          else {
            if !automatic {
              let a = NSAlert()
              a.messageText = "Ingen nyare version finns i vald kanal."
              a.runModal()
            }
            return
          }
          if automatic {
            self.notice = "Version \(asset.Version) finns. Välj Hjälp → Sök efter uppdatering."
            self.rebuild()
            return
          }
          self.offerUpdate(asset)
        }
      }
    }
  }
  func offerUpdate(_ asset: UpdateAsset) {
    guard !busy else { return }
    if Updates.pending(store) {
      showError(fail("RECOVERY", "Återställ läsaren innan du uppdaterar."))
      return
    }
    let alert = NSAlert()
    alert.messageText = "Uppdatera till \(asset.Version)?"
    alert.informativeText =
      (asset.Notes ?? "")
      + "\n\nAppen startas om när uppdateringen är klar. Dina inställningar sparas."
    alert.addButton(withTitle: "Hämta och installera")
    alert.addButton(withTitle: "Senare")
    guard alert.runModal() == .alertFirstButtonReturn else { return }
    busy = true
    clearResults()
    instruction = "Hämtar och kontrollerar uppdateringen"
    rebuild()
    worker.async { [weak self] in
      guard let self else { return }
      do {
        let plan = try Updates.stage(asset, store: self.store)
        try require(!Updates.pending(self.store), "Återställning väntar.")
        let helper = Process()
        helper.executableURL = plan.deletingLastPathComponent().appendingPathComponent("installer")
        helper.arguments = ["--install-update", plan.path]
        helper.standardOutput = FileHandle.nullDevice
        helper.standardError = FileHandle.nullDevice
        try helper.run()
        DispatchQueue.main.async {
          self.busy = false
          NSApp.terminate(nil)
        }
      } catch {
        DispatchQueue.main.async {
          self.busy = false
          self.readyMessage()
          self.rebuild()
          showError(error)
        }
      }
    }
  }
}
