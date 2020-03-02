using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using static TestInject.Memory;



namespace TestInject
{
	public class MyLibrary
	{
		public static unsafe CSGO.Delegates.EndSceneDelegate OrigFunction;
		public static unsafe RegisterStates endSceneRegisterStates;

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

			var ptrEndSceneFunc = CSGO.Methods.DX9GetVTablePointerAtIndex(42);

			if (ptrEndSceneFunc != null)
			{
				endSceneRegisterStates = new RegisterStates();
				endSceneRegisterStates.GeneratePushAd();
				endSceneRegisterStates.GeneratePopAd();

				CSGO.Delegates.EndSceneDelegate myHook = new CSGO.Delegates.EndSceneDelegate(EndScene_hk);
				var gc = GCHandle.Alloc(myHook, GCHandleType.Normal);

				void* OrigFunctionAddr = Hook(new IntPtr(*ptrEndSceneFunc), myHook, endSceneRegisterStates, 7);
				OrigFunction = Marshal.GetDelegateForFunctionPointer<CSGO.Delegates.EndSceneDelegate>((IntPtr)OrigFunctionAddr);

				int x = 1;
			}

			new Thread(SetUpHooks).Start();
			new Thread(() => new Overlay("Counter-Strike: Global Offensive").ShowDialog()).Start();

			Console.ReadLine();
		}

		public static unsafe void* Hook(IntPtr target, Delegate hkMethod, RegisterStates state ,int byteCountOverWrite = 5)
		{
			Protection.SetPageProtection(target, byteCountOverWrite, Memory.Enums.MemoryProtection.ExecuteReadWrite, out var old);

			byte[] origBytes = Reader.ReadBytes(target, (uint)byteCountOverWrite);
			IntPtr hkAddress = Marshal.GetFunctionPointerForDelegate(hkMethod);

			IntPtr middleman = Allocator.Unmanaged.Allocate(5 + (uint)byteCountOverWrite + 5 + (uint)(state.PopAd.Length + state.PushAd.Length));
			IntPtr origFunc = Allocator.Unmanaged.Allocate(5 + (uint)byteCountOverWrite);

			List<byte> TargetToMiddleman = new List<byte>();
			TargetToMiddleman.Add(0xE9);
			TargetToMiddleman.AddRange(BitConverter.GetBytes(HelperMethods.CalculateRelativeAddressForJmp((uint)target.ToInt32(), (uint)middleman.ToInt32())));

			for (int n = 0; n < byteCountOverWrite - 5; n++)
				TargetToMiddleman.Add(0x90);

			List<byte> MiddleManToHookBytes = new List<byte>();
			MiddleManToHookBytes.AddRange(state.PushAd); // pushad alternative

			MiddleManToHookBytes.Add(0xE8); // call hook
			MiddleManToHookBytes.AddRange(BitConverter.GetBytes(HelperMethods.CalculateRelativeAddressForJmp((uint)middleman.ToInt32() + (uint)state.PushAd.Length, (uint)hkAddress.ToInt32())));

			MiddleManToHookBytes.AddRange(state.PopAd); // popad alternative
			MiddleManToHookBytes.AddRange(origBytes);

			MiddleManToHookBytes.Add(0xE9); // jmp execution to original
			MiddleManToHookBytes.AddRange(BitConverter.GetBytes(HelperMethods.CalculateRelativeAddressForJmp((uint)(middleman.ToInt32() + MiddleManToHookBytes.Count -1), (uint)(target.ToInt32() + byteCountOverWrite))));

			Writer.WriteBytes(middleman, MiddleManToHookBytes.ToArray());

			List<byte> OrigFuncBytes = new List<byte>();
			OrigFuncBytes.AddRange(origBytes);
			OrigFuncBytes.Add(0xE9);
			OrigFuncBytes.AddRange(BitConverter.GetBytes(HelperMethods.CalculateRelativeAddressForJmp((uint)(origFunc.ToInt32() + byteCountOverWrite), (uint)(target.ToInt32() + byteCountOverWrite))));

			Writer.WriteBytes(origFunc, OrigFuncBytes.ToArray());

			Writer.WriteBytes(target, TargetToMiddleman.ToArray());
			Protection.SetPageProtection(target, byteCountOverWrite, old, out _);

			// Create original func
			return origFunc.ToPointer();// Cast this to desired delegate type;
		}

		public unsafe class RegisterStates
		{
			// Manually saving values of all general purpose registers
			// Instead of using PushAD

			public IntPtr BaseAddress;

			private byte[] _pushAd;
			private byte[] _popAd;

			private byte[] _pushFd;
			private byte[] _popFd;

			public byte[] PushAd => _pushAd;
			public byte[] PopAd => _popAd;
			public Structures.Registers* RegisterStructPointer { get; private set; }

			public RegisterStates()
			{
				BaseAddress = Allocator.Unmanaged.Allocate((uint)Marshal.SizeOf<Structures.Registers>(), 
					Enums.AllocationType.Commit |Enums.AllocationType.Reserve, 
					Enums.MemoryProtection.ReadWrite);
				if (BaseAddress == IntPtr.Zero)
					throw new InvalidOperationException();

				RegisterStructPointer = (Structures.Registers*) BaseAddress;
			}

			// Pushad
			public byte[] GeneratePushAd()
			{
				if (_pushAd != null && _pushAd.Length > 0)
					return _pushAd;

				// Generate mnemonics here

				bool assembleResult = Assembler.AssembleMnemonics(new[]
				{
					$"mov dword ptr [0x{BaseAddress.ToInt32():X8}], eax",
					$"mov dword ptr [0x{BaseAddress.ToInt32() + 4:X8}], ebx",
					$"mov dword ptr [0x{BaseAddress.ToInt32() + 8:X8}], ecx",
					$"mov dword ptr [0x{BaseAddress.ToInt32() + 12:X8}], edx",
					$"mov dword ptr [0x{BaseAddress.ToInt32() + 16:X8}], edi",
					$"mov dword ptr [0x{BaseAddress.ToInt32() + 20:X8}], esi",
					$"mov dword ptr [0x{BaseAddress.ToInt32() + 24:X8}], ebp",
					$"mov dword ptr [0x{BaseAddress.ToInt32() + 28:X8}], esp"
				}, true, out byte[] assembled);

				if (assembleResult)
				{
					_pushAd = assembled;
					return _pushAd;
				}

				throw new InvalidOperationException();
			}

			// Popad
			public byte[] GeneratePopAd()
			{
				if (_popAd != null)
					return _popAd;

				bool assembleResult = Assembler.AssembleMnemonics(new[]
				{
					/*
					$"mov dword ptr [eax], 0x{BaseAddress.ToInt32():X8}",
					$"mov dword ptr [ebx], 0x{BaseAddress.ToInt32() + 4:X8}",
					$"mov dword ptr [ecx], 0x{BaseAddress.ToInt32() + 8:X8}",
					$"mov dword ptr [edx], 0x{BaseAddress.ToInt32() + 12:X8}",
					$"mov dword ptr [edi], 0x{BaseAddress.ToInt32() + 16:X8}",
					$"mov dword ptr [esi], 0x{BaseAddress.ToInt32() + 20:X8}",
					$"mov dword ptr [ebp], 0x{BaseAddress.ToInt32() + 24:X8}",
					$"mov dword ptr [esp], 0x{BaseAddress.ToInt32() + 28:X8}",
					*/

					$"mov eax, [0x{BaseAddress.ToInt32():X8}]",
					$"mov ebx, [0x{BaseAddress.ToInt32() + 4:X8}]",
					$"mov ecx, [0x{BaseAddress.ToInt32() + 8:X8}]",
					$"mov edx, [0x{BaseAddress.ToInt32() + 12:X8}]",
					$"mov edi, [0x{BaseAddress.ToInt32() + 16:X8}]",
					$"mov esi, [0x{BaseAddress.ToInt32() + 20:X8}]",
					$"mov ebp, [0x{BaseAddress.ToInt32() + 24:X8}]",
					$"mov esp, [0x{BaseAddress.ToInt32() + 28:X8}]",
				}, true, out byte[] assembled);
				// Generate mnemonics here

				if (assembleResult)
				{
					_popAd = assembled;
					return _popAd;
				}

				throw new InvalidOperationException();
			}

			// pushfd
			public byte[] GeneratePushFd()
			{
				return null;
			}

			// popfd
			public byte[] GeneratePopFd()
			{
				return null;
			}
		}

		public static unsafe void EndScene_hk(IntPtr pDevice)
		{
			Console.WriteLine($"[{DateTime.Now.ToLongTimeString()}] Inside EndScene\n" +
			                  $"	pDevice:  0x{(uint)pDevice:X8}");

		}

		public static void SetUpHooks()
		{
			// Set up hooks here

			Console.WriteLine($"Thread 'SetUpHooks()' finished execution!");
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
