using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using static TestInject.HelperFunctions;

namespace TestInject
{
	public class GMenu
	{
		public GMenuConfig Config;
		public int SelectedItemIndex = 0;

		public readonly object threadLock = new object();
		public bool IsShowing = true;

		private Thread threadKeyProcesser;
		private static DateTime lastTimeStampActiondone = DateTime.Now;
		public const int ACTION_DELAY_VALUE = 175;

		public GMenu(GMenuConfig config)
		{
			Config = config;
			Config.MenuItems = Config.FixItemGroupIndexes();

			threadKeyProcesser = new Thread(ProcessKeys);
			threadKeyProcesser?.Start();

			IsShowing = true;
		}

		public void DrawMenu(ref Graphics gObj)
		{
			if (gObj == null || !IsShowing)
				return;

			gObj.FillRectangle(Brushes.Black, Config.DrawLocation.X, Config.DrawLocation.Y, Config.Dimensions.Width, Config.Dimensions.Height);
			gObj.FillRectangle(Brushes.Green, Config.DrawLocation.X, Config.DrawLocation.Y, Config.Dimensions.Width, 25f);

			gObj.DrawString(Config.MenuTitle, Config.MenuTitleFont, Brushes.Blue, 
				Config.DrawLocation.X + 4, 
				Config.DrawLocation.Y + 4);

			int yOffset = Config.DrawLocation.Y + 35;
			int xOffset = Config.DrawLocation.X + 15;

			if (Config.MenuItems != null)
			{
				foreach (GMenuItemGroup group in Config.MenuItems)
				{
					foreach (GMenuItem subItem in group.GroupItems)
					{
						gObj.DrawString($"{(subItem.IsIndented ? "    " : "")}{(SelectedItemIndex == subItem.ItemIndex ? "-->" : "")} {subItem.ItemText}: {subItem.DefaultValue}", 
							Config.FeatureFont, SelectedItemIndex == subItem.ItemIndex ? Brushes.Green : Brushes.Red, 
							xOffset, yOffset);
						yOffset += 15;
					}

					yOffset += 5;
				}
			}
		}
		public void ProcessKeys()
		{
			while (true)
			{
				if (DateTime.Now.Subtract(lastTimeStampActiondone).Milliseconds <= ACTION_DELAY_VALUE)
				{
					Thread.Sleep(25);
					continue;
				}

				// Process Keys
				if (IsKeyPushedDown(Keys.Up))
				{
					lock (threadLock)
					{
						UpdateSelectedIndex(Dir.Up, ref SelectedItemIndex, Config.GetTotalItemCount() - 1);
						lastTimeStampActiondone = DateTime.Now;
					}
				} else if (IsKeyPushedDown(Keys.Down))
				{
					lock (threadLock)
					{
						UpdateSelectedIndex(Dir.Down, ref SelectedItemIndex, Config.GetTotalItemCount() - 1);
						lastTimeStampActiondone = DateTime.Now;
					}
				} else if (IsKeyPushedDown(Keys.Right) || IsKeyPushedDown(Keys.Left))
				{
					int groupingIndex = Config.MenuItems.FindIndex(group => group.GroupItems.FirstOrDefault(subItem => subItem.ItemIndex == SelectedItemIndex) != default);
					if (groupingIndex == -1)
					{
						Console.WriteLine($"[ERROR] KeyProcesser - Failed getting 'groupingIndex'");
						Thread.Sleep(1000);
						continue;
					}

					int selectedItemIndex = Config.MenuItems[groupingIndex].GroupItems.FindIndex(x => x.ItemIndex == SelectedItemIndex);
					if (selectedItemIndex == -1)
					{
						Console.WriteLine($"[ERROR] KeyProcesser - Failed getting 'selectedItemIndex'");
						Thread.Sleep(1000);
						continue;
					}

					string selectedItemText = Config.MenuItems[groupingIndex].GroupItems[selectedItemIndex].DefaultValue;

					lock (threadLock)
					{
						Config.MenuItems[groupingIndex].GroupItems[selectedItemIndex].DefaultValue = selectedItemText == "Disabled" ? "Enabled" : "Disabled";
						lastTimeStampActiondone = DateTime.Now;
					}
				} else if (IsKeyPushedDown(Keys.Insert))
				{
					lock (threadLock)
					{
						IsShowing = !IsShowing;
						Console.WriteLine($"GMenu - Menu Visibility set to: {(IsShowing ? "Visible" : "Hidden")}");
						lastTimeStampActiondone = DateTime.Now;
					}
				}
				else
					Thread.Sleep(25);
			}
		}

		public bool IsFeatureEnabled(string tag)
		{
			foreach (GMenuItemGroup group in Config.MenuItems)
			{
				foreach (GMenuItem subItem in group.GroupItems)
				{
					if (string.Equals(subItem.ItemTag, tag, StringComparison.CurrentCultureIgnoreCase))
						return subItem.DefaultValue.ToLower() != "disabled";
				}
			}

			return false;
		}
	}

	public class GMenuItemGroup
	{
		public string GroupIdentifier;
		public List<GMenuItem> GroupItems = new List<GMenuItem>();

		public GMenuItemGroup(string identifier)
		{
			GroupIdentifier = identifier;
		}
	}

	public class GMenuItem
	{
		public string ItemText;
		public string ItemTag;
		public string DefaultValue = "Disabled";
		public int ItemIndex;
		public bool IsIndented = false;

		public GMenuItem(string itemText, string itemTag, bool isIndented = false)
		{
			ItemText = itemText;
			IsIndented = isIndented;
			ItemTag = itemTag;
		}
	}

	[Serializable]
	public class GMenuConfig
	{
		public Font MenuTitleFont = new Font(FontFamily.GenericSansSerif, 10f, FontStyle.Bold);
		public string MenuTitle = "Default Title";

		public Font FeatureFont = new Font(FontFamily.GenericSansSerif, 9f, FontStyle.Regular);

		public Size Dimensions = new Size(250, 400);
		public Point DrawLocation = new Point(100, 100);

		public List<GMenuItemGroup> MenuItems = new List<GMenuItemGroup>();

		public List<GMenuItemGroup> FixItemGroupIndexes()
		{
			List<GMenuItemGroup> Fixed = new List<GMenuItemGroup>();

			int n = 0;
			foreach (GMenuItemGroup group in MenuItems)
			{
				foreach (var subItem in group.GroupItems)
				{
					subItem.ItemIndex = n;
					n++;
				}

				Fixed.Add(group);
			}

			return Fixed;
		}

		public int GetTotalItemCount()
		{
			int count = 0;
			foreach (GMenuItemGroup group in MenuItems)
			{
				foreach (var subItem in group.GroupItems)
				{
					count++;
				}
			}

			return count;
		}
	}

	public class HelperFunctions
	{
		public class PInvoke
		{
			[DllImport("user32.dll")]
			public static extern ushort GetAsyncKeyState(int vKey);
		}

		public static bool IsKeyPushedDown(Keys vKey)
		{
			return 0 != (PInvoke.GetAsyncKeyState((int)vKey) & 0x8000);
		}

		public static void UpdateSelectedIndex(Dir direction, ref int value, int numMaxItems)
		{
			switch (direction)
			{
				case Dir.Up:
					if (value - 1 < 0)
						value = numMaxItems;
					else
						value--;
					break;
				case Dir.Down:
					if (value + 1 > numMaxItems)
						value = 0;
					else
						value++;
					break;
			}
		}
	}

	public enum Dir
	{
		Up = 1,
		Down = 2,
	}
}
