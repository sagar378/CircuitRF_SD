using CircuitRF.Diagnostics;

namespace CircuitRF.Core.Devices.Microstrip;

/// <summary>
/// MCURVE's geometry-to-electrical-length rule: a continuously curved (radiused) microstrip bend of
/// centre-line radius <c>R</c> and turn angle <c>θ</c> is a uniform line whose ELECTRICAL length is
/// <c>R_eff·|θ|</c>, not the centre-line arc <c>R·|θ|</c>.
///
/// <para><b>Source.</b> Poole &amp; Darwazeh, <i>Lecture 3 - Practical Transmission Lines</i>, eq (26)
/// (the same lecture <see cref="MicrostripBendLC"/> takes the KJK right-angle coefficients from):
/// once the bend radius exceeds about <c>2·W</c> the dominant parasitic of a curved bend is simply a
/// change in effective length, and for <c>3 &lt; R/W &lt; 7</c> the effective radius is
/// <c>R_eff = R_inner + 0.3·W</c>. With <c>R_inner = R − W/2</c> that is
/// <c>R_eff = R − 0.2·W</c>: the electrical path is SHORTER than the centre line, because the current
/// favours the inner edge (brief-mtaper-mklopf.md R-bnd-5). The deeper treatment is Weisshaar et al.,
/// <i>Modeling of radial microstrip bends</i>, IEEE MTT-S 1990, pp. 1051-1054.</para>
///
/// <para><b>What it does not model</b>, and therefore reports rather than hides: differential phase
/// across the width and mode conversion, which grow roughly as <c>(W/R)²</c> and are what make a tight
/// bend more than a length change. Below <c>R = 2·W</c> the source no longer claims a length change is
/// the dominant effect, so a model that is only a length change is no longer trustworthy there — the
/// instance says so and points at EM. Between <c>2·W</c> and <c>3·W</c>, and above <c>7·W</c>, the
/// correction is applied unchanged and reported as outside its published range: above <c>7·W</c> the
/// source says the correction shrinks further, and <c>0.2·W</c> is then at most 3 % of the radius.</para>
/// </summary>
public static class MicrostripCurve
{
    /// <summary>The published range of the effective-radius fit, in units of R/W.</summary>
    public static readonly ValidityRange RadiusOverWidthRange = new(3.0, 7.0);

    /// <summary>Below this R/W a length change is no longer the dominant parasitic (eq 26's own
    /// threshold), so the model's answer stops being trustworthy rather than merely extrapolated.</summary>
    public const double GentleBendRadiusOverWidth = 2.0;

    /// <summary>R_eff − R, as a multiple of W: eq (26)'s <c>R_inner + 0.3·W</c> re-expressed against
    /// the centre-line radius.</summary>
    public const double EffectiveRadiusOffsetPerWidth = -0.2;

    /// <summary>R_eff = R − 0.2·W (metres).</summary>
    public static double EffectiveRadius(double radiusMeters, double wMeters)
        => radiusMeters + EffectiveRadiusOffsetPerWidth * wMeters;

    /// <summary>The electrical length the bend is stamped as: R_eff·|θ| (metres).</summary>
    public static double ElectricalLength(double radiusMeters, double wMeters, double angleDeg)
        => EffectiveRadius(radiusMeters, wMeters) * Math.Abs(angleDeg) * Math.PI / 180.0;

    /// <summary>The centre-line arc length R·|θ| (metres) — what is DRAWN, reported beside the
    /// electrical length because the two differ by design.</summary>
    public static double CentreLineLength(double radiusMeters, double angleDeg)
        => radiusMeters * Math.Abs(angleDeg) * Math.PI / 180.0;

    /// <summary>
    /// Refuses geometry that cannot be drawn, naming the bound: a radius below half the width puts
    /// the inner edge on the far side of the centre of curvature, and a turn past a full circle
    /// overlaps itself. Everything else is computed and, where outside the published range, reported.
    /// </summary>
    public static void Validate(double wMeters, double radiusMeters, double angleDeg)
    {
        if (Refusal(wMeters, radiusMeters, angleDeg) is { } why)
            throw new ArgumentException(why.Render());
    }

    /// <summary>The refusal <see cref="Validate"/> throws, as a coded diagnostic; null when the geometry
    /// can be drawn.</summary>
    public static Diagnostic? Refusal(double wMeters, double radiusMeters, double angleDeg)
    {
        if (!(wMeters > 0.0))
            return Diagnostic.Create("mcurve.width.not-positive", DiagnosticSeverity.Error,
                "MCURVE: W={w} m must be positive.", ("w", wMeters));
        if (!(radiusMeters >= wMeters / 2.0))
            return Diagnostic.Create("mcurve.radius.below-half-width", DiagnosticSeverity.Error,
                "MCURVE: Radius={radius} m is below W/2={halfWidth} m. Radius is the CENTRE-LINE radius, " +
                "so the inner edge would cross the centre of curvature.",
                ("radius", radiusMeters), ("halfWidth", wMeters / 2.0));
        if (!(Math.Abs(angleDeg) <= 360.0))
            return Diagnostic.Create("mcurve.angle.past-full-circle", DiagnosticSeverity.Error,
                "MCURVE: Angle={angle} deg turns past a full circle, where the strip overlaps itself.",
                ("angle", angleDeg));
        return null;
    }

    /// <summary>Reports, once per instance, every way <paramref name="radiusMeters"/> sits outside what
    /// the length-only model can claim.</summary>
    public static void ReportValidity(double wMeters, double radiusMeters, MicrostripValidityReporter reporter)
    {
        double rOverW = radiusMeters / wMeters;
        if (rOverW < GentleBendRadiusOverWidth)
        {
            reporter.ReportOnce("MCURVE:tight",
                $"Radius/W={rOverW:G4} is below {GentleBendRadiusOverWidth:G2}: a bend this tight is " +
                "more than a change in length (differential phase across the width, mode conversion), " +
                "which this model does not include. EM-simulate this geometry.");
            return;
        }
        reporter.CheckRange("MCURVE effective radius (R - 0.2W)", "Radius/W", rOverW,
            RadiusOverWidthRange.Min, RadiusOverWidthRange.Max);
    }
}
