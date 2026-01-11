using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Noise
{
    public class OctaveNoise : ISmoothNoise
    {
        ISmoothNoise noise;
        int octaves = 4;
        double weightScale = 0.5f;
        double weightNorm = 0.0f;
        void calcWeightNorm()
        {
            double weight = 1.0;
            double weightSum = 0.0;
            for (int o = 0; o < octaves; ++o)
            {
                weightSum += weight * weight;
                weight *= weightScale;
            }
            weightNorm = 1.0 / Math.Sqrt(weightSum);
        }
        /* The initial size is the range of Evaluate().
         * So with an initialSize of 100, Evaluate will
         * check the noise function at 1.0.
         * This is also a good way to set the base wavelength
         * in a case where the noise parameters are normalized
         * before calling evaluate. So by setting 0.25 you 
         * won't have any lower frequency information than 
         * 4 cycles.
         */
        public double InitialSize { get; set; } = 1.0;
        public double SizeScale { get; set; } = 0.5f;
        public double WeightScale {
            get { return weightScale; }
            set { weightScale = value; calcWeightNorm(); }
        }
        public int Octaves
        {
            get { return octaves; }
            set { octaves = Math.Max(Math.Min(value, 16), 1); calcWeightNorm();  }
        }

        public OctaveNoise(ISmoothNoise noise, int octaves, double weightScale)
        {
            this.noise = noise;
            this.octaves = octaves;
            this.weightScale = 0.5;
            calcWeightNorm();
        }

        delegate double EvalFunc(double resize);
        private double Evaluate(EvalFunc eval)
        {
            double resize = 1.0 / InitialSize;
            double noiseSum = 0.0;
            double weight = 1.0;
            for (int o = 0; o < octaves; ++o)
            {
                noiseSum += weight * eval(resize);
                resize /= SizeScale;
                weight *= weightScale;
            }
            double result = noiseSum * weightNorm;
            return result;
        }

        public double Evaluate()
        {
            return Evaluate(scale => noise.Evaluate());
        }

        public double EvaluateT(double t)
        {
            return Evaluate(scale => noise.EvaluateT(t));
        }

        public double Evaluate(double x)
        {
            return Evaluate(scale => noise.Evaluate(x * scale));
        }

        public double EvaluateT(double x, double t)
        {
            return Evaluate(scale => noise.EvaluateT(x * scale, t));
        }

        public double Evaluate(double x, double y)
        {
            return Evaluate(scale => noise.Evaluate(x * scale, y * scale));
        }

        public double EvaluateT(double x, double y, double t)
        {
            return Evaluate(scale => noise.EvaluateT(x * scale, y * scale, t));
        }

        public double Evaluate(double x, double y, double z)
        {
            return Evaluate(scale => noise.Evaluate(x * scale, y * scale, z * scale));
        }

        public double EvaluateT(double x, double y, double z, double t)
        {
            return Evaluate(scale => noise.EvaluateT(x * scale, y * scale, z * scale, t));
        }

        public double Evaluate(double x, double y, double z, double w)
        {
            return Evaluate(scale => noise.Evaluate(x * scale, y * scale, z * scale, w * scale));
        }

    }
}
