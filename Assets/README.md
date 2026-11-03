# App icon

Original Tobtech Brickläsaren artwork: navy tile, ivory access tag and warm signal strokes. Shared by the Windows icon and macOS icon set. Covered by the project license.

Created using the built-in image generation tool. Prompt: a polished, readable desktop app icon with an ivory RFID key fob and three warm apricot signal strokes on a deep navy rounded square, transparent outside, without text or third-party branding.

`Mac/Icon.swift` converts the PNG to exact platform sizes. To regenerate the Windows container and Mac icon set:

```sh
swift Mac/Icon.swift /tmp/Bricklasaren.iconset Assets/Bricklasaren.png Program/Bricklasaren.ico
iconutil -c icns /tmp/Bricklasaren.iconset
```
