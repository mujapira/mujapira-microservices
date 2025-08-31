using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Contracts.BugReport
{
    public interface IBugReportService
    {
        Task<BugReportDto> CreateAsync(CreateBugReportDto dto, Guid? reporterUserId, CancellationToken ct = default);
        Task<BugReportDto?> GetAsync(Guid id, CancellationToken ct = default);
        Task<(IReadOnlyList<BugReportDto> Items, int Total)> SearchAsync(BugReportSearchQuery query, CancellationToken ct = default);
        Task<bool> UpdateStatusAsync(Guid id, BugStatus status, Guid? assigneeUserId, CancellationToken ct = default);
    }
}
