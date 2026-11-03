import AppKit
import UniformTypeIdentifiers

final class FormatEditor {
  let dialog = Dialog("Välj passersystem", width: 660, height: 600)
  let choices = NSPopUpButton(), chip = NSPopUpButton()
  var selected: FormatPreference? {
    guard choices.indexOfSelectedItem > 0 else { return nil }
    return FormatPreference(
      Mode: AccessMode.allCases[choices.indexOfSelectedItem - 1],
      RcoChip: chip.indexOfSelectedItem == 0 ? "M" : "E")
  }
  init(_ current: FormatPreference?, save: @escaping (FormatPreference) throws -> Void) {
    choices.addItems(withTitles: ["Välj passersystem …"] + AccessMode.allCases.map(\.title))
    chip.addItems(withTitles: ["MIFARE", "EM"])
    if let current {
      choices.selectItem(at: AccessMode.allCases.firstIndex(of: current.Mode)! + 1)
      chip.selectItem(at: current.RcoChip == "M" ? 0 : 1)
    }
    let root = stack(
      [
        label("Välj passersystem", size: 26, bold: true),
        label(
          "Valet sparas för ditt Mac-konto och används nästa gång programmet öppnas.",
          color: Theme.muted), choices,
        label("Teknik vid RCO – kort/lång ID-kod", size: 13, bold: true), chip,
      ], spacing: 16)
    for mode in AccessMode.allCases {
      addFull(root, label(mode.title + "\n" + mode.detail, size: 13))
    }
    let saveButton = ActionButton("Spara och börja", primary: true) { [weak self] in
      guard let self, let preference = self.selected else { return }
      do {
        try save(preference)
        self.dialog.finish(true)
      } catch { showError(error) }
    }
    saveButton.isEnabled = selected != nil
    let observer = SelectionObserver { [weak self, weak saveButton] in
      saveButton?.isEnabled = self?.selected != nil
      self?.chip.isEnabled = self?.selected?.Mode == .rco
    }
    choices.target = observer
    choices.action = #selector(SelectionObserver.changed)
    dialog.retained = observer
    chip.isEnabled = selected?.Mode == .rco
    root.addArrangedSubview(
      stack(
        [saveButton, ActionButton("Avbryt") { [weak self] in self?.dialog.finish(false) }],
        horizontal: true))
    setRoot(dialog, root)
  }
}
final class SelectionObserver: NSObject {
  let block: () -> Void
  init(_ block: @escaping () -> Void) { self.block = block }
  @objc func changed() { block() }
}

final class AppearanceEditor: NSObject, NSTextFieldDelegate {
  let dialog = Dialog("Utseende – färg och logotyp", width: 680, height: 680)
  let color = NSTextField(), organization = NSTextField(), subtitle = NSTextField(),
    preview = NSStackView()
  let preset = NSPopUpButton(), logoLabel = label("Ingen logotyp", size: 12, color: Theme.muted)
  var logo: Data?, settings: AppearanceSettings
  let store: Store
  init(store: Store, settings: AppearanceSettings, save: @escaping (AppearanceSettings) -> Void) {
    self.store = store
    self.settings = settings
    self.logo = AppearanceFiles.loadedLogo(store, settings)
    super.init()
    color.stringValue = settings.AccentHex
    organization.stringValue = settings.Organization
    subtitle.stringValue = settings.Subtitle
    for (field, name) in [
      (color, "Accentfärg"), (organization, "Verksamhetens namn"), (subtitle, "Underrubrik"),
    ] {
      field.delegate = self
      field.setAccessibilityLabel(name)
    }
    preset.addItems(withTitles: ["Välj färg …", "Marinblå", "Grön", "Lila"])
    preset.target = self
    preset.action = #selector(selectPreset)
    let root = stack(
      [
        label("Gör programmet till ert", size: 26, bold: true),
        label("Färg och logotyp sparas lokalt på den här datorn.", color: Theme.muted),
        stack([preset, color], horizontal: true),
        label("Verksamhetens namn (valfritt)", size: 13, bold: true), organization,
        label("Underrubrik (valfritt)", size: 13, bold: true), subtitle,
      ], spacing: 14)
    for field in [organization, subtitle] {
      field.widthAnchor.constraint(equalTo: root.widthAnchor).isActive = true
    }
    color.widthAnchor.constraint(equalToConstant: 150).isActive = true
    let logoButtons = stack(
      [
        ActionButton("Välj PNG-logotyp") { [weak self] in self?.pickLogo() },
        ActionButton("Ta bort logotyp") { [weak self] in
          self?.logo = nil
          self?.updatePreview()
        },
      ], horizontal: true)
    root.addArrangedSubview(logoButtons)
    root.addArrangedSubview(logoLabel)
    preview.orientation = .vertical
    preview.alignment = .leading
    preview.spacing = 8
    preview.translatesAutoresizingMaskIntoConstraints = false
    addFull(root, preview)
    let actions = stack(
      [
        ActionButton("Spara", primary: true) { [weak self] in
          guard let self else { return }
          do {
            var next = self.settings
            next.AccentHex = self.color.stringValue.trimmingCharacters(in: .whitespaces)
            next.Organization = self.organization.stringValue
            next.Subtitle = self.subtitle.stringValue
            next = try AppearanceFiles.save(self.store, next, logo: self.logo)
            save(next)
            self.dialog.finish(true)
          } catch { showError(error) }
        },
        ActionButton("Standardutseende") { [weak self] in
          guard let self else { return }
          self.color.stringValue = "#003B55"
          self.organization.stringValue = ""
          self.subtitle.stringValue = ""
          self.logo = nil
          self.updatePreview()
        }, ActionButton("Avbryt") { [weak self] in self?.dialog.finish(false) },
      ], horizontal: true)
    root.addArrangedSubview(actions)
    setRoot(dialog, root)
    updatePreview()
  }
  @objc func selectPreset() {
    let colors = ["#003B55", "#227054", "#67509B"]
    if preset.indexOfSelectedItem > 0 {
      color.stringValue = colors[preset.indexOfSelectedItem - 1]
      updatePreview()
    }
  }
  func controlTextDidChange(_ obj: Notification) { updatePreview() }
  func pickLogo() {
    let p = NSOpenPanel()
    p.allowedContentTypes = [.png]
    p.canChooseDirectories = false
    p.allowsMultipleSelection = false
    if p.runModal() == .OK, let url = p.url {
      do {
        logo = try AppearanceFiles.logoData(url)
        updatePreview()
      } catch { showError(error) }
    }
  }
  func updatePreview() {
    for view in preview.arrangedSubviews {
      preview.removeArrangedSubview(view)
      view.removeFromSuperview()
    }
    logoLabel.stringValue = logo == nil ? "Ingen logotyp" : "Logotyp vald · PNG"
    let theme = Theme(color.stringValue)
    let body = stack(spacing: 8)
    if let logo, let image = NSImage(data: logo) {
      let v = NSImageView()
      v.image = image
      v.imageScaling = .scaleProportionallyDown
      v.heightAnchor.constraint(equalToConstant: 38).isActive = true
      v.widthAnchor.constraint(equalToConstant: 200).isActive = true
      body.addArrangedSubview(v)
    }
    body.addArrangedSubview(
      label(
        organization.stringValue.isEmpty ? "Brickläsaren" : organization.stringValue, size: 20,
        bold: true, color: theme.text))
    if !subtitle.stringValue.isEmpty {
      body.addArrangedSubview(label(subtitle.stringValue, size: 12))
    }
    body.addArrangedSubview(ActionButton("Läs bricka", primary: true, theme: theme) {})
    addFull(preview, cardPanel(body, color: theme.light, padding: 16))
  }
}
final class ReferenceEditor {
  let dialog = Dialog("Ställ in referensbricka", width: 640, height: 530)
  let mifare = NSTextField(), em = NSTextField(),
    verified = NSButton(
      checkboxWithTitle: "Uppgifterna är kontrollerade mot en oberoende källa", target: nil,
      action: nil)
  init(store: Store) {
    if let old: ReferenceCard = try? store.read("referensbricka.json") {
      mifare.stringValue = old.MifareHex
      em.stringValue = old.EmHex
    }
    mifare.setAccessibilityLabel("Verifierat MIFARE-UID")
    em.setAccessibilityLabel("Verifierat EM-nummer i hex")
    let root = stack(
      [
        label("Frivilligt referensprov för IT", size: 25, bold: true),
        label(
          "Vanliga brickor kräver ingen registrering eller referensbricka. Använd detta bara när byteordningen behöver verifieras mot kända, oberoende uppgifter.",
          color: Theme.muted),
        label("Normaliserat MIFARE-UID i hex (4, 7 eller 10 byte)", size: 13, bold: true), mifare,
        label("Normaliserat EM-nummer i hex (5 byte)", size: 13, bold: true), em, verified,
      ], spacing: 16)
    for field in [mifare, em] {
      field.widthAnchor.constraint(equalTo: root.widthAnchor).isActive = true
    }
    root.addArrangedSubview(
      stack(
        [
          ActionButton("Spara lokalt", primary: true) { [weak self] in
            guard let self else { return }
            do {
              try require(
                self.verified.state == .on, "Bekräfta att uppgifterna är oberoende kontrollerade.")
              var r = ReferenceCard(MifareHex: self.mifare.stringValue, EmHex: self.em.stringValue)
              try r.validate()
              try store.write("referensbricka.json", r)
              self.dialog.finish(true)
            } catch { showError(error) }
          }, ActionButton("Avbryt") { [weak self] in self?.dialog.finish(false) },
        ], horizontal: true))
    setRoot(dialog, root)
  }
}

extension AppController {
  @discardableResult func chooseFormat(first: Bool = false) -> Bool {
    guard !busy else { return false }
    let editor = FormatEditor(first ? nil : preference) { [weak self] choice in
      guard let self else { return }
      try self.store.write("formatval.json", choice)
      self.preference = choice
      self.clearResults()
      self.rebuild()
    }
    return editor.dialog.run(relativeTo: window)
  }
  func editAppearance() {
    let editor = AppearanceEditor(store: store, settings: appearance) { [weak self] next in
      self?.appearance = next
      self?.rebuild()
    }
    _ = editor.dialog.run(relativeTo: window)
  }
  func help() {
    let d = Dialog("Hjälp", width: 650, height: 590)
    let root = stack(
      [
        label("Hjälp med Brickläsaren", size: 26, bold: true),
        label(
          "Lägg bara på en bricka åt gången och följ instruktionerna i huvudfönstret. I MIFARE + EM används samma bricka i båda stegen.",
          color: Theme.muted),
      ], spacing: 16)
    let actions: [(String, () -> Void)] = [
      ("Inställningar – Välj passersystem", { [weak self] in _ = self?.chooseFormat() }),
      (
        "Anslut läsaren igen",
        { [weak self, weak d] in
          d?.finish(true)
          self?.connect()
        }
      ),
      ("Spara felrapport", { [weak self] in self?.exportReport() }),
      ("Arbetsflöde och uppdateringar", { [weak self] in self?.editWorkflow() }),
      (
        "Sök efter uppdatering",
        { [weak self] in
          self?.closeModalParents()
          self?.checkUpdates()
        }
      ),
      ("Guidad felsökning", { [weak self] in self?.diagnostics() }),
      ("Om och licens", { [weak self] in self?.aboutApp() }),
      ("IT-inställningar", { [weak self] in self?.itSettings() }),
    ]
    for (title, action) in actions {
      addFull(root, button(title, action))
    }
    addFull(
      root,
      label(
        "Anslut läsaren med USB. Godkänn tillbehöret om din Mac frågar.",
        size: 13, color: Theme.muted))
    root.addArrangedSubview(button("Stäng") { [weak d] in d?.finish(false) })
    setRoot(d, root)
    _ = d.run(relativeTo: window)
  }
  func itSettings() {
    let d = Dialog("IT-inställningar", width: 700, height: 710)
    let description =
      info.map { "\($0.board)\nFirmware: \($0.firmware)\nProfil: \($0.profile)" }
      ?? "Ingen läsare ansluten. Utseende och lokala inställningar går fortfarande att ändra."
    let root = stack(
      [
        label("IT-inställningar", size: 26, bold: true),
        label(description, size: 13, color: Theme.muted),
      ], spacing: 12)
    let rows: [(String, Bool, () -> Void)] = [
      ("Utseende – färg och logotyp", true, { [weak self] in self?.editAppearance() }),
      (
        "Ställ in referensbricka", true,
        { [weak self] in
          guard let self else { return }
          let e = ReferenceEditor(store: self.store)
          _ = e.dialog.run(relativeTo: self.window)
        }
      ),
      (
        "Kontrollera med referensbricka", info != nil && info?.pending == false,
        { [weak self, weak d] in
          d?.finish(true)
          self?.closeModalParents()
          self?.scan(reference: true)
        }
      ),
      (
        "Importera läsarprofil", info != nil && info?.pending == false,
        { [weak self, weak d] in
          guard let self, let url = self.openProfile() else { return }
          d?.finish(true)
          self.closeModalParents()
          self.readerAction("Importerar läsarprofil") { try $0.importProfile(url) }
        }
      ),
      (
        "Exportera verifierad läsarprofil",
        info?.ready == true && info?.profile == "Verifierad lokal profil",
        { [weak self, weak d] in
          guard let self, let url = self.saveURL("lasarprofil.json", type: .json) else { return }
          d?.finish(true)
          self.closeModalParents()
          self.readerAction("Exporterar läsarprofil") { reader in
            try self.writeProfile(reader, to: url)
          }
        }
      ),
      (
        "Skapa färdig kopia till en annan Mac", true,
        { [weak self, weak d] in
          d?.finish(true)
          self?.closeModalParents()
          self?.exportApp()
        }
      ),
      (
        "Återställ läsaren efter avbruten läsning", info?.pending == true,
        { [weak self, weak d] in
          d?.finish(true)
          self?.closeModalParents()
          self?.readerAction("Återställer läsaren") { try $0.recover() }
        }
      ),
      (
        "Återställ ursprungligt USB-läge och avsluta", info != nil && info?.pending == false,
        { [weak self, weak d] in
          guard
            confirm(
              "Återställ läsarens USB-läge?",
              "Programmet avslutas när ursprungligt läge är verifierat. Om läsaren tidigare fungerade som tangentbord kan den därefter skriva till andra program."
            )
          else { return }
          d?.finish(true)
          self?.closeModalParents()
          self?.restoreUSB()
        }
      ),
      (
        "Visa lokala inställningar i Finder", true,
        { [weak self] in if let self { NSWorkspace.shared.open(self.store.root) } }
      ),
    ]
    for (title, enabled, action) in rows { addFull(root, button(title, enabled: enabled, action)) }
    root.addArrangedSubview(button("Stäng") { [weak d] in d?.finish(false) })
    setRoot(d, root)
    _ = d.run(relativeTo: window)
  }
  // End nested help modals before a reader job; never run hardware work on the UI thread.
  func closeModalParents() {
    DispatchQueue.main.async {
      if NSApp.modalWindow != nil {
        NSApp.stopModal()
        NSApp.modalWindow?.orderOut(nil)
      }
    }
  }
  func openProfile() -> URL? {
    let p = NSOpenPanel()
    p.allowedContentTypes = [.json]
    p.canChooseDirectories = false
    p.allowsMultipleSelection = false
    return p.runModal() == .OK ? p.url : nil
  }
  func saveURL(_ name: String, type: UTType) -> URL? {
    let p = NSSavePanel()
    p.nameFieldStringValue = name
    p.allowedContentTypes = [type]
    return p.runModal() == .OK ? p.url : nil
  }
  func writeProfile(_ reader: ReaderEngine, to url: URL) throws {
    try reader.checkIdentity()
    guard let c = reader.calibration else {
      throw fail(
        "PROFILE",
        "Läsaren använder standardformat. Ingen verifierad profil finns att exportera eller behövs för vanlig läsning."
      )
    }
    try c.validate()
    let data = try JSONEncoder().encode(ReaderProfile(Calibration: c))
    try data.write(to: url, options: .atomic)
    try FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: url.path)
  }
  func exportReport() {
    guard let url = saveURL("Bricklasaren-felrapport.txt", type: .plainText) else { return }
    do {
      let report =
        "Brickläsaren 3.5.1 · macOS · Apple Silicon\nmacOS: \(ProcessInfo.processInfo.operatingSystemVersionString)\nFelkod: \(latestErrorCode.isEmpty ? "Ingen i denna session":latestErrorCode)\nLäsare ansluten: \(info == nil ? "Nej":"Ja")\nÅterställning väntar: \(info?.pending == true ? "Ja":"Nej")\nRapporten innehåller inga kortnummer, läsar-ID, profiler eller användarsökvägar.\n"
      try Data(report.utf8).write(to: url, options: .atomic)
      notice = "Felrapporten är sparad."
      rebuild()
    } catch { showError(error) }
  }
  func readerAction(
    _ message: String, success: String = "Åtgärden är klar.",
    action: @escaping (ReaderEngine) throws -> Void
  ) {
    guard !busy else { return }
    clearResults()
    busy = true
    stage = "IT-ÅTGÄRD"
    instruction = message
    hint = "Låt läsaren vara ansluten."
    rebuild()
    worker.async { [weak self] in
      guard let self else { return }
      guard let reader = self.engine else {
        self.finishError(fail("DISCONNECTED", "Anslut läsaren först."), info: nil)
        return
      }
      do {
        try action(reader)
        let info = reader.info
        DispatchQueue.main.async {
          self.busy = false
          self.info = info
          self.readyMessage()
          self.notice = success
          self.rebuild()
        }
      } catch { self.finishError(error, info: reader.info) }
    }
  }
  func restoreUSB() {
    guard !busy else { return }
    clearResults()
    busy = true
    stage = "ÅTERSTÄLLER USB"
    instruction = "Återställer läsarens ursprungliga läge"
    hint = "Låt kabeln sitta i. Programmet avslutas när läget har verifierats."
    rebuild()
    worker.async { [weak self] in
      guard let self, let reader = self.engine else { return }
      do {
        try MacConnector.restore(reader)
        DispatchQueue.main.async {
          self.busy = false
          NSApp.terminate(nil)
        }
      } catch {
        self.engine = nil
        self.finishError(error, info: nil)
      }
    }
  }
  func exportApp() {
    let p = NSOpenPanel()
    p.canChooseFiles = false
    p.canChooseDirectories = true
    p.canCreateDirectories = true
    p.prompt = "Välj mapp"
    guard p.runModal() == .OK, let folder = p.url, !busy else { return }
    clearResults()
    busy = true
    stage = "EXPORTERAR"
    instruction = "Skapar en färdig Mac-kopia"
    hint = "Kopian innehåller appen och eventuell verifierad läsarprofil."
    rebuild()
    worker.async { [weak self] in
      guard let self else { return }
      let destination = folder.appendingPathComponent(
        "Bricklasaren-Mac-" + UUID().uuidString, isDirectory: true)
      var created = false
      do {
        try FileManager.default.createDirectory(at: destination, withIntermediateDirectories: false)
        created = true
        try FileManager.default.copyItem(
          at: Bundle.main.bundleURL, to: destination.appendingPathComponent("Bricklasaren.app"))
        if let reader = self.engine, reader.ready, reader.calibration != nil {
          try self.writeProfile(reader, to: destination.appendingPathComponent("lasarprofil.json"))
        }
        let note =
          "Flytta Bricklasaren.app till Program. En medföljande lasarprofil.json är valfri och kan importeras under Hjälp → IT-inställningar. Den gäller bara den verifierade läsaren och firmware. Personliga inställningar, referensbricka, logotyp och skannade nummer följer inte med.\n"
        try Data(note.utf8).write(to: destination.appendingPathComponent("Läs mig.txt"))
        DispatchQueue.main.async {
          self.busy = false
          self.readyMessage()
          self.notice = "Mac-kopian är klar."
          self.rebuild()
          NSWorkspace.shared.activateFileViewerSelecting([destination])
        }
      } catch {
        if created { try? FileManager.default.removeItem(at: destination) }
        self.finishError(error, info: self.engine?.info)
      }
    }
  }
}
