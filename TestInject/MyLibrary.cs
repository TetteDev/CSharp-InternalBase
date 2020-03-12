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
		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		public delegate int GetPlayerInCrosshair();

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		public unsafe delegate int CallbackDelegate(IntPtr addrRegisterStruct);

		public static Detours.Callback MyCallback;

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

			//SetUpHooks();
			//new Thread(() => new Overlay("Counter-Strike: Global Offensive").ShowDialog()).Start();

			Console.WriteLine($"Installing code execution callback");
			MyCallback = new Detours.Callback(new IntPtr(0x004637E9), 
				new CallbackDelegate(MyCallbackMethod), 
				7);
			MyCallback.Install();
			Console.WriteLine($"Done!");


			Console.ReadLine();
		}

		public static unsafe int MyCallbackMethod(IntPtr registerPtr)
		{
			if (registerPtr == IntPtr.Zero)
			{
				Console.WriteLine($"MyCallbackMethod - Failed");
				return 0;
			}

			Structures.Registers* reg = (Structures.Registers*)registerPtr;
			Console.WriteLine(reg->PrintRegisters());

			if (reg->ESI != 0)
			{
				Console.WriteLine($"Current Ammo Pointer: 0x{reg->ESI:X8}");

				Console.WriteLine("Setting our current ammo to a random number\n");
				*(int*) reg->ESI = new Random().Next(1, 999);
			}
			
			return 0;
		}
		 
		

		public static unsafe void SetUpHooks()
		{
			Console.WriteLine($"[{DateTime.Now.ToLongTimeString()}] Hooks::SetupHooks() started execution!\n");

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
