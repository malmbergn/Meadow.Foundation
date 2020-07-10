using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Meadow.Foundation.Sensors.Light
{
    public class Vcnl4010Conditions
    {
        public Vcnl4010Conditions() { }
        public Vcnl4010Conditions(float? ambient, float? lux, float? proximity)
        {
            Ambient = ambient;
            Proximity = proximity;
            Lux = lux;
        }

        public float? Ambient { get; set; }
        public float? Proximity { get; set; }
        public float? Lux { get; set; }

        public static Vcnl4010Conditions From(Vcnl4010Conditions conditions)
        {
            return new Vcnl4010Conditions(conditions?.Ambient, conditions?.Lux, conditions?.Proximity);
        }
    }
}
