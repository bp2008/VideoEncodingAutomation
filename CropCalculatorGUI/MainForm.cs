using BPUtil;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using VideoEncodingAutomation.Cropping;

namespace CropCalculatorGUI
{
	public partial class MainForm : Form
	{
		List<Thread> runningThreads = new List<Thread>();
		public MainForm()
		{
			InitializeComponent();
		}

		private void MainForm_DragEnter(object sender, DragEventArgs e)
		{
			if (e.Data.GetDataPresent(DataFormats.FileDrop))
				e.Effect = DragDropEffects.Copy;
		}

		private void MainForm_DragDrop(object sender, DragEventArgs e)
		{
			string[] filePaths = (string[])e.Data.GetData(DataFormats.FileDrop);
			if (filePaths != null && filePaths.Length > 0)
			{
				Thread thr = null;
				thr = new Thread(() =>
				{
					try
					{
						CropCalculator cc = new CropCalculator(filePaths[0], NewFrameCallback, true);
						CropRectangle cropRect = cc.CalculateCrop();
						SetTextOutput(cropRect.ToString() + " -- Handbrake: --crop " + cropRect.ToHandbrakeString());
					}
					catch (ThreadAbortException)
					{
						SetTextOutput("Aborted!");
					}
					catch (Exception ex)
					{
						SetTextOutput(ex.ToString());
					}
					finally
					{
						lock (runningThreads)
							runningThreads.Remove(thr);
					}
				});
				thr.IsBackground = false;
				thr.Name = "PictureBoxDragDrop";
				lock (runningThreads)
					runningThreads.Add(thr);
				thr.Start();
				lblDrag.Visible = false;
			}
		}

		private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
		{
			lock (runningThreads)
				foreach (Thread thr in runningThreads)
				{
					thr.Abort();
				}
		}
		private void NewFrameCallback(double progress, ImageFile imageFile)
		{
			SetTextOutput((int)Math.Round(progress * 100) + "%");
			if (imageFile != null)
				RenderImage(imageFile);
		}

		private void RenderImage(ImageFile imageFile)
		{
			if (pictureBox.InvokeRequired)
				pictureBox.Invoke((Action<ImageFile>)RenderImage, imageFile);
			else
			{
				Image oldImage = pictureBox.Image;
				pictureBox.Image = imageFile.img;
				imageFile.img = null; // Detach the Image from the ImageFile so the Image won't be disposed when the ImageFile is disposed.
				imageFile.Dispose();
				oldImage?.Dispose();
			}
		}

		private void SetTextOutput(string text)
		{
			if (txtOutput.InvokeRequired)
			{
				try
				{
					txtOutput.Invoke((Action<string>)SetTextOutput, text);
				}
				catch { }
			}
			else
			{
				txtOutput.Text = text;
			}
		}
	}
}
