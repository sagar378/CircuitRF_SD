using CircuitRF.Design.Layout;
using CircuitRF.Design.Layout.PCells;
using CircuitRF.Design.Schematic;
using CircuitRF.Design.Layout.Lvs;
using Xunit;

namespace CircuitRF.Ui.Tests.Layout.PCells;

/// <summary>
/// MCurve artwork: one CurveShape whose long edges are TRUE arcs, pin 1 at the origin heading +X, pin 2
/// at the end of the turn, and the flattened copper area equal to the annular sector it claims to be.
/// </summary>
public class MCurvePCellTests
{
    private const double W = 300e-6, R = 900e-6;
    private const int Dbu = 1000;                              // LayoutUnits.DefaultDbuPerMicron

    private static PCellResult Gen(double w, double angleDeg, double radius)
    {
        Assert.True(PCellRegistry.TryGet("MCURVE", out var generate));
        return generate(PCellParameters.FromReals(new Dictionary<string, double>
            { ["W"] = w, ["Angle"] = angleDeg, ["Radius"] = radius }), null, PCellLayerSelection.Default);
    }

    private static double Area(IReadOnlyList<long[]> rings)
    {
        double a = 0;
        foreach (var xy in rings)
        {
            int n = xy.Length / 2;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                a += (double)xy[2 * i] * xy[2 * j + 1] - (double)xy[2 * j] * xy[2 * i + 1];
            }
        }
        return Math.Abs(a) / 2;
    }

    [Fact]
    public void TheOutline_IsOneCurve_WithTwoTrueArcEdges_AtTanQuarterSweep()
    {
        var res = Gen(W, 90, R);
        var curve = Assert.IsType<CurveShape>(Assert.Single(res.Shapes));
        var arcs = curve.Edges!.Where(e => e.Kind == EdgeKind.Arc).ToList();
        Assert.Equal(2, arcs.Count);
        Assert.Equal(Math.Tan(Math.PI / 8), arcs[0].Bulge, 12);    // outer, counter-clockwise
        Assert.Equal(-Math.Tan(Math.PI / 8), arcs[1].Bulge, 12);   // inner, back the other way
        Assert.Null(res.Diagnostics);
    }

    [Theory]
    [InlineData(90.0)]
    [InlineData(-90.0)]
    [InlineData(45.0)]
    [InlineData(180.0)]
    public void Pins_AreAtBothEndsOfTheCentreLine_FacingAlongTheTangent(double angleDeg)
    {
        var res = Gen(W, angleDeg, R);
        double s = Math.Sign(angleDeg), t = Math.Abs(angleDeg) * Math.PI / 180, r = R * 1e6 * Dbu;
        var p1 = res.Pins.Single(p => p.Name == "1"); var p2 = res.Pins.Single(p => p.Name == "2");
        Assert.Equal((0L, 0L, 180.0), (p1.X, p1.Y, p1.OutwardDirectionDeg));
        Assert.Equal(Math.Round(r * Math.Sin(t)), p2.X);
        Assert.Equal(Math.Round(s * r * (1 - Math.Cos(t))), p2.Y);
        Assert.Equal(angleDeg, p2.OutwardDirectionDeg);
        Assert.Equal(300 * Dbu, p2.WidthDbu);
    }

    [Fact]
    public void FlattenedCopper_IsTheAnnularSector()
    {
        var curve = (CurveShape)Gen(W, 90, R).Shapes[0];
        double ro = (R + W / 2) * 1e6 * Dbu, ri = (R - W / 2) * 1e6 * Dbu;
        double expected = Math.PI / 4 * (ro * ro - ri * ri);
        double area = Area(LayoutFlattener.Flatten(curve, tolDbu: 100));   // 0.1 um chord tolerance
        Assert.InRange(area / expected, 0.999, 1.0001);
    }

    [Fact]
    public void AtRadiusHalfTheWidth_TheInnerEdgeCollapsesToTheCentre_AndNothingIsReported()
    {
        var res = Gen(W, 90, W / 2);
        var curve = (CurveShape)res.Shapes[0];
        Assert.Single(curve.Edges!, e => e.Kind == EdgeKind.Arc);
        Assert.Null(res.Diagnostics);
    }

    [Fact]
    public void BelowHalfTheWidth_ItIsDrawnAtHalfTheWidth_AndSaysSo()
    {
        var res = Gen(W, 90, 0.3 * W);
        Assert.Contains(res.Diagnostics!, d => d.Contains("below W/2"));
        Assert.Single(((CurveShape)res.Shapes[0]).Edges!, e => e.Kind == EdgeKind.Arc);
    }

    [Fact]
    public void ItIsAMicrostripKind_ARoundTripLvsDevice_AndAnEngineType()
    {
        Assert.True(MicrostripSubstrateInjection.IsMicrostripKind(SymbolKind.MCurve));
        Assert.Equal("MCURVE", ComponentTypeRegistry.EngineReference(SymbolKind.MCurve));
        Assert.True(DeviceTypes.TryGetSymbolKind("MCURVE", out var kind));
        Assert.Equal(SymbolKind.MCurve, kind);
    }
}
