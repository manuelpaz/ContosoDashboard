# Research: Document Upload and Management

**Feature**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md) | **Date**: 2026-09-25

Each entry resolves a technical unknown from the plan's Technical Context. Format: Decision /
Rationale / Alternatives considered.

## R1. File upload in Blazor Server (multi-file, 25 MB, progress)

- **Decision**: Use the built-in `InputFile` component with `multiple`, limited to 10 files per
  batch. For each file, capture name/size/content type into locals, open
  `OpenReadStream(maxAllowedSize: 25 MB)`, copy into a `MemoryStream` in 80 KB chunks while
  updating a per-file progress value and calling `StateHasChanged()`, then clear the
  `IBrowserFile` reference. Reset the component with `@key` after each batch.
- **Rationale**: Built-in, offline, no packages (Principle V). Chunked copy gives real progress
  (FR-007). The MemoryStream / `@key` pattern is mandated by the stakeholder document to avoid
  disposed-stream and reuse errors in Blazor Server. 25 MB × 10 files is acceptable memory for a
  single-user training app.
- **Alternatives considered**: JavaScript `fetch` upload to an HTTP endpoint (more code, needs
  antiforgery handling, adds JS); third-party upload components (new dependency, often CDN-based).

## R2. Serving downloads and previews

- **Decision**: Add minimal API endpoints in `ContosoDashboard/Endpoints/DocumentEndpoints.cs`,
  mapped from `Program.cs` with `.RequireAuthorization()`:
  `GET /documents/{id}/download` (attachment) and `GET /documents/{id}/preview` (inline, PDF and
  images only). Both call `IDocumentService.OpenDocumentAsync`, which enforces access and logs the
  activity. Unauthorized or missing documents both return **404** so existence is not revealed
  (FR-017).
- **Rationale**: Blazor components cannot stream file responses; files live outside `wwwroot`, so
  an authorized HTTP endpoint is required (stakeholder doc, Principle III). The app has no MVC
  controllers; minimal APIs are the lightest built-in option and keep the codebase consistent.
- **Alternatives considered**: Adding MVC controllers (extra framework surface for two endpoints);
  Blazor JS interop "download from stream" (base64 over SignalR, poor for 25 MB, no preview).

## R3. Preview without weakening security headers

- **Decision**: Images are previewed with an `<img>` tag pointing at the preview endpoint inside a
  modal on the document page. PDFs open the preview endpoint in a new browser tab
  (`target="_blank"`, `Content-Disposition: inline`), using the browser's built-in viewer. No
  iframes.
- **Rationale**: The global headers set `X-Frame-Options: DENY`, which blocks even same-origin
  iframes; the constitution forbids weakening those headers. `img-src 'self'` already allows
  images. Meets FR-022 and SC-005 with zero header changes.
- **Alternatives considered**: Relaxing to `SAMEORIGIN` for preview responses (violates
  Principle III); PDF.js (JavaScript library, extra dependency, CDN or vendoring).

## R4. Malware check offline (clarified: signature validation)

- **Decision**: `IMalwareScannerService` with `FileSignatureMalwareScanner`:
  1. Reject known executable signatures anywhere they are declared as documents: `MZ` (Windows
     PE), `7F 45 4C 46` (ELF), Mach-O magic numbers, `#!` scripts.
  2. Verify magic bytes match the extension: PDF `%PDF-`; PNG `89 50 4E 47 0D 0A 1A 0A`;
     JPEG `FF D8 FF`; Office Open XML = ZIP `50 4B 03 04` **and** the archive contains
     `[Content_Types].xml` plus the matching part folder (`word/`, `xl/`, `ppt/`) and **no**
     `vbaProject.bin` (macros), inspected with built-in `System.IO.Compression.ZipArchive`.
  3. Plain text: valid UTF-8, no NUL bytes, no control characters other than tab/CR/LF.
  Returns a result with `IsClean`, a user-facing reason, and the detected content type.
- **Rationale**: Implements clarification Q1 (option A) and FR-008 with built-in APIs only; the
  interface lets production swap in ClamAV/Microsoft Defender (README "Malware Scanning" note).
- **Alternatives considered**: ClamAV daemon (extra install, breaks "only the .NET SDK");
  no-op scanner (no protection).

## R5. Supported formats and content type

- **Decision**: Whitelist `.pdf`, `.docx`, `.xlsx`, `.pptx`, `.txt`, `.jpg`, `.jpeg`, `.png`.
  The stored `FileType` (MIME) is derived from the validated extension via a fixed server-side
  map, never from the browser-supplied content type. Legacy binary Office formats
  (`.doc/.xls/.ppt`) and macro-enabled formats (`.docm`, etc.) are rejected.
- **Rationale**: Satisfies FR-002 (Word, Excel, PowerPoint via their current formats). Legacy
  OLE formats cannot be checked for macros with built-in APIs, so accepting them would undermine
  FR-008. A trusted MIME type prevents content-type spoofing on download.
- **Alternatives considered**: Accepting legacy formats with signature-only checks (weaker
  protection); trusting `IBrowserFile.ContentType` (spoofable).

## R6. Local storage layout and path safety

- **Decision**: Root directory from configuration `FileStorage:RootPath` (default
  `AppData/uploads`, resolved against the content root, outside `wwwroot`, git-ignored). Storage
  key = `{uploaderUserId}/{projectId|"personal"}/{Guid:N}.{ext}` with forward slashes, stored in
  `Document.FilePath` (relative, portable, valid as an Azure blob name). `LocalFileStorageService`
  resolves keys with `Path.GetFullPath` and rejects any key that resolves outside the root.
- **Rationale**: Matches the stakeholder layout and Principle II (provider-neutral keys). The
  original file name is only metadata/download name, never part of a path (FR-009).
- **Alternatives considered**: Storing files as BLOBs in SQLite (no Azure Blob parity, large DB);
  using original names (path traversal and collision risk).

## R7. Upload atomicity and batch semantics

- **Decision**: Per file: validate (size, extension, scan) → generate key → save file → insert
  `Document` + tags + optional `TaskDocument` + `DocumentActivity(Upload)` in one `SaveChanges`.
  If the database save fails, delete the saved file. Each file in a batch succeeds or fails
  independently. After the batch, if at least one file succeeded and a project was selected,
  send **one** `DocumentAdded` notification per project member/manager except the uploader
  (clarification Q4). Replace-file uses the same order: save new file → update record → delete
  old file.
- **Rationale**: FR-010 (no orphaned records or files), stakeholder sequence "generate path →
  save file → save metadata", constitution Principle IV.
- **Alternatives considered**: DB first then file (orphaned records with empty paths — the exact
  failure the stakeholder doc warns about).

## R8. Authorization model and data source

- **Decision**: `DocumentService` builds one reusable "accessible documents" query for the
  requesting user, reading role and department **from the database** (`Users` table):
  - Administrator → all documents.
  - Otherwise any of: uploader is the user; document's project is managed by the user or has the
    user as member; a share targets the user; a share targets the user's department; the user is a
    TeamLead and the uploader's department equals the user's (non-null) department.
  Action permissions (edit metadata, replace, delete, share) are computed per document:
  uploader → all; project manager of the document's project → edit metadata + delete
  (clarification Q3); administrator → edit metadata + delete; team lead and share recipients →
  view only (clarification Q2). The `Department` claim is also added at login as the stakeholder
  document requires, but services never trust claims for authorization decisions.
- **Rationale**: Service-level checks with `requestingUserId` follow the existing IDOR pattern
  (Principle III). Reading from the DB avoids stale claims during the 8-hour cookie lifetime.
  "Project members" must include the project manager because `ProjectManagerId` is not always a
  `ProjectMember` row (see `ProjectService.GetUserProjectsAsync`).
- **Alternatives considered**: ASP.NET Core resource-based `IAuthorizationHandler` (more
  ceremony, unfamiliar to learners); claim-based department checks (stale data risk).

## R9. Search and filtering on SQLite

- **Decision**: Search uses `EF.Functions.Like` with `%term%` (escaping `%`, `_` and the escape
  character) over title, description, tags, uploader display name and project name, applied on
  top of the accessible-documents query. Sorting/filtering (title, date, category, size, project,
  date range) is done in the database. Indexes on `UploadedByUserId`, `ProjectId`, `Category`,
  `UploadedDate`, and `DocumentTag.Tag`.
- **Rationale**: SQLite `LIKE` is case-insensitive for ASCII, which matches user expectations;
  EF's `string.Contains` maps to `instr()` which is case-sensitive. With ≤ 500 accessible
  documents per user these queries comfortably meet SC-003/SC-004 (< 2 s).
- **Alternatives considered**: SQLite FTS5 (raw SQL, not portable to Azure SQL); in-memory
  filtering (does not scale, wastes memory).
- **Known limitation**: case-insensitive matching applies to ASCII letters only (SQLite default).

## R10. Schema changes with `EnsureCreated()`

- **Decision**: Keep `EnsureCreated()` (constitution). New tables and the new `Notification`
  column are created only for a new database, so existing local databases must be deleted once
  (`rm -f ContosoDashboard.db*`). No seeded documents (seeding would require files on disk).
- **Rationale**: Consistent with the training app's approach and the stakeholder's "clean state
  for testing" instruction; migrations are out of scope for the training context.
- **Alternatives considered**: Introducing EF migrations (more tooling for learners, violates
  Principle V's simplicity for this feature).

## R11. Notifications that link to documents

- **Decision**: Add nullable `LinkUrl` (max 500) to `Notification` and two enum values appended
  at the end of `NotificationType`: `DocumentShared`, `DocumentAdded`. The Notifications page
  shows an "Open" link when `LinkUrl` is set. Shares to a department notify each current member
  once; users who already have access, and the uploader, are not notified (spec edge case).
- **Rationale**: Clarification Q4 requires linking to the project's documents; appending enum
  values keeps existing stored integers valid.
- **Alternatives considered**: Encoding the link in message text (not clickable).

## R12. Activity tracking and reports

- **Decision**: `DocumentActivity` rows with action (`Upload`, `Download`, `Preview`, `Replace`,
  `Delete`, `Share`), user, timestamp, and snapshots of document title and file type; no foreign
  key to `Document`, so rows survive deletion (clarification Q1). `DocumentReportService`
  (Administrator only) computes for a selectable period (7/30/90 days): uploads by file type,
  top 10 uploaders, top 10 documents by access (downloads + previews), and daily access totals.
- **Rationale**: FR-032/FR-033. "Preview" is recorded separately so access patterns include
  in-browser views; "Replace" keeps the trail complete.
- **Alternatives considered**: Generic application logging (not queryable for reports).

## R13. Testing approach

- **Decision**: No automated test project for this feature; verification is manual via
  [quickstart.md](quickstart.md) plus the README security scenarios, and `dotnet build` with
  0 warnings.
- **Rationale**: The constitution only requires an xUnit project when the spec requests tests;
  this spec does not.
- **Alternatives considered**: Adding xUnit now (scope creep for the training MVP).

## R14. Production migration: asynchronous virus scanning with Azure Functions + Queue Storage

**Scope**: Design only — **not implemented in this feature**. The training build keeps the
synchronous, offline signature check (R4), as required by the constitution (Principle I: no
cloud services at runtime) and by FR-008 / clarification Q1 (check before storage). This entry
documents how production replaces it with enterprise antivirus scanning in a background job.

- **Decision (production target)**:
  1. **Upload (web app)** — keeps the synchronous gate: size, extension whitelist and
     `IMalwareScannerService` signature check. The file is written to a private
     **quarantine** blob container (`uploads-quarantine`) using the same key format
     `{userId}/{projectId|personal}/{guid}.{ext}`. The `Document` row is saved with
     `ScanStatus = Pending`, then a message `{ documentId, blobName, uploadBatchId }` is put on
     the Azure Storage queue `document-scan` through a new `IDocumentScanQueue` abstraction.
  2. **Background job (Azure Function)** — a .NET isolated-worker Azure Function with a
     `[QueueTrigger("document-scan")]` binding: loads the document, skips it if it is not
     `Pending` (idempotency), streams the blob to the antivirus engine (Microsoft Defender for
     Storage malware scanning or a ClamAV container), then:
     - **Clean** → copy blob to the `documents` container, delete the quarantine blob, set
       `ScanStatus = Clean`, record `ScannedDate`, and send the deferred `DocumentAdded`
       project notifications (one per member per batch, FR-028).
     - **Infected** → delete the quarantine blob, set `ScanStatus = Rejected`, log a
       `ScanRejected` `DocumentActivity`, notify the uploader.
  3. **Failures** — queue visibility timeout + `maxDequeueCount = 5`; messages that keep
     failing move to `document-scan-poison`, raise an alert, and stay `Pending` for
     administrator follow-up.
  4. **Access while scanning** — only `Clean` documents are listed, shared, attached,
     previewed or downloadable. The uploader sees their `Pending`/`Rejected` documents with a
     "Scanning…"/"Rejected" badge; nobody else sees them.
  5. **Identity and security** — web app and Function use managed identities (Storage Blob
     Data Contributor, Storage Queue Data Contributor, Azure SQL access); no connection
     strings or SAS tokens in configuration; containers are private.
- **Required production changes** (additive, outside this feature):
  - Schema: `Document.ScanStatus` (`Pending`/`Clean`/`Rejected`) and `ScannedDate?`, via an EF
    Core migration (production uses migrations, not `EnsureCreated()`). This is the one
    exception to the stakeholder statement that migration needs no schema change.
  - DI: register `AzureBlobStorageService`, `AzureQueueDocumentScanQueue` and keep
    `FileSignatureMalwareScanner` as the synchronous first gate.
  - Query filter: the accessible-documents query (R8) adds `ScanStatus == Clean`.
  - New project: `ContosoDashboard.Functions` (Azure Functions isolated worker) sharing the
    entity/`DbContext` code.
- **Rationale**: Full antivirus scanning of 25 MB files can take seconds to minutes and must not
  block the Blazor upload request. A queue decouples upload from scanning, absorbs spikes,
  retries automatically, and a poison queue makes failures visible. Keeping the interfaces
  (`IFileStorageService`, `IMalwareScannerService`, future `IDocumentScanQueue`) means business
  logic and pages stay unchanged, satisfying Principle II.
- **Alternatives considered**:
  - Defender for Storage on-upload scanning with Event Grid → Function (no queue to manage;
    good option, but ties the flow to Defender and Event Grid; the queue design works with any
    scanner).
  - In-process `BackgroundService` + `Channel` in the web app (no extra service, but scans
    compete with web traffic and are lost on restart without durable storage).
  - Synchronous antivirus call during upload (simple, but slow uploads and timeouts at 25 MB).
