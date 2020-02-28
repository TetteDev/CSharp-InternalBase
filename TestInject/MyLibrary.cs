using System;
using System.ComponentModel.Design.Serialization;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
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
		public static Detour.HookObj<glVertex3fDelegate> glVertex3fHook;

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

			IntPtr openGlModule = Modules.GetModuleBaseAddress("opengl32.dll");
			if (openGlModule == IntPtr.Zero)
				Log($"Cannot find base address of 'opengl32.dll'", LogType.Error);
			else
			{
				IntPtr glVertex3f = PInvoke.GetProcAddress(openGlModule, "glVertex3f");
				if (glVertex3f != IntPtr.Zero)
				{
					Console.WriteLine($"Function 'glVertex3f': 0x{glVertex3f.ToInt32():X8}");
					glVertex3fHook = new Detour.HookObj<glVertex3fDelegate>(glVertex3f,
						glVertex3f_Hk,
						11);

					if (glVertex3fHook != null)
						glVertex3fHook.Install();
					else
						Log($"Failed applying jmp hook at 0x{glVertex3f.ToInt32():X8}", LogType.Error);
				}
					
				else
					Log($"Failed getting base address of function 'glVertex3f' from module 'opengl32.dll'", LogType.Error);

			}

			/*
			obj = new Detour.HookObj<GetPlayerEntityInCrosshairDelegate>(new IntPtr(0x004607C0), 
				GetPlayerEntityInCrosshair_HK,
				6,
				true);
			*/

			Console.ReadLine();
		}


		[MethodImpl(MethodImplOptions.NoOptimization)]
		public static void glVertex3f_Hk(float x, float y, float z)
		{
			Console.WriteLine("Inside glVertex3f_Hk\n" +
			                  "	Parameters: \n" +
			                  $"		* X: {x}\n" +
			                  $"		* Y: {y}\n" +
			                  $"		* Z: {z}\n" +
			                  $"	Unmodified Function Call Address: 0x{Marshal.GetFunctionPointerForDelegate(glVertex3fHook.UnmodifiedOriginalFunction).ToInt32():X8}");

			glVertex3fHook.UnmodifiedOriginalFunction(x, y, z);
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
