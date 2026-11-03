import AppKit

struct WorkflowSettings: Codable {
  var copy = 0
  var sounds = false
  var channel = "stable"
  var autoCheck = true
  static func load(_ store: Store) -> WorkflowSettings {
    guard let value: WorkflowSettings = try? store.read("arbetsflode.json"),
      (0...3).contains(value.copy), ["stable", "preview"].contains(value.channel)
    else { return WorkflowSettings() }
    return value
  }
  func copyValue(_ values: (String, String), combo: Bool) -> String? {
    switch copy {
    case 1: return values.0
    case 2: return values.1.isEmpty ? nil : values.1
    case 3: return combo ? "mR: \(values.0)\neR: \(values.1)" : nil
    default: return nil
    }
  }
}
extension AppController {
  func cue(_ phase: Int) {
    guard workflow.sounds, (1...3).contains(phase) else { return }
    let name = ["Pop", "Tink", "Glass"][phase - 1]
    if let sound = NSSound(named: NSSound.Name(name)) {
      sound.volume = 0.25
      sound.play()
    }
  }
  func editWorkflow() {
    let dialog = makeWorkflowDialog()
    _ = dialog.run(relativeTo: window)
  }
  func makeWorkflowDialog() -> Dialog {
    let d = Dialog("Arbetsflöde och uppdateringar", width: 660, height: 560)
    let copy = NSPopUpButton()
    copy.addItems(withTitles: [
      "Av", "Första numret", "Andra numret (om det finns)", "Båda med rubriker (MIFARE + EM)",
    ])
    copy.selectItem(at: workflow.copy)
    let sounds = NSButton(
      checkboxWithTitle: "Ljud för lägg på, lyft bort och klart", target: nil, action: nil)
    sounds.state = workflow.sounds ? .on : .off
    let channel = NSPopUpButton()
    channel.addItems(withTitles: ["Stabil", "Förhandsversioner"])
    channel.selectItem(at: workflow.channel == "preview" ? 1 : 0)
    let auto = NSButton(
      checkboxWithTitle: "Sök efter nya versioner när appen startar", target: nil,
      action: nil)
    auto.state = workflow.autoCheck ? .on : .off
    let root = stack(
      [
        label("Arbetsflöde", size: 24, bold: true),
        label("Kopiera automatiskt efter en helt avslutad läsning"), copy, sounds,
        label("⌘R: Läs • Esc: Avbryt • ⌘1 / 2 / 3: Kopiera", size: 12),
        label("Uppdateringar", size: 20, bold: true), channel, auto,
        label(
          "Du väljer själv när en uppdatering ska installeras.",
          size: 12),
      ], spacing: 16)
    root.addArrangedSubview(
      button("Spara") { [weak self, weak d] in
        guard let self else { return }
        let next = WorkflowSettings(
          copy: copy.indexOfSelectedItem, sounds: sounds.state == .on,
          channel: channel.indexOfSelectedItem == 1 ? "preview" : "stable",
          autoCheck: auto.state == .on)
        do {
          try self.store.write("arbetsflode.json", next)
          self.workflow = next
          d?.finish(true)
        } catch { showError(error) }
      })
    setRoot(d, root)
    return d
  }
  @objc func shortcutRead() { if !busy && info?.ready == true { scan() } }
  @objc func shortcutCancel() { token?.cancel() }
  @objc func shortcutFirst() { if let outputs { copy(outputs.0) } }
  @objc func shortcutSecond() { if let outputs, !outputs.1.isEmpty { copy(outputs.1) } }
  @objc func shortcutBoth() {
    if let outputs, preference?.Mode == .combo { copy("mR: \(outputs.0)\neR: \(outputs.1)") }
  }
  @objc func shortcutUpdate() { checkUpdates() }
  func addWorkflowMenu(_ menu: NSMenu) {
    let item = NSMenuItem()
    item.title = "Läsare"
    let reader = NSMenu(title: "Läsare")
    item.submenu = reader
    for (title, action, key) in [
      ("Läs bricka", #selector(shortcutRead), "r"), ("Avbryt", #selector(shortcutCancel), "\u{1b}"),
      ("Kopiera första", #selector(shortcutFirst), "1"),
      ("Kopiera andra", #selector(shortcutSecond), "2"),
      ("Kopiera båda", #selector(shortcutBoth), "3"),
      ("Sök efter uppdatering", #selector(shortcutUpdate), ""),
    ] {
      let entry = NSMenuItem(title: title, action: action, keyEquivalent: key)
      entry.target = self
      if key == "\u{1b}" { entry.keyEquivalentModifierMask = [] }
      reader.addItem(entry)
    }
    menu.addItem(item)
  }
  func startConnectionWatch() {
    lastUSB = USBState.current()
    wakeObserver = NSWorkspace.shared.notificationCenter.addObserver(
      forName: NSWorkspace.didWakeNotification, object: nil, queue: .main
    ) { [weak self] _ in self?.invalidateConnection() }
    connectionTimer = Timer.scheduledTimer(withTimeInterval: 2, repeats: true) { [weak self] _ in
      guard let self else { return }
      let current = USBState.current()
      if let previous = self.lastUSB,
        current.count != previous.count || current.fingerprint != previous.fingerprint
      {
        self.lastUSB = current
        // Internal USB mode transitions during connection are expected. A scan always invalidates.
        if !self.busy || self.token != nil { self.invalidateConnection() }
      }
      if self.reconnectPending && !self.busy && NSApp.modalWindow == nil {
        self.reconnectPending = false
        self.connect()
      }
    }
  }
  func invalidateConnection() {
    clearResults()
    info = nil
    token?.cancel()
    reconnectPending = true
    connection = "USB-anslutningen behöver kontrolleras på nytt"
    rebuild()
  }
  func diagnostics() {
    closeModalParents()
    guard info != nil else {
      connect()
      return
    }
    readerAction(
      "Kontrollerar kontakt, identitet, profil och USB-skydd",
      success:
        "Kontroller godkända. Läs en bricka för att kontrollera resultatet i passersystemet. Spara felrapporten via Hjälp om problemet återkommer."
    ) { reader in
      try reader.checkIdentity()
      try reader.verifyProtection()
      try require(
        !reader.pending, "En avbruten läsning behöver återställas under IT-inställningar.")
      try require(reader.ready, "Kontrollera läsarprofilen under IT-inställningar.")
    }
  }
}
