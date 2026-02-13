using System.Text.Json;
using KaizokuBackend.Models.Enums;

namespace KaizokuBackend.Services.Jobs.Models;

public class JobInfo
{
    public string JobId { get; }
    public string? Key { get; set; }
    public string? Parameters { get; set; }
    public JobType JobType { get; }

    public string? GroupKey { get; set; }
    

    public JobInfo(Guid id, JobType jobType, string? key, string groupKey, string? parameter = null)
    {
        Key = key;
        JobType = jobType;
        JobId = id.ToString().Replace("-","");
        GroupKey = groupKey;
        Parameters = parameter;
    }




}