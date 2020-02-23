using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TestInject
{
	public class GDIMenuEx
	{
#region PInvoke
		[DllImport("user32.dll")]
		static extern ushort GetAsyncKeyState(int vKey);
		public static bool IsKeyPushedDown(Keys vKey)
		{
			return 0 != (GetAsyncKeyState((int)vKey) & 0x8000);
		}
#endregion

#region Threads
		private Thread KeyPollerThread;
#endregion

#region Fonts,Brushes and Pents
		public Font ItemTextFont = new Font(FontFamily.GenericSansSerif, 10f, FontStyle.Regular);
#endregion

		public Point Location = new Point(100, 100);
		public Size Size = new Size(250, 400);

		public List<MenuItem> MenuItems = new List<MenuItem>();
		public int CurrentPointerIndex = 0;

		public const int ACTION_DELAY_THRESHOLD = 150;

		public bool ShouldDraw = false;
		
		public GDIMenuEx(int posX, int posY, int dimWidth, int dimHeight, List<MenuItem> items)
		{
			if (dimWidth <= 0 || dimHeight <= 0)
			{
				Console.WriteLine($"[{DateTime.Now.ToLongTimeString()}] GDIMenuEx was initiated" +
				                  $"with a Width/Height less than or equal to 0");

				return; // Throw exception instead?
			}

			Location = new Point(posX, posY);
			Size = new Size(dimWidth, dimHeight);

			// DEBUG STUFF
			if (items == null || items.Count < 1)
			{
				var debugItems = new List<MenuItem>();

				MenuItem t_0 = new MenuItem("Feature #1", "feat1", ItemValueType.BasicStringToggle);
				SubMenuItem s_0_0 = new SubMenuItem("Sub Item #1", "sub_0_0" ,ref t_0);
				SubMenuItem s_0_1 = new SubMenuItem("Sub Item #2", "sub_0_1", ref t_0);

				t_0.SubItems.Add(s_0_0);
				t_0.SubItems.Add(s_0_1);

				MenuItem t_1 = new MenuItem("Feature #2", "feat2", ItemValueType.BasicStringToggle);
				MenuItem t_2 = new MenuItem("Feature #3", "feat3", ItemValueType.BasicStringToggle);

				MenuItem t_3 = new MenuItem("Feature #4", "feat4", ItemValueType.BasicStringToggle);
				SubMenuItem s_3_0 = new SubMenuItem("Sub Item #1", "sub_3_0", ref t_3);
				SubMenuItem s_3_1 = new SubMenuItem("Sub Item #2", "sub_3_1", ref t_3);

				t_3.SubItems.Add(s_3_0);
				t_3.SubItems.Add(s_3_1);


				debugItems.Add(t_0);
				debugItems.Add(t_1);
				debugItems.Add(t_2);
				debugItems.Add(t_3);
				MenuItems = debugItems;
			}
			else
				MenuItems = items;

			int nSubItemsCount = 0;
			MenuItems.ForEach(x => nSubItemsCount += x.SubItems?.Count ?? 0);

			int currIdx = 0;
			foreach (MenuItem itm in MenuItems)
			{
				itm.ItemIndex = currIdx;

				int n = 0;
				foreach (SubMenuItem subItm in itm.SubItems)
				{
					subItm.ItemIndex = itm.ItemIndex + 1 + n;
					n++;
				}

				currIdx = itm.SubItems.Count > 0 
					? itm.SubItems[itm.SubItems.Count - 1].ItemIndex + 1 
					: currIdx + 1;
			}

			KeyPollerThread = new Thread(KeyPoller);
			KeyPollerThread?.Start();
		}

		public void RenderMenu(ref Graphics obj)
		{
			if (!ShouldDraw)
				return;

			if (obj == null)
			{
				Console.WriteLine($"Passed reference to graphics object was null");
				return;
			}

			int yOffset = Location.Y + 35;
			int xOffset = Location.X + 15;

			obj.FillRectangle(Brushes.Black, Location.X, Location.Y, Size.Width, Size.Height);
			obj.FillRectangle(Brushes.Green, Location.X, Location.Y, Size.Width, 25);

			obj.DrawString($"My GDI Menu", new Font(FontFamily.GenericSansSerif, ItemTextFont.Size, FontStyle.Bold), Brushes.Red, 
				Location.X + ((int)(Size.Width / 2.7f) - ItemTextFont.Size), 
				Location.Y + 4);

			foreach (MenuItem itm in MenuItems)
			{
				bool pointsToCurrent = IsPointerPointingAtItem(itm);
				obj.DrawString($"{(pointsToCurrent ? ">>" : "")} {itm.ItemText}: {itm.CurrentValue}", ItemTextFont, Brushes.Red, xOffset, yOffset);

				yOffset += 15;
				if (yOffset > Size.Height - 35)
					Size.Height += 35;

				try
				{
					foreach (SubMenuItem subItm in itm.SubItems)
					{
						obj.DrawString($"  {(IsPointerPointingAtSubItem(subItm) ? ">>" : "")} {subItm.ItemText}: {itm.CurrentValue}", ItemTextFont, Brushes.Red, xOffset, yOffset);

						yOffset += 15;
						if (yOffset > Size.Height - 35)
							Size.Height += 35;
					}
				}
				finally
				{
					yOffset += 3;
					if (yOffset > Size.Height - 3)
						Size.Height += 3;
				}
				
			}
		}

		public void KeyPoller()
		{
			DateTime lastActionTimeStamp = DateTime.Now;

			while (true)
			{
				if (ShouldDraw)
				{
					int nSubItemsCount = 0;
					MenuItems.ForEach(x => nSubItemsCount += x.SubItems?.Count ?? 0);

					if (IsKeyPushedDown(Keys.Up))
					{
						if (DateTime.Now.Subtract(lastActionTimeStamp).Milliseconds <= ACTION_DELAY_THRESHOLD)
						{
							Thread.Sleep(25);
							continue;
						}
						
						if (CurrentPointerIndex - 1 < 0)
							CurrentPointerIndex = MenuItems.Count + nSubItemsCount - 1;
						else CurrentPointerIndex--;

						lastActionTimeStamp = DateTime.Now;
					} else if (IsKeyPushedDown(Keys.Down))
					{
						if (DateTime.Now.Subtract(lastActionTimeStamp).Milliseconds <= ACTION_DELAY_THRESHOLD)
						{
							Thread.Sleep(25);
							continue;
						}

						if (CurrentPointerIndex + 1 > MenuItems.Count + nSubItemsCount - 1)
							CurrentPointerIndex = 0;
						else CurrentPointerIndex += 1;

						lastActionTimeStamp = DateTime.Now;
					} else if (IsKeyPushedDown(Keys.Right))
					{
						if (DateTime.Now.Subtract(lastActionTimeStamp).Milliseconds <= ACTION_DELAY_THRESHOLD)
						{
							Thread.Sleep(25);
							continue;
						}

						Console.WriteLine($"Key 'Right' was pressed on item with idx {CurrentPointerIndex}");

						lastActionTimeStamp = DateTime.Now;
					} else if (IsKeyPushedDown(Keys.Left))
					{
						if (DateTime.Now.Subtract(lastActionTimeStamp).Milliseconds <= ACTION_DELAY_THRESHOLD)
						{
							Thread.Sleep(25);
							continue;
						}

						Console.WriteLine($"Key 'Left' was pressed on item with idx {CurrentPointerIndex}");
						lastActionTimeStamp = DateTime.Now;
					}
					else
						Thread.Sleep(25);
				}
				else
				{
					Thread.Sleep(25);
				}
			}
		}

		public bool IsPointerPointingAtItem(MenuItem itm) => itm != null && CurrentPointerIndex == itm.ItemIndex;
		public bool IsPointerPointingAtSubItem(SubMenuItem itm) => itm != null && CurrentPointerIndex == itm.ItemIndex;

		public void IsPointerIndexPointingToSubItem()
		{
			int nSubItemsCount = 0;
			MenuItems.ForEach(x => nSubItemsCount += x.SubItems?.Count ?? 0);

			for (int n = 0; n < MenuItems.Count + nSubItemsCount; n++)
			{
				
			}

			
		}

		public MenuItem GetCurrentPointerItem => MenuItems[CurrentPointerIndex];
		public void SetCurrentPointingItem(int index)
		{
			CurrentPointerIndex = index;
		}
		public void SetCurrentPointingItem(MenuItem item)
		{
			CurrentPointerIndex = item.ItemIndex;
		}
	}

	public class MenuItem
	{
		public string ItemText;
		public string ItemTag;
		public int ItemIndex = -1;
		public List<SubMenuItem> SubItems = new List<SubMenuItem>();

		public ItemValueType ItemType = ItemValueType.BasicStringToggle;
		public string CurrentValue = "";
		public List<string> ItemValues = new List<string>() { "Disabled", "Enabled" };

		public MenuItem(string itemText, string itemTag, ItemValueType valueType = ItemValueType.BasicStringToggle)
		{
			ItemText = itemText;
			ItemTag = itemTag;

			CurrentValue = ItemValues[0];
		}
	}

	public class SubMenuItem
	{
		public string ItemText;
		public string ItemTag;
		public int ItemIndex = -1;
		public ItemValueType ItemType;
		public string CurrentValue = "";
		public List<string> ItemValues = new List<string>() { "Disabled", "Enabled" };

		public MenuItem Parent;

		public SubMenuItem(string subItemText, string itemTag, ref MenuItem parentItem, ItemValueType valueType = ItemValueType.InheritFromParent, bool inheritValueTypeFromParent = true)
		{
			ItemText = subItemText;
			ItemTag = itemTag;
			Parent = parentItem ?? throw new Exception($"Parent cannot be null!");

			ItemType = inheritValueTypeFromParent ? parentItem.ItemType : valueType;
			CurrentValue = ItemValues[0];
		}
	}

	public enum CurrentItemType
	{
		MenuItem = 1,
		SubMenuItem = 2,
		Exception = 3,
	}

	public enum ItemValueType
	{
		InheritFromParent = -1,

		BasicStringToggle = 0,

		// Not yet implemented
		Checkbox = 1,
		Slider = 2,
	}
}
