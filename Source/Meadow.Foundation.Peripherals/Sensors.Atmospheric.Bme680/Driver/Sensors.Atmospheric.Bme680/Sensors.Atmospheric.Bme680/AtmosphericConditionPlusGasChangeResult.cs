using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Meadow.Foundation.Sensors.Atmospheric
{
    public class AtmosphericConditionPlusGasChangeResult : IChangeResult<AtmosphericPlusGasConditions>
    {

        public AtmosphericConditionPlusGasChangeResult(AtmosphericPlusGasConditions newValue, AtmosphericPlusGasConditions oldValue)
        {
            New = newValue;
            Old = oldValue;
            RecalcDelta();
        }

        public AtmosphericPlusGasConditions New { get; set; }
        public AtmosphericPlusGasConditions Old { get; set; }
        public AtmosphericPlusGasConditions Delta { get; protected set; }

        protected void RecalcDelta()
        {
            if (New == null || Old == null)
                return;

            float? temp = null;
            float? pressure = null;
            float? humidity = null;
            float? gas = null;
            float? alt = null;

            if (New.Temperature.HasValue && Old.Temperature.HasValue)
                temp = New.Temperature.Value - Old.Temperature.Value;

            if (New.Pressure.HasValue && Old.Pressure.HasValue)
                pressure = New.Pressure.Value - Old.Pressure.Value;

            if (New.Humidity.HasValue && Old.Humidity.HasValue)
                humidity = New.Humidity.Value - Old.Humidity.Value;

            if (New.GasResistance.HasValue && Old.GasResistance.HasValue)
                gas = New.GasResistance.Value - Old.GasResistance.Value;

            if (New.Altitude.HasValue && Old.Altitude.HasValue)
                alt = New.Altitude.Value - Old.Altitude.Value;

            Delta = new AtmosphericPlusGasConditions(temp, pressure, humidity, gas, alt);

        }
    }
}
