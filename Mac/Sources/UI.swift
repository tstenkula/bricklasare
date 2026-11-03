import AppKit
import UniformTypeIdentifiers

final class ActionButton: NSButton {
  var actionBlock: (() -> Void)?
  let primary: Bool
  var theme: Theme
  init(
    _ title: String, primary: Bool = false, theme: Theme = Theme("#003B55"),
    action: @escaping () -> Void
  ) {
    self.primary = primary
    self.theme = theme
    self.actionBlock = action
    super.init(frame: .zero)
    self.title = title
    self.target = self
    self.action = #selector(clicked)
    setButtonType(.momentaryPushIn)
    isBordered = false
    font = .systemFont(ofSize: 13, weight: .semibold)
    translatesAutoresizingMaskIntoConstraints = false
    heightAnchor.constraint(greaterThanOrEqualToConstant: 44).isActive = true
    widthAnchor.constraint(greaterThanOrEqualToConstant: max(100, CGFloat(title.count) * 7 + 30))
      .isActive = true
    setAccessibilityLabel(title)
  }
  required init?(coder: NSCoder) { fatalError("init(coder:) is not supported") }
  @objc private func clicked() { actionBlock?() }
  override func draw(_ dirtyRect: NSRect) {
    let background = primary ? theme.accent : .white
    (isEnabled ? background : Theme.mix(background, Theme.paper, 0.6)).setFill()
    NSBezierPath(rect: bounds).fill()
    if !primary {
      NSColor(srgbRed: 0.72, green: 0.78, blue: 0.81, alpha: 1).setStroke()
      NSBezierPath(rect: bounds.insetBy(dx: 0.5, dy: 0.5)).stroke()
    }
    let color = isEnabled ? (primary ? theme.buttonText : Theme.ink) : Theme.muted
    let paragraph = NSMutableParagraphStyle()
    paragraph.alignment = .center
    let attr: [NSAttributedString.Key: Any] = [
      .font: font ?? NSFont.systemFont(ofSize: 13), .foregroundColor: color,
      .paragraphStyle: paragraph,
    ]
    let s = NSAttributedString(string: title, attributes: attr)
    let h = s.size().height
    s.draw(in: NSRect(x: 8, y: (bounds.height - h) / 2, width: bounds.width - 16, height: h + 2))
    if window?.firstResponder === self {
      NSColor.keyboardFocusIndicatorColor.setStroke()
      let p = NSBezierPath(rect: bounds.insetBy(dx: 3, dy: 3))
      p.lineWidth = 2
      p.stroke()
    }
  }
}
func label(_ text: String, size: CGFloat = 14, bold: Bool = false, color: NSColor = Theme.ink)
  -> NSTextField
{
  let l = NSTextField(wrappingLabelWithString: text)
  l.font = .systemFont(ofSize: size, weight: bold ? .semibold : .regular)
  l.textColor = color
  l.isSelectable = false
  l.maximumNumberOfLines = 0
  l.translatesAutoresizingMaskIntoConstraints = false
  l.setContentCompressionResistancePriority(.required, for: .vertical)
  return l
}
func stack(_ views: [NSView] = [], horizontal: Bool = false, spacing: CGFloat = 12) -> NSStackView {
  let s = NSStackView(views: views)
  s.orientation = horizontal ? .horizontal : .vertical
  s.alignment = horizontal ? .centerY : .leading
  s.spacing = spacing
  s.translatesAutoresizingMaskIntoConstraints = false
  return s
}
func addFull(_ parent: NSStackView, _ child: NSView) {
  parent.addArrangedSubview(child)
  child.widthAnchor.constraint(equalTo: parent.widthAnchor).isActive = true
}
func cardPanel(_ contents: NSStackView, color: NSColor, padding: CGFloat = 20) -> NSView {
  let p = NSView()
  p.wantsLayer = true
  p.layer?.backgroundColor = color.cgColor
  p.translatesAutoresizingMaskIntoConstraints = false
  p.addSubview(contents)
  NSLayoutConstraint.activate([
    contents.leadingAnchor.constraint(equalTo: p.leadingAnchor, constant: padding),
    contents.trailingAnchor.constraint(equalTo: p.trailingAnchor, constant: -padding),
    contents.topAnchor.constraint(equalTo: p.topAnchor, constant: padding),
    contents.bottomAnchor.constraint(equalTo: p.bottomAnchor, constant: -padding),
  ])
  return p
}
final class FlippedView: NSView { override var isFlipped: Bool { true } }
func setRoot(_ window: NSWindow, _ root: NSStackView) {
  let scroll = NSScrollView()
  scroll.hasVerticalScroller = true
  scroll.drawsBackground = true
  scroll.backgroundColor = Theme.paper
  let document = FlippedView()
  document.translatesAutoresizingMaskIntoConstraints = false
  document.addSubview(root)
  scroll.documentView = document
  window.contentView = scroll
  NSLayoutConstraint.activate([
    document.widthAnchor.constraint(equalTo: scroll.contentView.widthAnchor),
    root.leadingAnchor.constraint(equalTo: document.leadingAnchor, constant: 28),
    root.trailingAnchor.constraint(equalTo: document.trailingAnchor, constant: -28),
    root.topAnchor.constraint(equalTo: document.topAnchor, constant: 26),
    root.bottomAnchor.constraint(equalTo: document.bottomAnchor, constant: -26),
  ])
}
final class Dialog: NSWindow, NSWindowDelegate {
  var accepted = false
  var retained: AnyObject?
  init(_ title: String, width: CGFloat = 620, height: CGFloat = 500) {
    super.init(
      contentRect: NSRect(x: 0, y: 0, width: width, height: height),
      styleMask: [.titled, .closable, .resizable], backing: .buffered, defer: false)
    self.title = title
    self.isReleasedWhenClosed = false
    self.backgroundColor = Theme.paper
    self.appearance = NSAppearance(named: .aqua)
    self.minSize = NSSize(width: 500, height: 350)
    self.delegate = self
  }
  func finish(_ accepted: Bool) {
    self.accepted = accepted
    NSApp.stopModal()
    orderOut(nil)
  }
  func windowShouldClose(_ sender: NSWindow) -> Bool {
    finish(false)
    return false
  }
  func run(relativeTo parent: NSWindow?) -> Bool {
    center()
    if let parent {
      setFrameOrigin(
        NSPoint(x: parent.frame.midX - frame.width / 2, y: parent.frame.midY - frame.height / 2))
    }
    NSApp.runModal(for: self)
    orderOut(nil)
    close()
    return accepted
  }
}
func showError(_ error: Error, _ window: NSWindow? = nil) {
  let alert = NSAlert()
  alert.messageText = "Åtgärden kunde inte slutföras"
  alert.informativeText =
    (error as? ReaderError)?.message
    ?? "Filen kunde inte läsas eller sparas. Kontrollera behörigheter och försök igen."
  alert.alertStyle = .warning
  alert.addButton(withTitle: "OK")
  alert.runModal()
}
func confirm(_ title: String, _ message: String) -> Bool {
  let a = NSAlert()
  a.messageText = title
  a.informativeText = message
  a.addButton(withTitle: "Fortsätt")
  a.addButton(withTitle: "Avbryt")
  return a.runModal() == .alertFirstButtonReturn
}

final class AppController: NSObject, NSApplicationDelegate, NSWindowDelegate {
  let store: Store
  var window: NSWindow!
  var preference: FormatPreference?
  var appearance: AppearanceSettings
  var theme: Theme { Theme(appearance.AccentHex) }
  let worker = DispatchQueue(label: "se.tobtech.bricklasaren.reader", qos: .userInitiated)
  // Engine and all transport/config mutations are confined to worker. UI uses ReaderInfo snapshots.
  var workflow: WorkflowSettings
  var connectionTimer: Timer?, wakeObserver: NSObjectProtocol?, lastUSB: USBState?
  var reconnectPending = false
  var updateChecking = false
  var startupUpdatePending = false
  var engine: ReaderEngine?
  var info: ReaderInfo?
  var busy = false, token: CancelToken?, outputs: (String, String)?
  var stage = "ANSLUTER", instruction = "Kontrollerar läsaren",
    hint = "Låt USB-kabeln sitta i. Du behöver inte lägga på någon bricka ännu.", notice = "",
    warning = false
  var connection = "Ansluter läsaren …"
  var latestErrorCode = "", preview = false
  private var lastCopied: String?
  init(store: Store, preview: Bool = false) {
    self.store = store
    self.workflow = WorkflowSettings.load(store)
    self.preference = store.preference()
    self.appearance = AppearanceFiles.load(store)
    self.preview = preview
    super.init()
  }
  deinit {
    connectionTimer?.invalidate()
    if let observer = wakeObserver { NSWorkspace.shared.notificationCenter.removeObserver(observer) }
  }
  func applicationDidFinishLaunching(_ notification: Notification) {
    makeWindow()
    buildMenu()
    rebuild()
    window.makeKeyAndOrderFront(nil)
    NSApp.activate(ignoringOtherApps: true)
    guard !preview else { return }
    startConnectionWatch()
    startupUpdatePending = workflow.autoCheck
    DispatchQueue.main.async { [weak self] in
      guard let self else { return }
      if self.preference == nil && !self.chooseFormat(first: true) {
        NSApp.terminate(nil)
        return
      }
      self.connect()
    }
  }
  func makeWindow() {
    window = NSWindow(
      contentRect: NSRect(x: 0, y: 0, width: 900, height: 720),
      styleMask: [.titled, .closable, .miniaturizable, .resizable], backing: .buffered, defer: false
    )
    window.title = "Brickläsaren · 3.5.0 · macOS"
    window.isReleasedWhenClosed = false
    window.minSize = NSSize(width: 800, height: 600)
    window.backgroundColor = Theme.paper
    window.appearance = NSAppearance(named: .aqua)
    window.delegate = self
    window.center()
  }
  func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }
  func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
    if busy {
      token?.cancel()
      notice = "Låt programmet avsluta åtgärden och återställa läsaren. Avsluta sedan igen."
      rebuild()
      return .terminateCancel
    }
    clearResults()
    return .terminateNow
  }
  func windowShouldClose(_ sender: NSWindow) -> Bool {
    if busy {
      token?.cancel()
      notice = "Vänta tills åtgärden är avslutad och stäng sedan igen."
      rebuild()
      return false
    }
    clearResults()
    return true
  }
  func buildMenu() {
    let menu = NSMenu()
    let item = NSMenuItem()
    menu.addItem(item)
    let app = NSMenu()
    item.submenu = app
    let about = NSMenuItem(title: "Om Brickläsaren", action: #selector(aboutApp), keyEquivalent: "")
    about.target = self
    app.addItem(about)
    app.addItem(.separator())
    app.addItem(
      withTitle: "Göm Brickläsaren", action: #selector(NSApplication.hide(_:)), keyEquivalent: "h")
    app.addItem(.separator())
    app.addItem(
      withTitle: "Avsluta Brickläsaren", action: #selector(NSApplication.terminate(_:)),
      keyEquivalent: "q")
    let editItem = NSMenuItem()
    editItem.title = "Redigera"
    let edit = NSMenu(title: "Redigera")
    editItem.submenu = edit
    for (title, action, key) in [
      ("Ångra", "undo:", "z"), ("Klipp ut", "cut:", "x"), ("Kopiera", "copy:", "c"),
      ("Klistra in", "paste:", "v"), ("Markera allt", "selectAll:", "a"),
    ] { edit.addItem(withTitle: title, action: NSSelectorFromString(action), keyEquivalent: key) }
    menu.addItem(editItem)
    addWorkflowMenu(menu)
    NSApp.mainMenu = menu
  }
  @objc func aboutApp() {
    let a = NSAlert()
    a.messageText = "Brickläsaren 3.5.0 för macOS"
    a.informativeText =
      "RCO, Axema VAKA och Bewator med OMNIKEY 5427 CK.\nLokal läsning utan register eller skanningshistorik."
    a.runModal()
  }
  func clearResults() {
    outputs = nil
    if let lastCopied, NSPasteboard.general.string(forType: .string) == lastCopied {
      NSPasteboard.general.clearContents()
    }
    lastCopied = nil
  }
  func button(
    _ title: String, primary: Bool = false, enabled: Bool = true, _ action: @escaping () -> Void
  ) -> ActionButton {
    let b = ActionButton(title, primary: primary, theme: theme, action: action)
    b.isEnabled = enabled
    return b
  }
  func rebuild() {
    guard window != nil else { return }
    let root = stack(spacing: 18)
    if let data = AppearanceFiles.loadedLogo(store, appearance), let image = NSImage(data: data) {
      let v = NSImageView()
      v.image = image
      v.imageScaling = .scaleProportionallyDown
      v.translatesAutoresizingMaskIntoConstraints = false
      v.heightAnchor.constraint(equalToConstant: 48).isActive = true
      v.widthAnchor.constraint(equalToConstant: 220).isActive = true
      root.addArrangedSubview(v)
    }
    if !appearance.Organization.isEmpty {
      root.addArrangedSubview(
        label(appearance.Organization, size: 15, bold: true, color: theme.text))
    }
    if !appearance.Subtitle.isEmpty {
      root.addArrangedSubview(label(appearance.Subtitle, size: 12, color: Theme.muted))
    }
    let titles = stack(
      [
        label("Brickläsaren", size: 30, bold: true),
        label(preference?.Mode.title ?? "Välj passersystem vid första starten", color: Theme.muted),
      ], spacing: 8)
    let top = stack(
      [titles, button("Hjälp", enabled: !busy) { [weak self] in self?.help() }], horizontal: true)
    top.distribution = .fill
    addFull(root, top)
    titles.setContentHuggingPriority(.defaultLow, for: .horizontal)
    addFull(root, label(connection, size: 13, color: Theme.muted))
    let messages = stack(
      [
        label(stage, size: 12, bold: true, color: warning ? .systemBrown : theme.text),
        label(instruction, size: 28, bold: true), label(hint, color: Theme.muted),
      ], spacing: 12)
    for v in messages.arrangedSubviews {
      v.widthAnchor.constraint(equalTo: messages.widthAnchor).isActive = true
    }
    addFull(
      root,
      cardPanel(
        messages,
        color: warning ? NSColor(srgbRed: 1, green: 0.945, blue: 0.867, alpha: 1) : theme.light))
    let actions = stack(horizontal: true)
    let read = button(
      outputs == nil ? "Läs bricka" : "Läs nästa bricka", primary: true,
      enabled: !busy && preference != nil && info?.ready == true
    ) { [weak self] in self?.scan() }
    read.widthAnchor.constraint(greaterThanOrEqualToConstant: 260).isActive = true
    read.heightAnchor.constraint(greaterThanOrEqualToConstant: 52).isActive = true
    actions.addArrangedSubview(read)
    if busy && token != nil {
      actions.addArrangedSubview(
        button("Avbryt") { [weak self] in
          self?.token?.cancel()
          self?.notice = "Avbryter och återställer läsaren …"
          self?.rebuild()
        })
    }
    if info == nil && !busy {
      actions.addArrangedSubview(button("Anslut läsare") { [weak self] in self?.connect() })
    }
    addFull(root, actions)
    let mode = preference?.Mode ?? .combo
    var leftTitle = "Eget passersystem"
    var rightTitle = "Hyresvärdens passersystem"
    var leftDetail = "mR · MIFARE"
    var rightDetail = "eR · EM"
    var leftCopy = "Kopiera till eget system"
    var rightCopy = "Kopiera till hyresvärden"
    if mode == .rco {
      leftTitle = "Kort ID"
      rightTitle = "Långt ID"
      leftDetail = "9 siffror · \(preference?.RcoChip == "E" ? "EM":"MIFARE")"
      rightDetail = "10 siffror"
      leftCopy = "Kopiera kort ID"
      rightCopy = "Kopiera långt ID"
    }
    if mode == .axema {
      leftTitle = "Axema VAKA"
      leftDetail = "EM · 14 siffror"
      leftCopy = "Kopiera VAKA-numret"
    }
    if mode == .bewator {
      leftTitle = "Bewator 2010 / Omnis"
      leftDetail = "EM · 13 siffror"
      leftCopy = "Kopiera Bewator-numret"
    }
    let results = stack(horizontal: true, spacing: 16)
    results.distribution = .fillEqually
    results.addArrangedSubview(
      resultCard(leftTitle, leftDetail, outputs?.0 ?? "—", leftCopy, index: 0))
    if mode == .combo || mode == .rco {
      results.addArrangedSubview(
        resultCard(rightTitle, rightDetail, outputs?.1 ?? "—", rightCopy, index: 1))
    }
    addFull(root, results)
    if mode == .combo {
      root.addArrangedSubview(
        button("Kopiera båda numren", enabled: outputs != nil && !busy && info?.ready == true) {
          [weak self] in
          guard let self, let values = self.outputs else { return }
          self.copy(
            "Eget passersystem (mR): \(values.0)\nHyresvärdens passersystem (eR): \(values.1)")
        })
    }
    if !notice.isEmpty { addFull(root, label(notice, size: 13, color: theme.text)) }
    addFull(
      root,
      label(
        mode == .combo
          ? "Använd samma bricka i båda stegen. Nya nummer läses endast när du väljer Läs bricka."
          : "Nya nummer läses endast när du väljer Läs bricka.", size: 12, color: Theme.muted))
    setRoot(window, root)
  }
  func resultCard(_ title: String, _ detail: String, _ value: String, _ copy: String, index: Int)
    -> NSView
  {
    let number = label(value, size: 32, bold: true)
    number.font = .monospacedDigitSystemFont(ofSize: 32, weight: .medium)
    number.setAccessibilityLabel(index == 0 ? "Första ID-numret" : "Andra ID-numret")
    let b = button(copy, enabled: outputs != nil && !busy && info?.ready == true) { [weak self] in
      guard let self, let o = self.outputs else { return }
      self.copy(index == 0 ? o.0 : o.1)
    }
    let body = stack(
      [label(title, size: 15, bold: true), label(detail, size: 12, color: Theme.muted), number, b],
      spacing: 14)
    b.widthAnchor.constraint(equalTo: body.widthAnchor).isActive = true
    return cardPanel(body, color: .white)
  }
  func copy(_ text: String) {
    guard !busy, info?.ready == true else { return }
    NSPasteboard.general.clearContents()
    NSPasteboard.general.setString(text, forType: .string)
    lastCopied = text
    notice = "Numret är kopierat. Klistra in med ⌘V."
    rebuild()
  }
  func readyMessage() {
    warning = false
    if let info {
      connection = "Läsaren är ansluten · Tangentbordsinmatning avstängd"
      if info.pending {
        stage = "HJÄLP BEHÖVS"
        instruction = "En läsning blev inte avslutad"
        hint = "Låt läsaren vara inkopplad. Välj Hjälp → IT-inställningar → Återställ läsaren."
        warning = true
      } else if !info.ready {
        stage = "KONTROLL BEHÖVS"
        instruction = "Kontrollera läsarprofilen"
        hint = info.profile
        warning = true
      } else {
        stage = "KLAR ATT BÖRJA"
        instruction = "Klicka på Läs bricka"
        hint = "Håll brickan borta från läsaren tills du blir ombedd att lägga på den."
      }
    } else {
      connection = "Ingen läsare är ansluten"
      stage = "INGEN KONTAKT"
      instruction = "Anslut läsaren till USB"
      hint = "Godkänn tillbehöret om macOS frågar. Klicka sedan på Anslut läsare."
      warning = true
    }
  }
  func connect() {
    guard !busy else { return }
    clearResults()
    busy = true
    info = nil
    notice = ""
    stage = "FÖRBEREDER"
    instruction = "Förbereder läsaren automatiskt"
    hint = "Låt USB-kabeln sitta i och håll alla brickor borta."
    warning = false
    rebuild()
    worker.async { [weak self] in
      guard let self else { return }
      self.engine = nil
      do {
        let reader = try MacConnector.connect(store: self.store) { [weak self] message in
          DispatchQueue.main.async {
            self?.hint = message
            self?.rebuild()
          }
        }
        self.engine = reader
        let info = reader.info
        DispatchQueue.main.async {
          self.info = info
          self.busy = false
          self.readyMessage()
          self.rebuild()
          self.checkStartupUpdates()
        }
      } catch { self.finishError(error, info: nil) }
    }
  }
  func finishError(_ error: Error, info: ReaderInfo?) {
    let code = (error as? ReaderError)?.code ?? "FILE_IO"
    let message =
      (error as? ReaderError)?.message
      ?? "Inställningen kunde inte läsas eller sparas. Kontrollera filbehörigheterna."
    // Only bounded codes and app/platform versions are persisted, never exception payloads/APDUs/card IDs.
    try? store.writeData("senaste-fel.txt", Data("Brickläsaren 3.5.0 macOS\nKod: \(code)\n".utf8))
    DispatchQueue.main.async {
      self.info = self.reconnectPending ? nil : info
      self.latestErrorCode = code
      self.busy = false
      self.token = nil
      self.clearResults()
      self.readyMessage()
      self.notice = message
      self.rebuild()
      self.checkStartupUpdates()
    }
  }
  func scan(reference: Bool = false) {
    guard !busy, let preference, info != nil else { return }
    clearResults()
    let cancellation = CancelToken()
    token = cancellation
    busy = true
    notice = ""
    rebuild()
    worker.async { [weak self] in
      guard let self, let engine = self.engine else { return }
      do {
        let result = try engine.scan(preference, reference: reference, token: cancellation) {
          [weak self] phase in
          DispatchQueue.main.async { self?.progress(phase, reference: reference) }
        }
        let values = reference ? nil : try result.outputs(preference)
        let info = engine.info
        DispatchQueue.main.async {
          self.busy = false
          self.token = nil
          self.info = info
          self.outputs = self.reconnectPending ? nil : values
          self.readyMessage()
          self.notice =
            reference
            ? "Kontrollen är godkänd. Den verifierade profilen används nu."
            : "Läsningen är klar. Kopiera önskat nummer."
          self.rebuild()
          if !reference, !self.reconnectPending, info.ready, let values {
            self.cue(3)
            if let text = self.workflow.copyValue(values, combo: preference.Mode == .combo) { self.copy(text) }
          }
        }
      } catch { self.finishError(error, info: engine.info) }
    }
  }
  func progress(_ phase: String, reference: Bool) {
    cue(phase.hasPrefix("place-") ? 1 : phase.hasPrefix("remove-") ? 2 : 0)
    let both = reference || preference?.Mode == .combo
    let card = reference ? "referensbrickan" : "brickan"
    let steps: [String: (String, String, String)] = [
      "remove-first": (
        "FÖRBEREDER", "Lyft bort brickan", "Läsaren behöver vara tom innan vi börjar."
      ),
      "prepare-m": (
        "FÖRBEREDER", "Håll brickan borta en stund", "Du får strax en uppmaning att lägga på den."
      ),
      "place-m": (
        both ? "STEG 1 AV 2 · EGET PASSERSYSTEM" : "LÄSER MIFARE", "Lägg på \(card)",
        "Håll kvar tills du får en ny instruktion."
      ),
      "remove-m": (
        "MIFARE ÄR LÄST", "Lyft bort brickan",
        both
          ? "Behåll samma bricka i handen. Vi ska läsa den en gång till."
          : "Numret visas när läsningen har avslutats."
      ),
      "prepare-e": (
        "FÖRBEREDER", "Håll brickan borta en stund",
        "Lägg inte på den förrän nästa instruktion visas."
      ),
      "place-e": (
        both ? "STEG 2 AV 2 · HYRESVÄRDEN" : "LÄSER EM",
        both ? "Lägg tillbaka samma \(card)" : "Lägg på \(card)",
        "Håll kvar tills du får en ny instruktion."
      ),
      "remove-e": ("EM ÄR LÄST", "Lyft bort brickan", "Numren visas när läsningen har avslutats."),
      "restore": ("SLUTFÖR", "Strax klart", "Håll brickan borta och låt läsaren vara inkopplad."),
    ]
    if let s = steps[phase] {
      stage = s.0
      instruction = s.1
      hint = s.2
    }
    rebuild()
  }
}
