using CircuitRF.Core.Devices.Microstrip;
using CircuitRF.Core.Elaboration;

namespace CircuitRF.Core.Devices;

/// <summary>
/// MCURVE — a continuously curved (radiused) microstrip bend, 2-port.
///
/// <para><b>It is MLIN over an effective length, and deliberately nothing else.</b> The line physics —
/// Hammerstad-Jensen Z₀/εeff, Kirschning-Jansen dispersion, conductor and dielectric loss — is
/// <see cref="MicrostripLineModel"/>'s, reached by constructing one rather than by a second copy of
/// it (R-pc-11's "one implementation"). What MCURVE adds is the length: the curve is stamped as a
/// uniform line of <see cref="MicrostripCurve.ElectricalLength"/> = <c>(R − 0.2·W)·|θ|</c>, the
/// effective-radius rule <see cref="MicrostripCurve"/> documents with its source. The difference from
/// MBEND is the whole point of the part: a right-angle MBEND is a zero-length corner discontinuity
/// (an L-C-L), while a radiused bend has no corner to speak of and behaves as the line it is.</para>
///
/// <para>Parameters (SI, resolved by the elaborator; the substrate injected as MLIN's is):
/// <c>W</c> — width (m). <c>Angle</c> — turn, counter-clockwise positive from the input arm (MBEND's
/// convention); only <c>|Angle|</c> is electrical, the sign says which way the artwork turns. This
/// constructor takes DEGREES; the factory receives the elaborated value in radians (as TLIN's <c>E</c>)
/// and converts once.
/// <c>Radius</c> — CENTRE-LINE radius (m). <c>H</c>, <c>T</c>, <c>Er</c>, <c>Sigma</c>, <c>TanD</c>,
/// optional <c>Roughness</c> — exactly MLIN's.</para>
///
/// <para>The reference planes are the two ends of the arc, so an MCURVE abuts an MLIN with no
/// overlap to subtract — unlike MBEND, whose planes sit on the edges of the W×W corner square.</para>
/// </summary>
public sealed class MicrostripCurveModel : ComponentModel, IReportsWarnings
{
    public override int PortCount => 2;
    public override ModelKind Kind => ModelKind.Linear;

    private readonly MicrostripLineModel _line;
    private readonly MicrostripValidityReporter _reporter;

    /// <summary>The electrical length the bend is stamped as, metres.</summary>
    public double ElectricalLengthMeters { get; }

    /// <summary>The drawn centre-line arc length, metres.</summary>
    public double CentreLineLengthMeters { get; }

    public IReadOnlyList<(string Key, string Message)> DrainWarnings()
        => [.. _reporter.Drain(), .. _line.DrainWarnings()];

    public MicrostripCurveModel(double wMeters, double angleDeg, double radiusMeters,
        double hMeters, double tMeters, double epsR, double sigmaSPerM, double tanD,
        string instancePath, double roughnessMeters = 0.0)
    {
        MicrostripCurve.Validate(wMeters, radiusMeters, angleDeg);
        _reporter = new MicrostripValidityReporter(instancePath);
        MicrostripCurve.ReportValidity(wMeters, radiusMeters, _reporter);

        ElectricalLengthMeters = MicrostripCurve.ElectricalLength(radiusMeters, wMeters, angleDeg);
        CentreLineLengthMeters = MicrostripCurve.CentreLineLength(radiusMeters, angleDeg);
        _line = new MicrostripLineModel(wMeters, ElectricalLengthMeters, hMeters, tMeters, epsR,
                                        sigmaSPerM, tanD, instancePath, roughnessMeters);
    }

    public override void Stamp(IMnaContext mna, ElaboratedComponent c, double omega)
        => _line.Stamp(mna, c, omega);
}
