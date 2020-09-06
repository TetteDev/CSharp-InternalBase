using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using static TestInject.Memory;
using static TestInject.DebugConsole;

namespace TestInject
{
	public class MyLibrary
	{
		[DllExport("DllMain", CallingConvention.Cdecl)]
		public static unsafe void EntryPoint()
		{
			PatchEtw(true);
			UpdateProcessInformation();
			AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
			
			if (!InitiateDebugConsole())
				MessageBox.Show("Failed initiating the debugging console, please restart the program as admin!",
					"Debugging Console Exception",
					MessageBoxButtons.OK,
					MessageBoxIcon.Error);

			Thread t = new Thread(SetUpHooks);
			t.Start(); t.Join();


			Console.ReadLine();
		}

		public static void SetUpHooks()
		{

			Console.WriteLine("Thread 'SetUpHooks()' finished execution!");
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

		public static bool PatchEtw(bool verbose = false)
		{
			var current = Process.GetCurrentProcess();
			var ntdll = current.Modules.Cast<ProcessModule>()
				.FirstOrDefault(proc => proc.ModuleName != null && proc.ModuleName.Contains("ntdll"));
			if (ntdll == default)
			{
				if (verbose) Console.WriteLine("Could not get base address of ntdll.dll");
				return false;
			}
			if (verbose) Console.WriteLine($"ntdll: 0x{ntdll.BaseAddress.ToInt64():X8}");

			var etwEventWrite = PInvoke.GetProcAddress(ntdll.BaseAddress, "EtwEventWrite");
			if (etwEventWrite == IntPtr.Zero)
			{
				if (verbose) Console.WriteLine("Could not find address of 'EtwEventWrite'");
				return false;
			}
			if (verbose) Console.WriteLine($"EtwEventWrite: 0x{etwEventWrite.ToInt64():X8}");

			unsafe
			{
				Enums.MemoryProtection oldProtection = default;
				if (IntPtr.Size == 4)
				{
					/* 32 bit */
					if (!Protection.SetPageProtection(etwEventWrite, 3, Enums.MemoryProtection.ExecuteReadWrite, out oldProtection))
					{
						if (verbose) Console.WriteLine("Could not change protection of 'etwEventWrite'");
						return false;
					}

					fixed (void* pArr = new byte[] { 0xc2, 0x14, 0x00 /* ret 0x14 */ })
						Unsafe.CopyBlockUnaligned(
							etwEventWrite.ToPointer(),
							pArr,
							3);
				}
				else
				{
					/* 64 bit */
					IntPtr etwEventWriteCall = IntPtr.Add(etwEventWrite, 0x24);
					if (*(byte*)etwEventWriteCall != 0xE8
						&& *(byte*)(etwEventWriteCall + 1) != 0x5B) /* check if instruction is call */
					{
						int offsetItterator = 0x0;
						int realOffset = -0x1;
						const int stopThreshold = 0x78;
						while (*(byte*)(etwEventWrite + offsetItterator) != 0xC3)
						{
							if (offsetItterator >= stopThreshold)
							{
								if (verbose) Console.WriteLine("PatchETW (64bit) itterated over 120 bytes without stopping, exiting from function ...");
								return false;
							}

							if (*(byte*)(etwEventWrite + offsetItterator) == 0xE8
								&& *(byte*)(etwEventWrite + offsetItterator + 1) == 0x5B)
							{
								realOffset = offsetItterator;
								break;
							}

							offsetItterator++;
						}

						if (realOffset == -0x1)
							return false;

						etwEventWriteCall = IntPtr.Add(etwEventWrite, realOffset);
					}

					if (!Protection.SetPageProtection(etwEventWriteCall, 5, Enums.MemoryProtection.ExecuteReadWrite, out oldProtection))
					{
						if (verbose) Console.WriteLine("Could not change protection of 'etwEventWrite'");
						return false;
					}

					fixed (void* pArr = new byte[] { 0x90, 0x90, 0x90, 0x90, 0x90 /* NOP call at etwEventWriteCall */ })
						Unsafe.CopyBlockUnaligned(
							etwEventWriteCall.ToPointer(),
							pArr,
							5);
				}

				bool restore = Protection.SetPageProtection(etwEventWrite, 3, oldProtection, out _);
				if (!restore && verbose)
					Console.WriteLine("Restoring of protection for EtwEventWrite failed");

				if (verbose) Console.WriteLine("EtwEventWrite has been patched successfully");
				return true;
			}
		}
	}
}
