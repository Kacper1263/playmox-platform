using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PlaymoxAPI.BLL.BO.Platform;
using PlaymoxAPI.Services.Proxmox;

namespace PlaymoxAPI.Services.Platform;

public sealed class TerrariaGameServerConnector : LinuxGsmConnectorBase
{
    public TerrariaGameServerConnector(
        ProxmoxAutomationClient proxmoxAutomationClient,
        ILogger<TerrariaGameServerConnector> logger)
        : base(proxmoxAutomationClient, logger)
    {
    }

    public override GameServerKind Kind => GameServerKind.Terraria;
    protected override string ServerBinary => "terrariaserver";
    protected override string BackupSlug => "terraria";
    protected override string GameDisplayName => "Terraria";

    public override async Task InstallOrUpdateAsync(
        VirtualGameInstance instance,
        string? configurationJson,
        GameDefinition gameDefinition,
        bool forceReinstall,
        CancellationToken cancellationToken)
    {
        var settings = ParseTerrariaSettings(configurationJson);
        var gamePort = instance.GamePublicPortStart ?? gameDefinition.DefaultPort ?? 7777;
        var steam = GameServerConnectorSshOps.ExtractSteamAccess(instance.ConfigurationJson);
        var useAnonymousSteam = string.IsNullOrWhiteSpace(steam.Username) ||
                                string.Equals(steam.Username, "anonymous", StringComparison.OrdinalIgnoreCase);
        if (!useAnonymousSteam && string.IsNullOrWhiteSpace(steam.Password))
        {
            throw new InvalidOperationException("SteamCMD password is missing. Configure full Steam login or use anonymous mode.");
        }

        var steamUsername = GameServerConnectorSshOps.EscapeForDoubleQuotedConfig(useAnonymousSteam ? "anonymous" : steam.Username!);
        var steamPassword = GameServerConnectorSshOps.EscapeForDoubleQuotedConfig(useAnonymousSteam ? string.Empty : steam.Password!);
        var worldName = GameServerConnectorSshOps.EscapeForDoubleQuotedConfig(settings.WorldName ?? "PlaymoxWorld");
        var worldFileName = BuildTerrariaWorldFileName(worldName);
        var worldSeed = GameServerConnectorSshOps.EscapeForDoubleQuotedConfig(settings.WorldSeed ?? string.Empty);
        var motd = GameServerConnectorSshOps.EscapeForDoubleQuotedConfig(settings.Motd ?? "Playmox Terraria");
        var maxPlayers = settings.MaxPlayers ?? 8;
        var autoCreate = settings.AutoCreateSize ?? 2;
        var serverPassword = GameServerConnectorSshOps.EscapeForDoubleQuotedConfig(settings.Password ?? string.Empty);
        const string terrariaConfigPath = "/home/gameserver/serverfiles/serverconfig.txt";
        const string terrariaCommonCfgPath = "/home/gameserver/lgsm/config-lgsm/terrariaserver/common.cfg";
        const string terrariaInstanceCfgPath = "/home/gameserver/lgsm/config-lgsm/terrariaserver/terrariaserver.cfg";
        const string terrariaSelfCfgPath = "/home/gameserver/serverfiles/terrariaserver.txt";
        var terrariaCustomScript = GameServerCustomParameters.BuildIniUpsertScript(terrariaConfigPath, settings.CustomParameters);
        var terrariaConfigWriteScript = $"""
install -d -m 755 -o gameserver -g gameserver /home/gameserver/lgsm/config-lgsm/terrariaserver /home/gameserver/serverfiles /home/gameserver/.local/share/Terraria/Worlds
cat > {terrariaCommonCfgPath} <<'EOF'
steamuser="{steamUsername}"
steampass="{steamPassword}"
port="{gamePort}"
maxplayers="{maxPlayers}"
worldname="{worldName}"
autocreate="{autoCreate}"
seed="{worldSeed}"
password="{serverPassword}"
motd="{motd}"
servercfg="serverconfig.txt"
servercfgfullpath="{terrariaConfigPath}"
configfile="{terrariaConfigPath}"
startparameters="-config {terrariaConfigPath}"
EOF
cat > {terrariaInstanceCfgPath} <<'EOF'
steamuser="{steamUsername}"
steampass="{steamPassword}"
port="{gamePort}"
maxplayers="{maxPlayers}"
worldname="{worldName}"
autocreate="{autoCreate}"
seed="{worldSeed}"
password="{serverPassword}"
motd="{motd}"
servercfg="serverconfig.txt"
servercfgfullpath="{terrariaConfigPath}"
configfile="{terrariaConfigPath}"
startparameters="-config {terrariaConfigPath}"
EOF
cat > {terrariaConfigPath} <<'EOF'
world=/home/gameserver/.local/share/Terraria/Worlds/{worldFileName}.wld
worldpath=/home/gameserver/.local/share/Terraria/Worlds
worldname={worldName}
autocreate={autoCreate}
seed={worldSeed}
maxplayers={maxPlayers}
password={serverPassword}
port={gamePort}
motd={motd}
secure=1
upnp=0
npcstream=60
priority=1
EOF
cp -f {terrariaConfigPath} {terrariaSelfCfgPath}
sed -i 's/\r$//' {terrariaCommonCfgPath} {terrariaInstanceCfgPath} {terrariaConfigPath} {terrariaSelfCfgPath}
{terrariaCustomScript}
cp -f {terrariaConfigPath} {terrariaSelfCfgPath}
chown -R gameserver:gameserver /home/gameserver/lgsm/config-lgsm || true
chown gameserver:gameserver {terrariaCommonCfgPath} {terrariaInstanceCfgPath} {terrariaConfigPath} {terrariaSelfCfgPath}
""";

        var baseSetupBlock = LinuxGsmScriptBuilder.BuildBaseSetupBlock("Install/update Terraria server", ServerBinary);
        var reinstallScript = LinuxGsmScriptBuilder.BuildForceReinstallBlock(ServerBinary, forceReinstall);
        var systemdBlock = LinuxGsmScriptBuilder.BuildSystemdServiceAndStartBlock(ServerBinary, "TERRARIA");

        var script = $"""
{baseSetupBlock}
{terrariaConfigWriteScript}
{reinstallScript}
chown -R gameserver:gameserver /home/gameserver || true
timeout 300 su - gameserver -c 'cd ~ && ./terrariaserver auto-install' || true
if [ ! -x /home/gameserver/serverfiles/TerrariaServer.bin.x86_64 ]; then
  su - gameserver -c '
set -e
TMP_ZIP="$HOME/terraria-server-1456.zip"
TMP_DIR="$HOME/terraria-official"
rm -rf "$TMP_DIR"
mkdir -p "$TMP_DIR" "$HOME/serverfiles"
wget -qO "$TMP_ZIP" "https://terraria.org/api/download/pc-dedicated-server/terraria-server-1456.zip"
unzip -oq "$TMP_ZIP" -d "$TMP_DIR"
LINUX_DIR="$(find "$TMP_DIR" -type d -name Linux | head -n1)"
if [ -z "$LINUX_DIR" ]; then
  echo "Official Terraria Linux folder not found in downloaded archive."
  exit 1
fi
cp -a "$LINUX_DIR"/. "$HOME/serverfiles"/
  [ -f "$HOME/serverfiles/TerrariaServer.bin.x86_64" ] && chmod +x "$HOME/serverfiles/TerrariaServer.bin.x86_64" || true
  [ -f "$HOME/serverfiles/TerrariaServer.bin.x86" ] && chmod +x "$HOME/serverfiles/TerrariaServer.bin.x86" || true
rm -rf "$TMP_DIR" "$TMP_ZIP"
'
fi
{terrariaConfigWriteScript}
if [ ! -x /home/gameserver/serverfiles/TerrariaServer.bin.x86_64 ]; then
  echo "Terraria executable is missing after SteamCMD and official ZIP fallback."
  exit 1
fi
chown -R gameserver:gameserver /home/gameserver/.local /home/gameserver/serverfiles /home/gameserver/lgsm || true
{systemdBlock}
""";

        await ProxmoxAutomationClient.RunGuestScriptAsync(
            instance.VmId,
            GameServerConnectorSshOps.NormalizeShellCommand(script),
            TimeSpan.FromMinutes(40),
            cancellationToken);
    }

    protected override string BuildRuntimeStatusScript(VirtualGameInstance instance, int gamePort)
    {
        var portProbe = gamePort > 0
            ? $$"""
               GAME_LISTENING="no"
               if ss -lunH 2>/dev/null | awk '{print $5}' | sed -E 's/.*:([0-9]+)$/\1/' | grep -qx '{{gamePort}}'; then
                 GAME_LISTENING="yes"
               elif ss -ltnH 2>/dev/null | awk '{print $4}' | sed -E 's/.*:([0-9]+)$/\1/' | grep -qx '{{gamePort}}'; then
                 GAME_LISTENING="yes"
               fi
               echo "PLAYMOX_GAME_PORT={{gamePort}}"
               echo "PLAYMOX_GAME_PROTOCOL=udp,tcp"
               echo "PLAYMOX_GAME_PORT_LISTENING=$GAME_LISTENING"
               """
            : string.Empty;

        return $$"""
set -eu
SERVICE_STATE="$(systemctl is-active playmox-game.service 2>/dev/null || true)"
echo "PLAYMOX_SERVICE_STATE=$SERVICE_STATE"
{{portProbe}}
""";
    }

    public override async Task<GameServerConfigurationSnapshot> GetConfigurationSnapshotAsync(
        VirtualGameInstance instance,
        string? storedConfigurationJson,
        bool refreshFromServer,
        CancellationToken cancellationToken)
    {
        if (!refreshFromServer)
        {
            return new GameServerConfigurationSnapshot
            {
                InstanceId = instance.Id,
                Source = "db",
                ConfigurationJson = GameServerConnectorSshOps.NormalizeConfigurationJson(storedConfigurationJson)
            };
        }

        var storedSettings = ParseTerrariaSettings(storedConfigurationJson);
        const string script = """
set -eu
CFG="/home/gameserver/lgsm/config-lgsm/terrariaserver/common.cfg"
SERVER_CFG="/home/gameserver/serverfiles/serverconfig.txt"
read_cfg() {
  local key="$1"
  local file="$2"
  local line
  line="$(grep -E "^${key}=" "$file" 2>/dev/null | tail -n1 || true)"
  line="${line#*=}"
  line="${line%$'\r'}"
  line="${line%\"}"
  line="${line#\"}"
  line="${line%\'}"
  line="${line#\'}"
  printf '%s' "$line"
}
pick_cfg() {
  local key="$1"
  local value
  value="$(read_cfg "$key" "$SERVER_CFG")"
  if [ -z "$value" ]; then
    value="$(read_cfg "$key" "$CFG")"
  fi
  printf '%s' "$value"
}
echo "PLAYMOX_WORLD_NAME=$(pick_cfg worldname)"
echo "PLAYMOX_WORLD_SEED=$(pick_cfg seed)"
echo "PLAYMOX_AUTO_CREATE=$(pick_cfg autocreate)"
echo "PLAYMOX_MAX_PLAYERS=$(pick_cfg maxplayers)"
echo "PLAYMOX_PASSWORD=$(pick_cfg password)"
echo "PLAYMOX_MOTD=$(pick_cfg motd)"
""";
        var result = await ProxmoxAutomationClient.RunGuestScriptForOutputAsync(
            instance.VmId,
            script,
            TimeSpan.FromMinutes(2),
            cancellationToken);
        if (result.ExitCode != 0)
        {
            return new GameServerConfigurationSnapshot
            {
                InstanceId = instance.Id,
                Source = "db-fallback",
                ConfigurationJson = GameServerConnectorSshOps.NormalizeConfigurationJson(storedConfigurationJson)
            };
        }

        var values = GameServerConnectorSshOps.ParseScriptOutputValues(result.StdOut);
        values.TryGetValue("PLAYMOX_WORLD_NAME", out var worldName);
        values.TryGetValue("PLAYMOX_WORLD_SEED", out var worldSeed);
        values.TryGetValue("PLAYMOX_AUTO_CREATE", out var autoCreateRaw);
        values.TryGetValue("PLAYMOX_MAX_PLAYERS", out var maxPlayersRaw);
        values.TryGetValue("PLAYMOX_PASSWORD", out var password);
        values.TryGetValue("PLAYMOX_MOTD", out var motd);
        var payload = new
        {
            worldName = worldName ?? "PlaymoxWorld",
            worldSeed = worldSeed ?? string.Empty,
            autoCreateSize = int.TryParse(autoCreateRaw, out var autoCreate) ? autoCreate : 2,
            maxPlayers = int.TryParse(maxPlayersRaw, out var maxPlayers) ? maxPlayers : 8,
            password = password ?? string.Empty,
            motd = motd ?? "Playmox Terraria",
            customParameters = storedSettings.CustomParameters
        };

        return new GameServerConfigurationSnapshot
        {
            InstanceId = instance.Id,
            Source = "live",
            ConfigurationJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true })
        };
    }

    protected override string BuildBackupCreateScript(string backupPath) => $"""
set -e
cd /home/gameserver
set --
for item in \
  ".local/share/Terraria/Worlds" \
  "lgsm/config-lgsm/terrariaserver" \
  "serverfiles/serverconfig.txt" \
  "serverfiles/terrariaserver.txt"; do
  if [ -e "$item" ]; then
    set -- "$@" "$item"
  fi
done
if [ "$#" -eq 0 ]; then
  echo "No Terraria backup data (world/config) found."
  exit 1
fi
tar -czf "{backupPath}" -C /home/gameserver "$@"
ls -l '{backupPath}'
""";

    protected override string BuildBackupRestoreScript(VirtualGameInstance instance, string tempArchivePath)
    {
        var gamePort = instance.GamePublicPortStart ?? instance.GameDefinition?.DefaultPort ?? 7777;
        const string terrariaConfigPath = "/home/gameserver/serverfiles/serverconfig.txt";

        return $"""
set -e
TMP_TAR='{tempArchivePath}'
CFG_DIR='/home/gameserver/lgsm/config-lgsm/terrariaserver'
CFG_FILE='/home/gameserver/serverfiles/serverconfig.txt'
SERVER_CFG='/home/gameserver/lgsm/config-lgsm/terrariaserver/terrariaserver.cfg'
COMMON_CFG='/home/gameserver/lgsm/config-lgsm/terrariaserver/common.cfg'
if [ "$(id -u)" -eq 0 ]; then
  systemctl stop playmox-game.service || true
  su - gameserver -c 'cd ~ && ./terrariaserver stop || true'
  su - gameserver -c 'rm -rf ~/lgsm/config-lgsm/terrariaserver ~/.local/share/Terraria/Worlds'
  su - gameserver -c 'rm -f ~/serverfiles/serverconfig.txt ~/serverfiles/terrariaserver.txt'
  su - gameserver -c 'mkdir -p ~/serverfiles ~/lgsm/config-lgsm/terrariaserver ~/.local/share/Terraria/Worlds'
  if file "$TMP_TAR" | grep -qi 'zstd'; then
    su - gameserver -c "tar --zstd -xf '$TMP_TAR' -C /home/gameserver"
  elif file "$TMP_TAR" | grep -qi 'gzip'; then
    su - gameserver -c "tar -xzf '$TMP_TAR' -C /home/gameserver"
  else
    su - gameserver -c "tar -xf '$TMP_TAR' -C /home/gameserver"
  fi
  chown -R gameserver:gameserver /home/gameserver/serverfiles /home/gameserver/lgsm /home/gameserver/.local || true
  for cfg in "$COMMON_CFG" "$SERVER_CFG"; do
    touch "$cfg"
    if grep -q '^configfile=' "$cfg"; then sed -i 's|^configfile=.*|configfile="{terrariaConfigPath}"|' "$cfg"; else echo 'configfile="{terrariaConfigPath}"' >> "$cfg"; fi
    if grep -q '^startparameters=' "$cfg"; then sed -i 's|^startparameters=.*|startparameters="-config {terrariaConfigPath}"|' "$cfg"; else echo 'startparameters="-config {terrariaConfigPath}"' >> "$cfg"; fi
    if grep -q '^port=' "$cfg"; then sed -i 's|^port=.*|port="{gamePort}"|' "$cfg"; else echo 'port="{gamePort}"' >> "$cfg"; fi
  done
  touch "$CFG_FILE"
  if grep -q '^port=' "$CFG_FILE"; then sed -i 's|^port=.*|port={gamePort}|' "$CFG_FILE"; else echo 'port={gamePort}' >> "$CFG_FILE"; fi
  cp -f "$CFG_FILE" /home/gameserver/serverfiles/terrariaserver.txt
  systemctl restart playmox-game.service || su - gameserver -c 'cd ~ && ./terrariaserver restart'
else
  sudo -n /bin/systemctl stop playmox-game.service 2>/dev/null || true
  cd ~ && ./terrariaserver stop || true
  rm -rf ~/lgsm/config-lgsm/terrariaserver ~/.local/share/Terraria/Worlds
  rm -f ~/serverfiles/serverconfig.txt ~/serverfiles/terrariaserver.txt
  mkdir -p ~/serverfiles ~/lgsm/config-lgsm/terrariaserver ~/.local/share/Terraria/Worlds
  if file "$TMP_TAR" | grep -qi 'zstd'; then
    tar --zstd -xf "$TMP_TAR" -C /home/gameserver
  elif file "$TMP_TAR" | grep -qi 'gzip'; then
    tar -xzf "$TMP_TAR" -C /home/gameserver
  else
    tar -xf "$TMP_TAR" -C /home/gameserver
  fi
  for cfg in "$COMMON_CFG" "$SERVER_CFG"; do
    touch "$cfg"
    if grep -q '^configfile=' "$cfg"; then sed -i 's|^configfile=.*|configfile="{terrariaConfigPath}"|' "$cfg"; else echo 'configfile="{terrariaConfigPath}"' >> "$cfg"; fi
    if grep -q '^startparameters=' "$cfg"; then sed -i 's|^startparameters=.*|startparameters="-config {terrariaConfigPath}"|' "$cfg"; else echo 'startparameters="-config {terrariaConfigPath}"' >> "$cfg"; fi
    if grep -q '^port=' "$cfg"; then sed -i 's|^port=.*|port="{gamePort}"|' "$cfg"; else echo 'port="{gamePort}"' >> "$cfg"; fi
  done
  touch "$CFG_FILE"
  if grep -q '^port=' "$CFG_FILE"; then sed -i 's|^port=.*|port={gamePort}|' "$CFG_FILE"; else echo 'port={gamePort}' >> "$CFG_FILE"; fi
  cp -f "$CFG_FILE" /home/gameserver/serverfiles/terrariaserver.txt
  sudo -n /bin/systemctl restart playmox-game.service 2>/dev/null || (cd ~ && ./terrariaserver restart)
fi
rm -f "$TMP_TAR"
""";
    }

    private static string BuildTerrariaWorldFileName(string worldName)
    {
        var cleaned = Regex.Replace(worldName, @"[^A-Za-z0-9 _-]", string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            cleaned = "PlaymoxWorld";
        }

        return cleaned.Replace(' ', '_');
    }

    private static TerrariaProvisioningSettings ParseTerrariaSettings(string? configurationJson)
    {
        if (string.IsNullOrWhiteSpace(configurationJson))
        {
            return new TerrariaProvisioningSettings();
        }

        try
        {
            using var json = JsonDocument.Parse(configurationJson);
            var root = json.RootElement;
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("user", out var userConfig) &&
                userConfig.ValueKind == JsonValueKind.Object)
            {
                root = userConfig;
            }

            var result = new TerrariaProvisioningSettings();
            if (root.TryGetProperty("worldName", out var worldNameProp) && worldNameProp.ValueKind == JsonValueKind.String)
            {
                result.WorldName = worldNameProp.GetString();
            }
            if (root.TryGetProperty("worldSeed", out var worldSeedProp) && worldSeedProp.ValueKind == JsonValueKind.String)
            {
                result.WorldSeed = worldSeedProp.GetString();
            }
            if (root.TryGetProperty("autoCreateSize", out var autoCreateProp) && autoCreateProp.TryGetInt32(out var autoCreate))
            {
                result.AutoCreateSize = autoCreate;
            }
            if (root.TryGetProperty("maxPlayers", out var maxPlayersProp) && maxPlayersProp.TryGetInt32(out var maxPlayers))
            {
                result.MaxPlayers = maxPlayers;
            }
            if (root.TryGetProperty("password", out var passwordProp) && passwordProp.ValueKind == JsonValueKind.String)
            {
                result.Password = passwordProp.GetString();
            }
            if (root.TryGetProperty("motd", out var motdProp) && motdProp.ValueKind == JsonValueKind.String)
            {
                result.Motd = motdProp.GetString();
            }
            result.CustomParameters = GameServerCustomParameters.ParseFromConfigRoot(root);

            result.AutoCreateSize = result.AutoCreateSize is >= 1 and <= 3 ? result.AutoCreateSize : 2;
            result.MaxPlayers = result.MaxPlayers is >= 1 and <= 255 ? result.MaxPlayers : 8;
            return result;
        }
        catch
        {
            return new TerrariaProvisioningSettings();
        }
    }

    private sealed class TerrariaProvisioningSettings
    {
        public string? WorldName { get; set; } = "PlaymoxWorld";
        public string? WorldSeed { get; set; } = string.Empty;
        public int? AutoCreateSize { get; set; } = 2;
        public int? MaxPlayers { get; set; } = 8;
        public string? Password { get; set; } = string.Empty;
        public string? Motd { get; set; } = "Playmox Terraria";
        public Dictionary<string, string> CustomParameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
