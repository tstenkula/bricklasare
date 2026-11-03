import CryptoKit
import Foundation

final class PCSCTransport: ReaderTransport {
  private var handle: OpaquePointer?
  let name: String
  init() throws {
    var reader: OpaquePointer?
    let status = br_open(&reader)
    guard status == 0, let reader else { throw Self.error(status) }
    handle = reader
    name = String(cString: br_name(reader))
  }
  deinit { close() }
  func close() {
    if let p = handle {
      br_close(p)
      handle = nil
    }
  }
  static func error(_ status: Int32) -> ReaderError {
    let code = String(format: "PCSC_%08X", UInt32(bitPattern: status))
    return fail(
      code,
      "Ingen fungerande smartkortsanslutning till läsaren. Kontrollera USB-kabeln och att bara en OMNIKEY är ansluten. Försök ansluta igen. (\(code))"
    )
  }
  func exchange(_ bytes: [UInt8]) throws -> [UInt8] {
    guard let handle else { throw fail("DISCONNECTED", "Läsaren är frånkopplad.") }
    var output = [UInt8](repeating: 0, count: 4096)
    var count: UInt32 = 4096
    let status = bytes.withUnsafeBufferPointer { input in
      output.withUnsafeMutableBufferPointer { target in
        br_exchange(handle, input.baseAddress, UInt32(input.count), target.baseAddress, &count)
      }
    }
    try require(status == 0, Self.error(status).message, code: Self.error(status).code)
    try require(count <= 4096, "För stort läsarsvar.")
    return Array(output.prefix(Int(count)))
  }
  func present() throws -> Bool {
    let status = try Codec.mibResponse("0302010300", exchange(Codec.mib("0302010300")))
    try require(status.tag == 4, "Oväntad datatyp för kortstatus.")
    return try !Codec.atr(status.value).isEmpty
  }
}
struct USBState {
  let count: Int
  let fingerprint: String
  let mode: Int
  static func current() -> USBState {
    var bytes = [CChar](repeating: 0, count: 256)
    var mode: Int32 = 0
    let count = br_usb_info(&bytes, bytes.count, &mode)
    return USBState(count: Int(count), fingerprint: String(cString: bytes), mode: Int(mode))
  }
}
struct USBBootstrap: Codable {
  var fingerprint: String
  var originalMode: Int
  var created: String
}
struct USBOrigin: Codable {
  var readerId: String
  var firmware: String
  var originalMode: Int
  var fingerprint: String
}
enum MacConnector {
  static func bootstrapName(_ fingerprint: String) -> String {
    "usb-bootstrap-"
      + SHA256.hash(data: Data(fingerprint.utf8)).map { String(format: "%02x", $0) }.joined()
      + ".json"
  }
  static func connect(store: Store, progress: (String) -> Void) throws -> ReaderEngine {
    let usb = USBState.current()
    try require(
      usb.count == 1,
      usb.count > 1
        ? "Anslut bara en OMNIKEY åt gången."
        : "Ingen OMNIKEY hittades. Anslut läsaren till USB och godkänn tillbehöret om macOS frågar.",
      code: "USB_COUNT")
    let bootstrap = bootstrapName(usb.fingerprint)
    if store.exists(bootstrap) {
      let previous: USBBootstrap = try store.read(bootstrap)
      try require(
        previous.fingerprint == usb.fingerprint && [0, 1, 2].contains(previous.originalMode),
        "Ogiltig sparad USB-övergång. Ingen ändring utfördes.")
    }
    if usb.mode != 0 {
      if !store.exists(bootstrap) {
        try store.write(
          bootstrap,
          USBBootstrap(
            fingerprint: usb.fingerprint, originalMode: usb.mode,
            created: ISO8601DateFormatter().string(from: Date())))
      }
      // Journal before the only HID mode-switch request. No card data is read here.
      progress("Förbereder smartkortsläget. Håll alla brickor borta från läsaren …")
      let code = usb.fingerprint.withCString { br_enable_ccid($0) }
      if code != 0 {
        throw fail(
          String(format: "USB_%08X", UInt32(bitPattern: code)),
          "Läsaren kunde inte växlas till smartkortsläge. Koppla ur och in den och försök igen. Om problemet kvarstår: spara felrapporten via Hjälp."
        )
      }
    }
    let deadline = Date().addingTimeInterval(usb.mode != 0 ? 45 : 4)
    var connected: PCSCTransport?
    var last: Error = fail("CONNECT", "Läsaren kunde inte anslutas.")
    repeat {
      do {
        connected = try PCSCTransport()
        break
      } catch {
        last = error
        Thread.sleep(forTimeInterval: 0.5)
      }
    } while Date() < deadline
    guard let transport = connected else { throw last }
    let engine = ReaderEngine(transport, store)
    try engine.connect()
    let after = USBState.current()
    try require(
      after.count == 1 && after.fingerprint == usb.fingerprint && after.mode == 0,
      "Läsarens USB-läge eller identitet ändrades under anslutningen.", code: "USB_IDENTITY")
    let originName = "usb-original-\(engine.id).json"
    if !store.exists(originName) {
      var originalMode = usb.mode
      if store.exists(bootstrap) {
        let before: USBBootstrap = try store.read(bootstrap)
        try require(
          before.fingerprint == usb.fingerprint && [0, 1, 2].contains(before.originalMode),
          "Ogiltig sparad USB-övergång.")
        originalMode = before.originalMode
      }
      try store.write(
        originName,
        USBOrigin(
          readerId: engine.id, firmware: engine.firmware, originalMode: originalMode,
          fingerprint: usb.fingerprint))
    }
    let pendingRestore = "usb-restore-\(engine.id).json"
    if store.exists(pendingRestore) {
      let previous: USBOrigin = try store.read(pendingRestore)
      try require(
        previous.readerId == engine.id && previous.firmware == engine.firmware
          && [0, 1, 2].contains(previous.originalMode), "Ogiltig USB-återställning. Kontakta IT.")
      // Reconnecting deliberately prepares CCID again. Keep the original backup, clear only the verified transition.
      try engine.verifyProtection()
      try store.remove(pendingRestore)
    }
    try store.remove(bootstrap)
    return engine
  }
  static func restore(_ engine: ReaderEngine) throws {
    try require(!engine.pending, "Återställ den avbrutna läsningen först.")
    try engine.checkIdentity()
    let original: USBOrigin = try engine.store.read("usb-original-\(engine.id).json")
    try require(
      original.readerId == engine.id && original.firmware == engine.firmware
        && [0, 1, 2].contains(original.originalMode),
      "USB-backupen gäller inte den anslutna läsaren och firmware.")
    if original.originalMode == 0 {
      try engine.verifyProtection()
      return
    }
    let current = USBState.current()
    try require(
      current.count == 1 && current.mode == 0, "Läsarens aktuella USB-läge kan inte verifieras.")
    let pending = "usb-restore-\(engine.id).json"
    try engine.store.write(pending, original)
    // Preserve the Custom Report bit, then perform the documented atomic switch/apply/reboot.
    let custom = TLV(tag: 2, value: [original.originalMode == 2 ? 255 : 0])
    try require(
      try Codec.mibResponse(
        ReaderEngine.custom, engine.send(Codec.mib(ReaderEngine.custom, set: custom))) == custom,
      "USB-inställningen bekräftades inte.")
    try engine.checkIdentity()
    do { _ = try engine.send(Codec.hex("FF6800020102")) } catch let error as ReaderError {
      // The command intentionally has no reply; only accept a disconnect, followed by verified USB enumeration.
      guard error.code.hasPrefix("PCSC_") else { throw error }
    }
    engine.transport.close()
    let deadline = Date().addingTimeInterval(45)
    repeat {
      let after = USBState.current()
      if after.count == 1 && after.fingerprint == current.fingerprint
        && after.mode == original.originalMode
      {
        try engine.store.remove(pending)
        return
      }
      Thread.sleep(forTimeInterval: 0.5)
    } while Date() < deadline
    throw fail(
      "USB_RESTORE",
      "USB-återställningen kunde inte verifieras. Behåll backupen och anslut samma läsare igen.")
  }
}
