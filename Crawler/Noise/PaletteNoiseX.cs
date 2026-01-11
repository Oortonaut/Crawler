using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Noise
{
    public class PaletteNoiseX : ISmoothNoise
    {
        List<ISmoothNoise> noise;

        public PaletteNoiseX(IEnumerable<ISmoothNoise> args)
        {
            noise = new List<ISmoothNoise>(args);
        }
        public PaletteNoiseX(params ISmoothNoise[] args)
            : this(args.AsEnumerable())
        {
        }

        delegate double EvalFunc(ISmoothNoise noise);
        private double Evaluate(double x, EvalFunc eval)
        {
            x *= noise.Count;
            int i = (int)Math.Min(Math.Max(x, 0), noise.Count - 1);
            int j = Math.Min(i + 1, noise.Count - 1);
            double frac = x - Math.Floor(x);
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
            return Evaluate(0.0, noise => noise.EvaluateT(t));
        }

        public double Evaluate(double x)
        {
            return Evaluate(x, noise => noise.Evaluate());
        }

        public double EvaluateT(double x, double t)
        {
            return Evaluate(x, noise => noise.EvaluateT(t));
        }

        public double Evaluate(double x, double y)
        {
            return Evaluate(y, noise => noise.Evaluate(x));
        }

        public double EvaluateT(double x, double y, double t)
        {
            return Evaluate(y, noise => noise.EvaluateT(x, t));
        }

        public double Evaluate(double x, double y, double z)
        {
            return Evaluate(z, noise => noise.Evaluate(x, y));
        }

        public double EvaluateT(double x, double y, double z, double t)
        {
            return Evaluate(z, noise => noise.EvaluateT(x, y, t));
        }

        public double Evaluate(double x, double y, double z, double w)
        {
            return Evaluate(w, noise => noise.Evaluate(x, y, z));
        }

    }
}
