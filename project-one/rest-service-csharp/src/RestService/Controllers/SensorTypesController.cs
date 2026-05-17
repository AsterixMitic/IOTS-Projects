using Microsoft.AspNetCore.Mvc;
using RestService.Application.Services;
using RestService.Contracts;

namespace RestService.Controllers;

[ApiController]
[Route("api/sensor-types")]
public sealed class SensorTypesController(IReadingsService readingsService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<SensorTypeDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SensorTypeDto>>> GetSensorTypes(CancellationToken cancellationToken)
    {
        var sensorTypes = await readingsService.GetSensorTypesAsync(cancellationToken);
        return Ok(sensorTypes);
    }
}
