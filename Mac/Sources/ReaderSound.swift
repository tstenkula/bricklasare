import AppKit

struct ReaderSoundBackup: Codable {
  var Schema = 1
  var ReaderId: String
  var Firmware: String
  var Original: String
  var Pending: String?
}
enum ReaderSound {
  // HID 5127-903 B.7 A.5.18/B.2.6. Preserve seven internal sequences and every LED/timing bit.
  static let oid = "02020104010100"
  static func validate(_ table: [UInt8]) throws {
    try require(table.count == 130, "Läsarens ljudformat stöds inte.", code: "SOUND_FORMAT")
    for sequence in 0..<3 {
      for step in 0..<6 {
        try require(table[sequence * 13 + step * 2] & ~0x13 == 0,
                    "Läsarens ljudformat stöds inte.", code: "SOUND_FORMAT")
      }
    }
  }
  static func quiet(_ table: [UInt8]) throws -> [UInt8] {
    try validate(table)
    var next = table
    for sequence in 0..<3 { for step in 0..<6 { next[sequence * 13 + step * 2] &= 0xEF } }
    return next
  }
  static func enabled(_ table: [UInt8]) throws -> Bool {
    try validate(table)
    for sequence in 0..<3 {
      for step in 0..<6 {
        let index = sequence * 13 + step * 2
        if table[index + 1] == 0 { break }
        if table[index] & 0x10 != 0 { return true }
      }
    }
    return false
  }
  static func name(_ reader: ReaderEngine) -> String { "lasarljud-\(reader.id).json" }
  static func table(_ reader: ReaderEngine) throws -> [UInt8] {
    let value = try reader.get(oid)
    try require(value.tag == 4, "Läsaren kunde inte lämna sitt ljudläge.", code: "SOUND_FORMAT")
    try validate(value.value)
    return value.value
  }
  static func backup(_ reader: ReaderEngine) throws -> ReaderSoundBackup? {
    guard reader.store.exists(name(reader)) else { return nil }
    let b: ReaderSoundBackup = try reader.store.read(name(reader))
    try require(b.Schema == 1 && b.ReaderId == reader.id && b.Firmware == reader.firmware
      && (try Codec.hex(b.Original)).count == 39
      && (try b.Pending.map { try Codec.hex($0).count == 39 } ?? true),
      "Ljudinställningen gäller inte den här läsaren och firmware.", code: "SOUND_BACKUP")
    return b
  }
  static func check(_ reader: ReaderEngine) throws {
    try require(reader.connected && !reader.pending, "Återställ läsaren innan ljudet ändras.", code: "RECOVERY")
    try reader.checkIdentity()
  }
  static func write(_ target: [UInt8], _ reader: ReaderEngine) throws {
    try validate(target)
    for sequence in 0..<3 {
      try check(reader)
      let command: [UInt8] = [255, 104, 3, UInt8(0x40 + sequence), 13]
        + Array(target[(sequence * 13)..<(sequence * 13 + 13)])
      try require(try Codec.success(reader.send(command)).isEmpty, "Läsaren bekräftade inte ljudändringen.")
    }
    // Store and automatic-control commands are separate; never trigger a test beep.
    try require(try Codec.success(reader.send([255, 104, 3, 128])).isEmpty,
                "Läsarens automatiska indikering kunde inte återställas.")
    try check(reader)
    try reader.apply()
    try require(try table(reader) == target, "Läsarens ljudändring kunde inte verifieras.", code: "SOUND_VERIFY")
  }
  static func read(_ reader: ReaderEngine) throws -> Bool {
    try check(reader)
    var current = try table(reader)
    if var saved = try backup(reader), let pending = saved.Pending {
      current.replaceSubrange(0..<39, with: try Codec.hex(pending))
      try write(current, reader)
      saved.Pending = nil
      try reader.store.write(name(reader), saved)
    }
    return try enabled(current)
  }
  static func set(_ enabled: Bool, _ reader: ReaderEngine) throws -> Bool {
    _ = try read(reader)
    try check(reader)
    let before = try table(reader)
    if try Self.enabled(before) == enabled { return enabled }
    var target = try quiet(before)
    var saved = try backup(reader)
    if enabled {
      if saved == nil {
        // Preserve LED colours/timing; an existing off-step is needed to finish the beep.
        try require(before[14] != 0 && before[16] != 0,
          "Läsaren saknar ett pipmönster som kan aktiveras utan att ändra ljussekvensen. Be IT konfigurera ljudsekvensen i läsaren.", code: "SOUND_ORIGINAL")
        target[13] |= 0x10
        saved = ReaderSoundBackup(ReaderId: reader.id, Firmware: reader.firmware,
                                  Original: Codec.text(Array(target.prefix(39))))
      }
      let bytes = try Codec.hex(saved!.Original)
      for i in 0..<39 {
        let mask: UInt8 = i % 13 < 12 && (i % 13) % 2 == 0 ? 0xEF : 0xFF
        try require(before[i] & mask == bytes[i] & mask,
          "Läsarens ljus- eller ljudsekvens har ändrats utanför appen. Kontrollera inställningarna innan ljudet återställs.")
      }
      target.replaceSubrange(0..<39, with: bytes)
      try validate(target)
    } else {
      saved = ReaderSoundBackup(ReaderId: reader.id, Firmware: reader.firmware,
                                Original: Codec.text(Array(before.prefix(39))))
    }
    guard var transaction = saved else { throw fail("SOUND_BACKUP", "Ljudinställningen kunde inte sparas.") }
    transaction.Pending = Codec.text(Array(before.prefix(39)))
    try reader.store.write(name(reader), transaction)
    do {
      try write(target, reader)
      transaction.Pending = nil
      try reader.store.write(name(reader), transaction)
      return try Self.enabled(target)
    } catch {
      let originalError = error
      do {
        try check(reader)
        try write(before, reader)
        transaction.Pending = nil
        try reader.store.write(name(reader), transaction)
      } catch { } // Keep the durable journal if rollback cannot be verified.
      throw originalError
    }
  }
}

// The same pinned Lucide assets are used on both platforms, with native template tinting.
final class SoundButton: NSButton {
  var actionBlock: (() -> Void)?
  let readerIcon: Bool
  var muted = false { didSet { image = muted ? Self.offIcon : Self.onIcon } }
  var color = Theme.ink { didSet {
    contentTintColor = color
    if color != oldValue { tintIcons() }
    needsDisplay = true
  } }
  private var tintedOn: NSImage?, tintedOff: NSImage?
  private func tintIcons() {
    func tint(_ source: NSImage) -> NSImage {
      let result = NSImage(size: source.size)
      result.lockFocus()
      source.draw(in: NSRect(origin: .zero, size: source.size))
      color.setFill()
      NSRect(origin: .zero, size: source.size).fill(using: .sourceAtop)
      result.unlockFocus()
      return result
    }
    tintedOn = tint(Self.onIcon); tintedOff = tint(Self.offIcon)
  }
  private static func icon(_ name: String) -> NSImage {
    guard let url = Bundle.main.resourceURL?.appendingPathComponent("Lucide/" + name + ".png"),
          let image = NSImage(contentsOf: url) else { return NSImage(size: NSSize(width: 24, height: 24)) }
    image.size = NSSize(width: 24, height: 24)
    image.isTemplate = true
    return image
  }
  private static let onIcon = icon("volume-2"), offIcon = icon("volume-off")
  init(reader: Bool, action: @escaping () -> Void) {
    readerIcon = reader
    actionBlock = action
    super.init(frame: .zero)
    title = reader ? "Läsare" : "App"
    image = Self.onIcon
    imagePosition = .imageAbove
    imageScaling = .scaleNone
    alignment = .center
    font = .systemFont(ofSize: 9)
    contentTintColor = color
    tintIcons()
    target = self; self.action = #selector(clicked)
    setButtonType(.momentaryPushIn)
    isBordered = false
    translatesAutoresizingMaskIntoConstraints = false
    widthAnchor.constraint(equalToConstant: 48).isActive = true
    heightAnchor.constraint(equalToConstant: 48).isActive = true
  }
  required init?(coder: NSCoder) { fatalError("init(coder:) is not supported") }
  @objc private func clicked() { actionBlock?() }
  override func draw(_ dirtyRect: NSRect) {
    (isHighlighted ? Theme.mix(.white, .black, 0.07) : .white).setFill()
    NSBezierPath(roundedRect: bounds, xRadius: 7, yRadius: 7).fill()
    // Fixed 48pt button: 5pt above a 22pt icon, 5pt before the 12pt text line.
    // Draw the original stock image; changing state never changes its layout slot.
    let icon = muted ? tintedOff : tintedOn
    icon?.draw(in: NSRect(x: (bounds.width - 22) / 2, y: 5, width: 22, height: 22),
               from: .zero, operation: .sourceOver, fraction: isEnabled ? 1 : 0.45,
               respectFlipped: true, hints: nil)
    let paragraph = NSMutableParagraphStyle(); paragraph.alignment = .center
    title.draw(in: NSRect(x: 0, y: 32, width: bounds.width, height: 12),
               withAttributes: [.font: font ?? NSFont.systemFont(ofSize: 9),
                                .foregroundColor: isEnabled ? color : Theme.muted,
                                .paragraphStyle: paragraph])
    if window?.firstResponder === self {
      NSColor.keyboardFocusIndicatorColor.setStroke()
      let focus = NSBezierPath(roundedRect: bounds.insetBy(dx: 2, dy: 2), xRadius: 5, yRadius: 5)
      focus.lineWidth = 2; focus.stroke()
    }
  }
  override func becomeFirstResponder() -> Bool {
    let result = super.becomeFirstResponder(); needsDisplay = true; return result
  }
  override func resignFirstResponder() -> Bool {
    let result = super.resignFirstResponder(); needsDisplay = true; return result
  }
}

extension AppController {
  func toggleAppSound() {
    guard !busy else { return }
    var next = workflow
    next.sounds.toggle()
    do { try store.write("arbetsflode.json", next); workflow = next; rebuild() }
    catch { showError(error) }
  }
  func toggleReaderSound() {
    guard !busy, !reconnectPending, info?.ready == true, usbMonitor?.timedOut != true,
          let current = readerSound else { return }
    busy = true; rebuild()
    worker.async { [weak self] in
      guard let self else { return }
      guard let reader = self.engine else {
        DispatchQueue.main.async { self.readerSound = nil; self.busy = false; self.rebuild() }
        return
      }
      do {
        let result = try ReaderSound.set(!current, reader)
        DispatchQueue.main.async {
          self.readerSound = self.reconnectPending || self.usbMonitor?.timedOut == true ? nil : result
          self.busy = false; self.rebuild()
        }
      } catch {
        DispatchQueue.main.async {
          self.readerSound = nil; self.busy = false
          self.notice = (error as? ReaderError)?.message ?? "Läsarens ljud kunde inte ändras. Anslut läsaren igen för att kontrollera läget."
          self.rebuild()
        }
      }
    }
  }
}
