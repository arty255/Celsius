﻿using Verse;

namespace TemperaturesPlus
{
    enum CellMaterialType
    {
        Air = 0,
        Rock,
        Structure
    };

    static class TemperatureUtility
    {
        public static TemperatureInfo TemperatureInfo(this Map map) => map.GetComponent<TemperatureInfo>();

        internal static CellMaterialType GetMaterialType(this IntVec3 cell, Map map)
        {
            if (!cell.InBounds(map))
                return CellMaterialType.Air;
            if (cell.GetFirstMineable(map) != null)
                return CellMaterialType.Rock;
            Building building;
            if ((building = cell.GetFirstBuilding(map)) != null && building.def.defName == "Wall")
                return CellMaterialType.Structure;
            return CellMaterialType.Air;
        }

        public static float GetHeatConductivity(this IntVec3 cell, Map map)
        {
            switch (cell.GetMaterialType(map))
            {
                case CellMaterialType.Rock:
                case CellMaterialType.Structure:
                    return 10;

                default:
                    return 1;
            }
        }

        /// <summary>
        /// Returns volumetric heat capacity for a cell
        /// </summary>
        public static float GetHeatCapacity(this IntVec3 cell, Map map)
        {
            switch (cell.GetMaterialType(map))
            {
                case CellMaterialType.Rock:
                    return 3000000;

                case CellMaterialType.Structure:
                    return 1000000;

                default:
                    return 10000;
            }
        }
    }
}