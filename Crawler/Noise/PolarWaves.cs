using Crawler;

namespace Noise {
    public class PolarWaves : ISmoothNoise {
        bool useAngle;
        double originX;
        double originY;
        double radius;
        double angle;
        double waveLength;

        public PolarWaves(int seed, double waveLength, bool useAngle) {
            var random = new XorShift(seed);
            this.useAngle = useAngle;
            this.waveLength = waveLength;
            var innerRadius = 1.5;
            var outerRadius = 2.25;
            // correct the density for the different diameters at the radius
            radius = random.NextDouble(Math.Sqrt(innerRadius), Math.Sqrt(outerRadius));
            radius = radius * radius;
            angle = random.NextDouble(-Math.PI, Math.PI);
            originX = 0.5 + radius * Math.Cos(angle);
            originY = 0.5 + radius * Math.Sin(angle);
        }

        public double Evaluate() {
            return Evaluate(0.0, 0.0);
        }

        public double EvaluateT(double t) {
            return EvaluateT(0.0, 0.0, t);
        }

        public double Evaluate(double x) {
            return Evaluate(x, 0.0);
        }

        public double EvaluateT(double x, double t) {
            return EvaluateT(x, 0.0, t);
        }

        public double Evaluate(double x, double y) {
            return EvaluateT(x, y, 0.0);
        }

        public bool ToPolar(double x, double y, out double r, out double a) {
            var x2c = 0.5 - originX; // point to box center
            var y2c = 0.5 - originY;
            var x2o = x - originX; // 
            var y2o = y - originY;

            r = Math.Sqrt(x2o * x2o + y2o * y2o);
            var rc = Math.Sqrt(x2c * x2c + y2c * y2c);
            var cross = (x2c * y2o - x2o * y2c);
            cross /= r * rc;
            a = Math.Asin(cross);

            r = r / waveLength;
            a = a * radius / waveLength + 0.5;
            return r > 0;
        }

        public double EvaluateT(double x, double y, double t) {
            double p = t * Math.PI * 2;
            double r, a;
            if (ToPolar(x, y, out r, out a)) {
                if (useAngle) {
                    double falloff = (radius - r) / radius; // 1 at center, 0 at radius, -1 at 2*radius
                    double heightScale = falloff * 0.5 + 1;
                    heightScale *= heightScale;
                    heightScale *= 0.6;
                    return Math.Cos((a * Math.PI * 2 + p) / waveLength) * heightScale;
                } else {
                    return Math.Cos((r * Math.PI * 2 + p) / waveLength );
                }
            } else {
                return 0;
            }
        }

        public double Evaluate(double x, double y, double z) {
            return Evaluate(x, y);
        }

        public double EvaluateT(double x, double y, double z, double t) {
            return EvaluateT(x, y, t);
        }

        public double Evaluate(double x, double y, double z, double w) {
            return Evaluate(x, y);
        }
    }
}
