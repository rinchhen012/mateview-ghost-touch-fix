import AppKit
import Darwin

final class MenuBarDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    private let appPath: String
    private let parentProcessID: pid_t
    private var statusItem: NSStatusItem?
    private var parentCheckTimer: Timer?
    private var protectionItem: NSMenuItem?
    private var startupItem: NSMenuItem?
    private var volumeItems: [NSMenuItem] = []

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
        menu.delegate = self
        let showItem = NSMenuItem(
            title: "Show MateView Guardian",
            action: #selector(sendShowSettings),
            keyEquivalent: "")
        showItem.target = self
        menu.addItem(showItem)

        let protection = NSMenuItem(
            title: "Protection",
            action: #selector(toggleProtection),
            keyEquivalent: "")
        protection.target = self
        menu.addItem(protection)
        protectionItem = protection

        let volumeMenu = NSMenu(title: "Target Volume")
        for volume in stride(from: 0, through: 100, by: 10) {
            let volumeItem = NSMenuItem(
                title: "Target Volume \(volume)",
                action: #selector(setVolume(_:)),
                keyEquivalent: "")
            volumeItem.tag = volume
            volumeItem.target = self
            volumeMenu.addItem(volumeItem)
            volumeItems.append(volumeItem)
        }
        let volumeRoot = NSMenuItem(title: "Target Volume", action: nil, keyEquivalent: "")
        volumeRoot.submenu = volumeMenu
        menu.addItem(volumeRoot)

        let startup = NSMenuItem(
            title: "Start at Login",
            action: #selector(toggleStartup),
            keyEquivalent: "")
        startup.target = self
        menu.addItem(startup)
        startupItem = startup

        let diagnostics = NSMenuItem(
            title: "Diagnostics",
            action: #selector(showDiagnostics),
            keyEquivalent: "")
        diagnostics.target = self
        menu.addItem(diagnostics)
        menu.addItem(.separator())
        let quitItem = NSMenuItem(
            title: "Quit MateView Guardian",
            action: #selector(quitGuardian),
            keyEquivalent: "q")
        quitItem.target = self
        menu.addItem(quitItem)

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

    @objc private func sendShowSettings() {
        if !sendCommand("show-settings") {
            showGuardian()
        }
    }

    @objc private func toggleProtection() {
        _ = sendCommand("toggle-protection")
    }

    @objc private func setVolume(_ sender: NSMenuItem) {
        _ = sendCommand("set-volume-\(sender.tag)")
    }

    @objc private func toggleStartup() {
        _ = sendCommand("toggle-startup")
    }

    @objc private func showDiagnostics() {
        _ = sendCommand("diagnostics")
    }

    @objc private func quitGuardian() {
        if !sendCommand("quit") {
            kill(parentProcessID, SIGTERM)
        }
    }

    @objc private func checkParent() {
        if kill(parentProcessID, 0) != 0 && errno == ESRCH {
            NSApp.terminate(nil)
        }
    }

    func menuWillOpen(_ menu: NSMenu) {
        let settings = readSettings()
        protectionItem?.state = settings.protectionEnabled ? .on : .off
        startupItem?.state = settings.startAtLogin ? .on : .off
        for item in volumeItems {
            item.state = item.tag == settings.desiredVolume ? .on : .off
        }
    }
}

private struct GuardianMenuSettings {
    let protectionEnabled: Bool
    let desiredVolume: Int
    let startAtLogin: Bool
}

private func readSettings() -> GuardianMenuSettings {
    let settingsURL = FileManager.default.homeDirectoryForCurrentUser
        .appendingPathComponent("Library/Application Support/MateViewGuardian/settings.json")
    guard
        let data = try? Data(contentsOf: settingsURL),
        let values = try? JSONSerialization.jsonObject(with: data) as? [String: Any]
    else {
        return GuardianMenuSettings(protectionEnabled: true, desiredVolume: 30, startAtLogin: true)
    }
    return GuardianMenuSettings(
        protectionEnabled: values["ProtectionEnabled"] as? Bool ?? true,
        desiredVolume: values["DesiredVolume"] as? Int ?? 30,
        startAtLogin: values["StartAtLogin"] as? Bool ?? true)
}

private func sendCommand(_ command: String) -> Bool {
    let descriptor = socket(AF_UNIX, SOCK_STREAM, 0)
    guard descriptor >= 0 else {
        return false
    }
    defer { close(descriptor) }

    var address = sockaddr_un()
    address.sun_family = sa_family_t(AF_UNIX)
    let socketPath = "/tmp/mateview-guardian-\(geteuid()).sock"
    let pathBytes = Array(socketPath.utf8) + [0]
    withUnsafeMutableBytes(of: &address.sun_path) { destination in
        pathBytes.withUnsafeBytes { source in
            destination.copyBytes(from: source)
        }
    }
    let length = socklen_t(MemoryLayout<sa_family_t>.size + pathBytes.count)
    let connected = withUnsafePointer(to: &address) { pointer in
        pointer.withMemoryRebound(to: sockaddr.self, capacity: 1) {
            connect(descriptor, $0, length)
        }
    }
    guard connected == 0 else {
        return false
    }
    return command.withCString { bytes in
        write(descriptor, bytes, strlen(bytes)) == strlen(bytes)
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
