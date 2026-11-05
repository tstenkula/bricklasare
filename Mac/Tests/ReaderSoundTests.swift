import AppKit

extension SelfTests {
  static func readerSoundTests() throws {
    for board: UInt8 in [6, 10] {
      let store = try tempStore()
      defer { try? FileManager.default.removeItem(at: store.root) }
      let (reader, mock) = try engine(store, board: board)
      let original = mock.settings[ReaderSound.oid]!.value
      try check(try ReaderSound.read(reader), "Hardware sound is read independently of computer volume")
      try check(try !ReaderSound.set(false, reader), "Reader mute confirmed")
      let quiet = mock.settings[ReaderSound.oid]!.value
      try check(try quiet == ReaderSound.quiet(original), "Only public buzzer bits changed")
      try check(quiet.dropFirst(39) == original.dropFirst(39), "Internal LED/buzzer sequences untouched")
      let writes = mock.mutations
      try check(try !ReaderSound.set(false, reader) && mock.mutations == writes, "Repeated mute does not write flash")
      try check(try ReaderSound.set(true, reader) && mock.settings[ReaderSound.oid]!.value == original,
                "Exact original LEDs, timing and sound restored")
      mock.soundFailure = 1
      try rejects("Lost response fails sound change") { _ = try ReaderSound.set(false, reader) }
      try check(try mock.settings[ReaderSound.oid]!.value == original && ReaderSound.read(reader),
                "Partial writes roll back before reporting failure")
      let saved = ReaderSoundBackup(ReaderId: reader.id, Firmware: reader.firmware,
        Original: Codec.text(Array(original.prefix(39))), Pending: Codec.text(Array(original.prefix(39))))
      try store.write(ReaderSound.name(reader), saved)
      mock.settings[ReaderSound.oid]!.value = quiet
      try check(try ReaderSound.read(reader) && mock.settings[ReaderSound.oid]!.value == original,
                "Crash journal restores sound at next connection")
      try check(!WorkflowSettings.load(store).sounds, "Hardware changes do not enable app sounds")
      try store.remove(ReaderSound.name(reader))
      mock.settings[ReaderSound.oid]!.value = quiet
      try check(try ReaderSound.set(true, reader), "Initially muted reader can enable an existing safe card-event pattern")
      try check(try ReaderSound.quiet(mock.settings[ReaderSound.oid]!.value) == quiet, "Enabling initial sound preserves all LEDs, durations and internal data")
      mock.changingIdentity = true
      let beforeIdentity = mock.mutations
      try rejects("Changed reader identity blocks sound writes") { _ = try ReaderSound.set(false, reader) }
      try check(mock.mutations == beforeIdentity, "Replacement reader untouched")
      mock.changingIdentity = false
      mock.settings[ReaderSound.oid]!.value = [UInt8](repeating: 0, count: 129)
      try rejects("Unknown table format fails closed") { _ = try ReaderSound.read(reader) }
    }
    let store = try tempStore()
    defer { try? FileManager.default.removeItem(at: store.root) }
    let controller = AppController(store: store, preview: true)
    controller.makeWindow()
    defer { controller.window.close() }
    controller.readerSound = true
    controller.info = ReaderInfo(board: "Simulated", id: "", firmware: "", profile: "Standardformat", pending: false, ready: true)
    controller.rebuild()
    controller.toggleAppSound()
    try check(controller.workflow.sounds && controller.readerSound == true,
              "App button leaves hardware state unchanged")
    try check(WorkflowSettings.load(store).sounds, "App preference persists")
    guard let view = controller.readerView else { throw fail("TEST", "Missing main view") }
    controller.window.contentView?.layoutSubtreeIfNeeded()
    let readerFrame = view.readerSoundButton.convert(view.readerSoundButton.bounds, to: nil)
    let appFrame = view.appSoundButton.convert(view.appSoundButton.bounds, to: nil)
    let helpFrame = view.helpButton.convert(view.helpButton.bounds, to: nil)
    try check(abs(readerFrame.height - helpFrame.height) < 0.5 && abs(appFrame.height - helpFrame.height) < 0.5,
              "Sound controls and Help share the same height")
    try check(abs(appFrame.minY - helpFrame.minY) < 0.5 && helpFrame.minX - appFrame.maxX >= 10,
              "Sound controls align with Help and keep a clear gap")
    try check(!view.appSoundButton.muted && !view.readerSoundButton.muted, "Both on icons")
    try check(view.appSoundButton.image?.size == NSSize(width: 24, height: 24)
      && view.appSoundButton.image?.representations.isEmpty == false
      && view.readerSoundButton.image?.size == view.appSoundButton.image?.size,
      "Pinned stock symbols use identical nonempty image slots")
    controller.readerSound = false; controller.rebuild()
    try check(view.readerSoundButton.muted && !view.appSoundButton.muted, "Reader muted and app on have distinct states")
    controller.busy = true; controller.rebuild()
    controller.toggleAppSound()
    try check(!view.readerSoundButton.isEnabled && !view.appSoundButton.isEnabled && controller.workflow.sounds,
              "Busy sound controls do not interrupt another operation")
    controller.busy = false; controller.info = nil; controller.rebuild()
    try check(!view.readerSoundButton.isEnabled && view.appSoundButton.isEnabled,
              "App sound remains usable without a reader")
  }
}
