using iPrinterTool.Services;
using Microsoft.EntityFrameworkCore;

namespace iPrinterTool.Data;

/// <summary>
/// Manages SQLite database initialization and schema migrations at application startup.
/// Ensures idempotent, thread-safe execution and detailed diagnostics logging.
/// </summary>
public static class DatabaseInitializer
{
    private static readonly SemaphoreSlim _lock = new(1, 1);
    private static bool _isInitialized;

    public static bool IsInitialized => _isInitialized;

    /// <summary>
    /// Executes database migrations asynchronously during application startup.
    /// </summary>
    public static async Task InitializeAsync(Func<PrintLogDb>? dbFactory = null, CancellationToken ct = default)
    {
        if (_isInitialized) return;

        await _lock.WaitAsync(ct);
        try
        {
            if (_isInitialized) return;

            AppLogger.Info("Checking local database schema and migrations (SQLite)...");

            var factory = dbFactory ?? (() => new PrintLogDb());
            await using var db = factory();

            var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
            if (pending.Count > 0)
            {
                AppLogger.Info($"Applying {pending.Count} database migration(s): {string.Join(", ", pending)}");
                await db.Database.MigrateAsync(ct);
                AppLogger.Info("Database migrations applied successfully.");
            }
            else
            {
                AppLogger.Info("Database schema is up to date.");
            }

            _isInitialized = true;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Database initialization or migration failed", ex);
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Executes database migrations synchronously if required in a synchronous startup context.
    /// </summary>
    public static void Initialize(Func<PrintLogDb>? dbFactory = null)
    {
        if (_isInitialized) return;

        _lock.Wait();
        try
        {
            if (_isInitialized) return;

            AppLogger.Info("Checking local database schema and migrations (SQLite)...");

            var factory = dbFactory ?? (() => new PrintLogDb());
            using var db = factory();

            var pending = db.Database.GetPendingMigrations().ToList();
            if (pending.Count > 0)
            {
                AppLogger.Info($"Applying {pending.Count} database migration(s): {string.Join(", ", pending)}");
                db.Database.Migrate();
                AppLogger.Info("Database migrations applied successfully.");
            }
            else
            {
                AppLogger.Info("Database schema is up to date.");
            }

            _isInitialized = true;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Database initialization or migration failed", ex);
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }
}
