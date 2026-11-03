#!/usr/bin/env bash
set -euo pipefail

# Run on Proxmox host as root.
# Goal: create service API token + create cloud-init VM template for Playmox.

PLAYMOX_USER="${PLAYMOX_USER:-playmox-svc@pve}"
PLAYMOX_ROLE="${PLAYMOX_ROLE:-PlaymoxProvisioner}"
PLAYMOX_TOKEN_ID="${PLAYMOX_TOKEN_ID:-playmox-api}"

TEMPLATE_VMID="${TEMPLATE_VMID:-9000}"
TEMPLATE_NAME="${TEMPLATE_NAME:-playmox-ubuntu-2404-template}"
BRIDGE="${BRIDGE:-vmbr0}"
STORAGE="${STORAGE:-local-lvm}"
IMAGE_URL="${IMAGE_URL:-https://cloud-images.ubuntu.com/noble/current/noble-server-cloudimg-amd64.img}"
IMAGE_PATH="${IMAGE_PATH:-/var/lib/vz/template/cloud/ubuntu-24.04-server-cloudimg-amd64.img}"

DESIRED_ROLE_PRIVS="Datastore.AllocateSpace Datastore.Audit Pool.Allocate SDN.Use Sys.Audit VM.Allocate VM.Audit VM.Clone VM.Config.CDROM VM.Config.Cloudinit VM.Config.CPU VM.Config.Disk VM.Config.Memory VM.Config.Network VM.Config.Options VM.Config.HWType VM.GuestAgent.Unrestricted VM.Monitor VM.PowerMgmt"

YELLOW=$'\033[1;33m'
GREEN=$'\033[1;32m'
NC=$'\033[0m'

log_step() {
  printf "%b[%s] %s%b\n" "$YELLOW" "$1" "$2" "$NC"
}

log_ok() {
  printf "%b%s%b\n\n" "$GREEN" "$1" "$NC"
  sleep 2
}

log_step "1/5" "Installing required packages..."
apt-get update -y
apt-get install -y wget ca-certificates libguestfs-tools jq nftables
log_ok "Step 1/5 completed."

log_step "2/5" "Creating Playmox service user/role/token..."
set +e
USER_ADD_OUTPUT="$(pveum user add "$PLAYMOX_USER" --comment "Playmox automation service account" 2>&1)"
USER_ADD_RC=$?
set -e
if [[ $USER_ADD_RC -ne 0 ]]; then
  if echo "$USER_ADD_OUTPUT" | grep -qi "already exists"; then
    echo "User ${PLAYMOX_USER} already exists. Continuing."
  else
    echo "$USER_ADD_OUTPUT"
    exit $USER_ADD_RC
  fi
fi

ROLE_PRIVS="$DESIRED_ROLE_PRIVS"

while true; do
  if [[ -z "$ROLE_PRIVS" ]]; then
    echo "No valid privileges available for role ${PLAYMOX_ROLE}."
    exit 1
  fi

  set +e
  ROLE_ADD_OUTPUT="$(pveum role add "$PLAYMOX_ROLE" -privs "$ROLE_PRIVS" 2>&1)"
  ROLE_ADD_RC=$?
  set -e

  if [[ $ROLE_ADD_RC -eq 0 ]]; then
    break
  fi

  if echo "$ROLE_ADD_OUTPUT" | grep -qi "already exists"; then
    break
  fi

  INVALID_PRIV="$(echo "$ROLE_ADD_OUTPUT" | sed -n "s/.*invalid privilege '\([^']*\)'.*/\1/p" | head -n1)"
  if [[ -n "$INVALID_PRIV" ]]; then
    echo "Skipping unavailable privilege on this Proxmox version: $INVALID_PRIV"
    ROLE_PRIVS="$(echo " $ROLE_PRIVS " | sed "s/ $INVALID_PRIV / /g" | xargs)"
    continue
  fi

  echo "$ROLE_ADD_OUTPUT"
  exit $ROLE_ADD_RC
done

while true; do
  if [[ -z "$ROLE_PRIVS" ]]; then
    echo "No valid privileges available for role ${PLAYMOX_ROLE}."
    exit 1
  fi

  set +e
  ROLE_MODIFY_OUTPUT="$(pveum role modify "$PLAYMOX_ROLE" -privs "$ROLE_PRIVS" 2>&1)"
  ROLE_MODIFY_RC=$?
  set -e

  if [[ $ROLE_MODIFY_RC -eq 0 ]]; then
    break
  fi

  INVALID_PRIV="$(echo "$ROLE_MODIFY_OUTPUT" | sed -n "s/.*invalid privilege '\([^']*\)'.*/\1/p" | head -n1)"
  if [[ -n "$INVALID_PRIV" ]]; then
    echo "Skipping unavailable privilege on this Proxmox version: $INVALID_PRIV"
    ROLE_PRIVS="$(echo " $ROLE_PRIVS " | sed "s/ $INVALID_PRIV / /g" | xargs)"
    continue
  fi

  echo "$ROLE_MODIFY_OUTPUT"
  exit $ROLE_MODIFY_RC
done

pveum acl modify / -user "$PLAYMOX_USER" -role "$PLAYMOX_ROLE"

set +e
TOKEN_ADD_OUTPUT="$(pveum user token add "$PLAYMOX_USER" "$PLAYMOX_TOKEN_ID" --privsep 1 --expire 0 --output-format json 2>&1)"
TOKEN_ADD_RC=$?
set -e
if [[ $TOKEN_ADD_RC -eq 0 ]]; then
  TOKEN_VALUE="$(echo "$TOKEN_ADD_OUTPUT" | sed -n 's/.*"value":"\([^"]*\)".*/\1/p')"
else
  if echo "$TOKEN_ADD_OUTPUT" | grep -qi "already exists"; then
    echo "Token ${PLAYMOX_TOKEN_ID} already exists for ${PLAYMOX_USER}. Continuing."
    echo "  If you need a new secret, remove token first:"
    echo "  pveum user token remove ${PLAYMOX_USER} ${PLAYMOX_TOKEN_ID}"
    TOKEN_VALUE=""
  else
    echo "$TOKEN_ADD_OUTPUT"
    exit $TOKEN_ADD_RC
  fi
fi

TOKEN_FULL_ID="${PLAYMOX_USER}!${PLAYMOX_TOKEN_ID}"
pveum acl modify / -token "$TOKEN_FULL_ID" -role "$PLAYMOX_ROLE"
log_ok "Step 2/5 completed."

log_step "3/5" "Preparing cloud image and template..."
if qm status "$TEMPLATE_VMID" >/dev/null 2>&1; then
  if qm config "$TEMPLATE_VMID" 2>/dev/null | grep -q "^template: 1$"; then
    echo "Template VMID ${TEMPLATE_VMID} already exists. Ensuring non-serial console defaults."
    qm set "$TEMPLATE_VMID" --delete serial0 >/dev/null 2>&1 || true
    qm set "$TEMPLATE_VMID" --vga std >/dev/null 2>&1 || true
    qm set "$TEMPLATE_VMID" --agent enabled=1 >/dev/null 2>&1 || true
    echo "NOTE: Existing template was not rebuilt. If you want to have up to date template, please remove current template and re-run the script."
  else
    echo "VMID ${TEMPLATE_VMID} already exists as regular VM. Skipping template creation to avoid overwriting."
  fi
else
  mkdir -p "$(dirname "$IMAGE_PATH")"
  if [[ -f "$IMAGE_PATH" ]]; then
    echo "Cloud image already present at ${IMAGE_PATH}. Reusing existing file."
  else
    wget -O "$IMAGE_PATH" "$IMAGE_URL"
  fi

  echo "Installing qemu-guest-agent into cloud image (required for remote password reset)..."
  virt-customize -a "$IMAGE_PATH" \
    --install qemu-guest-agent \
    --run-command 'systemctl enable qemu-guest-agent' \
    --run-command 'truncate -s 0 /etc/machine-id' \
    --run-command 'rm -f /var/lib/dbus/machine-id' \
    --run-command 'ln -s /etc/machine-id /var/lib/dbus/machine-id' \
    --run-command 'rm -rf /var/lib/cloud/*'

  qm create "$TEMPLATE_VMID" --name "$TEMPLATE_NAME" --memory 2048 --cores 2 --net0 "virtio,bridge=${BRIDGE}"
  qm importdisk "$TEMPLATE_VMID" "$IMAGE_PATH" "$STORAGE"
  qm set "$TEMPLATE_VMID" --scsihw virtio-scsi-pci --scsi0 "${STORAGE}:vm-${TEMPLATE_VMID}-disk-0"
  qm set "$TEMPLATE_VMID" --ide2 "${STORAGE}:cloudinit"
  qm set "$TEMPLATE_VMID" --boot c --bootdisk scsi0
  qm set "$TEMPLATE_VMID" --delete serial0 >/dev/null 2>&1 || true
  qm set "$TEMPLATE_VMID" --vga std
  qm set "$TEMPLATE_VMID" --agent enabled=1
  qm template "$TEMPLATE_VMID"
fi
log_ok "Step 3/5 completed."

log_step "4/5" "Installing host-level DNAT automation (nftables + VM hookscript)..."
if pvesm status --storage local >/dev/null 2>&1; then
  LOCAL_CONTENT="$(awk '
    $1=="dir:" && $2=="local" { in_local=1; next }
    /^[^[:space:]]/ && in_local { in_local=0 }
    in_local && $1=="content" {
      for (i = 2; i <= NF; i++) {
        printf "%s", $i
      }
      print ""
      exit
    }
  ' /etc/pve/storage.cfg | tr -d '[:space:]')"
  if [[ -n "$LOCAL_CONTENT" && ",$LOCAL_CONTENT," != *",snippets,"* ]]; then
    UPDATED_CONTENT="$(echo "$LOCAL_CONTENT,snippets" | tr ',' '\n' | awk 'NF && !seen[$0]++' | paste -sd, -)"
    pvesm set local --content "$UPDATED_CONTENT"
    echo "Enabled 'snippets' content type for storage 'local'."
  fi
fi

mkdir -p /var/lib/vz/snippets
mkdir -p /usr/local/sbin

cat > /usr/local/sbin/playmox-dnat-sync.sh <<'DNAT_SYNC'
#!/usr/bin/env bash
set -euo pipefail

VMID="${1:-}"
PHASE="${2:-post-start}"
LOG_TAG="playmox-dnat-sync"
MAX_IP_ATTEMPTS="${PLAYMOX_DNAT_SYNC_MAX_IP_ATTEMPTS:-60}"
IP_RETRY_DELAY_SECONDS="${PLAYMOX_DNAT_SYNC_IP_RETRY_DELAY_SECONDS:-2}"
LOCK_RETRY_COUNT="${PLAYMOX_DNAT_SYNC_LOCK_RETRY_COUNT:-5}"
LOCK_RETRY_DELAY_SECONDS="${PLAYMOX_DNAT_SYNC_LOCK_RETRY_DELAY_SECONDS:-1}"

dlog() {
  logger -t "$LOG_TAG" "vmid=${VMID:-?} phase=${PHASE:-?} $*"
}

if [[ -z "$VMID" ]] || [[ ! "$VMID" =~ ^[0-9]+$ ]]; then
  exit 0
fi

[[ "$MAX_IP_ATTEMPTS" =~ ^[0-9]+$ ]] || MAX_IP_ATTEMPTS=60
[[ "$LOCK_RETRY_COUNT" =~ ^[0-9]+$ ]] || LOCK_RETRY_COUNT=5
[[ "$IP_RETRY_DELAY_SECONDS" =~ ^[0-9]+([.][0-9]+)?$ ]] || IP_RETRY_DELAY_SECONDS=2
[[ "$LOCK_RETRY_DELAY_SECONDS" =~ ^[0-9]+([.][0-9]+)?$ ]] || LOCK_RETRY_DELAY_SECONDS=1

if (( MAX_IP_ATTEMPTS < 1 )); then
  MAX_IP_ATTEMPTS=1
fi
if (( LOCK_RETRY_COUNT < 1 )); then
  LOCK_RETRY_COUNT=1
fi

mkdir -p /run/playmox
exec 9>"/run/playmox/dnat-sync-${VMID}.lock"

acquire_vm_lock() {
  for _ in $(seq 1 "$LOCK_RETRY_COUNT"); do
    if flock -n 9; then
      return 0
    fi
    sleep "$LOCK_RETRY_DELAY_SECONDS"
  done

  dlog "lock-timeout retries=$LOCK_RETRY_COUNT delay=${LOCK_RETRY_DELAY_SECONDS}s"
  return 1
}

if ! acquire_vm_lock; then
  exit 0
fi

ensure_nat_chains() {
  nft list table ip playmox_nat >/dev/null 2>&1 || nft add table ip playmox_nat
  nft list chain ip playmox_nat prerouting >/dev/null 2>&1 || nft add chain ip playmox_nat prerouting '{ type nat hook prerouting priority dstnat; policy accept; }'
  nft list chain ip playmox_nat output >/dev/null 2>&1 || nft add chain ip playmox_nat output '{ type nat hook output priority -100; policy accept; }'
  nft list chain ip playmox_nat postrouting >/dev/null 2>&1 || nft add chain ip playmox_nat postrouting '{ type nat hook postrouting priority srcnat; policy accept; }'
}

delete_vm_rules_in_chain() {
  local chain="$1"
  while true; do
    local handle
    handle="$(nft -a list chain ip playmox_nat "$chain" 2>/dev/null | awk -v vmid="$VMID" '$0 ~ ("playmox-vm-" vmid "-") { for (i=1;i<=NF;i++) if ($i=="handle") {print $(i+1); exit} }')"
    [[ -z "$handle" ]] && break
    nft delete rule ip playmox_nat "$chain" handle "$handle" >/dev/null 2>&1 || break
  done
}

cleanup_vm_rules() {
  delete_vm_rules_in_chain prerouting
  delete_vm_rules_in_chain output
  delete_vm_rules_in_chain postrouting
}

rule_exists() {
  local chain="$1"
  local comment="$2"
  local pattern="$3"
  nft list chain ip playmox_nat "$chain" 2>/dev/null | grep -F "comment \"$comment\"" | grep -F "$pattern" >/dev/null 2>&1
}

read_dnat_metadata() {
  local description config_output rc
  local dnat_pattern='playmox-dnat:ssh=([^;]+);gameStart=([^;]+);gameCount=([^;]+);proto=([^;]+)'
  set +e
  config_output="$(qm config "$VMID" 2>&1)"
  rc=$?
  set -e
  if [[ $rc -ne 0 ]]; then
    dlog "qm-config-unavailable rc=$rc output='$(echo "$config_output" | tr '\n' ' ' | sed 's/[[:space:]]\+/ /g')'"
    return 2
  fi

  description="$(echo "$config_output" | sed -n 's/^description: //p' | head -n1 || true)"
  description="${description//%3A/:}"
  description="${description//%3a/:}"
  if [[ ! "$description" =~ $dnat_pattern ]]; then
    return 1
  fi

  DNAT_SSH_PORT="${BASH_REMATCH[1]}"
  DNAT_GAME_START="${BASH_REMATCH[2]}"
  DNAT_GAME_COUNT="${BASH_REMATCH[3]}"
  DNAT_PROTO="${BASH_REMATCH[4]}"
  DNAT_PROTO_EXTRA="$(echo "$description" | sed -n 's/.*;protoExtra=\([^;]*\).*/\1/p' | head -n1 || true)"

  [[ "$DNAT_SSH_PORT" == "-" ]] && DNAT_SSH_PORT=""
  [[ "$DNAT_GAME_START" == "-" ]] && DNAT_GAME_START=""
  [[ "$DNAT_GAME_COUNT" == "-" ]] && DNAT_GAME_COUNT=""
  [[ -n "$DNAT_SSH_PORT" && ! "$DNAT_SSH_PORT" =~ ^[0-9]+$ ]] && DNAT_SSH_PORT=""
  [[ -n "$DNAT_GAME_START" && ! "$DNAT_GAME_START" =~ ^[0-9]+$ ]] && DNAT_GAME_START=""
  [[ -n "$DNAT_GAME_COUNT" && ! "$DNAT_GAME_COUNT" =~ ^[0-9]+$ ]] && DNAT_GAME_COUNT=""
  [[ "$DNAT_PROTO" == "-" ]] && DNAT_PROTO="tcp"
  [[ -z "$DNAT_PROTO" ]] && DNAT_PROTO="tcp"
  return 0
}

resolve_vm_ip() {
  for _ in $(seq 1 "$MAX_IP_ATTEMPTS"); do
    local interfaces_json
    interfaces_json="$(qm guest cmd "$VMID" network-get-interfaces 2>/dev/null || true)"
    if [[ -n "$interfaces_json" ]]; then
      local ip
      # First try: exclude virtual adapters, containers, and tunnels (docker, veth, br-, tailscale, tun, tap)
      ip="$(echo "$interfaces_json" | jq -r '.[]? | select(.name != null and (.name | test("^(lo|docker|veth|br-|tailscale|tun|tap)") | not)) | ."ip-addresses"[]? | select(."ip-address-type" == "ipv4") | ."ip-address"' | grep -Ev '^(127\.|169\.254\.)' | head -n1 || true)"
      # Fallback: if no physical interface found, accept any non-loopback IPv4
      if [[ -z "$ip" ]]; then
        ip="$(echo "$interfaces_json" | jq -r '.[]? | select(.name != "lo") | ."ip-addresses"[]? | select(."ip-address-type" == "ipv4") | ."ip-address"' | grep -Ev '^(127\.|169\.254\.)' | head -n1 || true)"
      fi
      if [[ -n "$ip" ]]; then
        echo "$ip"
        return 0
      fi
    fi
    sleep "$IP_RETRY_DELAY_SECONDS"
  done

  return 1
}

iterate_game_protos() {
  local joined proto cleaned seen
  joined="${DNAT_PROTO:-tcp}"
  [[ -n "${DNAT_PROTO_EXTRA:-}" ]] && joined="${joined},${DNAT_PROTO_EXTRA}"
  seen=""
  IFS=',' read -ra proto <<< "$joined"
  for p in "${proto[@]}"; do
    cleaned="$(echo "$p" | tr '[:upper:]' '[:lower:]' | sed 's/^[[:space:]]*//;s/[[:space:]]*$//')"
    [[ -z "$cleaned" ]] && continue
    [[ "$cleaned" != "tcp" && "$cleaned" != "udp" ]] && continue
    [[ ",$seen," == *",$cleaned,"* ]] && continue
    seen="${seen:+$seen,}$cleaned"
    echo "$cleaned"
  done
  [[ -z "$seen" ]] && echo "tcp"
}

add_rule_pair() {
  local proto="$1"
  local public_port="$2"
  local vm_ip="$3"
  local label="$4"
  local target_port="${5:-$public_port}"

  local comment="playmox-vm-$VMID-$label"
  local snat_comment="${comment}-snat"

  rule_exists prerouting "$comment" "dnat to $vm_ip:$target_port" || \
    nft add rule ip playmox_nat prerouting "$proto" dport "$public_port" dnat to "$vm_ip:$target_port" comment "$comment" >/dev/null 2>&1 || true
  rule_exists output "$comment" "dnat to $vm_ip:$target_port" || \
    nft add rule ip playmox_nat output "$proto" dport "$public_port" dnat to "$vm_ip:$target_port" comment "$comment" >/dev/null 2>&1 || true
  rule_exists postrouting "$snat_comment" "ip daddr $vm_ip" || \
    nft add rule ip playmox_nat postrouting ip daddr "$vm_ip" "$proto" dport "$target_port" masquerade comment "$snat_comment" >/dev/null 2>&1 || true
}

rules_need_reconcile() {
  local vm_ip="$1"
  local expected
  local label
  local snat_label

  if [[ -n "${DNAT_SSH_PORT:-}" ]]; then
    label="playmox-vm-$VMID-ssh-$DNAT_SSH_PORT"
    snat_label="${label}-snat"
    expected="dnat to $vm_ip:22"
    rule_exists prerouting "$label" "$expected" || return 0
    rule_exists output "$label" "$expected" || return 0
    rule_exists postrouting "$snat_label" "ip daddr $vm_ip" || return 0
  fi

  if [[ -n "${DNAT_GAME_START:-}" && -n "${DNAT_GAME_COUNT:-}" ]]; then
    for offset in $(seq 0 $((DNAT_GAME_COUNT - 1))); do
      port=$((DNAT_GAME_START + offset))
      for proto in $(iterate_game_protos); do
        label="playmox-vm-$VMID-game-$proto-$port"
        snat_label="${label}-snat"
        expected="dnat to $vm_ip:$port"
        rule_exists prerouting "$label" "$expected" || return 0
        rule_exists output "$label" "$expected" || return 0
        rule_exists postrouting "$snat_label" "ip daddr $vm_ip" || return 0
      done
    done
  fi

  return 1
}

ensure_nat_chains

case "$PHASE" in
  post-start)
    set +e
    read_dnat_metadata
    meta_rc=$?
    set -e
    if [[ $meta_rc -eq 1 ]]; then
      dlog "metadata-removed-cleanup"
      cleanup_vm_rules
      exit 0
    elif [[ $meta_rc -eq 2 ]]; then
      dlog "metadata-unavailable-keeping-existing-rules"
      exit 0
    elif [[ $meta_rc -ne 0 ]]; then
      dlog "metadata-unknown-error rc=$meta_rc keeping-existing-rules"
      exit 0
    fi

    if [[ -z "${DNAT_SSH_PORT:-}" && -z "${DNAT_GAME_START:-}" ]]; then
      dlog "empty-metadata-cleanup"
      cleanup_vm_rules
      exit 0
    fi

    VM_IP="$(resolve_vm_ip || true)"
    if [[ -z "$VM_IP" ]]; then
      # Keep current rules when guest-agent temporarily cannot report IP.
      # Removing DNAT here causes short connection drops for active sessions.
      dlog "guest-ip-unavailable-keeping-existing-rules"
      exit 0
    fi

    if rules_need_reconcile "$VM_IP"; then
      dlog "reconcile-needed vm_ip=$VM_IP ssh=${DNAT_SSH_PORT:-none} gameStart=${DNAT_GAME_START:-none} gameCount=${DNAT_GAME_COUNT:-0} proto=${DNAT_PROTO:-tcp}"
      cleanup_vm_rules
    else
      dlog "reconcile-not-needed vm_ip=$VM_IP"
      exit 0
    fi

    if [[ -n "${DNAT_SSH_PORT:-}" ]]; then
      add_rule_pair tcp "$DNAT_SSH_PORT" "$VM_IP" "ssh-$DNAT_SSH_PORT" 22
    fi

    if [[ -n "${DNAT_GAME_START:-}" && -n "${DNAT_GAME_COUNT:-}" ]]; then
      for offset in $(seq 0 $((DNAT_GAME_COUNT - 1))); do
        port=$((DNAT_GAME_START + offset))
        for proto in $(iterate_game_protos); do
          add_rule_pair "$proto" "$port" "$VM_IP" "game-$proto-$port"
        done
      done
    fi
    dlog "reconcile-applied vm_ip=$VM_IP"
    ;;
  pre-stop|post-stop)
    dlog "phase-cleanup"
    cleanup_vm_rules
    ;;
  pre-start)
    dlog "phase-pre-start-noop"
    ;;
  *)
    dlog "phase-unknown-noop"
    ;;
esac

exit 0
DNAT_SYNC
chmod +x /usr/local/sbin/playmox-dnat-sync.sh

cat > /usr/local/sbin/playmox-dnat-gc.sh <<'DNAT_GC'
#!/usr/bin/env bash
set -euo pipefail
LOG_TAG="playmox-dnat-gc"

dlog() {
  logger -t "$LOG_TAG" "$*"
}

nft list table ip playmox_nat >/dev/null 2>&1 || exit 0

VMIDS="$(nft -a list table ip playmox_nat 2>/dev/null | sed -n 's/.*playmox-vm-\([0-9]\+\)-.*/\1/p' | sort -u)"
for vmid in $VMIDS; do
  set +e
  status_output="$(qm status "$vmid" 2>&1)"
  status_rc=$?
  set -e
  if [[ $status_rc -eq 0 ]]; then
    continue
  fi

  if echo "$status_output" | grep -Eqi 'does not exist|configuration file .* does not exist'; then
    dlog "vmid=$vmid missing -> cleanup"
    /usr/local/sbin/playmox-dnat-sync.sh "$vmid" pre-stop >/dev/null 2>&1 || true
  else
    dlog "vmid=$vmid qm-status-unavailable rc=$status_rc -> skip cleanup"
  fi
done
DNAT_GC
chmod +x /usr/local/sbin/playmox-dnat-gc.sh

cat > /usr/local/sbin/playmox-dnat-reconcile.sh <<'DNAT_RECONCILE'
#!/usr/bin/env bash
set -euo pipefail
LOG_TAG="playmox-dnat-reconcile"
PARALLELISM="${PLAYMOX_DNAT_RECONCILE_PARALLELISM:-8}"
SYNC_MAX_IP_ATTEMPTS="${PLAYMOX_DNAT_RECONCILE_SYNC_MAX_IP_ATTEMPTS:-3}"
SYNC_IP_RETRY_DELAY_SECONDS="${PLAYMOX_DNAT_RECONCILE_SYNC_IP_RETRY_DELAY_SECONDS:-1}"

dlog() {
  logger -t "$LOG_TAG" "$*"
}

[[ "$PARALLELISM" =~ ^[0-9]+$ ]] || PARALLELISM=8
[[ "$SYNC_MAX_IP_ATTEMPTS" =~ ^[0-9]+$ ]] || SYNC_MAX_IP_ATTEMPTS=3
[[ "$SYNC_IP_RETRY_DELAY_SECONDS" =~ ^[0-9]+([.][0-9]+)?$ ]] || SYNC_IP_RETRY_DELAY_SECONDS=1

if (( PARALLELISM < 1 )); then
  PARALLELISM=1
fi
if (( SYNC_MAX_IP_ATTEMPTS < 1 )); then
  SYNC_MAX_IP_ATTEMPTS=1
fi

declare -a vmids_to_reconcile=()
while read -r vmid status; do
  [[ -z "$vmid" ]] && continue
  [[ "${status,,}" == "running" ]] || continue
  set +e
  vm_config="$(qm config "$vmid" 2>&1)"
  vm_config_rc=$?
  set -e
  if [[ $vm_config_rc -ne 0 ]]; then
    dlog "vmid=$vmid qm-config-unavailable rc=$vm_config_rc -> skip"
    continue
  fi
  description="$(echo "$vm_config" | sed -n 's/^description: //p' | head -n1 || true)"
  description="${description//%3A/:}"
  description="${description//%3a/:}"
  [[ "$description" =~ ^playmox-dnat: ]] || continue
  vmids_to_reconcile+=("$vmid")
done < <(qm list 2>/dev/null | awk 'NR>1 {print $1" "$3}')

if [[ ${#vmids_to_reconcile[@]} -eq 0 ]]; then
  exit 0
fi

dlog "reconcile-batch size=${#vmids_to_reconcile[@]} parallelism=$PARALLELISM syncAttempts=$SYNC_MAX_IP_ATTEMPTS syncDelay=${SYNC_IP_RETRY_DELAY_SECONDS}s"

export PLAYMOX_DNAT_SYNC_MAX_IP_ATTEMPTS="$SYNC_MAX_IP_ATTEMPTS"
export PLAYMOX_DNAT_SYNC_IP_RETRY_DELAY_SECONDS="$SYNC_IP_RETRY_DELAY_SECONDS"

printf '%s\n' "${vmids_to_reconcile[@]}" | xargs -r -P "$PARALLELISM" -I{} /usr/local/sbin/playmox-dnat-sync.sh "{}" post-start || true
DNAT_RECONCILE
chmod +x /usr/local/sbin/playmox-dnat-reconcile.sh

cat > /var/lib/vz/snippets/playmox-dnat-hook.sh <<'DNAT_HOOK'
#!/usr/bin/env bash
set -euo pipefail

VMID="${1:-}"
PHASE="${2:-}"
/usr/local/sbin/playmox-dnat-sync.sh "$VMID" "$PHASE"
DNAT_HOOK
chmod +x /var/lib/vz/snippets/playmox-dnat-hook.sh

cat > /etc/systemd/system/playmox-dnat-gc.service <<'GC_SERVICE'
[Unit]
Description=Playmox DNAT stale rule cleanup

[Service]
Type=oneshot
ExecStart=/usr/local/sbin/playmox-dnat-gc.sh
GC_SERVICE

cat > /etc/systemd/system/playmox-dnat-reconcile.service <<'RECONCILE_SERVICE'
[Unit]
Description=Playmox DNAT reconcile for active VMs

[Service]
Type=oneshot
ExecStart=/usr/local/sbin/playmox-dnat-reconcile.sh
RECONCILE_SERVICE

cat > /etc/systemd/system/playmox-dnat-gc.timer <<'GC_TIMER'
[Unit]
Description=Run Playmox DNAT cleanup periodically

[Timer]
OnBootSec=2min
OnUnitActiveSec=1min
Unit=playmox-dnat-gc.service

[Install]
WantedBy=timers.target
GC_TIMER

cat > /etc/systemd/system/playmox-dnat-reconcile.timer <<'RECONCILE_TIMER'
[Unit]
Description=Run Playmox DNAT reconcile periodically

[Timer]
OnBootSec=30s
OnUnitActiveSec=15s
Unit=playmox-dnat-reconcile.service

[Install]
WantedBy=timers.target
RECONCILE_TIMER

echo "Reloading systemd daemon and enabling DNAT automation services..."
systemctl daemon-reload
systemctl enable --now nftables
systemctl enable --now playmox-dnat-gc.timer
systemctl enable --now playmox-dnat-reconcile.timer
systemctl start playmox-dnat-reconcile.service || true
log_ok "Step 4/5 completed."

log_step "5/5" "Output values for Playmox API config"
NODE_NAME="$(hostname)"
NODE_IP="$(hostname -I 2>/dev/null | awk '{print $1}')"
if [[ -z "$NODE_IP" ]]; then
  NODE_IP="$(getent ahostsv4 "$NODE_NAME" 2>/dev/null | awk '{print $1; exit}')"
fi
PVE_HOST="${NODE_IP:-$NODE_NAME}"
PVE_URL="https://${PVE_HOST}:8006/api2/json"

echo ""
echo "===== PLAYMOX PROXMOX CONFIG ====="
echo "PROXMOX_URL=${PVE_URL}"
echo "PROXMOX_NODE=${NODE_NAME}"
echo "PROXMOX_TEMPLATE_VMID=${TEMPLATE_VMID}"
echo "PROXMOX_STORAGE=${STORAGE}"
echo "PROXMOX_BRIDGE=${BRIDGE}"
echo "PROXMOX_API_USER=${PLAYMOX_USER}"
echo "PROXMOX_API_TOKEN_ID=${PLAYMOX_TOKEN_ID}"
if [[ -n "${TOKEN_VALUE}" ]]; then
  echo "PROXMOX_API_TOKEN_SECRET=${TOKEN_VALUE}"
else
  echo "PROXMOX_API_TOKEN_SECRET=<existing token secret not retrievable; create new token to get value>"
fi
echo "PLAYMOX_DNAT_HOOKSCRIPT=local:snippets/playmox-dnat-hook.sh"
log_ok "Step 5/5 completed."
