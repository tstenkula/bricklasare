import AppKit
import Darwin

let arguments = CommandLine.arguments
if let index = arguments.firstIndex(of: "--install-update"), arguments.count > index + 1 {
  do { try Updates.install(URL(fileURLWithPath: arguments[index + 1])); exit(0) }
  catch {
    if let store = try? Store() { try? store.writeData("uppdatering-status.txt", Data("Uppdateringen misslyckades. Starta appen igen. Tidigare version finns kvar.\n".utf8)) }
    exit(1)
  }
}
// Older installed helpers invoke --self-test. Preserve that spelling as a quiet
// installation check; full UI regression tests require an explicit separate flag.
if arguments.contains("--self-test") || arguments.contains("--validate-installation") {
  do {
    try Updates.validateInstallation()
    print("PASS: installation validated without application windows or reader access.")
    exit(0)
  } catch {
    fputs("INSTALLATION VALIDATION FAILED\n", stderr)
    exit(1)
  }
}
let application = NSApplication.shared
application.setActivationPolicy(.regular)
application.appearance = NSAppearance(named: .aqua)
if let index = arguments.firstIndex(of: "--verify-update-archive"), arguments.count > index + 1 {
  do { try Updates.validateZip(Data(contentsOf: URL(fileURLWithPath: arguments[index + 1]))); print("PASS: update archive paths, types and limits"); exit(0) }
  catch { fputs("UPDATE ARCHIVE FAILED: \(error)\n", stderr); exit(1) }
}
if arguments.contains("--full-self-test") {
  do {
    try SelfTests.run()
    exit(0)
  } catch {
    fputs("SELF-TEST FAILED: \(error)\n", stderr)
    exit(1)
  }
}
if let index = arguments.firstIndex(of: "--render-tests"), arguments.count > index + 1 {
  do {
    try SelfTests.render(URL(fileURLWithPath: arguments[index + 1], isDirectory: true))
    exit(0)
  } catch {
    fputs("RENDER TEST FAILED: \(error)\n", stderr)
    exit(1)
  }
}
do {
  let store = try Store()
  // Shared lock also covers copies of the app launched from different folders.
  let lockFD = open(store.url("app.lock").path, O_CREAT | O_RDWR | O_NOFOLLOW, 0o600)
  guard lockFD >= 0 else {
    throw fail("LOCK", "Programmet kunde inte låsa den lokala inställningsmappen.")
  }
  guard flock(lockFD, LOCK_EX | LOCK_NB) == 0 else {
    close(lockFD)
    throw fail("ALREADY_RUNNING", "Brickläsaren är redan igång för det här Mac-kontot.")
  }
  defer {
    flock(lockFD, LOCK_UN)
    close(lockFD)
  }
  let controller = AppController(store: store)
  application.delegate = controller
  withExtendedLifetime(controller) { application.run() }
} catch {
  showError(error)
  exit(1)
}
