using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Noise {
    public class CombineNoise : ISmoothNoise {
        List<ISmoothNoise> noise;

        public CombineNoise(IEnumerable<ISmoothNoise> args) {
            noise = new List<ISmoothNoise>(args);
        }
        public CombineNoise(params ISmoothNoise[] args)
                : this(args.AsEnumerable()) {
        }

        public double Evaluate() {
            return noise.Sum(i => i.Evaluate());
        }

        public double EvaluateT(double t) {
            return noise.Sum(i => i.EvaluateT(t));
        }

        public double Evaluate(double x) {
            return noise.Sum(i => i.Evaluate(x));
        }

        public double EvaluateT(double x, double t) {
            return noise.Sum(i => i.EvaluateT(x, t));
        }

        public double Evaluate(double x, double y) {
            return noise.Sum(i => i.Evaluate(x, y));
        }

        public double EvaluateT(double x, double y, double t) {
            return noise.Sum(i => i.EvaluateT(x, y, t));
        }

        public double Evaluate(double x, double y, double z) {
            return noise.Sum(i => i.Evaluate(x, y, z));
        }

        public double EvaluateT(double x, double y, double z, double t) {
            return noise.Sum(i => i.EvaluateT(x, y, z, t));
        }

        public double Evaluate(double x, double y, double z, double w) {
            return noise.Sum(i => i.Evaluate(x, y, z, w));
        }
    }
}
