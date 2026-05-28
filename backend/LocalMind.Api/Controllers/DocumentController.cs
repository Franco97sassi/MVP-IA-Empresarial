using System.Security.Claims;
using LocalMind.Api.Services.Rag;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LocalMind.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DocumentsController : ControllerBase
{
    private readonly IRagService _ragService;

    public DocumentsController(IRagService ragService)
    {
        _ragService = ragService;
    }

    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> Upload(IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null)
        {
            return BadRequest(new { message = "Tenés que subir un archivo." });
        }

        var userId = GetUserId();

        try
        {
            var document = await _ragService.UploadDocumentAsync(userId, file, cancellationToken);
            return Ok(document);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetDocuments(CancellationToken cancellationToken)
    {
        var documents = await _ragService.GetDocumentsAsync(GetUserId(), cancellationToken);
        return Ok(documents);
    }
    [HttpPost("rag/evaluate")]
    public async Task<IActionResult> EvaluateRag(
       IReadOnlyCollection<RagEvaluationRequest> requests,
       CancellationToken cancellationToken)
    {
        if (requests.Count == 0)
        {
            return BadRequest(new { message = "Agregá al menos una pregunta de evaluación." });
        }

        var summary = await _ragService.EvaluateAsync(
            GetUserId(),
            requests,
            cancellationToken);

        return Ok(summary);
    }

    [HttpPost("{documentId:int}/reindex")]
    public async Task<IActionResult> ReindexDocument(int documentId, CancellationToken cancellationToken)
    {
        var updated = await _ragService.ReindexDocumentAsync(GetUserId(), documentId, cancellationToken);
        if (updated is null)
        {
            return NotFound(new { message = "No se encontró el documento." });
        }

        return Ok(updated);
    }

    [HttpDelete("{documentId:int}")]
    public async Task<IActionResult> DeleteDocument(int documentId, CancellationToken cancellationToken)
    {
        var deleted = await _ragService.DeleteDocumentAsync(GetUserId(), documentId, cancellationToken);
        if (!deleted)
        {
            return NotFound(new { message = "No se encontró el documento." });
        }

        return NoContent();
    }
    [HttpGet("{documentId:int}/chunks")]
    public async Task<IActionResult> GetChunks(int documentId, CancellationToken cancellationToken)
    {
        var chunks = await _ragService.GetDocumentChunksAsync(GetUserId(), documentId, cancellationToken);
        if (chunks.Count == 0)
        {
            return NotFound();
        }

        return Ok(chunks);
    }

    private int GetUserId()
    {
        return int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    }
}
