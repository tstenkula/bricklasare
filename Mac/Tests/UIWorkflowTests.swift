import AppKit

private final class SlowUSBProbe {
  let release = DispatchSemaphore(value: 0)
  let entered = DispatchSemaphore(value: 0)
  private let lock = NSLock()
  private var callCount = 0
  var calls: Int { lock.lock(); defer { lock.unlock() }; return callCount }
  func read() -> USBState {
    lock.lock(); callCount += 1; let call = callCount; lock.unlock()
    if call == 1 {
      entered.signal()
      _ = release.wait(timeout: .now() + 3)
    }
    return USBState(count: 1, fingerprint: "synthetic-reader", mode: 1)
  }
}

enum UIWorkflowTests {
  static func pump(until condition: () -> Bool, timeout: TimeInterval = 1) -> Bool {
    let end = Date().addingTimeInterval(timeout)
    while !condition() && Date() < end { RunLoop.current.run(until: Date().addingTimeInterval(0.005)) }
    return condition()
  }
  static func run(_ store: Store) throws {
    let c = AppController(store: store, preview: true)
    c.makeWindow()
    defer { c.window.close() }
    c.preference = FormatPreference(Mode: .combo, RcoChip: "M")
    let ready = ReaderInfo(board: "Simulated", id: "", firmware: "", profile: "Standardformat", pending: false, ready: true)
    c.info = ready
    c.readyMessage()
    c.rebuild()
    c.window.makeKeyAndOrderFront(nil)
    let originalRoot = c.window.contentView
    let view = c.readerView!
    try SelfTests.check(view.first.number.stringValue == "—" && !view.first.copyButton.isEnabled, "Empty result cannot be copied")
    let minimumContent = NSWindow.contentRect(forFrameRect: NSRect(origin: .zero, size: c.window.minSize), styleMask: c.window.styleMask).size
    for size in [NSSize(width: 900, height: 650), NSSize(width: 800, height: 600), minimumContent] {
      c.window.setContentSize(size)
      c.outputs = ("025800772", "051501029")
      c.readyMessage()
      c.notice = "Läsningen är klar. Kopiera önskat nummer."
      c.rebuild()
      c.window.contentView?.layoutSubtreeIfNeeded()
      _ = pump(until: { view.root.frame.height > 0 }, timeout: 0.1)
      c.window.contentView?.layoutSubtreeIfNeeded()
      let scroll = c.window.contentView as! NSScrollView
      try SelfTests.check(view.root.frame.maxY + 20 <= scroll.contentView.bounds.height + 1, "Normal results fit \(Int(size.width))×\(Int(size.height)) without scrolling (\(view.root.frame.maxY + 20))")
      try SelfTests.check(view.first.number.frame.width >= view.first.number.attributedStringValue.size().width, "First ID is not clipped")
      try SelfTests.check(view.second.number.frame.width >= view.second.number.attributedStringValue.size().width, "Second ID is not clipped")
    }
    try SelfTests.check(c.instruction == "Läsningen är klar", "Completed scan has the main instruction")
    try SelfTests.check(c.window.contentView === originalRoot && c.readerView?.readButton === view.readButton, "State updates retain the view tree")
    c.window.makeFirstResponder(view.first.copyButton)
    c.notice = "Kopierat"
    c.rebuild()
    try SelfTests.check(c.window.firstResponder === view.first.copyButton, "Status updates preserve keyboard focus")
    c.clearDisplayedResults()
    try SelfTests.check(c.outputs == nil && view.first.number.stringValue == "—" && view.second.number.stringValue == "—" && !view.bothButton.isEnabled && !view.clearButton.isEnabled, "Clear removes both numbers and copy actions immediately")
    c.outputs = ("025800772", "051501029"); c.rebuild()
    c.invalidateConnection()
    try SelfTests.check(c.outputs == nil && !view.readButton.isEnabled && !view.first.copyButton.isEnabled && view.first.number.stringValue == "—", "Disconnect clears results and disables reading/copying")
    let cancelledRead = CancelToken()
    c.reconnectPending = false; c.info = ready; c.busy = true; c.token = cancelledRead
    c.workflow.copy = 1
    let pasteboardBefore = NSPasteboard.general.changeCount
    // The worker has already produced values; cancellation happens before the queued UI publication.
    DispatchQueue.main.async {
      c.completeScan(("025800772", "051501029"), info: ready, preference: c.preference!, reference: false, cancellation: cancelledRead)
    }
    cancelledRead.cancel(); c.clearResults()
    try SelfTests.check(pump(until: { !c.busy }) && c.outputs == nil && !view.first.copyButton.isEnabled && NSPasteboard.general.changeCount == pasteboardBefore, "Late cancelled scan cannot publish or auto-copy results")
    let incompleteRead = CancelToken()
    c.busy = true; c.token = incompleteRead
    let notReady = ReaderInfo(board: "Simulated", id: "", firmware: "", profile: "Standardformat", pending: true, ready: false)
    c.completeScan(("025800772", "051501029"), info: notReady, preference: c.preference!, reference: false, cancellation: incompleteRead)
    try SelfTests.check(c.outputs == nil && view.first.number.stringValue == "—" && view.second.number.stringValue == "—" && !view.first.copyButton.isEnabled && NSPasteboard.general.changeCount == pasteboardBefore, "Unready final reader state cannot publish or auto-copy values")
    let disconnectedRead = CancelToken()
    c.busy = true; c.token = disconnectedRead
    c.invalidateConnection()
    c.completeScan(("025800772", "051501029"), info: ready, preference: c.preference!, reference: false, cancellation: disconnectedRead)
    try SelfTests.check(c.info == nil && c.outputs == nil && !view.readButton.isEnabled && NSPasteboard.general.changeCount == pasteboardBefore, "Disconnect wins over queued scan completion and auto-copy")
    c.workflow.copy = 0

    let formats = FormatEditor(FormatPreference(Mode: .axema, RcoChip: "M")) { _ in }
    try SelfTests.check(formats.chipSection.isHidden && formats.detail.stringValue == AccessMode.axema.detail, "Only relevant format options appear")
    formats.choices.selectItem(at: 2); formats.updateSelection()
    try SelfTests.check(!formats.chipSection.isHidden && formats.chip.isEnabled, "RCO selection reveals technology")
    let formatButtons = SelfTests.descendants(formats.dialog.contentView!).compactMap { $0 as? ActionButton }
    try SelfTests.check(formatButtons.contains { $0.primary && $0.keyEquivalent == "\r" } && formatButtons.contains { $0.title == "Avbryt" && $0.keyEquivalent == "\u{1b}" }, "Settings have Return and Escape actions")
    formats.dialog.close()
    let workflow = c.makeWorkflowDialog()
    let workflowButtons = SelfTests.descendants(workflow.contentView!).compactMap { $0 as? ActionButton }
    try SelfTests.check(workflowButtons.contains { $0.title == "Spara" && $0.primary } && workflowButtons.contains { $0.title == "Avbryt" }, "Workflow has consistent save/cancel actions")
    workflow.close()

    c.info = ready; c.reconnectPending = false; c.outputs = ("025800772", "051501029"); c.rebuild()
    let slow = SlowUSBProbe()
    let monitor = USBMonitor(timeout: 0.06, probe: slow.read)
    c.usbMonitor = monitor
    c.lastUSB = USBState(count: 1, fingerprint: "synthetic-reader", mode: 1)
    c.pollUSB()
    try SelfTests.check(slow.entered.wait(timeout: .now() + 1) == .success, "USB probe started in the background")
    c.pollUSB(); c.pollUSB()
    var mainResponsive = false
    DispatchQueue.main.async { mainResponsive = true }
    try SelfTests.check(pump(until: { mainResponsive }) && slow.calls == 1, "Blocked USB probe leaves main queue responsive and never overlaps")
    try SelfTests.check(pump(until: { monitor.timedOut }), "USB probe has a main-queue deadline")
    try SelfTests.check(c.info == nil && c.outputs == nil && !view.first.copyButton.isEnabled && !view.readButton.isEnabled && !view.connectButton.isEnabled, "USB deadline clears readiness, IDs, and actions")
    c.connect()
    try SelfTests.check(!c.busy, "Timed-out probe cannot trigger overlapping reconnect work")
    c.completeConnection(ready)
    try SelfTests.check(c.info == nil && !view.readButton.isEnabled && !c.shouldReconnect, "Old connection completion cannot restore readiness after deadline")
    slow.release.signal()
    try SelfTests.check(pump(until: { !monitor.inFlight }) && c.info == nil && c.outputs == nil, "Late probe cannot restore old readiness or numbers")
    c.pollUSB()
    try SelfTests.check(monitor.timedOut && !c.shouldReconnect, "Reconnect remains blocked until a fresh probe completes")
    try SelfTests.check(pump(until: { !monitor.inFlight }) && !monitor.timedOut && c.shouldReconnect, "Fresh probe with unchanged identity permits automatic reconnect after timeout")
    c.reconnectPending = false // The same transition made when the watch starts a fresh connection job.
    c.completeConnection(ready)
    try SelfTests.check(c.info?.ready == true && view.readButton.isEnabled && c.outputs == nil, "Fresh verified connection restores reading without stale numbers")
    var received = 1, failures = 0
    monitor.stop()

    let stale = SlowUSBProbe()
    let cancelled = USBMonitor(timeout: 0.5, probe: stale.read)
    cancelled.poll({ _ in received += 1 }, onTimeout: { failures += 1 })
    try SelfTests.check(stale.entered.wait(timeout: .now() + 1) == .success, "Generation test probe started")
    cancelled.invalidate()
    stale.release.signal()
    try SelfTests.check(pump(until: { !cancelled.inFlight }) && received == 1, "Invalidated generation discards a late result")
    cancelled.stop()
    cancelled.poll({ _ in received += 1 }, onTimeout: { failures += 1 })
    try SelfTests.check(!cancelled.inFlight && stale.calls == 1 && failures == 0, "Stopped monitor schedules nothing")

    let stopping = SlowUSBProbe()
    let stopped = USBMonitor(timeout: 0.04, probe: stopping.read)
    stopped.poll({ _ in received += 1 }, onTimeout: { failures += 1 })
    try SelfTests.check(stopping.entered.wait(timeout: .now() + 1) == .success, "Shutdown test probe started")
    stopped.stop()
    let afterDeadline = Date().addingTimeInterval(0.06)
    _ = pump(until: { Date() >= afterDeadline })
    try SelfTests.check(stopped.inFlight && failures == 0, "Stopped monitor cancels deadline while native call is still blocked")
    stopping.release.signal()
    try SelfTests.check(pump(until: { !stopped.inFlight }) && received == 1 && failures == 0, "Shutdown suppresses probe result and deadline")
    c.usbMonitor = nil

    c.info = ready; c.readyMessage(); c.rebuild()
    for _ in 0..<25 { c.rebuild(); c.window.contentView?.layoutSubtreeIfNeeded() }
    let begin = CFAbsoluteTimeGetCurrent()
    for i in 0..<250 {
      c.notice = i % 2 == 0 ? "Läsningen är klar." : "Numret är kopierat."
      c.rebuild(); c.window.contentView?.layoutSubtreeIfNeeded()
    }
    let elapsed = CFAbsoluteTimeGetCurrent() - begin
    try SelfTests.check(c.window.contentView === originalRoot, "250 warm UI updates retain the same controls")
    print(String(format: "UI warm update measurement: 250 updates + layout in %.1f ms (%.3f ms/update)", elapsed * 1000, elapsed * 4))
  }
}
