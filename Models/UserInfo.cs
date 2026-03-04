using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace webapp_miniproject.Models;

[Table("UserInfo")]
public class UserInfo : BaseModel
{
    [PrimaryKey("id", false)]
    public int Id { get; set; }

    [Column("username")]
    public string Username { get; set; } = "";

    [Column("password")]
    public string Password { get; set; } = "";

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
}