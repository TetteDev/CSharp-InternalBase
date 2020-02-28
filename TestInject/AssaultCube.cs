using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using static TestInject.DebugConsole;

namespace TestInject
{
	public class AssaultCube
	{
		public class Delegates
		{
			[UnmanagedFunctionPointer(CallingConvention.StdCall)]
			public delegate int GetPlayerEntityInCrosshairDelegate();

			[UnmanagedFunctionPointer(CallingConvention.Winapi)]
			public delegate void glVertex3fDelegate(float x, float y, float z);

			[UnmanagedFunctionPointer(CallingConvention.Winapi)]
			public delegate void glEndDelegate();
		}

		[StructLayout(LayoutKind.Explicit)]
		public unsafe struct PlayerEntity
		{
			[FieldOffset(0x0)]
			public uint vTable;

			[FieldOffset(0x0004)]
			public Memory.Structures.Vector3 Head;

			[FieldOffset(0x0010)]
			public Memory.Structures.Vector3 Velocity;

			[FieldOffset(0x0034)]
			public Memory.Structures.Vector3 Origin;

			[FieldOffset(0x0040)]
			public Memory.Structures.Vector3 ViewAngles;

			[FieldOffset(0x004C)]
			public float PitchVelocity; //0x004C

			[FieldOffset(0x0050)]
			public float MaxSpeed; //0x0050

			[FieldOffset(0x0068)]
			public readonly bool IsJumping;

			[FieldOffset(0x0069)]
			public bool IsOnGround;

			[FieldOffset(0x0070)]
			public bool IsNotInGame;

			[FieldOffset(0x0071)]
			public byte IsScoping;

			[FieldOffset(0x00F8)]
			public int Health;

			[FieldOffset(0x00FC)]
			public int Armor;

			[FieldOffset(0x0224)]
			public byte IsShooting;

			[FieldOffset(0x0225)]
			public uint PlayerNameAddress;

			[FieldOffset(0x032C)]
			public CTeam Team;

			[FieldOffset(0x0338)]
			public CState State;

			[FieldOffset(0x0374)]
			public WeaponEntity* WeaponPtr;

			public void SetOrigin(Memory.Structures.Vector3 newOrigin) => Origin = newOrigin;
			public void SetViewAngles(Memory.Structures.Vector3 newViewangles) => ViewAngles = newViewangles;
			public void AimAt(PlayerEntity* ent)
			{
				if (ent == null)
					return;

				Memory.Structures.Vector3 vDelta = ent->Head - Head;
				vDelta.Normalize();

				ViewAngles = new Memory.Structures.Vector3((float)-Math.Atan2(vDelta.X, vDelta.Y) / (float)Math.PI * 180f - 180f,
					(float)Math.Atan2(vDelta.Z, Math.Sqrt(vDelta.X * vDelta.X + vDelta.Y * vDelta.Y)) / (float)Math.PI * 180, 
					ViewAngles.Z);
			}
			public bool IsInMyTeam(PlayerEntity* compareEntity)
			{
				CGameMode dwGameMode = *(CGameMode*)0x50F49C;
				return
					(dwGameMode == CGameMode.GMODE_BOTTEAMONESHOTONKILL ||
					 dwGameMode == CGameMode.GMODE_TEAMONESHOTONEKILL ||
					 dwGameMode == CGameMode.GMODE_BOTTEAMDEATHMATCH ||
					 dwGameMode == CGameMode.GMODE_TEAMDEATHMATCH ||
					 dwGameMode == CGameMode.GMODE_TEAMSURVIVOR ||
					 dwGameMode == CGameMode.GMODE_TEAMLSS ||
					 dwGameMode == CGameMode.GMODE_CTF ||
					 dwGameMode == CGameMode.GMODE_TEAMKEEPTHEFLAG ||
					 dwGameMode == CGameMode.GMODE_HUNTTHEFLAG ||
					 dwGameMode == CGameMode.GMODE_TEAMPF ||
					 dwGameMode == CGameMode.GMODE_BOTTEAMSURVIVOR ||
					 dwGameMode == CGameMode.GMODE_BOTTEAMONESHOTONKILL) && compareEntity->Team == Team;
			}
		}

		public static unsafe class EntityList
		{
			public static List<IntPtr> PlayerList = new List<IntPtr>();
			public static bool UpdatePlayerList()
			{
				if (PlayerList == null)
					PlayerList = new List<IntPtr>();

				if (PlayerList.Count > 0)
					PlayerList.Clear();

				uint entityBaseAddress = (*(uint*) 0x50F4F8) + 0x4;
				if (entityBaseAddress == 0)
					return false;
				for (int n = 0; n < ((*(int*) 0x0050F500) - 1) * 0x4; n += 0x4)
					PlayerList.Add(new IntPtr(*(uint*)(entityBaseAddress + n)));

				return true;
			}

			public static PlayerEntity* GetClosestToEntity(PlayerEntity* compareEntity)
			{
				if (PlayerList == null || PlayerList.Count < 1 || compareEntity == null)
					return null;

				float closestDist = 999999f;
				PlayerEntity* closestPlayer = null;

				foreach (var plr in PlayerList)
				{
					PlayerEntity* currPlayer = (PlayerEntity*)plr;
					if (compareEntity->IsInMyTeam(currPlayer))
						continue;

					float dst = currPlayer->Origin.Distance3D(compareEntity->Origin);
					if (dst < closestDist)
					{
						closestDist = dst;
						closestPlayer = currPlayer;
					}
				}

				return closestPlayer;
			}

			public static PlayerEntity* GetClosestEntityToCrosshair(PlayerEntity* localplayer)
			{
				if (PlayerList == null || PlayerList.Count < 1 || localplayer == null)
					return null;

				float closestDist = 999999f;
				PlayerEntity* closestPlayer = null;

				int* w = (int*)0x00510C94;
				int* h = (int*)0x00510C98;

				Memory.Structures.D3DMATRIX* viewMatrix = (Memory.Structures.D3DMATRIX*)0x00501AE8;
				Memory.Structures.Vector crossHairPos = new Memory.Structures.Vector(*w / 2f, *h / 2f);

				foreach (var plr in PlayerList)
				{
					PlayerEntity* currPlayer = (PlayerEntity*)plr;
					if (localplayer->IsInMyTeam(currPlayer) 
					    || localplayer->State != CState.CS_ALIVE
					    || localplayer->Health < 1
					    || currPlayer->Health < 1
					    || currPlayer->State != CState.CS_ALIVE)
						continue;

					if (currPlayer->Head.World2Screen(viewMatrix->AsArray(), out var screenPos)
					&& currPlayer->Origin.World2Screen(viewMatrix->AsArray(), out _))
					{
						float dist = screenPos.Distance(crossHairPos);

						if (dist < closestDist)
							closestPlayer = currPlayer;
					}
				}

				return closestPlayer;
			}
		}

		[StructLayout(LayoutKind.Explicit)]
		public unsafe struct WeaponEntity
		{
			[FieldOffset(0x0004)]
			public CWeaponType WeaponType;

			[FieldOffset(0x0008)]
			public PlayerEntity* Owner;

			[FieldOffset(0x00C)]
			public uint* WeaponNamePointer;

			[FieldOffset(0x010)]
			public AmmoInformation* AmmoInfo;
		}

		[StructLayout(LayoutKind.Explicit)]
		public unsafe struct AmmoInformation
		{
			[FieldOffset(0x0000)]
			public int BackupAmmoCount;

			[FieldOffset(0x0028)]
			public int CurrentAmmoCount;

			[FieldOffset(0x0050)]
			public int CurrentWeaponActionDelayMilliseconds;

			[FieldOffset(0x0078)]
			public int TotalCountShotsFired;
		}

		public enum CWeaponType : int
		{
			Knife = 0,
			Pistol = 1,
			Carbine = 2,
			Shotgun = 3,
			SubmachineGun = 4,
			Sniper = 5,
			Rifle = 6,
			Unk = 7,
			Grenade = 8,
			PistolAkimbo = 9
		}

		public enum CState
		{
			CS_ALIVE = 0, CS_DEAD, CS_WAITING, CS_EDITING,
		}

		public enum CTeam
		{
			TEAM_CLA = 0, TEAM_RVSF, TEAM_CLA_SPECT, TEAM_RVSF_SPECT, TEAM_SPECT, TEAM_NUM
		}

		public enum CGameMode : int
		{
			GMODE_DEMO = -1,
			GMODE_TEAMDEATHMATCH = 0,
			GMODE_COOPEDIT,
			GMODE_DEATHMATCH,
			GMODE_SURVIVOR,
			GMODE_TEAMSURVIVOR,
			GMODE_CTF,
			GMODE_PISTOLFRENZY,
			GMODE_BOTTEAMDEATHMATCH,
			GMODE_BOTDEATHMATCH,
			GMODE_LASTSWISSSTANDING,
			GMODE_ONESHOTONEKILL,
			GMODE_TEAMONESHOTONEKILL,
			GMODE_BOTONESHOTONEKILL,
			GMODE_HUNTTHEFLAG,
			GMODE_TEAMKEEPTHEFLAG,
			GMODE_KEEPTHEFLAG,
			GMODE_TEAMPF,
			GMODE_TEAMLSS,
			GMODE_BOTPISTOLFRENZY,
			GMODE_BOTLSS,
			GMODE_BOTTEAMSURVIVOR,
			GMODE_BOTTEAMONESHOTONKILL,
			GMODE_NUM
		};

	}
}
