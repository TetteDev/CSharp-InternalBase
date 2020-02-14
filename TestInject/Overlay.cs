using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using static TestInject.Memory;

namespace TestInject
{
	public partial class Overlay : Form
	{
		#region PInvoke
		[DllImport("user32.dll", EntryPoint = "GetWindowLong")]
		public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

		[DllImport("user32.dll", EntryPoint = "SetWindowLong")]
		private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

		[DllImport("user32.dll", SetLastError = true)]
		public static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);

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
		public static Pen Green = new Pen(Color.Green, 12f);
		public static Font Font = new Font(FontFamily.GenericSerif, 16f, FontStyle.Bold);

		public static bool ESP_ENABLED = true;

		[StructLayout(LayoutKind.Explicit)]
		public unsafe struct PlayerEntity
		{
			[FieldOffset(0x0)]
			public uint vTable;

			[FieldOffset(0x0004)]
			public Structures.Vector3 HeadVector;

			[FieldOffset(0x0034)]
			public Structures.Vector3 PositionMap;

			[FieldOffset(0x0040)]
			public Structures.Vector3 Angles;

			[FieldOffset(0x0071)]
			public byte IsScoping;

			[FieldOffset(0x00F8)]
			public int Health;

			[FieldOffset(0x00FC)]
			public int Armor;

			[FieldOffset(0x0224)]
			public byte IsAttacking;

			[FieldOffset(0x032C)]
			public byte Team;

			[FieldOffset(0x0338)]
			public byte State;

			[FieldOffset(0x0374)]
			public uint* WeaponPtr;
		}

		public Overlay(IntPtr targetWindowHandle)
		{
			InitializeComponent();

			CheckForIllegalCrossThreadCalls = false;

			TargetWindowHandle = targetWindowHandle;
			// Thread thread = new Thread(() => download(filename));
			Repositioner = new Thread(() => RepositionLoop(this));
			Repositioner?.Start();
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			base.OnPaint(e);
			
			// Draw here
			if (!ShouldShow) return;

			Graphics g = e.Graphics;
			// Somehow not disposing our graphics object when doublebuffered is enabled is good?

			g.DrawString($"Assault Cube - C# Internal", Font, Red, 25, 35);

			unsafe
			{
				if (ESP_ENABLED)
				{
					uint entityBaseAddress = (*(uint*)0x50F4F8) + 0x4;

					for (int n = 0; n < ((*(int*)0x0050F500) - 1) * 0x4; n += 0x4)
					{
						PlayerEntity* currPlayer = (PlayerEntity*)*(uint*)(entityBaseAddress + n);
						PlayerEntity* localPlayer = (PlayerEntity*) *(uint*)0x50F4F4;
						Structures.D3DMATRIX* viewMatrix = (Structures.D3DMATRIX*)0x00501AE8;

						if (viewMatrix == null || currPlayer == null || localPlayer == null)
							continue;

						if (currPlayer->Health < 1 || localPlayer->Health < 1)
							continue;

						float dst = currPlayer->HeadVector.Distance3D(localPlayer->HeadVector);

						if (!currPlayer->PositionMap.World2Screen(viewMatrix->AsArray(), out _)) continue;
						if (currPlayer->HeadVector.World2Screen(viewMatrix->AsArray(), out var screenPos))
						{
							g.DrawRectangle(Green, screenPos.X, screenPos.Y, 3, 3);

							g.DrawString($"{dst} m",
								Font, 
								Red, 
								screenPos.X - 25f, 
								screenPos.Y);
						}
							

					}

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

			// Set to click through
			int initialStyle = GetWindowLong(this.Handle, -20);
			SetWindowLong(Handle, -20, initialStyle | 0x80000 | 0x20);
		}

		public static void RepositionLoop(Overlay formInstance)
		{
			while (true)
			{
				if (ShouldShow /* && !IsIconic(TargetWindowHandle) Iconic returns true if window is minimized? */)
				{
					if (TargetWindowHandle != IntPtr.Zero &&
					    GetWindowRect(TargetWindowHandle, out RECT windowPosition))
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
				}
				else
				{
					// Dont render overlay
					if (formInstance.Visible)
						formInstance.Visible = false;
				}
				

				Thread.Sleep(25);
			}
		}
	}
}
