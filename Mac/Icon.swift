// Build-time vector drawing, not a bundled runtime dependency.
import AppKit

let path = CommandLine.arguments[1]
try FileManager.default.createDirectory(atPath: path, withIntermediateDirectories: true)
for (points, scale) in [
  (16, 1), (16, 2), (32, 1), (32, 2), (128, 1), (128, 2), (256, 1), (256, 2), (512, 1), (512, 2),
] {
  let pixels = points * scale
  let image = NSImage(size: NSSize(width: pixels, height: pixels))
  image.lockFocus()
  let ctx = NSGraphicsContext.current!.cgContext
  ctx.scaleBy(x: CGFloat(pixels) / 1024, y: CGFloat(pixels) / 1024)
  NSColor(srgbRed: 0, green: 59 / 255, blue: 85 / 255, alpha: 1).setFill()
  NSBezierPath(
    roundedRect: NSRect(x: 52, y: 52, width: 920, height: 920), xRadius: 204, yRadius: 204
  ).fill()
  ctx.saveGState()
  ctx.translateBy(x: 512, y: 512)
  ctx.rotate(by: -0.18)
  NSColor.white.setFill()
  NSBezierPath(
    roundedRect: NSRect(x: -165, y: -274, width: 330, height: 548), xRadius: 150, yRadius: 150
  ).fill()
  NSColor(srgbRed: 0, green: 59 / 255, blue: 85 / 255, alpha: 1).setFill()
  NSBezierPath(ovalIn: NSRect(x: -43, y: 145, width: 86, height: 86)).fill()
  NSColor(srgbRed: 0, green: 59 / 255, blue: 85 / 255, alpha: 1).setStroke()
  for radius: CGFloat in [65, 110, 155] {
    let arc = NSBezierPath()
    arc.appendArc(withCenter: NSPoint(x: 0, y: -100), radius: radius, startAngle: 35, endAngle: 145)
    arc.lineWidth = 22
    arc.lineCapStyle = .round
    arc.stroke()
  }
  ctx.restoreGState()
  image.unlockFocus()
  let rep = NSBitmapImageRep(data: image.tiffRepresentation!)!
  let filename = "icon_\(points)x\(points)" + (scale == 2 ? "@2x" : "") + ".png"
  try rep.representation(using: .png, properties: [:])!.write(
    to: URL(fileURLWithPath: path).appendingPathComponent(filename))
}
