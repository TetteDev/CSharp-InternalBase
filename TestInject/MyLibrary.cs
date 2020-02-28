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

// OpenGL stuff
using static TestInject.OpenGLHook.Delegates;
using static TestInject.OpenGLHook;

namespace TestInject
{
	public class MyLibrary
	{
		public static Detour.HookObj<GetPlayerEntityInCrosshairDelegate> getPlayerInCrossHook;

		// OpenGL stuff
		public static OpenGLHook o;

		public static void SetUpHooks()
		{
			#region GetPlayerEntityInCrosshair
			/*
			getPlayerInCrossHook = new Detour.HookObj<GetPlayerEntityInCrosshairDelegate>(new IntPtr(0x004607C0),
				GetPlayerEntityInCrosshair_hk,
				6);

			// Can call .Install for getPlayerInCrossHook if you want
			// Lets not implement the hook, and instead just keep it like this and call the function
			// with getPlayerInCrossHook.UnmodifiedOriginalFunction() to get the player in our crosshair
			*/
			#endregion

			Console.WriteLine($"Thread 'SetUpHooks()' finished execution!");
		}

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

			o = new OpenGLHook(glEnd_hk);

			//new Thread(SetUpHooks).Start();
			new Thread(() => new Overlay("AssaultCube").ShowDialog()).Start();

			
			Console.ReadLine();
		}

		public static void glEnd_hk()
		{
			// Draw here

			// Lets draw a line?
			//o.glVertex3f(100, 100, 100);
			//o.glVertex3f(200, 200, 200);

			o.glEndHook.UnmodifiedOriginalFunction();
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
