using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using static TestInject.Memory;

namespace TestInject
{
	public class MyLibrary
    {
	    [DllExport("DllMain", CallingConvention.Cdecl)]
		public static void EntryPoint()
		{
			UpdateProcessInformation();
			AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
			
			if (!DebugConsole.InitiateDebugConsole())
				MessageBox.Show("Failed initiating the debugging console, please restart the program as admin!",
					"Debugging Console Exception",
					MessageBoxButtons.OK,
					MessageBoxIcon.Error);

			// Probably dont do any hacking here, start a thread to your real main entry point
			new Thread(() => new Overlay("Counter-Strike: Global Offensive").ShowDialog()).Start();

			Console.ReadLine();
		}

		static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
		{
			Exception obj = (Exception)e?.ExceptionObject;
			if (obj != null)
				PInvoke.SetLastError((uint)obj.HResult);

			HelperMethods.PrintExceptionData(e?.ExceptionObject, true);
			Console.WriteLine($"Marshal.GetLastWin32Error Code: 0x{Marshal.GetLastWin32Error():X8}");


			if (Debugger.IsAttached)
				Debugger.Break();
		}
    }
}
