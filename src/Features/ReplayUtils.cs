/*
Copyright (C) 2024 Dea Brcka

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.
This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.
You should have received a copy of the GNU General Public License
along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/

using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core; // For CBeam, CBaseEntity, etc.
using CounterStrikeSharp.API.Modules.Utils; // For Vector
using CounterStrikeSharp.API.Modules.Entities.Constants;
using System.Text.Json;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;
// Removed: using System.Drawing; // For Color (no longer needed for CParticleSystem)

namespace SharpTimer
{
    public partial class SharpTimer
    {
        private void ReplayUpdate(CCSPlayerController player, int timerTicks)
        {
            try
            {
                if (!IsAllowedPlayer(player)) return;

                // Get the player's current position and rotation
                ReplayVector currentPosition = ReplayVector.GetVectorish(player.Pawn.Value!.CBodyComponent?.SceneNode?.AbsOrigin ?? new Vector(0, 0, 0));
                ReplayVector currentSpeed = ReplayVector.GetVectorish(player.PlayerPawn.Value!.AbsVelocity ?? new Vector(0, 0, 0));
                ReplayQAngle currentRotation = ReplayQAngle.GetQAngleish(player.PlayerPawn.Value.EyeAngles ?? new QAngle(0, 0, 0));

                var buttons = player.Buttons;
                var flags = player.Pawn.Value.Flags;
                var moveType = player.Pawn.Value.MoveType;

                var ReplayFrame = new PlayerReplays.ReplayFrames
                {
                    Position = currentPosition,
                    Rotation = currentRotation,
                    Speed = currentSpeed,
                    Buttons = buttons,
                    Flags = flags,
                    MoveType = moveType
                };

                playerReplays[player.Slot].replayFrames.Add(ReplayFrame);
            }
            catch (Exception ex)
            {
                SharpTimerError($"Error in ReplayUpdate: {ex.Message}");
            }
        }

        private void ReplayPlayback(CCSPlayerController player, int plackbackTick)
        {
            try
            {
                if (!IsAllowedPlayer(player)) return;

                //player.LerpTime = 0.0078125f;

                if (playerTimers.TryGetValue(player.Slot, out PlayerTimerInfo? value))
                {
                    
                    var replayFrame = playerReplays[player.Slot].replayFrames[plackbackTick];

                    if (((PlayerFlags)replayFrame.Flags & PlayerFlags.FL_ONGROUND) != 0)
                    {
                        SetMoveType(player, MoveType_t.MOVETYPE_WALK);
                    }
                    else
                    {
                        SetMoveType(player, MoveType_t.MOVETYPE_OBSERVER);
                    }

                    if (((PlayerFlags)replayFrame.Flags & PlayerFlags.FL_DUCKING) != 0)
                    {
                        value.MovementService!.DuckAmount = 1;
                    }
                    else
                    {
                        value.MovementService!.DuckAmount = 0;
                    }

                    player.PlayerPawn.Value!.Teleport(ReplayVector.ToVector(replayFrame.Position!), ReplayQAngle.ToQAngle(replayFrame.Rotation!), ReplayVector.ToVector(replayFrame.Speed!));

                    var replayButtons = $"{((replayFrame.Buttons & PlayerButtons.Moveleft) != 0 ? "A" : "_")} " +
                                        $"{((replayFrame.Buttons & PlayerButtons.Forward) != 0 ? "W" : "_")} " +
                                        $"{((replayFrame.Buttons & PlayerButtons.Moveright) != 0 ? "D" : "_")} " +
                                        $"{((replayFrame.Buttons & PlayerButtons.Back) != 0 ? "S" : "_")} " +
                                        $"{((replayFrame.Buttons & PlayerButtons.Jump) != 0 ? "J" : "_")} " +
                                        $"{((replayFrame.Buttons & PlayerButtons.Duck) != 0 ? "C" : "_")}";

                    if (value.HideKeys != true && value.IsReplaying == true && keysOverlayEnabled == true)
                    {
                        player.PrintToCenter(replayButtons);
                    }

                    if (plackbackTick > 0) // Ensure there's a previous frame
                    {
                        var currentFrameData = playerReplays[player.Slot].replayFrames[plackbackTick];
                        var previousFrameData = playerReplays[player.Slot].replayFrames[plackbackTick - 1];

                        if (currentFrameData != null && currentFrameData.Position != null && previousFrameData != null && previousFrameData.Position != null)
                        {
                            Vector currentPos = ReplayVector.ToVector(currentFrameData.Position);
                            Vector previousPos = ReplayVector.ToVector(previousFrameData.Position);

                            if (currentPos != null && previousPos != null && currentPos != previousPos) // Ensure positions are valid and distinct
                            {
                                // (previousPos and currentPos are assumed to be available as Vector objects)
                                // We'll spawn the particle effect at the currentPos of the replay frame.

                                try
                                {
                                    var particleSystem = Utilities.CreateEntityByName<CParticleSystem>("info_particle_system");
                                    if (particleSystem == null || !particleSystem.IsValid)
                                    {
                                        SharpTimerError($"Failed to create CParticleSystem entity for replay trail.");
                                        return; // Exit this attempt if creation failed
                                    }

                                    // User will need to change this path to their desired .vpcf file
                                    particleSystem.EffectName = "particles/ambient_fx/ambient_sparks_glow.vpcf"; // Default from Trails example

                                    // Teleport the particle system to the current replay position before starting it.
                                    // This makes the particle effect emit from this point.
                                    particleSystem.Teleport(currentPos, new QAngle(0, 0, 0), new Vector(0, 0, 0));
                                    
                                    particleSystem.DispatchSpawn();
                                    particleSystem.AcceptInput("Start"); // Start emitting particles

                                    // Add the particle system to the list for tracking
                                    playerReplays[player.Slot].replayParticleSystems.Add(particleSystem);

                                    // Lifetime management for this particle burst
                                    float particleLifetime = 2.0f; // Default lifetime in seconds, user might want to configure this later
                                    AddTimer(particleLifetime, () =>
                                    {
                                        if (particleSystem != null && particleSystem.IsValid)
                                        {
                                            // Optional: particleSystem.AcceptInput("Stop"); // May not be needed if Remove is sufficient
                                            particleSystem.Remove();
                                        }
                                    });
                                    
                                    // Note: The Trails example also had particle.AcceptInput("FollowEntity", ...);
                                    // For replays, simply spawning a short-lived effect at each point might be visually better
                                    // than trying to make one system follow the ghost, unless the ghost is a proper entity to follow.
                                    // The current approach creates a burst at each point.
                                }
                                catch (Exception ex)
                                {
                                    SharpTimerError($"Error creating CParticleSystem trail in ReplayPlayback: {ex.Message}");
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                SharpTimerError($"Error in ReplayPlayback: {ex.Message}");
            }
        }

        private void ReplayPlay(CCSPlayerController player)
        {
            try
            {
                int totalFrames = playerReplays[player.Slot].replayFrames.Count;

                if (playerReplays[player.Slot].CurrentPlaybackFrame >= totalFrames || playerReplays[player.Slot].CurrentPlaybackFrame < 0) // Check if playback is at or beyond the end, or invalid
                {
                    SharpTimerDebug($"Replay for player {player.PlayerName} (Slot: {player.Slot}) finished. CurrentFrame: {playerReplays[player.Slot].CurrentPlaybackFrame}, TotalFrames: {totalFrames}. Cleaning visuals.");
                    ClearReplayVisuals(player.Slot); // Updated call
                    
                    // Stop further replay actions for this player
                    playerTimers[player.Slot].IsReplaying = false; 
                    // Potentially call OnRecordingStop(player); if that contains other necessary "stop replay" logic
                    // For now, focus on IsReplaying = false and beam clear.
                    // Resetting CurrentPlaybackFrame to 0 might be done if a "view last replay again" feature exists,
                    // but for a single playthrough, it's done.
                    playerReplays[player.Slot].CurrentPlaybackFrame = 0; // Reset for any future replay.
                    
                    return; // Stop further execution in this tick if replay ended.
                }

                // This check seems problematic if totalFrames can be low for valid replays.
                // if (totalFrames <= 128) 
                // {
                //     OnRecordingStop(player); // This also sets IsRecordingReplay = false.
                // }

                if (jumpStatsEnabled) InvalidateJS(player.Slot);
                ReplayPlayback(player, playerReplays[player.Slot].CurrentPlaybackFrame); // Draw current frame's beams

                playerReplays[player.Slot].CurrentPlaybackFrame++; // Advance to next frame
            }
            catch (Exception ex)
            {
                SharpTimerError($"Error in ReplayPlay: {ex.Message}");
            }
        }

        private void OnRecordingStart(CCSPlayerController player, int bonusX = 0, int style = 0)
        {
            try
            {
                // Call the centralized cleanup function for the slot
                ClearReplayVisuals(player.Slot); 

                // Original lines from OnRecordingStart should follow:
                playerReplays.Remove(player.Slot); 
                playerReplays[player.Slot] = new PlayerReplays // This creates a new PlayerReplays instance with an empty replayBeams list.
                {
                    BonusX = bonusX,
                    Style = style
                };
                playerTimers[player.Slot].IsRecordingReplay = true;
            }
            catch (Exception ex)
            {
                SharpTimerError($"Error in OnRecordingStart: {ex.Message}");
            }
        }

        private void OnRecordingStop(CCSPlayerController player)
        {
            try
            {
                playerTimers[player.Slot].IsRecordingReplay = false;
                SetMoveType(player, MoveType_t.MOVETYPE_WALK);
            }
            catch (Exception ex)
            {
                SharpTimerError($"Error in OnRecordingStop: {ex.Message}");
            }
        }

        public async Task DumpReplayToJson(CCSPlayerController player, string steamID, int playerSlot, int bonusX = 0, int style = 0)
        {
            await Task.Run(() =>
            {
                if (!IsAllowedPlayer(player))
                {
                    SharpTimerError($"Error in DumpReplayToJson: Player not allowed or not on server anymore");
                    return;
                }

                string fileName = $"{steamID}_replay.json";
                string playerReplaysDirectory;
                if (style != 0) playerReplaysDirectory = Path.Join(gameDir, "csgo", "cfg", "SharpTimer", "PlayerReplayData", bonusX == 0 ? $"{currentMapName}" : $"{currentMapName}_bonus{bonusX}", GetNamedStyle(style));
                else playerReplaysDirectory = Path.Join(gameDir, "csgo", "cfg", "SharpTimer", "PlayerReplayData", bonusX == 0 ? $"{currentMapName}" : $"{currentMapName}_bonus{bonusX}");
                string playerReplaysPath = Path.Join(playerReplaysDirectory, fileName);

                try
                {
                    if (!Directory.Exists(playerReplaysDirectory))
                    {
                        Directory.CreateDirectory(playerReplaysDirectory);
                    }

                    if (playerReplays[playerSlot].replayFrames.Count >= maxReplayFrames) return;

                    var indexedReplayFrames = playerReplays[playerSlot].replayFrames
                        .Select((frame, index) => new IndexedReplayFrames { Index = index, Frame = frame })
                        .ToList();

                    using (Stream stream = new FileStream(playerReplaysPath, FileMode.Create))
                    {
                        JsonSerializer.Serialize(stream, indexedReplayFrames);
                    }
                }
                catch (Exception ex)
                {
                    SharpTimerError($"Error during serialization: {ex.Message}");
                }
            });
        }

        public async Task<string> GetReplayJson(CCSPlayerController player, int playerSlot)
        {
            if (!IsAllowedPlayer(player))
            {
                SharpTimerError($"Error in GetReplayJson: Player not allowed or not on server anymore");
                return "";
            }

            try
            {
                if (playerReplays[playerSlot].replayFrames.Count >= maxReplayFrames) return "";

                var indexedReplayFrames = playerReplays[playerSlot].replayFrames
                    .Select((frame, index) => new IndexedReplayFrames { Index = index, Frame = frame })
                    .ToList();

                return JsonSerializer.Serialize(indexedReplayFrames);
            }
            catch (Exception ex)
            {
                SharpTimerError($"Error during serialization: {ex.Message}");
                return "";
            }
        }

        private async Task ReadReplayFromJson(CCSPlayerController player, string steamId, int playerSlot, int bonusX = 0, int style = 0)
        {
            string fileName = $"{steamId}_replay.json";
            string playerReplaysPath;
            if (style != 0) playerReplaysPath = Path.Join(gameDir, "csgo", "cfg", "SharpTimer", "PlayerReplayData", bonusX == 0 ? currentMapName : $"{currentMapName}_bonus{bonusX}", GetNamedStyle(style), fileName);
            else playerReplaysPath = Path.Join(gameDir, "csgo", "cfg", "SharpTimer", "PlayerReplayData", bonusX == 0 ? currentMapName : $"{currentMapName}_bonus{bonusX}", fileName);

            try
            {
                if (File.Exists(playerReplaysPath))
                {
                    var jsonString = await File.ReadAllTextAsync(playerReplaysPath);
                    if (!jsonString.Contains("PositionString"))
                    {
                        var indexedReplayFrames = JsonSerializer.Deserialize<List<IndexedReplayFrames>>(jsonString);

                        if (indexedReplayFrames != null)
                        {
                            var replayFrames = indexedReplayFrames
                                .OrderBy(frame => frame.Index)
                                .Select(frame => frame.Frame)
                                .ToList();

                            if (!playerReplays.TryGetValue(playerSlot, out PlayerReplays? value))
                            {
                                value = new PlayerReplays();
                                playerReplays[playerSlot] = value;
                            }

                            value.replayFrames = replayFrames!;
                        }
                    }
                    else
                    {
                        Server.NextFrame(() => { PrintToChat(player, $"Unsupported replay format"); });
                    }
                }
                else
                {
                    SharpTimerError($"File does not exist: {playerReplaysPath}");
                    Server.NextFrame(() => PrintToChat(player, Localizer["replay_dont_exist"]));
                }
            }
            catch (Exception ex)
            {
                SharpTimerError($"Error during deserialization: {ex.Message}");
            }
        }

        private async Task ReadReplayFromGlobal(CCSPlayerController player, int recordId, int style, int bonusX = 0)
        {
            string currentMapFull = bonusX == 0 ? currentMapName! : $"{currentMapName}_bonus{bonusX}";
            var payload = new
            {
                record_id = recordId,
                map_name = currentMapFull,
                style = style
            };

            try
            {

                var jsonString = await GetReplayFromGlobal(payload);
                var indexedReplayFrames = JsonSerializer.Deserialize<List<IndexedReplayFrames>>(jsonString);

                if (indexedReplayFrames != null)
                {
                    var replayFrames = indexedReplayFrames
                        .OrderBy(frame => frame.Index)
                        .Select(frame => frame.Frame)
                        .ToList();

                    if (!playerReplays.TryGetValue(player.Slot, out PlayerReplays? value))
                    {
                        value = new PlayerReplays();
                        playerReplays[player.Slot] = value;
                    }

                    value.replayFrames = replayFrames!;
                }
            }
            catch (Exception ex)
            {
                SharpTimerError($"Error during deserialization: {ex.Message}");
            }
        }

        private async Task SpawnReplayBot()
        {
            try
            {
                if (await CheckSRReplay() != true) return;

                Server.NextFrame(() =>
                {
                    startKickingAllFuckingBotsExceptReplayOneIFuckingHateValveDogshitFuckingCompanySmile = false;
                    foreach (CCSPlayerController bot in connectedReplayBots.Values.ToList())
                    {
                        if (bot != null)
                        {
                            OnPlayerDisconnect(bot, true);
                            if (connectedReplayBots.TryGetValue(bot.Slot, out var someValue)) connectedReplayBots.Remove(bot.Slot);
                        }
                    }
                    Server.ExecuteCommand("sv_cheats 1");
                    Server.ExecuteCommand("bot_add_ct");
                    Server.ExecuteCommand("bot_quota 1");
                    Server.ExecuteCommand("bot_quota_mode 0");
                    Server.ExecuteCommand("bot_stop 1");
                    Server.ExecuteCommand("bot_freeze 1");
                    Server.ExecuteCommand("bot_zombie 1");
                    Server.ExecuteCommand("bot_chatter off");
                    Server.ExecuteCommand("sv_cheats 0");

                    AddTimer(3.0f, () =>
                    {
                        foundReplayBot = false;
                        SharpTimerDebug($"Trying to find replay bot!");
                        var playerEntities = Utilities.FindAllEntitiesByDesignerName<CCSPlayerController>("cs_player_controller");
                        foreach (var tempPlayer in playerEntities)
                        {
                            if (tempPlayer == null || !tempPlayer.IsValid || !tempPlayer.IsBot || tempPlayer.IsHLTV)
                                continue;
                            if (tempPlayer.UserId.HasValue)
                            {
                                if (foundReplayBot == true)
                                {
                                    OnPlayerDisconnect(tempPlayer, true);
                                    Server.ExecuteCommand($"kickid {tempPlayer.Slot}");
                                    SharpTimerDebug($"Kicking unused replay bot!");
                                }
                                else
                                {
                                    SharpTimerDebug($"Found replay bot!");
                                    OnReplayBotConnect(tempPlayer);
                                    tempPlayer.PlayerPawn.Value!.Bot!.IsSleeping = true;
                                    tempPlayer.PlayerPawn.Value!.Bot!.AllowActive = true;
                                    tempPlayer.RemoveWeapons();
                                    tempPlayer!.Pawn.Value!.Collision.CollisionAttribute.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_DISSOLVING;
                                    tempPlayer!.Pawn.Value!.Collision.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_DISSOLVING;
                                    Utilities.SetStateChanged(tempPlayer, "CCollisionProperty", "m_CollisionGroup");
                                    Utilities.SetStateChanged(tempPlayer, "CCollisionProperty", "m_collisionAttribute");
                                    SharpTimerDebug($"Removed Collison for replay bot!");
                                    foundReplayBot = true;
                                    startKickingAllFuckingBotsExceptReplayOneIFuckingHateValveDogshitFuckingCompanySmile = true;
                                }
                            }
                        }
                    });
                });
            }
            catch (Exception ex)
            {
                SharpTimerError($"Error in SpawnReplayBot: {ex.Message}");
            }
        }

        private void OnReplayBotConnect(CCSPlayerController bot)
        {
            try
            {
                var botSlot = bot.Slot;
                var botName = bot.PlayerName;
                
                if(bot.IsHLTV)
                    return;

                AddTimer(3.0f, () =>
                {
                    OnPlayerConnect(bot, true);
                    connectedReplayBots[botSlot] = new CCSPlayerController(bot.Handle);
                    ChangePlayerName(bot, replayBotName);
                    playerTimers[botSlot].IsTimerBlocked = true;
                    _ = Task.Run(async () => await ReplayHandler(bot, botSlot));
                    SharpTimerDebug($"Starting replay for {botName}");
                });
            }
            catch (Exception ex)
            {
                SharpTimerError($"Error in OnReplayBotConnect: {ex.Message}");
            }
        }

        public async Task<bool> CheckSRReplay(string topSteamID = "x", int bonusX = 0, int style = 0)
        {
            var (srSteamID, srPlayerName, srTime) = ("null", "null", "null");

            if (enableDb)
            {
                (srSteamID, srPlayerName, srTime) = await GetMapRecordSteamIDFromDatabase(bonusX);
            }
            else
            {
                (srSteamID, srPlayerName, srTime) = await GetMapRecordSteamID(bonusX);
            }

            if ((srSteamID == "null" || srPlayerName == "null" || srTime == "null") && topSteamID != "x") return false;

            string fileName = $"{(topSteamID == "x" ? $"{srSteamID}" : $"{topSteamID}")}_replay.json";
            string playerReplaysPath;
            if (style != 0) playerReplaysPath = Path.Join(gameDir, "csgo", "cfg", "SharpTimer", "PlayerReplayData", (bonusX == 0 ? currentMapName : $"{currentMapName}_bonus{bonusX}"), GetNamedStyle(style), fileName);
            else playerReplaysPath = Path.Join(gameDir, "csgo", "cfg", "SharpTimer", "PlayerReplayData", (bonusX == 0 ? currentMapName : $"{currentMapName}_bonus{bonusX}"), fileName);

            try
            {
                if (File.Exists(playerReplaysPath))
                {
                    var jsonString = await File.ReadAllTextAsync(playerReplaysPath);
                    if (!jsonString.Contains("PositionString"))
                    {
                        var indexedReplayFrames = JsonSerializer.Deserialize<List<IndexedReplayFrames>>(jsonString);

                        if (indexedReplayFrames != null)
                        {
                            return true;
                        }
                        return false;
                    }
                    else
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during deserialization: {ex.Message}");
                return false;
            }
        }

        private void ClearReplayVisuals(int playerSlot)
        {
            if (playerReplays.TryGetValue(playerSlot, out PlayerReplays? replayData) && replayData != null)
            {
                // Cleanup Particle Systems
                if (replayData.replayParticleSystems != null && replayData.replayParticleSystems.Count > 0)
                {
                    SharpTimerDebug($"Clearing {replayData.replayParticleSystems.Count} particle systems for slot {playerSlot}.");
                    foreach (var particleSystem in replayData.replayParticleSystems)
                    {
                        if (particleSystem != null && particleSystem.IsValid)
                        {
                            // particleSystem.AcceptInput("Stop"); // Optional: attempt to stop emission before removal
                            particleSystem.Remove();
                        }
                    }
                    replayData.replayParticleSystems.Clear();
                }

                // Cleanup Beams (if replayBeams list still exists and is managed)
                if (replayData.replayBeams != null && replayData.replayBeams.Count > 0)
                {
                    SharpTimerDebug($"Clearing {replayData.replayBeams.Count} beams for slot {playerSlot} (if any).");
                    foreach (var beam in replayData.replayBeams) // Assuming replayBeams is List<CEnvBeam>
                    {
                        if (beam != null && beam.IsValid)
                        {
                            beam.Remove();
                        }
                    }
                    replayData.replayBeams.Clear();
                }
            }
        }
    }
}