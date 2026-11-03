using System.Diagnostics;
using PlaymoxAPI.BLL.BO;
using PlaymoxAPI.BLL.BO.Platform;
using PlaymoxAPI.BLL.BO.Users;
using Microsoft.EntityFrameworkCore;

namespace PlaymoxAPI.BLL
{
    public class PlaymoxContext : DbContext
    {
        private const string defaultColumnTypeForString = "nvarchar(255)";
        
        public DbSet<User> Users { get; set; }
        public DbSet<Role> Roles { get; set; }
        public DbSet<RolePermission> RolesPermissions { get; set; }
        public DbSet<UserRole> UsersRoles { get; set; }
        public DbSet<UserToken> UsersTokens { get; set; }
        public DbSet<UserQuota> UsersQuotas { get; set; }
        public DbSet<GameDefinition> GameDefinitions { get; set; }
        public DbSet<VirtualGameInstance> VirtualGameInstances { get; set; }
        public DbSet<PortLease> PortLeases { get; set; }
        public DbSet<ProvisioningJob> ProvisioningJobs { get; set; }
        public DbSet<ProxmoxSettings> ProxmoxSettings { get; set; }
        
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            SetDefaultColumnTypes(modelBuilder);
            ConfigurePlatformModels(modelBuilder);
            base.OnModelCreating(modelBuilder);
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            var connectionString = Config.ConnectionString;

            if (connectionString == null)
            {
                connectionString = GetConnectionString();
            }

            optionsBuilder
                .UseSqlServer(
                    connectionString,
                    options => options.CommandTimeout(180)
                );

            if (Debugger.IsAttached)
            {
                optionsBuilder
                    .LogTo(p => { Debug.WriteLine(p); }, LogLevel.Information)
                    .EnableSensitiveDataLogging();
            }
        }

        private void SetDefaultColumnTypes(ModelBuilder modelBuilder)
        {
            foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(t => t.GetProperties()).Where(p => p.ClrType == typeof(string)))
            {
                var columnType = property.GetColumnType();
                var columnLength = property.GetMaxLength();

                if (columnType != BaseEntity<bool>.VarcharMaxColumnTypeName && columnType == null && columnLength == null)
                {
                    property.SetColumnType(defaultColumnTypeForString);
                }
            }
        }

        private void ConfigurePlatformModels(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<UserQuota>()
                .HasOne(p => p.User)
                .WithOne(p => p.UserQuota)
                .HasForeignKey<UserQuota>(p => p.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<VirtualGameInstance>()
                .HasOne(p => p.Owner)
                .WithMany(p => p.VirtualGameInstances)
                .HasForeignKey(p => p.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<VirtualGameInstance>()
                .HasOne(p => p.GameDefinition)
                .WithMany(p => p.VirtualGameInstances)
                .HasForeignKey(p => p.GameDefinitionId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<VirtualGameInstance>()
                .Property(p => p.InstanceKind)
                .HasConversion<string>();

            modelBuilder.Entity<VirtualGameInstance>()
                .Property(p => p.Status)
                .HasConversion<string>();

            modelBuilder.Entity<ProvisioningJob>()
                .HasOne(p => p.RequestedByUser)
                .WithMany(p => p.ProvisioningJobs)
                .HasForeignKey(p => p.RequestedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ProvisioningJob>()
                .HasOne(p => p.VirtualGameInstance)
                .WithMany(p => p.ProvisioningJobs)
                .HasForeignKey(p => p.VirtualGameInstanceId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<PortLease>()
                .HasOne(p => p.VirtualGameInstance)
                .WithMany(p => p.PortLeases)
                .HasForeignKey(p => p.VirtualGameInstanceId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ProvisioningJob>()
                .Property(p => p.Operation)
                .HasConversion<string>();

            modelBuilder.Entity<ProvisioningJob>()
                .Property(p => p.Status)
                .HasConversion<string>();

            modelBuilder.Entity<ProvisioningJob>()
                .HasIndex(p => new { p.VirtualGameInstanceId, p.Status })
                .HasDatabaseName("UX_ProvisioningJobs_ActivePerInstanceAndStatus")
                .HasFilter("[VirtualGameInstanceId] IS NOT NULL AND [Status] IN ('Queued','Running')")
                .IsUnique();
        }
        
        public override int SaveChanges()
        {
            SaveChangesBase();
            return base.SaveChanges();
        }

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            SaveChangesBase();
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        private void SaveChangesBase()
        {
            var inserted = ChangeTracker.Entries<IEntity>().Where(p => p.State == EntityState.Added).Select(p => p.Entity).ToList();
            var updated = ChangeTracker.Entries<IEntity>().Where(p => p.State == EntityState.Modified).Select(p => p.Entity).ToList();
            var deleted = ChangeTracker.Entries<IEntity>().Where(p => p.State == EntityState.Deleted).Select(p => p.Entity).ToList();

            var user = Config.UserHelper.GetUser()?.Login ?? "system";

            if (inserted.Any() || updated.Any() || deleted.Any())
            {
                var now = DateTime.UtcNow;

                foreach (IEntity entity in inserted)
                {
                    entity.CreatedDate = now;
                    entity.CreatedUser = user;
                    entity.ModifiedDate = now;
                    entity.ModifiedUser = user;
                }

                foreach (IEntity entity in updated)
                {
                    entity.ModifiedDate = now;
                    entity.ModifiedUser = user;
                }
            }
        }
        
        public virtual string GetConnectionString()
        {
            var envConnection = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
            if (!string.IsNullOrWhiteSpace(envConnection))
            {
                return envConnection;
            }

            throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
        }
    }
}
