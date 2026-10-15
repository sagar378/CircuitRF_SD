// SURFACE ROUGHNESS — ONE FACTOR, SHARED WITH THE CIRCUIT MODELS.
//
// A rough conductor surface lengthens the path the skin-effect current follows once the skin depth is
// comparable to the roughness, so the surface impedance rises. This file supplies that rise as a
// multiplier K(f) on a conductor's surface impedance, and it supplies it by CALLING the circuit
// models' own correction (MicrostripLoss.RoughnessFactor, Hammerstad-Bekkadal) rather than restating
// it: an MLIN and a full-wave run of the same line must disagree about geometry, never about what
// roughness does to copper.
//
//     K = 1 + (2/π)·arctan(1.4·(Δ/δ)²)        Δ = RMS roughness, δ = skin depth
//
// K rises from 1 (smooth, or δ ≫ Δ) to 2 (δ ≪ Δ). It is applied to the WHOLE complex surface
// impedance, not to its real part alone. A good conductor's smooth-surface impedance has R = ωL_int
// — the minimum-phase relation of the skin effect — and multiplying R alone breaks it, which is the
// classic non-causal way to add roughness. Scaling both keeps the internal inductance rising with the
// resistance, as it physically does.
//
// WHAT THIS CANNOT CARRY, SAID HERE: a single conductor level is one sheet in the planar kernel, so it
// cannot tell a rough (treated, substrate-side) face from a smooth (air-side) one; the factor applies
// to the whole sheet. That is the same convention the circuit models use (the correction multiplies
// the whole conductor loss), and it overstates the loss when only one face is rough. Hammerstad-
// Bekkadal also saturates at 2, which modern measurements of very rough foils exceed; a Huray
// (snowball) model is the usual next step and is not built.
//
// Δ ≤ 0, or a perfect conductor (σ ≤ 0 or σ = +∞), returns EXACTLY 1.0, so a smooth technology's
// surface impedance is bit-identical to the one computed before roughness existed.

using CircuitRF.Core.Devices.Microstrip;

namespace CircuitRF.Engine.Mom;

/// <summary>
/// The surface-roughness multiplier on a conductor's surface impedance — see the file header for the
/// model, why it scales the whole complex impedance, and what a single sheet cannot represent.
/// </summary>
public static class ConductorRoughness
{
    /// <summary>
    /// K(ω) for RMS roughness <paramref name="roughnessRmsM"/> on a conductor of conductivity
    /// <paramref name="sigmaSm"/>. Exactly 1.0 for a smooth surface, a perfect conductor, or ω ≤ 0
    /// (no skin effect, so nothing for the roughness to lengthen).
    /// </summary>
    public static double Factor(double roughnessRmsM, double sigmaSm, double omegaRadS)
    {
        if (!(roughnessRmsM > 0) || !(sigmaSm > 0) || double.IsPositiveInfinity(sigmaSm) || !(omegaRadS > 0))
            return 1.0;
        return MicrostripLoss.RoughnessFactor(roughnessRmsM, PlanarSurfaceImpedance.SkinDepthM(sigmaSm, omegaRadS));
    }
}
