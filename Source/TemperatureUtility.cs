﻿using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Celsius
{
    static class TemperatureUtility
    {
        public const float TemperatureChangePrecision = 0.01f;
        public const float MinFreezingTemperature = -3;

        public static TemperatureInfo TemperatureInfo(this Map map) => map.GetComponent<TemperatureInfo>();

        #region TEMPERATURE

        public static float GetTemperatureForCell(this IntVec3 cell, Map map)
        {
            TemperatureInfo tempInfo = map.TemperatureInfo();
            if (tempInfo == null)
                return map.mapTemperature.OutdoorTemp;
            return tempInfo.GetTemperatureForCell(cell);
        }

        public static float GetSurroundingTemperature(this IntVec3 cell, Map map)
        {
            TemperatureInfo tempInfo = map.TemperatureInfo();
            if (tempInfo == null || !cell.InBounds(map))
                return map.mapTemperature.OutdoorTemp;
            float sum = cell.GetTemperatureForCell(map);
            foreach (IntVec3 c in cell.AdjacentCells())
                sum += c.InBounds(map) ? c.GetTemperatureForCell(map) : cell.GetTemperatureForCell(map);
            return sum / 9;
        }

        public static float GetTemperature(this Room room)
        {
            if (room == null)
            {
                LogUtility.Log($"Trying to GetTemperature for null room!", LogLevel.Error);
                return TemperatureTuning.DefaultTemperature;
            }
            TemperatureInfo temperatureInfo = room.Map?.TemperatureInfo();
            if (temperatureInfo == null)
            {
                LogUtility.Log($"TemperatureInfo unavailable for {room.Map}.", LogLevel.Error);
                return room.Temperature;
            }
            if (room.TouchesMapEdge)
                return room.Map.mapTemperature.OutdoorTemp;
            return temperatureInfo.GetRoomTemperature(room);
        }

        #endregion TEMPERATURE

        #region DIFFUSION

        static float airLerpFactor, diffusionLerpFactor;

        internal static void SettingsChanged()
        {
            CellThermalProps.Air.heatCapacity = Settings.AirHeatCapacity;
            airLerpFactor = Mathf.Min(1 - Mathf.Pow(1 - CellThermalProps.Air.conductivity * CellThermalProps.Air.conductivity * Settings.ConvectionConductivityEffect / CellThermalProps.Air.heatCapacity, Celsius.TemperatureInfo.SecondsPerUpdate), 0.25f);
            diffusionLerpFactor = Mathf.Min(1 - Mathf.Pow(1 - CellThermalProps.Air.conductivity * CellThermalProps.Air.conductivity * Settings.ConvectionConductivityEffect * Settings.EnvironmentDiffusionFactor / CellThermalProps.Air.heatCapacity, Celsius.TemperatureInfo.SecondsPerUpdate), 0.25f);
            LogUtility.Log($"Air lerp factor: {airLerpFactor:P1}. Diffusion lerp factor: {diffusionLerpFactor:P1}.");
            FloatRange vanillaAutoIgnitionTemperatureRange = Settings.AutoignitionEnabled ? new FloatRange(10000, float.MaxValue) : new FloatRange(240, 1000);
            AccessTools.Field(typeof(SteadyEnvironmentEffects), "AutoIgnitionTemperatureRange").SetValue(null, vanillaAutoIgnitionTemperatureRange);

            // Resetting thermal props for all things and thingDefs
            if (Find.Maps != null)
                for (int m = 0; m < Find.Maps.Count; m++)
                {
                    List<Thing> things = Find.Maps[m]?.listerThings?.AllThings;
                    if (things != null)
                        for (int i = 0; i < things.Count; i++)
                            things[i].TryGetComp<CompThermal>()?.Reset();
                }
            for (int i = 0; i < DefDatabase<ThingDef>.AllDefsListForReading.Count; i++)
                DefDatabase<ThingDef>.AllDefsListForReading[i].GetModExtension<ThingThermalProperties>()?.Reset();
        }

        public static float EnvironmentDiffusionTemperatureChange(float oldTemp, float neighbourTemp, CellThermalProps thermalProps, bool log)
        {
            if (Mathf.Abs(oldTemp - neighbourTemp) < TemperatureChangePrecision)
                return 0;
            float finalTemp = (oldTemp + neighbourTemp) / 2;
            float lerpFactor = thermalProps == CellThermalProps.Air
                ? diffusionLerpFactor
                : Mathf.Min(1 - Mathf.Pow(1 - thermalProps.conductivity * thermalProps.conductivity * Mathf.Pow(Settings.ConvectionConductivityEffect, thermalProps.airflow) * Settings.EnvironmentDiffusionFactor / thermalProps.heatCapacity, Celsius.TemperatureInfo.SecondsPerUpdate), 0.25f);
            if (log)
            {
                LogUtility.Log($"Old temperature: {oldTemp:F1}C. Neighbour temperature: {neighbourTemp:F1}C. {thermalProps}");
                LogUtility.Log($"Final temperature: {finalTemp:F1}C. Lerp factor: {lerpFactor:P1}.");
            }
            return lerpFactor * (finalTemp - oldTemp);
        }

        public static (float, float) DiffusionTemperatureChange(float temp1, CellThermalProps props1, float temp2, CellThermalProps props2, bool log = false)
        {
            if (Mathf.Abs(temp1 - temp2) < TemperatureChangePrecision)
                return (0, 0);
            float finalTemp, lerpFactor1, lerpFactor2, convection = 1;

            if (props1 == CellThermalProps.Air && props2 == CellThermalProps.Air)
            {
                finalTemp = (temp1 + temp2) / 2;
                lerpFactor1 = lerpFactor2 = airLerpFactor;
            }

            else if (props1.Equals(props2))
            {
                finalTemp = (temp1 + temp2) / 2;
                convection = props1.airflow * props2.airflow;
                if (convection > 0)
                    lerpFactor1 = lerpFactor2 = Mathf.Min(1 - Mathf.Pow(1 - props1.conductivity * props2.conductivity * Mathf.Pow(Settings.ConvectionConductivityEffect, convection) / props1.heatCapacity, Celsius.TemperatureInfo.SecondsPerUpdate), 0.25f);
                else lerpFactor1 = lerpFactor2 = Mathf.Min(1 - Mathf.Pow(1 - props1.conductivity * props2.conductivity / props1.heatCapacity, Celsius.TemperatureInfo.SecondsPerUpdate), 0.25f);
            }

            else
            {
                finalTemp = GenMath.WeightedAverage(temp1, props1.heatCapacity, temp2, props2.heatCapacity);
                float conductivity = props1.conductivity * props2.conductivity;
                convection = props1.airflow * props2.airflow;
                if (convection > 0)
                    conductivity *= Mathf.Pow(Settings.ConvectionConductivityEffect, convection);
                lerpFactor1 = Mathf.Min(1 - Mathf.Pow(1 - conductivity / props1.heatCapacity, Celsius.TemperatureInfo.SecondsPerUpdate), 0.25f);
                lerpFactor2 = Mathf.Min(1 - Mathf.Pow(1 - conductivity / props2.heatCapacity, Celsius.TemperatureInfo.SecondsPerUpdate), 0.25f);
            }

            if (log)
            {
                LogUtility.Log($"Object 1: t = {temp1:F1}C. {props1}");
                LogUtility.Log($"Object 2: t = {temp2:F1}C. {props2}");
                LogUtility.Log($"Final temperature: {finalTemp:F1}C. Convection: {convection:P1}. Lerp factor 1: {lerpFactor1:P1}. Lerp factor 2: {lerpFactor2:P1}.");
            }

            return (lerpFactor1 * (finalTemp - temp1), lerpFactor2 * (finalTemp - temp2));
        }

        #endregion DIFFUSION

        #region THERMAL PROPERTIES

        public static CellThermalProps GetThermalProperties(this IntVec3 cell, Map map)
        {
            if (cell.InBounds(map))
            {
                List<Thing> thingsList = map.thingGrid.ThingsListAtFast(cell);
                for (int i = 0; i < thingsList.Count; i++)
                    if (CompThermal.ShouldApplyTo(thingsList[i].def))
                    {
                        CellThermalProps thermalProps = thingsList[i].TryGetComp<CompThermal>()?.ThermalProperties;
                        if (thermalProps != null)
                            return thermalProps;
                    }
            }
            return CellThermalProps.Air;
        }

        public static float GetHeatCapacity(this IntVec3 cell, Map map) => cell.GetThermalProperties(map).heatCapacity;

        public static ThingDef GetUnderlyingStuff(this Thing thing) => thing.Stuff ?? thing.def.defaultStuff;

        #endregion THERMAL PROPERTIES

        #region TERRAIN

        public static bool HasTerrainTemperature(this IntVec3 cell, Map map) => cell.GetTerrain(map).HasModExtension<ThingThermalProperties>();

        public static float FreezingPoint(this TerrainDef water)
        {
            switch (water.defName)
            {
                case "WaterOceanDeep":
                case "WaterOceanShallow":
                    return -2;

                case "WaterMovingChestDeep":
                    return -3;

                case "WaterMovingShallow":
                    return -2;
            }
            return 0;
        }

        #endregion TERRAIN

        #region HEAT PUSH AND SNOW MELTING

        public static bool TryPushHeat(IntVec3 cell, Map map, float energy)
        {
            if (UI.MouseCell() == cell)
                LogUtility.Log($"Pushing {energy} heat at {cell}.");
            TemperatureInfo temperatureInfo = map.TemperatureInfo();
            if (temperatureInfo == null)
            {
                LogUtility.Log($"TemperatureInfo for {map} unavailable!");
                return false;
            }
            temperatureInfo.SetTempteratureForCell(cell, temperatureInfo.GetTemperatureForCell(cell) + energy * GenTicks.TicksPerRealSecond * Settings.HeatPushEffect / cell.GetHeatCapacity(map));
            return true;
        }

        public static float MeltAmountAt(float temperature) => temperature * Mathf.Lerp(0, 0.0058f, temperature / 10);

        #endregion HEAT PUSH AND SNOW MELTING
    }
}
