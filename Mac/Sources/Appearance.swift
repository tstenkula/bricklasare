import AppKit
import ImageIO

struct AppearanceSettings: Codable {
  var Schema = 1
  var AccentHex = "#003B55"
  var Organization = ""
  var Subtitle = ""
  var LogoFile = ""
  func validate() throws {
    try require(
      Schema == 1 && AccentHex.range(of: "^#[0-9a-fA-F]{6}$", options: .regularExpression) != nil,
      "Ange en färg som #003B55 med sex hexadecimala tecken.")
    for (text, limit) in [(Organization, 80), (Subtitle, 120)] {
      try require(
        text.count <= limit
          && text.unicodeScalars.allSatisfy {
            !CharacterSet.controlCharacters.contains($0) && $0.properties.generalCategory != .format
          }, "Namn och underrubrik ska vara korta texter på en rad.")
    }
    try require(LogoFile.isEmpty || Self.ownedLogo(LogoFile), "Ogiltig logotypfil.")
  }
  static func ownedLogo(_ name: String) -> Bool {
    name.range(of: "^logo-[a-f0-9]{32}\\.png$", options: .regularExpression) != nil
  }
}
struct Theme {
  static let paper = NSColor(srgbRed: 245 / 255, green: 248 / 255, blue: 249 / 255, alpha: 1)
  static let ink = NSColor(srgbRed: 31 / 255, green: 48 / 255, blue: 60 / 255, alpha: 1)
  static let muted = NSColor(srgbRed: 79 / 255, green: 105 / 255, blue: 121 / 255, alpha: 1)
  let accent: NSColor, light: NSColor, text: NSColor, buttonText: NSColor
  init(_ hex: String) {
    let n = UInt32(hex.dropFirst(), radix: 16) ?? 0x003B55
    accent = NSColor(
      srgbRed: CGFloat((n >> 16) & 255) / 255, green: CGFloat((n >> 8) & 255) / 255,
      blue: CGFloat(n & 255) / 255, alpha: 1)
    light = Self.mix(accent, .white, 0.94)
    var t = accent
    while Self.contrast(t, light) < 4.5 || Self.contrast(t, Self.paper) < 4.5 {
      t = Self.mix(t, .black, 0.12)
    }
    text = t
    buttonText = Self.contrast(accent, .white) >= 4.5 ? .white : .black
  }
  static func mix(_ a: NSColor, _ b: NSColor, _ weight: CGFloat) -> NSColor {
    let x = a.usingColorSpace(.sRGB)!
    let y = b.usingColorSpace(.sRGB)!
    return NSColor(
      srgbRed: x.redComponent * (1 - weight) + y.redComponent * weight,
      green: x.greenComponent * (1 - weight) + y.greenComponent * weight,
      blue: x.blueComponent * (1 - weight) + y.blueComponent * weight, alpha: 1)
  }
  static func luminance(_ color: NSColor) -> CGFloat {
    let c = color.usingColorSpace(.sRGB)!
    func l(_ v: CGFloat) -> CGFloat { v <= 0.04045 ? v / 12.92 : pow((v + 0.055) / 1.055, 2.4) }
    return 0.2126 * l(c.redComponent) + 0.7152 * l(c.greenComponent) + 0.0722 * l(c.blueComponent)
  }
  static func contrast(_ a: NSColor, _ b: NSColor) -> CGFloat {
    let x = luminance(a)
    let y = luminance(b)
    return (max(x, y) + 0.05) / (min(x, y) + 0.05)
  }
}
enum AppearanceFiles {
  static func load(_ store: Store) -> AppearanceSettings {
    guard let a: AppearanceSettings = try? store.read("utseende.json"), (try? a.validate()) != nil
    else { return AppearanceSettings() }
    return a
  }
  static func logoData(_ url: URL) throws -> Data {
    let file = try FileHandle(forReadingFrom: url)
    defer { try? file.close() }
    let data = try file.read(upToCount: 2 * 1024 * 1024 + 1) ?? Data()
    let signature: [UInt8] = [137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82]
    try require(
      data.count >= 24 && data.count <= 2 * 1024 * 1024 && data.starts(with: signature),
      "Välj en PNG-bild på högst 2 MB och 2048 × 2048 bildpunkter.")
    func big(_ i: Int) -> UInt32 { data[i..<(i + 4)].reduce(0) { ($0 << 8) | UInt32($1) } }
    let w = big(16)
    let h = big(20)
    try require(
      w > 0 && h > 0 && w <= 2048 && h <= 2048, "Logotypen får vara högst 2048 × 2048 bildpunkter.")
    guard let source = CGImageSourceCreateWithData(data as CFData, nil),
      let image = CGImageSourceCreateImageAtIndex(source, 0, nil)
    else { throw fail("LOGO", "PNG-bilden kunde inte öppnas.") }
    try require(image.width == Int(w) && image.height == Int(h), "Ogiltiga bildmått.")
    return data
  }
  static func loadedLogo(_ store: Store, _ a: AppearanceSettings) -> Data? {
    guard AppearanceSettings.ownedLogo(a.LogoFile) else { return nil }
    return try? logoData(store.url("branding/" + a.LogoFile))
  }
  static func save(_ store: Store, _ settings: AppearanceSettings, logo: Data?) throws
    -> AppearanceSettings
  {
    try settings.validate()
    let before = load(store)
    var next = settings
    var created: String?
    do {
      if let data = logo {
        try require(data.count <= 2 * 1024 * 1024, "Logotypen är för stor.")
        try FileManager.default.createDirectory(
          at: store.url("branding"), withIntermediateDirectories: true,
          attributes: [.posixPermissions: 0o700])
        next.LogoFile =
          "logo-" + UUID().uuidString.replacingOccurrences(of: "-", with: "").lowercased() + ".png"
        created = "branding/" + next.LogoFile
        try store.writeData(created!, data)
        _ = try logoData(store.url(created!))
      } else {
        next.LogoFile = ""
      }
      try store.write("utseende.json", next)
    } catch {
      if let created { try? store.remove(created) }
      throw error
    }
    if AppearanceSettings.ownedLogo(before.LogoFile) && before.LogoFile != next.LogoFile {
      try? store.remove("branding/" + before.LogoFile)
    }
    return next
  }
}
