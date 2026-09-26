using Microsoft.EntityFrameworkCore;
using ContosoDashboard.Data;
using ContosoDashboard.Models;

namespace ContosoDashboard.Services;

// ----- Supporting types -----

// Browser-supplied content type is intentionally not included: the MIME type is derived
// from the validated extension on the server.
public record DocumentUploadFile(string FileName, long Size, Stream Content);

public class DocumentUploadMetadata
{
    public string Category { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? ProjectId { get; set; }
    public List<string> Tags { get; set; } = new();

    // Per-file titles keyed by file name; missing titles default to the file name without extension
    public Dictionary<string, string> Titles { get; set; } = new();
}

public record DocumentUploadResult(string FileName, bool Succeeded, int? DocumentId, string? ErrorMessage);

public class DocumentUploadBatchResult
{
    public List<DocumentUploadResult> Results { get; } = new();
    public int SucceededCount => Results.Count(r => r.Succeeded);
}

public enum DocumentSortField
{
    Title,
    UploadedDate,
    Category,
    FileSize
}

public class DocumentQuery
{
    public DocumentSortField SortBy { get; set; } = DocumentSortField.UploadedDate;
    public bool Descending { get; set; } = true;
    public string? Category { get; set; }
    public int? ProjectId { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
}

public class DocumentMetadataUpdate
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Category { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
}

public record DocumentPermissions(bool CanEditMetadata, bool CanReplace, bool CanDelete, bool CanShare);

public record DocumentDetails(Document Document, DocumentPermissions Permissions, bool CanPreview);

public sealed class DocumentContent : IAsyncDisposable
{
    public DocumentContent(Stream content, string fileName, string contentType)
    {
        Content = content;
        FileName = fileName;
        ContentType = contentType;
    }

    public Stream Content { get; }
    public string FileName { get; }
    public string ContentType { get; }

    public ValueTask DisposeAsync() => Content.DisposeAsync();
}

public record DocumentShareResult(bool Succeeded, int RecipientsAdded, string? ErrorMessage);

// ----- Service -----

public interface IDocumentService
{
    // Upload
    Task<DocumentUploadBatchResult> UploadDocumentsAsync(
        IReadOnlyList<DocumentUploadFile> files, DocumentUploadMetadata metadata,
        int requestingUserId, int? taskId = null);

    // Browsing and search
    Task<List<Document>> GetMyDocumentsAsync(int requestingUserId, DocumentQuery query);
    Task<List<Document>> GetSharedWithMeAsync(int requestingUserId);
    Task<List<Document>> GetProjectDocumentsAsync(int projectId, int requestingUserId);
    Task<List<Document>> GetTaskDocumentsAsync(int taskId, int requestingUserId);
    Task<List<Document>> GetAttachableDocumentsAsync(int taskId, int requestingUserId);
    Task<List<Document>> SearchDocumentsAsync(string searchText, int requestingUserId);
    Task<List<Document>> GetRecentDocumentsAsync(int requestingUserId, int count = 5);
    Task<int> GetDocumentCountAsync(int requestingUserId);

    // Single document
    Task<DocumentDetails?> GetDocumentAsync(int documentId, int requestingUserId);
    Task<DocumentContent?> OpenDocumentAsync(int documentId, int requestingUserId, bool preview);

    // Management
    Task<bool> UpdateMetadataAsync(int documentId, DocumentMetadataUpdate update, int requestingUserId);
    Task<DocumentUploadResult> ReplaceFileAsync(int documentId, DocumentUploadFile file, int requestingUserId);
    Task<bool> DeleteDocumentAsync(int documentId, int requestingUserId);
    Task<DocumentShareResult> ShareDocumentAsync(int documentId, IReadOnlyList<int> userIds,
        IReadOnlyList<string> departments, int requestingUserId);
    Task<bool> AttachToTaskAsync(int documentId, int taskId, int requestingUserId);
}

public class DocumentService : IDocumentService
{
    private const long MaxFileSize = 26_214_400; // 25 MB
    private const int MaxFilesPerBatch = 10;
    private const int MaxTags = 10;
    private const int MaxTagLength = 50;
    private const int MaxTitleLength = 200;
    private const int MaxDescriptionLength = 2000;

    // Whitelisted extensions and the MIME type stored for each (never trust the browser's value)
    private static readonly Dictionary<string, string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".txt"] = "text/plain",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png"
    };

    private static readonly HashSet<string> PreviewableTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "image/jpeg",
        "image/png"
    };

    private readonly ApplicationDbContext _context;
    private readonly IFileStorageService _fileStorage;
    private readonly IMalwareScannerService _malwareScanner;
    private readonly INotificationService _notificationService;
    private readonly ITaskService _taskService;
    private readonly ILogger<DocumentService> _logger;

    public DocumentService(
        ApplicationDbContext context,
        IFileStorageService fileStorage,
        IMalwareScannerService malwareScanner,
        INotificationService notificationService,
        ITaskService taskService,
        ILogger<DocumentService> logger)
    {
        _context = context;
        _fileStorage = fileStorage;
        _malwareScanner = malwareScanner;
        _notificationService = notificationService;
        _taskService = taskService;
        _logger = logger;
    }

    // ----- Upload -----

    public async Task<DocumentUploadBatchResult> UploadDocumentsAsync(
        IReadOnlyList<DocumentUploadFile> files, DocumentUploadMetadata metadata,
        int requestingUserId, int? taskId = null)
    {
        var batch = new DocumentUploadBatchResult();
        if (files.Count == 0) return batch;

        var user = await GetUserAsync(requestingUserId);
        if (user == null)
        {
            return FailAll(batch, files, "Upload is not available.");
        }

        if (files.Count > MaxFilesPerBatch)
        {
            return FailAll(batch, files, $"You can upload up to {MaxFilesPerBatch} files at a time.");
        }

        // Metadata shared by every file in the batch is validated once
        var metadataError = ValidateCategoryAndDescription(metadata.Category, metadata.Description);
        var (tags, tagError) = NormalizeTags(metadata.Tags);
        metadataError ??= tagError;
        if (metadataError != null)
        {
            return FailAll(batch, files, metadataError);
        }

        var projectId = metadata.ProjectId;
        if (projectId.HasValue &&
            !await GetUserProjectIdsQuery(user.UserId).ContainsAsync(projectId.Value))
        {
            return FailAll(batch, files, "Project not available.");
        }

        // Each file succeeds or fails independently
        foreach (var file in files)
        {
            metadata.Titles.TryGetValue(file.FileName, out var requestedTitle);
            batch.Results.Add(await UploadSingleFileAsync(file, requestedTitle, metadata, tags, user, projectId));
        }

        return batch;
    }

    // Order: validate -> generate unique key -> save file -> save metadata (FR-010)
    private async Task<DocumentUploadResult> UploadSingleFileAsync(DocumentUploadFile file, string? requestedTitle,
        DocumentUploadMetadata metadata, List<string> tags, User user, int? projectId)
    {
        var title = string.IsNullOrWhiteSpace(requestedTitle)
            ? Path.GetFileNameWithoutExtension(file.FileName).Trim()
            : requestedTitle.Trim();

        var error = ValidateTitle(title) ?? await ValidateFileAsync(file);
        if (error != null)
        {
            return new DocumentUploadResult(file.FileName, false, null, error);
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var storageKey = BuildStorageKey(user.UserId, projectId, extension);
        var fileStored = false;
        Document? document = null;

        try
        {
            file.Content.Position = 0;
            await _fileStorage.UploadAsync(file.Content, storageKey, AllowedTypes[extension]);
            fileStored = true;

            document = new Document
            {
                Title = title,
                Description = string.IsNullOrWhiteSpace(metadata.Description) ? null : metadata.Description.Trim(),
                Category = metadata.Category,
                OriginalFileName = Path.GetFileName(file.FileName),
                FileType = AllowedTypes[extension],
                FileSize = file.Content.Length,
                FilePath = storageKey,
                UploadedByUserId = user.UserId,
                ProjectId = projectId,
                UploadedDate = DateTime.UtcNow,
                UpdatedDate = DateTime.UtcNow
            };

            foreach (var tag in tags)
            {
                document.Tags.Add(new DocumentTag { Tag = tag });
            }

            // Document and its activity record are saved atomically
            await using var transaction = await _context.Database.BeginTransactionAsync();
            _context.Documents.Add(document);
            await _context.SaveChangesAsync();
            AddActivity(document, DocumentActions.Upload, user.UserId);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return new DocumentUploadResult(file.FileName, true, document.DocumentId, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Document upload failed for user {UserId}", user.UserId);
            DetachPendingChanges();

            // The transaction was rolled back, so the document may be tracked as saved when it is not
            if (document != null)
            {
                foreach (var tag in document.Tags)
                {
                    _context.Entry(tag).State = EntityState.Detached;
                }

                _context.Entry(document).State = EntityState.Detached;
            }

            if (fileStored)
            {
                await TryDeleteFileAsync(storageKey);
            }

            return new DocumentUploadResult(file.FileName, false, null, "The upload failed. Please try again.");
        }
    }

    // ----- Browsing and search -----

    public async Task<List<Document>> GetMyDocumentsAsync(int requestingUserId, DocumentQuery query)
    {
        return await _context.Documents
            .AsNoTracking()
            .Include(d => d.Project)
            .Include(d => d.Tags)
            .Where(d => d.UploadedByUserId == requestingUserId)
            .OrderByDescending(d => d.UploadedDate)
            .ToListAsync();
    }

    public Task<List<Document>> GetSharedWithMeAsync(int requestingUserId)
    {
        throw new NotImplementedException();
    }

    public Task<List<Document>> GetProjectDocumentsAsync(int projectId, int requestingUserId)
    {
        throw new NotImplementedException();
    }

    public Task<List<Document>> GetTaskDocumentsAsync(int taskId, int requestingUserId)
    {
        throw new NotImplementedException();
    }

    public Task<List<Document>> GetAttachableDocumentsAsync(int taskId, int requestingUserId)
    {
        throw new NotImplementedException();
    }

    public Task<List<Document>> SearchDocumentsAsync(string searchText, int requestingUserId)
    {
        throw new NotImplementedException();
    }

    public Task<List<Document>> GetRecentDocumentsAsync(int requestingUserId, int count = 5)
    {
        throw new NotImplementedException();
    }

    public Task<int> GetDocumentCountAsync(int requestingUserId)
    {
        throw new NotImplementedException();
    }

    // ----- Single document -----

    public async Task<DocumentDetails?> GetDocumentAsync(int documentId, int requestingUserId)
    {
        var user = await GetUserAsync(requestingUserId);
        if (user == null) return null;

        var document = await GetAccessibleDocuments(user)
            .Include(d => d.UploadedByUser)
            .Include(d => d.Project)
            .Include(d => d.Tags)
            .FirstOrDefaultAsync(d => d.DocumentId == documentId);

        if (document == null) return null; // Not found and not authorized look the same

        var isProjectManager = document.Project?.ProjectManagerId == user.UserId;
        return new DocumentDetails(document, GetPermissions(document, user, isProjectManager),
            IsPreviewable(document.FileType));
    }

    public async Task<DocumentContent?> OpenDocumentAsync(int documentId, int requestingUserId, bool preview)
    {
        var user = await GetUserAsync(requestingUserId);
        if (user == null) return null;

        var document = await GetAccessibleDocuments(user)
            .FirstOrDefaultAsync(d => d.DocumentId == documentId);

        if (document == null) return null;
        if (preview && !IsPreviewable(document.FileType)) return null;

        Stream stream;
        try
        {
            stream = await _fileStorage.DownloadAsync(document.FilePath);
        }
        catch (FileNotFoundException)
        {
            _logger.LogWarning("File for document {DocumentId} is missing from storage", documentId);
            return null;
        }

        try
        {
            AddActivity(document, preview ? DocumentActions.Preview : DocumentActions.Download, user.UserId);
            await _context.SaveChangesAsync();
        }
        catch
        {
            await stream.DisposeAsync();
            DetachPendingChanges();
            throw;
        }

        return new DocumentContent(stream, document.OriginalFileName, document.FileType);
    }

    // ----- Management -----

    public Task<bool> UpdateMetadataAsync(int documentId, DocumentMetadataUpdate update, int requestingUserId)
    {
        throw new NotImplementedException();
    }

    public Task<DocumentUploadResult> ReplaceFileAsync(int documentId, DocumentUploadFile file, int requestingUserId)
    {
        throw new NotImplementedException();
    }

    public Task<bool> DeleteDocumentAsync(int documentId, int requestingUserId)
    {
        throw new NotImplementedException();
    }

    public Task<DocumentShareResult> ShareDocumentAsync(int documentId, IReadOnlyList<int> userIds,
        IReadOnlyList<string> departments, int requestingUserId)
    {
        throw new NotImplementedException();
    }

    public Task<bool> AttachToTaskAsync(int documentId, int taskId, int requestingUserId)
    {
        throw new NotImplementedException();
    }

    // ----- Helpers -----

    private static DocumentUploadBatchResult FailAll(DocumentUploadBatchResult batch,
        IReadOnlyList<DocumentUploadFile> files, string error)
    {
        foreach (var file in files)
        {
            batch.Results.Add(new DocumentUploadResult(file.FileName, false, null, error));
        }

        return batch;
    }

    private static string? ValidateTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "A title is required.";
        if (title.Length > MaxTitleLength) return $"Titles can be up to {MaxTitleLength} characters.";
        return null;
    }

    private static string? ValidateCategoryAndDescription(string category, string? description)
    {
        if (!DocumentCategories.All.Contains(category)) return "Please choose a category.";
        if (description?.Length > MaxDescriptionLength)
        {
            return $"Descriptions can be up to {MaxDescriptionLength} characters.";
        }

        return null;
    }

    // Size, extension whitelist and malware check (FR-002, FR-003, FR-008)
    private async Task<string?> ValidateFileAsync(DocumentUploadFile file)
    {
        var size = file.Content.CanSeek ? file.Content.Length : file.Size;
        if (size <= 0) return "The file is empty.";
        if (size > MaxFileSize) return "The file exceeds the 25 MB limit.";

        var extension = Path.GetExtension(file.FileName);
        if (!AllowedTypes.ContainsKey(extension))
        {
            return "Unsupported file type. Allowed: PDF, Word (.docx), Excel (.xlsx), PowerPoint (.pptx), text, JPEG, PNG.";
        }

        var scan = await _malwareScanner.ScanAsync(file.Content, extension);
        return scan.IsClean ? null : scan.Reason ?? "The file failed the security check.";
    }

    private async Task TryDeleteFileAsync(string storageKey)
    {
        try
        {
            await _fileStorage.DeleteAsync(storageKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not delete stored file {StorageKey}", storageKey);
        }
    }

    // After a failed save, stop tracking unsaved changes so the scoped context stays usable
    private void DetachPendingChanges()
    {
        foreach (var entry in _context.ChangeTracker.Entries()
                     .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                     .ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    // Role and department always come from the database, never from (possibly stale) claims
    private Task<User?> GetUserAsync(int userId) =>
        _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId);

    // Projects the user manages or is a member of (a manager is not always a ProjectMember row)
    private IQueryable<int> GetUserProjectIdsQuery(int userId) =>
        _context.Projects
            .Where(p => p.ProjectManagerId == userId || p.ProjectMembers.Any(pm => pm.UserId == userId))
            .Select(p => p.ProjectId);

    // Single source of truth for "which documents can this user see" (IDOR protection)
    private IQueryable<Document> GetAccessibleDocuments(User user)
    {
        if (user.Role == UserRole.Administrator)
        {
            return _context.Documents.AsNoTracking();
        }

        var userId = user.UserId;
        var department = string.IsNullOrEmpty(user.Department) ? null : user.Department;
        var isTeamLead = user.Role == UserRole.TeamLead && department != null;
        var projectIds = GetUserProjectIdsQuery(userId);

        return _context.Documents.AsNoTracking().Where(d =>
            d.UploadedByUserId == userId
            || (d.ProjectId != null && projectIds.Contains(d.ProjectId.Value))
            || d.Shares.Any(s => s.SharedWithUserId == userId
                || (department != null && s.SharedWithDepartment == department))
            || (isTeamLead && d.UploadedByUser.Department == department));
    }

    // Permission matrix (see data-model.md); viewing is decided by GetAccessibleDocuments
    private static DocumentPermissions GetPermissions(Document document, User user, bool isProjectManager)
    {
        var isUploader = document.UploadedByUserId == user.UserId;
        var isAdministrator = user.Role == UserRole.Administrator;

        return new DocumentPermissions(
            CanEditMetadata: isUploader || isProjectManager || isAdministrator,
            CanReplace: isUploader,
            CanDelete: isUploader || isProjectManager || isAdministrator,
            CanShare: isUploader);
    }

    private static bool IsPreviewable(string fileType) => PreviewableTypes.Contains(fileType);

    private static (List<string> Tags, string? Error) NormalizeTags(IEnumerable<string>? tags)
    {
        var normalized = (tags ?? Enumerable.Empty<string>())
            .Select(t => t?.Trim() ?? string.Empty)
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalized.Any(t => t.Length > MaxTagLength))
        {
            return (normalized, $"Tags can be up to {MaxTagLength} characters.");
        }

        if (normalized.Count > MaxTags)
        {
            return (normalized, $"You can add up to {MaxTags} tags.");
        }

        return (normalized, null);
    }

    // GUID-based key; the user-supplied file name is never part of the path
    private static string BuildStorageKey(int userId, int? projectId, string extension) =>
        $"{userId}/{(projectId?.ToString() ?? "personal")}/{Guid.NewGuid():N}{extension.ToLowerInvariant()}";

    // Adds (does not save) an activity record with title/type snapshots
    private void AddActivity(Document document, string action, int userId, string? details = null)
    {
        _context.DocumentActivities.Add(new DocumentActivity
        {
            DocumentId = document.DocumentId,
            DocumentTitle = document.Title,
            FileType = document.FileType,
            Action = action,
            UserId = userId,
            Timestamp = DateTime.UtcNow,
            Details = details
        });
    }
}
