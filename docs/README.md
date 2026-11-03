# Playmox — Documentation Index

Welcome to the official technical documentation for **Playmox**, a self-hosted, multi-tenant game server provisioning and management platform built for **Proxmox VE** and **LinuxGSM**.

---

## Documentation Structure

| # | Document | Summary |
| :--- | :--- | :--- |
| **01** | **[01-overview.md](01-overview.md)** | Platform vision, core goals, user vs. administrator capabilities, and the end-to-end lifecycle of a game server virtual machine. |
| **02** | **[02-architecture.md](02-architecture.md)** | System architecture: ASP.NET Core 8 Web API, dual-queue `ProvisioningJobProcessor`, `InstanceStateSyncService`, `ProxmoxAutomationClient`, `SshWebSocketBridgeService`, and Blazor WASM frontend. |
| **03** | **[03-tech-stack.md](03-tech-stack.md)** | Actual technology stack across backend (.NET 8, EF Core 9, SSH.NET), database (SQL Server 2025 Express), frontend (Blazor WASM, MudBlazor 7), and infrastructure (Proxmox VE 8.x, Ubuntu 24.04 Cloud-Init, LinuxGSM, `nftables`). |
| **04** | **[04-proxmox-provisioning.md](04-proxmox-provisioning.md)** | Proxmox KVM template cloning (`VMID 9000`), Cloud-Init user provisioning, QEMU Guest Agent IPv4 discovery, DHCP-to-static Netplan IP pinning, guest `ufw` firewall hardening, and dynamic VM resource resizing. |
| **05** | **[05-game-automation-linuxgsm.md](05-game-automation-linuxgsm.md)** | Automated LinuxGSM lifecycle (`mcserver`, `terrariaserver`, `untserver`, `fctrserver`) and `blank-vm` mode, `playmox-game.service` systemd wrapper, SteamCMD anonymous/authenticated login with Steam Guard mobile approval, runtime telemetry probes, and SFTP `.tar.gz` backups/restores. |
| **06** | **[06-data-model-dynamic-params.md](06-data-model-dynamic-params.md)** | Entity Framework Core domain model (`GameDefinition`, `VirtualGameInstance`, `ProvisioningJob`, `UserQuota`, `PortLease`, `ProxmoxSettings`, `User`/`Role`), quota accounting rules, and JSON Schema + `customParameters` dynamic UI generation. |
| **07** | **[07-networking-security.md](07-networking-security.md)** | Public port leasing (`PortLeases`), Proxmox description tags (`playmox-dnat:ssh=...`), host-level `nftables` DNAT synchronization (`playmox-dnat-sync.sh`, hookscript, reconcile & GC timers), guest `ufw` policy, and JWT + AES secret encryption. |
| **08** | **[08-api-workflow.md](08-api-workflow.md)** | Complete REST and WebSocket API reference covering all controllers (`Auth`, `Instances`, `GameDefinitions`, `ProvisioningJobs`, `Users`, `ProxmoxDashboard`, `ProxmoxDiagnostics`, `ProxmoxSettings`, `Bootstrap`, and `/api/ws/instances/{id}/ssh`). |
| **09** | **[09-roadmap.md](09-roadmap.md)** | Completed platform milestones and future open-source roadmap ideas (new game connectors, LXC container mode, multi-node cluster scheduling, webhook notifications). |
| **10** | **[10-proxmox-bootstrap.md](10-proxmox-bootstrap.md)** | Administrator guide to `API/Bootstrap/bootstrap-playmox-proxmox.sh`, environment variables, RBAC permissions, Cloud-Init template generation, and troubleshooting. |
| **11** | **[11-local-db-and-compose.md](11-local-db-and-compose.md)** | Developer and deployment guide for SQL Server 2025 Express and Docker Compose (`.env` variables, automatic EF Core migrations, container ports, and CORS configuration). |
| **12** | **[12-adding-new-game-connectors.md](12-adding-new-game-connectors.md)** | Step-by-step developer and AI agent guide on adding new game servers using the modular `API/Services/Platform/Games/` connector architecture. |
| **AI** | **[ai-reference/CODEBASE_GUIDE.md](ai-reference/CODEBASE_GUIDE.md)** | Compact architectural reference and codebase map designed specifically for AI coding assistants and new contributors. |

---

## Recommended Reading Order

1. **New Users & Evaluators**: Start with **[01-overview.md](01-overview.md)** and **[02-architecture.md](02-architecture.md)**.
2. **System Administrators**: Read **[10-proxmox-bootstrap.md](10-proxmox-bootstrap.md)**, **[11-local-db-and-compose.md](11-local-db-and-compose.md)**, and **[07-networking-security.md](07-networking-security.md)**.
3. **Contributors & Developers**: Review **[03-tech-stack.md](03-tech-stack.md)**, **[06-data-model-dynamic-params.md](06-data-model-dynamic-params.md)**, **[08-api-workflow.md](08-api-workflow.md)**, and **[12-adding-new-game-connectors.md](12-adding-new-game-connectors.md)**.
