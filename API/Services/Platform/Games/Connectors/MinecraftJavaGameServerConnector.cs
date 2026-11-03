using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PlaymoxAPI.BLL.BO.Platform;
using PlaymoxAPI.Services.Proxmox;

namespace PlaymoxAPI.Services.Platform;

public sealed class MinecraftJavaGameServerConnector : LinuxGsmConnectorBase
{
    public MinecraftJavaGameServerConnector(
        ProxmoxAutomationClient proxmoxAutomationClient,
        ILogger<MinecraftJavaGameServerConnector> logger)
        : base(proxmoxAutomationClient, logger)
    {
    }

    public override GameServerKind Kind => GameServerKind.MinecraftJava;
    protected override string ServerBinary => "mcserver";
    protected override string BackupSlug => "minecraft";
    protected override string GameDisplayName => "Minecraft";

    public override async Task InstallOrUpdateAsync(
        VirtualGameInstance instance,
        string? configurationJson,
        GameDefinition gameDefinition,
        bool forceReinstall,
        CancellationToken cancellationToken)
    {
        var settings = ParseMinecraftSettings(configurationJson);
        var gamePort = instance.GamePublicPortStart ?? gameDefinition.DefaultPort ?? 25565;
        var motdUpsertScript = GameServerCustomParameters.BuildIniUpsertScript(
            "/home/gameserver/serverfiles/server.properties",
            new Dictionary<string, string>
            {
                ["motd"] = settings.Motd ?? "Playmox Minecraft"
            });
        var maxPlayers = settings.MaxPlayers ?? 20;
        var onlineMode = settings.OnlineMode ?? true;
        var onlineModeValue = onlineMode ? "true" : "false";
        var difficulty = NormalizeMinecraftDifficulty(settings.Difficulty) ?? "easy";
        var gameMode = NormalizeMinecraftGamemode(settings.GameMode) ?? "survival";
        var pvpValue = (settings.Pvp ?? true) ? "true" : "false";
        var allowFlightValue = (settings.AllowFlight ?? false) ? "true" : "false";
        var viewDistance = settings.ViewDistance is >= 3 and <= 32 ? settings.ViewDistance.Value : 10;
        var simulationDistance = settings.SimulationDistance is >= 3 and <= 32 ? settings.SimulationDistance.Value : 10;
        var spawnProtection = settings.SpawnProtection is >= 0 and <= 128 ? settings.SpawnProtection.Value : 16;
        var enableCommandBlockValue = (settings.EnableCommandBlock ?? false) ? "true" : "false";
        var whiteListValue = (settings.WhiteList ?? false) ? "true" : "false";
        var enforceWhitelistValue = (settings.EnforceWhitelist ?? false) ? "true" : "false";
        var allowNetherValue = (settings.AllowNether ?? true) ? "true" : "false";
        var hardcoreValue = (settings.Hardcore ?? false) ? "true" : "false";
        var serverVersion = settings.ServerVersion?.Trim();
        var customPropertiesScript = GameServerCustomParameters.BuildIniUpsertScript(
            "/home/gameserver/serverfiles/server.properties",
            settings.CustomParameters);
        if (!string.IsNullOrWhiteSpace(serverVersion) &&
            !Regex.IsMatch(serverVersion, "^[A-Za-z0-9._-]+$"))
        {
            throw new InvalidOperationException("Minecraft serverVersion contains unsupported characters. Allowed: letters, digits, dot, underscore, dash.");
        }

        var serverVersionEscaped = GameServerConnectorSshOps.EscapeForSingleQuotes(serverVersion ?? string.Empty);
        var xmxMb = CalculateJavaMemoryMb(instance.MemoryMb);

        const string java25SetupBlock = """
if ! java -version 2>&1 | grep -q 'version "25'; then
  if apt-cache show openjdk-25-jre-headless >/dev/null 2>&1; then
    apt-get -o DPkg::Lock::Timeout=300 install -y openjdk-25-jre-headless
  else
    TMP_JAVA_ARCHIVE="/tmp/temurin-jre25.tar.gz"
    wget -qO "$TMP_JAVA_ARCHIVE" "https://api.adoptium.net/v3/binary/latest/25/ga/linux/x64/jre/hotspot/normal/eclipse"
    mkdir -p /opt/java
    tar -xzf "$TMP_JAVA_ARCHIVE" -C /opt/java
    JAVA25_BIN="$(find /opt/java -maxdepth 2 -type f -path '*/bin/java' | sort | tail -n1)"
    if [ -n "$JAVA25_BIN" ]; then
      update-alternatives --install /usr/bin/java java "$JAVA25_BIN" 2525
      update-alternatives --set java "$JAVA25_BIN"
    fi
  fi
fi
java -version
""";

        var baseSetupBlock = LinuxGsmScriptBuilder.BuildBaseSetupBlock(
            "Install/update Minecraft server",
            ServerBinary,
            extraAptPackages: "openjdk-21-jre-headless",
            postAptCommands: java25SetupBlock);
        var reinstallScript = LinuxGsmScriptBuilder.BuildForceReinstallBlock(ServerBinary, forceReinstall);
        var systemdBlock = LinuxGsmScriptBuilder.BuildSystemdServiceAndStartBlock(
            ServerBinary,
            "MC",
            extraDiagnosticsCommands: "java -version 2>&1 || true");

        var script = $"""
{baseSetupBlock}
{reinstallScript}
chown -R gameserver:gameserver /home/gameserver || true
su - gameserver -c 'cd ~ && ./mcserver auto-install'
{(string.IsNullOrWhiteSpace(serverVersion) ? string.Empty : $"su - gameserver -c 'cd ~ && ./mcserver update --branch {serverVersionEscaped}'")}
su - gameserver -c 'mkdir -p ~/serverfiles'
su - gameserver -c 'echo eula=true > ~/serverfiles/eula.txt'
su - gameserver -c 'touch ~/serverfiles/server.properties'
su - gameserver -c 'install -d -m 755 ~/lgsm/config-lgsm/mcserver'
su - gameserver -c "for cfg in ~/lgsm/config-lgsm/mcserver/common.cfg ~/lgsm/config-lgsm/mcserver/mcserver.cfg; do touch \"\$cfg\"; if grep -q '^javaram=' \"\$cfg\"; then sed -i 's/^javaram=.*/javaram=\"{xmxMb}\"/' \"\$cfg\"; else echo 'javaram=\"{xmxMb}\"' >> \"\$cfg\"; fi; if grep -q '^startparameters=' \"\$cfg\"; then sed -i 's|^startparameters=.*|startparameters=\"nogui\"|' \"\$cfg\"; else echo 'startparameters=\"nogui\"' >> \"\$cfg\"; fi; done"
su - gameserver -c "if grep -q '^server-port=' ~/serverfiles/server.properties; then sed -i 's/^server-port=.*/server-port={gamePort}/' ~/serverfiles/server.properties; else echo 'server-port={gamePort}' >> ~/serverfiles/server.properties; fi"
su - gameserver -c "if grep -q '^max-players=' ~/serverfiles/server.properties; then sed -i 's/^max-players=.*/max-players={maxPlayers}/' ~/serverfiles/server.properties; else echo 'max-players={maxPlayers}' >> ~/serverfiles/server.properties; fi"
{motdUpsertScript}
su - gameserver -c "if grep -q '^online-mode=' ~/serverfiles/server.properties; then sed -i 's/^online-mode=.*/online-mode={onlineModeValue}/' ~/serverfiles/server.properties; else echo 'online-mode={onlineModeValue}' >> ~/serverfiles/server.properties; fi"
su - gameserver -c "if grep -q '^difficulty=' ~/serverfiles/server.properties; then sed -i 's/^difficulty=.*/difficulty={difficulty}/' ~/serverfiles/server.properties; else echo 'difficulty={difficulty}' >> ~/serverfiles/server.properties; fi"
su - gameserver -c "if grep -q '^gamemode=' ~/serverfiles/server.properties; then sed -i 's/^gamemode=.*/gamemode={gameMode}/' ~/serverfiles/server.properties; else echo 'gamemode={gameMode}' >> ~/serverfiles/server.properties; fi"
su - gameserver -c "if grep -q '^force-gamemode=' ~/serverfiles/server.properties; then sed -i 's/^force-gamemode=.*/force-gamemode=true/' ~/serverfiles/server.properties; else echo 'force-gamemode=true' >> ~/serverfiles/server.properties; fi"
su - gameserver -c "if grep -q '^pvp=' ~/serverfiles/server.properties; then sed -i 's/^pvp=.*/pvp={pvpValue}/' ~/serverfiles/server.properties; else echo 'pvp={pvpValue}' >> ~/serverfiles/server.properties; fi"
su - gameserver -c "if grep -q '^allow-flight=' ~/serverfiles/server.properties; then sed -i 's/^allow-flight=.*/allow-flight={allowFlightValue}/' ~/serverfiles/server.properties; else echo 'allow-flight={allowFlightValue}' >> ~/serverfiles/server.properties; fi"
su - gameserver -c "if grep -q '^view-distance=' ~/serverfiles/server.properties; then sed -i 's/^view-distance=.*/view-distance={viewDistance}/' ~/serverfiles/server.properties; else echo 'view-distance={viewDistance}' >> ~/serverfiles/server.properties; fi"
su - gameserver -c "if grep -q '^simulation-distance=' ~/serverfiles/server.properties; then sed -i 's/^simulation-distance=.*/simulation-distance={simulationDistance}/' ~/serverfiles/server.properties; else echo 'simulation-distance={simulationDistance}' >> ~/serverfiles/server.properties; fi"
su - gameserver -c "if grep -q '^spawn-protection=' ~/serverfiles/server.properties; then sed -i 's/^spawn-protection=.*/spawn-protection={spawnProtection}/' ~/serverfiles/server.properties; else echo 'spawn-protection={spawnProtection}' >> ~/serverfiles/server.properties; fi"
su - gameserver -c "if grep -q '^enable-command-block=' ~/serverfiles/server.properties; then sed -i 's/^enable-command-block=.*/enable-command-block={enableCommandBlockValue}/' ~/serverfiles/server.properties; else echo 'enable-command-block={enableCommandBlockValue}' >> ~/serverfiles/server.properties; fi"
su - gameserver -c "if grep -q '^white-list=' ~/serverfiles/server.properties; then sed -i 's/^white-list=.*/white-list={whiteListValue}/' ~/serverfiles/server.properties; else echo 'white-list={whiteListValue}' >> ~/serverfiles/server.properties; fi"
su - gameserver -c "if grep -q '^enforce-whitelist=' ~/serverfiles/server.properties; then sed -i 's/^enforce-whitelist=.*/enforce-whitelist={enforceWhitelistValue}/' ~/serverfiles/server.properties; else echo 'enforce-whitelist={enforceWhitelistValue}' >> ~/serverfiles/server.properties; fi"
su - gameserver -c "if grep -q '^allow-nether=' ~/serverfiles/server.properties; then sed -i 's/^allow-nether=.*/allow-nether={allowNetherValue}/' ~/serverfiles/server.properties; else echo 'allow-nether={allowNetherValue}' >> ~/serverfiles/server.properties; fi"
su - gameserver -c "if grep -q '^hardcore=' ~/serverfiles/server.properties; then sed -i 's/^hardcore=.*/hardcore={hardcoreValue}/' ~/serverfiles/server.properties; else echo 'hardcore={hardcoreValue}' >> ~/serverfiles/server.properties; fi"
{customPropertiesScript}
chown -R gameserver:gameserver /home/gameserver || true
chmod -R u+rwX,go+rX /home/gameserver/serverfiles /home/gameserver/lgsm || true
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
            ? $"""
               GAME_LISTENING="no"
               if ss -ltn "( sport = :{gamePort} )" 2>/dev/null | grep -q "{gamePort}"; then
                 GAME_LISTENING="yes"
               fi
               echo "PLAYMOX_GAME_PORT={gamePort}"
               echo "PLAYMOX_GAME_PORT_LISTENING=$GAME_LISTENING"
               """
            : string.Empty;

        return $$"""
set -eu
SERVICE_STATE="$(systemctl is-active playmox-game.service 2>/dev/null || true)"
JAVA_VERSION="$(java -version 2>&1 | head -n 1 || true)"
MC_VERSION="$(grep -Eom1 'Starting minecraft server version [^ ]+' /home/gameserver/serverfiles/logs/latest.log 2>/dev/null | sed 's/Starting minecraft server version //' || true)"
if [ -z "$MC_VERSION" ]; then
  MC_VERSION="$(ls -1 /home/gameserver/serverfiles/versions 2>/dev/null | sort -V | tail -n 1 || true)"
fi
echo "PLAYMOX_SERVICE_STATE=$SERVICE_STATE"
echo "PLAYMOX_JAVA_VERSION=$JAVA_VERSION"
echo "PLAYMOX_MC_VERSION=$MC_VERSION"
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

        var storedSettings = ParseMinecraftSettings(storedConfigurationJson);
        const string script = """
set -eu
PROP_FILE="/home/gameserver/serverfiles/server.properties"
read_prop() {
  local key="$1"
  grep -E "^${key}=" "$PROP_FILE" 2>/dev/null | tail -n1 | cut -d'=' -f2- || true
}
SERVER_VERSION="$(ls -1 /home/gameserver/serverfiles/versions 2>/dev/null | sort -V | tail -n 1 || true)"
MOTD="$(read_prop motd)"
MAX_PLAYERS="$(read_prop max-players)"
ONLINE_MODE="$(read_prop online-mode)"
DIFFICULTY="$(read_prop difficulty)"
GAME_MODE="$(read_prop gamemode)"
PVP="$(read_prop pvp)"
ALLOW_FLIGHT="$(read_prop allow-flight)"
VIEW_DISTANCE="$(read_prop view-distance)"
SIMULATION_DISTANCE="$(read_prop simulation-distance)"
SPAWN_PROTECTION="$(read_prop spawn-protection)"
ENABLE_COMMAND_BLOCK="$(read_prop enable-command-block)"
WHITE_LIST="$(read_prop white-list)"
ENFORCE_WHITELIST="$(read_prop enforce-whitelist)"
ALLOW_NETHER="$(read_prop allow-nether)"
HARDCORE="$(read_prop hardcore)"
echo "PLAYMOX_SERVER_VERSION=$SERVER_VERSION"
echo "PLAYMOX_MOTD=$MOTD"
echo "PLAYMOX_MAX_PLAYERS=$MAX_PLAYERS"
echo "PLAYMOX_ONLINE_MODE=$ONLINE_MODE"
echo "PLAYMOX_DIFFICULTY=$DIFFICULTY"
echo "PLAYMOX_GAME_MODE=$GAME_MODE"
echo "PLAYMOX_PVP=$PVP"
echo "PLAYMOX_ALLOW_FLIGHT=$ALLOW_FLIGHT"
echo "PLAYMOX_VIEW_DISTANCE=$VIEW_DISTANCE"
echo "PLAYMOX_SIMULATION_DISTANCE=$SIMULATION_DISTANCE"
echo "PLAYMOX_SPAWN_PROTECTION=$SPAWN_PROTECTION"
echo "PLAYMOX_ENABLE_COMMAND_BLOCK=$ENABLE_COMMAND_BLOCK"
echo "PLAYMOX_WHITE_LIST=$WHITE_LIST"
echo "PLAYMOX_ENFORCE_WHITELIST=$ENFORCE_WHITELIST"
echo "PLAYMOX_ALLOW_NETHER=$ALLOW_NETHER"
echo "PLAYMOX_HARDCORE=$HARDCORE"
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
        values.TryGetValue("PLAYMOX_SERVER_VERSION", out var serverVersion);
        values.TryGetValue("PLAYMOX_MOTD", out var motd);
        values.TryGetValue("PLAYMOX_MAX_PLAYERS", out var maxPlayersRaw);
        values.TryGetValue("PLAYMOX_ONLINE_MODE", out var onlineModeRaw);
        values.TryGetValue("PLAYMOX_DIFFICULTY", out var difficultyRaw);
        values.TryGetValue("PLAYMOX_GAME_MODE", out var gameModeRaw);
        values.TryGetValue("PLAYMOX_PVP", out var pvpRaw);
        values.TryGetValue("PLAYMOX_ALLOW_FLIGHT", out var allowFlightRaw);
        values.TryGetValue("PLAYMOX_VIEW_DISTANCE", out var viewDistanceRaw);
        values.TryGetValue("PLAYMOX_SIMULATION_DISTANCE", out var simulationDistanceRaw);
        values.TryGetValue("PLAYMOX_SPAWN_PROTECTION", out var spawnProtectionRaw);
        values.TryGetValue("PLAYMOX_ENABLE_COMMAND_BLOCK", out var enableCommandBlockRaw);
        values.TryGetValue("PLAYMOX_WHITE_LIST", out var whiteListRaw);
        values.TryGetValue("PLAYMOX_ENFORCE_WHITELIST", out var enforceWhitelistRaw);
        values.TryGetValue("PLAYMOX_ALLOW_NETHER", out var allowNetherRaw);
        values.TryGetValue("PLAYMOX_HARDCORE", out var hardcoreRaw);
        var payload = new
        {
            serverVersion = serverVersion ?? string.Empty,
            motd = motd ?? "Playmox Minecraft",
            maxPlayers = int.TryParse(maxPlayersRaw, out var maxPlayers) ? maxPlayers : 20,
            onlineMode = GameServerConnectorSshOps.ParseBoolOrDefault(onlineModeRaw, true),
            difficulty = NormalizeMinecraftDifficulty(difficultyRaw) ?? "easy",
            gameMode = NormalizeMinecraftGamemode(gameModeRaw) ?? "survival",
            pvp = GameServerConnectorSshOps.ParseBoolOrDefault(pvpRaw, true),
            allowFlight = GameServerConnectorSshOps.ParseBoolOrDefault(allowFlightRaw, false),
            viewDistance = int.TryParse(viewDistanceRaw, out var viewDistance) ? viewDistance : 10,
            simulationDistance = int.TryParse(simulationDistanceRaw, out var simulationDistance) ? simulationDistance : 10,
            spawnProtection = int.TryParse(spawnProtectionRaw, out var spawnProtection) ? spawnProtection : 16,
            enableCommandBlock = GameServerConnectorSshOps.ParseBoolOrDefault(enableCommandBlockRaw, false),
            whiteList = GameServerConnectorSshOps.ParseBoolOrDefault(whiteListRaw, false),
            enforceWhitelist = GameServerConnectorSshOps.ParseBoolOrDefault(enforceWhitelistRaw, false),
            allowNether = GameServerConnectorSshOps.ParseBoolOrDefault(allowNetherRaw, true),
            hardcore = GameServerConnectorSshOps.ParseBoolOrDefault(hardcoreRaw, false),
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
LEVEL_NAME="$(grep -E '^level-name=' serverfiles/server.properties 2>/dev/null | tail -n1 | cut -d= -f2- || true)"
set --
for item in \
  "serverfiles/server.properties" \
  "serverfiles/eula.txt" \
  "serverfiles/ops.json" \
  "serverfiles/whitelist.json" \
  "serverfiles/usercache.json" \
  "lgsm/config-lgsm/mcserver"; do
  if [ -e "$item" ]; then
    set -- "$@" "$item"
  fi
done
if [ -n "$LEVEL_NAME" ] && [ -d "serverfiles/$LEVEL_NAME" ]; then
  set -- "$@" "serverfiles/$LEVEL_NAME"
fi
for worldDir in serverfiles/world*; do
  if [ -d "$worldDir" ]; then
    set -- "$@" "$worldDir"
  fi
done
if [ "$#" -eq 0 ]; then
  echo "No Minecraft backup data (world/config) found."
  exit 1
fi
tar -czf "{backupPath}" -C /home/gameserver "$@"
ls -l "{backupPath}"
""";

    protected override string BuildBackupRestoreScript(VirtualGameInstance instance, string tempArchivePath)
    {
        var gamePort = instance.GamePublicPortStart ?? instance.GameDefinition?.DefaultPort ?? 25565;
        var xmxMb = CalculateJavaMemoryMb(instance.MemoryMb);

        return $"""
set -e
TMP_TAR='{tempArchivePath}'
if [ "$(id -u)" -eq 0 ]; then
  systemctl stop playmox-game.service || true
  su - gameserver -c 'cd ~ && ./mcserver stop || true'
  su - gameserver -c 'rm -rf ~/serverfiles/world* ~/lgsm/config-lgsm/mcserver'
  su - gameserver -c 'rm -f ~/serverfiles/server.properties ~/serverfiles/eula.txt ~/serverfiles/ops.json ~/serverfiles/whitelist.json ~/serverfiles/usercache.json'
  su - gameserver -c 'mkdir -p ~/serverfiles ~/lgsm/config-lgsm/mcserver'
  if file "$TMP_TAR" | grep -qi 'zstd'; then
    su - gameserver -c "tar --zstd -xf '$TMP_TAR' -C /home/gameserver"
  elif file "$TMP_TAR" | grep -qi 'gzip'; then
    su - gameserver -c "tar -xzf '$TMP_TAR' -C /home/gameserver"
  else
    su - gameserver -c "tar -xf '$TMP_TAR' -C /home/gameserver"
  fi
  chown -R gameserver:gameserver /home/gameserver/serverfiles /home/gameserver/lgsm || true
  su - gameserver -c 'echo eula=true > ~/serverfiles/eula.txt'
  su - gameserver -c 'touch ~/serverfiles/server.properties'
  su - gameserver -c "for cfg in ~/lgsm/config-lgsm/mcserver/common.cfg ~/lgsm/config-lgsm/mcserver/mcserver.cfg; do touch \"\$cfg\"; if grep -q '^javaram=' \"\$cfg\"; then sed -i 's/^javaram=.*/javaram=\"{xmxMb}\"/' \"\$cfg\"; else echo 'javaram=\"{xmxMb}\"' >> \"\$cfg\"; fi; if grep -q '^startparameters=' \"\$cfg\"; then sed -i 's|^startparameters=.*|startparameters=\"nogui\"|' \"\$cfg\"; else echo 'startparameters=\"nogui\"' >> \"\$cfg\"; fi; done"
  su - gameserver -c "if grep -q '^server-port=' ~/serverfiles/server.properties; then sed -i 's/^server-port=.*/server-port={gamePort}/' ~/serverfiles/server.properties; else echo 'server-port={gamePort}' >> ~/serverfiles/server.properties; fi"
  su - gameserver -c "if grep -q '^force-gamemode=' ~/serverfiles/server.properties; then sed -i 's/^force-gamemode=.*/force-gamemode=true/' ~/serverfiles/server.properties; else echo 'force-gamemode=true' >> ~/serverfiles/server.properties; fi"
  systemctl restart playmox-game.service || su - gameserver -c 'cd ~ && ./mcserver restart'
else
  sudo -n /bin/systemctl stop playmox-game.service 2>/dev/null || true
  cd ~ && ./mcserver stop || true
  rm -rf ~/serverfiles/world* ~/lgsm/config-lgsm/mcserver
  rm -f ~/serverfiles/server.properties ~/serverfiles/eula.txt ~/serverfiles/ops.json ~/serverfiles/whitelist.json ~/serverfiles/usercache.json
  mkdir -p ~/serverfiles ~/lgsm/config-lgsm/mcserver
  if file "$TMP_TAR" | grep -qi 'zstd'; then
    tar --zstd -xf "$TMP_TAR" -C /home/gameserver
  elif file "$TMP_TAR" | grep -qi 'gzip'; then
    tar -xzf "$TMP_TAR" -C /home/gameserver
  else
    tar -xf "$TMP_TAR" -C /home/gameserver
  fi
  echo eula=true > ~/serverfiles/eula.txt
  touch ~/serverfiles/server.properties
  for cfg in ~/lgsm/config-lgsm/mcserver/common.cfg ~/lgsm/config-lgsm/mcserver/mcserver.cfg; do touch "$cfg"; if grep -q '^javaram=' "$cfg"; then sed -i 's/^javaram=.*/javaram="{xmxMb}"/' "$cfg"; else echo 'javaram="{xmxMb}"' >> "$cfg"; fi; if grep -q '^startparameters=' "$cfg"; then sed -i 's|^startparameters=.*|startparameters="nogui"|' "$cfg"; else echo 'startparameters="nogui"' >> "$cfg"; fi; done
  if grep -q '^server-port=' ~/serverfiles/server.properties; then sed -i 's/^server-port=.*/server-port={gamePort}/' ~/serverfiles/server.properties; else echo 'server-port={gamePort}' >> ~/serverfiles/server.properties; fi
  if grep -q '^force-gamemode=' ~/serverfiles/server.properties; then sed -i 's/^force-gamemode=.*/force-gamemode=true/' ~/serverfiles/server.properties; else echo 'force-gamemode=true' >> ~/serverfiles/server.properties; fi
  sudo -n /bin/systemctl restart playmox-game.service 2>/dev/null || (cd ~ && ./mcserver restart)
fi
rm -f "$TMP_TAR"
""";
    }

    private static string? NormalizeMinecraftDifficulty(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "peaceful" => "peaceful",
            "easy" => "easy",
            "normal" => "normal",
            "hard" => "hard",
            _ => null
        };
    }

    private static string? NormalizeMinecraftGamemode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "survival" => "survival",
            "creative" => "creative",
            "adventure" => "adventure",
            "spectator" => "spectator",
            _ => null
        };
    }

    private static MinecraftProvisioningSettings ParseMinecraftSettings(string? configurationJson)
    {
        if (string.IsNullOrWhiteSpace(configurationJson))
        {
            return new MinecraftProvisioningSettings();
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

            string? motd = null;
            int? maxPlayers = null;
            string? serverVersion = null;
            bool? onlineMode = null;
            string? difficulty = null;
            string? gameMode = null;
            bool? pvp = null;
            bool? allowFlight = null;
            int? viewDistance = null;
            int? simulationDistance = null;
            int? spawnProtection = null;
            bool? enableCommandBlock = null;
            bool? whiteList = null;
            bool? enforceWhitelist = null;
            bool? allowNether = null;
            bool? hardcore = null;

            if (root.TryGetProperty("serverVersion", out var versionProp) && versionProp.ValueKind == JsonValueKind.String)
            {
                serverVersion = versionProp.GetString();
            }

            if (root.TryGetProperty("motd", out var motdProp) && motdProp.ValueKind == JsonValueKind.String)
            {
                motd = motdProp.GetString();
            }

            if (root.TryGetProperty("maxPlayers", out var maxPlayersProp) && maxPlayersProp.TryGetInt32(out var max))
            {
                maxPlayers = max;
            }

            if (root.TryGetProperty("onlineMode", out var onlineModeProp))
            {
                if (onlineModeProp.ValueKind == JsonValueKind.True)
                {
                    onlineMode = true;
                }
                else if (onlineModeProp.ValueKind == JsonValueKind.False)
                {
                    onlineMode = false;
                }
            }

            if (root.TryGetProperty("difficulty", out var difficultyProp) && difficultyProp.ValueKind == JsonValueKind.String)
            {
                difficulty = difficultyProp.GetString();
            }

            if (root.TryGetProperty("gameMode", out var gameModeProp) && gameModeProp.ValueKind == JsonValueKind.String)
            {
                gameMode = gameModeProp.GetString();
            }

            if (root.TryGetProperty("pvp", out var pvpProp))
            {
                if (pvpProp.ValueKind == JsonValueKind.True)
                {
                    pvp = true;
                }
                else if (pvpProp.ValueKind == JsonValueKind.False)
                {
                    pvp = false;
                }
            }

            if (root.TryGetProperty("allowFlight", out var allowFlightProp))
            {
                if (allowFlightProp.ValueKind == JsonValueKind.True)
                {
                    allowFlight = true;
                }
                else if (allowFlightProp.ValueKind == JsonValueKind.False)
                {
                    allowFlight = false;
                }
            }

            if (root.TryGetProperty("viewDistance", out var viewDistanceProp) && viewDistanceProp.TryGetInt32(out var parsedViewDistance))
            {
                viewDistance = parsedViewDistance;
            }

            if (root.TryGetProperty("simulationDistance", out var simulationDistanceProp) && simulationDistanceProp.TryGetInt32(out var parsedSimulationDistance))
            {
                simulationDistance = parsedSimulationDistance;
            }

            if (root.TryGetProperty("spawnProtection", out var spawnProtectionProp) && spawnProtectionProp.TryGetInt32(out var parsedSpawnProtection))
            {
                spawnProtection = parsedSpawnProtection;
            }

            if (root.TryGetProperty("enableCommandBlock", out var enableCommandBlockProp))
            {
                if (enableCommandBlockProp.ValueKind == JsonValueKind.True)
                {
                    enableCommandBlock = true;
                }
                else if (enableCommandBlockProp.ValueKind == JsonValueKind.False)
                {
                    enableCommandBlock = false;
                }
            }

            if (root.TryGetProperty("whiteList", out var whiteListProp))
            {
                if (whiteListProp.ValueKind == JsonValueKind.True)
                {
                    whiteList = true;
                }
                else if (whiteListProp.ValueKind == JsonValueKind.False)
                {
                    whiteList = false;
                }
            }

            if (root.TryGetProperty("enforceWhitelist", out var enforceWhitelistProp))
            {
                if (enforceWhitelistProp.ValueKind == JsonValueKind.True)
                {
                    enforceWhitelist = true;
                }
                else if (enforceWhitelistProp.ValueKind == JsonValueKind.False)
                {
                    enforceWhitelist = false;
                }
            }

            if (root.TryGetProperty("allowNether", out var allowNetherProp))
            {
                if (allowNetherProp.ValueKind == JsonValueKind.True)
                {
                    allowNether = true;
                }
                else if (allowNetherProp.ValueKind == JsonValueKind.False)
                {
                    allowNether = false;
                }
            }

            if (root.TryGetProperty("hardcore", out var hardcoreProp))
            {
                if (hardcoreProp.ValueKind == JsonValueKind.True)
                {
                    hardcore = true;
                }
                else if (hardcoreProp.ValueKind == JsonValueKind.False)
                {
                    hardcore = false;
                }
            }

            var customParameters = GameServerCustomParameters.ParseFromConfigRoot(root);

            return new MinecraftProvisioningSettings
            {
                ServerVersion = serverVersion,
                Motd = motd,
                MaxPlayers = maxPlayers,
                OnlineMode = onlineMode,
                Difficulty = difficulty,
                GameMode = gameMode,
                Pvp = pvp,
                AllowFlight = allowFlight,
                ViewDistance = viewDistance,
                SimulationDistance = simulationDistance,
                SpawnProtection = spawnProtection,
                EnableCommandBlock = enableCommandBlock,
                WhiteList = whiteList,
                EnforceWhitelist = enforceWhitelist,
                AllowNether = allowNether,
                Hardcore = hardcore,
                CustomParameters = customParameters
            };
        }
        catch
        {
            return new MinecraftProvisioningSettings();
        }
    }

    private static int CalculateJavaMemoryMb(int memoryMb)
    {
        return memoryMb > 512 ? memoryMb - 512 : memoryMb;
    }

    private sealed class MinecraftProvisioningSettings
    {
        public string? ServerVersion { get; init; }
        public string? Motd { get; init; }
        public int? MaxPlayers { get; init; }
        public bool? OnlineMode { get; init; }
        public string? Difficulty { get; init; }
        public string? GameMode { get; init; }
        public bool? Pvp { get; init; }
        public bool? AllowFlight { get; init; }
        public int? ViewDistance { get; init; }
        public int? SimulationDistance { get; init; }
        public int? SpawnProtection { get; init; }
        public bool? EnableCommandBlock { get; init; }
        public bool? WhiteList { get; init; }
        public bool? EnforceWhitelist { get; init; }
        public bool? AllowNether { get; init; }
        public bool? Hardcore { get; init; }
        public Dictionary<string, string> CustomParameters { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
