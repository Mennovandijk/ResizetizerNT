﻿using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
//using SixLabors.ImageSharp;
//using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;

namespace Resizetizer
{
	public class ResizetizeSharedImages : AsyncTask, ILogger
	{
		[Required]
		public string PlatformType { get; set; } = "android";

		[Required]
		public string IntermediateOutputPath { get; set; }

		public string InputsFile { get; set; }

		public ITaskItem[] SharedImages { get; set; }

		[Output]
		public ITaskItem[] CopiedResources { get; set; }

		public string IsMacEnabled { get;set; }

		public override bool Execute()
		{
			Log.LogMessage("ResizetizeSharedImages Executing...");

			System.Threading.Tasks.Task.Run(async () =>
			{
				try
				{
					await DoExecute();
				}
				catch (Exception ex)
				{
					Log.LogErrorFromException(ex);
				}
				finally
				{
					Log.LogMessage("ResizetizeSharedImages Completed...");
					Complete();
				}

			});

			Log.LogMessage("ResizetizeSharedImages Executing - returning...");
			return base.Execute();
		}

		System.Threading.Tasks.Task DoExecute()
		{
			Log.LogMessage("ResizetizeSharedImages DoExecute...");

			Svg.SvgDocument.SkipGdiPlusCapabilityCheck = true;

			Log.LogMessage("ResizetizeSharedImages Skipped GDI Check...");

			var images = ParseImageTaskItems(SharedImages);

			Log.LogMessage($"ResizetizeSharedImages Parsed Image Task Items... {images?.Count ?? -1}");

			var dpis = DpiPath.GetDpis(PlatformType);

			Log.LogMessage("ResizetizeSharedImages Got DPIs...");

			if (dpis == null || dpis.Length <= 0)
				return System.Threading.Tasks.Task.CompletedTask;

			var originalScaleDpi = DpiPath.GetOriginal(PlatformType);

			Log.LogMessage("ResizetizeSharedImages Got original scale dpi...");

			var resizedImages = new ConcurrentBag<ResizedImageInfo>();

			Log.LogMessage("ResizetizeSharedImages Created concurrent image info bag...");

			System.Threading.Tasks.Parallel.ForEach(images, img =>
			{
				Log.LogMessage($"ResizetizeSharedImages Resizing in parallel {img.Filename}...");
				var opStopwatch = new Stopwatch();
				opStopwatch.Start();

				var op = "Resize";

				// By default we resize, but let's make sure
				if (img.Resize)
				{
					Log.LogMessage("ResizetizeSharedImages Begin Resize...");

					var resizer = new Resizer(img, IntermediateOutputPath, this);

					foreach (var dpi in dpis)
					{
						var r = resizer.Resize(dpi, InputsFile);
						resizedImages.Add(r);
					}
				}
				else
				{
					Log.LogMessage("ResizetizeSharedImages Begin Copy...");

					op = "Copy";
					// Otherwise just copy the thing over to the 1.0 scale
					var r = Resizer.CopyFile(img, originalScaleDpi, IntermediateOutputPath, InputsFile, this, PlatformType.ToLower().Equals("android"));
					resizedImages.Add(r);
				}

				opStopwatch.Stop();

				Log.LogMessage(MessageImportance.Low, $"{op} took {opStopwatch.ElapsedMilliseconds}ms");
			});
			
			var copiedResources = new List<TaskItem>();

			foreach (var img in resizedImages)
			{
				var attr = new Dictionary<string, string>();
				string itemSpec = Path.GetFullPath(img.Filename);

				// Fix the item spec to be relative for mac
				if (bool.TryParse(IsMacEnabled, out bool isMac) && isMac)
					itemSpec = img.Filename;

				// Add DPI info to the itemspec so we can use it in the targets
				attr.Add("_ResizetizerDpiPath", img.Dpi.Path);
				attr.Add("_ResizetizerDpiScale", img.Dpi.Scale.ToString());

				copiedResources.Add(new TaskItem(itemSpec, attr));
			}

			CopiedResources = copiedResources.ToArray();

			return System.Threading.Tasks.Task.CompletedTask;
		}

		void ILogger.Log(string message)
		{
			Log?.LogMessage(message);
		}

		List<SharedImageInfo> ParseImageTaskItems(ITaskItem[] images)
		{
			var r = new List<SharedImageInfo>();

			if (images == null)
				return r;

			foreach (var image in images)
			{
				var info = new SharedImageInfo();

				info.Filename = image.GetMetadata("FullPath");

				info.BaseSize = Utils.ParseSizeString(image.GetMetadata("BaseSize"));

				if (bool.TryParse(image.GetMetadata("Resize"), out var rz))
					info.Resize= rz;

				info.TintColor = Utils.ParseColorString(image.GetMetadata("TintColor"));

				// TODO:
				// - Parse out custom DPI's

				r.Add(info);
			}

			return r;
		}
	}

	public interface ILogger
	{
		void Log(string message);
	}
}
