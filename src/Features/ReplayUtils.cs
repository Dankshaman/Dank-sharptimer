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
using System.Drawing; // For Color

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

                    if (plackbackTick > 0) // Ensure there's a previous frame and process every 10th frame
                    {
                        var currentFrameData = playerReplays[player.Slot].replayFrames[plackbackTick];
                        var previousFrameData = playerReplays[player.Slot].replayFrames[plackbackTick - 2];

                        if (currentFrameData != null && currentFrameData.Position != null && previousFrameData != null && previousFrameData.Position != null && currentFrameData.Speed != null)
                        {
                            Vector currentPos = ReplayVector.ToVector(currentFrameData.Position);
                            Vector previousPos = ReplayVector.ToVector(previousFrameData.Position);
                            Vector currentSpeedVec = ReplayVector.ToVector(currentFrameData.Speed);

                            if (currentPos != null && previousPos != null && currentPos != previousPos) // Ensure positions are valid and distinct
                            {
                                try
                                {
                                    var beam = Utilities.CreateEntityByName<CEnvBeam>("env_beam");
                                    if (beam == null || !beam.IsValid)
                                    {
                                        SharpTimerError($"Failed to create CEnvBeam entity for replay trail.");
                                        return;
                                    }

                                    // Start Position
                                    beam.Teleport(previousPos, new QAngle(0, 0, 0), new Vector(0, 0, 0));

                                    // End Position (component-wise assignment)
                                    if (beam.EndPos != null)
                                    {
                                        beam.EndPos.X = currentPos.X;
                                        beam.EndPos.Y = currentPos.Y;
                                        beam.EndPos.Z = currentPos.Z;
                                    }
                                    else
                                    {
                                        SharpTimerError($"beam.EndPos was null for CEnvBeam entity. Cannot set components for trail.");
                                        if (beam.IsValid) beam.Remove(); // Clean up partially formed beam
                                        return;
                                    }
                                    Utilities.SetStateChanged(beam, "CBeam", "m_vecEndPos"); // Use "CBeam" as per original working version for EndPos

                                    // Visual Properties - Dynamic Beam Color Logic
                                    if (SharpTimer.replayBeamColorDynamicEnabled && SharpTimer.ReplayBeamVelocityThresholds.Count > 0 && SharpTimer.ReplayBeamColors.Count == SharpTimer.ReplayBeamVelocityThresholds.Count)
                                    {
                                        float speed = SharpTimer.use2DSpeed ? new Vector(currentSpeedVec.X, currentSpeedVec.Y, 0).Length() : currentSpeedVec.Length();
                                        Color beamColor = SharpTimer.ReplayBeamColors[0]; // Default to the first color

                                        for (int i = 0; i < SharpTimer.ReplayBeamVelocityThresholds.Count; i++)
                                        {
                                            if (speed >= SharpTimer.ReplayBeamVelocityThresholds[i])
                                            {
                                                beamColor = SharpTimer.ReplayBeamColors[i];
                                            }
                                            else
                                            {
                                                // If speed is less than the current threshold, use the color from the previous threshold (or the first if this is the first threshold)
                                                // However, the loop structure ensures `beamColor` is already set to the highest met threshold's color.
                                                // So, if speed is less, the current beamColor (from a lower or initial threshold) is correct.
                                                // We can break if we want the first matching threshold from low to high, but current logic implies highest matched.
                                                // For "highest met threshold", we simply continue and overwrite.
                                                // For "first met threshold" (color for 500-999, then 1000-1499 etc):
                                                // beamColor = SharpTimer.ReplayBeamColors[i]; break; // if we want this behavior
                                            }
                                        }
                                        beam.Render = beamColor;
                                    }
                                    else
                                    {
                                        // Fallback to existing logic if dynamic is disabled or misconfigured
                                        if (SharpTimer.replayBotTrailCustomColorEnabled)
                                        {
                                            beam.Render = Color.FromArgb(255, SharpTimer.replayBotTrailColorR, SharpTimer.replayBotTrailColorG, SharpTimer.replayBotTrailColorB);
                                        }
                                        else
                                        {
                                            beam.Render = Color.FromArgb(255, 255, 255, 0); // Default yellow
                                        }
                                    }

                                    beam.Width = SharpTimer.replayBotTrailWidth;
                                    // Utilities.SetStateChanged(beam, "CBeam", "m_flWidth"); // REMOVED

                                    beam.DispatchSpawn();

                                    // Add to list for persistent trails
                                    playerReplays[player.Slot].replayBeams.Add(beam);

                                    // Determine lifetime for this beam segment
                                    float beamSegmentLifetime = 2.0f;
                                    string lifetimeSource = "default"; // Renamed from lifetimeSourceInfo for consistency
                                    if (currentMapRecordTimeSeconds > 0.0f)
                                    {
                                        beamSegmentLifetime = currentMapRecordTimeSeconds; // REVERTED
                                        lifetimeSource = $"SR ({currentMapRecordTimeSeconds}s)";
                                    }
                                    SharpTimerDebug($"Beam lifetime for {player.PlayerName}: {beamSegmentLifetime}s (Source: {lifetimeSource}). currentMapRecordTimeSeconds is {currentMapRecordTimeSeconds}s.");

                                    // Schedule removal of the beam entity AND its reference from the list
                                    var playerSlotForTimer = player.Slot;
                                    var beamToRemove = beam;

                                    AddTimer(beamSegmentLifetime, () =>
                                    {
                                        if (beamToRemove != null && beamToRemove.IsValid)
                                        {
                                            beamToRemove.Remove();
                                        }

                                        if (playerReplays.TryGetValue(playerSlotForTimer, out PlayerReplays? replayData) && replayData != null && replayData.replayBeams != null)
                                        {
                                            replayData.replayBeams.Remove(beamToRemove);
                                        }
                                    });
                                }
                                catch (Exception ex)
                                {
                                    SharpTimerError($"Error creating CEnvBeam trail in ReplayPlayback: {ex.Message}");
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
            if (playerTimers.TryGetValue(player.Slot, out var timerInfo) && timerInfo.IsReplaying) // Check if it's a replaying bot
            {
                SharpTimerDebug($"ReplayPlay called for {player.PlayerName}. CurrentPlaybackFrame: {playerReplays[player.Slot].CurrentPlaybackFrame}. currentMapRecordTimeSeconds: {currentMapRecordTimeSeconds}s");
            }
            try
            {
                int totalFrames = playerReplays[player.Slot].replayFrames.Count;

                if (totalFrames <= 128) // User's version includes this check
                {
                    OnRecordingStop(player); // This sets IsRecordingReplay = false and MoveType.
                }

                if (playerReplays[player.Slot].CurrentPlaybackFrame < 0 || playerReplays[player.Slot].CurrentPlaybackFrame >= totalFrames)
                {
                    // User's version does not ClearReplayVisuals here. If looping, this means trails will accumulate.
                    playerReplays[player.Slot].CurrentPlaybackFrame = 0;
                    Action<CCSPlayerController?, float, bool> adjustVelocity = use2DSpeed ? AdjustPlayerVelocity2D : AdjustPlayerVelocity;
                    adjustVelocity(player, 0, false); // Resets velocity.
                    // No 'return;' here and IsReplaying is not set to false, so it will loop immediately.
                }

                if (jumpStatsEnabled) InvalidateJS(player.Slot);
                ReplayPlayback(player, playerReplays[player.Slot].CurrentPlaybackFrame); // Play current frame & draw visuals

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

                if (bot.IsHLTV)
                    return;

                SharpTimerDebug($"OnReplayBotConnect for bot {bot.PlayerName} (Slot {botSlot}). Scheduling ReplayHandler start.");
                AddTimer(3.0f, () =>
                {
                    SharpTimerDebug($"OnReplayBotConnect: 3s timer expired for bot {bot.PlayerName}. Initializing bot for replay.");
                    OnPlayerConnect(bot, true);
                    connectedReplayBots[botSlot] = new CCSPlayerController(bot.Handle);
                    ChangePlayerName(bot, replayBotName);
                    playerTimers[botSlot].IsTimerBlocked = true;

                    SharpTimerDebug($"OnReplayBotConnect: About to start ReplayHandler task for bot {bot.PlayerName}. currentMapRecordTimeSeconds at this point: {currentMapRecordTimeSeconds}s");
                    _ = Task.Run(async () => await ReplayHandler(bot, botSlot));
                    SharpTimerDebug($"OnReplayBotConnect: ReplayHandler task started for {bot.PlayerName}.");
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
                // Cleanup Beams
                if (replayData.replayBeams != null && replayData.replayBeams.Count > 0)
                {
                    SharpTimerDebug($"Clearing {replayData.replayBeams.Count} CEnvBeam trails for slot {playerSlot}.");
                    foreach (var beam in replayData.replayBeams)
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
