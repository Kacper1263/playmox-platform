using System.Linq.Dynamic.Core;
using PlaymoxAPI.BLL.BO.Users;
using PlaymoxAPI.Extensions.Auth;
using Microsoft.EntityFrameworkCore;

namespace PlaymoxAPI.BLL.DAL.Users;

public class UsersRepository : BaseRepository<User, long>
{
    public User Login(string login, string password)
    {
        var passHash = Crypto.HashPassword(password);

        var user = Q
            .Include(x => x.UserRoles).ThenInclude(x => x.Role).ThenInclude(x => x.Permissions)
            .SingleOrDefault(p =>
                p.Login == login && p.PasswordHash == passHash
            );

        return user;
    }
    
    public void UpdateRefreshToken(long userId, string? oldRefreshToken, string? newRefreshToken, DateTime? refreshTokenExpiryTime = null)
    {
        var userToken = oldRefreshToken == null ? null : DC.UsersTokens.SingleOrDefault(x => x.RefreshToken == oldRefreshToken);
        
        if (userToken == null && newRefreshToken != null && refreshTokenExpiryTime != null) // create new token if it does not exist
        {
            // check if user exists
            var user = Q.SingleOrDefault(x => x.Id == userId);
            if (user == null)
            {
                return; // user does not exist
            }
            
            userToken = new UserToken
            {
                UserId = userId,
                RefreshToken = newRefreshToken,
                RefreshTokenGenerateDate = DateTime.Now,
                RefreshTokenExpiryTime = refreshTokenExpiryTime
            };
            DC.UsersTokens.Add(userToken);
            SaveChanges();
            return;
        }
        
        if (userToken == null)
        {
            return;
        }
        
        if (refreshTokenExpiryTime == null || newRefreshToken == null) // remove token
        {
            DC.UsersTokens.Remove(userToken);
            SaveChanges();
            return;
        }
        
        userToken.RefreshToken = newRefreshToken;
        userToken.RefreshTokenGenerateDate = DateTime.Now;
        userToken.RefreshTokenExpiryTime = refreshTokenExpiryTime;
        SaveChanges();
    }

    public void RevokeRefreshToken(long userId, string refreshToken)
    {
        var userToken = DC.UsersTokens.SingleOrDefault(x => x.UserId == userId && x.RefreshToken == refreshToken);
        if (userToken == null)
        {
            return;
        }

        DC.UsersTokens.Remove(userToken);
        SaveChanges();
    }

    public void RevokeAllRefreshTokens(long userId)
    {
        var tokens = DC.UsersTokens.Where(x => x.UserId == userId).ToList();
        if (!tokens.Any())
        {
            return;
        }

        DC.UsersTokens.RemoveRange(tokens);
        SaveChanges();
    }
}
