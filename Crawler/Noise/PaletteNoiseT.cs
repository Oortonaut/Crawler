using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Noise
{
    public class PaletteNoiseT : ISmoothNoise
    {
        List<ISmoothNoise> noise;

        public PaletteNoiseT(IEnumerable<ISmoothNoise> args)
        {
            noise = new List<ISmoothNoise>(args);
        }
        public PaletteNoiseT(params ISmoothNoise[] args)
            : this(args.AsEnumerable())
        {
        }

        delegate double EvalFunc(ISmoothNoise noise);
        private double Evaluate(double t, EvalFunc eval)
        {
            t *= noise.Count;
            int i = (int)Math.Min(Math.Max(t, 0), noise.Count - 1);
            int j = Math.Min(i + 1, noise.Count - 1);
            double frac = t - Math.Floor(t);
            double l = eval(noise[i]);
            double r = eval(noise[j]);
            double result = l * (1.0 - frac) + r * frac;
            return result;
        }

        public double Evaluate()
        {
            return Evaluate(0.0, noise => noise.Evaluate());
        }

        public double EvaluateT(double t)
        {
            return Evaluate(t, noise => noise.Evaluate());
        }

        public double Evaluate(double x)
        {
            return Evaluate(0.0, noise => noise.Evaluate(x));
        }

        public double EvaluateT(double x, double t)
        {
            return Evaluate(t, noise => noise.Evaluate(x));
        }

        public double Evaluate(double x, double y)
        {
            return Evaluate(0.0, noise => noise.Evaluate(x, y));
        }

        public double EvaluateT(double x, double y, double t)
        {
            return Evaluate(t, noise => noise.Evaluate(x, y));
        }

        public double Evaluate(double x, double y, double z)
        {
            return Evaluate(0.0, noise => noise.Evaluate(x, y, z));
        }

        public double EvaluateT(double x, double y, double z, double t)
        {
            return Evaluate(t, noise => noise.Evaluate(x, y, z));
        }

        public double Evaluate(double x, double y, double z, double w)
        {
            return Evaluate(0.0, noise => noise.Evaluate(x, y, z, w));
        }

    }

}
