# 04. Proxmox VM Provisioning & Guest Hardening

Playmox provisions every game server inside a dedicated **KVM Virtual Machine** on Proxmox VE. This document details the complete lifecycle of VM cloning, Cloud-Init injection, QEMU Guest Agent IP discovery, DHCP-to-static Netplan pinning, guest firewall configuration, and live resource scaling.

---

## 1. Base Cloud-Init Template (`VMID 9000`)

Before creating user instances, the Proxmox host is prepared with an **Ubuntu 24.04 LTS Cloud-Init Template** (default `VMID 9000`) built by `API/Bootstrap/bootstrap-playmox-proxmox.sh`:
- **Disk & Controller**: VirtIO SCSI single controller (`virtio-scsi-pci`) with discard/SSD emulation enabled.
- **QEMU Guest Agent**: Pre-installed (`qemu-guest-agent`) and enabled via systemd drop-in inside the image (`agent: enabled=1`).
- **Unique DHCP Identity**: Both `/etc/machine-id` and `/var/lib/dbus/machine-id` are truncated in the template image so every cloned VM generates a fresh DUID/client identifier on first boot and receives a distinct DHCP lease.
- **Cloud-Init Drive & Display**: Attached on `ide2` (`<storage>:cloudinit`) with standard VGA display (`--vga std`, `serial0` removed) so Proxmox noVNC console works out of the box.

---

## 2. Step-by-Step VM Creation Flow (`CreateVm`)

```mermaid
sequenceDiagram
    autonumber
    participant User as User (UI)
    participant API as InstanceLifecycleService
    participant Job as ProvisioningJobProcessor
    participant PVE as Proxmox VE API (:8006)
    participant VM as Guest VM (QGA & SSH)

    User->>API: POST /api/instances (CPU, RAM, Disk, GameKey, Config)
    API->>API: Validate UserQuota & reserve PortLeases (22000-32000)
    API->>API: Generate random SSH password (AES encrypted in DB)
    API-->>User: 202 Accepted (JobId, InstanceId, Status=Creating)

    Job->>PVE: POST /nodes/{node}/qemu/{templateVmid}/clone (full=1, newid=VmId)
    PVE-->>Job: UPID Task Completed
    Job->>PVE: PUT /nodes/{node}/qemu/{vmid}/resize (disk=scsi0, size={DiskGb}G)
    Job->>PVE: POST /nodes/{node}/qemu/{vmid}/config (cores, memory, bridge, ciuser=gameserver, cipassword, ipconfig0=ip=dhcp, description=playmox-dnat:ssh=...;gameStart=...;gameCount=...;proto=...)
    Job->>PVE: POST /nodes/{node}/qemu/{vmid}/status/start
    Job->>PVE: Poll /agent/network-get-interfaces until private IPv4 discovered
    PVE-->>Job: Guest IPv4 (e.g., 192.168.0.218)
    Job->>VM: Configure SSH & UFW firewall (allow 22/tcp + game ports)
    alt GameKey != "blank-vm"
        Job->>Job: Enqueue InstallGameServer job (Status=Updating -> Running)
    else Blank VM
        Job->>Job: Mark Instance Status=Running
    end
```

### 2.1 Quota & Port Lease Reservation
When `POST /api/instances` is called:
1. `InstanceLifecycleService` validates the requested `CpuCores`, `MemoryMb`, `DiskGb`, instance count, and required public ports (`1` optional SSH port + `GameDefinition.RequiredPublicPorts` contiguous game ports) against the user's `UserQuota`.
2. It allocates the next available Proxmox `VmId` (`>= 100`, skipping `TemplateVmid` and any VMIDs already present on the Proxmox node).
3. It atomically reserves public ports in the `PortLeases` table (`22000`–`32000`) and generates a cryptographically random password for the `gameserver` Linux account, storing it encrypted via `SecureSecret.Encrypt` inside `VirtualGameInstance.ConfigurationJson`.

### 2.2 Full Cloning & Cloud-Init Configuration
`ProxmoxAutomationClient` executes:
1. **Full Clone**: Calls `POST /api2/json/nodes/{node}/qemu/{templateVmid}/clone` with `full=1` onto the configured storage pool.
2. **Disk Resize**: If the requested `DiskGb` is larger than the base template disk, calls `PUT /api2/json/nodes/{node}/qemu/{vmid}/resize` on disk `scsi0` (or `virtio0`/`sata0`). Inside the guest, Cloud-Init's `growpart` and `resizefs` modules automatically expand the root filesystem on boot.
3. **Hardware & Cloud-Init Injection**:
   - `cores={CpuCores}`, `memory={MemoryMb}`, `net0=virtio,bridge={Bridge}`
   - `ciuser=gameserver`, `cipassword={GeneratedPassword}`, `ipconfig0=ip=dhcp`
   - `description=playmox-dnat:ssh={SshPort};gameStart={GamePort};gameCount={Count};proto={Protocol}`
   - Attaches `hookscript=local:snippets/playmox-dnat-hook.sh` when supported by the API token privileges.

### 2.3 QEMU Guest Agent IP Discovery & Static Netplan Pinning
Once the VM starts, `ProxmoxAutomationClient` polls the QEMU Guest Agent endpoint (`GET /nodes/{node}/qemu/{vmid}/agent/network-get-interfaces`) until a non-loopback IPv4 address is assigned via DHCP.

To guarantee that the guest VM's internal IPv4 address **never changes** after subsequent reboots or DHCP lease expirations (which would break host DNAT port forwarding):
1. `InstanceStateSyncService` (via `ProxmoxAutomationClient.TryPinGuestDhcpAddressAsStaticAsync`) inspects the active network interface, subnet prefix, default gateway (`ip route`), and DNS resolvers inside the guest VM.
2. It writes `/etc/netplan/70-playmox-static.yaml` pinning the discovered IPv4 address with `dhcp4: false` and applies the configuration (`netplan generate && netplan apply`).

---

## 3. Guest Firewall Hardening (`UFW`)

During initial provisioning (and whenever game ports are reconfigured), Playmox hardens the guest VM's internal firewall using `ufw`:
- Default incoming policy: `deny incoming`
- Default outgoing policy: `allow outgoing`
- Explicit allow rules:
  - `22/tcp` (SSH access for Playmox automation, WebSocket SSH console, and optional user public SSH port)
  - Allocated game ports (e.g., `25565/tcp` for Minecraft, `7777/tcp,udp` for Terraria, `27015:27016/tcp,udp` for Unturned, `34197/udp,tcp` for Factorio)

---

## 4. VM Power & Resource Operations

| Operation | Execution Mechanism |
| :--- | :--- |
| **`StartVm`** | Re-validates `UserQuota` (CPU, RAM, MaxInstances, MaxPublicPorts), starts the VM on Proxmox, verifies guest IP via QGA, and refreshes host DNAT rules. |
| **`ShutdownVm`** | Connects to the guest VM via SSH to gracefully stop `playmox-game.service` and the LinuxGSM server first (preventing world corruption), then triggers an ACPI shutdown (`POST /status/shutdown`) with fallback to hard stop. |
| **`StopVm` / `ForceStop`** | Attempts graceful game stop when reachable, then issues `POST /status/stop` on Proxmox and releases active host DNAT rules. |
| **`RestartVm`** | Gracefully stops the game service before rebooting the VM (`POST /status/reboot`), waits for QGA readiness, and verifies `playmox-game.service` restarts cleanly. |
| **`UpdateVmResources`** | Validates quota deltas (CPU, RAM, Disk). Disk size can only be increased (`DiskGb >= current`). Applies CPU/RAM/Disk changes on Proxmox (restarting the VM if required for non-hotplug changes) and invokes `IGameServerConnector.RestartAfterVmResourceChangeAsync` (e.g., recalculating Minecraft JVM `-Xmx` heap size to ~75% of VM RAM). |
| **`ResetVmPassword`** | Rotates the `gameserver` password inside the guest VM via SSH (`chpasswd`) or QEMU Guest Agent (`guest-exec`), updates Proxmox `cipassword`, and stores the newly encrypted password in `ConfigurationJson`. |
| **`DeleteVm`** | Stops the VM, destroys it on Proxmox (`DELETE /nodes/{node}/qemu/{vmid}?purge=1&destroy-unreferenced-disks=1`), deletes `PortLeases` rows, and triggers host DNAT cleanup. |
