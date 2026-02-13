using System.ComponentModel.DataAnnotations;

namespace KaizokuBackend.Migration.Models
{
    public class Setting
    {
        [Key]
        public string Name { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }
}
