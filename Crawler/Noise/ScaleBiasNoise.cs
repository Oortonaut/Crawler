using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Noise {
    public class ScaleBiasNoise: ISmoothNoise {
        ISmoothNoise noise;
        double scale;
        double bias;
        public ScaleBiasNoise(ISmoothNoise noise, double scale, double bias) {
            this.noise = noise;
            this.scale = scale;
            this.bias = bias;
        }
        public double Evaluate() {
            return scale * noise.Evaluate() + bias;
        }

        public double EvaluateT(double t) {
            return scale * noise.EvaluateT(t) + bias;
        }

        public double Evaluate(double x) {
            return scale * noise.Evaluate(x) + bias;
        }

        public double EvaluateT(double x, double t) {
            return scale * noise.EvaluateT(x, t) + bias;
        }

        public double Evaluate(double x, double y) {
            return scale * noise.Evaluate(x, y) + bias;
        }

        public double EvaluateT(double x, double y, double t) {
            return scale * noise.EvaluateT(x, y, t) + bias;
        }

        public double Evaluate(double x, double y, double z) {
            return scale * noise.Evaluate(x, y, z) + bias;
        }

        public double EvaluateT(double x, double y, double z, double t) {
            return scale * noise.EvaluateT(x, y, z, t) + bias;
        }

        public double Evaluate(double x, double y, double z, double w) {
            return scale * noise.Evaluate(x, y, z, w) + bias;
        }
    }
}
