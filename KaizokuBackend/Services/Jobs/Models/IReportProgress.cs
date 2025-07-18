using KaizokuBackend.Models;

namespace KaizokuBackend.Services.Jobs.Models;

public interface IReportProgress
{
    Task ReportProgressAsync(ProgressState state);
}