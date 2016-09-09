using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Stills2DV
{
	class Smoothing
	{
		public static double SmoothedStep(double start, double end, int count, double total, double smoothratio)
		{
			double virtual_step, f_count, low_limit, hi_limit;
			int integer_xfer;

			if (smoothratio < 0.0)
				smoothratio = 0.0;

			if (smoothratio > 0.5)
				smoothratio = 0.5;

			if (total <= 1.0)
				return (end - start);

			f_count = count;
			virtual_step = (end - start) / ((1.0 - smoothratio) * total);

			if (smoothratio == 0.0)
				return virtual_step;

			if (virtual_step == 0.0)
				return 0.0;

			low_limit = smoothratio * total;
			integer_xfer = (int)low_limit;
			if (integer_xfer < 1)
				return virtual_step;

			hi_limit = total - low_limit;
			if (f_count < low_limit)
				return (virtual_step * (f_count) / low_limit);

			if (f_count > hi_limit)
				return (virtual_step * (total - f_count) / low_limit);

			return virtual_step;
		}
	}
}
