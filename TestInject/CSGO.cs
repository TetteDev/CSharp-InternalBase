using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using static TestInject.CSGO.Structures;
using static TestInject.Memory.Structures;

namespace TestInject
{
	public class CSGO
	{
		public class Modules
		{
			public static IntPtr Engine = IntPtr.Zero;
			public static IntPtr Client = IntPtr.Zero;

			public static bool UpdateModules()
			{
				Engine = Memory.Modules.GetModuleBaseAddress("engine.dll");
				Client = Memory.Modules.GetModuleBaseAddress("client_panorama.dll");

				if (Engine == IntPtr.Zero || Client == IntPtr.Zero) return false;

				Console.WriteLine($"Engine.dll Base: 0x{Engine.ToInt32():X8}");
				Console.WriteLine($"Client_Panorama.dll Base: 0x{Client.ToInt32():X8}");
				return true;
			}
		}

		public class Methods
		{
			public static unsafe LocalPlayer_t* GetLocalPlayer()
				=> (LocalPlayer_t*)*(uint*)(Modules.Client + Offsets.signatures.dwLocalPlayer);

			public static unsafe ClientState_t* GetClientState()
				=> (ClientState_t*)*(uint*)(Modules.Engine + Offsets.signatures.dwClientState);

			public static unsafe GlobalVars_t* GetGlobalVars()
				=> (GlobalVars_t*)*(uint*)(Modules.Engine + Offsets.signatures.dwGlobalVars);
		}

		public class Structures
		{
			[StructLayout(LayoutKind.Explicit)]
			public unsafe struct LocalPlayer_t
			{
				[FieldOffset(Offsets.netvars.m_lifeState)] public int LifeState;

				[FieldOffset(Offsets.netvars.m_iHealth)] public int Health;

				[FieldOffset(Offsets.netvars.m_fFlags)] public int Flags;

				[FieldOffset(Offsets.netvars.m_iTeamNum)] public int Team;

				[FieldOffset(Offsets.netvars.m_iShotsFired)] public int ShotsFired;

				[FieldOffset(Offsets.netvars.m_iCrosshairId)] public int CrosshairID;

				[FieldOffset(Offsets.signatures.m_bDormant)] public bool Dormant;

				[FieldOffset(Offsets.netvars.m_MoveType)] public int MoveType;

				[FieldOffset(Offsets.netvars.m_vecOrigin)] public Memory.Structures.Vector3 Position;

				[FieldOffset(Offsets.netvars.m_aimPunchAngle)] public Memory.Structures.Vector3 AimPunch;

				[FieldOffset(Offsets.netvars.m_vecViewOffset)] public Memory.Structures.Vector3 VecView;

				[FieldOffset(Offsets.netvars.m_bIsScoped)] public bool IsScoped;

				[FieldOffset(Offsets.netvars.m_nTickBase)] public int TickBase;

				[FieldOffset(Offsets.netvars.m_iFOV)] public int FOV;

				[FieldOffset(Offsets.netvars.m_iObserverMode)] public int ShittyThirdperson;

				[FieldOffset(Offsets.netvars.m_hActiveWeapon)] public int ActiveWeapon;

				[FieldOffset(Offsets.netvars.m_flFlashDuration)] public float FlashDuration;
			}

			[StructLayout(LayoutKind.Explicit)]
			public unsafe struct ClientState_t
			{
				[FieldOffset(Offsets.signatures.dwClientState_State)] public Enums.GameState GameState;

				[FieldOffset(Offsets.signatures.dwClientState_MaxPlayer)] public int MaxPlayers;

				[FieldOffset(Offsets.signatures.dwClientState_ViewAngles)] public Vector3 ViewAngles;

				[FieldOffset(Offsets.signatures.dwClientState_IsHLTV)] public int IsHLTV;

				[FieldOffset(Offsets.signatures.dwClientState_Map)] public int Map;

				[FieldOffset(Offsets.signatures.dwClientState_MapDirectory)] public int MapDirectory;

				[FieldOffset(Offsets.signatures.dwClientState_PlayerInfo)] public int PlayerInfo;
			}

			[StructLayout(LayoutKind.Explicit)]
			public unsafe struct GlobalVars_t
			{
				[FieldOffset(0x0000)] public float RealTime; // 0x00

				[FieldOffset(0x0004)] public int FrameCount;

				[FieldOffset(0x0008)] public float AbsoluteFrametime;

				[FieldOffset(0x000C)] public float AbsoluteFrameStartTimestddev;

				[FieldOffset(0x0010)] public float Curtime;

				[FieldOffset(0x0014)] public float Frametime;

				[FieldOffset(0x0018)] public int MaxClients;

				[FieldOffset(0x001c)] public int TickCount;

				[FieldOffset(0x0020)] public float Interval_Per_Tick;

				[FieldOffset(0x0024)] public float Interpolation_Amount;

				[FieldOffset(0x0028)] public int SimTicksThisFrame;

				[FieldOffset(0x002c)] public int Network_Protocol;

				[FieldOffset(0x0030)] public IntPtr pSaveData;

				[FieldOffset(0x0031)] public bool m_bClient;

				[FieldOffset(0x0032)] public bool m_bRemoteClient;

				[FieldOffset(0x0036)] public int nTimestampNetworkingBase;

				[FieldOffset(0x003A)] public int nTimestampRandomizeWindow;
			}

			[StructLayout(LayoutKind.Explicit)]
			public struct Enemy_t
			{
				[FieldOffset(Offsets.netvars.m_iHealth)] public int Health;

				[FieldOffset(Offsets.netvars.m_iTeamNum)] public int Team;

				[FieldOffset(Offsets.signatures.m_bDormant)] public bool Dormant;

				[FieldOffset(Offsets.netvars.m_bSpotted)] public bool Spotted;

				[FieldOffset(Offsets.netvars.m_bSpottedByMask)] public bool SpottedByMask;

				[FieldOffset(Offsets.netvars.m_vecOrigin)] public Vector3 Origin;
			}
		}

		public class Enums
		{
			public enum GameState
			{
				MENU = 0,
				GAME = 6
			}
		}

		public class Offsets
		{
			public const Int32 timestamp = 1581596373;
			public static class netvars
			{
				public const Int32 cs_gamerules_data = 0x0;
				public const Int32 m_ArmorValue = 0xB368;
				public const Int32 m_Collision = 0x320;
				public const Int32 m_CollisionGroup = 0x474;
				public const Int32 m_Local = 0x2FBC;
				public const Int32 m_MoveType = 0x25C;
				public const Int32 m_OriginalOwnerXuidHigh = 0x31B4;
				public const Int32 m_OriginalOwnerXuidLow = 0x31B0;
				public const Int32 m_SurvivalGameRuleDecisionTypes = 0x1320;
				public const Int32 m_SurvivalRules = 0xCF8;
				public const Int32 m_aimPunchAngle = 0x302C;
				public const Int32 m_aimPunchAngleVel = 0x3038;
				public const Int32 m_angEyeAnglesX = 0xB36C;
				public const Int32 m_angEyeAnglesY = 0xB370;
				public const Int32 m_bBombPlanted = 0x99D;
				public const Int32 m_bFreezePeriod = 0x20;
				public const Int32 m_bGunGameImmunity = 0x3930;
				public const Int32 m_bHasDefuser = 0xB378;
				public const Int32 m_bHasHelmet = 0xB35C;
				public const Int32 m_bInReload = 0x3295;
				public const Int32 m_bIsDefusing = 0x391C;
				public const Int32 m_bIsQueuedMatchmaking = 0x74;
				public const Int32 m_bIsScoped = 0x3914;
				public const Int32 m_bIsValveDS = 0x75;
				public const Int32 m_bSpotted = 0x93D;
				public const Int32 m_bSpottedByMask = 0x980;
				public const Int32 m_bStartedArming = 0x33E0;
				public const Int32 m_bUseCustomAutoExposureMax = 0x9D9;
				public const Int32 m_bUseCustomAutoExposureMin = 0x9D8;
				public const Int32 m_bUseCustomBloomScale = 0x9DA;
				public const Int32 m_clrRender = 0x70;
				public const Int32 m_dwBoneMatrix = 0x26A8;
				public const Int32 m_fAccuracyPenalty = 0x3320;
				public const Int32 m_fFlags = 0x104;
				public const Int32 m_flC4Blow = 0x2990;
				public const Int32 m_flCustomAutoExposureMax = 0x9E0;
				public const Int32 m_flCustomAutoExposureMin = 0x9DC;
				public const Int32 m_flCustomBloomScale = 0x9E4;
				public const Int32 m_flDefuseCountDown = 0x29AC;
				public const Int32 m_flDefuseLength = 0x29A8;
				public const Int32 m_flFallbackWear = 0x31C0;
				public const Int32 m_flFlashDuration = 0xA410;
				public const Int32 m_flFlashMaxAlpha = 0xA40C;
				public const Int32 m_flLastBoneSetupTime = 0x2924;
				public const Int32 m_flLowerBodyYawTarget = 0x3A7C;
				public const Int32 m_flNextAttack = 0x2D70;
				public const Int32 m_flNextPrimaryAttack = 0x3228;
				public const Int32 m_flSimulationTime = 0x268;
				public const Int32 m_flTimerLength = 0x2994;
				public const Int32 m_hActiveWeapon = 0x2EF8;
				public const Int32 m_hMyWeapons = 0x2DF8;
				public const Int32 m_hObserverTarget = 0x3388;
				public const Int32 m_hOwner = 0x29CC;
				public const Int32 m_hOwnerEntity = 0x14C;
				public const Int32 m_iAccountID = 0x2FC8;
				public const Int32 m_iClip1 = 0x3254;
				public const Int32 m_iCompetitiveRanking = 0x1A84;
				public const Int32 m_iCompetitiveWins = 0x1B88;
				public const Int32 m_iCrosshairId = 0xB3D4;
				public const Int32 m_iEntityQuality = 0x2FAC;
				public const Int32 m_iFOV = 0x31E4;
				public const Int32 m_iFOVStart = 0x31E8;
				public const Int32 m_iGlowIndex = 0xA428;
				public const Int32 m_iHealth = 0x100;
				public const Int32 m_iItemDefinitionIndex = 0x2FAA;
				public const Int32 m_iItemIDHigh = 0x2FC0;
				public const Int32 m_iMostRecentModelBoneCounter = 0x2690;
				public const Int32 m_iObserverMode = 0x3374;
				public const Int32 m_iShotsFired = 0xA380;
				public const Int32 m_iState = 0x3248;
				public const Int32 m_iTeamNum = 0xF4;
				public const Int32 m_lifeState = 0x25F;
				public const Int32 m_nFallbackPaintKit = 0x31B8;
				public const Int32 m_nFallbackSeed = 0x31BC;
				public const Int32 m_nFallbackStatTrak = 0x31C4;
				public const Int32 m_nForceBone = 0x268C;
				public const Int32 m_nTickBase = 0x342C;
				public const Int32 m_rgflCoordinateFrame = 0x444;
				public const Int32 m_szCustomName = 0x303C;
				public const Int32 m_szLastPlaceName = 0x35B0;
				public const Int32 m_thirdPersonViewAngles = 0x31D8;
				public const Int32 m_vecOrigin = 0x138;
				public const Int32 m_vecVelocity = 0x114;
				public const Int32 m_vecViewOffset = 0x108;
				public const Int32 m_viewPunchAngle = 0x3020;
			}
			public static class signatures
			{
				public const Int32 anim_overlays = 0x2980;
				public const Int32 clientstate_choked_commands = 0x4D28;
				public const Int32 clientstate_delta_ticks = 0x174;
				public const Int32 clientstate_last_outgoing_command = 0x4D24;
				public const Int32 clientstate_net_channel = 0x9C;
				public const Int32 convar_name_hash_table = 0x2F0F8;
				public const Int32 dwClientState = 0x588D9C;
				public const Int32 dwClientState_GetLocalPlayer = 0x180;
				public const Int32 dwClientState_IsHLTV = 0x4D40;
				public const Int32 dwClientState_Map = 0x28C;
				public const Int32 dwClientState_MapDirectory = 0x188;
				public const Int32 dwClientState_MaxPlayer = 0x388;
				public const Int32 dwClientState_PlayerInfo = 0x52B8;
				public const Int32 dwClientState_State = 0x108;
				public const Int32 dwClientState_ViewAngles = 0x4D88;
				public const Int32 dwEntityList = 0x4D3C7BC;
				public const Int32 dwForceAttack = 0x316DD80;
				public const Int32 dwForceAttack2 = 0x316DD8C;
				public const Int32 dwForceBackward = 0x316DDD4;
				public const Int32 dwForceForward = 0x316DDB0;
				public const Int32 dwForceJump = 0x51E0004;
				public const Int32 dwForceLeft = 0x316DDC8;
				public const Int32 dwForceRight = 0x316DDEC;
				public const Int32 dwGameDir = 0x6274F8;
				public const Int32 dwGameRulesProxy = 0x52532EC;
				public const Int32 dwGetAllClasses = 0xD4ED9C;
				public const Int32 dwGlobalVars = 0x588AA0;
				public const Int32 dwGlowObjectManager = 0x527DFA0;
				public const Int32 dwInput = 0x5187980;
				public const Int32 dwInterfaceLinkList = 0x8F4084;
				public const Int32 dwLocalPlayer = 0xD28B74;
				public const Int32 dwMouseEnable = 0xD2E718;
				public const Int32 dwMouseEnablePtr = 0xD2E6E8;
				public const Int32 dwPlayerResource = 0x316C10C;
				public const Int32 dwRadarBase = 0x517152C;
				public const Int32 dwSensitivity = 0xD2E5B4;
				public const Int32 dwSensitivityPtr = 0xD2E588;
				public const Int32 dwSetClanTag = 0x89D60;
				public const Int32 dwViewMatrix = 0x4D2E0E4;
				public const Int32 dwWeaponTable = 0x5188440;
				public const Int32 dwWeaponTableIndex = 0x324C;
				public const Int32 dwYawPtr = 0xD2E378;
				public const Int32 dwZoomSensitivityRatioPtr = 0xD33598;
				public const Int32 dwbSendPackets = 0xD386A;
				public const Int32 dwppDirect3DDevice9 = 0xA6030;
				public const Int32 find_hud_element = 0x26B0BD40;
				public const Int32 force_update_spectator_glow = 0x398642;
				public const Int32 interface_engine_cvar = 0x3E9EC;
				public const Int32 is_c4_owner = 0x3A4A70;
				public const Int32 m_bDormant = 0xED;
				public const Int32 m_flSpawnTime = 0xA360;
				public const Int32 m_pStudioHdr = 0x294C;
				public const Int32 m_pitchClassPtr = 0x51717D0;
				public const Int32 m_yawClassPtr = 0xD2E378;
				public const Int32 model_ambient_min = 0x58BDBC;
				public const Int32 set_abs_angles = 0x1CED30;
				public const Int32 set_abs_origin = 0x1CEB70;
			}
		}
	}
}

