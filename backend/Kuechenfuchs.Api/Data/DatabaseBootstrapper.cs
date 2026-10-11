using Kuechenfuchs.Api.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Kuechenfuchs.Api.Data;

public static class DatabaseBootstrapper
{
    public static async Task InitializeAsync(
        IServiceProvider services,
        IConfiguration configuration,
        bool applyMigrations)
    {
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<KuechenfuchsDbContext>();

        if (applyMigrations)
        {
            await database.Database.MigrateAsync();
        }

        if (await database.Users.AnyAsync())
        {
            return;
        }

        var email = configuration["Bootstrap:AdminEmail"];
        var password = configuration["Bootstrap:AdminPassword"];
        var householdName = configuration["Bootstrap:HouseholdName"];
        if (string.IsNullOrWhiteSpace(email)
            || string.IsNullOrWhiteSpace(password)
            || string.IsNullOrWhiteSpace(householdName))
        {
            throw new InvalidOperationException(
                "The database has no users. Configure Bootstrap:AdminEmail, Bootstrap:AdminPassword and Bootstrap:HouseholdName to create the first administrator.");
        }

        await using var transaction = await database.Database.BeginTransactionAsync();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        const string administratorRole = "Administrator";
        if (!await roleManager.RoleExistsAsync(administratorRole))
        {
            var roleResult = await roleManager.CreateAsync(new IdentityRole<Guid>(administratorRole));
            EnsureSucceeded(roleResult, "administrator role");
        }

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            UserName = email.Trim(),
            Email = email.Trim(),
            EmailConfirmed = true,
        };
        EnsureSucceeded(await userManager.CreateAsync(user, password), "initial administrator");
        EnsureSucceeded(await userManager.AddToRoleAsync(user, administratorRole), "administrator role assignment");

        var household = new Household { Name = householdName.Trim() };
        database.Households.Add(household);
        database.HouseholdMembers.Add(new HouseholdMember
        {
            Household = household,
            User = user,
            Role = HouseholdRoles.Owner,
        });
        await database.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private static void EnsureSucceeded(IdentityResult result, string operation)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Unable to create the {operation}: {string.Join(", ", result.Errors.Select(error => error.Code))}");
        }
    }
}

public static class HouseholdRoles
{
    public const string Owner = "Owner";
    public const string Member = "Member";
}
