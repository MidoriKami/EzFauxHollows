using Dalamud.Plugin;
using ImGuiNET;
using System;
using System.Linq;
using System.Numerics;
using FFXIVClientStructs.Component.GUI;
using Dalamud.Game.Internal;
using System.Collections.Generic;

namespace FauxHollowsSolver
{

    public sealed class FauxHollowsPlugin : IDalamudPlugin
    {
        public string Name => "ezFauxHollows";

        private const int TotalTiles = PerfectFauxHollows.TotalTiles;

        internal DalamudPluginInterface Interface;

        public void Initialize(DalamudPluginInterface pluginInterface)
        {
            this.Interface = pluginInterface ?? throw new ArgumentNullException(nameof(pluginInterface), "DalamudPluginInterface cannot be null");

            this.Interface.UiBuilder.OnBuildUi += UiBuilder_OnBuildUi_Overlay;
            this.Interface.Framework.OnUpdateEvent += Framework_OnUpdateEvent;
        }

        public void Dispose()
        {
            this.Interface.UiBuilder.OnBuildUi -= UiBuilder_OnBuildUi_Overlay;
            this.Interface.Framework.OnUpdateEvent -= Framework_OnUpdateEvent;
        }

        #region GameLogic

        private readonly PerfectFauxHollows PerfectFauxHollows = new PerfectFauxHollows();
        private Tile[] previousState = new Tile[TotalTiles];

        private unsafe void Framework_OnUpdateEvent(Framework framework)
        {
            if (Interface.ClientState.TerritoryType != 478) // Idyllshire
                return;

            UpdateGameData();

            if (!FauxHollowsGameData.IsVisible)
                return;

            var gameState = GetSolverState();
            if (!Enumerable.SequenceEqual(gameState, previousState))
            {
                previousState = gameState;

                // A valid gameState has atleast one blocked tile
                // The board is likely not ready and in transition
                if (!gameState.Contains(Tile.Blocked))
                    return;

                var solution = PerfectFauxHollows.Solve(gameState);
                var solnMaxValue = solution.Where(s => s < TotalTiles).Max();
                if (solnMaxValue <= 1)
                    solnMaxValue = -1;

                for (int i = 0; i < TotalTiles; i++)
                {
                    var soln = solution[i];
                    var bgNode = FauxHollowsGameData.BgNodes[i];

                    if (soln == PerfectFauxHollows.ConfirmedSword ||
                        soln == PerfectFauxHollows.ConfirmedBox ||
                        soln == PerfectFauxHollows.ConfirmedChest ||
                        soln == solnMaxValue)
                    {
                        bgNode->AtkResNode.AddRed = 32;
                        bgNode->AtkResNode.AddGreen = 143;
                        bgNode->AtkResNode.AddBlue = 46;
                    }
                    else
                    {
                        bgNode->AtkResNode.AddRed = 0;
                        bgNode->AtkResNode.AddGreen = 0;
                        bgNode->AtkResNode.AddBlue = 0;
                    }
                }

                var msg = "";
                for (int i = 0; i < TotalTiles; i++)
                {
                    msg += $"{solution[i],2} ";
                    if ((i + 1) % 6 == 0)
                        msg += "\n";
                }
                PluginLog.Information($"SOLUTION=\n{msg}");
            }
        }

        public static unsafe class FauxHollowsGameData
        {
            public static float X;
            public static float Y;
            public static ushort Width;
            public static ushort Height;
            public static bool IsVisible;
            public static AtkImageNode*[] BgNodes = new AtkImageNode*[TotalTiles];
            public static AtkImageNode*[] IconNodes = new AtkImageNode*[TotalTiles];
        }

        private unsafe void UpdateGameData()
        {
            var addon = Interface.Framework.Gui.GetAddonByName("WeeklyPuzzle", 1);

            if (addon is null || addon.Address == IntPtr.Zero)
            {
                FauxHollowsGameData.IsVisible = false;
                return;
            }

            var uiAddon = (AtkUnitBase*)addon.Address;

            FauxHollowsGameData.X = uiAddon->RootNode->X;
            FauxHollowsGameData.Y = uiAddon->RootNode->Y;
            FauxHollowsGameData.Width = (ushort)(uiAddon->RootNode->Width * uiAddon->RootNode->ScaleX);
            FauxHollowsGameData.Height = (ushort)(uiAddon->RootNode->Height * uiAddon->RootNode->ScaleY);
            FauxHollowsGameData.IsVisible = (uiAddon->Flags & 0x20) == 0x20;

            var baseParentNode = uiAddon->RootNode->ChildNode->PrevSiblingNode;

            var tileNode = baseParentNode->ChildNode;
            for (var i = 0; i < TotalTiles; i++)
            {
                if (tileNode == null)
                    throw new Exception("Problem fetching tile node");

                var tileCompNode = (AtkComponentNode*)tileNode;
                var tileRootNode = tileCompNode->Component->ULDData.RootNode;
                var tileBgImageNode = (AtkImageNode*)tileRootNode->PrevSiblingNode->ChildNode->PrevSiblingNode;
                var iconFgImageNode = (AtkImageNode*)tileBgImageNode->AtkResNode.PrevSiblingNode->ChildNode->PrevSiblingNode;

                FauxHollowsGameData.BgNodes[TotalTiles - 1 - i] = tileBgImageNode;
                FauxHollowsGameData.IconNodes[TotalTiles - 1 - i] = iconFgImageNode;

                tileNode = tileNode->PrevSiblingNode;
            }
        }

        /// <summary>
        /// Get the list of revealed numbers PerfectCactbot style
        /// </summary>
        /// <param name="tileNodes">Current UI data</param>
        /// <returns>Int array of numbers, 0 for unknown.</returns>
        private unsafe Tile[] GetSolverState()
        {
            var tiles = new Tile[TotalTiles];

            for (int i = 0; i < TotalTiles; i++)
            {
                var tileBgImageNode = FauxHollowsGameData.BgNodes[i];
                var iconFgImageNode = FauxHollowsGameData.IconNodes[i];


                if (i == 11 || i == 14)
                    PluginLog.Verbose(
                        $"{i} " +
                        $"{(ulong)iconFgImageNode:X} " +
                        $"{iconFgImageNode->AtkResNode.Flags:X}");

                var tileTexBg = (WeeklyPuzzleTexture)tileBgImageNode->PartId;
                if (tileTexBg == WeeklyPuzzleTexture.Hidden)
                {
                    tiles[i] = Tile.Hidden;
                }
                else if (tileTexBg == WeeklyPuzzleTexture.Blocked)
                {
                    tiles[i] = Tile.Blocked;
                }
                else if (tileTexBg == WeeklyPuzzleTexture.Blank)
                {
                    var iconIsVisible = (iconFgImageNode->AtkResNode.Flags & 0x10) == 0x10;
                    if (!iconIsVisible)
                    {
                        tiles[i] = Tile.Empty;
                    }
                    else
                    {
                        var prizeTexFg = (WeeklyPuzzlePrizeTexture)iconFgImageNode->PartId;
                        var tile = prizeTexFg switch
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
                            _ => Tile.Unknown,
                        };

                        if (tile == Tile.Unknown)
                        {
                            PluginLog.Error($"Unknown Tile: bgID={tileTexBg} ({(int)tileTexBg}) fgID={prizeTexFg} ({(int)prizeTexFg})");
                        }

                        // Rotation
                        var rotation = iconFgImageNode->AtkResNode.Rotation;
                        if (rotation < 0)
                            tile |= Tile.RotatedLeft;
                        else if (rotation > 0)
                            tile |= Tile.RotatedRight;

                        tiles[i] = tile;
                    }
                }
                else
                {
                    PluginLog.Error($"Unknown TileBgTexID {tileTexBg} ({(int)tileTexBg}) at index {i}");
                }
            }

            return tiles;
        }

        #endregion GameLogic

        #region ImGui

        private unsafe void UiBuilder_OnBuildUi_Overlay()
        {
            if (!FauxHollowsGameData.IsVisible)
                return;

            ImGui.SetNextWindowPos(new Vector2(500, 500), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowSize(new Vector2(500, 500), ImGuiCond.FirstUseEver);

            bool alwaysTrue = true;
            ImGui.Begin("FFXIV FauxHollows Solver", ref alwaysTrue);


            var state = GetSolverState();

            for (int i = 1; i <= TotalTiles; i++)
            {
                ImGui.Text($"{i:D2}");
                if (i % 6 != 0)
                    ImGui.SameLine();
            }

            ImGui.Text("");

            for (int i = 1; i <= TotalTiles; i++)
            {
                var maskedTile = state[i - 1];

                var c = maskedTile switch
                {
                    Tile.Hidden => "?",
                    Tile.Blocked => "X",
                    Tile.Empty => "-",

                    Tile.BoxUpperLeft => "BUL",
                    Tile.BoxUpperRight => "BUR",
                    Tile.BoxLowerLeft => "BLL",
                    Tile.BoxLowerRight => "BLR",
                    Tile.BoxUpperLeft | Tile.RotatedLeft => "LBUL",
                    Tile.BoxUpperRight | Tile.RotatedLeft => "LBUR",
                    Tile.BoxLowerLeft | Tile.RotatedLeft => "LBLL",
                    Tile.BoxLowerRight | Tile.RotatedLeft => "LBLR",
                    Tile.BoxUpperLeft | Tile.RotatedRight => "RBUL",
                    Tile.BoxUpperRight | Tile.RotatedRight => "RBUR",
                    Tile.BoxLowerLeft | Tile.RotatedRight => "RBLL",
                    Tile.BoxLowerRight | Tile.RotatedRight => "RBLR",

                    Tile.ChestUpperLeft => "CUL",
                    Tile.ChestUpperRight => "CUR",
                    Tile.ChestLowerLeft => "CLL",
                    Tile.ChestLowerRight => "CLR",
                    Tile.ChestUpperLeft | Tile.RotatedLeft => "LCUL",
                    Tile.ChestUpperRight | Tile.RotatedLeft => "LCUR",
                    Tile.ChestLowerLeft | Tile.RotatedLeft => "LCLL",
                    Tile.ChestLowerRight | Tile.RotatedLeft => "LCLR",
                    Tile.ChestUpperLeft | Tile.RotatedRight => "RCUL",
                    Tile.ChestUpperRight | Tile.RotatedRight => "RCUR",
                    Tile.ChestLowerLeft | Tile.RotatedRight => "RCLL",
                    Tile.ChestLowerRight | Tile.RotatedRight => "RCLR",

                    Tile.SwordsUpperLeft => "SUL",
                    Tile.SwordsUpperRight => "SUR",
                    Tile.SwordsMiddleLeft => "SML",
                    Tile.SwordsMiddleRight => "MR",
                    Tile.SwordsLowerLeft => "SLL",
                    Tile.SwordsLowerRight => "SLR",
                    Tile.SwordsUpperLeft | Tile.RotatedLeft => "LSUL",
                    Tile.SwordsUpperRight | Tile.RotatedLeft => "LSUR",
                    Tile.SwordsMiddleLeft | Tile.RotatedLeft => "LSML",
                    Tile.SwordsMiddleRight | Tile.RotatedLeft => "LSMR",
                    Tile.SwordsLowerLeft | Tile.RotatedLeft => "LSLL",
                    Tile.SwordsLowerRight | Tile.RotatedLeft => "LSLR",
                    Tile.SwordsUpperLeft | Tile.RotatedRight => "RSUL",
                    Tile.SwordsUpperRight | Tile.RotatedRight => "RSUR",
                    Tile.SwordsMiddleLeft | Tile.RotatedRight => "RSML",
                    Tile.SwordsMiddleRight | Tile.RotatedRight => "RSMR",
                    Tile.SwordsLowerLeft | Tile.RotatedRight => "RSLL",
                    Tile.SwordsLowerRight | Tile.RotatedRight => "RSLR",

                    Tile.Commander => "C",
                    _ => "!",
                };
                ImGui.Text($"{c}");
                if (i % 6 != 0)
                    ImGui.SameLine();
            }

            ImGui.End();


            /*
            if(!FauxHollowsUiSettings.IsVisible)
                return;
            var x = FauxHollowsUiSettings.X;
            var y = FauxHollowsUiSettings.Y;
            var w = FauxHollowsUiSettings.Width;
            var h = FauxHollowsUiSettings.Height;
            ImGui.SetNextWindowPos(new Vector2(x + w / 2 - 3, y + h - 35), ImGuiCond.Always);
            ImGui.SetNextWindowSize(new Vector2(w / 2, 30), ImGuiCond.Always);
            ImGui.SetNextWindowBgAlpha(0.0f);

            ImGui.PushStyleColor(ImGuiCol.ResizeGrip, 0);  // Hide the resize window grip
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, 0));
            
            bool alwaysTrue = true;
            ImGui.Begin("FFXIV FauxHollows Solver", ref alwaysTrue,
                ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse |
                ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar |
                ImGuiWindowFlags.NoTitleBar);

            var poweredText = $"Powered by PerfectFauxHollows";
            var textSize = ImGui.CalcTextSize(poweredText);
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetColumnWidth() - textSize.X - ImGui.GetScrollX() - 2 * ImGui.GetStyle().ItemSpacing.X);  // right aligned
            ImGui.Text(poweredText);

            ImGui.End();

            ImGui.PopStyleVar();
            ImGui.PopStyleColor();
            */
        }

        #endregion ImGui
    }
}
