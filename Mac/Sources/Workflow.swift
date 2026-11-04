import AppKit

// All scheduling state belongs to the UI thread. A slow USB probe cannot block typing,
// stack probes, or overwrite a newer wake/reconnect decision when it eventually returns.
final class USBMonitor {
  private let queue = DispatchQueue(label: "se.tobtech.bricklasaren.usb-watch", qos: .utility)
  private let probe: () -> USBState
  private let timeout: TimeInterval
  private var generation: UInt64 = 0
  private var flight: UInt64 = 0
  private var stopped = false
  private var deadline: DispatchWorkItem?
  private(set) var inFlight = false
  private(set) var timedOut = false
  init(timeout: TimeInterval = 3, probe: @escaping () -> USBState = USBState.current) {
    self.timeout = timeout
    self.probe = probe
  }
  deinit { deadline?.cancel() }
  func invalidate() {
    dispatchPrecondition(condition: .onQueue(.main))
    generation &+= 1
  }
  func stop() {
    invalidate()
    stopped = true
    deadline?.cancel()
    deadline = nil
  }
  func poll(_ receive: @escaping (USBState) -> Void, onTimeout: @escaping () -> Void) {
    dispatchPrecondition(condition: .onQueue(.main))
    guard !inFlight, !stopped else { return }
    inFlight = true
    flight &+= 1
    let currentFlight = flight
    let started = generation
    let probe = self.probe
    let deadline = DispatchWorkItem { [weak self] in
      guard let self, self.inFlight, self.flight == currentFlight, !self.stopped else { return }
      self.generation &+= 1
      self.timedOut = true
      // Keep inFlight true until the native call returns; never pile up blocked probes.
      onTimeout()
    }
    self.deadline = deadline
    DispatchQueue.main.asyncAfter(deadline: .now() + timeout, execute: deadline)
    queue.async { [weak self] in
      let state = probe()
      DispatchQueue.main.async { [weak self] in
        guard let self else { return }
        self.inFlight = false
        self.deadline?.cancel()
        self.deadline = nil
        guard !self.stopped, started == self.generation else { return }
        self.timedOut = false
        receive(state)
      }
    }
  }
}

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
    copy.setAccessibilityLabel("Kopiera automatiskt")
    let sounds = NSButton(
      checkboxWithTitle: "Ljud för lägg på, lyft bort och klart", target: nil, action: nil)
    sounds.state = workflow.sounds ? .on : .off
    let channel = NSPopUpButton()
    channel.addItems(withTitles: ["Stabil", "Förhandsversioner"])
    channel.selectItem(at: workflow.channel == "preview" ? 1 : 0)
    channel.setAccessibilityLabel("Uppdateringskanal")
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
    let save = button("Spara", primary: true) { [weak self, weak d] in
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
      }
    root.addArrangedSubview(stack([
      save, button("Avbryt") { [weak d] in d?.finish(false) },
    ], horizontal: true))
    setRoot(d, root)
    d.initialFirstResponder = copy
    return d
  }
  @objc func shortcutRead() { if !busy && info?.ready == true { scan() } }
  @objc func shortcutCancel() {
    if let dialog = NSApp.modalWindow as? Dialog { dialog.finish(false); return }
    guard let token else { return }
    token.cancel()
    if busy { clearResults(); notice = "Avbryter och återställer läsaren …"; rebuild() }
  }
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
    let monitor = USBMonitor()
    usbMonitor = monitor
    wakeObserver = NSWorkspace.shared.notificationCenter.addObserver(
      forName: NSWorkspace.didWakeNotification, object: nil, queue: .main
    ) { [weak self] _ in self?.invalidateConnection() }
    connectionTimer = Timer.scheduledTimer(withTimeInterval: 2, repeats: true) { [weak self] _ in
      guard let self else { return }
      self.pollUSB()
      if self.shouldReconnect {
        self.reconnectPending = false
        self.connect()
      }
    }
    pollUSB()
  }
  var shouldReconnect: Bool {
    reconnectPending && !busy && usbMonitor?.timedOut != true && NSApp.modalWindow == nil
  }
  func pollUSB() {
    usbMonitor?.poll({ [weak self] current in
      guard let self else { return }
      let previous = self.lastUSB
      self.lastUSB = current
      let changed = previous.map { current.count != $0.count || current.fingerprint != $0.fingerprint } ?? (current.count != 1)
      // Internal USB mode transitions during connection are expected. A scan always invalidates.
      if changed && (!self.busy || self.token != nil) { self.invalidateConnection() }
    }, onTimeout: { [weak self] in
      guard let self else { return }
      self.invalidateConnection()
      self.readyMessage()
      self.rebuild()
    })
  }
  func invalidateConnection() {
    usbMonitor?.invalidate()
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
