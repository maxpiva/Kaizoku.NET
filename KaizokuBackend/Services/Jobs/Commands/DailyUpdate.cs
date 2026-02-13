using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using KaizokuBackend.Models.Enums;
using KaizokuBackend.Services.Daily;
using KaizokuBackend.Services.Downloads;
using KaizokuBackend.Services.Jobs.Models;

namespace KaizokuBackend.Services.Jobs.Commands
{
    public class DailyUpdate : ICommand
    {
        public JobType JobType => JobType.DailyUpdate;
        public Type? ParameterType => null;
        private readonly DailyService _dailyService;

        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(DailyUpdate))]
        public DailyUpdate(DailyService dailyService)
        {
            _dailyService = dailyService;
        }

        public Task<JobResult> ExecuteAsync(JobInfo job, CancellationToken token = default)
        {
            return _dailyService.ExecuteAsync(job, token);
        }

    }
}
