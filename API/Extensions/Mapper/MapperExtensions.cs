namespace PlaymoxAPI.Extensions.Mapper;

public static class MapperExtensions
{
    /// <summary>
    /// Updates the current object with values from the source object using AutoMapper.
    /// </summary>
    public static void From<DEST, SOURCE>(this DEST dst, SOURCE src)
    {
        Config.Mapper.Map(src, dst);
    }

    /// <summary>
    /// Maps the source object to a new instance of the destination type using AutoMapper.
    /// </summary>
    public static DEST To<DEST>(this object src)
    {
        return Config.Mapper.Map<DEST>(src);
    }
}
