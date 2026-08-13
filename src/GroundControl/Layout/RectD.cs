namespace GroundControl.Layout;

/// <summary>A double-precision rectangle (X,Y = top-left, W,H = size).</summary>
public readonly record struct RectD(double X, double Y, double W, double H)
{
    public double Right => X + W;
    public double Bottom => Y + H;
    public double CenterX => X + W / 2.0;
    public double CenterY => Y + H / 2.0;
}
