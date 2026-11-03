import Foundation

extension SelfTests {
  static func updates(_ store: Store) throws {
    let fixture = Data(
      base64Encoded:
        "QlJJQ0tMQVNBUkUtVVBEQVRFLTEKZXlKVFkyaGxiV0VpT2pFc0lrTm9ZVzV1Wld3aU9pSndjbVYyYVdWM0lpd2lWMmx1Wkc5M2N5STZleUpXWlhKemFXOXVJam9pT1RrdU1TNHdJaXdpVlhKc0lqb2lhSFIwY0hNNkx5OW5hWFJvZFdJdVkyOXRMM1J6ZEdWdWEzVnNZUzlpY21samEyeGhjMkZ5WlM5eVpXeGxZWE5sY3k5a2IzZHViRzloWkM5MFpYTjBMMlpwZUhSMWNtVXVlbWx3SWl3aVUyaGhNalUySWpvaVpqRTJaREExWldNMllqSTVNalE0WkRKak5qRmhaR0l4WlRreU5qTm1OemhsTkdZM1ltRmpaVEZpT1RVMU1ERTBZVEprTVRjNE56SmpabVUwTURZMFpDSXNJbE5wZW1VaU9qY3NJazV2ZEdWeklqb2lVM2x1ZEdobGRHbGpJSFJsYzNRZ1ptbDRkSFZ5WlRzZ2JtVjJaWElnWkc5M2JteHZZV1JsWkM0aWZTd2lUV0ZqSWpwN0lsWmxjbk5wYjI0aU9pSTVPUzR4TGpBaUxDSlZjbXdpT2lKb2RIUndjem92TDJkcGRHaDFZaTVqYjIwdmRITjBaVzVyZFd4aEwySnlhV05yYkdGellYSmxMM0psYkdWaGMyVnpMMlJ2ZDI1c2IyRmtMM1JsYzNRdlptbDRkSFZ5WlM1NmFYQWlMQ0pUYUdFeU5UWWlPaUptTVRaa01EVmxZelppTWpreU5EaGtNbU0yTVdGa1lqRmxPVEkyTTJZM09HVTBaamRpWVdObE1XSTVOVFV3TVRSaE1tUXhOemczTW1ObVpUUXdOalJrSWl3aVUybDZaU0k2Tnl3aVRtOTBaWE1pT2lKVGVXNTBhR1YwYVdNZ2RHVnpkQ0JtYVhoMGRYSmxPeUJ1WlhabGNpQmtiM2R1Ykc5aFpHVmtMaUo5ZlE9PQpBc0pXSEJXTFB3M0VIRWNrT244Z0IydFM3ODNzY1BhRUlMQkYvL24xMTNqRjE1QmRHektMTnN3QzdmN3hSRnA5ZVRSSEtwSWlFTGg3L1A3WXE5U3NPcHV2cVppUGorS2RnbG8rTG52MzUzZHJJa0pNMG9tMklZUlJKOEJ3RTQ3N2NRWHpVQTJFSGplYncvWnRLS3ZEaFdBREN3cGw1UlpKS0kyWVUyOXk5blhVbjFDLzhYR2d5UU9ld1BDbDZaNVVPMHROeW5JdHRjMTdxVTJWckFnUGVDYUxIWmhTdG9VaEs5a3NlaUx3UDFtMk1YMkNheEllMFFkM2dPaDdBdlkrY0NaYmJQWE53N05hV2FrV1B6VXc0M1ZTTUdWRmRTYXVXblRROExQWnd6YmZDWE9VMnpPZnhmYitFQ2F3QmdsQ1dvVm1pV3pkeVBWNzc1b05KM3BMN0krU0tmV0tHcGFGVEljNmdoUUQraE9QNWdZZ2pLUUJaQkh4VUEwWE96UW5WSjlHYml5U2Q1TGRyRW9LNU9yZS9COTdickN6RXJtZXR3T2p1NVNnSnRJY0Y0SUVjcEYrQ0E4RWhNZmJWaXdEekFaQUlybHhXc2dMU3NuRXdKKzR2T3VtUUxZdkRPT0FSR0s4T2JqQkZmcGhZSUYwdDdNNnk1enNLMHhsSXRIZgo="
    )!
    let asset = try Updates.decode(fixture, channel: "preview")!
    try check(asset.Version == "99.1.0", "Signed feed is accepted")
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
