# Contract: HTTP Endpoints

Defined in `ContosoDashboard/Endpoints/DocumentEndpoints.cs` as minimal API endpoints, mapped in
`Program.cs` after `UseAuthorization()`. Both require an authenticated cookie
(`.RequireAuthorization()`); the user ID comes from the `NameIdentifier` claim. The global
security-header middleware applies unchanged.

## GET `/documents/{documentId:int}/download`

| Case | Response |
|------|----------|
| Authorized, file present | `200`, body = file bytes, `Content-Type` = stored `FileType`, `Content-Disposition: attachment; filename="<OriginalFileName>"` (RFC 6266 encoding via `Results.File(..., fileDownloadName)`) |
| Not authenticated | `302` → `/login` (cookie auth default) |
| No access **or** document does not exist | `404` (identical response — FR-017) |
| Record exists but file missing on disk | `404` (logged server-side) |

Side effect: `DocumentActivity` `Download`.

## GET `/documents/{documentId:int}/preview`

Same as download, except:

- Only for PDF (`application/pdf`) and images (`image/jpeg`, `image/png`); other types → `404`.
- `Content-Disposition: inline`.
- Used by `<img src>` (images) and `target="_blank"` links (PDF). Not framed (X-Frame-Options
  stays `DENY`).

Side effect: `DocumentActivity` `Preview`.

## Not provided

- No upload endpoint: uploads go through the Blazor `InputFile` component and
  `IDocumentService` over the existing authenticated SignalR circuit.
- No JSON API; all other operations are Blazor UI actions calling services.
