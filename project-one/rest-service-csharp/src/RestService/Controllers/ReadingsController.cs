using Microsoft.AspNetCore.Mvc;
using RestService.Application.Services;
using RestService.Contracts;

namespace RestService.Controllers;

[ApiController]
[Route("api/readings")]
public sealed class ReadingsController(IReadingsService readingsService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ReadingSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<ReadingSummaryDto>>> GetReadings(
        [FromQuery] long? deviceId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string? sensors,
        [FromQuery] int limit = 100,
        [FromQuery] int offset = 0,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0 || limit > 1000)
        {
            return BadRequest("Query parameter 'limit' must be between 1 and 1000.");
        }

        if (offset < 0)
        {
            return BadRequest("Query parameter 'offset' must be 0 or greater.");
        }

        if (from.HasValue && to.HasValue && from > to)
        {
            return BadRequest("Query parameter 'from' must be less than or equal to 'to'.");
        }

        var query = new ReadingsQuery(deviceId, from, to, ParseSensorCodes(sensors), limit, offset);
        var readings = await readingsService.GetReadingsAsync(query, cancellationToken);
        return Ok(readings);
    }

    [HttpGet("{id:long}")]
    [ProducesResponseType(typeof(ReadingDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReadingDetailDto>> GetReadingById(long id, CancellationToken cancellationToken)
    {
        var reading = await readingsService.GetReadingByIdAsync(id, cancellationToken);
        return reading is null ? NotFound() : Ok(reading);
    }

    [HttpPost]
    [ProducesResponseType(typeof(CreateReadingResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CreateReadingResponse>> CreateReading(
        [FromBody] CreateReadingRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var id = await readingsService.CreateReadingAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetReadingById), new { id }, new CreateReadingResponse(id));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("aggregate")]
    [ProducesResponseType(typeof(IReadOnlyList<ReadingAggregatePointDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<ReadingAggregatePointDto>>> GetAggregate(
        [FromQuery] string sensorCode,
        [FromQuery] int bucketMinutes,
        [FromQuery] long? deviceId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sensorCode))
        {
            return BadRequest("Query parameter 'sensorCode' is required.");
        }

        if (bucketMinutes <= 0 || bucketMinutes > 1440)
        {
            return BadRequest("Query parameter 'bucketMinutes' must be between 1 and 1440.");
        }

        if (from.HasValue && to.HasValue && from > to)
        {
            return BadRequest("Query parameter 'from' must be less than or equal to 'to'.");
        }

        var aggregate = await readingsService.GetAggregatesAsync(
            sensorCode.Trim().ToLowerInvariant(),
            bucketMinutes,
            deviceId,
            from,
            to,
            cancellationToken);

        return Ok(aggregate);
    }

    private static IReadOnlyList<string>? ParseSensorCodes(string? sensors)
    {
        if (string.IsNullOrWhiteSpace(sensors))
        {
            return null;
        }

        var parsed = sensors
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(code => code.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return parsed.Length == 0 ? null : parsed;
    }
}
