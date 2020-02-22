using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading;
using System.Windows.Forms;
using static TestInject.Memory;
using static TestInject.AssaultCube;

namespace TestInject
{
	public partial class Overlay : Form
	{
		#region PInvoke
		[DllImport("user32.dll")]
		static extern ushort GetAsyncKeyState(int vKey);
		public static bool IsKeyPushedDown(System.Windows.Forms.Keys vKey)
		{
			return 0 != (GetAsyncKeyState((int)vKey) & 0x8000);
		}

		[DllImport("user32.dll", EntryPoint = "GetWindowLong")]
		public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

		[DllImport("user32.dll", EntryPoint = "SetWindowLong")]
		private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

		[DllImport("user32.dll", SetLastError = true)]
		public static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);

		[DllImport("user32.dll", EntryPoint = "FindWindow", SetLastError = true)]
		public static extern IntPtr FindWindowByCaption(IntPtr ZeroOnly, string lpWindowName);

		[StructLayout(LayoutKind.Sequential)]
		public struct RECT
		{
			public int Left;        // x position of upper-left corner
			public int Top;         // y position of upper-left corner
			public int Right;       // x position of lower-right corner
			public int Bottom;      // y position of lower-right corner

			public (int X, int Y) GetPositionOnScreen()
			{
				return (Left, Top);
			}

			public (int Width, int Height) GetDimensions()
			{
				return (Right - Left + 1, Bottom - Top + 1);
			}
		}

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		static extern bool IsIconic(IntPtr hWnd);
		#endregion

		public static IntPtr TargetWindowHandle;
		public static Thread Repositioner;

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		public delegate int GetEntityInCrosshair();

		public static GetEntityInCrosshair GetEntInCrosshair;

		public class GDIControl
		{
			public string ControlTag;

			public string ControlText;
			public int ControlIndex;

			public int SelectedValueIndex = 0;
			public string SelectedValueString;
			public List<string> Values = new List<string>()
			{
				"Disabled",
				"Enabled"
			};
		}
		public class GDIMenu
		{
			public string Title;

			public int Width;
			public int Height;

			public int X;
			public int Y;

			public int SelectedIndex = 0;
			public GDIControl ActiveControl;

			public bool ShouldDraw;

			public List<GDIControl> Controls = new List<GDIControl>();

			public Thread KeyThread;

			public readonly Brush Black = new SolidBrush(Color.Black);
			public readonly Brush Green = new SolidBrush(Color.Green);
			public readonly Font Arial = new Font(new FontFamily("Arial"), 9f, FontStyle.Bold);

			public GDIMenu(string title, int x, int y, int width = 250, int height = 400)
			{
				Title = title;
				Width = width;
				Height = height;
				X = x;
				Y = y;

				KeyThread = new Thread(KeyLoop);
				KeyThread.Start();
			}
			public void Render(ref Graphics obj)
			{
				if (!ShouldDraw)
					return;

				obj?.FillRectangle(Black, X, Y, Width, Height);
				obj?.FillRectangle(Brushes.Green, X, Y, Width, 25f);

				obj?.DrawString($"{Title}", Arial, Brushes.Red, X + (Width / 2) - (Title.Length * 3), Y + 7);

				int yOffset = Y + 35;
				int xOffset = X + 15;

				foreach (var controlObject in Controls)
				{
					string translatedValue = "ERROR";
					try
					{
						translatedValue = controlObject.Values[controlObject.SelectedValueIndex];
					}
					catch
					{
						translatedValue = "ERROR";
					}


					obj?.DrawString($"{(SelectedIndex == controlObject.ControlIndex ? ">> " : "")}" +
									$"{controlObject.ControlText} - {translatedValue}", Arial, Green, xOffset, yOffset);

					yOffset += 15;

					if (yOffset > Height - 35)
						Height += 35;


				}
			}

			public string GetValueByControlTag(string tag)
			{
				var obj = Controls.FirstOrDefault(x => string.Equals(x.ControlTag, tag, StringComparison.CurrentCultureIgnoreCase));
				return obj != default ? obj.SelectedValueString.ToLower() : "CONTROL_TITLE_INVALID";
			}
			public void SetValueByControlTag(string tag, string value)
			{
				Controls[Controls.FindIndex(menuItem => 
					string.Equals(menuItem.ControlTag, tag, StringComparison.CurrentCultureIgnoreCase))
				].SelectedValueString = value;
				// Incomplete
			}

			private void KeyLoop()
			{
				bool hasSlept = false;

				while (true)
				{
					if (IsKeyPushedDown(Keys.Insert))
					{
						ShouldDraw = !ShouldDraw;
						if (!ShouldDraw)
							Thread.Sleep(475);
					}

					if (!ShouldDraw)
					{
						Thread.Sleep(25);
						continue;
					}

					if (IsKeyPushedDown(Keys.Down))
					{
						if (SelectedIndex + 1 > Controls.Count - 1)
							SelectedIndex = 0;
						else
							SelectedIndex++;

						Thread.Sleep(250);
						hasSlept = true;
					}

					if (IsKeyPushedDown(Keys.Up))
					{
						if (SelectedIndex - 1 < 0)
							SelectedIndex = Controls.Count - 1;
						else
							SelectedIndex--;

						Thread.Sleep(250);
						hasSlept = true;
					}

					ActiveControl = Controls.FirstOrDefault(x => x.ControlIndex == SelectedIndex);

					if (IsKeyPushedDown(Keys.Left))
					{
						if (ActiveControl == null)
						{
							Thread.Sleep(25);
							continue;
						}

						var currentValueIndex = ActiveControl.SelectedValueIndex;

						if (currentValueIndex - 1 < 0)
							currentValueIndex = ActiveControl.Values.Count - 1;
						else
							currentValueIndex--;

						ActiveControl.SelectedValueIndex = currentValueIndex;
						ActiveControl.SelectedValueString = ActiveControl.Values[ActiveControl.SelectedValueIndex];

						Thread.Sleep(250);
						hasSlept = true;
					}

					if (IsKeyPushedDown(Keys.Right))
					{
						if (ActiveControl == null)
						{
							Thread.Sleep(25);
							continue;
						}

						var currentValueIndex = ActiveControl.SelectedValueIndex;

						if (currentValueIndex + 1 > ActiveControl.Values.Count - 1)
							currentValueIndex = 0;
						else
							currentValueIndex++;

						ActiveControl.SelectedValueIndex = currentValueIndex;
						ActiveControl.SelectedValueString = ActiveControl.Values[ActiveControl.SelectedValueIndex];

						Thread.Sleep(250);
						hasSlept = true;
					}

					if (!hasSlept)
						Thread.Sleep(25);
					else
						hasSlept = false;
				}
			}
		}

		public static bool ShouldShow = true;
		public static Brush Red = new SolidBrush(Color.Red);
		public new static Font Font = new Font(FontFamily.GenericSerif, 16f, FontStyle.Bold);

		public static GDIMenu myMenu;

		public static Thread Feature_Triggerbot;
		public static Thread Feature_Aimbot;

		public Overlay(IntPtr targetWindowHandle)
		{
			InitializeComponent();

			if (targetWindowHandle == IntPtr.Zero)
				throw new InvalidOperationException($"Null handle was passed to the Overlay");

			TargetWindowHandle = targetWindowHandle;

			myMenu = new GDIMenu("C# Internal", 100, 100);

			GDIControl itm1 = new GDIControl { ControlText = "Aimbot", ControlIndex = 0, ControlTag = "aimbot" };
			GDIControl itm2 = new GDIControl { ControlText = "ESP", ControlIndex = 1, ControlTag = "esp" };
			GDIControl itm3 = new GDIControl { ControlText = "Bhop", ControlIndex = 2, ControlTag = "bhop" };
			GDIControl itm4 = new GDIControl { ControlText = "Auto Pistol", ControlIndex = 3, ControlTag = "autopistol" };
			GDIControl itm5 = new GDIControl { ControlText = "Triggerbot", ControlIndex = 4, ControlTag = "triggerbot" };

			myMenu.Controls.Add(itm1);
			myMenu.Controls.Add(itm2);
			myMenu.Controls.Add(itm3);
			myMenu.Controls.Add(itm4);
			myMenu.Controls.Add(itm5);

			int i = 0;
			foreach (var controlObject in myMenu.Controls)
			{
				controlObject.SelectedValueIndex = 0;
				controlObject.SelectedValueString = controlObject.Values[controlObject.SelectedValueIndex];
				controlObject.ControlIndex = i;

				i++;
			}

			GetEntInCrosshair = Marshal.GetDelegateForFunctionPointer<GetEntityInCrosshair>(new IntPtr(0x004607C0));

			Repositioner = new Thread(() => RepositionLoop(this));
			Repositioner?.Start();

			Feature_Triggerbot = new Thread(TriggerBot);
			Feature_Triggerbot.Start();

			Feature_Aimbot = new Thread(Aimbot);
			Feature_Triggerbot.Start();
		}
		public Overlay(string targetWindowTitle)
		{
			InitializeComponent();

			TargetWindowHandle = FindWindowByCaption(IntPtr.Zero, targetWindowTitle);
			if (TargetWindowHandle == IntPtr.Zero)
				throw new InvalidOperationException($"Could not find window with title '{targetWindowTitle}'");

			myMenu = new GDIMenu("C# Internal", 100, 100);

			GDIControl itm1 = new GDIControl { ControlText = "Aimbot", ControlIndex = 0, ControlTag = "aimbot" };
			GDIControl itm2 = new GDIControl { ControlText = "ESP", ControlIndex = 1, ControlTag = "esp" };
			GDIControl itm3 = new GDIControl { ControlText = "Bhop", ControlIndex = 2, ControlTag = "bhop" };
			GDIControl itm4 = new GDIControl { ControlText = "Auto Pistol", ControlIndex = 3, ControlTag = "autopistol" };
			GDIControl itm5 = new GDIControl { ControlText = "Triggerbot", ControlIndex = 4, ControlTag = "triggerbot" };

			myMenu.Controls.Add(itm1);
			myMenu.Controls.Add(itm2);
			myMenu.Controls.Add(itm3);
			myMenu.Controls.Add(itm4);
			myMenu.Controls.Add(itm5);

			int i = 0;
			foreach (var controlObject in myMenu.Controls)
			{
				controlObject.SelectedValueIndex = 0;
				controlObject.SelectedValueString = controlObject.Values[controlObject.SelectedValueIndex];
				controlObject.ControlIndex = i;

				i++;
			}

			GetEntInCrosshair = Marshal.GetDelegateForFunctionPointer<GetEntityInCrosshair>(new IntPtr(0x004607C0));

			Repositioner = new Thread(() => RepositionLoop(this));
			Repositioner?.Start();

			Feature_Triggerbot = new Thread(TriggerBot);
			Feature_Triggerbot.Start();

			Feature_Aimbot = new Thread(Aimbot);
			Feature_Aimbot.Start();

		}

		protected override void OnPaint(PaintEventArgs e)
		{
			base.OnPaint(e);
			
			Graphics g = e.Graphics;
			// Somehow not disposing our graphics object when doublebuffered is enabled is good?

			if (myMenu.ShouldDraw)
				myMenu.Render(ref g);

			unsafe
			{
				Structures.D3DMATRIX* viewMatrix = (Structures.D3DMATRIX*)0x00501AE8;
				PlayerEntity* localPlayer = (PlayerEntity*)*(uint*)0x50F4F4;

				if (!EntityList.UpdatePlayerList())
				{
					Invalidate();
					return;
				}

				foreach (var plr in EntityList.PlayerList)
				{
					if (myMenu.GetValueByControlTag("esp") == "enabled")
					{
						PlayerEntity* currPlayer = (PlayerEntity*)plr;
						if (currPlayer->Health < 1 || currPlayer->State != CState.CS_ALIVE || localPlayer->Health < 1 || localPlayer->State != CState.CS_ALIVE)
							continue;
						int distance = (int)Math.Round(currPlayer->Origin.Distance3D(localPlayer->Origin), MidpointRounding.AwayFromZero);

						if (!currPlayer->Origin.World2Screen(viewMatrix->AsArray(), out var feetScreenPos)) continue;
						if (currPlayer->Head.World2Screen(viewMatrix->AsArray(), out var headScreenPos))
						{
							int offset = 20;
							float height = Math.Abs(headScreenPos.Y - feetScreenPos.Y);
							float width = height / 2;

							g.DrawRectangle(currPlayer->IsInMyTeam(localPlayer) ? Pens.Green : Pens.Red, 
								headScreenPos.X - width / 2, headScreenPos.Y - offset, 
								width, height + offset);

							g.DrawString($"{distance} m",
								Font,
								Red,
								feetScreenPos.X - 25f,
								feetScreenPos.Y);

						}
					}
				}

				if (myMenu.GetValueByControlTag("autopistol") == "enabled")
				{
					if (localPlayer->WeaponPtr->AmmoInfo->CurrentAmmoCount > 0
					    && IsKeyPushedDown(Keys.LButton))
						localPlayer->IsShooting = 1;
				}
			}

			Invalidate();
		}

		private void Overlay_Load(object sender, EventArgs e)
		{
			BackColor = Color.Teal;
			TransparencyKey = Color.Teal;

			FormBorderStyle = FormBorderStyle.None;

			TopMost = true;

			SetWindowLong(Handle, -20, 
				GetWindowLong(Handle, -20) | 0x80000 | 0x20);

			myMenu.ShouldDraw = true;
		}

		public static void RepositionLoop(Overlay formInstance)
		{
			while (true)
			{
				if (ShouldShow && formInstance != null/* && !IsIconic(TargetWindowHandle) Iconic returns true if window is minimized? */)
				{
					bool wndRectTry = GetWindowRect(TargetWindowHandle, out RECT windowPosition);

					if (TargetWindowHandle != IntPtr.Zero &&
					    wndRectTry)
					{
						var (x, y) = windowPosition.GetPositionOnScreen();
						var (width, height) = windowPosition.GetDimensions();

						if (formInstance.InvokeRequired)
						{
							formInstance.Invoke((MethodInvoker)delegate {
								formInstance.Location = new Point(x, y);
								formInstance.Size = new Size(width, height);
							});
						}
						else
						{
							formInstance.Location = new Point(x, y);
							formInstance.Size = new Size(width, height);
						}
					}
					else
					{
						Console.WriteLine($"{nameof(TargetWindowHandle)} = '0x{TargetWindowHandle.ToInt32():X8}'\n" +
						                  $"{nameof(wndRectTry)} = {(wndRectTry == false ? "Failed" : "Succeeded")}");
					}
				}
				else
				{
					// Dont render overlay
					if (formInstance != null
					    && formInstance.Visible)
						formInstance.Visible = false;
				}


				Thread.Sleep(25);
			}
		}

		#region Features
		public static unsafe void TriggerBot()
		{
			PlayerEntity* localPlayer = (PlayerEntity*)*(uint*)0x50F4F4;

			while (true)
			{
				PlayerEntity* inCrosshair = (PlayerEntity*)GetEntInCrosshair();
				if (inCrosshair != null 
				    && myMenu.GetValueByControlTag("triggerbot") == "enabled")
				{
					if (!localPlayer->IsInMyTeam(inCrosshair))
					{
						if (inCrosshair->Health > 0 && inCrosshair->State == CState.CS_ALIVE
						                            && localPlayer->Health > 0 && localPlayer->State == CState.CS_ALIVE)
						{
							localPlayer->IsShooting = GetEntInCrosshair() > 0 ? (byte)1 : (byte)0;
						}
					}
				}

				Thread.Sleep(150);
			}
		}
		public static unsafe void Aimbot()
		{
			PlayerEntity* localPlayer = (PlayerEntity*)*(uint*)0x50F4F4;

			while (true)
			{
				if (myMenu.GetValueByControlTag("aimbot") == "enabled" 
				    && EntityList.UpdatePlayerList()
				    && localPlayer != null)
				{
					PlayerEntity* closest = EntityList.GetClosestEntityToCrosshair(localPlayer);
					//PlayerEntity* closest = (PlayerEntity*)GetEntInCrosshair();
					if (closest != null && IsKeyPushedDown(Keys.RButton)
					                    && closest->Health > 0 && closest->State == CState.CS_ALIVE &&
					                    localPlayer->Health > 0 && closest->State == CState.CS_ALIVE)
						localPlayer->AimAt(closest);
				}
			}
		}
		public static unsafe void BhopLoop()
		{
			PlayerEntity* localPlayer = (PlayerEntity*)*(uint*)0x50F4F4;

			while (true)
			{

				Thread.Sleep(25);
			}
		}
		#endregion
	}
}
