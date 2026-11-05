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
      for changedIndex in [0, 1, 12] {
        var changed = quiet
        changed[changedIndex] ^= 1
        mock.settings[ReaderSound.oid]!.value = changed
        try store.write(ReaderSound.name(reader), saved)
        let journalBytes = try Data(contentsOf: store.url(ReaderSound.name(reader)))
        let mutations = mock.mutations
        try rejects("Pending sound recovery rejects external LED, timing and repeat changes") {
          _ = try ReaderSound.read(reader)
        }
        try check(mock.mutations == mutations && mock.settings[ReaderSound.oid]!.value == changed
          && (try Data(contentsOf: store.url(ReaderSound.name(reader)))) == journalBytes,
          "Rejected recovery preserves live state and the pending journal without writes")
      }
      var damaged = saved
      var badPending = Array(original.prefix(39))
      badPending[0] |= 0x80
      damaged.Pending = Codec.text(badPending)
      mock.settings[ReaderSound.oid]!.value = quiet
      try store.write(ReaderSound.name(reader), damaged)
      let damagedBytes = try Data(contentsOf: store.url(ReaderSound.name(reader)))
      let beforeDamaged = mock.mutations
      try rejects("Corrupt pending sound flags never enter a reader command") {
        _ = try ReaderSound.read(reader)
      }
      try check(mock.mutations == beforeDamaged && mock.settings[ReaderSound.oid]!.value == quiet
        && (try Data(contentsOf: store.url(ReaderSound.name(reader)))) == damagedBytes,
        "Corrupt pending sound state remains available without reader writes")
      try store.write(ReaderSound.name(reader), saved)
      mock.settings[ReaderSound.oid]!.value = quiet
      try check(try ReaderSound.read(reader) && mock.settings[ReaderSound.oid]!.value == original,
                "Crash journal restores sound at next connection")
      try store.write(ReaderSound.name(reader), saved)
      var changedInternal = quiet
      changedInternal[39] ^= 1
      mock.settings[ReaderSound.oid]!.value = changedInternal
      var expectedRecovery = changedInternal
      expectedRecovery.replaceSubrange(0..<39, with: original.prefix(39))
      try check(try ReaderSound.read(reader) && mock.settings[ReaderSound.oid]!.value == expectedRecovery,
        "Matching recovery restores buzzer state and preserves current internal sequences")
      let recovered: ReaderSoundBackup = try store.read(ReaderSound.name(reader))
      try check(recovered.Pending == nil, "Pending journal clears only after matching readback")
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
    controller.busy = false; controller.info = nil; controller.readerSound = nil; controller.rebuild()
    try check(!view.readerSoundButton.stateKnown && view.appSoundButton.stateKnown, "Unknown reader sound does not show a confirmed on/off state")
    try check(!view.readerSoundButton.isEnabled && view.appSoundButton.isEnabled,
              "App sound remains usable without a reader")
  }
}
