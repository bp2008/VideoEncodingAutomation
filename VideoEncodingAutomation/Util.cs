using System;

namespace VideoEncodingAutomation
{
	public static class Util
	{
		public static double ToDouble(string str, double defaultValue = 0)
		{
			double num;
			if (double.TryParse(str, out num))
				return num;
			return defaultValue;
		}

		public static int ToInt(string str, int defaultValue = 0)
		{
			int num;
			if (int.TryParse(str, out num))
				return num;
			return defaultValue;
		}

		public static int Clamp(int i, int min, int max)
		{
			if (i < min)
				return min;
			else if (i > max)
				return max;
			else
				return i;
		}
	}
}