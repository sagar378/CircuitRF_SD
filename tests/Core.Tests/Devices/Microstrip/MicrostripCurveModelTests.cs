using CircuitRF.Core.Devices;
using CircuitRF.Core.Devices.Microstrip;
using CircuitRF.Core.Elaboration;
using CircuitRF.Core.Expressions;
using Xunit;

namespace CircuitRF.Core.Tests.Devices.Microstrip;

/// <summary>
/// MCURVE: a radiused bend is MLIN over the effective length (R − 0.2·W)·|θ|, and nothing else. The
/// stamps are compared bit for bit against an MLIN built at that length, because "one implementation"
/// is the claim (MicrostripCurveModel's own header) and a tolerance would let a second copy of the line
/// physics hide in it.
/// </summary>
public class MicrostripCurveModelTests
{
    private const double W = 2.9e-3, H = 1.6e-3, T = 35e-6, Er = 4.4, Sigma = 5.8e7, TanD = 0.02;

    private static ElaboratedComponent MakeEc(ComponentModel model, string type)
        => new(type, "X1", [1, 2], new Dictionary<string, Value>(), model);

    private static Dictionary<(int Row, int Col), System.Numerics.Complex> Stamp(ComponentModel m, string type, double fHz)
    {
        var mna = new CapturingMnaContext();
        m.Stamp(mna, MakeEc(m, type), 2 * Math.PI * fHz);
        return mna.Entries;
    }

    [Fact]
    public void EffectiveRadius_IsCentreLineMinusOneFifthOfTheWidth()
    {
        // eq (26): R_eff = R_inner + 0.3W, and R_inner = R - W/2.
        double r = 10e-3, rInner = r - W / 2;
        Assert.Equal(rInner + 0.3 * W, MicrostripCurve.EffectiveRadius(r, W), 15);
        Assert.Equal((r - 0.2 * W) * Math.PI / 2, MicrostripCurve.ElectricalLength(r, W, 90), 15);
        Assert.Equal(r * Math.PI / 2, MicrostripCurve.CentreLineLength(r, 90), 15);
    }

    [Theory]
    [InlineData(90.0, 1e9)]
    [InlineData(45.0, 3.5e9)]
    [InlineData(180.0, 6e9)]
    public void Stamps_AreExactlyAnMlinOfTheEffectiveLength(double angleDeg, double fHz)
    {
        double r = 3 * W;
        var curve = new MicrostripCurveModel(W, angleDeg, r, H, T, Er, Sigma, TanD, "MCURVE:X1");
        var line = new MicrostripLineModel(W, (r - 0.2 * W) * angleDeg * Math.PI / 180, H, T, Er, Sigma, TanD, "MLIN:X1");

        var a = Stamp(curve, "MCURVE", fHz); var b = Stamp(line, "MLIN", fHz);
        Assert.Equal(b.Keys.OrderBy(k => k), a.Keys.OrderBy(k => k));
        foreach (var k in b.Keys) Assert.Equal(b[k], a[k]);
    }

    [Fact]
    public void TheElectricalPath_IsShorterThanTheDrawnCentreLine()
    {
        var curve = new MicrostripCurveModel(W, 90, 4 * W, H, T, Er, Sigma, TanD, "MCURVE:X1");
        Assert.True(curve.ElectricalLengthMeters < curve.CentreLineLengthMeters);
        Assert.Equal(0.2 * W * Math.PI / 2, curve.CentreLineLengthMeters - curve.ElectricalLengthMeters, 15);
    }

    [Fact]
    public void TheSignOfAngle_SteersOnlyTheArtwork()
    {
        var left = new MicrostripCurveModel(W, 90, 4 * W, H, T, Er, Sigma, TanD, "MCURVE:X1");
        var right = new MicrostripCurveModel(W, -90, 4 * W, H, T, Er, Sigma, TanD, "MCURVE:X1");
        var a = Stamp(left, "MCURVE", 2e9); var b = Stamp(right, "MCURVE", 2e9);
        foreach (var k in a.Keys) Assert.Equal(a[k], b[k]);
    }

    [Fact]
    public void AtDc_ItIsAThroughConnection_WithNoNaN()
    {
        var curve = new MicrostripCurveModel(W, 90, 3 * W, H, T, Er, Sigma, TanD, "MCURVE:X1");
        foreach (var v in Stamp(curve, "MCURVE", 0).Values)
            Assert.False(double.IsNaN(v.Real) || double.IsNaN(v.Imaginary));
    }

    [Theory]
    [InlineData(0.49)]          // inner edge would cross the centre of curvature
    [InlineData(0.0)]
    public void ARadiusBelowHalfTheWidth_IsRefused_NamingTheBound(double rOverW)
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            new MicrostripCurveModel(W, 90, rOverW * W, H, T, Er, Sigma, TanD, "MCURVE:X1"));
        Assert.Contains("below W/2", ex.Message);
    }

    [Fact]
    public void ATurnPastAFullCircle_IsRefused()
    {
        Assert.Throws<ArgumentException>(() =>
            new MicrostripCurveModel(W, 361, 3 * W, H, T, Er, Sigma, TanD, "MCURVE:X1"));
    }

    [Fact]
    public void InsideThePublishedRange_NothingIsReported()
    {
        var curve = new MicrostripCurveModel(W, 90, 5 * W, H, T, Er, Sigma, TanD, "MCURVE:X1");
        Assert.DoesNotContain(curve.DrainWarnings(), w => w.Key.StartsWith("MCURVE", StringComparison.Ordinal));
    }

    [Fact]
    public void ATightBend_SaysToEmSimulate()
    {
        var curve = new MicrostripCurveModel(W, 90, 1.5 * W, H, T, Er, Sigma, TanD, "MCURVE:X1");
        var w = Assert.Single(curve.DrainWarnings(), m => m.Key == "MCURVE:tight");
        Assert.Contains("EM-simulate", w.Message);
    }

    [Theory]
    [InlineData(2.5)]
    [InlineData(10.0)]
    public void OutsideThePublishedRange_IsReportedAsAnExtrapolation_ButStillComputed(double rOverW)
    {
        var curve = new MicrostripCurveModel(W, 90, rOverW * W, H, T, Er, Sigma, TanD, "MCURVE:X1");
        Assert.Contains(curve.DrainWarnings(), m => m.Message.Contains("outside its published validity range"));
        Assert.Equal((rOverW - 0.2) * W * Math.PI / 2, curve.ElectricalLengthMeters, 15);
    }

    [Fact]
    public void Factory_BuildsMcurve_FromItsOwnParameterNames()
    {
        var parms = new Dictionary<string, Value>
        {
            // Radians: the elaborator has already applied "deg" (pi/180), as for TLIN's E.
            ["W"] = new(W), ["Angle"] = new(Math.PI / 2), ["Radius"] = new(4 * W),
            ["H"] = new(H), ["T"] = new(T), ["Er"] = new(Er),
        };
        var model = Assert.IsType<MicrostripCurveModel>(ComponentModelFactory.TryCreate("MCURVE", parms));
        Assert.Equal((4 * W - 0.2 * W) * Math.PI / 2, model.ElectricalLengthMeters, 15);
        Assert.True(ComponentModelFactory.IsPrimitive("MCURVE"));
    }
}
