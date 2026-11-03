using System.Text;
using Isopoh.Cryptography.Argon2;
using Isopoh.Cryptography.SecureArray;

namespace PlaymoxAPI.Extensions.Auth;

public static class Crypto
{
    public static string HashPassword(string password)
         {
             var config = new Argon2Config
             {
                 Type = Argon2Type.HybridAddressing,
                 Version = Argon2Version.Nineteen,
                 TimeCost = 4, // iterations
                 MemoryCost = 1 << 16, // 64 MB
                 Lanes = 2,
                 Threads = 2,
                 Salt = Encoding.UTF8.GetBytes(Config.PasswordSalt),
                 HashLength = 24,
                 Password = Encoding.UTF8.GetBytes(password)
             };
     
             using var argon2 = new Argon2(config);
             string hashString;
             using (SecureArray<byte> hashA = argon2.Hash())
             {
                 hashString = config.EncodeString(hashA.Buffer);
             }
     
             return hashString;
         }

    public static bool VerifyPassword(string password, string encodedHash)
    {
        return Argon2.Verify(encodedHash, password);
    }
}
