using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Noise {
    public class RemapNoise : ISmoothNoise {
        ISmoothNoise noise;
        RemapFunc remap;
        public delegate double RemapFunc(double x);
        public RemapNoise(ISmoothNoise noise, RemapFunc remap) {
            this.noise = noise;
            this.remap = remap;
        }
        public double Evaluate() {
            return remap(noise.Evaluate());
        }

        public double EvaluateT(double t) {
            return remap(noise.EvaluateT(t));
        }

        public double Evaluate(double x) {
            return remap(noise.Evaluate(x));
        }

        public double EvaluateT(double x, double t) {
            return remap(noise.EvaluateT(x, t));
        }

        public double Evaluate(double x, double y) {
            return remap(noise.Evaluate(x, y));
        }

        public double EvaluateT(double x, double y, double t) {
            return remap(noise.EvaluateT(x, y, t));
        }

        public double Evaluate(double x, double y, double z) {
            return remap(noise.Evaluate(x, y, z));
        }

        public double EvaluateT(double x, double y, double z, double t) {
            return remap(noise.EvaluateT(x, y, z, t));
        }

        public double Evaluate(double x, double y, double z, double w) {
            return remap(noise.Evaluate(x, y, z, w));
        }
    }
}
