using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using static TestInject.Memory;
using static TestInject.DebugConsole;
using static TestInject.Memory.Enums;
using static TestInject.AssaultCube.Delegates;

using static TestInject.AssaultCube;

using static TestInject.Aura_Kingdom.Structures;

namespace TestInject
{
	public class MyLibrary
	{
		public static Detour.HookObj<GetPlayerEntityInCrosshairDelegate> getPlayerInCrossHook;

		public static Detour.HookObj<glEndDelegate> glEndHook;

		public const bool IMPLEMENT_GL_HOOKS = true;

		[DllExport("DllMain", CallingConvention.Cdecl)]
		public static unsafe void EntryPoint()
		{
			UpdateProcessInformation();
			AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
			
			if (!InitiateDebugConsole())
				MessageBox.Show("Failed initiating the debugging console, please restart the program as admin!",
					"Debugging Console Exception",
					MessageBoxButtons.OK,
					MessageBoxIcon.Error);

			//new Thread(SetUpHooks).Start();
			//new Thread(() => new Overlay("AssaultCube").ShowDialog()).Start();

			SelectedTarget* current = Aura_Kingdom.Pointers.SelectedTargetInformation;
			uint address = (uint)current;
			Console.WriteLine($"0x{address:X8} Name: {current->Name()}");


			Console.ReadLine();
		}

		public static void SetUpHooks()
		{
			if (IMPLEMENT_GL_HOOKS)
			{
				IntPtr openGlModule = Modules.GetModuleBaseAddress("opengl32.dll");
				if (openGlModule == IntPtr.Zero)
					Log($"Cannot find base address of 'opengl32.dll'", LogType.Error);
				else
				{
					#region glEnd
					IntPtr glEnd = PInvoke.GetProcAddress(openGlModule, "glEnd");
					if (glEnd != IntPtr.Zero)
					{
						IntPtr glVertex3f = PInvoke.GetProcAddress(openGlModule, "glVertex3f");
						OpenGL.glVertex3f = Marshal.GetDelegateForFunctionPointer<glVertex3fDelegate>(glVertex3f);

						IntPtr glColor3f = PInvoke.GetProcAddress(openGlModule, "glColor3f");
						OpenGL.glColor3f = Marshal.GetDelegateForFunctionPointer<glColor3fDelegate>(glColor3f);

						glEndHook = new Detour.HookObj<glEndDelegate>(glEnd, glEnd_hk, 6);
						if (!glEndHook.Install())
							Log($"[glEnd] Failed applying jmp hook at 0x{glEnd.ToInt32():X8}", LogType.Error);
						else
							Log($"[glEnd] Successfully applied jmp hook at 0x{glEnd.ToInt32():X8}");
					}
					else
						Log($"Failed getting base address of function 'glEnd' from module 'opengl32.dll'", LogType.Error);

					#endregion
				}
			}

			#region GetPlayerEntityInCrosshair
			//getPlayerInCrossHook = new Detour.HookObj<GetPlayerEntityInCrosshairDelegate>(new IntPtr(0x004607C0),
			//	GetPlayerEntityInCrosshair_hk,
			//	6);

			// Can call .Install for getPlayerInCrossHook if you want
			// Lets not implement the hook, and instead just keep it like this and call the function
			// with getPlayerInCrossHook.UnmodifiedOriginalFunction() to get the player in our crosshair
			#endregion

			Console.WriteLine($"Thread 'SetUpHooks()' finished execution!");
		}

		
		public static void glEnd_hk()
		{
			Console.WriteLine($"[{DateTime.Now.ToLongTimeString()}] Inside glEnd_hk");

			// Draw here

			//OpenGL.glColor3f(125, 125, 125);
			//OpenGL.glVertex3f(100, 100, 0.0f);

			glEndHook.UnmodifiedOriginalFunction();
		}

		public static int GetPlayerEntityInCrosshair_hk()
		{
			// No real use for this hook, was just playing around
			// can call

			return getPlayerInCrossHook.UnmodifiedOriginalFunction();
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
