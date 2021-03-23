namespace VideoEncodingAutomation.Cropping
{
	/// <summary>
	/// A rectangle representing the non-black boundaries of a video.
	/// </summary>
	public class CropRectangle
	{
		public int Left;
		public int Right;
		public int Top;
		public int Bottom;
		public int SourceWidth;
		public int SourceHeight;
		public int CroppedWidth { get { return (Right + 1) - Left; } }
		public int CroppedHeight { get { return (Bottom + 1) - Top; } }
		/// <summary>
		/// Returns a CropRectangle that will immediately take all the values of the first rectangle it is merged with.
		/// </summary>
		/// <returns></returns>
		public static CropRectangle InitialValue()
		{
			return new CropRectangle(int.MaxValue, 0, int.MaxValue, 0);
		}
		public CropRectangle(int Left, int Right, int Top, int Bottom)
		{
			this.Left = Left;
			this.Right = Right;
			this.Top = Top;
			this.Bottom = Bottom;
		}
		/// <summary>
		/// Pushes out boundaries by one pixel if necessary to obtain an even-numbered width and height.
		/// </summary>
		public void InflateToModulus2()
		{
			if (CroppedWidth % 2 != 0)
			{
				if (Left > 0 && Left % 2 == 1)
					Left--; // Prefer to shift left to make it even.
				else if (Right < SourceWidth - 1)
					Right++; // Fallback to shift right.
				else if (Left > 0)
					Left--; // As a last resort, shift left, making it an odd index.
			}
			if (CroppedHeight % 2 != 0)
			{
				if (Top > 0 && Top % 2 == 1)
					Top--; // Prefer to raise top to make it even.
				else if (Bottom < SourceHeight - 1)
					Bottom++; // Fallback to lowering the bottom.
				else if (Top > 0)
					Top--; // As a last resort, raise top, making it an odd index.
			}
		}
		/// <summary>
		/// Encodes a string with the format "([SourceWidth]x[SourceHeight]) top:bottom:left:right" where each value is the row or column index.
		/// </summary>
		/// <returns></returns>
		public override string ToString()
		{
			return "(" + SourceWidth + "x" + SourceHeight + ") " + Top + ":" + Bottom + ":" + Left + ":" + Right;
		}
		/// <summary>
		/// CEncodes a string in handbrakecli format "top:bottom:left:right" where each value is the number of pixels to remove.
		/// </summary>
		/// <returns></returns>
		public string ToHandbrakeString()
		{
			return Top + ":" + ((SourceHeight - 1) - Bottom) + ":" + Left + ":" + ((SourceWidth - 1) - Right);
		}

		/// <summary>
		/// Expands this rectangle if necessary such that this rectangle contains the other rectangle.
		/// </summary>
		/// <param name="other">other rectangle to merge with</param>
		public void MergeWith(CropRectangle other)
		{
			if (Left > other.Left)
				Left = other.Left;
			if (Right < other.Right)
				Right = other.Right;
			if (Top > other.Top)
				Top = other.Top;
			if (Bottom < other.Bottom)
				Bottom = other.Bottom;
		}

		/// <summary>
		/// Returns true if the rectangle has valid values. The "InitialValue" is not valid.
		/// </summary>
		/// <returns></returns>
		public bool IsValid()
		{
			return Top >= 0
				&& Right >= 0
				&& Bottom >= 0
				&& Left >= 0
				&& Left <= Right
				&& Top <= Bottom;
		}
	}
}
