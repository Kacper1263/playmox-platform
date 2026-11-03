using PlaymoxAPI.BLL.BO.Platform;

namespace PlaymoxAPI.Services.Platform;

public sealed class GameServerConnectorRegistry
{
    private readonly IReadOnlyDictionary<GameServerKind, IGameServerConnector> connectors;

    public GameServerConnectorRegistry(IEnumerable<IGameServerConnector> connectors)
    {
        this.connectors = connectors.ToDictionary(x => x.Kind);
    }

    public IGameServerConnector? Resolve(GameDefinition? definition)
    {
        var kind = GameServerKindResolver.FromKey(definition?.Key);
        return connectors.TryGetValue(kind, out var connector) ? connector : null;
    }

    public IGameServerConnector? Resolve(GameServerKind kind)
        => connectors.TryGetValue(kind, out var connector) ? connector : null;
}
