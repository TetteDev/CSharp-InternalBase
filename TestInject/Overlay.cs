using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using static TestInject.Memory;
using static TestInject.CSGO;
using static TestInject.CSGO.Structures;
using static TestInject.Memory.Detours;

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

		public static bool ShouldShow = true;
		public static Brush Red = new SolidBrush(Color.Red);
		public new static Font Font = new Font(FontFamily.GenericSerif, 16f, FontStyle.Bold);

		public static Delegates.EndSceneDelegate endScene;
		public static HookObj<Delegates.EndSceneDelegate> endSceneHook;

		public static GMenu _menu;

		public Overlay(IntPtr targetWindowHandle)
		{
			InitializeComponent();

			if (targetWindowHandle == IntPtr.Zero)
				throw new InvalidOperationException($"Null handle was passed to the Overlay");

			TargetWindowHandle = targetWindowHandle;

			GMenuConfig conf = new GMenuConfig
			{
				Dimensions = new Size(250, 400),
				DrawLocation = new Point(100, 100),
				MenuTitle = "C# GDI Menu",
				MenuTitleFont = new Font(FontFamily.GenericSansSerif, 10f, FontStyle.Bold),
				MenuItems = new List<GMenuItemGroup>()
				{
					new GMenuItemGroup("Empty")
					{
						GroupItems = new List<GMenuItem>()
						{
							new GMenuItem("Test 1", "aimbot"),
							new GMenuItem("Test 2", "deathmatch" ,true),
						}
					},
				}
			};

			_menu = new GMenu(conf);

			Repositioner = new Thread(() => RepositionLoop(this));
			Repositioner?.Start();
		}
		public Overlay(string targetWindowTitle)
		{
			InitializeComponent();

			TargetWindowHandle = FindWindowByCaption(IntPtr.Zero, targetWindowTitle);
			if (TargetWindowHandle == IntPtr.Zero)
				throw new InvalidOperationException($"Could not find window with title '{targetWindowTitle}'");

			GMenuConfig conf = new GMenuConfig
			{
				Dimensions = new Size(250, 400),
				DrawLocation = new Point(50, 50),
				MenuTitle = "C# GDI Menu",
				MenuTitleFont = new Font(FontFamily.GenericSansSerif, 10f, FontStyle.Bold),
				MenuItems = new List<GMenuItemGroup>()
				{
					new GMenuItemGroup("Empty")
					{
						GroupItems = new List<GMenuItem>()
						{
							new GMenuItem("Feature 1", "aimbot"),
							new GMenuItem("Sub Feature 1 for Feature 1", "deathmatch" ,true),
							new GMenuItem("Sub Feature 2 for Feature 1", "deathmatch" ,true),
						}
					},
				}
			};

			_menu = new GMenu(conf);

			Repositioner = new Thread(() => RepositionLoop(this));
			Repositioner?.Start();
		}

		protected override unsafe void OnPaint(PaintEventArgs e)
		{
			base.OnPaint(e);
			
			Graphics g = e.Graphics;
			// Somehow not disposing our graphics object when doublebuffered is enabled is good?
			
			if (_menu != null && _menu.IsShowing)
				_menu.DrawMenu(ref g);

			// Draw here
			/*
			ClientState_t* client = Methods.GetClientState();
			if (client != null && client->GameState == CSGO.Enums.GameState.GAME)
			{
				LocalPlayer_t* local = Methods.GetLocalPlayer();
				GlobalVars_t* global = Methods.GetGlobalVars();

				for (int playerIndex = 0; playerIndex < client->MaxPlayers; playerIndex++)
				{
					Enemy_t* enemy = (Enemy_t*)((CSGO.Modules.Client + Offsets.signatures.dwEntityList) + (playerIndex * 0x10));

					if (enemy == null)
						break;

					if ((uint)enemy == (uint)local || enemy->Dormant)
						continue;

					Console.WriteLine($"Player Base: 0x{(uint)enemy:X8}\n" +
					                  $"	Health: {enemy->Health}");
				}

				int x = 1;
			}
			*/

			Invalidate();
		}

		private unsafe void Overlay_Load(object sender, EventArgs e)
		{
			BackColor = Color.Teal;
			TransparencyKey = Color.Teal;
			FormBorderStyle = FormBorderStyle.None;
			TopMost = true;

			SetWindowLong(Handle, -20, 
				GetWindowLong(Handle, -20) | 0x80000 | 0x20);

			CSGO.Modules.UpdateModules();

			var ptrEndSceneFunc = Methods.DX9GetVTablePointerAtIndex(42);

			if (ptrEndSceneFunc != null)
			{

				/*
				endSceneHook = new HookObj<Delegates.EndSceneDelegate>(new IntPtr(*ptrEndSceneFunc),
					EndScene_hk, 7, true);
					*/

				Console.WriteLine($"EndScene hook {(endSceneHook.IsImplemented ? "implemented successfully!" : " failed to implement ...")}");
			}
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
	}
}
