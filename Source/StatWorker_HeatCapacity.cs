﻿using RimWorld;
using Verse;

namespace Celsius
{
    public class StatWorker_HeatCapacity : StatWorker
    {
        public override float GetValueUnfinalized(StatRequest req, bool applyPostProcess = true)
        {
            CellThermalProps thermalProps = req.Thing?.TryGetComp<CompThermal>()?.ThermalProperties;
            return thermalProps != null ? thermalProps.heatCapacity / 1000 : 0;
        }
    }
}
