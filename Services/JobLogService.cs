using iPrinterTool.Data;
using Microsoft.EntityFrameworkCore;

namespace iPrinterTool.Services;

public sealed record UserStat(string UserName, int Jobs, int Pages);

public sealed class JobLogService
{
    private readonly CanonJobLogClient _client;

    public JobLogService(CanonJobLogClient client)
    {
        _client = client;
        using var db = new PrintLogDb();
        db.Database.Migrate();
    }

    // Đọc Job Log từ máy in, chỉ insert các job chưa có. Trả về số job mới.
    public async Task<int> SyncAsync(CancellationToken ct = default)
    {
        var jobs = await _client.GetJobLogAsync(ct);
        if (jobs.Count == 0) return 0;

        var minStart = jobs.Min(j => j.Start);

        await using var db = new PrintLogDb();
        var existing = await db.Jobs
            .Where(j => j.Start >= minStart)
            .Select(j => new { j.JobNumber, j.Start })
            .ToListAsync(ct);
        var keys = existing.Select(e => (e.JobNumber, e.Start)).ToHashSet();

        var added = jobs
            .Where(j => !keys.Contains((j.JobNumber, j.Start)))
            .Select(j => new JobRecord
            {
                JobNumber = j.JobNumber,
                Result = j.Result,
                Start = j.Start,
                End = j.End,
                JobType = j.JobType,
                FileName = j.FileName,
                UserName = j.UserName,
                OriginalPages = j.OriginalPages,
                OutputPages = j.OutputPages,
                SheetsCopies = j.SheetsCopies,
                ErrorCode = j.ErrorCode
            })
            .ToList();

        db.Jobs.AddRange(added);
        await db.SaveChangesAsync(ct);
        return added.Count;
    }

    public async Task<List<JobRecord>> GetRecentAsync(int take = 300, CancellationToken ct = default)
    {
        await using var db = new PrintLogDb();
        return await db.Jobs.OrderByDescending(j => j.Start).Take(take).ToListAsync(ct);
    }

    public async Task<JobRecord?> GetLastPrinterJobAsync(CancellationToken ct = default)
    {
        await using var db = new PrintLogDb();
        return await db.Jobs
            .Where(j => j.JobType == "Printer")
            .OrderByDescending(j => j.Start)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<List<UserStat>> GetUserStatsAsync(int days, CancellationToken ct = default)
    {
        var from = DateTime.Now.AddDays(-days);

        await using var db = new PrintLogDb();
        return await db.Jobs
            .Where(j => j.JobType == "Printer" && j.Start >= from)
            .GroupBy(j => j.UserName)
            .OrderByDescending(g => g.Sum(j => j.OutputPages))
            .Select(g => new UserStat(g.Key, g.Count(), g.Sum(j => j.OutputPages)))
            .ToListAsync(ct);
    }
    public async Task RecordPaperOutAsync(CancellationToken ct = default)
    {
        await using var db = new PrintLogDb();
        var now = DateTime.Now;
        var from = now.AddHours(-3);

        var last = await db.Jobs
            .Where(j => j.JobType == "Printer")
            .OrderByDescending(j => j.Start)
            .FirstOrDefaultAsync(ct);

        var top = await db.Jobs
            .Where(j => j.JobType == "Printer" && j.Start >= from)
            .GroupBy(j => j.UserName)
            .Select(g => new { User = g.Key, Pages = g.Sum(j => j.OutputPages) })
            .OrderByDescending(x => x.Pages)
            .FirstOrDefaultAsync(ct);

        db.PaperEvents.Add(new PaperEvent
        {
            Time = now,
            LastUser = last?.UserName ?? "",
            LastFile = last?.FileName ?? "",
            LastPages = last?.OutputPages ?? 0,
            LastStart = last?.Start ?? default,
            TopUser3h = top?.User ?? "",
            TopPages3h = top?.Pages ?? 0
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<PaperEvent>> GetPaperEventsAsync(int take = 50, CancellationToken ct = default)
    {
        await using var db = new PrintLogDb();
        return await db.PaperEvents.OrderByDescending(e => e.Time).Take(take).ToListAsync(ct);
    }
}