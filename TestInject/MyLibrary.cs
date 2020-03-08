using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using static TestInject.Memory;
using static TestInject.CSGO.Enums;


namespace TestInject
{
	public class MyLibrary
	{
		public static Detours.Hook<GetPlayerInCrosshair> method1;

		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		public delegate int GetPlayerInCrosshair();

		[DllExport("DllMain", CallingConvention.Cdecl)]
		public static unsafe void EntryPoint()
		{
			UpdateProcessInformation();
			AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
			
			if (!DebugConsole.InitiateDebugConsole())
				MessageBox.Show("Failed initiating the debugging console, please restart the program as admin!",
					"Debugging Console Exception",
					MessageBoxButtons.OK,
					MessageBoxIcon.Error);

			SetUpHooks();
			//new Thread(() => new Overlay("Counter-Strike: Global Offensive").ShowDialog()).Start();

			Console.ReadLine();
		}
		 
		public static int MyHook()
		{
			// You have access to modify the register values before it jumps back to the original

			// Continue flow at original function with changes to the register values (if decided to change)
			return method1.Original();
		}

		public static unsafe void SetUpHooks()
		{
			Console.WriteLine($"[{DateTime.Now.ToLongTimeString()}] Hooks::SetupHooks() started execution!\n");

			// Setup hooks here
			Console.WriteLine($"Attempting to hook GetPlayerInCrosshair");
			method1 = new Detours.Hook<GetPlayerInCrosshair>(
				new IntPtr(0x004607C0), new GetPlayerInCrosshair(MyHook), true, 6);
			Console.WriteLine($"GetPlayerInCrosshair Hook {(method1.Install() ? "installed!" : "failed to install ...")}");

			Console.WriteLine($"\n[{DateTime.Now.ToLongTimeString()}] Hooks::SetupHooks() finished execution!");
		}
		static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
		{
			HelperMethods.PrintExceptionData(e?.ExceptionObject, true);

			Exception obj = (Exception)e?.ExceptionObject;
			if (obj != null)
				PInvoke.SetLastError((uint)obj.HResult);

			if (Debugger.IsAttached)
				Debugger.Break();
		}
	}
}
