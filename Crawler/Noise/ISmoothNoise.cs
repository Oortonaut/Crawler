using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Noise
{
    public interface ISmoothNoise
    {
        double Evaluate();
        double EvaluateT(double t);
        double Evaluate(double x);
        double EvaluateT(double x, double t);
        double Evaluate(double x, double y);
        double EvaluateT(double x, double y, double t);
        double Evaluate(double x, double y, double z);
        double EvaluateT(double x, double y, double z, double t);
        double Evaluate(double x, double y, double z, double w);
    }
}
