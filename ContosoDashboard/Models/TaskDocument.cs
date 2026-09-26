using System.ComponentModel.DataAnnotations.Schema;

namespace ContosoDashboard.Models;

/// <summary>
/// Links a document to a task. Composite key (TaskId, DocumentId) is configured in the DbContext.
/// Attaching never changes who can access the document.
/// </summary>
public class TaskDocument
{
    public int TaskId { get; set; }

    public int DocumentId { get; set; }

    public int AttachedByUserId { get; set; }

    public DateTime AttachedDate { get; set; } = DateTime.UtcNow;

    // Navigation properties
    [ForeignKey("TaskId")]
    public virtual TaskItem Task { get; set; } = null!;

    [ForeignKey("DocumentId")]
    public virtual Document Document { get; set; } = null!;

    [ForeignKey("AttachedByUserId")]
    public virtual User AttachedByUser { get; set; } = null!;
}
