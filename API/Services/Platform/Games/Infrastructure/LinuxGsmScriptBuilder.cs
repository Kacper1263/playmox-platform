namespace PlaymoxAPI.Services.Platform;

internal static class LinuxGsmScriptBuilder
{
    public const string LeastPrivilegeSudoersRule =
        "gameserver ALL=(root) NOPASSWD:/bin/systemctl start playmox-game.service,/bin/systemctl stop playmox-game.service,/bin/systemctl restart playmox-game.service,/usr/bin/systemctl start playmox-game.service,/usr/bin/systemctl stop playmox-game.service,/usr/bin/systemctl restart playmox-game.service";

    public static string BuildBaseSetupBlock(
        string summary,
        string serverBinary,
        string? extraAptPackages = null,
        string? postAptCommands = null)
    {
        var packages = string.IsNullOrWhiteSpace(extraAptPackages)
            ? "curl wget tar unzip sudo tmux jq ca-certificates file zstd"
            : $"curl wget tar unzip sudo tmux jq ca-certificates file zstd {extraAptPackages.Trim()}";
        var extraCommands = string.IsNullOrWhiteSpace(postAptCommands)
            ? string.Empty
            : $"\n{postAptCommands.Trim()}\n";

        return $"""
set -e
# PLAYMOX_SUMMARY: {summary}
export DEBIAN_FRONTEND=noninteractive
if [ ! -f /var/lib/apt/periodic/update-success-stamp ] || [ -n "$(find /var/lib/apt/periodic/update-success-stamp -mmin +60 -print -quit 2>/dev/null)" ]; then
  apt-get -o DPkg::Lock::Timeout=300 update -y
fi
apt-get -o DPkg::Lock::Timeout=300 install -y {packages}{extraCommands}
id -u gameserver >/dev/null 2>&1 || useradd -m -s /bin/bash gameserver
echo '{LeastPrivilegeSudoersRule}' > /etc/sudoers.d/99-gameserver && chmod 440 /etc/sudoers.d/99-gameserver && rm -f /etc/sudoers.d/99-gameserver-nopasswd
mkdir -p /home/gameserver/log/script /home/gameserver/log/console
chown -R gameserver:gameserver /home/gameserver || true
su - gameserver -c 'cd ~ && [ -f linuxgsm.sh ] || (wget -O linuxgsm.sh https://linuxgsm.sh && chmod +x linuxgsm.sh)'
chown -R gameserver:gameserver /home/gameserver || true
su - gameserver -c 'cd ~ && [ -f {serverBinary} ] || bash linuxgsm.sh {serverBinary}'
chown -R gameserver:gameserver /home/gameserver || true
""";
    }

    public static string BuildForceReinstallBlock(string serverBinary, bool forceReinstall)
    {
        if (!forceReinstall)
        {
            return string.Empty;
        }

        return $"""
systemctl stop playmox-game.service || true
su - gameserver -c 'cd ~ && ./{serverBinary} stop || true'
rm -rf /home/gameserver/serverfiles
chown -R gameserver:gameserver /home/gameserver || true
""";
    }

    public static string BuildSystemdServiceAndStartBlock(
        string serverBinary,
        string diagnosticsLabel,
        string? extraDiagnosticsCommands = null)
    {
        var extraDiag = string.IsNullOrWhiteSpace(extraDiagnosticsCommands)
            ? string.Empty
            : $"\n  {extraDiagnosticsCommands.Trim()}";

        return $"""
cat >/etc/systemd/system/playmox-game.service <<'UNIT'
[Unit]
Description=Playmox Game Server
After=network-online.target
Wants=network-online.target

[Service]
Type=oneshot
RemainAfterExit=yes
User=gameserver
WorkingDirectory=/home/gameserver
ExecStart=/bin/bash -lc '/home/gameserver/{serverBinary} start || [ $$? -eq 2 ]'
ExecStop=/bin/bash -lc '/home/gameserver/{serverBinary} stop || true'
ExecReload=/bin/bash -lc '/home/gameserver/{serverBinary} restart'
SuccessExitStatus=0 2
StandardOutput=journal
StandardError=journal

[Install]
WantedBy=multi-user.target
UNIT
systemctl daemon-reload
if ! systemctl enable --now playmox-game.service; then
  echo "=== PLAYMOX {diagnosticsLabel} DIAGNOSTICS BEGIN ==="{extraDiag}
  su - gameserver -c 'cd ~ && ./{serverBinary} details' || true
  ls -lah /home/gameserver/serverfiles || true
  find /home/gameserver/log -maxdepth 2 -type f -print || true
  journalctl -u playmox-game.service -n 200 --no-pager || true
  echo "=== PLAYMOX {diagnosticsLabel} DIAGNOSTICS END ==="
  exit 1
fi
""";
    }

    public static string BuildStopForShutdownScript() => """
set -e
if systemctl is-enabled playmox-game.service >/dev/null 2>&1; then
  systemctl stop playmox-game.service || true
fi
""";

    public static string BuildRestartAfterResourceChangeScript(string serverBinary) => $"""
set -e
# PLAYMOX_SUMMARY: Restart after resource change
if [ -x /home/gameserver/{serverBinary} ]; then
  su - gameserver -c 'cd ~ && ./{serverBinary} restart'
fi
""";

    public static string BuildServiceDiagnosticsScript() => """
set -eu
echo "PLAYMOX_SYSTEMD_STATUS_BEGIN"
systemctl status playmox-game.service --no-pager -l 2>&1 || true
echo "PLAYMOX_SYSTEMD_STATUS_END"
echo "PLAYMOX_JOURNAL_BEGIN"
journalctl -u playmox-game.service -n 80 --no-pager 2>&1 || true
echo "PLAYMOX_JOURNAL_END"
""";
}
