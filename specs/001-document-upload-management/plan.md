# Implementation Plan: Document Upload and Management

**Branch**: `001-document-upload-management` | **Date**: 2026-09-25 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/001-document-upload-management/spec.md`

## Summary

Add a secure, offline document library to ContosoDashboard: multi-file upload (≤ 25 MB, whitelisted
types, signature-based malware check), "My Documents" with sort/filter, project documents,
search, preview/download through authorized endpoints, metadata edit/replace/delete, sharing with
users or departments, task attachments, dashboard widgets, notifications, and an admin activity
report. Technically: new EF Core entities on SQLite, files on local disk behind
`IFileStorageService`, checks behind `IMalwareScannerService`, all rules enforced in a new
`DocumentService` with the existing `requestingUserId` pattern, Blazor pages plus two minimal API
endpoints for file streaming. See [research.md](research.md) for decisions. For production,
[research.md R14](research.md#r14-production-migration-asynchronous-virus-scanning-with-azure-functions--queue-storage)
documents (without implementing) asynchronous antivirus scanning with an Azure Function
triggered by Queue Storage.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`)
**Primary Dependencies**: ASP.NET Core Blazor Server + Razor Pages, EF Core 10 (SQLite provider),
Bootstrap 5.3 / Bootstrap Icons; built-in `System.IO.Compression` for Office checks. No new NuGet
packages.
**Storage**: SQLite (`ContosoDashboard.db`) for metadata; local filesystem
`AppData/uploads/{userId}/{projectId|personal}/{guid}.{ext}` for files (outside `wwwroot`,
git-ignored)
**Testing**: Manual validation via [quickstart.md](quickstart.md) and README security scenarios;
`dotnet build` with 0 warnings (no automated tests requested by the spec)
**Target Platform**: Local developer machine — Windows, Linux (Ubuntu), macOS; modern browsers
**Project Type**: Single ASP.NET Core web application (existing `ContosoDashboard/` project)
**Performance Goals**: Upload ≤ 30 s for 25 MB; lists and search < 2 s for ≤ 500 accessible
documents; preview < 3 s
**Constraints**: Fully offline; no cloud SDKs; security headers unchanged; `EnsureCreated()`
schema (delete local DB after pulling); max 10 files per upload batch
**Scale/Scope**: Training app — 4 seeded users, 1 seeded project; ≤ 500 documents per user;
6 new pages, 1 shared component, 5 new entities, 4 new services, 2 endpoints

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle / Gate | Pre-research | Post-design | Evidence |
|------------------|:-:|:-:|----------|
| I. Offline-first, local-only | ✅ | ✅ | SQLite + local disk; no external calls; signature scanner instead of an AV service (R4); no new CDN assets |
| II. Infrastructure abstraction | ✅ | ✅ | `IFileStorageService` (stakeholder signature) and `IMalwareScannerService` registered in DI; provider-neutral storage keys (R6) |
| III. Security by default | ✅ | ✅ | `[Authorize]` on all pages, admin policy on reports; service checks with DB-sourced role/department (R8); 404 for denied/missing (FR-017); files outside `wwwroot`, GUID keys, whitelist, trusted MIME (R5/R6); preview without iframes so headers stay unchanged (R3) |
| IV. Layered architecture | ✅ | ✅ | Models/Data/Services/Pages; pages and endpoints use services only; async end to end; path → file → record ordering with cleanup (R7); indexes declared |
| V. Training-first simplicity | ✅ | ✅ | No new packages; built-in `InputFile`, minimal APIs, `ZipArchive`; limitations documented (malware note already in README; ASCII-only case-insensitive search added to README) |
| Tech constraints (SQLite types, `EnsureCreated`, UTC `DateTime`) | ✅ | ✅ | No `decimal`/`DateTimeOffset`; `FileSize` is `long`; DB reset documented (R10) |
| Workflow gates (0 warnings, empty-DB start, README updated) | ✅ | ✅ | Quickstart setup; README updates listed below |

No violations — Complexity Tracking not required.

## Project Structure

### Documentation (this feature)

```text
specs/001-document-upload-management/
├── spec.md
├── plan.md              # This file
├── research.md          # Phase 0 decisions (R1–R13)
├── data-model.md        # Entities, permissions, lifecycle
├── quickstart.md        # Manual validation scenarios
├── contracts/
│   ├── service-interfaces.md
│   ├── http-endpoints.md
│   └── ui-routes.md
├── checklists/
│   └── requirements.md
└── tasks.md             # Created by /speckit-tasks
```

### Source Code (repository root)

```text
ContosoDashboard/
├── Models/
│   ├── Document.cs                 # NEW
│   ├── DocumentTag.cs              # NEW
│   ├── DocumentShare.cs            # NEW
│   ├── TaskDocument.cs             # NEW
│   ├── DocumentActivity.cs         # NEW
│   ├── DocumentCategories.cs       # NEW (static category list)
│   └── Notification.cs             # MODIFIED: LinkUrl, DocumentShared/DocumentAdded types
├── Data/
│   └── ApplicationDbContext.cs     # MODIFIED: DbSets, relationships, indexes
├── Services/
│   ├── FileStorageService.cs       # NEW: IFileStorageService + LocalFileStorageService
│   ├── MalwareScannerService.cs    # NEW: IMalwareScannerService + FileSignatureMalwareScanner
│   ├── DocumentService.cs          # NEW: IDocumentService + DTOs
│   ├── DocumentReportService.cs    # NEW: IDocumentReportService
│   └── DashboardService.cs         # MODIFIED: TotalDocuments
├── Endpoints/
│   └── DocumentEndpoints.cs        # NEW: download/preview minimal APIs
├── Pages/
│   ├── Documents.razor             # NEW  /documents
│   ├── SharedDocuments.razor       # NEW  /documents/shared
│   ├── DocumentSearch.razor        # NEW  /documents/search
│   ├── DocumentDetails.razor       # NEW  /documents/{id}
│   ├── DocumentReports.razor       # NEW  /documents/reports (Administrator)
│   ├── TaskDetails.razor           # NEW  /tasks/{id}
│   ├── Index.razor                 # MODIFIED: document card + Recent Documents
│   ├── ProjectDetails.razor        # MODIFIED: Documents section
│   ├── Tasks.razor                 # MODIFIED: link to task details
│   ├── Notifications.razor         # MODIFIED: Open link
│   └── Login.cshtml.cs             # MODIFIED: Department claim
├── Shared/
│   ├── DocumentUpload.razor        # NEW reusable upload modal
│   └── NavMenu.razor               # MODIFIED: Documents links
├── Program.cs                      # MODIFIED: DI registrations, endpoint mapping
└── appsettings.json                # MODIFIED: FileStorage:RootPath
.gitignore                          # MODIFIED: AppData/
README.md                           # MODIFIED: feature, pages table, structure, limitations
```

**Structure Decision**: Extend the existing single project `ContosoDashboard/` following its
Models/Data/Services/Pages/Shared layers. The only new folder is `Endpoints/`, needed because
file streaming requires HTTP endpoints and the project has no controllers (R2).

## Implementation Notes by Priority

Delivery follows the spec's story priorities so each increment is usable:

1. **P1 foundation + Story 1**: entities, DbContext, storage, scanner, `DocumentService` upload /
   my-documents / open, endpoints, `DocumentUpload`, `/documents`, `/documents/{id}` (view +
   download), NavMenu, Department claim, `.gitignore`, config. → MVP.
2. **Story 2**: project association rules, project documents section, batch notification with
   `LinkUrl`, Notifications "Open" link.
3. **Story 3**: sort/filter on `/documents`, `/documents/search`.
4. **Story 4**: preview, edit metadata, replace, delete with confirmation.
5. **Story 5**: sharing (users/departments), `/documents/shared`, share notifications.
6. **Story 6**: `/tasks/{id}` with attachments, Tasks list link, dashboard card + widget.
7. **Story 7**: activity logging already in place from P1; `DocumentReportService` and
   `/documents/reports`.
8. **Polish**: README updates (features, pages table, project structure, known limitations:
   ASCII-only case-insensitive search, DB reset after schema change), full quickstart run.

## Production Migration Path (design only — not implemented)

Background virus scanning with **Azure Functions + Queue Storage** is the production target for
full antivirus scanning; details in [research.md R14](research.md#r14-production-migration-asynchronous-virus-scanning-with-azure-functions--queue-storage).
It is deliberately **out of scope for this training feature** because it would violate
constitution Principle I (cloud services at runtime, extra tooling beyond the .NET SDK) and
contradict FR-008 / clarification Q1 (check before storage) and the stakeholder constraint
"No Azure SDK dependencies in training implementation".

```text
Production flow
Blazor upload ─► sync signature check ─► blob "uploads-quarantine" ─► Document(ScanStatus=Pending)
                                                                   └─► queue "document-scan"
Azure Function [QueueTrigger] ─► antivirus (Defender for Storage / ClamAV)
   ├─ clean    ─► move to "documents", ScanStatus=Clean, send DocumentAdded notifications
   ├─ infected ─► delete blob, ScanStatus=Rejected, notify uploader, log ScanRejected
   └─ failing  ─► retries (maxDequeueCount 5) ─► "document-scan-poison" + alert
```

What this training design already provides for that migration: `IFileStorageService` and
`IMalwareScannerService` abstractions, provider-neutral storage keys, service-level access
checks in one query (where the future `ScanStatus == Clean` filter is added), and deferred-able
batch notifications. Production additions: `ScanStatus`/`ScannedDate` columns via EF
migration, `IDocumentScanQueue`, Azure implementations, and a `ContosoDashboard.Functions`
project.

## Complexity Tracking

No constitution violations to justify.
