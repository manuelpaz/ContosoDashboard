using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ContosoDashboard.Models;

/// <summary>
/// Audit record of a document action. It has no foreign key to <see cref="Document"/>
/// so the record outlives the document; title and file type are snapshots.
/// </summary>
public class DocumentActivity
{
    [Key]
    public int DocumentActivityId { get; set; }

    // Soft reference - the document may have been deleted
    public int DocumentId { get; set; }

    // Snapshot at time of action
    [Required]
    [MaxLength(200)]
    public string DocumentTitle { get; set; } = string.Empty;

    // Snapshot at time of action
    [Required]
    [MaxLength(255)]
    public string FileType { get; set; } = string.Empty;

    // One of DocumentActions
    [Required]
    [MaxLength(20)]
    public string Action { get; set; } = string.Empty;

    [Required]
    public int UserId { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [MaxLength(500)]
    public string? Details { get; set; }

    // Navigation properties
    [ForeignKey("UserId")]
    public virtual User User { get; set; } = null!;
}

public static class DocumentActions
{
    public const string Upload = "Upload";
    public const string Download = "Download";
    public const string Preview = "Preview";
    public const string Replace = "Replace";
    public const string Delete = "Delete";
    public const string Share = "Share";
}
