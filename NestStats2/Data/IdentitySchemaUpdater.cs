using System.Data;
using Microsoft.EntityFrameworkCore;

namespace NestStats2.Data;

public sealed class IdentitySchemaUpdater
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<IdentitySchemaUpdater> _logger;

    public IdentitySchemaUpdater(ApplicationDbContext dbContext, ILogger<IdentitySchemaUpdater> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        await _dbContext.Database.EnsureCreatedAsync(cancellationToken);

        if (!string.Equals(_dbContext.Database.ProviderName, "Microsoft.EntityFrameworkCore.Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await CreateUserEnergySystemsTableAsync(cancellationToken);
        await EnsureAspNetUsersColumnsAsync(cancellationToken);
        await EnsureUserEnergySystemsColumnsAsync(cancellationToken);
    }

    private async Task CreateUserEnergySystemsTableAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS "UserEnergySystems" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_UserEnergySystems" PRIMARY KEY AUTOINCREMENT,
                "UserId" TEXT NOT NULL,
                "SnNumber" TEXT NOT NULL,
                "SystemName" TEXT NOT NULL DEFAULT '',
                "SystemAddress" TEXT NULL,
                "EncryptedPassword" TEXT NOT NULL DEFAULT '',
                "IsPrimary" INTEGER NOT NULL DEFAULT 0,
                "ConnectedUtc" TEXT NOT NULL DEFAULT '',
                "LastVerifiedUtc" TEXT NULL,
                CONSTRAINT "FK_UserEnergySystems_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
            );
            """;

        await _dbContext.Database.ExecuteSqlRawAsync(sql, cancellationToken);
        await _dbContext.Database.ExecuteSqlRawAsync(
            """CREATE UNIQUE INDEX IF NOT EXISTS "IX_UserEnergySystems_UserId_SnNumber" ON "UserEnergySystems" ("UserId", "SnNumber");""",
            cancellationToken);
        await _dbContext.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_UserEnergySystems_UserId" ON "UserEnergySystems" ("UserId");""",
            cancellationToken);
    }

    private async Task EnsureAspNetUsersColumnsAsync(CancellationToken cancellationToken)
    {
        await EnsureColumnAsync("AspNetUsers", "DisplayName", """ALTER TABLE "AspNetUsers" ADD COLUMN "DisplayName" TEXT NOT NULL DEFAULT '';""", cancellationToken);
        await EnsureColumnAsync("AspNetUsers", "PreferredProviderKey", """ALTER TABLE "AspNetUsers" ADD COLUMN "PreferredProviderKey" TEXT NOT NULL DEFAULT '';""", cancellationToken);
        await EnsureColumnAsync("AspNetUsers", "PreferredTariffKey", """ALTER TABLE "AspNetUsers" ADD COLUMN "PreferredTariffKey" TEXT NOT NULL DEFAULT '';""", cancellationToken);
        await EnsureColumnAsync("AspNetUsers", "PreferredSystemSn", """ALTER TABLE "AspNetUsers" ADD COLUMN "PreferredSystemSn" TEXT NOT NULL DEFAULT '';""", cancellationToken);
        await EnsureColumnAsync("AspNetUsers", "CreatedUtc", """ALTER TABLE "AspNetUsers" ADD COLUMN "CreatedUtc" TEXT NOT NULL DEFAULT '';""", cancellationToken);
        await EnsureColumnAsync("AspNetUsers", "LastSeenUtc", """ALTER TABLE "AspNetUsers" ADD COLUMN "LastSeenUtc" TEXT NULL;""", cancellationToken);
        await EnsureColumnAsync("AspNetUsers", "OnboardingCompletedUtc", """ALTER TABLE "AspNetUsers" ADD COLUMN "OnboardingCompletedUtc" TEXT NULL;""", cancellationToken);
    }

    private async Task EnsureUserEnergySystemsColumnsAsync(CancellationToken cancellationToken)
    {
        await EnsureColumnAsync("UserEnergySystems", "SystemName", """ALTER TABLE "UserEnergySystems" ADD COLUMN "SystemName" TEXT NOT NULL DEFAULT '';""", cancellationToken);
        await EnsureColumnAsync("UserEnergySystems", "SystemAddress", """ALTER TABLE "UserEnergySystems" ADD COLUMN "SystemAddress" TEXT NULL;""", cancellationToken);
        await EnsureColumnAsync("UserEnergySystems", "EncryptedPassword", """ALTER TABLE "UserEnergySystems" ADD COLUMN "EncryptedPassword" TEXT NOT NULL DEFAULT '';""", cancellationToken);
        await EnsureColumnAsync("UserEnergySystems", "IsPrimary", """ALTER TABLE "UserEnergySystems" ADD COLUMN "IsPrimary" INTEGER NOT NULL DEFAULT 0;""", cancellationToken);
        await EnsureColumnAsync("UserEnergySystems", "ConnectedUtc", """ALTER TABLE "UserEnergySystems" ADD COLUMN "ConnectedUtc" TEXT NOT NULL DEFAULT '';""", cancellationToken);
        await EnsureColumnAsync("UserEnergySystems", "LastVerifiedUtc", """ALTER TABLE "UserEnergySystems" ADD COLUMN "LastVerifiedUtc" TEXT NULL;""", cancellationToken);
    }

    private async Task EnsureColumnAsync(string tableName, string columnName, string alterSql, CancellationToken cancellationToken)
    {
        var columns = await GetColumnsAsync(tableName, cancellationToken);
        if (columns.Contains(columnName))
        {
            return;
        }

        _logger.LogInformation("Adding missing SQLite column {ColumnName} to {TableName}", columnName, tableName);
        await _dbContext.Database.ExecuteSqlRawAsync(alterSql, cancellationToken);
    }

    private async Task<HashSet<string>> GetColumnsAsync(string tableName, CancellationToken cancellationToken)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var connection = _dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;

        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"""PRAGMA table_info("{tableName}");""";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (!reader.IsDBNull(1))
                {
                    columns.Add(reader.GetString(1));
                }
            }
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }

        return columns;
    }
}
