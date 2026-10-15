// Surface roughness in the EM kernels: one Hammerstad-Bekkadal factor (MicrostripLoss.RoughnessFactor,
// the circuit models' own) multiplying a conductor's surface impedance — the planar sheet, the planar
// ground plane and the cross-section kernel's per-surface Wheeler term. A smooth conductor must be
// BIT-identical to the pre-roughness answer, because every cached result and accuracy figure sits on it.

using System;
using System.Numerics;
using CircuitRF.Core.Devices.Microstrip;
using CircuitRF.Engine.Mom;
using CircuitRF.Engine.Tests.Mom.Support;
using Xunit;

namespace CircuitRF.Engine.Tests.Mom;

public sealed class ConductorRoughnessTests
{
    private const double Sigma = 5.8e7, T = 35e-6, Rough = 1.5e-6;
    private static readonly double W10 = 2 * Math.PI * 10e9;

    [Fact]
    public void TheFactor_IsExactlyOne_ForASmoothOrPerfectConductor_OrAtDc()
    {
        Assert.Equal(1.0, ConductorRoughness.Factor(0, Sigma, W10));
        Assert.Equal(1.0, ConductorRoughness.Factor(Rough, double.PositiveInfinity, W10));
        Assert.Equal(1.0, ConductorRoughness.Factor(Rough, 0, W10));
        Assert.Equal(1.0, ConductorRoughness.Factor(Rough, Sigma, 0));
    }

    [Fact]
    public void TheFactor_IsTheCircuitModelsOwnHammerstadBekkadal()
    {
        double f = 3.5e9;
        double expected = MicrostripLoss.RoughnessFactor(Rough, MicrostripLoss.SkinDepth(f, Sigma));
        Assert.Equal(expected, ConductorRoughness.Factor(Rough, Sigma, 2 * Math.PI * f), 12);
        Assert.InRange(expected, 1.0, 2.0);
        // Saturates toward 2 once the skin depth is far below the roughness.
        Assert.True(ConductorRoughness.Factor(10e-6, Sigma, 2 * Math.PI * 40e9) > 1.95);
    }

    private static PlanarProblem OneLevel(double roughness) =>
        new([new PlanarConductorLayer("M1", [], Sigma, T, double.NaN, roughness)], GroundedSlab.Fr4Starter, 10e9);

    [Fact]
    public void ThePlanarSheet_ScalesByTheFactor_AndASmoothSheetIsBitIdentical()
    {
        var smooth = PlanarConductorLoss.For(OneLevel(0)).SheetAt(0, W10);
        Assert.Equal(PlanarSurfaceImpedance.Sheet(Sigma, T, W10), smooth);              // exact, not close

        var rough = PlanarConductorLoss.For(OneLevel(Rough)).SheetAt(0, W10);
        double k = ConductorRoughness.Factor(Rough, Sigma, W10);
        Assert.Equal(k, rough.Real / smooth.Real, 12);
        Assert.Equal(k, rough.Imaginary / smooth.Imaginary, 12);                         // the WHOLE Z_s scales
    }

    [Fact]
    public void TheGroundPlane_ScalesByTheFactor_AndASmoothPlaneIsBitIdentical()
    {
        Assert.Equal(PlanarSurfaceImpedance.Plane(Sigma, T, W10), Termination.LossyGround(Sigma, T).SurfaceImpedanceAt(W10));
        Assert.Equal(Termination.LossyGround(Sigma, T), Termination.LossyGround(Sigma, T, 0));

        var rough = Termination.LossyGround(Sigma, T, Rough);
        Assert.NotEqual(Termination.LossyGround(Sigma, T), rough);                       // record equality sees it
        Assert.Equal(ConductorRoughness.Factor(Rough, Sigma, W10),
                     rough.SurfaceImpedanceAt(W10).Real / PlanarSurfaceImpedance.Plane(Sigma, T, W10).Real, 12);
    }

    [Fact]
    public void TheCrossSectionKernel_ScalesEachSurfacesSkinResistanceByItsOwnFactor()
    {
        var p = EmProblemBuilders.Microstrip(3.0e-3, 1.6e-3, T, 4.4);
        RlgcModel Extract(EmProblem q) => RlgcExtractor.Extract(q, BoundaryMesher.Mesh(q, EmMeshSettings.Default));
        var smooth = Extract(p);
        var rough  = Extract(p with
        {
            Conductors = [p.Conductors[0] with { RoughnessRmsM = Rough }],
            Ground     = p.Ground! with { RoughnessRmsM = Rough },
        });

        // At 10 GHz the DC floor is negligible against the skin term, so R itself scales by K.
        double k = ConductorRoughness.Factor(Rough, Sigma, W10);
        Assert.Equal(k, rough.RMatrix(W10)[0, 0] / smooth.RMatrix(W10)[0, 0], 3);
        Assert.Equal(smooth.RMatrix(W10)[0, 0], Extract(p).RMatrix(W10)[0, 0]);         // smooth: unchanged

        // Strip rough, ground smooth: between the two, and strictly above smooth.
        var stripOnly = Extract(p with { Conductors = [p.Conductors[0] with { RoughnessRmsM = Rough }] });
        double r = stripOnly.RMatrix(W10)[0, 0];
        Assert.InRange(r, smooth.RMatrix(W10)[0, 0] * 1.0001, rough.RMatrix(W10)[0, 0] * 0.9999);
    }
}
