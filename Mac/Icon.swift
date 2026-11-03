// Build platform icon containers from the shared original artwork.
import AppKit

let args = CommandLine.arguments
let output = URL(fileURLWithPath: args[1], isDirectory: true)
let source = URL(fileURLWithPath: args.count > 2 ? args[2] : "Assets/Bricklasaren.png")
guard let image = NSImage(contentsOf: source) else { fatalError("Icon source missing") }
try FileManager.default.createDirectory(at: output, withIntermediateDirectories: true)
func png(_ pixels: Int) -> Data {
  let bitmap = NSBitmapImageRep(
    bitmapDataPlanes: nil, pixelsWide: pixels, pixelsHigh: pixels,
    bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
    colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
  NSGraphicsContext.saveGraphicsState()
  NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: bitmap)
  NSGraphicsContext.current?.imageInterpolation = .high
  image.draw(
    in: NSRect(x: 0, y: 0, width: pixels, height: pixels), from: .zero, operation: .copy,
    fraction: 1)
  NSGraphicsContext.restoreGraphicsState()
  return bitmap.representation(using: .png, properties: [:])!
}
for (points, scale) in [
  (16, 1), (16, 2), (32, 1), (32, 2), (128, 1), (128, 2), (256, 1), (256, 2), (512, 1), (512, 2),
] {
  let name = "icon_\(points)x\(points)" + (scale == 2 ? "@2x" : "") + ".png"
  try png(points * scale).write(to: output.appendingPathComponent(name))
}
if args.count > 3 {
  let sizes = [16, 24, 32, 48, 64, 128, 256]
  let images = sizes.map(png)
  var ico = Data()
  func u16(_ value: Int) {
    ico.append(UInt8(value & 255))
    ico.append(UInt8((value >> 8) & 255))
  }
  func u32(_ value: Int) {
    u16(value & 65535)
    u16(value >> 16)
  }
  u16(0)
  u16(1)
  u16(sizes.count)
  var offset = 6 + sizes.count * 16
  for (index, size) in sizes.enumerated() {
    ico.append(contentsOf: [UInt8(size % 256), UInt8(size % 256), 0, 0])
    u16(1)
    u16(32)
    u32(images[index].count)
    u32(offset)
    offset += images[index].count
  }
  for bytes in images { ico.append(bytes) }
  try ico.write(to: URL(fileURLWithPath: args[3]))
}
