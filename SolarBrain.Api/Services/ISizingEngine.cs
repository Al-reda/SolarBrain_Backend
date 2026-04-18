using SolarBrain.Api.Models.Dtos;

namespace SolarBrain.Api.Services;

/// <summary>
/// Takes a facility profile from the Layer 1 form and returns a complete
/// hybrid-energy system design: requirements, top-3 components for each
/// category, protection/BoS, optional generator, CAPEX breakdown, 10-year
/// financial model, and the handoff config for the Layer 2 simulation.
/// </summary>
public interface ISizingEngine
{
    Task<SystemDesignDto> SizeSystemAsync(FacilityProfileDto profile);
}
