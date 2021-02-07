using Dalamud.Plugin;
using ImGuiNET;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.FFXIV.Client.UI;
using System;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace FauxHollowsSolver
{
    public sealed class FauxHollowsPlugin : IDalamudPlugin
    {
        public string Name => "ezFauxHollows";

        internal DalamudPluginInterface Interface;

        public void Initialize(DalamudPluginInterface pluginInterface)
        {
            Interface = pluginInterface ?? throw new ArgumentNullException(nameof(pluginInterface), "DalamudPluginInterface cannot be null");

            // Interface.UiBuilder.OnBuildUi += UiBuilder_OnBuildUi_DebugUI;
            LoopTokenSource = new CancellationTokenSource();
            LoopTask = Task.Run(() => GameBoardUpdaterLoop(LoopTokenSource.Token));
        }

        public void Dispose()
        {
            // Interface.UiBuilder.OnBuildUi -= UiBuilder_OnBuildUi_DebugUI;
            LoopTokenSource.Cancel();
        }

        private Task LoopTask;
        private CancellationTokenSource LoopTokenSource;
        private readonly Tile[] GameState = new Tile[36];
        private readonly PerfectFauxHollows PerfectFauxHollows = new PerfectFauxHollows();

        private async void GameBoardUpdaterLoop(CancellationToken token)
        {
            for (int i = 0; i < 36; i++)
                GameState[i] = Tile.Unknown;

            try
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(100, token);
                    GameBoardUpdater();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Updater loop has crashed");
                Interface.Framework.Gui.Chat.PrintError($"{Name} has encountered a critical error");
            }
        }

        private unsafe void GameBoardUpdater()
        {
            if (Interface.ClientState.TerritoryType != 478) // Idyllshire
                return;

            var addonPtr = Interface.Framework.Gui.GetUiObjectByName("WeeklyPuzzle", 1);
            if (addonPtr == IntPtr.Zero)
                return;

            var addon = (AddonWeeklyPuzzle*)addonPtr;
            if (addon == null)
                return;

            if (!addon->AtkUnitBase.IsVisible || addon->AtkUnitBase.ULDData.LoadedState != 3)
                return;

            var stateChanged = UpdateGameState(addon);
            if (stateChanged)
            {
                // A valid gameState has atleast one blocked tile
                // The board is likely not ready and in transition
                if (!GameState.Contains(Tile.Blocked))
                    return;

                var solution = PerfectFauxHollows.Solve(GameState);
                var solnMaxValue = solution.Where(s => s < 16).Max();
                if (solnMaxValue <= 1)
                    solnMaxValue = -1;

                for (int i = 0; i < 36; i++)
                {
                    var soln = solution[i];
                    var tileButton = GetTileButton(addon, i);
                    var tileBackgroundImage = GetBackgroundImageNode(tileButton);

                    if (soln == PerfectFauxHollows.ConfirmedSword ||
                        soln == PerfectFauxHollows.ConfirmedBox ||
                        soln == PerfectFauxHollows.ConfirmedChest ||
                        soln == solnMaxValue)
                    {
                        tileBackgroundImage->AtkResNode.AddRed = 32;
                        tileBackgroundImage->AtkResNode.AddGreen = 143;
                        tileBackgroundImage->AtkResNode.AddBlue = 46;
                    }
                    else
                    {
                        tileBackgroundImage->AtkResNode.AddRed = 0;
                        tileBackgroundImage->AtkResNode.AddGreen = 0;
                        tileBackgroundImage->AtkResNode.AddBlue = 0;
                    }
                }
            }
        }

        private unsafe bool UpdateGameState(AddonWeeklyPuzzle* addon)
        {
            var stateChanged = false;
            for (int i = 0; i < 36; i++)
            {
                var tileButton = GetTileButton(addon, i);
                var tileBackgroundImage = GetBackgroundImageNode(tileButton);
                var tileBackgroundTex = (WeeklyPuzzleTexture)tileBackgroundImage->PartId;

                var newState = Tile.Unknown;
                if (tileBackgroundTex == WeeklyPuzzleTexture.Hidden)
                {
                    newState = Tile.Hidden;
                }
                else if (tileBackgroundTex == WeeklyPuzzleTexture.Blocked)
                {
                    newState = Tile.Blocked;
                }
                else if (tileBackgroundTex == WeeklyPuzzleTexture.Blank)
                {
                    var tileIconImage = GetIconImageNode(tileButton);
                    var tileIconTex = (WeeklyPuzzlePrizeTexture)tileIconImage->PartId;

                    if (!tileIconImage->AtkResNode.IsVisible)
                    {
                        newState = Tile.Empty;
                    }
                    else
                    {
                        newState = tileIconTex switch
                        {
                            WeeklyPuzzlePrizeTexture.BoxUpperLeft => Tile.BoxUpperLeft,
                            WeeklyPuzzlePrizeTexture.BoxUpperRight => Tile.BoxUpperRight,
                            WeeklyPuzzlePrizeTexture.BoxLowerLeft => Tile.BoxLowerLeft,
                            WeeklyPuzzlePrizeTexture.BoxLowerRight => Tile.BoxLowerRight,
                            WeeklyPuzzlePrizeTexture.ChestUpperLeft => Tile.ChestUpperLeft,
                            WeeklyPuzzlePrizeTexture.ChestUpperRight => Tile.ChestUpperRight,
                            WeeklyPuzzlePrizeTexture.ChestLowerLeft => Tile.ChestLowerLeft,
                            WeeklyPuzzlePrizeTexture.ChestLowerRight => Tile.ChestLowerRight,
                            WeeklyPuzzlePrizeTexture.SwordsUpperLeft => Tile.SwordsUpperLeft,
                            WeeklyPuzzlePrizeTexture.SwordsUpperRight => Tile.SwordsUpperRight,
                            WeeklyPuzzlePrizeTexture.SwordsMiddleLeft => Tile.SwordsMiddleLeft,
                            WeeklyPuzzlePrizeTexture.SwordsMiddleRight => Tile.SwordsMiddleRight,
                            WeeklyPuzzlePrizeTexture.SwordsLowerLeft => Tile.SwordsLowerLeft,
                            WeeklyPuzzlePrizeTexture.SwordsLowerRight => Tile.SwordsLowerRight,
                            WeeklyPuzzlePrizeTexture.Commander => Tile.Commander,
                            _ => throw new Exception($"Unknown tile icon state: {tileIconTex} at index {i}")
                        };

                        var rotation = tileIconImage->AtkResNode.Rotation;
                        if (rotation < 0)
                            newState |= Tile.RotatedLeft;
                        else if (rotation > 0)
                            newState |= Tile.RotatedRight;
                    }
                }
                else
                {
                    throw new Exception($"Unknown tile bg state: {tileBackgroundTex} at index {i}");
                }

                stateChanged |= GameState[i] != newState;
                GameState[i] = newState;
            }
            return stateChanged;
        }

        private unsafe AtkComponentButton* GetTileButton(AddonWeeklyPuzzle* addon, int index) => addon->GameBoard[index / 6][index % 6].Button;

        private unsafe AtkImageNode* GetBackgroundImageNode(AtkComponentButton* button) => (AtkImageNode*)button->AtkComponentBase.ULDData.NodeList[3];

        private unsafe AtkImageNode* GetIconImageNode(AtkComponentButton* button) => (AtkImageNode*)button->AtkComponentBase.ULDData.NodeList[6];

        /*
        private unsafe void UiBuilder_OnBuildUi_DebugUI()
        {
            ImGui.SetNextWindowPos(new Vector2(500, 500), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowSize(new Vector2(500, 500), ImGuiCond.FirstUseEver);

            bool alwaysTrue2 = true;
            ImGui.Begin("FFXIV FauxHollows Solver", ref alwaysTrue2);

            for (int i = 0; i < 36; i++)
            {
                ImGui.Text($"{GameState[i]}");
                if ((i + 1) % 6 != 0)
                    ImGui.SameLine();
            }

            ImGui.Text("");

            var tileStates = new string[] {
                "Hidden", "Blocked", "Empty",
                "BoxUL", "BoxUR", "BoxLL", "BoxLR",
                "ChestUL", "ChestUR", "ChestLL", "ChestLR",
                "SwordsUL", "SwordsUR", "SwordsML", "SwordsMR", "SwordsLL", "SwordsLR",
                "Commander", "Unknown"
            };
            var rotationStates = new string[] { "-", "L", "R" };

            for (int i = 0; i < 36; i++)
            {
                var maskedTile = GameState[i];

                var tileIndex = maskedTile.Unmask() switch
                {
                    Tile.Hidden => 0,
                    Tile.Blocked => 1,
                    Tile.Empty => 2,
                    Tile.BoxUpperLeft => 3,
                    Tile.BoxUpperRight => 4,
                    Tile.BoxLowerLeft => 5,
                    Tile.BoxLowerRight => 6,
                    Tile.ChestUpperLeft => 7,
                    Tile.ChestUpperRight => 8,
                    Tile.ChestLowerLeft => 9,
                    Tile.ChestLowerRight => 10,
                    Tile.SwordsUpperLeft => 11,
                    Tile.SwordsUpperRight => 12,
                    Tile.SwordsMiddleLeft => 13,
                    Tile.SwordsMiddleRight => 14,
                    Tile.SwordsLowerLeft => 15,
                    Tile.SwordsLowerRight => 16,
                    Tile.Commander => 17,
                    _ => 18,
                };
                var rotationIndex = maskedTile.IsRotatedLeft() ? 1 : maskedTile.IsRotatedRight() ? 2 : 0;


                ImGui.SetNextItemWidth(90);
                if (ImGui.Combo($"##EditTile{i}", ref tileIndex, tileStates, tileStates.Length))
                {
                    SetTileState(i, tileIndex, rotationIndex);
                }

                ImGui.SameLine();

                ImGui.SetNextItemWidth(40);
                if (ImGui.Combo($"##EditTileRotation{i}", ref rotationIndex, rotationStates, rotationStates.Length))
                {
                    SetTileState(i, tileIndex, rotationIndex);
                }

                if ((i + 1) % 6 != 0)
                    ImGui.SameLine();
            }


            if (ImGui.Button($"Freshen"))
            {
                for (int i = 0; i < 36; i++)
                {
                    SetTileState(i, 0, 0); // Hidden
                }

                var Random = new Random();
                var randomValues = new int[6] { -1, -1, 1 - 1, -1, -1, -1 };
                for (int i = 0; i < randomValues.Length; i++)
                {
                    int nextRandom = Random.Next(0, 36);
                    while (randomValues.Contains(nextRandom))
                    {
                        nextRandom = Random.Next(0, 36);
                    }
                    randomValues[i] = nextRandom;
                    SetTileState(nextRandom, 1, 0);  // Blocked
                }
            }
            ImGui.End();
        }
        */

        private unsafe void SetTileState(int index, int newState, int newRotation)
        {
            var addonPtr = Interface.Framework.Gui.GetUiObjectByName("WeeklyPuzzle", 1);
            if (addonPtr == IntPtr.Zero)
                return;

            var addon = (AddonWeeklyPuzzle*)addonPtr;
            var tileButton = GetTileButton(addon, index);
            var fgTile = GetIconImageNode(tileButton);
            var bgTile = GetBackgroundImageNode(tileButton);

            if (newState == 0)  // Hidden
            {
                bgTile->PartId = (ushort)WeeklyPuzzleTexture.Hidden;
                if (!bgTile->AtkResNode.IsVisible)
                    bgTile->AtkResNode.Flags ^= 0x10;
                if (fgTile->AtkResNode.IsVisible)
                    fgTile->AtkResNode.Flags ^= 0x10;
            }
            else if (newState == 1)  // Blocked
            {
                bgTile->PartId = (ushort)WeeklyPuzzleTexture.Blocked;
                if (!bgTile->AtkResNode.IsVisible)
                    bgTile->AtkResNode.Flags ^= 0x10;
                if (fgTile->AtkResNode.IsVisible)
                    fgTile->AtkResNode.Flags ^= 0x10;
            }
            else if (newState == 2)  // Empty
            {
                bgTile->PartId = (ushort)WeeklyPuzzleTexture.Blank;
                if (!bgTile->AtkResNode.IsVisible)
                    bgTile->AtkResNode.Flags ^= 0x10;
                if (fgTile->AtkResNode.IsVisible)
                    fgTile->AtkResNode.Flags ^= 0x10;
            }
            else
            {
                bgTile->PartId = (ushort)WeeklyPuzzleTexture.Blank;
                if (!fgTile->AtkResNode.IsVisible)
                    fgTile->AtkResNode.Flags ^= 0x10;
                var tileTexID = newState switch
                {
                    3 => WeeklyPuzzlePrizeTexture.BoxUpperLeft,
                    4 => WeeklyPuzzlePrizeTexture.BoxUpperRight,
                    5 => WeeklyPuzzlePrizeTexture.BoxLowerLeft,
                    6 => WeeklyPuzzlePrizeTexture.BoxLowerRight,
                    7 => WeeklyPuzzlePrizeTexture.ChestUpperLeft,
                    8 => WeeklyPuzzlePrizeTexture.ChestUpperRight,
                    9 => WeeklyPuzzlePrizeTexture.ChestLowerLeft,
                    10 => WeeklyPuzzlePrizeTexture.ChestLowerRight,
                    11 => WeeklyPuzzlePrizeTexture.SwordsUpperLeft,
                    12 => WeeklyPuzzlePrizeTexture.SwordsUpperRight,
                    13 => WeeklyPuzzlePrizeTexture.SwordsMiddleLeft,
                    14 => WeeklyPuzzlePrizeTexture.SwordsMiddleRight,
                    15 => WeeklyPuzzlePrizeTexture.SwordsLowerLeft,
                    16 => WeeklyPuzzlePrizeTexture.SwordsLowerRight,
                    17 => WeeklyPuzzlePrizeTexture.Commander,
                    _ => throw new Exception("Invalid tile state")
                };
                fgTile->PartId = (ushort)tileTexID;
                if (newRotation == 0)
                {
                    fgTile->AtkResNode.Rotation = 0;
                }
                else if (newRotation == 1)
                {
                    fgTile->AtkResNode.Rotation = -90;
                }
                else if (newRotation == 2)
                {
                    fgTile->AtkResNode.Rotation = 90;
                }
            }
        }
    }
}
