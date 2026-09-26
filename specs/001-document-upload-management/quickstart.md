# Quickstart: Validate Document Upload and Management

**Feature**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md)

Manual, end-to-end validation guide. Contracts: [service-interfaces](contracts/service-interfaces.md),
[http-endpoints](contracts/http-endpoints.md), [ui-routes](contracts/ui-routes.md); permissions:
[data-model.md](data-model.md#permission-matrix-per-document).

## Prerequisites

- .NET 10 SDK; no database server or internet needed.
- Seeded users (login page dropdown): Administrator (IT), Camille Nicole — ProjectManager
  (Engineering), Floris Kregel — TeamLead (Engineering), Ni Kang — Employee (Engineering).
  Seeded project 1 "ContosoDashboard Development": manager Camille; members Floris and Ni.

## Setup

```bash
cd ContosoDashboard
# New tables are only created for a new database (EnsureCreated):
rm -f ContosoDashboard.db ContosoDashboard.db-shm ContosoDashboard.db-wal
rm -rf AppData/uploads
dotnet build          # expect: 0 warnings, 0 errors
dotnet run --launch-profile http   # http://localhost:5000
```

Test files (in a scratch folder):

```bash
printf '%%PDF-1.4\n%%test\n' > report.pdf
printf 'Meeting notes\n' > notes.txt
cp report.pdf exact-25mb.pdf && truncate -s 26214400 exact-25mb.pdf   # exactly 25 MB
cp report.pdf too-big.pdf   && truncate -s 26214401 too-big.pdf       # 25 MB + 1 byte
printf 'MZ\x90\x00' > fake.pdf        # executable disguised as PDF
: > empty.txt                         # 0 bytes
printf 'x' > script.exe               # unsupported type
```

Plus any real `.png`/`.jpg` and a real `.docx` saved from an office app.

## Scenarios

| # | As | Steps | Expected |
|---|----|-------|----------|
| 1 | Ni | Documents → Upload → select `report.pdf`, `notes.txt`, a `.png` → category "Reports" → Upload | Progress per file, 3 success messages; all listed in My Documents with title, category, date, size |
| 2 | Ni | Upload `too-big.pdf`, `empty.txt`, `script.exe`, `fake.pdf`, `exact-25mb.pdf` in one batch | First four rejected with specific messages (size, empty, type, content mismatch); `exact-25mb.pdf` accepted; no record or file for rejected ones (`ls -R AppData/uploads`) |
| 3 | Ni | Submit upload with empty title or no category | Validation message; nothing uploaded |
| 4 | Ni | Download `report.pdf`; preview the PNG and the PDF | Download identical to original; PNG shows in modal; PDF opens in new tab |
| 5 | Ni | Upload `report.pdf` with project "ContosoDashboard Development" | Camille and Floris each get **one** notification with "Open" link to project documents; Ni gets none |
| 6 | Ni | Upload 3 files to the project in one batch | Camille and Floris each get exactly one notification saying 3 documents were added |
| 7 | Floris | Open project 1 → Documents | Project documents listed, downloadable; no edit/delete actions on Ni's documents |
| 8 | Camille | Open a project document uploaded by Ni | Can edit metadata and delete; **no** replace; cannot share |
| 9 | Floris (TeamLead, Engineering) | Open Ni's personal (no project, unshared) document via Search | View/download/preview only; edit/replace/delete/share absent |
| 10 | Administrator | Copy a document URL `/documents/{id}` of Ni's personal document; open it and `/documents/{id}/download` | Admin can view/download/edit/delete (no replace/share) |
| 11 | Ni | Share a personal document with Administrator; share another with department "Engineering" | Recipients notified once; listed in their Shared with Me; view/download only |
| 12 | Camille | Request `/documents/{id}/download` for one of Ni's personal, unshared documents, and `/documents/99999/download` | Both return 404 with identical responses; `/documents/{id}` shows "Document not available" |
| 13 | Ni | My Documents: sort by size desc; filter category "Reports" + date range | Largest first; only matching documents |
| 14 | Ni | Search a tag, an uploader name (e.g. "camille"), a project name; search "zzzz" | Only accessible matches, case-insensitive; "no documents found" for zzzz |
| 15 | Ni (uploader) | Edit title/tags; replace file with another valid PDF | New metadata in lists/search; download returns new file; old file gone from disk |
| 16 | Ni | Delete a shared, task-attached document → Cancel, then Delete → Confirm | Cancel: nothing changes. Confirm: gone from lists, search, Shared with Me, task, widget; file removed |
| 17 | Ni | Tasks → open "Implement authentication" (project 1) → Upload a document; Attach existing → list | Uploaded doc attached and associated with project 1; attach list shows only project 1 documents |
| 18 | Ni | Dashboard | "Recent Documents" shows last 5 uploads newest first; document count card matches My Documents count |
| 19 | Administrator | Document Reports, period 30 days | Uploads by type, top uploaders, top accessed documents, daily accesses — including the document deleted in #16 (by title) |
| 20 | Ni | Navigate to `/documents/reports` | Access denied / redirected |
| 21 | Any | README security scenarios (auth required, user isolation, IDOR, roles) | Still pass |

## Performance spot checks

- With ~500 accessible documents (bulk upload small files), My Documents and Search render in
  under 2 seconds (SC-003/004).
- Upload of `exact-25mb.pdf` completes within 30 seconds locally (SC-002).
- PDF/image preview appears within 3 seconds (SC-005).

## Offline check

Disconnect the network and repeat scenarios 1, 4 and 12: functionality works (Bootstrap styling
from CDN may be missing — known constitution exception).
