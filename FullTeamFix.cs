using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Serilog.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;

namespace FullTeamFix;


public class FullTeamFix : BasePlugin
{
    public override string ModuleName => "FullTeamFix";
    public override string ModuleVersion => "0.0.1";
    public override string ModuleAuthor => "ShookEagle";
    public override string ModuleDescription => "Fixes Full teams by adding a spwan on a pre-existing one.";
    public Random Random = new Random();
    public enum JoinTeamReason
    {
        TeamsFull = 1,
        TerroristTeamFull = 2,
        CTTeamFull = 3,
        TTeamLimit = 7,
        CTTeamLimit = 8
    }
    public int TerroristSpawns = -1;
    public int CTSpawns = -1;
    public bool respectlimitteams = true;
    private Dictionary<CCSPlayerController, int> SelectedTeam = new Dictionary<CCSPlayerController, int>();
    public class CustomSpawnPoint
    {
        public CsTeam Team { get; set; }
        public required string Origin { get; set; }
        public required string Angle { get; set; }
    }
    public override void Load(bool hotReload)
    {
        RegisterListener<Listeners.OnMapStart>(OnMapStart);
        AddCommandListener("jointeam", Command_Jointeam, HookMode.Pre);
    }

    public HookResult Command_Jointeam(CCSPlayerController? player, CommandInfo info)
    {
        if (player != null && player.IsValid && info.ArgCount > 0 && info.ArgByIndex(0).ToLower() == "jointeam")
        {
            if (info.ArgCount > 1)
            {
                string teamArg = info.ArgByIndex(1);

                if (int.TryParse(teamArg, out int teamId))
                {
                    if (teamId >= (int)CsTeam.Spectator && teamId <= (int)CsTeam.CounterTerrorist)
                    {
                        SelectedTeam[player] = teamId;
                    }
                }
                else
                {
                    Console.WriteLine("Failed to parse team ID.");
                }
            }
        }
        return HookResult.Continue;
    }

    [GameEventHandler(HookMode.Pre)]
    public HookResult TeamJoinFailed(EventJointeamFailed @event, GameEventInfo info)
    {
        CCSPlayerController? player = @event.Userid;
        if (player == null || !player.IsValid)
        {
            return HookResult.Continue;
        }

        JoinTeamReason m_eReason = (JoinTeamReason)@event.Reason;
        int iTs = Utilities.GetPlayers().Where(p => p.Team == CsTeam.Terrorist).Count();
        int iCTs = Utilities.GetPlayers().Where(p => p.Team == CsTeam.CounterTerrorist).Count();

        if (!SelectedTeam.ContainsKey(player))
        {
            SelectedTeam[player] = 0;
        }

        switch (m_eReason)
        {
            case JoinTeamReason.TeamsFull:

                if (iCTs >= CTSpawns && iTs >= TerroristSpawns)
                {
                    NewSpawnFromDefault("t");
                    NewSpawnFromDefault("ct");
                    return HookResult.Continue;
                }

                break;

            case JoinTeamReason.TerroristTeamFull:
                if (iTs >= TerroristSpawns)
                {
                    return NewSpawnFromDefault("t") ? HookResult.Continue : HookResult.Stop;
                }

                break;

            case JoinTeamReason.CTTeamFull:
                if (iCTs >= CTSpawns)
                {
                    return NewSpawnFromDefault("ct") ? HookResult.Continue : HookResult.Stop;
                }

                break;

            default:
                {
                    return HookResult.Continue;
                }
        }

        return HookResult.Continue;
    }

    public void OnMapStart(string mapname)
    {
        AddTimer(0.1f, () =>
        {
            TerroristSpawns = 0;
            CTSpawns = 0;

            var tSpawns = Utilities.FindAllEntitiesByDesignerName<SpawnPoint>("info_player_terrorist");
            var ctSpawns = Utilities.FindAllEntitiesByDesignerName<SpawnPoint>("info_player_counterterrorist");

            foreach (var spawn in tSpawns)
            {
                TerroristSpawns++;
            }

            foreach (var spawn in ctSpawns)
            {
                CTSpawns++;
            }
        });
    }

    public bool NewSpawnFromDefault(string team)
    {
        var ctspawns = Utilities.FindAllEntitiesByDesignerName<SpawnPoint>("info_player_counterterrorist").ToList();
        var tspawns = Utilities.FindAllEntitiesByDesignerName<SpawnPoint>("info_player_terrorist").ToList();
        if (team == "ct" && ctspawns.Count == 0)
        {
            Logger.LogError("This map contains no CT spawns.");
            return false;
        }
        if (team == "t" && tspawns.Count == 0)
        {
            Logger.LogError("This map contains no T spawns.");
            return false;
        }
        var spawn = (team == "ct") ? ctspawns[Random.Next(ctspawns.Count)] : tspawns[Random.Next(tspawns.Count)];
        var origin = spawn.AbsOrigin;
        var angle = spawn.AbsRotation;
        if (origin == null || angle == null)
        {
            Logger.LogError("Origin or angle not found when attempting to create a spawn.");
            return false;
        }

        var point = new CustomSpawnPoint
        {
            Team = team == "ct" ? CsTeam.CounterTerrorist : CsTeam.Terrorist,
            Origin = VectorToString(new Vector3(origin.X, origin.Y, origin.Z)),
            Angle = VectorToString(new Vector3(angle.X, angle.Y, angle.Z))
        };

        if (CreateEntity(point))
        {
            if (team == "ct") CTSpawns++;
            else TerroristSpawns++;

            Logger.LogInformation($"New {team} spawn created.");
            return true;
        }
        else
        {
            Logger.LogInformation("Spawn creation failed.");
            return false;
        }
    }

    public bool CreateEntity(CustomSpawnPoint spawnPoint)
    {
        var noVel = new Vector(0f, 0f, 0f);
        SpawnPoint? entity;
        if (spawnPoint.Team == CsTeam.Terrorist)
        {
            entity = Utilities.CreateEntityByName<CInfoPlayerTerrorist>("info_player_terrorist");
        }
        else
        {
            entity = Utilities.CreateEntityByName<CInfoPlayerCounterterrorist>("info_player_counterterrorist");
        }
        if (entity == null)
        {
            return false;
        }
        var angle = StringToVector(spawnPoint.Angle);
        entity.Teleport(NormalVectorToValve(StringToVector(spawnPoint.Origin)), new QAngle(angle.X, angle.Y, angle.Z), noVel);
        entity.DispatchSpawn();
        return true;
    }

    private static string VectorToString(Vector3 vec)
    {
        return $"{vec.X}|{vec.Y}|{vec.Z}";
    }

    private static Vector3 StringToVector(string str)
    {
        var explode = str.Split("|");
        return new Vector3(x: float.Parse(explode[0]), y: float.Parse(explode[1]), z: float.Parse(explode[2]));
    }

    private static Vector NormalVectorToValve(Vector3 v)
    {
        return new Vector(v.X, v.Y, v.Z);
    }
    public static int GetTeamPlayerCount(CsTeam team)
    {
        return Utilities.GetPlayers().Count(p => p.Team == team);
    }
}
