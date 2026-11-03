# Playmox — Architecture & AI Agent Reference Guide

> This document serves as a technical reference for developers and AI coding agents working on the Playmox codebase. It maps out the architecture, domain models, background job lifecycle, Proxmox integration, networking/DNAT pipeline, and the Game Server Connector framework.

---

## 1. System Overview

**Playmox** is a self-hosted, multi-tenant game server provisioning and management platform built on top of **Proxmox VE** and **LinuxGSM**.
It allows administrators to allocate resource quotas (CPU, RAM, Disk, Instances, Public Ports, SSH access) to users, who can then spin up isolated KVM virtual machines with pre-configured game servers, manage live game configurations, view runtime telemetry, take/restore backups, and access an in-browser WebSocket SSH terminal.

### Tech Stack
- **Backend (`API/`)**: ASP.NET Core 8.0 Web API, Entity Framework Core 9.0 (SQL Server), JWT Bearer Auth, `Renci.SshNet` (SSH.NET) for guest VM SSH/SFTP automation, custom `IHostedService` job queue & state sync workers.
- **Frontend (`Frontend/PlaymoxWeb/`)**: Blazor WebAssembly (.NET 8.0), MudBlazor 7.x UI component library, interactive onboarding tutorial, WebSocket SSH terminal client.
- **Infrastructure (`API/Bootstrap/`, `docker-compose.yml`)**:
  - Proxmox VE host bootstrap script (`bootstrap-playmox-proxmox.sh`) setting up least-privilege RBAC, Ubuntu 24.04 cloud-init template (`VMID 9000`), and automated `nftables` DNAT port-forwarding timers (`playmox-dnat-reconcile.timer`, `playmox-dnat-gc.timer`).
  - Docker Compose environment running SQL Server 2025 Express (`playmox-db`), Backend API (`playmox-api`), and Nginx-hosted Blazor WASM (`playmox-frontend`).

---

## 2. Repository Structure

```text
Playmox/
├── API/                                    # ASP.NET Core 8.0 Web API
│   ├── BLL/
│   │   ├── BO/                             # Entity Framework Core domain entities
│   │   │   ├── Platform/                   # GameDefinition, VirtualGameInstance, ProvisioningJob, UserQuota, PortLease, ProxmoxSettings
│   │   │   └── Users/                      # User, Role, UserRole, RolePermission, UserToken
│   │   ├── DAL/                            # Repositories (Repository.cs, UsersRepository.cs)
│   │   ├── DTO/                            # Data Transfer Objects
│   │   └── PlaymoxContext.cs               # EF Core DbContext & schema configuration
│   ├── Bootstrap/
│   │   └── bootstrap-playmox-proxmox.sh    # Proxmox VE host setup & nftables DNAT automation script
│   ├── Controllers/                        # REST API Controllers (Auth, Users, Instances, GameDefinitions, ProvisioningJobs, Admin/*)
│   ├── Extensions/                         # Auth, Error handling middleware, AutoMapper, Swagger BasicAuth
│   ├── Helpers/                            # BootstrapDataSeeder, SecureSecret (encryption), UserHelper
│   ├── Migrations/                         # EF Core SQL Server migrations
│   └── Services/
│       ├── Platform/
│       │   ├── Games/                      # Modular Game Server Connectors
│       │   │   ├── Abstractions/           # GameServerKind, IGameServerConnector, GameServerConnectorRegistry, LinuxGsmConnectorBase
│       │   │   ├── Connectors/             # BlankVm, MinecraftJava, Terraria, Unturned, Factorio connectors
│       │   │   └── Infrastructure/         # GameServerConnectorSshOps, GameServerCustomParameters, LinuxGsmScriptBuilder
│       │   ├── InstanceLifecycleService.cs # Core orchestration for VM/game operations, quotas, port leasing, backups
│       │   ├── InstanceStateSyncService.cs # Background worker syncing Proxmox VM states with DB
│       │   ├── ProvisioningJobProcessor.cs # Executes queued VM & game provisioning jobs
│       │   ├── ProvisioningWorkerService.cs# Background loop polling and dispatching ProvisioningJobs
│       │   └── SshWebSocketBridgeService.cs# Bridges browser WebSocket to guest VM SSH shell stream
│       └── Proxmox/
│           ├── AdminProxmoxDashboardService.cs # Proxmox node telemetry for admin dashboard
│           └── ProxmoxAutomationClient.cs      # Proxmox REST API + QEMU Guest Agent + SSH/SFTP client
├── Frontend/
│   └── PlaymoxWeb/                         # Blazor WebAssembly (.NET 8.0 + MudBlazor)
│       ├── Layout/                         # MainLayout, NavMenu, Dialogs (CreateInstance, GameConfig, InstanceJobs, ResetPassword)
│       ├── Models/                         # Frontend DTOs & view models
│       ├── Pages/                          # Home, Login, Instances, AdminUsers, Configuration
│       ├── Services/                       # PlaymoxApiClient, AuthStateService, DynamicConfigService, ThemeService, TutorialService, LocalizationService
│       └── wwwroot/                        # Static assets, JS interop (auth-storage.js, tutorial.js), CSS
├── docs/                                   # Platform & developer documentation
├── docker-compose.yml                      # Full-stack container orchestration
└── Playmox.sln                             # .NET Solution file
```

---

## 3. Core Domain Model (`API/BLL/BO/Platform/`)

1. **`GameDefinition`**:
   - Catalog entry representing an installable game server type (`Key`: `blank-vm`, `minecraft-java`, `terraria`, `unturned`, `factorio`, `cs2`).
   - Defines `LinuxGsmServerId`, `DefaultPort`, `RequiredPublicPorts`, and `JsonSchema` (used by the frontend `DynamicConfigService` to dynamically render configuration forms).
2. **`VirtualGameInstance`**:
   - Represents a user's virtual machine on Proxmox (`VmId`, `VmDisplayName`, `Status`, `CpuCores`, `MemoryMb`, `DiskGb`).
   - Tracks allocated public ports (`SshPublicPort`, `GamePublicPortStart`, `GamePublicPortCount`, `GamePublicPortProtocol`).
   - Stores JSON configuration (`ConfigurationJson`) containing user game parameters, `customParameters`, `systemAccess` (encrypted SSH password, internal IP), and `network` metadata.
3. **`ProvisioningJob`**:
   - Asynchronous background job tied to a `VirtualGameInstance` (`Operation`: `CreateVm`, `StartVm`, `StopVm`, `ShutdownVm`, `RestartVm`, `DeleteVm`, `UpdateVmResources`, `ResetVmPassword`, `InstallGameServer`, `ReinstallGameServer`, `UpdateGameConfiguration`).
   - Enforces concurrency rules: at most one active (`Queued` or `Running`) job per VM via filtered unique index `UX_ProvisioningJobs_ActivePerInstanceAndStatus`.
4. **`UserQuota`**:
   - Per-user resource limits (`CpuCoresLimit`, `MemoryMbLimit`, `DiskGbLimit`, `MaxInstancesLimit`, `MaxPublicPortsLimit`, `AllowSshPublicPort`).
   - Note: Stopped VMs only consume **Disk** quota; CPU, RAM, Instance count, and Public Ports are consumed only by active/running VMs and checked again when starting a stopped VM.
5. **`PortLease`**:
   - Tracks active public port reservations (`Port` with unique index + `VirtualGameInstanceId`) to prevent TOCTOU port collisions.
6. **`ProxmoxSettings`**:
   - Singleton configuration row storing Proxmox API URL, Node name, API Token credentials (encrypted), Template VMID, Storage name, Network bridge, and optional public `ExternalAddress`.

---

## 4. Provisioning & Concurrency Pipeline

### 4.1 Job Queue Concurrency (`ProvisioningJobProcessor` & `ProvisioningWorkerService`)
- **Proxmox Host-Level Lane (`JobProcessingLane.Proxmox`, 1 worker)**:
  - Host-heavy and hypervisor-executed operations (`CreateVm`, `StartVm`, `StopVm`, `RestartVm`, `DeleteVm`, `UpdateVmResources`, `ShutdownVm`, `ResetVmPassword`) run serialized with 1 global worker so Proxmox storage, cloning, and hypervisor operations are never overwhelmed. Note: `ResetVmPassword` executes in this lane because it uses Proxmox QEMU Guest Exec.
- **VM-Scoped Lane (`JobProcessingLane.VmScoped`, 3 parallel workers)**:
  - Guest-level operations (`InstallGameServer`, `ReinstallGameServer`, `UpdateGameConfiguration`) run across up to 3 worker threads in parallel for different VMs, while remaining strictly sequential per individual VM.

### 4.2 End-to-End VM Creation Flow
1. **API Request (`POST /api/instances`)**:
   - `InstanceLifecycleService.EnqueueCreateVmAsync` validates `UserQuota`, allocates a unique `VmId`, reserves public ports in `PortLeases` (range `22000`–`32000`), generates random `gameserver` SSH credentials, encrypts the password into `ConfigurationJson`, creates the `VirtualGameInstance` (`Status = Creating`), and enqueues a `CreateVm` job.
2. **Proxmox VM Cloning & Cloud-Init (`ExecuteCreateVmAsync`)**:
   - `ProvisioningJobProcessor` clones the Ubuntu cloud-init template (`TemplateVmid`, default `9000`), resizes the disk, sets CPU/RAM, configures `cloud-init` (`ciuser=gameserver`, `cipassword`, DHCP), writes DNAT metadata into the VM's Proxmox `description` (`playmox-dnat:ssh=...;gameStart=...`), and starts the VM.
   - Once the QEMU Guest Agent reports an internal IPv4 address, Playmox converts the guest network configuration (`netplan`) from dynamic DHCP to a static IP lease (`/etc/netplan/70-playmox-static.yaml`) so the internal IP never drifts across reboots, and configures guest `ufw` firewall rules.
3. **Game Server Installation (`ExecuteInstallGameServerAsync`)**:
   - If the selected game is not `blank-vm`, `ExecuteCreateVmAsync` chains an `InstallGameServer` job.
   - The job resolves the matching `IGameServerConnector` from `GameServerConnectorRegistry` and invokes `InstallOrUpdateAsync`, streaming live console output so the user can watch installation progress (including Steam Guard mobile confirmation prompts) in the UI.

---

## 5. Networking & Proxmox Host DNAT Architecture

- Each VM runs on an internal bridge (e.g., `vmbr0` or SDN bridge) with a private IPv4 address.
- Playmox stores a structured tag in the Proxmox VM description:
  `playmox-dnat:ssh=<port>;gameStart=<port>;gameCount=<n>;proto=<tcp|udp|tcp,udp>`
- On the Proxmox host, `bootstrap-playmox-proxmox.sh` installs:
  - `/usr/local/sbin/playmox-dnat-sync.sh`
  - `playmox-dnat-reconcile.timer` (runs every 15s) and `playmox-dnat-gc.timer` (cleans up orphaned rules)
  - Proxmox VM lifecycle hookscript (`local:snippets/playmox-dnat-hook.sh`) that immediately triggers DNAT rule synchronization in `nftables` (`table ip playmox_nat`) when a VM starts (`post-start`) or stops (`pre-stop`).

---

## 6. Game Server Connector Architecture (`API/Services/Platform/Games/`)

Every supported game implements `IGameServerConnector` and is registered in DI:
- `Kind`: `GameServerKind` enum value (`BlankVm`, `MinecraftJava`, `Terraria`, `Unturned`, `Factorio`, etc.).
- `InstallOrUpdateAsync`: Installs dependencies, downloads and runs LinuxGSM (`mcserver`, `terrariaserver`, `untserver`, `fctrserver`), writes game configuration files, registers `/etc/systemd/system/playmox-game.service`, and starts the server.
- `StopForShutdownAsync`: Gracefully stops `playmox-game.service` and LinuxGSM before VM shutdown to prevent world corruption.
- `RestartAfterVmResourceChangeAsync`: Adjusts memory/resource limits (e.g., JVM `-Xmx` for Minecraft) and restarts the game service.
- `GetRuntimeInfoAsync`: Queries `systemctl is-active playmox-game.service`, checks listening ports via `ss`, and extracts live game metadata (version, online players, max players, direct connect code).
- `GetConfigurationSnapshotAsync`: Reads either cached DB config (`source=db`) or parses live config files on the guest VM (`source=live`) so UI users can inspect and edit settings in `GameConfigDialog`.
- `CreateBackupAsync` / `RestoreBackupAsync`: Packages world saves and config files into a `.tar.gz` archive over SFTP and restores uploaded archives back into `/home/gameserver`.
