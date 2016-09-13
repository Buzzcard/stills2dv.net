namespace Stills2DV
{
	internal class Smoothing
	{
		public static double SmoothedStep(double start, double end, int count, double total, double smoothratio)
		{
			if (smoothratio < 0.0)
				smoothratio = 0.0;

			if (smoothratio > 0.5)
				smoothratio = 0.5;

			if (total <= 1.0)
				return end - start;

			double fCount = count;
			var virtualStep = (end - start) / ((1.0 - smoothratio) * total);

			if (smoothratio == 0.0)
				return virtualStep;

			if (virtualStep == 0.0)
				return 0.0;

			var lowLimit = smoothratio * total;
			var integerXfer = (int) lowLimit;
			if (integerXfer < 1)
				return virtualStep;

			var hiLimit = total - lowLimit;
			if (fCount < lowLimit)
				return virtualStep * fCount / lowLimit;

			if (fCount > hiLimit)
				return virtualStep * (total - fCount) / lowLimit;

			return virtualStep;
		}
	}
}