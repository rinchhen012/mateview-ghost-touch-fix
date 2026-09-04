import AppKit
import Darwin

final class MenuBarDelegate: NSObject, NSApplicationDelegate {
    private let appPath: String
    private let parentProcessID: pid_t
    private var statusItem: NSStatusItem?
    private var parentCheckTimer: Timer?

    init(appPath: String, parentProcessID: pid_t) {
        self.appPath = appPath
        self.parentProcessID = parentProcessID
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory)

        let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        item.button?.image = NSImage(
            systemSymbolName: "speaker.wave.2.fill",
            accessibilityDescription: "MateView Guardian")
        item.button?.image?.isTemplate = true
        item.button?.toolTip = "MateView Guardian"

        let menu = NSMenu()
        let showItem = NSMenuItem(
            title: "Show MateView Guardian",
            action: #selector(showGuardian),
            keyEquivalent: "")
        showItem.target = self
        menu.addItem(showItem)

        item.menu = menu
        statusItem = item
        parentCheckTimer = Timer.scheduledTimer(
            timeInterval: 2,
            target: self,
            selector: #selector(checkParent),
            userInfo: nil,
            repeats: true)
    }

    @objc private func showGuardian() {
        NSWorkspace.shared.open(URL(fileURLWithPath: appPath))
    }

    @objc private func checkParent() {
        if kill(parentProcessID, 0) != 0 && errno == ESRCH {
            NSApp.terminate(nil)
        }
    }
}

func bundlePathFromExecutable() -> String {
    let executableURL = URL(fileURLWithPath: CommandLine.arguments[0])
    return executableURL
        .deletingLastPathComponent()
        .deletingLastPathComponent()
        .deletingLastPathComponent()
        .path
}

var appPath = bundlePathFromExecutable()
var parentProcessID = getppid()
var index = 1
while index < CommandLine.arguments.count {
    switch CommandLine.arguments[index] {
    case "--app-path" where index + 1 < CommandLine.arguments.count:
        appPath = CommandLine.arguments[index + 1]
        index += 2
    case "--parent-pid" where index + 1 < CommandLine.arguments.count:
        parentProcessID = Int32(CommandLine.arguments[index + 1]) ?? parentProcessID
        index += 2
    default:
        index += 1
    }
}

let app = NSApplication.shared
let delegate = MenuBarDelegate(appPath: appPath, parentProcessID: parentProcessID)
app.delegate = delegate
app.run()
