
namespace CircuitRF.Design.Layout.PCells;

/// <summary>
/// MCurve artwork: a radiused microstrip bend of width <c>W</c>, CENTRE-LINE radius <c>Radius</c> and
/// turn <c>Angle</c> (degrees, counter-clockwise positive from arm 1's +X, MBend's convention).
/// R-pc-3: pin 1 at the origin, arriving along +X; the centre of curvature is at
/// <c>(0, s·Radius)</c> with <c>s = sign(Angle)</c>.
///
/// <para><b>The outline is ONE <see cref="CurveShape"/> whose two long edges are TRUE ARCS</b>, not
/// chords: the outer edge at <c>Radius + W/2</c> and the inner at <c>Radius − W/2</c>, joined by the two
/// radial end caps. Every consumer that needs straight edges — the planar EM extractor, the Gerber and
/// GDSII writers, DRC — already flattens arcs to the technology's own tolerance
/// (<see cref="LayoutFlattener"/>), so the artwork stores the exact geometry once and each consumer
/// decides how finely to see it. A chord polygon here would bake one tolerance into every one of them.</para>
///
/// <para>At <c>Radius = W/2</c> the inner edge is a single point — the centre of curvature — and the
/// outline is a sector; below that the inner edge would cross the centre, which the electrical model
/// refuses and this cell reports and clamps rather than drawing a self-intersecting ring.</para>
/// </summary>
public static class MCurvePCell
{
    public const string GeneratorId = "MCURVE";

    public static PCellResult Generate(
        IReadOnlyDictionary<string, PCellValue> parameters,
        Technology? technology,
        PCellLayerSelection layerSelection)
    {
        double wMeters = parameters.Real("W", 0.0);
        double angleDeg = parameters.Real("Angle", 90.0);
        double radiusMeters = parameters.Real("Radius", 0.0);

        int dbuPerMicron = LayoutUnits.DefaultDbuPerMicron;
        long w = PCellUnits.MetresToDbu(wMeters, dbuPerMicron);
        double r = PCellUnits.MetresToDbu(radiusMeters, dbuPerMicron);
        var signalLayer = SubstrateResolver.ResolveSignalLayerKey(technology, layerSelection, out _);
        List<string>? diagnostics = null;

        if (r < w / 2.0)
        {
            (diagnostics ??= []).Add(
                $"MCurve: Radius {radiusMeters * 1e6:G6} um is below W/2 ({wMeters * 0.5e6:G6} um). Radius is " +
                "the CENTRE-LINE radius, so the inner edge would cross the centre of curvature; drawn at " +
                "W/2 instead, and the simulation refuses this geometry.");
            r = w / 2.0;
        }
        if (Math.Abs(angleDeg) > 360.0)
        {
            (diagnostics ??= []).Add(
                $"MCurve: Angle {angleDeg:G6} deg turns past a full circle, where the strip overlaps itself; " +
                "drawn at 360 deg, and the simulation refuses this geometry.");
            angleDeg = Math.Sign(angleDeg) * 360.0;
        }

        double s = angleDeg < 0 ? -1.0 : 1.0;
        double theta = Math.Abs(angleDeg) * Math.PI / 180.0;
        double cy = s * r;                                   // centre of curvature (0, cy)
        double ro = r + w / 2.0, ri = r - w / 2.0;

        // A point at radius rho, swept theta' from pin 1's radial: P = C + rho*(sin, -s*cos).
        (long X, long Y) At(double rho, double t) =>
            ((long)Math.Round(rho * Math.Sin(t), MidpointRounding.AwayFromZero),
             (long)Math.Round(cy - s * rho * Math.Cos(t), MidpointRounding.AwayFromZero));

        var a0 = At(ro, 0); var a1 = At(ro, theta);
        var b1 = At(ri, theta); var b0 = At(ri, 0);
        double bulge = s * Math.Tan(theta / 4.0);            // tan(sweep/4), +ve = counter-clockwise

        var xy = new List<long>(); var edges = new List<LayoutEdge>();
        void Vertex((long X, long Y) p, LayoutEdge e) { xy.Add(p.X); xy.Add(p.Y); edges.Add(e); }
        var line = new Func<LayoutEdge>(() => new LayoutEdge { Kind = EdgeKind.Line });
        LayoutEdge Arc(double b) => new() { Kind = EdgeKind.Arc, Bulge = b };

        if (theta <= 1e-12)
        {
            (diagnostics ??= []).Add("MCurve: Angle 0 is a zero-length curve; nothing is drawn.");
            return new PCellResult([], [new PCellPin("1", 0, 0, signalLayer, w, 180.0),
                                        new PCellPin("2", 0, 0, signalLayer, w, 0.0)], diagnostics);
        }

        Vertex(a0, Arc(bulge));                              // outer edge, pin 1 -> pin 2
        Vertex(a1, line());                                  // pin 2's end cap
        if (ri >= 1.0)
        {
            Vertex(b1, Arc(-bulge));                         // inner edge, back to pin 1
            Vertex(b0, line());                              // pin 1's end cap
        }
        else
        {
            Vertex(At(0, 0), line());                        // inner edge collapsed onto the centre
        }

        var outline = new CurveShape { Layer = signalLayer, Xy = [.. xy], Edges = edges };

        var p2 = At(r, theta);
        var pins = new[]
        {
            new PCellPin("1", 0, 0, signalLayer, w, 180.0),
            new PCellPin("2", p2.X, p2.Y, signalLayer, w, angleDeg),
        };

        // pcell-parameter-handles.md — three grips, each on the quantity it changes:
        //   W      — the outer edge at mid-turn, anchored on the centre line there, along the radius.
        //   Radius — the centre line at mid-turn, anchored on pin 1, which the radius does not move.
        //   Angle  — pin 2, swung about the centre of curvature (its real path).
        var mid = At(r, theta / 2.0); var midOuter = At(ro, theta / 2.0);
        double radialDeg = Math.Atan2(midOuter.Y - mid.Y, midOuter.X - mid.X) * 180.0 / Math.PI;
        double chordDeg = Math.Atan2(mid.Y, mid.X) * 180.0 / Math.PI;
        long cyDbu = (long)Math.Round(cy, MidpointRounding.AwayFromZero);
        var handles = new[]
        {
            new PCellHandle("W", mid.X, mid.Y, midOuter.X, midOuter.Y, AxisDeg: radialDeg,
                Quantity: PCellHandleQuantity.Length),
            new PCellHandle("Radius", 0, 0, mid.X, mid.Y, AxisDeg: chordDeg,
                Quantity: PCellHandleQuantity.Length),
            new PCellHandle("Angle", 0, cyDbu, p2.X, p2.Y, AxisDeg: -s * 90.0,
                Kind: PCellHandleKind.Angular, Quantity: PCellHandleQuantity.Angle),
        };

        return new PCellResult([outline], pins, diagnostics, Handles: handles);
    }
}
