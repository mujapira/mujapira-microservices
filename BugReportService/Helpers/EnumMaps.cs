using BRContracts = Contracts.BugReport;

namespace BugReportService.Helpers;

public static class EnumMaps
{
    public static BRContracts.BugSeverity ToContract(this Models.BugSeverity s) => s switch
    {
        Models.BugSeverity.low => BRContracts.BugSeverity.Low,
        Models.BugSeverity.high => BRContracts.BugSeverity.High,
        _ => BRContracts.BugSeverity.Medium
    };

    public static Models.BugSeverity ToModel(this BRContracts.BugSeverity s) => s switch
    {
        BRContracts.BugSeverity.Low => Models.BugSeverity.low,
        BRContracts.BugSeverity.High => Models.BugSeverity.high,
        _ => Models.BugSeverity.medium
    };

    public static BRContracts.BugStatus ToContract(this Models.BugStatus s) => s switch
    {
        Models.BugStatus.open => BRContracts.BugStatus.Open,
        Models.BugStatus.triaged => BRContracts.BugStatus.Triaged,
        Models.BugStatus.in_progress => BRContracts.BugStatus.InProgress,
        Models.BugStatus.resolved => BRContracts.BugStatus.Resolved,
        Models.BugStatus.closed => BRContracts.BugStatus.Closed,
        _ => BRContracts.BugStatus.Open
    };

    public static Models.BugStatus ToModel(this BRContracts.BugStatus s) => s switch
    {
        BRContracts.BugStatus.Triaged => Models.BugStatus.triaged,
        BRContracts.BugStatus.InProgress => Models.BugStatus.in_progress,
        BRContracts.BugStatus.Resolved => Models.BugStatus.resolved,
        BRContracts.BugStatus.Closed => Models.BugStatus.closed,
        _ => Models.BugStatus.open
    };
}
