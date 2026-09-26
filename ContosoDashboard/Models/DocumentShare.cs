using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ContosoDashboard.Models;

/// <summary>
/// Grants read access to a document for a user or a team (department).
/// Exactly one of <see cref="SharedWithUserId"/> / <see cref="SharedWithDepartment"/> is set
/// (validated in service).
/// </summary>
public class DocumentShare
{
    [Key]
    public int DocumentShareId { get; set; }

    [Required]
    public int DocumentId { get; set; }

    // Set for a user share
    public int? SharedWithUserId { get; set; }

    // Set for a team share (department name)
    [MaxLength(100)]
    public string? SharedWithDepartment { get; set; }

    [Required]
    public int SharedByUserId { get; set; }

    public DateTime SharedDate { get; set; } = DateTime.UtcNow;

    // Navigation properties
    [ForeignKey("DocumentId")]
    public virtual Document Document { get; set; } = null!;

    [ForeignKey("SharedWithUserId")]
    public virtual User? SharedWithUser { get; set; }

    [ForeignKey("SharedByUserId")]
    public virtual User SharedByUser { get; set; } = null!;
}
