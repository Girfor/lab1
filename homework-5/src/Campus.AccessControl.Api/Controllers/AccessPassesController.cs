using System.Diagnostics;
using Campus.AccessControl.Api.Data;
using Campus.AccessControl.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace Campus.AccessControl.Api.Controllers;

[ApiController]
[Route("api/access-passes")]
public sealed class AccessPassesController(AccessPassRepository repository) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<AccessPass>> Create(
        [FromBody] CreateAccessPassRequest request, CancellationToken cancellationToken)
    {
        Activity.Current?.SetTag("jira.issue", "CAMPUS-101");

        if (!AccessPassRules.IsValid(request, DateTimeOffset.UtcNow))
        {
            return BadRequest(new { error = "Invalid person, badge code or expiry." });
        }

        var pass = new AccessPass(Guid.NewGuid(), request.PersonId,
            request.BadgeCode.Trim(), request.ValidUntil.ToUniversalTime(), true,
            request.IsTest);
        Activity.Current?.SetTag("campus.pass_id", pass.Id.ToString("D"));

        await repository.CreateAsync(pass, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = pass.Id }, pass);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AccessPass>> Get(
        Guid id, CancellationToken cancellationToken)
    {
        Activity.Current?.SetTag("jira.issue", "CAMPUS-101");
        Activity.Current?.SetTag("campus.pass_id", id.ToString("D"));

        var pass = await repository.GetAsync(id, cancellationToken);
        return pass is null ? NotFound() : Ok(pass);
    }

    // Cleanup endpoint only deletes records that were explicitly marked as tests.
    [HttpDelete("{id:guid}/test")]
    public async Task<IActionResult> DeleteTest(
        Guid id, CancellationToken cancellationToken)
    {
        Activity.Current?.SetTag("jira.issue", "CAMPUS-101");
        Activity.Current?.SetTag("campus.pass_id", id.ToString("D"));

        var removed = await repository.DeleteTestAsync(id, cancellationToken);
        return removed ? NoContent() : NotFound();
    }
}
