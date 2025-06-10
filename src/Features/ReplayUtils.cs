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
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using System.Text.Json;
using FixVectorLeak;
using CounterStrikeSharp.API.Modules.Timers;
using System.Drawing;
using System.Globalization;

namespace SharpTimer
{
    public partial class SharpTimer
    {
        private static FixVectorLeak.Vector_t ConvertCssVectorToFixVec(CounterStrikeSharp.API.Modules.Utils.Vector cssVector)
        {
            return new FixVectorLeak.Vector_t(cssVector.X, cssVector.Y, cssVector.Z);
        }

        public void ClearReplayBotTrail()
        {
            if (Utils == null) { Console.WriteLine("[SharpTimer] ClearReplayBotTrail: Utils is null"); } //Changed _utils to Utils
            else { Utils.LogDebug($"Clearing replay bot trail. Segments: {replayBotBeamSegments.Count}"); }
            foreach (var segment in replayBotBeamSegments)
            {
                if (segment.BeamEntity != null && segment.BeamEntity.IsValid) { segment.BeamEntity.Remove(); }
            }
            replayBotBeamSegments.Clear();
            replayBotPreviousPosition = null;
            currentMapSRTicksForTrail = 0;
        }

        private void ReplayUpdate(CCSPlayerController player, int timerTicks)
        {
            try
            {
                if (!IsAllowedPlayer(player)) return;

                // Get the player's current position and rotation
                ReplayVector currentPosition = ReplayVector.GetVectorish(player.Pawn.Value!.CBodyComponent?.SceneNode?.AbsOrigin ?? new(0, 0, 0));
                ReplayVector currentSpeed = ReplayVector.GetVectorish(player.PlayerPawn.Value!.AbsVelocity);
                ReplayQAngle currentRotation = ReplayQAngle.GetQAngleish(player.PlayerPawn.Value.EyeAngles);

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
                Utils.LogError($"Error in ReplayUpdate: {ex.Message}");
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

                    // var replayFrame = playerReplays[player.Slot].replayFrames[plackbackTick]; // Original line before replayInfo check - THIS IS THE LINE TO REMOVE

                    // Check for replayInfo first
                    if (!playerReplays.TryGetValue(player.Slot, out PlayerReplays? replayInfo) || replayInfo == null)
                    {
                        if (Utils != null) Utils.LogError($"Replay info not found for bot slot {player.Slot} in ReplayPlayback.");
                        return;
                    }

                    // New check for replayFrames list and playbackTick bounds:
                    if (replayInfo.replayFrames == null || plackbackTick < 0 || plackbackTick >= replayInfo.replayFrames.Count)
                    {
                        if (Utils != null) Utils.LogError($"Replay frames list is null or playbackTick ({plackbackTick}) is out of bounds for bot slot {player.Slot}. Frames count: {(replayInfo.replayFrames?.Count.ToString() ?? "null")}");
                        return;
                    }

                    var replayFrame = replayInfo.replayFrames[plackbackTick]; // This line is now safer

                    // The null checks for replayFrame.Position and replayFrame.Speed (added in the previous step) should follow this.
                    if (replayFrame.Position == null || replayFrame.Speed == null)
                    {
                        if (Utils != null) Utils.LogError($"Replay frame at tick {plackbackTick} has null Position or Speed for bot slot {player.Slot}.");
                        return;
                    }

                    // Teleport must happen before we manage the trail, so current/prev positions are correct for the frame
                    player.PlayerPawn.Value!.Teleport(ReplayVector.ToVector(replayFrame.Position), ReplayQAngle.ToQAngle(replayFrame.Rotation!), ReplayVector.ToVector(replayFrame.Speed));

                    if (player.IsBot && player == replayBotController)
                    {
                        if (replayBotBeamSegments.Count > 0)
                        {
                            for (int i = replayBotBeamSegments.Count - 1; i >= 0; i--)
                            {
                                var segment = replayBotBeamSegments[i];
                                if (currentMapSRTicksForTrail > 0 && (Server.TickCount - segment.CreationTick) > currentMapSRTicksForTrail)
                                {
                                    if (segment.BeamEntity != null && segment.BeamEntity.IsValid) { segment.BeamEntity.Remove(); }
                                    replayBotBeamSegments.RemoveAt(i);
                                }
                                // If currentMapSRTicksForTrail is 0, segments are not removed here, effectively lasting until cleared otherwise.
                            }
                        }

                        // replayInfo is already checked and valid here
                        // replayInfo is already checked and valid here
                        // replayFrame is already checked and valid here

                        var currentBotOriginCssVec = ReplayVector.ToVector(replayFrame.Position!); // Type: CounterStrikeSharp.API.Modules.Utils.Vector
                        var currentBotCssSpeed = ReplayVector.ToVector(replayFrame.Speed!);     // Type: CounterStrikeSharp.API.Modules.Utils.Vector
                        var currentBotOriginFixVec = ConvertCssVectorToFixVec(currentBotOriginCssVec); // Type: FixVectorLeak.Vector_t

                        // Create new beam segment if previous position exists
                        if (replayBotPreviousPosition != null)
                        {
                            float speedMagnitude = currentBotCssSpeed.Length();
                            string beamColorHex;
                            System.Drawing.Color finalBeamColor;

                            if (Utils == null)
                            { // Changed _utils to Utils
                                finalBeamColor = System.Drawing.Color.LimeGreen;
                            }
                            else
                            {
                                int[] velThresholds = SharpTimer.VelocityThresholds;
                                string[] hexColors = SharpTimer.HudHexColors;

                                // Default to the first color
                                if (!Utils.TryParseHexColor(hexColors[0], out finalBeamColor))
                                {
                                    finalBeamColor = System.Drawing.Color.LimeGreen;
                                }

                                if (speedMagnitude < velThresholds[0])
                                {
                                    // Already handled by default assignment to hexColors[0]
                                    // If TryParseHexColor failed for hexColors[0], finalBeamColor is LimeGreen.
                                    // If it succeeded, finalBeamColor is hexColors[0]. This is fine.
                                }
                                else if (speedMagnitude >= velThresholds[velThresholds.Length - 1])
                                { // Speed is >= last threshold
                                    // Use the last color in hexColors (index velThresholds.Length, which is hexColors.Length - 1)
                                    if (!Utils.TryParseHexColor(hexColors[velThresholds.Length], out finalBeamColor))
                                        finalBeamColor = System.Drawing.Color.Red; // Fallback
                                }
                                else
                                {
                                    for (int i = 0; i < velThresholds.Length - 1; i++)
                                    {
                                        if (speedMagnitude >= velThresholds[i] && speedMagnitude < velThresholds[i + 1])
                                        {
                                            System.Drawing.Color color1, color2;
                                            if (!Utils.TryParseHexColor(hexColors[i], out color1)) // Color for current threshold
                                                color1 = finalBeamColor; // Use current finalBeamColor as fallback if parsing fails
                                            if (!Utils.TryParseHexColor(hexColors[i + 1], out color2)) // Color for next threshold
                                                color2 = color1; // Fallback to color1 if parsing fails

                                            float factor = (speedMagnitude - velThresholds[i]) / (float)(velThresholds[i + 1] - velThresholds[i]);
                                            finalBeamColor = Utils.InterpolateColor(color1, color2, factor);
                                            break;
                                        }
                                    }
                                }
                            }
                            beamColorHex = (Utils != null) ? Utils.ColorToHexString(finalBeamColor) : "#00FF00";

                            var prevFixVecActual = replayBotPreviousPosition.Value;
                            var prevPosCssVec = new CounterStrikeSharp.API.Modules.Utils.Vector(prevFixVecActual.X, prevFixVecActual.Y, prevFixVecActual.Z);
                            float distanceToPrevious = (currentBotOriginCssVec - prevPosCssVec).Length();

                            const float MAX_BEAM_DISTANCE = 1000.0f; // Threshold for drawing a beam

                            if (distanceToPrevious <= MAX_BEAM_DISTANCE)
                            {
                                CBeam beam = Utilities.CreateEntityByName<CBeam>("beam");
                                if (beam != null)
                                {
                                    try { beam.Render = System.Drawing.ColorTranslator.FromHtml(beamColorHex); }
                                    catch { beam.Render = System.Drawing.Color.LimeGreen; } // Fallback color
                                    beam.Width = SharpTimer.ReplayBeamWidth;

                                    beam.Teleport(prevPosCssVec, new CounterStrikeSharp.API.Modules.Utils.QAngle(0, 0, 0), new CounterStrikeSharp.API.Modules.Utils.Vector(0, 0, 0));
                                    beam.EndPos.X = currentBotOriginCssVec.X; // Use CssVec for EndPos components
                                    beam.EndPos.Y = currentBotOriginCssVec.Y;
                                    beam.EndPos.Z = currentBotOriginCssVec.Z;
                                    beam.DispatchSpawn();
                                    BeamSegment newSegment = new BeamSegment(beam, Server.TickCount, prevFixVecActual, currentBotOriginFixVec); // Use FixVec for segment storage
                                    replayBotBeamSegments.Add(newSegment);
                                }
                            }
                        }
                        // Update previous position for the next frame, unconditionally for the replay bot.
                        replayBotPreviousPosition = currentBotOriginFixVec; // Store as FixVec

                    }
                    else if (player.IsBot && replayBotPreviousPosition != null) // If it's a bot but not the replayBotController, clear its previous position
                    {
                        replayBotPreviousPosition = null;
                    }


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

                    // This was the old location of teleport, moved up before trail logic
                    // player.PlayerPawn.Value!.Teleport(ReplayVector.ToVector(replayFrame.Position!), ReplayQAngle.ToQAngle(replayFrame.Rotation!), ReplayVector.ToVector(replayFrame.Speed!));

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
                }
            }
            catch (Exception ex)
            {
                Utils.LogError($"Error in ReplayPlayback: {ex.Message}");
            }
        }

        private void ReplayPlay(CCSPlayerController player)
        {
            try
            {
                int totalFrames = playerReplays[player.Slot].replayFrames.Count;

                if (totalFrames <= 128)
                {
                    OnRecordingStop(player);
                }

                if (playerReplays[player.Slot].CurrentPlaybackFrame < 0 || playerReplays[player.Slot].CurrentPlaybackFrame >= totalFrames)
                {
                    playerReplays[player.Slot].CurrentPlaybackFrame = 0;
                    Action<CCSPlayerController?, float, bool> adjustVelocity = use2DSpeed ? AdjustPlayerVelocity2D : AdjustPlayerVelocity;
                    adjustVelocity(player, 0, false);
                    // --- Add weapon removal logic here ---
                    if (player.IsBot && player.IsValid && player.PlayerPawn.IsValid && player.PlayerPawn.Value.IsValid) // Ensure player and pawn are valid
                    {
                        player.RemoveWeapons();
                    }
                    // --- End of weapon removal logic ---
                }

                ReplayPlayback(player, playerReplays[player.Slot].CurrentPlaybackFrame);

                if (playerReplays.TryGetValue(player.Slot, out PlayerReplays? replayDataCheck) && replayDataCheck != null)
                {
                    int totalFramesInternal = replayDataCheck.replayFrames.Count;
                    if (replayDataCheck.CurrentPlaybackFrame >= totalFramesInternal - 1)
                    {
                        // Removed ClearReplayBotTrail() call here
                    }
                }
                playerReplays[player.Slot].CurrentPlaybackFrame++;
            }
            catch (Exception ex)
            {
                Utils.LogError($"Error in ReplayPlay: {ex.Message}");
            }
        }

        private void OnRecordingStart(CCSPlayerController player, int bonusX = 0, int style = 0)
        {
            try
            {
                playerReplays.Remove(player.Slot);
                playerReplays[player.Slot] = new PlayerReplays
                {
                    BonusX = bonusX,
                    Style = style
                };
                playerTimers[player.Slot].IsRecordingReplay = true;
            }
            catch (Exception ex)
            {
                Utils.LogError($"Error in OnRecordingStart: {ex.Message}");
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
                Utils.LogError($"Error in OnRecordingStop: {ex.Message}");
            }
        }

        public async Task DumpReplayToJson(CCSPlayerController player, string steamID, int slot, int bonusX = 0, int style = 0)
        {
            await Task.Run(() =>
            {
                if (!IsAllowedPlayer(player))
                {
                    Utils.LogError($"Error in DumpReplayToJson: Player not allowed or not on server anymore");
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

                    if (playerReplays[slot].replayFrames.Count >= maxReplayFrames) return;

                    var indexedReplayFrames = playerReplays[slot].replayFrames
                        .Select((frame, index) => new IndexedReplayFrames { Index = index, Frame = frame })
                        .ToList();

                    using (Stream stream = new FileStream(playerReplaysPath, FileMode.Create))
                    {
                        JsonSerializer.Serialize(stream, indexedReplayFrames);
                    }
                }
                catch (Exception ex)
                {
                    Utils.LogError($"Error during serialization: {ex.Message}");
                }
            });
        }

        public string GetReplayJson(CCSPlayerController player, int slot)
        {
            if (!IsAllowedPlayer(player))
            {
                Utils.LogError($"Error in GetReplayJson: Player not allowed or not on server anymore");
                return "";
            }

            try
            {
                if (playerReplays[slot].replayFrames.Count >= maxReplayFrames) return "";

                var indexedReplayFrames = playerReplays[slot].replayFrames
                    .Select((frame, index) => new IndexedReplayFrames { Index = index, Frame = frame })
                    .ToList();

                return JsonSerializer.Serialize(indexedReplayFrames);
            }
            catch (Exception ex)
            {
                Utils.LogError($"Error during serialization: {ex.Message}");
                return "";
            }
        }

        private async Task ReadReplayFromJson(CCSPlayerController player, string steamId, int slot, int bonusX = 0, int style = 0)
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

                            if (!playerReplays.TryGetValue(slot, out PlayerReplays? value))
                            {
                                value = new PlayerReplays();
                                playerReplays[slot] = value;
                            }

                            value.replayFrames = replayFrames!;
                        }
                    }
                    else
                    {
                        Server.NextFrame(() => { Utils.PrintToChat(player, $"Unsupported replay format"); });
                    }
                }
                else
                {
                    Utils.LogError($"File does not exist: {playerReplaysPath}");
                    Server.NextFrame(() => Utils.PrintToChat(player, Localizer["replay_dont_exist"]));
                }
            }
            catch (Exception ex)
            {
                Utils.LogError($"Error during deserialization: {ex.Message}");
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
                Utils.LogError($"Error during deserialization: {ex.Message}");
            }
        }

        private async Task SpawnReplayBot()
        {
            ClearReplayBotTrail(); // Clear at the very beginning

            if (!await CheckSRReplay()) //This check might be redundant if CheckSRReplay is called before SR time fetching.
            {
                Utils.LogError("Replay check failed (2nd check), not spawning bot.");
                return;
            }

            var (srSteamID, srPlayerName, srTime) = await GetMapRecordSteamIDFromDatabase();
            if (srTime != "null" && srTime != null && Utils != null)
            {
                currentMapSRTicksForTrail = Utils.ParseFormattedTimeToTicks(srTime);
                Utils.LogDebug($"SR time for trail: {srTime} ({currentMapSRTicksForTrail} ticks).");
            }
            else
            {
                currentMapSRTicksForTrail = 0;
                if (Utils != null) Utils.LogDebug($"No SR time for trail (srTime: {srTime}).");
            }

            Server.NextFrame(() =>
            {
                AddTimer(3.0f, () =>
                {
                    Server.ExecuteCommand("bot_quota_mode normal");
                    Server.ExecuteCommand("bot_quota 0");
                    Server.ExecuteCommand("bot_chatter off");
                    Server.ExecuteCommand("bot_controllable 0");
                    Server.ExecuteCommand("bot_kick");
                    replayBotController = null;

                    AddTimer(3.0f, () =>
                    {
                        // wtf is this game even
                        Server.ExecuteCommand("bot_quota 1");
                        Server.ExecuteCommand("bot_add_ct");
                        Server.ExecuteCommand("bot_quota 1");

                        Utils.LogDebug("Searching for replay bot...");

                        AddTimer(0.0f, () =>
                        {
                            // find and setup bot
                            var bot = Utilities.GetPlayers().Where(b => b.IsBot && !b.IsHLTV).FirstOrDefault();
                            if (bot != null)
                            {
                                replayBotController = bot;
                                if (bot.PlayerPawn.Value != null)
                                {
                                    bot.PlayerPawn.Value.SetModel("weapons/models/taser/weapon_pist_taser_mag.vmdl");
                                    Utils.LogDebug($"Set replay bot model to weapons/models/taser/weapon_pist_taser_mag.vmdl");
                                }
                                if (Utils != null) Utils.LogDebug($"Replay bot trail ready for {bot.PlayerName}. SR Ticks: {currentMapSRTicksForTrail}");
                                Utils.LogDebug($"Found replay bot: {bot.PlayerName}");

                                var botPlayerPawn = bot.PlayerPawn();
                                if (botPlayerPawn == null) return;

                                // bot settings
                                botPlayerPawn.Bot!.IsStopping = true;
                                botPlayerPawn.Bot.IsSleeping = true;
                                botPlayerPawn.Bot.AllowActive = true;

                                // start bot replay
                                OnPlayerConnect(bot, true);
                                ChangePlayerName(bot, replayBotName);
                                playerTimers[bot.Slot].IsTimerBlocked = true;
                                bot.RemoveWeapons();

                                // Initialize/clear trail variables
                                replayBotBeamSegments.Clear();
                                replayBotPreviousPosition = null;
                                Utils.LogDebug($"Replay bot trail variables cleared and initialized for {bot.PlayerName}.");

                                _ = Task.Run(async () => await ReplayHandler(bot, bot.Slot));
                                Utils.LogDebug($"Starting replay for {bot.PlayerName}");
                            }
                            else
                            {
                                Utils.LogError($"Failed to spawn replay bot");
                                return;
                            }

                            // kick unused bots if there are any
                            var bots = Utilities.GetPlayers().Where(b => b.IsBot && !b.IsHLTV && b != replayBotController);
                            foreach (var kicked in bots)
                            {
                                OnPlayerDisconnect(kicked, true);
                                Server.ExecuteCommand("bot_quota 1");
                                Server.ExecuteCommand($"kickid {kicked.UserId}");
                                Server.ExecuteCommand("bot_quota 1");
                                Utils.LogDebug($"Kicking unused bot on spawn... {kicked.PlayerName}");
                            }
                        });
                    }, TimerFlags.STOP_ON_MAPCHANGE);
                }, TimerFlags.STOP_ON_MAPCHANGE);
            });
        }

        public async Task<bool> CheckSRReplay(string topSteamID = "x", int bonusX = 0, int style = 0)
        {
            if (Utils != null) Utils.LogDebug($"[CheckSRReplay] Called. Map: {currentMapName}, BonusX: {bonusX}, Style: {style}, TopSteamID: {topSteamID}");
            var (srSteamID, srPlayerName, srTime) = ("null", "null", "null");

            if (enableDb)
            {
                (srSteamID, srPlayerName, srTime) = await GetMapRecordSteamIDFromDatabase(bonusX);
            }
            else
            {
                (srSteamID, srPlayerName, srTime) = await GetMapRecordSteamID(bonusX);
            }

            if (Utils != null) Utils.LogDebug($"[CheckSRReplay] Fetched SR info: SteamID={srSteamID}, Name={srPlayerName}, Time={srTime}");

            if ((srSteamID == "null" || srPlayerName == "null" || srTime == "null") && topSteamID != "x")
            {
                if (Utils != null) Utils.LogDebug($"[CheckSRReplay] SR data is null and topSteamID not 'x'. Returning false early.");
                return false;
            }

            string fileName = $"{(topSteamID == "x" ? $"{srSteamID}" : $"{topSteamID}")}_replay.json";
            string playerReplaysPath;
            if (style != 0) playerReplaysPath = Path.Join(gameDir, "csgo", "cfg", "SharpTimer", "PlayerReplayData", (bonusX == 0 ? currentMapName : $"{currentMapName}_bonus{bonusX}"), GetNamedStyle(style), fileName);
            else playerReplaysPath = Path.Join(gameDir, "csgo", "cfg", "SharpTimer", "PlayerReplayData", (bonusX == 0 ? currentMapName : $"{currentMapName}_bonus{bonusX}"), fileName);

            if (Utils != null) Utils.LogDebug($"[CheckSRReplay] Constructed replay file path: {playerReplaysPath}");

            try
            {
                bool fileExists = File.Exists(playerReplaysPath);
                if (Utils != null) Utils.LogDebug($"[CheckSRReplay] File.Exists({playerReplaysPath}) result: {fileExists}");
                if (fileExists)
                {
                    var jsonString = await File.ReadAllTextAsync(playerReplaysPath);
                    if (!jsonString.Contains("PositionString"))
                    {
                        var indexedReplayFrames = JsonSerializer.Deserialize<List<IndexedReplayFrames>>(jsonString);

                        if (indexedReplayFrames != null)
                        {
                            if (Utils != null) Utils.LogDebug($"[CheckSRReplay] JSON Deserialization successful. Frame count: {indexedReplayFrames.Count}. Returning true.");
                            return true;
                        }
                        else
                        {
                            if (Utils != null) Utils.LogDebug($"[CheckSRReplay] JSON Deserialization failed (result is null). Returning false.");
                            return false;
                        }
                    }
                    else
                    {
                        if (Utils != null) Utils.LogDebug($"[CheckSRReplay] Unsupported replay format (contains 'PositionString'). Returning false.");
                        return false;
                    }
                }
                else
                {
                    // Log for non-existent file is implicitly covered by fileExists log
                    return false;
                }
            }
            catch (Exception ex)
            {
                if (Utils != null) Utils.LogError($"[CheckSRReplay] Exception during deserialization or file read: {ex.Message}. Path: {playerReplaysPath}. Returning false.");
                return false;
            }
        }
    }

    public class BeamSegment
    {
        public CBeam BeamEntity { get; set; }
        public int CreationTick { get; set; }
        public Vector_t StartPos { get; set; }
        public Vector_t EndPos { get; set; }

        public BeamSegment(CBeam beamEntity, int creationTick, Vector_t startPos, Vector_t endPos)
        {
            BeamEntity = beamEntity;
            CreationTick = creationTick;
            StartPos = startPos;
            EndPos = endPos;
        }
    }
}