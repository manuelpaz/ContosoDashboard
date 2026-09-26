# Contract: UI Routes and Components

All pages carry `@attribute [Authorize]` (reports: `[Authorize(Policy = "Administrator")]`) and
call services only (no `ApplicationDbContext` in pages). Styling follows existing Bootstrap
cards/tables/badges.

## New pages

| Route | File | Purpose | Spec |
|-------|------|---------|------|
| `/documents` | `Pages/Documents.razor` | "My Documents": table (title, category, upload date, size, project), sort + filters (category, project, date range), "Upload" button | Story 1, 3; FR-018/019 |
| `/documents/shared` | `Pages/SharedDocuments.razor` | "Shared with Me" list | Story 5; FR-027 |
| `/documents/search` | `Pages/DocumentSearch.razor` | Search box (`?q=`) over all accessible documents; "no documents found" state | Story 3; FR-021 |
| `/documents/{documentId:int}` | `Pages/DocumentDetails.razor` | Metadata, preview/download, edit metadata, replace file, delete (confirm dialog), share (users + departments) — actions shown per `DocumentPermissions`; unknown/denied → "Document not available" | Story 4, 5 |
| `/documents/reports` | `Pages/DocumentReports.razor` | Admin reports with period selector (7/30/90 days) | Story 7; FR-033 |
| `/tasks/{taskId:int}` | `Pages/TaskDetails.razor` | Task info, attached documents, "Attach existing" (project documents only) and "Upload" | Story 6; FR-029/030 |

## New shared component

| Component | File | Contract |
|-----------|------|----------|
| `DocumentUpload` | `Shared/DocumentUpload.razor` | Parameters: `ProjectId` (int?, preselects and locks project), `TaskId` (int?), `OnUploaded` (EventCallback). Renders a modal with `InputFile multiple` (max 10), per-file title fields, category (required), description, project dropdown (user's projects), tags; per-file progress bars and success/error messages. Reset via `@key`. Upload in ≤ 3 clicks: open → choose files → upload. |

## Modified pages and layout

| File | Change | Spec |
|------|--------|------|
| `Shared/NavMenu.razor` | Add "Documents" (`/documents`), "Shared with Me", and admin-only "Document Reports" (`<AuthorizeView Roles="Administrator">`) | Navigation |
| `Pages/Index.razor` | Documents summary card (count) and "Recent Documents" widget (last 5 uploads, link to details) | FR-031 |
| `Pages/ProjectDetails.razor` | "Documents" section (`id="documents"`) listing project documents with download/preview and `DocumentUpload` (ProjectId set) | Story 2; FR-020 |
| `Pages/Tasks.razor` | Task title links to `/tasks/{id}` | Story 6 |
| `Pages/Notifications.razor` | "Open" link when `LinkUrl` is set | FR-028 |
| `Pages/Login.cshtml.cs` | Add `Department` claim | Stakeholder requirement |
