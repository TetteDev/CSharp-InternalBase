using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Cache;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Windows.Forms;
using static TestInject.HelperMethods;

namespace TestInject
{
	public class Memory
	{
		public const string MODULE_NAME = "MyLibrary.dll";
		public static Process HostProcess;
		public static ProcessModule OurModule;

		static Memory(/* string moduleName */)
		{
			HostProcess = Process.GetCurrentProcess();
			/* MODULE_NAME = moduleName */
			OurModule = HostProcess?.FindProcessModule(MODULE_NAME);
		}

		public static void UpdateProcessInformation()
		{
			HostProcess = Process.GetCurrentProcess();
			OurModule = HostProcess?.FindProcessModule(MODULE_NAME);
		}

		public class Reader
		{
			public static unsafe byte[] ReadBytes(IntPtr location, uint numBytes)
			{
				byte[] buff = new byte[numBytes];

				fixed (void* bufferPtr = buff)
				{
					Unsafe.CopyBlockUnaligned(bufferPtr, (void*)location, numBytes);
					return buff;
				}
			}

			public static unsafe T Read<T>(IntPtr location)
				=> Unsafe.Read<T>(location.ToPointer());

			public static string ReadString(IntPtr location, Encoding encodingType, int maxLength = 256)
			{
				var data = ReadBytes(location, (uint)maxLength);
				var text = new string(encodingType.GetChars(data));
				if (text.Contains("\0"))
					text = text.Substring(0, text.IndexOf('\0'));
				return text;
			}

			public static unsafe void CopyBytesToLocation(IntPtr location, IntPtr targetLocation, int numBytes)
			{
				for(int offset = 0; offset < numBytes; offset++)
					*(byte*)(location.ToInt32() + offset) = *(byte*)(targetLocation.ToInt32() + offset);
			}
		}

		public class Writer
		{
			public static unsafe void WriteBytes(IntPtr location, byte[] buffer)
			{
				if (location == IntPtr.Zero) return;
				if (buffer == null || buffer.Length < 1) return;

				var ptr = (void*)location;
				fixed (void* pBuff = buffer)
				{
					Unsafe.CopyBlockUnaligned(ptr, pBuff, (uint)buffer.Length);
				}
			}

			public static unsafe void Write<T>(IntPtr location, T value) 
				=> Unsafe.Write(location.ToPointer(), value);

			public static void WriteString(IntPtr location, string str, Encoding encodingType)
			{
				byte[] bytes = encodingType.GetBytes(str);
				WriteBytes(location, bytes);
			}

			public static unsafe void CopyBytesToLocation(IntPtr location, IntPtr targetLocation, int numBytes)
			{
				for (int offset = 0; offset < numBytes; offset++)
					*(byte*)(location.ToInt32() + offset) = *(byte*)(targetLocation.ToInt32() + offset);
			}
		}

		public class Detours
		{
			public unsafe class CodeExecutionCallback
			{
				public readonly IntPtr InstallLocation;
				public bool IsInstalled { get; private set; }

				private readonly int _count;
				private byte[] _orig;
				private readonly IntPtr _func;
				private readonly GCHandle _funcGC;
				private readonly Structures.RegisterStates _registerstates;
				private IntPtr _region;

				public CodeExecutionCallback(IntPtr location, Structures.CallbackDelegate callback, int numBytesOverwrite = 5)
				{
					if (location == IntPtr.Zero)
						throw new InvalidOperationException("");

					if (numBytesOverwrite < 5)
						throw new InvalidOperationException("");


					InstallLocation = location;
					_count = numBytesOverwrite;
					_func = Marshal.GetFunctionPointerForDelegate(callback);
					_funcGC = GCHandle.Alloc(_func);

					_registerstates = new Structures.RegisterStates();
					_registerstates.GeneratePushAd();
					_registerstates.GeneratePopAd();
					_registerstates.GeneratePushFd();
					_registerstates.GeneratePopFd();
				}

				public void Install(bool verbose = false)
				{
					if (IsInstalled)
						return;

					if (_orig == null)
						_orig = Reader.ReadBytes(InstallLocation, (uint) _count);

					if (verbose) Console.WriteLine($"Read {_orig.Length} bytes from 0x{InstallLocation.ToInt32():X8}");


					if (_region == IntPtr.Zero)
					{
						_region = Allocator.Unmanaged.Allocate((uint)_registerstates.PushAdBytes.Length
						                                       + (uint)_registerstates.PushFdBytes.Length
						                                       + (uint)_registerstates.PopAdBytes.Length
						                                       + (uint)_registerstates.PopFdBytes.Length
						                                       + 5 /* jmp to original code */);

						if (_region == IntPtr.Zero)
						{
							if (verbose) Console.WriteLine($"Failed allocating a new region for callback");
							throw new InvalidOperationException();
						}

						if (verbose) Console.WriteLine($"Allocated a new region - 0x{_region.ToInt32():X8}");
					}
					else
						if (verbose) Console.WriteLine($"Using old region for callback - 0x{_region.ToInt32():X8}");


					Writer.WriteBytes(_region, _orig);
					if (verbose) Console.WriteLine($"Wrote original bytes at address 0x{_region.ToInt32():X8}");
					uint start = (uint)_region + (uint) _orig.Length;


					Writer.WriteBytes((IntPtr) start, _registerstates.PushFdBytes);
					if (verbose) Console.WriteLine($"Wrote PushFD bytes at address 0x{start:X8}");
					start += (uint) _registerstates.PushFdBytes.Length;
					
					Writer.WriteBytes((IntPtr) start, _registerstates.PushAdBytes);
					if (verbose) Console.WriteLine($"Wrote PushAd bytes at address 0x{start:X8}");
					start += (uint) _registerstates.PushAdBytes.Length;
					
					Assembler.AssembleMnemonics(new []
					{
						$"push 0x{(_registerstates.BaseAddress).ToInt32():X8}",
					}, true, out byte[] push);

					Writer.WriteBytes((IntPtr)start, push);
					if (verbose) Console.WriteLine($"Wrote parameter push to callback at address 0x{start:X8}");
					start += (uint) push.Length;
					
					*(byte*) start = 0xE8;
					*(uint*) (start + 1) = RelAddr(start, (uint) _func); // call to callback
					if (verbose) Console.WriteLine($"Wrote call to C# callback at address 0x{start:X8}");
					start += 5;

					Writer.WriteBytes((IntPtr)start, _registerstates.PopFdBytes);
					if (verbose) Console.WriteLine($"Wrote PopFD bytes at address 0x{start:X8}");
					start += (uint)_registerstates.PopFdBytes.Length;

					Writer.WriteBytes((IntPtr)start, _registerstates.PopAdBytes);
					if (verbose) Console.WriteLine($"Wrote PopAD bytes at address 0x{start:X8}");
					start += (uint) _registerstates.PopAdBytes.Length;

					*(byte*)start = 0xE9;
					*(uint*) (start + 1) = RelAddr(start, (uint)InstallLocation + (uint)_count);
					if (verbose) Console.WriteLine($"Wrote jmp from middleman region to start at address 0x{InstallLocation.ToInt32():X8}");

					if (!Protection.SetPageProtection(InstallLocation, _count, Enums.MemoryProtection.ExecuteReadWrite, out var old))
					{
						Allocator.Unmanaged.FreeMemory(_region);
						if (verbose) Console.WriteLine($"Failed changing Page Protection of address 0x{InstallLocation.ToInt32():X8} to {Enums.MemoryProtection.ExecuteReadWrite.ToString()}, returning...");
						return;
					}

					if (verbose) Console.WriteLine($"Changed PageProtection of address 0x{InstallLocation.ToInt32():X8} to {Enums.MemoryProtection.ExecuteReadWrite.ToString()}");

					*(byte*)InstallLocation = 0xE9;
					*(uint*)(InstallLocation + 1) = RelAddr((uint)InstallLocation, (uint)_region);

					for (int n = 0; n < _count - 5; n++)
						*(byte*)(InstallLocation + 5 + n) = 0x90;
					if (verbose) Console.WriteLine($"Wrote {_count - 5} NOPs to address 0x{(InstallLocation + 5).ToInt32():X8}");

					Protection.SetPageProtection(InstallLocation, _count, old, out _);
					if (verbose) Console.WriteLine($"Restored Page Protection at adress 0x{InstallLocation.ToInt32():X8} to {old.ToString()}");

					IsInstalled = true;
				}

				public void Uninstall()
				{
					if (!IsInstalled)
						return;

					Protection.SetPageProtection(InstallLocation, _count, Enums.MemoryProtection.ExecuteReadWrite, out var old);
					Writer.WriteBytes(InstallLocation, _orig);
					Protection.SetPageProtection(InstallLocation, _count, old, out _);

					IsInstalled = false;
				}
			}

			public unsafe class BasicHook<T> where T : Delegate
			{
				public bool IsInstalled;

				public readonly IntPtr InstalledAt;
				private readonly IntPtr dwRegistersBaseAddress;
				public readonly Structures.RegisterStates Registers;

				public T Original { get; private set; }
				private GCHandle _cleanOriginalHandle;

				private readonly int _overwriteCount;
				
				private readonly Delegate _hkMethod;
				private readonly IntPtr _hkMethodAddress;

				private readonly byte[] _origBytes;
				private readonly IntPtr _middleman;

				public BasicHook(IntPtr installLocation, Delegate hkMethod, int byteOverwriteCount = 5)
				{
					if (installLocation == IntPtr.Zero || byteOverwriteCount < 5)
						throw new Exception("ERROR");

					InstalledAt = installLocation;
					_overwriteCount = byteOverwriteCount;
					_origBytes = Reader.ReadBytes(InstalledAt, (uint) _overwriteCount);

					Registers = new Structures.RegisterStates();

					Registers.GeneratePushAd(true);
					Registers.GeneratePushFd(true);

					Registers.GeneratePopAd(true);
					Registers.GeneratePopFd(true);
					dwRegistersBaseAddress = Registers.BaseAddress;

					_hkMethod = hkMethod;
					_hkMethodAddress = Marshal.GetFunctionPointerForDelegate(_hkMethod);

					_middleman = Allocator.Managed.ManagedAllocate(12, Enums.MemoryProtection.ExecuteReadWrite);
					if (_middleman == IntPtr.Zero)
					{
						_middleman = Allocator.Unmanaged.Allocate(12);
						if (_middleman == IntPtr.Zero)
							throw new Exception($"Allocation for middleman region failed");
					}

					Original = Marshal.GetDelegateForFunctionPointer<T>(InstalledAt);
					_cleanOriginalHandle = GCHandle.Alloc(Original, GCHandleType.Normal);
				}

				public void Install()
				{
					if (IsInstalled)
						return;

					int offset = 0;

					#region Fix Clean Unhooked Function
					IntPtr clean = Allocator.Unmanaged.Allocate(0x10000);
					if (clean == IntPtr.Zero)
						throw new Exception("ERROR");

					Writer.WriteBytes(clean, Registers.PopAdBytes);
					offset += Registers.PopAdBytes.Length;

					Writer.WriteBytes(clean + offset, Registers.PopFdBytes);
					offset += Registers.PopFdBytes.Length;

					Writer.WriteBytes(clean + offset, _origBytes);
					offset += _origBytes.Length;

					*(byte*) (clean + offset) = 0xE9;
					offset++;
					*(uint*) (clean + offset) = RelAddr((uint) (clean + (offset - 1)), (uint) (InstalledAt +  _overwriteCount));

					offset += 4;
					*(byte*) (clean + offset) = 0xC3;
					offset += 1;

					Protection.SetPageProtection(clean, _origBytes.Length + 5, Enums.MemoryProtection.ExecuteRead, out _);
					Original = Marshal.GetDelegateForFunctionPointer<T>(clean);
					if (_cleanOriginalHandle.IsAllocated)
						_cleanOriginalHandle.Free();

					_cleanOriginalHandle = GCHandle.Alloc(Original, GCHandleType.Normal);
					Console.WriteLine($"Clean Function Address: 0x{clean.ToInt32():X8}");
					offset = 0;
					#endregion

					#region Fix Jmp To Hook with Restore States

					IntPtr fixedJmpToHook = Allocator.Unmanaged.Allocate(0x10000);

					*(byte*) (fixedJmpToHook) = 0xE8;
					*(uint*) (fixedJmpToHook + 1) = RelAddr((uint)fixedJmpToHook, (uint)_hkMethodAddress);
					offset += 5;

					*(byte*)(fixedJmpToHook + offset) = 0xE9;
					*(uint*) (fixedJmpToHook + offset + 1) = RelAddr((uint) (fixedJmpToHook + offset), 
						(uint) (_middleman 
						        + _origBytes.Length
						        + Registers.PushAdBytes.Length
						        + Registers.PushFdBytes.Length
						        + 5));

					Console.WriteLine($"Fixed Jmp to Hook Address: 0x{fixedJmpToHook.ToInt32():X8}");

					offset = 0;
					#endregion

					Writer.WriteBytes(_middleman, _origBytes);
					offset += _origBytes.Length;

					Writer.WriteBytes(_middleman + offset, Registers.PushAdBytes);
					offset += Registers.PushAdBytes.Length;

					Writer.WriteBytes(_middleman + offset, Registers.PushFdBytes);
					offset += Registers.PushFdBytes.Length;

					*(byte*) (_middleman + offset) = 0xE9; // JMP
					*(uint*) (_middleman + offset + 1) = RelAddr((uint) (_middleman + offset), (uint) fixedJmpToHook);
					offset += 5;

					Writer.WriteBytes(_middleman + offset, Registers.PopAdBytes);
					offset += Registers.PopAdBytes.Length;

					Writer.WriteBytes(_middleman + offset, Registers.PopFdBytes);
					offset += Registers.PopFdBytes.Length;

					Writer.WriteBytes(_middleman + offset, new byte[] { 0xC3 });
					//MessageBox.Show($"Middleman Size: {(offset + 1)} bytes");

					offset = 0;
					Console.WriteLine($"Middleman: 0x{_middleman.ToInt32():X8}");

					Protection.SetPageProtection(InstalledAt, _overwriteCount, Enums.MemoryProtection.ExecuteReadWrite, out var old);
					*(byte*) (InstalledAt) = 0xE9;
					*(uint*) (InstalledAt + 1) = RelAddr((uint)InstalledAt, (uint) (_middleman));

					offset += 5;

					for (int n = 0; n < _overwriteCount - 5; n++)
						*(byte*)(InstalledAt + (offset + n)) = 0x90;

					Protection.SetPageProtection(InstalledAt, _overwriteCount, old, out _);

					IsInstalled = true;
				}

				public void Uninstall()
				{
					throw new NotImplementedException($"Unhooking is not implemented");
				}
			}

			public unsafe class Hk<T>
			{
				public T ContinueExecution { get; private set; }

				public Structures.RegisterStates Registers { get; private set; }
				public bool IsInstalled => backingIsInstalled;
				public readonly uint InstalledAt;

				#region Private Fields/Variables
				private readonly uint numBytes = 5;

				private readonly uint hkMethodAddress;
				private readonly GCHandle hkMethodGCHandle;

				private readonly byte[] overwrittenBytes;

				private readonly uint middlemanRegion;
				private uint unhookedOriginal;

				private bool backingIsInstalled = false;

				// for verbose
				private bool v = false;
				#endregion

				public Hk(uint InstallLocation, T hkMethod, uint PrologueByteLengthOverwrite = 5, bool verbose = false)
				{
					v = verbose;

					Stopwatch ts = v ? Stopwatch.StartNew() : null;
					if (InstallLocation == 0)
						throw new InvalidOperationException($"Parameter 'InstallLocation' cannot be zero");

					if (PrologueByteLengthOverwrite < 5)
						throw new InvalidOperationException($"Parameter 'PrologueByteLengthOverwrite' cannot be less than 5 bytes");

					InstalledAt = InstallLocation;
					numBytes = PrologueByteLengthOverwrite;

					overwrittenBytes = Reader.ReadBytes(new IntPtr(InstalledAt), numBytes);
					if (v) Console.WriteLine($"Read and stored {numBytes} bytes from 0x{InstalledAt:X8} (Original Overwriten Bytes)");

					Registers = new Structures.RegisterStates();
					if (Registers.BaseAddress == IntPtr.Zero)
						throw new InsufficientMemoryException($"Failed allocating for struct 'RegisterStates'");

					if (v) Console.WriteLine($"RegisterStates Base Address: 0x{Registers.BaseAddress.ToInt32():X8}");

					middlemanRegion = (uint)Allocator.Unmanaged.Allocate(0x10000);
					if (middlemanRegion == 0)
					{
						Allocator.Unmanaged.FreeMemory(Registers.BaseAddress);
						throw new InsufficientMemoryException($"Failed allocating for field 'middlemanRegion'");
					}

					if (v) Console.WriteLine($"Middleman Base Address: 0x{middlemanRegion:X8}");

					// This is the slow part
					Registers.GeneratePushAd(true);
					Registers.GeneratePushFd(true);
					Registers.GeneratePopAd(true);
					Registers.GeneratePopFd(true);
					if (v) Console.WriteLine($"Generation of PushAd/PushFd && PopAd/PopFd completed!");

					hkMethodAddress = (uint)Marshal.GetFunctionPointerForDelegate(hkMethod);
					hkMethodGCHandle = GCHandle.Alloc(hkMethodAddress, GCHandleType.Normal);
					if (v) Console.WriteLine($"Hook Method Address: 0x{hkMethodAddress:X8}");

					ContinueExecution = Marshal.GetDelegateForFunctionPointer<T>(new IntPtr(InstalledAt));

					if (v) Console.WriteLine($"Initialization phase took {ts.ElapsedMilliseconds} ms");
				}

				public void Install()
				{
					if (backingIsInstalled)
						return;

					int offset = 0;

					#region Middleman -> (Target + Num Bytes) & Middleman -> HookMethod
					// Original Bytes
					Writer.WriteBytes((IntPtr)middlemanRegion + offset, overwrittenBytes); offset += overwrittenBytes.Length;

					// PushAD/PushFD
					Writer.WriteBytes((IntPtr)middlemanRegion + offset, Registers.PushAdBytes); offset += Registers.PushAdBytes.Length;
					Writer.WriteBytes((IntPtr)middlemanRegion + offset, Registers.PushFdBytes); offset += Registers.PushFdBytes.Length;

					// Call/Jmp to hook
					*(byte*)(middlemanRegion + offset) = 0xE9;
					*(uint*) (middlemanRegion + offset + 1) = RelAddr((uint)(middlemanRegion + offset), hkMethodAddress); offset += 5;

					// Pop AD
					Writer.WriteBytes((IntPtr)middlemanRegion + offset, Registers.PopAdBytes); offset += Registers.PopAdBytes.Length;

					// PopFD
					Writer.WriteBytes((IntPtr)middlemanRegion + offset, Registers.PopFdBytes); offset += Registers.PopFdBytes.Length;

					// Jmp back to original code
					*(byte*) (middlemanRegion + offset) = 0xE9;
					*(uint*) (middlemanRegion + offset + 1) = RelAddr((uint) (middlemanRegion + offset), InstalledAt + numBytes);

					if (v) Console.WriteLine($"Middleman Region: 0x{middlemanRegion:X8}");
					offset = 0;
					#endregion

					#region Creating unmodified function delegate
					if (unhookedOriginal == 0)
					{
						unhookedOriginal = (uint)Allocator.Unmanaged.Allocate(0x10000);
						if (unhookedOriginal == 0)
						{
							if (middlemanRegion == 0)
								Allocator.Unmanaged.FreeMemory((IntPtr) middlemanRegion);
							throw new InvalidOperationException("err");
						}
					}

					Writer.WriteBytes((IntPtr)unhookedOriginal + offset, Registers.PopAdBytes); offset += Registers.PopAdBytes.Length;
					Writer.WriteBytes((IntPtr)unhookedOriginal + offset, Registers.PopFdBytes); offset += Registers.PopFdBytes.Length;

					Writer.WriteBytes((IntPtr)unhookedOriginal + offset, overwrittenBytes); offset += overwrittenBytes.Length;

					*(byte*) (unhookedOriginal + offset) = 0xE9;
					*(uint*) (unhookedOriginal + offset + 1) = RelAddr((uint)(unhookedOriginal + offset), InstalledAt + numBytes); offset += 5;

					*(byte*) (unhookedOriginal + offset) = 0xC3; offset += 1;

					ContinueExecution = Marshal.GetDelegateForFunctionPointer<T>((IntPtr)unhookedOriginal);
					if (v) Console.WriteLine($"Unhooked Function: 0x{unhookedOriginal:X8}");
					offset = 0;
					#endregion

					#region Target -> Middleman
					Protection.SetPageProtection(new IntPtr(InstalledAt), (int)numBytes, Enums.MemoryProtection.ExecuteReadWrite, out var old);
					*(byte*)(InstalledAt + offset) = 0xE9; offset++;
					*(uint*)(InstalledAt + offset) = RelAddr(InstalledAt, middlemanRegion); offset += 4;

					for (int n = 0; n < numBytes - 5; n++)
						*(byte*)((InstalledAt + offset) + n) = 0x90;

					Protection.SetPageProtection(new IntPtr(InstalledAt), (int)numBytes, old, out _);
					offset = 0;
					#endregion

					backingIsInstalled = true;
				}
				public void Uninstall()
				{
					if (!backingIsInstalled)
						return;

					int offset = 0;

					Protection.SetPageProtection(new IntPtr(InstalledAt), (int)numBytes, Enums.MemoryProtection.ExecuteReadWrite, out var old);
					Writer.WriteBytes((IntPtr)InstalledAt, overwrittenBytes); offset += overwrittenBytes.Length;
					Protection.SetPageProtection(new IntPtr(InstalledAt), (int)numBytes, old, out _);

					backingIsInstalled = false;
				}
			}


			public unsafe class HookEx<TDelegate>
			{
				private readonly IntPtr _target;
				private readonly int _len;

				private readonly Delegate _hook;
				private readonly IntPtr _hookAddress;

				private readonly Structures.RegisterStates _reg;

				public TDelegate Original { get; private set; }
				public bool IsInstalled { get; private set; } = false;

				public HookEx(IntPtr target, Delegate hook, int length)
				{
					if (target == IntPtr.Zero || hook == null || length < 5)
						throw new InvalidOperationException();

					_target = target;
					_len = length;
					_hook = hook;
					_hookAddress = Marshal.GetFunctionPointerForDelegate(_hook);

					Original = Marshal.GetDelegateForFunctionPointer<TDelegate>(target);

					_reg = new Structures.RegisterStates();
				}

				public bool Install()
				{
					if (!Protection.SetPageProtection(_target, _len, Enums.MemoryProtection.ExecuteReadWrite, out var old))
					{
						Console.WriteLine($"Failed setting to RWX");
						return false;
					}

					List<byte> overwrittenBytes = new List<byte>();
					for (int i = 0; i < _len; i++)
					{
						overwrittenBytes.Add(*(byte*)(_target.ToInt32() + i));
						*(byte*) (_target.ToInt32() + i) = 0x90;
					}

					*(byte*) (_target.ToInt32()) = 0xE9;
					*(uint*)(_target.ToInt32() + 1) = RelAddr((uint)_target.ToInt32(), (uint)_hookAddress);

					IntPtr origFunction = Allocator.Managed.ManagedAllocate(overwrittenBytes.Count + 5);
					if (origFunction == IntPtr.Zero)
						return false;

					for (var i = 0; i < overwrittenBytes.Count; i++)
						*(byte*)(origFunction.ToInt32() + i) = overwrittenBytes[i];

					int offset = overwrittenBytes.Count;
					*(byte*) (origFunction.ToInt32() + offset) = 0xE9;
					offset++;
					*(uint*)(origFunction.ToInt32() + offset) = RelAddr((uint)origFunction.ToInt32(), (uint)(_target.ToInt32() + _len));
					Original = Marshal.GetDelegateForFunctionPointer<TDelegate>(origFunction);

					Protection.SetPageProtection(_target, _len, old, out _);

					IsInstalled = true;
					return true;
				}
			}
		}

		public class Pattern
		{
			public static unsafe ulong FindPatternExecutable(string processModule, string pattern, bool resultAbsolute = true)
			{
				UpdateProcessInformation();

				var tmpSplitPattern = pattern.TrimStart(' ').TrimEnd(' ').Split(' ');
				var tmpPattern = new byte[tmpSplitPattern.Length];
				var tmpMask = new byte[tmpSplitPattern.Length];

				for (var i = 0; i < tmpSplitPattern.Length; i++)
				{
					var ba = tmpSplitPattern[i];

					if (ba == "??" || ba.Length == 1 && ba == "?")
					{
						tmpMask[i] = 0x00;
						tmpSplitPattern[i] = "0x00";
					}
					else if (char.IsLetterOrDigit(ba[0]) && ba[1] == '?')
					{
						tmpMask[i] = 0xF0;
						tmpSplitPattern[i] = ba[0] + "0";
					}
					else if (char.IsLetterOrDigit(ba[1]) && ba[0] == '?')
					{
						tmpMask[i] = 0x0F;
						tmpSplitPattern[i] = "0" + ba[1];
					}
					else
					{
						tmpMask[i] = 0xFF;
					}
				}

				for (var i = 0; i < tmpSplitPattern.Length; i++)
					tmpPattern[i] = (byte)(Convert.ToByte(tmpSplitPattern[i], 16) & tmpMask[i]);

				if (tmpMask.Length != tmpPattern.Length)
					throw new ArgumentException($"{nameof(pattern)}.Length != {nameof(tmpMask)}.Length");

				if (string.IsNullOrEmpty(processModule))
				{
					// do shut
					return 0;
				}

				ProcessModule pm = HostProcess.Modules.Cast<ProcessModule>().FirstOrDefault(x => string.Equals(x.ModuleName, processModule, StringComparison.CurrentCultureIgnoreCase));
				if (pm == null)
					return 0;

				byte[] buffer = new byte[pm.ModuleMemorySize];
				try
				{
					buffer = Reader.ReadBytes(pm.BaseAddress, (uint)pm.ModuleMemorySize);
				}
				catch
				{
					Console.WriteLine($"ReadBytes(location: 0x{pm.BaseAddress.ToInt32():X8}, numBytes: {buffer.Length}) failed ...");
					return 0;
				}

				if (buffer == null || buffer.Length < 1) return 0;

				long result = 0 - tmpPattern.LongLength;
				fixed (byte* pPacketBuffer = buffer)
				{
					do
					{
						result = HelperMethods.FindPattern(pPacketBuffer, buffer.Length, tmpPattern, tmpMask, result + tmpPattern.LongLength);
						if (result >= 0)
							return resultAbsolute ? (ulong)pm.BaseAddress.ToInt64() + (ulong)result : (ulong)result;
					} while (result != -1);
				}
				return 0;
			}

			public static unsafe List<long> FindPattern(string pattern, string? optionalModuleName, bool readable, bool writable, bool executable)
			{
				#region Creation of Byte Array from string pattern
				var tmpSplitPattern = pattern.TrimStart(' ').TrimEnd(' ').Split(' ');
				var tmpPattern = new byte[tmpSplitPattern.Length];
				var tmpMask = new byte[tmpSplitPattern.Length];

				for (var i = 0; i < tmpSplitPattern.Length; i++)
				{
					var ba = tmpSplitPattern[i];

					if (ba == "??" || ba.Length == 1 && ba == "?")
					{
						tmpMask[i] = 0x00;
						tmpSplitPattern[i] = "0x00";
					}
					else if (char.IsLetterOrDigit(ba[0]) && ba[1] == '?')
					{
						tmpMask[i] = 0xF0;
						tmpSplitPattern[i] = ba[0] + "0";
					}
					else if (char.IsLetterOrDigit(ba[1]) && ba[0] == '?')
					{
						tmpMask[i] = 0x0F;
						tmpSplitPattern[i] = "0" + ba[1];
					}
					else
					{
						tmpMask[i] = 0xFF;
					}
				}


				for (var i = 0; i < tmpSplitPattern.Length; i++)
					tmpPattern[i] = (byte)(Convert.ToByte(tmpSplitPattern[i], 16) & tmpMask[i]);

				if (tmpMask.Length != tmpPattern.Length)
					throw new ArgumentException($"{nameof(pattern)}.Length != {nameof(tmpMask)}.Length");
				#endregion

				Structures.SYSTEM_INFO si = new Structures.SYSTEM_INFO();
				PInvoke.GetSystemInfo(ref si);

				ConcurrentBag<(IntPtr RegionBase, IntPtr RegionSize)> regions = new ConcurrentBag<(IntPtr RegionBase, IntPtr RegionSize)>();
				ConcurrentQueue<long> results = new ConcurrentQueue<long>();

				ProcessModule pm = null;
				if (!string.IsNullOrEmpty(optionalModuleName))
				{
					UpdateProcessInformation();
					pm = HostProcess.FindProcessModule(optionalModuleName);
					if (pm == null)
						throw new Exception($"Cannot find module '{optionalModuleName}'");
				}

				uint lpMem = (pm != null && optionalModuleName != "") 
					? (uint)pm.BaseAddress 
					: (uint) si.lpMinimumApplicationAddress;

				if (lpMem < (uint) si.lpMinimumApplicationAddress)
					lpMem = (uint) si.lpMinimumApplicationAddress;

				uint maxAddress = (pm != null && optionalModuleName != ""
					? (uint) (pm.BaseAddress + pm.ModuleMemorySize)
					: (uint) si.lpMaximumApplicationAddress);

				if (maxAddress > (uint) si.lpMaximumApplicationAddress)
					maxAddress = (uint) si.lpMaximumApplicationAddress;

				//uint lpMem = (uint)si.lpMinimumApplicationAddress;

					// while (lpMem < ((uint)si.lpMaximumApplicationAddress))
				while (lpMem < maxAddress)
				{
					if (PInvoke.VirtualQuery((IntPtr)lpMem,
							out Structures.MEMORY_BASIC_INFORMATION lpBuffer,
							(uint)Marshal.SizeOf<Structures.MEMORY_BASIC_INFORMATION>()) != 0)
					{
						var buff = &lpBuffer;

						bool isValid = buff->State == Enums.MemoryState.MEM_COMMIT;
						isValid &= (uint)(buff->BaseAddress) < (uint)si.lpMaximumApplicationAddress;
						isValid &= ((buff->Protect & Enums.MemoryProtection.GuardModifierflag) == 0);
						isValid &= ((buff->Protect & Enums.MemoryProtection.NoAccess) == 0);
						isValid &= (buff->Type == Enums.MemoryType.MEM_PRIVATE) || (buff->Type == Enums.MemoryType.MEM_IMAGE);

						if (isValid)
						{
							bool isReadable = (buff->Protect & Enums.MemoryProtection.ReadOnly) > 0;



							bool isWritable = ((buff->Protect & Enums.MemoryProtection.ReadWrite) > 0) ||
											  ((buff->Protect & Enums.MemoryProtection.WriteCopy) > 0) ||
											  ((buff->Protect & Enums.MemoryProtection.ExecuteReadWrite) > 0) ||
											  ((buff->Protect & Enums.MemoryProtection.ExecuteWriteCopy) > 0);

							bool isExecutable = ((buff->Protect & Enums.MemoryProtection.Execute) > 0) ||
												((buff->Protect & Enums.MemoryProtection.ExecuteRead) > 0) ||
												((buff->Protect & Enums.MemoryProtection.ExecuteReadWrite) > 0) ||
												((buff->Protect & Enums.MemoryProtection.ExecuteWriteCopy) > 0);

							isReadable &= readable;
							isWritable &= writable;
							isExecutable &= executable;

							isValid &= isReadable || isWritable || isExecutable;

							if (isValid)
								regions.Add((buff->BaseAddress, buff->RegionSize));

						}

						lpMem = (uint)buff->BaseAddress + (uint)buff->RegionSize;
					}
				}

				Parallel.ForEach(regions, (currentRegion) =>
				{
					long result = 0 - tmpPattern.LongLength;
					do
					{
						var (regionBase, regionSize) = currentRegion;
						result = HelperMethods.FindPattern((byte*)regionBase, (int)regionSize, tmpPattern, tmpMask, result + tmpPattern.LongLength);
						if (result >= 0)
							results.Enqueue((long)regionBase + result);

					} while (result != -1);
				});

				return results.ToList().OrderBy(address => address).ToList();
			}
		}

		public class Assembler
		{
			public static bool AssembleMnemonics(string[] assemblyCode, bool isx86, out byte[] assembled)
			{
				try
				{
					if (assemblyCode == null || assemblyCode.Length < 1)
					{
						assembled = null;
						return false;
					}

					string instructions = HttpUtility.UrlEncode(string.Join("\\n", assemblyCode), Encoding.UTF8);
					string url = $"http://shell-storm.org/online/Online-Assembler-and-Disassembler/?inst={instructions}&arch={(isx86 ? "x86-32" : "x86-64")}&as_format=inline";

					WebClient req = new WebClient();
					string body = req.DownloadString(url);

					int start = body.IndexOf("<pre>");
					if (start == -1)
					{
						assembled = null;
						return false;
					}

					int end = body.IndexOf("</pre>");
					if (end == -1)
					{
						assembled = null;
						return false;
					}

					string extractedText = new string(body.Skip(start + 5).Take((end - 5) - start).ToArray());
					extractedText = extractedText.Trim('"');

					var bytes = extractedText.Split(new[] {"\\x"}, StringSplitOptions.None);
					List<byte> returnList = new List<byte>();

					foreach (var b in bytes)
					{
						bool result = byte.TryParse(b, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte parsed);
						if (result)
							returnList.Add(parsed);
					}

					assembled = returnList.ToArray();
					return true;
				}
				catch (Exception)
				{
					assembled = null;
					return false;
				}
			}
		}

		public class Allocator
		{
			public class Managed
			{
				public static IntPtr ManagedAllocate(int size, Enums.MemoryProtection flMemProtectType = Enums.MemoryProtection.ExecuteReadWrite)
				{
					IntPtr alloc = Marshal.AllocHGlobal(size);
					if (alloc != IntPtr.Zero)
						Protection.SetPageProtection(alloc, size, flMemProtectType, out var old);

					return alloc;
				}

				public static void ManagedFree(IntPtr address)
					=> Marshal.FreeHGlobal(address);
			}
			public class Unmanaged
			{
				public static IntPtr Allocate(uint size, Enums.AllocationType flAllocType = Enums.AllocationType.Commit | Enums.AllocationType.Reserve, Enums.MemoryProtection flMemProtectType = Enums.MemoryProtection.ExecuteReadWrite)
					=> PInvoke.VirtualAlloc(IntPtr.Zero, new UIntPtr(size), flAllocType, flMemProtectType);

				public static bool FreeMemory(IntPtr address, uint optionalSize = 0)
					=> PInvoke.VirtualFree(address, optionalSize, Enums.FreeType.Release);

				public class Extended
				{
					private static IntPtr _heapObject;

					public static IntPtr AllocateHeap(uint size, uint dwFlags = 0x00000004 | 0x00000008)
					{
						if (_heapObject == IntPtr.Zero)
						{
							_heapObject = PInvoke.HeapCreate(0x00040000 | 0x00000004, new UIntPtr(0),
								new UIntPtr(0));

							if (_heapObject == IntPtr.Zero) throw new Exception($"HeapCreate failed!");
						}
							

						return size < 1 ? IntPtr.Zero : PInvoke.HeapAlloc(_heapObject, dwFlags, (UIntPtr) size);
					}

					public static bool FreeHeap(IntPtr addr, uint dwFlags = 0x00000004 | 0x00000008)
					{
						return _heapObject != IntPtr.Zero && PInvoke.HeapFree(_heapObject, dwFlags, addr);
					}
				}
			}

			public IntPtr FindEmptySpaceInRegion(IntPtr targetRegion, int desiredSize)
			{
				IntPtr minAddress = IntPtr.Subtract(targetRegion, 0x70000000);
				IntPtr maxAddress = IntPtr.Add(targetRegion, 0x70000000);

				IntPtr ret = IntPtr.Zero;
				IntPtr tmpAddress = IntPtr.Zero;

				Structures.SYSTEM_INFO si = new Structures.SYSTEM_INFO();
				PInvoke.GetSystemInfo(ref si);

				if (Environment.Is64BitProcess)
				{
					if ((long)minAddress > (long)si.lpMaximumApplicationAddress ||
					    (long)minAddress < (long)si.lpMinimumApplicationAddress)
						minAddress = si.lpMinimumApplicationAddress;

					if ((long)maxAddress < (long)si.lpMinimumApplicationAddress ||
					    (long)maxAddress > (long)si.lpMaximumApplicationAddress)
						maxAddress = si.lpMaximumApplicationAddress;
				}
				else
				{
					minAddress = si.lpMinimumApplicationAddress;
					maxAddress = si.lpMaximumApplicationAddress;
				}

				IntPtr current = minAddress;
				IntPtr previous = current;

				while (PInvoke.VirtualQuery(current, out var mbi, (uint) Marshal.SizeOf<Structures.MEMORY_BASIC_INFORMATION>()) != 0)
				{
					if ((long) mbi.BaseAddress > (long) maxAddress)
						return IntPtr.Zero; // No memory found, let windows handle

					if (mbi.State == Enums.MemoryState.MEM_FREE && (int) mbi.RegionSize > desiredSize)
					{
						if ((long) mbi.BaseAddress % si.dwAllocationGranularity > 0)
						{
							// The whole size can not be used
							tmpAddress = mbi.BaseAddress;
							int offset = (int) (si.dwAllocationGranularity -
							                    ((long) tmpAddress % si.dwAllocationGranularity));
							// Check if there is enough left
							if ((int) (mbi.RegionSize - offset) >= desiredSize)
							{
								if ((long) tmpAddress < (long)targetRegion)
								{
									tmpAddress = IntPtr.Add(tmpAddress, (int)(mbi.RegionSize - offset - desiredSize));

									if ((long)tmpAddress > (long)targetRegion)
										tmpAddress = targetRegion;

									// decrease tmpAddress until its alligned properly
									tmpAddress = IntPtr.Subtract(tmpAddress, (int)((long)tmpAddress % si.dwAllocationGranularity));
								}

								if (Math.Abs((long)tmpAddress - (long)targetRegion) < Math.Abs((long)ret - (long)targetRegion))
									ret = tmpAddress;
							}
						}
						else
						{
							tmpAddress = mbi.BaseAddress;

							if ((long)tmpAddress < (long)targetRegion) // try to get it the cloest possible 
								// (so to the end of the region - size and
								// aligned by system allocation granularity)
							{
								tmpAddress = IntPtr.Add(tmpAddress, (int)(mbi.RegionSize - desiredSize));

								if ((long)tmpAddress > (long)targetRegion)
									tmpAddress = targetRegion;

								// decrease until aligned properly
								tmpAddress =
									IntPtr.Subtract(tmpAddress, (int)((long)tmpAddress % si.dwAllocationGranularity));
							}

							if (Math.Abs((long)tmpAddress - (long)targetRegion) < Math.Abs((long)ret - (long)targetRegion))
								ret = tmpAddress;
						}
					}

					if ((int)mbi.RegionSize % si.dwAllocationGranularity > 0)
						mbi.RegionSize += (int)(si.dwAllocationGranularity - ((int)mbi.RegionSize % (int)si.dwAllocationGranularity));

					previous = current;
					current = IntPtr.Add(mbi.BaseAddress, (int)mbi.RegionSize);

					if ((long)current > (long)maxAddress)
						return ret;

					if ((long)previous > (long)current)
						return ret; // Overflow

				}

				return IntPtr.Zero;
			}
		}

		public class Protection
		{
			public static bool SetPageProtection(IntPtr baseAddress, int size, Enums.MemoryProtection newProtection, out Enums.MemoryProtection oldProtection)
			{
				bool res = PInvoke.VirtualProtect(baseAddress, size, newProtection, out var oldProtect);
				oldProtection = oldProtect;
				return res;
			}
			public static bool GetPageProtection(IntPtr baseAddress, out Structures.MEMORY_BASIC_INFORMATION pageinfo)
			{
				int res = PInvoke.VirtualQuery(baseAddress,
					out pageinfo, 
					(uint)Marshal.SizeOf<Structures.MEMORY_BASIC_INFORMATION>());
				return res == Marshal.SizeOf<Structures.MEMORY_BASIC_INFORMATION>();
			}
		}

		public class Threads
		{
			public static void SuspendProcess()
			{
				UpdateProcessInformation();

				ProcessModule ourModule = OurModule;
				ProcessModule clrJit = HostProcess.FindProcessModule("clrjit.dll");
				ProcessModule clr = HostProcess.FindProcessModule("clr.dll");

				foreach (ProcessThread pT in HostProcess.Threads)
				{
					if (AddressResidesWithinModule(pT.StartAddress, ourModule, "our module") ||
					    AddressResidesWithinModule(pT.StartAddress, clrJit, "clrJit") ||
					    AddressResidesWithinModule(pT.StartAddress, clr, "clr"))
						continue;

					IntPtr pOpenThread = PInvoke.OpenThread(Enums.ThreadAccess.SUSPEND_RESUME, false, (uint)pT.Id);
					if (pOpenThread == IntPtr.Zero)
						continue;

					PInvoke.SuspendThread(pOpenThread);
					PInvoke.CloseHandle(pOpenThread);
				}
			}
			public static void ResumeProcess()
			{
				if (HostProcess.ProcessName == string.Empty)
					return;

				foreach (ProcessThread pT in HostProcess.Threads)
				{
					IntPtr pOpenThread = PInvoke.OpenThread(Enums.ThreadAccess.SUSPEND_RESUME, false, (uint)pT.Id);

					if (pOpenThread == IntPtr.Zero)
						continue;

					var suspendCount = 0;
					do
					{
						suspendCount = PInvoke.ResumeThread(pOpenThread);
					} while (suspendCount > 0);

					PInvoke.CloseHandle(pOpenThread);
				}
			}

			private static bool AddressResidesWithinModule(IntPtr address, ProcessModule processModule, string moduleDescriptor)
			{
				if (processModule == null)
					throw new Exception($"AddressResidesWithinModule - Module with descriptor '{moduleDescriptor}' was null");

				long modEnd = processModule.BaseAddress.ToInt64() + processModule.ModuleMemorySize;
				return address.ToInt64() >= processModule.BaseAddress.ToInt64() && address.ToInt64() <= modEnd;
			}
		}

		public class Modules
		{
			public static IntPtr GetModuleBaseAddress(string moduleName)
			{
				UpdateProcessInformation();
				foreach (ProcessModule pm in HostProcess.Modules)
					if (string.Equals(pm.ModuleName, moduleName, StringComparison.CurrentCultureIgnoreCase))
						return pm.BaseAddress;
				return PInvoke.GetModuleHandle(moduleName);
			}
		}

		public class Structures
		{
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
			public unsafe delegate void CallbackDelegate(IntPtr addrRegisterStruct);

			public unsafe class RegisterStates
			{
				// Manually saving values of all general purpose registers and EFLAGS
				// Instead of using PushAD/PushFD (and PopAD & PopFD)

				public IntPtr BaseAddress;

				public byte[] PushAdBytes { get; private set; }
				public byte[] PopAdBytes { get; private set; }
				public byte[] PopFdBytes { get; private set; }
				public byte[] PushFdBytes { get; private set; }

				public Registers* RegisterStructPointer { get; private set; }

				public RegisterStates()
				{
					BaseAddress = Allocator.Unmanaged.Allocate((uint)Marshal.SizeOf<Structures.Registers>(),
						Enums.AllocationType.Commit | Enums.AllocationType.Reserve,
						Enums.MemoryProtection.ReadWrite);
					if (BaseAddress == IntPtr.Zero)
						throw new InvalidOperationException();

					RegisterStructPointer = (Registers*)BaseAddress;
				}

				// Pushad
				public byte[] GeneratePushAd(bool force = false)
				{
					if (PushAdBytes != null && PushAdBytes.Length > 0 && !force)
						return PushAdBytes;

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
						$"mov dword ptr [0x{BaseAddress.ToInt32() + 28:X8}], esp",

						$"mov word ptr [0x{BaseAddress.ToInt32() + 36:X8}], ax",
						$"mov word ptr [0x{BaseAddress.ToInt32() + 38:X8}], bx",
						$"mov word ptr [0x{BaseAddress.ToInt32() + 40:X8}], cx",
						$"mov word ptr [0x{BaseAddress.ToInt32() + 42:X8}], dx",

						$"mov byte ptr [0x{BaseAddress.ToInt32() + 44:X8}], ah",
						$"mov byte ptr [0x{BaseAddress.ToInt32() + 45:X8}], al",

						$"mov byte ptr [0x{BaseAddress.ToInt32() + 46:X8}], bh",
						$"mov byte ptr [0x{BaseAddress.ToInt32() + 47:X8}], bl",

						$"mov byte ptr [0x{BaseAddress.ToInt32() + 48:X8}], ch",
						$"mov byte ptr [0x{BaseAddress.ToInt32() + 49:X8}], cl",

						$"mov byte ptr [0x{BaseAddress.ToInt32() + 50:X8}], dh",
						$"mov byte ptr [0x{BaseAddress.ToInt32() + 51:X8}], dl",

						"push [esp]",
						$"pop dword ptr [0x{BaseAddress.ToInt32() + 52:X8}]" // EIP?
					}, true, out byte[] assembled);

					if (assembleResult)
					{
						PushAdBytes = assembled;
						return PushAdBytes;
					}

					throw new InvalidOperationException();
				}

				// Popad
				public byte[] GeneratePopAd(bool force = false)
				{
					if (PopAdBytes != null && PopAdBytes.Length > 0 && !force)
						return PopAdBytes;

					bool assembleResult = Assembler.AssembleMnemonics(new[]
					{
						$"mov eax, [0x{BaseAddress.ToInt32():X8}]",
						$"mov ebx, [0x{BaseAddress.ToInt32() + 4:X8}]",
						$"mov ecx, [0x{BaseAddress.ToInt32() + 8:X8}]",
						$"mov edx, [0x{BaseAddress.ToInt32() + 12:X8}]",
						$"mov edi, [0x{BaseAddress.ToInt32() + 16:X8}]",
						$"mov esi, [0x{BaseAddress.ToInt32() + 20:X8}]",
						$"mov ebp, [0x{BaseAddress.ToInt32() + 24:X8}]",
						$"mov esp, [0x{BaseAddress.ToInt32() + 28:X8}]",

						$"mov ax, [0x{BaseAddress.ToInt32() + 36:X8}]",
						$"mov bx, [0x{BaseAddress.ToInt32() + 38:X8}]",
						$"mov cx, [0x{BaseAddress.ToInt32() + 40:X8}]",
						$"mov dx, [0x{BaseAddress.ToInt32() + 42:X8}]",

						$"mov ah, [0x{BaseAddress.ToInt32() + 44:X8}]",
						$"mov al, [0x{BaseAddress.ToInt32() + 45:X8}]",

						$"mov bh, [0x{BaseAddress.ToInt32() + 46:X8}]",
						$"mov bl, [0x{BaseAddress.ToInt32() + 47:X8}]",

						$"mov ch, [0x{BaseAddress.ToInt32() + 48:X8}]",
						$"mov cl, [0x{BaseAddress.ToInt32() + 49:X8}]",

						$"mov dh, [0x{BaseAddress.ToInt32() + 50:X8}]",
						$"mov dl, [0x{BaseAddress.ToInt32() + 51:X8}]",
				}, true, out byte[] assembled);
					// Generate mnemonics here

					if (assembleResult)
					{
						PopAdBytes = assembled;
						return PopAdBytes;
					}

					throw new InvalidOperationException();
				}

				// pushfd
				public byte[] GeneratePushFd(bool force = false)
				{
					if (PushFdBytes != null && PushFdBytes.Length > 0 && !force)
						return PushFdBytes;

					bool assembleResult = Assembler.AssembleMnemonics(new[]
					{
						"pushfd",
						$"pop [0x{BaseAddress.ToInt32() + 32:X8}]"
					}, true, out byte[] assembled);

					if (assembleResult)
					{
						PushFdBytes = assembled;
						return PushFdBytes;
					}

					throw new InvalidOperationException();
				}

				// popfd
				public byte[] GeneratePopFd(bool force = false)
				{
					if (PopFdBytes != null && PopFdBytes.Length > 0 && !force)
						return PopFdBytes;

					bool assembleResult = Assembler.AssembleMnemonics(new[]
					{
						$"push [0x{BaseAddress.ToInt32() + 32:X8}]",
						$"popfd"
					}, true, out byte[] assembled);

					if (assembleResult)
					{
						PopFdBytes = assembled;
						return PopFdBytes;
					}

					throw new InvalidOperationException();
				}
			}

			[StructLayout(LayoutKind.Sequential)]
			public unsafe struct Registers
			{
				public int EAX; // 0
				public int EBX; // 4
				public int ECX; // 8
				public int EDX; // 12
				public int EDI; // 16
				public int ESI; // 20
				public int EBP; // 24
				public int ESP; // 28

				public int EFLAGS; // 32

				public short AX; // 36
				public short BX; // 38
				public short CX; // 40
				public short DX; // 42

				public byte AH; // 44;
				public byte AL; // 45
				public byte BH; // 46
				public byte BL; // 47
				public byte CH; // 48
				public byte CL; // 49
				public byte DH; // 50
				public byte DL; // 51

				public readonly int EIP; // 52

				public string PrintRegisters()
				{
					string ret = "";

					ret += $"EIP: 0x{EIP:X8} ({EIP})\n";
					ret += $"EFLAGS: 0x{EFLAGS:X8} ({EFLAGS})\n";

					ret += $"EAX: 0x{EAX:X8} ({EAX})\n";
					ret += $"EBX: 0x{EBX:X8} ({EBX})\n";
					ret += $"ECX: 0x{ECX:X8} ({ECX})\n";
					ret += $"EDX: 0x{EDX:X8} ({EDX})\n";
					ret += $"EDI: 0x{EDI:X8} ({EDI})\n";
					ret += $"ESI: 0x{ESI:X8} ({ESI})\n";
					ret += $"EBP: 0x{EBP:X8} ({EBP})\n";
					ret += $"ESP: 0x{ESP:X8} ({ESP})\n";

					ret += $"AX: 0x{AX:X} ({AX})\n";
					ret += $"BX: 0x{BX:X} ({BX})\n";
					ret += $"CX: 0x{CX:X} ({CX})\n";
					ret += $"DX: 0x{DX:X} ({DX})\n";

					ret += $"AH: 0x{AH:X} ({AH})\n";
					ret += $"AL: 0x{AL:X} ({AL})\n";

					ret += $"BH: 0x{BH:X} ({BH})\n";
					ret += $"BL: 0x{BL:X} ({BL})\n";

					ret += $"CH: 0x{CH:X} ({CH})\n";
					ret += $"CL: 0x{CL:X} ({CL})\n";

					ret += $"DH: 0x{DH:X} ({DH})\n";
					ret += $"DL: 0x{DL:X} ({DL})";
					return ret;
				}
			}

			[StructLayout(LayoutKind.Sequential)]
			internal struct SYSTEM_INFO
			{
				internal ushort wProcessorArchitecture;
				internal ushort wReserved;
				internal uint dwPageSize;
				internal IntPtr lpMinimumApplicationAddress;
				internal IntPtr lpMaximumApplicationAddress;
				internal IntPtr dwActiveProcessorMask;
				internal uint dwNumberOfProcessors;
				internal uint dwProcessorType;
				internal uint dwAllocationGranularity;
				internal ushort wProcessorLevel;
				internal ushort wProcessorRevision;
			}

			[StructLayout(LayoutKind.Sequential)]
			public struct Vector4
			{
				public float X;
				public float Y;
				public float Z;
				public float W;
			}

			[StructLayout(LayoutKind.Sequential, Pack = 1)]
			public struct Vector
			{
				public float X;
				public float Y;

				public Vector(float x, float y)
				{
					X = x;
					Y = y;
				}

				public float Distance(Vector vector)
				{
					float dx = vector.X - X;
					float dy = vector.Y - Y;
					return (float)Math.Sqrt(dx * dx + dy * dy);
				}
			}

			[StructLayout(LayoutKind.Sequential)]
			public struct D3DMATRIX
			{
				public float _11, _12, _13, _14;
				public float _21, _22, _23, _24;
				public float _31, _32, _33, _34;
				public float _41, _42, _43, _44;

				public float[][] As2DArray()
				{
					return new float[4][]
					{
						new[] { _11, _12, _13, _14 },
						new[] { _21, _22, _23, _24 },
						new[] { _31, _32, _33, _34 },
						new[] { _41, _42, _43, _44 },
					};
				}
				public float[] AsArray()
				{
					return new[]
					{
						_11, _12, _13, _14,
						_21, _22, _23, _24,
						_31, _32, _33, _34,
						_41, _42, _43, _44
					};
				}
			}

			[StructLayout(LayoutKind.Sequential, Pack = 1)]
			public unsafe struct Vector3
			{
				public Vector3(float x, float y, float z)
				{
					X = x;
					Y = y;
					Z = z;
				}

				public float X;
				public float Y;
				public float Z;

				public Vector3 Zero => new Vector3(0, 0, 0);

				public bool World2Screen(float[] matrix, out Vector screenPosition)
				{
					if (matrix == null || matrix.Length != 16)
					{
						screenPosition = new Vector(0f, 0f);
						return false;
					}

					Vector4 vec = new Vector4
					{
						X = this.X * matrix[0] + this.Y * matrix[4] + this.Z * matrix[8] + matrix[12],
						Y = this.X * matrix[1] + this.Y * matrix[5] + this.Z * matrix[9] + matrix[13],
						Z = this.X * matrix[2] + this.Y * matrix[6] + this.Z * matrix[10] + matrix[14],
						W = this.X * matrix[3] + this.Y * matrix[7] + this.Z * matrix[11] + matrix[15]
					};

					if (vec.W < 0.1f)
					{
						screenPosition = new Vector(0f, 0f);
						return false;
					}

					Vector3 NDC = new Vector3
					{
						X = vec.X / vec.W,
						Y = vec.Y / vec.W,
						Z = vec.Z / vec.Z
					};

					int* w = (int*)0x00510C94;
					int* h = (int*)0x00510C98;


					screenPosition = new Vector
					{
						X = (*w / 2 * NDC.X) + (NDC.X + *w / 2),
						Y = -(*h / 2 * NDC.Y) + (NDC.Y + *h / 2)
					};
					return true;
				}
				public bool World2Screen(float* matrix, out Vector screenPosition)
				{
					if (matrix == null)
					{
						screenPosition = new Vector(0f, 0f);
						return false;
					}

					Vector4 vec = new Vector4
					{
						X = this.X * matrix[0] + this.Y * matrix[4] + this.Z * matrix[8] + matrix[12],
						Y = this.X * matrix[1] + this.Y * matrix[5] + this.Z * matrix[9] + matrix[13],
						Z = this.X * matrix[2] + this.Y * matrix[6] + this.Z * matrix[10] + matrix[14],
						W = this.X * matrix[3] + this.Y * matrix[7] + this.Z * matrix[11] + matrix[15]
					};

					if (vec.W < 0.1f)
					{
						screenPosition = new Vector(0f, 0f);
						return false;
					}

					Vector3 NDC = new Vector3
					{
						X = vec.X / vec.W,
						Y = vec.Y / vec.W,
						Z = vec.Z / vec.Z
					};

					int* w = (int*)0x00510C94;
					int* h = (int*)0x00510C98;


					screenPosition = new Vector
					{
						X = (*w / 2 * NDC.X) + (NDC.X + *w / 2),
						Y = -(*h / 2 * NDC.Y) + (NDC.Y + *h / 2)
					};
					return true;
				}

				public float Max => (X > Y) ? ((X > Z) ? X : Z) : ((Y > Z) ? Y : Z);
				public float Min => (X < Y) ? ((X < Z) ? X : Z) : ((Y < Z) ? Y : Z);
				public float EuclideanNorm => (float)Math.Sqrt(X * X + Y * Y + Z * Z);
				public float Square => X * X + Y * Y + Z * Z;
				public float Magnitude => (float)Math.Sqrt(SumComponentSqrs());
				public float Distance3D(Vector3 v1, Vector3 v2)
				{
					return
						(float)Math.Sqrt
						(
							(v1.X - v2.X) * (v1.X - v2.X) +
							(v1.Y - v2.Y) * (v1.Y - v2.Y) +
							(v1.Z - v2.Z) * (v1.Z - v2.Z)
						);
				}
				public float Distance3D(Vector3 other)
				{
					return Distance3D(this, other);
				}

				public float Normalize()
				{
					float norm = (float)System.Math.Sqrt(X * X + Y * Y + Z * Z);
					float invNorm = 1.0f / norm;

					X *= invNorm;
					Y *= invNorm;
					Z *= invNorm;

					return norm;
				}
				public Vector3 Inverse()
				{
					return new Vector3(
						(X == 0) ? 0 : 1.0f / X,
						(Y == 0) ? 0 : 1.0f / Y,
						(Z == 0) ? 0 : 1.0f / Z);
				}
				public Vector3 Abs()
				{
					return new Vector3(Math.Abs(X), Math.Abs(Y), Math.Abs(Z));
				}
				public Vector3 CrossProduct(Vector3 vector1, Vector3 vector2)
				{
					return new Vector3(
						vector1.Y * vector2.Z - vector1.Z * vector2.Y,
						vector1.Z * vector2.X - vector1.X * vector2.Z,
						vector1.X * vector2.Y - vector1.Y * vector2.X);
				}
				public float DotProduct(Vector3 vector1, Vector3 vector2)
				{
					return vector1.X * vector2.X + vector1.Y * vector2.Y + vector1.Z * vector2.Z;
				}


				public override string ToString()
				{
					return string.Format(CultureInfo.InvariantCulture,
						"{0}, {1}, {2}", X, Y, Z);
				}
				public float[] ToArray()
				{
					return new float[3] { X, Y, Z };
				}

				public float this[int index]
				{
					get
					{
						switch (index)
						{
							case 0: { return X; }
							case 1: { return Y; }
							case 2: { return Z; }
							default: throw new IndexOutOfRangeException($"Range is from 0 to 2");
						}
					}
				}

				public static Vector3 operator +(Vector3 vector, float value)
				{
					return new Vector3(vector.X + value, vector.Y + value, vector.Z + value);
				}
				public static Vector3 operator +(Vector3 vector1, Vector3 vector2)
				{
					return new Vector3(vector1.X + vector2.X, vector1.Y + vector2.Y, vector1.Z + vector2.Z);
				}
				public Vector3 Add(Vector3 vector1, Vector3 vector2)
				{
					return vector1 + vector2;
				}
				public Vector3 Add(Vector3 vector, float value)
				{
					return vector + value;
				}

				private Vector3 SqrComponents(Vector3 v1)
				{
					return
					(
						new Vector3
						(
							v1.X * v1.X,
							v1.Y * v1.Y,
							v1.Z * v1.Z
						)
					);
				}
				private double SumComponentSqrs(Vector3 v1)
				{
					Vector3 v2 = SqrComponents(v1);
					return v2.SumComponents();
				}
				private double SumComponentSqrs()
				{
					return SumComponentSqrs(this);
				}
				private double SumComponents(Vector3 v1)
				{
					return (v1.X + v1.Y + v1.Z);
				}
				private double SumComponents()
				{
					return SumComponents(this);
				}

				public static Vector3 operator -(Vector3 vector1, Vector3 vector2)
				{
					return new Vector3(vector1.X - vector2.X, vector1.Y - vector2.Y, vector1.Z - vector2.Z);
				}
				public Vector3 Subtract(Vector3 vector1, Vector3 vector2)
				{
					return vector1 - vector2;
				}
				public static Vector3 operator -(Vector3 vector, float value)
				{
					return new Vector3(vector.X - value, vector.Y - value, vector.Z - value);
				}
				public Vector3 Subtract(Vector3 vector, float value)
				{
					return vector - value;
				}

				public static Vector3 operator *(Vector3 vector1, Vector3 vector2)
				{
					return new Vector3(vector1.X * vector2.X, vector1.Y * vector2.Y, vector1.Z * vector2.Z);
				}
				public Vector3 Multiply(Vector3 vector1, Vector3 vector2)
				{
					return vector1 * vector2;
				}
				public static Vector3 operator *(Vector3 vector, float factor)
				{
					return new Vector3(vector.X * factor, vector.Y * factor, vector.Z * factor);
				}
				public Vector3 Multiply(Vector3 vector, float factor)
				{
					return vector * factor;
				}

				public static Vector3 operator /(Vector3 vector1, Vector3 vector2)
				{
					return new Vector3(vector1.X / vector2.X, vector1.Y / vector2.Y, vector1.Z / vector2.Z);
				}
				public Vector3 Divide(Vector3 vector1, Vector3 vector2)
				{
					return vector1 / vector2;
				}
				public static Vector3 operator /(Vector3 vector, float factor)
				{
					return new Vector3(vector.X / factor, vector.Y / factor, vector.Z / factor);
				}
				public Vector3 Divide(Vector3 vector, float factor)
				{
					return vector / factor;
				}

				public static bool operator ==(Vector3 vector1, Vector3 vector2)
				{
					return ((vector1.X == vector2.X) && (vector1.Y == vector2.Y) && (vector1.Z == vector2.Z));
				}
				public static bool operator !=(Vector3 vector1, Vector3 vector2)
				{
					return ((vector1.X != vector2.X) || (vector1.Y != vector2.Y) || (vector1.Z != vector2.Z));
				}

				public static bool operator <(Vector3 v1, Vector3 v2)
				{
					return v1.SumComponentSqrs() < v2.SumComponentSqrs();
				}
				public static bool operator <=(Vector3 v1, Vector3 v2)
				{
					return v1.SumComponentSqrs() <= v2.SumComponentSqrs();
				}

				public static bool operator >=(Vector3 v1, Vector3 v2)
				{
					return v1.SumComponentSqrs() >= v2.SumComponentSqrs();
				}
				public static bool operator >(Vector3 v1, Vector3 v2)
				{
					return v1.SumComponentSqrs() > v2.SumComponentSqrs();
				}


				public bool Equals(Vector3 vector)
				{
					return ((vector.X == X) && (vector.Y == Y) && (vector.Z == Z));
				}
				public override bool Equals(object obj)
				{
					if (obj is Vector3 vector3)
					{
						return Equals(vector3);
					}
					return false;
				}

				public override int GetHashCode()
				{
					return X.GetHashCode() + Y.GetHashCode() + Z.GetHashCode();
				}
			}

			[StructLayout(LayoutKind.Sequential)]
			public struct MEMORY_BASIC_INFORMATION
			{
				public IntPtr BaseAddress;
				public IntPtr AllocationBase;
				public Enums.AllocationType AllocationProtect;
				public IntPtr RegionSize;
				public Enums.MemoryState State;
				public Enums.MemoryProtection Protect;
				public Enums.MemoryType Type;
			}
		}
		public class Enums	
		{
			[Flags]
			public enum AllocationType
			{
				Commit = 0x1000,
				Reserve = 0x2000,
				Decommit = 0x4000,
				Release = 0x8000,
				Reset = 0x80000,
				Physical = 0x400000,
				TopDown = 0x100000,
				WriteWatch = 0x200000,
				LargePages = 0x20000000
			}

			[Flags]
			public enum MemoryProtection
			{
				Execute = 0x10,
				ExecuteRead = 0x20,
				ExecuteReadWrite = 0x40,
				ExecuteWriteCopy = 0x80,
				NoAccess = 0x01,
				ReadOnly = 0x02,
				ReadWrite = 0x04,
				WriteCopy = 0x08,
				GuardModifierflag = 0x100,
				NoCacheModifierflag = 0x200,
				WriteCombineModifierflag = 0x400
			}

			public enum FreeType
			{
				Decommit = 0x4000,
				Release = 0x8000,
			}

			[Flags]
			public enum MemoryState : uint
			{
				MEM_COMMIT = 0x1000,
				MEM_FREE = 0x10000,
				MEM_RESERVE = 0x2000
			}

			[Flags]
			public enum MemoryType : uint
			{
				MEM_IMAGE = 0x1000000,
				MEM_MAPPED = 0x40000,
				MEM_PRIVATE = 0x20000
			}

			[Flags]
			public enum DesiredAccess : uint
			{
				GenericRead = 0x80000000,
				GenericWrite = 0x40000000,
				GenericExecute = 0x20000000,
				GenericAll = 0x10000000
			}

			public enum StdHandle : int
			{
				Input = -10,
				Output = -11,
				Error = -12
			}

			public enum ThreadAccess : int
			{
				TERMINATE = (0x0001),
				SUSPEND_RESUME = (0x0002),
				GET_CONTEXT = (0x0008),
				SET_CONTEXT = (0x0010),
				SET_INFORMATION = (0x0020),
				QUERY_INFORMATION = (0x0040),
				SET_THREAD_TOKEN = (0x0080),
				IMPERSONATE = (0x0100),
				DIRECT_IMPERSONATION = (0x0200)
			}
		}
		public class PInvoke
		{
			[DllImport("kernel32", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
			public static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

			[DllImport("kernel32", SetLastError = true, CharSet = CharSet.Ansi)]
			public static extern IntPtr LoadLibrary([MarshalAs(UnmanagedType.LPStr)]string lpFileName);

			[DllImport("kernel32.dll", SetLastError = true)]

			internal static extern void GetSystemInfo(ref Structures.SYSTEM_INFO Info);

			[DllImport("kernel32.dll", SetLastError = true)]
			public static extern IntPtr VirtualAlloc(IntPtr lpAddress, UIntPtr dwSize, Enums.AllocationType lAllocationType, Enums.MemoryProtection flProtect);

			[DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
			public static extern bool VirtualFree(IntPtr lpAddress,
				uint dwSize, Enums.FreeType dwFreeType);

			[DllImport("kernel32.dll", SetLastError = true)]
			public static extern bool VirtualProtect(IntPtr lpAddress, int dwSize,
				Enums.MemoryProtection flNewProtect, out Enums.MemoryProtection lpflOldProtect);

			[DllImport("kernel32.dll", SetLastError = true)]
			public static extern int VirtualQuery(IntPtr lpAddress, out Structures.MEMORY_BASIC_INFORMATION lpBuffer, uint dwLength);

			[DllImport("kernel32.dll")]
			public static extern bool AllocConsole();

			[DllImport("kernel32.dll", SetLastError = true)]
			public static extern IntPtr CreateFile(string lpFileName
				, [MarshalAs(UnmanagedType.U4)] Enums.DesiredAccess dwDesiredAccess
				, [MarshalAs(UnmanagedType.U4)] FileShare dwShareMode
				, uint lpSecurityAttributes
				, [MarshalAs(UnmanagedType.U4)] FileMode dwCreationDisposition
				, [MarshalAs(UnmanagedType.U4)] FileAttributes dwFlagsAndAttributes
				, uint hTemplateFile);

			[DllImport("kernel32.dll", SetLastError = true)]
			public static extern bool SetStdHandle(Enums.StdHandle nStdHandle, IntPtr hHandle);

			[DllImport("kernel32.dll")]
			public static extern IntPtr OpenThread(Enums.ThreadAccess dwDesiredAccess, bool bInheritHandle, uint dwThreadId);

			[DllImport("kernel32.dll")]
			public static extern int SuspendThread(IntPtr hThread);

			[DllImport("kernel32.dll", SetLastError = true)]
			public static extern int ResumeThread(IntPtr hThread);

			[DllImport("kernel32.dll", SetLastError = true)]
			[SuppressUnmanagedCodeSecurity]
			[return: MarshalAs(UnmanagedType.Bool)]
			public static extern bool CloseHandle(IntPtr hObject);

			[DllImport("kernel32.dll", SetLastError = true)]
			public static extern void SetLastError(uint dwErrorCode);

			[DllImport("kernel32.dll", CharSet = CharSet.Auto)]
			public static extern IntPtr GetModuleHandle(string lpModuleName);

			[DllImport("user32.dll", EntryPoint = "FindWindow", SetLastError = true)]
			public static extern IntPtr FindWindowByCaption(IntPtr ZeroOnly, string lpWindowName);

			[DllImport("kernel32.dll", SetLastError = false)]
			public static extern IntPtr HeapAlloc(IntPtr hHeap, uint dwFlags, UIntPtr dwBytes);

			[DllImport("kernel32.dll", SetLastError = true)]
			public static extern IntPtr HeapCreate(uint flOptions, UIntPtr dwInitialSize,
				UIntPtr dwMaximumSize);

			[DllImport("kernel32.dll", SetLastError = true)]
			public static extern bool HeapFree(IntPtr hHeap, uint dwFlags, IntPtr lpMem);
		}
	}



	public class DebugConsole
	{
		public static bool InitiateDebugConsole()
		{
			if (Memory.PInvoke.AllocConsole())
			{
				//https://developercommunity.visualstudio.com/content/problem/12166/console-output-is-gone-in-vs2017-works-fine-when-d.html
				// Console.OpenStandardOutput eventually calls into GetStdHandle. As per MSDN documentation of GetStdHandle: http://msdn.microsoft.com/en-us/library/windows/desktop/ms683231(v=vs.85).aspx will return the redirected handle and not the allocated console:
				// "The standard handles of a process may be redirected by a call to  SetStdHandle, in which case  GetStdHandle returns the redirected handle. If the standard handles have been redirected, you can specify the CONIN$ value in a call to the CreateFile function to get a handle to a console's input buffer. Similarly, you can specify the CONOUT$ value to get a handle to a console's active screen buffer."
				// Get the handle to CONOUT$.    
				var stdOutHandle = Memory.PInvoke.CreateFile("CONOUT$", Memory.Enums.DesiredAccess.GenericRead | Memory.Enums.DesiredAccess.GenericWrite, FileShare.ReadWrite, 0, FileMode.Open, FileAttributes.Normal, 0);
				var stdInHandle = Memory.PInvoke.CreateFile("CONIN$", Memory.Enums.DesiredAccess.GenericRead | Memory.Enums.DesiredAccess.GenericWrite, FileShare.ReadWrite, 0, FileMode.Open, FileAttributes.Normal, 0);

				if (stdOutHandle == new IntPtr(-1))
				{
					throw new Win32Exception(Marshal.GetLastWin32Error());
				}

				if (stdInHandle == new IntPtr(-1))
				{
					throw new Win32Exception(Marshal.GetLastWin32Error());
				}


				if (!Memory.PInvoke.SetStdHandle(Memory.Enums.StdHandle.Output, stdOutHandle))
				{
					throw new Win32Exception(Marshal.GetLastWin32Error());
				}

				if (!Memory.PInvoke.SetStdHandle(Memory.Enums.StdHandle.Input, stdInHandle))
				{
					throw new Win32Exception(Marshal.GetLastWin32Error());
				}

				var standardOutput = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
				var standardInput = new StreamReader(Console.OpenStandardInput());

				Console.SetIn(standardInput);
				Console.SetOut(standardOutput);
				return true;
			}
			return false;
		}
	}

	public class HelperMethods
	{

		public static unsafe T* malloc<T>(T obj) where T : unmanaged
		{
			var ptr = (T*)Memory.Allocator.Unmanaged.Extended.AllocateHeap((uint) Marshal.SizeOf<T>());
			if (ptr == null)
				throw new Exception("malloc failed");

			*ptr = default;
			return ptr;
		} 

		public static void PrintExceptionData(object exceptionObj, bool writeToFile = false)
		{
			if (exceptionObj == null) return;
			Type actualType = exceptionObj.GetType();

			Exception exceptionObject = exceptionObj as Exception;

			var s = new StackTrace(exceptionObject);
			var thisasm = Assembly.GetExecutingAssembly();

			var methodName = s.GetFrames().Select(f => f.GetMethod()).First(m => m.Module.Assembly == thisasm).Name;
			var parameterInfo = s.GetFrames().Select(f => f.GetMethod()).First(m => m.Module.Assembly == thisasm).GetParameters();
			var methodReturnType = s.GetFrame(1).GetMethod().GetType();

			var lineNumber = s.GetFrame(0).GetFileLineNumber();

			// string formatedMethodNameAndParameters = $"{methodReturnType} {methodName}(";
			string formatedMethodNameAndParameters = $"{methodName}(";

			if (parameterInfo.Length < 1)
			{
				formatedMethodNameAndParameters += ")";
			}
			else
			{
				for (int n = 0; n < parameterInfo.Length; n++)
				{
					ParameterInfo param = parameterInfo[n];
					string parameterName = param.Name;

					if (n == parameterInfo.Length - 1)
						formatedMethodNameAndParameters += $"{param.ParameterType} {parameterName})";
					else
						formatedMethodNameAndParameters += $"{param.ParameterType} {parameterName},";
				}
			}

			string formattedContent = $"[UNHANDLED_EXCEPTION] Caught Exception of type {actualType}\n\n" +
									  $"Exception Message: {exceptionObject.Message}\n" +
									  $"Exception Origin File/Module: {exceptionObject.Source}\n" +
									  $"Method that threw the Exception: {formatedMethodNameAndParameters}\n" +
									  $"Line Number: {lineNumber}\n";

			Console.WriteLine(formattedContent);

			if (exceptionObject.Data.Count > 0)
			{
				Console.WriteLine($"Exception Data Dictionary Results:");
				foreach (DictionaryEntry pair in exceptionObject.Data)
					Console.WriteLine("	* {0} = {1}", pair.Key, pair.Value);
			}

			if (writeToFile)
				WriteToFile(formattedContent);

		}

		public static uint RelAddr(uint from, uint to)
			=> to - from - 5;

		public static unsafe long FindPattern(byte* body, int bodyLength, byte[] pattern, byte[] masks, long start = 0)
		{
			long foundIndex = -1;

			if (bodyLength <= 0 || pattern.Length <= 0 || start > bodyLength - pattern.Length ||
			    pattern.Length > bodyLength) return foundIndex;

			for (long index = start; index <= bodyLength - pattern.Length; index++)
			{
				if (((body[index] & masks[0]) != (pattern[0] & masks[0]))) continue;

				var match = true;
				for (int index2 = 1; index2 <= pattern.Length - 1; index2++)
				{
					if ((body[index + index2] & masks[index2]) == (pattern[index2] & masks[index2])) continue;
					match = false;
					break;

				}

				if (!match) continue;

				foundIndex = index;
				break;
			}

			return foundIndex;
		}

		public static void WriteToFile(string contents)
		{
			if (contents.Length < 1) return;
			try
			{
				File.WriteAllText($"{Memory.HostProcess.ProcessName}_SessionLogs.txt", contents);
			}
			catch
			{
				Debug.WriteLine($"WriteToFile - Failed writing contents to file '{Memory.HostProcess.ProcessName}_SessionLogs.txt'");
			}
		}
	}

	// Extensions
	public static class ProcessExtensions
	{
		public static ProcessModule FindProcessModule(this Process obj, string moduleName)
		{
			foreach (ProcessModule pm in obj.Modules)
				if (string.Equals(pm.ModuleName, moduleName, StringComparison.CurrentCultureIgnoreCase))
					return pm;

			return null;
		}
	}

	public static class ByteArrayExtensions
	{
		public static byte[] FromHexCE(this byte[] destination, string CEStyleHexString)
		{
			if (string.IsNullOrEmpty(CEStyleHexString))
				return null;
			// Expects CE styled string array

			string[] split = CEStyleHexString.TrimStart().TrimEnd().Split(' ');
			byte[] ret = new byte[split.Length];

			for (int n = 0; n < split.Length; n++)
			{
				ret[n] = Convert.ToByte(split[n], 16);
			}
				

			return ret;
		}

		public static void FromHexIDA(ref byte[] destination, string IStyleHexString)
		{
			if (string.IsNullOrEmpty(IStyleHexString))
				return;
			// Expects CE styled string array

			string[] split = IStyleHexString.TrimStart().TrimEnd().Split(new [] { "\\x" }, StringSplitOptions.None);
			byte[] ret = new byte[split.Length];

			for (int n = 0; n < split.Length; n++)
			{
				ret[n] = Convert.ToByte(split[n], 16);
			}

			destination = ret;
		}

		public static string ByteArrayToHexString(this byte[] obj, bool CEStyleString = true)
		{
			string repres = "";
			if (CEStyleString)
			{
				foreach (var bt in obj)
				{
					repres += $"{bt:X2} ";
				}
				return repres.TrimEnd(' ');
			}
			else
			{
				foreach (var bt in obj)
				{
					repres += $"\\x{bt:X2}";
				}
				return repres;
			}
		}
	}

	public static class GraphicsExtensions
	{
		public static void DrawBoundingBox(this Graphics gObj, Memory.Structures.Vector headScreenPos, Memory.Structures.Vector feetScreenPos, Pen color)
		{
			if (gObj == null)
				return;

			const int OFFSET = 20;
			float height = Math.Abs(headScreenPos.Y - feetScreenPos.Y);
			float width = height / 2;

			gObj.DrawRectangle(color,
				headScreenPos.X - width / 2, headScreenPos.Y - OFFSET,
				width, height + OFFSET);
		}
	}
}
