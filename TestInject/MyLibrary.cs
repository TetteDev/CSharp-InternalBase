using System;
using System.Collections;
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

		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		public delegate int SDLSwapBuffers();

		public static Detours.Hk<SDLSwapBuffers> SwapBuffersHK;

		public static Detours.Hk<GetPlayerInCrosshair> MyHk;

		[DllExport("DllMain", CallingConvention.Cdecl)]
		public static void EntryPoint()
		{
			//PatchETW(true);
			UpdateProcessInformation();
			AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
			
			if (!DebugConsole.InitiateDebugConsole())
				MessageBox.Show("Failed initiating the debugging console, please restart the program as admin!",
					"Debugging Console Exception",
					MessageBoxButtons.OK,
					MessageBoxIcon.Error);

			var al = Allocator.Managed.ManagedAllocate(12, Enums.MemoryProtection.ExecuteReadWrite);
			Console.WriteLine($"Managed Allocatio Base: 0x{al.ToInt32():X8}");
			Console.ReadLine();

			//new Thread(() => new Overlay("Counter-Strike: Global Offensive").ShowDialog()).Start();

			/*
			IntPtr mod = Modules.GetModuleBaseAddress("SDL.dll");
			uint oSDLSwapBuffers = (uint)PInvoke.GetProcAddress(mod, "SDL_GL_SwapBuffers");
			if (oSDLSwapBuffers != 0)
			{
				Console.WriteLine($"SDLSwapBuffers: 0x{oSDLSwapBuffers:X8}");
				SwapBuffersHK = new Detours.Hk<SDLSwapBuffers>(oSDLSwapBuffers, HkSDLSwapBuffers, 5, true);
				SwapBuffersHK.Install();
			}
			else
				Console.WriteLine("Failed SDLSwapBuffers");
			*/


			/*
			Console.WriteLine("Preparing to hook @ 0x004607C0");
			MyHk = new Detours.Hk<GetPlayerInCrosshair>(0x004607C0, HkGetPlayerInCrosshair, 6, true);
			if (!MyHk.IsInstalled)
				MyHk.Install();
			Console.WriteLine("Done installing hook @ 0x004607C0");
			*/
		}

		public static int HkSDLSwapBuffers()
		{
			Console.WriteLine($"[{DateTime.Now.ToLongTimeString()}] Inside Hook");

			return MyHk.ContinueExecution();
		}

		public static int HkGetPlayerInCrosshair()
		{
			Console.WriteLine($"[{DateTime.Now.ToLongTimeString()}] Inside Hook");

			return MyHk.ContinueExecution();
		}

		public static unsafe void MyCallbackMethod(IntPtr registerPtr)
		{
			if (registerPtr == IntPtr.Zero)
			{
				Console.WriteLine("MyCallbackMethod - Failed");
				return;
			}

			Structures.Registers* reg = (Structures.Registers*)registerPtr;
			Console.WriteLine(reg->PrintRegisters());

			// We know that the pointer in ESI points to the value of our current ammo
			if ((int*) reg->ESI != null)
			{
				*(int*)reg->ESI = new Random().Next(1, 1337);
				Console.WriteLine($"Current Ammo: {*(int*)reg->ESI}");
			}

			return;
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

		public static void PatchETW(bool verbose = false)
		{
			var ntdll = Modules.GetModuleBaseAddress("ntdll");
			if (ntdll == IntPtr.Zero)
				ntdll = PInvoke.LoadLibrary("ntdll.dll");

			if (verbose) Console.WriteLine($"ntdll: 0x{ntdll.ToInt32():X8}");

			var etwEventSend = PInvoke.GetProcAddress(ntdll, "EtwEventWrite");
			if (verbose) Console.WriteLine($"EtwEventWrite: 0x{etwEventSend.ToInt32():X8}");
			if (etwEventSend != IntPtr.Zero)
			{
				Protection.SetPageProtection(etwEventSend, 3, Enums.MemoryProtection.ExecuteReadWrite, out var old);
				Writer.WriteBytes(etwEventSend, new byte[] { 0xc2, 0x14, 0x00 });
				Protection.SetPageProtection(etwEventSend, 3, old, out _);
				if (verbose) Console.WriteLine("EtwEventWrite Patching Done!");
			}
		}
	}
}
