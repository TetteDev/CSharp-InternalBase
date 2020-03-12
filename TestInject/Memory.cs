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
using System.Runtime.Remoting.Messaging;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using TestInject;
using Iced.Intel;
using static Iced.Intel.AssemblerRegisters;
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
			public unsafe class PrologueHook<T> : IDisposable where T : Delegate
			{
				public bool Enabled { get; private set; }

				public IntPtr Target { get; private set; }

				private Delegate HookMethod;
				private bool PreserveBytes;
				public byte[] OriginalBytes;

				public PrologueHook(IntPtr FunctionStartAddress, T HookDelegate,bool PreserveOverwrittenBytes = true, int numBytesPrologueLengthFixup = 5)
				{
					if (FunctionStartAddress == IntPtr.Zero)
						throw new InvalidOperationException($"[PrologueHook] Parameter '{nameof(FunctionStartAddress)}' cannot be zero!");

					if (numBytesPrologueLengthFixup < 5)
						throw new InvalidOperationException($"[PrologueHook] Parameter '{nameof(numBytesPrologueLengthFixup)}' cannot be less than 0");

					Target = FunctionStartAddress;
					HookMethod = HookDelegate ?? throw new InvalidOperationException($"[PrologueHook] Parameter '{nameof(HookDelegate)}' cannot be null!");
					PreserveBytes = PreserveOverwrittenBytes;

					if (PreserveBytes)
					{
						OriginalBytes = new byte[numBytesPrologueLengthFixup];
						Reader.ReadBytes(Target, (uint)numBytesPrologueLengthFixup);
					}
				}


				#region IDisposable Methods
				private bool _disposed;
				public void Dispose()
				{
					Dispose(true);
					GC.SuppressFinalize(this);
				}
				protected virtual void Dispose(bool disposing)
				{
					if (!_disposed && disposing)
					{
						// Dispose

						_disposed = true;
					}
				}
				#endregion
			}

			public unsafe class Hook<T> where T : Delegate
			{
				public readonly IntPtr Target;
				public bool IsEnabled;

				public T Original;
				private GCHandle origHandle;

				private GCHandle hookHandle;
				private Delegate hook;

				private byte[] originalBytes;
				private bool compabilityModeEnabled;
				public RegisterStates RegisterStates;
				private int numBytes;

				private IntPtr middleman;
				private IntPtr origFunc;


				public Hook(IntPtr target, Delegate hook, bool compabilityMode = true, int prologueLengthFixup = 5)
				{
					if (prologueLengthFixup < 5)
						throw new InvalidOperationException($"Need atleast 5 bytes");

					if (target == IntPtr.Zero)
						throw new InvalidOperationException($"Target cannot be null");

					this.hook = hook ?? throw new InvalidOperationException($"Hook cannot be null");
					hookHandle = GCHandle.Alloc(hook, GCHandleType.Normal);
					compabilityModeEnabled = compabilityMode;
					RegisterStates = compabilityModeEnabled ? new RegisterStates() : null;

					if (compabilityMode && RegisterStates != null)
					{
						RegisterStates.GeneratePopFd(true);
						RegisterStates.GeneratePushFd(true);

						RegisterStates.GeneratePushAd(true);
						RegisterStates.GeneratePopAd(true);
					}

					Target = target;
					numBytes = prologueLengthFixup;
					originalBytes = Reader.ReadBytes(Target, (uint)numBytes);

					middleman = Allocator.Unmanaged.Allocate(5 + (uint)prologueLengthFixup + 
					                                         (compabilityMode ? (uint)RegisterStates.PushAdBytes.Length + (uint)RegisterStates.PushFdBytes.Length : 0));
					if (middleman == IntPtr.Zero)
						throw new InvalidOperationException($"Failed to allocate memory for middleman");

					origFunc = Allocator.Unmanaged.Allocate(5 + (uint) prologueLengthFixup + 
					                                        (compabilityMode ? (uint) RegisterStates.PopAdBytes.Length + (uint)RegisterStates.PopFdBytes.Length : 0));
					if (origFunc == IntPtr.Zero)
					{
						if (middleman != IntPtr.Zero)
							Allocator.Unmanaged.FreeMemory(middleman);

						throw new InvalidOperationException($"Failed allocating memory for orig func");
					}

					Original = Marshal.GetDelegateForFunctionPointer<T>(Target);
					origHandle = GCHandle.Alloc(Original, GCHandleType.Normal);

					Console.Title = $"Middleman: 0x{middleman.ToInt32():X8}, Orig Func: 0x{origFunc.ToInt32():X8}";
					Console.WriteLine($"Middleman: 0x{middleman.ToInt32():X8}, Orig Func: 0x{origFunc.ToInt32():X8}");
				}

				public bool Install()
				{
					if (IsEnabled)
						return false;

					Protection.SetPageProtection(Target, numBytes, Enums.MemoryProtection.ExecuteReadWrite, out var oldProtection);

					int offset = 0;
					// Middleman to hook
					if (compabilityModeEnabled)
					{
						Writer.WriteBytes(middleman, RegisterStates.PushAdBytes);
						offset += RegisterStates.PushAdBytes.Length;

						Writer.WriteBytes(middleman + offset, RegisterStates.PushFdBytes);
						offset += RegisterStates.PushFdBytes.Length;
					}

					Writer.WriteBytes(middleman + offset, originalBytes);
					offset += originalBytes.Length;

					*(byte*) (middleman + offset) = 0xE9;
					*(uint*) (middleman + offset + 1) = HelperMethods.CalculateRelativeAddressForJmp((uint) (middleman + offset), (uint) Marshal.GetFunctionPointerForDelegate(hook));

					// Orig func region to original func
					offset = 0;
					if (compabilityModeEnabled)
					{
						Writer.WriteBytes(origFunc, RegisterStates.PopAdBytes);
						offset += RegisterStates.PopAdBytes.Length;

						Writer.WriteBytes(origFunc + offset, RegisterStates.PopFdBytes);
						offset += RegisterStates.PopFdBytes.Length;
					}

					Writer.WriteBytes(origFunc + offset, originalBytes);
					offset += originalBytes.Length;

					*(byte*) (origFunc + offset) = 0xE9;
					*(uint*) (origFunc + offset + 1) = HelperMethods.CalculateRelativeAddressForJmp((uint) (origFunc + offset), (uint)(Target + numBytes));

					Original = Marshal.GetDelegateForFunctionPointer<T>(origFunc);
					if (origHandle.IsAllocated)
						origHandle.Free();
					origHandle = GCHandle.Alloc(Original, GCHandleType.Normal);

					// Target To Middleman
					*(byte*)Target = 0xE9;
					*(uint*)(Target + 1) = HelperMethods.CalculateRelativeAddressForJmp((uint)Target, (uint)middleman);

					for (int n = 0; n < numBytes - 5; n++)
						*(byte*)((Target + 5) + n) = 0x90;


					Protection.SetPageProtection(Target, numBytes, oldProtection, out _);
					IsEnabled = true;
					return IsEnabled;
				}

				public bool Uninstall()
				{
					if (!IsEnabled)
						return false;


					IsEnabled = false;
					return IsEnabled;
				}
			}
			public unsafe class RegisterStates
			{
				// Manually saving values of all general purpose registers and EFLAGS
				// Instead of using PushAD/PushFD (and PopAD & PopFD)

				public IntPtr BaseAddress;

				public byte[] PushAdBytes { get; private set; }
				public byte[] PopAdBytes { get; private set; }
				public byte[] PopFdBytes { get; private set; }
				public byte[] PushFdBytes { get; private set; }

				public Structures.Registers* RegisterStructPointer { get; private set; }

				public RegisterStates()
				{
					BaseAddress = Allocator.Unmanaged.Allocate((uint)Marshal.SizeOf<Structures.Registers>(),
						Enums.AllocationType.Commit | Enums.AllocationType.Reserve,
						Enums.MemoryProtection.ReadWrite);
					if (BaseAddress == IntPtr.Zero)
						throw new InvalidOperationException();

					RegisterStructPointer = (Structures.Registers*)BaseAddress;
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
					$"mov dword ptr [0x{BaseAddress.ToInt32() + 28:X8}], esp"
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

			public unsafe class Callback
			{
				public readonly IntPtr InstallLocation;
				public bool IsInstalled { get; private set; }

				private readonly int _count;
				private byte[] _orig;
				private readonly IntPtr _func;
				private readonly GCHandle _funcGC;
				private readonly RegisterStates _registerstates;

				public Callback(IntPtr location, Delegate callback, int numBytesOverwrite = 5)
				{
					if (location == IntPtr.Zero)
						throw new InvalidOperationException("");

					if (numBytesOverwrite < 5)
						throw new InvalidOperationException("");


					InstallLocation = location;
					_count = numBytesOverwrite;
					_func = Marshal.GetFunctionPointerForDelegate(callback);
					_funcGC = GCHandle.Alloc(_func);

					_registerstates = new RegisterStates();
					_registerstates.GeneratePushAd();
					_registerstates.GeneratePopAd();
					_registerstates.GeneratePushFd();
					_registerstates.GeneratePopFd();
				}

				public void Install()
				{
					if (IsInstalled)
						return;

					if (_orig == null)
						_orig = Reader.ReadBytes(InstallLocation, (uint) _count);

					/*
					IntPtr region = Allocator.Unmanaged.Extended.AllocateHeap((uint) b.Length
					                                                          + (uint) _registerstates.PushAdBytes.Length
					                                                          + (uint) _registerstates.PopAdBytes.Length
					                                                          //+ (uint) _registerstates.PushFdBytes.Length
					                                                          //+ (uint) _registerstates.PopFdBytes.Length
					                                                          + 5);
					*/

					IntPtr region = Allocator.Unmanaged.Allocate((uint) _registerstates.PushAdBytes.Length
					                                             + (uint)_registerstates.PushFdBytes.Length
																 + (uint) _registerstates.PopAdBytes.Length
					                                             + (uint)_registerstates.PopFdBytes.Length
																 + 5);
					if (region == null) throw new InvalidOperationException();

					Writer.WriteBytes(region, _orig);
					uint start = (uint) region + (uint) _orig.Length;

					Writer.WriteBytes((IntPtr) start, _registerstates.PushFdBytes);
					start += (uint) _registerstates.PushFdBytes.Length;

					Writer.WriteBytes((IntPtr) start, _registerstates.PushAdBytes);
					start += (uint) _registerstates.PushAdBytes.Length;

					Assembler.AssembleMnemonics(new []
					{
						// Pushing our register address as our first param to
						// our callback method
						$"push 0x{(_registerstates.BaseAddress).ToInt32():X8}",
					}, true, out byte[] push);

					Writer.WriteBytes((IntPtr)start, push);
					start += (uint) push.Length;

					*(byte*) start = 0xE8;
					*(uint*) (start + 1) = CalculateRelativeAddressForJmp(start, (uint) _func); // call to callback
					start += 5;

					Writer.WriteBytes((IntPtr)start, new byte[] { 0x58 }); // pop eax
					start += 1;

					Writer.WriteBytes((IntPtr)start, _registerstates.PopFdBytes);
					start += (uint)_registerstates.PopFdBytes.Length;

					Writer.WriteBytes((IntPtr)start, _registerstates.PopAdBytes);
					start += (uint) _registerstates.PopAdBytes.Length;

					*(byte*)start = 0xE9;
					*(uint*) (start + 1) = CalculateRelativeAddressForJmp(start, (uint)InstallLocation + (uint)_count);

					if (!Protection.SetPageProtection(InstallLocation, _count, Enums.MemoryProtection.ExecuteReadWrite, out var old))
					{
						Allocator.Unmanaged.FreeMemory(region);
						return;
					}

					*(byte*)InstallLocation = 0xE9;
					*(uint*)(InstallLocation + 1) = CalculateRelativeAddressForJmp((uint)InstallLocation, (uint)region);

					for (int n = 0; n < _count - 5; n++)
						*(byte*)(InstallLocation + 5 + n) = 0x90;
					Protection.SetPageProtection(InstallLocation, _count, old, out _);
					IsInstalled = true;
				}

				public void Uninstall()
				{
					if (!IsInstalled)
						return;

					Protection.SetPageProtection(InstallLocation, _count, Enums.MemoryProtection.ExecuteReadWrite, out var old);
					Writer.WriteBytes(InstallLocation, _orig);
					Protection.SetPageProtection(InstallLocation, _count, old, out _);
				}
			}

			public class HookObj<T> : IDisposable where T : Delegate
			{
				private bool _disposed;
				private GCHandle _selfGcHandle;

				public bool IsImplemented { get; private set; } = false;

				public readonly IntPtr TargetAddress;

				public T UnmodifiedOriginalFunction;
				private GCHandle _unmodifiedOriginalFunctionDelegateGcHandle;

				public readonly T HookMethod;
				public readonly IntPtr HookMethodAddress;
				private GCHandle _hookMethodGcHandle;

				private readonly uint _overwrittenByteCount;
				private List<IntPtr> _unmanagedAllocations;

				public HookObj(IntPtr targetAddress, T hkDelegate, uint optionalPrologueLengthFixup = 5, bool implementImmediately = false)
				{
					if (targetAddress == IntPtr.Zero || hkDelegate == null || optionalPrologueLengthFixup < 5)
						throw new InvalidOperationException($"write something here");

					if (Environment.Is64BitProcess)
						throw new InvalidOperationException($"No 64Bit support has been Implemented!");

					TargetAddress = targetAddress;
					UnmodifiedOriginalFunction = Marshal.GetDelegateForFunctionPointer<T>(targetAddress);
					_unmodifiedOriginalFunctionDelegateGcHandle = GCHandle.Alloc(UnmodifiedOriginalFunction/*, GCHandleType.Pinned*/);

					_overwrittenByteCount = optionalPrologueLengthFixup;

					HookMethod = hkDelegate;
					_hookMethodGcHandle = GCHandle.Alloc(HookMethod/*, GCHandleType.Pinned*/);
					HookMethodAddress = Marshal.GetFunctionPointerForDelegate(HookMethod);

					_unmanagedAllocations = new List<IntPtr>();

					_selfGcHandle = GCHandle.Alloc(this/*, GCHandleType.Pinned*/);

					if (implementImmediately)
						Install();
				}

				public unsafe bool Install()
				{
					if (_disposed) throw new NullReferenceException($"This object has been explicitly disposed already!");

					if (!IsImplemented)
					{
						if (!Protection.SetPageProtection(TargetAddress, (int)_overwrittenByteCount, Enums.MemoryProtection.ExecuteReadWrite, out var oldProtect))
							return false;

						IntPtr addrMiddleMan = IntPtr.Zero;
						IntPtr addrOFuncAddress = IntPtr.Zero;

						if (_unmanagedAllocations != null && _unmanagedAllocations.Count == 2)
						{
							addrMiddleMan = _unmanagedAllocations[0];
							addrOFuncAddress = _unmanagedAllocations[1];

							// Maybe extra check to see if the actual values used are zero
							// or not before actually using them?
						}
						else
						{
							addrMiddleMan = Allocator.Unmanaged.Allocate(_overwrittenByteCount + 5);
							addrOFuncAddress = Allocator.Unmanaged.Allocate(_overwrittenByteCount + 5);

							if (addrMiddleMan == IntPtr.Zero || addrOFuncAddress == IntPtr.Zero)
							{
								Protection.SetPageProtection(TargetAddress, (int)_overwrittenByteCount, oldProtect, out _);
								return false;
							}

							_unmanagedAllocations = new List<IntPtr> { addrMiddleMan, addrOFuncAddress };
						}

						IntPtr addrHook = Marshal.GetFunctionPointerForDelegate(HookMethod);

						if (addrMiddleMan == IntPtr.Zero ||
							addrOFuncAddress == IntPtr.Zero ||
							addrHook == IntPtr.Zero)
						{
							Protection.SetPageProtection(TargetAddress, (int)_overwrittenByteCount, oldProtect, out _);
							if (addrMiddleMan != IntPtr.Zero)
							{
								Allocator.Unmanaged.FreeMemory(addrMiddleMan);
								_unmanagedAllocations.Remove(addrMiddleMan);
							}
							if (addrOFuncAddress != IntPtr.Zero)
							{
								Allocator.Unmanaged.FreeMemory(addrOFuncAddress);
								_unmanagedAllocations.Remove(addrOFuncAddress);
							}
							return false;
						}

						// Middleman -> Hook
						for (int n = 0; n < _overwrittenByteCount; n++)
							*(byte*)(addrMiddleMan + n) = *(byte*)(TargetAddress + n);

						*(byte*)((uint)addrMiddleMan + _overwrittenByteCount) = 0xE9;
						*(uint*)((uint)addrMiddleMan + _overwrittenByteCount + 1) = HelperMethods.CalculateRelativeAddressForJmp(
							(uint)addrMiddleMan + _overwrittenByteCount,
							(uint)addrHook);

						// Original Function Address Copy Region -> (Target Address + optionalPrologueLength)
						for (int n = 0; n < _overwrittenByteCount; n++)
							*(byte*)(addrOFuncAddress + n) = *(byte*)(TargetAddress + n);

						*(byte*)((uint)addrOFuncAddress + _overwrittenByteCount) = 0xE9;
						*(uint*)((uint)addrOFuncAddress + _overwrittenByteCount + 1) = HelperMethods.CalculateRelativeAddressForJmp(
							(uint)addrOFuncAddress + _overwrittenByteCount,
							(uint)TargetAddress + _overwrittenByteCount);

						// Target -> Middleman
						*(byte*)TargetAddress = 0xE9;
						*(uint*)(TargetAddress + 1) = HelperMethods.CalculateRelativeAddressForJmp((uint)TargetAddress, (uint)addrMiddleMan);

						for (int n = 0; n < _overwrittenByteCount - 5; n++)
							*(byte*)(TargetAddress + 5 + n) = 0x90;

						Protection.SetPageProtection(TargetAddress, (int)_overwrittenByteCount, oldProtect, out _);

						UnmodifiedOriginalFunction = Marshal.GetDelegateForFunctionPointer<T>(_unmanagedAllocations[1]);
						if (_unmodifiedOriginalFunctionDelegateGcHandle.IsAllocated)
							_unmodifiedOriginalFunctionDelegateGcHandle.Free();

						_unmodifiedOriginalFunctionDelegateGcHandle = GCHandle.Alloc(_unmodifiedOriginalFunctionDelegateGcHandle, GCHandleType.Pinned);

						IsImplemented = true;
						return IsImplemented;
					}

					return true;
				}
				public unsafe bool Uninstall()
				{
					if (_disposed) throw new NullReferenceException($"This object has been explicitly disposed already!");

					if (IsImplemented)
					{
						// Restore
						if (!Protection.SetPageProtection(TargetAddress, (int)_overwrittenByteCount, Enums.MemoryProtection.ExecuteReadWrite, out var oldProtect))
							return false;

						for (int n = 0; n < _overwrittenByteCount; n++)
							*(byte*)(TargetAddress + n) = *(byte*)(_unmanagedAllocations[1] + n);

						Protection.SetPageProtection(TargetAddress, (int)_overwrittenByteCount, oldProtect, out _);

						UnmodifiedOriginalFunction = Marshal.GetDelegateForFunctionPointer<T>(TargetAddress);
						if (_unmodifiedOriginalFunctionDelegateGcHandle.IsAllocated)
							_unmodifiedOriginalFunctionDelegateGcHandle.Free();

						_unmodifiedOriginalFunctionDelegateGcHandle = GCHandle.Alloc(UnmodifiedOriginalFunction, GCHandleType.Pinned);

						IsImplemented = false;
						return !IsImplemented;
					}

					return false;
				}

				public void Dispose()
				{
					Dispose(true);
					GC.SuppressFinalize(this);
				}
				protected virtual void Dispose(bool disposing)
				{
					if (!_disposed && disposing)
					{
						if (IsImplemented)
							Uninstall();

						if (_unmanagedAllocations != null)
							foreach (IntPtr allocationBaseAddress in _unmanagedAllocations)
								Allocator.Unmanaged.FreeMemory(allocationBaseAddress);

						if (_hookMethodGcHandle.IsAllocated)
							_hookMethodGcHandle.Free();

						if (_unmodifiedOriginalFunctionDelegateGcHandle.IsAllocated)
							_unmodifiedOriginalFunctionDelegateGcHandle.Free();

						if (_selfGcHandle.IsAllocated)
							_selfGcHandle.Free();

						_disposed = true;
					}
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

			public static unsafe List<long> FindPattern(string pattern, string optionalModuleName = "", bool readable = true, bool writable = true, bool executable = true)
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
				if (optionalModuleName != "")
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
			public static bool AssembleMnemonics(string[] assemblycode, bool isX86, out byte[] assembled)
			{
				if (assemblycode == null || assemblycode.Length < 1) throw new Exception("bla bla bla shit went wrong");
				
				var request = (HttpWebRequest)WebRequest.Create("https://defuse.ca/online-x86-assembler.htm");

				var postData = $"instructions={string.Join("\n", assemblycode)}";
				postData += $"&arch={(isX86 ? "x86" : "x64")}";
				postData += "&submit=Assemble";

				var data = Encoding.UTF8.GetBytes(postData);

				request.Method = "POST";
				request.Credentials = CredentialCache.DefaultCredentials;
				request.Accept = "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.9";
				request.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/80.0.3987.122 Safari/537.3";
				request.Referer = "https://defuse.ca/online-x86-assembler.htm";
				request.ContentType = "application/x-www-form-urlencoded";
				request.ContentLength = data.Length;
				request.CachePolicy = new RequestCachePolicy(RequestCacheLevel.NoCacheNoStore);
				request.Host = "defuse.ca";
				request.KeepAlive = true;
				request.AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip;

				request.Headers.Add("Upgrade-Insecure-Requests", @"1");
				request.Headers.Add("DNT", @"1");
				request.Headers.Add("Origin", "https://defuse.ca");

				request.Headers.Set(HttpRequestHeader.AcceptEncoding, "gzip, deflate, br");
				request.Headers.Set(HttpRequestHeader.AcceptLanguage, "en-SE,en;q=0.9,sv-SE;q=0.8,sv;q=0.7,en-US;q=0.6");

				using (var stream = request.GetRequestStream())
				{
					stream.Write(data, 0, data.Length);
				}

				var response = (HttpWebResponse)request.GetResponse();
				var responseString = new StreamReader(response.GetResponseStream() ?? throw new InvalidOperationException("ResponseStream was null!")).ReadToEnd();

				try
				{
					int startIdx = responseString.IndexOf("{ ", StringComparison.Ordinal);
					int endIdx = responseString.IndexOf(" }                </p>", StringComparison.Ordinal);
					string extracted = responseString.Substring(startIdx + 2, endIdx - startIdx - 2);
					string[] arr = extracted.Split(',');

					List<byte> ret = new List<byte>();
					foreach (string strByte in arr)
					{
						ret.Add(Convert.ToByte(strByte.Replace(" ", string.Empty), 16));
					}

					assembled = ret.ToArray();
					return response.StatusCode == HttpStatusCode.OK && responseString.Contains("Array Literal") && assembled.Length > 0;
				}
				catch
				{
					if (responseString.Contains("Error:")) {
						// Parse out error code
						string errorText = responseString.Substring(
							responseString.IndexOf("Error: "),
							responseString.IndexOf("</div>"));

						throw new Exception(errorText);
					}
					assembled = null;
					return false;
				}
			}
		}

		public class Allocator
		{
			public class SmartAllocation
			{
				public readonly IntPtr Base;

				public uint BytesLeft;
				public IntPtr UnallocatedPositionStart;

				public List<(IntPtr InnerAllocationBase, string Identifier)> Allocations;

				public SmartAllocation()
				{
					Base = Unmanaged.Allocate(0x10000);
					if (Base == IntPtr.Zero)
						throw new Exception();

					UnallocatedPositionStart = Base;
					BytesLeft = 0x10000;
					Allocations = new List<(IntPtr InnerAllocationBase, string Identifier)>();
				}

				public IntPtr Allocate(uint numBytes, string identifier)
				{
					if (numBytes == 0 || numBytes > BytesLeft || BytesLeft < 1)
						throw new Exception($"Cannot allocate");

					IntPtr start = UnallocatedPositionStart;

					UnallocatedPositionStart = IntPtr.Add(UnallocatedPositionStart, (int)numBytes);
					BytesLeft -= numBytes;

					Allocations.Add((start, identifier));
					return start;
				}
 			}

			public class Managed
			{
				public static IntPtr ManagedAllocate(int size)
					=> Marshal.AllocHGlobal(size);

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

				public string PrintRegisters()
				{
					string ret = "";
					ret += $"\nEAX: 0x{EAX:X8}\n";
					ret += $"EBX: 0x{EBX:X8}\n";
					ret += $"ECX: 0x{ECX:X8}\n";
					ret += $"EDX: 0x{EDX:X8}\n";
					ret += $"EDI: 0x{EDI:X8}\n";
					ret += $"ESI: 0x{ESI:X8}\n";
					ret += $"EBP: 0x{EBP:X8}\n";
					ret += $"ESP: 0x{ESP:X8}\n";
					ret += $"EFLAGS: 0x{EFLAGS:X8}\n";
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

		public static uint CalculateRelativeAddressForJmp(uint from, uint to)
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

		public static byte[] FromHexIDA(this byte[] destination, string IStyleHexString)
		{
			if (string.IsNullOrEmpty(IStyleHexString))
				return null;
			// Expects CE styled string array

			string[] split = IStyleHexString.TrimStart().TrimEnd().Split(new [] { "\\x" }, StringSplitOptions.None);
			byte[] ret = new byte[split.Length];

			for (int n = 0; n < split.Length; n++)
			{
				ret[n] = Convert.ToByte(split[n], 16);
			}


			return ret;
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
