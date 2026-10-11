using Kuechenfuchs.Api.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Kuechenfuchs.Api.Data;

public sealed class KuechenfuchsDbContext(
    DbContextOptions<KuechenfuchsDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Household> Households => Set<Household>();
    public DbSet<HouseholdMember> HouseholdMembers => Set<HouseholdMember>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(user => user.IsEnabled).IsRequired();
            entity.Property(user => user.CreatedAt).IsRequired();
        });

        builder.Entity<Household>(entity =>
        {
            entity.HasKey(household => household.Id);
            entity.Property(household => household.Name).HasMaxLength(120).IsRequired();
            entity.Property(household => household.CreatedAt).IsRequired();
        });

        builder.Entity<HouseholdMember>(entity =>
        {
            entity.HasKey(member => new { member.HouseholdId, member.UserId });
            entity.Property(member => member.Role).HasMaxLength(32).IsRequired();
            entity.HasIndex(member => member.UserId);
            entity.HasOne(member => member.Household)
                .WithMany(household => household.Members)
                .HasForeignKey(member => member.HouseholdId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(member => member.User)
                .WithMany()
                .HasForeignKey(member => member.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
