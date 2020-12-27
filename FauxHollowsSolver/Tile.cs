using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FauxHollowsSolver
{
    [Flags]
    public enum Tile
    {
        Unknown = 1 << 0,
        Hidden = 1 << 1,
        Blocked = 1 << 2,
        Empty = 1 << 3,
        BoxUpperLeft = 1 << 4,
        BoxUpperRight = 1 << 5,
        BoxLowerLeft = 1 << 6,
        BoxLowerRight = 1 << 7,
        ChestUpperLeft = 1 << 8,
        ChestUpperRight = 1 << 9,
        ChestLowerLeft = 1 << 10,
        ChestLowerRight = 1 << 11,
        SwordsUpperLeft = 1 << 12,
        SwordsUpperRight = 1 << 13,
        SwordsMiddleLeft = 1 << 14,
        SwordsMiddleRight = 1 << 15,
        SwordsLowerLeft = 1 << 16,
        SwordsLowerRight = 1 << 17,
        Commander = 1 << 18,

        RotatedLeft = 1 << 19,
        RotatedRight = 1 << 20,
        RotatedEither = RotatedLeft | RotatedRight,
        Box = BoxUpperLeft | BoxUpperRight | BoxLowerLeft | BoxLowerRight | RotatedEither,
        Chest = ChestUpperLeft | ChestUpperRight | ChestLowerLeft | ChestLowerRight | RotatedEither,
        BoxChest = Box | Chest,
        Swords = SwordsUpperLeft | SwordsUpperRight | SwordsMiddleLeft | SwordsMiddleRight | SwordsLowerLeft | SwordsLowerRight | RotatedEither,
    }

    internal static class TileExtensions
    {
        public static Tile Unmask(this Tile tile)
        {
            return tile & ~Tile.RotatedEither;
        }

        public static bool IsRotatedLeft(this Tile tile)
        {
            return (tile & Tile.RotatedLeft) == Tile.RotatedLeft;
        }

        public static bool IsRotatedRight(this Tile tile)
        {
            return (tile & Tile.RotatedRight) == Tile.RotatedRight;
        }
    }
}
