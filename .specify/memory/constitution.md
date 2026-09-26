<!--
Sync Impact Report
==================
Version change: (unversioned template) → 1.0.0
Bump rationale: initial ratification; all placeholders replaced with project-specific governance.

Modified principles (template placeholder → new title):
- [PRINCIPLE_1_NAME] → I. Offline-First, Local-Only Operation
- [PRINCIPLE_2_NAME] → II. Infrastructure Abstraction
- [PRINCIPLE_3_NAME] → III. Security by Default (NON-NEGOTIABLE)
- [PRINCIPLE_4_NAME] → IV. Layered Architecture and Consistent Patterns
- [PRINCIPLE_5_NAME] → V. Training-First Simplicity

Added sections:
- Technology and Platform Constraints (was [SECTION_2_NAME])
- Development Workflow and Quality Gates (was [SECTION_3_NAME])
- Governance (rules defined)

Removed sections: none

Templates reviewed (read at runtime, not modified by this command):
- .specify/templates/plan-template.md ✅ "Constitution Check" gate reads this file; no change needed
- .specify/templates/spec-template.md ✅ no constitution-specific slots
- .specify/templates/tasks-template.md ✅ no constitution-specific slots

Follow-up TODOs:
- Bootstrap CSS/JS and Bootstrap Icons load from cdn.jsdelivr.net (Pages/_Host.cshtml,
  Pages/Login.cshtml). Recorded as a known exception under Principle I; consider vendoring
  them into wwwroot/lib in a future change.
- No automated test project exists yet; see Development Workflow and Quality Gates.
-->

# ContosoDashboard Constitution

## Core Principles

### I. Offline-First, Local-Only Operation

- The application MUST run end to end on a single developer machine (Windows, Linux or
  macOS) with only the .NET SDK installed. No database server, cloud subscription, or
  external service may be required at runtime.
- Data MUST be persisted with EF Core on SQLite; files MUST be stored on the local
  filesystem.
- New features MUST NOT introduce runtime calls to cloud services or third-party APIs.
- Known exception: Bootstrap and Bootstrap Icons are currently loaded from
  cdn.jsdelivr.net. Without internet the UI loses styling but all functionality MUST
  keep working. New features MUST NOT add further CDN or remote dependencies.

Rationale: this is a training application; it must work in classrooms and labs without
network access, subscriptions, or cost.

### II. Infrastructure Abstraction

- Every infrastructure concern (file storage, data access configuration, authentication
  provider, and any future external integration) MUST sit behind an interface and be
  registered through ASP.NET Core dependency injection in `Program.cs`.
- Business logic and UI components MUST depend on the interface, never on a concrete
  infrastructure type or API (for example, no direct `System.IO.File` calls from
  services or pages; use `IFileStorageService`).
- Swapping a local implementation for a cloud one (SQLite → Azure SQL, local disk →
  Azure Blob Storage, mock auth → Microsoft Entra ID) MUST require only a new
  implementation, a DI registration change, and configuration — no business logic
  changes.
- Storage keys and paths MUST use a provider-neutral format that is valid both as a
  local path and as a blob name (e.g. `{userId}/{projectId|personal}/{guid}.{ext}`).

Rationale: learners practice real-world abstraction and DI patterns, and the documented
cloud migration path stays achievable.

### III. Security by Default (NON-NEGOTIABLE)

- Every routable page, except login and logout, MUST carry `[Authorize]` (or a stricter
  role policy).
- Authorization MUST be enforced again in the service layer: any service method that
  reads or changes user-owned or project-scoped data MUST take the requesting user's ID
  and verify ownership, membership, or role before acting. Returning `null`/`false` for
  unauthorized access is the established pattern (IDOR protection).
- Role checks MUST use the existing hierarchical policies
  (Employee < TeamLead < ProjectManager < Administrator); new ad-hoc role strings are
  prohibited.
- File handling MUST:
  - store uploads outside `wwwroot`;
  - validate extension and size against a whitelist before saving;
  - generate GUID-based storage names and never use user-supplied file names in paths;
  - serve downloads only through an endpoint that performs authorization checks.
- The security headers and cookie settings configured in `Program.cs` MUST NOT be
  weakened by a feature change.
- User-facing errors MUST NOT expose stack traces, file-system paths, or internal IDs of
  other users' resources.

Rationale: the application teaches defense in depth; a feature that bypasses these
layers teaches the wrong lesson and reintroduces vulnerabilities the code already
guards against.

### IV. Layered Architecture and Consistent Patterns

- Code MUST follow the existing layers: `Models/` (entities), `Data/`
  (`ApplicationDbContext`, relationships, indexes, seed data), `Services/` (interface +
  implementation in the same file), `Pages/` and `Shared/` (Blazor UI).
- Pages MUST access data only through services; injecting `ApplicationDbContext` into a
  page or component is prohibited.
- I/O-bound operations MUST be asynchronous (`async`/`await`) end to end.
- Nullable reference types MUST stay enabled; new code MUST build without new compiler
  warnings.
- Multi-step operations that touch more than one store MUST be ordered so a failure
  leaves no orphaned records. For file uploads: generate unique path → save file →
  save metadata; if the metadata save fails, delete the saved file.
- Entities that are filtered or joined frequently MUST declare indexes in
  `ApplicationDbContext.OnModelCreating`.

Rationale: consistent structure keeps the codebase readable for learners and lets
Spec Kit plans map directly onto known folders and patterns.

### V. Training-First Simplicity

- Prefer built-in .NET, ASP.NET Core, and EF Core capabilities over new NuGet packages.
  Any new package MUST be justified in the feature plan and MUST work offline.
- Implement the simplest design that satisfies the specification (YAGNI); speculative
  extensibility beyond the interfaces required by Principle II is not allowed.
- Intentional simplifications (mock authentication, `EnsureCreated()` instead of
  migrations, no audit logging, no real malware scanning) are acceptable only when they
  are documented in the README's "Known Limitations" section or the feature spec.
- Code MUST remain readable by learners: descriptive names, small methods, and
  comments that explain *why* where the reason is not obvious.

Rationale: the codebase exists to teach Spec-Driven Development; unnecessary
complexity or undocumented shortcuts get in the way of that goal.

## Technology and Platform Constraints

- Runtime: .NET 10 (`net10.0`), ASP.NET Core Blazor Server with Razor Pages for
  login/logout.
- Data: Entity Framework Core 10 with the SQLite provider; connection string
  `Data Source=ContosoDashboard.db`. The schema is created and seeded with
  `EnsureCreated()` at startup; changes to entities or seed data MUST keep that working
  from an empty database. Database files MUST NOT be committed.
- SQLite limitations MUST be respected in queries: do not sort or compare `decimal` or
  `DateTimeOffset` columns in the database; use `DateTime` (UTC) for timestamps.
- Authentication: cookie-based mock authentication with claims (`NameIdentifier`,
  `Name`, `Email`, `Role`); 8-hour sliding expiration.
- UI: Bootstrap 5.3 and Bootstrap Icons; follow the existing card, badge and table
  styles in `wwwroot/css/site.css`.
- File storage: local directory outside `wwwroot` (e.g. `AppData/uploads`), excluded
  from source control.
- Supported developer platforms: Windows, Linux (Ubuntu) and macOS.

## Development Workflow and Quality Gates

- Features follow the Spec Kit flow: constitution → specify → clarify → plan → tasks →
  analyze → implement. Work happens on a feature branch created by `/speckit-specify`.
- The plan's "Constitution Check" MUST pass before tasks are generated; any violation
  MUST be listed with a justification in the plan's Complexity Tracking section.
- Before a feature is considered done:
  - `dotnet build` MUST succeed with 0 errors and 0 warnings;
  - the application MUST start from an empty database and seed successfully;
  - the README security scenarios (authentication required, user isolation, IDOR
    protection, role-based access) MUST still pass when verified manually, plus the
    acceptance scenarios in the feature spec.
- No automated test project exists yet. If a feature spec requests tests, they MUST be
  added as a separate xUnit project and MUST run offline.
- The README (features, pages table, project structure, known limitations) MUST be
  updated in the same change whenever setup steps or user-visible behavior change.
- Commits SHOULD be small and follow the tutorial's cadence: one commit per Spec Kit
  phase (constitution, spec, plan, tasks, implementation).

## Governance

- This constitution supersedes other practices and guidance in the repository. Where
  the README or a stakeholder document conflicts with it, the constitution wins until it
  is amended.
- Amendments MUST be made through `/speckit-constitution`, which records a Sync Impact
  Report, and MUST be committed separately from feature work.
- Versioning follows semantic versioning:
  - MAJOR: a principle is removed or redefined in a backward-incompatible way;
  - MINOR: a principle or section is added or materially expanded;
  - PATCH: clarifications, wording, or typo fixes.
- Every feature plan and `/speckit-analyze` run MUST check compliance with these
  principles; reviewers MUST reject changes that violate Principle III regardless of
  other justification.
- Runtime development guidance for AI agents lives in the Spec Kit skills under
  `.claude/skills/` and the templates under `.specify/templates/`.

**Version**: 1.0.0 | **Ratified**: 2026-09-25 | **Last Amended**: 2026-09-25
