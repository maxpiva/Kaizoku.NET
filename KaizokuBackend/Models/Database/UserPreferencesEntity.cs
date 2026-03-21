using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using KaizokuBackend.Models.Dto;

namespace KaizokuBackend.Models.Database
{
    public class UserPreferencesEntity
    {
        [Key]
        [ForeignKey(nameof(User))]
        public Guid UserId { get; set; }

        public string Theme { get; set; } = "dark";
        public string DefaultLanguage { get; set; } = "en";
        public string CardSize { get; set; } = "medium";
        public NsfwVisibility NsfwVisibility { get; set; } = NsfwVisibility.HideByDefault;

        public virtual UserEntity? User { get; set; }
    }
}
