import Foundation
import Darwin

extension SelfTests {
  static func updates(_ store: Store) throws {
    let fixture = Data(
      base64Encoded:
        "QlJJQ0tMQVNBUkUtVVBEQVRFLTEKZXlKVFkyaGxiV0VpT2pFc0lrTm9ZVzV1Wld3aU9pSndjbVYyYVdWM0lpd2lWMmx1Wkc5M2N5STZleUpXWlhKemFXOXVJam9pT1RrdU1TNHdJaXdpVlhKc0lqb2lhSFIwY0hNNkx5OW5hWFJvZFdJdVkyOXRMM1J6ZEdWdWEzVnNZUzlpY21samEyeGhjMkZ5WlM5eVpXeGxZWE5sY3k5a2IzZHViRzloWkM5MFpYTjBMMlpwZUhSMWNtVXVlbWx3SWl3aVUyaGhNalUySWpvaVpqRTJaREExWldNMllqSTVNalE0WkRKak5qRmhaR0l4WlRreU5qTm1OemhsTkdZM1ltRmpaVEZpT1RVMU1ERTBZVEprTVRjNE56SmpabVUwTURZMFpDSXNJbE5wZW1VaU9qY3NJazV2ZEdWeklqb2lVM2x1ZEdobGRHbGpJSFJsYzNRZ1ptbDRkSFZ5WlRzZ2JtVjJaWElnWkc5M2JteHZZV1JsWkM0aWZTd2lUV0ZqSWpwN0lsWmxjbk5wYjI0aU9pSTVPUzR4TGpBaUxDSlZjbXdpT2lKb2RIUndjem92TDJkcGRHaDFZaTVqYjIwdmRITjBaVzVyZFd4aEwySnlhV05yYkdGellYSmxMM0psYkdWaGMyVnpMMlJ2ZDI1c2IyRmtMM1JsYzNRdlptbDRkSFZ5WlM1NmFYQWlMQ0pUYUdFeU5UWWlPaUptTVRaa01EVmxZelppTWpreU5EaGtNbU0yTVdGa1lqRmxPVEkyTTJZM09HVTBaamRpWVdObE1XSTVOVFV3TVRSaE1tUXhOemczTW1ObVpUUXdOalJrSWl3aVUybDZaU0k2Tnl3aVRtOTBaWE1pT2lKVGVXNTBhR1YwYVdNZ2RHVnpkQ0JtYVhoMGRYSmxPeUJ1WlhabGNpQmtiM2R1Ykc5aFpHVmtMaUo5ZlE9PQpBc0pXSEJXTFB3M0VIRWNrT244Z0IydFM3ODNzY1BhRUlMQkYvL24xMTNqRjE1QmRHektMTnN3QzdmN3hSRnA5ZVRSSEtwSWlFTGg3L1A3WXE5U3NPcHV2cVppUGorS2RnbG8rTG52MzUzZHJJa0pNMG9tMklZUlJKOEJ3RTQ3N2NRWHpVQTJFSGplYncvWnRLS3ZEaFdBREN3cGw1UlpKS0kyWVUyOXk5blhVbjFDLzhYR2d5UU9ld1BDbDZaNVVPMHROeW5JdHRjMTdxVTJWckFnUGVDYUxIWmhTdG9VaEs5a3NlaUx3UDFtMk1YMkNheEllMFFkM2dPaDdBdlkrY0NaYmJQWE53N05hV2FrV1B6VXc0M1ZTTUdWRmRTYXVXblRROExQWnd6YmZDWE9VMnpPZnhmYitFQ2F3QmdsQ1dvVm1pV3pkeVBWNzc1b05KM3BMN0krU0tmV0tHcGFGVEljNmdoUUQraE9QNWdZZ2pLUUJaQkh4VUEwWE96UW5WSjlHYml5U2Q1TGRyRW9LNU9yZS9COTdickN6RXJtZXR3T2p1NVNnSnRJY0Y0SUVjcEYrQ0E4RWhNZmJWaXdEekFaQUlybHhXc2dMU3NuRXdKKzR2T3VtUUxZdkRPT0FSR0s4T2JqQkZmcGhZSUYwdDdNNnk1enNLMHhsSXRIZgo="
    )!
    let asset = try Updates.decode(fixture, channel: "preview")!
    try check(asset.Version == "99.1.0", "Signed feed is accepted")
    _ = try Updates.authenticated(asset)
    var unsigned = asset
    unsigned.signedFeed = nil
    try rejects("Unsigned local asset cannot authorize staging") { _ = try Updates.authenticated(unsigned) }
    let substituted = UpdateAsset(Version: "99.2.0", Url: asset.Url, Sha256: asset.Sha256,
      Size: asset.Size, Notes: nil, signedFeed: fixture, channel: "preview")
    try rejects("Mutable metadata cannot replace signed fields") { _ = try Updates.authenticated(substituted) }
    let installDirectory = store.url("update-auth-tests")
    try FileManager.default.createDirectory(at: installDirectory, withIntermediateDirectories: false)
    let envelopeURL = installDirectory.appendingPathComponent("release.feed")
    let archiveURL = installDirectory.appendingPathComponent("package.zip")
    try fixture.write(to: envelopeURL)
    try Data("fixture".utf8).write(to: archiveURL)
    let (installedAsset, installedBytes) = try Updates.installPackage(installDirectory, channel: "preview")
    try check(installedAsset.Version == asset.Version && installedBytes == Data("fixture".utf8),
      "Final installer authenticates original envelope and exact bytes")
    try rejects("Final installer rejects substituted channel") {
      _ = try Updates.installPackage(installDirectory, channel: "stable")
    }
    try Data("changed".utf8).write(to: archiveURL)
    try rejects("Final installer rejects replaced archive") {
      _ = try Updates.installPackage(installDirectory, channel: "preview")
    }
    try Data("fixture".utf8).write(to: archiveURL)
    var alteredEnvelope = fixture
    alteredEnvelope[40] ^= 1
    try alteredEnvelope.write(to: envelopeURL)
    try rejects("Final installer rejects replaced signed envelope") {
      _ = try Updates.installPackage(installDirectory, channel: "preview")
    }
    try fixture.write(to: envelopeURL)
    let linked = installDirectory.appendingPathComponent("linked.zip")
    try FileManager.default.createSymbolicLink(at: linked, withDestinationURL: archiveURL)
    try rejects("Installer refuses symlinked update inputs") { _ = try Updates.readRegular(linked, maximum: 100) }
    try Data(repeating: 65, count: 65537).write(to: envelopeURL)
    try rejects("Installer bounds envelope reads") {
      _ = try Updates.installPackage(installDirectory, channel: "preview")
    }
    let zipBytes = Data(base64Encoded: "UEsDBBQAAAAIAIC8Y10Kp1u2EQAAAA8AAAAaAAAAQnJpY2tsYXNhcmVuLmFwcC9wcm9iZS50eHQrSy3KTMtMTdHNTc3NL6oEAFBLAQIUAxQAAAAIAIC8Y10Kp1u2EQAAAA8AAAAaAAAAAAAAAAAAAACAAQAAAABCcmlja2xhc2FyZW4uYXBwL3Byb2JlLnR4dFBLBQYAAAAAAQABAEgAAABJAAAAAAA=")!
    try Updates.validateZip(zipBytes)
    let extracted = installDirectory.appendingPathComponent("from-memory")
    try FileManager.default.createDirectory(at: extracted, withIntermediateDirectories: false)
    try Updates.run("/usr/bin/ditto", ["-x", "-k", "-", extracted.path], input: zipBytes)
    try check(try String(contentsOf: extracted.appendingPathComponent("Bricklasaren.app/probe.txt"))
      == "verified-memory", "Installer extracts verified memory snapshot through stdin")
    let childPID = installDirectory.appendingPathComponent("timed-out-child.pid")
    try rejects("Installer terminates a child that ignores graceful cancellation") {
      try Updates.run("/bin/sh", ["-c", "trap '' TERM; echo $$ > \"$1\"; while :; do :; done",
        "installer-timeout-test", childPID.path], timeout: 1)
    }
    let stoppedPID = Int32(try String(contentsOf: childPID).trimmingCharacters(in: .whitespacesAndNewlines))!
    try check(kill(stoppedPID, 0) != 0, "Timed-out installer child cannot outlive cleanup")
    try FileManager.default.removeItem(at: installDirectory)
    try rejects("Channel cannot be substituted") {
      _ = try Updates.decode(fixture, channel: "stable")
    }
    var changed = fixture
    changed[40] ^= 1
    try rejects("Tampered feed fails closed") {
      _ = try Updates.decode(changed, channel: "preview")
    }
    try Updates.verify(Data("fixture".utf8), asset: asset)
    try rejects("Archive hash cannot be substituted") {
      try Updates.verify(Data("changed".utf8), asset: asset)
    }
    for version in ["3.5", "3.5.1.1", "3.5.-1", "65536.0.0", "3.5.1\n", "x.0.0"] {
      try rejects("Invalid version") { _ = try Updates.version(version) }
    }
    try check(
      try Updates.version("3.10.0").lexicographicallyPrecedes(Updates.version("3.9.0")) == false,
      "Versions compare numerically")
    try rejects("Non-ZIP rejected") { try Updates.validateZip(Data(repeating: 0, count: 40)) }
    try check(!Updates.pending(store), "Clean user store permits updates")
    try store.writeData("aterstall-test.json", Data("pending".utf8))
    try check(Updates.pending(store), "Recovery journal blocks installation")
    try store.remove("aterstall-test.json")
    try check(
      !WorkflowSettings().sounds && WorkflowSettings().copy == 0, "Clipboard and sounds opt in")
    try check(
      WorkflowSettings(copy: 3).copyValue(("123", "456"), combo: true) == "mR: 123\neR: 456",
      "Copy labels match visible outputs")
    try check(
      WorkflowSettings(copy: 2).copyValue(("123", ""), combo: false) == nil,
      "Absent second output is not copied")
    let controller = AppController(store: store, preview: true)
    controller.makeWindow()
    controller.outputs = ("123", "456")
    controller.info = ReaderInfo(
      board: "mock", id: "", firmware: "", profile: "Standardformat", pending: false, ready: true)
    controller.invalidateConnection()
    try check(
      controller.outputs == nil && controller.info == nil && controller.reconnectPending,
      "USB/wake invalidation removes results and readiness")
    controller.window.close()
    let target = store.url("old.app")
    let candidate = store.url("new.app")
    let backup = store.url("backup.app")
    try FileManager.default.createDirectory(at: target, withIntermediateDirectories: false)
    try Data("old".utf8).write(to: target.appendingPathComponent("version"))
    try rejects("Failed replacement rolls back") {
      try Updates.replace(target: target, candidate: candidate, backup: backup)
    }
    try check(
      try String(contentsOf: target.appendingPathComponent("version")) == "old",
      "Old version remains after failed swap")
    try FileManager.default.createDirectory(at: candidate, withIntermediateDirectories: false)
    try Data("new".utf8).write(to: candidate.appendingPathComponent("version"))
    try store.writeData("user-setting.txt", Data("keep".utf8))
    try Updates.replace(target: target, candidate: candidate, backup: backup)
    try check(
      try String(contentsOf: target.appendingPathComponent("version")) == "new"
        && String(contentsOf: backup.appendingPathComponent("version")) == "old"
        && String(contentsOf: store.url("user-setting.txt")) == "keep",
      "Swap keeps backup and user settings")
  }
}
