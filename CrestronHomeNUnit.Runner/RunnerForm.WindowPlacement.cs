// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Windows.Forms;

namespace CrestronHomeNUnit.Runner;

public sealed partial class RunnerForm
	{
	private string? _windowPlacementPath;
	private bool _windowPlacementLoaded;

	protected override void OnLoad (EventArgs e)
		{
		base.OnLoad (e);
		RestoreWindowPlacement ();
		}

	protected override void OnFormClosing (FormClosingEventArgs e)
		{
		base.OnFormClosing (e);
		if (!e.Cancel)
			SaveWindowPlacement ();
		}

	private void RestoreWindowPlacement ()
		{
		try
			{
			if (_windowPlacementPath == null || !File.Exists (_windowPlacementPath))
				return;
			if (new FileInfo (_windowPlacementPath).Length > 4096)
				throw new InvalidDataException ("Saved window placement is too large.");
			using var stream = File.OpenRead (_windowPlacementPath);
			var saved = (SavedWindowPlacement?)new DataContractJsonSerializer (typeof (SavedWindowPlacement)).ReadObject (stream);
			if (saved == null || saved.Width <= 0 || saved.Height <= 0 || saved.Width > 100000 || saved.Height > 100000 || Math.Abs ((long)saved.X) > 1000000 || Math.Abs ((long)saved.Y) > 1000000)
				throw new InvalidDataException ("Saved window placement is invalid.");
			Rectangle bounds = new (saved.X, saved.Y, saved.Width, saved.Height);
			Rectangle[] areas = Screen.AllScreens.Select (screen => screen.WorkingArea).ToArray ();
			Rectangle fallback = (Screen.PrimaryScreen ?? Screen.AllScreens[0]).WorkingArea;
			bounds = FitWindowBounds (bounds, MinimumSize, areas, fallback);
			// A smaller replacement display must still expose the whole window.
			MinimumSize = new Size (Math.Min (MinimumSize.Width, bounds.Width), Math.Min (MinimumSize.Height, bounds.Height));
			StartPosition = FormStartPosition.Manual;
			Bounds = bounds;
			WindowState = saved.Maximized ? FormWindowState.Maximized : FormWindowState.Normal;
			}
		catch (Exception exception) { _status.Text = "Cannot restore window placement: " + exception.Message; }
		finally { _windowPlacementLoaded = true; }
		}

	private static Rectangle FitWindowBounds (Rectangle bounds, Size minimum, Rectangle[] areas, Rectangle fallback)
		{
		Rectangle target = fallback;
		long bestArea = 0;
		foreach (Rectangle area in areas)
			{
			Rectangle intersection = Rectangle.Intersect (bounds, area);
			long overlap = (long)intersection.Width * intersection.Height;
			if (overlap <= bestArea)
				continue;
			bestArea = overlap;
			target = area;
			}
		int width = Math.Min (target.Width, Math.Max (minimum.Width, bounds.Width));
		int height = Math.Min (target.Height, Math.Max (minimum.Height, bounds.Height));
		int x = Math.Clamp (bounds.X, target.Left, target.Right - width);
		int y = Math.Clamp (bounds.Y, target.Top, target.Bottom - height);
		return new Rectangle (x, y, width, height);
		}

	private void SaveWindowPlacement ()
		{
		if (_windowPlacementPath == null || !_windowPlacementLoaded || WindowState == FormWindowState.Minimized)
			return;
		try
			{
			Rectangle bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
			if (bounds.Width <= 0 || bounds.Height <= 0)
				return;
			var saved = new SavedWindowPlacement
				{
				X = bounds.X,
				Y = bounds.Y,
				Width = bounds.Width,
				Height = bounds.Height,
				Maximized = WindowState == FormWindowState.Maximized
				};
			Directory.CreateDirectory (Path.GetDirectoryName (Path.GetFullPath (_windowPlacementPath))!);
			string temporary = _windowPlacementPath + "." + Guid.NewGuid ().ToString ("N") + ".tmp";
			try
				{
				using (var stream = File.Create (temporary))
					new DataContractJsonSerializer (typeof (SavedWindowPlacement)).WriteObject (stream, saved);
				File.Move (temporary, _windowPlacementPath, true);
				}
			finally { if (File.Exists (temporary)) File.Delete (temporary); }
			}
		catch (Exception exception) { _status.Text = "Cannot save window placement: " + exception.Message; }
		}

	[DataContract]
	private sealed class SavedWindowPlacement
		{
		[DataMember]
		public int X
			{
			get; set;
			}
		[DataMember]
		public int Y
			{
			get; set;
			}
		[DataMember]
		public int Width
			{
			get; set;
			}
		[DataMember]
		public int Height
			{
			get; set;
			}
		[DataMember]
		public bool Maximized
			{
			get; set;
			}
		}
	}