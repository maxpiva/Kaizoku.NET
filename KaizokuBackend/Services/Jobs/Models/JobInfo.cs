using System.Text.Json;
using KaizokuBackend.Models;

namespace KaizokuBackend.Services.Jobs.Models;

public class JobInfo
{
    public string JobId { get; }
    public string? Key { get; set; }
    public string? Parameters { get; set; }
    public JobType JobType { get; }
    

    public JobInfo(Guid id, JobType jobType, string? key, string? parameter = null)
    {
        Key = key;
        JobType = jobType;
        JobId = id.ToString().Replace("-","");
        Parameters = parameter;
    }




}