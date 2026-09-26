# Data Model: Document Upload and Management

**Feature**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md) | **Date**: 2026-09-25

Conventions follow the existing models: integer keys, data annotations for lengths, UTC
`DateTime`, navigation properties, relationships and indexes configured in
`ApplicationDbContext.OnModelCreating`. SQLite limitations respected: no `decimal` or
`DateTimeOffset` columns.

## New entities

### Document (`Models/Document.cs`)

| Field | Type | Rules |
|-------|------|-------|
| DocumentId | int, PK | Integer key (stakeholder constraint) |
| Title | string(200) | Required, trimmed, 1–200 chars |
| Description | string(2000)? | Optional |
| Category | string(50) | Required; one of `DocumentCategories.All` (stored as text) |
| OriginalFileName | string(255) | Required; display/download name only, never used in paths |
| FileType | string(255) | Required; MIME type from server-side extension map (R5) |
| FileSize | long | Required; 1 … 26,214,400 bytes (25 MB) |
| FilePath | string(500) | Required, **unique**; storage key `{userId}/{projectId\|personal}/{guid}.{ext}` |
| UploadedByUserId | int, FK → User | Required; `OnDelete: Restrict` |
| ProjectId | int?, FK → Project | Optional; user must manage or belong to the project at upload; `OnDelete: SetNull` |
| UploadedDate | DateTime (UTC) | Set on upload |
| UpdatedDate | DateTime (UTC) | Set on upload, metadata edit and file replace |

Navigation: `UploadedByUser`, `Project`, `Tags`, `Shares`, `TaskAttachments`.
Indexes: `UploadedByUserId`, `ProjectId`, `Category`, `UploadedDate`, unique `FilePath`.

### DocumentTag (`Models/DocumentTag.cs`)

| Field | Type | Rules |
|-------|------|-------|
| DocumentTagId | int, PK | |
| DocumentId | int, FK → Document | `OnDelete: Cascade` |
| Tag | string(50) | Required; trimmed; case-insensitive duplicates removed |

Rules: max 10 tags per document. Indexes: unique (`DocumentId`, `Tag`), `Tag`.

### DocumentShare (`Models/DocumentShare.cs`)

| Field | Type | Rules |
|-------|------|-------|
| DocumentShareId | int, PK | |
| DocumentId | int, FK → Document | `OnDelete: Cascade` |
| SharedWithUserId | int?, FK → User | Set for a user share; `OnDelete: Restrict` |
| SharedWithDepartment | string(100)? | Set for a team share (department name) |
| SharedByUserId | int, FK → User | Required; `OnDelete: Restrict` |
| SharedDate | DateTime (UTC) | |

Rules: exactly one of `SharedWithUserId` / `SharedWithDepartment` is set (validated in
service). Department shares are evaluated at access time (current members). No share is created
for the uploader or for a user who already has a share of the same document.
Indexes: (`DocumentId`, `SharedWithUserId`), (`DocumentId`, `SharedWithDepartment`),
`SharedWithUserId`, `SharedWithDepartment`.

### TaskDocument (`Models/TaskDocument.cs`)

| Field | Type | Rules |
|-------|------|-------|
| TaskId | int, FK → TaskItem | Composite PK part; `OnDelete: Cascade` |
| DocumentId | int, FK → Document | Composite PK part; `OnDelete: Cascade` |
| AttachedByUserId | int, FK → User | `OnDelete: Restrict` |
| AttachedDate | DateTime (UTC) | |

Rules (clarification Q2): document must be associated with the task's project; if the task
has no project, the document must have been uploaded by the attaching user. Attaching never
changes document access.

### DocumentActivity (`Models/DocumentActivity.cs`)

| Field | Type | Rules |
|-------|------|-------|
| DocumentActivityId | int, PK | |
| DocumentId | int | **No FK** — record outlives the document (clarification Q1) |
| DocumentTitle | string(200) | Snapshot at time of action |
| FileType | string(255) | Snapshot at time of action |
| Action | string(20) | One of `Upload`, `Download`, `Preview`, `Replace`, `Delete`, `Share` |
| UserId | int, FK → User | Actor; `OnDelete: Restrict` |
| Timestamp | DateTime (UTC) | |
| Details | string(500)? | E.g. share targets, replaced file name |

Indexes: (`Action`, `Timestamp`), `DocumentId`, `UserId`.

### DocumentCategories (`Models/DocumentCategories.cs`)

Static list (not a table): `Project Documents`, `Team Resources`, `Personal Files`, `Reports`,
`Presentations`, `Other`.

## Modified entities

### Notification (`Models/Notification.cs`)

- Add `LinkUrl` — `string(500)?`, app-relative URL (e.g. `/projects/1#documents`,
  `/documents/42`).
- Append to `NotificationType`: `DocumentShared`, `DocumentAdded` (appended to keep existing
  integer values).

### Existing entities used without schema change

- **User**: `Role` and `Department` drive permissions (team = department).
- **Project / ProjectMember**: "project member" = `ProjectManagerId` **or** a `ProjectMember` row.
- **TaskItem**: `ProjectId` (nullable) drives attachment rules and auto-association (FR-030).

## Relationships

```text
User 1 ──< Document (UploadedBy)           Project 0..1 ──< Document
Document 1 ──< DocumentTag                  Document 1 ──< DocumentShare >── 0..1 User
Document >──< TaskItem (via TaskDocument)   User 1 ──< DocumentActivity (DocumentId: soft reference)
User 1 ──< Notification (LinkUrl new)
```

## Permission matrix (per document)

| Actor | View / download / preview | Edit metadata | Replace file | Delete | Share |
|-------|:-:|:-:|:-:|:-:|:-:|
| Uploader | ✓ | ✓ | ✓ | ✓ | ✓ |
| Manager of the document's project | ✓ | ✓ | – | ✓ | – |
| Member of the document's project | ✓ | – | – | – | – |
| Team lead, same department as uploader | ✓ | – | – | – | – |
| Share recipient (user or department) | ✓ | – | – | – | – |
| Administrator | ✓ | ✓ | – | ✓ | – |
| Anyone else | 404 | – | – | – | – |

## Lifecycle

```text
(upload request) ──validate+scan fail──> rejected (no file, no record)
       │ ok
       ▼
 save file ──fail──> rejected (no record)
       │ ok
       ▼
 save record ──fail──> file deleted, rejected
       │ ok
       ▼
   ACTIVE ──edit metadata──> ACTIVE (UpdatedDate)
     │  └──replace file────> ACTIVE (new FilePath, old file deleted)
     ▼ delete (confirmed)
   DELETED: Document, file, tags, shares, task attachments removed;
            DocumentActivity rows kept + "Delete" activity added
```

## Validation summary

| Rule | Source |
|------|--------|
| Size 1 B – 25 MB | FR-003 |
| Extension whitelist `.pdf .docx .xlsx .pptx .txt .jpg .jpeg .png` | FR-002, R5 |
| Signature/content check, no executables, no macros | FR-008, R4 |
| Title required ≤ 200; description ≤ 2000; ≤ 10 tags ≤ 50 chars | FR-004/005, spec assumptions |
| Category in fixed list | FR-004 |
| Project must be managed by or include the uploader | Story 2 scenario 2 |
| Max 10 files per batch | Plan decision (R1) |
