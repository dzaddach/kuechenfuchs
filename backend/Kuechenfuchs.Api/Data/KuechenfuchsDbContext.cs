using Microsoft.EntityFrameworkCore;

namespace Kuechenfuchs.Api.Data;

public sealed class KuechenfuchsDbContext(DbContextOptions<KuechenfuchsDbContext> options) : DbContext(options);
