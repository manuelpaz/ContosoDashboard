---

description: "Task list for Document Upload and Management"
---

# Tasks: Document Upload and Management

**Input**: Design documents from `specs/001-document-upload-management/`
**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: Not requested by the spec. Verification is manual per [quickstart.md](quickstart.md)
(constitution: an xUnit project is only required when the spec asks for tests). Each story ends
with a validation task that lists the quickstart scenarios to run.

**Organization**: Tasks are grouped by user story (spec priorities P1–P7) so each story can be
implemented and validated independently.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: User story the task belongs to (US1–US7)
- All paths are relative to the repository root; application code lives in `ContosoDashboard/`

## Conventions for every task

- Follow existing code style: file-scoped `namespace ContosoDashboard.Models;` /
  `namespace ContosoDashboard.Services;`, data annotations for lengths, interface + implementation
  in the same service file, `async`/`await`, UTC `DateTime`.
- Every service method that touches documents takes `requestingUserId`, loads the requesting
  `User` from the database (role and department are never read from claims), and returns
  `null` / `false` / empty for denied **and** not-found alike (FR-017).
- Pages get the current user ID from the `ClaimTypes.NameIdentifier` claim exactly as
  `Pages/ProjectDetails.razor` does, carry `@attribute [Authorize]`, and never inject
  `ApplicationDbContext`.
- Do not change the security-header middleware in `Program.cs`. No new NuGet packages.
- `dotnet build` must finish with 0 warnings after every phase.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Configuration and repository hygiene needed before any code.

- [ ] T001 Add an `# Uploaded document files (local storage)` comment followed by the line `AppData/` to the end of `.gitignore` (repository root)
- [ ] T002 [P] Add a top-level section `"FileStorage": { "RootPath": "AppData/uploads" }` to `ContosoDashboard/appsettings.json` (keep all existing settings unchanged)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: All entities, the schema, storage, malware scanning and the `DocumentService`
skeleton. All five entities are created here (not per story) because the schema is built with
`EnsureCreated()`; adding tables later would force another database reset for every story.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [ ] T003 [P] Create `ContosoDashboard/Models/DocumentCategories.cs`: `public static class DocumentCategories` with `const string` fields `ProjectDocuments = "Project Documents"`, `TeamResources = "Team Resources"`, `PersonalFiles = "Personal Files"`, `Reports = "Reports"`, `Presentations = "Presentations"`, `Other = "Other"`, and `public static readonly IReadOnlyList<string> All` containing them in that order
- [ ] T004 [P] Create `ContosoDashboard/Models/Document.cs` with: `DocumentId` int `[Key]` ("Integer key (stakeholder constraint)"); `Title` `[Required][MaxLength(200)]` ("Required, trimmed, 1–200 chars"); `Description` `[MaxLength(2000)]` nullable ("Optional"); `Category` `[Required][MaxLength(50)]` ("one of `DocumentCategories.All` (stored as text)"); `OriginalFileName` `[Required][MaxLength(255)]` ("display/download name only, never used in paths"); `FileType` `[Required][MaxLength(255)]` ("MIME type from server-side extension map"); `FileSize` long ("1 … 26,214,400 bytes (25 MB)"); `FilePath` `[Required][MaxLength(500)]` ("unique; storage key `{userId}/{projectId|personal}/{guid}.{ext}`"); `UploadedByUserId` int required; `ProjectId` int?; `UploadedDate` and `UpdatedDate` DateTime defaulting to `DateTime.UtcNow`; navigation properties `UploadedByUser` (`[ForeignKey("UploadedByUserId")] User`, `= null!`), `Project` (`Project?`), `ICollection<DocumentTag> Tags`, `ICollection<DocumentShare> Shares`, `ICollection<TaskDocument> TaskAttachments` (all initialized to `new List<>()`)
- [ ] T005 [P] Create `ContosoDashboard/Models/DocumentTag.cs`: `DocumentTagId` int `[Key]`; `DocumentId` int required; `Tag` `[Required][MaxLength(50)]` ("Required; trimmed; case-insensitive duplicates removed"); navigation `Document`
- [ ] T006 [P] Create `ContosoDashboard/Models/DocumentShare.cs`: `DocumentShareId` int `[Key]`; `DocumentId` int required; `SharedWithUserId` int? ("Set for a user share"); `SharedWithDepartment` `[MaxLength(100)]` string? ("Set for a team share (department name)"); `SharedByUserId` int required; `SharedDate` DateTime UTC default; navigations `Document`, `SharedWithUser` (`User?`), `SharedByUser` (`User`). Add an XML comment: "exactly one of `SharedWithUserId` / `SharedWithDepartment` is set (validated in service)"
- [ ] T007 [P] Create `ContosoDashboard/Models/TaskDocument.cs`: `TaskId` int, `DocumentId` int (composite key configured in DbContext), `AttachedByUserId` int, `AttachedDate` DateTime UTC default; navigations `Task` (`TaskItem`), `Document`, `AttachedByUser` (`User`)
- [ ] T008 [P] Create `ContosoDashboard/Models/DocumentActivity.cs`: `DocumentActivityId` int `[Key]`; `DocumentId` int with **no** navigation/foreign key ("record outlives the document"); `DocumentTitle` `[Required][MaxLength(200)]` ("Snapshot at time of action"); `FileType` `[Required][MaxLength(255)]` ("Snapshot at time of action"); `Action` `[Required][MaxLength(20)]`; `UserId` int required with navigation `User`; `Timestamp` DateTime UTC default; `Details` `[MaxLength(500)]` string?. In the same file add `public static class DocumentActions` with `const string` values `Upload`, `Download`, `Preview`, `Replace`, `Delete`, `Share`
- [ ] T009 [P] Modify `ContosoDashboard/Models/Notification.cs`: add `[MaxLength(500)] public string? LinkUrl { get; set; }` ("app-relative URL (e.g. `/projects/1#documents`, `/documents/42`)") and append `DocumentShared, DocumentAdded` at the **end** of `enum NotificationType` (do not reorder existing values)
- [ ] T010 Modify `ContosoDashboard/Data/ApplicationDbContext.cs` (depends on T003–T009): add `DbSet<Document> Documents`, `DbSet<DocumentTag> DocumentTags`, `DbSet<DocumentShare> DocumentShares`, `DbSet<TaskDocument> TaskDocuments`, `DbSet<DocumentActivity> DocumentActivities` (each `= null!`), and in `OnModelCreating`: Document→User (UploadedBy) `OnDelete(DeleteBehavior.Restrict)`; Document→Project optional `OnDelete(DeleteBehavior.SetNull)`; DocumentTag→Document `Cascade`; DocumentShare→Document `Cascade`, →SharedWithUser `Restrict`, →SharedByUser `Restrict`; `TaskDocument` composite key `(TaskId, DocumentId)` with →TaskItem `Cascade`, →Document `Cascade`, →AttachedByUser `Restrict`; DocumentActivity→User `Restrict`. Indexes: Document `UploadedByUserId`, `ProjectId`, `Category`, `UploadedDate`, unique `FilePath`; DocumentTag unique `(DocumentId, Tag)` and `Tag`; DocumentShare `(DocumentId, SharedWithUserId)`, `(DocumentId, SharedWithDepartment)`, `SharedWithUserId`, `SharedWithDepartment`; DocumentActivity `(Action, Timestamp)`, `DocumentId`, `UserId`. Do not seed documents
- [ ] T011 [P] Create `ContosoDashboard/Services/FileStorageService.cs` per [contracts/service-interfaces.md](contracts/service-interfaces.md#ifilestorageservice-servicesfilestorageservicecs): interface `IFileStorageService` with `UploadAsync(Stream fileStream, string storageKey, string contentType)` → `Task<string>`, `DeleteAsync(string storageKey)`, `DownloadAsync(string storageKey)` → `Task<Stream>`, `GetUrlAsync(string storageKey, TimeSpan expiration)` → `Task<string>`; class `LocalFileStorageService` taking `IConfiguration` and `IWebHostEnvironment`, root = `Path.GetFullPath(Path.Combine(env.ContentRootPath, config["FileStorage:RootPath"] ?? "AppData/uploads"))`; a private `ResolvePath(storageKey)` that combines root + key (forward slashes → `Path.DirectorySeparatorChar`), calls `Path.GetFullPath`, and throws `InvalidOperationException` if the result does not start with root + separator; `UploadAsync` creates directories and writes with `FileMode.CreateNew`; `DeleteAsync` is a no-op if the file is missing; `DownloadAsync` returns a read-only `FileStream` (async, `FileShare.Read`) and throws `FileNotFoundException` if missing; `GetUrlAsync` throws `NotSupportedException("Local files are served only through the authorized document endpoints.")`
- [ ] T012 [P] Create `ContosoDashboard/Services/MalwareScannerService.cs` per [research.md R4](research.md#r4-malware-check-offline-clarified-signature-validation): `public record MalwareScanResult(bool IsClean, string? Reason);` interface `IMalwareScannerService { Task<MalwareScanResult> ScanAsync(Stream content, string extension); }`; class `FileSignatureMalwareScanner` that reads the header bytes and (1) rejects executables: `MZ`, `7F 45 4C 46`, Mach-O `FE ED FA CE`/`FE ED FA CF`/`CE FA ED FE`/`CF FA ED FE`/`CA FE BA BE`, and `#!`; (2) requires `%PDF-` for `.pdf`, `89 50 4E 47 0D 0A 1A 0A` for `.png`, `FF D8 FF` for `.jpg`/`.jpeg`; (3) for `.docx`/`.xlsx`/`.pptx` requires `50 4B 03 04`, opens `new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true)` and requires an entry `[Content_Types].xml`, at least one entry starting with `word/` / `xl/` / `ppt/` respectively, and **no** entry whose name ends with `vbaProject.bin`; invalid ZIP → not clean; (4) for `.txt` requires strict UTF-8 decoding (`new UTF8Encoding(false, true)`), no NUL bytes, and no control characters other than tab, CR, LF; any other extension → not clean. Reasons are user-facing (e.g. "The file content does not match a PDF document.", "Office files with macros are not allowed."). Always reset `content.Position = 0` before returning
- [ ] T013 [P] Modify `ContosoDashboard/Pages/Login.cshtml.cs`: after the `Role` claim add `new Claim("Department", user.Department)` only when `user.Department` is not null or empty (stakeholder requirement; services still read department from the database)
- [ ] T014 Create `ContosoDashboard/Services/DocumentService.cs` (depends on T010–T012) containing: (a) all supporting types from [contracts/service-interfaces.md](contracts/service-interfaces.md#supporting-types-same-file) — `DocumentUploadFile(string FileName, long Size, Stream Content)`, `DocumentUploadMetadata` (`Category`, `Description?`, `ProjectId?`, `List<string> Tags`, `Dictionary<string,string> Titles` keyed by file name), `DocumentUploadResult`, `DocumentUploadBatchResult` (`List<DocumentUploadResult> Results`, `int SucceededCount`), `DocumentSortField` enum (`Title`, `UploadedDate`, `Category`, `FileSize`), `DocumentQuery` (`SortBy` default `UploadedDate`, `Descending` default true, `Category?`, `ProjectId?`, `FromDate?`, `ToDate?`), `DocumentMetadataUpdate`, `DocumentPermissions`, `DocumentDetails`, `DocumentContent` (implements `IAsyncDisposable` disposing the stream), `DocumentShareResult`; (b) interface `IDocumentService` with **every** method signature listed in the contract; (c) class `DocumentService` with constructor dependencies `ApplicationDbContext`, `IFileStorageService`, `IMalwareScannerService`, `INotificationService`, `ITaskService`, `ILogger<DocumentService>`, every public method initially `throw new NotImplementedException();`, and these private helpers fully implemented: `const long MaxFileSize = 26_214_400`, `const int MaxFilesPerBatch = 10`, `const int MaxTags = 10`; `static readonly Dictionary<string,string> AllowedTypes` mapping `.pdf→application/pdf`, `.docx→application/vnd.openxmlformats-officedocument.wordprocessingml.document`, `.xlsx→application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`, `.pptx→application/vnd.openxmlformats-officedocument.presentationml.presentation`, `.txt→text/plain`, `.jpg`/`.jpeg→image/jpeg`, `.png→image/png` (case-insensitive keys); `GetUserAsync(int userId)`; `GetUserProjectIdsQuery(int userId)` = projects where `ProjectManagerId == userId` **or** a `ProjectMember` row exists; `GetAccessibleDocuments(User user)` returning `IQueryable<Document>` per [research.md R8](research.md#r8-authorization-model-and-data-source) (Administrator → all; else uploader, project member/manager, user share, department share, or TeamLead with same non-null department as uploader); `GetPermissions(Document doc, User user, bool isProjectManager)` per the [permission matrix](data-model.md#permission-matrix-per-document); `NormalizeTags(IEnumerable<string>)` (trim, drop empty, max 50 chars, case-insensitive distinct, max 10 else validation error); `BuildStorageKey(int userId, int? projectId, string extension)` = `$"{userId}/{(projectId?.ToString() ?? "personal")}/{Guid.NewGuid():N}{extension.ToLowerInvariant()}"`; `AddActivity(Document doc, string action, int userId, string? details = null)` that adds (does not save) a `DocumentActivity` with title/type snapshots
- [ ] T015 Modify `ContosoDashboard/Program.cs` (depends on T011, T012, T014): register `builder.Services.AddSingleton<IFileStorageService, LocalFileStorageService>();`, `builder.Services.AddSingleton<IMalwareScannerService, FileSignatureMalwareScanner>();`, `builder.Services.AddScoped<IDocumentService, DocumentService>();` next to the existing service registrations
- [ ] T016 Foundation check: from `ContosoDashboard/` run `rm -f ContosoDashboard.db ContosoDashboard.db-shm ContosoDashboard.db-wal`, `dotnet build` (0 warnings, 0 errors), start the app once and confirm with `sqlite3` or Python that tables `Documents`, `DocumentTags`, `DocumentShares`, `TaskDocuments`, `DocumentActivities` exist and `Notifications` has a `LinkUrl` column

**Checkpoint**: Foundation ready — user story implementation can begin.

---

## Phase 3: User Story 1 - Upload and retrieve my own documents (Priority: P1) 🎯 MVP

**Goal**: Users upload one or more validated files with metadata, see them in "My Documents",
open their details and download them.

**Independent Test**: Log in as Ni Kang, upload a PDF with title and category, see it in
My Documents with title, category, date, size and project, and download an identical file
(quickstart scenarios 1–3, 4 download part, 12).

### Implementation for User Story 1

- [ ] T017 [US1] Implement `UploadDocumentsAsync` in `ContosoDashboard/Services/DocumentService.cs` (ignore `taskId` for now — handled in T049): load user; if `files.Count` > `MaxFilesPerBatch` fail every file with "You can upload up to 10 files at a time."; validate batch metadata once — category in `DocumentCategories.All`, description ≤ 2000, tags via `NormalizeTags`, and if `ProjectId` is set it must be in `GetUserProjectIdsQuery(user)` (otherwise every file fails with "Project not available."). Then per file, independently: title = `Titles[FileName]` trimmed or file name without extension, required and ≤ 200 chars; size 0 → "The file is empty."; size > `MaxFileSize` → "The file exceeds the 25 MB limit."; extension not in `AllowedTypes` → "Unsupported file type. Allowed: PDF, Word (.docx), Excel (.xlsx), PowerPoint (.pptx), text, JPEG, PNG."; `ScanAsync` not clean → its reason; then key = `BuildStorageKey`, `UploadAsync`, create `Document` (FileType from `AllowedTypes`, `OriginalFileName` = `Path.GetFileName(FileName)`), tags, `AddActivity(Upload)`, `SaveChangesAsync`; on save failure delete the stored file, detach the entities and return a generic error; on unexpected exceptions log and return "The upload failed. Please try again." (no internals). Return per-file results and `SucceededCount`
- [ ] T018 [US1] Implement `GetMyDocumentsAsync` in `ContosoDashboard/Services/DocumentService.cs`: documents with `UploadedByUserId == requestingUserId`, `Include(Project)` and `Include(Tags)`, ordered by `UploadedDate` descending (the `DocumentQuery` filters/sorting are completed in T032)
- [ ] T019 [US1] Implement `GetDocumentAsync` and `OpenDocumentAsync` in `ContosoDashboard/Services/DocumentService.cs`: `GetDocumentAsync` loads the document through `GetAccessibleDocuments` (Include `UploadedByUser`, `Project`, `Tags`) and returns `DocumentDetails` with `Permissions` from `GetPermissions` (project manager = `doc.Project?.ProjectManagerId == userId`) and `CanPreview` = FileType is `application/pdf`, `image/jpeg` or `image/png`; `OpenDocumentAsync` returns null if inaccessible, or if `preview` is true and the type is not previewable; otherwise opens `DownloadAsync` (catch `FileNotFoundException` → log warning, return null), adds `Download` or `Preview` activity, saves, and returns `DocumentContent(stream, OriginalFileName, FileType)`
- [ ] T020 [P] [US1] Create `ContosoDashboard/Endpoints/DocumentEndpoints.cs`: `public static class DocumentEndpoints` with `public static IEndpointRouteBuilder MapDocumentEndpoints(this IEndpointRouteBuilder app)` mapping `GET /documents/{documentId:int}/download` with `.RequireAuthorization()` per [contracts/http-endpoints.md](contracts/http-endpoints.md): parse `ClaimTypes.NameIdentifier` (missing → `Results.NotFound()`), call `IDocumentService.OpenDocumentAsync(id, userId, preview: false)`, null → `Results.NotFound()`, else `Results.File(content.Content, content.ContentType, fileDownloadName: content.FileName)`
- [ ] T021 [US1] Modify `ContosoDashboard/Program.cs`: call `app.MapDocumentEndpoints();` after `app.UseAuthorization();` and before `app.MapBlazorHub();` (add `using ContosoDashboard.Endpoints;`)
- [ ] T022 [P] [US1] Create `ContosoDashboard/Shared/DocumentUpload.razor` per [contracts/ui-routes.md](contracts/ui-routes.md#new-shared-component): parameters `int? ProjectId`, `int? TaskId`, `EventCallback OnUploaded`; an "Upload Documents" button opening a Bootstrap modal (same markup style as the task modal in `Pages/Tasks.razor`) with `<InputFile multiple OnChange=... @key="inputKey">`, per selected file a title input defaulting to the name without extension, category `<select>` from `DocumentCategories.All` (required), description textarea, project `<select>` from `IProjectService.GetUserProjectsAsync(currentUserId)` (preselected and disabled when `ProjectId` is set; hidden when `TaskId` is set), tags input (comma-separated). On Upload: validate title/category client-side, then for each file capture `Name`/`Size` into locals **before** opening the stream, reject > 25 MB immediately, copy `OpenReadStream(26_214_400)` into a `MemoryStream` in 81,920-byte chunks updating a per-file progress bar and calling `StateHasChanged()`, set `Position = 0`, clear the `IBrowserFile` reference; call `IDocumentService.UploadDocumentsAsync(files, metadata, currentUserId, TaskId)`, show a success or error line per file, dispose the streams, change `inputKey` to reset `InputFile`, and invoke `OnUploaded` when `SucceededCount > 0`. Opening the modal, choosing files and clicking Upload must be the only clicks required (SC-001)
- [ ] T023 [US1] Create `ContosoDashboard/Pages/Documents.razor` (`@page "/documents"`, `[Authorize]`, title "My Documents"): header with `<DocumentUpload OnUploaded="LoadAsync" />`; table of `GetMyDocumentsAsync` results with columns Title (link to `/documents/{id}`), Category (badge), Uploaded (local date/time), Size (human-readable KB/MB), Project (name or "—"), and a Download link to `/documents/{id}/download`; "Loading..." and "You have not uploaded any documents yet." states like `Pages/Tasks.razor`
- [ ] T024 [US1] Create `ContosoDashboard/Pages/DocumentDetails.razor` (`@page "/documents/{DocumentId:int}"`, `[Authorize]`): load `GetDocumentAsync`; if null show an alert "Document not available." with a link back to `/documents` (never reveal title/owner); otherwise show title, description, category, tags, original file name, type, size, uploader, project (link to `/projects/{id}`), uploaded/updated dates and a Download button (`href="/documents/{id}/download"`). Leave clearly separated placeholders (`@* Preview/Edit/Replace/Delete: US4 *@`, `@* Share: US5 *@`) for later stories
- [ ] T025 [P] [US1] Modify `ContosoDashboard/Shared/NavMenu.razor`: add a "Documents" nav item (`href="documents"`, icon `bi bi-file-earmark-text`) after "My Projects"
- [ ] T026 [US1] Validate US1 using `specs/001-document-upload-management/quickstart.md`: `dotnet build` (0 warnings), reset the database, run quickstart scenarios 1, 2, 3, the download part of 4, and 12; confirm `AppData/uploads/{userId}/personal/` contains only GUID-named files and nothing for rejected uploads

**Checkpoint**: MVP — User Story 1 works on its own.

---

## Phase 4: User Story 2 - Share documents within a project (Priority: P2)

**Goal**: Project members see, download and upload project documents; members get one
notification per upload batch; the project manager can manage project documents.

**Independent Test**: Ni uploads to project 1; Floris and Camille see the document on the project
page and get exactly one notification per batch; a non-member cannot see it (quickstart 5–7, 12).

### Implementation for User Story 2

- [ ] T027 [US2] Implement `GetProjectDocumentsAsync` in `ContosoDashboard/Services/DocumentService.cs`: return empty unless the project id is in `GetUserProjectIdsQuery(user)` or the user is Administrator; documents with that `ProjectId` (Include `UploadedByUser`), newest first
- [ ] T028 [US2] Extend `UploadDocumentsAsync` in `ContosoDashboard/Services/DocumentService.cs`: after the batch, if `SucceededCount > 0` and a project is set, create **one** notification per distinct recipient (project manager + all `ProjectMember` users, excluding the uploader) via `INotificationService.CreateNotificationAsync` with `Type = NotificationType.DocumentAdded`, `Priority = NotificationPriority.Informational`, `Title = "New project documents"`, `Message = "{uploader.DisplayName} added {n} document(s) to {project.Name}."`, `LinkUrl = "/projects/{projectId}#documents"` (clarification Q4)
- [ ] T029 [US2] Modify `ContosoDashboard/Pages/ProjectDetails.razor`: inject `IDocumentService`; add a card section `<div id="documents">` titled "Documents" containing `<DocumentUpload ProjectId="ProjectId" OnUploaded="LoadDocumentsAsync" />` and a table (title → `/documents/{id}`, category, uploader, date, size, Download link) from `GetProjectDocumentsAsync`, with a "No documents yet." empty state
- [ ] T030 [P] [US2] Modify `ContosoDashboard/Pages/Notifications.razor`: when `notification.LinkUrl` is not null render an "Open" link (`<a href="@notification.LinkUrl" class="btn btn-sm btn-link">Open</a>`) next to the existing notification actions
- [ ] T031 [US2] Validate US2 using `specs/001-document-upload-management/quickstart.md`: run quickstart scenarios 5, 6 and 7. All seeded non-admin users belong to project 1, so for the non-member check (Story 2 scenario 4) stop the app, run `sqlite3 ContosoDashboard.db "DELETE FROM ProjectMembers WHERE UserId = 4;"` (or the Python `sqlite3` module), start the app, and confirm Ni Kang no longer sees project 1 documents in lists, search or `/documents/{id}` (404 on download) while still seeing their own uploads; reset the database afterwards

**Checkpoint**: User Stories 1 and 2 work independently.

---

## Phase 5: User Story 3 - Find documents quickly (Priority: P3)

**Goal**: Sort and filter My Documents; search all accessible documents.

**Independent Test**: Apply each sort/filter on My Documents and search by tag, uploader name and
project; only accessible matches appear (quickstart 13–14).

### Implementation for User Story 3

- [ ] T032 [US3] Complete `GetMyDocumentsAsync` in `ContosoDashboard/Services/DocumentService.cs` to apply `DocumentQuery` in the database: optional `Category` equality, `ProjectId` equality, `FromDate` (≥ start of day UTC) and `ToDate` (< start of next day UTC), then sort by `SortBy` (`Title`, `UploadedDate`, `Category`, `FileSize`) ascending/descending
- [ ] T033 [US3] Implement `SearchDocumentsAsync` in `ContosoDashboard/Services/DocumentService.cs`: trim input; empty → empty list; escape `\`, `%`, `_` with `\` and build `pattern = $"%{escaped}%"`; on top of `GetAccessibleDocuments(user)` filter with `EF.Functions.Like(d.Title, pattern, "\\")` OR description OR `d.Tags.Any(t => EF.Functions.Like(t.Tag, pattern, "\\"))` OR uploader `DisplayName` OR `Project.Name`; Include `UploadedByUser` and `Project`; order by `UploadedDate` desc; `Take(200)` ([research.md R9](research.md#r9-search-and-filtering-on-sqlite))
- [ ] T034 [US3] Modify `ContosoDashboard/Pages/Documents.razor`: add a filter row (Category select with "All", Project select from `IProjectService.GetUserProjectsAsync`, From/To date inputs, Sort by select with Title/Upload date/Category/File size, direction select) and an "Apply" button that reloads with a `DocumentQuery`; add a search box + button that navigates to `/documents/search?q={Uri.EscapeDataString(text)}`
- [ ] T035 [P] [US3] Create `ContosoDashboard/Pages/DocumentSearch.razor` (`@page "/documents/search"`, `[Authorize]`): `[SupplyParameterFromQuery(Name = "q")] public string? Query`; search box pre-filled with `Query`; results table (title → `/documents/{id}`, category, uploader, project, date, size) from `SearchDocumentsAsync`; "No documents found." when the result is empty and a query was given
- [ ] T036 [US3] Validate US3 using `specs/001-document-upload-management/quickstart.md`: run quickstart scenarios 13 and 14 (including a mixed-case query and a query containing `%`)

**Checkpoint**: User Stories 1–3 work independently.

---

## Phase 6: User Story 4 - Preview, edit, replace and delete documents (Priority: P4)

**Goal**: Preview PDFs/images, edit metadata, replace files, delete with confirmation — each
limited by the permission matrix.

**Independent Test**: Upload, preview, edit title/tags, replace the file and delete after
confirming; unauthorized users see no actions and forced calls fail (quickstart 4, 8–10, 15–16).

### Implementation for User Story 4

- [ ] T037 [P] [US4] Modify `ContosoDashboard/Endpoints/DocumentEndpoints.cs`: add `GET /documents/{documentId:int}/preview` with `.RequireAuthorization()` calling `OpenDocumentAsync(id, userId, preview: true)`; null → `Results.NotFound()`; otherwise `Results.File(content.Content, content.ContentType)` **without** `fileDownloadName` and set `Content-Disposition: inline; filename="..."` via `httpContext.Response.Headers.ContentDisposition` using `System.Net.Mime.ContentDisposition { Inline = true, FileName = ... }.ToString()`
- [ ] T038 [US4] Implement `UpdateMetadataAsync` in `ContosoDashboard/Services/DocumentService.cs`: load via `GetAccessibleDocuments`; require `Permissions.CanEditMetadata` (uploader, project manager of the document's project, Administrator — clarification Q3); validate title (required, ≤ 200), description (≤ 2000), category (in `DocumentCategories.All`), tags (`NormalizeTags`); replace the tag rows; set `UpdatedDate`; save; return false on any failure
- [ ] T039 [US4] Implement `ReplaceFileAsync` in `ContosoDashboard/Services/DocumentService.cs`: require uploader (`CanReplace`); run the same size/extension/scan validation as T017; new key via `BuildStorageKey(doc.UploadedByUserId, doc.ProjectId, ext)`; `UploadAsync` the new file; update `FilePath`, `FileSize`, `FileType`, `OriginalFileName`, `UpdatedDate`; `AddActivity(Replace, details: new file name)`; save; on save failure delete the **new** file and return an error; on success delete the **old** file (log a warning if that fails)
- [ ] T040 [US4] Implement `DeleteDocumentAsync` in `ContosoDashboard/Services/DocumentService.cs`: require `CanDelete` (uploader, project manager, Administrator); `AddActivity(Delete)` (snapshot kept — clarification Q1); remove the `Document` (tags, shares and task attachments cascade); save; then `DeleteAsync` the file (log a warning if it fails)
- [ ] T041 [US4] Modify `ContosoDashboard/Pages/DocumentDetails.razor` (replace the US4 placeholder): Preview button when `CanPreview` — images open a Bootstrap modal with `<img src="/documents/{id}/preview" class="img-fluid">`, PDFs use `<a href="/documents/{id}/preview" target="_blank" rel="noopener">`; "Edit details" form (title, description, category, tags) when `CanEditMetadata`; "Replace file" with a single `InputFile` (same MemoryStream/`@key` pattern as T022) when `CanReplace`; "Delete" button when `CanDelete` that opens a confirmation modal ("Delete permanently? This cannot be undone.") with Cancel and Delete, navigating to `/documents` after success; show success/error alerts; reload details after edits. No iframes (X-Frame-Options stays `DENY`)
- [ ] T042 [US4] Validate US4 using `specs/001-document-upload-management/quickstart.md`: run quickstart scenarios 4 (preview), 8, 9, 10, 15 and 16; confirm the replaced and deleted files are gone from `AppData/uploads`

**Checkpoint**: User Stories 1–4 work independently.

---

## Phase 7: User Story 5 - Share a document with specific people or teams (Priority: P5)

**Goal**: Uploaders share documents with users or departments; recipients are notified and see
them in "Shared with Me" with view/download only.

**Independent Test**: Ni shares a personal document with Administrator; Administrator is
notified and sees it in Shared with Me; Camille (not a recipient) cannot (quickstart 11).

### Implementation for User Story 5

- [ ] T043 [US5] Implement `ShareDocumentAsync` in `ContosoDashboard/Services/DocumentService.cs`: require `CanShare` (uploader only); validate that each user id exists and each department equals an existing `User.Department`; compute the set of users who already have access (reuse `GetAccessibleDocuments` per candidate or equivalent) **before** adding shares; skip the uploader and existing user/department shares; add `DocumentShare` rows (exactly one of `SharedWithUserId` / `SharedWithDepartment` set, `SharedByUserId`, `SharedDate`); `AddActivity(Share, details: "Users: …; Departments: …")`; save; notify each newly granted user once (department members expanded now, uploader and users who already had access excluded) with `Type = NotificationType.DocumentShared`, `Title = "Document shared with you"`, `Message = "{sharer} shared \"{title}\" with you."`, `LinkUrl = "/documents/{id}"`; return `DocumentShareResult`
- [ ] T044 [US5] Implement `GetSharedWithMeAsync` in `ContosoDashboard/Services/DocumentService.cs`: documents not uploaded by the user that have a share with `SharedWithUserId == userId` or `SharedWithDepartment == user.Department` (non-null), Include `UploadedByUser` and `Project`, newest first
- [ ] T045 [P] [US5] Create `ContosoDashboard/Pages/SharedDocuments.razor` (`@page "/documents/shared"`, `[Authorize]`, title "Shared with Me"): table (title → `/documents/{id}`, shared by/uploader, category, date, size, Download link) and "Nothing has been shared with you yet." empty state
- [ ] T046 [US5] Modify `ContosoDashboard/Pages/DocumentDetails.razor` (replace the US5 placeholder): when `CanShare`, a "Share" card with a multi-select of users from `IUserService.GetAllUsersAsync()` excluding the current user, a multi-select of distinct non-empty departments from the same list, and a Share button calling `ShareDocumentAsync`; show the result message
- [ ] T047 [P] [US5] Modify `ContosoDashboard/Shared/NavMenu.razor`: add "Shared with Me" (`href="documents/shared"`, icon `bi bi-share`) after "Documents"
- [ ] T048 [US5] Validate US5 using `specs/001-document-upload-management/quickstart.md`: run quickstart scenario 11 plus a repeat share to the same recipient (no duplicate notification) and a share with the uploader's own name excluded from the list

**Checkpoint**: User Stories 1–5 work independently.

---

## Phase 8: User Story 6 - Documents in tasks and on the dashboard (Priority: P6)

**Goal**: Task detail page with attachments and upload; dashboard document count and Recent
Documents widget.

**Independent Test**: Open a project task, upload from it (auto-associated with the project),
attach an existing project document; dashboard shows the count and last 5 uploads
(quickstart 17–18).

### Implementation for User Story 6

- [ ] T049 [US6] Extend `UploadDocumentsAsync` in `ContosoDashboard/Services/DocumentService.cs` for `taskId`: load the task with `ITaskService.GetTaskByIdAsync(taskId, userId)`; null → every file fails with "Task not available."; set the batch project to `task.ProjectId` (overriding `metadata.ProjectId`, skipping the project-membership check because task access is already verified); for each successful file add a `TaskDocument { TaskId, DocumentId, AttachedByUserId, AttachedDate }` in the same save (FR-030)
- [ ] T050 [US6] Implement `GetTaskDocumentsAsync`, `GetAttachableDocumentsAsync` and `AttachToTaskAsync` in `ContosoDashboard/Services/DocumentService.cs`: all verify task access with `ITaskService.GetTaskByIdAsync`; `GetTaskDocumentsAsync` = documents attached to the task intersected with `GetAccessibleDocuments(user)`; `GetAttachableDocumentsAsync` = if the task has a project, accessible documents with that `ProjectId`, otherwise documents uploaded by the user, excluding ones already attached; `AttachToTaskAsync` enforces the same rule (clarification Q2), rejects duplicates, adds the `TaskDocument` and never creates shares
- [ ] T051 [US6] Implement `GetRecentDocumentsAsync` (documents uploaded by the user, newest first, `Take(count)`) and `GetDocumentCountAsync` (count uploaded by the user) in `ContosoDashboard/Services/DocumentService.cs`
- [ ] T052 [P] [US6] Modify `ContosoDashboard/Services/DashboardService.cs`: add `public int TotalDocuments { get; set; }` to `DashboardSummary` and set it in `GetDashboardSummaryAsync` to `await _context.Documents.CountAsync(d => d.UploadedByUserId == userId)`
- [ ] T053 [US6] Create `ContosoDashboard/Pages/TaskDetails.razor` (`@page "/tasks/{TaskId:int}"`, `[Authorize]`): load the task with `ITaskService.GetTaskByIdAsync`; null → "Task not available." with a link to `/tasks`; otherwise show title, description, status, priority, due date, assignee, project (link to `/projects/{id}`); an "Attached documents" table from `GetTaskDocumentsAsync` (title → `/documents/{id}`, Download link); an "Attach existing" select from `GetAttachableDocumentsAsync` with an Attach button; and `<DocumentUpload TaskId="TaskId" OnUploaded="ReloadAsync" />`
- [ ] T054 [P] [US6] Modify `ContosoDashboard/Pages/Tasks.razor`: render each task title as a link to `/tasks/{task.TaskId}`
- [ ] T055 [US6] Modify `ContosoDashboard/Pages/Index.razor`: add a summary card "Documents" showing `summary.TotalDocuments` in the existing cards row (same markup as the other cards), and a "Recent Documents" card listing `GetRecentDocumentsAsync(userId, 5)` (title → `/documents/{id}`, upload date) with a "View all" link to `/documents` and an empty state
- [ ] T056 [US6] Validate US6 using `specs/001-document-upload-management/quickstart.md`: run quickstart scenarios 17 and 18, plus a task without a project (only own documents attachable)

**Checkpoint**: User Stories 1–6 work independently.

---

## Phase 9: User Story 7 - Activity tracking and administrator reports (Priority: P7)

**Goal**: Administrators see upload/access reports built from the activity log (which already
records every action since US1).

**Independent Test**: After uploads, downloads, shares and a delete, the Administrator sees them
in the reports (including the deleted document by title); other roles are denied
(quickstart 19–20).

### Implementation for User Story 7

- [ ] T057 [P] [US7] Create `ContosoDashboard/Services/DocumentReportService.cs` per [contracts/service-interfaces.md](contracts/service-interfaces.md#idocumentreportservice-servicesdocumentreportservicecs): records `FileTypeCount(string FileType, int Count)`, `UploaderCount(string DisplayName, int Count)`, `DocumentAccessCount(string DocumentTitle, int Accesses)`, `DailyAccessCount(DateOnly Date, int Count)` and `DocumentReport(List<FileTypeCount> UploadsByFileType, List<UploaderCount> TopUploaders, List<DocumentAccessCount> TopAccessedDocuments, List<DailyAccessCount> DailyAccesses)`; interface `IDocumentReportService.GetReportAsync(int periodDays, int requestingUserId)`; implementation loads the user from the database and returns null unless `Role == UserRole.Administrator`; clamp `periodDays` to 7, 30 or 90 (default 30); `from = DateTime.UtcNow.AddDays(-periodDays)`; uploads by type = `Upload` activities grouped by `FileType`; top 10 uploaders = `Upload` activities grouped by `UserId` joined to `Users.DisplayName`; top 10 accessed = `Download` + `Preview` grouped by `DocumentId` using the most recent `DocumentTitle`; daily accesses = `Download` + `Preview` projected to `Timestamp` and grouped by date in memory (training volumes)
- [ ] T058 [US7] Modify `ContosoDashboard/Program.cs`: register `builder.Services.AddScoped<IDocumentReportService, DocumentReportService>();`
- [ ] T059 [US7] Create `ContosoDashboard/Pages/DocumentReports.razor` (`@page "/documents/reports"`, `@attribute [Authorize(Policy = "Administrator")]`, title "Document Reports"): period select (7/30/90 days), and four tables (Uploads by file type, Top uploaders, Most accessed documents, Daily accesses) with empty states
- [ ] T060 [P] [US7] Modify `ContosoDashboard/Shared/NavMenu.razor`: add `<AuthorizeView Roles="Administrator">` wrapping a "Document Reports" item (`href="documents/reports"`, icon `bi bi-bar-chart`)
- [ ] T061 [US7] Validate US7 using `specs/001-document-upload-management/quickstart.md`: run quickstart scenarios 19 and 20

**Checkpoint**: All user stories are functional.

---

## Phase 10: Polish & Cross-Cutting Concerns

- [ ] T062 [P] Update `README.md`: add "Document Upload and Management" to Implemented Features; add rows for `/documents`, `/documents/shared`, `/documents/search`, `/documents/{id}`, `/documents/reports` (Administrator) and `/tasks/{id}` to the Application Pages table; add the new Models/Services/Endpoints/Pages/Shared files to Project Structure; add Known Limitations bullets: "Document search is case-insensitive for ASCII letters only (SQLite)", "Schema changes require deleting the local SQLite database (`EnsureCreated()`)", "Up to 10 files per upload; legacy (.doc/.xls/.ppt) and macro-enabled Office files are not accepted"; mention that uploaded files are stored in `ContosoDashboard/AppData/uploads` (git-ignored). Keep the existing "Malware Scanning (Training Implementation)" subsection unchanged
- [ ] T063 [P] Constitution compliance review (no code changes expected): `grep -rn "ApplicationDbContext" ContosoDashboard/Pages ContosoDashboard/Shared` returns nothing; every new `.razor` page has `[Authorize]`; the security-header block in `ContosoDashboard/Program.cs` is unchanged (`git diff main -- ContosoDashboard/Program.cs` shows only DI registrations and `MapDocumentEndpoints`); no new `PackageReference` in `ContosoDashboard/ContosoDashboard.csproj`; no `NotImplementedException` remains in `ContosoDashboard/Services/DocumentService.cs`
- [ ] T064 Final validation: delete the local database and `AppData/uploads`, `dotnet build` (0 warnings, 0 errors), run every scenario in [quickstart.md](quickstart.md) including performance spot checks and the offline check, and the README "Testing Security Features" scenarios

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: none.
- **Foundational (Phase 2)**: after Setup — **blocks all user stories**.
- **User Stories (Phases 3–9)**: all after Foundational. US1 must be done before the others
  because it creates the shared upload component, endpoints and pages they extend.
- **Polish (Phase 10)**: after the desired stories.

### User Story Dependencies

| Story | Depends on | Why |
|-------|------------|-----|
| US1 (P1) | Foundational | — |
| US2 (P2) | US1 | Reuses `DocumentUpload` and the upload method |
| US3 (P3) | US1 | Extends `Documents.razor` and `GetMyDocumentsAsync` |
| US4 (P4) | US1 | Extends `DocumentDetails.razor` and the endpoints file |
| US5 (P5) | US1 | Extends `DocumentDetails.razor` |
| US6 (P6) | US1 (US2 recommended for project documents to attach) | Reuses `DocumentUpload`; project attachments need project documents |
| US7 (P7) | US1 | Reads activity recorded since US1 |

After US1, US2–US7 are independent of each other **except** that US4 and US5 both edit
`DocumentDetails.razor` and every story edits `DocumentService.cs` — do those edits sequentially.

### Within Each Story

Service methods → endpoints → shared components → pages → navigation → validation.

## Parallel Opportunities

- Phase 1: T001 ∥ T002.
- Phase 2: T003–T009 (seven model files) ∥ T011 ∥ T012 ∥ T013; then T010 → T014 → T015 → T016.
- US1: T020 (endpoints) ∥ T022 (upload component) ∥ T025 (nav) while T017–T019 are done in
  `DocumentService.cs`.
- US2: T030 ∥ T027–T029. US3: T035 ∥ T032–T034. US4: T037 ∥ T038–T040.
  US5: T045 ∥ T047 ∥ T043–T044. US6: T052 ∥ T054 ∥ T049–T051. US7: T057 ∥ T060.
- Polish: T062 ∥ T063.

## Parallel Example: User Story 1

```bash
# While one agent implements DocumentService upload/list/open (T017 → T018 → T019):
Task: "T020 Create ContosoDashboard/Endpoints/DocumentEndpoints.cs (download endpoint)"
Task: "T022 Create ContosoDashboard/Shared/DocumentUpload.razor (upload modal with progress)"
Task: "T025 Add Documents link to ContosoDashboard/Shared/NavMenu.razor"
```

## Parallel Example: Foundational

```bash
Task: "T003 DocumentCategories.cs"  Task: "T004 Document.cs"      Task: "T005 DocumentTag.cs"
Task: "T006 DocumentShare.cs"       Task: "T007 TaskDocument.cs"  Task: "T008 DocumentActivity.cs"
Task: "T009 Notification.cs"        Task: "T011 FileStorageService.cs"
Task: "T012 MalwareScannerService.cs"  Task: "T013 Login.cshtml.cs Department claim"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1 Setup → Phase 2 Foundational (reset the database once).
2. Phase 3 US1 → **stop and validate** (T026). Commit.
3. This already delivers the core value: a secure place to upload, list and download documents.

### Incremental Delivery

1. Setup + Foundational → foundation ready.
2. US1 → validate → commit (MVP).
3. US2 → validate → commit; then US3, US4, US5, US6, US7 in priority order, validating and
   committing after each checkpoint (tutorial cadence: commit and sync after implementation).
4. Polish → final validation → commit.

### Production note

Asynchronous antivirus scanning with Azure Functions + Queue Storage is **design only**
([plan.md](plan.md#production-migration-path-design-only--not-implemented), research R14) — no
tasks are generated for it.

---

## Notes

- [P] = different files and no dependency on an incomplete task.
- `DocumentService.cs` is edited by most stories: never run two of its tasks in parallel.
- Reset the local database only after Foundational (T016) and before final validation (T064);
  story phases add no tables.
- Commit after each phase checkpoint.
