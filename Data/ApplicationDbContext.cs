using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using NestStats2.Models;

namespace NestStats2.Data;

public sealed class ApplicationDbContext : IdentityDbContext<ApplicationUser, IdentityRole, string>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<UserEnergySystemAssignment> UserEnergySystems => Set<UserEnergySystemAssignment>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(x => x.DisplayName).HasMaxLength(160);
            entity.Property(x => x.PreferredProviderKey).HasMaxLength(64);
            entity.Property(x => x.PreferredTariffKey).HasMaxLength(64);
            entity.Property(x => x.PreferredSystemSn).HasMaxLength(128);
        });

        builder.Entity<UserEnergySystemAssignment>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.SnNumber).HasMaxLength(128).IsRequired();
            entity.Property(x => x.SystemName).HasMaxLength(256);
            entity.Property(x => x.SystemAddress).HasMaxLength(512);
            entity.Property(x => x.EncryptedPassword).IsRequired();
            entity.HasIndex(x => new { x.UserId, x.SnNumber }).IsUnique();
            entity.HasOne(x => x.User)
                .WithMany(x => x.EnergySystems)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
