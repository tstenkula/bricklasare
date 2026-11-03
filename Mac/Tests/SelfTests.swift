import AppKit
import QuartzCore

final class MockReader: ReaderTransport {
  let name = "OMNIKEY simulated"
  static let identity = Array(UInt8(1)...UInt8(16))
  var settings: [String: TLV] = [:], mutations = 0, presentValue = false, reads = 0, closed = false
  var readFailure = false, unstable = false, recoveryFailure = false, changingIdentity = false
  init(board: UInt8 = 6) {
    for oid in ReaderEngine.polls { settings[oid] = TLV(tag: 1, value: [255]) }
    settings[ReaderEngine.ask] = TLV(tag: 4, value: [9, 8, 7, 6, 5, 4, 3, 2])
    settings[ReaderEngine.keyboard] = TLV(tag: 1, value: [0])
    settings[ReaderEngine.custom] = TLV(tag: 2, value: [0])
    settings["010200"] = TLV(tag: 2, value: [board])
    settings["010100"] = TLV(tag: 2, value: [1, 2, 3, 4])
    settings["010A00"] = TLV(tag: 4, value: Self.identity)
  }
  func present() throws -> Bool { presentValue }
  func close() { closed = true }
  func exchange(_ b: [UInt8]) throws -> [UInt8] {
    try require(!closed, "Mock transport closed")
    if b == (try Codec.hex("FF6800010101")) { return [0x90, 0] }
    if try b == Codec.hex("FFCA000000") || b == Codec.hex("FF70076B07A005A10380010400") {
      reads += 1
      if readFailure { throw fail("SIMULATED_READ", "Simulated read failure") }
      if b[1] == 0xCA {
        return (unstable && reads % 2 == 0 ? [5, 3, 2, 1] : [4, 3, 2, 1]) + [0x90, 0]
      }
      return try Codec.hex("9D0803060001020304059000")
    }
    try require(b.count >= 15 && b[0] == 255 && b[1] == 104, "Unexpected mock command")
    let length = Int(b[9])
    let oid = Codec.text(Array(b[10..<(10 + length)]))
    let tag = b[10 + length]
    let n = Int(b[11 + length])
    if b[5] == 2 {
      mutations += 1
      if recoveryFailure { throw fail("SIMULATED_WRITE", "Simulated restore failure") }
      settings[oid] = TLV(tag: tag, value: Array(b[(12 + length)..<(12 + length + n)]))
    }
    guard var t = settings[oid] else { throw fail("SIMULATED_OID", "Unexpected OID") }
    if changingIdentity && oid == "010A00" { t.value = Array(repeating: 42, count: 16) }
    return [3, 0, 0, 13, UInt8(length)] + (try Codec.hex(oid)) + [t.tag, UInt8(t.value.count)]
      + t.value + [0x90, 0]
  }
}
struct SelfTests {
  static var count = 0
  static func check(_ value: Bool, _ name: String) throws {
    try require(value, "Test failed: " + name, code: "TEST")
    count += 1
  }
  static func rejects(_ name: String, _ action: () throws -> Void) throws {
    do { try action() } catch {
      count += 1
      return
    }
    throw fail("TEST", "Expected rejection: " + name)
  }
  static func tempStore() throws -> Store {
    try Store(
      FileManager.default.temporaryDirectory.appendingPathComponent(
        "bricklasare-test-" + UUID().uuidString))
  }
  static func engine(_ store: Store, board: UInt8 = 6) throws -> (ReaderEngine, MockReader) {
    let m = MockReader(board: board)
    let e = ReaderEngine(m, store)
    e.delay = { _ in }
    e.waitDelay = 0
    try e.connect()
    return (e, m)
  }
  static func run() throws {
    let store = try tempStore()
    defer { try? FileManager.default.removeItem(at: store.root) }
    try updates(store)
    try check(
      Codec.text(try Codec.mib("010100")) == "FF6800000A0000000D03010100050000",
      "Windows GET vector")
    try check(
      Codec.text(try Codec.mib("02020301030100", set: TLV(tag: 1, value: [255])))
        == "FF6800000F0200000D07020203010301000101FF00", "Windows SET vector")
    try check(
      Codec.text(try Codec.mib("040C0200", set: TLV(tag: 4, value: Codec.hex("0101000000000000"))))
        == "FF680000130200000D04040C02000408010100000000000000", "ASK command")
    for (input, expected) in [
      ("01020304", "025800772"), ("0102030405", "051501029"), ("01020304050607", "102901543"),
      ("00000000", "000000000"), ("FFFFFFFF", "553565535"),
    ] { try check(try Codec.rco(Codec.hex(input)) == expected, "RCO parity") }
    try check(try Codec.rcoLong(Codec.hex("01020304")) == "0025800772", "RCO long")
    try check(try Codec.vaka(Codec.hex("0102030405")) == "00004328719365", "VAKA decimal")
    try check(try Codec.bewator(Codec.hex("0102030405")) == "0010033752069", "Bewator split")
    try check(try Codec.vaka(Codec.hex("FFFFFFFFFF")) == "01099511627775", "VAKA max")
    try check(try Codec.bewator(Codec.hex("FFFFFFFFFF")) == "2554294967295", "Bewator max")
    try check(Codec.text(try Codec.atr(Array("3B8F00\0".utf8))) == "3B8F00", "ASCII ATR")
    try check(try Codec.atr([0, 0]).isEmpty, "Empty ATR")
    try check(try Codec.atr([0x3F, 0]) == [0x3F, 0], "Binary ATR")
    try rejects("Unknown ATR") { _ = try Codec.atr([1, 2, 3]) }
    try check(
      Codec.text(try Codec.em(Codec.hex("9D0803060001020304059000"))) == "0102030405",
      "EM LF envelope")
    for input in [
      "03060001020304059000", "9D07030506010203049000", "9D080306000102030405009000", "9D80039000",
      "9000", "6F00",
    ] { try rejects("Malformed LF") { _ = try Codec.em(Codec.hex(input)) } }
    for s in ["1", "GG", "ＦＦ"] { try rejects("Malformed hex") { _ = try Codec.hex(s) } }
    try rejects("MIB OID mismatch") {
      _ = try Codec.mibResponse("010200", Codec.hex("0300000D030101000204010501769000"))
    }
    try rejects("MIB error") {
      _ = try Codec.mibResponse("010100", Codec.hex("0302010D0301010005009000"))
    }
    try check(try Codec.referenceOrder([4, 3, 2, 1], "01020304") == "Reverse", "Reference reverse")
    try rejects("Ambiguous reference") { _ = try Codec.referenceOrder([1, 2, 2, 1], "01020201") }
    try check(store.preference() == nil, "No initial selection")
    let choice = FormatPreference(Mode: .rco, RcoChip: "E")
    try store.write("formatval.json", choice)
    try check(
      store.preference()?.Mode == .rco && store.preference()?.RcoChip == "E", "Persist selection")
    try store.writeData(
      "formatval.json",
      Data([239, 187, 191]) + Data("{\"Mode\":\"AxemaVAKA\",\"RcoChip\":\"M\"}".utf8))
    try check(store.preference()?.Mode == .axema, "Windows BOM config")
    try store.writeData("formatval.json", Data(repeating: 65, count: 16385))
    try check(store.preference() == nil, "Oversized config")
    try store.remove("formatval.json")
    let (e, m) = try engine(store)
    let original = m.settings
    try check(e.ready && e.calibration == nil, "Fresh reader ready without reference")
    func phases(_ phase: String) { m.presentValue = phase.hasPrefix("place-") }
    let result = try e.scan(
      FormatPreference(Mode: .combo, RcoChip: "M"), token: CancelToken(), progress: phases)
    let values = try result.outputs(FormatPreference(Mode: .combo, RcoChip: "M"))
    try check(values.0 == "025800772" && values.1 == "051501029", "Complete two-step scan")
    try check(
      m.settings == original && !e.pending && e.ready, "Original settings restored before output")
    try check(
      !e.log.text.contains("01020304") && !e.log.text.contains("04030201"), "Log excludes card data"
    )
    let token = CancelToken()
    try rejects("Cancellation restores") {
      _ = try e.scan(FormatPreference(Mode: .combo, RcoChip: "M"), token: token) { phase in
        phases(phase)
        if phase == "place-m" { token.cancel() }
      }
    }
    try check(m.settings == original && !e.pending, "Cancel recovery")
    m.readFailure = true
    try rejects("Read failure") { _ = try e.scan(choice, token: CancelToken(), progress: phases) }
    try check(m.settings == original && !e.pending, "Read failure recovery")
    m.readFailure = false
    m.unstable = true
    try rejects("Unstable UID") {
      _ = try e.scan(
        FormatPreference(Mode: .rco, RcoChip: "M"), token: CancelToken(), progress: phases)
    }
    try check(m.settings == original && !e.pending, "Unstable UID recovery")
    m.unstable = false
    try e.backup()
    let writes = m.mutations
    m.changingIdentity = true
    try rejects("Wrong reader recovery") { try e.recover() }
    try check(m.mutations == writes && e.pending, "No writes to wrong reader")
    m.changingIdentity = false
    m.recoveryFailure = true
    try rejects("Failed restore retained") { try e.recover() }
    try check(e.pending && !e.ready, "Journal blocks reads")
    try rejects("Pending journal blocks scan") {
      _ = try e.scan(choice, token: CancelToken(), progress: phases)
    }
    m.recoveryFailure = false
    try e.recover()
    try check(e.ready, "Recovery retry")
    var j = Journal(
      ReaderId: e.id, Firmware: e.firmware, Created: "test", Settings: try e.readSettings())
    j.Settings[0] = SavedSetting(Oid: ReaderEngine.keyboard, Tag: 1, HexValue: "FF")
    try store.write(e.journalName, j)
    let writesBefore = m.mutations
    try rejects("Untrusted journal setting") { try e.recover() }
    try check(writesBefore == m.mutations, "Validate full journal before any writes")
    try store.remove(e.journalName)
    let wrongFirmware = Journal(
      ReaderId: e.id, Firmware: "FFFFFFFF", Created: "test", Settings: try e.readSettings())
    try store.write(e.journalName, wrongFirmware)
    try rejects("Journal firmware mismatch") { try e.recover() }
    try check(m.mutations == writesBefore, "No restore writes after firmware change")
    try store.remove(e.journalName)
    var reference = ReferenceCard(MifareHex: "01020304", EmHex: "0102030405")
    try reference.validate()
    try store.write("referensbricka.json", reference)
    _ = try e.scan(choice, reference: true, token: CancelToken(), progress: phases)
    try check(
      e.calibration?.MifareOrder == "Reverse" && e.calibration?.EmOrder == "Forward",
      "Reference verification")
    var bad = Calibration(
      ReaderId: e.id, Firmware: "00000000", MifareOrder: "Reverse", EmOrder: "Forward")
    try store.write(e.calibrationName, bad)
    let (stale, _) = try engine(store)
    try check(!stale.ready, "Stale profile blocks scanning")
    bad.Firmware = e.firmware
    bad.ReaderId = String(repeating: "F", count: 32)
    try store.write("profile.json", ReaderProfile(Calibration: bad))
    try rejects("Wrong reader import") { try stale.importProfile(store.url("profile.json")) }
    bad.ReaderId = e.id
    try store.write("profile.json", ReaderProfile(Calibration: bad))
    try stale.importProfile(store.url("profile.json"))
    try check(stale.ready, "Matching import repairs stale profile")
    try store.remove(e.calibrationName)
    let (gen2, _) = try engine(store, board: 10)
    try check(gen2.ready, "Fresh Gen2 ready")
    try rejects("Other reader model") { _ = try engine(store, board: 1) }
    try check(AppearanceFiles.load(store).AccentHex == "#003B55", "Navy default")
    for color in ["#003B55", "#FFFFFF", "#000000", "#FFFF00", "#00FF00", "#67509B"] {
      let t = Theme(color)
      try check(
        Theme.contrast(t.text, t.light) >= 4.5 && Theme.contrast(t.text, Theme.paper) >= 4.5
          && Theme.contrast(t.accent, t.buttonText) >= 4.5, "Theme contrast " + color)
    }
    var a = AppearanceSettings()
    a.AccentHex = "#ABCDEF"
    a.Organization = "Testverksamhet"
    _ = try AppearanceFiles.save(store, a, logo: nil)
    try check(AppearanceFiles.load(store).AccentHex == "#ABCDEF", "Custom theme persisted")
    a.Organization = "wrong\nline"
    try rejects("Control characters") { _ = try AppearanceFiles.save(store, a, logo: nil) }
    try check(
      AppearanceFiles.load(store).Organization == "Testverksamhet",
      "Failed save preserves prior settings")
    a = AppearanceSettings()
    a.LogoFile = "../../secret.png"
    try rejects("Logo traversal") { try a.validate() }
    try store.writeData("utseende.json", Data("broken".utf8))
    try check(AppearanceFiles.load(store).AccentHex == "#003B55", "Invalid theme fallback")
    let bitmap = NSBitmapImageRep(
      bitmapDataPlanes: nil, pixelsWide: 2, pixelsHigh: 2,
      bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
      colorSpaceName: .deviceRGB, bytesPerRow: 8, bitsPerPixel: 32)!
    memset(bitmap.bitmapData!, 255, 16)
    let png = bitmap.representation(using: .png, properties: [:])!
    try store.writeData("sample.png", png)
    try check(try AppearanceFiles.logoData(store.url("sample.png")) == png, "Validated PNG logo")
    let branded = try AppearanceFiles.save(store, AppearanceSettings(), logo: png)
    try check(store.exists("branding/" + branded.LogoFile), "Owned logo copied locally")
    let cache = AppearanceCache()
    let cachedImage = cache.logo(store, settings: branded)
    try check(cachedImage != nil && cache.logo(store, settings: branded) === cachedImage, "Validated logo is decoded once and reused")
    try check(cache.logo(store, settings: AppearanceSettings()) == nil, "Clearing branding invalidates the logo cache")
    try rejects("Malformed logo does not replace prior") {
      _ = try AppearanceFiles.save(store, AppearanceSettings(), logo: Data("invalid".utf8))
    }
    try check(
      AppearanceFiles.load(store).LogoFile == branded.LogoFile
        && store.exists("branding/" + branded.LogoFile), "Logo rollback preserves original")
    _ = try AppearanceFiles.save(store, AppearanceSettings(), logo: nil)
    try check(!store.exists("branding/" + branded.LogoFile), "Logo reset removes owned image")
    var huge = png
    huge[16] = 0
    huge[17] = 0
    huge[18] = 8
    huge[19] = 1
    try store.writeData("sample.png", huge)
    try rejects("Oversized image rejected before decode") {
      _ = try AppearanceFiles.logoData(store.url("sample.png"))
    }
    let log = LocalLog()
    for i in 0..<3000 { log.add("length \(i)") }
    try check(log.text.split(separator: "\n").count == 2500, "Bounded diagnostics")
    log.clear()
    try check(log.text.isEmpty, "Clear diagnostics")
    for _ in 0..<25 {
      try e.backup()
      try e.recover()
    }
    try check(
      store.files().filter { $0.hasPrefix("backup-") }.count == 20, "Bounded backup retention")
    try e.backup()
    let pendingCount = store.files().filter { $0.hasPrefix("backup-") }.count
    store.prune()
    try check(
      store.files().filter { $0.hasPrefix("backup-") }.count == pendingCount,
      "Pending journal prevents cleanup")
    try e.recover()
    try uiTests(store)
    print(
      "PASS: \(count) checks; simulated scan, failure/recovery, privacy, settings, 100 UI lifecycle cycles. No hardware accessed."
    )
  }
  static func uiTests(_ store: Store) throws {
    try UIWorkflowTests.run(store)
    let controller = AppController(store: store, preview: true)
    controller.makeWindow()
    controller.preference = FormatPreference(Mode: .combo, RcoChip: "M")
    controller.readyMessage()
    controller.rebuild()
    var saves = 0
    let first = FormatEditor(nil) { _ in saves += 1 }
    try check(first.selected == nil, "First startup has no default")
    let buttons = descendants(first.dialog.contentView!).compactMap { $0 as? ActionButton }
    try check(
      buttons.first(where: { $0.title == "Spara och börja" })?.isEnabled == false,
      "First save disabled until selection")
    first.dialog.close()
    let selected = FormatEditor(nil) { p in
      saves += 1
      try store.write("formatval.json", p)
    }
    DispatchQueue.main.async {
      selected.choices.selectItem(at: 2)
      selected.chip.selectItem(at: 1)
      NSApp.sendAction(
        selected.choices.action!, to: selected.choices.target, from: selected.choices)
      descendants(selected.dialog.contentView!).compactMap { $0 as? ActionButton }.first {
        $0.title == "Spara och börja"
      }!.performClick(nil)
    }
    try check(selected.dialog.run(relativeTo: controller.window), "First-start modal saves")
    try check(
      store.preference()?.Mode == .rco && store.preference()?.RcoChip == "E",
      "First-start selection reaches disk")
    saves = 0
    let before = AppearanceFiles.load(store)
    let editor = AppearanceEditor(store: store, settings: before) { _ in saves += 1 }
    editor.color.stringValue = "#FF0000"
    editor.updatePreview()
    editor.dialog.close()
    try check(
      saves == 0 && AppearanceFiles.load(store).AccentHex == before.AccentHex,
      "Cancel changes nothing")
    for _ in 0..<20 {
      autoreleasepool {
        let f = FormatEditor(nil) { _ in }
        let a = AppearanceEditor(store: store, settings: AppearanceSettings()) { _ in }
        let r = ReferenceEditor(store: store)
        f.dialog.close()
        a.dialog.close()
        r.dialog.close()
        controller.rebuild()
      }
    }
    autoreleasepool { RunLoop.current.run(until: Date().addingTimeInterval(0.05)) }
    let footprintBefore = br_memory_footprint()
    let windowsBefore = NSApp.windows.count
    for cycle in 0..<100 {
      try autoreleasepool {
        weak var weakFormat: FormatEditor?
        weak var weakAppearance: AppearanceEditor?
        weak var weakReference: ReferenceEditor?
        do {
          let f = FormatEditor(nil) { _ in }
          let a = AppearanceEditor(store: store, settings: AppearanceSettings()) { _ in }
          let r = ReferenceEditor(store: store)
          weakFormat = f
          weakAppearance = a
          weakReference = r
          f.dialog.close()
          a.dialog.close()
          r.dialog.close()
        }
        try check(
          weakFormat == nil && weakAppearance == nil && weakReference == nil,
          "Editors release after close")
        controller.rebuild()
        if cycle % 10 == 0 { RunLoop.current.run(until: Date().addingTimeInterval(0.01)) }
      }
    }
    autoreleasepool { RunLoop.current.run(until: Date().addingTimeInterval(0.05)) }
    let footprintAfter = br_memory_footprint()
    try check(NSApp.windows.count <= windowsBefore + 2, "Closed windows do not accumulate")
    try check(
      footprintBefore > 0 && footprintAfter <= footprintBefore + 48 * 1024 * 1024,
      "Bounded UI memory growth after warmup")
    print(
      "UI memory footprint: \(footprintBefore/1024) → \(footprintAfter/1024) KiB; windows: \(windowsBefore) → \(NSApp.windows.count)"
    )
    controller.window.close()
  }
  static func descendants(_ view: NSView) -> [NSView] {
    [view] + view.subviews.flatMap(descendants)
  }
  static func snapshot(_ window: NSWindow, to url: URL) throws {
    window.makeKeyAndOrderFront(nil)
    window.contentView?.layoutSubtreeIfNeeded()
    window.displayIfNeeded()
    CATransaction.flush()
    RunLoop.current.run(until: Date().addingTimeInterval(0.1))
    guard let view = window.contentView,
      let rep = view.bitmapImageRepForCachingDisplay(in: view.bounds)
    else { throw fail("RENDER", "Cannot capture view") }
    view.cacheDisplay(in: view.bounds, to: rep)
    guard let data = rep.representation(using: .png, properties: [:]) else {
      throw fail("RENDER", "Cannot encode PNG")
    }
    try data.write(to: url)
  }
  static func render(_ folder: URL) throws {
    try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
    let store = try tempStore()
    defer { try? FileManager.default.removeItem(at: store.root) }
    let c = AppController(store: store, preview: true)
    c.makeWindow()
    c.preference = FormatPreference(Mode: .combo, RcoChip: "M")
    c.info = ReaderInfo(
      board: "OMNIKEY 5427 CK", id: "", firmware: "", profile: "Standardformat", pending: false,
      ready: true)
    c.readyMessage()
    c.rebuild()
    try snapshot(c.window, to: folder.appendingPathComponent("marinbla-klar.png"))
    c.outputs = ("025800772", "051501029")
    c.readyMessage()
    c.notice = "Konstruerade testnummer – inga verkliga brickor."
    c.rebuild()
    try snapshot(c.window, to: folder.appendingPathComponent("marinbla-resultat.png"))
    c.window.setFrame(NSRect(origin: c.window.frame.origin, size: c.window.minSize), display: true)
    try snapshot(c.window, to: folder.appendingPathComponent("minimum-resultat.png"))
    c.outputs = nil
    c.busy = true
    c.token = CancelToken()
    c.progress("place-m", reference: false)
    try snapshot(c.window, to: folder.appendingPathComponent("minimum-steg-1.png"))
    c.progress("place-e", reference: false)
    try snapshot(c.window, to: folder.appendingPathComponent("minimum-steg-2.png"))
    c.busy = false
    c.token = nil
    c.window.setContentSize(NSSize(width: 900, height: 650))
    c.info = nil
    c.readyMessage()
    c.rebuild()
    try snapshot(c.window, to: folder.appendingPathComponent("ingen-lasare.png"))
    for mode in [AccessMode.rco, .axema, .bewator] {
      c.preference = FormatPreference(Mode: mode, RcoChip: "M")
      c.info = ReaderInfo(board: "Simulated", id: "", firmware: "", profile: "Standardformat", pending: false, ready: true)
      c.outputs = mode == .rco ? ("025800772", "0123456789")
        : mode == .axema ? ("12345678901234", "") : ("1234567890123", "")
      c.readyMessage()
      c.rebuild()
      try snapshot(c.window, to: folder.appendingPathComponent(mode.rawValue + ".png"))
    }
    c.clearResults()
    c.info = nil
    c.readyMessage()
    c.appearance.Organization = String(repeating: "Långt namn ", count: 8)
    c.appearance.Subtitle = String(repeating: "Valfri underrubrik. ", count: 6)
    c.window.setFrame(NSRect(origin: c.window.frame.origin, size: c.window.minSize), display: true)
    c.instruction = "Anslutningen behöver kontrolleras"
    c.hint = "Kontrollera USB-kabeln och godkänn tillbehöret om macOS frågar. Låt läsaren vara inkopplad och välj sedan Anslut läsare. Om problemet kvarstår kan du spara en felrapport under Hjälp."
    c.rebuild()
    try snapshot(c.window, to: folder.appendingPathComponent("minimum-lang-branding.png"))
    let f = FormatEditor(nil) { _ in }
    try snapshot(f.dialog, to: folder.appendingPathComponent("forsta-start.png"))
    f.dialog.close()
    let a = AppearanceEditor(store: store, settings: AppearanceSettings()) { _ in }
    try snapshot(a.dialog, to: folder.appendingPathComponent("utseende.png"))
    a.dialog.close()
    let r = ReferenceEditor(store: store)
    try snapshot(r.dialog, to: folder.appendingPathComponent("referensprov.png"))
    r.dialog.close()
    let workflow = c.makeWorkflowDialog()
    try snapshot(workflow, to: folder.appendingPathComponent("arbetsflode.png"))
    workflow.close()
    c.window.close()
    print("Rendered 14 interface images with synthetic data; no hardware accessed.")
  }
}
