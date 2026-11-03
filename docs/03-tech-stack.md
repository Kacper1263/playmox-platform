# 03. Technology Stack

Playmox is built with a cohesive .NET 8 and Linux infrastructure stack chosen for strong typing, low memory footprint, native SSH/WebSocket streaming, and zero-dependency deployment via Docker Compose.

---

## 1. Backend (`API/`)

| Component | Technology / Package | Version | Purpose |
| :--- | :--- | :--- | :--- |
| **Runtime & Framework** | ASP.NET Core Web API | `.NET 8.0` | REST API controllers, WebSocket endpoints, dependency injection, and background `IHostedService` workers. |
| **ORM & Migrations** | Entity Framework Core (`Microsoft.EntityFrameworkCore.SqlServer`) | `9.0.6` | Relational data access, LINQ queries, and automatic schema migrations on startup. |
| **Authentication** | `Microsoft.AspNetCore.Authentication.JwtBearer` | `8.0.17` | Stateless JWT access tokens + database-backed refresh tokens (`UsersTokens`). |
| **Password Hashing** | `Isopoh.Cryptography.Argon2` | `2.0.0` | Memory-hard Argon2id password hashing (`Crypto.HashPassword`). |
| **SSH & SFTP Automation** | `SSH.NET` (`Renci.SshNet`) | `2026.0.0` | Guest VM command execution, live stdout/stderr streaming, SFTP backup transfers, and interactive WebSocket shell bridging. |
| **Object Mapping** | `AutoMapper` | `14.0.0` | Mapping between EF Core domain entities (`BO`) and API Data Transfer Objects (`DTO`). |
| **JSON Processing** | `System.Text.Json` & `Newtonsoft.Json` | `8.0.x` / `13.0.3` | Dynamic `JsonSchema` parsing, `ConfigurationJson` manipulation, and Proxmox API payload serialization. |
| **API Documentation** | `Swashbuckle.AspNetCore` (Swagger / OpenAPI) | `6.6.2` | Interactive OpenAPI documentation at `/swagger` (protected with Basic Auth in non-development environments). |

---

## 2. Database (`playmox-db`)

| Component | Technology / Image | Details |
| :--- | :--- | :--- |
| **Database Engine** | **Microsoft SQL Server 2025 Express** | Container image `mcr.microsoft.com/mssql/server:2025-latest`. |
| **Storage Capacity** | Up to **50 GB** per database | SQL Server 2025 Express increases the maximum relational database size limit to 50 GB, providing ample headroom for job logs, audit trails, and instance configurations. |
| **Concurrency Indices** | Filtered Unique Indexes | Enforces invariants at the database level (e.g., `UX_ProvisioningJobs_ActivePerInstanceAndStatus` for active job locking and `IX_PortLeases_Port` for atomic port reservation). |

---

## 3. Frontend (`Frontend/PlaymoxWeb/`)

| Component | Technology / Package | Version | Purpose |
| :--- | :--- | :--- | :--- |
| **SPA Framework** | **Blazor WebAssembly** (`Microsoft.AspNetCore.Components.WebAssembly`) | `.NET 8.0` | Client-side C# SPA running in the browser via WebAssembly. |
| **Component Library** | **MudBlazor** | `7.15.0` | Material Design components, dialogs, data grids, snackbars, and theming. |
| **Web Server (Container)** | **Nginx** (`nginx:alpine`) | Latest | Serves static Blazor WASM assets and injects runtime `appsettings.json` (`API_BASE_URL`). |
| **Browser Interop** | Vanilla JS (`auth-storage.js`, `tutorial.js`) | ES6+ | Manages token persistence, WebSocket SSH terminal scrolling/resizing, and the 14-step onboarding spotlight overlay. |

---

## 4. Hypervisor, Guest OS & Networking Infrastructure

| Component | Technology | Purpose |
| :--- | :--- | :--- |
| **Hypervisor** | **Proxmox VE 8.x** (KVM / QEMU) | Hardware-virtualized KVM virtual machines managed via the Proxmox REST API (`/api2/json`) using least-privilege API tokens (`PVEAPIToken`). |
| **Base VM Template** | **Ubuntu 24.04 LTS (Noble Numbat) Cloud Image** | Pre-built Cloud-Init template (`VMID 9000`) customized with `qemu-guest-agent` and sanitized `machine-id`. |
| **In-Guest Agent** | **QEMU Guest Agent (`qemu-guest-agent`)** | Reports internal IPv4 network interfaces to Playmox and provides out-of-band command execution (`guest-exec`). |
| **Guest Network Config** | **Netplan** (`/etc/netplan/70-playmox-static.yaml`) | Pins the initial DHCP IPv4 lease as a static IP inside the guest VM so port forwarding never breaks across reboots. |
| **Host Port Forwarding** | **`nftables`** (`table ip playmox_nat`) | High-performance kernel-level TCP/UDP DNAT port forwarding synchronized via `/usr/local/sbin/playmox-dnat-sync.sh`, Proxmox hookscripts, and systemd timers. |
| **Guest Firewall** | **UFW (Uncomplicated Firewall)** | Hardens each guest VM with default-deny incoming policy, allowing only `22/tcp` (SSH) and the instance's allocated game ports. |
| **Game Server Manager** | **LinuxGSM** + **systemd** (`playmox-game.service`) | Standardizes installation, dependency resolution, SteamCMD downloads, and automatic background startup for dedicated game servers. |
