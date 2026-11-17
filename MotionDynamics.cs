using System.Windows.Media.Animation;

namespace DesktopIsland;

internal sealed record MotionAxis(double Duration, double Delay, IEasingFunction Easing)
{
    public double End => Duration + Delay;
}

internal sealed record ShellMotionPlan(MotionAxis Width, MotionAxis Height)
{
    public double Duration => Math.Max(Width.End, Height.End);
}

internal static class MotionDynamics
{
    internal static ShellMotionPlan Plan(string style, bool opening, double speed = 100, double spring = 60)
    {
        double tempo = 100 / Math.Clamp(speed, 70, 140);
        MotionAxis Spring(double ms, double delay, double damping, double frequency = 11)
            => new(ms * tempo, delay * tempo, new PhysicalResponse(Math.Clamp(damping + (.6 - Math.Clamp(spring, 0, 100) / 100) * .35, .3, 1), frequency));
        MotionAxis Smooth(double ms, double delay = 0) => new(ms * tempo, delay * tempo, new CubicEase { EasingMode = EasingMode.EaseInOut });
        return style switch
        {
            "Плавно" => new(Smooth(380), Smooth(380)),
            "Пружина" => new(Spring(700, 0, opening ? .54 : .7, 12), Spring(700, 0, opening ? .54 : .7, 12)),
            "Желе" => new(Spring(820, 65, opening ? .44 : .72, 13), Spring(870, 0, opening ? .4 : .7, 14)),
            "Тянучка" => opening ? new(Spring(490, 0, .74, 12), Spring(610, 120, .66)) : new(Spring(440, 100, .86, 12), Spring(400, 0, .9, 12)),
            "Мяч" when opening => new(Spring(540, 30, .75), new(690 * tempo, 0, new BounceEase { Bounces = 2, Bounciness = 3.2, EasingMode = EasingMode.EaseOut })),
            "Мяч" => new(Smooth(320), Smooth(350, 30)),
            "Резинка" => new(Spring(opening ? 860 : 560, 40, opening ? .37 : .72, 16), Spring(opening ? 860 : 560, 0, opening ? .37 : .72, 16)),
            "Щелчок" => new(Spring(230, 0, .9, 15), Spring(230, 0, .9, 15)),
            "Пузырь" => new(Spring(650, 0, opening ? .4 : .72, 17), Spring(650, 0, opening ? .4 : .72, 17)),
            "Волна" => opening ? new(Spring(660, 85, .78), Spring(610, 0, .7)) : new(Spring(470, 0, .92, 12), Spring(520, 55, .84, 12)),
            _ => opening ? new(Spring(620, 45, .76), Spring(600, 0, .7)) : new(Spring(440, 0, .92, 12), Spring(520, 45, .84, 12))
        };
    }

    internal static IEasingFunction SafeGeometry(IEasingFunction source, double from, double target, double minimum, double maximum) => new GeometryResponse(source, from, target, minimum, maximum);
    private sealed class GeometryResponse(IEasingFunction source, double from, double target, double minimum, double maximum) : IEasingFunction
    {
        public double Ease(double time)
        {
            double delta = target - from;
            if (Math.Abs(delta) < .001) return time;
            return (Math.Clamp(from + delta * source.Ease(time), minimum, Math.Max(minimum, maximum)) - from) / delta;
        }
    }

    // Integrate a mass/spring response rather than chaining unrelated easing curves.
    internal sealed class PhysicalResponse : IEasingFunction
    {
        private readonly double[] positions = new double[601];
        public PhysicalResponse(double damping = .86, double frequency = 11)
        {
            double position = 0, velocity = 0, step = 1d / 600;
            double Acceleration(double x, double v) => frequency * frequency * (1 - x) - 2 * damping * frequency * v;
            for (int i = 1; i < positions.Length; i++)
            {
                double a = velocity, b = Acceleration(position, velocity);
                double c = velocity + b * step / 2, d = Acceleration(position + a * step / 2, c);
                double e = velocity + d * step / 2, f = Acceleration(position + c * step / 2, e);
                double g = velocity + f * step, h = Acceleration(position + e * step, g);
                position += step * (a + 2 * c + 2 * e + g) / 6;
                velocity += step * (b + 2 * d + 2 * f + h) / 6;
                positions[i] = position;
            }
            positions[^1] = 1;
        }
        public double Ease(double normalizedTime)
        {
            double index = Math.Clamp(normalizedTime, 0, 1) * 600;
            int lower = Math.Min(599, (int)index);
            return positions[lower] + (positions[lower + 1] - positions[lower]) * (index - lower);
        }
    }
}
