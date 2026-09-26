using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ContosoDashboard.Models;

public class Document
{
    [Key]
    public int DocumentId { get; set; }

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    // One of DocumentCategories.All, stored as text
    [Required]
    [MaxLength(50)]
    public string Category { get; set; } = string.Empty;

    // Display/download name only - never used to build storage paths
    [Required]
    [MaxLength(255)]
    public string OriginalFileName { get; set; } = string.Empty;

    // MIME type from the server-side extension map (not the browser-supplied value)
    [Required]
    [MaxLength(255)]
    public string FileType { get; set; } = string.Empty;

    public long FileSize { get; set; }

    // Storage key: {userId}/{projectId|personal}/{guid}.{ext}
    [Required]
    [MaxLength(500)]
    public string FilePath { get; set; } = string.Empty;

    [Required]
    public int UploadedByUserId { get; set; }

    public int? ProjectId { get; set; }

    public DateTime UploadedDate { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedDate { get; set; } = DateTime.UtcNow;

    // Navigation properties
    [ForeignKey("UploadedByUserId")]
    public virtual User UploadedByUser { get; set; } = null!;

    [ForeignKey("ProjectId")]
    public virtual Project? Project { get; set; }

    public virtual ICollection<DocumentTag> Tags { get; set; } = new List<DocumentTag>();
    public virtual ICollection<DocumentShare> Shares { get; set; } = new List<DocumentShare>();
    public virtual ICollection<TaskDocument> TaskAttachments { get; set; } = new List<TaskDocument>();
}
