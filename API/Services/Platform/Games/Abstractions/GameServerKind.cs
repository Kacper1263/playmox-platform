namespace PlaymoxAPI.Services.Platform;

public enum GameServerKind
{
    Unknown = 0,
    BlankVm = 1,
    MinecraftJava = 2,
    Terraria = 3,
    Unturned = 4,
    Factorio = 5
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
            _ => GameServerKind.Unknown
        };
    }
}
