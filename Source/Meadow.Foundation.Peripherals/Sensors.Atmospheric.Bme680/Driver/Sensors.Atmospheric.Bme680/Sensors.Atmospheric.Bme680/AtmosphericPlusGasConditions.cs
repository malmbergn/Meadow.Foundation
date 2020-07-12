using Meadow.Peripherals.Sensors.Atmospheric;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Meadow.Foundation.Sensors.Atmospheric
{
    public class AtmosphericPlusGasConditions : AtmosphericConditions
    {
        public AtmosphericPlusGasConditions()
        {

        }

        public AtmosphericPlusGasConditions(float? temperature, float? pressure, float? humidity, float? gasResistance, float? altitude) : base(temperature, pressure, humidity)
        {
            GasResistance = gasResistance;
            Altitude = altitude;
        }

        public float? GasResistance { get; set; }

        public float? Altitude { get; set; }

        public static AtmosphericPlusGasConditions From(AtmosphericPlusGasConditions conditions)
        {
            if (conditions == null)
                return null;

            return new AtmosphericPlusGasConditions(conditions.Temperature, conditions.Pressure, conditions.Humidity, conditions.GasResistance, conditions.Altitude);
        }

    }
}
