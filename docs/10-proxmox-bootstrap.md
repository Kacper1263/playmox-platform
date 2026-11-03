# 10. Proxmox VE Host Bootstrap Guide

Playmox is designed so that administrators do not need to manually configure Proxmox RBAC roles, build Cloud-Init images by hand, or write custom `nftables` port-forwarding rules. Everything on the Proxmox VE host is configured by a single, idempotent script: `API/Bootstrap/bootstrap-playmox-proxmox.sh`.

---

## 1. What `bootstrap-playmox-proxmox.sh` Configures

When executed as `root` on a Proxmox VE host, the script executes 5 automated steps (`1/5` through `5/5`):

1. **Step 1/5 — Host Packages**:
   - Installs required host utilities via `apt-get`: `wget`, `ca-certificates`, `libguestfs-tools`, `jq`, and `nftables`.
2. **Step 2/5 — Least-Privilege RBAC Role, Service User & API Token**:
   - Creates or updates the `PlaymoxProvisioner` role with the privileges required by Playmox (`Datastore.AllocateSpace`, `Datastore.Audit`, `Pool.Allocate`, `SDN.Use`, `Sys.Audit`, `VM.Allocate`, `VM.Audit`, `VM.Clone`, `VM.Config.CDROM`, `VM.Config.Cloudinit`, `VM.Config.CPU`, `VM.Config.Disk`, `VM.Config.Memory`, `VM.Config.Network`, `VM.Config.Options`, `VM.Config.HWType`, `VM.GuestAgent.Unrestricted`, `VM.Monitor`, `VM.PowerMgmt`).
   - Automatically skips any privilege name not supported by the target Proxmox version.
   - Creates the service user (`playmox-svc@pve` by default), creates an API token (`playmox-api`), and applies ACL bindings on `/` for both the user and token.
3. **Step 3/5 — Ubuntu 24.04 LTS Cloud-Init Template (`VMID 9000`)**:
   - Checks whether `TEMPLATE_VMID` (default `9000`) already exists.
   - If it does not exist, downloads the Ubuntu 24.04 Cloud Image (`noble-server-cloudimg-amd64.img`), runs `virt-customize` to install and enable `qemu-guest-agent`, and clears `/etc/machine-id`, `/var/lib/dbus/machine-id`, and `/var/lib/cloud/*` so cloned VMs always generate unique DHCP identifiers.
   - Creates VM `9000` (`--memory 2048 --cores 2`), imports the disk onto `$STORAGE` (`local-lvm`), attaches a Cloud-Init drive on `ide2`, removes `serial0`, sets `--vga std` and `--agent enabled=1`, and converts the VM into a template (`qm template 9000`).
4. **Step 4/5 — Host-Level `nftables` DNAT Automation**:
   - Ensures the `local` datastore has the `snippets` content type enabled.
   - Installs `/usr/local/sbin/playmox-dnat-sync.sh` (synchronizes `playmox-dnat:ssh=...;gameStart=...;gameCount=...;proto=...` VM description tags to `nftables` `table ip playmox_nat` using per-VM flock locks `/run/playmox/dnat-sync-<vmid>.lock`).
   - Installs `/usr/local/sbin/playmox-dnat-gc.sh` (cleans up stale rules for stopped or deleted VMs) and `/usr/local/sbin/playmox-dnat-reconcile.sh` (reconciles all running VMs in parallel).
   - Installs `/var/lib/vz/snippets/playmox-dnat-hook.sh` (`local:snippets/playmox-dnat-hook.sh`) and enables `nftables`, `playmox-dnat-gc.timer` (every 1 minute), and `playmox-dnat-reconcile.timer` (every 15 seconds).
5. **Step 5/5 — Configuration Summary Output**:
   - Prints the exact values required for Playmox's `/configuration` page (`PROXMOX_URL`, `PROXMOX_NODE`, `PROXMOX_TEMPLATE_VMID`, `PROXMOX_STORAGE`, `PROXMOX_BRIDGE`, `PROXMOX_API_USER`, `PROXMOX_API_TOKEN_ID`, `PROXMOX_API_TOKEN_SECRET`, `PLAYMOX_DNAT_HOOKSCRIPT`).

---

## 2. Running the Bootstrap Script

### Option A: Download Directly from a Running Playmox API Instance
Playmox serves the script with normalized LF line endings at `GET /api/bootstrap/proxmox-script`. On your Proxmox VE host as `root`:

```bash
curl -fsSL "http://<PLAYMOX_API_HOST>:8080/api/bootstrap/proxmox-script" -o /root/bootstrap-playmox-proxmox.sh
chmod +x /root/bootstrap-playmox-proxmox.sh
/root/bootstrap-playmox-proxmox.sh
```

### Option B: Run Locally from the Repository
Copy `API/Bootstrap/bootstrap-playmox-proxmox.sh` to your Proxmox VE host and run:

```bash
chmod +x ./bootstrap-playmox-proxmox.sh
./bootstrap-playmox-proxmox.sh
```

---

## 3. Environment Variables Supported by `bootstrap-playmox-proxmox.sh`

The script supports the following environment variables at the top of `API/Bootstrap/bootstrap-playmox-proxmox.sh`:

| Variable | Default Value | Description |
| :--- | :--- | :--- |
| `PLAYMOX_USER` | `playmox-svc@pve` | Proxmox service user account. |
| `PLAYMOX_ROLE` | `PlaymoxProvisioner` | Name of the Proxmox RBAC role to create/update. |
| `PLAYMOX_TOKEN_ID` | `playmox-api` | API token name under `PLAYMOX_USER`. |
| `TEMPLATE_VMID` | `9000` | VMID for the Ubuntu 24.04 Cloud-Init template. |
| `TEMPLATE_NAME` | `playmox-ubuntu-2404-template` | Name of the template VM on Proxmox. |
| `BRIDGE` | `vmbr0` | Network bridge attached to `net0` on the template VM. |
| `STORAGE` | `local-lvm` | Proxmox storage pool where the template disk is imported. |
| `IMAGE_URL` | `https://cloud-images.ubuntu.com/noble/current/noble-server-cloudimg-amd64.img` | Source URL for the Ubuntu 24.04 LTS Cloud Image. |
| `IMAGE_PATH` | `/var/lib/vz/template/cloud/ubuntu-24.04-server-cloudimg-amd64.img` | Local path on the Proxmox host where the cloud image is cached and customized. |

### Example with Custom Overrides

```bash
TEMPLATE_VMID=9000 \
STORAGE=local-zfs \
BRIDGE=vmbr0 \
/root/bootstrap-playmox-proxmox.sh
```

---

## 4. Troubleshooting & Maintenance

- **Re-running the Script**: The script is idempotent. Re-running it updates RBAC permissions, refreshes `/usr/local/sbin/playmox-dnat-*.sh`, and restarts the systemd DNAT timers without overwriting an existing template VM.
- **Generating a New API Token Secret**: Proxmox only displays a token secret at creation time. If the token already exists, the script prints the command to remove it before re-running:
  ```bash
  pveum user token remove playmox-svc@pve playmox-api
  /root/bootstrap-playmox-proxmox.sh
  ```
- **Inspecting Host DNAT Rules**:
  ```bash
  nft -a list table ip playmox_nat
  systemctl status playmox-dnat-reconcile.timer playmox-dnat-gc.timer
  ```
- **Rebuilding the Template Image**: If you want to rebuild `VMID 9000` from scratch, destroy the existing template first (`qm destroy 9000 --purge`) and re-run `/root/bootstrap-playmox-proxmox.sh`.
