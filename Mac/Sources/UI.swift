import AppKit
import UniformTypeIdentifiers

final class ActionButton: NSButton {
  var actionBlock: (() -> Void)?
  let primary: Bool
  var theme: Theme
  init(
    _ title: String, primary: Bool = false, theme: Theme = Theme("#003B55"),
    height: CGFloat = 40, action: @escaping () -> Void
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
    heightAnchor.constraint(greaterThanOrEqualToConstant: height).isActive = true
    widthAnchor.constraint(greaterThanOrEqualToConstant: max(100, CGFloat(title.count) * 7 + 30))
      .isActive = true
    setAccessibilityLabel(title)
    if primary { keyEquivalent = "\r" }
    if title == "Avbryt" || title == "Stäng" { keyEquivalent = "\u{1b}" }
  }
  required init?(coder: NSCoder) { fatalError("init(coder:) is not supported") }
  @objc private func clicked() { actionBlock?() }
  override func becomeFirstResponder() -> Bool {
    let accepted = super.becomeFirstResponder()
    needsDisplay = true
    return accepted
  }
  override func resignFirstResponder() -> Bool {
    let accepted = super.resignFirstResponder()
    needsDisplay = true
    return accepted
  }
  override func draw(_ dirtyRect: NSRect) {
    let base = primary ? theme.accent : .white
    let background = isHighlighted ? Theme.mix(base, .black, 0.1) : base
    (isEnabled ? background : Theme.mix(background, Theme.paper, 0.6)).setFill()
    NSBezierPath(roundedRect: bounds, xRadius: 7, yRadius: 7).fill()
    if !primary {
      NSColor(srgbRed: 0.72, green: 0.78, blue: 0.81, alpha: 1).setStroke()
      NSBezierPath(roundedRect: bounds.insetBy(dx: 0.5, dy: 0.5), xRadius: 7, yRadius: 7).stroke()
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
      let p = NSBezierPath(roundedRect: bounds.insetBy(dx: 3, dy: 3), xRadius: 5, yRadius: 5)
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
  s.distribution = .fill
  s.spacing = spacing
  s.translatesAutoresizingMaskIntoConstraints = false
  return s
}
func addFull(_ parent: NSStackView, _ child: NSView) {
  parent.addArrangedSubview(child)
  child.widthAnchor.constraint(equalTo: parent.widthAnchor).isActive = true
}
func cardPanel(_ contents: NSStackView, color: NSColor, padding: CGFloat = 16) -> NSView {
  let p = NSView()
  p.wantsLayer = true
  p.layer?.backgroundColor = color.cgColor
  p.layer?.cornerRadius = 10
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
  scroll.autohidesScrollers = true
  scroll.drawsBackground = true
  scroll.backgroundColor = Theme.paper
  let document = FlippedView()
  document.translatesAutoresizingMaskIntoConstraints = false
  document.addSubview(root)
  scroll.documentView = document
  window.contentView = scroll
  NSLayoutConstraint.activate([
    document.widthAnchor.constraint(equalTo: scroll.contentView.widthAnchor),
    root.leadingAnchor.constraint(equalTo: document.leadingAnchor, constant: 24),
    root.trailingAnchor.constraint(equalTo: document.trailingAnchor, constant: -24),
    root.topAnchor.constraint(equalTo: document.topAnchor, constant: 20),
    root.bottomAnchor.constraint(equalTo: document.bottomAnchor, constant: -20),
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
  override func cancelOperation(_ sender: Any?) { finish(false) }
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

final class ResultView {
  let title = label("", size: 14, bold: true)
  let detail = label("", size: 12, color: Theme.muted)
  let number = label("—", size: 32)
  let empty = label("Väntar på läsning", size: 11, color: Theme.muted)
  let copyButton: ActionButton
  let panel: NSView
  init(index: Int, controller: AppController) {
    number.font = .monospacedDigitSystemFont(ofSize: 32, weight: .medium)
    number.maximumNumberOfLines = 1
    number.setContentCompressionResistancePriority(.required, for: .horizontal)
    number.setAccessibilityLabel(index == 0 ? "Första ID-numret" : "Andra ID-numret")
    copyButton = ActionButton("Kopiera nummer") { [weak controller] in
      guard let controller, let values = controller.outputs else { return }
      controller.copy(index == 0 ? values.0 : values.1)
    }
    let body = stack([title, detail, number, empty, copyButton], spacing: 6)
    for view in [title, detail, empty, copyButton] {
      view.widthAnchor.constraint(equalTo: body.widthAnchor).isActive = true
    }
    panel = cardPanel(body, color: .white, padding: 14)
  }
  func update(title: String, detail: String, value: String?, copy: String, enabled: Bool, theme: Theme) {
    self.title.stringValue = title
    self.detail.stringValue = detail
    number.stringValue = value ?? "—"
    empty.stringValue = value == nil ? "Väntar på läsning" : " "
    copyButton.title = copy
    copyButton.setAccessibilityLabel(copy)
    copyButton.isEnabled = enabled && value != nil
    copyButton.theme = theme
    copyButton.needsDisplay = true
  }
  func clear() {
    number.stringValue = "—"
    empty.stringValue = "Väntar på läsning"
    copyButton.isEnabled = false
  }
}

// Stable views preserve keyboard focus and avoid rebuilding/decoding branding during a scan.
final class ReaderView {
  let root = stack(spacing: 14)
  let branding = stack(spacing: 4)
  let logo = NSImageView()
  let organization = label("", size: 14, bold: true)
  let subtitle = label("", size: 12, color: Theme.muted)
  let mode = label("", size: 13, color: Theme.muted)
  let status = label("", size: 12, color: Theme.muted)
  let stage = label("", size: 11, bold: true)
  let instruction = label("", size: 25, bold: true)
  let hint = label("", size: 13, color: Theme.muted)
  let notice = label("", size: 12)
  let footer = label("", size: 11, color: Theme.muted)
  let readButton: ActionButton, cancelButton: ActionButton, connectButton: ActionButton
  let helpButton: ActionButton, bothButton: ActionButton, clearButton: ActionButton
  let first: ResultView, second: ResultView
  let instructionPanel: NSView
  private var lastInstruction = ""

  init(_ controller: AppController) {
    readButton = ActionButton("Läs bricka", primary: true, height: 44) { [weak controller] in
      controller?.scan()
    }
    // Reading has its own explicit shortcut; Return remains safe in settings and text fields.
    readButton.keyEquivalent = ""
    readButton.setAccessibilityHelp("Läs en bricka. Kortkommando: kommando R.")
    cancelButton = ActionButton("Avbryt") { [weak controller] in
      controller?.token?.cancel()
      controller?.clearResults()
      controller?.notice = "Avbryter och återställer läsaren …"
      controller?.rebuild()
    }
    connectButton = ActionButton("Anslut läsare") { [weak controller] in controller?.connect() }
    helpButton = ActionButton("Hjälp") { [weak controller] in controller?.help() }
    helpButton.widthAnchor.constraint(equalToConstant: 100).isActive = true
    bothButton = ActionButton("Kopiera båda numren", height: 34) { [weak controller] in
      guard let controller, let values = controller.outputs else { return }
      controller.copy("Eget passersystem (mR): \(values.0)\nHyresvärdens passersystem (eR): \(values.1)")
    }
    clearButton = ActionButton("Rensa", height: 34) { [weak controller] in
      controller?.clearDisplayedResults()
    }
    bothButton.widthAnchor.constraint(equalToConstant: 176).isActive = true
    clearButton.widthAnchor.constraint(equalToConstant: 100).isActive = true
    clearButton.setAccessibilityHelp("Rensa visade nummer och senast kopierat nummer från den här appen.")
    first = ResultView(index: 0, controller: controller)
    second = ResultView(index: 1, controller: controller)

    logo.translatesAutoresizingMaskIntoConstraints = false
    logo.imageScaling = .scaleProportionallyDown
    logo.imageAlignment = .alignLeft
    logo.heightAnchor.constraint(equalToConstant: 40).isActive = true
    logo.widthAnchor.constraint(equalToConstant: 200).isActive = true
    logo.setAccessibilityElement(false)
    branding.addArrangedSubview(logo)
    addFull(branding, organization)
    addFull(branding, subtitle)
    addFull(root, branding)

    let titles = stack([label("Brickläsaren", size: 27, bold: true), mode, status], spacing: 4)
    for field in [mode, status] { field.widthAnchor.constraint(equalTo: titles.widthAnchor).isActive = true }
    titles.setContentHuggingPriority(.defaultLow, for: .horizontal)
    let top = stack([titles, helpButton], horizontal: true, spacing: 20)
    addFull(root, top)

    let messages = stack([stage, instruction, hint], spacing: 7)
    for field in [stage, instruction, hint] {
      field.widthAnchor.constraint(equalTo: messages.widthAnchor).isActive = true
    }
    messages.setContentHuggingPriority(.defaultLow, for: .horizontal)
    let actions = stack([readButton, cancelButton, connectButton], spacing: 8)
    actions.widthAnchor.constraint(equalToConstant: 200).isActive = true
    for button in actions.arrangedSubviews {
      button.widthAnchor.constraint(equalTo: actions.widthAnchor).isActive = true
    }
    instructionPanel = cardPanel(stack([messages, actions], horizontal: true, spacing: 20), color: .white)
    addFull(root, instructionPanel)

    let resultTitle = label("Bricknummer", size: 14, bold: true)
    resultTitle.setContentHuggingPriority(.defaultLow, for: .horizontal)
    addFull(root, stack([resultTitle, bothButton, clearButton], horizontal: true, spacing: 8))
    let results = stack([first.panel, second.panel], horizontal: true, spacing: 14)
    results.distribution = .fillEqually
    addFull(root, results)
    addFull(root, notice)
    addFull(root, footer)
  }

  func clearNumbers() {
    first.clear()
    second.clear()
    bothButton.isEnabled = false
    clearButton.isEnabled = false
  }
  func update(_ c: AppController) {
    let theme = c.theme
    logo.image = c.visualCache.logo(c.store, settings: c.appearance)
    logo.isHidden = logo.image == nil
    organization.stringValue = c.appearance.Organization
    organization.textColor = theme.text
    organization.isHidden = organization.stringValue.isEmpty
    subtitle.stringValue = c.appearance.Subtitle
    subtitle.isHidden = subtitle.stringValue.isEmpty
    branding.isHidden = logo.isHidden && organization.isHidden && subtitle.isHidden
    mode.stringValue = c.preference?.Mode.title ?? "Välj passersystem vid första starten"
    status.stringValue = c.connection
    status.textColor = c.warning ? .systemBrown : Theme.muted
    stage.stringValue = c.stage
    stage.textColor = c.warning ? .systemBrown : theme.text
    instruction.stringValue = c.instruction
    hint.stringValue = c.hint
    instructionPanel.layer?.backgroundColor = (c.warning
      ? NSColor(srgbRed: 1, green: 0.955, blue: 0.9, alpha: 1) : theme.light).cgColor
    readButton.title = c.outputs == nil ? "Läs bricka" : "Läs nästa bricka"
    readButton.setAccessibilityLabel(readButton.title)
    readButton.isEnabled = !c.busy && !c.reconnectPending && c.preference != nil && c.info?.ready == true && c.usbMonitor?.timedOut != true
    cancelButton.isHidden = !c.busy || c.token == nil
    connectButton.isHidden = c.info != nil || c.busy
    connectButton.isEnabled = c.usbMonitor?.timedOut != true
    helpButton.isEnabled = !c.busy
    let mode = c.preference?.Mode ?? .combo
    let canCopy = !c.busy && !c.reconnectPending && c.info?.ready == true && c.usbMonitor?.timedOut != true
    var title = "Eget passersystem", detail = "mR · MIFARE", copy = "Kopiera till eget system"
    var secondTitle = "Hyresvärdens passersystem", secondDetail = "eR · EM", secondCopy = "Kopiera till hyresvärden"
    switch mode {
    case .combo: break
    case .rco:
      title = "Kort ID"; detail = "9 siffror · \(c.preference?.RcoChip == "E" ? "EM" : "MIFARE")"
      copy = "Kopiera kort ID"; secondTitle = "Långt ID"; secondDetail = "10 siffror"; secondCopy = "Kopiera långt ID"
    case .axema: title = "Axema VAKA"; detail = "EM · 14 siffror"; copy = "Kopiera VAKA-numret"
    case .bewator: title = "Bewator 2010 / Omnis"; detail = "EM · 13 siffror"; copy = "Kopiera Bewator-numret"
    }
    first.update(title: title, detail: detail, value: c.outputs?.0, copy: copy, enabled: canCopy, theme: theme)
    second.update(title: secondTitle, detail: secondDetail, value: c.outputs?.1, copy: secondCopy, enabled: canCopy, theme: theme)
    second.panel.isHidden = mode != .combo && mode != .rco
    bothButton.isHidden = mode != .combo
    bothButton.isEnabled = canCopy && c.outputs != nil
    clearButton.isEnabled = !c.busy && c.outputs != nil
    notice.stringValue = c.notice
    notice.textColor = theme.text
    notice.isHidden = c.notice.isEmpty
    footer.stringValue = mode == .combo ? "Använd samma bricka i båda stegen.  ⌘R Läs · ⌘1 / 2 Kopiera" : "Läs en bricka i taget.  ⌘R Läs · ⌘1 Kopiera"
    for button in [readButton, cancelButton, connectButton, helpButton, bothButton, clearButton] {
      button.theme = theme
      button.needsDisplay = true
    }
    if !c.preview && c.window.isVisible && lastInstruction != c.instruction {
      NSAccessibility.post(element: instruction, notification: .valueChanged)
    }
    lastInstruction = c.instruction
  }
}

final class AppController: NSObject, NSApplicationDelegate, NSWindowDelegate {
  let store: Store
  var window: NSWindow!
  var preference: FormatPreference?
  var appearance: AppearanceSettings
  let visualCache = AppearanceCache()
  var theme: Theme { visualCache.theme(appearance.AccentHex) }
  var readerView: ReaderView?
  let worker = DispatchQueue(label: "se.tobtech.bricklasaren.reader", qos: .userInitiated)
  // Engine and all transport/config mutations are confined to worker. UI uses ReaderInfo snapshots.
  var workflow: WorkflowSettings
  var connectionTimer: Timer?, wakeObserver: NSObjectProtocol?, lastUSB: USBState?
  var usbMonitor: USBMonitor?
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
    if let observer = wakeObserver {
      NSWorkspace.shared.notificationCenter.removeObserver(observer)
    }
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
      contentRect: NSRect(x: 0, y: 0, width: 900, height: 650),
      styleMask: [.titled, .closable, .miniaturizable, .resizable], backing: .buffered, defer: false
    )
    window.title = "Brickläsaren · 3.6.0 · macOS"
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
    usbMonitor?.stop()
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
    usbMonitor?.stop()
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
    a.messageText = "Brickläsaren 3.6.0 för macOS"
    a.informativeText =
      "Tobtech\n\nGratis för privat bruk. Kommersiellt bruk kräver en betald licens."
    a.addButton(withTitle: "Stäng")
    a.addButton(withTitle: "Läs licensen")
    if a.runModal() == .alertSecondButtonReturn,
      let url = Bundle.main.url(forResource: "LICENSE", withExtension: "txt")
    {
      NSWorkspace.shared.open(url)
    }
  }
  func clearResults() {
    outputs = nil
    readerView?.clearNumbers()
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
  // The name is retained for callers; normal state changes update the existing controls.
  func rebuild() {
    guard window != nil else { return }
    if readerView == nil {
      let view = ReaderView(self)
      readerView = view
      setRoot(window, view.root)
      window.initialFirstResponder = view.readButton
    }
    readerView?.update(self)
  }
  func clearDisplayedResults() {
    guard !busy else { return }
    clearResults()
    notice = "Numren är rensade."
    readyMessage()
    rebuild()
  }
  func copy(_ text: String) {
    guard !busy, !reconnectPending, info?.ready == true, usbMonitor?.timedOut != true else { return }
    NSPasteboard.general.clearContents()
    NSPasteboard.general.setString(text, forType: .string)
    lastCopied = text
    notice = "Numret är kopierat. Klistra in med ⌘V."
    rebuild()
  }
  func readyMessage() {
    warning = false
    if usbMonitor?.timedOut == true {
      connection = "Läsarens anslutning behöver kontrolleras"
      stage = "KONTROLLERAR ANSLUTNINGEN"
      instruction = "Väntar på kontakt med läsaren"
      hint = "Numren är rensade. Låt USB-kabeln sitta i medan anslutningen kontrolleras."
      warning = true
      return
    }
    if let info {
      connection = "Läsaren är ansluten"
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
      } else if outputs != nil {
        stage = "KLART"
        instruction = "Läsningen är klar"
        hint = "Kopiera önskat nummer eller läs nästa bricka."
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
    guard !busy, usbMonitor?.timedOut != true else { return }
    reconnectPending = false
    clearResults()
    usbMonitor?.invalidate()
    busy = true
    info = nil
    connection = "Ansluter läsaren …"
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
            guard let self, !self.reconnectPending, self.usbMonitor?.timedOut != true else { return }
            self.hint = message
            self.rebuild()
          }
        }
        self.engine = reader
        let info = reader.info
        DispatchQueue.main.async { self.completeConnection(info) }
      } catch { self.finishError(error, info: nil) }
    }
  }
  func completeConnection(_ info: ReaderInfo) {
    // A wake/disconnect/monitor deadline can supersede an otherwise successful reader job.
    self.info = reconnectPending || usbMonitor?.timedOut == true ? nil : info
    busy = false
    readyMessage()
    rebuild()
    checkStartupUpdates()
  }
  func finishError(_ error: Error, info: ReaderInfo?) {
    let code = (error as? ReaderError)?.code ?? "FILE_IO"
    let message =
      (error as? ReaderError)?.message
      ?? "Inställningen kunde inte läsas eller sparas. Kontrollera filbehörigheterna."
    // Only bounded codes and app/platform versions are persisted, never exception payloads/APDUs/card IDs.
    try? store.writeData("senaste-fel.txt", Data("Brickläsaren 3.6.0 macOS\nKod: \(code)\n".utf8))
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
    guard !busy, !reconnectPending, let preference, info != nil, usbMonitor?.timedOut != true else { return }
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
          DispatchQueue.main.async {
            guard let self, self.token === cancellation, !cancellation.cancelled else { return }
            self.progress(phase, reference: reference)
          }
        }
        let values = reference ? nil : try result.outputs(preference)
        let info = engine.info
        DispatchQueue.main.async {
          self.completeScan(values, info: info, preference: preference, reference: reference, cancellation: cancellation)
        }
      } catch { self.finishError(error, info: engine.info) }
    }
  }
  func completeScan(_ values: (String, String)?, info: ReaderInfo, preference: FormatPreference,
                    reference: Bool, cancellation: CancelToken) {
    // Cancellation or disconnect can win after the worker finishes but before this UI callback.
    guard token === cancellation else { return }
    busy = false
    token = nil
    let unavailable = reconnectPending || usbMonitor?.timedOut == true
    self.info = unavailable ? nil : info
    let incomplete = !reference && !info.ready
    let discard = cancellation.cancelled || unavailable || incomplete
    outputs = discard ? nil : values
    readyMessage()
    if cancellation.cancelled {
      notice = "Läsningen avbröts. Inga nummer visas."
    } else if unavailable {
      notice = "Anslutningen ändrades. Läs brickan igen när läsaren är redo."
    } else if incomplete {
      notice = "Läsningen kunde inte avslutas. Inga nummer visas."
    } else {
      notice = reference ? "Kontrollen är godkänd. Den verifierade profilen används nu."
        : "Läsningen är klar. Kopiera önskat nummer."
    }
    rebuild()
    if !discard, !reference, info.ready, let values = outputs {
      cue(3)
      if let text = workflow.copyValue(values, combo: preference.Mode == .combo) { copy(text) }
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
