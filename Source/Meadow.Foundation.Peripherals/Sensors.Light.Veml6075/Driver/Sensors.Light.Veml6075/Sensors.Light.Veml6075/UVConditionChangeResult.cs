using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Meadow.Foundation.Sensors.Light
{
    public class UVConditionChangeResult : IChangeResult<UVConditions>
    {
        public UVConditions New { get; set; }
        public UVConditions Old { get; set; }
        public UVConditions Delta { get; protected set; }

        public UVConditionChangeResult(UVConditions newValue, UVConditions oldValue)
        {
            New = newValue;
            Old = oldValue;

            RecalcDelta();

        }

        protected void RecalcDelta()
        {
            if (New == null)
                return;

            if(Old == null)
                Delta = new UVConditions(New.UVA, New.UVB, New.UVIndex);
            else
                Delta = new UVConditions(New.UVA - Old.UVA, New.UVB - Old.UVB, New.UVIndex - Old.UVIndex);

        }
    }
}
