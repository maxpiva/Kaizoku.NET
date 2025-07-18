using KaizokuBackend.Models;
using KaizokuBackend.Models.Database;
using KaizokuBackend.Services.Import;
using KaizokuBackend.Services.Jobs.Models;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.Json;

namespace KaizokuBackend.Services.Jobs.Commands
{
    public class ImportSeries : ICommand
    {
        public JobType JobType => JobType.ImportSeries;
        public Type? ParameterType => null;
        private readonly ImportCommandService _service;
        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(ImportSeries))]
        public ImportSeries(ImportCommandService service)
        {
            _service = service;
        }

        public async Task<JobResult> ExecuteAsync(JobInfo job, CancellationToken token = default)
        {
            if (job.Parameters == null)
                return JobResult.Failed;
            bool disableJob = JsonSerializer.Deserialize<bool>(job.Parameters);
            return await _service.ImportSeriesAsync(job, disableJob, token).ConfigureAwait(false);
        }
    }
}
