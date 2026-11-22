using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace i25Core.Core.Entities
{
    /// <summary>A named group of permissions, such as "Admin". Users can have several roles.</summary>
    public class Role : BaseEntity
    {
        [MaxLength(64)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(256)]
        public string? Description { get; set; }

        [JsonIgnore]
        public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    }

    /// <summary>Links a user to a role.</summary>
    public class UserRole
    {
        public int UserId { get; set; }
        public int RoleId { get; set; }

        [JsonIgnore] public User? User { get; set; }
        [JsonIgnore] public Role? Role { get; set; }
    }
}
