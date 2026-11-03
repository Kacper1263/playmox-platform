# 08. REST & WebSocket API Reference

The Playmox API (`API/`) exposes RESTful JSON endpoints and a WebSocket SSH bridge under `/api/*`. Interactive OpenAPI / Swagger documentation is available at `/swagger`.

---

## 1. Authentication & Authorization

All protected endpoints require a JWT Bearer token in the `Authorization` header:

```http
Authorization: Bearer <access_token>
```

For browser WebSocket connections (`/api/ws/instances/{id}/ssh`) and direct file downloads (`/api/instances/{id}/game/backup`), the JWT can also be supplied via query string (`?access_token=<access_token>`).

### `AuthController` (`/api/auth`)

| Method | Route | Auth | Description |
| :--- | :--- | :--- | :--- |
| `POST` | `/api/auth/login` | Anonymous | Authenticates with `{ "login", "password" }` and returns `{ "userBO", "accessToken", "refreshToken" }`. |
| `POST` | `/api/auth/refresh` | Anonymous | Exchanges an expired `accessToken` and valid `refreshToken` for a new token pair. |
| `POST` | `/api/auth/revoke` | `User` / `Admin` | Revokes a specific refresh token or all active refresh tokens for the authenticated user. |
| `GET` | `/api/auth/get-current-user` | `User` / `Admin` | Returns the currently authenticated user's profile and roles (`CurrentUserModel`). |

---

## 2. Virtual Machine & Game Server Lifecycle

### `InstancesController` (`/api/instances`)

Standard users only see and manage their own instances; users with the `Admin` role can view and manage all instances across the platform.

| Method | Route | Auth | Response | Description |
| :--- | :--- | :--- | :--- | :--- |
| `GET` | `/api/instances` | `User` / `Admin` | `200 OK` | Lists virtual machine instances (`VirtualGameInstanceDto[]`) including allocated ports, credentials, and quota context. |
| `POST` | `/api/instances` | `User` / `Admin` | `202 Accepted` | Validates quota, allocates `VmId` & `PortLeases`, creates a `VirtualGameInstance`, and enqueues a `CreateVm` job (`JobAcceptedResponse`). |
| `POST` | `/api/instances/{id}/start` | `User` / `Admin` | `202 Accepted` | Re-validates active quota limits and enqueues a `StartVm` job. |
| `POST` | `/api/instances/{id}/shutdown` | `User` / `Admin` | `202 Accepted` | Enqueues a `ShutdownVm` job (gracefully stops `playmox-game.service` via SSH, then issues ACPI shutdown). |
| `POST` | `/api/instances/{id}/stop` | `User` / `Admin` | `202 Accepted` | Enqueues a `StopVm` job. |
| `POST` | `/api/instances/{id}/force-stop` | `User` / `Admin` | `200 OK` | Immediately halts the VM on Proxmox and cancels/cleans up stuck state when an instance is unresponsive. |
| `POST` | `/api/instances/{id}/restart` | `User` / `Admin` | `202 Accepted` | Enqueues a `RestartVm` job (gracefully stops game service, reboots VM, and restarts service). |
| `DELETE` | `/api/instances/{id}` | `User` / `Admin` | `202 Accepted` | Enqueues a `DeleteVm` job (destroys VM on Proxmox and releases `PortLeases`). |
| `PUT` | `/api/instances/{id}/resources` | `User` / `Admin` | `202 Accepted` | Enqueues an `UpdateVmResources` job to scale `cpuCores`, `memoryMb`, and/or `diskGb` (disk can only grow). |
| `POST` | `/api/instances/{id}/reset-password` | `User` / `Admin` | `202 Accepted` | Enqueues a `ResetVmPassword` job to rotate the `gameserver` Linux account password. |
| `GET` | `/api/instances/{id}/game-runtime` | `User` / `Admin` | `200 OK` | Probes live `playmox-game.service` status, listening ports, game version, player count, and connect endpoint. |
| `GET` | `/api/instances/{id}/game/config` | `User` / `Admin` | `200 OK` | Retrieves game configuration JSON (`?refresh=false` from DB, or `?refresh=true` parsed live from guest VM config files). |
| `PUT` | `/api/instances/{id}/game/config` | `User` / `Admin` | `202 Accepted` | Enqueues an `UpdateGameConfiguration` job to write updated settings + `customParameters` to the guest VM and restart the game service. |
| `POST` | `/api/instances/{id}/game/reinstall` | `User` / `Admin` | `202 Accepted` | Enqueues a `ReinstallGameServer` job to force re-run the LinuxGSM installer and reapply configuration. |
| `GET` | `/api/instances/{id}/game/backup` | `User` / `Admin` | `200 OK` (File) | Creates and downloads a `.tar.gz` backup archive of the instance's world saves and configuration files over SFTP. |
| `POST` | `/api/instances/{id}/game/restore` | `User` / `Admin` | `200 OK` | Accepts a multipart `.tar.gz` upload (up to 500 MB), stops the game server, restores files over SFTP, and restarts the server. |
| `GET` | `/api/instances/{id}/ssh-preview` | `User` / `Admin` | `200 OK` | Returns the recent terminal output tail (`?tailLines=250`) from the guest VM. |
| `GET` | `/api/instances/{id}/live-command-stream` | `User` / `Admin` | `200 OK` | Returns live streaming stdout/stderr output of an active installation/provisioning command (including Steam Guard prompts). |

---

## 3. Interactive WebSocket SSH Endpoint

| Protocol | Route | Auth | Description |
| :--- | :--- | :--- | :--- |
| `WS / WSS` | `/api/ws/instances/{id}/ssh?access_token=<jwt>` | `User` / `Admin` | Opens a full-duplex WebSocket bridged to `gameserver@<guest_ip>:22` via `SshWebSocketBridgeService`. Supports terminal resize frames (`{"type":"resize","cols":120,"rows":36}`) and raw UTF-8 terminal input/output. |

---

## 4. Game Catalog & Background Jobs

### `GameDefinitionsController` (`/api/game-definitions`)

| Method | Route | Auth | Description |
| :--- | :--- | :--- | :--- |
| `GET` | `/api/game-definitions` | `User` / `Admin` | Returns all active `GameDefinitionDto` entries (`Key`, `DisplayName`, `LinuxGsmServerId`, `DefaultPort`, `RequiredPublicPorts`, `JsonSchema`). |

### `ProvisioningJobsController` (`/api/jobs`)

| Method | Route | Auth | Description |
| :--- | :--- | :--- | :--- |
| `GET` | `/api/jobs` | `User` / `Admin` | Lists provisioning jobs for the current user (or all users if `Admin`), optionally filtered by `?instanceId={id}`. |
| `GET` | `/api/jobs/{id}` | `User` / `Admin` | Returns detailed status, timestamps, and error message for a specific `ProvisioningJob`. |

---

## 5. Users, Quotas & Platform Settings

### `UsersController` (`/api/users`)

| Method | Route | Auth | Description |
| :--- | :--- | :--- | :--- |
| `GET` | `/api/users` | `Admin` | Lists all users with their roles and assigned `UserQuota`. |
| `GET` | `/api/users/{id}` | `Admin` | Gets a specific user by ID. |
| `POST` | `/api/users/create` | `Admin` | Creates a new user account along with their initial `UserQuota` (validating CPU quota against physical Proxmox host cores). |
| `DELETE` | `/api/users/{id}` | `Admin` | Deletes a non-admin user account along with their tokens, role bindings, and quota record. |
| `GET` | `/api/users/{id}/quota` | `Admin` | Returns the `UserQuotaDto` for a specific user. |
| `PUT` | `/api/users/{id}/quota` | `Admin` | Updates a user's `CpuCoresLimit`, `MemoryMbLimit`, `MaxInstancesLimit`, `DiskGbLimit`, `MaxPublicPortsLimit`, and `AllowSshPublicPort`. |
| `GET` | `/api/users/me/quota` | `User` / `Admin` | Returns the authenticated user's own `UserQuotaDto`. |
| `GET` | `/api/users/me/platform-settings` | `User` / `Admin` | Returns the authenticated user's saved Steam username and whether an encrypted Steam password is set. |
| `PUT` | `/api/users/me/platform-settings` | `User` / `Admin` | Updates or clears the authenticated user's personal SteamCMD credentials (`SteamUsername`, `SteamPassword`). |

---

## 6. Proxmox Administration & Bootstrap

### `ProxmoxSettingsController` (`/api/admin/proxmox-settings`)

| Method | Route | Auth | Description |
| :--- | :--- | :--- | :--- |
| `GET` | `/api/admin/proxmox-settings` | `Admin` | Retrieves the current Proxmox VE connection settings (`ProxmoxUrl`, `ProxmoxNode`, `ApiUser`, `ApiTokenId`, `HasApiTokenSecret`, `TemplateVmid`, `Storage`, `Bridge`, `ExternalAddress`). |
| `PUT` | `/api/admin/proxmox-settings` | `Admin` | Creates or updates Proxmox VE connection settings (encrypting `ApiTokenSecret` with `SecureSecret.Encrypt`). |

### `ProxmoxDashboardController` (`/api/admin/proxmox-dashboard`)

| Method | Route | Auth | Description |
| :--- | :--- | :--- | :--- |
| `GET` | `/api/admin/proxmox-dashboard` | `Admin` | Returns live Proxmox node telemetry (CPU usage, memory usage, storage capacity, uptime, and VM summary). |

### `ProxmoxDiagnosticsController` (`/api/admin/proxmox-diagnostics`)

| Method | Route | Auth | Description |
| :--- | :--- | :--- | :--- |
| `GET` | `/api/admin/proxmox-diagnostics/vm/{vmId}` | `Admin` | Returns low-level Proxmox VM configuration, status, and QEMU Guest Agent network diagnostics for troubleshooting. |

### `BootstrapController` (`/api/bootstrap`) & `PingController` (`/api/ping`)

| Method | Route | Auth | Description |
| :--- | :--- | :--- | :--- |
| `GET` | `/api/bootstrap/proxmox-script` | Anonymous | Downloads `bootstrap-playmox-proxmox.sh` with LF line endings ready for execution on the Proxmox host. |
| `GET` | `/api/ping` | Anonymous | Health check endpoint returning `200 OK` (`"Pong"`). |
