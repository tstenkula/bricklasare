import Foundation

struct ReaderError: Error, LocalizedError {
  let code: String
  let message: String
  var errorDescription: String? { message }
}
func fail(_ code: String, _ message: String) -> ReaderError {
  ReaderError(code: code, message: message)
}
func require(_ condition: Bool, _ message: String, code: String = "FORMAT") throws {
  if !condition { throw fail(code, message) }
}

struct TLV: Equatable {
  var tag: UInt8
  var value: [UInt8]
}
enum Codec {
  static func hex(_ text: String) throws -> [UInt8] {
    let s = text.filter { !$0.isWhitespace }
    let chars = Array(s)
    try require(
      chars.count % 2 == 0 && chars.allSatisfy { $0.isASCII && $0.isHexDigit },
      "Ogiltiga hexadecimala data.")
    return stride(from: 0, to: chars.count, by: 2).map {
      UInt8(String(chars[$0...($0 + 1)]), radix: 16)!
    }
  }
  static func text(_ bytes: [UInt8]) -> String { bytes.map { String(format: "%02X", $0) }.joined() }
  static func success(_ bytes: [UInt8]) throws -> [UInt8] {
    try require(bytes.count >= 2, "Tomt svar från läsaren.")
    let sw = Int(bytes[bytes.count - 2]) * 256 + Int(bytes.last!)
    try require(
      sw == 0x9000, String(format: "Läsaren svarade %04X. Försök igen med brickan stilla.", sw),
      code: String(format: "APDU_%04X", sw))
    return Array(bytes.dropLast(2))
  }
  static func tlv(_ bytes: [UInt8], _ offset: inout Int) throws -> TLV {
    try require(offset >= 0 && offset + 2 <= bytes.count, "Avklippt TLV-svar.")
    let tag = bytes[offset]
    var length = Int(bytes[offset + 1])
    offset += 2
    try require(tag & 31 != 31, "Okänd TLV-tagg.")
    if length & 128 != 0 {
      let count = length & 127
      try require(count > 0 && count <= 2 && offset + count <= bytes.count, "Ogiltig TLV-längd.")
      length = 0
      for _ in 0..<count {
        length = length * 256 + Int(bytes[offset])
        offset += 1
      }
    }
    try require(offset + length <= bytes.count, "Avklippt TLV-innehåll.")
    let value = Array(bytes[offset..<(offset + length)])
    offset += length
    return TLV(tag: tag, value: value)
  }
  static func mib(_ oid: String, set: TLV? = nil) throws -> [UInt8] {
    let name = try hex(oid)
    let t = set ?? TLV(tag: 5, value: [])
    try require(!name.isEmpty && name.count <= 32 && t.value.count <= 127, "Ogiltig inställning.")
    let pdu: [UInt8] =
      [set == nil ? 0 : 2, 0, 0, 13, UInt8(name.count)] + name + [t.tag, UInt8(t.value.count)]
      + t.value
    return [255, 104, 0, 0, UInt8(pdu.count)] + pdu + [0]
  }
  static func mibResponse(_ oid: String, _ response: [UInt8]) throws -> TLV {
    let data = try success(response)
    try require(
      data.count >= 7 && data[0] == 3 && data[1] == 0,
      "Läsaren stöder inte den begärda inställningen.", code: "MIB")
    var p = 3
    let name = try tlv(data, &p)
    let value = try tlv(data, &p)
    try require(
      name.tag == 13 && text(name.value) == oid.uppercased() && p == data.count,
      "Inställningssvaret motsvarar inte frågan.")
    return value
  }
  static func atr(_ bytes: [UInt8]) throws -> [UInt8] {
    if bytes.isEmpty || bytes.allSatisfy({ $0 == 0 }) { return [] }
    if bytes[0] == 0x3B || bytes[0] == 0x3F { return bytes }
    let text =
      String(bytes: bytes, encoding: .ascii)?.trimmingCharacters(
        in: CharacterSet(charactersIn: "\0 \r\n\t")) ?? "?"
    if text.isEmpty { return [] }
    try require(
      text.uppercased().hasPrefix("3B") || text.uppercased().hasPrefix("3F"), "Okänt ATR-format.")
    return try hex(text)
  }
  static func em(_ response: [UInt8]) throws -> [UInt8] {
    let payload = try success(response)
    var p = 0
    let envelope = try tlv(payload, &p)
    try require(
      envelope.tag == 0x9D && p == payload.count,
      "Svaret är inte ett LF/Prox-svar. Inget EM-nummer visas.")
    var q = 0
    let bits = try tlv(envelope.value, &q)
    try require(
      bits.tag == 3 && q == envelope.value.count && bits.value.count == 6 && bits.value[0] == 0,
      "Brickan ger inte 40-bitars EM-data utan utfyllnad.")
    return Array(bits.value.dropFirst())
  }
  static func ordered(_ raw: [UInt8], _ order: String) throws -> [UInt8] {
    try require(order == "Forward" || order == "Reverse", "Ogiltig byteordning.")
    return order == "Forward" ? raw : raw.reversed()
  }
  static func rcoLong(_ bytes: [UInt8]) throws -> String {
    try require(bytes.count >= 4 && bytes.count <= 16, "Ogiltig UID-längd.")
    let b = Array(bytes.suffix(4))
    return String(format: "%05d%05d", Int(b[0]) * 256 + Int(b[1]), Int(b[2]) * 256 + Int(b[3]))
  }
  static func rco(_ bytes: [UInt8]) throws -> String { String(try rcoLong(bytes).dropFirst()) }
  static func vaka(_ bytes: [UInt8]) throws -> String {
    try require(bytes.count == 5, "VAKA kräver fem EM-byte.")
    return String(format: "%014llu", bytes.reduce(UInt64(0)) { ($0 << 8) | UInt64($1) })
  }
  static func bewator(_ bytes: [UInt8]) throws -> String {
    try require(bytes.count == 5, "Bewator kräver fem EM-byte.")
    return String(
      format: "%03d%010llu", Int(bytes[0]),
      bytes.dropFirst().reduce(UInt64(0)) { ($0 << 8) | UInt64($1) })
  }
  static func referenceOrder(_ bytes: [UInt8], _ expected: String) throws -> String {
    let forward = text(bytes) == expected
    let reverse = text(bytes.reversed()) == expected
    try require(
      forward != reverse, "Referensbrickan matchar inte entydigt. Ingen profil sparades.",
      code: "REFERENCE")
    return forward ? "Forward" : "Reverse"
  }
}
enum AccessMode: String, Codable, CaseIterable {
  case combo = "Kombi"
  case rco = "RCO"
  case axema = "AxemaVAKA"
  case bewator = "Bewator13"
  var title: String {
    switch self {
    case .combo: return "RCO – MIFARE + EM (mR + eR)"
    case .rco: return "RCO – kort/lång ID-kod"
    case .axema: return "Axema VAKA – 14 siffror"
    case .bewator: return "Bewator 2010/Omnis – 13 siffror"
    }
  }
  var detail: String {
    switch self {
    case .combo:
      return "Två avläsningar av samma bricka: MIFARE (mR) och EM (eR), båda som 9-siffriga RCO-ID."
    case .rco:
      return "En skanning av vald teknik. Visar kort ID med 9 siffror och långt ID med 10 siffror."
    case .axema: return "En skanning av EM-chip. VAKA-format med 14 siffror, inte Axbase."
    case .bewator: return "En skanning av EM-chip. 13 siffror för 2010/Omnis, inte Entro."
    }
  }
}
struct FormatPreference: Codable {
  var Mode: AccessMode
  var RcoChip: String
  func validate() throws { try require(["M", "E"].contains(RcoChip), "Ogiltigt teknikval.") }
}
struct Calibration: Codable {
  var Schema = 2
  var ReaderId: String
  var Firmware: String
  var MifareOrder: String
  var EmOrder: String
  func validate() throws {
    try require(
      Schema == 2 && ReaderId.range(of: "^[0-9A-F]{32}$", options: .regularExpression) != nil
        && Firmware.range(of: "^[0-9A-F]{8}$", options: .regularExpression) != nil,
      "Ogiltig läsarprofil.")
    _ = try Codec.ordered([], MifareOrder)
    _ = try Codec.ordered([], EmOrder)
  }
}
struct ReaderProfile: Codable {
  var Schema = 1
  var Address = "192.168.63.99"
  var Calibration: Calibration
}
struct ReferenceCard: Codable {
  var MifareHex: String
  var EmHex: String
  mutating func validate() throws {
    let m = try Codec.hex(MifareHex)
    let e = try Codec.hex(EmHex)
    try require(
      [4, 7, 10].contains(m.count) && e.count == 5 && m != Array(m.reversed())
        && e != Array(e.reversed()),
      "Ange verifierat MIFARE-UID (4, 7 eller 10 byte) och EM (5 byte) med entydig byteordning.")
    MifareHex = Codec.text(m)
    EmHex = Codec.text(e)
  }
}
final class Store {
  let root: URL
  init(_ root: URL? = nil) throws {
    self.root =
      root
      ?? FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
      .appendingPathComponent("Tobtech/Bricklasaren", isDirectory: true)
    try FileManager.default.createDirectory(
      at: self.root, withIntermediateDirectories: true, attributes: [.posixPermissions: 0o700])
  }
  func url(_ name: String) -> URL { root.appendingPathComponent(name) }
  func exists(_ name: String) -> Bool { FileManager.default.fileExists(atPath: url(name).path) }
  func read<T: Decodable>(_ name: String, as: T.Type = T.self) throws -> T {
    try Self.decode(url(name), as: T.self)
  }
  static func decode<T: Decodable>(_ url: URL, as: T.Type = T.self) throws -> T {
    let handle = try FileHandle(forReadingFrom: url)
    defer { try? handle.close() }
    let data = try handle.read(upToCount: 16385) ?? Data()
    try require(
      !data.isEmpty && data.count <= 16384, "Inställningsfilen är tom eller för stor.",
      code: "CONFIG")
    let clean = data.starts(with: [239, 187, 191]) ? data.dropFirst(3) : data[...]
    do { return try JSONDecoder().decode(T.self, from: Data(clean)) } catch {
      throw fail("CONFIG", "Inställningsfilen är ogiltig eller hör till en annan version.")
    }
  }
  func write<T: Encodable>(_ name: String, _ value: T) throws {
    let encoder = JSONEncoder()
    encoder.outputFormatting = [.sortedKeys, .prettyPrinted]
    try writeData(name, encoder.encode(value))
  }
  func writeData(_ name: String, _ data: Data) throws {
    let path = url(name)
    try data.write(to: path, options: [.atomic])
    try FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: path.path)
  }
  func remove(_ name: String) throws {
    if exists(name) { try FileManager.default.removeItem(at: url(name)) }
  }
  func preference() -> FormatPreference? {
    guard let p: FormatPreference = try? read("formatval.json"), (try? p.validate()) != nil else {
      return nil
    }
    return p
  }
  func files() -> [String] {
    (try? FileManager.default.contentsOfDirectory(atPath: root.path)) ?? []
  }
  func prune() {
    let all = files()
    guard !all.contains(where: { $0.hasPrefix("aterstall-") && $0.hasSuffix(".json") }) else {
      return
    }
    let backups = all.filter {
      $0.range(of: "^backup-[0-9]{17}-[0-9A-Fa-f-]{36}\\.json$", options: .regularExpression) != nil
    }.sorted().reversed()
    for name in backups.dropFirst(20) { try? remove(name) }
  }
}
final class CancelToken {
  private let lock = NSLock()
  private var value = false
  func cancel() {
    lock.lock()
    value = true
    lock.unlock()
  }
  var cancelled: Bool {
    lock.lock()
    defer { lock.unlock() }
    return value
  }
  func check() throws {
    if cancelled { throw fail("CANCELLED", "Läsningen avbröts. Inga delresultat visas.") }
  }
  func wait(_ seconds: Double) throws {
    let end = Date().addingTimeInterval(seconds)
    repeat {
      try check()
      Thread.sleep(forTimeInterval: min(0.05, max(0, end.timeIntervalSinceNow)))
    } while Date() < end
  }
}
final class LocalLog {
  private var entries: [String] = []
  func add(_ message: String) {
    entries.append(message)
    if entries.count > 2500 { entries.removeFirst(entries.count - 2500) }
  }
  func clear() { entries.removeAll(keepingCapacity: false) }
  var text: String { entries.joined(separator: "\n") }
}
protocol ReaderTransport: AnyObject {
  var name: String { get }
  func exchange(_ bytes: [UInt8]) throws -> [UInt8]
  func present() throws -> Bool
  func close()
}
struct SavedSetting: Codable, Equatable {
  var Oid: String
  var Tag: Int
  var HexValue: String
}
struct Journal: Codable {
  var Schema = 3
  var ReaderId: String
  var Firmware: String
  var Created: String
  var Settings: [SavedSetting]
}
struct ScanResult {
  var rawM: [UInt8] = []
  var rawE: [UInt8] = []
  var mOrder = "Reverse"
  var eOrder = "Forward"
  var m: [UInt8] { get throws { try Codec.ordered(rawM, mOrder) } }
  var e: [UInt8] { get throws { try Codec.ordered(rawE, eOrder) } }
  func outputs(_ preference: FormatPreference) throws -> (String, String) {
    switch preference.Mode {
    case .combo: return (try Codec.rco(m), try Codec.rco(e))
    case .rco:
      let bytes = try preference.RcoChip == "M" ? m : e
      return (try Codec.rco(bytes), try Codec.rcoLong(bytes))
    case .axema: return (try Codec.vaka(e), "")
    case .bewator: return (try Codec.bewator(e), "")
    }
  }
}
struct ReaderInfo {
  var board: String
  var id: String
  var firmware: String
  var profile: String
  var pending: Bool
  var ready: Bool
}
final class ReaderEngine {
  static let polls = [
    "02020301030100", "02020301040100", "02020301050100", "02020301060100", "02020301070100",
    "02020301070400", "020203010A0100",
  ]
  static let ask = "040C0200", keyboard = "04010100", custom = "04010500"
  let transport: ReaderTransport
  let store: Store
  let log = LocalLog()
  var delay: (Double) -> Void = { Thread.sleep(forTimeInterval: $0) }
  var waitDelay: Double = 0.35
  private(set) var id = "", firmware = "", board = ""
  private(set) var calibration: Calibration?
  private(set) var profileIssue: String?
  private(set) var connected = false
  private(set) var protectedOutput = false
  init(_ transport: ReaderTransport, _ store: Store) {
    self.transport = transport
    self.store = store
  }
  deinit { transport.close() }
  var journalName: String { "aterstall-\(id).json" }
  var calibrationName: String { "byteordning-\(id).json" }
  var pending: Bool { !id.isEmpty && store.exists(journalName) }
  var ready: Bool { connected && protectedOutput && profileIssue == nil && !pending }
  var info: ReaderInfo {
    ReaderInfo(
      board: board, id: id, firmware: firmware,
      profile: profileIssue ?? (calibration == nil ? "Standardformat" : "Verifierad lokal profil"),
      pending: pending, ready: ready)
  }
  func send(_ command: [UInt8]) throws -> [UInt8] {
    log.add("TX antal byte: \(command.count)")
    let response = try transport.exchange(command)
    try require(response.count <= 4096, "För stort svar från läsaren.")
    log.add("RX antal byte: \(response.count)")
    return response
  }
  func get(_ oid: String) throws -> TLV { try Codec.mibResponse(oid, send(Codec.mib(oid))) }
  func checkIdentity() throws {
    let value = try get("010A00")
    let fw = try get("010100")
    try require(
      value.tag == 4 && Codec.text(value.value) == id && fw.tag == 2
        && Codec.text(fw.value) == firmware,
      "En annan läsare eller firmware är ansluten. Åtgärden stoppades.", code: "IDENTITY")
  }
  func verifyProtection() throws {
    protectedOutput = false
    try checkIdentity()
    let state = try get(Self.keyboard)
    try require(
      state.tag == 1 && state.value == [0],
      "Läsaren är inte i skyddat smartkortsläge. Anslut igen via Hjälp.", code: "USB_MODE")
    protectedOutput = true
  }
  func connect() throws {
    connected = false
    protectedOutput = false
    calibration = nil
    profileIssue = nil
    let b = try get("010200")
    try require(
      b.tag == 2 && (b.value == [6] || b.value == [10]), "Endast OMNIKEY 5427 CK och Gen2 stöds.",
      code: "MODEL")
    board = b.value == [6] ? "OMNIKEY 5427 CK" : "OMNIKEY 5427 CK Gen2"
    let fw = try get("010100")
    let identifier = try get("010A00")
    try require(
      fw.tag == 2 && fw.value.count == 4 && identifier.tag == 4 && identifier.value.count == 16,
      "Läsarens identitet kunde inte verifieras.")
    firmware = Codec.text(fw.value)
    id = Codec.text(identifier.value)
    _ = try readSettings()
    _ = try transport.present()
    try verifyProtection()
    if store.exists(calibrationName) {
      do {
        let c: Calibration = try store.read(calibrationName)
        try c.validate()
        try require(
          c.ReaderId == id && c.Firmware == firmware, "Profilen gäller annan läsare eller firmware."
        )
        calibration = c
      } catch {
        profileIssue =
          "Den sparade läsarprofilen behöver kontrolleras. Importera rätt profil eller gör ett nytt referensprov."
      }
    }
    connected = true
  }
  static func validate(_ setting: SavedSetting) throws {
    let value = try Codec.hex(setting.HexValue)
    if setting.Oid == ask {
      try require(setting.Tag == 4 && value.count == 8, "Ogiltig ASK-inställning.")
    } else {
      try require(
        polls.contains(setting.Oid) && setting.Tag == 1 && value.count == 1
          && [0, 1, 255].contains(value[0]), "Otillåten inställning i återställningen.")
    }
  }
  func readSettings() throws -> [SavedSetting] {
    try (Self.polls + [Self.ask]).map { oid in
      let t = try get(oid)
      let s = SavedSetting(Oid: oid, Tag: Int(t.tag), HexValue: Codec.text(t.value))
      try Self.validate(s)
      return s
    }
  }
  func set(_ s: SavedSetting) throws {
    try Self.validate(s)
    let t = TLV(tag: UInt8(s.Tag), value: try Codec.hex(s.HexValue))
    try require(
      try Codec.mibResponse(s.Oid, send(Codec.mib(s.Oid, set: t))) == t,
      "Läsaren bekräftade inte inställningen.")
  }
  func apply() throws {
    try require(try Codec.success(send(Codec.hex("FF6800010101"))).isEmpty, "Oväntat Apply-svar.")
    delay(0.35)
  }
  func backup() throws {
    try require(!pending, "En tidigare läsning behöver återställas först.", code: "RECOVERY")
    let saved = Journal(
      ReaderId: id, Firmware: firmware, Created: ISO8601DateFormatter().string(from: Date()),
      Settings: try readSettings())
    try store.write(journalName, saved)
    let format = DateFormatter()
    format.dateFormat = "yyyyMMddHHmmssSSS"
    format.locale = Locale(identifier: "en_US_POSIX")
    try store.write("backup-\(format.string(from: Date()))-\(UUID().uuidString).json", saved)
  }
  func recover() throws {
    guard pending else { return }
    try checkIdentity()
    let j: Journal = try store.read(journalName)
    try require(
      j.Schema == 3 && j.ReaderId == id && j.Firmware == firmware && j.Settings.count == 8
        && Set(j.Settings.map(\.Oid)).count == 8, "Återställningsfilen är ogiltig.",
      code: "RECOVERY")
    for s in j.Settings { try Self.validate(s) }
    var failed = false
    for s in j.Settings { do { try set(s) } catch { failed = true } }
    do { try apply() } catch { failed = true }
    for s in j.Settings {
      do {
        let t = try get(s.Oid)
        if Int(t.tag) != s.Tag || Codec.text(t.value) != s.HexValue { failed = true }
      } catch { failed = true }
    }
    try require(
      !failed,
      "Återställningen kunde inte verifieras. Behåll läsaren ansluten och välj Återställ läsaren.",
      code: "RECOVERY")
    try store.remove(journalName)
    store.prune()
    try verifyProtection()
  }
  func setMode(mifare: Bool) throws {
    try checkIdentity()
    let target = mifare ? Self.polls[0] : Self.polls[6]
    for oid in Self.polls {
      try set(SavedSetting(Oid: oid, Tag: 1, HexValue: oid == target ? "FF" : "00"))
    }
    if !mifare { try set(SavedSetting(Oid: Self.ask, Tag: 4, HexValue: "0101000000000000")) }
    try apply()
    for oid in Self.polls {
      let actual = try get(oid)
      try require(
        actual.tag == 1 && actual.value.count == 1 && (actual.value[0] != 0) == (oid == target),
        "Teknikväxlingen kunde inte verifieras.")
    }
  }
  func waitFor(_ presence: Bool, _ token: CancelToken) throws {
    let deadline = Date().addingTimeInterval(60)
    var stable = 0
    while Date() < deadline {
      try token.check()
      stable = (try transport.present()) == presence ? stable + 1 : 0
      if stable >= 2 { return }
      try token.wait(waitDelay)
    }
    throw fail(
      "TIMEOUT",
      presence
        ? "Ingen bricka lästes. Lyft den helt och försök igen."
        : "Ta bort alla brickor från läsaren och försök igen.")
  }
  func readCard(mifare: Bool, _ token: CancelToken) throws -> [UInt8] {
    func once() throws -> [UInt8] {
      if mifare {
        let data = try Codec.success(send(Codec.hex("FFCA000000")))
        try require([4, 7, 10].contains(data.count), "MIFARE-UID måste vara 4, 7 eller 10 byte.")
        return data
      }
      return try Codec.em(send(Codec.hex("FF70076B07A005A10380010400")))
    }
    try token.check()
    try require(try transport.present(), "Brickan togs bort för tidigt.")
    let first = try once()
    try token.wait(waitDelay == 0 ? 0 : 0.18)
    let second = try once()
    try require(
      try first == second && transport.present(),
      "Instabil avläsning eller brickan byttes. Försök igen.")
    return first
  }
  // Progress values are semantic; the UI translates them to identical operator steps.
  func scan(
    _ preference: FormatPreference, reference: Bool = false, token: CancelToken,
    progress: (String) -> Void
  ) throws -> ScanResult {
    try preference.validate()
    var expected: ReferenceCard?
    if reference {
      var r: ReferenceCard = try store.read("referensbricka.json")
      try r.validate()
      expected = r
    }
    try require(
      connected && (reference || profileIssue == nil) && !pending,
      "Anslut och förbered läsaren först.")
    try verifyProtection()
    log.clear()
    progress("remove-first")
    try waitFor(false, token)
    try backup()
    var result = ScanResult()
    var failure: Error?
    do {
      if reference || preference.Mode == .combo
        || (preference.Mode == .rco && preference.RcoChip == "M")
      {
        progress("prepare-m")
        try setMode(mifare: true)
        progress("place-m")
        try waitFor(true, token)
        result.rawM = try readCard(mifare: true, token)
        result.mOrder =
          try expected.map { try Codec.referenceOrder(result.rawM, $0.MifareHex) } ?? calibration?
          .MifareOrder ?? "Reverse"
        progress("remove-m")
        try waitFor(false, token)
      }
      if reference || preference.Mode != .rco || preference.RcoChip == "E" {
        progress("prepare-e")
        try setMode(mifare: false)
        progress("place-e")
        try waitFor(true, token)
        result.rawE = try readCard(mifare: false, token)
        result.eOrder =
          try expected.map { try Codec.referenceOrder(result.rawE, $0.EmHex) } ?? calibration?
          .EmOrder ?? "Forward"
        progress("remove-e")
        try waitFor(false, token)
      }
      try token.check()
    } catch { failure = error }
    progress("restore")
    do { try recover() } catch { failure = error }
    if let e = failure { throw e }
    try token.check()
    try verifyProtection()
    if reference {
      let c = Calibration(
        ReaderId: id, Firmware: firmware, MifareOrder: result.mOrder, EmOrder: result.eOrder)
      try store.write(calibrationName, c)
      calibration = c
      profileIssue = nil
    }
    return result
  }
  func importProfile(_ url: URL) throws {
    let p: ReaderProfile = try Store.decode(url)
    try p.Calibration.validate()
    try require(
      p.Schema == 1 && p.Calibration.ReaderId == id && p.Calibration.Firmware == firmware
        && !pending, "Profilen gäller inte den anslutna läsaren, eller återställning väntar.")
    try store.write(calibrationName, p.Calibration)
    calibration = p.Calibration
    profileIssue = nil
  }
}
