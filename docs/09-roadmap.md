# 09. Roadmap & Future Improvements

This document tracks completed milestones, prioritized technical tasks, and planned future enhancements for the Playmox platform.

---

## 1. Completed Milestones (v1.0 — Initial Release)

The core requirements established for the engineering thesis and initial open-source release are fully implemented:

- [x] **Automated KVM Provisioning on Proxmox VE**: Clones Cloud-Init templates (`VMID 9000`), injects dynamic credentials, discovers guest IPv4 via QEMU Guest Agent, and pins static Netplan configuration.
- [x] **Multi-Tenant Quota System**: Enforces limits on CPU cores, RAM (MB), disk size (GB), active instances, public ports, and SSH access per user, releasing CPU/RAM/port limits for stopped VMs.
- [x] **Automated Host DNAT & Guest Firewall**: Allocates collision-free public ports (`22000`–`32000`), synchronizes `nftables` DNAT rules on the Proxmox host via lifecycle hookscripts and periodic timers, and hardens guest `ufw` policies.
- [x] **LinuxGSM Integration & Modular Connectors**:
  - `blank-vm` (clean Ubuntu 24.04 KVM machine).
  - `minecraft-java` (dynamic OpenJDK 8/17/21 matching server version, `server.properties` sync, and dynamic JVM `-Xmx` heap scaling).
  - `terraria` (world auto-creation and `serverconfig.txt` management).
  - `unturned` (dual-port forwarding, SteamCMD integration, and `Commands.dat` / `Config.json` management).
  - `factorio` (headless server installation, save generation, and `server-settings.json` management via `jq`).
- [x] **Dynamic JSON Schema Forms**: Client-side MudBlazor forms rendered dynamically from `GameDefinition.JsonSchema` with two-way synchronization (`source=db` vs. `source=live`) and `customParameters` injection.
- [x] **Dual-Lane Asynchronous Job Pipeline**: Concurrency-controlled execution distinguishing hypervisor-level operations (`JobProcessingLane.Proxmox`, serialized) from guest-level tasks (`JobProcessingLane.VmScoped`, parallel across distinct VMs).
- [x] **In-Browser WebSocket SSH Console**: Direct interactive terminal using `SshWebSocketBridgeService` with terminal resize support and zero third-party proxy daemons.
- [x] **One-Click SFTP Backups & Restores**: Creation and extraction of `.tar.gz` world archives directly over SFTP via the Web UI.

---

## 2. High-Priority Technical Improvements (TODO)

The following architectural and security refactorings are planned for upcoming minor releases:

### 2.1 Per-User Random Salt & Argon2id PHC Hash Migration (Security)
- **Current State**: User passwords are encrypted with Argon2id using a platform-wide shared salt (`Security:PasswordSalt`).
- **Target**: Generate a cryptographically secure, per-user 16-byte random salt stored alongside the hash in standard PHC string format:
  ```text
  $argon2id$v=19$m=65536,t=3,p=1$<salt-b64>$<hash-b64>
  ```
- **Action Plan**:
  1. Update `API/Extensions/Auth/BaseAuth.cs` (`Crypto.cs`) to generate unique salts for new passwords.
  2. Implement an automatic hash-upgrade mechanism in `UsersRepository.cs` upon successful login of existing users so old hashes transition transparently.

### 2.2 Streaming SFTP Backup Downloads Without Full RAM Buffering (Performance)
- **Current State**: `LinuxGsmConnectorBase.CreateBackupArchiveAsync` downloads the `.tar.gz` archive from the guest VM into an in-memory `byte[]` before returning it to the caller.
- **Target**: Stream the SFTP archive directly to the client HTTP response stream or to a temporary disk-backed spool for large game worlds (several gigabytes).
- **Action Plan**:
  1. Refactor `IGameServerConnector.CreateBackupArchiveAsync` to return a `Stream` rather than `byte[]`.
  2. Implement progressive chunked transfer in `InstancesController.DownloadBackup`.

### 2.3 Bypass Response Buffering for Large Binary Streams in Middleware (Memory Efficiency)
- **Current State**: `PrettyErrorMiddleware` wraps `context.Response.Body` with a `MemoryStream` to intercept unhandled exceptions and format them as JSON.
- **Target**: Skip buffering when the requested path is a binary streaming endpoint (such as `/api/instances/{id}/game/backup` or file restore endpoints).
- **Action Plan**:
  1. Add path/content-type inspection in `PrettyErrorMiddleware.cs` to pass through large file downloads directly to the underlying response stream.

---

## 3. Medium-Term Roadmap (TODO)

### 3.1 Additional Game Server Connectors
Expand the catalog of built-in connectors with popular dedicated servers.

### 3.2 Scheduled Automated Backups & Off-Host Storage
- Add cron/interval-based backup scheduling in `VirtualGameInstance` settings.
- Implement automated backup retention policies (keep last $N$ daily/weekly backups).
- Support offloading backup archives to external storages.

### 3.3 User Password Self-Service in Web UI
- Provide a "Change Password" dialog within the user profile menu in Blazor WASM.
- Require verification of current password before applying changes.

### 3.4 Multi-User Instance Sharing & Sub-User Permissions
- Allow the instance owner to grant read-only or operator permissions to other platform users.
- Granular permission flags:
  - `CanStartStop`: Allowed to power on/off or restart.
  - `CanAccessSsh`: Allowed to open the web SSH console.
  - `CanEditConfig`: Allowed to modify game settings.
  - `CanManageBackups`: Allowed to download or restore backups.

### 3.5 Declarative Configuration Helpers for Connectors
- Replace imperative shell / `sed` commands in game server connectors with fluent C# configuration file parsers.
- Standardize cross-platform configuration file manipulation.
