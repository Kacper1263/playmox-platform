using System.Text.Json;
using Microsoft.Extensions.Logging;
using PlaymoxAPI.BLL.BO.Platform;
using PlaymoxAPI.Services.Proxmox;

namespace PlaymoxAPI.Services.Platform;

public sealed class UnturnedGameServerConnector : LinuxGsmConnectorBase
{
    public UnturnedGameServerConnector(
        ProxmoxAutomationClient proxmoxAutomationClient,
        ILogger<UnturnedGameServerConnector> logger)
        : base(proxmoxAutomationClient, logger)
    {
    }

    public override GameServerKind Kind => GameServerKind.Unturned;
    protected override string ServerBinary => "untserver";
    protected override string BackupSlug => "unturned";
    protected override string GameDisplayName => "Unturned";

    public override async Task InstallOrUpdateAsync(
        VirtualGameInstance instance,
        string? configurationJson,
        GameDefinition gameDefinition,
        bool forceReinstall,
        CancellationToken cancellationToken)
    {
        var settings = ParseUnturnedSettings(configurationJson);
        var gamePort = instance.GamePublicPortStart ?? gameDefinition.DefaultPort ?? 27015;
        var queryPort = gamePort + 1;
        var steam = GameServerConnectorSshOps.ExtractSteamAccess(instance.ConfigurationJson);
        var useAnonymousSteam = string.IsNullOrWhiteSpace(steam.Username) ||
                                string.Equals(steam.Username, "anonymous", StringComparison.OrdinalIgnoreCase);
        if (!useAnonymousSteam && string.IsNullOrWhiteSpace(steam.Password))
        {
            throw new InvalidOperationException("SteamCMD password is missing. Configure full Steam login or use anonymous mode.");
        }

        var steamUsername = GameServerConnectorSshOps.EscapeForDoubleQuotedConfig(useAnonymousSteam ? "anonymous" : steam.Username!);
        var steamPassword = GameServerConnectorSshOps.EscapeForDoubleQuotedConfig(useAnonymousSteam ? string.Empty : steam.Password!);
        var serverName = GameServerConnectorSshOps.EscapeForDoubleQuotedConfig(settings.ServerName ?? "Playmox Unturned");
        var map = NormalizeUnturnedMap(settings.Map) ?? "PEI";
        var mode = NormalizeUnturnedMode(settings.Mode) ?? "normal";
        var maxPlayers = settings.MaxPlayers is >= 1 and <= 64 ? settings.MaxPlayers.Value : 24;
        var password = GameServerConnectorSshOps.EscapeForDoubleQuotedConfig(settings.Password ?? string.Empty);
        var pvp = settings.Pvp ?? true;
        var pvpLine = pvp ? "PVP" : "PVE";
        var perspective = NormalizeUnturnedPerspective(settings.Perspective) ?? "first";
        var cheatsLine = (settings.Cheats ?? false) ? "Cheats" : string.Empty;
        var welcomeMessage = GameServerConnectorSshOps.EscapeForDoubleQuotedConfig(settings.WelcomeMessage ?? "Welcome to Playmox Unturned server");
        var mapEscaped = GameServerConnectorSshOps.EscapeForDoubleQuotedConfig(map);

        const string commonCfg = "/home/gameserver/lgsm/config-lgsm/untserver/common.cfg";
        const string instanceCfg = "/home/gameserver/lgsm/config-lgsm/untserver/untserver.cfg";
        const string commandsDat = "/home/gameserver/serverfiles/Servers/untserver/Server/Commands.dat";
        const string untConfigJson = "/home/gameserver/serverfiles/Servers/untserver/Config.json";
        var customCommandsScript = GameServerCustomParameters.BuildSpaceSeparatedUpsertScript(commandsDat, settings.CustomParameters);
        var cfgWriteScript = $"""
install -d -m 755 -o gameserver -g gameserver /home/gameserver/lgsm/config-lgsm/untserver /home/gameserver/serverfiles/Servers/untserver/Server
cat > {commonCfg} <<'EOF'
steamuser="{steamUsername}"
steampass="{steamPassword}"
servername="{serverName}"
port="{gamePort}"
queryport="{queryPort}"
maxplayers="{maxPlayers}"
defaultmap="{mapEscaped}"
gamemode="{mode}"
startparameters="-nographics -batchmode -bind 0.0.0.0 -port {gamePort} -maxplayers {maxPlayers} -perspective {perspective} -mode {mode} -name \"{serverName}\" -map {mapEscaped} -gslt $gslt +InternetServer/untserver"
EOF
cat > {instanceCfg} <<'EOF'
steamuser="{steamUsername}"
steampass="{steamPassword}"
servername="{serverName}"
port="{gamePort}"
queryport="{queryPort}"
maxplayers="{maxPlayers}"
defaultmap="{mapEscaped}"
gamemode="{mode}"
startparameters="-nographics -batchmode -bind 0.0.0.0 -port {gamePort} -maxplayers {maxPlayers} -perspective {perspective} -mode {mode} -name \"{serverName}\" -map {mapEscaped} -gslt $gslt +InternetServer/untserver"
EOF
cat > {commandsDat} <<'EOF'
Name {serverName}
Password {password}
MaxPlayers {maxPlayers}
Map {mapEscaped}
Mode {mode}
Perspective {perspective}
Welcome {welcomeMessage}
{pvpLine}
{cheatsLine}
EOF
cat > {untConfigJson} <<'EOF'
{"{"}
  "Browser": {"{"}
    "Name": "{serverName}",
    "Map": "{mapEscaped}",
    "Password": "{password}",
    "IsPvP": {(pvp ? "true" : "false")}
  {"}"}
{"}"}
EOF
{customCommandsScript}
chown -R gameserver:gameserver /home/gameserver/lgsm /home/gameserver/serverfiles || true
""";

        var baseSetupBlock = LinuxGsmScriptBuilder.BuildBaseSetupBlock("Install/update Unturned server", ServerBinary);
        var reinstallScript = LinuxGsmScriptBuilder.BuildForceReinstallBlock(ServerBinary, forceReinstall);
        var systemdBlock = LinuxGsmScriptBuilder.BuildSystemdServiceAndStartBlock(ServerBinary, "UNTURNED");

        var script = $"""
{baseSetupBlock}
{cfgWriteScript}
{reinstallScript}
chown -R gameserver:gameserver /home/gameserver || true
su - gameserver -c 'cd ~ && ./untserver auto-install'
{cfgWriteScript}
{systemdBlock}
""";

        await ProxmoxAutomationClient.RunGuestScriptAsync(
            instance.VmId,
            GameServerConnectorSshOps.NormalizeShellCommand(script),
            TimeSpan.FromMinutes(45),
            cancellationToken);
    }

    protected override string BuildRuntimeStatusScript(VirtualGameInstance instance, int gamePort)
    {
        var queryPort = gamePort > 0 ? gamePort + 1 : 0;
        return $$"""
set -eu
SERVICE_STATE="$(systemctl is-active playmox-game.service 2>/dev/null || true)"
echo "PLAYMOX_SERVICE_STATE=$SERVICE_STATE"
if [ {{gamePort}} -gt 0 ]; then
  GAME_LISTENING="no"
  if ss -lunH "sport = :{{gamePort}}" 2>/dev/null | grep -q .; then
    GAME_LISTENING="yes"
  elif ss -ltnH "sport = :{{gamePort}}" 2>/dev/null | grep -q .; then
    GAME_LISTENING="yes"
  elif pgrep -f 'Unturned_Headless' >/dev/null 2>&1; then
    GAME_LISTENING="yes"
  fi
  QUERY_LISTENING="no"
  if ss -lunH "sport = :{{queryPort}}" 2>/dev/null | grep -q .; then
    QUERY_LISTENING="yes"
  elif ss -ltnH "sport = :{{queryPort}}" 2>/dev/null | grep -q .; then
    QUERY_LISTENING="yes"
  fi
  SERVER_CODE="$(grep -Eo 'Server Code: [0-9]+' /home/gameserver/log/console/untserver-console.log 2>/dev/null | tail -n1 | awk '{print $3}' || true)"
  echo "PLAYMOX_GAME_PORT={{gamePort}}"
  echo "PLAYMOX_GAME_PORT_LISTENING=$GAME_LISTENING"
  echo "PLAYMOX_QUERY_PORT={{queryPort}}"
  echo "PLAYMOX_QUERY_PORT_LISTENING=$QUERY_LISTENING"
  echo "PLAYMOX_SERVER_CODE=$SERVER_CODE"
fi
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

        var storedSettings = ParseUnturnedSettings(storedConfigurationJson);
        const string script = """
set -eu
CFG="/home/gameserver/lgsm/config-lgsm/untserver/common.cfg"
read_cfg() {
  local key="$1"
  grep -E "^${key}=" "$CFG" 2>/dev/null | tail -n1 | cut -d'=' -f2- | tr -d '"' || true
}
echo "PLAYMOX_SERVER_NAME=$(grep -E '^Name ' /home/gameserver/serverfiles/Servers/untserver/Server/Commands.dat 2>/dev/null | head -n1 | cut -d' ' -f2- || true)"
echo "PLAYMOX_PASSWORD=$(grep -E '^Password ' /home/gameserver/serverfiles/Servers/untserver/Server/Commands.dat 2>/dev/null | head -n1 | cut -d' ' -f2- || true)"
echo "PLAYMOX_MAX_PLAYERS=$(grep -E '^MaxPlayers ' /home/gameserver/serverfiles/Servers/untserver/Server/Commands.dat 2>/dev/null | head -n1 | awk '{print $2}' || true)"
echo "PLAYMOX_MAP=$(grep -E '^Map ' /home/gameserver/serverfiles/Servers/untserver/Server/Commands.dat 2>/dev/null | head -n1 | awk '{print $2}' || true)"
echo "PLAYMOX_PERSPECTIVE=$(grep -E '^Perspective ' /home/gameserver/serverfiles/Servers/untserver/Server/Commands.dat 2>/dev/null | head -n1 | awk '{print $2}' || true)"
if grep -Eq '^Cheats($|[[:space:]])' /home/gameserver/serverfiles/Servers/untserver/Server/Commands.dat 2>/dev/null; then
  echo "PLAYMOX_CHEATS=true"
else
  echo "PLAYMOX_CHEATS=false"
fi
echo "PLAYMOX_WELCOME_MESSAGE=$(grep -E '^Welcome ' /home/gameserver/serverfiles/Servers/untserver/Server/Commands.dat 2>/dev/null | head -n1 | cut -d' ' -f2- || true)"
if grep -Eq '^PVE$' /home/gameserver/serverfiles/Servers/untserver/Server/Commands.dat 2>/dev/null; then
  echo "PLAYMOX_PVP=false"
else
  echo "PLAYMOX_PVP=true"
fi
MODE="$(read_cfg gamemode)"
if [ -z "$MODE" ]; then
  MODE="$(read_cfg mode)"
fi
echo "PLAYMOX_MODE=$MODE"
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
        values.TryGetValue("PLAYMOX_SERVER_NAME", out var serverName);
        values.TryGetValue("PLAYMOX_PASSWORD", out var password);
        values.TryGetValue("PLAYMOX_MAX_PLAYERS", out var maxPlayersRaw);
        values.TryGetValue("PLAYMOX_MAP", out var map);
        values.TryGetValue("PLAYMOX_PERSPECTIVE", out var perspective);
        values.TryGetValue("PLAYMOX_CHEATS", out var cheatsRaw);
        values.TryGetValue("PLAYMOX_WELCOME_MESSAGE", out var welcomeMessage);
        values.TryGetValue("PLAYMOX_MODE", out var mode);
        values.TryGetValue("PLAYMOX_PVP", out var pvpRaw);
        var payload = new
        {
            serverName = serverName ?? "Playmox Unturned",
            password = password ?? string.Empty,
            maxPlayers = int.TryParse(maxPlayersRaw, out var maxPlayers) ? maxPlayers : 24,
            map = NormalizeUnturnedMap(map) ?? "PEI",
            mode = NormalizeUnturnedMode(mode) ?? "normal",
            pvp = GameServerConnectorSshOps.ParseBoolOrDefault(pvpRaw, true),
            perspective = NormalizeUnturnedPerspective(perspective) ?? "first",
            cheats = GameServerConnectorSshOps.ParseBoolOrDefault(cheatsRaw, false),
            welcomeMessage = welcomeMessage ?? "Welcome to Playmox Unturned server",
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
  "serverfiles/Servers/untserver" \
  "serverfiles/Servers/untserver/Server/Commands.dat" \
  "serverfiles/Servers/untserver/Config.json" \
  "lgsm/config-lgsm/untserver"; do
  if [ -e "$item" ]; then
    set -- "$@" "$item"
  fi
done
if [ "$#" -eq 0 ]; then
  echo "No Unturned backup data (world/config) found."
  exit 1
fi
tar -czf "{backupPath}" -C /home/gameserver "$@"
ls -l "{backupPath}"
""";

    protected override string BuildBackupRestoreScript(VirtualGameInstance instance, string tempArchivePath)
    {
        var gamePort = instance.GamePublicPortStart ?? instance.GameDefinition?.DefaultPort ?? 27015;
        var queryPort = gamePort + 1;

        return $"""
set -e
TMP_TAR='{tempArchivePath}'
if [ "$(id -u)" -eq 0 ]; then
  systemctl stop playmox-game.service || true
  su - gameserver -c 'cd ~ && ./untserver stop || true'
  su - gameserver -c 'rm -rf ~/serverfiles/Servers/untserver ~/lgsm/config-lgsm/untserver'
  su - gameserver -c 'mkdir -p ~/serverfiles/Servers/untserver/Server ~/lgsm/config-lgsm/untserver'
  if file "$TMP_TAR" | grep -qi 'zstd'; then
    su - gameserver -c "tar --zstd -xf '$TMP_TAR' -C /home/gameserver"
  elif file "$TMP_TAR" | grep -qi 'gzip'; then
    su - gameserver -c "tar -xzf '$TMP_TAR' -C /home/gameserver"
  else
    su - gameserver -c "tar -xf '$TMP_TAR' -C /home/gameserver"
  fi
  chown -R gameserver:gameserver /home/gameserver/serverfiles /home/gameserver/lgsm || true
  su - gameserver -c "for cfg in ~/lgsm/config-lgsm/untserver/common.cfg ~/lgsm/config-lgsm/untserver/untserver.cfg; do touch \"$cfg\"; if grep -q '^port=' \"$cfg\"; then sed -i 's/^port=.*/port=\"{gamePort}\"/' \"$cfg\"; else echo 'port=\"{gamePort}\"' >> \"$cfg\"; fi; if grep -q '^queryport=' \"$cfg\"; then sed -i 's/^queryport=.*/queryport=\"{queryPort}\"/' \"$cfg\"; else echo 'queryport=\"{queryPort}\"' >> \"$cfg\"; fi; done"
  systemctl restart playmox-game.service || su - gameserver -c 'cd ~ && ./untserver restart'
else
  sudo -n /bin/systemctl stop playmox-game.service 2>/dev/null || true
  cd ~ && ./untserver stop || true
  rm -rf ~/serverfiles/Servers/untserver ~/lgsm/config-lgsm/untserver
  mkdir -p ~/serverfiles/Servers/untserver/Server ~/lgsm/config-lgsm/untserver
  if file "$TMP_TAR" | grep -qi 'zstd'; then
    tar --zstd -xf "$TMP_TAR" -C /home/gameserver
  elif file "$TMP_TAR" | grep -qi 'gzip'; then
    tar -xzf "$TMP_TAR" -C /home/gameserver
  else
    tar -xf "$TMP_TAR" -C /home/gameserver
  fi
  for cfg in ~/lgsm/config-lgsm/untserver/common.cfg ~/lgsm/config-lgsm/untserver/untserver.cfg; do touch "$cfg"; if grep -q '^port=' "$cfg"; then sed -i 's/^port=.*/port="{gamePort}"/' "$cfg"; else echo 'port="{gamePort}"' >> "$cfg"; fi; if grep -q '^queryport=' "$cfg"; then sed -i 's/^queryport=.*/queryport="{queryPort}"/' "$cfg"; else echo 'queryport="{queryPort}"' >> "$cfg"; fi; done
  sudo -n /bin/systemctl restart playmox-game.service 2>/dev/null || (cd ~ && ./untserver restart)
fi
rm -f "$TMP_TAR"
""";
    }

    private static string? NormalizeUnturnedMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "easy" => "easy",
            "normal" => "normal",
            "hard" => "hard",
            _ => null
        };
    }

    private static string? NormalizeUnturnedMap(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "pei" => "PEI",
            "washington" => "Washington",
            "yukon" => "Yukon",
            "russia" => "Russia",
            "germany" => "Germany",
            "hawaii" => "Hawaii",
            _ => null
        };
    }

    private static string? NormalizeUnturnedPerspective(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "both" => "both",
            "first" => "first",
            "third" => "third",
            "vehicle" => "vehicle",
            _ => null
        };
    }

    private static UnturnedProvisioningSettings ParseUnturnedSettings(string? configurationJson)
    {
        if (string.IsNullOrWhiteSpace(configurationJson))
        {
            return new UnturnedProvisioningSettings();
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

            var result = new UnturnedProvisioningSettings();
            if (root.TryGetProperty("serverName", out var serverNameProp) && serverNameProp.ValueKind == JsonValueKind.String)
            {
                result.ServerName = serverNameProp.GetString();
            }
            if (root.TryGetProperty("map", out var mapProp) && mapProp.ValueKind == JsonValueKind.String)
            {
                result.Map = mapProp.GetString();
            }
            if (root.TryGetProperty("mode", out var modeProp) && modeProp.ValueKind == JsonValueKind.String)
            {
                result.Mode = modeProp.GetString();
            }
            if (root.TryGetProperty("maxPlayers", out var maxPlayersProp) && maxPlayersProp.TryGetInt32(out var maxPlayers))
            {
                result.MaxPlayers = maxPlayers;
            }
            if (root.TryGetProperty("password", out var passwordProp) && passwordProp.ValueKind == JsonValueKind.String)
            {
                result.Password = passwordProp.GetString();
            }
            if (root.TryGetProperty("perspective", out var perspectiveProp) && perspectiveProp.ValueKind == JsonValueKind.String)
            {
                result.Perspective = perspectiveProp.GetString();
            }
            if (root.TryGetProperty("welcomeMessage", out var welcomeMessageProp) && welcomeMessageProp.ValueKind == JsonValueKind.String)
            {
                result.WelcomeMessage = welcomeMessageProp.GetString();
            }
            if (root.TryGetProperty("pvp", out var pvpProp))
            {
                if (pvpProp.ValueKind == JsonValueKind.True)
                {
                    result.Pvp = true;
                }
                else if (pvpProp.ValueKind == JsonValueKind.False)
                {
                    result.Pvp = false;
                }
            }
            if (root.TryGetProperty("cheats", out var cheatsProp))
            {
                if (cheatsProp.ValueKind == JsonValueKind.True)
                {
                    result.Cheats = true;
                }
                else if (cheatsProp.ValueKind == JsonValueKind.False)
                {
                    result.Cheats = false;
                }
            }
            result.CustomParameters = GameServerCustomParameters.ParseFromConfigRoot(root);

            return result;
        }
        catch
        {
            return new UnturnedProvisioningSettings();
        }
    }

    private sealed class UnturnedProvisioningSettings
    {
        public string? ServerName { get; set; } = "Playmox Unturned";
        public string? Map { get; set; } = "PEI";
        public string? Mode { get; set; } = "normal";
        public int? MaxPlayers { get; set; } = 24;
        public string? Password { get; set; } = string.Empty;
        public bool? Pvp { get; set; } = true;
        public string? Perspective { get; set; } = "first";
        public bool? Cheats { get; set; } = false;
        public string? WelcomeMessage { get; set; } = "Welcome to Playmox Unturned server";
        public Dictionary<string, string> CustomParameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
