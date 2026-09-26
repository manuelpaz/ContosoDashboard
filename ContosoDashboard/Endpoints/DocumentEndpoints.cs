using System.Security.Claims;
using ContosoDashboard.Services;

namespace ContosoDashboard.Endpoints;

/// <summary>
/// HTTP endpoints that stream stored document files. Files live outside wwwroot, so every
/// request goes through IDocumentService authorization. Denied and missing documents both
/// return 404 so a document's existence is never revealed.
/// </summary>
public static class DocumentEndpoints
{
    public static IEndpointRouteBuilder MapDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/documents/{documentId:int}/download", DownloadAsync)
            .RequireAuthorization();

        return app;
    }

    private static async Task<IResult> DownloadAsync(int documentId, ClaimsPrincipal user, IDocumentService documentService)
    {
        if (!TryGetUserId(user, out var userId))
        {
            return Results.NotFound();
        }

        var content = await documentService.OpenDocumentAsync(documentId, userId, preview: false);
        if (content == null)
        {
            return Results.NotFound();
        }

        // The file result disposes the stream after writing the response
        return Results.File(content.Content, content.ContentType, fileDownloadName: content.FileName);
    }

    private static bool TryGetUserId(ClaimsPrincipal user, out int userId)
    {
        userId = 0;
        var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier);
        return userIdClaim != null && int.TryParse(userIdClaim.Value, out userId);
    }
}
