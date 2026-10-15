// The planar run's conductor-thickness correction (PlanarThicknessCorrection, opt-in through the .cem's
// ConductorThicknessCorrection) and the technology's conductor roughness (StackupLayer.RoughnessRmsM)
// reaching the EM kernels and the microstrip circuit models.

using CircuitRF.Core.Devices.Microstrip;
using CircuitRF.Design.Layout;
using CircuitRF.Design.Layout.Em;
using CircuitRF.Design.Schematic;
using CircuitRF.Engine.Mom;
using CircuitRF.Ui.Layout;
using Xunit;

namespace CircuitRF.Ui.Tests.Em;

public sealed class ConductorThicknessAndRoughnessTests
{
    private const int Dbu = LayoutUnits.DefaultDbuPerMicron;
    private static readonly LayerKey TopCopper = new(1, 0);
    private static long Um(double um) => (long)Math.Round(um * Dbu);
    private static EmPoint P(double xUm, double yUm) => new(xUm * 1e-6, yUm * 1e-6);

    private static PlanarPolygon Rect(double x0, double y0, double x1, double y1) =>
        new([P(x0, y0), P(x1, y0), P(x1, y1), P(x0, y1)]);

    // ── calibration ──────────────────────────────────────────────────────────────────────────────

    /// <summary>A plain microstrip: 35 µm copper on 1.6 mm FR-4, the strip's band from h to h + t.</summary>
    private static (PlanarThicknessCalibration Cal, double H, double T) Fr4Line(double wUm)
    {
        const double h = 1.6e-3, t = 35e-6;
        var regions = PlanarThicknessCorrection.RegionsFor([(0.0, h, new EmMaterial(4.4, 0.02))], 0, 0, h);
        var (cal, refusal) = PlanarThicknessCorrection.Calibrate(regions, h, h + t, h, wUm * 1e-6);
        Assert.True(cal is not null, refusal);
        return (cal!, h, t);
    }

    [Fact]
    public void TheCalibration_WidensTheSheet_ByAboutWhatTheClosedFormThicknessCorrectionSays()
    {
        var (cal, h, t) = Fr4Line(3000);
        Assert.True(cal.Z0SheetDrawn > cal.Z0Thick, "a zero-thickness sheet must read Z0 HIGHER than the thick strip");
        Assert.InRange(cal.Z0Thick, 45, 55);                                     // a ~50 Ω line

        // Hammerstad-Jensen's own effective-width offset is the independent reference: same physics,
        // closed form. The 2-D solve must land in its neighbourhood, not on it.
        HammerstadJensen.ThicknessCorrectedWidths(3e-3 / h, t / h, 4.4, out double u1, out _);
        double hj = (u1 - 3e-3 / h) * h;
        Assert.InRange(cal.DeltaWM / hj, 0.6, 1.6);
        Assert.InRange(cal.DeltaWM, 0.5 * t, 4 * t);
    }

    [Fact]
    public void TheCalibration_IsTheThicknessesOwnEffect_SoNoThicknessMeansNoCorrection()
    {
        var regions = PlanarThicknessCorrection.RegionsFor([(0.0, 1.6e-3, new EmMaterial(4.4, 0.02))], 0, 0, 1.6e-3);
        var (cal, refusal) = PlanarThicknessCorrection.Calibrate(regions, 1.6e-3, 1.6e-3, 1.6e-3, 3e-3);
        Assert.Null(cal);
        Assert.NotNull(refusal);
    }

    // ── growing the outlines ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Growing_WidensALine_AndHoldsBothPortEdgesWhereTheyAreDrawn()
    {
        var line = Rect(0, -400, 5000, 400);
        var (grown, decline, held) = PlanarThicknessCorrection.Grow([line], 30e-6, [P(0, 0), P(5000, 0)]);
        Assert.Null(decline);
        Assert.Equal(2, held);
        var (x0, y0, x1, y1) = Assert.Single(grown!).Bounds();
        Assert.Equal(0, x0, 9);
        Assert.Equal(5000e-6, x1, 9);                                            // length unchanged
        Assert.Equal(-415e-6, y0, 9);                                            // ΔW/2 = 15 µm per side
        Assert.Equal(415e-6, y1, 9);
    }

    [Fact]
    public void Growing_WithNoPorts_GrowsEveryEdge()
    {
        var (grown, _, held) = PlanarThicknessCorrection.Grow([Rect(0, 0, 1000, 500)], 20e-6, []);
        Assert.Equal(0, held);
        var (x0, y0, x1, y1) = Assert.Single(grown!).Bounds();
        Assert.Equal(-10e-6, x0, 9);
        Assert.Equal(1010e-6, x1, 9);
        Assert.Equal(-10e-6, y0, 9);
        Assert.Equal(510e-6, y1, 9);
    }

    [Fact]
    public void Growing_IsDeclined_WhenItWouldJoinTwoConductors()
    {
        // 20 µm apart, ΔW = 30 µm: they would touch, shorting two nets.
        var (grown, decline, _) = PlanarThicknessCorrection.Grow(
            [Rect(0, 0, 1000, 500), Rect(0, 520, 1000, 1000)], 30e-6, []);
        Assert.Null(grown);
        Assert.Contains("closer than", decline);
    }

    [Fact]
    public void Growing_IsDeclined_WhenItWouldCloseASlotInsideOneConductor()
    {
        // A U whose slot is 20 µm wide; ΔW = 30 µm would close it into a hole.
        var u = new PlanarPolygon([P(0, 0), P(1000, 0), P(1000, 1000), P(510, 1000), P(510, 200),
                                   P(490, 200), P(490, 1000), P(0, 1000)]);
        var (grown, decline, _) = PlanarThicknessCorrection.Grow([u], 30e-6, []);
        Assert.Null(grown);
        Assert.Contains("slot or hole", decline);
    }

    [Fact]
    public void Growing_ACurvedOutline_IsNotMistakenForAClosedGap()
    {
        // A flattened quarter ring (radii 300 / 1000 µm, 32 chords per arc): every edge is short and
        // half of them are concave, which a careless closing test reads as filled area.
        var ring = new List<EmPoint>();
        for (int i = 0; i <= 32; i++) { double a = i * Math.PI / 64; ring.Add(P(1000 * Math.Cos(a), 1000 * Math.Sin(a))); }
        for (int i = 32; i >= 0; i--) { double a = i * Math.PI / 64; ring.Add(P(300 * Math.Cos(a), 300 * Math.Sin(a))); }
        var (grown, decline, _) = PlanarThicknessCorrection.Grow([new PlanarPolygon(ring)], 30e-6, []);
        Assert.Null(decline);
        Assert.True(Assert.Single(grown!).Area() > new PlanarPolygon(ring).Area());
    }

    [Fact]
    public void TheReferenceWidth_IsThePortWidth_WhenThereArePorts()
    {
        var line = Rect(0, -400, 5000, 400);
        Assert.Equal(800e-6, PlanarThicknessCorrection.ReferenceWidth([line], [P(0, 0)], out bool fromPorts), 12);
        Assert.True(fromPorts);
        // 2·area/perimeter: 690 µm for a 5 mm × 800 µm strip — the ends count against it.
        Assert.InRange(PlanarThicknessCorrection.ReferenceWidth([line], [], out fromPorts), 650e-6, 800e-6);
        Assert.False(fromPorts);
    }

    // ── through the extractor ────────────────────────────────────────────────────────────────────

    private static List<LayoutShape> PortedLine(double wUm, double lUm) =>
    [
        new RectShape  { Layer = TopCopper, X1 = 0, Y1 = Um(-wUm / 2), X2 = Um(lUm), Y2 = Um(wUm / 2) },
        new LabelShape { Layer = TopCopper, X = 0,       Y = 0, Text = "1", Height = Um(500), IsPort = true },
        new LabelShape { Layer = TopCopper, X = Um(lUm), Y = 0, Text = "2", Height = Um(500), IsPort = true },
    ];

    [Fact]
    public void OffByDefault_TheArtworkReachesTheKernelExactlyAsDrawn()
    {
        var shapes = PortedLine(3000, 20000);
        var a = PlanarExtractor.Extract(shapes, StarterTechnologies.Pcb2Layer(), Dbu, 5e9);
        var b = PlanarExtractor.Extract(shapes, StarterTechnologies.Pcb2Layer(), Dbu, 5e9,
                                        EmExtractionSettings.Default with { ConductorThicknessCorrection = false });
        Assert.True(a.Ok, a.Refusal);
        Assert.Equal(EmSnpProvenance.GeometryHash(a.Problem!), EmSnpProvenance.GeometryHash(b.Problem!));
        var (_, y0, _, y1) = Assert.Single(Assert.Single(a.Problem!.Layers).Polygons).Bounds();
        Assert.Equal(3000e-6, y1 - y0, 9);
        Assert.DoesNotContain(a.Notes, n => n.StartsWith("Conductor thickness", StringComparison.Ordinal));
    }

    [Fact]
    public void On_TheLineIsWidenedByItsSheetEquivalentWidth_PortEdgesHeld_AndTheRunSaysSo()
    {
        var x = PlanarExtractor.Extract(PortedLine(3000, 20000), StarterTechnologies.Pcb2Layer(), Dbu, 5e9,
                                        EmExtractionSettings.Default with { ConductorThicknessCorrection = true });
        Assert.True(x.Ok, x.Refusal);
        var (x0, y0, x1, y1) = Assert.Single(Assert.Single(x.Problem!.Layers).Polygons).Bounds();
        Assert.Equal(0, x0, 9);
        Assert.Equal(20000e-6, x1, 9);                                           // reference planes did not move
        Assert.InRange((y1 - y0) - 3000e-6, 10e-6, 150e-6);                      // ΔW, tens of µm for 35 µm copper
        var note = Assert.Single(x.Notes, n => n.StartsWith("Conductor thickness", StringComparison.Ordinal));
        Assert.Contains("2 port edge(s) held", note);
        Assert.Contains("the port width", note);
    }

    // ── roughness ────────────────────────────────────────────────────────────────────────────────

    private static Technology RoughPcb(double roughnessM)
    {
        var tech = StarterTechnologies.Pcb2Layer();
        foreach (var l in tech.Stackup.Layers)
            if (l.Kind == StackupKind.Conductor) l.RoughnessRmsM = roughnessM;
        return tech;
    }

    [Fact]
    public void Roughness_RoundTripsThroughTheCtech_AndASmoothTechnologyWritesNoKey()
    {
        var smoothJson = TechPersistence.Serialize(StarterTechnologies.Pcb2Layer());
        Assert.DoesNotContain("RoughnessRmsM", smoothJson);

        var back = TechPersistence.Deserialize(TechPersistence.Serialize(RoughPcb(1.2e-6)));
        Assert.All(back.Stackup.Layers.Where(l => l.Kind == StackupKind.Conductor),
                   l => Assert.Equal(1.2e-6, l.RoughnessRmsM));
    }

    [Fact]
    public void Roughness_ReachesThePlanarLevel_AndTheReturnPlane()
    {
        var x = PlanarExtractor.Extract(PortedLine(3000, 20000), RoughPcb(1.2e-6), Dbu, 5e9);
        Assert.True(x.Ok, x.Refusal);
        Assert.Equal(1.2e-6, Assert.Single(x.Problem!.Layers).RoughnessRmsM);
        var floor = x.Problem!.MediumStack?.Bottom ?? x.Problem!.Slab.Floor;
        Assert.Equal(TerminationKind.SurfaceImpedance, floor.Kind);
        Assert.Equal(1.2e-6, floor.RoughnessRmsM);
        Assert.NotEqual(EmSnpProvenance.GeometryHash(x.Problem!),
            EmSnpProvenance.GeometryHash(PlanarExtractor.Extract(PortedLine(3000, 20000), StarterTechnologies.Pcb2Layer(), Dbu, 5e9).Problem!));
    }

    [Fact]
    public void Roughness_ReachesTheMicrostripCircuitModels_OnlyWhenTheTechnologyStatesIt()
    {
        var rough = MicrostripSubstrateInjection.BuildOverrides(RoughPcb(1.2e-6), out _);
        Assert.Equal("1.2E-06", Assert.Single(rough, o => o.Name == "Roughness").Expression);

        var smooth = MicrostripSubstrateInjection.BuildOverrides(StarterTechnologies.Pcb2Layer(), out _);
        Assert.DoesNotContain(smooth, o => o.Name == "Roughness");
    }

    [Fact]
    public void TheCemOption_RoundTrips_AndIsOmittedWhenOff()
    {
        var setup = new EmSetup { Name = "t", LayoutRef = "x.clay" };
        Assert.DoesNotContain("ConductorThicknessCorrection", EmSetupPersistence.Serialize(setup));
        setup.ConductorThicknessCorrection = true;
        var back = EmSetupPersistence.Deserialize(EmSetupPersistence.Serialize(setup));
        Assert.True(back.ConductorThicknessCorrection);
        Assert.True(back.ToExtractionSettings().ConductorThicknessCorrection);
        Assert.Contains("ConductorThicknessCorrection", back.PlanarOnlyFieldsSet());
    }
}
