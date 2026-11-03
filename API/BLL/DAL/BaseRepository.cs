using PlaymoxAPI.BLL.BO;

namespace PlaymoxAPI.BLL.DAL;

public class BaseRepository<Type, Key> : Repository<Type, Key, PlaymoxContext>
    where Type : BaseEntity<Key>
    where Key : IComparable<Key>
{
    public BaseRepository(PlaymoxContext dc = null) : base(dc)
    {
            
    }
}