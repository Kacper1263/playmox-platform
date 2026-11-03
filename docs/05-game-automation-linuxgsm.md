# 05. Game Server Automation & LinuxGSM Connectors

Playmox automates the installation, configuration, systemd service management, live telemetry probing, and backup/restore lifecycle of dedicated game servers inside guest VMs using a modular **Connector Architecture** (`API/Services/Platform/Games/`).

---

## 1. Supported Game Connectors

| Game Key | Enum (`GameServerKind`) | Connector Class | LinuxGSM ID | Default Port(s) | Protocol |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `blank-vm` | `BlankVm` | `BlankVmGameServerConnector` | *(none)* | *(none)* | N/A |
| `minecraft-java` | `MinecraftJava` | `MinecraftJavaGameServerConnector` | `mcserver` | `25565` (1 port) | `tcp` |
| `terraria` | `Terraria` | `TerrariaGameServerConnector` | `terrariaserver` | `7777` (1 port) | `udp,tcp` |
| `unturned` | `Unturned` | `UnturnedGameServerConnector` | `untserver` | `27015`–`27016` (2 ports) | `udp,tcp` |
| `factorio` | `Factorio` | `FactorioGameServerConnector` | `fctrserver` | `34197` (1 port) | `udp,tcp` |

---

## 2. How LinuxGSM Automation Works

All game server operations run under the unprivileged `gameserver` Linux account (`/home/gameserver`) inside the guest VM, orchestrated over SSH/SFTP by `ProxmoxAutomationClient` and the respective `IGameServerConnector`.

### 2.1 Dependency & Runtime Provisioning
During `InstallOrUpdateAsync`:
1. Playmox waits for any background `apt`/`dpkg`/`unattended-upgrades` locks inside the freshly booted Ubuntu 24.04 VM to finish.
2. Installs required base packages (`curl`, `wget`, `file`, `tar`, `bzip2`, `gzip`, `unzip`, `bsdmainutils`, `python3`, `util-linux`, `ca-certificates`, `binutils`, `bc`, `jq`, `tmux`, `netcat-openbsd`, `lib32gcc-s1`, `lib32stdc++6`, `libsdl2-2.0-0:i386`, `steamcmd`).
3. **Game-Specific Runtime Adjustments**:
   - **Minecraft Java (`mcserver`)**: Inspects the requested `serverVersion` (e.g., `1.20.4`, `1.16.5`, `1.12.2`, or latest Mojang release manifest) and automatically installs the matching OpenJDK (`openjdk-21-jre-headless` for 1.20.5+, `openjdk-17-jre-headless` for 1.17–1.20.4, or `openjdk-8-jre-headless` for legacy versions). It also calculates an optimal JVM `-Xmx` heap size (~75% of the VM's allocated RAM, clamped to safe bounds) and writes `eula=true`.
   - **Terraria (`terrariaserver`)**: Pre-creates `/home/gameserver/.local/share/Terraria/Worlds`, generates `serverconfig.txt` and LinuxGSM `common.cfg`, and auto-generates the world file on first start if it does not yet exist.
   - **Unturned (`untserver`)**: Configures `Commands.dat` (`Name`, `Port`, `MaxPlayers`, `Map`, `Mode`, `PvE`, `Password`, `Perspective`, `Cheats`, `Welcome`) and `Config.json`, and binds consecutive ports (`GamePublicPortStart` and `GamePublicPortStart + 1`).
   - **Factorio (`fctrserver`)**: Downloads the Factorio headless server via LinuxGSM, generates an initial map save (`savename.zip`) if missing, and populates `server-settings.json` using `jq`.

### 2.2 The `playmox-game.service` Systemd Wrapper
To ensure the game server starts automatically when the VM boots and can be reliably monitored or stopped by Playmox, every game connector registers `/etc/systemd/system/playmox-game.service`:

```ini
[Unit]
Description=Playmox Game Server (<server_id>)
After=network-online.target
Wants=network-online.target

[Service]
Type=forking
User=gameserver
WorkingDirectory=/home/gameserver
ExecStart=/home/gameserver/<server_id> start
ExecStop=/home/gameserver/<server_id> stop
Restart=on-failure
RestartSec=10
TimeoutStartSec=300
TimeoutStopSec=120

[Install]
WantedBy=multi-user.target
```

---

## 3. SteamCMD Authentication & Steam Guard Mobile Support

Games downloaded via SteamCMD (such as **Terraria** and **Unturned**) support two authentication modes:
1. **Anonymous Mode (Default)**: Uses `steamuser="anonymous"`. Works out of the box for dedicated servers that allow anonymous SteamCMD app depot downloads (such as Unturned `1110390`).
2. **Authenticated Steam Account Mode**:
   - Users can save their personal Steam username and password (encrypted at rest via `SecureSecret.Encrypt`) under **Platform Settings** (`PUT /api/users/me/platform-settings`) or override credentials when creating an instance.
   - During `InstallGameServer` / `ReinstallGameServer`, Playmox streams the live SSH installation output (`/api/instances/{id}/live-command-stream`) directly to the user's browser every few seconds.
   - If the account is protected by **Steam Guard Mobile Authenticator**, SteamCMD outputs the confirmation prompt in real time, and the Playmox UI displays a prominent **Steam Guard Approval Required** banner instructing the user to approve the sign-in request in their Steam Mobile App while the installation job waits for confirmation.

---

## 4. Live Runtime Telemetry Probes (`GetRuntimeInfoAsync`)

When a user opens the **Instances** page or inspects a running VM (`GET /api/instances/{id}/game-runtime`), the active `IGameServerConnector` executes a lightweight probe over SSH:
- Checks `systemctl is-active playmox-game.service` and `tmux` session status.
- Inspects listening sockets via `ss -lntup` to confirm the game server process is bound to its expected port.
- Extracts game-specific live metadata:
  - **Server Version** (e.g., Minecraft version from server logs/properties, Factorio binary version, Terraria/Unturned build info).
  - **Online Players / Max Players** (parsed from server status/logs where available).
  - **Direct Connect Endpoint** (`<ExternalAddress or HostIP>:<GamePublicPortStart>`).

---

## 5. One-Click Backups & Restore (`CreateBackupAsync` / `RestoreBackupAsync`)

Playmox provides built-in world and configuration backups over SFTP without requiring external storage agents:

### Creating a Backup (`GET /api/instances/{id}/game/backup`)
1. Playmox connects to the guest VM via SSH and creates a compressed `.tar.gz` archive in `/tmp` containing the game's world saves and configuration directories (e.g., `serverfiles/`, `lgsm/config-lgsm/`, `.local/share/Terraria/Worlds/`, or Factorio `saves/`).
2. Streams the archive over SFTP (`Renci.SshNet.SftpClient`) directly to the user's browser as `<instance-name>-<game>-backup-<timestamp>.tar.gz` and cleans up the temporary file on the guest VM.

### Restoring a Backup (`POST /api/instances/{id}/game/restore`)
1. Accepts a `.tar.gz` upload up to **500 MB** via multipart form upload.
2. Gracefully stops `playmox-game.service` on the guest VM so open world files are flushed and unlocked.
3. Uploads the archive to the guest VM over SFTP, validates archive paths against directory traversal, extracts the contents into `/home/gameserver` with `gameserver:gameserver` ownership, re-applies essential port bindings, and restarts `playmox-game.service`.
