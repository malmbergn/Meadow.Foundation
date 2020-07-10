using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Meadow.Foundation.Sensors.Light
{
    public class Vcnl4010ConditionChangeResult : IChangeResult<Vcnl4010Conditions>
    {

        public Vcnl4010ConditionChangeResult(Vcnl4010Conditions newValue, Vcnl4010Conditions oldValue)
        {
            New = newValue;
            Old = oldValue;

            RecalcDelta();

        }

        public Vcnl4010Conditions New { get; set; }
        public Vcnl4010Conditions Old { get; set; }

        public Vcnl4010Conditions Delta { get; protected set; }

        protected void RecalcDelta() 
        {
            float? deltaAmbient = New?.Ambient;
            float? deltaLux = New?.Lux;
            float? deltaProximity = New?.Proximity;

            if (Old?.Ambient != null && New?.Ambient != null)
                deltaAmbient = New.Ambient - Old.Ambient;

            if (Old?.Lux != null && New?.Lux != null)
                deltaLux = New.Lux - Old.Lux;

            if (Old?.Proximity != null && New?.Proximity != null)
                deltaProximity = New.Proximity - Old.Proximity;

            Delta = new Vcnl4010Conditions(deltaAmbient, deltaLux, deltaProximity);
        }

    }
}
