using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using static TestInject.Memory;
using static TestInject.DebugConsole;
using static TestInject.Memory.Enums;
using static TestInject.AssaultCube.Delegates;

namespace TestInject
{
	public class MyLibrary
	{
		public static Detour.HookObj<GetPlayerEntityInCrosshairDelegate> obj;

		[DllExport("DllMain", CallingConvention.Cdecl)]
		public static void EntryPoint()
		{
			UpdateProcessInformation();
			AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
			
			if (!InitiateDebugConsole())
				MessageBox.Show("Failed initiating the debugging console, please restart the program as admin!",
					"Debugging Console Exception",
					MessageBoxButtons.OK,
					MessageBoxIcon.Error);

			//new Thread(() => new Overlay("AssaultCube").ShowDialog()).Start();

			obj = new Detour.HookObj<GetPlayerEntityInCrosshairDelegate>(new IntPtr(0x004607C0), 
				GetPlayerEntityInCrosshair_HK,
				6,
				true);

			Console.ReadLine();
		}

		
		public static int GetPlayerEntityInCrosshair_HK()
		{
			Console.WriteLine($"GetPlayerEntityInCrosshair_HK - Return Value: 0x{obj.UnmodifiedOriginalFunction():X8}");

			return obj.UnmodifiedOriginalFunction();
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
