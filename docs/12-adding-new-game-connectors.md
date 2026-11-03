# 12. Adding a New Game Server Connector (Developer & AI Agent Guide)

Playmox uses a modular **Game Server Connector Architecture** located in `API/Services/Platform/Games/`. Adding support for a new dedicated game server (whether powered by **LinuxGSM**, **SteamCMD**, or a custom binary) requires **5 clean, deterministic steps** without modifying the core provisioning queue or frontend Razor pages.

---

## 1. Architecture Overview (`API/Services/Platform/Games/`)

```text
API/Services/Platform/Games/
├── Abstractions/
│   ├── GameServerKind.cs              # Enum of supported game types + GameServerKindResolver
│   ├── IGameServerConnector.cs        # Core connector contract
│   ├── LinuxGsmConnectorBase.cs       # Abstract base class handling systemd, runtime probes, SFTP backups/restores
│   └── GameServerConnectorRegistry.cs # DI-backed lookup resolving GameDefinition.Key -> IGameServerConnector
├── Infrastructure/
│   ├── GameServerConnectorSshOps.cs   # SSH/SFTP connection factories, output parsers, Steam credential helpers
│   ├── GameServerCustomParameters.cs  # Safe shell upsert builders for INI (key=value), space-separated, and JSON configs
│   └── LinuxGsmScriptBuilder.cs       # Reusable bash script blocks (apt setup, linuxgsm.sh bootstrap, playmox-game.service)
└── Connectors/                        # Individual game implementations (BlankVm, MinecraftJava, Terraria, Unturned, Factorio, ...)
```

When a class inherits from **`LinuxGsmConnectorBase`**, it automatically gets:
- Graceful pre-shutdown stopping (`StopForShutdownAsync`).
- Post-resource-resize restarting (`RestartAfterVmResourceChangeAsync`).
- Standardized runtime status and diagnostics probing (`GetRuntimeInfoAsync`).
- Full SFTP `.tar.gz` backup streaming (`CreateBackupAsync`) and archive upload/extraction (`RestoreBackupAsync`).

You only need to implement the game-specific scripts and configuration parsing!

---

## 2. Step-by-Step Guide: Adding a New Game (Example: Valheim / `vhserver`)

### Step 1: Register the Enum & Key in `GameServerKind.cs`

Open `API/Services/Platform/Games/Abstractions/GameServerKind.cs`:
1. Add a new value to the `GameServerKind` enum.
2. Map the string catalog key (e.g., `"valheim"`) in `GameServerKindResolver.FromKey`.

```csharp
public enum GameServerKind
{
    Unknown = 0,
    BlankVm = 1,
    MinecraftJava = 2,
    Terraria = 3,
    Unturned = 4,
    Factorio = 5,
    Valheim = 6 // <-- 1. Add enum entry
}

public static class GameServerKindResolver
{
    public static GameServerKind FromKey(string? key)
    {
        return key?.Trim().ToLowerInvariant() switch
        {
            "blank-vm" => GameServerKind.BlankVm,
            "minecraft-java" => GameServerKind.MinecraftJava,
            "terraria" => GameServerKind.Terraria,
            "unturned" => GameServerKind.Unturned,
            "factorio" => GameServerKind.Factorio,
            "valheim" => GameServerKind.Valheim, // <-- 2. Map catalog key
            _ => GameServerKind.Unknown
        };
    }
}
```

---

### Step 2: Create the Connector Class (`ValheimGameServerConnector.cs`)

Create a new connector class in `API/Services/Platform/Games/` deriving from `LinuxGsmConnectorBase`:

```csharp
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PlaymoxAPI.BLL.BO.Platform;
using PlaymoxAPI.Services.Proxmox;

namespace PlaymoxAPI.Services.Platform;

public sealed class ValheimGameServerConnector : LinuxGsmConnectorBase
{
    public ValheimGameServerConnector(
        ProxmoxAutomationClient proxmoxAutomationClient,
        ILogger<ValheimGameServerConnector> logger)
        : base(proxmoxAutomationClient, logger)
    {
    }

    public override GameServerKind Kind => GameServerKind.Valheim;
    protected override string ServerBinary => "vhserver";
    protected override string BackupSlug => "valheim";
    protected override string GameDisplayName => "Valheim";

    public override async Task InstallOrUpdateAsync(
        VirtualGameInstance instance,
        string? configurationJson,
        GameDefinition gameDefinition,
        bool forceReinstall,
        CancellationToken cancellationToken)
    {
        var gamePort = instance.GamePublicPortStart ?? gameDefinition.DefaultPort ?? 2456;

        // 1. Build base apt + LinuxGSM bootstrap block
        var baseBlock = LinuxGsmScriptBuilder.BuildBaseSetupBlock(
            summary: $"Install/update {GameDisplayName} ({ServerBinary})",
            serverBinary: ServerBinary,
            extraAptPackages: "lib32gcc-s1 steamcmd",
            postAptCommands: "dpkg --add-architecture i386");

        var forceBlock = LinuxGsmScriptBuilder.BuildForceReinstallBlock(ServerBinary, forceReinstall);

        // 2. Write /home/gameserver/lgsm/config-lgsm/vhserver/common.cfg and run installer
        var configAndInstallBlock = $"""
install -d -m 755 -o gameserver -g gameserver /home/gameserver/lgsm/config-lgsm/vhserver
cat > /home/gameserver/lgsm/config-lgsm/vhserver/common.cfg <<'EOF'
port="{gamePort}"
servername="Playmox Valheim"
gameworld="PlaymoxWorld"
serverpassword="changeme"
EOF
chown gameserver:gameserver /home/gameserver/lgsm/config-lgsm/vhserver/common.cfg
su - gameserver -c 'cd ~ && ./vhserver auto-install'
""";

        // 3. Register playmox-game.service and start the server
        var systemdBlock = LinuxGsmScriptBuilder.BuildSystemdServiceAndStartBlock(
            serverBinary: ServerBinary,
            diagnosticsLabel: "VALHEIM");

        var fullScript = string.Join("\n", baseBlock, forceBlock, configAndInstallBlock, systemdBlock);

        await ProxmoxAutomationClient.RunGuestScriptAsync(
            instance.VmId,
            fullScript,
            TimeSpan.FromMinutes(30),
            cancellationToken);
    }

    public override async Task<GameServerConfigurationSnapshot> GetConfigurationSnapshotAsync(
        VirtualGameInstance instance,
        string? storedConfigurationJson,
        bool refreshFromServer,
        CancellationToken cancellationToken)
    {
        // Return cached DB configuration or parse live /home/gameserver/lgsm/config-lgsm/vhserver/common.cfg when refreshFromServer == true
        return new GameServerConfigurationSnapshot
        {
            InstanceId = instance.Id,
            Source = refreshFromServer ? "live" : "db",
            ConfigurationJson = string.IsNullOrWhiteSpace(storedConfigurationJson) ? "{}" : storedConfigurationJson
        };
    }

    protected override string BuildRuntimeStatusScript(VirtualGameInstance instance, int gamePort) => $"""
set -eu
STATE="$(systemctl is-active playmox-game.service 2>/dev/null || echo inactive)"
LISTENING="no"
if ss -lntu 2>/dev/null | grep -q ':{gamePort} '; then
  LISTENING="yes"
fi
echo "PLAYMOX_SERVICE_STATE=$STATE"
echo "PLAYMOX_GAME_PORT={gamePort}"
echo "PLAYMOX_GAME_PORT_LISTENING=$LISTENING"
""";

    protected override string BuildBackupCreateScript(string backupPath) => $"""
set -eu
cd /home/gameserver
tar -czf '{backupPath}' --ignore-failed-read lgsm/config-lgsm/vhserver .config/unity3d/IronGate/Valheim
""";

    protected override string BuildBackupRestoreScript(VirtualGameInstance instance, string tempArchivePath) => $"""
set -eu
systemctl stop playmox-game.service || true
tar -xzf '{tempArchivePath}' -C /home/gameserver
chown -R gameserver:gameserver /home/gameserver/lgsm /home/gameserver/.config || true
rm -f '{tempArchivePath}'
systemctl start playmox-game.service
""";
}
```

#### Useful Infrastructure Helpers Available to Connectors
- **`LinuxGsmScriptBuilder.BuildBaseSetupBlock(...)`**: Configures `apt` packages, creates the `gameserver` user, sets up the least-privilege sudoers rule (`/etc/sudoers.d/99-gameserver` allowing only `systemctl start|stop|restart playmox-game.service`), and downloads `linuxgsm.sh`.
- **`GameServerCustomParameters`**:
  - `GameServerCustomParameters.ParseFromConfigRoot(root)` extracts user-supplied `customParameters` from `ConfigurationJson`.
  - `BuildIniUpsertScript(filePath, customParams)` generates safe `awk` upsert commands for `key=value` files.
  - `BuildSpaceSeparatedUpsertScript(filePath, customParams)` generates safe `awk` upsert commands for `key value` files.
  - `BuildFactorioJsonUpsertScript(filePath, customParams)` generates safe `jq` upsert commands for `.json` files.

---

### Step 3: Register the Connector in Dependency Injection (`API/Program.cs`)

Open `API/Program.cs` and register your new connector alongside the existing `IGameServerConnector` implementations:

```csharp
builder.Services.AddTransient<IGameServerConnector, BlankVmGameServerConnector>();
builder.Services.AddTransient<IGameServerConnector, MinecraftJavaGameServerConnector>();
builder.Services.AddTransient<IGameServerConnector, TerrariaGameServerConnector>();
builder.Services.AddTransient<IGameServerConnector, UnturnedGameServerConnector>();
builder.Services.AddTransient<IGameServerConnector, FactorioGameServerConnector>();
builder.Services.AddTransient<IGameServerConnector, ValheimGameServerConnector>(); // <-- Add here
builder.Services.AddSingleton<GameServerConnectorRegistry>();
```

Because `GameServerConnectorRegistry` injects `IEnumerable<IGameServerConnector>` and indexes them by `connector.Kind`, no changes are required inside `InstanceLifecycleService` or `ProvisioningJobProcessor`.

---

### Step 4: Seed the `GameDefinition` & JSON Schema (`API/Helpers/BootstrapDataSeeder.cs`)

Open `API/Helpers/BootstrapDataSeeder.cs` and add an `UpsertGameDefinition` call inside `EnsureGameDefinitions`:

```csharp
UpsertGameDefinition(dbContext, new GameDefinition
{
    Key = "valheim",
    DisplayName = "Valheim",
    LinuxGsmServerId = "vhserver",
    IsActive = true,
    DefaultPort = 2456,
    RequiredPublicPorts = 2, // Valheim uses 2456-2457 UDP
    JsonSchema = """
{
  "type": "object",
  "properties": {
    "serverName": { "type": "string", "title": "Server Name" },
    "worldName": { "type": "string", "title": "World Name" },
    "password": { "type": "string", "title": "Server Password (min 5 chars)" },
    "publicServer": { "type": "boolean", "title": "Public Server Listing" }
  }
}
"""
});
```

#### How `RequiredPublicPorts` & Protocols Work
- Playmox automatically allocates `RequiredPublicPorts` contiguous ports in `PortLeases` starting at `GamePublicPortStart` and forwards all of them on the Proxmox host via `nftables` DNAT and inside the guest VM via `ufw`.
- If your game only needs UDP or TCP+UDP, inspect `InstanceLifecycleService.ResolveGamePortProtocol(string gameKey)` to specify `"udp"`, `"tcp"`, or `"tcp,udp"`.

---

### Step 5: (Optional) Add Bilingual UI Labels in `en.json` and `pl.json`

The Blazor frontend (`DynamicConfigService`) automatically renders form controls for every property in `JsonSchema` using its `"title"` attribute as a fallback. To provide polished **English** and **Polish** translations for your schema fields or custom connector labels, add keys in `Frontend/PlaymoxWeb/wwwroot/locales/en.json` and `pl.json` (and strongly-typed constants in `Frontend/PlaymoxWeb/Services/Tr.cs`).

---

## 3. Verification Checklist for New Connectors

1. **Build the Solution**: Run `dotnet build Playmox.sln` and ensure zero compiler warnings or errors.
2. **Database Seeding**: Start `playmox-api` and verify `GET /api/game-definitions` includes the new game with `isActive: true`.
3. **Provisioning & Port Forwarding**: Create an instance in the UI and verify:
   - `PortLeases` reserves the expected number of contiguous ports.
   - `playmox-game.service` reaches `active` state inside the guest VM.
   - `GET /api/instances/{id}/game-runtime` reports `isRunning: true` and `isGamePortListening: true`.
4. **Live Config & Backups**:
   - Open **Game Configuration** in the UI, click **Refresh from Server (`source=live`)**, modify a parameter, and save.
   - Download a `.tar.gz` backup and test restoring it via the UI.
