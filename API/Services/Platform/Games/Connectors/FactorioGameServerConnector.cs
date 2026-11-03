using System.Text.Json;
using Microsoft.Extensions.Logging;
using PlaymoxAPI.BLL.BO.Platform;
using PlaymoxAPI.Services.Proxmox;

namespace PlaymoxAPI.Services.Platform;

public sealed class FactorioGameServerConnector : LinuxGsmConnectorBase
{
    public FactorioGameServerConnector(
        ProxmoxAutomationClient proxmoxAutomationClient,
        ILogger<FactorioGameServerConnector> logger)
        : base(proxmoxAutomationClient, logger)
    {
    }

    public override GameServerKind Kind => GameServerKind.Factorio;
    protected override string ServerBinary => "fctrserver";
    protected override string BackupSlug => "factorio";
    protected override string GameDisplayName => "Factorio";

    public override async Task InstallOrUpdateAsync(
        VirtualGameInstance instance,
        string? configurationJson,
        GameDefinition gameDefinition,
        bool forceReinstall,
        CancellationToken cancellationToken)
    {
        var settings = ParseFactorioSettings(configurationJson);
        var gamePort = instance.GamePublicPortStart ?? gameDefinition.DefaultPort ?? 34197;
        var serverName = GameServerConnectorSshOps.EscapeForJson(settings.ServerName ?? "Playmox Factorio");
        var description = GameServerConnectorSshOps.EscapeForJson(settings.Description ?? "Playmox hosted server");
        var maxPlayers = settings.MaxPlayers is >= 1 and <= 255 ? settings.MaxPlayers.Value : 20;
        var password = GameServerConnectorSshOps.EscapeForJson(settings.Password ?? string.Empty);
        var visibilityPublic = settings.VisibilityPublic ?? true;
        var visibilityPublicValue = visibilityPublic ? "true" : "false";

        const string commonCfg = "/home/gameserver/lgsm/config-lgsm/fctrserver/common.cfg";
        const string instanceCfg = "/home/gameserver/lgsm/config-lgsm/fctrserver/fctrserver.cfg";
        const string settingsJson = "/home/gameserver/serverfiles/data/fctrserver.json";
        const string legacySettingsJson = "/home/gameserver/serverfiles/data/server-settings.json";
        var customSettingsScript = GameServerCustomParameters.BuildFactorioJsonUpsertScript(settingsJson, settings.CustomParameters);
        var cfgWriteScript = $$"""
install -d -m 755 -o gameserver -g gameserver /home/gameserver/lgsm/config-lgsm/fctrserver /home/gameserver/serverfiles/data
cat > {{commonCfg}} <<'EOF'
port="{{gamePort}}"
EOF
cat > {{instanceCfg}} <<'EOF'
port="{{gamePort}}"
EOF
cat > {{settingsJson}} <<'EOF'
{
  "name": "{{serverName}}",
  "description": "{{description}}",
  "tags": ["playmox"],
  "max_players": {{maxPlayers}},
  "visibility": {
    "public": {{visibilityPublicValue}},
    "lan": false
  },
  "username": "",
  "password": "{{password}}",
  "require_user_verification": false,
  "max_upload_in_kilobytes_per_second": 0,
  "max_upload_slots": 5,
  "minimum_latency_in_ticks": 0,
  "ignore_player_limit_for_returning_players": false,
  "allow_commands": "admins-only",
  "autosave_interval": 10,
  "autosave_slots": 5,
  "afk_autokick_interval": 0,
  "auto_pause": true,
  "only_admins_can_pause_the_game": true
}
EOF
{{customSettingsScript}}
cp {{settingsJson}} {{legacySettingsJson}} || true
chown -R gameserver:gameserver /home/gameserver/lgsm /home/gameserver/serverfiles || true
""";

        var baseSetupBlock = LinuxGsmScriptBuilder.BuildBaseSetupBlock("Install/update Factorio server", ServerBinary);
        var reinstallScript = LinuxGsmScriptBuilder.BuildForceReinstallBlock(ServerBinary, forceReinstall);
        var systemdBlock = LinuxGsmScriptBuilder.BuildSystemdServiceAndStartBlock(ServerBinary, "FACTORIO");

        var script = $"""
{baseSetupBlock}
{cfgWriteScript}
{reinstallScript}
chown -R gameserver:gameserver /home/gameserver || true
su - gameserver -c 'cd ~ && ./fctrserver auto-install'
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
        return $$"""
set -eu
SERVICE_STATE="$(systemctl is-active playmox-game.service 2>/dev/null || true)"
echo "PLAYMOX_SERVICE_STATE=$SERVICE_STATE"
if [ {{gamePort}} -gt 0 ]; then
  GAME_LISTENING="no"
  if ss -lunH 2>/dev/null | awk '{print $5}' | sed -E 's/.*:([0-9]+)$/\1/' | grep -qx '{{gamePort}}'; then
    GAME_LISTENING="yes"
  fi
  echo "PLAYMOX_GAME_PORT={{gamePort}}"
  echo "PLAYMOX_GAME_PORT_LISTENING=$GAME_LISTENING"
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

        var storedSettings = ParseFactorioSettings(storedConfigurationJson);
        const string script = """
set -eu
CFG_PRIMARY="/home/gameserver/serverfiles/data/fctrserver.json"
CFG_LEGACY="/home/gameserver/serverfiles/data/server-settings.json"
NAME_PLACEHOLDER="Name of the game as it will appear in the game listing"
DESC_PLACEHOLDER="Description of the game that will appear in the listing"
score_cfg() {
  local cfg="$1"
  [ -f "$cfg" ] || { echo -1; return; }
  local score=0
  local name description has_playmox_tag
  name="$(jq -r '.name // ""' "$cfg" 2>/dev/null || true)"
  description="$(jq -r '.description // ""' "$cfg" 2>/dev/null || true)"
  has_playmox_tag="$(jq -r '[.tags[]?] | any(. == "playmox")' "$cfg" 2>/dev/null || true)"
  [ "$has_playmox_tag" = "true" ] && score=$((score + 3))
  if [ -n "$name" ] && [ "$name" != "$NAME_PLACEHOLDER" ]; then
    score=$((score + 2))
  fi
  if [ -n "$description" ] && [ "$description" != "$DESC_PLACEHOLDER" ]; then
    score=$((score + 1))
  fi
  echo "$score"
}
primary_score="$(score_cfg "$CFG_PRIMARY")"
legacy_score="$(score_cfg "$CFG_LEGACY")"
if [ "$primary_score" -ge "$legacy_score" ]; then
  CFG="$CFG_PRIMARY"
else
  CFG="$CFG_LEGACY"
fi
if [ ! -f "$CFG" ]; then
  echo "PLAYMOX_SERVER_NAME="
  echo "PLAYMOX_DESCRIPTION="
  echo "PLAYMOX_MAX_PLAYERS="
  echo "PLAYMOX_VISIBILITY_PUBLIC="
  echo "PLAYMOX_PASSWORD="
  exit 0
fi
echo "PLAYMOX_SERVER_NAME=$(jq -r '.name // empty' "$CFG" 2>/dev/null || true)"
echo "PLAYMOX_DESCRIPTION=$(jq -r '.description // empty' "$CFG" 2>/dev/null || true)"
echo "PLAYMOX_MAX_PLAYERS=$(jq -r '.max_players // empty' "$CFG" 2>/dev/null || true)"
echo "PLAYMOX_VISIBILITY_PUBLIC=$(jq -r '.visibility.public // empty' "$CFG" 2>/dev/null || true)"
echo "PLAYMOX_PASSWORD=$(jq -r '.password // empty' "$CFG" 2>/dev/null || true)"
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
        values.TryGetValue("PLAYMOX_DESCRIPTION", out var description);
        values.TryGetValue("PLAYMOX_MAX_PLAYERS", out var maxPlayersRaw);
        values.TryGetValue("PLAYMOX_VISIBILITY_PUBLIC", out var visibilityPublicRaw);
        values.TryGetValue("PLAYMOX_PASSWORD", out var password);
        var payload = new
        {
            serverName = serverName ?? "Playmox Factorio",
            description = description ?? "Playmox hosted server",
            maxPlayers = int.TryParse(maxPlayersRaw, out var maxPlayers) ? maxPlayers : 20,
            visibilityPublic = GameServerConnectorSshOps.ParseBoolOrDefault(visibilityPublicRaw, storedSettings.VisibilityPublic ?? true),
            password = password ?? string.Empty,
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
  "serverfiles/saves" \
  "serverfiles/data/fctrserver.json" \
  "serverfiles/data/server-settings.json" \
  "serverfiles/data/server-adminlist.json" \
  "serverfiles/data/server-banlist.json" \
  "serverfiles/data/server-whitelist.json" \
  "lgsm/config-lgsm/fctrserver"; do
  if [ -e "$item" ]; then
    set -- "$@" "$item"
  fi
done
for saveZip in serverfiles/*.zip; do
  if [ -f "$saveZip" ]; then
    set -- "$@" "$saveZip"
  fi
done
if [ "$#" -eq 0 ]; then
  echo "No Factorio backup data (world/config) found."
  exit 1
fi
tar -czf "{backupPath}" -C /home/gameserver "$@"
ls -l "{backupPath}"
""";

    protected override string BuildBackupRestoreScript(VirtualGameInstance instance, string tempArchivePath)
    {
        var gamePort = instance.GamePublicPortStart ?? instance.GameDefinition?.DefaultPort ?? 34197;

        return $"""
set -e
TMP_TAR='{tempArchivePath}'
if [ "$(id -u)" -eq 0 ]; then
  systemctl stop playmox-game.service || true
  su - gameserver -c 'cd ~ && ./fctrserver stop || true'
  su - gameserver -c 'rm -rf ~/serverfiles/saves ~/lgsm/config-lgsm/fctrserver'
  su - gameserver -c 'rm -f ~/serverfiles/data/fctrserver.json ~/serverfiles/data/server-settings.json ~/serverfiles/data/server-adminlist.json ~/serverfiles/data/server-banlist.json ~/serverfiles/data/server-whitelist.json'
  su - gameserver -c 'mkdir -p ~/serverfiles/data ~/serverfiles/saves ~/lgsm/config-lgsm/fctrserver'
  if file "$TMP_TAR" | grep -qi 'zstd'; then
    su - gameserver -c "tar --zstd -xf '$TMP_TAR' -C /home/gameserver"
  elif file "$TMP_TAR" | grep -qi 'gzip'; then
    su - gameserver -c "tar -xzf '$TMP_TAR' -C /home/gameserver"
  else
    su - gameserver -c "tar -xf '$TMP_TAR' -C /home/gameserver"
  fi
  chown -R gameserver:gameserver /home/gameserver/serverfiles /home/gameserver/lgsm || true
  su - gameserver -c "CFG_PRIMARY=~/serverfiles/data/fctrserver.json; CFG_LEGACY=~/serverfiles/data/server-settings.json; if [ -f \"\$CFG_PRIMARY\" ]; then CFG=\"\$CFG_PRIMARY\"; elif [ -f \"\$CFG_LEGACY\" ]; then CFG=\"\$CFG_LEGACY\"; else CFG=\"\$CFG_PRIMARY\"; fi; touch \"\$CFG\"; TMP_CFG=\"\$CFG.tmp\"; if jq '.require_user_verification = false' \"\$CFG\" > \"\$TMP_CFG\" 2>/dev/null; then mv \"\$TMP_CFG\" \"\$CFG\"; else rm -f \"\$TMP_CFG\"; if grep -q '\"require_user_verification\"' \"\$CFG\" 2>/dev/null; then sed -i 's/\"require_user_verification\"[[:space:]]*:[[:space:]]*true/\"require_user_verification\": false/g' \"\$CFG\"; else printf '\173\n  \"require_user_verification\": false\n\175\n' > \"\$CFG\"; fi; fi; if [ \"\$CFG\" != \"\$CFG_PRIMARY\" ]; then cp \"\$CFG\" \"\$CFG_PRIMARY\"; fi; cp \"\$CFG_PRIMARY\" \"\$CFG_LEGACY\" || true"
  su - gameserver -c "for cfg in ~/lgsm/config-lgsm/fctrserver/common.cfg ~/lgsm/config-lgsm/fctrserver/fctrserver.cfg; do touch \"$cfg\"; if grep -q '^port=' \"$cfg\"; then sed -i 's/^port=.*/port=\"{gamePort}\"/' \"$cfg\"; else echo 'port=\"{gamePort}\"' >> \"$cfg\"; fi; done"
  systemctl restart playmox-game.service || su - gameserver -c 'cd ~ && ./fctrserver restart'
else
  sudo -n /bin/systemctl stop playmox-game.service 2>/dev/null || true
  cd ~ && ./fctrserver stop || true
  rm -rf ~/serverfiles/saves ~/lgsm/config-lgsm/fctrserver
  rm -f ~/serverfiles/data/fctrserver.json ~/serverfiles/data/server-settings.json ~/serverfiles/data/server-adminlist.json ~/serverfiles/data/server-banlist.json ~/serverfiles/data/server-whitelist.json
  mkdir -p ~/serverfiles/data ~/serverfiles/saves ~/lgsm/config-lgsm/fctrserver
  if file "$TMP_TAR" | grep -qi 'zstd'; then
    tar --zstd -xf "$TMP_TAR" -C /home/gameserver
  elif file "$TMP_TAR" | grep -qi 'gzip'; then
    tar -xzf "$TMP_TAR" -C /home/gameserver
  else
    tar -xf "$TMP_TAR" -C /home/gameserver
  fi
  CFG_PRIMARY=~/serverfiles/data/fctrserver.json; CFG_LEGACY=~/serverfiles/data/server-settings.json; if [ -f "$CFG_PRIMARY" ]; then CFG="$CFG_PRIMARY"; elif [ -f "$CFG_LEGACY" ]; then CFG="$CFG_LEGACY"; else CFG="$CFG_PRIMARY"; fi; touch "$CFG"; TMP_CFG="$CFG.tmp"; if jq '.require_user_verification = false' "$CFG" > "$TMP_CFG" 2>/dev/null; then mv "$TMP_CFG" "$CFG"; else rm -f "$TMP_CFG"; if grep -q '"require_user_verification"' "$CFG" 2>/dev/null; then sed -i 's/"require_user_verification"[[:space:]]*:[[:space:]]*true/"require_user_verification": false/g' "$CFG"; else printf '\173\n  "require_user_verification": false\n\175\n' > "$CFG"; fi; fi; if [ "$CFG" != "$CFG_PRIMARY" ]; then cp "$CFG" "$CFG_PRIMARY"; fi; cp "$CFG_PRIMARY" "$CFG_LEGACY" || true
  for cfg in ~/lgsm/config-lgsm/fctrserver/common.cfg ~/lgsm/config-lgsm/fctrserver/fctrserver.cfg; do touch "$cfg"; if grep -q '^port=' "$cfg"; then sed -i 's/^port=.*/port="{gamePort}"/' "$cfg"; else echo 'port="{gamePort}"' >> "$cfg"; fi; done
  sudo -n /bin/systemctl restart playmox-game.service 2>/dev/null || (cd ~ && ./fctrserver restart)
fi
rm -f "$TMP_TAR"
""";
    }

    private static FactorioProvisioningSettings ParseFactorioSettings(string? configurationJson)
    {
        if (string.IsNullOrWhiteSpace(configurationJson))
        {
            return new FactorioProvisioningSettings();
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

            var result = new FactorioProvisioningSettings();
            if (root.TryGetProperty("serverName", out var serverNameProp) && serverNameProp.ValueKind == JsonValueKind.String)
            {
                result.ServerName = serverNameProp.GetString();
            }
            if (root.TryGetProperty("description", out var descriptionProp) && descriptionProp.ValueKind == JsonValueKind.String)
            {
                result.Description = descriptionProp.GetString();
            }
            if (root.TryGetProperty("maxPlayers", out var maxPlayersProp) && maxPlayersProp.TryGetInt32(out var maxPlayers))
            {
                result.MaxPlayers = maxPlayers;
            }
            if (root.TryGetProperty("password", out var passwordProp) && passwordProp.ValueKind == JsonValueKind.String)
            {
                result.Password = passwordProp.GetString();
            }
            if (root.TryGetProperty("visibilityPublic", out var visibilityProp))
            {
                if (visibilityProp.ValueKind == JsonValueKind.True)
                {
                    result.VisibilityPublic = true;
                }
                else if (visibilityProp.ValueKind == JsonValueKind.False)
                {
                    result.VisibilityPublic = false;
                }
                else if (visibilityProp.ValueKind == JsonValueKind.String)
                {
                    var raw = visibilityProp.GetString();
                    if (!string.IsNullOrWhiteSpace(raw))
                    {
                        result.VisibilityPublic = raw.Trim().ToLowerInvariant() switch
                        {
                            "true" or "1" or "yes" => true,
                            "false" or "0" or "no" => false,
                            _ => result.VisibilityPublic
                        };
                    }
                }
            }
            result.CustomParameters = GameServerCustomParameters.ParseFromConfigRoot(root);

            return result;
        }
        catch
        {
            return new FactorioProvisioningSettings();
        }
    }

    private sealed class FactorioProvisioningSettings
    {
        public string? ServerName { get; set; } = "Playmox Factorio";
        public string? Description { get; set; } = "Playmox hosted server";
        public int? MaxPlayers { get; set; } = 20;
        public bool? VisibilityPublic { get; set; } = true;
        public string? Password { get; set; } = string.Empty;
        public Dictionary<string, string> CustomParameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
